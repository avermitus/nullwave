using System.Numerics;

namespace Nullwave.Render;

/// <summary>
/// A fully generated backdrop. Nothing is picked from a fixed list of looks: every backdrop is
/// assembled from scratch out of 1-3 layers, each with
///   - a chain of up to four motion transforms that move space itself (tunnel flight, endless
///     log-spiral zoom, kaleidoscope folds, flowing warps, vortex swirls, ripples, scrolling,
///     independently spinning tiles, beat breathing) with continuous random parameters,
///   - one of 13 animated pattern generators with its own random parameters,
///   - a random cosine colourway, blend mode and gain,
/// plus global spin, zoom and mirror. Seeds come from an unseeded RNG, so a backdrop is
/// effectively never seen twice.
/// </summary>
public sealed class BackdropGenome
{
    public const int OpCount = 11;        // 1..11 (0 = none)
    public const int GeneratorCount = 13; // 0..12
    public const int MaxLayers = 3;

    public static readonly string[] OpNames = { "-", "SPIN", "TUNNEL", "FOLD", "WARP", "SWIRL", "MIRROR", "SPIRAL", "STREAM", "RIPPLE", "TILES", "BREATHE" };
    public static readonly string[] GeneratorNames = { "INK", "CELLS", "FRACTAL", "CIRCUIT", "MOIRE", "STREAMS", "GRID", "LIQUID", "FIELD", "PLASMA", "AURORA", "HEX", "HYPERSPACE" };

    /// <summary>Raymarched generators: only allowed as the base layer, at most once per backdrop.</summary>
    public static bool IsExpensive(int gen) => gen is 7 or 8;

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

    /// <summary>Compact description, e.g. "TUNNEL+WARP>CIRCUIT | SPIN>STREAMS".</summary>
    public string Name => string.Join(" | ", Layers.Select(l =>
        string.Join("+", l.Ops.Where(o => o > 0).Select(o => OpNames[o])) is { Length: > 0 } ops
            ? ops + ">" + GeneratorNames[l.Generator]
            : GeneratorNames[l.Generator]));

    public static BackdropGenome Random(Random rng, BackdropGenome? previous = null)
    {
        for (int attempt = 0; ; attempt++)
        {
            var g = Build(rng);
            // consecutive backdrops must look different: change the base generator or its motion
            if (previous == null || attempt > 20 || g.Layers[0].Generator != previous.Layers[0].Generator
                || !g.Layers[0].Ops.SequenceEqual(previous.Layers[0].Ops))
                return g;
        }
    }

    private static BackdropGenome Build(Random rng)
    {
        float F() => (float)rng.NextDouble();
        Vector4 V() => new(F(), F(), F(), F());
        var g = new BackdropGenome { Global = V(), Seed = (ulong)rng.NextInt64() };
        int layers = F() switch { < 0.2f => 1, < 0.7f => 2, _ => 3 };

        // one colour family per backdrop, drifted per layer, so layers feel related but not flat
        var baseOffset = new Vector3(F(), F(), F());
        var baseFreq = new Vector3(0.3f + F() * 1.1f, 0.3f + F() * 1.1f, 0.3f + F() * 1.1f);
        if (F() < 0.35f) { float f = 0.4f + F() * 0.8f; baseFreq = new Vector3(f, f, f); }

        for (int i = 0; i < layers; i++)
        {
            var l = new Layer();
            // generator: raymarched ones only as the base layer, and then keep the stack light
            int gen;
            do gen = rng.Next(GeneratorCount);
            while (i > 0 && IsExpensive(gen));
            if (i == 0 && IsExpensive(gen) && layers == 3) layers = 2;
            l.Generator = gen;
            l.GenA = V();
            l.GenB = V();

            // motion chain: 0-4 transforms; at most one space remap (tunnel or spiral), placed first
            int opCount = F() switch { < 0.1f => 0, < 0.35f => 1, < 0.7f => 2, < 0.9f => 3, _ => 4 };
            var used = new HashSet<int>();
            int slot = 0;
            if (opCount > 0 && !IsExpensive(gen) && gen != 12 && F() < 0.45f)
            {
                int remap = F() < 0.5f ? 2 : 7;
                l.Ops[slot] = remap; l.OpParams[slot] = V(); used.Add(remap); slot++;
            }
            while (slot < opCount)
            {
                int op = 1 + rng.Next(OpCount);
                if (op is 2 or 7 || used.Contains(op)) continue;
                // raymarched scenes only take gentle screen-space motion
                if (IsExpensive(gen) && op is 3 or 10) continue;
                l.Ops[slot] = op; l.OpParams[slot] = V(); used.Add(op); slot++;
            }

            l.Blend = i == 0 ? 0 : F() switch { < 0.35f => 0, < 0.65f => 1, < 0.8f => 2, < 0.92f => 3, _ => 4 };
            l.Gain = i == 0 ? 1f : 0.45f + F() * 0.55f;
            l.PaletteOffset = baseOffset + new Vector3(F(), F(), F()) * (0.08f + i * 0.12f);
            l.PaletteFrequency = baseFreq * (0.85f + F() * 0.3f);
            g.Layers.Add(l);
            if (g.Layers.Count >= layers) break;
        }
        return g;
    }
}
