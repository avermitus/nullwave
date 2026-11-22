using System.Numerics;
using Nullwave.Core;

namespace Nullwave.World;

public enum GamePhase
{
    Playing,
    Attuning,
    Intermission,
    Dead,
}

public enum EnemyRole
{
    Drifter,
    Orbiter,
    Sniper,
    Dasher,
    Turret,
    Splitter,
    Swarm,
    Boss,
    /// <summary>Snakes toward the player and drops slow drifting mines on the beat.</summary>
    Weaver,
    /// <summary>Lobs a telegraphed strike at the player on the downbeat; it detonates two beats later.</summary>
    Mortar,
    /// <summary>Teleports beside the player every other beat, then fires.</summary>
    Phantom,
    /// <summary>Projects a short rotating laser blade around itself.</summary>
    Prism,
}

/// <summary>Telegraphed area strike (mortar): a ring closes on the target, then it detonates.</summary>
public sealed class Strike
{
    public Vector2 Pos;
    public float Radius = 95f;
    public float Timer;
    public float Duration;
    public float Hue;
    public bool Done;
}

public enum BulletKind
{
    Orb,
    Needle,
    Shard,
    Player,
}

public enum ParticleKind
{
    Dot,
    Spark,
    Shard,
    Ring,
}

public enum PickupKind
{
    Health,
    Resonance,
    Score,
    Core,
}

public sealed class Player
{
    public Vector2 Pos;
    public Vector2 PrevPos;
    public Vector2 Vel;
    public Vector2 AimDir = new(0, -1);
    public int Health = 3;
    public int MaxHealth = 3;
    public float Invuln;
    public float DashTime;
    public float DashCooldown;
    public float DashCooldownBase = 0.82f;
    public bool IsDashing;
    public Vector2 DashDir;
    public float Resonance;
    public float ResonanceMax = 100f;
    public float FireTimer;
    public float Radius = 11f;
    public float HitFlash;
    public float Focus;
    /// <summary>Gravity-well slingshot: charge while skimming a well, boost (0..1) after leaving it.</summary>
    public float SlingCharge, SlingBoost;
    /// <summary>Melody rail: riding right now, boost (0..1), total seconds ridden.</summary>
    public bool RailRiding;
    public float RailBoost, RailTime;
    public float TrailTimer;
    public float Time;
    public bool InvincibleDemo;
    public int PhaseSyncs;
    public int PhaseSyncStreak;
    public float PhaseSyncFlash;
    public float LastPhaseSyncErrorMs;
    public float SyncOverdriveTimer;
    public bool IsSyncOverdrive => SyncOverdriveTimer > 0f;
    public int ShotCounter;
    public SynergyFlags ActiveSynergies;
    public float VoidPhantomFlash;
    /// <summary>Smoothed health shown by the HUD bar (lags behind real health to show the chunk lost).</summary>
    public float HealthDisplay = 3f;
    public float Bank;
    public int SplitLevel, FanLevel, PierceLevel, RapidLevel, HeavyLevel, LongLevel;
    public int HomingLevel, RicochetLevel, RearLevel, SiphonLevel, PhaseLevel, GrazerLevel;

    public float FireInterval => 0.105f / (1f + RapidLevel * 0.14f);
    public float Damage => 8f * (1f + HeavyLevel * 0.32f);
    public float BulletRadius => 3.4f + HeavyLevel * 0.7f;
    public float BulletLife => 0.95f * (1f + LongLevel * 0.22f);
    public float BulletSpeed => 1550f * (1f - HeavyLevel * 0.035f);
    /// <summary>Top cruising speed. The ship is a racer: fast, with momentum.</summary>
    public const float TopSpeed = 560f;
    public float SpeedFrac => Vel.Length() / TopSpeed;
    public float DashCooldownMax => (DashCooldownBase * (1f - PhaseLevel * 0.10f));
    public float TotalResonanceGain => (1f + GrazerLevel * 0.25f);
    public int AttunementCount => SplitLevel + FanLevel + PierceLevel + RapidLevel + HeavyLevel + LongLevel + HomingLevel + RicochetLevel + RearLevel + SiphonLevel + PhaseLevel + GrazerLevel;
}

public sealed class Enemy
{
    /// <summary>Maps: whether the enemy can run straight at the player, and when to look again.</summary>
    public bool NavSight = true;
    public float NavCheck;
    public EnemyRole Role;
    public Vector2 Pos;
    public Vector2 Vel;
    public Vector2 SpawnPos;
    public float Radius = 18f;
    public float Hp = 20f;
    public float MaxHp = 20f;
    public float Hue = 0.55f;
    public float Angle;
    public float AngleVel;
    public float Rot;
    public float RotVel;
    public int ShapeSides = 5;
    public Vector2[] Shape = [];
    public bool Boss;
    public bool Elite;
    public bool Spawning = true;
    public float SpawnTimer = 0.8f;
    public float SpawnDuration = 0.8f;
    public float Age;
    public float HitFlash;
    public float FireBeats;
    public float FireIntervalBeats = 1f;
    public float StateTimer;
    public int State;
    public Vector2 DashDir;
    public bool Dead;
    public bool MarkedForRemoval;
    public float ContactCooldown;
    public int Tier;
    public int BossPhase;
    public int BossPhaseCount = 3;
    public int HighestTriggeredPhase;
    public float BossInvuln;
    public float BossAttackBeats;
    public float BossScale = 1f;
    public float PhaseBreakTimer;
    public float PhaseBreakDuration = 1.15f;
    public float BossHealthDisplay = 1f;
    public float DeathPulse;
    public bool DroppedCore;
    public int SplitChildren;
    /// <summary>0..1 ramp over the last beat before this enemy fires (drives the visual telegraph).</summary>
    public float Telegraph;
    /// <summary>Smoothed heading toward the player, used by aiming models.</summary>
    public float Facing;
    /// <summary>Muzzle flash, set to 1 when firing and decays.</summary>
    public float Muzzle;
    /// <summary>Bitmask over the 8 eighth-notes of a bar on which this enemy is allowed to fire.</summary>
    public byte Rhythm = 0xFF;
    /// <summary>THE DROP: a boss built from the song (otherwise the boss is THE CHOIR).</summary>
    public bool SongBoss;
    /// <summary>Per-enemy phase (0..1): handedness and offset of its movement style.</summary>
    public float MovePhase;
    /// <summary>1 on the enemy's own part of the beat, decaying to 0 (drives its movement surge and glow).</summary>
    public float Groove;
    public bool IsBoss => Boss;

    public float HealthFrac => MaxHp <= 0 ? 0 : M.Clamp01(Hp / MaxHp);
}

public sealed class Bullet
{
    public bool FromPlayer;
    public BulletKind Kind;
    public Vector2 Pos;
    public Vector2 PrevPos;
    public Vector2 Vel;
    public float Radius;
    public float Life;
    public float MaxLife;
    public float Damage;
    public float Hue;
    public bool Grazed;
    public int Pierce;
    public int Bounces;
    public float Homing;
    public float AngularVel;
    public float Age;
    public float WaveAmp;
    public float WaveFreq;
    public float WavePhase;
    public bool Glow = true;
    /// <summary>Surge bullets lurch forward on every beat.</summary>
    public bool BeatSurge;
    public float BaseSpeed;
    public float Surge;
    /// <summary>0 = normal, 1 = SeekerSalvo, 2 = PrismLance (supercharged), 3 = RailCadence.</summary>
    public byte SynergyKind;
    /// <summary>Crossed a melody rail: wears the rail's colour and passes through a ship riding it.</summary>
    public bool Tuned;

    public void Reset()
    {
        FromPlayer = false;
        Kind = BulletKind.Orb;
        Pos = Vector2.Zero;
        PrevPos = Vector2.Zero;
        Vel = Vector2.Zero;
        Radius = 5;
        Life = 7;
        MaxLife = 7;
        Damage = 1;
        Hue = 0.55f;
        Grazed = false;
        Pierce = 0;
        Bounces = 0;
        Homing = 0;
        AngularVel = 0;
        Age = 0;
        WaveAmp = 0;
        WaveFreq = 0;
        WavePhase = 0;
        Glow = true;
        BeatSurge = false;
        BaseSpeed = 0;
        Surge = 0;
        SynergyKind = 0;
        Tuned = false;
    }
}

public sealed class LaserBeam
{
    public Vector2 Origin;
    public float Angle;
    public float Length = 1800;
    public float Width = 22;
    public float Warmup = 0.85f;
    public float Duration = 0.6f;
    public float Age;
    public float Sweep;
    public float Hue;
    public bool HitPlayer;
    public bool PlayedFire;
    public bool BossLaser;
    public bool Done;
    public float Fade = 0.16f;

    public bool Active => Age >= Warmup && Age < Warmup + Duration;
    public Vector2 Direction => M.FromAngle(Angle);
    public Vector2 End => Origin + Direction * Length;
    /// <summary>
    /// The part of the beam that is not behind a wall, as distances along it (recomputed every
    /// step; in the open arena the whole beam). <see cref="Length"/> stays the requested length so
    /// a rotating beam that was cut short can reach out again once it swings clear.
    /// </summary>
    public float ClipStart;
    public float ClipEnd = -1f;
    public float BeamLength => ClipEnd < 0f ? Length : MathF.Max(0f, ClipEnd - ClipStart);
    public Vector2 BeamStart => ClipEnd < 0f ? Origin : Origin + Direction * ClipStart;
    public Vector2 BeamEnd => BeamStart + Direction * BeamLength;
    public float Alpha
    {
        get
        {
            if (Age < Warmup)
            {
                float t = Age / MathF.Max(0.01f, Warmup);
                return 0.25f + 0.55f * t;
            }
            if (Age < Warmup + Duration) return 1f;
            return M.Clamp01(1f - (Age - Warmup - Duration) / MathF.Max(0.01f, Fade));
        }
    }
}

public sealed class Obstacle
{
    public Vector2 Pos;
    public float Radius = 100f;
    public float Hue;
    public float Rotation;
    public float RotVel;
    public int Sides = 6;
    public Vector2[] Shape = [];
    public float Hp = 120f;
    public float MaxHp = 120f;
    public float HitFlash;
    public float Age;
    public float FireBeats;
    public float Grow = 1f;
    public float RegenDelay;
    public bool Dead;
    public bool CanRegrow;
    public bool CanFire;
    public float Phase;
}

public sealed class Particle
{
    public ParticleKind Kind;
    public Vector2 Pos;
    public Vector2 PrevPos;
    public Vector2 Vel;
    public float Life;
    public float MaxLife;
    public float Size;
    public float EndSize;
    public Vector4 Color;
    public float Drag;
    public float Rot;
    public float RotVel;
}

public sealed class Shockwave
{
    public Vector2 Pos;
    public float Radius;
    public float MaxRadius;
    public float Life;
    public float MaxLife;
    public float Width;
    public Vector4 Color;
    public bool Filled;
    public bool Heavy;
    /// <summary>Set once the 3D backdrop has been sent a matching shockwave.</summary>
    public bool BackdropPulsed;
}

public sealed class Pickup
{
    public PickupKind Kind;
    public Vector2 Pos;
    public Vector2 Vel;
    public float Life;
    public float Phase;
    public float Value;
    public float Hue;
}

public sealed class FloatText
{
    public string Text = "";
    public Vector2 Pos;
    public Vector2 Vel;
    public float Life;
    public float MaxLife;
    public Vector4 Color;
    public float Size;
}

public sealed class Camera
{
    public Vector2 Position;
    public Vector2 ShakeOffset;
    public float Zoom = 1f;
    public float Shake;
    public float BaseZoom = 1.0f;
    /// <summary>0..1 how fast the target is moving: the camera leads further and pulls back at speed.</summary>
    public float SpeedFrac;
    private readonly Rng _rng = new(0xCAFE1234);

    public void Update(Vector2 target, Vector2 velocity, Vector2 aim, bool dashing, float energy, Vector2 arena, float dt, bool dead = false)
    {
        SpeedFrac = M.Clamp01(velocity.Length() / 560f);
        // racing camera: look well ahead along the velocity so you see what you are flying into
        Vector2 desired = target + velocity * 0.24f + aim * 30f;
        desired.X = M.Clamp(desired.X, -arena.X * 0.5f, arena.X * 0.5f);
        desired.Y = M.Clamp(desired.Y, -arena.Y * 0.5f, arena.Y * 0.5f);
        Position = M.Lerp(Position, desired, 1f - MathF.Exp(-dt * 4.2f));
        float zTarget = BaseZoom - energy * 0.03f - SpeedFrac * 0.07f + (dashing ? -0.04f : 0f) + (dead ? 0.06f : 0f);
        Zoom = M.ExpSmooth(Zoom, M.Clamp(zTarget, 0.82f, 1.1f), 2.4f, dt);
        Shake = MathF.Max(0, Shake - dt * (18f + Shake * 2.4f));
        float amp = Shake;
        ShakeOffset = amp <= 0.01f ? Vector2.Zero : new Vector2(_rng.Signed(), _rng.Signed()) * amp;
    }

    public void AddShake(float amount)
    {
        float scale = Nullwave.Core.GameSettings.Current.ScreenShake / 100f;
        // the cap grows with SCREEN SHAKE above 150 % so big hits keep getting bigger
        Shake = MathF.Min(28f * MathF.Max(1f, scale / 1.5f), Shake + amount * scale);
    }

    public void Snap(Vector2 target) => Position = target;
}
