using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.Render;
using Nullwave.World;
using GameWorld = Nullwave.World.World;

namespace Nullwave.UI;

public static class Screens
{
    private static readonly string[] TitleItems = ["NEW SIGNAL", "DAILY SIGNAL", "ARCHIVE", "SETTINGS", "QUIT"];
    private static readonly string[] PauseItems = ["RESUME", "ARCHIVE", "SETTINGS", "ABANDON SIGNAL"];

    public static void DrawTitle(Renderer r, Palette p, MetaProgress meta, ulong seed, bool dailySeed, int menuIndex, float time, AudioState audio)
    {
        r.UiRect(0, 0, 1600, 900, new Vector4(0.002f, 0.004f, 0.016f, 0.86f));
        DrawFrameDecor(r, p, 38, 38, 1524, 824);

        r.UiText("SIGNAL // " + SeedCode.ToCode(seed), 64, 62, 1.8f, M.WithAlpha(p.Accent, 0.9f));
        r.UiTextRight("ARCHIVE " + meta.UnlockedModifiers.Count.ToString("00") + " / " + ModifierCatalog.All.Length.ToString("00"), 1536, 62, 1.8f, M.WithAlpha(p.Dim, 0.9f));

        float y = 178;
        r.UiTextCentered("NULLWAVE", 800 + 5, y + 4, 15f, M.WithAlpha(p.Secondary, 0.2f), true);
        r.UiTextCentered("NULLWAVE", 800 - 4, y - 2, 15f, M.WithAlpha(p.Danger, 0.18f), true);
        r.UiTextCentered("NULLWAVE", 800, y, 15f, p.Text, true);
        r.UiTextCentered("THE MUSIC BUILDS THE ENEMY", 800, y + 130, 2.3f, M.WithAlpha(p.Accent, 0.94f), true);
        r.UiLine(540, y + 165, 1060, y + 165, 1f, M.WithAlpha(p.Primary, 0.34f), true);
        r.UiTextCentered("AN ENDLESS AUDIO-DRIVEN BULLET HELL", 800, y + 184, 1.65f, p.Dim);

        float menuY = 452;
        for (int i = 0; i < TitleItems.Length; i++)
        {
            bool selected = i == menuIndex;
            float x = 580;
            float w = 440;
            if (selected)
            {
                r.UiRect(x, menuY - 12, w, 47, M.WithAlpha(p.Primary, 0.075f));
                r.UiFrame(x, menuY - 12, w, 47, 1f, M.WithAlpha(p.Primary, 0.55f), true);
                r.UiText(">", x + 18, menuY + 2, 3f, p.Accent, true);
                r.UiText("<", x + w - 42, menuY + 2, 3f, p.Accent, true);
            }
            r.UiTextCentered(TitleItems[i], 800, menuY + 2, selected ? 3.2f : 2.9f, selected ? p.Text : M.WithAlpha(p.Dim, 0.9f), selected);
            menuY += 56;
        }

        r.UiText("BEST WAVE " + meta.BestWave.ToString("00") + "   //   BEST SCORE " + FormatScore(meta.BestScore), 64, 760, 1.75f, M.WithAlpha(p.Dim, 0.85f));
        r.UiText("MOVE  WASD / STICK     AIM  MOUSE / STICK     FIRE  LMB / RT", 64, 794, 1.55f, M.WithAlpha(p.Dim, 0.68f));
        r.UiText("DASH  SPACE / A     RESONANCE  E / X     FOCUS  SHIFT / LB", 64, 818, 1.55f, M.WithAlpha(p.Dim, 0.68f));
        r.UiTextRight("AUDIO // " + SourceLabel(audio), 1536, 774, 1.55f, M.WithAlpha(audio.External ? p.Accent : p.Dim, 0.9f));

        DrawSpectrum(r, p, audio, 1090, 850, 420, 44);
        if (dailySeed)
            r.UiTextCentered("DAILY SIGNAL // " + SeedCode.DailyLabel(DateTime.Now), 800, 420, 1.6f, M.WithAlpha(p.Accent, 0.9f));
    }

    public static void DrawArchive(Renderer r, Palette p, MetaProgress meta, int selected, float time, bool fromPause, bool fromGameOver)
    {
        r.UiRect(0, 0, 1600, 900, new Vector4(0.003f, 0.005f, 0.014f, 0.84f));
        DrawFrameDecor(r, p, 34, 34, 1532, 832);
        r.UiText("ARCHIVE OF THE SIGNAL", 64, 54, 4.4f, p.Text, true);
        r.UiText("UNLOCKED SIGNALS CHANGE THE RULES. STACK THEM FOR A HIGHER SCORE.", 66, 112, 1.65f, p.Dim);
        string back = fromPause ? "BACK TO PAUSE" : fromGameOver ? "BACK TO SIGNAL LOST" : "BACK TO TITLE";
        r.UiTextRight("ESC / B // " + back, 1534, 64, 1.55f, M.WithAlpha(p.Dim, 0.9f));
        r.UiText("BEST WAVE " + meta.BestWave.ToString("00") + "   BEST " + FormatScore(meta.BestScore) + "   BOSSES " + meta.TotalBossKills + "   GRAZES " + meta.TotalGrazes + "   RUNS " + meta.Runs, 66, 134, 1.7f, M.WithAlpha(p.Accent, 0.85f));
        r.UiTextRight("GAIN x" + meta.SelectedMultiplier().ToString("0.00"), 1534, 134, 2.2f, M.WithAlpha(p.Accent, 0.9f), true);

        float startX = 50;
        float startY = 176;
        float colW = 748;
        float rowH = 78;
        for (int i = 0; i < ModifierCatalog.All.Length; i++)
        {
            var def = ModifierCatalog.All[i];
            int col = i / 8;
            int row = i % 8;
            float x = startX + col * (colW + 22);
            float y = startY + row * rowH;
            bool unlocked = meta.IsModifierUnlocked(def.Id);
            bool on = meta.SelectedModifiers.Contains(def.Id);
            bool sel = i == selected;
            var accent = unlocked ? M.Hsv((p.BaseHue + i * 0.043f) % 1f, 0.55f, 1f) : M.WithAlpha(p.Dim, 0.55f);
            if (sel)
            {
                r.UiRect(x - 10, y - 7, colW + 8, rowH - 8, M.WithAlpha(p.Primary, 0.075f));
                r.UiFrame(x - 10, y - 7, colW + 8, rowH - 8, 1f, M.WithAlpha(p.Primary, 0.75f), true);
            }
            r.UiText(unlocked ? (on ? "[ON ] " : "[OFF] ") : "[ -- ] ", x, y, 1.8f, accent, unlocked && on);
            float nameX = x + (unlocked ? 72f : 82f);
            r.UiText(def.Name, nameX, y - 1, 2.55f, unlocked ? (on ? p.Text : M.WithAlpha(p.Text, 0.72f)) : M.WithAlpha(p.Dim, 0.55f), sel && unlocked);
            r.UiTextRight("x" + def.Multiplier.ToString("0.00"), x + colW - 18, y + 1, 2.1f, unlocked ? accent : M.WithAlpha(p.Dim, 0.45f));
            string detail = unlocked ? def.Desc : def.UnlockText;
            var color = unlocked ? M.WithAlpha(p.Dim, 0.88f) : M.WithAlpha(p.Danger, 0.62f);
            DrawWrapped(r, detail, nameX, y + 29, 1.55f, x + colW - nameX - 20, color, 17f, 1);
        }

        r.UiLine(50, 798, 1550, 798, 1f, M.WithAlpha(p.Primary, 0.22f));
        string footer = fromPause
            ? "MODIFIERS LOCKED DURING A RUN // ABANDON OR FINISH TO CHANGE THEM"
            : "W / S OR ARROWS SELECT     LEFT / RIGHT SWITCH COLUMN     SPACE / A TOGGLE";
        r.UiText(footer, 64, 818, 1.6f, M.WithAlpha(p.Dim, 0.86f));
        r.UiTextRight("ATTUNEMENTS " + (6 + meta.UnlockedAttunements.Count).ToString("00") + " / " + AttunementCatalog.All.Length.ToString("00"), 1534, 818, 1.6f, M.WithAlpha(p.Accent, 0.8f));
        r.UiText("MODIFIERS ARE UNLOCKED BY PLAYING.  LOCKED SIGNALS SHOW THEIR CONDITION.", 64, 846, 1.4f, M.WithAlpha(p.Dim, 0.55f));
    }

    public static void DrawHud(Renderer r, GameWorld w, Palette p, AudioState audio, float time, float fps, bool debug, bool showEvent = true)
    {
        var player = w.Player;
        r.UiText("WAVE " + w.Wave.ToString("00"), 56, 38, 3.1f, p.Text, true);
        r.UiText("MOVEMENT " + Roman(w.Movement + 1) + " // " + w.Rules.Name, 58, 78, 1.65f, M.WithAlpha(p.Accent, 0.9f));
        r.UiText(FormatScore(w.Score), 56, 108, 4.2f, p.Text, true);
        if (w.Combo > 0)
        {
            float alpha = M.Clamp01(w.ComboTimer / 1.2f);
            r.UiText("x" + w.ComboMultiplier.ToString("0.00") + "  //  " + w.Combo + " CHAIN", 58, 164, 1.9f, M.WithAlpha(p.Accent, 0.55f + alpha * 0.45f), true);
        }
        if (w.HypeLevel > 0)
        {
            var hc = M.Hsv((0.85f - w.HypeLevel * 0.1f + 1f) % 1f, 0.6f, 1f, 0.6f + w.Hype * 0.4f);
            r.UiTextRight("HYPE " + new string('|', w.HypeLevel + 1), 520, 116, 2.2f, hc, true);
        }
        if (w.Mods.Active.Count > 0)
        {
            string mods;
            if (w.Mods.Active.Count <= 3)
                mods = string.Join("  ", w.Mods.Active.Select(id => ModifierCatalog.Get(id)?.Name ?? id));
            else
                mods = string.Join("  ", w.Mods.Active.Take(2).Select(id => ModifierCatalog.Get(id)?.Name ?? id)) + "  +" + (w.Mods.Active.Count - 2);
            r.UiText("SIGNAL " + mods, 58, 190, 1.3f, M.WithAlpha(p.Dim, 0.65f));
        }

        r.UiTextRight("BPM " + audio.Bpm.ToString("000"), 1542, 40, 2.2f, M.WithAlpha(p.Accent, 0.85f));
        r.UiTextRight("AUDIO // " + SourceLabel(audio), 1542, 72, 1.45f, M.WithAlpha(p.Dim, 0.85f));
        DrawSpectrum(r, p, audio, 1118, 188, 424, 74);

        if (w.BossEnemy is { Dead: false } boss)
        {
            float bx = 380, by = 34, bw = 840, bh = 18;
            r.UiTextCentered("THE CHOIR // PHASE " + (boss.BossPhase + 1) + " OF " + Math.Max(3, w.Mods.BossPhases), 800, by - 24, 1.7f, M.WithAlpha(p.Text, 0.9f), true);
            r.UiRect(bx, by, bw, bh, new Vector4(0.02f, 0.01f, 0.03f, 0.84f));
            r.UiFrame(bx, by, bw, bh, 1f, M.WithAlpha(p.Danger, 0.5f));
            r.UiRect(bx + 2, by + 2, (bw - 4) * boss.HealthFrac, bh - 4, M.WithAlpha(p.Danger, 0.8f), true);
            for (int i = 1; i < Math.Max(3, w.Mods.BossPhases); i++)
                r.UiRect(bx + bw * i / Math.Max(3, w.Mods.BossPhases) - 1, by, 2, bh, new Vector4(0, 0, 0, 0.7f));
        }

        DrawHealthBar(r, p, player, time);
        var settings = GameSettings.Current;
        if (settings.Minimap) DrawMinimap(r, p, w, time);
        if (settings.ThreatArrows) DrawThreatIndicators(r, p, w, time);
        if (settings.ShowFps) r.UiTextRight("FPS " + fps.ToString("0"), 1542, 790, 1.4f, M.WithAlpha(p.Dim, 0.85f));
        r.UiText("RESONANCE", 56, 836, 1.4f, M.WithAlpha(p.Dim, 0.9f));
        float resFrac = M.Clamp01(player.Resonance / player.ResonanceMax);
        Vector4 resColor = resFrac >= 1f ? p.Accent : p.Primary;
        r.UiRect(56, 856, 370, 8, new Vector4(0.02f, 0.02f, 0.04f, 0.85f));
        r.UiRect(56, 856, 370 * resFrac, 8, M.WithAlpha(resColor, 0.92f), resFrac >= 1f);
        for (int i = 1; i < 4; i++)
            r.UiRect(56 + 370 * i / 4f, 855, 1.5f, 10, new Vector4(0.01f, 0.01f, 0.03f, 0.9f));
        if (resFrac >= 1f)
            r.UiText("E / X  //  CASCADE READY", 56, 874, 1.35f, p.Accent, true);
        else
            r.UiText((int)player.Resonance + " / " + (int)player.ResonanceMax, 440, 852, 1.3f, M.WithAlpha(p.Dim, 0.8f));

        DrawFlowMeter(r, p, w, time);

        if (time < 16f)
        {
            float a = 0.45f + 0.55f * (1f - time / 16f);
            r.UiTextRight("WASD MOVE   MOUSE AIM   LMB FIRE", 1542, 812, 1.45f, M.WithAlpha(p.Dim, a));
            r.UiTextRight("SPACE DASH   SHIFT FOCUS   E CASCADE", 1542, 834, 1.45f, M.WithAlpha(p.Dim, a));
        }
        else
        {
            r.UiTextRight("F1 DIAGNOSTICS   F2 FULLSCREEN   F3 AUDIO SOURCE", 1542, 842, 1.25f, M.WithAlpha(p.Dim, 0.35f));
        }

        if (showEvent && w.EventTimer > 0 && w.EventText.Length > 0)
        {
            float a = M.Clamp01(w.EventTimer / 0.5f);
            r.UiTextCentered(w.EventText, 800, 238, 2.4f, M.WithAlpha(p.Text, a * 0.85f), true);
        }

        DrawFloatingTexts(r, w, p);
        if (debug)
        {
            r.UiText("FPS " + fps.ToString("0") + "   ENEMY " + w.Enemies.Count + "   BULLET " + (w.EnemyBullets.Count + w.PlayerBullets.Count) + "   FX " + w.Particles.Count, 58, 224, 1.3f, M.WithAlpha(p.Dim, 0.75f));
        }
    }

    /// <summary>
    /// Segmented integrity bar. Lost health drains out of the bar as a bright "ghost" chunk so a
    /// hit is legible at a glance; at one point left the frame pulses red.
    /// </summary>
    private static void DrawHealthBar(Renderer r, Palette p, World.Player player, float time)
    {
        const float x = 56, y = 790, w = 370, h = 16;
        int max = Math.Max(1, player.MaxHealth);
        bool critical = player.Health <= 1 && player.Health > 0;
        float pulse = critical ? 0.5f + 0.5f * MathF.Sin(time * 9f) : 0f;
        var healthy = new Vector4(0.42f, 1f, 0.72f, 1f);
        var hurt = new Vector4(1f, 0.78f, 0.32f, 1f);
        var danger = new Vector4(1f, 0.25f, 0.32f, 1f);
        float frac = player.Health / (float)max;
        Vector4 fill = frac > 0.5f ? Vector4.Lerp(hurt, healthy, (frac - 0.5f) * 2f) : Vector4.Lerp(danger, hurt, frac * 2f);

        r.UiText("INTEGRITY", 56, 770, 1.4f, critical ? M.WithAlpha(danger, 0.7f + pulse * 0.3f) : M.WithAlpha(p.Dim, 0.9f), critical);
        r.UiTextRight(player.Health + " / " + max, x + w, 770, 1.4f, M.WithAlpha(fill, 0.9f), true);
        r.UiRect(x - 2, y - 2, w + 4, h + 4, new Vector4(0.01f, 0.012f, 0.03f, 0.88f));
        r.UiFrame(x - 2, y - 2, w + 4, h + 4, 1f, critical ? M.WithAlpha(danger, 0.35f + pulse * 0.6f) : M.WithAlpha(p.Primary, 0.35f), critical);

        float gap = 4f;
        float segW = (w - gap * (max - 1)) / max;
        for (int i = 0; i < max; i++)
        {
            float sx = x + i * (segW + gap);
            if (i < player.Health)
            {
                r.UiRect(sx, y, segW, h, M.WithAlpha(fill, 0.88f), true);
                r.UiRect(sx, y, segW, 3f, new Vector4(1f, 1f, 1f, 0.35f));
            }
            else
            {
                // the ghost of health just lost, draining away
                float ghost = M.Clamp01(player.HealthDisplay - i);
                if (ghost > 0.01f)
                    r.UiRect(sx, y, segW * ghost, h, new Vector4(1f, 0.92f, 0.92f, 0.75f * ghost + 0.15f), true);
                r.UiFrame(sx, y, segW, h, 1f, M.WithAlpha(p.Dim, 0.22f));
            }
        }
    }

    /// <summary>Minimap of the (now much larger) arena: monoliths, threats, pickups and the view.</summary>
    private static void DrawMinimap(Renderer r, Palette p, GameWorld w, float time)
    {
        const float mw = 220f;
        float mh = mw * w.ArenaH / MathF.Max(1f, w.ArenaW);
        float x = 1542f - mw, y = 212f;
        r.UiRect(x, y, mw, mh, new Vector4(0.004f, 0.006f, 0.016f, 0.72f));
        r.UiFrame(x, y, mw, mh, 1f, M.WithAlpha(p.Primary, 0.3f));
        r.UiText(w.LayoutName, x, y + mh + 6, 1.2f, M.WithAlpha(p.Dim, 0.6f));
        Vector2 Map(Vector2 wp) => new(x + (wp.X / w.ArenaW + 0.5f) * mw, y + (wp.Y / w.ArenaH + 0.5f) * mh);

        foreach (var o in w.Obstacles)
        {
            if (o.Dead) continue;
            Vector2 m = Map(o.Pos);
            float s = MathF.Max(2f, o.Radius / w.ArenaW * mw * 1.6f);
            r.UiRect(m.X - s * 0.5f, m.Y - s * 0.5f, s, s, M.Hsv(o.Hue, 0.45f, 0.7f, 0.55f));
        }
        foreach (var pk in w.Pickups)
        {
            Vector2 m = Map(pk.Pos);
            r.UiRect(m.X - 1.5f, m.Y - 1.5f, 3f, 3f, pk.Kind == PickupKind.Health ? new Vector4(0.45f, 1f, 0.68f, 0.9f) : new Vector4(0.4f, 0.9f, 1f, 0.7f), true);
        }
        foreach (var e in w.Enemies)
        {
            if (e.Dead) continue;
            Vector2 m = Map(e.Pos);
            float s = e.Boss ? 7f : e.Elite ? 4f : 3f;
            var c = e.Boss ? new Vector4(1f, 0.3f, 0.5f, 1f) : M.Hsv(e.Hue, 0.7f, 1f, e.Spawning ? 0.4f : 0.95f);
            r.UiRect(m.X - s * 0.5f, m.Y - s * 0.5f, s, s, c, true);
        }
        // camera rectangle
        Vector2 cam = r.Camera, view = r.ViewExtents;
        Vector2 a = Map(cam - view), b = Map(cam + view);
        a = new Vector2(MathF.Max(x, a.X), MathF.Max(y, a.Y));
        b = new Vector2(MathF.Min(x + mw, b.X), MathF.Min(y + mh, b.Y));
        if (b.X > a.X && b.Y > a.Y) r.UiFrame(a.X, a.Y, b.X - a.X, b.Y - a.Y, 1f, M.WithAlpha(p.Text, 0.22f));
        Vector2 pm = Map(w.Player.Pos);
        Vector2 d = w.Player.AimDir;
        float blink = 0.75f + 0.25f * MathF.Sin(time * 6f);
        r.UiLine(pm.X, pm.Y, pm.X + d.X * 7f, pm.Y + d.Y * 7f, 1.6f, M.WithAlpha(p.Player, blink), true);
        r.UiRect(pm.X - 2f, pm.Y - 2f, 4f, 4f, new Vector4(1f, 1f, 1f, blink), true);
    }

    /// <summary>Edge arrows pointing at enemies outside the view, nearer = brighter.</summary>
    private static void DrawThreatIndicators(Renderer r, Palette p, GameWorld w, float time)
    {
        const float cx = 800f, cy = 450f, ex = 770f, ey = 415f;
        foreach (var e in w.Enemies)
        {
            if (e.Dead || r.InView(e.Pos, -20f)) continue;
            Vector2 ui = r.WorldToUi(e.Pos);
            Vector2 d = ui - new Vector2(cx, cy);
            if (d.LengthSquared() < 1f) continue;
            float k = MathF.Min(ex / MathF.Max(0.001f, MathF.Abs(d.X)), ey / MathF.Max(0.001f, MathF.Abs(d.Y)));
            Vector2 at = new Vector2(cx, cy) + d * k;
            float dist = M.Dist(e.Pos, w.Player.Pos);
            float near = M.Clamp01(1f - (dist - 700f) / 1800f);
            if (near <= 0.02f && !e.Boss) continue;
            Vector2 n = Vector2.Normalize(d);
            Vector2 side = new(-n.Y, n.X);
            float s = e.Boss ? 18f : e.Elite ? 13f : 10f;
            var c = e.Boss ? new Vector4(1f, 0.3f, 0.5f, 0.9f) : M.Hsv(e.Hue, 0.7f, 1f, 0.25f + near * 0.6f);
            Vector2 tip = at, l = at - n * s * 1.5f + side * s * 0.7f, rr = at - n * s * 1.5f - side * s * 0.7f;
            r.UiLine(tip.X, tip.Y, l.X, l.Y, 2.4f, c, true);
            r.UiLine(tip.X, tip.Y, rr.X, rr.Y, 2.4f, c, true);
            if (e.Telegraph > 0.5f)
                r.UiCircle(tip.X - n.X * s, tip.Y - n.Y * s, s * 0.5f * (2f - e.Telegraph), 1f, c, 12, true);
        }
    }

    public static string SourceText(AudioState audio) => SourceLabel(audio);

    /// <summary>FLOW meter, bottom centre: speed bar with the score multiplier it grants.</summary>
    private static void DrawFlowMeter(Renderer r, Palette p, GameWorld w, float time)
    {
        float flow = w.Flow;
        float speed = M.Clamp01(w.Player.SpeedFrac);
        const float x = 560, y = 846, wdt = 480, h = 10;
        bool hot = flow > 0.85f;
        var col = Vector4.Lerp(M.WithAlpha(p.Primary, 0.9f), new Vector4(1f, 0.55f, 0.95f, 1f), M.Clamp01((flow - 0.5f) * 2f));
        r.UiTextCentered(hot ? "// FLOW STATE //" : "FLOW", 800, 824, 1.45f, hot ? M.WithAlpha(col, 0.75f + 0.25f * MathF.Sin(time * 10f)) : M.WithAlpha(p.Dim, 0.8f), hot);
        r.UiRect(x - 2, y - 2, wdt + 4, h + 4, new Vector4(0.01f, 0.012f, 0.03f, 0.85f));
        r.UiRect(x, y, wdt * flow, h, M.WithAlpha(col, 0.9f), true);
        // speed tick: where your current speed sits (keep it right of the line to build flow)
        r.UiRect(x + wdt * 0.7f - 1, y - 4, 2, h + 8, M.WithAlpha(p.Dim, 0.5f));
        r.UiRect(x + wdt * speed - 2, y - 5, 4, h + 10, new Vector4(1f, 1f, 1f, 0.85f), true);
        r.UiTextRight("x" + (1f + flow * 1.5f).ToString("0.00"), x + wdt, 862, 1.3f, M.WithAlpha(col, 0.85f), hot);
        r.UiText("STAY FAST // GRAZE // KILL", x, 862, 1.2f, M.WithAlpha(p.Dim, 0.45f));
    }

    public static void DrawPause(Renderer r, Palette p, GameWorld w, int menuIndex, AudioState audio)
    {
        r.UiRect(0, 0, 1600, 900, new Vector4(0.002f, 0.004f, 0.012f, 0.72f));
        DrawCard(r, p, 500, 220, 600, 500, M.WithAlpha(p.Primary, 0.75f));
        r.UiTextCentered("SIGNAL HELD", 800, 260, 5.2f, p.Text, true);
        r.UiTextCentered("WAVE " + w.Wave.ToString("00") + "  //  " + SeedCode.ToCode(w.Seed), 800, 326, 1.7f, M.WithAlpha(p.Dim, 0.9f));
        r.UiLine(560, 362, 1040, 362, 1f, M.WithAlpha(p.Primary, 0.3f));
        for (int i = 0; i < PauseItems.Length; i++)
        {
            bool sel = i == menuIndex;
            float y = 388 + i * 58;
            if (sel)
            {
                r.UiRect(610, y - 7, 380, 44, M.WithAlpha(p.Primary, 0.08f));
                r.UiFrame(610, y - 7, 380, 44, 1f, M.WithAlpha(p.Primary, 0.6f), true);
            }
            r.UiTextCentered(PauseItems[i], 800, y, sel ? 2.9f : 2.5f, sel ? p.Text : M.WithAlpha(p.Dim, 0.9f), sel);
        }
        r.UiText("AUDIO // " + SourceLabel(audio), 546, 646, 1.45f, M.WithAlpha(p.Dim, 0.75f));
        r.UiTextRight("P / START // RESUME", 1054, 646, 1.35f, M.WithAlpha(p.Dim, 0.65f));
    }

    public static void DrawAttunement(Renderer r, Palette p, GameWorld w, float time, int hover)
    {
        r.UiRect(0, 0, 1600, 900, new Vector4(0.002f, 0.004f, 0.012f, 0.76f));
        r.UiTextCentered("ATTUNE // SELECT A SIGNAL", 800, 204, 4.8f, p.Text, true);
        r.UiTextCentered("THE ARENA IS LISTENING.  CHOOSE ONE.", 800, 262, 1.65f, M.WithAlpha(p.Dim, 0.9f));
        for (int i = 0; i < w.Offers.Count; i++)
        {
            var offer = w.Offers[i];
            float x = 160 + i * 438;
            float y = 326;
            float cw = 404;
            float ch = 330;
            bool sel = hover == i;
            var accent = M.Hsv((p.BaseHue + 0.16f * i + offer.Rarity * 0.08f) % 1f, 0.62f, 1f);
            DrawCard(r, p, x, y, cw, ch, sel ? p.Text : accent);
            if (sel)
                r.UiRect(x, y, cw, ch, M.WithAlpha(accent, 0.055f), true);
            r.UiText((i + 1).ToString(), x + 24, y + 20, 2.4f, accent, true);
            r.UiText("SIGNAL " + offer.Rarity, x + cw - 108, y + 23, 1.3f, M.WithAlpha(p.Dim, 0.7f));
            DrawWrapped(r, offer.Name, x + 24, y + 62, 3.0f, cw - 48, p.Text, 34f, 2, true);
            r.UiLine(x + 24, y + 144, x + cw - 24, y + 144, 1f, M.WithAlpha(accent, 0.32f));
            DrawWrapped(r, offer.Desc, x + 24, y + 168, 1.85f, cw - 48, M.WithAlpha(p.Dim, 0.96f), 24f, 3);
            r.UiText("STACK " + offer.NextLevel + " / " + offer.MaxStacks, x + 24, y + ch - 42, 1.55f, M.WithAlpha(accent, 0.9f));
            for (int k = 0; k < offer.MaxStacks; k++)
            {
                float bx = x + cw - 30 - k * 18;
                r.UiRect(bx, y + ch - 42, 11, 11, k < offer.NextLevel ? M.WithAlpha(accent, 0.9f) : new Vector4(0.08f, 0.1f, 0.14f, 0.8f), k < offer.NextLevel);
            }
        }
        r.UiTextCentered("1 / 2 / 3  OR CLICK A CARD", 800, 730, 1.7f, M.WithAlpha(p.Accent, 0.9f));
        r.UiTextCentered("THE NEXT WAVE BEGINS WHEN YOU CHOOSE.", 800, 760, 1.3f, M.WithAlpha(p.Dim, 0.65f));
    }

    public static void DrawGameOver(Renderer r, Palette p, GameWorld w, MetaProgress meta, List<string> unlocks, float time)
    {
        r.UiRect(0, 0, 1600, 900, new Vector4(0.002f, 0.003f, 0.010f, 0.80f));
        DrawCard(r, p, 280, 94, 1040, 728, M.WithAlpha(p.Danger, 0.7f));
        var s = w.Summary;
        r.UiTextCentered("SIGNAL LOST", 800, 132, 6.5f, p.Text, true);
        if (s != null)
            r.UiTextCentered("SEED " + SeedCode.ToCode(s.Seed) + "  //  " + (s.Daily ? "DAILY SIGNAL" : "ENDLESS SIGNAL") + "  //  " + (int)s.Duration + "S", 800, 214, 1.7f, M.WithAlpha(p.Dim, 0.9f));
        r.UiLine(340, 250, 1260, 250, 1f, M.WithAlpha(p.Danger, 0.3f));
        if (s != null)
        {
            DrawStat(r, "WAVE", s.Wave.ToString("00"), 380, 292, p);
            DrawStat(r, "SCORE", FormatScore(s.Score), 800, 292, p);
            DrawStat(r, "CHAIN", s.MaxCombo.ToString(), 1180, 292, p);
            DrawStat(r, "GRAZES", s.Grazes.ToString(), 380, 380, p);
            DrawStat(r, "KILLS", s.Kills.ToString(), 800, 380, p);
            DrawStat(r, "BOSSES", s.BossKills.ToString(), 1180, 380, p);
            DrawStat(r, "SHATTERS", s.Shatters.ToString(), 380, 468, p);
            DrawStat(r, "BURSTS", s.Bursts.ToString(), 800, 468, p);
            DrawStat(r, "MOVEMENT", s.MovementName, 1180, 468, p);
            r.UiTextCentered("BEST WAVE " + meta.BestWave.ToString("00") + "  //  BEST SCORE " + FormatScore(meta.BestScore), 800, 550, 1.65f, M.WithAlpha(p.Accent, 0.9f));
        }
        if (unlocks.Count > 0)
        {
            r.UiTextCentered("NEW SIGNALS UNLOCKED", 800, 594, 1.6f, M.WithAlpha(p.Accent, 0.9f));
            for (int i = 0; i < Math.Min(3, unlocks.Count); i++)
                r.UiTextCentered(unlocks[i], 800, 620 + i * 22, 1.5f, p.Text, true);
        }
        else
        {
            r.UiTextCentered("NO NEW SIGNAL UNLOCKED", 800, 606, 1.4f, M.WithAlpha(p.Dim, 0.6f));
        }
        r.UiLine(340, 698, 1260, 698, 1f, M.WithAlpha(p.Primary, 0.24f));
        r.UiTextCentered("R  RETRY SEED        N  NEW SIGNAL        TAB  ARCHIVE        ESC  TITLE", 800, 730, 1.75f, M.WithAlpha(p.Dim, 0.95f));
        if (s?.Daily == true)
            r.UiTextCentered("DAILY BEST " + FormatScore(meta.DailyBest) + "  //  WAVE " + meta.DailyBestWave.ToString("00"), 800, 766, 1.4f, M.WithAlpha(p.Accent, 0.7f));
    }

    public static void DrawToasts(Renderer r, Palette p, List<string> toasts, float time)
    {
        if (toasts.Count == 0) return;
        float y = 760;
        for (int i = 0; i < Math.Min(2, toasts.Count); i++)
        {
            r.UiTextRight(toasts[i], 1540, y - i * 26, 1.5f, M.WithAlpha(p.Accent, 0.85f), true);
        }
    }

    public static void DrawCrosshair(Renderer r, Palette p, Vector2 mouse, bool focus)
    {
        float x = mouse.X;
        float y = mouse.Y;
        float pulse = GameSettings.Current.CrosshairSize / 100f;
        var c = M.WithAlpha(p.Player, 0.88f);
        r.UiCircle(x, y, 11f * pulse, 1.2f, c, 22, true);
        r.UiCircle(x, y, 3f, 1f, M.WithAlpha(p.Accent, 0.9f), 12, true);
        r.UiLine(x - 20 * pulse, y, x - 13 * pulse, y, 1.2f, c, true);
        r.UiLine(x + 13 * pulse, y, x + 20 * pulse, y, 1.2f, c, true);
        r.UiLine(x, y - 20 * pulse, x, y - 13 * pulse, 1.2f, c, true);
        r.UiLine(x, y + 13 * pulse, x, y + 20 * pulse, 1.2f, c, true);
        if (focus)
            r.UiCircle(x, y, 3.5f, 0.8f, new Vector4(1f, 0.35f, 0.5f, 0.85f), 12);
    }

    public static void DrawFloatingTexts(Renderer r, GameWorld w, Palette p)
    {
        foreach (var f in w.FloatTexts)
        {
            Vector2 ui = r.WorldToUi(f.Pos);
            if (ui.X < -100 || ui.X > 1700 || ui.Y < -50 || ui.Y > 950) continue;
            float a = M.Clamp01(f.Life / MathF.Max(0.01f, f.MaxLife));
            r.UiTextCentered(f.Text, ui.X, ui.Y, f.Size, M.WithAlpha(f.Color, a), true);
        }
    }

    private static void DrawSpectrum(Renderer r, Palette p, AudioState audio, float x, float baseline, float width, float height)
    {
        float step = width / AudioState.SpectrumBars;
        for (int i = 0; i < AudioState.SpectrumBars; i++)
        {
            float v = M.Clamp01(MathF.Sqrt(M.Clamp01(audio.Spectrum[i])) * 1.12f);
            float h = MathF.Max(2f, v * height);
            float alpha = 0.22f + 0.72f * v;
            var col = M.Hsv((p.BaseHue + i * 0.012f) % 1f, 0.65f, 1f, alpha);
            float bw = MathF.Max(1.5f, step - 2.2f);
            r.UiRect(x + i * step, baseline - h, bw, h, col, v > 0.5f);
            r.UiRect(x + i * step, baseline + 2f, bw, h * 0.16f, M.WithAlpha(col, alpha * 0.22f));
        }
        r.UiLine(x, baseline + 1, x + width, baseline + 1, 0.8f, M.WithAlpha(p.Primary, 0.32f));
    }

    private static void DrawFrameDecor(Renderer r, Palette p, float x, float y, float w, float h)
    {
        var c = M.WithAlpha(p.Primary, 0.22f);
        r.UiLine(x, y + 18, x, y, 0.8f, c);
        r.UiLine(x, y, x + 18, y, 0.8f, c);
        r.UiLine(x + w - 18, y, x + w, y, 0.8f, c);
        r.UiLine(x + w, y, x + w, y + 18, 0.8f, c);
        r.UiLine(x, y + h - 18, x, y + h, 0.8f, c);
        r.UiLine(x, y + h, x + 18, y + h, 0.8f, c);
        r.UiLine(x + w - 18, y + h, x + w, y + h, 0.8f, c);
        r.UiLine(x + w, y + h - 18, x + w, y + h, 0.8f, c);
    }

    private static void DrawCard(Renderer r, Palette p, float x, float y, float w, float h, Vector4 accent)
    {
        r.UiRect(x, y, w, h, new Vector4(0.006f, 0.009f, 0.021f, 0.9f));
        r.UiFrame(x, y, w, h, 1f, M.WithAlpha(accent, 0.5f));
        r.UiRect(x, y, w, 2, M.WithAlpha(accent, 0.55f), true);
        r.UiRect(x, y + h - 2, w, 2, M.WithAlpha(accent, 0.18f));
    }

    private static void DrawStat(Renderer r, string label, string value, float centerX, float y, Palette p)
    {
        r.UiTextCentered(label, centerX, y, 1.45f, M.WithAlpha(p.Dim, 0.8f));
        r.UiTextCentered(value, centerX, y + 20, 2.8f, p.Text, true);
    }

    private static void DrawHeart(Renderer r, Vector2 c, float s, Vector4 color, bool glow)
    {
        if (glow) r.UiRect(c.X - s * 0.55f, c.Y - s * 0.55f, s * 1.1f, s * 1.1f, M.WithAlpha(color, 0.08f), true);
        r.UiLine(c.X, c.Y - s, c.X + s, c.Y, 1.5f, color, glow);
        r.UiLine(c.X + s, c.Y, c.X, c.Y + s, 1.5f, color, glow);
        r.UiLine(c.X, c.Y + s, c.X - s, c.Y, 1.5f, color, glow);
        r.UiLine(c.X - s, c.Y, c.X, c.Y - s, 1.5f, color, glow);
    }

    private static void DrawWrapped(Renderer r, string text, float x, float y, float size, float maxWidth, Vector4 color, float lineHeight, int maxLines, bool glow = false)
    {
        var lines = Wrap(text, size, maxWidth);
        for (int i = 0; i < lines.Count && i < maxLines; i++)
        {
            string line = lines[i];
            if (i == maxLines - 1 && lines.Count > maxLines)
                line = line.Length > 3 ? line[..Math.Max(0, line.Length - 1)] + "." : line;
            r.UiText(line, x, y + i * lineHeight, size, color, glow);
        }
    }

    private static List<string> Wrap(string text, float size, float maxWidth)
    {
        var lines = new List<string>();
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string line = "";
        foreach (var word in words)
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (PixelFont.Width(candidate, size) > maxWidth && line.Length > 0)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }
        if (line.Length > 0) lines.Add(line);
        if (lines.Count == 0) lines.Add("");
        return lines;
    }

    private static string SourceLabel(AudioState audio)
    {
        if (audio.External)
        {
            string name = audio.DeviceName;
            if (name.Length > 28) name = name[..28] + "..";
            return (audio.AppCapture ? "APP / " : "LOOPBACK / ") + (string.IsNullOrWhiteSpace(name) ? "SYSTEM" : name.ToUpperInvariant());
        }
        return audio.Quiet ? "SILENT SIGNAL" : "GENERATIVE SCORE";
    }

    private static string FormatScore(long score)
    {
        return score >= 1_000_000 ? (score / 1_000_000.0).ToString("0.00") + "M" : score.ToString("N0");
    }

    private static string Roman(int n) => n switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", _ => "X"
    };
}
