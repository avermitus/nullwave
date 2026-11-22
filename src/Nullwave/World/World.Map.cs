using System.Numerics;
using Nullwave.Core;

namespace Nullwave.World;

/// <summary>
/// Experimental maps: how the world uses <see cref="MapGeometry"/>. With the open arena
/// (<see cref="Map"/> null) none of this runs and every path keeps its original behaviour.
/// </summary>
public sealed partial class World
{
    /// <summary>The experimental map's geometry, or null for the open arena.</summary>
    public readonly MapGeometry? Map;
    public MapKind MapKind => Map?.Kind ?? MapKind.Arena;
    public bool HasMap => Map != null;

    /// <summary>Shared shortest-path field toward the player (rebuilt only when the player's node changes).</summary>
    public MapGeometry.FlowField? PlayerField;
    /// <summary>Shortest-path field toward the boss's current target node.</summary>
    private MapGeometry.FlowField? _bossField;
    /// <summary>Clearance a boss target needs: the boss body plus room to dodge around it.</summary>
    public const float BossRoomMargin = 96f + 150f;
    private readonly List<float> _crossings = new();

    private void InitMap()
    {
        if (Map == null) return;
        ArenaW = Map.HalfExtents.X * 2f + 200f;
        ArenaH = Map.HalfExtents.Y * 2f + 200f;
        PlayerField = new MapGeometry.FlowField(Map);
        _bossField = new MapGeometry.FlowField(Map, 96f);
    }

    /// <summary>Keeps the shared flow field pointed at the player's current navigation node.</summary>
    private void UpdateNav()
    {
        if (Map == null || PlayerField == null) return;
        int goal = Map.NearestWalkableNode(Player.Pos, PlayerField.Radius);
        if (goal != PlayerField.Goal) PlayerField.Build(goal);
    }

    /// <summary>
    /// Moves a body by <paramref name="delta"/>. In a map it is swept against the walls (no
    /// tunnelling) and its velocity slides along any wall it touched; in the arena it just moves.
    /// </summary>
    internal void MoveBody(ref Vector2 pos, ref Vector2 vel, Vector2 delta, float radius)
    {
        if (Map == null) { pos += delta; return; }
        Vector2 n = Map.MoveCircle(ref pos, delta, radius);
        if (n.LengthSquared() > 1e-6f)
        {
            n = Vector2.Normalize(n);
            float into = Vector2.Dot(vel, n);
            if (into < 0f) vel -= into * n;
        }
    }

    /// <summary>Applies a correction (a shove, separation) to a body without letting it cross a wall.</summary>
    internal Vector2 ShiftBody(Vector2 from, Vector2 to, float radius)
    {
        if (Map == null) return to;
        Vector2 p = from;
        Map.MoveCircle(ref p, to - from, radius);
        return p;
    }

    /// <summary>Last-resort rescue: a body that is somehow off the floor goes back to the nearest valid spot.</summary>
    private void RescueBody(ref Vector2 pos, float radius)
    {
        if (Map == null || Map.IsFloor(pos)) return;
        pos = Map.ProjectToFloor(pos, radius + 4f);
        MapRescues++;
    }

    /// <summary>Count of bodies the rescue had to move (tests expect it to stay at zero).</summary>
    public int MapRescues;

    /// <summary>The farthest floor point the player can see along a direction, short of the wall.</summary>
    internal Vector2 WallAnchor(Vector2 from, Vector2 dir, float maxDist, float inset = 30f)
    {
        if (Map == null) return ClampToArena(from + dir * maxDist, inset);
        Vector2 to = from + dir * maxDist;
        if (Map.SegmentHit(from, to, out float t, out _, out _))
            return from + dir * MathF.Max(0f, maxDist * t - inset);
        return Map.ProjectToFloor(to, inset);
    }

    /// <summary>True if a body of this radius has a clear straight run from a to b.</summary>
    public bool HasClearPath(Vector2 a, Vector2 b, float radius) => Map == null || Map.CapsuleClear(a, b, radius);

    /// <summary>True if nothing solid stands between two points (sight and shots).</summary>
    public bool HasLineOfSight(Vector2 a, Vector2 b) => Map == null || Map.SegmentClear(a, b);

    /// <summary>
    /// Where an enemy arrives in a map: a walkable node 800-1700 path units from the player (so it
    /// is reachable and never spawns on top of you), preferring ones out of direct sight.
    /// </summary>
    private Vector2 MapSpawnPoint(float radius)
    {
        var map = Map!;
        var field = PlayerField!;
        UpdateNav();
        float need = MathF.Max(radius + 24f, map.NavClearance);
        int pick = -1;
        for (int attempt = 0; attempt < 40 && pick < 0; attempt++)
        {
            int i = Rng.Range(0, map.Walkable.Length);
            if (!map.Walkable[i] || map.NodeClearance[i] < need) continue;
            float d = field.Dist[i];
            if (d < 800f * map.Scale || d > 1700f * map.Scale) continue;
            if (M.Dist(map.NodeCenter(i), Player.Pos) < 560f) continue;
            pick = i;
        }
        if (pick < 0)
        {
            // fallback: the reachable node whose path distance is closest to the ideal range
            float best = float.MaxValue;
            for (int i = 0; i < map.Walkable.Length; i++)
            {
                if (!map.Walkable[i] || map.NodeClearance[i] < need || field.Dist[i] == float.MaxValue) continue;
                if (M.Dist(map.NodeCenter(i), Player.Pos) < 560f) continue;
                float score = MathF.Abs(field.Dist[i] - 1100f * map.Scale);
                if (score < best) { best = score; pick = i; }
            }
        }
        if (pick < 0) pick = map.NearestNodeBrute(map.Spawn, need);
        Vector2 c = map.NodeCenter(pick);
        float jitter = MathF.Max(0f, MathF.Min(map.NavCell * 0.35f, map.NodeClearance[pick] - radius - 8f));
        Vector2 p = c + Rng.InCircle(jitter);
        return map.IsBodyValid(p, radius + 4f) ? p : c;
    }

    /// <summary>
    /// Where a boss should hover near <paramref name="desired"/>: the spot itself when it is open
    /// and in sight of the player, else the open node (room for the boss to fight in) with the best
    /// mix of short path from the player and closeness to the desired spot.
    /// </summary>
    internal Vector2 BossTarget(Vector2 desired, float margin = BossRoomMargin)
    {
        var map = Map!;
        if (map.IsFloor(desired) && map.Clearance(desired, margin) >= margin - 1f && map.SegmentClear(Player.Pos, desired))
            return desired;
        var field = PlayerField!;
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < map.Walkable.Length; i++)
        {
            if (!map.Walkable[i] || map.NodeClearance[i] < margin || field.Dist[i] == float.MaxValue) continue;
            Vector2 c = map.NodeCenter(i);
            float score = field.Dist[i] + 0.35f * Vector2.Distance(c, desired);
            if (field.Dist[i] < 260f) score += 600f;   // not on top of the player
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best < 0) best = map.NearestNodeBrute(desired, margin);
        return best < 0 ? Player.Pos : map.NodeCenter(best);
    }

    /// <summary>
    /// Moves a boss toward its target the way it always has (an eased lerp), but through the map:
    /// straight when the way is clear, otherwise along the path field, always swept against walls.
    /// </summary>
    internal void MoveBossInMap(Enemy e, Vector2 target, float rate, float dt)
    {
        var map = Map!;
        Vector2 step = (target - e.Pos) * (1f - MathF.Exp(-dt * rate));
        if (!map.CapsuleClear(e.Pos, target, e.Radius))
        {
            int goal = map.NearestNodeBrute(target, e.Radius);
            if (goal != _bossField!.Goal) _bossField.Build(goal);
            float speed = MathF.Max(260f, step.Length() / MathF.Max(dt, 1e-4f));
            step = _bossField.Direction(e.Pos, target, e.Radius) * MathF.Min(speed, 520f) * dt;
        }
        Vector2 before = e.Pos;
        map.MoveCircle(ref e.Pos, step, e.Radius);
        e.Vel = (e.Pos - before) / MathF.Max(dt, 1e-4f);
    }

    /// <summary>Boss arrival point: open room near the player by path, not straight through a wall.</summary>
    private Vector2 BossSpawnPoint(Vector2 desired)
    {
        UpdateNav();
        Vector2 p = BossTarget(desired);
        return Map!.ProjectToFloor(p, 96f + 20f);
    }

    /// <summary>
    /// Laser origins that start inside a wall or the void (the song's light show fires from far
    /// away) are moved to the first wall they would come through on the way to their aim point,
    /// so every beam starts on the floor and is then stopped by the next wall it meets.
    /// </summary>
    private Vector2 AnchorLaserOrigin(Vector2 origin, Vector2 target)
    {
        var map = Map!;
        if (map.IsFloor(origin)) return origin;
        // anchor from the aim point, or from the player when the aim point is itself off the floor
        Vector2 reference = map.IsFloor(target) ? target : Player.Pos;
        if (!map.IsFloor(reference)) return origin;   // nothing to anchor to: the beam stays blocked
        if (map.SegmentHit(reference, origin, out float t, out _, out _))
        {
            Vector2 dir = M.SafeNorm(reference - origin);
            return reference + (origin - reference) * t + dir * 2f;
        }
        return origin;
    }

    /// <summary>Recomputes the part of a beam that is not behind a wall.</summary>
    private void ClipLaser(LaserBeam l)
    {
        if (Map == null) { l.ClipStart = 0f; l.ClipEnd = l.Length; return; }
        Vector2 dir = l.Direction;
        Vector2 end = l.Origin + dir * l.Length;
        if (!Map.IsFloor(l.Origin)) { l.ClipStart = 0f; l.ClipEnd = 0f; return; }
        if (Map.SegmentHit(l.Origin, end, out float t, out _, out _))
        {
            l.ClipStart = 0f;
            l.ClipEnd = l.Length * t;
        }
        else
        {
            l.ClipStart = 0f;
            l.ClipEnd = l.Length;
        }
    }

    /// <summary>
    /// Swept wall test for a projectile that just moved from <paramref name="from"/> to its
    /// position: bounces it (ricochet, decrementing once) or reports that the wall consumed it.
    /// </summary>
    private bool BulletHitsWall(Bullet b, Vector2 from, bool canBounce)
    {
        var map = Map!;
        if (!map.SegmentHit(from, b.Pos, out _, out Vector2 hit, out Vector2 n)) return false;
        if (canBounce && b.Bounces > 0)
        {
            b.Bounces--;
            float into = Vector2.Dot(b.Vel, n);
            if (into < 0f) b.Vel -= 2f * into * n;
            b.Pos = hit + n * (b.Radius + 2f);
            if (!map.IsFloor(b.Pos) || !map.SegmentClear(hit + n * 0.5f, b.Pos)) b.Pos = hit + n * 0.75f;
            b.PrevPos = b.Pos;
            SuperchargePrismLance(b);
            return false;
        }
        b.Pos = hit + n * 0.5f;
        return true;
    }

    // ------------------------------------------------------------------ test hooks

    internal MapGeometry.FlowField? BossFieldForTest => _bossField;
    internal void UpdateNavForTest() => UpdateNav();
    internal Vector2 MapSpawnPointForTest(float radius) => MapSpawnPoint(radius);
    internal void SpawnEnemyForTest(EnemyRole role, Vector2 pos) => SpawnEnemyAt(role, pos, false);
    internal void SpawnNextForTest() => SpawnNext();
    internal void UpdateBulletsForTest(float dt) => UpdateBullets(dt);
    internal void UpdateLasersForTest(float dt) => UpdateLasers(dt);
    internal void UpdatePickupsForTest(float dt) => UpdatePickups(dt);
    internal void GrowRailForTest(MelodyRail r, float dt, Audio.AudioState audio) => GrowRail(r, dt, audio);
    internal MelodyRail StartRailForTest(Audio.AudioState audio) => StartRail(audio);
}
