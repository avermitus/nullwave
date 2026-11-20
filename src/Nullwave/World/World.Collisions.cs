using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;

namespace Nullwave.World;

public sealed partial class World
{
    /// <summary>Test helper: run collision resolution once.</summary>
    public void ResolveCollisionsForTest() => ResolveCollisions(new AudioState());

    private void ResolveCollisions(AudioState audio)
    {
        if (Phase != GamePhase.Playing) return;
        var p = Player;

        // Enemy fire is stopped by monoliths: the arena is cover as well as hazard.
        for (int i = EnemyBullets.Count - 1; i >= 0; i--)
        {
            var b = EnemyBullets[i];
            bool blocked = false;
            foreach (var o in Obstacles)
            {
                if (o.Dead) continue;
                float rr = o.Radius * 0.79f + b.Radius;
                if (M.DistSq(b.Pos, o.Pos) < rr * rr)
                {
                    blocked = true;
                    SpawnParticle(b.Pos, FxRng.InCircle(70), 0.25f, 2f, M.Hsv(b.Hue, 0.6f, 1f, 0.7f), ParticleKind.Spark, 3f);
                    break;
                }
            }
            if (blocked) ReturnBullet(EnemyBullets, i);
        }

        // Player shots shatter cover first, then pierce enemies.
        for (int i = PlayerBullets.Count - 1; i >= 0; i--)
        {
            var b = PlayerBullets[i];
            bool consumed = false;
            foreach (var o in Obstacles)
            {
                if (o.Dead) continue;
                float rr = o.Radius * 0.79f + b.Radius;
                if (M.DistSq(b.Pos, o.Pos) >= rr * rr) continue;
                DamageObstacle(o, b.Damage);
                SpawnParticle(b.Pos, FxRng.InCircle(90), 0.22f, 2.3f, M.Hsv(o.Hue, 0.55f, 1f, 0.7f), ParticleKind.Spark, 3f);
                if (b.Bounces > 0)
                {
                    b.Bounces--;
                    Vector2 n = M.SafeNorm(b.Pos - o.Pos);
                    if (n.LengthSquared() < 0.01f) n = new Vector2(0, 1);
                    b.Vel -= 2f * Vector2.Dot(b.Vel, n) * n;
                    b.Pos = o.Pos + n * (rr + 4f);
                    SuperchargePrismLance(b);
                }
                else
                {
                    consumed = true;
                }
                break;
            }
            if (consumed)
            {
                ReturnBullet(PlayerBullets, i);
                continue;
            }

            bool burst = false;
            foreach (var e in Enemies)
            {
                if (e.Dead || e.Spawning) continue;
                float rr = e.Radius * 0.88f + b.Radius;
                if (M.DistSq(b.Pos, e.Pos) >= rr * rr) continue;
                DamageEnemy(e, b.Damage, b.Pos);
                if (b.SynergyKind == 4) { burst = true; consumed = true; break; }   // bass orb bursts
                if (b.Pierce > 0)
                {
                    b.Pierce--;
                    b.Pos += M.SafeNorm(b.Vel) * (rr + 5f);
                }
                else
                {
                    consumed = true;
                }
                break;
            }
            if (burst) BassBurst(b.Pos, b.Damage);
            if (consumed) ReturnBullet(PlayerBullets, i);
        }

        // Beams.
        foreach (var l in Lasers)
        {
            if (!l.Active || l.HitPlayer || p.Invuln > 0 || p.IsDashing) continue;
            if (M.DistanceToSegment(p.Pos, l.Origin, l.End) < l.Width * 0.46f + p.Radius * 0.72f)
            {
                l.HitPlayer = true;
                HurtPlayer();
            }
        }

        // Bullet contact, dash absorption and grazes.
        bool voidPhantom = (p.ActiveSynergies & SynergyFlags.VoidPhantom) != 0;
        float absorbRadius = p.Radius + (voidPhantom ? 40f : 26f);
        for (int i = EnemyBullets.Count - 1; i >= 0; i--)
        {
            var b = EnemyBullets[i];
            float hitRadius = p.Radius + b.Radius;
            float distSq = M.DistSq(b.Pos, p.Pos);
            if (p.IsDashing && distSq < (absorbRadius + b.Radius) * (absorbRadius + b.Radius))
            {
                ReturnBullet(EnemyBullets, i);
                p.Resonance = MathF.Min(p.ResonanceMax, p.Resonance + 4.5f * p.TotalResonanceGain * Mods.GrazeGain * OverdriveResonanceScale);
                Grazes++;
                FlowGain(0.03f);
                AddCombo(1);
                AddScore(15);
                SpawnParticle(b.Pos, FxRng.InCircle(120), 0.34f, 2.6f, new Vector4(0.55f, 1f, 1f, 0.9f), ParticleKind.Spark, 2.4f);
                if (FxRng.Chance(0.12f)) Sfx?.Trigger(SfxType.Graze, 0.18f);
                continue;
            }

            // riding a melody rail, bullets tuned to it pass straight through
            if (b.Tuned && p.RailRiding && distSq < hitRadius * hitRadius) continue;
            if (distSq < hitRadius * hitRadius)
            {
                if (p.Invuln <= 0f && StartGrace <= 0f)
                {
                    ReturnBullet(EnemyBullets, i);
                    HurtPlayer();
                }
                continue;
            }

            float grazeRadius = hitRadius + (voidPhantom ? 34f : 27f);
            if (!b.Grazed && distSq < grazeRadius * grazeRadius)
            {
                b.Grazed = true;
                Grazes++;
                FlowGain(0.03f);
                p.Resonance = MathF.Min(p.ResonanceMax, p.Resonance + 1.25f * p.TotalResonanceGain * Mods.GrazeGain * OverdriveResonanceScale);
                AddCombo(1);
                AddScore(18);
                SpawnParticle(b.Pos, -M.SafeNorm(b.Vel) * 30f + FxRng.InCircle(40), 0.24f, 1.8f, M.Hsv(b.Hue, 0.5f, 1f, 0.65f), ParticleKind.Spark, 4f);
                if (FxRng.Chance(0.18f)) Sfx?.Trigger(SfxType.Graze, 0.12f, M.Clamp((b.Pos.X - p.Pos.X) / 300f, -1f, 1f));
            }
        }

        // Contact damage.
        foreach (var e in Enemies)
        {
            if (e.Dead || e.Spawning) continue;
            float rr = e.Radius * 0.86f + p.Radius * 0.9f;
            Vector2 delta = p.Pos - e.Pos;
            float d = delta.Length();
            if (d >= rr) continue;
            if (!p.IsDashing)
            {
                Vector2 n = d > 0.01f ? delta / d : new Vector2(1, 0);
                p.Pos = e.Pos + n * rr;
                p.Vel += n * 220f;
            }
            if (p.Invuln <= 0 && StartGrace <= 0 && !p.IsDashing)
                HurtPlayer();
        }
    }
}
