using System.Numerics;

namespace Nullwave.Render;

/// <summary>
/// One randomly generated background: a style plus continuous parameters, mutators and a
/// cosine colourway. Seeds come from a fresh random source every time, so a backdrop is
/// practically never repeated: six styles x four free parameters x mutators x palette.
/// </summary>
public sealed class BackdropGenome
{
    public static readonly string[] StyleNames = { "LIQUID", "HYPERSPACE", "KALEIDOSCOPE", "FIELD", "AURORA", "SHATTER" };
    public const int StyleCount = 6;

    public int Style;
    public Vector4 Params;
    /// <summary>x mirror (&gt;0.6 on), y swirl strength, z kick zoom pulse, w spare.</summary>
    public Vector4 Mutators;
    public Vector3 PaletteOffset;
    public Vector3 PaletteFrequency;
    public ulong Seed;

    public string Name => StyleNames[Style] + " " + (Seed % 0xFFFF).ToString("X4");

    public static BackdropGenome Random(Random rng, int avoidStyle = -1)
    {
        int style;
        do style = rng.Next(StyleCount); while (style == avoidStyle && StyleCount > 1);
        float F() => (float)rng.NextDouble();
        var g = new BackdropGenome
        {
            Style = style,
            Params = new Vector4(F(), F(), F(), F()),
            Mutators = new Vector4(F() < 0.3f ? 1f : 0f, F() < 0.4f ? F() : 0f, F(), F()),
            PaletteOffset = new Vector3(F(), F(), F()),
            PaletteFrequency = new Vector3(0.3f + F() * 1.1f, 0.3f + F() * 1.1f, 0.3f + F() * 1.1f),
            Seed = (ulong)rng.NextInt64(),
        };
        // sometimes force a tight two-tone palette (all channels share a frequency): reads as designed
        if (F() < 0.35f)
        {
            float f = 0.4f + F() * 0.8f;
            g.PaletteFrequency = new Vector3(f, f, f);
        }
        return g;
    }
}
