#!/usr/bin/env python3
"""5 時間枠の消費を集計し、モデル別のレート($ / 枠の 100%)を解く。

出所:
  ~/.claude/projects/*visionary*/**/*.jsonl  セッションの transcript
                                             (サブエージェント・worktree 起動分を含む)
  <repo>/.pipeline/*.jsonl                   rate_limit_event(枠の utilization の観測点)

使い方:
  python scripts/usage-rates.py              モデル別レートと枠ごとの当てはまり
  python scripts/usage-rates.py --sessions   セッション別の API $ と枠 %
  python scripts/usage-rates.py --since 2026-09-19

前提(docs/process/04-issue-driven.md「枠」/ issue #122 決定 3):
  - 枠はリセット直後が 0%。**枠頭からの累積**で解くと $ の帰属誤差が消える。
    観測点の差分で解くと帰属がずれ、R2 が負になる(決定 1 で踏んだ)
  - 単価は API 定価。1h キャッシュ書きは 2 倍、読みは 0.1 倍
  - 2026-09-17 夜に Pro -> Max。それ以前は枠のサイズが違うので既定で除く

レートは初期値であり、#123 の確認で動かす。
"""
import argparse
import collections
import datetime
import glob
import json
import os
import sys

JST = datetime.timezone(datetime.timedelta(hours=9))
MAX_PLAN_FROM = datetime.datetime(2026, 9, 18, tzinfo=JST)  # Pro -> Max は 09-17 夜

# $/MTok: 入力, キャッシュ書き(1h = 2 倍), キャッシュ読み, 出力
PRICE = {
    'claude-fable-5-1': (10, 20, 0.25, 50),
    'claude-opus-5': (5, 10, 0.5, 25),
    'claude-sonnet-5': (2, 4, 0.2, 10),
    'claude-haiku-4-5': (1, 2, 0.1, 5),
}
# レートを解くときの列。haiku は opus と同じ列に入れる(量が微小で単独では識別できない)
COLUMNS = [('opus', ['claude-opus-5', 'claude-haiku-4-5']),
           ('sonnet', ['claude-sonnet-5']),
           ('fable', ['claude-fable-5-1'])]


def cost(model, inp, cache_write, cache_read, out):
    p = PRICE.get(model)
    return 0.0 if not p else (inp * p[0] + cache_write * p[1] + cache_read * p[2] + out * p[3]) / 1e6


def to_jst(stamp):
    return datetime.datetime.fromisoformat(stamp.replace('Z', '+00:00')).astimezone(JST)


def lstsq(rows, ys):
    """正規方程式 (A^T A) x = A^T y をガウス・ジョルダンで解く。numpy を使わない。"""
    n = len(rows[0])
    ata = [[sum(r[i] * r[j] for r in rows) for j in range(n)] for i in range(n)]
    aty = [sum(r[i] * y for r, y in zip(rows, ys)) for i in range(n)]
    for i in range(n):
        pivot = max(range(i, n), key=lambda k: abs(ata[k][i]))
        if abs(ata[pivot][i]) < 1e-12:
            return None
        ata[i], ata[pivot] = ata[pivot], ata[i]
        aty[i], aty[pivot] = aty[pivot], aty[i]
        d = ata[i][i]
        ata[i] = [v / d for v in ata[i]]
        aty[i] /= d
        for k in range(n):
            if k != i and ata[k][i]:
                f = ata[k][i]
                ata[k] = [a - f * b for a, b in zip(ata[k], ata[i])]
                aty[k] -= f * aty[i]
    return aty


def r_squared(rows, ys, weights):
    pred = [sum(r[i] * weights[i] for i in range(len(weights))) for r in rows]
    mean = sum(ys) / len(ys)
    ss_res = sum((y - p) ** 2 for y, p in zip(ys, pred))
    ss_tot = sum((y - mean) ** 2 for y in ys)
    return 1 - ss_res / ss_tot if ss_tot else float('nan')


def _is_developer_turn(entry):
    """ツール結果ではなく、開発者が実際に入力したターンなら 1。"""
    content = (entry.get('message') or {}).get('content')
    if isinstance(content, str):
        return 1 if content.strip() else 0
    if isinstance(content, list):
        if any(isinstance(x, dict) and x.get('type') == 'tool_result' for x in content):
            return 0
        text = ' '.join(x.get('text', '') for x in content if isinstance(x, dict) and x.get('type') == 'text')
        return 1 if text.strip() else 0
    return 0


def read_transcripts():
    """セッションごとの (モデル別 $, 開始, 終了, 開発者ターン数)、呼び出しの時系列、uuid -> 時刻を返す。

    uuid -> 時刻は観測点の時刻解決に使う。サブエージェントの行まで拾わないと、
    rate_limit_event の直前の assistant 行が見つからず観測点が落ちる。
    """
    root = os.path.expanduser('~/.claude/projects')
    sessions = {}
    uuid_time = {}
    calls = []  # (時刻, モデル, $)
    for pdir in glob.glob(root + '/*isionary*'):
        if not os.path.isdir(pdir):
            continue
        for main in glob.glob(pdir + '/*.jsonl'):
            sid = os.path.basename(main)[:-6]
            s = sessions.setdefault(sid, dict(models=collections.Counter(), t0=None, t1=None, turns=0))
            subagents = glob.glob(pdir + '/' + sid + '/subagents/*.jsonl')
            for path in [main] + subagents:
                is_sub = path != main
                seen = set()
                for line in open(path, encoding='utf-8'):
                    try:
                        o = json.loads(line)
                    except ValueError:
                        continue
                    stamp = o.get('timestamp')
                    if stamp:
                        t = to_jst(stamp)
                        s['t0'] = t if s['t0'] is None or t < s['t0'] else s['t0']
                        s['t1'] = t if s['t1'] is None or t > s['t1'] else s['t1']
                        if o.get('uuid'):
                            uuid_time[o['uuid']] = t
                    if not is_sub and o.get('type') == 'user' and not o.get('isMeta'):
                        s['turns'] += _is_developer_turn(o)
                    if o.get('type') != 'assistant' or not stamp:
                        continue
                    msg = o.get('message') or {}
                    usage = msg.get('usage')
                    if not usage:
                        continue
                    # 同じ requestId が複数行に分かれて出るので、最初の 1 行だけ数える
                    key = o.get('requestId') or msg.get('id')
                    if key in seen:
                        continue
                    seen.add(key)
                    model = msg.get('model')
                    c = cost(model, usage.get('input_tokens', 0), usage.get('cache_creation_input_tokens', 0),
                             usage.get('cache_read_input_tokens', 0), usage.get('output_tokens', 0))
                    s['models'][model] += c
                    calls.append((to_jst(stamp), model, c))
    calls.sort(key=lambda x: x[0])
    return sessions, calls, uuid_time


def read_observations(repo, uuid_time):
    """.pipeline/*.jsonl の rate_limit_event から (時刻, utilization, resetsAt) を集める。"""
    out = []
    for path in sorted(glob.glob(os.path.join(repo, '.pipeline', '*.jsonl'))):
        last_uuid = None
        for line in open(path, encoding='utf-8'):
            try:
                o = json.loads(line)
            except ValueError:
                continue
            if o.get('type') == 'assistant' and o.get('uuid'):
                last_uuid = o['uuid']
            if o.get('type') == 'rate_limit_event':
                w = (o.get('rate_limit_info') or {}).get('unifiedWindows', {}).get('five_hour')
                # 観測点の時刻は、その直前の assistant 行から取る
                if w and last_uuid in uuid_time:
                    out.append((uuid_time[last_uuid], w['utilization'], w['resetsAt']))
    out.sort(key=lambda x: x[0])
    return out


def build_windows(observations, calls, since):
    """枠ごとに (枠頭, 最終観測の utilization, 列別の $) を作る。"""
    by_reset = collections.defaultdict(list)
    for t, util, reset in observations:
        by_reset[reset].append((t, util))
    windows = []
    for reset in sorted(by_reset):
        end = datetime.datetime.fromtimestamp(reset, JST)
        start = end - datetime.timedelta(hours=5)
        last_t, last_util = by_reset[reset][-1]
        spend = collections.Counter()
        for t, model, c in calls:
            if start <= t <= last_t and model in PRICE:
                spend[model] += c
        row = [sum(spend[m] for m in models) for _, models in COLUMNS]
        windows.append(dict(start=start, util=last_util, row=row, pro=start < MAX_PLAN_FROM,
                            observations=len(by_reset[reset])))
    return [w for w in windows if w['start'] >= since]


def solve_rates(windows):
    """Pro 期と、消費がほぼ無い枠を除いて解く。"""
    usable = [w for w in windows if not w['pro'] and w['util'] > 0.05]
    if len(usable) < len(COLUMNS):
        return None, usable
    return lstsq([w['row'] for w in usable], [w['util'] for w in usable]), usable


def as_dollars_per_100(weight):
    return 1 / weight if weight and weight > 0 else float('inf')


def report_rates(windows):
    weights, usable = solve_rates(windows)
    if not weights:
        print('枠が足りません(Max 期・utilization > 0.05 の枠が %d 本)' % len(usable))
        return
    print('=== モデル別レート(枠の 100%% に相当する API $) — Max 期 %d 枠' % len(usable))
    loo = collections.defaultdict(list)
    for k in range(len(usable)):
        rest = [w for i, w in enumerate(usable) if i != k]
        dropped = lstsq([w['row'] for w in rest], [w['util'] for w in rest])
        if dropped:
            for (name, _), weight in zip(COLUMNS, dropped):
                loo[name].append(as_dollars_per_100(weight))
    for (name, _), weight in zip(COLUMNS, weights):
        finite = sorted(v for v in loo[name] if v == v and v != float('inf'))
        span = '   leave-one-out $%.0f〜$%.0f' % (finite[0], finite[-1]) if finite else ''
        print('  %-7s $%6.0f%s' % (name, as_dollars_per_100(weight), span))
    print('  R2 = %.4f' % r_squared([w['row'] for w in usable], [w['util'] for w in usable], weights))
    print()
    print('=== 枠ごとの当てはまり')
    print('  枠頭            観測 実測util  予測  ' + '  '.join('%7s' % (n + ' $') for n, _ in COLUMNS))
    for w in windows:
        pred = '%5.2f' % sum(v * x for v, x in zip(w['row'], weights)) if not w['pro'] else '    -'
        print('  %s %4d    %5.2f %s  %s%s' % (
            w['start'].strftime('%m-%d %H:%M'), w['observations'], w['util'], pred,
            '  '.join('%7.1f' % v for v in w['row']),
            '   (Pro 期・レートの算出から除外)' if w['pro'] else ''))


def report_sessions(sessions, windows, since):
    weights, _ = solve_rates(windows)
    rate = {}
    if weights:
        for (name, models), weight in zip(COLUMNS, weights):
            for m in models:
                rate[m] = as_dollars_per_100(weight)
    print('=== セッション別(%s 以降)' % since.strftime('%Y-%m-%d'))
    print('  開始            分  ターン      $     枠%   モデル別 $')
    for s in sorted((s for s in sessions.values() if s['t0'] and s['t0'] >= since), key=lambda s: s['t0']):
        total = sum(s['models'].values())
        if total < 0.05:
            continue
        util = sum(v / rate[m] * 100 for m, v in s['models'].items() if m in rate) if rate else float('nan')
        minutes = (s['t1'] - s['t0']).total_seconds() / 60
        breakdown = ', '.join('%s %.1f' % (m.replace('claude-', ''), v)
                              for m, v in s['models'].most_common() if v >= 0.05)
        print('  %s %5.0f %6d %7.1f %6.1f   %s' % (
            s['t0'].strftime('%m-%d %H:%M'), minutes, s['turns'], total, util, breakdown))


def main():
    # Windows の既定は cp932 で、日本語の見出しも R2 の記号も落ちる
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    repo_default = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--sessions', action='store_true', help='セッション別の API $ と枠 %% を出す')
    ap.add_argument('--since', default='2026-09-18', help='この日以降の枠だけ見る(既定: Max 期の開始)')
    ap.add_argument('--repo', default=repo_default, help='.pipeline/ を持つリポジトリ(既定: このスクリプトの親)')
    args = ap.parse_args()
    since = datetime.datetime.strptime(args.since, '%Y-%m-%d').replace(tzinfo=JST)

    sessions, calls, uuid_time = read_transcripts()
    windows = build_windows(read_observations(args.repo, uuid_time), calls, since)
    if not windows:
        print('観測点のある枠がありません(%s の .pipeline/ を見ています)' % args.repo)
        return
    if args.sessions:
        report_sessions(sessions, windows, since)
    else:
        report_rates(windows)


if __name__ == '__main__':
    main()
