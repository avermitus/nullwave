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
    // Only the first three IDs and count affect the displayed caption. The list itself is mutable.
    private static int _captionCount = -1;
    private static string? _captionFirst, _captionSecond, _captionThird;
    private static string _modifierCaption = "";

    private static string ModifierCaption(GameWorld world)
    {
        var active = world.Mods.Active;
        int count = active.Count;
        string? first = count > 0 ? active[0] : null;
        string? second = count > 1 ? active[1] : null;
        string? third = count == 3 ? active[2] : null;
        if (count != _captionCount || first != _captionFirst || second != _captionSecond || third != _captionThird)
        {
            static string Name(string id) => ModifierCatalog.Get(id)?.Name ?? id;
            _modifierCaption = count == 0 ? "" : "SIGNAL " + Name(first!);
            if (count > 1) _modifierCaption += "  " + Name(second!);
            if (count == 3) _modifierCaption += "  " + Name(third!);
            if (count > 3) _modifierCaption += "  +" + (count - 2);
            _captionCount = count;
            _captionFirst = first;
            _captionSecond = second;
            _captionThird = third;
        }
        return _modifierCaption;
    }

    private static bool Abyssal => GameSettings.Current.IsAbyssal;
    private static Vector4 PanelFill(Vector4 standard, float abyssalAlpha = 0.94f) =>
        Abyssal ? new Vector4(0.002f, 0.003f, 0.007f, abyssalAlpha) : standard;

    // Shared with title mouse hit testing in Game.
    public const float TitleMenuX = 74f, TitleMenuY = 296f, TitleMenuWidth = 480f, TitleMenuRowHeight = 52f, TitleMenuRowHeightVisible = 46f;
    public const float TitleShipLeftX = 76f, TitleShipRightX = 318f, TitleShipY = 664f, TitleShipButtonWidth = 42f, TitleShipButtonHeight = 30f;

    public static void DrawTitle(Renderer r, Palette p, MetaProgress meta, ulong seed, bool dailySeed, int menuIndex, float time, AudioState audio)
    {
        // Keep the live demo visible across the open right side of the console.
        r.UiRect(0, 0, 1600, 900, new Vector4(0.002f, 0.004f, 0.016f, 0.30f));
        r.UiRect(40, 38, 550, 824, PanelFill(new Vector4(0.004f, 0.008f, 0.020f, 0.92f), 0.95f));
        DrawFrameDecor(r, p, 40, 38, 1520, 824);
        r.UiRect(40, 38, 5, 824, M.WithAlpha(p.Accent, 0.9f), true);
        r.UiText("SIGNAL // " + SeedCode.ToCode(seed), 74, 66, 1.6f, M.WithAlpha(p.Accent, 0.95f), true);
        r.UiText("NULLWAVE", 70, 112, 9.4f, p.Text, true);
        r.UiText("THE MUSIC BUILDS THE ENEMY", 76, 196, 1.85f, M.WithAlpha(p.Text, 0.92f));
        r.UiLine(74, 237, 554, 237, 1f, M.WithAlpha(p.Primary, 0.65f));
        r.UiText("AN ENDLESS AUDIO-DRIVEN BULLET HELL", 76, 257, 1.4f, M.WithAlpha(p.Text, 0.8f));
        for (int i = 0; i < TitleItems.Length; i++)
        {
            float y = TitleMenuY + i * TitleMenuRowHeight;
            bool selected = i == menuIndex;
            r.UiRect(TitleMenuX, y, TitleMenuWidth, TitleMenuRowHeightVisible, selected
                ? M.WithAlpha(p.Primary, 0.28f) : new Vector4(0.025f, 0.035f, 0.065f, 0.70f));
            if (selected)
            {
                r.UiFrame(TitleMenuX, y, TitleMenuWidth, TitleMenuRowHeightVisible, 1.5f, M.WithAlpha(p.Accent, 0.95f), true);
                r.UiRect(TitleMenuX, y, 5, TitleMenuRowHeightVisible, p.Accent, true);
            }
            r.UiText(TitleItems[i], 96, y + 10, selected ? 2.7f : 2.45f,
                selected ? p.Text : M.WithAlpha(p.Text, 0.82f), selected);
            if (selected) r.UiTextRight(">", 530, y + 10, 2.5f, p.Accent, true);
        }
        r.UiText("BEST WAVE " + meta.BestWave.ToString("00") + "  //  " + FormatScore(meta.BestScore), 76, 803, 1.55f, M.WithAlpha(p.Text, 0.85f));
        r.UiText("MOVE WASD / STICK   AIM MOUSE / STICK   FIRE LMB / RT", 76, 829, 1.25f, M.WithAlpha(p.Text, 0.72f));
        r.UiText("DASH SPACE / A   RESONANCE E / X   FOCUS SHIFT / LB", 76, 847, 1.25f, M.WithAlpha(p.Text, 0.72f));
        r.UiTextRight("ARCHIVE " + meta.UnlockedModifiers.Count.ToString("00") + " / " + ModifierCatalog.All.Length.ToString("00"), 1532, 64, 1.5f, M.WithAlpha(p.Text, 0.9f));
        if (audio.LocalTrackActive && !string.IsNullOrEmpty(audio.LocalTrackName))
            r.UiTextRight("TRACK // " + FormatTrack(audio.LocalTrackName, 38), 1532, 774, 1.4f, p.Text, true);
        else if (!string.IsNullOrEmpty(audio.SpotifyTrack))
            r.UiTextRight("SPOTIFY // " + FormatTrack(audio.SpotifyTrack, 38), 1532, 774, 1.4f, p.Text, true);
        r.UiTextRight("AUDIO // " + SourceLabel(audio), 1532, 797, 1.4f, M.WithAlpha(p.Text, 0.85f));
        DrawSpectrum(r, p, audio, 1110, 850, 420, 40);
        if (dailySeed) r.UiTextRight("DAILY // " + SeedCode.DailyLabel(DateTime.Now), 1532, 90, 1.4f, p.Accent);
    }

    public static void DrawArchive(Renderer r, Palette p, MetaProgress meta, int selected, float time, bool fromPause, bool fromGameOver, string selectedSeed = "", bool editingSeed = false, string editBuffer = "")
    {
        r.UiRect(0, 0, 1600, 900, PanelFill(new Vector4(0.003f, 0.005f, 0.014f, 0.84f), 0.92f));
        DrawFrameDecor(r, p, 34, 34, 1532, 832);
        r.UiText("ARCHIVE OF THE SIGNAL", 64, 54, 4.4f, p.Text, true);
        DrawArchiveSeedDecoder(r, p, meta, time, fromPause, selectedSeed, editingSeed, editBuffer);
        r.UiText("UNLOCKED SIGNALS CHANGE THE RULES. STACK THEM FOR A HIGHER SCORE.", 66, 112, 1.65f, p.Dim);
        string back = fromPause ? "BACK TO PAUSE" : fromGameOver ? "BACK TO SIGNAL LOST" : "BACK TO TITLE";
        r.UiTextRight("ESC / B // " + back, 1534, 64, 1.55f, M.WithAlpha(p.Dim, 0.9f));
        r.UiText("BEST WAVE " + meta.BestWave.ToString("00") + "   BEST " + FormatScore(meta.BestScore) + "   BEST CHAIN " + meta.BestCombo + "   BOSSES " + meta.TotalBossKills + "   GRAZES " + meta.TotalGrazes + "   RUNS " + meta.Runs, 66, 134, 1.7f, M.WithAlpha(p.Accent, 0.85f));
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
        if (meta.RecentRuns.Count > 0)
            DrawRecentSignals(r, p, meta, 64, 1534, 846, selectedSeed);
        else
            r.UiText("MODIFIERS ARE UNLOCKED BY PLAYING.  LOCKED SIGNALS SHOW THEIR CONDITION.", 64, 846, 1.4f, M.WithAlpha(p.Dim, 0.55f));
    }

    private static void DrawArchiveSeedDecoder(Renderer r, Palette p, MetaProgress meta, float time, bool fromPause, string selectedSeed, bool editingSeed, string editBuffer)
    {
        const float bx = 646f, by = 46f, bw = 588f, bh = 52f;
        bool active = editingSeed || !string.IsNullOrEmpty(selectedSeed);
        var border = editingSeed ? p.Accent : active ? M.WithAlpha(p.Accent, 0.72f) : M.WithAlpha(p.Primary, 0.42f);
        r.UiRect(bx, by, bw, bh, PanelFill(new Vector4(0.006f, 0.010f, 0.024f, 0.92f), 0.95f));
        r.UiFrame(bx, by, bw, bh, 1f, border, active);
        r.UiRect(bx, by, bw, 1.5f, M.WithAlpha(active ? p.Accent : p.Primary, 0.65f), active);

        if (editingSeed)
        {
            string cursor = editBuffer.Length < SeedCode.CodeLength && MathF.Sin(time * 8f) > 0f ? "_" : "";
            string padded = (editBuffer + cursor).PadRight(SeedCode.CodeLength, '.');
            r.UiText("CUSTOM SEED // [" + padded + "]", bx + 12f, by + 8f, 1.4f, p.Accent, true);
            r.UiTextRight("ENTER OK // ESC CANCEL", bx + bw - 12f, by + 9f, 1.25f, M.WithAlpha(p.Text, 0.85f));
        }
        else if (!string.IsNullOrEmpty(selectedSeed))
        {
            r.UiText("TARGET SEED // " + selectedSeed, bx + 12f, by + 8f, 1.4f, p.Accent, true);
            string actions = fromPause ? "[R] CYCLE HISTORY" : "[L] LAUNCH  [R] NEXT  [C] TYPE  [N] CLEAR";
            r.UiTextRight(actions, bx + bw - 12f, by + 9f, 1.2f, M.WithAlpha(p.Text, 0.86f));
        }
        else
        {
            r.UiText("TARGET SEED // RANDOM", bx + 12f, by + 8f, 1.4f, M.WithAlpha(p.Text, 0.85f));
            string actions = fromPause ? "[R] CYCLE HISTORY" : "[R] CYCLE HISTORY  [C] CUSTOM SEED";
            r.UiTextRight(actions, bx + bw - 12f, by + 9f, 1.25f, M.WithAlpha(p.Dim, 0.85f));
        }

        r.UiLine(bx + 12f, by + 25f, bx + bw - 12f, by + 25f, 1f, M.WithAlpha(active ? p.Accent : p.Primary, 0.22f));

        string probe = editingSeed ? editBuffer : selectedSeed;
        string sub;
        Vector4 subCol;
        if (!string.IsNullOrWhiteSpace(probe) && SeedCode.TryParse(probe, out _, out string canonical))
        {
            if (meta.TryGetSeedInfo(canonical, out int bestWave, out long bestScore, out int runs, out bool daily))
            {
                sub = "RECORD // WAVE " + bestWave.ToString("00") + "   BEST " + FormatScore(bestScore) + "   RUNS " + runs + (daily ? "   [DAILY SIGNAL]" : "   [ARCHIVE SIGNAL]");
                subCol = M.WithAlpha(p.Accent, 0.88f);
            }
            else
            {
                sub = "UNEXPLORED SIGNAL // CODE " + canonical + (fromPause ? "" : " // PRESS L TO LAUNCH");
                subCol = M.WithAlpha(p.Text, 0.8f);
            }
        }
        else
        {
            sub = "SIGNAL DECODER // REPLAY RECORDED SEEDS OR ENTER ANY 8-CHAR SEED";
            subCol = M.WithAlpha(p.Dim, 0.72f);
        }
        r.UiText(sub, bx + 12f, by + 32f, 1.22f, subCol);
    }

    /// <summary>Title: the instrument ship (left / right).</summary>
    public static void DrawShipSelect(Renderer r, Palette p, ShipKind ship)
    {
        r.UiLine(74, 624, 554, 624, 1f, M.WithAlpha(p.Primary, 0.55f));
        r.UiText("SHIP", 76, 646, 1.4f, M.WithAlpha(p.Text, 0.8f));
        r.UiFrame(TitleShipLeftX, TitleShipY, TitleShipButtonWidth, TitleShipButtonHeight, 1f, M.WithAlpha(p.Accent, 0.65f));
        r.UiFrame(TitleShipRightX, TitleShipY, TitleShipButtonWidth, TitleShipButtonHeight, 1f, M.WithAlpha(p.Accent, 0.65f));
        r.UiTextCentered("<", TitleShipLeftX + TitleShipButtonWidth / 2, TitleShipY + 5, 2f, p.Accent, true);
        r.UiTextCentered(Ships.Name(ship), 218, TitleShipY + 5, 2.1f, p.Text, true);
        r.UiTextCentered(">", TitleShipRightX + TitleShipButtonWidth / 2, TitleShipY + 5, 2f, p.Accent, true);
        r.UiText(Ships.Blurb(ship), 76, 701, 1.45f, M.WithAlpha(p.Text, 0.85f));
        r.UiText("UP / DOWN SELECT   LEFT / RIGHT SHIP   ENTER / A CONFIRM", 76, 770, 1.25f, M.WithAlpha(p.Text, 0.75f));
    }

    public static void DrawHud(Renderer r, GameWorld w, Palette p, AudioState audio, MetaProgress meta, float time, float fps, bool debug, bool showEvent = true)
    {
        var player = w.Player;
        // Instrument plates keep critical readings legible over bright arena effects.
        r.UiRect(40, 26, 300, 180, PanelFill(new Vector4(0.004f, 0.008f, 0.020f, 0.76f), 0.88f));
        r.UiRect(40, 26, 3, 180, M.WithAlpha(p.Accent, 0.8f));
        r.UiRect(1210, 26, 346, 91, PanelFill(new Vector4(0.004f, 0.008f, 0.020f, 0.76f), 0.88f));
        r.UiRect(1210, 26, 346, 2, M.WithAlpha(p.Accent, 0.75f));
        DrawStageAndSong(r, w, p, meta, time);
        r.UiText("WAVE " + w.Wave.ToString("00"), 56, 38, 3.1f, p.Text, true);
        r.UiText("MOVEMENT " + Roman(w.Movement + 1) + " // " + w.Rules.Name, 58, 78, 1.65f, M.WithAlpha(p.Accent, 0.9f));
        r.UiText(FormatScore(w.Score), 56, 108, 4.2f, p.Text, true);
        if (w.Combo > 0)
        {
            float alpha = M.Clamp01(w.ComboTimer / 1.2f);
            r.UiText("x" + w.ComboMultiplier.ToString("0.00") + "  //  " + w.Combo + " CHAIN", 58, 164, 1.9f, M.WithAlpha(p.Accent, 0.7f + alpha * 0.3f), true);
            r.UiRect(58, 184, 180, 4, PanelFill(new Vector4(0.01f, 0.015f, 0.03f, 0.9f)));
            r.UiRect(58, 184, 180 * M.Clamp01(w.ComboTimer / GameWorld.ComboDuration), 4, M.WithAlpha(p.Accent, 0.95f));
        }
        if (w.HypeLevel > 0)
        {
            var hc = M.Hsv((0.85f - w.HypeLevel * 0.1f + 1f) % 1f, 0.6f, 1f, 0.6f + w.Hype * 0.4f);
            r.UiTextRight("HYPE " + new string('|', w.HypeLevel + 1), 520, 116, 2.2f, hc, true);
        }
        string modifierCaption = ModifierCaption(w);
        if (modifierCaption.Length > 0)
            r.UiText(modifierCaption, 58, 195, 1.3f, M.WithAlpha(p.Text, 0.85f));

        r.UiTextRight("BPM " + audio.Bpm.ToString("000"), 1542, 40, 2.2f, M.WithAlpha(p.Accent, 0.85f));
        r.UiTextRight("AUDIO // " + SourceLabel(audio), 1542, 72, 1.45f, M.WithAlpha(audio.LocalTrackActive ? p.Accent : p.Text, 0.9f));
        if (audio.LocalTrackActive && !string.IsNullOrEmpty(audio.LocalTrackName))
            r.UiTextRight("TRACK // " + FormatTrack(audio.LocalTrackName, 28), 1542, 93, 1.25f, M.WithAlpha(p.Accent, 0.9f), true);
        else if (!string.IsNullOrEmpty(audio.SpotifyTrack))
            r.UiTextRight("SPOTIFY // " + FormatTrack(audio.SpotifyTrack, 28), 1542, 93, 1.25f, M.WithAlpha(p.Accent, 0.9f), true);
        DrawSpectrum(r, p, audio, 1118, 188, 424, 74);

        if (w.BossEnemy is { Dead: false } boss)
            DrawBossBar(r, w, p, boss, time);

        DrawDashIndicator(r, p, player);
        DrawHealthBar(r, p, player, time);
        var settings = GameSettings.Current;
        if (settings.ShowAttunements && player.AttunementCount > 0) DrawHudAttunements(r, p, player, time);
        if (settings.Minimap) DrawMinimap(r, p, w, time);
        if (settings.ThreatArrows) DrawThreatIndicators(r, p, w, time);
        if (settings.ShowFps) r.UiTextRight("FPS " + fps.ToString("0"), 1542, 790, 1.4f, M.WithAlpha(p.Dim, 0.85f));
        r.UiText("RESONANCE", 56, 836, 1.4f, M.WithAlpha(p.Text, 0.9f));
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

    /// <summary>HUD: the ship and how this song is going against your record.</summary>
    private static void DrawStageAndSong(Renderer r, GameWorld w, Palette p, MetaProgress meta, float time)
    {
        float y = 214;
        if (w.Ship != ShipKind.Classic)
        {
            r.UiText("SHIP " + Ships.Name(w.Ship), 58, y, 1.4f, M.WithAlpha(p.Accent, 0.8f));
            y += 22;
        }
        var t = w.CurrentTrack;
        if (t == null || !t.IsSong) return;
        var rec = meta.GetSong(t.Title);
        string line;
        Vector4 col;
        if (rec == null || rec.BestScore <= 0)
        {
            line = "NEW SONG  //  " + FormatScore(t.Score) + "  //  RANK " + t.Rank;
            col = M.WithAlpha(p.Dim, 0.85f);
        }
        else
        {
            float pct = t.Score / (float)Math.Max(1, rec.BestScore);
            bool ahead = t.Score > rec.BestScore;
            line = (ahead ? "NEW BEST +" + ((pct - 1f) * 100f).ToString("0") + "%" : (pct * 100f).ToString("0") + "% OF BEST " + FormatScore(rec.BestScore))
                   + "  //  RANK " + t.Rank + (rec.BestRank.Length > 0 ? " (BEST " + rec.BestRank + ")" : "");
            col = ahead ? M.WithAlpha(p.Accent, 0.75f + 0.25f * MathF.Sin(time * 6f)) : M.WithAlpha(p.Dim, 0.9f);
        }
        r.UiText(line, 58, y, 1.4f, col, true);
    }

    private static void DrawBossBar(Renderer r, GameWorld w, Palette p, Enemy boss, float time)
    {
        const float bx = 380f, by = 34f, bw = 840f, bh = 18f;
        int phases = Math.Max(3, w.Mods.BossPhases);
        string title = w.BossTitle(boss, phases);
        r.UiTextCentered(title, 800f, 10f, 1.6f, M.WithAlpha(p.Text, 0.94f), true);

        float borderPulse = Math.Clamp(0.45f + w.BeatPulse * 0.35f + boss.Telegraph * 0.25f, 0.35f, 1f);
        r.UiRect(bx, by, bw, bh, new Vector4(0.02f, 0.01f, 0.03f, 0.86f));
        if (boss.BossHealthDisplay > boss.HealthFrac + 0.002f)
            r.UiRect(bx + 2f, by + 2f, (bw - 4f) * M.Clamp01(boss.BossHealthDisplay), bh - 4f, new Vector4(1f, 0.90f, 0.92f, 0.55f), true);
        r.UiRect(bx + 2f, by + 2f, (bw - 4f) * boss.HealthFrac, bh - 4f, M.WithAlpha(p.Danger, 0.86f), true);
        r.UiFrame(bx, by, bw, bh, 1f, M.WithAlpha(boss.PhaseBreakTimer > 0.01f ? p.Accent : p.Danger, borderPulse), boss.PhaseBreakTimer > 0.01f || boss.Telegraph > 0.55f);
        for (int i = 1; i < phases; i++)
            r.UiRect(bx + bw * i / phases - 1f, by, 2f, bh, new Vector4(0f, 0f, 0f, 0.78f));

        const float pipW = 34f, pipH = 5f, pipGap = 8f, pipY = 58f;
        float totalPipW = phases * pipW + (phases - 1) * pipGap;
        float pipStartX = 800f - totalPipW * 0.5f;
        float pulse = 0.72f + 0.28f * MathF.Sin(time * 8f);
        for (int i = 0; i < phases; i++)
        {
            float px = pipStartX + i * (pipW + pipGap);
            if (i < boss.BossPhase)
            {
                r.UiRect(px, pipY, pipW, pipH, M.WithAlpha(p.Danger, 0.26f));
                r.UiFrame(px, pipY, pipW, pipH, 1f, M.WithAlpha(p.Danger, 0.45f));
            }
            else if (i == boss.BossPhase)
            {
                var c = Vector4.Lerp(p.Danger, p.Accent, boss.PhaseBreakTimer > 0.01f ? 0.8f : 0.3f);
                r.UiRect(px, pipY, pipW, pipH, M.WithAlpha(c, pulse), true);
            }
            else
            {
                r.UiRect(px, pipY, pipW, pipH, new Vector4(0.02f, 0.02f, 0.05f, 0.85f));
                r.UiFrame(px, pipY, pipW, pipH, 1f, M.WithAlpha(p.Dim, 0.38f));
            }
        }

        if (boss.PhaseBreakTimer > 0.01f || boss.BossInvuln > 0.01f)
            r.UiTextRight("// PHASE SHIFT //", bx + bw, 57f, 1.3f, M.WithAlpha(p.Accent, pulse), true);
        else if (boss.Telegraph > 0.55f)
            r.UiTextRight("INCOMING VOLLEY", bx + bw, 57f, 1.3f, M.WithAlpha(p.Danger, pulse), true);
    }

    private static void DrawDashIndicator(Renderer r, Palette p, World.Player player)
    {
        // Above both attunement rows and the optional synergy badges (top edge y=688).
        const float x = 56f, y = 650f, width = 150f;
        bool ready = player.DashCooldown <= 0f && !player.IsDashing;
        float fraction = player.DashCooldownMax > 0f
            ? 1f - M.Clamp01(player.DashCooldown / player.DashCooldownMax) : 1f;
        r.UiRect(x - 4f, y - 4f, 230f, 25f, PanelFill(new Vector4(0.01f, 0.012f, 0.03f, 0.88f)));
        r.UiText(ready ? "DASH // READY" : "DASH // RECHARGING", x, y, 1.3f,
            M.WithAlpha(ready ? p.Accent : p.Text, 0.95f), ready);
        r.UiRect(x, y + 15f, width, 4f, new Vector4(0.06f, 0.07f, 0.10f, 0.95f));
        r.UiRect(x, y + 15f, width * fraction, 4f, M.WithAlpha(ready ? p.Accent : p.Primary, 0.95f));
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

        r.UiText("INTEGRITY", 56, 770, 1.4f, critical ? M.WithAlpha(danger, 0.7f + pulse * 0.3f) : M.WithAlpha(p.Text, 0.9f), critical);
        r.UiTextRight(player.Health + " / " + max, x + w, 770, 1.4f, M.WithAlpha(fill, 0.9f), true);
        r.UiRect(x - 2, y - 2, w + 4, h + 4, PanelFill(new Vector4(0.01f, 0.012f, 0.03f, 0.88f), 0.94f));
        r.UiFrame(x - 2, y - 2, w + 4, h + 4, 1f, critical ? M.WithAlpha(danger, 0.35f + pulse * 0.6f) : M.WithAlpha(p.Primary, Abyssal ? 0.30f : 0.35f), critical);

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
        r.UiRect(x, y, mw, mh, PanelFill(new Vector4(0.004f, 0.006f, 0.016f, 0.72f), 0.86f));
        r.UiFrame(x, y, mw, mh, 1f, M.WithAlpha(p.Primary, Abyssal ? 0.28f : 0.3f));
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
        if (w.IsDropOverdrive)
        {
            float odPulse = 0.76f + 0.24f * MathF.Sin(time * 11f);
            var dropCol = Vector4.Lerp(new Vector4(1f, 0.45f, 0.88f, 1f), new Vector4(1f, 0.88f, 0.40f, 1f), odPulse * 0.5f);
            r.UiText("DROP OD " + w.DropOverdriveTimer.ToString("0.0") + "S", x, 824, 1.3f, M.WithAlpha(dropCol, 0.95f), true);
        }
        if (w.IsDropOverdrive || w.Player.IsSyncOverdrive)
        {
            float frac = w.IsDropOverdrive
                ? M.Clamp01(w.DropOverdriveTimer / 4.0f)
                : M.Clamp01(w.Player.SyncOverdriveTimer / 5.0f);
            var odBarCol = w.IsDropOverdrive
                ? new Vector4(1f, 0.58f, 0.88f, 0.92f)
                : new Vector4(0.45f, 1f, 0.95f, 0.92f);
            r.UiRect(x, 839, wdt, 3, new Vector4(0.015f, 0.015f, 0.03f, 0.82f));
            r.UiRect(x, 839, wdt * frac, 3, odBarCol, true);
        }
        r.UiRect(x - 2, y - 2, wdt + 4, h + 4, PanelFill(new Vector4(0.01f, 0.012f, 0.03f, 0.85f), 0.94f));
        r.UiRect(x, y, wdt * flow, h, M.WithAlpha(col, 0.9f), true);
        // speed tick: where your current speed sits (keep it right of the line to build flow)
        r.UiRect(x + wdt * 0.7f - 1, y - 4, 2, h + 8, M.WithAlpha(p.Dim, 0.5f));
        r.UiRect(x + wdt * speed - 2, y - 5, 4, h + 10, new Vector4(1f, 1f, 1f, 0.85f), true);
        float mult = 1f + flow * 1.5f + w.OverdriveScoreBonus;
        r.UiTextRight("x" + mult.ToString("0.00"), x + wdt, 862, 1.3f, M.WithAlpha(w.OverdriveScoreBonus > 0f ? new Vector4(1f, 0.88f, 0.45f, 1f) : col, 0.92f), hot || w.OverdriveScoreBonus > 0f);
        r.UiText("STAY FAST // GRAZE // KILL", x, 862, 1.2f, M.WithAlpha(p.Dim, 0.45f));
        if (w.Player.IsSyncOverdrive)
        {
            float sp = 0.76f + 0.24f * MathF.Sin(time * 11f);
            var syncOdCol = Vector4.Lerp(new Vector4(0.45f, 1f, 0.95f, 1f), new Vector4(1f, 0.88f, 0.45f, 1f), sp * 0.45f);
            r.UiTextRight("SYNC OD " + w.Player.SyncOverdriveTimer.ToString("0.0") + "S", x + wdt, 824, 1.3f, M.WithAlpha(syncOdCol, 0.96f), true);
        }
        else if (w.Player.PhaseSyncFlash > 0.01f)
        {
            float sf = w.Player.PhaseSyncFlash;
            var syncCol = Vector4.Lerp(new Vector4(0.45f, 1f, 0.95f, 1f), new Vector4(1f, 0.88f, 0.45f, 1f), sf);
            int pip = w.Player.PhaseSyncStreak > 0 && w.Player.PhaseSyncStreak % 3 == 0 ? 3 : w.Player.PhaseSyncStreak % 3;
            r.UiTextRight("PHASE SYNC " + pip + "/3", x + wdt, 824, 1.3f, M.WithAlpha(syncCol, 0.45f + 0.55f * sf), true);
        }
        else if (w.Player.PhaseSyncStreak > 0)
        {
            r.UiTextRight("SYNC " + w.PhaseSyncs + " (" + (w.Player.PhaseSyncStreak % 3) + "/3)", x + wdt, 824, 1.3f, M.WithAlpha(p.Accent, 0.72f));
        }
        else if (w.PhaseSyncs > 0)
        {
            r.UiTextRight("SYNC " + w.PhaseSyncs, x + wdt, 824, 1.3f, M.WithAlpha(p.Accent, 0.65f));
        }
    }

    public static void DrawPause(Renderer r, Palette p, GameWorld w, int menuIndex, AudioState audio)
    {
        r.UiRect(0, 0, 1600, 900, PanelFill(new Vector4(0.002f, 0.004f, 0.012f, 0.72f), 0.84f));
        DrawCard(r, p, 500, 214, 600, 524, M.WithAlpha(p.Primary, 0.75f));
        r.UiTextCentered("SIGNAL HELD", 800, 254, 5.2f, p.Text, true);
        r.UiTextCentered("WAVE " + w.Wave.ToString("00") + "  //  " + SeedCode.ToCode(w.Seed), 800, 320, 1.7f, M.WithAlpha(p.Dim, 0.9f));
        r.UiLine(560, 354, 1040, 354, 1f, M.WithAlpha(p.Primary, 0.3f));
        for (int i = 0; i < PauseItems.Length; i++)
        {
            bool sel = i == menuIndex;
            float y = 380 + i * 56;
            if (sel)
            {
                r.UiRect(610, y - 7, 380, 44, M.WithAlpha(p.Primary, 0.28f));
                r.UiRect(610, y - 7, 5, 44, p.Accent, true);
                r.UiFrame(610, y - 7, 380, 44, 1f, M.WithAlpha(p.Primary, 0.6f), true);
            }
            r.UiTextCentered(PauseItems[i], 800, y, sel ? 2.9f : 2.5f, sel ? p.Text : M.WithAlpha(p.Text, 0.8f), sel);
        }
        r.UiLine(546, 602, 1054, 602, 1f, M.WithAlpha(p.Primary, 0.24f));
        DrawPauseAttunements(r, p, w.Player);
        if (audio.LocalTrackActive && !string.IsNullOrEmpty(audio.LocalTrackName))
            r.UiText("TRACK // " + FormatTrack(audio.LocalTrackName, 42), 546, 686, 1.35f, M.WithAlpha(p.Accent, 0.9f), true);
        else if (!string.IsNullOrEmpty(audio.SpotifyTrack))
            r.UiText("SPOTIFY // " + FormatTrack(audio.SpotifyTrack, 42), 546, 686, 1.35f, M.WithAlpha(p.Accent, 0.9f), true);
        r.UiText("AUDIO // " + SourceLabel(audio), 546, 706, 1.45f, M.WithAlpha(p.Dim, 0.75f));
        r.UiTextRight("P / START // RESUME", 1054, 706, 1.35f, M.WithAlpha(p.Dim, 0.65f));
    }

    private static void DrawHudAttunements(Renderer r, Palette p, World.Player player, float time)
    {
        Span<ActiveAttunement> active = stackalloc ActiveAttunement[12];
        int count = AttunementCatalog.GetActive(player, active);
        if (count <= 0) return;

        const float startX = 56f, bw = 58f, bh = 20f, gapX = 4.4f;
        float baseY = count > 6 ? 712f : 736f;

        if (player.ActiveSynergies != SynergyFlags.None)
        {
            Span<int> syns = stackalloc int[4];
            int synCount = SynergyCatalog.GetActive(player, syns);
            float synY = count > 6 ? 688f : 712f;
            float pulse = 0.78f + 0.22f * MathF.Sin(time * 6.5f);
            var gold = new Vector4(1f, 0.85f, 0.40f, 1f);
            const float sw = 58f, sh = 18f, sGap = 5f;
            for (int i = 0; i < synCount; i++)
            {
                var def = SynergyCatalog.All[syns[i]];
                float sx = startX + i * (sw + sGap);
                var hueCol = M.Hsv(def.Hue, 0.72f, 1f);
                r.UiRect(sx, synY, sw, sh, PanelFill(new Vector4(0.020f, 0.014f, 0.006f, 0.90f), 0.95f));
                r.UiFrame(sx, synY, sw, sh, 1f, M.WithAlpha(gold, 0.82f * pulse), true);
                r.UiRect(sx, synY, sw, 1.5f, M.WithAlpha(hueCol, 0.92f), true);
                r.UiTextCentered("*" + def.Tag, sx + sw * 0.5f, synY + 4.5f, 1.3f, M.WithAlpha(gold, 0.98f), true);
            }
        }

        for (int i = 0; i < count; i++)
        {
            int col = i % 6;
            int row = i / 6;
            float bx = startX + col * (bw + gapX);
            float by = baseY + row * 24f;
            DrawAttunementBadge(r, p, active[i], bx, by, bw, bh);
        }
    }

    private static void DrawPauseAttunements(Renderer r, Palette p, World.Player player)
    {
        Span<ActiveAttunement> active = stackalloc ActiveAttunement[12];
        int count = AttunementCatalog.GetActive(player, active);
        r.UiText("ATTUNEMENTS // " + player.AttunementCount.ToString("00"), 546, 612, 1.35f, M.WithAlpha(p.Accent, 0.85f));
        if (player.ActiveSynergies != SynergyFlags.None)
        {
            Span<int> syns = stackalloc int[4];
            int synCount = SynergyCatalog.GetActive(player, syns);
            Span<char> buf = stackalloc char[40];
            int pos = 0;
            "OVERCLOCK // ".AsSpan().CopyTo(buf);
            pos += 13;
            for (int i = 0; i < synCount; i++)
            {
                if (i > 0) buf[pos++] = '/';
                var tag = SynergyCatalog.All[syns[i]].Tag.AsSpan();
                tag.CopyTo(buf[pos..]);
                pos += tag.Length;
            }
            r.UiTextCentered(new string(buf[..pos]), 800, 612, 1.15f, new Vector4(1f, 0.85f, 0.40f, 0.95f), true);
        }
        r.UiTextRight(count.ToString("00") + " / " + AttunementCatalog.All.Length.ToString("00") + " SIGNALS", 1054, 612, 1.3f, M.WithAlpha(p.Dim, 0.7f));
        if (count <= 0)
        {
            r.UiTextCentered("NO ATTUNEMENTS ACTIVE", 800, 642, 1.4f, M.WithAlpha(p.Dim, 0.55f));
            return;
        }

        const float bw = 74f, bh = 20f, gapX = 8f;
        int rows = count > 6 ? 2 : 1;
        float baseY = rows == 2 ? 630f : 640f;
        for (int row = 0; row < rows; row++)
        {
            int start = row * 6;
            int rowCount = Math.Min(6, count - start);
            float rowW = rowCount * bw + (rowCount - 1) * gapX;
            float startX = 800f - rowW * 0.5f;
            float by = baseY + row * 24f;
            for (int c = 0; c < rowCount; c++)
                DrawAttunementBadge(r, p, active[start + c], startX + c * (bw + gapX), by, bw, bh);
        }
    }

    private static void DrawAttunementBadge(Renderer r, Palette p, ActiveAttunement entry, float bx, float by, float bw, float bh)
    {
        var accent = M.Hsv((p.BaseHue + entry.CatalogIndex * 0.055f) % 1f, 0.62f, 1f);
        bool maxed = entry.Level >= entry.MaxStacks;
        r.UiRect(bx, by, bw, bh, PanelFill(new Vector4(0.008f, 0.012f, 0.028f, 0.88f), 0.94f));
        r.UiFrame(bx, by, bw, bh, 1f, M.WithAlpha(accent, maxed ? 0.78f : 0.44f), maxed);
        r.UiRect(bx, by, bw, 1.5f, M.WithAlpha(accent, 0.72f), true);
        r.UiText(entry.Tag, bx + 5f, by + 5.5f, 1.3f, maxed ? p.Accent : M.WithAlpha(p.Text, 0.95f), true);
        int maxPips = Math.Max(1, entry.MaxStacks);
        float pipStep = 5.5f;
        float pipsRight = bx + bw - 5f;
        float pipsStart = pipsRight - maxPips * pipStep + 1.5f;
        for (int k = 0; k < maxPips; k++)
        {
            bool lit = k < entry.Level;
            r.UiRect(pipsStart + k * pipStep, by + 6f, 4f, 8f, lit ? M.WithAlpha(accent, 0.95f) : new Vector4(0.08f, 0.10f, 0.16f, 0.85f), lit);
        }
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
            if (SynergyCatalog.WouldUnlock(w.Player, offer.Id) is { } syn)
            {
                float pulse = 0.78f + 0.22f * MathF.Sin(time * 7f);
                var gold = new Vector4(1f, 0.85f, 0.40f, 1f);
                r.UiRect(x + 24, y + ch - 74, cw - 48, 22, PanelFill(new Vector4(0.04f, 0.025f, 0.008f, 0.9f), 0.95f));
                r.UiFrame(x + 24, y + ch - 74, cw - 48, 22, 1f, M.WithAlpha(gold, 0.85f * pulse), true);
                r.UiTextCentered("OVERCLOCK // " + syn.Name, x + cw * 0.5f, y + ch - 68, 1.35f, M.WithAlpha(gold, 0.98f), true);
            }
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

    public static void DrawGameOver(Renderer r, Palette p, GameWorld w, MetaProgress meta, List<string> unlocks, RecordBreak records, float time)
    {
        r.UiRect(0, 0, 1600, 900, PanelFill(new Vector4(0.002f, 0.003f, 0.010f, 0.80f), 0.88f));
        DrawCard(r, p, 280, 94, 1040, 728, M.WithAlpha(p.Danger, 0.7f));
        var s = w.Summary;
        r.UiTextCentered("SIGNAL LOST", 800, 132, 6.5f, p.Text, true);
        if (s != null)
        {
            string mode = s.Daily ? "DAILY SIGNAL" : "ENDLESS SIGNAL";
            r.UiTextCentered("SEED " + SeedCode.ToCode(s.Seed) + "  //  " + mode + "  //  SHIP " + Ships.Name(s.Ship) + "  //  " + (int)s.Duration + "S", 800, 214, 1.7f, M.WithAlpha(p.Dim, 0.9f));
        }
        r.UiLine(340, 250, 1260, 250, 1f, M.WithAlpha(p.Danger, 0.3f));
        float pulse = 0.72f + 0.28f * MathF.Sin(time * 5.5f);
        if (s != null)
        {
            DrawStat(r, "WAVE", s.Wave.ToString("00"), 380, 286, p, records.Wave, pulse);
            DrawStat(r, "SCORE", FormatScore(s.Score), 800, 286, p, records.Score, pulse);
            DrawStat(r, "CHAIN", s.MaxCombo.ToString(), 1180, 286, p, records.Combo, pulse);
            DrawStat(r, "KILLS", s.Kills.ToString(), 380, 362, p);
            DrawStat(r, "BOSSES", s.BossKills.ToString(), 800, 362, p);
            DrawStat(r, "MOVEMENT", s.MovementName, 1180, 362, p);
            r.UiTextCentered("GRAZES " + s.Grazes + "  //  SHATTERS " + s.Shatters + "  //  BURSTS " + s.Bursts + "  //  BEST WAVE " + meta.BestWave.ToString("00") + "  //  BEST " + FormatScore(meta.BestScore), 800, 432, 1.5f, M.WithAlpha(p.Accent, 0.85f));
            if (records.Any)
                r.UiTextCentered(RecordBanner(records, s), 800, 456, 1.7f, M.WithAlpha(p.Accent, pulse), true);
            DrawRunTracks(r, p, s, meta, 486);
            if (s.Timeline != null) DrawRecap(r, p, s, 588, time);
        }
        if (unlocks.Count > 0)
            r.UiTextCentered("NEW SIGNALS UNLOCKED // " + string.Join("  //  ", unlocks.Take(2)), 800, 690, 1.45f, M.WithAlpha(p.Accent, 0.95f), true);
        r.UiLine(340, 708, 1260, 708, 1f, M.WithAlpha(p.Primary, 0.24f));
        r.UiTextCentered("R  RETRY        N  NEW        C  SAVE CLIP        TAB  ARCHIVE        ESC  TITLE", 800, 732, 1.7f, M.WithAlpha(p.Dim, 0.95f));
        if (s?.Daily == true)
            r.UiTextCentered("DAILY BEST " + FormatScore(meta.DailyBest) + "  //  WAVE " + meta.DailyBestWave.ToString("00"), 800, 766, 1.4f, M.WithAlpha(p.Accent, 0.7f));
    }

    /// <summary>Game over: the tracks this run played, with score, rank and how each compares with your record.</summary>
    private static void DrawRunTracks(Renderer r, Palette p, RunSummary s, MetaProgress meta, float y)
    {
        var tracks = s.Tracks.Where(t => t.Duration >= 5f).TakeLast(4).ToList();
        if (tracks.Count == 0) return;
        foreach (var t in tracks)
        {
            string title = FormatTrack(t.Title, 34);
            var rec = t.IsSong ? meta.GetSong(t.Title) : null;
            bool best = rec != null && rec.BestScore == t.Score && t.Score > 0;
            string right = FormatScore(t.Score) + "   RANK " + t.Rank + (best ? "   SONG BEST" : "");
            r.UiText(title, 360, y, 1.45f, M.WithAlpha(p.Text, 0.9f));
            r.UiTextRight(right, 1240, y, 1.45f, best ? p.Accent : M.WithAlpha(p.Dim, 0.95f), best);
            y += 22;
        }
    }

    /// <summary>Recap strip: the run along the song — sections, score climb, hits, drops and your best moment.</summary>
    public static void DrawRecap(Renderer r, Palette p, RunSummary s, float y, float time)
    {
        var tl = s.Timeline!;
        const float x0 = 340f, x1 = 1260f, h = 78f;
        float dur = MathF.Max(1f, tl.Duration);
        float X(float t) => x0 + (x1 - x0) * M.Clamp01(t / dur);
        r.UiRect(x0, y, x1 - x0, h, new Vector4(0.01f, 0.012f, 0.03f, 0.8f));
        // song sections as coloured bands, brightness from the music's energy
        var smp = tl.Samples;
        for (int i = 1; i < smp.Count; i++)
        {
            float a = X(smp[i - 1].Time), b = X(smp[i].Time);
            var c = M.Hsv((0.55f + smp[i].Section * 0.17f) % 1f, 0.55f, 0.9f, 0.10f + smp[i].Energy * 0.25f);
            r.UiRect(a, y, MathF.Max(1f, b - a), h, c);
        }
        // track starts
        foreach (var (t, title) in tl.TrackStarts)
        {
            if (t <= 0.5f) continue;
            r.UiRect(X(t), y - 4, 1.5f, h + 8, M.WithAlpha(p.Text, 0.55f));
        }
        // the score climbing
        long top = Math.Max(1, smp.Count > 0 ? smp[^1].Score : 1);
        for (int i = 1; i < smp.Count; i++)
        {
            float ya = y + h - 6 - (h - 12) * (smp[i - 1].Score / (float)top);
            float yb = y + h - 6 - (h - 12) * (smp[i].Score / (float)top);
            r.UiLine(X(smp[i - 1].Time), ya, X(smp[i].Time), yb, 2f, M.WithAlpha(p.Accent, 0.9f), true);
        }
        foreach (float d in tl.Drops) r.UiText("v", X(d) - 4, y - 16, 1.3f, M.WithAlpha(p.Accent, 0.9f));
        foreach (float hit in tl.Hits) r.UiRect(X(hit) - 1.5f, y + h - 14, 3f, 14, new Vector4(1f, 0.3f, 0.35f, 0.95f), true);
        if (tl.BestMomentTime >= 0f)
        {
            float bx = X(tl.BestMomentTime);
            float pulse = 0.7f + 0.3f * MathF.Sin(time * 6f);
            r.UiFrame(bx - 18, y - 2, 36, h + 4, 1.5f, M.WithAlpha(p.Accent, pulse), true);
            r.UiTextCentered("BEST MOMENT +" + FormatScore(tl.BestMomentGain), bx, y + h + 6, 1.25f, M.WithAlpha(p.Accent, 0.95f), true);
        }
        r.UiText("0S", x0, y + h + 6, 1.15f, M.WithAlpha(p.Dim, 0.7f));
        r.UiTextRight((int)dur + "S", x1, y + h + 6, 1.15f, M.WithAlpha(p.Dim, 0.7f));
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
        r.UiRect(x, y, w, h, PanelFill(new Vector4(0.006f, 0.009f, 0.021f, 0.9f), 0.94f));
        r.UiFrame(x, y, w, h, 1f, M.WithAlpha(accent, Abyssal ? 0.58f : 0.7f));
        r.UiRect(x, y, w, 3, M.WithAlpha(accent, 0.85f), true);
        r.UiRect(x, y + h - 2, w, 2, M.WithAlpha(accent, 0.18f));
    }

    private static void DrawStat(Renderer r, string label, string value, float centerX, float y, Palette p)
    {
        r.UiTextCentered(label, centerX, y, 1.45f, M.WithAlpha(p.Dim, 0.8f));
        r.UiTextCentered(value, centerX, y + 20, 2.8f, p.Text, true);
    }

    /// <summary>Stat with a pulsing gold "NEW BEST" tag under the value when it broke a personal best.</summary>
    private static void DrawStat(Renderer r, string label, string value, float centerX, float y, Palette p, bool best, float pulse)
    {
        if (!best)
        {
            DrawStat(r, label, value, centerX, y, p);
            return;
        }
        r.UiTextCentered(label, centerX, y, 1.45f, M.WithAlpha(p.Accent, 0.9f));
        r.UiTextCentered(value, centerX, y + 20, 2.8f, p.Accent, true);
        float tagW = r.UiTextWidth("NEW BEST", 1.3f) + 16f;
        r.UiFrame(centerX - tagW * 0.5f, y + 45, tagW, 17, 1f, M.WithAlpha(p.Accent, 0.55f * pulse), true);
        r.UiTextCentered("NEW BEST", centerX, y + 49, 1.3f, M.WithAlpha(p.Accent, pulse), true);
    }

    /// <summary>"NEW RECORD // SCORE +12,300 // CHAIN +6" (no delta when there was no previous best).</summary>
    public static string RecordBanner(RecordBreak b, RunSummary s)
    {
        var parts = new List<string>(3);
        if (b.Score) parts.Add(b.PreviousScore > 0 ? "SCORE +" + FormatScore(s.Score - b.PreviousScore) : "SCORE");
        if (b.Wave) parts.Add(b.PreviousWave > 0 ? "WAVE +" + (s.Wave - b.PreviousWave) : "WAVE");
        if (b.Combo) parts.Add(b.PreviousCombo > 0 ? "CHAIN +" + (s.MaxCombo - b.PreviousCombo) : "CHAIN");
        return parts.Count == 0 ? "" : (parts.Count > 1 ? "NEW RECORDS // " : "NEW RECORD // ") + string.Join(" // ", parts);
    }

    /// <summary>One flight-recorder entry for the archive strip: "D 4F2A9C1B W12 84,500 CH36*".</summary>
    public static string RecentSignalEntry(RunRecord run) =>
        (run.Daily ? "D " : "") + run.Seed + " W" + run.Wave.ToString("00") + " " + FormatScore(run.Score) + " CH" + run.MaxCombo + (run.Records != RunRecords.None ? "*" : "");

    private static void DrawRecentSignals(Renderer r, Palette p, MetaProgress meta, float x, float right, float y, string selectedSeed = "")
    {
        const float size = 1.4f;
        const string label = "RECENT SIGNALS //";
        r.UiText(label, x, y, size, M.WithAlpha(p.Accent, 0.85f));
        float cx = x + r.UiTextWidth(label, size) + 14f;
        float gap = r.UiTextWidth("  |  ", size);
        for (int i = 0; i < meta.RecentRuns.Count; i++)
        {
            string entry = RecentSignalEntry(meta.RecentRuns[i]);
            float w = r.UiTextWidth(entry, size);
            if (cx + w > right) break;
            bool picked = !string.IsNullOrEmpty(selectedSeed) && string.Equals(meta.RecentRuns[i].Seed, selectedSeed, StringComparison.OrdinalIgnoreCase);
            if (picked)
            {
                r.UiRect(cx - 5f, y - 4f, w + 10f, 17f, M.WithAlpha(p.Accent, 0.14f));
                r.UiFrame(cx - 5f, y - 4f, w + 10f, 17f, 1f, M.WithAlpha(p.Accent, 0.8f), true);
            }
            var color = picked ? p.Accent : i == 0 ? M.WithAlpha(p.Text, 0.9f) : M.WithAlpha(p.Dim, 0.85f - i * 0.05f);
            if (!picked && meta.RecentRuns[i].Records != RunRecords.None) color = M.WithAlpha(p.Accent, i == 0 ? 0.95f : 0.75f);
            r.UiText(entry, cx, y, size, color, i == 0 || picked);
            cx += w;
            if (i + 1 < meta.RecentRuns.Count && cx + gap < right)
                r.UiText("  |  ", cx, y, size, M.WithAlpha(p.Dim, 0.4f));
            cx += gap;
        }
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

    public static string FormatTrack(string? track, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(track)) return "";
        string s = track.Trim().ToUpperInvariant();
        if (s.Length <= maxChars) return s;
        return maxChars <= 2 ? s[..Math.Max(0, maxChars)] : s[..(maxChars - 2)] + "..";
    }

    private static string SourceLabel(AudioState audio)
    {
        if (audio.LocalTrackActive)
            return audio.LookaheadSeconds > 0.01f ? $"LOCAL TRACK (+{audio.LookaheadSeconds:0.00}S)" : "LOCAL TRACK";
        if (audio.External)
        {
            string name = audio.DeviceName;
            if (name.Length > 28) name = name[..28] + "..";
            return (audio.AppCapture ? "APP / " : "LOOPBACK / ") + (string.IsNullOrWhiteSpace(name) ? "SYSTEM" : name.ToUpperInvariant());
        }
        if (audio.Quiet) return "SILENT SIGNAL";
        return audio.LookaheadSeconds > 0.01f ? $"GENERATIVE SCORE (+{audio.LookaheadSeconds:0.00}S)" : "GENERATIVE SCORE";
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
