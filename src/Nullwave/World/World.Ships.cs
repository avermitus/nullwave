using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;

namespace Nullwave.World;

/// <summary>The ship you pick before a run: each fires from a different part of the music.</summary>
public enum ShipKind
{
    /// <summary>Steady auto-fire (the original ship).</summary>
    Classic,
    /// <summary>Fires on drum hits: kick = heavy slug, snare = fan, hi-hat = needle.</summary>
    Drums,
    /// <summary>Lobs slow heavy orbs on the beat and on bass notes; they burst on impact.</summary>
    Bass,
    /// <summary>A continuous beam that bends with the melody and cuts through everything.</summary>
    Lead,
}

public static class Ships
{
    public static readonly ShipKind[] All = [ShipKind.Classic, ShipKind.Drums, ShipKind.Bass, ShipKind.Lead];

    public static string Name(ShipKind k) => k switch
    {
        ShipKind.Drums => "DRUMS",
        ShipKind.Bass => "BASS",
        ShipKind.Lead => "LEAD",
        _ => "CLASSIC",
    };

    public static string Blurb(ShipKind k) => k switch
    {
        ShipKind.Drums => "FIRES ON DRUM HITS // KICK SLUGS, SNARE FANS, HAT NEEDLES",
        ShipKind.Bass => "LOBS HEAVY ORBS ON THE BEAT AND ON BASS NOTES // THEY BURST",
        ShipKind.Lead => "A BEAM THAT BENDS WITH THE MELODY AND CUTS THROUGH EVERYTHING",
        _ => "STEADY AUTO-FIRE",
    };

    public static ShipKind Parse(string? s) =>
        Enum.TryParse<ShipKind>(s ?? "", true, out var k) ? k : ShipKind.Classic;
}

/// <summary>Per-shot tweaks so instrument ships can reuse the classic volley (and every attunement).</summary>
public readonly record struct ShotStyle(float DamageScale = 1f, float SpeedScale = 1f, float RadiusScale = 1f,
    int ExtraPierce = 0, float AngleOffset = 0f, byte Kind = 0, float Hue = 0.51f, bool Quiet = false)
{
    /// <summary>The original volley (use this, not default(ShotStyle), which is all zeros).</summary>
    public static ShotStyle Classic => new(1f);
}

public sealed partial class World
{
    public ShipKind Ship => Config.Ship;
    /// <summary>Lead ship: the beam's points this frame (empty when not firing).</summary>
    public readonly List<Vector2> LeadBeam = new();
    public float LeadBeamHue { get; private set; } = 0.55f;
    public float LeadBeamPower { get; private set; }
    private float _leadCenter = 64f;
    private double _lastOnsetClock = -1;
    private float _drumSilence;
    private float _bassOrbCooldown;

    /// <summary>Fires the selected ship's weapon for this step.</summary>
    private void FireWeapon(InputFrame input, AudioState audio, float dt)
    {
        var p = Player;
        LeadBeam.Clear();
        LeadBeamPower = 0f;
        // onsets are one-frame flags from the audio thread; the sim can step several times per
        // frame, so only the first step of each audio frame acts on them
        bool freshAudio = audio.Time != _lastOnsetClock;
        _lastOnsetClock = audio.Time;
        switch (Ship)
        {
            case ShipKind.Drums:
                if (!input.Fire) break;
                bool hit = false;
                if (freshAudio && audio.KickHit) { hit = true; Shoot(new ShotStyle(2.6f, 0.92f, 1.7f, 1, 0f, 0, 0.08f)); }
                if (freshAudio && audio.SnareHit)
                {
                    hit = true;
                    for (int k = -1; k <= 1; k++) Shoot(new ShotStyle(0.9f, 1f, 1.1f, 0, k * 0.2f, 0, 0.92f, k != 0));
                }
                if (freshAudio && audio.HatHit) { hit = true; Shoot(new ShotStyle(0.5f, 1.35f, 0.7f, 0, 0f, 0, 0.55f)); }
                // no drums to play along with (quiet passage, no analysis): keep a light eighth-note pulse
                _drumSilence = hit ? 0f : _drumSilence + dt;
                if (_drumSilence > 0.6f && EighthTick && freshAudio) Shoot(new ShotStyle(0.8f, 1.1f, 0.9f));
                break;
            case ShipKind.Bass:
                _bassOrbCooldown -= dt;
                if (!input.Fire) break;
                bool bassNote = freshAudio && audio.ChordChanged && audio.HarmLow > 0.3f;
                if ((BeatTick || bassNote) && _bassOrbCooldown <= 0f)
                {
                    _bassOrbCooldown = 0.18f;
                    float weight = 0.7f + M.Clamp01(audio.HarmLow) * 0.6f;
                    Shoot(new ShotStyle(4.5f * weight, 0.34f, 2.6f * weight, 0, 0f, 4, 0.05f));
                }
                break;
            case ShipKind.Lead:
                if (input.Fire) FireLeadBeam(audio, dt);
                break;
            default:
                if (input.Fire && p.FireTimer <= 0) Shoot(ShotStyle.Classic);
                break;
        }
    }

    /// <summary>
    /// The lead beam: a 900-unit ribbon from the ship along the aim that curves with the melody's
    /// pitch (above the tune's centre bends it one way, below the other) and damages everything it touches.
    /// </summary>
    private void FireLeadBeam(AudioState audio, float dt)
    {
        var p = Player;
        bool melody = audio.MelodyConfidence > 0.3f;
        if (melody) _leadCenter = M.ExpSmooth(_leadCenter, audio.MelodyPitch, 0.6f, dt);
        float bend = melody ? M.Clamp((audio.MelodyPitch - _leadCenter) / 7f, -1f, 1f) * 0.9f : MathF.Sin(Time * 1.3f) * 0.15f;
        LeadBeamHue = melody ? ((MathF.Round(audio.MelodyPitch) % 12f) + 12f) % 12f / 12f : 0.55f;
        LeadBeamPower = melody ? 1f : 0.7f;
        const int segments = 18;
        const float length = 900f;
        float a = M.AngleOf(p.AimDir);
        Vector2 pos = p.Pos + p.AimDir * 20f;
        LeadBeam.Add(pos);
        for (int i = 0; i < segments; i++)
        {
            a += bend / segments;
            pos += M.FromAngle(a, length / segments);
            LeadBeam.Add(pos);
        }
        // ~85 % of the classic ship's damage per second, to every enemy the beam crosses
        float dps = p.Damage * 16f * 0.85f * LeadBeamPower * OverdriveDamageScale / 1f;
        float width = 16f + p.HeavyLevel * 3f;
        int count = Enemies.Count;
        for (int k = 0; k < count; k++)
        {
            var e = Enemies[k];
            if (e.Dead || e.Spawning) continue;
            for (int i = 1; i < LeadBeam.Count; i++)
            {
                if (M.DistanceToSegment(e.Pos, LeadBeam[i - 1], LeadBeam[i]) < width + e.Radius * 0.8f)
                {
                    DamageEnemy(e, dps * dt, null);
                    if (FxRng.Chance(0.25f)) SpawnParticle(e.Pos, FxRng.InCircle(90f), 0.18f, 2.4f, M.Hsv(LeadBeamHue, 0.6f, 1f, 0.8f), ParticleKind.Spark, 3f);
                    break;
                }
            }
        }
        if (FxRng.Chance(dt * 9f)) Sfx?.Trigger(SfxType.Shot, 0.06f, 0f);
    }

    /// <summary>Bass orbs burst where they land (or when they run out).</summary>
    private void BassBurst(Vector2 at, float damage)
    {
        const float radius = 135f;
        int count = Enemies.Count;
        for (int k = 0; k < count; k++)
        {
            var e = Enemies[k];
            if (e.Dead || e.Spawning) continue;
            if (M.DistSq(e.Pos, at) < (radius + e.Radius) * (radius + e.Radius)) DamageEnemy(e, damage * 0.9f, at);
        }
        AddShockwave(at, radius * 1.3f, 0.45f, 8f, new Vector4(1f, 0.55f, 0.25f, 1f));
        BurstParticles(at, new Vector4(1f, 0.6f, 0.3f, 0.85f), 18, 320f, 2.6f, 0.5f, ParticleKind.Spark);
        Cam.AddShake(3f);
        Sfx?.Trigger(SfxType.Explosion, 0.22f);
    }
}
