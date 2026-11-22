using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;

namespace Nullwave.World;

public sealed partial class World
{
    private void UpdatePlayer(float dt, InputFrame input, AudioState audio)
    {
        var p = Player;
        p.Time += dt;
        p.HitFlash = MathF.Max(0, p.HitFlash - dt * 4.5f);
        p.PhaseSyncFlash = MathF.Max(0f, p.PhaseSyncFlash - dt * 2.6f);
        p.VoidPhantomFlash = MathF.Max(0f, p.VoidPhantomFlash - dt * 3f);
        p.SyncOverdriveTimer = MathF.Max(0f, p.SyncOverdriveTimer - dt);
        p.Invuln = MathF.Max(0, p.Invuln - dt);
        p.DashCooldown = MathF.Max(0, p.DashCooldown - dt);
        p.TrailTimer -= dt;

        if (input.Aim.LengthSquared() > 0.001f)
            p.AimDir = Vector2.Normalize(input.Aim);
        p.Focus = M.ExpSmooth(p.Focus, input.Focus ? 1f : 0f, 14f, dt);

        if (input.Burst) Burst();

        if (input.Dash && p.DashCooldown <= 0f && !p.IsDashing)
        {
            Vector2 dir = input.Move.LengthSquared() > 0.01f ? input.Move : p.AimDir;
            p.DashDir = M.SafeNorm(dir);
            if (p.DashDir.LengthSquared() < 0.01f) p.DashDir = p.AimDir;
            p.IsDashing = true;
            p.DashTime = 0.15f;
            p.DashCooldown = p.DashCooldownMax;
            p.Invuln = MathF.Max(p.Invuln, 0.24f);
            Cam.AddShake(3.5f);
            Sfx?.Trigger(SfxType.Dash, 0.55f);
            for (int i = 0; i < 18; i++)
                SpawnParticle(p.Pos, -p.DashDir * FxRng.Range(80, 360) + FxRng.InCircle(100), FxRng.Range(0.2f, 0.45f), 2.5f, M.Hsv(0.50f, 0.65f, 1f, 0.8f), ParticleKind.Spark, 3f);
            if ((p.ActiveSynergies & SynergyFlags.VoidPhantom) != 0)
            {
                p.VoidPhantomFlash = 1f;
                AddShockwave(p.Pos, 180f, 0.38f, 4f, new Vector4(0.75f, 0.45f, 1f, 1f));
                const float dmgR2 = 180f * 180f;
                for (int i = Enemies.Count - 1; i >= 0; i--)
                {
                    var e = Enemies[i];
                    if (e.Dead || e.Spawning) continue;
                    if (M.DistSq(e.Pos, p.Pos) <= dmgR2)
                        DamageEnemy(e, 35f, p.Pos);
                }
                const float cancelR2 = 135f * 135f;
                float resPerBullet = 2f * p.TotalResonanceGain * Mods.ResonanceGain * OverdriveResonanceScale;
                for (int i = EnemyBullets.Count - 1; i >= 0; i--)
                {
                    var eb = EnemyBullets[i];
                    if (M.DistSq(eb.Pos, p.Pos) <= cancelR2)
                    {
                        SpawnParticle(eb.Pos, FxRng.InCircle(110), 0.28f, 2.4f, new Vector4(0.78f, 0.55f, 1f, 0.9f), ParticleKind.Spark, 2.8f);
                        ReturnBullet(EnemyBullets, i);
                        p.Resonance = MathF.Min(p.ResonanceMax, p.Resonance + resPerBullet);
                    }
                }
            }
            if (IsInPhaseSyncWindow(out float syncErrMs))
            {
                PhaseSyncs++;
                p.PhaseSyncs++;
                p.PhaseSyncStreak++;
                p.PhaseSyncFlash = 1f;
                p.LastPhaseSyncErrorMs = syncErrMs;
                FlowGain(0.05f);
                bool triggeredSyncOd = p.PhaseSyncStreak > 0 && p.PhaseSyncStreak % 3 == 0;
                if (triggeredSyncOd)
                {
                    p.SyncOverdriveTimer = 5.0f;
                    AddShockwave(p.Pos, 280f, 0.55f, 6.5f, new Vector4(0.45f, 1f, 0.95f, 1f), heavy: true);
                    AddShockwave(p.Pos, 175f, 0.42f, 3.5f, new Vector4(1f, 0.88f, 0.40f, 1f));
                    EmitEvent("SYNC OVERDRIVE // 3x PHASE", 1.8f);
                    Sfx?.Trigger(SfxType.Unlock, 0.55f);
                }
                float resGain = 5f * p.TotalResonanceGain * Mods.ResonanceGain * OverdriveResonanceScale;
                p.Resonance = MathF.Min(p.ResonanceMax, p.Resonance + resGain);
                p.DashCooldown *= 0.82f;
                p.Invuln = MathF.Max(p.Invuln, 0.30f);
                AddScore(120, p.Pos, triggeredSyncOd ? "SYNC OVERDRIVE" : "PHASE SYNC", new Vector4(0.45f, 1f, 0.95f, 1f));
                AddShockwave(p.Pos, 195f, 0.42f, 3.6f, new Vector4(0.45f, 1f, 0.95f, 1f));
                for (int i = 0; i < 10; i++)
                {
                    var sc = i % 2 == 0 ? new Vector4(0.45f, 1f, 0.95f, 0.9f) : new Vector4(1f, 0.85f, 0.4f, 0.9f);
                    SpawnParticle(p.Pos, -p.DashDir * FxRng.Range(110, 420) + FxRng.InCircle(120), FxRng.Range(0.25f, 0.5f), 2.8f, sc, ParticleKind.Spark, 3f);
                }
                Sfx?.Trigger(SfxType.Pickup, 0.28f);
            }
            else
            {
                p.PhaseSyncStreak = 0;
            }
        }

        Vector2 move = M.ClampLength(input.Move, 1f);
        if (p.IsDashing)
        {
            p.Vel = p.DashDir * 1700f;
            p.DashTime -= dt;
            p.Invuln = MathF.Max(p.Invuln, 0.12f);
            if (p.DashTime <= 0)
            {
                p.IsDashing = false;
                p.Vel *= 0.42f;
            }
            if (p.TrailTimer <= 0)
            {
                p.TrailTimer = 0.018f;
                SpawnParticle(p.Pos + FxRng.InCircle(5), -p.Vel * 0.08f, 0.33f, 3.2f, M.Hsv(0.51f, 0.55f, 1f, 0.55f), ParticleKind.Dot, 1.5f);
            }
        }
        else
        {
            // racer handling: high top speed, quick to accelerate, glides when you let go
            // melody rails and gravity slingshots lift the top speed
            float speed = Player.TopSpeed * (1f - p.Focus * 0.6f) * (1f + p.RailBoost * 0.45f + p.SlingBoost * 0.4f);
            Vector2 targetVel = move * speed;
            p.Vel = M.Lerp(p.Vel, targetVel, 1f - MathF.Exp(-dt * (move.LengthSquared() > 0.01f ? 6.5f : 2.6f)));
        }

        p.PrevPos = p.Pos;
        p.Pos += p.Vel * dt;
        float margin = 28f + p.Radius;
        p.Pos = new Vector2(
            M.Clamp(p.Pos.X, -ArenaW * 0.5f + margin, ArenaW * 0.5f - margin),
            M.Clamp(p.Pos.Y, -ArenaH * 0.5f + margin, ArenaH * 0.5f - margin));

        foreach (var o in Obstacles)
        {
            if (o.Dead) continue;
            Vector2 delta = p.Pos - o.Pos;
            float minDist = o.Radius * 0.78f + p.Radius;
            float d = delta.Length();
            if (d < minDist && d > 0.001f)
                p.Pos = o.Pos + delta / d * minDist;
        }

        if (p.TrailTimer <= 0 && p.Vel.LengthSquared() > 900f && !p.IsDashing)
        {
            p.TrailTimer = 0.045f;
            SpawnParticle(p.Pos - M.SafeNorm(p.Vel) * 9f + FxRng.InCircle(3), -p.Vel * 0.05f, 0.38f, 2.8f, M.Hsv(0.51f, 0.65f, 0.9f, 0.42f), ParticleKind.Dot, 1.8f);
        }

        p.FireTimer -= dt;
        FireWeapon(input, audio, dt);
        UpdateFlow(dt);
    }

    /// <summary>One volley. Instrument ships pass a style (bigger, slower, fanned, ...); attunements always apply.</summary>
    private void Shoot(ShotStyle style)
    {
        var p = Player;
        p.ShotCounter++;
        p.FireTimer = p.FireInterval * OverdriveFireScale / MathF.Max(0.4f, Mods.PlayerFireRate);
        float dmgMult = OverdriveDamageScale * style.DamageScale;
        float baseAngle = M.AngleOf(p.AimDir) + style.AngleOffset;
        int fired = 0;

        void FireAngle(float angle, float sideOffset, float damageScale = 1f)
        {
            Vector2 dir = M.FromAngle(angle);
            Vector2 side = M.Perp(dir) * sideOffset;
            Vector2 pos = p.Pos + dir * 18f + side;
            float spread = FxRng.Signed() * 0.012f;
            SpawnPlayerBullet(pos, M.FromAngle(angle + spread, p.BulletSpeed * style.SpeedScale), p.BulletRadius * style.RadiusScale,
                p.Damage * damageScale * dmgMult, p.BulletLife * (style.Kind == 4 ? 1.1f : 1f), p.PierceLevel + style.ExtraPierce,
                p.HomingLevel * 2.2f, p.RicochetLevel, style.Kind, style.Hue);
            fired++;
        }

        if (style.Kind == 4) FireAngle(baseAngle, 0f);   // bass: one heavy orb
        else
        {
            FireAngle(baseAngle, -4.4f);
            FireAngle(baseAngle, 4.4f);
        }
        for (int i = 0; i < p.SplitLevel; i++)
        {
            float spread = 0.09f + i * 0.055f;
            FireAngle(baseAngle - spread, -3f, 0.88f);
            FireAngle(baseAngle + spread, 3f, 0.88f);
        }
        for (int i = 0; i < p.FanLevel; i++)
        {
            float spread = 0.22f + i * 0.075f;
            FireAngle(baseAngle - spread, -2f, 0.72f);
            FireAngle(baseAngle + spread, 2f, 0.72f);
        }
        for (int i = 0; i < p.RearLevel; i++)
        {
            float spread = 0.10f + i * 0.06f;
            FireAngle(baseAngle + MathF.PI - spread, 0f, 0.55f);
            FireAngle(baseAngle + MathF.PI + spread, 0f, 0.55f);
        }

        // Synergy: SEEKER SALVO (SPLIT 2+ & HOMING 2+) -> twin flanking micro-seekers
        if ((p.ActiveSynergies & SynergyFlags.SeekerSalvo) != 0)
        {
            float seekHoming = p.HomingLevel * 3.85f;
            float seekLife = p.BulletLife * 1.25f;
            float seekDmg = p.Damage * 0.62f * dmgMult;
            float seekRad = p.BulletRadius * 0.92f;
            for (int s = -1; s <= 1; s += 2)
            {
                float a = baseAngle + s * 0.66f;
                Vector2 dir = M.FromAngle(a);
                SpawnPlayerBullet(p.Pos + dir * 16f, dir * (p.BulletSpeed * 0.92f), seekRad, seekDmg, seekLife, p.PierceLevel, seekHoming, p.RicochetLevel, synergyKind: 1, hue: 0.84f);
                fired++;
            }
        }

        // Synergy: RAIL CADENCE (HEAVY 2+ & RAPID 2+) -> every 4th shot launches a hyper-velocity centerline rail bolt
        if ((p.ActiveSynergies & SynergyFlags.RailCadence) != 0 && p.ShotCounter % 4 == 0)
        {
            Vector2 dir = M.FromAngle(baseAngle);
            SpawnPlayerBullet(
                p.Pos + dir * 22f,
                dir * 2350f,
                p.BulletRadius * 1.45f,
                p.Damage * 2.1f * dmgMult,
                p.BulletLife * 1.15f,
                p.PierceLevel + 2,
                p.HomingLevel * 1.4f,
                p.RicochetLevel,
                synergyKind: 3,
                hue: 0.09f);
            fired++;
            for (int k = 0; k < 6; k++)
                SpawnParticle(p.Pos + dir * 22f, dir * FxRng.Range(180f, 440f) + FxRng.InCircle(90f), 0.16f, 3.2f, new Vector4(1f, 0.85f, 0.35f, 0.95f), ParticleKind.Spark, 4f);
        }

        Vector4 flash = M.Hsv(0.51f, 0.5f, 1f, 0.8f);
        SpawnParticle(p.Pos + p.AimDir * 20f, p.AimDir * 120f + FxRng.InCircle(50), 0.12f, 4.5f, flash, ParticleKind.Spark, 4f);
        if (!style.Quiet)
            Sfx?.Trigger(style.Sound, (0.16f + MathF.Min(0.12f, fired * 0.005f)) * (style.Kind == 4 ? 1.6f : 1f), M.Clamp(p.Pos.X / (ArenaW * 0.5f), -1f, 1f) * 0.45f);
    }

    public void SpawnPlayerBullet(Vector2 pos, Vector2 vel, float radius, float damage, float life, int pierce, float homing = 0f, int bounces = 0, byte synergyKind = 0, float hue = 0.51f)
    {
        if (PlayerBullets.Count >= 900) return;
        if (!_bulletPool.TryPop(out var b)) b = new Bullet();
        b.Reset();
        b.FromPlayer = true;
        b.Kind = BulletKind.Player;
        b.Pos = pos;
        b.PrevPos = pos;
        b.Vel = vel;
        b.Radius = radius;
        b.Life = life;
        b.MaxLife = life;
        b.Damage = damage;
        b.Hue = hue;
        b.Pierce = pierce;
        b.Homing = homing;
        b.Bounces = bounces;
        b.SynergyKind = synergyKind;
        PlayerBullets.Add(b);
    }

    /// <summary>PRISM LANCE (PIERCE 2+ & RICOCHET 2+): wall/monolith bounces supercharge bolts.</summary>
    private void SuperchargePrismLance(Bullet b)
    {
        if ((Player.ActiveSynergies & SynergyFlags.PrismLance) == 0) return;
        b.Damage = MathF.Min(Player.Damage * 4f, b.Damage * 1.35f);
        b.Pierce += 1;
        b.Life = MathF.Min(b.MaxLife * 1.5f, b.Life + 0.35f);
        float speed = b.Vel.Length();
        if (speed > 1f)
            b.Vel = b.Vel / speed * MathF.Min(2400f, speed * 1.12f);
        b.SynergyKind = 2;
        b.Hue = 0.13f;
        for (int k = 0; k < 6; k++)
            SpawnParticle(b.Pos, FxRng.InCircle(170f), 0.24f, 2.5f, new Vector4(1f, 0.92f, 0.48f, 0.92f), ParticleKind.Spark, 3.2f);
    }

    /// <summary>All enemy fire travels this much faster than the original tuning (fewer, faster shots).</summary>
    public const float BulletPace = 1.75f;

    public Bullet? SpawnEnemyBullet(Vector2 pos, Vector2 vel, float radius, float hue, BulletKind kind, float life = 7f, float damage = 1f, float angular = 0f)
    {
        if (EnemyBullets.Count >= 2600) return null;
        vel *= BulletPace;
        life = MathF.Min(life, 4.2f);
        if (!_bulletPool.TryPop(out var b)) b = new Bullet();
        b.Reset();
        b.FromPlayer = false;
        b.Kind = kind;
        b.Pos = pos;
        b.PrevPos = pos;
        b.Vel = vel;
        b.Radius = radius;
        b.Life = life;
        b.MaxLife = life;
        b.Damage = damage;
        b.Hue = hue;
        b.AngularVel = angular;
        if (Rules.DriftBullets && angular == 0f && FxRng.Chance(0.28f))
            b.AngularVel = FxRng.Signed() * 0.45f;
        EnemyBullets.Add(b);
        return b;
    }

    public void ReturnBullet(List<Bullet> list, int index)
    {
        var b = list[index];
        list.RemoveAt(index);
        b.Reset();
        if (_bulletPool.Count < 4000) _bulletPool.Push(b);
    }

    private void UpdateBullets(float dt)
    {
        for (int i = PlayerBullets.Count - 1; i >= 0; i--)
        {
            var b = PlayerBullets[i];
            b.PrevPos = b.Pos;
            b.Age += dt;
            b.Life -= dt;
            if (b.Homing > 0f)
            {
                float seekRange = b.SynergyKind == 1 ? 640f : 460f;
                var target = NearestEnemy(b.Pos, seekRange);
                if (target != null)
                {
                    float desired = M.AngleOf(target.Pos - b.Pos);
                    float current = M.AngleOf(b.Vel);
                    float speed = b.Vel.Length();
                    b.Vel = M.FromAngle(M.MoveTowardsAngle(current, desired, b.Homing * dt), speed);
                }
            }
            b.Pos += b.Vel * dt;
            if (b.Life <= 0 || b.Pos.LengthSquared() > (ArenaW * 0.5f + 240f) * (ArenaW * 0.5f + 240f))
            {
                if (b.SynergyKind == 4 && b.Life <= 0) BassBurst(b.Pos, b.Damage);
                ReturnBullet(PlayerBullets, i);
                continue;
            }
            if (MathF.Abs(b.Pos.X) > ArenaW * 0.5f - 4f || MathF.Abs(b.Pos.Y) > ArenaH * 0.5f - 4f)
            {
                if (b.Bounces > 0)
                {
                    b.Bounces--;
                    if (MathF.Abs(b.Pos.X) > ArenaW * 0.5f - 4f) b.Vel = new Vector2(-b.Vel.X, b.Vel.Y);
                    if (MathF.Abs(b.Pos.Y) > ArenaH * 0.5f - 4f) b.Vel = new Vector2(b.Vel.X, -b.Vel.Y);
                    b.Pos = new Vector2(M.Clamp(b.Pos.X, -ArenaW * 0.5f + 5, ArenaW * 0.5f - 5), M.Clamp(b.Pos.Y, -ArenaH * 0.5f + 5, ArenaH * 0.5f - 5));
                    SuperchargePrismLance(b);
                }
                else
                {
                    SpawnParticle(b.Pos, FxRng.InCircle(40), 0.16f, 2.2f, M.Hsv(b.Hue, 0.7f, 1f, 0.7f), ParticleKind.Spark, 3f);
                    ReturnBullet(PlayerBullets, i);
                }
            }
        }

        float marginX = ArenaW * 0.5f + 160f;
        float marginY = ArenaH * 0.5f + 160f;
        float surge = BeatSurge(1.4f);
        float needleSurge = BeatSurge(0.9f);
        for (int i = EnemyBullets.Count - 1; i >= 0; i--)
        {
            var b = EnemyBullets[i];
            b.PrevPos = b.Pos;
            b.Age += dt;
            b.Life -= dt;
            if (MathF.Abs(b.AngularVel) > 0.0001f)
                b.Vel = M.Rotate(b.Vel, b.AngularVel * dt);
            // every enemy shot lurches forward on the beat and glides to the next one
            b.Pos += b.Vel * dt * (b.Kind == BulletKind.Needle ? needleSurge : surge);
            if (b.Life <= 0 || MathF.Abs(b.Pos.X) > marginX || MathF.Abs(b.Pos.Y) > marginY)
                ReturnBullet(EnemyBullets, i);
        }
    }

    private Enemy? NearestEnemy(Vector2 from, float maxDistance)
    {
        Enemy? best = null;
        float bestDist = maxDistance * maxDistance;
        foreach (var e in Enemies)
        {
            if (e.Dead || e.Spawning) continue;
            float d = M.DistSq(from, e.Pos);
            if (d < bestDist)
            {
                bestDist = d;
                best = e;
            }
        }
        return best;
    }

    public void SpawnLaser(Vector2 origin, Vector2 target, float warmup, float duration, float width, float hue, float sweep = 0f, bool boss = false, float length = 0f)
    {
        if (Lasers.Count >= 40) return;
        float angle = M.AngleOf(target - origin);
        Lasers.Add(new LaserBeam
        {
            Origin = origin,
            Angle = angle,
            Length = length > 0f ? length : MathF.Sqrt(ArenaW * ArenaW + ArenaH * ArenaH) * 1.3f,
            Width = width,
            Warmup = warmup,
            Duration = duration,
            Hue = hue,
            Sweep = sweep,
            BossLaser = boss,
            Source = _laserSource ?? (boss ? "THE CHOIR" : "LASER"),
        });
        Sfx?.Trigger(SfxType.LaserWarn, boss ? 0.55f : 0.25f);
    }

    private void UpdateLasers(float dt)
    {
        for (int i = Lasers.Count - 1; i >= 0; i--)
        {
            var l = Lasers[i];
            l.Age += dt;
            l.Angle += l.Sweep * dt;
            if (!l.PlayedFire && l.Age >= l.Warmup)
            {
                l.PlayedFire = true;
                Sfx?.Trigger(SfxType.LaserFire, l.BossLaser ? 0.65f : 0.35f);
                Cam.AddShake(l.BossLaser ? 4f : 1.4f);
                for (int k = 0; k < 10; k++)
                {
                    float d = FxRng.Range(0, l.Length);
                    SpawnParticle(l.Origin + l.Direction * d, FxRng.InCircle(80), 0.25f, 2f, M.Hsv(l.Hue, 0.7f, 1f, 0.6f), ParticleKind.Spark, 2f);
                }
            }
            if (l.Age > l.Warmup + l.Duration + l.Fade)
                Lasers.RemoveAt(i);
        }
    }
}
