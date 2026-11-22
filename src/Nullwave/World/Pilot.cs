using System.Numerics;
using Nullwave.Core;

namespace Nullwave.World;

public static class Pilot
{
    public static InputFrame Think(World w)
    {
        var p = w.Player;
        Enemy? target = null;
        float bestDist = float.MaxValue;
        foreach (var e in w.Enemies)
        {
            if (e.Dead || e.Spawning) continue;
            float d = M.DistSq(p.Pos, e.Pos);
            if (d < bestDist)
            {
                bestDist = d;
                target = e;
            }
        }
        Vector2 aim = p.AimDir;
        if (target != null)
        {
            // Lead the shot: orbiters strafe fast enough that aiming at their
            // current position makes every volley trail behind them.
            float travel = M.Dist(p.Pos, target.Pos) / MathF.Max(1f, p.BulletSpeed);
            Vector2 predicted = target.Pos + target.Vel * travel * 0.92f;
            aim = M.SafeNorm(predicted - p.Pos);
        }
        if (aim.LengthSquared() < 0.01f) aim = new Vector2(0, -1);

        // maps: head down the shared path field toward the nearest enemy when it is round a corner
        Vector2 navDir = Vector2.Zero;
        if (w.Map != null && target != null && !w.Map.SegmentClear(p.Pos, target.Pos))
        {
            var field = new Vector2(0f);
            int node = w.Map.NodeAt(target.Pos);
            if (w.PlayerField != null && node >= 0 && w.PlayerField.Dist[node] < float.MaxValue)
            {
                // walk back from the enemy toward the player along the field and aim at the first visible step
                int n = node, guard = 0;
                Vector2 aimAt = target.Pos;
                while (n >= 0 && guard++ < 400)
                {
                    Vector2 c = w.Map.NodeCenter(n);
                    if (w.Map.CapsuleClear(p.Pos, c, p.Radius + 6f)) { aimAt = c; break; }
                    n = w.PlayerField.Next[n];
                }
                field = M.SafeNorm(aimAt - p.Pos);
            }
            navDir = field;
        }
        Vector2 bestMove = Vector2.Zero;
        float bestScore = float.MinValue;
        Vector2[] candidates = new Vector2[17];
        candidates[0] = Vector2.Zero;
        for (int i = 0; i < 16; i++)
            candidates[i + 1] = M.FromAngle(i / 16f * M.Tau);

        foreach (var dir in candidates)
        {
            Vector2 test = p.Pos + dir * (p.IsDashing ? 80f : 70f);
            float score = 0f;
            float wallMargin = 80f;
            if (w.Map != null)
            {
                // maps: stay off the walls, and when the target is out of sight take the path toward it
                if (!w.Map.IsFloor(test)) score -= 900f;
                else score -= MathF.Max(0f, wallMargin - w.Map.Clearance(test, wallMargin)) * 6f;
                if (navDir.LengthSquared() > 0.1f) score += Vector2.Dot(dir, navDir) * 14f;
            }
            else
            {
                score -= MathF.Max(0, MathF.Abs(test.X) - (w.ArenaW * 0.5f - wallMargin)) * 7f;
                score -= MathF.Max(0, MathF.Abs(test.Y) - (w.ArenaH * 0.5f - wallMargin)) * 7f;
                score -= test.Length() * 0.012f;
            }

            foreach (var o in w.Obstacles)
            {
                if (o.Dead) continue;
                float d = M.Dist(test, o.Pos);
                float danger = o.Radius * 0.82f + 40f;
                if (d < danger) score -= (danger - d) * 4f;
            }

            foreach (var b in w.EnemyBullets)
            {
                float d = M.DistSq(test, b.Pos);
                if (d < 210f * 210f)
                {
                    float danger = 1f - MathF.Sqrt(d) / 210f;
                    Vector2 rel = b.Vel;
                    if (Vector2.Dot(test - b.Pos, rel) < 0) danger *= 0.5f;
                    score -= danger * 130f;
                }
            }

            foreach (var e in w.Enemies)
            {
                if (e.Dead || e.Spawning) continue;
                float d = M.Dist(test, e.Pos);
                float danger = e.Radius + 55f;
                if (d < danger) score -= (danger - d) * (e.Role == EnemyRole.Dasher ? 4f : 2.2f);
            }

            if (target != null)
            {
                Vector2 toTarget = M.SafeNorm(target.Pos - test);
                float desiredRange = target.Boss ? 420f : 300f;
                float d = M.Dist(test, target.Pos);
                score -= MathF.Abs(d - desiredRange) * 0.035f;
                score += Vector2.Dot(dir, M.Perp(toTarget)) * 10f;
            }
            score += Vector2.Dot(dir, aim) * 4f;
            score += M.Hash01((int)(test.X * 0.1f), (int)(test.Y * 0.1f), (int)w.Seed) * 0.5f;

            if (score > bestScore)
            {
                bestScore = score;
                bestMove = dir;
            }
        }

        bool dash = false;
        bool burst = p.Resonance >= p.ResonanceMax;
        if (!p.IsDashing && p.DashCooldown <= 0.01f)
        {
            float closestSq = float.MaxValue;
            foreach (var b in w.EnemyBullets)
            {
                float dSq = M.DistSq(p.Pos, b.Pos);
                if (dSq < closestSq) closestSq = dSq;
            }
            float bossDist = w.BossEnemy != null ? M.Dist(p.Pos, w.BossEnemy.Pos) : float.MaxValue;
            bool emergency = closestSq < 44f * 44f || bossDist < 120f;
            bool mediumThreat = closestSq < 95f * 95f;
            bool flowSync = (w.EnemyBullets.Count > 0 || w.Enemies.Count > 0) && w.Flow < 0.92f;
            if (emergency) dash = true;
            else if ((mediumThreat || flowSync) && w.IsInPhaseSyncWindow(out _)) dash = true;
        }
        return new InputFrame(bestMove, aim, target != null || w.EnemyBullets.Count > 0, dash, false, burst);
    }

    public static int ChooseAttunement(World w)
    {
        if (w.Offers.Count == 0) return -1;
        return 0;
    }
}
