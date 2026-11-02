using Visionary.Sim;

namespace Visionary.Presentation;

/// <summary>
/// 感謝の効果音の波形(TDD01 §3.10、GDD05 §2.1)。整数だけで作る矩形波。
/// 作り込んだ音ではなく、段階を聞き分けられるだけのグレーボックス。
/// </summary>
public static class Chime
{
    /// <summary>標本化周波数。単位: Hz(標本/秒)。</summary>
    public const int MixRate = 22050;

    private const int Amplitude = 6000;   // 最大振幅。16 bit 符号付きの値(最大 32767 に対する絶対値)

    // (周波数 Hz, 長さ ms) の並び。
    private static readonly (int FrequencyHz, int LengthMs)[] Reluctant = [(220, 200)];
    private static readonly (int FrequencyHz, int LengthMs)[] Grateful = [(440, 120), (554, 120)];
    private static readonly (int FrequencyHz, int LengthMs)[] DeepGratitude = [(523, 100), (659, 100), (784, 160)];

    /// <summary>モノラル・16 bit 符号付き・リトルエンディアンの PCM。</summary>
    public static byte[] Pcm16(Gratitude gratitude)
    {
        var notes = gratitude switch
        {
            Gratitude.Reluctant => Reluctant,
            Gratitude.Grateful => Grateful,
            Gratitude.DeepGratitude => DeepGratitude,
            _ => throw new ArgumentOutOfRangeException(nameof(gratitude), gratitude, "知らない段階。"),
        };

        var bytes = new List<byte>();

        foreach (var (frequencyHz, lengthMs) in notes)
        {
            int n = MixRate * lengthMs / 1000;   // 標本数

            for (int i = 0; i < n; i++)
            {
                int amplitude = Amplitude * (n - i) / n;
                bool positive = (i * frequencyHz * 2 / MixRate) % 2 == 0;
                short sample = (short)(positive ? amplitude : -amplitude);

                bytes.Add((byte)(sample & 0xFF));
                bytes.Add((byte)((sample >> 8) & 0xFF));
            }
        }

        return bytes.ToArray();
    }
}
