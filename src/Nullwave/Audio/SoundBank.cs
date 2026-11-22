using Nullwave.Core;

namespace Nullwave.Audio;

public enum SfxType
{
    Shot,
    Hit,
    Kill,
    Explosion,
    Dash,
    Graze,
    Pickup,
    LaserWarn,
    LaserFire,
    Burst,
    Hurt,
    Boss,
    Menu,
    Unlock,
    Wave,
    /// <summary>A pitched note (in-key guns): FM bell or soft pluck.</summary>
    Note,
    DrumKick,
    DrumSnare,
    DrumHat,
    BassShot,
    LeadShot,
}

/// <summary>
/// Layered electronic percussion, formant bass and tuned glass. Each trigger snapshots the
/// current harmony; ongoing voices keep their tuning through chord changes. Transients stay
/// immediate, with short decorrelated room tails and a bounded effects bus.
/// </summary>
public sealed class SoundBank
{
    private const int SampleRate = 48000;
    private const int MaxVoices = 48;
    private readonly object _lock = new();
    private readonly Voice[] _voices = new Voice[MaxVoices];
    private int _voiceCount;
    private long _seed = 0x9E3779B9;
    private float[] _dry = new float[4096];
    private readonly Reverb _reverb = new();
    private readonly IReadOnlyDictionary<SfxType, SfxSample> _samples;
    public int GeneratedSampleCount => _samples.Count;

    public SoundBank(bool useGeneratedSamples = true)
    {
        _samples = useGeneratedSamples ? SfxSamples.All : new Dictionary<SfxType, SfxSample>();
    }

    /// <summary>
    /// Playback rate for a sample layer. Pitched layers move to the voice's note (nearest octave, so the
    /// sample never shifts more than a tritone); unpitched ones keep the per-trigger variation.
    /// </summary>
    private static float PlayRateOf(SfxSample? sample, float targetHz, float variation)
    {
        if (sample is null) return 1f;
        if (sample.RootHz <= 0f || targetHz <= 0f) return variation;
        float ratio = targetHz / sample.RootHz;
        while (ratio > 1.41421356f) ratio *= 0.5f;
        while (ratio < 0.70710678f) ratio *= 2f;
        return ratio;
    }
    private float _rootHz = 130.8128f; // C3 until music is known
    private float _thirdRatio = 1.1892071f;

    /// <summary>Update future voices only. Disabling in-key sounds uses a fixed C minor palette.</summary>
    public void SetHarmony(int root, bool minor, bool enabled = true)
    {
        root = enabled && root >= 0 ? root % 12 : 0;
        lock (_lock)
        {
            _rootHz = 130.8128f * MathF.Pow(2f, root / 12f);
            _thirdRatio = MathF.Pow(2f, (enabled && !minor ? 4f : 3f) / 12f);
        }
    }

    private struct Voice
    {
        public SfxType Type;
        public float[]? Sample;
        public float PlayRate;     // playback rate of the sample layer (pitch)
        public double Age;
        public double Life;
        public float Gain;
        public float Pan;
        public uint Seed;
        public float Pitch;     // per-trigger variation so repeats do not machine-gun
        public double Phase1, Phase2, Phase3;
        public float Lp, Lp2, Hp, HpIn, Bp, BpLow, Hold;
        public int HoldCount;
        public float Freq, ThirdRatio;
        public int Timbre;
    }

    /// <summary>
    /// Plays a note (MIDI number) after <paramref name="delay"/> seconds — used to make kills and hits
    /// sound in the song's key and, for kills, land on the next sixteenth note.
    /// </summary>
    public void PlayNote(float midi, float gain, float pan = 0f, float delay = 0f, int timbre = 0)
    {
        lock (_lock)
        {
            if (_voiceCount >= MaxVoices)
            {
                Array.Copy(_voices, 1, _voices, 0, MaxVoices - 1);
                _voiceCount = MaxVoices - 1;
            }
            float hz = 440f * MathF.Pow(2f, (midi - 69f) / 12f);
            var sample = _samples.GetValueOrDefault(SfxType.Note);
            _voices[_voiceCount++] = new Voice
            {
                Type = SfxType.Note,
                Sample = sample?.Data,
                PlayRate = PlayRateOf(sample, hz, 1f),
                Life = timbre == 0 ? 0.9 : 0.35,
                Age = -Math.Clamp(delay, 0f, 0.5f),
                Gain = gain,
                Pan = Math.Clamp(pan, -1f, 1f),
                Seed = 1,
                Pitch = 1f,
                Freq = hz,
                Timbre = timbre,
            };
        }
    }

    public void Trigger(SfxType type, float gain = 1f, float pan = 0f)
    {
        lock (_lock)
        {
            if (_voiceCount >= MaxVoices)
            {
                Array.Copy(_voices, 1, _voices, 0, MaxVoices - 1);
                _voiceCount = MaxVoices - 1;
            }
            uint seed = (uint)(_seed = _seed * 6364136223846793005L + 1442695040888963407L >> 1);
            float pitch = 1f + ((seed >> 8) % 1000 / 1000f - 0.5f) * 0.12f;
            float freq = type == SfxType.Note ? _rootHz * 4f : _rootHz;
            var sample = _samples.GetValueOrDefault(type);
            _voices[_voiceCount++] = new Voice
            {
                Type = type,
                Life = LifeOf(type),
                Sample = sample?.Data,
                PlayRate = PlayRateOf(sample, freq, pitch),
                Gain = gain * LevelOf(type),
                Pan = Math.Clamp(pan, -1f, 1f),
                Seed = seed,
                Pitch = pitch,
                Freq = freq,
                ThirdRatio = _thirdRatio,
            };
        }
    }

    public void Clear()
    {
        lock (_lock) _voiceCount = 0;
    }

    public void Mix(float[] buffer, int offset, int count, float volume = 1f)
    {
        if (count <= 0) return;
        if (_dry.Length < count) _dry = new float[count];
        Array.Clear(_dry, 0, count);
        double dt = 1.0 / SampleRate;
        lock (_lock)
        {
            for (int v = 0; v < _voiceCount; v++)
            {
                ref Voice voice = ref _voices[v];
                float leftGain = voice.Gain * (voice.Pan <= 0 ? 1f : 1f - voice.Pan * 0.65f);
                float rightGain = voice.Gain * (voice.Pan >= 0 ? 1f : 1f + voice.Pan * 0.65f);
                for (int i = 0; i < count; i += 2)
                {
                    if (voice.Age >= voice.Life) break;
                    if (voice.Age < 0) { voice.Age += dt; continue; }   // scheduled note not started yet
                    float s = Render(ref voice);
                    _dry[i] += s * leftGain;
                    _dry[i + 1] += s * rightGain;
                    voice.Age += dt;
                }
            }
            int w = 0;
            for (int v = 0; v < _voiceCount; v++)
                if (_voices[v].Age < _voices[v].Life) _voices[w++] = _voices[v];
            _voiceCount = w;
        }
        for (int i = 0; i < count; i += 2)
        {
            float l = _dry[i], r = _dry[i + 1];
            _reverb.Process(l, r, out float wl, out float wr);
            // soft clip the bus so stacked explosions stay round instead of cracking
            buffer[offset + i] += SoftClip((l + wl * 0.14f) * volume);
            buffer[offset + i + 1] += SoftClip((r + wr * 0.14f) * volume);
        }
    }

    private static float SoftClip(float x) => 0.94f * MathF.Tanh(x / 0.94f);

    // Match the previous set's measured RMS rather than making a redesign louder by default.
    private static float LevelOf(SfxType type) => type switch
    {
        SfxType.Shot => 0.95f, SfxType.Hit => 0.82f, SfxType.Kill => 1.1f,
        SfxType.Explosion => 1.1f, SfxType.Dash => 0.95f, SfxType.Pickup => 0.78f,
        SfxType.LaserWarn => 0.86f, SfxType.LaserFire => 0.85f, SfxType.Burst => 1.3f,
        SfxType.Hurt => 0.87f, SfxType.Boss => 0.7f, SfxType.Menu => 0.6f,
        SfxType.Unlock => 0.63f, SfxType.Wave => 1.1f, _ => 1f,
    };

    private static double LifeOf(SfxType type) => type switch
    {
        SfxType.Shot => 0.12,
        SfxType.DrumKick => 0.25,
        SfxType.DrumSnare => 0.18,
        SfxType.DrumHat => 0.07,
        SfxType.BassShot => 0.42,
        SfxType.LeadShot => 0.15,
        SfxType.Hit => 0.12,
        SfxType.Kill => 0.42,
        SfxType.Explosion => 1.4,
        SfxType.Dash => 0.32,
        SfxType.Graze => 0.14,
        SfxType.Pickup => 0.55,
        SfxType.LaserWarn => 0.6,
        SfxType.LaserFire => 0.6,
        SfxType.Burst => 1.6,
        SfxType.Hurt => 0.5,
        SfxType.Boss => 1.8,
        SfxType.Menu => 0.06,
        SfxType.Unlock => 1.0,
        SfxType.Wave => 0.7,
        _ => 0.2,
    };

    // ---------------------------------------------------------------- building blocks

    private static float Noise(ref Voice v)
    {
        uint h = v.Seed += 0x9E3779B9u;
        h ^= h >> 16; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
        return (h & 0xFFFFFF) / 8388607.5f - 1f;
    }

    private static float Osc(ref double phase, float freq)
    {
        phase += freq / SampleRate;
        if (phase > 1.0) phase -= Math.Floor(phase);
        return MathF.Sin((float)(phase * Math.Tau));
    }

    private static float Saw(ref double phase, float freq)
    {
        float step = Math.Clamp(freq / SampleRate, 0.00001f, 0.45f);
        phase += step;
        phase -= Math.Floor(phase);
        float t = (float)phase;
        float y = t * 2f - 1f;
        if (t < step) { float x = t / step; y -= x + x - x * x - 1f; }
        else if (t > 1f - step) { float x = (t - 1f) / step; y -= x * x + x + x + 1f; }
        return y;
    }

    // Phase modulation in radians rather than frequency modulation with a moving DC offset.
    private static float Fm(ref Voice v, float hz, float ratio, float index)
    {
        float mod = Osc(ref v.Phase2, hz * ratio);
        v.Phase1 += hz / SampleRate;
        v.Phase1 -= Math.Floor(v.Phase1);
        return MathF.Sin((float)(v.Phase1 * Math.Tau) + mod * index);
    }

    private static float Fold(float x) => MathF.Sin(x * 2.4f);

    private static float LowPass(ref float state, float x, float cutoff)
    {
        float a = 1f - MathF.Exp(-M.Tau * cutoff / SampleRate);
        state += (x - state) * a;
        return state;
    }

    private static float HighPass(ref float lp, float x, float cutoff) => x - LowPass(ref lp, x, cutoff);

    /// <summary>Chamberlin state-variable band-pass.</summary>
    private static float BandPass(ref Voice v, float x, float freq, float q)
    {
        float f = 2f * MathF.Sin(MathF.PI * MathF.Min(freq, SampleRate * 0.2f) / SampleRate);
        float high = x - v.BpLow - v.Bp / q;
        v.Bp += f * high;
        v.BpLow += f * v.Bp;
        return v.Bp;
    }

    private static float Env(float t, float attack, float decay) => t < attack ? t / attack : MathF.Exp(-(t - attack) * decay);

    // ---------------------------------------------------------------- voices

    private static float Render(ref Voice v)
    {
        float t = (float)v.Age;
        float u = (float)(v.Age / v.Life);
        float p = v.Pitch;
        float f = v.Freq;
        // All voices fade to zero before expiry, including warnings and sample-held glitches.
        float end = Math.Clamp((float)(v.Life - v.Age) * 250f, 0f, 1f);
        float dry = RenderVoice(ref v, t, u, p, f);
        if (v.Sample is { } sample)
        {
            // The recorded fal layer leads; the procedural voice stays underneath for its transient punch.
            double pos = v.Age * SampleRate * v.PlayRate;
            int frame = (int)pos;
            dry *= 0.3f;
            if (frame + 1 < sample.Length)
            {
                float frac = (float)(pos - frame);
                dry += (sample[frame] + (sample[frame + 1] - sample[frame]) * frac) * 1.1f;
            }
        }
        return dry * end;
    }

    private static float RenderVoice(ref Voice v, float t, float u, float p, float f)
    {
        switch (v.Type)
        {
            case SfxType.Shot:
            {
                // Tight electro stab: a tuned wavefolder body and a dry, granular spark.
                float body = Fm(ref v, f * 2f, 2f, 3.6f * MathF.Exp(-t * 55f));
                float snap = HighPass(ref v.Hp, Noise(ref v), 4200f) * MathF.Exp(-t * 210f);
                return (Fold(body * (0.9f + MathF.Exp(-t * 40f))) * Env(t, 0.001f, 45f) * 0.48f + snap * 0.32f);
            }
            case SfxType.DrumKick:
            {
                float body = Osc(ref v.Phase1, (f * 0.5f + 185f * MathF.Exp(-t * 65f)) * p);
                float click = HighPass(ref v.Hp, Noise(ref v), 2800f) * MathF.Exp(-t * 180f);
                return MathF.Tanh(body * 1.8f) * Env(t, 0.001f, 20f) * 0.65f + click * 0.28f;
            }
            case SfxType.DrumSnare:
            {
                float shell = Osc(ref v.Phase1, 185f * p) + Osc(ref v.Phase3, 320f * p) * 0.4f;
                float wire = HighPass(ref v.Hp, Noise(ref v), 1400f);
                return shell * Env(t, 0.001f, 48f) * 0.36f + wire * Env(t, 0.002f, 27f) * 0.52f;
            }
            case SfxType.DrumHat:
            {
                float metal = Osc(ref v.Phase1, 6173f * p) * Osc(ref v.Phase2, 8351f * p);
                float air = HighPass(ref v.Hp, Noise(ref v), 7500f);
                return (metal * 0.32f + air * 0.45f) * Env(t, 0.0007f, 75f);
            }
            case SfxType.BassShot:
            {
                // Reese growl with a moving vowel over a clean tuned sub.
                float saw = Saw(ref v.Phase1, f * 0.5f * 0.994f) + Saw(ref v.Phase2, f * 0.5f * 1.006f);
                float growl = BandPass(ref v, Fold(saw), 260f + 1700f * MathF.Exp(-t * 12f), 2.3f);
                float sub = Osc(ref v.Phase3, f * 0.5f);
                return (MathF.Tanh(growl * 2f) * 0.42f + sub * 0.38f) * Env(t, 0.003f, 9f);
            }
            case SfxType.LeadShot:
            {
                // Smooth resonant ribbon, separate from the classic weapon's hard transient.
                float ribbon = Saw(ref v.Phase1, f * 4f) + Saw(ref v.Phase2, f * 4.012f);
                float tone = LowPass(ref v.Lp, ribbon, 1700f + 2300f * MathF.Exp(-t * 18f));
                return tone * Env(t, 0.008f, 18f) * 0.32f;
            }
            case SfxType.Hit:
            {
                // Short ceramic knock with a sand-like impact, rather than a repeating beep.
                float knock = Fm(ref v, f * 3f, 2.76f * p, 2.8f * MathF.Exp(-t * 85f));
                float sand = HighPass(ref v.Hp, Noise(ref v), 2400f) * MathF.Exp(-t * 135f);
                return knock * Env(t, 0.0008f, 57f) * 0.37f + sand * 0.24f;
            }
            case SfxType.Kill:
            {
                // Shattered digital glass: pitched fragments flutter away from a hard body hit.
                float glass = Fm(ref v, f * 4f, 2.003f * p, 5f * MathF.Exp(-t * 12f));
                float gate = 0.45f + 0.55f * MathF.Pow(MathF.Cos(t * 95f), 2f);
                float debris = BandPass(ref v, Noise(ref v), 4300f * MathF.Exp(-t * 11f) + 450f, 2f);
                float body = Osc(ref v.Phase3, (85f + 150f * MathF.Exp(-t * 45f)) * p);
                return glass * gate * Env(t, 0.001f, 17f) * 0.28f + debris * Env(t, 0.001f, 15f) * 0.6f + body * MathF.Exp(-t * 28f) * 0.52f;
            }
            case SfxType.Explosion:
            {
                // A sub drop, torn-speaker growl and a scattering tail of bitcrushed debris.
                float sub = Osc(ref v.Phase1, f * 0.25f + 100f * MathF.Exp(-t * 14f));
                float tear = Saw(ref v.Phase2, f * 0.5f * p);
                float growl = BandPass(ref v, Fold(tear * 1.5f), 220f + 1100f * MathF.Exp(-t * 4f), 1.5f);
                if (--v.HoldCount <= 0) { v.HoldCount = 12 + (int)(u * 180f); v.Hold = Noise(ref v); }
                float debris = LowPass(ref v.Lp, v.Hold, 5000f * MathF.Exp(-t * 4f) + 120f);
                return (sub * 0.65f + MathF.Tanh(growl * 2f) * 0.3f) * Env(t, 0.003f, 4.5f) + debris * Env(t, 0.001f, 5f) * 0.55f;
            }
            case SfxType.Dash:
            {
                // Vacuum intake followed by a rip through the stereo room.
                float air = BandPass(ref v, Noise(ref v), 300f + 4600f * MathF.Pow(1f-u, 2f), 1.7f);
                float zip = Fm(ref v, f * (1f + 6f * MathF.Exp(-t * 22f)), 2f, 1.6f);
                return (air * 0.8f + zip * 0.22f) * Env(t, 0.003f, 13f);
            }
            case SfxType.Graze:
            {
                float shimmer = Fm(ref v, f * 8f, 1.5f, 1.8f * MathF.Exp(-t * 35f));
                return shimmer * Env(t, 0.002f, 35f) * 0.22f;
            }
            case SfxType.Pickup:
            {
                float step = t < 0.055f ? 1f : t < 0.11f ? 1.5f : 2f;
                float local = t < 0.11f ? t % 0.055f : t - 0.11f;
                float pluck = Fm(ref v, f * 4f * step, 2f, 2.8f * MathF.Exp(-local * 25f));
                return pluck * Env(local, 0.002f, 14f) * MathF.Exp(-t * 3f) * 0.36f;
            }
            case SfxType.LaserWarn:
            {
                // Accelerating gated charge, with a deliberate gap before the discharge.
                float charge = Saw(ref v.Phase1, f * (1f + 3f * u * u));
                float pulse = 0.2f + 0.8f * MathF.Pow(MathF.Max(0f, MathF.Sin(t * M.Tau * (8f + 20f*u))), 2f);
                return LowPass(ref v.Lp, charge, 600f + 6500f*u) * pulse * MathF.Sin(MathF.PI*u) * 0.48f;
            }
            case SfxType.LaserFire:
            {
                float beam = Fm(ref v, f * 2f, 3f, 7f * MathF.Exp(-t * 12f));
                float hiss = HighPass(ref v.Hp, Noise(ref v), 5300f) * MathF.Exp(-t * 20f);
                return MathF.Tanh(beam * 2f) * Env(t, 0.001f, 7f) * 0.49f + hiss * 0.28f;
            }
            case SfxType.Burst:
            {
                // Immediate drop impact, opening chord and a chopped, dissolving air tail.
                float root = Saw(ref v.Phase1, f);
                float third = Saw(ref v.Phase2, f * v.ThirdRatio);
                float fifth = Saw(ref v.Phase3, f * 1.498307f);
                float chord = LowPass(ref v.Lp, (root + third + fifth) / 3f, 300f + 4200f * MathF.Exp(-t*3f));
                float air = BandPass(ref v, Noise(ref v), 500f + 3500f*MathF.Exp(-t*2f), 1.7f);
                float chop = 0.65f + 0.35f*MathF.Cos(t * 40f);
                return chord * Env(t, 0.004f, 3.4f) * 0.95f + air * chop * Env(t, 0.002f, 3f) * 0.58f;
            }
            case SfxType.Hurt:
            {
                // Low broken-machine alarm: instantly distinct from positive feedback.
                float thud = Osc(ref v.Phase1, 47f + 130f*MathF.Exp(-t*35f));
                if (--v.HoldCount <= 0) { v.HoldCount = 80 + (int)(u * 700f); v.Hold = Noise(ref v); }
                float glitch = LowPass(ref v.Lp, v.Hold, 1500f) * (0.5f + 0.5f*MathF.Cos(t*105f));
                return MathF.Tanh(thud*3f) * Env(t, 0.002f, 8f) * 0.65f + glitch * Env(t, 0.002f, 7f) * 0.3f;
            }
            case SfxType.Boss:
            {
                // Detuned reese horn with a slow formant opening, tuned down two octaves.
                float reese = Saw(ref v.Phase1, f * 0.25f * 0.989f) + Saw(ref v.Phase2, f * 0.25f * 1.011f);
                float formant = BandPass(ref v, Fold(reese), 180f + 1400f*Env(t, 0.18f, 2f), 2f);
                float sub = Osc(ref v.Phase3, f*0.25f);
                return (MathF.Tanh(formant*2.3f)*0.5f + sub*0.38f) * Env(t, 0.012f, 1.8f);
            }
            case SfxType.Menu:
                return Fm(ref v, f*4f, 2f, 1.5f*MathF.Exp(-t*140f)) * Env(t, 0.001f, 100f)*0.24f;
            case SfxType.Unlock:
            {
                float step = t < 0.09f ? 1f : t < 0.18f ? v.ThirdRatio : t < 0.27f ? 1.498307f : 2f;
                float local = t < 0.27f ? t % 0.09f : t - 0.27f;
                float bell = Fm(ref v, f*4f*step, 2f, 2.5f*MathF.Exp(-local*12f));
                return bell * Env(local, 0.003f, 5.5f)*MathF.Exp(-t*1.5f)*0.4f;
            }
            case SfxType.Wave:
            {
                float sweep = BandPass(ref v, Noise(ref v), 500f + 4800f*u*u, 2f);
                float stab = Fm(ref v, f*2f, 1.5f, 4f*MathF.Exp(-t*15f));
                return sweep * MathF.Sin(MathF.PI*u)*0.6f + stab * Env(t, 0.002f, 8f)*0.32f;
            }
            case SfxType.Note:
            {
                // In-key rewards become a wavetable-like glass pluck instead of a plain sine bell.
                float pluck = Fm(ref v, f, 2f, (v.Timbre == 0 ? 3.2f : 1.4f)*MathF.Exp(-t*18f));
                float overtone = Osc(ref v.Phase3, f*3f)*MathF.Exp(-t*26f)*0.12f;
                return (pluck + overtone) * Env(t, 0.002f, v.Timbre == 0 ? 7f : 18f) * 0.32f;
            }
            default: return 0f;
        }
    }

    /// <summary>Small stereo Schroeder reverb: 4 combs + 2 all-passes per side.</summary>
    private sealed class Reverb
    {
        private readonly Comb[] _l = { new(1557), new(1617), new(1491), new(1422) };
        private readonly Comb[] _r = { new(1580), new(1640), new(1514), new(1445) };
        private readonly AllPass[] _al = { new(556), new(441) };
        private readonly AllPass[] _ar = { new(579), new(464) };

        public void Process(float inL, float inR, out float outL, out float outR)
        {
            float mono = (inL + inR) * 0.5f;
            float l = 0f, r = 0f;
            foreach (var c in _l) l += c.Process(mono);
            foreach (var c in _r) r += c.Process(mono);
            foreach (var a in _al) l = a.Process(l);
            foreach (var a in _ar) r = a.Process(r);
            outL = l * 0.25f;
            outR = r * 0.25f;
        }

        private sealed class Comb
        {
            private readonly float[] _buf;
            private int _i;
            private float _store;
            public Comb(int size) => _buf = new float[size];
            public float Process(float x)
            {
                float y = _buf[_i];
                _store = y * 0.75f + _store * 0.25f;     // damping
                _buf[_i] = x + _store * 0.78f;           // feedback = room size
                if (++_i >= _buf.Length) _i = 0;
                return y;
            }
        }

        private sealed class AllPass
        {
            private readonly float[] _buf;
            private int _i;
            public AllPass(int size) => _buf = new float[size];
            public float Process(float x)
            {
                float b = _buf[_i];
                _buf[_i] = x + b * 0.5f;
                if (++_i >= _buf.Length) _i = 0;
                return b - x;
            }
        }
    }
}
