using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.Render;

namespace Nullwave.UI;

/// <summary>
/// Settings screen: keyboard, gamepad and mouse. Up/Down select, Left/Right adjust (hold to
/// repeat), Enter toggles, Esc goes back and saves. Sliders can be clicked or dragged.
/// </summary>
public sealed class SettingsMenu
{
    private enum Kind { Slider, Toggle, Action }

    private sealed record Row(
        string Section, string Label, Kind Kind,
        Func<GameSettings, int> Get, Action<GameSettings, int> Set,
        int Min, int Max, int Step, string Unit, string Hint);

    private const float PanelX = 330, PanelW = 940, Top = 122, RowH = 26, HeaderH = 32;
    private const float BarX = 800, BarW = 300;

    private readonly List<Row> _rows = new();
    private readonly float[] _rowY;
    private int _index;
    private int _dragRow = -1;
    private float _repeat;
    private int _repeatDir;

    /// <summary>Called when the fullscreen toggle changes.</summary>
    public Action<bool>? FullscreenChanged;
    /// <summary>Cycles the audio source and returns the new label.</summary>
    public Func<string>? CycleAudioSource;
    /// <summary>Applies audio volumes immediately so changes are heard while adjusting.</summary>
    public Action? AudioChanged;

    public int Index => _index;

    public SettingsMenu()
    {
        Slider("AUDIO", "MASTER VOLUME", s => s.MasterVolume, (s, v) => s.MasterVolume = v, 0, 100, 5, "%", "Overall game volume. Never affects the app you are capturing.");
        Slider("AUDIO", "MUSIC VOLUME", s => s.MusicVolume, (s, v) => s.MusicVolume = v, 0, 100, 5, "%", "The built-in score. It already fades out whenever other music is playing.");
        Slider("AUDIO", "EFFECTS VOLUME", s => s.SfxVolume, (s, v) => s.SfxVolume = v, 0, 100, 5, "%", "Shots, hits, explosions and menu sounds.");
        _rows.Add(new Row("AUDIO", "AUDIO SOURCE", Kind.Action, _ => 0, (_, _) => { }, 0, 0, 0, "", "Which audio drives the game: all system audio, or one app (F3 in game)."));
        Slider("AUDIO", "BEAT OFFSET", s => s.BeatOffsetMs, (s, v) => s.BeatOffsetMs = v, -200, 200, 10, "MS", "Enemies fire early or late versus what you hear? Shift them later (+) or earlier (-).");
        Slider("AUDIO", "BEAT MOTION", s => s.BeatMotion, (s, v) => s.BeatMotion = v, 0, 150, 10, "%", "How hard bullets and enemies surge forward on each beat. 0 = steady speed, same difficulty.");
        Toggle("VIDEO", "FULLSCREEN", s => s.Fullscreen, (s, v) => s.Fullscreen = v, "Also F2.");
        Slider("VIDEO", "BACKGROUND INTENSITY", s => s.BackgroundIntensity, (s, v) => s.BackgroundIntensity = v, 0, 150, 10, "%", "Brightness of the liquid backdrop. Lower it if the fight is hard to read.");
        Slider("VIDEO", "BACKGROUND MOTION", s => s.BackgroundMotion, (s, v) => s.BackgroundMotion = v, 0, 200, 10, "%", "How fast the backdrop flows with the music. 0 freezes it.");
        Slider("VIDEO", "BLOOM", s => s.Bloom, (s, v) => s.Bloom = v, 0, 150, 10, "%", "Glow around bright shots and lights.");
        Slider("VIDEO", "SCREEN SHAKE", s => s.ScreenShake, (s, v) => s.ScreenShake = v, 0, 150, 10, "%", "Camera shake from hits, bosses and explosions.");
        Slider("VIDEO", "CHROMATIC ABERRATION", s => s.Aberration, (s, v) => s.Aberration = v, 0, 100, 10, "%", "Colour fringing that pulses on the beat.");
        Slider("VIDEO", "MUSIC FX", s => s.MusicFx, (s, v) => s.MusicFx = v, 0, 150, 10, "%", "Colour mood, kick thump, camera punch, build-ups and drops driven by the song.");
        Toggle("VIDEO", "REDUCE FLASHING", s => s.ReduceFlashing, (s, v) => s.ReduceFlashing = v, "Softens full-screen flashes and turns off glitch effects.");
        Toggle("VIDEO", "SHOW FPS", s => s.ShowFps, (s, v) => s.ShowFps = v, "Frame rate counter in the corner of the HUD.");
        Toggle("INTERFACE", "MINIMAP", s => s.Minimap, (s, v) => s.Minimap = v, "Map of the arena: monoliths, enemies, pickups and your view.");
        Toggle("INTERFACE", "THREAT ARROWS", s => s.ThreatArrows, (s, v) => s.ThreatArrows = v, "Arrows at the screen edge pointing at enemies outside the view.");
        Toggle("INTERFACE", "ENEMY HEALTH BARS", s => s.EnemyHealthBars, (s, v) => s.EnemyHealthBars = v, "Bars over damaged and elite enemies.");
        Toggle("INTERFACE", "BEAT TELEGRAPHS", s => s.BeatTelegraphs, (s, v) => s.BeatTelegraphs = v, "Rings that close on an enemy exactly when it fires on the beat.");
        Slider("INTERFACE", "CROSSHAIR SIZE", s => s.CrosshairSize, (s, v) => s.CrosshairSize = v, 50, 200, 10, "%", "Size of the mouse crosshair.");
        _rows.Add(new Row("", "RESET TO DEFAULTS", Kind.Action, _ => 0, (_, _) => { }, 0, 0, 0, "", "Restore every setting. The audio source is kept."));
        _rows.Add(new Row("", "BACK", Kind.Action, _ => 0, (_, _) => { }, 0, 0, 0, "", "Save and return. Esc also works."));

        _rowY = new float[_rows.Count];
        float y = Top;
        string section = "";
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Section != section)
            {
                section = _rows[i].Section;
                y += section.Length > 0 ? HeaderH : 16;
            }
            _rowY[i] = y;
            y += RowH;
        }
    }

    private void Slider(string section, string label, Func<GameSettings, int> get, Action<GameSettings, int> set, int min, int max, int step, string unit, string hint)
        => _rows.Add(new Row(section, label, Kind.Slider, get, set, min, max, step, unit, hint));

    private void Toggle(string section, string label, Func<GameSettings, bool> get, Action<GameSettings, bool> set, string hint)
        => _rows.Add(new Row(section, label, Kind.Toggle, s => get(s) ? 1 : 0, (s, v) => set(s, v != 0), 0, 1, 1, "", hint));

    public void Open()
    {
        _index = 0;
        _dragRow = -1;
    }

    /// <summary>Returns true when the player leaves the menu.</summary>
    public bool Update(InputState input, Vector2 mouse, SoundBank sfx, float dt)
    {
        var s = GameSettings.Current;
        if (input.BackPressed || input.PausePressed) return true;
        if (input.UpPressed) { _index = (_index + _rows.Count - 1) % _rows.Count; sfx.Trigger(SfxType.Menu, 0.2f); }
        if (input.DownPressed) { _index = (_index + 1) % _rows.Count; sfx.Trigger(SfxType.Menu, 0.2f); }

        // mouse: hover selects, click activates, sliders follow a drag
        int hover = RowAt(mouse);
        if (hover >= 0 && (mouse != _lastMouse)) _index = hover;
        _lastMouse = mouse;
        if (input.MouseLeftPressed && hover >= 0)
        {
            var row = _rows[hover];
            if (row.Kind == Kind.Slider && mouse.X >= BarX - 10) _dragRow = hover;
            else if (Activate(hover, 1, sfx)) return true;
        }
        if (!input.MouseLeftDown) _dragRow = -1;
        if (_dragRow >= 0)
        {
            var row = _rows[_dragRow];
            float t = M.Clamp01((mouse.X - BarX) / BarW);
            int v = row.Min + (int)MathF.Round(t * (row.Max - row.Min) / row.Step) * row.Step;
            if (v != row.Get(s)) { row.Set(s, v); Changed(row, sfx); }
        }

        // keyboard / pad: left-right adjusts, holding repeats
        int dir = input.LeftPressed ? -1 : input.RightPressed ? 1 : 0;
        if (dir != 0)
        {
            _repeatDir = dir;
            _repeat = 0.38f;
            if (Activate(_index, dir, sfx)) return true;
        }
        else if (_repeatDir != 0)
        {
            bool held = _repeatDir < 0 ? input.Move.X < -0.5f : input.Move.X > 0.5f;
            if (!held) _repeatDir = 0;
            else if ((_repeat -= dt) <= 0f && _rows[_index].Kind == Kind.Slider)
            {
                _repeat = 0.055f;
                Activate(_index, _repeatDir, sfx);
            }
        }
        if (input.ConfirmPressed && Activate(_index, 1, sfx, confirm: true)) return true;
        return false;
    }

    private Vector2 _lastMouse;

    private bool Activate(int index, int dir, SoundBank sfx, bool confirm = false)
    {
        var s = GameSettings.Current;
        var row = _rows[index];
        switch (row.Kind)
        {
            case Kind.Slider:
                if (confirm) return false;
                int v = Math.Clamp(row.Get(s) + dir * row.Step, row.Min, row.Max);
                if (v != row.Get(s)) { row.Set(s, v); Changed(row, sfx); }
                return false;
            case Kind.Toggle:
                row.Set(s, row.Get(s) == 0 ? 1 : 0);
                Changed(row, sfx);
                return false;
            default:
                if (row.Label == "BACK") return true;
                if (row.Label == "RESET TO DEFAULTS")
                {
                    bool wasFull = s.Fullscreen;
                    GameSettings.Current = new GameSettings();
                    AudioChanged?.Invoke();
                    if (wasFull) FullscreenChanged?.Invoke(false);
                    sfx.Trigger(SfxType.Unlock, 0.4f);
                    return false;
                }
                if (row.Label == "AUDIO SOURCE")
                {
                    _sourceToast = CycleAudioSource?.Invoke() ?? "";
                    _sourceToastTime = 2.5f;
                    sfx.Trigger(SfxType.Menu, 0.3f);
                }
                return false;
        }
    }

    private string _sourceToast = "";
    private float _sourceToastTime;

    private void Changed(Row row, SoundBank sfx)
    {
        if (row.Label == "FULLSCREEN") FullscreenChanged?.Invoke(GameSettings.Current.Fullscreen);
        if (row.Section == "AUDIO") AudioChanged?.Invoke();
        sfx.Trigger(SfxType.Menu, 0.25f);
    }

    private int RowAt(Vector2 m)
    {
        if (m.X < PanelX || m.X > PanelX + PanelW) return -1;
        for (int i = 0; i < _rows.Count; i++)
            if (m.Y >= _rowY[i] - 4 && m.Y < _rowY[i] + RowH - 4) return i;
        return -1;
    }

    public void Draw(Renderer r, Palette p, AudioState audio, string sourceLabel, float time, float dt)
    {
        var s = GameSettings.Current;
        if (_sourceToastTime > 0) _sourceToastTime -= dt;
        r.UiRect(0, 0, 1600, 900, new Vector4(0.002f, 0.004f, 0.012f, 0.8f));
        r.UiRect(PanelX - 30, 40, PanelW + 60, 830, new Vector4(0.006f, 0.009f, 0.021f, 0.9f));
        r.UiFrame(PanelX - 30, 40, PanelW + 60, 830, 1f, M.WithAlpha(p.Primary, 0.4f));
        r.UiRect(PanelX - 30, 40, PanelW + 60, 2, M.WithAlpha(p.Primary, 0.55f), true);
        r.UiTextCentered("SETTINGS", 800, 60, 4.0f, p.Text, true);

        string section = "";
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            float y = _rowY[i];
            if (row.Section != section)
            {
                section = row.Section;
                if (section.Length > 0)
                {
                    r.UiText(section, PanelX, y - 26, 1.6f, M.WithAlpha(p.Accent, 0.9f), true);
                    r.UiLine(PanelX + r.UiTextWidth(section, 1.6f) + 14, y - 19, PanelX + PanelW, y - 19, 1f, M.WithAlpha(p.Primary, 0.22f));
                }
            }
            bool sel = i == _index;
            if (sel)
            {
                r.UiRect(PanelX - 12, y - 6, PanelW + 24, RowH - 2, M.WithAlpha(p.Primary, 0.09f));
                r.UiRect(PanelX - 12, y - 6, 3, RowH - 2, p.Accent, true);
            }
            var labelCol = sel ? p.Text : M.WithAlpha(p.Dim, 0.95f);
            float textY = y + 1;
            bool centred = row.Section.Length == 0;
            if (centred) r.UiTextCentered(row.Label, 800, textY, 1.9f, labelCol, sel);
            else r.UiText(row.Label, PanelX + 8, textY, 1.75f, labelCol, sel);

            float right = PanelX + PanelW - 8;
            switch (row.Kind)
            {
                case Kind.Slider:
                {
                    int v = row.Get(s);
                    float t = (v - row.Min) / (float)Math.Max(1, row.Max - row.Min);
                    float by = y + 6;
                    r.UiRect(BarX, by, BarW, 6, new Vector4(0.03f, 0.035f, 0.07f, 1f));
                    if (row.Min < 0)
                    {
                        float zero = BarX + BarW * (-row.Min) / (float)(row.Max - row.Min);
                        float vx = BarX + BarW * t;
                        r.UiRect(MathF.Min(zero, vx), by, MathF.Abs(vx - zero), 6, M.WithAlpha(p.Primary, sel ? 0.95f : 0.6f), sel);
                        r.UiRect(zero - 1, by - 3, 2, 12, M.WithAlpha(p.Dim, 0.6f));
                    }
                    else
                    {
                        r.UiRect(BarX, by, BarW * t, 6, M.WithAlpha(p.Primary, sel ? 0.95f : 0.6f), sel);
                    }
                    r.UiRect(BarX + BarW * t - 3, by - 5, 6, 16, sel ? p.Text : M.WithAlpha(p.Dim, 0.9f), sel);
                    string txt = (row.Min < 0 && v > 0 ? "+" : "") + v + (row.Unit == "MS" ? " MS" : row.Unit);
                    r.UiTextRight(txt, right, textY, 1.75f, sel ? p.Text : M.WithAlpha(p.Dim, 0.95f));
                    break;
                }
                case Kind.Toggle:
                {
                    bool on = row.Get(s) != 0;
                    float px = right - 88;
                    r.UiRect(px, y - 3, 88, 22, on ? M.WithAlpha(p.Primary, 0.25f) : new Vector4(0.03f, 0.035f, 0.07f, 1f));
                    r.UiFrame(px, y - 3, 88, 22, 1f, on ? M.WithAlpha(p.Primary, 0.8f) : M.WithAlpha(p.Dim, 0.35f), on && sel);
                    r.UiTextCentered(on ? "ON" : "OFF", px + 44, y + 1, 1.7f, on ? p.Text : M.WithAlpha(p.Dim, 0.8f), on);
                    break;
                }
                default:
                    if (row.Label == "AUDIO SOURCE")
                    {
                        string label = sourceLabel.Length > 30 ? sourceLabel[..30] + ".." : sourceLabel;
                        r.UiTextRight("< " + label + " >", right, textY, 1.6f, audio.External ? p.Accent : M.WithAlpha(p.Dim, 0.95f), sel);
                    }
                    break;
            }
        }

        var cur = _rows[_index];
        string hint = _sourceToastTime > 0 && cur.Label == "AUDIO SOURCE" ? "NOW LISTENING TO // " + _sourceToast : cur.Hint;
        r.UiTextCentered(hint.ToUpperInvariant(), 800, 818, 1.4f, M.WithAlpha(p.Accent, 0.85f));
        r.UiTextCentered("UP / DOWN SELECT     LEFT / RIGHT ADJUST     ENTER TOGGLE     ESC BACK     MOUSE CLICK / DRAG", 800, 846, 1.25f, M.WithAlpha(p.Dim, 0.6f));
    }
}
