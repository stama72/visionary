using System.Text.Json;
using System.Text.Json.Serialization;

namespace Visionary.Sim.Runner;

/// <summary>
/// <c>vsim run --config</c> の設定ファイル(W2-20 タスク仕様「5. vsim run と vsim hash」)。
/// </summary>
/// <remarks>
/// <b>未知のキーはエラーにする</b>(<see cref="JsonSerializerOptions.UnmappedMemberHandling"/> =
/// <see cref="JsonUnmappedMemberHandling.Disallow"/>)。黙って無視すると、綴り違いの設定が
/// 「効いたつもり」で走る。<c>features</c> / <c>coefficients</c> は W4 の予約 ── キーとしては
/// 許すが、空でなければエラーにする(<see cref="Validate"/>)。
/// </remarks>
internal sealed class RunConfig
{
    public long[]? MasterSeeds { get; init; }

    public int DurationDays { get; init; }

    public WorldConfig? World { get; init; }

    /// <summary>W4 の予約。キーがあってもよいが、空でなければエラー(<see cref="Validate"/>)。</summary>
    public Dictionary<string, JsonElement>? Features { get; init; }

    /// <summary>W4 の予約。同上。</summary>
    public Dictionary<string, JsonElement>? Coefficients { get; init; }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// <paramref name="json"/> を解析し、妥当なら <paramref name="config"/> を設定して <c>null</c> を
    /// 返す。妥当でなければ人間可読なエラーメッセージを返す(<paramref name="config"/> は <c>null</c>)。
    /// </summary>
    public static string? TryParse(string json, out RunConfig? config)
    {
        RunConfig? parsed;

        try
        {
            parsed = JsonSerializer.Deserialize<RunConfig>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            config = null;
            return $"設定ファイルの解析に失敗した: {ex.Message}";
        }

        if (parsed is null)
        {
            config = null;
            return "設定ファイルが空、または null。";
        }

        string? validationError = parsed.Validate();

        if (validationError is not null)
        {
            config = null;
            return validationError;
        }

        config = parsed;
        return null;
    }

    /// <summary>妥当なら <c>null</c>、そうでなければエラーメッセージ。</summary>
    private string? Validate()
    {
        if (MasterSeeds is null || MasterSeeds.Length == 0)
        {
            return "masterSeeds は1件以上の整数配列が必要。";
        }

        if (DurationDays < 1)
        {
            return $"durationDays は1以上が必要(実際: {DurationDays})。";
        }

        if (World is null || World.Preset != "m0-small")
        {
            return $"world.preset は \"m0-small\" のみ対応(実際: {World?.Preset ?? "(なし)"})。";
        }

        if (Features is { Count: > 0 })
        {
            return "features は W4 で実装する。現時点では空のオブジェクトのみ許可。";
        }

        if (Coefficients is { Count: > 0 })
        {
            return "coefficients は W4 で実装する。現時点では空のオブジェクトのみ許可。";
        }

        return null;
    }
}

/// <summary><c>world</c> キーの中身。</summary>
internal sealed class WorldConfig
{
    /// <summary>現時点では <c>"m0-small"</c> のみ対応。</summary>
    public string? Preset { get; init; }
}
