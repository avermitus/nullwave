using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;

namespace Nullwave.World;

/// <summary>
/// THE DROP: a boss built from the song. Its kick slams out rings, its snare fires fans, the hats
/// spit needles, a laser traces the melody, and every new section of the song is a new phase
/// (a returning chorus brings back the same phase).
/// </summary>
public sealed partial class World
{
    private LaserBeam? _melodyLaser;
    private float _songBossCenter = 64f;
    private double _songBossAudioClock = -1;
    private int _songBossHat;

    /// <summary>Boss bar title for whichever boss is up.</summary>
    public string BossTitle(Enemy boss, int phases)
    {
        if (!boss.SongBoss)
            return "THE CHOIR // PHASE " + RomanNumeral(boss.BossPhase + 1) + " OF " + RomanNumeral(phases) + " // " + BossPhaseSubtitle(boss.BossPhase, phases);
        string track = CurrentTrack?.Title ?? "";
        if (track.Length > 28) track = track[..28] + "...";
        return "THE DROP // " + (track.Length > 0 ? track + " // " : "") + "SECTION " + RomanNumeral(boss.BossPhase + 1);
    }

    private static string RomanNumeral(int n) => n switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", _ => n.ToString(),
    };

    /// <summary>Spawns THE DROP near the player (the boss on every 10th wave).</summary>
    public void SpawnSongBoss()
    {
        if (BossEnemy is { Dead: false }) return;
        Vector2 pos = ClampToArena(Player.Pos + new Vector2(0f, -520f), 300f);
        SpawnEnemyAt(EnemyRole.Boss, pos, true);
        var e = Enemies[^1];
        e.SongBoss = true;
        e.Hue = 0.86f;
        e.Hp *= 0.9f;
        e.MaxHp = e.Hp;
        _songBossHat = 0;
        EmitEvent("THE DROP // IT PLAYS THE SONG", 3.0f);
    }

    private void UpdateSongBoss(Enemy e, float dt, AudioState audio)
    {
        if (e.BossInvuln > 0f) e.BossInvuln = MathF.Max(0f, e.BossInvuln - dt);
        if (e.PhaseBreakTimer > 0f) e.PhaseBreakTimer = MathF.Max(0f, e.PhaseBreakTimer - dt);
        float frac = e.HealthFrac;
        e.BossHealthDisplay = e.BossHealthDisplay > frac ? MathF.Max(frac, e.BossHealthDisplay - dt * 0.42f) : frac;
        e.Angle += dt * (0.3f + audio.Energy * 0.6f);
        e.Rot -= dt * 0.8f;

        // hovers on a slow orbit around you, always on screen
        float orbit = Time * 0.22f;
        Vector2 target = ClampToArena(Player.Pos + new Vector2(MathF.Sin(orbit) * 420f, -260f + MathF.Cos(orbit * 1.3f) * 90f), 240f);
        e.Vel = (target - e.Pos) * 1.2f;
        e.Pos = M.Lerp(e.Pos, target, 1f - MathF.Exp(-dt * 1.6f));
        e.BossScale = 1f + audio.PercLow * 0.06f + audio.KickPulse * 0.05f;

        // each new section of the song is a new phase; a returning section brings its phase back
        if (audio.SectionChanged && e.BossInvuln <= 0f)
        {
            e.BossPhase = Math.Abs(audio.SectionLabel) % 4;
            e.BossInvuln = 1.0f;
            e.PhaseBreakTimer = e.PhaseBreakDuration;
            e.Hue = (0.86f + e.BossPhase * 0.17f) % 1f;
            for (int i = EnemyBullets.Count - 1; i >= 0; i--)
                if (M.DistSq(EnemyBullets[i].Pos, e.Pos) < 600f * 600f) ReturnBullet(EnemyBullets, i);
            AddShockwave(e.Pos, 900f, 1.2f, 18f, M.Hsv(e.Hue, 0.6f, 1f, 1f), filled: true, heavy: true);
            EmitEvent("THE DROP // SECTION " + RomanNumeral(e.BossPhase + 1), 1.8f);
            Sfx?.Trigger(SfxType.Boss, 0.6f);
        }

        bool fresh = audio.Time != _songBossAudioClock;
        _songBossAudioClock = audio.Time;
        bool canFire = e.BossInvuln <= 0f && EnemyBullets.Count < 2200;
        float bulletSpeed = 175f * GetDifficultyBulletScale();
        Vector2 toPlayer = M.SafeNorm(Player.Pos - e.Pos);
        float aim = M.AngleOf(toPlayer);
        bool anyOnset = false;
        if (canFire && fresh)
        {
            // kick: a ring slams out (denser in later phases), rotated each time so gaps move
            if (audio.KickHit)
            {
                anyOnset = true;
                int n = 12 + e.BossPhase * 2;
                FireRing(e.Pos, n, bulletSpeed * 0.95f, BulletKind.Orb, e.Hue, e.Radius * 0.9f);
                Cam.AddShake(2f);
            }
            // snare: a fan aimed at you
            if (audio.SnareHit)
            {
                anyOnset = true;
                SpawnFan(e.Pos + toPlayer * e.Radius, aim, 5 + (e.BossPhase >= 2 ? 2 : 0), 0.5f, bulletSpeed * 1.25f, BulletKind.Needle, (e.Hue + 0.5f) % 1f);
            }
            // hats: needles flicked out on every other hat
            if (audio.HatHit && (++_songBossHat % 2 == 0))
            {
                anyOnset = true;
                float a = Rng.Range(0f, M.Tau);
                for (int k = 0; k < 2; k++)
                    SpawnEnemyBullet(e.Pos + M.FromAngle(a + k * MathF.PI, e.Radius), M.FromAngle(a + k * MathF.PI, bulletSpeed * 1.6f), 3.6f, (e.Hue + 0.25f) % 1f, BulletKind.Needle, 4f);
            }
        }
        // no onsets to play (quiet or unanalysed audio): fall back to the beat grid
        if (canFire && !anyOnset && BeatTick && audio.PercLow < 0.05f && audio.KickPulse < 0.05f)
            FireRing(e.Pos, 10, bulletSpeed * 0.9f, BulletKind.Orb, e.Hue, e.Radius * 0.9f);

        // the melody laser: sweeps with the tune's pitch while a melody is playing
        bool melody = audio.MelodyConfidence > 0.35f && e.BossInvuln <= 0f;
        if (melody)
        {
            _songBossCenter = M.ExpSmooth(_songBossCenter, audio.MelodyPitch, 0.5f, dt);
            float offset = M.Clamp((audio.MelodyPitch - _songBossCenter) / 6f, -1f, 1f) * 1.1f;
            if (_melodyLaser == null || _melodyLaser.Done || !Lasers.Contains(_melodyLaser))
            {
                _laserSource = "THE DROP";
                SpawnLaser(e.Pos, Player.Pos, 0.9f, 60f, 18f, (e.Hue + 0.12f) % 1f, 0f, true, 1100f);
                _laserSource = null;
                _melodyLaser = Lasers[^1];
            }
            _melodyLaser.Origin = e.Pos;
            _melodyLaser.Angle = M.MoveTowardsAngle(_melodyLaser.Angle, aim + offset, dt * 1.6f);
        }
        else if (_melodyLaser != null)
        {
            // end it gracefully
            _melodyLaser.Age = MathF.Max(_melodyLaser.Age, _melodyLaser.Warmup + _melodyLaser.Duration - _melodyLaser.Fade);
            _melodyLaser = null;
        }
        e.Telegraph = 0.4f + audio.PercLow * 0.6f;
    }

    /// <summary>Called when THE DROP dies: its melody laser stops.</summary>
    private void OnSongBossKilled(Enemy e)
    {
        if (_melodyLaser != null)
        {
            _melodyLaser.Age = _melodyLaser.Warmup + _melodyLaser.Duration;
            _melodyLaser = null;
        }
        EmitEvent("THE DROP // SILENCED", 3.0f);
    }
}
