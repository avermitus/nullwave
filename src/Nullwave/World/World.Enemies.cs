using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;

namespace Nullwave.World;

public sealed partial class World
{
    /// <summary>Capture/test helper: place a fully formed enemy.</summary>
    public void SpawnForCapture(EnemyRole role, Vector2 pos) => SpawnEnemyAt(role, pos, false);

    private void SpawnEnemyAt(EnemyRole role, Vector2 pos, bool spawning)
    {
        bool boss = role == EnemyRole.Boss;
        bool elite = !boss && Wave >= 3 && Rng.Chance(MathF.Min(0.24f, 0.035f + Wave * 0.007f));
        float baseHp = role switch
        {
            EnemyRole.Drifter => 28f,
            EnemyRole.Orbiter => 36f,
            EnemyRole.Sniper => 32f,
            EnemyRole.Dasher => 52f,
            EnemyRole.Turret => 64f,
            EnemyRole.Splitter => 74f,
            EnemyRole.Swarm => 14f,
            EnemyRole.Weaver => 44f,
            EnemyRole.Mortar => 72f,
            EnemyRole.Phantom => 34f,
            EnemyRole.Prism => 92f,
            EnemyRole.Boss => 1250f + Wave * 220f,
            _ => 30f,
        };
        float hp = boss ? baseHp * Mods.EnemyHealth * (1f + MathF.Max(0, Wave / 5 - 1) * 0.22f) : baseHp * GetDifficultyHealthScale() * Mods.EnemyHealth;
        if (elite) hp *= 2.25f;
        float radius = role switch
        {
            EnemyRole.Drifter => 17f,
            EnemyRole.Orbiter => 20f,
            EnemyRole.Sniper => 18f,
            EnemyRole.Dasher => 22f,
            EnemyRole.Turret => 24f,
            EnemyRole.Splitter => 27f,
            EnemyRole.Swarm => 10f,
            EnemyRole.Weaver => 18f,
            EnemyRole.Mortar => 25f,
            EnemyRole.Phantom => 18f,
            EnemyRole.Prism => 24f,
            EnemyRole.Boss => 96f,
            _ => 18f,
        };
        if (elite) radius *= 1.28f;
        // bigger targets: easier to see and to hit at racing speed
        if (!boss) radius *= 1.3f;
        // maps: every spawn path (waves, children, phase adds, captures) lands somewhere the body fits
        if (Map != null && !Map.IsBodyValid(pos, radius + 4f)) pos = Map.ProjectToFloor(pos, radius + 6f);

        float hue = role switch
        {
            EnemyRole.Drifter => 0.96f,
            EnemyRole.Orbiter => 0.54f,
            EnemyRole.Sniper => 0.12f,
            EnemyRole.Dasher => 0.78f,
            EnemyRole.Turret => 0.33f,
            EnemyRole.Splitter => 0.05f,
            EnemyRole.Swarm => 0.91f,
            EnemyRole.Weaver => 0.46f,
            EnemyRole.Mortar => 0.07f,
            EnemyRole.Phantom => 0.74f,
            EnemyRole.Prism => 0.2f,
            EnemyRole.Boss => 0.66f,
            _ => 0.6f,
        };
        hue = (hue + Rng.Range(-0.055f, 0.055f) + 1f) % 1f;

        int sides = boss ? 10 : role switch
        {
            EnemyRole.Drifter => 3,
            EnemyRole.Orbiter => 5,
            EnemyRole.Sniper => 4,
            EnemyRole.Dasher => 3,
            EnemyRole.Turret => 8,
            EnemyRole.Splitter => 6,
            EnemyRole.Swarm => 3,
            _ => 5,
        };
        var shape = new Vector2[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = i / (float)sides * M.Tau;
            float r = radius * Rng.Range(0.74f, 1.12f);
            if (boss && i % 2 == 0) r *= 1.16f;
            shape[i] = M.FromAngle(a, r);
        }

        var e = new Enemy
        {
            Role = role,
            Pos = pos,
            SpawnPos = pos,
            Vel = Vector2.Zero,
            Radius = radius,
            Hp = hp,
            MaxHp = hp,
            Hue = hue,
            Angle = Rng.Range(0, M.Tau),
            AngleVel = Rng.Range(-0.8f, 0.8f),
            Rot = Rng.Range(0, M.Tau),
            RotVel = Rng.Range(-0.7f, 0.7f),
            ShapeSides = sides,
            Shape = shape,
            Boss = boss,
            Elite = elite,
            Spawning = spawning,
            SpawnTimer = boss ? 1.7f : 0.76f,
            SpawnDuration = boss ? 1.7f : 0.76f,
            FireBeats = Rng.Range(0.8f, 2.5f),
            FireIntervalBeats = role switch
            {
                EnemyRole.Sniper => 3.2f,
                EnemyRole.Turret => 3.6f,
                EnemyRole.Splitter => 3.4f,
                EnemyRole.Orbiter => 3.0f,
                EnemyRole.Drifter => 4.2f,
                EnemyRole.Dasher => 5.0f,
                EnemyRole.Swarm => 6.0f,
                EnemyRole.Weaver => 2.0f,
                EnemyRole.Mortar => 4.0f,
                EnemyRole.Phantom => 2.0f,
                EnemyRole.Prism => 8.0f,
                _ => 3.3f,
            },
            BossPhase = 0,
            BossPhaseCount = Mods.BossPhases,
            BossAttackBeats = 1.6f,
            BossScale = 1f,
            State = 0,
            StateTimer = Rng.Range(0.6f, 1.8f),
        };
        e.FireIntervalBeats *= Mods.EnemyFireInterval * (Rules.DoubleTime ? 0.78f : 1f);
        if (elite) e.FireIntervalBeats *= 0.78f;
        // Each role plays its own part of the bar, always on the beat: drifters and splitters on
        // 1 and 3, snipers and orbiters on the backbeat (2 and 4), turrets on the downbeat, swarms
        // and dashers on every beat. Only DOUBLE TIME opens the off-beats.
        e.Rhythm = role switch
        {
            EnemyRole.Drifter => 0x11,
            EnemyRole.Orbiter => 0x44,
            EnemyRole.Sniper => 0x44,
            EnemyRole.Turret => 0x01,
            EnemyRole.Splitter => 0x11,
            EnemyRole.Dasher => 0x55,
            EnemyRole.Swarm => 0x55,
            EnemyRole.Weaver => 0x55,
            EnemyRole.Mortar => 0x01,
            EnemyRole.Phantom => 0x44,
            EnemyRole.Prism => 0x01,
            _ => 0xFF,
        };
        if (elite) e.Rhythm |= 0x55;
        if (Rules.DoubleTime) e.Rhythm |= 0xAA;
        e.Facing = Rng.Range(0, M.Tau);
        e.FireBeats = MathF.Min(e.FireBeats, e.FireIntervalBeats);
        e.MovePhase = Rng.NextFloat();

        Enemies.Add(e);
        if (boss)
        {
            BossEnemy = e;
            EmitEvent("THE CHOIR // LISTEN", 3.0f);
            Sfx?.Trigger(SfxType.Boss, 1f);
            AddShockwave(pos, 500, 1.1f, 10f, new Vector4(0.8f, 0.4f, 1f, 1f));
        }
        else
        {
            AddShockwave(pos, radius * 3.4f, 0.55f, 3f, M.Hsv(hue, 0.5f, 1f, 0.7f));
            BurstParticles(pos, M.Hsv(hue, 0.7f, 1f, 0.7f), elite ? 26 : 10, 120, 2f, 0.5f, ParticleKind.Dot);
        }
    }

    private void UpdateEnemies(float dt, AudioState audio)
    {
        for (int i = 0; i < Enemies.Count; i++)
        {
            var e = Enemies[i];
            if (e.Dead) continue;
            e.HitFlash = MathF.Max(0, e.HitFlash - dt * 5f);
            e.Muzzle = MathF.Max(0, e.Muzzle - dt * 6f);
            e.ContactCooldown = MathF.Max(0, e.ContactCooldown - dt);
            if (e.Spawning)
            {
                e.SpawnTimer -= dt;
                if (e.SpawnTimer <= 0)
                {
                    e.Spawning = false;
                    AddShockwave(e.Pos, e.Radius * 5f, 0.6f, 2.5f, M.Hsv(e.Hue, 0.6f, 1f, 0.8f));
                    BurstParticles(e.Pos, M.Hsv(e.Hue, 0.65f, 1f, 0.65f), 20, 190, 2.4f, 0.55f, ParticleKind.Spark);
                }
                continue;
            }

            e.Age += dt;
            e.Angle += e.AngleVel * dt;
            e.Rot += e.RotVel * dt;
            if (e.Boss)
            {
                if (e.SongBoss) UpdateSongBoss(e, dt, audio);
                else UpdateBoss(e, dt, audio);
                continue;
            }

            UpdateEnemyBrain(e, dt, audio);
            if (Map != null) RescueBody(ref e.Pos, e.Radius);

            if (EighthTick)
            {
                e.FireBeats -= 0.5f;
                if (e.FireBeats <= 0f && (e.Rhythm & (1 << EighthInBar)) != 0 && EnemyBullets.Count < 2400)
                {
                    FireEnemy(e, audio);
                    e.Muzzle = 1f;
                    e.FireBeats = e.FireIntervalBeats * Rng.Range(0.9f, 1.1f);
                }
            }
            e.Telegraph = TelegraphFor(e);

            if (Map == null)
            {
                float margin = e.Radius + 20f;
                e.Pos = new Vector2(
                    M.Clamp(e.Pos.X, -ArenaW * 0.5f + margin, ArenaW * 0.5f - margin),
                    M.Clamp(e.Pos.Y, -ArenaH * 0.5f + margin, ArenaH * 0.5f - margin));
            }
        }
        ResolveEnemySpacing();
    }

    private void ResolveEnemySpacing()
    {
        // Steering alone loses to fast catch-up movement and is bypassed by hoppers/dashes.
        // Resolve bodies after every brain has moved, even outside the camera. Several passes
        // let a correction propagate through a pack instead of compressing its next neighbour.
        for (int pass = 0; pass < 4; pass++)
        for (int i = 0; i < Enemies.Count; i++)
        {
            var a = Enemies[i];
            if (a.Dead || a.Spawning || a.Boss) continue;
            for (int j = i + 1; j < Enemies.Count; j++)
            {
                var b = Enemies[j];
                if (b.Dead || b.Spawning || b.Boss) continue;
                Vector2 delta = b.Pos - a.Pos;
                float room = a.Radius + b.Radius + 8f;
                float distanceSq = delta.LengthSquared();
                if (distanceSq >= room * room) continue;
                float distance = MathF.Sqrt(distanceSq);
                // Coincident spawns still need a stable direction; don't consume gameplay RNG.
                Vector2 normal = distance > 0.001f ? delta / distance
                    : M.FromAngle((i * 0.618034f + j * 0.381966f) * M.Tau, 1f);
                Vector2 correction = normal * ((room - distance) * 0.5f);
                if (Map != null)
                {
                    // separation is a swept shove too: a pack against a wall compresses along it, never through it
                    a.Pos = ShiftBody(a.Pos, a.Pos - correction, a.Radius);
                    b.Pos = ShiftBody(b.Pos, b.Pos + correction, b.Radius);
                    continue;
                }
                a.Pos = ClampToArena(a.Pos - correction, a.Radius + 20f);
                b.Pos = ClampToArena(b.Pos + correction, b.Radius + 20f);
            }
        }
    }

    /// <summary>How close (0..1) the enemy is to its next on-beat shot, over the final beat.</summary>
    private float TelegraphFor(Enemy e)
    {
        int need = Math.Max(1, (int)MathF.Ceiling(e.FireBeats / 0.5f - 0.001f));
        if (e.FireBeats <= 0f) need = 1;
        if (need > 4) return 0f;
        int k = need;
        for (; k < need + 8; k++)
            if ((e.Rhythm & (1 << ((EighthInBar + k) % 8))) != 0) break;
        float eighthFrac = (BeatPhase * 2f) % 1f;
        float beatsUntil = (k - eighthFrac) * 0.5f;
        return M.Clamp01(1f - beatsUntil);
    }

    /// <summary>
    /// Every enemy hunts you, each in its own style. The further away it is, the harder it
    /// chases (up to ~3.5x its close-range speed), so nothing is ever left behind when you race off.
    /// Each role also moves on its own part of the bar (see <see cref="Pocket"/>): the Juggernaut
    /// lurches on the downbeat, the Pouncer lunges on the backbeat, the Hunter darts on the off-beats,
    /// the Pack skitters on the sixteenths, and so on, so a crowded wave reads like a drum pattern.
    /// </summary>
    private void UpdateEnemyBrain(Enemy e, float dt, AudioState audio)
    {
        Vector2 toPlayer = Player.Pos - e.Pos;
        float dist = toPlayer.Length();
        Vector2 dir = dist > 0.01f ? toPlayer / dist : new Vector2(1, 0);
        // Maps: with a clear run at the player every role hunts in its own style; without one it
        // follows the shared path field round the walls (one Dijkstra per player move, not per enemy).
        bool routed = false;
        if (Map != null && PlayerField != null)
        {
            e.NavCheck -= dt;
            if (e.NavCheck <= 0f)
            {
                e.NavCheck = 0.12f + (e.MovePhase * 0.08f);
                e.NavSight = Map.CapsuleClear(e.Pos, Player.Pos, e.Radius + 6f);
            }
            if (!e.NavSight)
            {
                routed = true;
                dir = PlayerField.Direction(e.Pos, Player.Pos, e.Radius);
                dist = MathF.Max(dist, PlayerField.PathDistance(e.Pos, Player.Pos));
            }
        }
        Vector2 perp = M.Perp(dir);
        e.Facing = M.MoveTowardsAngle(e.Facing, M.AngleOf(dir), dt * (e.Role == EnemyRole.Sniper ? 2.2f : 4f));
        // the first waves ease in (72 % speed on wave 1, full speed from wave 5)
        float speed = EnemyBaseSpeed * GetDifficultySpeedScale() * Mods.EnemySpeed * EarlyWaveEase(Wave);
        float pursuit = ChaseBoost(dist);
        // pack steering: from range, lead the ship and fan out across its path, each enemy in its own
        // lane, instead of everyone queueing in one line behind you. Fades out by ~260 units, so close
        // attacks (lunges, rams, orbits) are unchanged. Facing above still points at the ship.
        float flankU = M.Clamp01((dist - 260f) / 440f);
        float flank = (e.Boss || routed) ? 0f : flankU * flankU * (3f - 2f * flankU);
        if (flank > 0f)
        {
            float lead = M.Clamp(dist / MathF.Max(240f, speed * pursuit * 1.4f), 0f, 0.75f) * flank;
            Vector2 predicted = Player.Pos + Player.Vel * lead;
            float lane = (e.MovePhase - 0.5f) * 2.2f * flank;
            Vector2 away = M.SafeNorm(e.Pos - predicted);
            float standoff = MathF.Min((dist - 260f) * 0.55f, 320f) * flank;
            Vector2 to = predicted + M.FromAngle(M.AngleOf(away) + lane, standoff) - e.Pos;
            if (to.LengthSquared() > 1f)
            {
                dir = Vector2.Normalize(to);
                perp = M.Perp(dir);
            }
        }
        float spin = e.MovePhase < 0.5f ? 1f : -1f;
        float t = e.Age + e.MovePhase * 10f;
        int beatInBar = Math.Clamp((int)BarPos, 0, 3);
        Vector2 desired = Vector2.Zero;
        bool direct = false;
        float surge = 1f;
        float groove;

        switch (e.Role)
        {
            case EnemyRole.Drifter:
            {
                // HUNTER (off-beats): homes in on where you are about to be, darting on every "and"
                Vector2 lead = Player.Pos + Player.Vel * MathF.Min(0.45f, dist / 1400f);
                desired = M.SafeNorm(lead - e.Pos) * speed * 1.3f * pursuit;
                surge = PocketSurge(dt, Pocket.OffBeats, 1.8f, 7f, out groove);
                break;
            }
            case EnemyRole.Orbiter:
                // SPIRALLER (pickups): corkscrews in around you and whips round on the "a" before each beat
                desired = (dir * (dist > 190f ? 0.6f : 0.15f) + perp * spin * 0.85f) * speed * 1.45f * pursuit;
                surge = PocketSurge(dt, Pocket.Pickups, 2.0f, 9f, out groove);
                break;
            case EnemyRole.Sniper:
            {
                // STALKER (the "and" of 4): holds its aim through the bar, then jumps to your
                // other side on the pickup into the next bar
                float side = (BarIndex & 1) == 0 ? spin : -spin;
                desired = dir * (dist > 430f ? speed * 1.35f * pursuit : dist < 300f ? -speed * 0.7f : 0f) + perp * side * speed * 0.75f;
                surge = PocketSurge(dt, Pocket.AndOfFour, 6f, 3f, out groove);
                break;
            }
            case EnemyRole.Dasher:
                // POUNCER (backbeat): chases with a side-to-side sway, braces, then lunges on 2 or 4
                e.StateTimer -= dt;
                surge = PocketSurge(dt, Pocket.Backbeat, 1.2f, 4f, out groove);
                if (e.State == 0)
                {
                    desired = (dir * pursuit + perp * MathF.Sin(t * 3f) * 0.6f) * speed * 1.15f;
                    if (e.StateTimer <= 0 && dist < 640f)
                    {
                        e.State = 1;
                        e.StateTimer = 0.2f;
                        e.Angle = M.AngleOf(dir);
                        e.AngleVel = 0;
                    }
                }
                else if (e.State == 1)
                {
                    desired = Vector2.Zero;
                    e.DashDir = dir;
                    if (e.StateTimer <= 0 && BeatTick && (beatInBar == 1 || beatInBar == 3))
                    {
                        e.State = 2;
                        e.StateTimer = 0.36f;
                        FireRing(e.Pos, 4, 190f, BulletKind.Shard, e.Hue, e.Radius + 8f);
                    }
                }
                else if (e.State == 2)
                {
                    desired = e.DashDir * 1300f;
                    e.Vel = desired;
                    surge = 1f;
                    if (e.StateTimer <= 0) { e.State = 3; e.StateTimer = 0.5f; }
                }
                else
                {
                    desired = dir * speed * 0.8f;
                    if (e.StateTimer <= 0) { e.State = 0; e.StateTimer = Rng.Range(0.7f, 1.4f); }
                }
                break;
            case EnemyRole.Turret:
                // HOPPER (every beat): leaps at you on each beat, skids to a stop in between
                if (BeatTick) e.Vel = dir * (420f + MathF.Min(dist, 1500f) * 0.9f) * GetDifficultySpeedScale() * Mods.EnemySpeed;
                else e.Vel *= MathF.Exp(-dt * 4.5f);
                direct = true;
                e.Angle += dt * 0.4f;
                PocketSurge(dt, Pocket.Beats, 0f, 6f, out groove);
                break;
            case EnemyRole.Splitter:
                // JUGGERNAUT (downbeat): slow and relentless, with one heavy lurch on beat 1
                desired = dir * speed * 1.15f * pursuit;
                surge = PocketSurge(dt, Pocket.Downbeat, 4f, 1.6f, out groove);
                break;
            case EnemyRole.Swarm:
            {
                // PACK (sixteenths): skitters in short steps, each member closing in from its own side
                Vector2 target = Player.Pos + M.FromAngle(e.MovePhase * M.Tau + Time * 0.6f * spin, MathF.Min(140f, dist * 0.4f));
                desired = M.SafeNorm(target - e.Pos) * speed * 1.9f * pursuit;
                surge = PocketSurge(dt, Pocket.Sixteenths, 1.6f, 16f, out groove);
                break;
            }
            case EnemyRole.Weaver:
            {
                // SERPENT (beat to beat): its S-curve crosses your line on every beat and swings
                // widest on the "and"; it weaves hard up close and straightens out from far away
                float swing = MathF.Sin(BarPos * MathF.PI) * spin;
                float wiggle = swing * 1.3f * M.Clamp(1f - (dist - 400f) / 700f, 0.15f, 1f);
                desired = M.SafeNorm(dir + perp * wiggle) * speed * 1.25f * pursuit;
                PocketSurge(dt, Pocket.Beats, 0f, 5f, out groove);
                break;
            }
            case EnemyRole.Mortar:
                // SIEGE (half bar): marches on beats 1 and 2, plants on 3 and 4 to fire
                desired = BarPos < 2f ? dir * speed * 1.7f * pursuit : Vector2.Zero;
                PocketSurge(dt, Pocket.HalfNotes, 0f, 2f, out groove);
                if (BarPos >= 2f) groove *= 0.3f;
                break;
            case EnemyRole.Phantom:
            {
                // BLINKER (half notes): drifts in, and blinks to a flank closer to you on beats 1 and 3
                desired = (dir * pursuit + perp * 0.4f) * speed * 0.9f;
                PocketSurge(dt, Pocket.HalfNotes, 0f, 4f, out groove);
                if (BeatTick && (beatInBar == 0 || beatInBar == 2))
                {
                    Vector2 from = e.Pos;
                    float side = Rng.Chance(0.5f) ? 1f : -1f;
                    float r = MathF.Max(220f, MathF.Min(dist * 0.55f, 420f));
                    Vector2 to = ClampToArena(Player.Pos + M.FromAngle(M.AngleOf(-dir) + side * Rng.Range(0.7f, 1.5f), r), e.Radius + 30f);
                    // maps: only blink to a flank the player can see (never into another room); else stay
                    if (Map != null && (!Map.IsBodyValid(to, e.Radius + 4f) || !Map.SegmentClear(Player.Pos, to))) to = e.Pos;
                    AddShockwave(from, e.Radius * 3f, 0.35f, 3f, M.Hsv(e.Hue, 0.6f, 1f, 0.8f));
                    BurstParticles(from, M.Hsv(e.Hue, 0.6f, 1f, 0.7f), 14, 260f, 2.4f, 0.35f, ParticleKind.Spark);
                    e.Pos = to;
                    e.Vel = Vector2.Zero;
                    e.HitFlash = 0.8f;
                    AddShockwave(to, e.Radius * 3.5f, 0.4f, 3f, M.Hsv(e.Hue, 0.5f, 1f, 0.9f));
                    Sfx?.Trigger(SfxType.Dash, 0.25f);
                }
                break;
            }
            case EnemyRole.Prism:
            {
                // SENTINEL (the bar): circles you at range, a quarter turn per bar, easing into each downbeat
                float bar = BarPos / 4f;
                float turn = (BarIndex & 3) + bar * bar * (3f - 2f * bar);
                Vector2 target = Player.Pos + M.FromAngle(turn * MathF.PI * 0.5f * spin + e.MovePhase * M.Tau, 340f);
                desired = M.SafeNorm(target - e.Pos) * speed * 1.2f * pursuit;
                e.Angle += dt * 0.8f;
                PocketSurge(dt, Pocket.Downbeat, 0f, 1.5f, out groove);
                break;
            }
            default:
                groove = 0f;
                break;
        }
        e.Groove = groove;

        if (routed && !direct && !(e.Role == EnemyRole.Dasher && e.State == 2) && desired.LengthSquared() > 1f)
        {
            // a role's own target (lead point, orbit slot, flank) is behind a wall: take the route instead
            desired = dir * MathF.Max(desired.Length(), speed * pursuit);
        }

        // the wave's last enemies stop keeping their distance and come straight for you
        float hunt = e.Boss || e.Spawning ? 0f : StragglerHunt;
        if (hunt > 0f)
        {
            Vector2 rush = (dir * 1.5f * pursuit + perp * spin * 0.25f) * speed;
            if (direct) e.Vel = M.Lerp(e.Vel, rush, hunt * (1f - MathF.Exp(-dt * 4f)));
            else desired = M.Lerp(desired, rush, hunt);
        }
        if (!direct)
        {
            // personal space so the chase reads as a pack, not a blob
            foreach (var o in Enemies)
            {
                if (o == e || o.Dead || o.Spawning || o.Boss) continue;
                Vector2 d = e.Pos - o.Pos;
                float l = d.Length();
                // a looser formation while chasing from range, the usual personal space up close
                float room = e.Radius + o.Radius + 30f + 40f * flank;
                if (l < room && l > 0.01f) desired += d / l * (room - l) * 5f;
            }
            if (e.Role != EnemyRole.Dasher || e.State != 2)
                e.Vel = M.Lerp(e.Vel, desired, 1f - MathF.Exp(-dt * 4f));
        }
        // each role strides on its own part of the bar: a push on its hit, then a coast
        if (Map != null) MoveBody(ref e.Pos, ref e.Vel, e.Vel * dt * surge, e.Radius);
        else e.Pos += e.Vel * dt * surge;
    }

    /// <summary>Speed scale for the opening waves: 0.72 on wave 1 rising to 1 by wave 5.</summary>
    public static float EarlyWaveEase(int wave) => M.Lerp(0.72f, 1f, M.Clamp01((wave - 1) / 4f));

    /// <summary>Close-range enemy speed (scaled by difficulty and mods).</summary>
    public const float EnemyBaseSpeed = 175f;

    /// <summary>Chase multiplier: 1 within 300 units, rising steeply to 4.4 by ~1300 units away.</summary>
    public static float ChaseBoost(float dist) => 1f + M.Clamp((dist - 300f) / 300f, 0f, 3.4f);

    private void FireEnemy(Enemy e, AudioState audio)
    {
        Vector2 toPlayer = Player.Pos - e.Pos;
        float aim = M.AngleOf(toPlayer);
        bool high = High > Bass * 1.05f && High > 0.22f;
        bool bass = Bass > 0.36f;
        BulletKind kind = high ? BulletKind.Needle : bass ? BulletKind.Orb : BulletKind.Shard;
        float speed = (195f + MathF.Min(145f, Wave * 2.4f)) * GetDifficultyBulletScale();
        float hue = e.Hue;
        int before = EnemyBullets.Count;

        switch (e.Role)
        {
            case EnemyRole.Drifter:
                SpawnEnemyBullet(e.Pos + M.FromAngle(aim, e.Radius), M.FromAngle(aim, speed), 5.6f, hue, kind, 7f, 1f);
                break;
            case EnemyRole.Orbiter:
                SpawnFan(e.Pos, aim, 2, 0.14f, speed, kind, hue);
                break;
            case EnemyRole.Sniper:
                SpawnFan(e.Pos, aim, 3, 0.07f, speed * 1.15f, BulletKind.Needle, hue);
                break;
            case EnemyRole.Turret:
                FireRing(e.Pos, 6 + Math.Min(6, Wave / 5), speed * 0.82f, kind, hue, e.Radius + 6f, true);
                break;
            case EnemyRole.Splitter:
                FireRing(e.Pos, 5, speed * 0.7f, BulletKind.Orb, hue, e.Radius + 4f, true);
                break;
            case EnemyRole.Swarm:
                if (Wave >= 6) SpawnEnemyBullet(e.Pos + M.FromAngle(aim, e.Radius), M.FromAngle(aim, speed * 1.1f), 3.8f, hue, BulletKind.Needle, 5f, 1f);
                break;
            case EnemyRole.Weaver:
            {
                // a mine: slow, long-lived, drifting off the weaver's wake
                int before2 = EnemyBullets.Count;
                SpawnEnemyBullet(e.Pos - M.SafeNorm(e.Vel) * e.Radius, M.FromAngle(Rng.Range(0, M.Tau), 18f), 7f, hue, BulletKind.Orb, 9f, 1f);
                if (EnemyBullets.Count > before2) EnemyBullets[^1].Life = EnemyBullets[^1].MaxLife = 7f;
                break;
            }
            case EnemyRole.Mortar:
            {
                float beat = 60f / MathF.Max(60f, Bpm);
                Vector2 target = Player.Pos + Player.Vel * beat * 1.2f;
                Strikes.Add(new Strike { Pos = ClampToArena(target, 40f), Timer = beat * 2f, Duration = beat * 2f, Hue = hue });
                Sfx?.Trigger(SfxType.LaserWarn, 0.3f);
                break;
            }
            case EnemyRole.Phantom:
                SpawnFan(e.Pos, aim, 3, 0.16f, speed * 1.05f, BulletKind.Needle, hue);
                break;
            case EnemyRole.Prism:
            {
                float beat = 60f / MathF.Max(60f, Bpm);
                float spin = Rng.Chance(0.5f) ? 1.15f : -1.15f;
                _laserSource = "SENTINEL BEAM";
                SpawnLaser(e.Pos, Player.Pos, beat, beat * 2.5f, 15f, hue, spin, false, 560f);
                if (Wave >= 3 || e.Elite)
                {
                    Vector2 opp = e.Pos - (Player.Pos - e.Pos);
                    SpawnLaser(e.Pos, opp, beat, beat * 2.5f, 15f, (hue + 0.08f) % 1f, spin, false, 560f);
                }
                _laserSource = null;
                break;
            }
        }

        if (e.Elite)
        {
            if (e.Role == EnemyRole.Sniper) FireRing(e.Pos, 5, speed * 0.7f, BulletKind.Shard, hue, e.Radius + 6f);
            else SpawnFan(e.Pos, aim + 0.17f, 2, 0.12f, speed * 1.1f, kind, hue);
        }

        if (EnemyBullets.Count > before)
        {
            AddShockwave(e.Pos, e.Radius * 1.8f, 0.22f, 1f, M.Hsv(hue, 0.7f, 1f, 0.4f));
            SpawnParticle(e.Pos + M.FromAngle(aim, e.Radius), M.FromAngle(aim, 200), 0.2f, 3f, M.Hsv(hue, 0.7f, 1f, 0.75f), ParticleKind.Spark, 3f);
        }
    }

    private void SpawnFan(Vector2 origin, float aim, int count, float spread, float speed, BulletKind kind, float hue)
    {
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : i / (float)(count - 1);
            float angle = aim + (t - 0.5f) * spread * 2f;
            SpawnEnemyBullet(origin + M.FromAngle(angle, 16f), M.FromAngle(angle, speed), kind == BulletKind.Needle ? 3.8f : 5f, hue, kind, 7.5f, 1f);
        }
    }

    private void FireRing(Vector2 origin, int count, float speed, BulletKind kind, float hue, float radius, bool surge = false)
    {
        float offset = Rng.Range(0, M.Tau);
        for (int i = 0; i < count; i++)
        {
            float angle = offset + i / (float)count * M.Tau;
            int before = EnemyBullets.Count;
            SpawnEnemyBullet(origin + M.FromAngle(angle, radius), M.FromAngle(angle, speed), kind == BulletKind.Needle ? 3.8f : 5.4f, hue, kind, 8f, 1f);
            if (surge && EnemyBullets.Count > before)
            {
                var b = EnemyBullets[^1];
                b.BeatSurge = true;
                b.BaseSpeed = speed;
                b.Surge = 1f;
            }
        }
    }

    public const float BossPhaseClearRadius = 580f;

    public static string BossPhaseSubtitle(int phase, int totalPhases) => phase switch
    {
        0 => "OVERTURE",
        1 => "HARMONIC CONVERGENCE",
        2 => "NULL RESONANCE",
        _ => "BLIND CHOIR",
    };

    public static int ComputeDesiredBossPhase(float healthFrac, int totalPhases)
    {
        int phases = Math.Max(3, totalPhases);
        int desired = 0;
        for (int p = 0; p < phases - 1; p++)
        {
            float threshold = 1f - (p + 1) / (float)phases;
            if (healthFrac < threshold) desired = p + 1;
        }
        return desired;
    }

    private void TriggerBossPhaseTransition(Enemy e, int newPhase, int totalPhases)
    {
        e.HighestTriggeredPhase = newPhase;
        e.BossPhase = newPhase;
        e.BossInvuln = e.PhaseBreakDuration;
        e.PhaseBreakTimer = e.PhaseBreakDuration;
        e.HitFlash = 1f;
        e.BossAttackBeats = 1.25f;
        e.Telegraph = 0f;

        // Clear nearby enemy bullets so phase shifts open cleanly on the beat.
        float clearSq = BossPhaseClearRadius * BossPhaseClearRadius;
        for (int i = EnemyBullets.Count - 1; i >= 0; i--)
        {
            var b = EnemyBullets[i];
            if (M.DistSq(b.Pos, e.Pos) > clearSq) continue;
            SpawnParticle(b.Pos, M.SafeNorm(b.Pos - e.Pos) * 140f + FxRng.InCircle(60f), 0.25f, 2.4f, M.Hsv(b.Hue, 0.55f, 1f, 0.85f), ParticleKind.Spark, 2.5f);
            ReturnBullet(EnemyBullets, i);
        }

        AddShockwave(e.Pos, BossPhaseClearRadius, 0.85f, 12f, M.Hsv(e.Hue, 0.65f, 1f, 1f), filled: true, heavy: true);
        AddShockwave(e.Pos, 320f, 0.55f, 4f, Vector4.One);
        BurstParticles(e.Pos, M.Hsv(e.Hue, 0.7f, 1f, 0.9f), 28, 360f, 3.2f, 0.65f, ParticleKind.Spark);
        EmitEvent($"THE CHOIR // {BossPhaseSubtitle(e.BossPhase, totalPhases)}", 2.0f);
        Sfx?.Trigger(SfxType.Boss, 0.9f);
        AddMicroHitStop(0.055f);
        Cam.AddShake(18f);
        Flash = MathF.Max(Flash, 0.35f);
        for (int i = 0; i < 3; i++)
        {
            Vector2 p = e.Pos + FxRng.InCircle(240);
            if (Map != null) p = ShiftBody(e.Pos, p, 10f * 1.3f * 1.28f + 4f);
            if (Enemies.Count < 28) SpawnEnemyAt(EnemyRole.Swarm, p, true);
        }
    }

    private void UpdateBoss(Enemy e, float dt, AudioState audio)
    {
        if (e.BossInvuln > 0f) e.BossInvuln = MathF.Max(0f, e.BossInvuln - dt);
        if (e.PhaseBreakTimer > 0f) e.PhaseBreakTimer = MathF.Max(0f, e.PhaseBreakTimer - dt);
        float targetFrac = e.HealthFrac;
        e.BossHealthDisplay = e.BossHealthDisplay > targetFrac
            ? MathF.Max(targetFrac, e.BossHealthDisplay - dt * 0.42f)
            : targetFrac;
        e.Angle += dt * 0.22f;
        e.Rot += dt * 0.55f;
        // The Choir stalks the player instead of parking at the top of the arena;
        // a boss that can drift off-screen is a boss you cannot read.
        // Fixed distances (not arena fractions) so the Choir stays on screen in the large arena.
        float targetX = Player.Pos.X + MathF.Sin(Time * 0.29f) * 400f;
        float targetY = Player.Pos.Y - 230f + MathF.Sin(Time * 0.23f + 1.4f) * 72f;
        Vector2 target = new(
            M.Clamp(targetX, -ArenaW * 0.36f, ArenaW * 0.36f),
            M.Clamp(targetY, -ArenaH * 0.36f, ArenaH * 0.36f));
        if (Map != null)
        {
            // maps: hover over open floor near the player, travelling round walls, never through them
            MoveBossInMap(e, BossTarget(new Vector2(targetX, targetY)), 1.4f, dt);
        }
        else
        {
            e.Vel = (target - e.Pos) * 1.1f;
            e.Pos = M.Lerp(e.Pos, target, 1f - MathF.Exp(-dt * 1.4f));
        }
        e.BossScale = 1f + BeatPulse * 0.025f + MathF.Sin(Time * 0.7f) * 0.01f;

        int phases = Math.Max(3, Mods.BossPhases);
        int desiredPhase = ComputeDesiredBossPhase(targetFrac, phases);
        if (desiredPhase > e.HighestTriggeredPhase)
            TriggerBossPhaseTransition(e, desiredPhase, phases);

        if (e.BossInvuln <= 0f)
        {
            if (BeatTick)
            {
                e.BossAttackBeats -= 1f;
                if (e.BossAttackBeats <= 0f)
                {
                    BossAttack(e, audio);
                    e.BossAttackBeats = e.BossPhase switch
                    {
                        0 => 2.0f,
                        1 => 1.65f,
                        2 => 1.45f,
                        _ => 1.15f,
                    };
                }
            }
            e.Telegraph = e.BossAttackBeats <= 1.0f ? M.Clamp01(1f - e.BossAttackBeats + BeatPhase * 0.5f) : 0f;
        }
        else
        {
            e.Telegraph = 0f;
        }
    }

    private void BossAttack(Enemy e, AudioState audio)
    {
        int phase = e.BossPhase;
        int pattern = e.State++ % (phase >= 2 ? 4 : 3);
        float aim = M.AngleOf(Player.Pos - e.Pos);
        float baseSpeed = (225f + MathF.Min(130f, Wave * 2f)) * GetDifficultyBulletScale();
        float hue = e.Hue;

        if (pattern == 0)
        {
            FireRing(e.Pos, 10 + Math.Min(6, Wave / 4), baseSpeed * 0.85f, BulletKind.Orb, hue, e.Radius + 18f);
            SpawnFan(e.Pos, aim, 4, 0.12f, baseSpeed * 1.15f, BulletKind.Needle, hue + 0.02f);
        }
        else if (pattern == 1)
        {
            int arms = 2 + phase;
            for (int arm = 0; arm < arms; arm++)
            {
                float a = e.Angle + arm * M.Tau / arms;
                for (int k = 0; k < 3; k++)
                    SpawnEnemyBullet(e.Pos + M.FromAngle(a, e.Radius + 14 + k * 8), M.FromAngle(a + 0.16f * k, baseSpeed * (0.85f + k * 0.06f)), 4.6f, hue, BulletKind.Shard, 8f, 1f);
            }
            if (Lasers.Count < 28)
            {
                if (phase == 0)
                {
                    SpawnLaser(e.Pos, Player.Pos, 0.9f, 0.55f, 28f, hue, 0.55f, true);
                }
                else if (phase == 1)
                {
                    SpawnLaser(e.Pos, Player.Pos, 0.85f, 0.65f, 28f, hue, 0.55f, true);
                    SpawnLaser(e.Pos, Player.Pos, 0.85f, 0.65f, 28f, (hue + 0.06f) % 1f, -0.55f, true);
                }
                else
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float ang = aim + (i - 1) * 0.42f;
                        SpawnLaser(e.Pos, e.Pos + M.FromAngle(ang, 120f), 0.80f, 0.72f, 26f, (hue + i * 0.04f) % 1f, (i % 2 == 0 ? 0.52f : -0.52f), true);
                    }
                }
            }
        }
        else if (pattern == 2)
        {
            int count = 14;
            int gap = Rng.Range(0, count);
            for (int i = 0; i < count; i++)
            {
                if (i == gap || i == (gap + 1) % count) continue;
                float a = i / (float)count * M.Tau;
                Vector2 spawn = e.Pos + M.FromAngle(a, MathF.Min(ArenaW * 0.48f, 1300f));
                // maps: the closing ring forms at the walls of the boss's room instead of behind them
                if (Map != null) spawn = WallAnchor(e.Pos, M.FromAngle(a), 1300f, 24f);
                Vector2 vel = M.FromAngle(a + MathF.PI, baseSpeed * 0.72f);
                SpawnEnemyBullet(spawn, vel, 6f, hue, BulletKind.Orb, 9f, 1f);
            }
            SpawnLaser(e.Pos, Player.Pos, 0.75f, 0.8f, 38f, hue, -0.55f, true);
            if (phase >= 1)
                SpawnLaser(e.Pos, Player.Pos, 0.75f, 0.8f, 32f, (hue + 0.08f) % 1f, 0.55f, true);
        }
        else
        {
            FireRing(e.Pos, 10, baseSpeed * 0.7f, BulletKind.Orb, hue, e.Radius + 24f);
            SpawnFan(e.Pos, aim + 0.2f, 3, 0.18f, baseSpeed, BulletKind.Needle, hue);
            SpawnLaser(e.Pos, Player.Pos, 0.65f, 1.0f, 30f, hue, 0.9f, true);
            SpawnLaser(e.Pos, Player.Pos, 0.65f, 1.0f, 30f, (hue + 0.08f) % 1f, -0.9f, true);
        }

        BurstParticles(e.Pos, M.Hsv(hue, 0.7f, 1f, 0.7f), 16, 240, 2.4f, 0.4f, ParticleKind.Spark);
        Cam.AddShake(2.2f);
        Sfx?.Trigger(SfxType.LaserWarn, 0.2f);
    }

    private void DamageEnemy(Enemy e, float damage, Vector2? source = null)
    {
        if (e.Dead || e.Spawning || e.BossInvuln > 0) return;
        e.Hp -= damage;
        e.HitFlash = 1f;
        PlayHitNote();
        if (source.HasValue)
        {
            SpawnParticle(source.Value, FxRng.InCircle(60), 0.16f, 2.2f, M.Hsv(e.Hue, 0.7f, 1f, 0.75f), ParticleKind.Spark, 4f);
        }
        if (e.Hp <= 0)
            KillEnemy(e);
    }

    private void KillEnemy(Enemy e)
    {
        if (e.Dead) return;
        e.Dead = true;
        e.MarkedForRemoval = true;
        Kills++;
        if (CurrentTrack != null) CurrentTrack.Kills++;
        PlayKillNote(e);
        FlowGain(e.Boss ? 0.3f : 0.035f);
        AddCombo(e.Boss ? 16 : 2);
        AddScore(e.Boss ? 6000 + Wave * 400 : 80 + e.Tier * 20 + (e.Elite ? 120 : 0), e.Pos, null, M.Hsv(e.Hue, 0.7f, 1f, 1f));
        Sfx?.Trigger(e.Boss ? SfxType.Explosion : SfxType.Kill, e.Boss ? 1f : 0.35f);
        Cam.AddShake(e.Boss ? 20f : 2.4f);
        BurstParticles(e.Pos, M.Hsv(e.Hue, 0.75f, 1f, 0.8f), e.Boss ? 140 : e.Elite ? 44 : 22, e.Boss ? 520 : 230, e.Boss ? 4.5f : 2.8f, e.Boss ? 1.4f : 0.65f, ParticleKind.Spark);
        AddShockwave(e.Pos, e.Boss ? 680 : e.Radius * 7f, e.Boss ? 1.3f : 0.55f, e.Boss ? 12f : 2.5f, M.Hsv(e.Hue, 0.6f, 1f, 0.9f));
        // starburst: long fast streaks plus a white core pop, so every kill reads at full speed
        int rays = e.Boss ? 48 : e.Elite ? 22 : 12;
        for (int i = 0; i < rays; i++)
        {
            float a = i / (float)rays * M.Tau + FxRng.Signed() * 0.1f;
            SpawnParticle(e.Pos, M.FromAngle(a, FxRng.Range(700f, 1300f)), FxRng.Range(0.18f, 0.32f), FxRng.Range(2f, 3.4f), M.Hsv((e.Hue + FxRng.Range(-0.06f, 0.06f) + 1f) % 1f, 0.5f, 1f, 0.95f), ParticleKind.Spark, 5f);
        }
        AddShockwave(e.Pos, e.Radius * 2.6f, 0.2f, 9f, new Vector4(1f, 1f, 1f, 0.9f), true);

        if (e.Role == EnemyRole.Splitter && !e.Boss)
        {
            for (int i = 0; i < 2; i++)
            {
                if (Enemies.Count >= 28) break;
                Vector2 p = e.Pos + FxRng.InCircle(e.Radius * 2.2f);
                if (Map != null) p = ShiftBody(e.Pos, p, 10f * 1.3f * 1.28f);
                SpawnChild(EnemyRole.Swarm, p);
            }
        }

        if (e.Boss)
        {
            BossKills++;
            BossEnemy = null;
            AddMicroHitStop(0.085f);
            for (int i = EnemyBullets.Count - 1; i >= 0; i--) ReturnBullet(EnemyBullets, i);
            for (int i = 0; i < 4; i++)
                SpawnPickup(e.Pos + FxRng.InCircle(180), i == 0 ? PickupKind.Health : PickupKind.Core, 1f, e.Hue);
            AddShockwave(e.Pos, 1200, 1.8f, 30f, new Vector4(0.8f, 0.5f, 1f, 1f), filled: true, heavy: true);
            if (e.SongBoss) OnSongBossKilled(e);
            else EmitEvent("CHOIR SILENCED // MOVE ON", 3.2f);
            Flash = 0.9f;
        }
        else
        {
            if (e.Elite) AddMicroHitStop(0.026f);
            float drop = FxRng.NextFloat();
            if (drop < 0.055f * Mods.PickupRate) SpawnPickup(e.Pos, PickupKind.Health, 1, 0.4f);
            else if (drop < 0.24f * Mods.PickupRate) SpawnPickup(e.Pos, PickupKind.Resonance, 1, 0.55f);
            else if (e.Elite) SpawnPickup(e.Pos, PickupKind.Score, 1, e.Hue);
        }
    }

    private void SpawnChild(EnemyRole role, Vector2 pos)
    {
        SpawnEnemyAt(role, pos, false);
        var child = Enemies[^1];
        child.Spawning = false;
        child.Hp *= 0.55f;
        child.MaxHp = child.Hp;
        child.Radius *= 0.8f;
        child.FireBeats = Rng.Range(1.5f, 3f);
    }

    private void DamageObstacle(Obstacle o, float damage)
    {
        if (o.Dead) return;
        o.Hp -= damage;
        o.HitFlash = 1f;
        o.RegenDelay = 3.2f;
        if (o.Hp <= 0) ShatterObstacle(o);
    }

    private void ShatterObstacle(Obstacle o)
    {
        if (o.Dead) return;
        o.Dead = true;
        Shatters++;
        AddCombo(1);
        AddScore(180, o.Pos, "SHATTER", new Vector4(0.8f, 0.95f, 1f, 1f));
        Sfx?.Trigger(SfxType.Explosion, 0.4f);
        Cam.AddShake(4.5f);
        BurstParticles(o.Pos, M.Hsv(o.Hue, 0.55f, 1f, 0.75f), 36, 300, 3f, 0.9f, ParticleKind.Shard);
        AddShockwave(o.Pos, o.Radius * 5.5f, 0.65f, 3f, M.Hsv(o.Hue, 0.6f, 1f, 0.8f));
        if (Rules.ShatterBurst)
        {
            FireRing(o.Pos, 6, 190f, BulletKind.Shard, o.Hue, o.Radius * 0.7f);
        }
        if (FxRng.Chance(0.16f * Mods.PickupRate))
            SpawnPickup(o.Pos, PickupKind.Resonance, 1, o.Hue);
    }

    private void UpdateObstacles(float dt, AudioState audio)
    {
        foreach (var o in Obstacles)
        {
            if (o.Dead) continue;
            o.Age += dt;
            o.Rotation += o.RotVel * dt;
            o.HitFlash = MathF.Max(0, o.HitFlash - dt * 3.5f);
            o.Grow = M.ExpSmooth(o.Grow, 1f + Bass * 0.055f + BeatPulse * 0.02f, 7f, dt);
            o.Phase += dt * (1.1f + Energy);
            if (o.CanRegrow)
            {
                o.RegenDelay -= dt;
                if (o.RegenDelay <= 0f && o.Hp < o.MaxHp)
                    o.Hp = MathF.Min(o.MaxHp, o.Hp + o.MaxHp * 0.08f * dt * Mods.ObstacleRate);
            }
            if (o.CanFire && Lasers.Count < 18 && M.Dist(o.Pos, Player.Pos) < 1050f)
            {
                if (BeatTick)
                {
                    o.FireBeats -= 1f;
                    if (o.FireBeats <= 0f)
                    {
                        _laserSource = "PYLON BEAM";
                        SpawnLaser(o.Pos, Player.Pos, 0.95f, 0.45f, 20f, o.Hue, 0f, false);
                        _laserSource = null;
                        o.FireBeats = Rng.Range(4f, 7f) / MathF.Max(0.6f, Mods.ObstacleRate * (Rules.DoubleTime ? 1.3f : 1f));
                        o.RegenDelay = 0f;
                    }
                }
            }
        }
    }
}
