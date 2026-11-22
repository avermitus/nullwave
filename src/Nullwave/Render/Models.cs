using System.Numerics;
using System.Runtime.CompilerServices;
using Nullwave.World;

namespace Nullwave.Render;

/// <summary>
/// Procedural model library. The player and pylon use the shared material atlas.
/// Model space: +x forward, +y right wing, +z up toward the camera.
/// Enemy models are unit-radius and scaled by the enemy's collision radius at draw time.
/// </summary>
public static class Models
{
    public static readonly Mesh Player = AtlasAssets.LoadMesh("player");
    public static readonly Mesh PlayerThruster = BuildThruster();
    public static readonly Mesh Drifter = BuildDrifter();
    public static readonly Mesh Orbiter = BuildOrbiter();
    public static readonly Mesh OrbiterPod = BuildPod();
    public static readonly Mesh Sniper = BuildSniper();
    public static readonly Mesh Dasher = BuildDasher();
    public static readonly Mesh TurretBase = BuildTurretBase();
    public static readonly Mesh TurretHead = BuildTurretHead();
    public static readonly Mesh SplitterCore = BuildSplitterCore();
    public static readonly Mesh SplitterCell = BuildSplitterCell();
    public static readonly Mesh Swarm = BuildSwarm();
    public static readonly Mesh BossCore = BuildBossCore();
    public static readonly Mesh BossPillar = BuildBossPillar();
    public static readonly Mesh BossHalo = BuildBossHalo();
    public static readonly Mesh BossShard = BuildBossShard();
    public static readonly Mesh EliteRing = BuildEliteRing();
    public static readonly Mesh Pylon = AtlasAssets.LoadMesh("pylon");
    public static readonly Mesh Crystal = BuildCrystal();
    public static readonly Mesh Column = BuildColumn();
    /// <summary>Unit wall block (centred, 1 x 1 x 1) stretched along each map wall segment.</summary>
    public static readonly Mesh WallBlock = BuildWallBlock();
    public static readonly Mesh Ico = BuildIco();
    public static readonly Mesh Octa = BuildOcta();
    public static readonly Mesh Ring = BuildRing();
    public static readonly Mesh HaloSegment = BuildHaloSegment();
    public static readonly Mesh Knot = BuildKnot();
    public static readonly Mesh PickupGem = BuildPickupGem();
    public static readonly Mesh WeaverHead = BuildWeaverHead();
    public static readonly Mesh WeaverSegment = BuildWeaverSegment();
    public static readonly Mesh MortarBase = BuildMortarBase();
    public static readonly Mesh MortarBarrel = BuildMortarBarrel();
    public static readonly Mesh Phantom = BuildPhantom();
    public static readonly Mesh Prism = BuildPrism();

    private static readonly ConditionalWeakTable<Obstacle, Mesh> MonolithCache = new();

    public static IEnumerable<Mesh> All => new[]
    {
        Player, PlayerThruster, Drifter, Orbiter, OrbiterPod, Sniper, Dasher, TurretBase, TurretHead, SplitterCore,
        SplitterCell, Swarm, BossCore, BossPillar, BossHalo, BossShard, EliteRing, Pylon, Crystal, Column, Ico, Octa,
        Ring, HaloSegment, Knot, PickupGem, WeaverHead, WeaverSegment, MortarBase, MortarBarrel, Phantom, Prism,
    };

    // ---------------------------------------------------------------- player

    private static Mesh BuildThruster()
    {
        var b = new MeshBuilder();
        b.Loft(new Vector4[]
        {
            new(-1f, 0.05f, 0.05f, 0f),
            new(0f, 1f, 1f, 0f),
            new(0.3f, 0.6f, 0.6f, 0f),
        }, 6, Mat.Core, Mat.Core, Mat.Core);
        return b.Build();
    }

    // ---------------------------------------------------------------- enemies (unit radius)

    private static Mesh BuildDrifter()
    {
        // "Thorn": a crystalline tetra core with three swept spikes and a glowing heart.
        var b = new MeshBuilder();
        b.Frustum(3, 0.62f, 0f, -0.25f, 0.55f, Mat.Hull, Mat.Hull, Mat.Trim);
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * MathF.Tau;
            b.At(new Vector3(MathF.Cos(a) * 0.45f, MathF.Sin(a) * 0.45f, 0.05f), a, MathF.PI * 0.5f - 0.15f);
            b.Frustum(4, 0.16f, 0f, 0f, 0.75f, Mat.Trim, Mat.Glow, Mat.Hull, MathF.PI * 0.25f);
        }
        b.At(new Vector3(0, 0, 0.32f));
        b.Octahedron(0.22f, 0.22f, 0.3f, Mat.Core, Mat.Glow);
        return b.Build();
    }

    private static Mesh BuildOrbiter()
    {
        // "Halo saucer": lens-shaped hull, glowing equator, raised dome.
        var b = new MeshBuilder();
        b.Frustum(10, 0.95f, 0.55f, 0f, 0.22f, Mat.Trim, Mat.Hull, Mat.Hull);
        b.Frustum(10, 0.95f, 0.55f, 0f, -0.18f, Mat.Hull, Mat.Hull, Mat.Hull);
        b.At(new Vector3(0, 0, 0.22f));
        b.Frustum(10, 0.5f, 0.22f, 0f, 0.26f, Mat.Glow, Mat.Core, Mat.Hull);
        b.At(new Vector3(0, 0, 0.02f));
        b.Torus(0.98f, 0.055f, 20, 4, Mat.Glow);
        return b.Build();
    }

    private static Mesh BuildPod()
    {
        var b = new MeshBuilder();
        b.Octahedron(0.2f, 0.12f, 0.12f, Mat.Trim, Mat.Glow);
        return b.Build();
    }

    private static Mesh BuildSniper()
    {
        // "Lancer": long diamond hull with a forward rail barrel and a lens.
        var b = new MeshBuilder();
        b.Octahedron(0.85f, 0.42f, 0.32f, Mat.Hull, Mat.Trim);
        b.At(new Vector3(0.4f, 0, 0.22f));
        b.Loft(new Vector4[]
        {
            new(0f, 0.11f, 0.11f, 0f),
            new(1.35f, 0.08f, 0.08f, 0f),
            new(1.5f, 0.14f, 0.14f, 0f),
        }, 6, Mat.Trim, Mat.Core, Mat.Hull);
        for (int side = -1; side <= 1; side += 2)
        {
            b.At(new Vector3(-0.2f, side * 0.3f, 0.05f));
            b.Slab(side > 0
                ? new Vector2[] { new(0.2f, 0f), new(-0.55f, 0.55f), new(-0.75f, 0.5f), new(-0.4f, 0f) }
                : new Vector2[] { new(-0.4f, 0f), new(-0.75f, -0.5f), new(-0.55f, -0.55f), new(0.2f, 0f) },
                0.07f, Mat.Glow, Mat.Trim);
        }
        b.At(new Vector3(0.1f, 0, 0.36f));
        b.Octahedron(0.16f, 0.16f, 0.12f, Mat.Core, Mat.Glow);
        return b.Build();
    }

    private static Mesh BuildDasher()
    {
        // "Blade": a swept arrowhead with glowing leading edges.
        var b = new MeshBuilder();
        b.At(new Vector3(0, 0, 0.1f));
        b.Prism(new Vector2[] { new(1.15f, 0f), new(-0.55f, 0.9f), new(-0.3f, 0f), new(-0.55f, -0.9f) }, -0.12f, 0.12f, Mat.Trim, Mat.Hull, Mat.Hull);
        b.At(new Vector3(0, 0, 0.22f));
        b.Prism(new Vector2[] { new(0.75f, 0f), new(-0.3f, 0.3f), new(-0.2f, 0f), new(-0.3f, -0.3f) }, 0f, 0.2f, Mat.Glow, Mat.Hull);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 a = new(1.12f, 0f), c = new(-0.55f, 0.9f * side);
            Vector2 mid = (a + c) * 0.5f;
            Vector2 d = c - a;
            b.At(new Vector3(mid, 0.1f), MathF.Atan2(d.Y, d.X));
            b.Box(new Vector3(d.Length(), 0.07f, 0.28f), Mat.Glow, Mat.Core);
        }
        b.At(new Vector3(-0.35f, 0, 0.15f), 0, MathF.PI * 0.5f);
        b.Frustum(6, 0.17f, 0.12f, 0f, 0.15f, Mat.Hull, Mat.Core, Mat.Core);
        return b.Build();
    }

    private static Mesh BuildTurretBase()
    {
        var b = new MeshBuilder();
        b.Frustum(8, 1.0f, 0.82f, -0.1f, 0.2f, Mat.Hull, Mat.Trim, Mat.Hull, MathF.PI / 8f);
        b.At(new Vector3(0, 0, 0.2f));
        b.Frustum(8, 0.82f, 0.78f, 0f, 0.06f, Mat.Glow, Mat.Hull, Mat.Hull, MathF.PI / 8f);
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathF.Tau + MathF.PI / 4f;
            b.At(new Vector3(MathF.Cos(a) * 0.86f, MathF.Sin(a) * 0.86f, 0.05f), a);
            b.Box(new Vector3(0.3f, 0.34f, 0.36f), Mat.Trim, Mat.Glow);
        }
        return b.Build();
    }

    private static Mesh BuildTurretHead()
    {
        var b = new MeshBuilder();
        b.Frustum(6, 0.55f, 0.42f, 0f, 0.3f, Mat.Trim, Mat.Hull, Mat.Hull);
        for (int i = 0; i < 4; i++)
        {
            float a = i / 4f * MathF.Tau;
            b.At(new Vector3(MathF.Cos(a) * 0.4f, MathF.Sin(a) * 0.4f, 0.18f), a);
            b.Loft(new Vector4[] { new(0f, 0.1f, 0.1f, 0f), new(0.62f, 0.08f, 0.08f, 0f), new(0.7f, 0.12f, 0.12f, 0f) }, 6, Mat.Hull, Mat.Glow, Mat.Hull);
        }
        b.At(new Vector3(0, 0, 0.32f));
        b.Icosphere(0.22f, 1, Mat.Core, Mat.Glow, 0.4f);
        return b.Build();
    }

    private static Mesh BuildSplitterCore()
    {
        var b = new MeshBuilder();
        b.Icosphere(0.48f, 1, Mat.Hull, Mat.Glow, 0.22f, 31);
        return b.Build();
    }

    private static Mesh BuildSplitterCell()
    {
        var b = new MeshBuilder();
        b.Box(new Vector3(0.42f, 0.42f, 0.42f), Mat.Trim, Mat.Glow);
        b.At(new Vector3(0, 0, 0.215f));
        b.Box(new Vector3(0.26f, 0.26f, 0.02f), Mat.Core, Mat.Core);
        return b.Build();
    }

    private static Mesh BuildSwarm()
    {
        var b = new MeshBuilder();
        b.Prism(new Vector2[] { new(1.2f, 0f), new(-0.7f, 0.75f), new(-0.35f, 0f), new(-0.7f, -0.75f) }, -0.1f, 0.15f, Mat.Glow, Mat.Trim, Mat.Hull);
        b.At(new Vector3(0.1f, 0, 0.2f));
        b.Octahedron(0.3f, 0.18f, 0.12f, Mat.Core, Mat.Glow);
        return b.Build();
    }

    private static Mesh BuildBossCore()
    {
        var b = new MeshBuilder();
        b.Icosphere(0.42f, 2, Mat.Glow, Mat.Core, 0.18f, 99);
        b.Frustum(10, 0.7f, 0.5f, -0.3f, -0.05f, Mat.Hull, Mat.Trim, Mat.Hull);
        return b.Build();
    }

    private static Mesh BuildBossPillar()
    {
        // Unit-height pillar; scaled per-frame by the spectrum.
        var b = new MeshBuilder();
        b.Frustum(4, 0.09f, 0.07f, 0f, 1f, Mat.Hull, Mat.Glow, Mat.Hull, MathF.PI / 4f);
        b.At(new Vector3(0, 0, 1.05f));
        b.Octahedron(0.09f, 0.09f, 0.12f, Mat.Core, Mat.Glow);
        return b.Build();
    }

    private static Mesh BuildBossHalo()
    {
        var b = new MeshBuilder();
        b.Torus(1.0f, 0.035f, 48, 4, Mat.Trim, Mat.Glow, 4);
        return b.Build();
    }

    private static Mesh BuildBossShard()
    {
        var b = new MeshBuilder();
        b.Octahedron(0.22f, 0.07f, 0.07f, Mat.Glow, Mat.Trim);
        return b.Build();
    }

    private static Mesh BuildEliteRing()
    {
        var b = new MeshBuilder();
        b.Torus(1.0f, 0.04f, 28, 4, Mat.Glow, Mat.Core, 7);
        return b.Build();
    }

    private static Mesh BuildWeaverHead()
    {
        // serpent head: flat arrow skull with glowing eyes and mandibles
        var b = new MeshBuilder();
        b.Loft(new Vector4[] { new(-0.6f, 0.35f, 0.22f, 0.1f), new(0.1f, 0.55f, 0.3f, 0.15f), new(0.8f, 0.2f, 0.14f, 0.1f), new(1.05f, 0.04f, 0.04f, 0.05f) }, 6, Mat.Trim, Mat.Glow, Mat.Hull);
        for (int s = -1; s <= 1; s += 2)
        {
            b.At(new Vector3(0.45f, s * 0.22f, 0.32f));
            b.Octahedron(0.12f, 0.08f, 0.08f, Mat.Core, Mat.Glow);
            b.At(new Vector3(0.75f, s * 0.3f, 0.05f), s * 0.5f);
            b.Slab(new Vector2[] { new(0f, 0f), new(0.45f, s * 0.12f), new(0.1f, s * 0.08f) }, 0.08f, Mat.Glow, Mat.Trim);
        }
        return b.Build();
    }

    private static Mesh BuildWeaverSegment()
    {
        var b = new MeshBuilder();
        b.Icosphere(0.32f, 1, Mat.Trim, Mat.Glow, 0.3f, 3);
        b.At(new Vector3(0, 0, 0.2f));
        b.Box(new Vector3(0.12f, 0.7f, 0.06f), Mat.Glow, Mat.Core);
        return b.Build();
    }

    private static Mesh BuildMortarBase()
    {
        var b = new MeshBuilder();
        b.Frustum(6, 1.0f, 0.75f, -0.1f, 0.35f, Mat.Hull, Mat.Trim, Mat.Hull, MathF.PI / 6f);
        for (int i = 0; i < 3; i++)
        {
            float a = i / 3f * MathF.Tau;
            b.At(new Vector3(MathF.Cos(a) * 0.95f, MathF.Sin(a) * 0.95f, 0f), a);
            b.Box(new Vector3(0.55f, 0.22f, 0.25f), Mat.Trim, Mat.Glow);
        }
        b.At(new Vector3(0, 0, 0.35f));
        b.Frustum(6, 0.5f, 0.46f, 0f, 0.08f, Mat.Glow, Mat.Hull, Mat.Hull);
        return b.Build();
    }

    private static Mesh BuildMortarBarrel()
    {
        // fat barrel along +x, raised by the caller toward the target
        var b = new MeshBuilder();
        b.Loft(new Vector4[] { new(-0.3f, 0.32f, 0.32f, 0f), new(0.6f, 0.28f, 0.28f, 0f), new(0.75f, 0.36f, 0.36f, 0f), new(0.9f, 0.3f, 0.3f, 0f) }, 8, Mat.Trim, Mat.Core, Mat.Hull);
        b.At(new Vector3(0.1f, 0, 0));
        b.Torus(0.33f, 0.05f, 12, 4, Mat.Glow);
        return b.Build();
    }

    private static Mesh BuildPhantom()
    {
        // tall split crystal with a hollow glowing heart
        var b = new MeshBuilder();
        b.At(new Vector3(0, 0, 0.35f));
        b.Octahedron(0.38f, 0.38f, 0.95f, Mat.Glow, Mat.Trim);
        b.At(new Vector3(0, 0, 0.35f));
        b.Torus(0.6f, 0.04f, 20, 4, Mat.Core);
        return b.Build();
    }

    private static Mesh BuildPrism()
    {
        // triangular prism with a lens on top
        var b = new MeshBuilder();
        b.Prism(new Vector2[] { new(1f, 0f), new(-0.5f, 0.87f), new(-0.5f, -0.87f) }, 0f, 0.55f, Mat.Trim, Mat.Hull, Mat.Hull);
        b.Prism(new Vector2[] { new(1.08f, 0f), new(-0.54f, 0.94f), new(-0.54f, -0.94f) }, 0.12f, 0.16f, Mat.Glow, Mat.Glow);
        b.At(new Vector3(0, 0, 0.62f));
        b.Icosphere(0.32f, 1, Mat.Core, Mat.Glow, 0.4f, 9);
        return b.Build();
    }

    // ---------------------------------------------------------------- environment

    /// <summary>Unit-height border pylon; height is driven by the spectrum.</summary>
    private static Mesh BuildCrystal()
    {
        var b = new MeshBuilder();
        b.Octahedron(0.5f, 0.5f, 1f, Mat.Glow, Mat.Core);
        return b.Build();
    }

    /// <summary>Monolith built from an obstacle's own polygon. Cached per obstacle.</summary>
    public static Mesh Monolith(Obstacle o)
    {
        if (MonolithCache.TryGetValue(o, out var cached)) return cached;
        var b = new MeshBuilder();
        int n = o.Shape.Length;
        float h = o.Radius * 1.05f;
        var poly = new Vector2[n];
        var inset = new Vector2[n];
        var band = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            poly[i] = o.Shape[i] * 0.86f;
            band[i] = o.Shape[i] * 0.875f;
            inset[i] = o.Shape[i] * 0.62f;
        }
        b.Prism(poly, 0f, h, Mat.Hull, Mat.Hull);
        // glowing seams at two heights
        b.Prism(band, h * 0.18f, h * 0.2f, Mat.Glow, Mat.Glow);
        b.Prism(band, h * 0.86f, h * 0.885f, Mat.Glow, Mat.Glow);
        // bevelled crown
        for (int i = 0; i < n; i++)
        {
            var a0 = poly[i]; var a1 = poly[(i + 1) % n];
            var c0 = inset[i]; var c1 = inset[(i + 1) % n];
            b.Quad(new Vector3(a0, h), new Vector3(a1, h), new Vector3(c1, h + o.Radius * 0.14f), new Vector3(c0, h + o.Radius * 0.14f), Mat.Trim, new Vector3(0, 0, h * 0.5f));
        }
        b.Prism(inset, h + o.Radius * 0.14f, h + o.Radius * 0.16f, Mat.Trim, Mat.Hull);
        // vertical ribs at every other corner
        for (int i = 0; i < n; i += 2)
        {
            var c = o.Shape[i] * 0.885f;
            b.At(new Vector3(c, h * 0.5f), MathF.Atan2(c.Y, c.X));
            b.Box(new Vector3(o.Radius * 0.05f, o.Radius * 0.05f, h * 0.62f), Mat.Glow, Mat.Glow);
        }
        var mesh = b.Build();
        MonolithCache.AddOrUpdate(o, mesh);
        return mesh;
    }

    // ---------------------------------------------------------------- backdrop

    /// <summary>Unit-height equaliser column for the backdrop skyline.</summary>
    private static Mesh BuildWallBlock()
    {
        var b = new MeshBuilder();
        b.Box(new Vector3(1f, 1f, 0.86f), Mat.Hull, Mat.Trim);
        b.At(new Vector3(0, 0, 0.45f));
        b.Box(new Vector3(1f, 0.36f, 0.1f), Mat.Glow, Mat.Core);
        return b.Build();
    }

    private static Mesh BuildColumn()
    {
        var b = new MeshBuilder();
        b.Frustum(4, 0.5f, 0.5f, 0f, 0.96f, Mat.Hull, Mat.Trim, Mat.Hull, MathF.PI / 4f);
        b.At(new Vector3(0, 0, 0.96f));
        b.Frustum(4, 0.5f, 0.5f, 0f, 0.04f, Mat.Glow, Mat.Core, Mat.Glow, MathF.PI / 4f);
        return b.Build();
    }

    private static Mesh BuildIco()
    {
        var b = new MeshBuilder();
        b.Icosphere(1f, 0, Mat.Trim, Mat.Glow, 0.35f, 5);
        return b.Build();
    }

    private static Mesh BuildOcta()
    {
        var b = new MeshBuilder();
        b.Octahedron(1f, 1f, 1.35f, Mat.Trim, Mat.Glow);
        return b.Build();
    }

    private static Mesh BuildRing()
    {
        var b = new MeshBuilder();
        b.Torus(1f, 0.012f, 64, 3, Mat.Glow);
        return b.Build();
    }

    private static Mesh BuildHaloSegment()
    {
        var b = new MeshBuilder();
        b.Box(new Vector3(1f, 1f, 1f), Mat.Trim, Mat.Glow);
        return b.Build();
    }

    private static Mesh BuildKnot()
    {
        // A twisted torus ring made of beads: reads as "3D" at any rotation.
        var b = new MeshBuilder();
        for (int i = 0; i < 36; i++)
        {
            float t = i / 36f * MathF.Tau;
            float r = 1f + 0.35f * MathF.Cos(3f * t);
            Vector3 p = new(MathF.Cos(2f * t) * r, MathF.Sin(2f * t) * r, 0.45f * MathF.Sin(3f * t));
            b.At(p, t);
            b.Octahedron(0.16f, 0.11f, 0.11f, i % 3 == 0 ? Mat.Glow : Mat.Trim, Mat.Hull);
        }
        return b.Build();
    }

    private static Mesh BuildPickupGem()
    {
        var b = new MeshBuilder();
        b.Octahedron(1f, 1f, 1.4f, Mat.Glow, Mat.Core);
        return b.Build();
    }
}
