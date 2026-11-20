using System.Numerics;

namespace Nullwave.Render;

/// <summary>
/// A map-anchored 3D backdrop genome. Every backdrop is an organic 3D world lying *underneath*
/// the arena floor, viewed through the same camera as the fight, so it is fixed to the map. One of
/// six worlds (sea, molten crust, dunes, gyroid cavern, volumetric storm nebula, aurora curtains)
/// is picked with every structural, motion and colour parameter rolled fresh, plus an atmosphere.
/// Nothing is laid out on a grid. The shapes are driven by the music's spectrum (bass, mids and
/// highs each move their own part of the world), the beat clock, onsets and world shockwaves.
/// </summary>
public sealed class BackdropGenome
{
    public const int OpCount = 0;          // no 2D screen-space warps: the world is real 3D
    public const int GeneratorCount = 6;   // scenes 0..5
    public const int AtmosphereCount = 4;  // atmospheres 0..3 (0 = clear air)
    public const int MaxLayers = 1;

    public static readonly string[] OpNames = { "-" };
    public static readonly string[] GeneratorNames =
        { "SPECTRAL SEA", "MOLTEN CRUST", "SOUND DUNES", "GYROID CAVERN", "STORM NEBULA", "AURORA ABYSS" };
    public static readonly string[] AtmosphereNames = { "CLEAR", "MOTES", "FOG BANKS", "EMBERS" };

    /// <summary>Every scene is a raymarched 3D world.</summary>
    public static bool IsExpensive(int gen) => gen is >= 0 and < GeneratorCount;

    /// <summary>True iff the genome is one map-anchored 3D scene with no 2D warp ops.</summary>
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
    public int Atmosphere;
    public ulong Seed;

    /// <summary>Compact description, e.g. "CUBE SWARM + MOTES".</summary>
    public string Name => GeneratorNames[Layers[0].Generator] + " + " + AtmosphereNames[Atmosphere];

    public static BackdropGenome Random(Random rng, BackdropGenome? previous = null)
    {
        for (int attempt = 0; ; attempt++)
        {
            var g = Build(rng);
            // consecutive backdrops always change world
            if (previous == null || attempt > 20 || g.Layers[0].Generator != previous.Layers[0].Generator)
                return g;
        }
    }

    private static BackdropGenome Build(Random rng)
    {
        float F() => (float)rng.NextDouble();
        Vector4 V() => new(F(), F(), F(), F());
        var g = new BackdropGenome
        {
            Global = V(),
            Seed = (ulong)rng.NextInt64(),
            // clear air is rarer than the others so most worlds carry some depth cue
            Atmosphere = F() < 0.12f ? 0 : 1 + rng.Next(AtmosphereCount - 1),
        };

        // Cosine palette: either a tight hue family (frequencies equal) or a wide rainbow
        // spread. Offsets are fully random so every hue family is reachable.
        float baseF = 0.35f + F() * 0.65f;
        bool family = F() < 0.45f;
        var l = new Layer
        {
            Generator = rng.Next(GeneratorCount),
            GenA = V(),
            GenB = V(),
            Blend = 0,
            Gain = 1f,
            PaletteOffset = new Vector3(F(), F(), F()),
            PaletteFrequency = family
                ? new Vector3(baseF, baseF, baseF)
                : new Vector3(baseF, 0.3f + F() * 0.9f, 0.3f + F() * 0.9f),
        };
        g.Layers.Add(l);
        return g;
    }
}
