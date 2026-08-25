# Visionary(仮題)

中近世都市を舞台にした商人ライフシム。エージェントベースの経済シミュレーションが中核。
プロジェクト全体は [docs/README.md](docs/README.md) を参照。

現在は **M0プロトタイプ**(〜2026-10上旬)。計画は [TDD01 §5.4](docs/04-tdd/01-sim-core-and-m0.md) を正とする。

## 構成

| パス                       | 内容                                                              |
| -------------------------- | ----------------------------------------------------------------- |
| `docs/`                    | 企画・GDD・TDD・ADR。**設計判断は必ずここが正**                   |
| `src/Visionary.Sim/`       | 経済シミュレーション本体。純C#(net8.0)、**Godot参照ゼロ**         |
| `src/Visionary.Sim.Runner/` | ヘッドレス実験ハーネス(`vsim`)。M0の比較実験はすべてここから実行 |
| `src/Visionary.Game/`      | Godot 4プロジェクト。**W5で作成予定・現時点では未作成**            |
| `tests/Visionary.Sim.Tests/` | xUnit                                                           |

## 技術規約

- **Godot 4 + C#**([ADR-0001](docs/adr/0001-engine-and-simulation-architecture.md))。`Visionary.Sim` に Godot 依存を持ち込まない。CIをUbuntuで回しているのはこの制約の継続検証を兼ねる
- Godot の作例は GDScript / Godot 3系が多い。**C# かつ Godot 4 系のAPIであることを公式クラスリファレンスで確認してから書く**

### 決定論([ADR-0002](docs/adr/0002-time-model-and-determinism.md))

M0の比較実験はすべて決定論に依存する。以下はレビュー観点でもある:

- 1 tick = ゲーム内1時間。時刻の真実は `Visionary.Sim` のみが持ち、Godot側で独自にゲーム時間を進めない
- **浮動小数点をシム状態と計算に使わない。** 金額・信用はint、比率係数は千分率(‰)の整数。除算は切り上げヘルパー経由(GDD01 §2.3「全計算式の結果は小数点以下切り上げ」)
- 係数の定数定義には**単位のコメントを必須**とする(`alphaPermille = 200` は 20% の意)
- 乱数は系統別ストリームから取る。**系統をまたいで乱数を借用しない**(共通乱数法が壊れ、A/B比較が無意味になる)
- `Dictionary` など列挙順が保証されないコレクションの**列挙結果をロジックに使わない**。NPCの処理順はId昇順で固定

## コマンド

```
dotnet build Visionary.sln -c Release     # 警告はエラーとして扱われる
dotnet test  Visionary.sln -c Release
dotnet format Visionary.sln               # CIのフォーマット検証を通す
```

## ドキュメント運用

- 個人開発だが、チーム開発の意思決定プロセスを模して文書を運用する
- 大きな技術的決定は **ADR** に「背景・選択肢・決定・理由」を記録する
- GDD/TDD は「育てる文書」。実装が仕様と乖離したら、コードだけでなく**文書側も直す**
- 数値(閾値・係数)は初期値であり調整対象。固定すべきは構造と依存関係
