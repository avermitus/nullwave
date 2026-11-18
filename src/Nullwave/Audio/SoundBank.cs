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
}

/// <summary>
/// Procedural sound effects. Each voice is synthesised with its own filter state (one-pole
/// low/high-pass and a state-variable band-pass), layered from physically motivated parts:
/// a pitch-dropping body for punch, filtered noise for air and debris, inharmonic partials for
/// metal, FM for bells and zaps, a sub thump for weight. Everything goes through a small
/// stereo Schroeder reverb so the effects sit in a space instead of beeping in a void.
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

    private struct Voice
    {
        public SfxType Type;
        public double Age;
        public double Life;
        public float Gain;
        public float Pan;
        public uint Seed;
        public float Pitch;     // per-trigger variation so repeats do not machine-gun
        public double Phase1, Phase2, Phase3;
        public float Lp, Lp2, Hp, HpIn, Bp, BpLow, Hold;
        public int HoldCount;
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
            _voices[_voiceCount++] = new Voice
            {
                Type = type,
                Life = LifeOf(type),
                Gain = gain,
                Pan = Math.Clamp(pan, -1f, 1f),
                Seed = seed,
                Pitch = 1f + ((seed >> 8) % 1000 / 1000f - 0.5f) * 0.12f,
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
            buffer[offset + i] += SoftClip((l + wl * 0.22f) * volume);
            buffer[offset + i + 1] += SoftClip((r + wr * 0.22f) * volume);
        }
    }

    private static float SoftClip(float x) => x / (1f + MathF.Abs(x) * 0.6f);

    private static double LifeOf(SfxType type) => type switch
    {
        SfxType.Shot => 0.09,
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
        phase += freq / SampleRate;
        if (phase > 1.0) phase -= Math.Floor(phase);
        return (float)(phase * 2.0 - 1.0);
    }

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
        switch (v.Type)
        {
            case SfxType.Shot:
            {
                // punchy blaster: fast pitch drop body + bright noise click
                float body = Osc(ref v.Phase1, (1300f * MathF.Exp(-t * 55f) + 260f) * p) * MathF.Exp(-t * 38f);
                float click = HighPass(ref v.Hp, Noise(ref v), 3500f) * MathF.Exp(-t * 160f);
                float grit = Saw(ref v.Phase2, 180f * p) * MathF.Exp(-t * 70f) * 0.15f;
                return (body * 0.5f + click * 0.45f + grit) * 0.75f;
            }
            case SfxType.Hit:
            {
                // metallic tink: inharmonic partials ring briefly over a click
                float metal = Osc(ref v.Phase1, 1830f * p) * 0.4f + Osc(ref v.Phase2, 2710f * p) * 0.3f + Osc(ref v.Phase3, 3950f * p) * 0.2f;
                float click = HighPass(ref v.Hp, Noise(ref v), 2000f);
                return (metal * MathF.Exp(-t * 34f) + click * MathF.Exp(-t * 120f) * 0.5f) * 0.45f;
            }
            case SfxType.Kill:
            {
                // crunchy shatter: body thump + debris noise whose brightness falls away
                float body = Osc(ref v.Phase1, (190f * MathF.Exp(-t * 12f) + 55f) * p) * MathF.Exp(-t * 11f);
                float debris = LowPass(ref v.Lp, Noise(ref v), 7000f * MathF.Exp(-t * 7f) + 300f) * Env(t, 0.002f, 9f);
                float glass = BandPass(ref v, Noise(ref v), 3200f * p, 6f) * MathF.Exp(-t * 14f);
                return (body * 0.55f + debris * 0.55f + glass * 0.35f) * 0.8f;
            }
            case SfxType.Explosion:
            {
                // sub thump + rumble that darkens + sparse crackle
                float sub = Osc(ref v.Phase1, (70f * MathF.Exp(-t * 4f) + 30f) * p) * Env(t, 0.004f, 3.5f);
                float rumble = LowPass(ref v.Lp2, LowPass(ref v.Lp, Noise(ref v), 2600f * MathF.Exp(-t * 3f) + 120f), 1800f * MathF.Exp(-t * 2f) + 90f) * Env(t, 0.01f, 2.6f);
                if (--v.HoldCount <= 0)
                {
                    v.HoldCount = 200 + (int)((v.Seed >> 4) % 2400);
                    v.Hold = Noise(ref v) > 0.2f ? 1f : 0f;
                }
                float crackle = v.Hold * HighPass(ref v.Hp, Noise(ref v), 1500f) * MathF.Exp(-t * 4f) * 0.4f;
                v.Hold *= 0.995f;
                return (sub * 0.75f + rumble * 1.4f + crackle) * 0.9f;
            }
            case SfxType.Dash:
            {
                // air whoosh: band-passed noise sweeping up then away
                float centre = 350f + 2600f * MathF.Sin(MathF.PI * MathF.Min(1f, u * 1.2f));
                float air = BandPass(ref v, Noise(ref v), centre * p, 2.2f);
                float env = MathF.Sin(MathF.PI * MathF.Min(1f, u)) * MathF.Exp(-u * 1.5f);
                float tone = Osc(ref v.Phase1, (140f + 260f * u) * p) * 0.15f;
                return (air * 1.1f + tone) * env * 0.8f;
            }
            case SfxType.Graze:
            {
                // bright "shing": shimmering FM partial
                float mod = Osc(ref v.Phase2, 3100f * p) * 900f * MathF.Exp(-t * 30f);
                return Osc(ref v.Phase1, 2350f * p + mod) * Env(t, 0.003f, 30f) * 0.22f;
            }
            case SfxType.Pickup:
            {
                // FM bell, two notes a fifth apart
                float i1 = 4f * MathF.Exp(-t * 6f);
                float b1 = Osc(ref v.Phase1, 880f * p + Osc(ref v.Phase2, 880f * 3.5f * p) * 880f * i1) * Env(t, 0.002f, 6f);
                float t2 = MathF.Max(0f, t - 0.06f);
                float b2 = t > 0.06f ? Osc(ref v.Phase3, 1320f * p) * Env(t2, 0.002f, 7f) : 0f;
                return (b1 * 0.25f + b2 * 0.2f);
            }
            case SfxType.LaserWarn:
            {
                // charging whine: filtered saw rising in pitch with a fast tremolo
                float f = (180f + 900f * u * u) * p;
                float saw = LowPass(ref v.Lp, Saw(ref v.Phase1, f) + Saw(ref v.Phase2, f * 1.007f), 1200f + 3000f * u);
                float trem = 0.6f + 0.4f * MathF.Sin(t * M.Tau * (12f + 30f * u));
                return saw * trem * MathF.Min(1f, u * 6f) * 0.18f;
            }
            case SfxType.LaserFire:
            {
                // zap: FM with a collapsing index + hiss + mains-like hum
                float index = 8f * MathF.Exp(-t * 10f);
                float zap = Osc(ref v.Phase1, 420f * p + Osc(ref v.Phase2, 1260f * p) * 420f * index) * Env(t, 0.002f, 5f);
                float hiss = HighPass(ref v.Hp, Noise(ref v), 4000f) * Env(t, 0.001f, 9f);
                float hum = Saw(ref v.Phase3, 55f) * Env(t, 0.02f, 4f);
                return (zap * 0.35f + hiss * 0.25f + LowPass(ref v.Lp, hum, 400f) * 0.35f) * 0.9f;
            }
            case SfxType.Burst:
            {
                // cascade: reverse-swell whoosh into a bright chord and sub drop
                float swell = BandPass(ref v, Noise(ref v), 400f + 5000f * MathF.Exp(-t * 2f), 1.5f) * Env(t, 0.05f, 2.2f);
                float chord = (Osc(ref v.Phase1, 220f * p) + Osc(ref v.Phase2, 330f * p) + Osc(ref v.Phase3, 440f * p)) * Env(t, 0.02f, 2.5f) * 0.22f;
                float sub = MathF.Sin(t * M.Tau * (80f * MathF.Exp(-t * 2f) + 30f)) * Env(t, 0.005f, 3f);
                return (swell * 0.7f + chord + sub * 0.6f) * 0.8f;
            }
            case SfxType.Hurt:
            {
                // distorted thud with a sample-and-hold glitch tail
                float thud = MathF.Tanh(Osc(ref v.Phase1, (95f * MathF.Exp(-t * 6f) + 40f) * p) * 3f) * Env(t, 0.003f, 6f);
                if (--v.HoldCount <= 0) { v.HoldCount = 600; v.Hold = Noise(ref v); }
                float glitch = v.Hold * Saw(ref v.Phase2, 330f) * Env(t, 0.02f, 9f);
                return (thud * 0.6f + glitch * 0.25f) * 0.9f;
            }
            case SfxType.Boss:
            {
                // brass-like horn: detuned saws through a slow-opening filter
                float f = 55f * p;
                float horn = Saw(ref v.Phase1, f) + Saw(ref v.Phase2, f * 1.01f) + Saw(ref v.Phase3, f * 1.5f) * 0.6f;
                float filt = LowPass(ref v.Lp, horn, 150f + 1400f * Env(t, 0.25f, 1.5f));
                return LowPass(ref v.Lp2, filt, 2000f) * Env(t, 0.08f, 1.4f) * 0.35f;
            }
            case SfxType.Menu:
                return (Osc(ref v.Phase1, 1800f) * MathF.Exp(-t * 90f) * 0.4f + HighPass(ref v.Hp, Noise(ref v), 5000f) * MathF.Exp(-t * 400f) * 0.3f) * 0.4f;
            case SfxType.Unlock:
            {
                // rising bell arpeggio
                float a = Osc(ref v.Phase1, 523f) * Env(t, 0.002f, 4f);
                float b = t > 0.08f ? Osc(ref v.Phase2, 784f) * Env(t - 0.08f, 0.002f, 4f) : 0f;
                float c = t > 0.16f ? Osc(ref v.Phase3, 1046f) * Env(t - 0.16f, 0.002f, 3.5f) : 0f;
                return (a + b + c) * 0.18f;
            }
            case SfxType.Wave:
            {
                float sweep = BandPass(ref v, Noise(ref v), 300f + 4000f * u, 4f) * MathF.Sin(MathF.PI * u);
                float chime = Osc(ref v.Phase1, 660f) * Env(t, 0.005f, 5f) * 0.18f;
                return sweep * 0.5f + chime;
            }
            default:
                return 0f;
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
