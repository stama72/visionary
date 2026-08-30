using System.Reflection;
using Visionary.Sim.Time;

namespace Visionary.Sim.Tests.Architecture;

/// <summary>
/// ADR-0002 の決定論規約と ADR-0001 のエンジン非依存を、レビューではなくテストで守る。
/// </summary>
/// <remarks>
/// これらの規約は破ってもコンパイルが通り、実行もでき、テストも緑のまま進む。
/// 表面化するのは比較実験(TDD01 §5)で「同じシードなのに結果が違う」と気づく時点であり、
/// M0で最も手戻りの高い場所にあたる。だから発覚を型とテストの側へ前倒しする。
///
/// 分担: 呼び出し側(new Random() / DateTime.Now など)は BannedSymbols.txt の
/// アナライザが RS0030 でビルドを止める。アナライザは宣言の型を見ないため、
/// 状態・シグネチャ・ローカル変数に浮動小数点が現れる経路はここが受け持つ。
/// </remarks>
public sealed class DeterminismConventionTests
{
    private static readonly Assembly Sim = typeof(Tick).Assembly;

    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    /// <summary>ADR-0002: 浮動小数点はシムの状態と計算に持ち込まない。</summary>
    private static readonly HashSet<Type> FloatingPointTypes =
        new() { typeof(float), typeof(double), typeof(decimal) };

    /// <summary>
    /// ADR-0001「Visionary.Sim に Godot 依存を持ち込まない」。
    /// CIをUbuntuで回すことによる間接的な証明を、直接の表明に置き換える。
    /// </summary>
    [Fact]
    public void CheckNoExternalDependencies()
    {
        var violations = Sim.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !IsFrameworkAssembly(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Visionary.Sim が標準ライブラリ以外を参照している(ADR-0001): {string.Join(", ", violations)}");
    }

    /// <summary>
    /// ADR-0002:「シム状態はint/longで保持し、金額・スコア計算に浮動小数点を使わない」。
    /// 比率係数は千分率(‰)の整数で持ち、除算は切り上げヘルパーを通す。
    /// </summary>
    [Fact]
    public void CheckNoFloatingPointInStateAndCalculations()
    {
        var violations = new List<string>();

        foreach (var type in Sim.GetTypes())
        {
            foreach (var field in type.GetFields(AllMembers))
            {
                Collect(field.FieldType, $"{Describe(type)}.{field.Name}", violations);
            }

            foreach (var property in type.GetProperties(AllMembers))
            {
                Collect(property.PropertyType, $"{Describe(type)}.{property.Name}", violations);
            }

            foreach (var method in type.GetMethods(AllMembers).Cast<MethodBase>()
                .Concat(type.GetConstructors(AllMembers)))
            {
                CollectFromMethod(type, method, violations);
            }
        }

        Assert.True(
            violations.Count == 0,
            "浮動小数点はシムの状態と計算に使わない(ADR-0002)。比率は千分率(‰)の int で持つ:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations.OrderBy(v => v, StringComparer.Ordinal)));
    }

    private static void CollectFromMethod(Type type, MethodBase method, List<string> violations)
    {
        var where = $"{Describe(type)}.{method.Name}";

        if (method is MethodInfo info)
        {
            Collect(info.ReturnType, $"{where}() の戻り値", violations);
        }

        foreach (var parameter in method.GetParameters())
        {
            Collect(parameter.ParameterType, $"{where}() の引数 {parameter.Name}", violations);
        }

        // ローカル変数の型は IL に残るため、宣言だけでなく計算の途中も一定まで見える。
        // ただし変数に入らない一時値(例: (int)(a / 3.0))までは追えない。
        // そこは状態ハッシュの回帰テストが最後の網になる。
        foreach (var local in LocalVariableTypes(method))
        {
            Collect(local, $"{where}() のローカル変数", violations);
        }
    }

    private static IEnumerable<Type> LocalVariableTypes(MethodBase method)
    {
        MethodBody? body;

        try
        {
            body = method.GetMethodBody();
        }
        catch (Exception)
        {
            // abstract・extern・ランタイム実装のメソッドは本体を持たない
            return Array.Empty<Type>();
        }

        return body is null
            ? Array.Empty<Type>()
            : body.LocalVariables.Select(local => local.LocalType);
    }

    private static void Collect(Type type, string where, List<string> violations)
    {
        foreach (var part in Decompose(type))
        {
            if (FloatingPointTypes.Contains(part))
            {
                violations.Add($"  {where}: {part.Name}");
            }
        }
    }

    /// <summary>配列・参照・ジェネリック引数の中に隠れた型まで開く(例: <c>List&lt;double?&gt;</c>)。</summary>
    private static IEnumerable<Type> Decompose(Type type)
    {
        if (type.HasElementType)
        {
            foreach (var element in Decompose(type.GetElementType()!))
            {
                yield return element;
            }

            yield break;
        }

        yield return type;

        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var part in Decompose(argument))
            {
                yield return part;
            }
        }
    }

    private static bool IsFrameworkAssembly(string name) =>
        name.StartsWith("System.", StringComparison.Ordinal)
        || name is "System" or "netstandard" or "mscorlib";

    private static string Describe(Type type) => type.FullName ?? type.Name;

    /// <summary>
    /// ADR-0002:「反復順序が不定なコレクション(Dictionary 等)の列挙結果をロジックに使わない。
    /// NPCの処理順はId昇順で固定」。列挙して安全かどうかを型の側で保証する。
    /// </summary>
    /// <remarks>
    /// 禁止をインターフェース(IDictionary 等)にも広げてある。これがないと
    /// 「宣言はインターフェース、実体は Dictionary」で素通りし、宣言された型だけを見る
    /// このテストでは検出できない。
    ///
    /// 性能: SortedDictionary は O(1) が O(log n) になるが、NPC規模は v1.0 で50〜100体、
    /// 箱庭感を保つ設計判断により拡張しても200体程度にとどまるため無視できる。
    /// ただし Id が密な整数なら Id 添字の配列が O(1) かつ列挙順が確定するので、
    /// 状態の保持にはまず配列を検討し、Id が疎な場合に SortedDictionary を使う。
    /// </remarks>
    [Fact]
    public void CheckNoUnorderedCollectionsInState()
    {
        var banned = UnorderedCollectionTypes();
        var violations = new List<string>();

        foreach (var type in Sim.GetTypes())
        {
            foreach (var field in type.GetFields(AllMembers))
            {
                CollectUnordered(field.FieldType, $"{Describe(type)}.{field.Name}", banned, violations);
            }

            foreach (var property in type.GetProperties(AllMembers))
            {
                CollectUnordered(
                    property.PropertyType, $"{Describe(type)}.{property.Name}", banned, violations);
            }
        }

        Assert.True(
            violations.Count == 0,
            "列挙順が保証されないコレクションをシムの状態に持たない(ADR-0002)。"
                + "Id 添字の配列、または SortedDictionary / SortedSet を使う:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations.OrderBy(v => v, StringComparer.Ordinal)));
    }

    /// <summary>
    /// シムの状態として持つことを禁じる、列挙順が保証されないコレクション。
    /// </summary>
    private static IReadOnlyCollection<Type> UnorderedCollectionTypes() =>
        new HashSet<Type>
        {
            typeof(Dictionary<,>),
            typeof(HashSet<>),
            typeof(System.Collections.Concurrent.ConcurrentDictionary<,>),
            typeof(System.Collections.Hashtable),

            // 実体の列挙順を保証しないため、インターフェースで受けることも禁じる
            typeof(IDictionary<,>),
            typeof(IReadOnlyDictionary<,>),
            typeof(ISet<>),
            typeof(IReadOnlySet<>),
        };

    private static void CollectUnordered(
        Type type, string where, IReadOnlyCollection<Type> banned, List<string> violations)
    {
        foreach (var part in Decompose(type))
        {
            var definition = part.IsGenericType ? part.GetGenericTypeDefinition() : part;

            if (banned.Contains(definition))
            {
                violations.Add($"  {where}: {definition.Name}");
            }
        }
    }
}
