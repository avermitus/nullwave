using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.World;
using GameWorld = Nullwave.World.World;

namespace Nullwave.Render;

public static class WorldRenderer
{
    private static readonly Vector4 Shadow = new(0f, 0f, 0.004f, 0.42f);

    public static void Draw(Renderer r, GameWorld world, Palette palette, AudioState audio, float time)
    {
        r.Palette = palette;
        PopulateCombatLights(r, world, palette);
        // the ship's dark backdrop pool goes first so it is never dropped when the field is crowded
        if (world.Phase != GamePhase.Dead) r.AddBackdropFocus(world.Player.Pos, 260f, 0.75f);
        DrawArena(r, world, palette, audio, time);
        DrawMotes(r, world, palette, audio, time);
        DrawRails(r, world, time);
        DrawWells(r, world, time);
        DrawObstacles(r, world, palette, time);
        DrawPickups(r, world, palette, time);
        DrawLasers(r, world, palette);
        DrawStrikes(r, world, time);
        DrawEnemies(r, world, palette, audio, time);
        DrawBullets(r, world, palette);
        DrawLeadBeam(r, world, time);
        DrawPlayer(r, world, palette, audio, time);
        DrawParticles(r, world, palette);
        DrawShockwaves(r, world);
    }

    /// <summary>
    /// Collects up to 12 live combat light sources (zero allocations) so Abyssal mode illuminates
    /// the dark generative backdrop dynamically around the ship, Choir boss, shockwaves, and lasers.
    /// </summary>
    private static void PopulateCombatLights(Renderer r, GameWorld w, Palette p)
    {
        static Vector3 Rgb(Vector4 c) => new(c.X, c.Y, c.Z);

        // 1. Player interceptor light (+ Phase Sync / Void Phantom / Overdrive / dash surge)
        var pl = w.Player;
        float od = w.OverdriveIntensity;
        Vector3 playerCol = Vector3.Lerp(Rgb(p.Player), new Vector3(0.55f, 0.96f, 1f), pl.PhaseSyncFlash);
        if (pl.VoidPhantomFlash > 0.02f)
            playerCol = Vector3.Lerp(playerCol, new Vector3(0.75f, 0.45f, 1f), pl.VoidPhantomFlash * 0.85f);
        if (od > 0.02f)
        {
            Vector3 odCol = w.IsDropOverdrive ? new Vector3(1f, 0.48f, 0.88f) : new Vector3(0.45f, 0.98f, 1f);
            playerCol = Vector3.Lerp(playerCol, odCol, MathF.Min(0.75f, od * 0.65f));
        }
        float playerRad = 290f + pl.PhaseSyncFlash * 150f + pl.VoidPhantomFlash * 170f + od * 220f + (pl.IsDashing ? 90f : 0f);
        float playerInt = 0.95f + pl.PhaseSyncFlash * 0.65f + pl.VoidPhantomFlash * 0.75f + od * 0.75f + (pl.IsDashing ? 0.25f : 0f);
        r.AddBackdropLight(pl.Pos, playerRad, playerCol, playerInt);

        // 2. Choir Boss core light (if alive)
        if (w.BossEnemy is { Dead: false } boss)
        {
            Vector3 bossCol = Rgb(M.Hsv(boss.Hue, 0.8f, 1f));
            r.AddBackdropLight(
                boss.Pos,
                440f + boss.PhaseBreakTimer * 260f,
                bossCol,
                1.25f + w.BeatPulse * 0.4f + boss.PhaseBreakTimer * 0.6f);
        }

        // 3. Up to 4 newest active shockwaves
        int shockAdded = 0;
        for (int i = w.Shockwaves.Count - 1; i >= 0 && shockAdded < 4 && r.BackdropLightCount < Renderer.MaxBackdropLights; i--)
        {
            var s = w.Shockwaves[i];
            if (s.Life <= 0f || !r.InView(s.Pos, 350f)) continue;
            float frac = M.Clamp01(s.Life / MathF.Max(0.01f, s.MaxLife));
            r.AddBackdropLight(s.Pos, Math.Clamp(s.Radius * 0.85f, 140f, 360f), Rgb(s.Color), 0.78f * frac);
            shockAdded++;
        }

        // 4. Up to 4 active / warming lasers sampled nearest the player
        int laserAdded = 0;
        for (int i = 0; i < w.Lasers.Count && laserAdded < 4 && r.BackdropLightCount < Renderer.MaxBackdropLights; i++)
        {
            var l = w.Lasers[i];
            if (l.Done) continue;
            float alpha = l.Alpha;
            if (alpha <= 0.05f) continue;
            Vector2 dir = l.Direction;
            float t = Math.Clamp(Vector2.Dot(pl.Pos - l.Origin, dir), 0f, l.Length);
            Vector2 pt = l.Origin + dir * t;
            r.AddBackdropLight(pt, 240f + l.Width * 3f, Rgb(M.Hsv(l.Hue, 0.75f, 1f)), (l.Active ? 0.95f : 0.45f) * alpha);
            laserAdded++;
        }

        // 5. Remaining slots: active in-view elites / firing enemies
        for (int i = 0; i < w.Enemies.Count && r.BackdropLightCount < Renderer.MaxBackdropLights; i++)
        {
            var e = w.Enemies[i];
            if (e.Dead || e.Boss || !r.InView(e.Pos, 180f)) continue;
            if (!e.Elite && e.Muzzle < 0.25f) continue;
            r.AddBackdropLight(
                e.Pos,
                210f + (e.Elite ? 70f : 0f) + e.Muzzle * 90f,
                Rgb(M.Hsv(e.Hue, 0.75f, 1f)),
                0.58f + (e.Elite ? 0.25f : 0f) + e.Muzzle * 0.45f);
        }

        // Fill any leftover light slots with nearby active enemies so dark combat clusters breathe
        for (int i = 0; i < w.Enemies.Count && r.BackdropLightCount < Renderer.MaxBackdropLights; i++)
        {
            var e = w.Enemies[i];
            if (e.Dead || e.Boss || e.Elite || e.Muzzle >= 0.25f || !r.InView(e.Pos, 120f)) continue;
            r.AddBackdropLight(e.Pos, 175f, Rgb(M.Hsv(e.Hue, 0.7f, 1f)), 0.46f);
        }
    }

    private static Matrix4x4 Xf(Vector2 pos, float z, float yaw, float scale, float roll = 0f, float pitch = 0f)
    {
        return Matrix4x4.CreateScale(scale)
               * Matrix4x4.CreateRotationX(roll)
               * Matrix4x4.CreateRotationY(pitch)
               * Matrix4x4.CreateRotationZ(yaw)
               * Matrix4x4.CreateTranslation(pos.X, pos.Y, z);
    }

    /// <summary>Screen position of a point lifted to height z (matches the oblique mesh pass).</summary>
    private static Vector2 Lift(Renderer r, Vector2 p, float z) => r.Lift(p, z);

    // ------------------------------------------------------------------ arena

    private static void DrawArena(Renderer r, GameWorld w, Palette p, AudioState audio, float time)
    {
        float hw = w.ArenaW * 0.5f;
        float hh = w.ArenaH * 0.5f;

        // Floor plate: darkens the backdrop inside the arena so the fight always reads first.
        bool abyssal = GameSettings.Current.IsAbyssal;
        var floorCol = abyssal
            ? new Vector4(0.002f, 0.003f, 0.008f, 0.52f)
            : new Vector4(0.006f, 0.008f, 0.02f, 0.40f);
        r.Quad(new Vector2(-hw, -hh), new Vector2(hw, -hh), new Vector2(hw, hh), new Vector2(-hw, hh), floorCol);

        // Floor grid, only the part in view; lines near the player brighten on the beat.
        const float cell = 200f;
        Vector2 cam = r.Camera;
        Vector2 view = r.ViewExtents;
        float x0 = MathF.Max(-hw, MathF.Floor((cam.X - view.X) / cell) * cell);
        float x1 = MathF.Min(hw, cam.X + view.X);
        float y0 = MathF.Max(-hh, MathF.Floor((cam.Y - view.Y) / cell) * cell);
        float y1 = MathF.Min(hh, cam.Y + view.Y);
        float ya = MathF.Max(-hh, cam.Y - view.Y - 40), yb = MathF.Min(hh, cam.Y + view.Y + 40);
        float xa = MathF.Max(-hw, cam.X - view.X - 40), xb = MathF.Min(hw, cam.X + view.X + 40);
        var line = M.WithAlpha(p.Primary, abyssal ? 0.032f + audio.BeatPulse * 0.038f : 0.045f + audio.BeatPulse * 0.03f);
        for (float x = x0; x <= x1; x += cell) r.Line(new Vector2(x, ya), new Vector2(x, yb), 1f, line);
        for (float y = y0; y <= y1; y += cell) r.Line(new Vector2(xa, y), new Vector2(xb, y), 1f, line);
        // a beat ripple across the floor, centred on the player
        float ripple = w.BeatPhase;
        float rippleR = 80f + ripple * 900f;
        r.Circle(w.Player.Pos, rippleR, abyssal ? 2.4f : 2f, M.WithAlpha(p.Primary, (abyssal ? 0.11f : 0.05f) * (1f - ripple)), 64, true);

        var edge = new Vector4(p.Primary.X, p.Primary.Y, p.Primary.Z, 0.35f);
        var edgeGlow = new Vector4(p.Primary.X, p.Primary.Y, p.Primary.Z, 0.07f + audio.Bass * 0.06f);
        Vector2 a = new(-hw, -hh), b = new(hw, -hh), c = new(hw, hh), d = new(-hw, hh);
        r.Line(a, b, 2f, edge, true); r.Line(b, c, 2f, edge, true); r.Line(c, d, 2f, edge, true); r.Line(d, a, 2f, edge, true);
        r.Line(a, b, 14f, edgeGlow, true); r.Line(b, c, 14f, edgeGlow, true); r.Line(c, d, 14f, edgeGlow, true); r.Line(d, a, 14f, edgeGlow, true);

        // Spectrum pylons around the border: every pylon is one bar of the equaliser.
        const float spacing = 260f;
        int nx = (int)(w.ArenaW / spacing);
        int ny = (int)(w.ArenaH / spacing);
        int index = 0;
        void Pylon(Vector2 pos)
        {
            int i = index++;
            if (!r.InView(pos, 200)) return;
            float wave = 0.5f + 0.5f * MathF.Sin(i * 0.22f - time * 2.4f);
            float v = M.Clamp01(audio.Bass * (0.35f + wave * 0.65f) + audio.BeatPulse * 0.3f);
            float h = 46f + v * 70f;
            float hue = (p.BaseHue + wave * 0.08f) % 1f;
            var mats = MaterialSet.ForHue(M.Hsv(hue, 0.7f, 1f), 0.4f + v * 1.3f);
            var xf = Matrix4x4.CreateScale(1f, 1f, h) * Matrix4x4.CreateRotationZ(i * 0.37f) * Matrix4x4.CreateTranslation(pos.X, pos.Y, 0f);
            r.Meshes.Add(Models.Pylon, xf, mats);
            r.FillCircle(pos + new Vector2(10, 14), 18f, Shadow, 10);
        }
        for (int i = 0; i <= nx; i++) { float x = -hw + i * w.ArenaW / nx; Pylon(new Vector2(x, -hh)); Pylon(new Vector2(x, hh)); }
        for (int i = 1; i < ny; i++) { float y = -hh + i * w.ArenaH / ny; Pylon(new Vector2(-hw, y)); Pylon(new Vector2(hw, y)); }
    }

    // ------------------------------------------------------------------ ambient motes

    private const int MoteCount = 260;
    private static readonly Vector2[] MotePos = new Vector2[MoteCount];
    private static readonly Vector2[] MoteVel = new Vector2[MoteCount];
    private static bool _motesInit;
    private static float _moteTime = -1f;

    /// <summary>
    /// Specks of light drifting on a slowly turning flow field. The music sets their speed (energy
    /// and every kick), hi-hats make them twinkle, breakdowns thin them out and drops flare them.
    /// </summary>
    private static void DrawMotes(Renderer r, GameWorld w, Palette p, AudioState audio, float time)
    {
        float fx = GameSettings.Current.MusicFx / 100f * r.HypeScale;
        if (fx <= 0f) return;
        var mood = r.Mood;
        float dt = _moteTime < 0 ? 1f / 60f : Math.Clamp(time - _moteTime, 0f, 0.1f);
        _moteTime = time;
        Vector2 cam = r.Camera, view = r.ViewExtents + new Vector2(80, 80);
        uint seed = 0x9E3779B9;
        float Rand() { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / (float)0xFFFFFF; }
        if (!_motesInit)
        {
            for (int i = 0; i < MoteCount; i++) MotePos[i] = cam + new Vector2((Rand() * 2 - 1) * view.X, (Rand() * 2 - 1) * view.Y);
            _motesInit = true;
        }
        float intensity = mood?.Intensity ?? audio.Energy;
        float kick = mood?.Kick ?? audio.BeatPulse;
        float sparkle = mood?.Sparkle ?? audio.High;
        float drop = mood?.DropPulse ?? 0f;
        float breakdown = mood?.Breakdown ?? 0f;
        float speed = (16f + intensity * 70f + kick * 160f + drop * 420f) * fx;
        int beatIndex = (int)Math.Floor(audio.BeatPosition);
        for (int i = 0; i < MoteCount; i++)
        {
            ref Vector2 pos = ref MotePos[i];
            float a = (MathF.Sin(pos.X * 0.0021f + time * 0.13f) + MathF.Cos(pos.Y * 0.0027f - time * 0.11f) + MathF.Sin((pos.X + pos.Y) * 0.0013f + i * 0.01f)) * MathF.PI;
            Vector2 target = M.FromAngle(a, speed * (0.6f + (i % 7) * 0.08f));
            MoteVel[i] = M.Lerp(MoteVel[i], target, 1f - MathF.Exp(-dt * 3f));
            pos += MoteVel[i] * dt;
            // wrap around the view so the field always surrounds the camera
            if (pos.X < cam.X - view.X) pos.X += view.X * 2;
            else if (pos.X > cam.X + view.X) pos.X -= view.X * 2;
            if (pos.Y < cam.Y - view.Y) pos.Y += view.Y * 2;
            else if (pos.Y > cam.Y + view.Y) pos.Y -= view.Y * 2;

            float h = M.Hash01(i, beatIndex, 7);
            float twinkle = h > 0.7f ? sparkle : 0f;
            float alpha = (0.10f + intensity * 0.22f + twinkle * 0.5f + drop * 0.5f) * (1f - breakdown * 0.6f) * MathF.Min(1f, fx);
            if (alpha < 0.02f) continue;
            float size = 0.9f + twinkle * 2.2f + drop * 1.5f + (i % 5) * 0.15f;
            var col = Vector4.Lerp(p.Primary, p.Secondary, M.Hash01(i, 1, 3));
            col.W = alpha;
            Vector2 tail = pos - MoteVel[i] * 0.05f;
            r.Line(tail, pos, size, col, true);
            if (twinkle > 0.3f) r.FillCircle(pos, size * 1.6f, M.WithAlpha(col, alpha * 0.35f), 6, true);
        }
    }

    // ------------------------------------------------------------------ monoliths

    private static void DrawObstacles(Renderer r, GameWorld w, Palette p, float time)
    {
        foreach (var o in w.Obstacles)
        {
            if (o.Dead || !r.InView(o.Pos, o.Radius * 2.2f + 100)) continue;
            int n = o.Shape.Length;
            var verts = new Vector2[n];
            for (int i = 0; i < n; i++) verts[i] = o.Pos + M.Rotate(o.Shape[i] * o.Grow, o.Rotation);
            Vector4 hue = M.Hsv(o.Hue, 0.55f, 1f);

            // cast shadow toward the lower right
            var shadow = new Vector2[n];
            Vector2 off = new(o.Radius * 0.45f, o.Radius * 0.55f);
            for (int i = 0; i < n; i++) shadow[i] = verts[i] + off;
            r.FillPolygon(shadow, Shadow);

            float glow = 0.55f + w.Bass * 0.8f + w.BeatPulse * 0.7f;
            var mats = MaterialSet.ForHue(hue, glow, o.HitFlash * 0.85f);
            mats.Hull = Vector4.Lerp(new Vector4(0.05f, 0.055f, 0.08f, 1f), Vector4.One, o.HitFlash * 0.6f);
            mats.Trim = Vector4.Lerp(new Vector4(hue.X * 0.22f, hue.Y * 0.22f, hue.Z * 0.26f, 1f), Vector4.One, o.HitFlash * 0.6f);
            r.Meshes.Add(Models.Monolith(o), Matrix4x4.CreateScale(o.Grow) * Matrix4x4.CreateRotationZ(o.Rotation) * Matrix4x4.CreateTranslation(o.Pos.X, o.Pos.Y, 0f), mats);

            float top = o.Radius * 1.05f * o.Grow + o.Radius * 0.16f;
            float bob = MathF.Sin(o.Phase * 2.1f) * o.Radius * 0.06f;
            float cs = o.Radius * (0.15f + 0.03f * MathF.Sin(o.Phase * 2.1f)) * (1f + w.BeatPulse * 0.25f);
            var crystal = MaterialSet.ForHue(hue, 1.2f + w.BeatPulse);
            r.Meshes.Add(Models.Crystal, Xf(o.Pos, top + o.Radius * 0.32f + bob, time * 0.9f + o.Phase, cs), crystal);

            r.Polyline(verts, 6f, M.WithAlpha(hue, 0.08f), true, true);
            if (o.HitFlash > 0.02f)
                r.Polyline(verts, 2.4f + o.HitFlash * 4f, new Vector4(1f, 1f, 1f, o.HitFlash * 0.6f), true, true);
            float frac = o.MaxHp > 0 ? M.Clamp01(o.Hp / o.MaxHp) : 0;
            if (frac < 0.98f)
                r.Arc(Lift(r, o.Pos, top), o.Radius * 0.5f, -MathF.PI * 0.5f, M.Tau * frac, 2f, M.WithAlpha(hue, 0.7f), 24, true);
        }
    }

    // ------------------------------------------------------------------ pickups

    private static void DrawPickups(Renderer r, GameWorld w, Palette p, float time)
    {
        foreach (var pickup in w.Pickups)
        {
            if (!r.InView(pickup.Pos, 40)) continue;
            Vector4 col = pickup.Kind switch
            {
                PickupKind.Health => new Vector4(0.45f, 1f, 0.68f, 1f),
                PickupKind.Resonance => new Vector4(0.35f, 0.9f, 1f, 1f),
                PickupKind.Score => new Vector4(1f, 0.76f, 0.35f, 1f),
                _ => new Vector4(1f, 0.4f, 0.8f, 1f),
            };
            float blink = pickup.Life < 3f ? (MathF.Sin(time * 22f) > 0 ? 1f : 0.35f) : 1f;
            float bob = MathF.Sin(pickup.Phase) * 4f;
            r.FillCircle(pickup.Pos + new Vector2(5, 7), 7f, Shadow, 10);
            var mats = MaterialSet.ForHue(col, 1.3f * blink, 0f);
            r.Meshes.Add(Models.PickupGem, Xf(pickup.Pos, 16f + bob, pickup.Phase * 1.3f, 6.5f, 0.3f), mats);
            Vector2 pos = Lift(r, pickup.Pos, 16f + bob);
            r.Circle(pickup.Pos, 15f, 4f, M.WithAlpha(col, 0.14f * blink), 20, true);
            r.Circle(pos, 11f, 1f, M.WithAlpha(col, 0.6f * blink), 14, true);
            if (pickup.Kind == PickupKind.Health)
            {
                r.Line(pos + new Vector2(0, -5), pos + new Vector2(0, 5), 2f, new Vector4(1, 1, 1, 0.9f * blink), true);
                r.Line(pos + new Vector2(-5, 0), pos + new Vector2(5, 0), 2f, new Vector4(1, 1, 1, 0.9f * blink), true);
            }
        }
    }

    // ------------------------------------------------------------------ mortar strikes

    /// <summary>Target rings: an outer danger zone and an inner ring closing to it at detonation.</summary>
    private static void DrawStrikes(Renderer r, GameWorld w, float time)
    {
        foreach (var s in w.Strikes)
        {
            if (!r.InView(s.Pos, s.Radius + 40)) continue;
            float t = 1f - M.Clamp01(s.Timer / MathF.Max(0.01f, s.Duration));
            var col = M.Hsv(s.Hue, 0.75f, 1f);
            float blink = 0.6f + 0.4f * MathF.Sin(time * (14f + t * 30f));
            r.FillCircle(s.Pos, s.Radius, M.WithAlpha(col, 0.06f + t * 0.14f), 32);
            r.Circle(s.Pos, s.Radius, 2.5f, M.WithAlpha(col, 0.5f + 0.4f * blink), 40, true);
            r.Circle(s.Pos, s.Radius * (2.2f - t * 1.2f), 2f + t * 2f, M.WithAlpha(col, 0.35f + t * 0.5f), 40, true);
            for (int k = 0; k < 4; k++)
            {
                Vector2 d = M.FromAngle(k * MathF.PI * 0.5f + time * 0.8f);
                r.Line(s.Pos + d * s.Radius * 0.35f, s.Pos + d * s.Radius * 0.75f, 2f, M.WithAlpha(col, 0.8f), true);
            }
        }
    }

    // ------------------------------------------------------------------ lasers

    private static void DrawLasers(Renderer r, GameWorld w, Palette p)
    {
        foreach (var l in w.Lasers)
        {
            float a = l.Alpha;
            if (a <= 0.01f) continue;
            var col = M.Hsv(l.Hue, l.BossLaser ? 0.75f : 0.60f, 1f);
            Vector2 dir = l.Direction;
            Vector2 perp = M.Perp(dir);
            Vector2 end = l.Origin + dir * l.Length;
            if (l.Age < l.Warmup)
            {
                float progress = M.Clamp01(l.Age / MathF.Max(0.01f, l.Warmup));
                float pulse = 0.55f + 0.45f * MathF.Sin(l.Age * 26f);
                float halfW = l.Width * (0.58f - progress * 0.14f);
                var railCol = M.WithAlpha(col, a * (0.18f + progress * 0.34f));
                r.Line(l.Origin + perp * halfW, end + perp * halfW, 1.1f, railCol, true);
                r.Line(l.Origin - perp * halfW, end - perp * halfW, 1.1f, railCol, true);
                var warn = M.WithAlpha(col, a * (0.30f + pulse * 0.24f));
                r.Line(l.Origin, end, 1.4f, warn, true);
                int dashes = 20;
                for (int i = 0; i < dashes; i++)
                {
                    float t0 = i / (float)dashes;
                    float t1 = MathF.Min(1f, t0 + 0.03f);
                    Vector2 a0 = l.Origin + dir * (l.Length * t0);
                    Vector2 a1 = l.Origin + dir * (l.Length * t1);
                    r.Line(a0, a1, 2.2f + l.Width * 0.04f, M.WithAlpha(col, a * (0.55f + progress * 0.4f)), true);
                }
                float lockR = 14f + (1f - progress) * 14f + MathF.Sin(l.Age * 18f) * 2.5f;
                r.Circle(l.Origin, lockR, 1.4f, M.WithAlpha(col, a * 0.9f), 20, true);
                r.Arc(l.Origin, lockR * 1.35f, l.Age * 5f, 1.6f, 1.4f, new Vector4(1f, 1f, 1f, a * 0.7f), 10, true);
            }
            else
            {
                // 4-layer HDR beam: outer bloom sheath, saturated mid body, beat-pulsing side rails, white-hot core
                r.Line(l.Origin, end, l.Width * 3.4f, M.WithAlpha(col, 0.06f * a), true);
                r.Line(l.Origin, end, l.Width * 1.25f, M.WithAlpha(col, 0.26f * a), true);
                float railOff = l.Width * 0.36f * (1f + w.BeatPulse * 0.35f);
                var railCol = M.WithAlpha(Vector4.Lerp(col, Vector4.One, 0.28f), 0.52f * a);
                r.Line(l.Origin + perp * railOff, end + perp * railOff, MathF.Max(1.4f, l.Width * 0.09f), railCol, true);
                r.Line(l.Origin - perp * railOff, end - perp * railOff, MathF.Max(1.4f, l.Width * 0.09f), railCol, true);
                r.Line(l.Origin, end, MathF.Max(2.5f, l.Width * 0.18f), new Vector4(1f, 1f, 1f, 0.98f * a), true);

                // Travelling directional energy chevrons along the beam
                float chevW = MathF.Max(6f, l.Width * 0.32f);
                for (int i = 0; i < 8; i++)
                {
                    float d = (l.Age * 1250f + i * (l.Length / 8f)) % l.Length;
                    Vector2 tip = l.Origin + dir * d;
                    Vector2 back = tip - dir * (chevW * 1.15f);
                    r.Line(tip, back + perp * chevW, 1.7f, new Vector4(1f, 1f, 1f, a * 0.78f), true);
                    r.Line(tip, back - perp * chevW, 1.7f, new Vector4(1f, 1f, 1f, a * 0.78f), true);
                    r.FillCircle(tip, MathF.Max(2.2f, l.Width * 0.12f), new Vector4(1f, 1f, 1f, a * 0.9f), 8, true);
                }

                // Emitter and endpoint flares
                float flareR = l.Width * (0.65f + w.BeatPulse * 0.25f);
                r.FillCircle(l.Origin, flareR * 1.4f, M.WithAlpha(col, 0.35f * a), 16, true);
                r.Circle(l.Origin, flareR * 1.75f, 1.8f, new Vector4(1f, 1f, 1f, 0.72f * a), 20, true);
                r.FillCircle(end, flareR * 1.15f, M.WithAlpha(col, 0.30f * a), 14, true);
                r.Line(end - perp * flareR * 1.6f, end + perp * flareR * 1.6f, 2.2f, new Vector4(1f, 1f, 1f, 0.75f * a), true);
            }
        }
    }

    // ------------------------------------------------------------------ enemies

    private static void DrawEnemies(Renderer r, GameWorld w, Palette p, AudioState audio, float time)
    {
        foreach (var e in w.Enemies)
        {
            if (e.Dead || !r.InView(e.Pos, e.Radius * 3 + 140)) continue;
            Vector4 hue = M.Hsv(e.Hue, 0.72f, 1f);
            float R = e.Radius;

            if (e.Spawning)
            {
                float progress = 1f - e.SpawnTimer / MathF.Max(0.01f, e.SpawnDuration);
                float ringRadius = R * (2.4f - progress * 1.25f);
                r.Circle(e.Pos, R * 2.5f, 1.2f, M.WithAlpha(hue, 0.22f + progress * 0.4f), 28, true);
                r.Circle(e.Pos, ringRadius, 2f, M.WithAlpha(hue, 0.45f), 24, true);
                for (int i = 0; i < 8; i++)
                {
                    float a = i / 8f * M.Tau + time * 0.8f;
                    r.Line(e.Pos + M.FromAngle(a, R * 1.45f), e.Pos + M.FromAngle(a, R * 2.2f), 1.1f, M.WithAlpha(hue, 0.4f + progress * 0.3f), true);
                }
                // materialise: the model grows in white-hot and cools to its colours
                float s = M.EaseOutBack(M.Clamp01(progress * 1.1f));
                if (s > 0.02f)
                    DrawEnemyModel(r, w, e, hue, s, 1f - progress, time, audio);
                continue;
            }

            if (r.InView(e.Pos, 60f)) r.AddBackdropFocus(e.Pos, R * (e.Boss ? 5f : 4f), e.Boss ? 0.7f : 0.55f);
            r.FillCircle(e.Pos, R * 1.7f, new Vector4(0f, 0f, 0.01f, 0.38f), 20);
            r.FillCircle(e.Pos + new Vector2(R * 0.32f, R * 0.42f), R * (e.Boss ? 0.8f : 0.92f), Shadow, 16);
            if (!e.Boss)
            {
                // threat halo + outline: every enemy carries a coloured glow you can spot at speed
                r.FillCircle(e.Pos, R * 2.1f, M.WithAlpha(hue, 0.06f), 24, true);
                r.FillCircle(e.Pos, R * 1.45f, M.WithAlpha(hue, 0.09f), 24, true);
                r.Circle(e.Pos, R * 1.5f, 2.2f, M.WithAlpha(hue, 0.6f), 32, true);
            }
            // every enemy kicks on the beat (from the locked beat clock, not the lagging audio pulse)
            float kick = MathF.Exp(-w.BeatPhase * 9f);
            float pulse = 1f + MathF.Sin(time * 3f + e.Angle) * 0.025f + kick * (e.Boss ? 0.06f : 0.16f);
            DrawEnemyModel(r, w, e, hue, pulse, e.HitFlash, time, audio);

            // Galaga-style arrival: a streak behind the dive, then lock-on brackets that close in
            // while it holds fire.
            // On-beat telegraph: a ring that closes onto the enemy exactly as it fires.
            if (e.Telegraph > 0.02f && !e.Boss && GameSettings.Current.BeatTelegraphs)
            {
                float t = e.Telegraph;
                float rad = R * (1.2f + (1f - t) * (1f - t) * 2.2f);
                r.Circle(e.Pos, rad, 1.2f + t * 1.8f, M.WithAlpha(hue, 0.18f + t * 0.62f), 28, true);
                if (e.Role is EnemyRole.Sniper or EnemyRole.Drifter && t > 0.35f)
                {
                    Vector2 dir = M.FromAngle(e.Facing);
                    r.Line(e.Pos + dir * R, e.Pos + dir * (R + 160f * t), 1f + t, M.WithAlpha(hue, (t - 0.35f) * 0.45f), true);
                }
            }
            if (e.Muzzle > 0.05f)
                r.Circle(e.Pos, R * (1.1f + (1f - e.Muzzle) * 1.4f), 2.5f * e.Muzzle, new Vector4(1f, 1f, 1f, e.Muzzle * 0.7f), 24, true);

            if (e.Elite)
                r.Circle(e.Pos, R * 1.42f, 1f, new Vector4(1f, 0.84f, 0.35f, 0.35f), 24, true);

            // health bar for anything tougher than fodder once it has been hurt
            if (GameSettings.Current.EnemyHealthBars && !e.Boss && (e.Elite || e.HealthFrac < 0.995f) && e.Role != EnemyRole.Swarm)
            {
                float bw = MathF.Max(28f, R * 2f);
                Vector2 c = Lift(r, e.Pos, R * 0.9f) + new Vector2(0, -R - 12f);
                r.Rect(c, new Vector2(bw + 3f, 5f), 0f, new Vector4(0.01f, 0.01f, 0.025f, 0.85f));
                float f = e.HealthFrac;
                r.Rect(c + new Vector2(-bw * 0.5f * (1f - f), 0), new Vector2(bw * f, 2.6f), 0f, M.WithAlpha(e.Elite ? new Vector4(1f, 0.84f, 0.35f, 1f) : hue, 0.95f), true);
            }
        }
    }

    private static void DrawEnemyModel(Renderer r, GameWorld w, Enemy e, Vector4 hue, float scale, float flash, float time, AudioState audio)
    {
        float R = e.Radius * scale * 1.4f;
        float glow = 0.8f + e.Telegraph * 1.6f + e.Muzzle * 1.4f + w.BeatPulse * 0.25f;
        var mats = MaterialSet.ForHue(hue, glow, flash);
        // lit, hue-tinted bodies instead of near-black ones, so silhouettes read on any backdrop
        mats.Hull = Vector4.Lerp(new Vector4(hue.X * 0.38f + 0.1f, hue.Y * 0.38f + 0.1f, hue.Z * 0.38f + 0.12f, 1f), Vector4.One, flash);
        mats.Trim = Vector4.Lerp(new Vector4(hue.X * 0.75f + 0.15f, hue.Y * 0.75f + 0.15f, hue.Z * 0.75f + 0.15f, 1f), Vector4.One, flash);
        var batch = r.Meshes;
        float bob = MathF.Sin(time * 2.3f + e.Angle * 3f) * R * 0.06f;
        float lift = R * 0.25f + bob;
        switch (e.Role)
        {
            case EnemyRole.Drifter:
                batch.Add(Models.Drifter, Xf(e.Pos, lift, e.Rot * 1.6f, R), mats);
                break;
            case EnemyRole.Orbiter:
            {
                float tilt = MathF.Sin(time * 1.3f + e.Angle) * 0.22f;
                batch.Add(Models.Orbiter, Xf(e.Pos, lift, e.Rot, R, tilt, tilt * 0.6f), mats);
                for (int i = 0; i < 3; i++)
                {
                    float a = e.Angle * 2.4f + time * 1.8f + i * M.Tau / 3f;
                    Vector2 pp = e.Pos + M.FromAngle(a, R * 1.4f);
                    batch.Add(Models.OrbiterPod, Xf(pp, lift + MathF.Sin(a * 2f) * R * 0.2f, a + MathF.PI * 0.5f, R), mats);
                }
                break;
            }
            case EnemyRole.Sniper:
                batch.Add(Models.Sniper, Xf(e.Pos, lift, e.Facing, R * 1.1f, MathF.Sin(time * 2f + e.Angle) * 0.12f), mats);
                break;
            case EnemyRole.Dasher:
            {
                float yaw = e.State >= 1 && e.State <= 2 ? M.AngleOf(e.DashDir) : e.Facing;
                float shake = e.State == 1 ? MathF.Sin(time * 70f) * 0.12f : 0f;
                float roll = e.State == 2 ? time * 18f : MathF.Sin(time * 2f) * 0.25f;
                batch.Add(Models.Dasher, Xf(e.Pos, lift, yaw + shake, R * 1.05f, roll), mats);
                if (e.State == 2)
                    r.Line(e.Pos, e.Pos - M.FromAngle(yaw, R * 3.2f), R * 0.8f, M.WithAlpha(hue, 0.12f), true);
                break;
            }
            case EnemyRole.Turret:
                batch.Add(Models.TurretBase, Xf(e.Pos, R * 0.1f, e.Rot * 0.25f, R), mats);
                batch.Add(Models.TurretHead, Xf(e.Pos, R * 0.38f, e.Angle * 1.6f, R), mats);
                break;
            case EnemyRole.Splitter:
            {
                batch.Add(Models.SplitterCore, Xf(e.Pos, lift + R * 0.2f, e.Rot, R, e.Rot * 0.7f), mats);
                int cells = e.Elite ? 6 : 4;
                for (int i = 0; i < cells; i++)
                {
                    float a = e.Rot * 1.3f + i * M.Tau / cells;
                    float breathe = 0.78f + w.BeatPulse * 0.12f + e.Telegraph * 0.15f;
                    Vector2 cp = e.Pos + M.FromAngle(a, R * breathe);
                    batch.Add(Models.SplitterCell, Xf(cp, lift + R * 0.2f + MathF.Sin(a * 2f + time) * R * 0.18f, a, R, a, a * 0.5f), mats);
                }
                break;
            }
            case EnemyRole.Swarm:
            {
                float yaw = e.Vel.LengthSquared() > 4f ? M.AngleOf(e.Vel) : e.Facing;
                batch.Add(Models.Swarm, Xf(e.Pos, lift, yaw, R * 1.15f, MathF.Sin(time * 9f + e.Angle) * 0.4f), mats);
                break;
            }
            case EnemyRole.Boss:
                if (e.SongBoss) DrawSongBoss(r, w, e, hue, scale, flash, time, audio);
                else DrawBoss(r, w, e, hue, scale, flash, time, audio);
                break;
            case EnemyRole.Weaver:
            {
                Vector2 fwd = e.Vel.LengthSquared() > 4f ? M.SafeNorm(e.Vel) : M.FromAngle(e.Facing);
                batch.Add(Models.WeaverHead, Xf(e.Pos, lift, M.AngleOf(fwd), R), mats);
                // body segments trail along the serpentine path
                for (int k = 1; k <= 5; k++)
                {
                    float sway = MathF.Sin(e.Age * 3.2f + e.Angle - k * 0.75f) * R * 0.75f;
                    Vector2 sp = e.Pos - fwd * (k * R * 0.95f) + M.Perp(fwd) * sway;
                    float sc = R * (1f - k * 0.11f);
                    batch.Add(Models.WeaverSegment, Xf(sp, lift * 0.8f, M.AngleOf(fwd) + sway * 0.01f, sc, MathF.Sin(e.Age * 4f - k) * 0.4f), mats);
                }
                break;
            }
            case EnemyRole.Mortar:
            {
                float aimYaw = e.Facing;
                batch.Add(Models.MortarBase, Xf(e.Pos, R * 0.1f, e.Rot * 0.2f, R), mats);
                float recoil = e.Muzzle * 0.25f;
                var barrel = Matrix4x4.CreateScale(R) * Matrix4x4.CreateTranslation(-recoil * R, 0, 0)
                             * Matrix4x4.CreateRotationY(-0.9f) * Matrix4x4.CreateRotationZ(aimYaw)
                             * Matrix4x4.CreateTranslation(e.Pos.X, e.Pos.Y, R * 0.55f);
                batch.Add(Models.MortarBarrel, barrel, mats);
                break;
            }
            case EnemyRole.Phantom:
            {
                float ghost = 0.55f + 0.45f * MathF.Sin(time * 6f + e.Angle);
                var pm = MaterialSet.ForHue(hue, glow * (0.8f + ghost * 0.6f), flash, 0.6f + ghost * 0.4f);
                batch.Add(Models.Phantom, Xf(e.Pos, lift, time * 1.5f + e.Angle, R, MathF.Sin(time * 2f) * 0.3f), pm);
                for (int k = 0; k < 3; k++)
                {
                    float a = time * 2.4f + k * M.Tau / 3f + e.Angle;
                    batch.Add(Models.BossShard, Xf(e.Pos + M.FromAngle(a, R * 1.3f), lift + R * 0.4f, a, R * 1.2f), pm);
                }
                break;
            }
            case EnemyRole.Prism:
                batch.Add(Models.Prism, Xf(e.Pos, R * 0.1f, e.Angle, R), mats);
                break;
        }
        if (e.Elite && !e.Boss)
        {
            var gold = MaterialSet.ForHue(new Vector4(1f, 0.8f, 0.3f, 1f), 1.2f, flash);
            batch.Add(Models.EliteRing, Xf(e.Pos, lift, time * 1.4f, R * 1.3f, 0.35f * MathF.Sin(time), 0.2f), gold);
        }
    }

    private static void DrawBoss(Renderer r, GameWorld w, Enemy e, Vector4 hue, float scale, float flash, float time, AudioState audio)
    {
        float R = e.Radius * scale * e.BossScale;
        float glow = 1f + w.BeatPulse * 1.2f + (e.BossInvuln > 0 ? 1.5f : 0f);
        var mats = MaterialSet.ForHue(hue, glow, flash);
        var batch = r.Meshes;
        float lift = R * 0.45f;
        batch.Add(Models.BossCore, Xf(e.Pos, lift, e.Rot, R * (1f + w.BeatPulse * 0.08f), e.Rot * 0.3f), mats);

        // the crown: ten pillars that sing the spectrum
        int pillars = 10 + e.BossPhase * 2;
        for (int i = 0; i < pillars; i++)
        {
            float a = e.Angle + i * M.Tau / pillars;
            float v = M.Clamp01(0.35f + 0.35f * MathF.Sin(a * 3f + time * 2.2f) + w.Bass * 0.5f + w.BeatPulse * 0.3f);
            float h = R * (0.35f + v * 0.9f);
            Vector2 pp = e.Pos + M.FromAngle(a, R * 0.95f);
            var pm = MaterialSet.ForHue(hue, 0.8f + v * 1.6f, flash);
            var xf = Matrix4x4.CreateScale(R, R, h) * Matrix4x4.CreateRotationZ(a) * Matrix4x4.CreateTranslation(pp.X, pp.Y, R * 0.05f);
            batch.Add(Models.BossPillar, xf, pm);
        }
        batch.Add(Models.BossHalo, Xf(e.Pos, lift, -e.Angle * 1.5f, R * 1.25f, MathF.Sin(time * 0.6f) * 0.35f, MathF.Cos(time * 0.5f) * 0.25f), mats);
        if (e.BossPhase >= 1)
            batch.Add(Models.BossHalo, Xf(e.Pos, lift, e.Angle * 2.1f, R * 1.55f, MathF.Cos(time * 0.7f) * 0.45f, 0.5f), mats);
        if (e.BossPhase >= 2)
        {
            int shards = e.BossPhase >= 3 ? 18 : 14;
            float shardSpeed = e.BossPhase >= 3 ? -1.6f : -1.2f;
            for (int i = 0; i < shards; i++)
            {
                float a = time * shardSpeed + i * M.Tau / shards;
                Vector2 sp = e.Pos + M.FromAngle(a, R * 1.85f);
                batch.Add(Models.BossShard, Xf(sp, lift + MathF.Sin(a * 3f + time) * R * 0.2f, a, R), mats);
            }
        }
        if (e.BossPhase >= 3)
        {
            var apex = MaterialSet.ForHue(new Vector4(1f, 0.42f, 0.35f, 1f), glow * 1.15f, flash);
            batch.Add(Models.BossHalo, Xf(e.Pos, lift, -e.Angle * 2.8f, R * 1.82f, MathF.Sin(time * 0.9f) * 0.55f, -0.45f), apex);
        }
        r.Circle(e.Pos, R * 1.2f, 2.2f, M.WithAlpha(hue, 0.3f), 48, true);
        if (e.Telegraph > 0.04f && GameSettings.Current.BeatTelegraphs)
        {
            float tele = e.Telegraph;
            float teleR = R * M.Lerp(2.1f, 1.15f, tele);
            r.Circle(e.Pos, teleR, 1.5f + tele * 2.8f, M.WithAlpha(hue, 0.25f + tele * 0.65f + w.BeatPulse * 0.1f), 48, true);
            r.Circle(e.Pos, teleR * 0.92f, 1.0f + tele, new Vector4(1f, 1f, 1f, 0.15f + tele * 0.35f), 36, true);
        }
        if (e.PhaseBreakTimer > 0f)
        {
            float pfrac = 1f - M.Clamp01(e.PhaseBreakTimer / MathF.Max(0.01f, e.PhaseBreakDuration));
            float fade = 1f - pfrac;
            float ringR = M.Lerp(R * 1.1f, GameWorld.BossPhaseClearRadius, M.EaseOutCubic(pfrac));
            r.Circle(e.Pos, ringR, 4f * fade + 1.5f, M.WithAlpha(hue, 0.78f * fade), 56, true);
            r.Circle(e.Pos, ringR * 0.72f, 2f * fade + 1f, new Vector4(1f, 1f, 1f, 0.65f * fade), 44, true);
            for (int i = 0; i < 12; i++)
            {
                float a = i * M.Tau / 12f + time * 1.8f;
                r.Line(e.Pos + M.FromAngle(a, ringR * 0.88f), e.Pos + M.FromAngle(a, ringR * 1.02f), 2f * fade, M.WithAlpha(hue, 0.7f * fade), true);
            }
        }
        if (e.BossInvuln > 0)
            r.Circle(e.Pos, R * (1.3f + MathF.Sin(time * 20f) * 0.04f), 5f, new Vector4(1f, 1f, 1f, 0.35f), 48, true);
    }

    // ------------------------------------------------------------------ bullets

    private static void DrawBullets(Renderer r, GameWorld w, Palette p)
    {
        foreach (var b in w.PlayerBullets)
        {
            if (!r.InView(b.Pos, 80)) continue;
            Vector2 dir = M.SafeNorm(b.Vel);
            float baseTail = Math.Clamp(b.Vel.Length() * 0.032f, 22f, 56f);
            if (b.SynergyKind == 4)
            {
                // BASS orb: a heavy, slow, throbbing ember
                float throb = 1f + MathF.Sin(b.Age * 18f) * 0.12f;
                var ember = new Vector4(1f, 0.52f, 0.2f, 1f);
                r.GradientLine(b.Pos - dir * baseTail * 1.6f, b.Pos, b.Radius * 2.2f, M.WithAlpha(ember, 0f), M.WithAlpha(ember, 0.35f), true);
                r.FillCircle(b.Pos, b.Radius * 2.1f * throb, M.WithAlpha(ember, 0.22f), 18, true);
                r.FillCircle(b.Pos, b.Radius * 1.15f * throb, M.WithAlpha(ember, 0.85f), 16, true);
                r.FillCircle(b.Pos, b.Radius * 0.55f, new Vector4(1f, 0.95f, 0.8f, 1f), 12, true);
                continue;
            }
            if (b.SynergyKind == 1)
            {
                // SEEKER SALVO: hyper-magenta/cyan flanking micro-seekers with elongated plasma ribbons
                float tailLen = baseTail * 1.45f;
                Vector2 tailStart = b.Pos - dir * tailLen;
                var mag = new Vector4(1f, 0.34f, 0.92f, 1f);
                var cya = new Vector4(0.42f, 0.98f, 1f, 1f);
                r.GradientLine(tailStart, b.Pos, b.Radius * 4.4f, M.WithAlpha(mag, 0f), M.WithAlpha(mag, 0.34f), true);
                r.GradientLine(b.Pos - dir * (tailLen * 0.72f), b.Pos, b.Radius * 1.65f, M.WithAlpha(cya, 0f), M.WithAlpha(cya, 0.92f), true);
                r.Line(b.PrevPos, b.Pos, b.Radius * 1.25f, new Vector4(1f, 0.85f, 1f, 0.95f), true);
                r.FillCircle(b.Pos, b.Radius * 1.05f, new Vector4(1f, 1f, 1f, 0.98f), 8, true);
                r.FillCircle(b.Pos - dir * (tailLen * 0.45f), b.Radius * 0.52f, M.WithAlpha(mag, 0.65f), 6, true);
            }
            else if (b.SynergyKind == 2)
            {
                // PRISM LANCE: refractive solar-gold + white-hot core streak + twin parallel refraction hairlines
                float tailLen = baseTail * 1.6f;
                Vector2 tailStart = b.Pos - dir * tailLen;
                Vector2 perp = M.Perp(dir) * (b.Radius * 1.15f);
                var gold = new Vector4(1f, 0.86f, 0.32f, 1f);
                r.GradientLine(tailStart, b.Pos, b.Radius * 4.6f, M.WithAlpha(gold, 0f), M.WithAlpha(gold, 0.36f), true);
                r.GradientLine(b.Pos - dir * (tailLen * 0.8f) + perp, b.Pos + perp, 1.35f, M.WithAlpha(gold, 0f), new Vector4(0.65f, 1f, 1f, 0.85f), true);
                r.GradientLine(b.Pos - dir * (tailLen * 0.8f) - perp, b.Pos - perp, 1.35f, M.WithAlpha(gold, 0f), new Vector4(1f, 0.55f, 0.95f, 0.85f), true);
                r.GradientLine(b.Pos - dir * (tailLen * 0.7f), b.Pos, b.Radius * 1.75f, new Vector4(1f, 0.96f, 0.8f, 0f), new Vector4(1f, 0.98f, 0.86f, 0.96f), true);
                r.FillCircle(b.Pos, b.Radius * 1.15f, new Vector4(1f, 1f, 1f, 1f), 8, true);
            }
            else if (b.SynergyKind == 3)
            {
                // RAIL CADENCE: thick hyper-rail solar-amber/white core + outer plasma rails + large nose flare
                float tailLen = Math.Clamp(b.Vel.Length() * 0.056f, 52f, 118f);
                Vector2 tailStart = b.Pos - dir * tailLen;
                Vector2 perp = M.Perp(dir) * (b.Radius * 1.25f);
                var amber = new Vector4(1f, 0.68f, 0.18f, 1f);
                var gold = new Vector4(1f, 0.92f, 0.48f, 1f);
                r.GradientLine(tailStart, b.Pos, b.Radius * 5.2f, M.WithAlpha(amber, 0f), M.WithAlpha(amber, 0.38f), true);
                r.GradientLine(b.Pos - dir * (tailLen * 0.85f) + perp, b.Pos + perp, 1.6f, M.WithAlpha(gold, 0f), M.WithAlpha(gold, 0.82f), true);
                r.GradientLine(b.Pos - dir * (tailLen * 0.85f) - perp, b.Pos - perp, 1.6f, M.WithAlpha(gold, 0f), M.WithAlpha(gold, 0.82f), true);
                r.GradientLine(b.Pos - dir * (tailLen * 0.75f), b.Pos, b.Radius * 1.95f, M.WithAlpha(gold, 0f), new Vector4(1f, 0.98f, 0.90f, 0.98f), true);
                r.FillCircle(b.Pos, b.Radius * 2.1f, M.WithAlpha(amber, 0.34f), 10, true);
                r.FillCircle(b.Pos, b.Radius * 1.25f, new Vector4(1f, 1f, 1f, 1f), 10, true);
            }
            else
            {
                Vector2 tailStart = b.Pos - dir * baseTail;
                var col = new Vector4(0.52f, 0.98f, 1f, 1f);
                r.GradientLine(tailStart, b.Pos, b.Radius * 3.8f, M.WithAlpha(col, 0f), new Vector4(0.35f, 0.95f, 1f, 0.22f), true);
                r.GradientLine(b.Pos - dir * (baseTail * 0.65f), b.Pos, b.Radius * 1.45f, M.WithAlpha(col, 0f), M.WithAlpha(col, 0.88f), true);
                r.Line(b.PrevPos, b.Pos, b.Radius * 1.15f, new Vector4(0.85f, 1f, 1f, 0.92f), true);
                r.FillCircle(b.Pos, b.Radius * 0.95f, new Vector4(1f, 1f, 1f, 0.98f), 8, true);
                r.FillCircle(b.Pos - dir * (baseTail * 0.55f), b.Radius * 0.45f, new Vector4(0.6f, 1f, 1f, 0.55f), 6, true);
            }
        }

        // Enemy shots flash and swell on the beat as they surge; a stronger dark halo + comet ribbon keeps them readable.
        float onBeat = MathF.Exp(-w.BeatPhase * 7f);
        float beat = 1f + onBeat * 0.35f;
        foreach (var b in w.EnemyBullets)
        {
            if (!r.InView(b.Pos, 80)) continue;
            Vector4 hue = M.Hsv(b.Hue, 0.78f, 1f);
            r.FillCircle(b.Pos, b.Radius * 2.8f, new Vector4(0f, 0f, 0.012f, 0.72f), 10);
            if (b.Tuned)
                r.Circle(b.Pos, b.Radius * 2.2f, 1.4f, new Vector4(1f, 1f, 1f, w.Player.RailRiding ? 0.85f : 0.45f), 12, true);
            // glowing dual-layer comet ribbon + shimmer nodes
            {
                Vector2 vdir = M.SafeNorm(b.Vel);
                float tail = Math.Clamp(b.Vel.Length() * 0.145f, 28f, 120f) * (1f + onBeat * 0.7f);
                Vector2 end = b.Pos - vdir * tail;
                r.GradientLine(end, b.Pos, b.Radius * 3.2f, M.WithAlpha(hue, 0f), M.WithAlpha(hue, 0.40f), true);
                r.GradientLine(b.Pos - vdir * (tail * 0.72f), b.Pos, MathF.Max(1.6f, b.Radius * 1.1f), new Vector4(1f, 1f, 1f, 0f), M.WithAlpha(Vector4.Lerp(hue, Vector4.One, 0.55f), 0.95f), true);
                r.FillCircle(b.Pos - vdir * (tail * 0.45f), MathF.Max(1.3f, b.Radius * 0.42f), M.WithAlpha(hue, 0.55f), 6, true);
                r.FillCircle(b.Pos - vdir * (tail * 0.80f), MathF.Max(1.0f, b.Radius * 0.28f), M.WithAlpha(hue, 0.28f), 6, true);
            }
            if (b.Kind == BulletKind.Needle)
            {
                Vector2 dir = M.SafeNorm(b.Vel);
                r.Line(b.Pos - dir * 14f, b.Pos + dir * 15f, b.Radius * 2.4f * beat, M.WithAlpha(hue, 0.20f), true);
                r.Line(b.Pos - dir * 11f, b.Pos + dir * 12f, b.Radius * 0.9f, M.WithAlpha(hue, 0.95f), true);
                r.FillCircle(b.Pos, 2.0f, new Vector4(1f, 1f, 1f, 0.98f), 6, true);
            }
            else if (b.Kind == BulletKind.Shard)
            {
                Vector2 dir = M.SafeNorm(b.Vel);
                Vector2 n = M.Perp(dir);
                float rr = b.Radius * beat;
                r.Triangle(b.Pos + dir * rr * 1.8f, b.Pos - dir * rr * 0.85f + n * rr * 1.05f, b.Pos - dir * rr * 0.85f - n * rr * 1.05f, M.WithAlpha(hue, 0.78f), true);
                r.Triangle(b.Pos + dir * b.Radius * 1.25f, b.Pos - dir * b.Radius * 0.4f + n * b.Radius * 0.58f, b.Pos - dir * b.Radius * 0.4f - n * b.Radius * 0.58f, new Vector4(1f, 1f, 1f, 0.88f), true);
            }
            else
            {
                r.FillCircle(b.Pos, b.Radius * 2.6f * beat, M.WithAlpha(hue, 0.14f), 10, true);
                r.FillCircle(b.Pos, b.Radius * 1.32f, M.WithAlpha(hue, 0.62f), 10, true);
                r.FillCircle(b.Pos, b.Radius * 0.68f, new Vector4(1f, 1f, 1f, 0.98f), 8, true);
            }
        }
    }

    // ------------------------------------------------------------------ player

    private const int TrailLength = 56;
    private static readonly Vector2[] TrailL = new Vector2[TrailLength];
    private static readonly Vector2[] TrailR = new Vector2[TrailLength];
    private static int _trailCount;
    private static float _trailTimer;
    private static readonly Matrix4x4[] Ghosts = new Matrix4x4[6];
    private static int _ghostCount;
    private static float _ghostTimer;
    private static float _trailTime = -1f;
    private static World.World? _trailWorld;

    /// <summary>Twin engine light trails (podracer style) and fading afterimages at high speed.</summary>
    private static void DrawSpeedFx(Renderer r, GameWorld w, Palette p, float time, Matrix4x4 ship, float aim)
    {
        var player = w.Player;
        if (_trailWorld != w) { _trailWorld = w; _trailCount = 0; _ghostCount = 0; }
        float dt = _trailTime < 0 ? 1f / 60f : Math.Clamp(time - _trailTime, 0f, 0.1f);
        _trailTime = time;
        float speed = player.SpeedFrac;
        Vector2 back = M.FromAngle(aim + MathF.PI, 22f);
        Vector2 side = M.Perp(M.FromAngle(aim)) * 11f;
        _trailTimer -= dt;
        if (_trailTimer <= 0f)
        {
            _trailTimer = 0.016f;
            for (int i = Math.Min(_trailCount, TrailLength - 1); i > 0; i--) { TrailL[i] = TrailL[i - 1]; TrailR[i] = TrailR[i - 1]; }
            TrailL[0] = player.Pos + back + side;
            TrailR[0] = player.Pos + back - side;
            _trailCount = Math.Min(TrailLength, _trailCount + 1);
        }
        float strength = M.Clamp01(speed * 1.2f - 0.15f) * (0.6f + w.Flow * 0.8f) + (player.IsDashing ? 0.6f : 0f);
        if (strength > 0.02f && _trailCount > 2)
        {
            int len = (int)(_trailCount * (0.35f + 0.65f * MathF.Min(1f, strength)));
            var hot = new Vector4(0.20f, 0.91f, 1f, 1f);
            var cool = Vector4.Lerp(p.Accent, new Vector4(0.35f, 0.24f, 0.82f, 1f), 0.65f);
            for (int i = 0; i < len - 1; i++)
            {
                float t = i / (float)(len - 1);
                float next = (i + 1f) / (len - 1);
                float width = (5f + w.Flow * 4f) * (1f - t);
                var c0 = M.WithAlpha(Vector4.Lerp(hot, cool, t), strength * (1f - t) * 0.48f);
                var c1 = M.WithAlpha(Vector4.Lerp(hot, cool, next), strength * (1f - next) * 0.48f);
                r.GradientLine(TrailL[i], TrailL[i + 1], width, c0, c1, true);
                r.GradientLine(TrailR[i], TrailR[i + 1], width, c0, c1, true);
                if (i < len / 3)
                {
                    r.GradientLine(TrailL[i], TrailL[i + 1], width * 0.25f, new Vector4(1, 1, 1, c0.W), new Vector4(1, 1, 1, c1.W), true);
                    r.GradientLine(TrailR[i], TrailR[i + 1], width * 0.25f, new Vector4(1, 1, 1, c0.W), new Vector4(1, 1, 1, c1.W), true);
                }
            }
        }
        // afterimages: ghost copies of the ship strung out behind it in flow, while dashing, or during Phase Sync
        _ghostTimer -= dt;
        bool ghosting = (speed > 0.75f && (w.Flow > 0.45f || player.IsDashing)) || player.PhaseSyncFlash > 0.15f;
        if (_ghostTimer <= 0f)
        {
            _ghostTimer = 0.045f;
            if (ghosting)
            {
                for (int i = Math.Min(_ghostCount, Ghosts.Length - 1); i > 0; i--) Ghosts[i] = Ghosts[i - 1];
                Ghosts[0] = ship;
                _ghostCount = Math.Min(Ghosts.Length, _ghostCount + 1);
            }
            else if (_ghostCount > 0) _ghostCount--;
        }
        for (int i = 1; i < _ghostCount; i++)
        {
            float a = (1f - i / (float)Ghosts.Length) * 0.22f;
            var gm = MaterialSet.ForHue(p.Accent, 0.85f, 0f, a);
            r.Meshes.Add(Models.Player, Ghosts[i], gm);
        }
    }

    private static void DrawPlayer(Renderer r, GameWorld w, Palette p, AudioState audio, float time)
    {
        var player = w.Player;
        if (w.Phase == GamePhase.Dead) return;
        float flicker = player.Invuln > 0 && !player.InvincibleDemo ? 0.55f + 0.45f * MathF.Sin(time * 34f) : 1f;
        Vector4 col = p.Player;
        float aim = M.AngleOf(player.AimDir);
        Vector2 pos = player.Pos;
        float speed = player.Vel.Length();

        // bank into strafes; a full barrel roll while dashing
        float lateral = Vector2.Dot(player.Vel, M.Perp(player.AimDir)) / 312f;
        float roll = M.Clamp(lateral, -1f, 1f) * 0.6f;
        if (player.IsDashing)
        {
            float t = 1f - M.Clamp01(player.DashTime / 0.16f);
            float side = Vector2.Dot(player.DashDir, M.Perp(player.AimDir)) >= 0 ? 1f : -1f;
            roll += t * M.Tau * side;
        }
        float z = 8f + MathF.Sin(time * 2.6f) * 1.2f;

        // readability: the ship sits on a dark disc with a crisp rim (its backdrop pool is added first in Draw)
        r.FillCircle(pos, 34f, new Vector4(0f, 0f, 0.01f, 0.55f), 24);
        r.FillCircle(pos + new Vector2(9, 12), 15f, Shadow, 16);
        r.FillCircle(pos, 30f, new Vector4(col.X, col.Y, col.Z, 0.05f * (player.IsDashing ? 2f : 1f)), 24, true);
        r.Circle(pos, 31f, 1.6f, new Vector4(0.85f, 1f, 1f, 0.42f * flicker), 32, true);

        var mats = MaterialSet.ForHue(col, (1f + (player.IsDashing ? 1f : 0f) + w.BeatPulse * 0.3f + player.PhaseSyncFlash * 0.65f) * flicker, player.HitFlash, 1f);
        mats.Hull = Vector4.Lerp(new Vector4(0.12f, 0.23f, 0.34f, 1f), Vector4.One, player.HitFlash);
        mats.Trim = Vector4.Lerp(new Vector4(0.53f, 0.72f, 0.80f, 1f), Vector4.One, player.HitFlash);
        mats.Glow = Vector4.Lerp(new Vector4(0.08f, 0.85f, 0.94f, 1f), Vector4.One, player.HitFlash);
        var ship = Xf(pos, z, aim, 1.35f, roll);
        DrawSpeedFx(r, w, p, time, ship, aim);
        r.Meshes.Add(Models.Player, ship, mats);

        float thrust = 0.35f + M.Clamp01(speed / 312f) * 0.9f + (player.IsDashing ? 1.4f : 0f) + player.PhaseSyncFlash * 0.45f;
        var flame = MaterialSet.ForHue(col, 1.15f * flicker);
        flame.Core = new Vector4(0.25f, 0.90f, 1f, 1f);
        for (int s = -1; s <= 1; s += 2)
        {
            float flick = 0.85f + 0.15f * MathF.Sin(time * 47f + s);
            var local = Matrix4x4.CreateScale(9f * thrust * flick, 2.7f, 2.7f) * Matrix4x4.CreateTranslation(-17f, s * 8.2f, 1.8f);
            r.Meshes.Add(Models.PlayerThruster, local * ship, flame);
        }

        // readability overlays: rings, the true hitbox and the aim guide stay flat and bright
        if (player.PhaseSyncFlash > 0.02f)
        {
            float sf = player.PhaseSyncFlash;
            float ringR = 28f + (1f - sf) * 18f;
            r.Circle(pos, ringR, 2f * sf, new Vector4(0.45f, 1f, 0.95f, 0.75f * sf), 28, true);
            r.Circle(pos, ringR * 0.72f, 1.2f * sf, new Vector4(1f, 0.86f, 0.42f, 0.5f * sf), 24, true);
        }
        if (player.VoidPhantomFlash > 0.02f || ((player.ActiveSynergies & SynergyFlags.VoidPhantom) != 0 && player.IsDashing))
        {
            float vf = MathF.Max(player.VoidPhantomFlash, player.IsDashing ? 0.85f : 0f);
            float haloR = 34f + (1f - vf) * 26f;
            r.Circle(pos, haloR, 2.2f * vf, new Vector4(0.75f, 0.45f, 1f, 0.72f * vf), 32, true);
            r.Arc(pos, haloR * 0.82f, time * 4.5f, 1.9f, 1.8f * vf, new Vector4(0.48f, 0.98f, 1f, 0.82f * vf), 14, true);
            r.Arc(pos, haloR * 0.82f, time * 4.5f + MathF.PI, 1.9f, 1.8f * vf, new Vector4(0.85f, 0.48f, 1f, 0.82f * vf), 14, true);
        }
        if (w.IsDropOverdrive || player.IsSyncOverdrive)
        {
            float odPulse = 0.75f + 0.25f * MathF.Sin(time * 12f);
            var odPrimary = w.IsDropOverdrive ? new Vector4(1f, 0.48f, 0.88f, 0.78f * odPulse) : new Vector4(0.45f, 1f, 0.95f, 0.78f * odPulse);
            var odGold = new Vector4(1f, 0.86f, 0.38f, 0.72f * odPulse);
            float odR = 42f + MathF.Sin(time * 8f) * 3f + w.BeatPulse * 5f;
            r.Arc(pos, odR, time * 3.2f, 1.65f, 2.2f, odPrimary, 16, true);
            r.Arc(pos, odR, time * 3.2f + MathF.PI, 1.65f, 2.2f, odGold, 16, true);
            if (w.IsDropOverdrive && player.IsSyncOverdrive)
                r.Circle(pos, odR + 6f, 1.4f, new Vector4(0.55f, 1f, 0.98f, 0.48f * odPulse), 36, true);
        }
        if (audio.DropAnticipation > 0.25f && !GameSettings.Current.ReduceFlashing)
        {
            float ant = M.Clamp01(audio.DropAnticipation);
            float antR = M.Lerp(210f, 36f, ant);
            r.Circle(pos, antR, 1.4f + ant * 1.4f, new Vector4(0.62f, 0.52f, 1f, 0.20f + ant * 0.55f), 40, true);
        }
        r.Circle(pos, 22f, 4.5f, M.WithAlpha(col, 0.08f * flicker), 30, true);
        r.Arc(pos, 25f, time * 1.6f, 2.2f, 1.2f, M.WithAlpha(p.Secondary, 0.35f * flicker), 12, true);
        r.FillCircle(pos, 4.2f, new Vector4(0f, 0f, 0f, 0.6f), 10);
        r.FillCircle(pos, 3.1f, new Vector4(1f, 1f, 1f, 0.98f), 8, true);
        if (player.Focus > 0.2f)
        {
            float f = player.Focus;
            r.Circle(pos, player.Radius, 1.2f, new Vector4(1f, 1f, 1f, 0.55f * f), 16, true);
            r.Circle(pos, 2.2f, 0.8f, new Vector4(1f, 0.35f, 0.5f, 0.85f * f), 8, true);
            r.Arc(pos, 30f, aim - 0.4f, 0.8f, 1.3f, M.WithAlpha(col, 0.5f * f), 10, true);
        }
        Vector2 aimEnd = pos + player.AimDir * 110f;
        float aimAlpha = 0.03f + w.BeatPulse * 0.06f;
        r.Line(pos + player.AimDir * 30f, aimEnd, 0.8f, M.WithAlpha(col, aimAlpha), true);

        // health pips under the ship, brighter right after a hit
        int max = Math.Max(1, player.MaxHealth);
        float recent = M.Clamp01(player.HealthDisplay - player.Health) + (player.Health <= 1 ? 0.5f + 0.5f * MathF.Sin(time * 8f) : 0f);
        float span = MathF.Min(1.9f, 0.42f * max);
        for (int i = 0; i < max; i++)
        {
            float t = max == 1 ? 0.5f : i / (float)(max - 1);
            float a0 = MathF.PI * 0.5f + (t - 0.5f) * span - 0.12f;
            bool full = i < player.Health;
            var pc = full
                ? (player.Health <= 1 ? new Vector4(1f, 0.3f, 0.35f, 0.9f) : M.WithAlpha(p.Accent, 0.55f + recent * 0.4f))
                : new Vector4(0.6f, 0.65f, 0.8f, 0.14f);
            r.Arc(pos, 36f, a0, 0.24f, full ? 3f : 2f, pc, 4, full);
        }
    }

    // ------------------------------------------------------------------ fx

    private static void DrawParticles(Renderer r, GameWorld w, Palette p)
    {
        foreach (var particle in w.Particles)
        {
            if (!r.InView(particle.Pos, 40)) continue;
            float t = M.Clamp01(particle.Life / MathF.Max(0.01f, particle.MaxLife));
            var col = M.WithAlpha(particle.Color, particle.Color.W * t);
            if (particle.Kind == ParticleKind.Spark)
            {
                r.Line(particle.PrevPos, particle.Pos, MathF.Max(0.6f, particle.Size * t), col, true);
            }
            else if (particle.Kind == ParticleKind.Shard)
            {
                Vector2 dir = M.SafeNorm(particle.Pos - particle.PrevPos);
                Vector2 n = M.Perp(dir) * particle.Size * t;
                r.Triangle(particle.Pos, particle.Pos - dir * particle.Size * 1.6f * t + n, particle.Pos - dir * particle.Size * 1.6f * t - n, col, true);
            }
            else if (particle.Kind == ParticleKind.Ring)
            {
                r.Circle(particle.Pos, particle.Size * (1.4f - t * 0.4f), MathF.Max(0.5f, 1.5f * t), col, 16, true);
            }
            else
            {
                r.FillCircle(particle.Pos, MathF.Max(0.3f, particle.Size * t), col, 8, true);
            }
        }
    }

    /// <summary>LEAD ship: a ribbon of light along the melody, white core, note-coloured sheath.</summary>
    private static void DrawLeadBeam(Renderer r, GameWorld w, float time)
    {
        var pts = w.LeadBeam;
        if (pts.Count < 2) return;
        var c = M.Hsv(w.LeadBeamHue, 0.6f, 1f);
        float power = w.LeadBeamPower;
        for (int i = 1; i < pts.Count; i++)
        {
            float f = 1f - i / (float)pts.Count * 0.6f;
            float shimmer = 0.8f + 0.2f * MathF.Sin(time * 40f - i * 0.9f);
            r.Line(pts[i - 1], pts[i], 46f * f, M.WithAlpha(c, 0.10f * power), true);
            r.Line(pts[i - 1], pts[i], 18f * f, M.WithAlpha(c, 0.42f * power * shimmer), true);
            r.Line(pts[i - 1], pts[i], 5f * f, new Vector4(1f, 1f, 1f, 0.9f * power), true);
        }
        r.FillCircle(pts[0], 16f, M.WithAlpha(c, 0.5f), 14, true);
        r.FillCircle(pts[^1], 10f, M.WithAlpha(c, 0.4f), 12, true);
    }

    /// <summary>THE DROP: a speaker-like core whose rings answer the kick, snare and hats.</summary>
    private static void DrawSongBoss(Renderer r, GameWorld w, Enemy e, Vector4 hue, float scale, float flash, float time, AudioState audio)
    {
        float R = e.Radius * scale * e.BossScale;
        Vector2 c = e.Pos;
        var hc = M.Hsv(e.Hue, 0.65f, 1f);
        var hc2 = M.Hsv((e.Hue + 0.5f) % 1f, 0.6f, 1f);
        float kick = MathF.Max(audio.PercLow, audio.KickPulse);
        float snare = MathF.Max(audio.PercMid, audio.SnarePulse);
        float hat = MathF.Max(audio.PercHigh, audio.HatPulse);
        // dark body and cone
        r.FillCircle(c, R * 1.05f, new Vector4(0.02f, 0.02f, 0.05f, 0.92f), 48);
        r.FillCircle(c, R * (0.55f + kick * 0.12f), M.WithAlpha(hc, 0.25f + kick * 0.5f), 40, true);
        r.FillCircle(c, R * (0.22f + kick * 0.08f), new Vector4(1f, 1f, 1f, 0.75f + kick * 0.25f), 28, true);
        // kick: the cone throws rings outward
        for (int k = 0; k < 3; k++)
        {
            float ph = (time * 1.6f + k / 3f) % 1f;
            r.Circle(c, R * (0.6f + ph * 1.1f), 2.5f * (1f - ph), M.WithAlpha(hc, (1f - ph) * (0.25f + kick * 0.6f)), 48, true);
        }
        // snare ring: segments that flash
        int segs = 12;
        for (int i = 0; i < segs; i++)
        {
            float a0 = e.Angle + i * M.Tau / segs;
            float lit = 0.25f + snare * 0.75f * (0.5f + 0.5f * MathF.Sin(i * 1.7f + time * 8f));
            r.Arc(c, R * 1.18f, a0, M.Tau / segs * 0.7f, 5f, M.WithAlpha(hc2, lit), 8, true);
        }
        // hat ring: fine counter-rotating ticks
        for (int i = 0; i < 24; i++)
        {
            float a = -e.Angle * 1.6f + i * M.Tau / 24f;
            Vector2 dir = M.FromAngle(a);
            float len = 8f + hat * 16f * (i % 2 == 0 ? 1f : 0.5f);
            r.Line(c + dir * R * 1.38f, c + dir * (R * 1.38f + len), 1.6f, M.WithAlpha(Vector4.One, 0.25f + hat * 0.6f), true);
        }
        r.Circle(c, R * 1.05f, 2f, M.WithAlpha(Vector4.Lerp(hc, Vector4.One, flash), 0.8f), 48, true);
        if (e.BossInvuln > 0f)
            r.Circle(c, R * 1.6f, 3f, new Vector4(1f, 1f, 1f, 0.4f + 0.3f * MathF.Sin(time * 20f)), 48, true);
    }

    // ------------------------------------------------------------------ music fields

    /// <summary>Bass gravity wells: a dark lens with a spinning accretion ring and inflowing sparks.</summary>
    private static void DrawWells(Renderer r, GameWorld w, float time)
    {
        foreach (var g in w.Wells)
        {
            if (!r.InView(g.Pos, g.Radius)) continue;
            float s = g.Strength;
            var hue = M.Hsv(g.Hue, 0.65f, 1f);
            r.AddBackdropFocus(g.Pos, g.Radius * 0.7f, 0.55f * s);
            // influence edge
            r.Circle(g.Pos, g.Radius, 1.2f, M.WithAlpha(hue, 0.07f + s * 0.06f), 64, true);
            // The black lens is face-on. The accretion disk tilts independently around it.
            r.FillCircle(g.Pos, g.Core, new Vector4(0.006f, 0.009f, 0.025f, 0.97f), 32);
            Vector2 major = M.FromAngle(-0.42f + (g.Hue - 0.5f) * 0.7f);
            Vector2 minor = M.Perp(major) * 0.62f;
            var ember = Vector4.Lerp(hue, new Vector4(1f, 0.76f, 0.40f, 1f), 0.55f);
            for (int arm = 0; arm < 3; arm++)
            {
                Vector2 prev = default;
                Vector4 prevColor = default;
                for (int j = 0; j <= 28; j++)
                {
                    float t = j / 28f;
                    float angle = g.Spin + arm * M.Tau / 3f + t * 3.7f;
                    float radius = g.Core * (2.85f - 1.32f * t);
                    Vector2 pos = g.Pos + radius * (major * MathF.Cos(angle) + minor * MathF.Sin(angle));
                    float taper = MathF.Sin(t * MathF.PI);
                    var color = M.WithAlpha(Vector4.Lerp(hue, ember, t), taper * (0.46f + 0.36f * s));
                    if (j > 0)
                        r.GradientLine(prev, pos, 1.2f + taper * (2.8f + 1.8f * s), prevColor, color, true);
                    prev = pos;
                    prevColor = color;
                }
            }
            r.Circle(g.Pos, g.Core * 1.05f, 1.8f, M.WithAlpha(ember, 0.50f + s * 0.3f), 32, true);
            // sparks falling in
            for (int k = 0; k < 10; k++)
            {
                float ph = (time * (0.6f + s) + k * 0.1f) % 1f;
                float ang = k * 2.39996f + g.Spin * 0.5f + ph * 2.5f;
                float rad = g.Radius * 0.8f * (1f - ph) + g.Core;
                r.FillCircle(g.Pos + M.FromAngle(ang, rad), 2.2f, M.WithAlpha(hue, 0.6f * ph * s), 6, true);
            }
        }
    }

    /// <summary>Melody rails: a glowing ribbon coloured note by note, fading from its tail.</summary>
    private static void DrawRails(Renderer r, GameWorld w, float time)
    {
        bool riding = w.Player.RailRiding;
        foreach (var rail in w.Rails)
        {
            var pts = rail.Points;
            for (int k = 1; k < pts.Count; k++)
            {
                if (!r.InView(pts[k], 80f)) continue;
                float f0 = 1f - rail.Ages[k - 1] / GameWorld.RailLife, f1 = 1f - rail.Ages[k] / GameWorld.RailLife;
                f0 = MathF.Min(1f, f0 * 2.5f); f1 = MathF.Min(1f, f1 * 2.5f);
                var c0 = M.Hsv(rail.Hues[k - 1], 0.6f, 1f);
                var c1 = M.Hsv(rail.Hues[k], 0.6f, 1f);
                float shimmer = 0.85f + 0.15f * MathF.Sin(time * 9f - k * 0.5f);
                r.GradientLine(pts[k - 1], pts[k], 34f, M.WithAlpha(c0, 0.10f * f0), M.WithAlpha(c1, 0.10f * f1), true);
                r.GradientLine(pts[k - 1], pts[k], 10f, M.WithAlpha(c0, 0.45f * f0 * shimmer), M.WithAlpha(c1, 0.45f * f1 * shimmer), true);
                r.GradientLine(pts[k - 1], pts[k], 3f, new Vector4(1f, 1f, 1f, 0.8f * f0), new Vector4(1f, 1f, 1f, 0.8f * f1), true);
            }
            if (rail.Growing && pts.Count > 0)
            {
                var hc = M.Hsv(rail.Hues[^1], 0.5f, 1f);
                r.FillCircle(pts[^1], 16f, M.WithAlpha(hc, 0.25f), 16, true);
                r.FillCircle(pts[^1], 6f, new Vector4(1f, 1f, 1f, 0.95f), 10, true);
            }
        }
        if (riding)
        {
            var p = w.Player;
            r.Circle(p.Pos, 40f + MathF.Sin(time * 20f) * 3f, 2f, new Vector4(1f, 1f, 1f, 0.5f), 32, true);
        }
    }

    private static void DrawShockwaves(Renderer r, GameWorld w)
    {
        foreach (var s in w.Shockwaves)
        {
            // big impacts (drops, overdrive, bombs, kills of big enemies) roll through the 3D world too
            if (!s.BackdropPulsed)
            {
                s.BackdropPulsed = true;
                if (s.Heavy) r.PulseBackdrop(s.Pos, 1.3f);
                else if (s.MaxRadius >= 180f) r.PulseBackdrop(s.Pos, MathF.Min(0.9f, s.MaxRadius / 600f));
            }
            float t = M.Clamp01(s.Life / MathF.Max(0.01f, s.MaxLife));
            if (s.Heavy)
            {
                float prog = 1f - t;
                float env = MathF.Sin(prog * MathF.PI) * (1f - prog * 0.35f);
                r.AddScreenShockwave(s.Pos, s.Radius, env * 0.85f);
            }
            var col = M.WithAlpha(s.Color, s.Color.W * t * 0.65f);
            if (s.Filled)
                r.FillCircle(s.Pos, s.Radius, M.WithAlpha(s.Color, 0.012f * t), 48, true);
            r.Circle(s.Pos, s.Radius, MathF.Max(0.6f, s.Width * (0.25f + t * 0.9f)), col, 48, true);
            r.Circle(s.Pos, s.Radius * 0.94f, MathF.Max(0.4f, s.Width * t * 0.3f), M.WithAlpha(s.Color, col.W * 0.7f), 40, true);
        }
    }
}
