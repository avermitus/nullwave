using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.World;
using GameWorld = Nullwave.World.World;

namespace Nullwave.Tools;

public static partial class SelfTest
{
    public readonly record struct PackShape(float NearestNeighbour, float Spread, float Clumped, float MeanDistance, float Closest);

    /// <summary>
    /// The owner's report: "they still bunch up when I run away". Ten mixed enemies chase a ship that flees
    /// in a straight line at top speed; measure how the pack looks after it has been chasing for a while.
    /// NearestNeighbour: mean distance to the closest other enemy. Spread: how wide the pack is across the
    /// ship's path (std. dev.). Clumped: share of enemies with another within 90 units.
    /// </summary>
    public static PackShape MeasureFleeingPack(ulong seed, float seconds = 4f, bool zigzag = false)
    {
        const float dt = 1f / 120f;
        var w = new GameWorld(new RunConfig { Seed = seed });
        w.StartGrace = 0f;
        w.Player.InvincibleDemo = true;
        w.BeginWave(3);
        w.ClearSpawnQueueForCapture();
        var roles = new[] { EnemyRole.Drifter, EnemyRole.Drifter, EnemyRole.Swarm, EnemyRole.Swarm, EnemyRole.Orbiter,
                            EnemyRole.Dasher, EnemyRole.Weaver, EnemyRole.Splitter, EnemyRole.Drifter, EnemyRole.Swarm };
        for (int i = 0; i < roles.Length; i++)
            w.SpawnForCapture(roles[i], w.Player.Pos + M.FromAngle(i / (float)roles.Length * M.Tau, 380f + (i % 3) * 60f));
        var a = new AudioState { Bpm = 128f };
        int steps = (int)(seconds / dt);
        var path = new Vector2(1f, 0.15f);
        for (int s = 0; s < steps; s++)
        {
            float t = s * dt;
            Vector2 move = zigzag ? M.SafeNorm(new Vector2(1f, MathF.Sin(t * 2.2f) * 0.9f)) : M.SafeNorm(path);
            a.Time += dt;
            a.BeatPosition += dt * 128.0 / 60.0;
            w.Step(dt, a, new InputFrame(move, move, false, false, false, false));
        }
        var alive = w.Enemies.Where(e => !e.Dead && !e.Spawning && !e.Boss).ToList();
        if (alive.Count < 2) return new PackShape(0, 0, 0, 0, 0);
        Vector2 heading = M.SafeNorm(w.Player.Vel.LengthSquared() > 1f ? w.Player.Vel : path);
        Vector2 across = M.Perp(heading);
        float nn = 0f, clumped = 0f;
        foreach (var e in alive)
        {
            float best = float.MaxValue;
            foreach (var o in alive) if (o != e) best = MathF.Min(best, Vector2.Distance(e.Pos, o.Pos));
            nn += best;
            if (best < 90f) clumped++;
        }
        var lateral = alive.Select(e => Vector2.Dot(e.Pos - w.Player.Pos, across)).ToList();
        float mean = lateral.Average();
        float spread = MathF.Sqrt(lateral.Select(x => (x - mean) * (x - mean)).Average());
        float meanDist = alive.Average(e => Vector2.Distance(e.Pos, w.Player.Pos));
        float closest = alive.Min(e => Vector2.Distance(e.Pos, w.Player.Pos));
        return new PackShape(nn / alive.Count, spread, clumped / alive.Count, meanDist, closest);
    }

    private static void TestPackSpreadsWhenFleeing()
    {
        var straight = MeasureFleeingPack(0xFACEUL);
        var zig = MeasureFleeingPack(0xFACFUL, 4f, true);
        Console.WriteLine($"  pack (straight): nn={straight.NearestNeighbour:0} spread={straight.Spread:0} clumped={straight.Clumped * 100:0}% meanDist={straight.MeanDistance:0} closest={straight.Closest:0}");
        // before pack steering: 70 % clumped in both, nearest neighbour 149 / 123
        Check(straight.Clumped <= 0.2f && straight.NearestNeighbour >= 190f, "a fleeing ship is chased by a spread pack, not a clump");
        Check(zig.Clumped <= 0.5f && zig.Spread >= 280f, "zigzagging away still leaves the pack fanned out");
        Check(straight.MeanDistance <= 900f && zig.MeanDistance <= 820f, "spreading out does not let the pack fall far behind");
        Console.WriteLine($"  pack (zigzag):   nn={zig.NearestNeighbour:0} spread={zig.Spread:0} clumped={zig.Clumped * 100:0}% meanDist={zig.MeanDistance:0} closest={zig.Closest:0}");
    }
}
