using System.Numerics;
using Nullwave.Core;

namespace Nullwave.World;

/// <summary>Which playfield a run uses: the original open arena, or one of the experimental maps.</summary>
public enum MapKind
{
    Arena = 0,
    Halo = 1,
    Cathedral = 2,
}

/// <summary>
/// The experimental maps. Each one is a set of rooms and corridors (signed-distance shapes)
/// unioned into a single connected footprint; <see cref="MapGeometry"/> turns it into the wall
/// segments that drive the floor art, collision, spawning, navigation and the minimap.
/// </summary>
public static class MapCatalog
{
    public static readonly MapKind[] All = { MapKind.Arena, MapKind.Halo, MapKind.Cathedral };

    public static string Name(MapKind k) => k switch
    {
        MapKind.Halo => "HALO CIRCUIT",
        MapKind.Cathedral => "CATHEDRAL",
        _ => "OPEN ARENA",
    };

    public static string Blurb(MapKind k) => k switch
    {
        MapKind.Halo => "EXPERIMENTAL // FOUR ROOMS ON A RING, A NARROW SPINE THROUGH THE CORE",
        MapKind.Cathedral => "EXPERIMENTAL // PILLARED NAVE, APSE, CHAPELS AND A LONG CLOISTER LOOP",
        _ => "THE ORIGINAL OPEN ARENA",
    };

    public static bool IsExperimental(MapKind k) => k != MapKind.Arena;

    public static MapKind Next(MapKind k, int dir)
    {
        int i = Array.IndexOf(All, k);
        if (i < 0) i = 0;
        return All[((i + dir) % All.Length + All.Length) % All.Length];
    }

    /// <summary>Parses "arena", "halo", "cathedral" (and the display names), case-insensitive.</summary>
    public static bool TryParse(string? s, out MapKind kind)
    {
        kind = MapKind.Arena;
        if (string.IsNullOrWhiteSpace(s)) return false;
        string t = s.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
        switch (t)
        {
            case "arena": case "open": case "openarena": case "off": case "none": kind = MapKind.Arena; return true;
            case "halo": case "halocircuit": case "circuit": case "ring": kind = MapKind.Halo; return true;
            case "cathedral": case "nave": kind = MapKind.Cathedral; return true;
        }
        kind = MapKind.Arena;
        return false;
    }

    /// <summary>Builds the geometry for a map at an arena scale (null for the open arena).</summary>
    public static MapGeometry? Build(MapKind kind, float scale)
    {
        var def = Define(kind);
        return def == null ? null : new MapGeometry(def, MathF.Max(0.4f, scale));
    }

    private static readonly Dictionary<MapKind, MapGeometry> PreviewCache = new();

    /// <summary>Shared, unscaled geometry for UI previews (never used by a simulation).</summary>
    public static MapGeometry? Preview(MapKind kind)
    {
        if (kind == MapKind.Arena) return null;
        if (!PreviewCache.TryGetValue(kind, out var g))
        {
            g = Build(kind, 1f)!;
            PreviewCache[kind] = g;
        }
        return g;
    }

    public static MapDefinition? Define(MapKind kind)
    {
        switch (kind)
        {
            case MapKind.Halo:
            {
                // A ring of four rooms joined by wide, bent corridors, with a narrow spine through
                // the core chamber as the chokepoint shortcut between the west dock and the east halo.
                var d = new MapDefinition(kind, new Vector2(2700f, 1700f), new Vector2(-2000f, 0f));
                d.Circle(-2000f, 0f, 540f);                       // WEST DOCK (start)
                d.Box(-200f, -1150f, 650f, 400f, 170f);           // NORTH GALLERY
                d.Box(-200f, 1150f, 650f, 400f, 170f);            // SOUTH GALLERY
                d.Circle(1750f, 0f, 800f);                        // EAST HALO (the big fight room)
                d.Capsule(-2000f, -300f, -2000f, -1150f, 200f);   // dock -> north (bent)
                d.Capsule(-2000f, -1150f, -700f, -1150f, 200f);
                d.Capsule(-2000f, 300f, -2000f, 1150f, 200f);     // dock -> south (bent)
                d.Capsule(-2000f, 1150f, -700f, 1150f, 200f);
                d.Capsule(300f, -1150f, 1750f, -1150f, 200f);     // north -> halo (bent)
                d.Capsule(1750f, -1150f, 1750f, -600f, 200f);
                d.Capsule(300f, 1150f, 1750f, 1150f, 200f);       // south -> halo (bent)
                d.Capsule(1750f, 1150f, 1750f, 600f, 200f);
                d.Capsule(-1600f, 0f, 1100f, 0f, 150f);           // the spine (chokepoint)
                d.Circle(-250f, 0f, 360f);                        // CORE chamber on the spine
                d.Room("WEST DOCK", -2000f, 0f, 480f);
                d.Room("NORTH GALLERY", -200f, -1150f, 360f);
                d.Room("SOUTH GALLERY", -200f, 1150f, 360f);
                d.Room("EAST HALO", 1750f, 0f, 700f);
                d.Room("CORE", -250f, 0f, 300f);
                return d;
            }
            case MapKind.Cathedral:
            {
                // A pillared nave between a narthex and a round apse, two side chapels, and loops:
                // north chapel -> apse, and a long south cloister back round to the narthex.
                var d = new MapDefinition(kind, new Vector2(2750f, 1800f), new Vector2(-2250f, 0f));
                d.Box(-2250f, 0f, 420f, 360f, 120f);              // NARTHEX (start)
                d.Capsule(-1950f, 0f, -1450f, 0f, 230f);          // narthex -> nave
                d.Box(-50f, 0f, 1500f, 560f, 0f);                 // NAVE
                d.Circle(1850f, 0f, 780f);                        // APSE (the big fight room)
                d.Circle(-300f, -1300f, 430f);                    // NORTH CHAPEL
                d.Capsule(-300f, -500f, -300f, -950f, 190f);
                d.Circle(300f, 1300f, 430f);                      // SOUTH CHAPEL
                d.Capsule(300f, 500f, 300f, 950f, 190f);
                d.Capsule(-300f, -1300f, 1300f, -1300f, 190f);    // north chapel -> apse
                d.Capsule(1300f, -1300f, 1700f, -650f, 190f);
                d.Capsule(300f, 1300f, -2250f, 1300f, 190f);      // south cloister -> narthex
                d.Capsule(-2250f, 1300f, -2250f, 250f, 190f);
                foreach (float x in new[] { -1100f, -600f, -100f, 400f, 900f })
                {
                    d.Pillar(x, -250f, 80f);
                    d.Pillar(x, 250f, 80f);
                }
                d.Room("NARTHEX", -2250f, 0f, 320f);
                d.Room("APSE", 1850f, 0f, 700f);
                d.Room("NORTH CHAPEL", -300f, -1300f, 380f);
                d.Room("SOUTH CHAPEL", 300f, 1300f, 380f);
                return d;
            }
            default:
                return null;
        }
    }
}

/// <summary>Named open space inside a map (used for tests, captures and the minimap).</summary>
public readonly record struct MapRoom(string Name, Vector2 Center, float Radius);

/// <summary>Authoring form of a map: floor shapes, pillar holes, the spawn and named rooms.</summary>
public sealed class MapDefinition
{
    public readonly MapKind Kind;
    public readonly Vector2 HalfExtents;
    public readonly Vector2 Spawn;
    internal readonly List<Func<Vector2, float, float>> Floor = new();
    internal readonly List<Func<Vector2, float, float>> Holes = new();
    public readonly List<MapRoom> Rooms = new();

    public MapDefinition(MapKind kind, Vector2 halfExtents, Vector2 spawn)
    {
        Kind = kind;
        HalfExtents = halfExtents;
        Spawn = spawn;
    }

    // Each shape is an SDF in unscaled map units: f(p / scale) * scale.
    public void Circle(float x, float y, float r) => Floor.Add((p, s) => Vector2.Distance(p, new Vector2(x, y) * s) - r * s);
    public void Pillar(float x, float y, float r) => Holes.Add((p, s) => Vector2.Distance(p, new Vector2(x, y) * s) - r * s);

    public void Box(float x, float y, float hx, float hy, float chamfer) => Floor.Add((p, s) =>
    {
        Vector2 q = Vector2.Abs(p - new Vector2(x, y) * s) - new Vector2(hx, hy) * s;
        float box = Vector2.Max(q, Vector2.Zero).Length() + MathF.Min(MathF.Max(q.X, q.Y), 0f);
        if (chamfer <= 0f) return box;
        Vector2 a = Vector2.Abs(p - new Vector2(x, y) * s);
        float cut = (a.X + a.Y - (hx + hy - chamfer) * s) * 0.70710678f;
        return MathF.Max(box, cut);
    });

    public void Capsule(float ax, float ay, float bx, float by, float halfWidth) => Floor.Add((p, s) =>
    {
        Vector2 a = new Vector2(ax, ay) * s, b = new Vector2(bx, by) * s;
        return Vector2.Distance(p, World.SegmentClosest(a, b, p)) - halfWidth * s;
    });

    public void Room(string name, float x, float y, float r) => Rooms.Add(new MapRoom(name, new Vector2(x, y), r));

    public float Sdf(Vector2 p, float s)
    {
        float d = float.MaxValue;
        foreach (var f in Floor) d = MathF.Min(d, f(p, s));
        foreach (var h in Holes) d = MathF.Max(d, -h(p, s));
        return d;
    }
}

/// <summary>
/// Shared geometry of an experimental map. The floor footprint is the zero set of the map's
/// signed distance field, traced once with marching squares into closed wall loops. Everything
/// reads these same wall segments: the rendered floor and walls, the even-odd floor test, circle
/// collision with sliding, swept bullet and laser hits, line of sight, safe placement, the
/// navigation graph and the minimap.
/// </summary>
public sealed class MapGeometry
{
    public readonly MapKind Kind;
    public readonly string Name;
    public readonly float Scale;
    public readonly Vector2 HalfExtents;
    public readonly Vector2 Spawn;
    public readonly MapRoom[] Rooms;

    /// <summary>Wall segments (A to B) and their unit normals pointing into the floor.</summary>
    public readonly Vector2[] SegA, SegB, SegN;
    /// <summary>Closed wall loops (the outer boundary and the pillar holes).</summary>
    public readonly List<Vector2[]> Loops = new();
    /// <summary>Exact floor fill: trapezoids (a, b, c, d) from an even-odd sweep of the loops.</summary>
    public readonly List<(Vector2 A, Vector2 B, Vector2 C, Vector2 D)> FloorQuads = new();
    /// <summary>Pylon spots spaced along the walls, set just outside the floor.</summary>
    public readonly List<Vector2> PylonSpots = new();
    public readonly float FloorArea;

    // spatial hash: per cell, the segments whose bounds touch it, and whether its centre is floor
    private const float HashCell = 128f;
    private readonly Vector2 _hashOrigin;
    private readonly int _hw, _hh;
    private readonly int[][] _cellSegs;
    private readonly bool[] _cellCenterFloor;
    /// <summary>Per hash row, the segments whose Y range overlaps it (for the half-open floor test).</summary>
    private readonly int[][] _rowSegs;
    private readonly int[] _segStamp;
    private int _stamp;
    private readonly List<int> _scratch = new(64);

    // navigation graph
    public readonly float NavCell;
    public readonly int NavW, NavH;
    public readonly Vector2 NavOrigin;
    /// <summary>Body radius the shared enemy field is built for (the largest normal enemy, an elite Splitter at 44.9, plus room).</summary>
    public readonly float NavClearance;
    /// <summary>Smallest clearance for a node to be in the graph at all.</summary>
    public const float MinNodeClearance = 20f;
    public readonly bool[] Walkable;
    public readonly float[] NodeClearance;
    /// <summary>Per node, 8 neighbour indices (-1 = none).</summary>
    public readonly int[] Links;
    /// <summary>Per link, the largest body radius that can travel it (straight between the node centres).</summary>
    public readonly float[] LinkClearance;
    private const float LinkClearanceCap = 160f;

    public MapGeometry(MapDefinition def, float scale)
    {
        Kind = def.Kind;
        Name = MapCatalog.Name(def.Kind);
        Scale = scale;
        HalfExtents = def.HalfExtents * scale;
        Spawn = def.Spawn * scale;
        Rooms = def.Rooms.Select(r => new MapRoom(r.Name, r.Center * scale, r.Radius * scale)).ToArray();

        TraceLoops(def, scale);
        int n = Loops.Sum(l => l.Length);
        SegA = new Vector2[n];
        SegB = new Vector2[n];
        SegN = new Vector2[n];
        int k = 0;
        foreach (var loop in Loops)
            for (int i = 0; i < loop.Length; i++)
            {
                Vector2 a = loop[i], b = loop[(i + 1) % loop.Length];
                Vector2 nn = M.SafeNorm(M.Perp(b - a));
                Vector2 mid = (a + b) * 0.5f;
                if (def.Sdf(mid + nn * 4f, scale) > 0f) nn = -nn;
                SegA[k] = a; SegB[k] = b; SegN[k] = nn;
                k++;
            }

        // hash
        _hashOrigin = -HalfExtents - new Vector2(HashCell * 2f);
        _hw = (int)MathF.Ceiling(HalfExtents.X * 2f / HashCell) + 4;
        _hh = (int)MathF.Ceiling(HalfExtents.Y * 2f / HashCell) + 4;
        var lists = new List<int>[_hw * _hh];
        for (int s = 0; s < n; s++)
        {
            Vector2 mn = Vector2.Min(SegA[s], SegB[s]) - Vector2.One, mx = Vector2.Max(SegA[s], SegB[s]) + Vector2.One;
            CellRange(mn, mx, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    (lists[y * _hw + x] ??= new List<int>(4)).Add(s);
        }
        _cellSegs = new int[_hw * _hh][];
        _cellCenterFloor = new bool[_hw * _hh];
        for (int i = 0; i < lists.Length; i++)
        {
            _cellSegs[i] = lists[i]?.ToArray() ?? Array.Empty<int>();
            Vector2 c = _hashOrigin + new Vector2((i % _hw + 0.5f) * HashCell, (i / _hw + 0.5f) * HashCell);
            _cellCenterFloor[i] = EvenOddBrute(c);
        }
        _segStamp = new int[n];
        _rowSegs = new int[_hh][];
        for (int y = 0; y < _hh; y++)
        {
            float y0 = _hashOrigin.Y + y * HashCell, y1 = y0 + HashCell;
            var row = new List<int>();
            for (int sgi = 0; sgi < n; sgi++)
                if (MathF.Max(SegA[sgi].Y, SegB[sgi].Y) >= y0 - 1f && MathF.Min(SegA[sgi].Y, SegB[sgi].Y) <= y1 + 1f) row.Add(sgi);
            _rowSegs[y] = row.ToArray();
        }

        BuildFloorQuads();
        FloorArea = FloorQuads.Sum(q => TrapArea(q.A, q.B, q.C, q.D));
        BuildPylons();

        // navigation graph
        NavCell = 80f * scale;
        NavClearance = 48f;
        NavOrigin = -HalfExtents;
        NavW = (int)MathF.Ceiling(HalfExtents.X * 2f / NavCell);
        NavH = (int)MathF.Ceiling(HalfExtents.Y * 2f / NavCell);
        Walkable = new bool[NavW * NavH];
        NodeClearance = new float[NavW * NavH];
        for (int i = 0; i < Walkable.Length; i++)
        {
            Vector2 c = NodeCenter(i);
            if (!IsFloor(c)) continue;
            float clear = float.MaxValue;
            for (int s = 0; s < n; s++) clear = MathF.Min(clear, Vector2.Distance(c, World.SegmentClosest(SegA[s], SegB[s], c)));
            NodeClearance[i] = clear;
            Walkable[i] = clear >= MinNodeClearance;
        }
        Links = new int[Walkable.Length * 8];
        LinkClearance = new float[Walkable.Length * 8];
        Array.Fill(Links, -1);
        for (int i = 0; i < Walkable.Length; i++)
        {
            if (!Walkable[i]) continue;
            int x = i % NavW, y = i / NavW;
            for (int d = 0; d < 8; d++)
            {
                int nx = x + NavDx[d], ny = y + NavDy[d];
                if (nx < 0 || ny < 0 || nx >= NavW || ny >= NavH) continue;
                int j = ny * NavW + nx;
                if (!Walkable[j]) continue;
                if (d >= 4 && (!Walkable[y * NavW + nx] || !Walkable[ny * NavW + x])) continue;
                float clear = SegmentClearance(NodeCenter(i), NodeCenter(j), LinkClearanceCap);
                if (clear < MinNodeClearance) continue;
                Links[i * 8 + d] = j;
                LinkClearance[i * 8 + d] = clear;
            }
        }
    }

    internal static readonly int[] NavDx = { 1, -1, 0, 0, 1, 1, -1, -1 };
    internal static readonly int[] NavDy = { 0, 0, 1, -1, 1, -1, 1, -1 };

    public int SegmentCount => SegA.Length;
    public Vector2 NodeCenter(int i) => NavOrigin + new Vector2((i % NavW + 0.5f) * NavCell, (i / NavW + 0.5f) * NavCell);

    public int NodeAt(Vector2 p)
    {
        int x = (int)MathF.Floor((p.X - NavOrigin.X) / NavCell), y = (int)MathF.Floor((p.Y - NavOrigin.Y) / NavCell);
        if (x < 0 || y < 0 || x >= NavW || y >= NavH) return -1;
        return y * NavW + x;
    }

    /// <summary>The walkable node nearest a point that it can see (falls back to any walkable node).</summary>
    public int NearestWalkableNode(Vector2 p, float minClearance = 0f)
    {
        int own = NodeAt(p);
        if (own >= 0 && Walkable[own] && NodeClearance[own] >= minClearance && SegmentClear(p, NodeCenter(own))) return own;
        if (own >= 0)
        {
            int ox = own % NavW, oy = own / NavW;
            int best = -1;
            float bestD = float.MaxValue;
            for (int ring = 1; ring <= 3 && best < 0; ring++)
                for (int y = oy - ring; y <= oy + ring; y++)
                    for (int x = ox - ring; x <= ox + ring; x++)
                    {
                        if (x < 0 || y < 0 || x >= NavW || y >= NavH) continue;
                        if (Math.Max(Math.Abs(x - ox), Math.Abs(y - oy)) != ring) continue;
                        int j = y * NavW + x;
                        if (!Walkable[j] || NodeClearance[j] < minClearance) continue;
                        float d = Vector2.DistanceSquared(p, NodeCenter(j));
                        if (d < bestD && SegmentClear(p, NodeCenter(j))) { bestD = d; best = j; }
                    }
            if (best >= 0) return best;
        }
        int any = NearestNodeBrute(p, minClearance);
        return any;
    }

    /// <summary>Nearest node with at least the given clearance, or -1 if no node has that much room.</summary>
    public int NearestNodeBrute(Vector2 p, float minClearance)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < Walkable.Length; i++)
        {
            if (!Walkable[i] || NodeClearance[i] < minClearance) continue;
            float d = Vector2.DistanceSquared(p, NodeCenter(i));
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Nearest node with the clearance that p can see in a straight line, or -1.</summary>
    public int NearestVisibleNode(Vector2 p, float minClearance)
    {
        var order = new List<(float d, int i)>();
        for (int i = 0; i < Walkable.Length; i++)
            if (Walkable[i] && NodeClearance[i] >= minClearance) order.Add((Vector2.DistanceSquared(p, NodeCenter(i)), i));
        order.Sort((a, b) => a.d.CompareTo(b.d));
        int tries = 0;
        foreach (var (_, i) in order)
        {
            if (SegmentClear(p, NodeCenter(i))) return i;
            if (++tries > 48) break;
        }
        return -1;
    }

    /// <summary>The node with the most room (last resort; never -1 for a real map).</summary>
    public int RoomiestNode()
    {
        int best = -1;
        float bestClear = -1f;
        for (int i = 0; i < Walkable.Length; i++)
            if (Walkable[i] && NodeClearance[i] > bestClear) { bestClear = NodeClearance[i]; best = i; }
        return best;
    }

    // ------------------------------------------------------------------ queries

    private void CellRange(Vector2 mn, Vector2 mx, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = Math.Clamp((int)MathF.Floor((mn.X - _hashOrigin.X) / HashCell), 0, _hw - 1);
        y0 = Math.Clamp((int)MathF.Floor((mn.Y - _hashOrigin.Y) / HashCell), 0, _hh - 1);
        x1 = Math.Clamp((int)MathF.Floor((mx.X - _hashOrigin.X) / HashCell), 0, _hw - 1);
        y1 = Math.Clamp((int)MathF.Floor((mx.Y - _hashOrigin.Y) / HashCell), 0, _hh - 1);
    }

    /// <summary>Unique segment indices whose bounds touch the box (reuses an internal list).</summary>
    private List<int> Query(Vector2 mn, Vector2 mx)
    {
        _scratch.Clear();
        CellRange(mn, mx, out int x0, out int y0, out int x1, out int y1);
        if ((x1 - x0 + 1) * (y1 - y0 + 1) > SegA.Length / 2)
        {
            for (int s = 0; s < SegA.Length; s++) _scratch.Add(s);
            return _scratch;
        }
        if (++_stamp == int.MaxValue) { Array.Clear(_segStamp); _stamp = 1; }
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                foreach (int s in _cellSegs[y * _hw + x])
                    if (_segStamp[s] != _stamp) { _segStamp[s] = _stamp; _scratch.Add(s); }
        return _scratch;
    }

    private bool EvenOddBrute(Vector2 p)
    {
        bool inside = false;
        for (int s = 0; s < SegA.Length; s++)
        {
            Vector2 a = SegA[s], b = SegB[s];
            if ((a.Y > p.Y) != (b.Y > p.Y))
            {
                float x = a.X + (p.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                if (x > p.X) inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>True if the point is on the map's floor (exact against the wall segments).</summary>
    public bool IsFloor(Vector2 p)
    {
        Vector2 rel = p - _hashOrigin;
        int x = (int)MathF.Floor(rel.X / HashCell), y = (int)MathF.Floor(rel.Y / HashCell);
        if (x < 0 || y < 0 || x >= _hw || y >= _hh) return false;
        int cell = y * _hw + x;
        // no wall touches this cell: the whole cell shares its centre's classification
        if (_cellSegs[cell].Length == 0) return _cellCenterFloor[cell];
        // otherwise the same half-open even-odd rule as the floor fill (a ray to +x, counting each
        // segment whose Y range straddles p.Y with a strict test on one end), so a ray through a
        // shared vertex counts exactly once and a tangent touch counts zero or twice
        bool inside = false;
        foreach (int s in _rowSegs[y])
        {
            Vector2 a = SegA[s], b = SegB[s];
            if ((a.Y > p.Y) != (b.Y > p.Y))
            {
                float cx = a.X + (p.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                if (cx > p.X) inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>Reference floor test over every segment (tests compare <see cref="IsFloor"/> against it).</summary>
    internal bool IsFloorBrute(Vector2 p) => EvenOddBrute(p);

    /// <summary>Distance from the segment a-b to the nearest wall, capped (0 if it crosses one).</summary>
    public float SegmentClearance(Vector2 a, Vector2 b, float cap)
    {
        float best = cap;
        Vector2 pad = new(cap + 1f);
        var list = Query(Vector2.Min(a, b) - pad, Vector2.Max(a, b) + pad);
        foreach (int s in list)
        {
            Vector2 sa = SegA[s], sb = SegB[s];
            if (SegmentsCross(a, b, sa, sb, out _)) return 0f;
            best = MathF.Min(best, Vector2.Distance(sa, World.SegmentClosest(a, b, sa)));
            best = MathF.Min(best, Vector2.Distance(sb, World.SegmentClosest(a, b, sb)));
            best = MathF.Min(best, Vector2.Distance(a, World.SegmentClosest(sa, sb, a)));
            best = MathF.Min(best, Vector2.Distance(b, World.SegmentClosest(sa, sb, b)));
        }
        return best;
    }

    /// <summary>Distance from the point to the nearest wall, capped at <paramref name="maxR"/>.</summary>
    public float Clearance(Vector2 p, float maxR)
    {
        float best = maxR;
        var list = Query(p - new Vector2(maxR), p + new Vector2(maxR));
        foreach (int s in list)
        {
            float d = Vector2.Distance(p, World.SegmentClosest(SegA[s], SegB[s], p));
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>A body of this radius may stand here: on the floor and clear of every wall.</summary>
    public bool IsBodyValid(Vector2 p, float radius) => IsFloor(p) && Clearance(p, radius) >= radius - 0.75f;

    private static bool SegmentsCross(Vector2 p, Vector2 q, Vector2 a, Vector2 b, out float t)
    {
        t = 0f;
        Vector2 r = q - p, s = b - a;
        float den = r.X * s.Y - r.Y * s.X;
        if (MathF.Abs(den) < 1e-9f) return false;
        Vector2 ap = a - p;
        t = (ap.X * s.Y - ap.Y * s.X) / den;
        float u = (ap.X * r.Y - ap.Y * r.X) / den;
        return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
    }

    /// <summary>First wall crossed going from a to b: fraction along a-b, the hit point and the wall's floor-side normal.</summary>
    public bool SegmentHit(Vector2 a, Vector2 b, out float t, out Vector2 point, out Vector2 normal)
    {
        t = float.MaxValue;
        normal = Vector2.Zero;
        var list = Query(Vector2.Min(a, b) - Vector2.One, Vector2.Max(a, b) + Vector2.One);
        foreach (int s in list)
        {
            if (SegmentsCross(a, b, SegA[s], SegB[s], out float ts) && ts < t)
            {
                t = ts;
                normal = SegN[s];
            }
        }
        if (t == float.MaxValue) { t = 1f; point = b; return false; }
        point = a + (b - a) * t;
        return true;
    }

    /// <summary>All wall crossings along a-b, as sorted fractions (into the given list).</summary>
    public void SegmentCrossings(Vector2 a, Vector2 b, List<float> into)
    {
        into.Clear();
        var list = Query(Vector2.Min(a, b) - Vector2.One, Vector2.Max(a, b) + Vector2.One);
        foreach (int s in list)
            if (SegmentsCross(a, b, SegA[s], SegB[s], out float ts)) into.Add(ts);
        into.Sort();
    }

    /// <summary>
    /// No wall crosses a-b. This is a boundary test: callers pass a floor endpoint (two points
    /// in the void far from every wall also have nothing between them).
    /// </summary>
    public bool SegmentClear(Vector2 a, Vector2 b) => !SegmentHit(a, b, out _, out _, out _);

    /// <summary>A body of the given radius can travel straight from a to b without touching a wall (a must be on the floor).</summary>
    public bool CapsuleClear(Vector2 a, Vector2 b, float radius)
    {
        Vector2 pad = new(radius + 1f);
        var list = Query(Vector2.Min(a, b) - pad, Vector2.Max(a, b) + pad);
        float r2 = radius * radius;
        foreach (int s in list)
        {
            Vector2 sa = SegA[s], sb = SegB[s];
            if (SegmentsCross(a, b, sa, sb, out _)) return false;
            if (Vector2.DistanceSquared(sa, World.SegmentClosest(a, b, sa)) < r2) return false;
            if (Vector2.DistanceSquared(sb, World.SegmentClosest(a, b, sb)) < r2) return false;
            if (Vector2.DistanceSquared(a, World.SegmentClosest(sa, sb, a)) < r2) return false;
            if (Vector2.DistanceSquared(b, World.SegmentClosest(sa, sb, b)) < r2) return false;
        }
        return true;
    }

    /// <summary>Pushes a circle out of every wall it overlaps; returns the summed push normal.</summary>
    public Vector2 ResolveCircle(ref Vector2 p, float radius, int iterations = 4)
    {
        Vector2 sum = Vector2.Zero;
        for (int it = 0; it < iterations; it++)
        {
            bool any = false;
            var list = Query(p - new Vector2(radius), p + new Vector2(radius));
            foreach (int s in list)
            {
                Vector2 q = World.SegmentClosest(SegA[s], SegB[s], p);
                Vector2 d = p - q;
                float l2 = d.LengthSquared();
                if (l2 >= radius * radius) continue;
                float l = MathF.Sqrt(l2);
                Vector2 n = l > 1e-4f ? d / l : SegN[s];
                p = q + n * radius;
                sum += n;
                any = true;
            }
            if (!any) break;
        }
        return sum;
    }

    /// <summary>
    /// Moves a circle by <paramref name="delta"/> in sub-steps no longer than ~half its radius,
    /// pushing it out of walls after each step, so it slides along walls and can never cross one.
    /// Returns the summed wall normal it touched (zero if none).
    /// </summary>
    public Vector2 MoveCircle(ref Vector2 pos, Vector2 delta, float radius)
    {
        Vector2 normals = Vector2.Zero;
        float len = delta.Length();
        int steps = len < 1e-5f ? 1 : Math.Clamp((int)MathF.Ceiling(len / MathF.Max(2f, radius * 0.45f)), 1, 160);
        Vector2 step = delta / steps;
        float clearNow = -1f;
        for (int i = 0; i < steps; i++)
        {
            Vector2 cand = pos + step;
            normals += ResolveCircle(ref cand, radius);
            bool crossed = SegmentHit(pos, cand, out _, out _, out Vector2 hitN);
            bool ok = !crossed && IsFloor(cand);
            if (ok)
            {
                // the body itself must fit, not just its centre (opposing pushes in a passage that is
                // too narrow can leave it overlapping); a body that already overlaps may still move
                // if the step does not make that worse, so it can always work itself free
                float clear = Clearance(cand, radius);
                if (clear < radius - 0.75f)
                {
                    if (clearNow < 0f) clearNow = Clearance(pos, radius);
                    ok = clear >= clearNow - 0.01f;
                }
            }
            if (!ok)
            {
                normals += crossed ? hitN : M.SafeNorm(pos - cand);
                break;
            }
            pos = cand;
            clearNow = -1f;
        }
        return normals;
    }

    /// <summary>
    /// A valid place for a body near <paramref name="p"/>: p itself when it is, otherwise p pushed
    /// clear of the walls it is near, otherwise the nearest open navigation node.
    /// </summary>
    public Vector2 ProjectToFloor(Vector2 p, float margin)
    {
        TryProjectToFloor(p, margin, out Vector2 q);
        return q;
    }

    /// <summary>
    /// Like <see cref="ProjectToFloor"/>, but says whether the requested margin was met. When no
    /// node anywhere has that much room it returns false and the roomiest node instead. A floor
    /// request is corrected locally first, then to the nearest node it can see (same side of any
    /// partition); only after both fail does it fall back to the nearest node anywhere.
    /// </summary>
    public bool TryProjectToFloor(Vector2 p, float margin, out Vector2 result)
    {
        bool onFloor = IsFloor(p);
        if (onFloor)
        {
            if (Clearance(p, margin) >= margin - 0.75f) { result = p; return true; }
            Vector2 q = p;
            ResolveCircle(ref q, margin, 8);
            if (IsBodyValid(q, margin) && SegmentClear(p, q)) { result = q; return true; }
        }
        int node = onFloor ? NearestVisibleNode(p, margin) : -1;
        if (node < 0) node = NearestNodeBrute(p, margin);
        if (node < 0)
        {
            int roomiest = RoomiestNode();
            result = roomiest >= 0 ? NodeCenter(roomiest) : Spawn;
            return false;
        }
        Vector2 c = NodeCenter(node);
        // pull the node centre toward p as far as it stays valid (keeps placement near the request)
        Vector2 toward = p - c;
        if (toward.LengthSquared() > 1f && onFloor && SegmentClear(c, p))
        {
            Vector2 moved = c;
            MoveCircle(ref moved, toward, margin);
            if (IsBodyValid(moved, margin)) { result = moved; return true; }
        }
        result = c;
        return true;
    }

    // ------------------------------------------------------------------ construction

    private void TraceLoops(MapDefinition def, float scale)
    {
        float g = 40f * scale;
        Vector2 o = -HalfExtents - new Vector2(g * 2f);
        int nx = (int)MathF.Ceiling(HalfExtents.X * 2f / g) + 5;
        int ny = (int)MathF.Ceiling(HalfExtents.Y * 2f / g) + 5;
        var v = new float[nx * ny];
        for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                float s = def.Sdf(o + new Vector2(x * g, y * g), scale);
                if (MathF.Abs(s) < 1e-3f) s = 1e-3f;
                if (x == 0 || y == 0 || x == nx - 1 || y == ny - 1) s = MathF.Max(s, 1f);
                v[y * nx + x] = s;
            }
        Vector2 Vert(int x, int y) => o + new Vector2(x * g, y * g);
        Vector2 Cross(int x0, int y0, int x1, int y1)
        {
            float a = v[y0 * nx + x0], b = v[y1 * nx + x1];
            return Vector2.Lerp(Vert(x0, y0), Vert(x1, y1), a / (a - b));
        }
        int HId(int x, int y) => (y * nx + x) * 2;
        int VId(int x, int y) => (y * nx + x) * 2 + 1;

        var segEdges = new List<(int e0, int e1)>();
        var edgePoint = new Dictionary<int, Vector2>();
        for (int y = 0; y < ny - 1; y++)
            for (int x = 0; x < nx - 1; x++)
            {
                bool i0 = v[y * nx + x] < 0, i1 = v[y * nx + x + 1] < 0, i2 = v[(y + 1) * nx + x + 1] < 0, i3 = v[(y + 1) * nx + x] < 0;
                int c = (i0 ? 1 : 0) | (i1 ? 2 : 0) | (i2 ? 4 : 0) | (i3 ? 8 : 0);
                if (c == 0 || c == 15) continue;
                int eT = HId(x, y), eR = VId(x + 1, y), eB = HId(x, y + 1), eL = VId(x, y);
                void Edge(int id)
                {
                    if (edgePoint.ContainsKey(id)) return;
                    edgePoint[id] = id == eT ? Cross(x, y, x + 1, y) : id == eR ? Cross(x + 1, y, x + 1, y + 1)
                        : id == eB ? Cross(x, y + 1, x + 1, y + 1) : Cross(x, y, x, y + 1);
                }
                void Seg(int a, int b) { Edge(a); Edge(b); segEdges.Add((a, b)); }
                bool centerIn = def.Sdf(o + new Vector2((x + 0.5f) * g, (y + 0.5f) * g), scale) < 0f;
                switch (c)
                {
                    case 1: Seg(eL, eT); break;
                    case 2: Seg(eT, eR); break;
                    case 3: Seg(eL, eR); break;
                    case 4: Seg(eR, eB); break;
                    case 5: if (centerIn) { Seg(eT, eR); Seg(eB, eL); } else { Seg(eL, eT); Seg(eR, eB); } break;
                    case 6: Seg(eT, eB); break;
                    case 7: Seg(eB, eL); break;
                    case 8: Seg(eB, eL); break;
                    case 9: Seg(eT, eB); break;
                    case 10: if (centerIn) { Seg(eL, eT); Seg(eR, eB); } else { Seg(eT, eR); Seg(eB, eL); } break;
                    case 11: Seg(eR, eB); break;
                    case 12: Seg(eR, eL); break;
                    case 13: Seg(eT, eR); break;
                    case 14: Seg(eL, eT); break;
                }
            }

        // join segments sharing a grid-edge crossing into closed loops
        var byEdge = new Dictionary<int, List<int>>();
        for (int i = 0; i < segEdges.Count; i++)
        {
            (byEdge.TryGetValue(segEdges[i].e0, out var l0) ? l0 : byEdge[segEdges[i].e0] = new List<int>()).Add(i);
            (byEdge.TryGetValue(segEdges[i].e1, out var l1) ? l1 : byEdge[segEdges[i].e1] = new List<int>()).Add(i);
        }
        var used = new bool[segEdges.Count];
        for (int start = 0; start < segEdges.Count; start++)
        {
            if (used[start]) continue;
            var pts = new List<Vector2>();
            int cur = start;
            int at = segEdges[start].e0;
            while (cur >= 0 && !used[cur])
            {
                used[cur] = true;
                pts.Add(edgePoint[at]);
                int next = segEdges[cur].e0 == at ? segEdges[cur].e1 : segEdges[cur].e0;
                at = next;
                cur = -1;
                foreach (int cand in byEdge[next])
                    if (!used[cand]) { cur = cand; break; }
            }
            if (pts.Count >= 3) Loops.Add(Simplify(pts, 0.6f));
        }
    }

    /// <summary>Douglas-Peucker on a closed loop: drops points within tol of the simplified walls.</summary>
    private static Vector2[] Simplify(List<Vector2> pts, float tol)
    {
        int n = pts.Count;
        int far = 0;
        float farD = -1f;
        for (int i = 1; i < n; i++)
        {
            float d = Vector2.DistanceSquared(pts[0], pts[i]);
            if (d > farD) { farD = d; far = i; }
        }
        var keep = new bool[n];
        keep[0] = keep[far] = true;
        void Rdp(int a, int b)
        {
            // indices along the loop from a to b (exclusive), wrapping
            int count = (b - a + n) % n;
            if (count < 2) return;
            Vector2 pa = pts[a], pb = pts[b];
            float best = -1f;
            int bi = -1;
            for (int k = 1; k < count; k++)
            {
                int i = (a + k) % n;
                float d = Vector2.Distance(pts[i], World.SegmentClosest(pa, pb, pts[i]));
                if (d > best) { best = d; bi = i; }
            }
            if (best > tol)
            {
                keep[bi] = true;
                Rdp(a, bi);
                Rdp(bi, b);
            }
        }
        Rdp(0, far);
        Rdp(far, 0);
        var res = new List<Vector2>();
        for (int i = 0; i < n; i++) if (keep[i]) res.Add(pts[i]);
        return res.ToArray();
    }

    private void BuildFloorQuads()
    {
        var ys = new List<float>(SegA.Length * 2);
        for (int s = 0; s < SegA.Length; s++) { ys.Add(SegA[s].Y); ys.Add(SegB[s].Y); }
        ys.Sort();
        var uniq = new List<float>();
        foreach (float y in ys) if (uniq.Count == 0 || y - uniq[^1] > 0.01f) uniq.Add(y);
        var xs = new List<(float mid, float x0, float x1)>();
        for (int i = 0; i + 1 < uniq.Count; i++)
        {
            float y0 = uniq[i], y1 = uniq[i + 1], ym = (y0 + y1) * 0.5f;
            xs.Clear();
            for (int s = 0; s < SegA.Length; s++)
            {
                Vector2 a = SegA[s], b = SegB[s];
                if ((a.Y > ym) == (b.Y > ym)) continue;
                float X(float y) => a.X + (y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                xs.Add((X(ym), X(y0), X(y1)));
            }
            xs.Sort((p, q) => p.mid.CompareTo(q.mid));
            for (int k = 0; k + 1 < xs.Count; k += 2)
                FloorQuads.Add((new Vector2(xs[k].x0, y0), new Vector2(xs[k + 1].x0, y0), new Vector2(xs[k + 1].x1, y1), new Vector2(xs[k].x1, y1)));
        }
    }

    private static float TrapArea(Vector2 a, Vector2 b, Vector2 c, Vector2 d) =>
        ((b.X - a.X) + (c.X - d.X)) * 0.5f * (d.Y - a.Y);

    private void BuildPylons()
    {
        const float spacing = 300f;
        int seg = 0;
        foreach (var loop in Loops)
        {
            float carry = spacing * 0.5f;
            for (int i = 0; i < loop.Length; i++, seg++)
            {
                Vector2 a = loop[i], b = loop[(i + 1) % loop.Length];
                float len = Vector2.Distance(a, b);
                float t = carry;
                while (t < len)
                {
                    PylonSpots.Add(Vector2.Lerp(a, b, t / len) - SegN[seg] * 26f);
                    t += spacing;
                }
                carry = t - len;
            }
        }
    }

    // ------------------------------------------------------------------ flow fields

    /// <summary>
    /// Shortest-path distances to one goal node over the shared navigation graph (one Dijkstra per
    /// goal change, shared by every enemy), with each node's next step toward the goal.
    /// </summary>
    public sealed class FlowField
    {
        public readonly MapGeometry Map;
        public readonly float[] Dist;
        public readonly int[] Next;
        public int Goal = -1;
        public int Rebuilds;
        public readonly float Radius;
        private readonly PriorityQueue<int, float> _queue = new();

        public FlowField(MapGeometry map, float radius = 48f)
        {
            Map = map;
            Radius = radius;
            Dist = new float[map.Walkable.Length];
            Next = new int[map.Walkable.Length];
        }

        public void Build(int goal)
        {
            Goal = goal;
            Rebuilds++;
            Array.Fill(Dist, float.MaxValue);
            Array.Fill(Next, -1);
            if (goal < 0 || Map.NodeClearance[goal] < Radius) return;
            _queue.Clear();
            Dist[goal] = 0f;
            _queue.Enqueue(goal, 0f);
            float cell = Map.NavCell, diag = cell * 1.41421356f;
            while (_queue.TryDequeue(out int i, out float d))
            {
                if (d > Dist[i]) continue;
                for (int k = 0; k < 8; k++)
                {
                    int j = Map.Links[i * 8 + k];
                    if (j < 0 || Map.LinkClearance[i * 8 + k] < Radius) continue;
                    float nd = d + (k < 4 ? cell : diag);
                    if (nd < Dist[j])
                    {
                        Dist[j] = nd;
                        Next[j] = i;
                        _queue.Enqueue(j, nd);
                    }
                }
            }
        }

        /// <summary>Path distance from a point to the goal (straight-line fallback off the graph).</summary>
        public float PathDistance(Vector2 p, Vector2 goalPos)
        {
            int n = Map.NodeAt(p);
            if (n < 0 || Dist[n] == float.MaxValue) return Vector2.Distance(p, goalPos) * 1.5f + 400f;
            return Dist[n] + Vector2.Distance(p, Map.NodeCenter(n));
        }

        /// <summary>Unit direction a body at p should travel to follow the shortest path to the goal.</summary>
        public Vector2 Direction(Vector2 p, Vector2 goalPos, float radius)
        {
            if (Map.IsBodyValid(goalPos, radius) && Map.CapsuleClear(p, goalPos, radius))
                return M.SafeNorm(goalPos - p);
            int n = Map.NodeAt(p);
            if (n < 0 || Dist[n] == float.MaxValue || !Map.CapsuleClear(p, Map.NodeCenter(n), radius))
            {
                float best = float.MaxValue;
                n = -1;
                for (int i = 0; i < Dist.Length; i++)
                {
                    if (Dist[i] == float.MaxValue || Map.NodeClearance[i] < radius) continue;
                    float d = Vector2.Distance(p, Map.NodeCenter(i)) + Dist[i];
                    if (d >= best || !Map.CapsuleClear(p, Map.NodeCenter(i), radius)) continue;
                    n = i; best = d;
                }
                if (n < 0) return Vector2.Zero;
                return M.SafeNorm(Map.NodeCenter(n) - p);
            }
            if (n == Goal || Next[n] < 0) return M.SafeNorm(Map.NodeCenter(n) - p);
            // look ahead along the path and aim at the furthest node still in clear sight
            int a = Next[n];
            Vector2 target = Map.NodeCenter(a);
            int b = Next[a];
            if (b >= 0)
            {
                Vector2 tb = Map.NodeCenter(b);
                int c = Next[b];
                if (c >= 0 && Map.CapsuleClear(p, Map.NodeCenter(c), radius)) target = Map.NodeCenter(c);
                else if (Map.CapsuleClear(p, tb, radius)) target = tb;
            }
            if (!Map.CapsuleClear(p, target, radius)) target = Map.NodeCenter(n);
            return M.SafeNorm(target - p);
        }

        /// <summary>The node sequence from p to the goal (for tests and debugging).</summary>
        public int PathLength(Vector2 p)
        {
            int n = Map.NodeAt(p);
            int steps = 0;
            while (n >= 0 && n != Goal && steps < 100000) { n = Next[n]; steps++; }
            return n == Goal ? steps : -1;
        }
    }
}
