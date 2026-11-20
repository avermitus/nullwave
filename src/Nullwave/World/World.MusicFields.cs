using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;

namespace Nullwave.World;

/// <summary>A bass note made solid: bends and swallows bullets, slingshots the ship, detonates on a drop.</summary>
public sealed class GravityWell
{
    public Vector2 Pos;
    public float Strength;      // 0..1, follows the bass notes
    public float Age;
    public float Hue;
    public float Quiet;         // seconds the bass has been gone
    public bool Dying;
    public float Spin;          // visual accretion spin
    public float Radius => 260f + 170f * Strength;
    public float Core => 26f + 22f * Strength;
}

/// <summary>A rail drawn across the map by the melody: each point carries the note's colour.</summary>
public sealed class MelodyRail
{
    public readonly List<Vector2> Points = new();
    public readonly List<float> Ages = new();
    public readonly List<float> Hues = new();
    public Vector2 Head;
    public float Heading;
    public float Center;        // running centre of the melody's pitch (MIDI)
    public bool Growing = true;
    public float Silence;
    public float Length;
    public Vector2 Min, Max;    // bounding box for quick rejects
}

public sealed partial class World
{
    public readonly List<GravityWell> Wells = new();
    public readonly List<MelodyRail> Rails = new();
    public const int MaxWells = 3;
    public const int MaxRails = 2;
    public const float RailPointSpacing = 34f;
    public const float RailHeadSpeed = 820f;
    public const float RailLife = 5.5f;
    public const float RailRideDistance = 48f;
    private float _wellCooldown;
    private GravityWell? _slingWell;   // the well the ship is skimming
    private float _bassHeld;

    /// <summary>
    /// Bass Gravity Wells: a sustained bass note opens a well ahead of you; it grows with the bass
    /// and fades when the bass stops. Bullets bend around it and are swallowed by its core; the
    /// ship can slingshot round it for a burst of speed. A drop detonates every well.
    /// </summary>
    private void UpdateWells(float dt, AudioState audio)
    {
        _wellCooldown -= dt;
        bool bassNote = audio.HarmLow > 0.5f && audio.Rms > 0.01f;
        _bassHeld = bassNote ? _bassHeld + dt : MathF.Max(0f, _bassHeld - dt * 2f);
        bool newNote = audio.ChordChanged || _bassHeld > 1.2f;
        if (newNote && bassNote && _wellCooldown <= 0f && Wells.Count < MaxWells)
        {
            if (TrySpawnWell(audio)) { _wellCooldown = 2.5f; _bassHeld = 0f; }
        }

        for (int i = Wells.Count - 1; i >= 0; i--)
        {
            var w = Wells[i];
            w.Age += dt;
            w.Quiet = bassNote ? 0f : w.Quiet + dt;
            if (w.Age > 10f || (w.Age > 2f && w.Quiet > 1.5f)) w.Dying = true;
            float target = w.Dying ? 0f : 0.25f + M.Clamp01(audio.HarmLow) * 0.75f;
            w.Strength = M.ExpSmooth(w.Strength, target, target > w.Strength ? 2.5f : 1.4f, dt);
            w.Spin += dt * (1.5f + w.Strength * 4f);
            if (w.Dying && w.Strength < 0.04f)
            {
                if (_slingWell == w) { _slingWell = null; Player.SlingCharge = 0f; }
                Wells.RemoveAt(i);
                continue;
            }
            ApplyWell(w, dt);
        }
        Player.SlingBoost = MathF.Max(0f, Player.SlingBoost - dt * 0.6f);
    }

    private bool TrySpawnWell(AudioState audio)
    {
        Vector2 heading = Player.Vel.LengthSquared() > 150f * 150f ? M.SafeNorm(Player.Vel) : Player.AimDir;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 p = Player.Pos + heading * Rng.Range(380f, 600f) + M.Perp(heading) * Rng.Range(-280f, 280f);
            p = ClampToArena(p, 260f);
            if (M.Dist(p, Player.Pos) < 260f) continue;
            bool clear = true;
            foreach (var o in Wells) if (M.Dist(o.Pos, p) < 520f) { clear = false; break; }
            if (!clear) continue;
            float hue = audio.ChordRoot >= 0 ? audio.ChordRoot / 12f : Rng.NextFloat();
            Wells.Add(new GravityWell { Pos = p, Strength = 0.05f, Hue = hue });
            AddShockwave(p, 320f, 0.7f, 6f, M.Hsv(hue, 0.6f, 1f, 0.8f));
            return true;
        }
        return false;
    }

    /// <summary>Acceleration a well applies at a point (u/s²), capped so nothing whips unreadably.</summary>
    public static Vector2 WellPull(GravityWell w, Vector2 at, float scale = 1f)
    {
        Vector2 d = w.Pos - at;
        float l = d.Length();
        if (l < 1f || l > w.Radius) return Vector2.Zero;
        float fade = 1f - l / w.Radius;
        float a = MathF.Min(2400f, 4.0e7f * w.Strength / (l * l + 90f * 90f)) * fade * scale;
        return d / l * a;
    }

    private void ApplyWell(GravityWell w, float dt)
    {
        float core = w.Core;
        for (int i = EnemyBullets.Count - 1; i >= 0; i--)
        {
            var b = EnemyBullets[i];
            if (M.DistSq(b.Pos, w.Pos) > w.Radius * w.Radius) continue;
            if (M.DistSq(b.Pos, w.Pos) < core * core)
            {
                SpawnParticle(b.Pos, M.SafeNorm(w.Pos - b.Pos) * 60f, 0.25f, 2f, M.Hsv(w.Hue, 0.6f, 1f, 0.7f), ParticleKind.Spark, 2f);
                ReturnBullet(EnemyBullets, i);
                continue;
            }
            b.Vel += WellPull(w, b.Pos) * dt;
        }
        for (int i = PlayerBullets.Count - 1; i >= 0; i--)
        {
            var b = PlayerBullets[i];
            if (M.DistSq(b.Pos, w.Pos) > w.Radius * w.Radius) continue;
            b.Vel += WellPull(w, b.Pos, 0.45f) * dt;
        }

        // the ship: a gentle pull, and a slingshot for skimming the well's ring at speed
        var p = Player;
        Vector2 toWell = w.Pos - p.Pos;
        float dist = toWell.Length();
        if (dist < w.Radius && dist > 1f)
        {
            p.Vel += WellPull(w, p.Pos, 0.06f) * dt;   // a nudge, never a drag
            float speed = p.Vel.Length();
            float radial = MathF.Abs(Vector2.Dot(p.Vel, toWell / dist)) / MathF.Max(1f, speed);
            bool skimming = dist > w.Radius * 0.25f && dist < w.Radius * 0.85f && radial < 0.55f && speed > 300f;
            if (skimming && (_slingWell == null || _slingWell == w))
            {
                _slingWell = w;
                p.SlingCharge = MathF.Min(1f, p.SlingCharge + dt * (0.9f + w.Strength));
            }
        }
        if (_slingWell == w && p.SlingCharge > 0f && (dist > w.Radius * 0.85f || dist < w.Radius * 0.25f))
        {
            if (p.SlingCharge > 0.35f)
            {
                p.SlingBoost = MathF.Max(p.SlingBoost, p.SlingCharge);
                FlowGain(0.08f * p.SlingCharge);
                AddScore((int)(120 * p.SlingCharge), p.Pos + new Vector2(0, -40), "SLINGSHOT", M.Hsv(w.Hue, 0.5f, 1f, 1f));
                Sfx?.Trigger(SfxType.Dash, 0.35f);
            }
            p.SlingCharge = 0f;
            _slingWell = null;
        }
    }

    /// <summary>A drop detonates every well: a shockwave that clears bullets and hurts enemies around it.</summary>
    private void CollapseWells(float strength)
    {
        foreach (var w in Wells)
        {
            float r = 380f + 260f * w.Strength;
            AddShockwave(w.Pos, r * 1.4f, 1.0f, 16f, M.Hsv(w.Hue, 0.5f, 1f, 1f), filled: true, heavy: true);
            BurstParticles(w.Pos, M.Hsv(w.Hue, 0.6f, 1f, 0.9f), 70, 700f, 3f, 0.9f, ParticleKind.Spark);
            for (int i = EnemyBullets.Count - 1; i >= 0; i--)
                if (M.DistSq(EnemyBullets[i].Pos, w.Pos) < r * r) ReturnBullet(EnemyBullets, i);
            // index loop: a kill can split an enemy and add children mid-loop
            int count = Enemies.Count;
            for (int k = 0; k < count; k++)
            {
                var e = Enemies[k];
                if (e.Dead) continue;
                float d = M.Dist(e.Pos, w.Pos);
                if (d < r) DamageEnemy(e, (45f + 70f * w.Strength) * strength * (1f - d / r * 0.6f), w.Pos);
            }
        }
        if (Wells.Count > 0) EmitEvent("// GRAVITY COLLAPSE //", 1.6f);
        Wells.Clear();
        _slingWell = null;
        Player.SlingCharge = 0f;
    }

    /// <summary>
    /// Melody Rails: while a melody plays, a rail is drawn across the map ahead of you, curving up
    /// and down with the tune's pitch and coloured by its notes. Riding it boosts your speed;
    /// enemy bullets that cross it are tuned to it and pass harmlessly through you while you ride.
    /// </summary>
    private void UpdateRails(float dt, AudioState audio)
    {
        bool melody = audio.MelodyConfidence > 0.32f && audio.HarmMid > 0.25f && audio.Rms > 0.008f;
        MelodyRail? growing = null;
        foreach (var r in Rails) if (r.Growing) growing = r;
        if (growing == null && melody && Rails.Count < MaxRails) growing = StartRail(audio);

        for (int ri = Rails.Count - 1; ri >= 0; ri--)
        {
            var r = Rails[ri];
            for (int k = 0; k < r.Ages.Count; k++) r.Ages[k] += dt;
            while (r.Ages.Count > 0 && r.Ages[0] > RailLife)
            {
                r.Points.RemoveAt(0); r.Ages.RemoveAt(0); r.Hues.RemoveAt(0);
            }
            if (r.Growing)
            {
                r.Silence = melody ? 0f : r.Silence + dt;
                if (r.Silence > 0.4f || r.Length > 4200f) r.Growing = false;
                else GrowRail(r, dt, audio);
            }
            if (!r.Growing && r.Points.Count == 0) { Rails.RemoveAt(ri); continue; }
            UpdateRailBounds(r);
        }

        // riding: close to a rail and moving along it
        var p = Player;
        bool riding = false;
        float speed = p.Vel.Length();
        if (speed > 150f)
        {
            Vector2 v = p.Vel / speed;
            foreach (var r in Rails)
            {
                if (!InBounds(r, p.Pos, RailRideDistance)) continue;
                for (int k = 1; k < r.Points.Count; k++)
                {
                    Vector2 a = r.Points[k - 1], b = r.Points[k];
                    if (M.DistSq(SegmentClosest(a, b, p.Pos), p.Pos) > RailRideDistance * RailRideDistance) continue;
                    if (MathF.Abs(Vector2.Dot(v, M.SafeNorm(b - a))) > 0.55f) { riding = true; break; }
                }
                if (riding) break;
            }
        }
        p.RailRiding = riding;
        p.RailBoost = riding ? MathF.Min(1f, p.RailBoost + dt * 6f) : MathF.Max(0f, p.RailBoost - dt * 2f);
        if (riding) { FlowGain(0.12f * dt); p.RailTime += dt; }

        // tune enemy bullets that cross a rail to its colour (spatial hash: each bullet only looks
        // at rail points in its own and neighbouring 64-unit cells)
        foreach (var r in Rails)
        {
            if (r.Points.Count < 2) continue;
            _railCells.Clear();
            for (int k = 0; k < r.Points.Count; k++)
            {
                long key = CellKey(r.Points[k]);
                if (!_railCells.TryGetValue(key, out var list)) { list = RentCellList(); _railCells[key] = list; }
                list.Add(k);
            }
            foreach (var b in EnemyBullets)
            {
                if (b.Tuned || !InBounds(r, b.Pos, 30f)) continue;
                int cx = (int)MathF.Floor(b.Pos.X / RailCell), cy = (int)MathF.Floor(b.Pos.Y / RailCell);
                for (int dx = -1; dx <= 1 && !b.Tuned; dx++)
                    for (int dy = -1; dy <= 1 && !b.Tuned; dy++)
                    {
                        if (!_railCells.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cy + dy), out var list)) continue;
                        foreach (int k in list)
                            if (M.DistSq(r.Points[k], b.Pos) < 30f * 30f) { b.Tuned = true; b.Hue = r.Hues[k]; break; }
                    }
            }
            foreach (var list in _railCells.Values) { list.Clear(); _cellPool.Push(list); }
        }
    }

    private const float RailCell = 64f;
    private readonly Dictionary<long, List<int>> _railCells = new();
    private readonly Stack<List<int>> _cellPool = new();
    private List<int> RentCellList() => _cellPool.Count > 0 ? _cellPool.Pop() : new List<int>(8);
    private static long CellKey(Vector2 p) =>
        ((long)(int)MathF.Floor(p.X / RailCell) << 32) ^ (uint)(int)MathF.Floor(p.Y / RailCell);

    private MelodyRail StartRail(AudioState audio)
    {
        Vector2 heading = Player.Vel.LengthSquared() > 150f * 150f ? M.SafeNorm(Player.Vel) : Player.AimDir;
        var r = new MelodyRail
        {
            Head = ClampToArena(Player.Pos + heading * 140f, 120f),
            Heading = M.AngleOf(heading),
            Center = audio.MelodyPitch,
        };
        AddRailPoint(r, audio);
        Rails.Add(r);
        return r;
    }

    private void GrowRail(MelodyRail r, float dt, AudioState audio)
    {
        // pitch above the tune's centre turns the rail one way, below it the other
        r.Center = M.ExpSmooth(r.Center, audio.MelodyPitch, 0.5f, dt);
        float turn = M.Clamp((audio.MelodyPitch - r.Center) / 6f, -1f, 1f) * 1.7f;
        // keep the rail inside the arena
        float hw = ArenaW * 0.5f - 320f, hh = ArenaH * 0.5f - 320f;
        if (MathF.Abs(r.Head.X) > hw || MathF.Abs(r.Head.Y) > hh)
        {
            float toCenter = M.AngleOf(-r.Head);
            float diff = MathF.IEEERemainder(toCenter - r.Heading, M.Tau);
            turn = M.Clamp(diff * 3f, -2.6f, 2.6f);
        }
        r.Heading += turn * dt;
        Vector2 step = M.FromAngle(r.Heading, RailHeadSpeed * dt);
        r.Head = ClampToArena(r.Head + step, 80f);
        r.Length += step.Length();
        if (r.Points.Count == 0 || M.Dist(r.Points[^1], r.Head) >= RailPointSpacing) AddRailPoint(r, audio);
    }

    private static void AddRailPoint(MelodyRail r, AudioState audio)
    {
        float note = audio.MelodyPitch;
        float pc = ((MathF.Round(note) % 12f) + 12f) % 12f;
        r.Points.Add(r.Head);
        r.Ages.Add(0f);
        r.Hues.Add(pc / 12f);
    }

    private static void UpdateRailBounds(MelodyRail r)
    {
        if (r.Points.Count == 0) return;
        Vector2 mn = r.Points[0], mx = r.Points[0];
        foreach (var q in r.Points) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
        r.Min = mn; r.Max = mx;
    }

    private static bool InBounds(MelodyRail r, Vector2 p, float pad) =>
        r.Points.Count > 0 && p.X > r.Min.X - pad && p.X < r.Max.X + pad && p.Y > r.Min.Y - pad && p.Y < r.Max.Y + pad;

    public static Vector2 SegmentClosest(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float l2 = ab.LengthSquared();
        if (l2 < 1e-6f) return a;
        float t = M.Clamp01(Vector2.Dot(p - a, ab) / l2);
        return a + ab * t;
    }
}
