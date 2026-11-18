using Nullwave.Core;

namespace Nullwave.Audio;

/// <summary>
/// Reads the structure of a song, not just its instantaneous level, and turns it into a small
/// set of visual drivers shared by the renderer, the post pass, the camera and the world:
///   Intensity  - where the current energy sits in the song's own recent range (0..1)
///   Build      - rising energy and highs: a build-up is under way (0..1)
///   Breakdown  - a sustained quiet section after louder music (0..1)
///   Drop       - one-frame event + decaying pulse when energy slams back after a build/quiet stretch
///   Brightness - spectral centroid (dark/bassy 0 .. bright/airy 1)
///   MoodHue    - slow hue shift (radians) driven by brightness and intensity
///   Kick       - sharp pulse on every beat from the locked beat grid, stronger on downbeats
///   Sparkle    - hi-hat / air transients
/// Pure function of the AudioState sequence, so it is unit tested with synthetic sections.
/// </summary>
public sealed class MusicDirector
{
    private float _inst, _fast, _mid, _slow, _hi = 0.5f, _lo = 0.1f;
    private float _highFast, _highSlow;
    private float _quietTime, _sinceDrop = 99f;
    private float _buildRaw;
    private float _prevHigh;
    private float _age = -1f;
    // 10 Hz history of the fast level, ~4 s, to compare against the recent peak
    private readonly float[] _hist = new float[40];
    private int _histPos;
    private float _histTimer;
    private float _slotPeak;
    private float _sinceBeat = 99f;

    public float Intensity { get; private set; }
    public float Build { get; private set; }
    public float Breakdown { get; private set; }
    public float DropPulse { get; private set; }
    /// <summary>Lingering post-drop energy (decays over ~8 s).</summary>
    public float DropGlow { get; private set; }
    public bool DropTriggered { get; private set; }
    public float Brightness { get; private set; } = 0.5f;
    public float MoodHue { get; private set; }
    public float Kick { get; private set; }
    public float Sparkle { get; private set; }
    public int Drops { get; private set; }

    /// <summary>Pins the section state for deterministic screenshots.</summary>
    public void ForceForCapture(float intensity, float build, float breakdown, float drop, float hue, float kick)
    {
        Intensity = intensity; Build = build; Breakdown = breakdown;
        DropPulse = drop; DropGlow = drop; MoodHue = hue; Kick = kick;
        _forced = true;
    }

    private bool _forced;

    public void Reset()
    {
        _fast = _mid = _slow = 0f;
        _age = -1f;
        _hi = 0.5f; _lo = 0.1f;
        _quietTime = 0; _sinceDrop = 99f;
        Intensity = Build = Breakdown = DropPulse = DropGlow = Kick = Sparkle = 0f;
        Brightness = 0.5f; MoodHue = 0f;
        Drops = 0;
    }

    public void Update(AudioState a, float dt)
    {
        if (_forced) return;
        dt = Math.Clamp(dt, 0f, 0.1f);
        DropTriggered = false;
        _sinceDrop += dt;

        // dynamics in dB: Energy saturates on loud masters, decibels do not
        float e = M.Clamp01((20f * MathF.Log10(MathF.Max(a.Loudness, 1e-5f)) + 48f) / 42f);
        if (_age < 0f)
        {
            // seed the averages with the first reading so the opening is not mistaken for a slam
            _inst = _fast = _mid = _slow = e;
            _hi = e + 0.1f; _lo = MathF.Max(0f, e - 0.1f);
            Array.Fill(_hist, e);
            _age = 0f;
        }
        _age += dt;
        _inst = M.ExpSmooth(_inst, e, 18f, dt);      // ~0.06 s, for the slam itself
        _fast = M.ExpSmooth(_fast, e, 4f, dt);       // ~0.25 s
        _mid = M.ExpSmooth(_mid, e, 0.8f, dt);       // ~1.2 s
        _slow = M.ExpSmooth(_slow, e, 0.15f, dt);    // ~7 s
        // the song's own dynamic range, slowly forgetting
        // the song's dynamic range, remembered for about a minute so a breakdown does not erase it
        _hi = MathF.Max(_mid, _hi - dt * 0.004f);
        _lo = MathF.Min(_mid, _lo + dt * 0.004f);
        float range = MathF.Max(0.14f, _hi - _lo);   // ~6 dB: compressed masters still read sections
        Intensity = M.ExpSmooth(Intensity, M.Clamp01((_mid - _lo) / range), 3f, dt);

        // build-up: energy and highs trending up over a few seconds
        _highFast = M.ExpSmooth(_highFast, a.High + a.Air * 0.5f, 1.2f, dt);
        _highSlow = M.ExpSmooth(_highSlow, a.High + a.Air * 0.5f, 0.25f, dt);
        float rise = (_mid - _slow) * 3f + (_highFast - _highSlow) * 2.5f;
        _buildRaw = M.ExpSmooth(_buildRaw, M.Clamp01(rise), 1.5f, dt);
        Build = DropPulse > 0.2f ? M.ExpSmooth(Build, 0f, 6f, dt) : _buildRaw;

        // breakdown: clearly below the song's usual level for a while
        bool quiet = _mid < _lo + range * 0.3f && _slow > 0.08f;
        _quietTime = quiet ? _quietTime + dt : MathF.Max(0f, _quietTime - dt * 2f);
        Breakdown = M.ExpSmooth(Breakdown, M.Clamp01((_quietTime - 1.5f) / 2f), 2f, dt);

        // drop: energy slams up past its recent average after a build or quiet stretch, on a beat
        // history keeps the peak of the fast level in each 0.1 s slot, so beat bumps count
        _slotPeak = MathF.Max(_slotPeak, _inst);
        _histTimer += dt;
        if (_histTimer >= 0.1f)
        {
            _histTimer = 0f;
            _hist[_histPos] = _slotPeak;
            _histPos = (_histPos + 1) % _hist.Length;
            _slotPeak = 0f;
        }
        // peak of the window from ~3.5 s to ~0.5 s ago
        float pastPeak = 0f;
        for (int k = 5; k < 35; k++) pastPeak = MathF.Max(pastPeak, _hist[(_histPos - 1 - k + _hist.Length * 2) % _hist.Length]);
        // a drop clears the recent peak by a wide margin; a gradual build never does
        bool slam = _inst > _slow + range * 0.45f && _inst > pastPeak + range * 0.25f && _inst > 0.3f;
        bool primed = _buildRaw > 0.25f || _quietTime > 1.5f || Breakdown > 0.3f;
        _sinceBeat = a.Beat || a.Downbeat ? 0f : _sinceBeat + dt;
        // the level needs a few frames to rise after the drop's first kick, so accept a recent beat
        bool onBeat = _sinceBeat < 0.15f || !a.BeatLocked;
        if (slam && primed && onBeat && _sinceDrop > 10f && _age > 8f)
        {
            DropTriggered = true;
            if (Environment.GetEnvironmentVariable("NULLWAVE_DIRDEBUG") == "1")
                Console.WriteLine($"    drop: age={_age:0.00} inst={_inst:0.000} past={pastPeak:0.000} slow={_slow:0.000} range={range:0.000} build={_buildRaw:0.00} quiet={_quietTime:0.0}");
            DropPulse = 1f;
            DropGlow = 1f;
            _sinceDrop = 0f;
            _quietTime = 0f;
            _buildRaw = 0f;
            Drops++;
        }
        DropPulse *= MathF.Exp(-dt * 2.2f);
        DropGlow *= MathF.Exp(-dt * 0.14f);

        // colour of the sound: spectral centroid over the 48 bars
        float sum = 0f, weighted = 0f;
        var spec = a.Spectrum;
        for (int i = 0; i < spec.Length; i++) { sum += spec[i]; weighted += spec[i] * i; }
        float centroid = sum > 0.01f ? weighted / sum / (spec.Length - 1) : 0.4f;
        Brightness = M.ExpSmooth(Brightness, M.Clamp01((centroid - 0.2f) / 0.45f), 0.6f, dt);
        float hueTarget = (Brightness - 0.5f) * 0.55f + (Intensity - 0.5f) * 0.35f + DropGlow * 0.25f - Breakdown * 0.3f;
        MoodHue = M.ExpSmooth(MoodHue, hueTarget, 0.35f, dt);

        // kick: exact from the locked grid, otherwise the audio pulse
        if (a.BeatLocked)
        {
            double frac = a.BeatPosition - Math.Floor(a.BeatPosition);
            long index = (long)Math.Floor(a.BeatPosition);
            bool down = ((index - a.DownbeatOffset) % 4 + 4) % 4 == 0;
            Kick = MathF.Exp(-(float)frac * 7f) * (down ? 1f : 0.65f);
        }
        else
        {
            Kick = a.BeatPulse * 0.6f;
        }
        Kick *= 0.5f + 0.5f * Intensity;

        float hi = a.High + a.Air;
        float transient = MathF.Max(0f, hi - _prevHigh);
        _prevHigh = hi;
        Sparkle = MathF.Max(Sparkle * MathF.Exp(-dt * 9f), M.Clamp01(transient * 6f));
    }
}
