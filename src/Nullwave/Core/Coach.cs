using System.Numerics;
using Nullwave.Render;
using Nullwave.UI;
using GameWorld = Nullwave.World.World;

namespace Nullwave.Core;

/// <summary>
/// First-run tutorial and one-time tips. Six short steps teach the core loop (move, fire, dash,
/// Flow, Phase Sync, Cascade), each finishing when you actually do it; tips explain wells, rails,
/// bosses, Overdrive and stages the first time they appear. Tab skips the tutorial.
/// </summary>
public sealed class Coach
{
    private readonly record struct Step(string Title, string Text);

    private static readonly Step[] Steps =
    [
        new("MOVE", "WASD OR LEFT STICK. FLY AROUND A LITTLE."),
        new("AIM AND FIRE", "AIM WITH THE MOUSE (OR RIGHT STICK) AND HOLD LEFT CLICK (OR RT). TAKE OUT 3 ENEMIES."),
        new("DASH", "SPACE OR A. YOU ARE UNTOUCHABLE MID-DASH, AND DASHING THROUGH BULLETS ABSORBS THEM. DASH TWICE."),
        new("BUILD FLOW", "FLY FAST WITHOUT GETTING HIT. FLOW MULTIPLIES YOUR SCORE AND SPEEDS EVERYTHING UP."),
        new("PHASE SYNC", "DASH EXACTLY ON THE BEAT FOR A PHASE SYNC BONUS. WATCH THE PULSE, THEN DASH."),
        new("CASCADE", "GRAZE BULLETS TO FILL RESONANCE. WHEN IT IS FULL, PRESS E (OR X) TO WIPE THE BULLETS AROUND YOU."),
    ];

    public bool Active { get; private set; }
    public int StepIndex { get; private set; }
    public float Progress { get; private set; }
    private float _doneFlash;
    private Vector2 _lastPos;
    private float _travel;
    private int _killsAtStep, _syncsAtStep, _burstsAtStep, _dashes;
    private bool _wasDashing;
    private string _tip = "";
    private string _tipTitle = "";
    private float _tipTimer;
    private readonly Queue<(string Title, string Text)> _tips = new();

    /// <summary>Starts the tutorial for a run (only if it has never been finished).</summary>
    public void Begin(GameWorld w, bool tutorial)
    {
        Active = tutorial;
        StepIndex = 0;
        Progress = 0f;
        _doneFlash = 0f;
        _travel = 0f;
        _lastPos = w.Player.Pos;
        _dashes = 0;
        _tips.Clear();
        _tipTimer = 0f;
        MarkStepStart(w);
    }

    public void Skip() => Active = false;

    private void MarkStepStart(GameWorld w)
    {
        _killsAtStep = w.Kills;
        _syncsAtStep = w.PhaseSyncs;
        _burstsAtStep = w.Bursts;
        _dashes = 0;
        _travel = 0f;
    }

    /// <summary>Advances the tutorial; returns true when the last step was just finished.</summary>
    public bool Update(GameWorld w, float dt, HashSet<string> seenTips, bool tips)
    {
        bool finished = false;
        var p = w.Player;
        _travel += Vector2.Distance(p.Pos, _lastPos);
        _lastPos = p.Pos;
        if (p.IsDashing && !_wasDashing) _dashes++;
        _wasDashing = p.IsDashing;

        if (Active)
        {
            if (_doneFlash > 0f)
            {
                _doneFlash -= dt;
                if (_doneFlash <= 0f)
                {
                    StepIndex++;
                    Progress = 0f;
                    MarkStepStart(w);
                    if (StepIndex >= Steps.Length) { Active = false; finished = true; }
                }
            }
            else
            {
                Progress = StepIndex switch
                {
                    0 => _travel / 900f,
                    1 => (w.Kills - _killsAtStep) / 3f,
                    2 => _dashes / 2f,
                    3 => w.Flow / 0.6f,
                    4 => w.PhaseSyncs > _syncsAtStep ? 1f : 0f,
                    _ => w.Bursts > _burstsAtStep ? 1f : M.Clamp01(p.Resonance / MathF.Max(1f, p.ResonanceMax)) * 0.95f,
                };
                Progress = M.Clamp01(Progress);
                if (Progress >= 1f) _doneFlash = 0.9f;
            }
        }

        if (tips)
        {
            void Tip(string id, bool when, string title, string text)
            {
                if (!when || seenTips.Contains(id)) return;
                seenTips.Add(id);
                _tips.Enqueue((title, text));
            }
            Tip("well", w.Wells.Count > 0, "BASS WELL", "A SUSTAINED BASS NOTE OPENED A GRAVITY WELL. ITS CORE SWALLOWS BULLETS; SKIM ITS RING AT SPEED AND BREAK AWAY TO SLINGSHOT.");
            Tip("rail", w.Rails.Count > 0, "MELODY RAIL", "THE MELODY IS DRAWING A RAIL. RIDE ALONG IT FOR SPEED; BULLETS IT TOUCHES TURN WHITE-RINGED AND PASS THROUGH YOU WHILE YOU RIDE.");
            Tip("choir", w.BossEnemy is { Dead: false, SongBoss: false }, "THE CHOIR", "A BOSS. IT CHANGES PHASE AS IT LOSES HEALTH; EACH PHASE BREAK CLEARS ITS BULLETS.");
            Tip("drop", w.BossEnemy is { Dead: false, SongBoss: true }, "THE DROP", "THIS BOSS PLAYS THE SONG: RINGS ON THE KICK, FANS ON THE SNARE, A LASER THAT FOLLOWS THE MELODY. EACH SONG SECTION IS A NEW PHASE.");
            Tip("overdrive", w.IsDropOverdrive, "DROP OVERDRIVE", "THE DROP HIT: FASTER FIRE, MORE DAMAGE AND MORE SCORE FOR A FEW SECONDS.");
            Tip("stage", w.IsSetlist && w.Stage > 1, "STAGE CLEAR", "A NEW TRACK STARTED: THE FLOOR IS CLEARED AND YOU PICK AN UPGRADE. THE LAST TRACK ENDS WITH THE DROP.");
            Tip("ship_" + w.Ship, w.Ship != World.ShipKind.Classic && w.SessionTime > 1f, World.Ships.Name(w.Ship) + " SHIP", World.Ships.Blurb(w.Ship) + ". HOLD FIRE AND LET THE MUSIC PULL THE TRIGGER.");
        }
        if (_tipTimer > 0f) _tipTimer -= dt;
        else if (_tips.Count > 0)
        {
            (_tipTitle, _tip) = _tips.Dequeue();
            _tipTimer = 6f;
        }
        return finished;
    }

    public void Draw(Renderer r, Palette p, float time)
    {
        if (Active && StepIndex < Steps.Length)
        {
            var step = Steps[StepIndex];
            const float x = 520, y = 262, w = 560, h = 92;
            bool done = _doneFlash > 0f;
            r.UiRect(x, y, w, h, new Vector4(0.01f, 0.012f, 0.03f, 0.78f));
            r.UiFrame(x, y, w, h, 1f, M.WithAlpha(done ? p.Accent : p.Primary, 0.7f), true);
            r.UiText("TUTORIAL " + (StepIndex + 1) + " / " + Steps.Length + "  //  " + step.Title + (done ? "  //  DONE" : ""), x + 16, y + 12, 1.6f, done ? p.Accent : p.Text, true);
            DrawWrap(r, step.Text, x + 16, y + 38, 1.3f, w - 32, M.WithAlpha(p.Dim, 0.95f));
            r.UiRect(x + 16, y + h - 12, w - 32, 4, new Vector4(0.03f, 0.03f, 0.06f, 0.9f));
            r.UiRect(x + 16, y + h - 12, (w - 32) * (done ? 1f : Progress), 4, M.WithAlpha(p.Accent, 0.95f), true);
            r.UiTextRight("TAB SKIP", x + w - 14, y + 12, 1.2f, M.WithAlpha(p.Dim, 0.7f));
        }
        if (_tipTimer > 0f)
        {
            float a = M.Clamp01(_tipTimer / 0.6f) * M.Clamp01((6f - _tipTimer) / 0.3f);
            const float x = 1060, y = 230, w = 480, h = 86;
            r.UiRect(x, y, w, h, new Vector4(0.01f, 0.012f, 0.03f, 0.78f * a));
            r.UiFrame(x, y, w, h, 1f, M.WithAlpha(p.Accent, 0.7f * a), true);
            r.UiText("TIP // " + _tipTitle, x + 14, y + 10, 1.5f, M.WithAlpha(p.Accent, a), true);
            DrawWrap(r, _tip, x + 14, y + 34, 1.2f, w - 28, M.WithAlpha(p.Dim, 0.95f * a));
        }
    }

    private static void DrawWrap(Renderer r, string text, float x, float y, float size, float maxWidth, Vector4 color)
    {
        // ~7 px per character at size 1 in the UI font
        int perLine = Math.Max(10, (int)(maxWidth / (7.2f * size)));
        var words = text.Split(' ');
        string line = "";
        foreach (var word in words)
        {
            if (line.Length + word.Length + 1 > perLine)
            {
                r.UiText(line, x, y, size, color);
                y += 16 * size;
                line = word;
            }
            else line = line.Length == 0 ? word : line + " " + word;
        }
        if (line.Length > 0) r.UiText(line, x, y, size, color);
    }
}
