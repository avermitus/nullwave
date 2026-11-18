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
    public float TrailTimer;
    public float Time;
    public bool InvincibleDemo;
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
    public float BossInvuln;
    public float BossAttackBeats;
    public float BossScale = 1f;
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

    public void AddShake(float amount) => Shake = MathF.Min(28f, Shake + amount * Nullwave.Core.GameSettings.Current.ScreenShake / 100f);

    public void Snap(Vector2 target) => Position = target;
}
