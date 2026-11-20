using System.Numerics;

namespace Nullwave.Render;

/// <summary>
/// A stationary, 100% 3D procedural backdrop genome. Every backdrop selects one of 5 genuine
/// 3D raymarched or 3D perspective scenes with continuous random structural and cool/deep
/// palette parameters (single layer, zero 2D space-warping ops) so the background stays
/// rock-steady relative to the ship while pulsing its 3D lighting and rims to the music.
/// </summary>
public sealed class BackdropGenome
{
    public const int OpCount = 0;         // 2D space-warping ops retired (stationary 3D only)
    public const int GeneratorCount = 5;  // 0..4 (100% genuine 3D scenes)
    public const int MaxLayers = 1;

    public static readonly string[] OpNames = { "-" };
    public static readonly string[] GeneratorNames = { "LIQUID 3D", "CYBER TUNNEL 3D", "VOID HORIZON 3D", "CATHEDRAL 3D", "ORBITAL GYROID 3D" };

    /// <summary>All 5 scenes are full 3D raymarched or perspective scenes (single base layer).</summary>
    public static bool IsExpensive(int gen) => gen is >= 0 and < GeneratorCount;

    /// <summary>True iff the genome is a single-layer genuine 3D scene with zero 2D space-warping ops.</summary>
    public static bool Is3DOnly(BackdropGenome? g) =>
        g != null
        && g.Layers.Count == 1
        && g.Layers[0].Generator >= 0
        && g.Layers[0].Generator < GeneratorCount
        && g.Layers[0].Ops.All(o => o == 0);

    public sealed class Layer
    {
        public int[] Ops = new int[4];
        public Vector4[] OpParams = new Vector4[4];
        public int Generator;
        public Vector4 GenA, GenB;
        public int Blend;
        public float Gain = 1f;
        public Vector3 PaletteOffset, PaletteFrequency;
    }

    public readonly List<Layer> Layers = new();
    public Vector4 Global;
    public ulong Seed;

    /// <summary>Compact description, e.g. "CATHEDRAL 3D".</summary>
    public string Name => string.Join(" | ", Layers.Select(l => GeneratorNames[l.Generator]));

    public static BackdropGenome Random(Random rng, BackdropGenome? previous = null)
    {
        for (int attempt = 0; ; attempt++)
        {
            var g = Build(rng);
            // consecutive backdrops always switch to a different 3D environment
            if (previous == null || attempt > 20 || g.Layers[0].Generator != previous.Layers[0].Generator)
                return g;
        }
    }

    private static BackdropGenome Build(Random rng)
    {
        float F() => (float)rng.NextDouble();
        Vector4 V() => new(F(), F(), F(), F());
        var g = new BackdropGenome { Global = V(), Seed = (ulong)rng.NextInt64() };

        // Deep, cool architectural palette families so bright neon ships, lasers and bullets pop
        var l = new Layer
        {
            Generator = rng.Next(GeneratorCount),
            GenA = V(),
            GenB = V(),
            Blend = 0,
            Gain = 1f,
            PaletteOffset = new Vector3(0.45f + F() * 0.45f, 0.35f + F() * 0.45f, 0.08f + F() * 0.35f),
            PaletteFrequency = new Vector3(0.35f + F() * 0.45f, 0.35f + F() * 0.45f, 0.40f + F() * 0.45f),
        };
        g.Layers.Add(l);
        return g;
    }
}
