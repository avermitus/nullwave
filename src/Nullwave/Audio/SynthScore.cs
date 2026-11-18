using NAudio.Wave;

namespace Nullwave.Audio;

public sealed class SynthScore : ISampleProvider
{
    private const int SampleRate = 48000;
    private readonly WaveFormat _format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);
    private double _position;
    private float _level = 0f;
    private float _targetLevel = 1f;
    private long _noiseState = 0x123456789abcdefL;

    public WaveFormat WaveFormat => _format;
    public float TargetLevel { get => _targetLevel; set => _targetLevel = Math.Clamp(value, 0f, 1f); }
    public float CurrentLevel => _level;

    public int Read(float[] buffer, int offset, int count)
    {
        float samplesPerBeat = SampleRate * 60f / SynthVisual.Tempo;
        float samplesPerStep = samplesPerBeat * 0.25f;
        for (int i = 0; i < count; i += 2)
        {
            _level += (_targetLevel - _level) * 0.00012f;
            double t = _position / SampleRate;
            long step = (long)(_position / samplesPerStep);
            double stepAge = (_position - step * samplesPerStep) / SampleRate;
            int stepInBar = (int)(step % 16);
            int bar = (int)((step / 16) % 4);

            float left = 0f;
            float right = 0f;

            float kickEnv = 0f;
            if (stepInBar % 4 == 0 && stepAge < 0.4) kickEnv = MathF.Exp((float)-stepAge * 13f);
            if (kickEnv > 0.001f)
            {
                float kt = (float)stepAge;
                float f = 52f + 95f * MathF.Exp(-kt * 34f);
                float kick = MathF.Sin(kt * f * MathF.Tau) * kickEnv * 0.20f;
                left += kick;
                right += kick;
            }

            if ((stepInBar == 4 || stepInBar == 12) && stepAge < 0.2)
            {
                float env = MathF.Exp((float)-stepAge * 26f);
                float noise = NextNoise() * env * 0.08f;
                float body = MathF.Sin((float)stepAge * 185f * MathF.Tau) * env * 0.06f;
                left += noise + body;
                right += noise * 0.9f + body;
            }

            if (stepInBar % 2 == 1 && stepAge < 0.045)
            {
                float env = MathF.Exp((float)-stepAge * 70f);
                float noise = NextNoise() * env * 0.025f;
                left += noise * 0.8f;
                right += noise;
            }

            // A minor - F - C - G, one bar each.
            int[] roots = { 55, 44, 65, 49 };
            float root = roots[bar];
            if (stepAge < 0.5f && (stepInBar == 0 || stepInBar == 6 || stepInBar == 10 || stepInBar == 14))
            {
                float env = MathF.Exp((float)-stepAge * 8f);
                float phase = (float)(_position * root / SampleRate);
                float saw = (phase - MathF.Floor(phase)) * 2f - 1f;
                float sub = MathF.Sin(phase * MathF.Tau) * 0.4f;
                float bass = (saw * 0.55f + sub) * env * 0.075f;
                left += bass;
                right += bass;
            }

            float chordRoot = root * 2f;
            float[] chord = { 1f, 1.1892f, 1.4983f };
            float pad = 0f;
            for (int c = 0; c < chord.Length; c++)
            {
                float freq = chordRoot * chord[c] * (1f + (c - 1) * 0.0015f);
                pad += MathF.Sin((float)t * freq * MathF.Tau + c * 0.8f) * 0.008f;
            }
            float padEnv = 0.35f + 0.12f * MathF.Sin((float)t * 0.15f);
            left += pad * padEnv;
            right += pad * padEnv * 0.94f;

            if (stepInBar % 2 == 0 && stepAge < 0.25)
            {
                int[] arpNotes = { 0, 7, 12, 7, 3, 10, 15, 10 };
                int note = arpNotes[(stepInBar / 2) % arpNotes.Length];
                float freq = root * 4f * MathF.Pow(2f, note / 12f);
                float env = MathF.Exp((float)-stepAge * 14f);
                float phase = (float)(_position * freq / SampleRate);
                float sq = (phase - MathF.Floor(phase)) < 0.5f ? 1f : -1f;
                float pan = 0.35f + 0.4f * MathF.Sin(stepInBar * 0.7f);
                float v = sq * env * 0.022f;
                left += v * (1f - pan);
                right += v * pan;
            }

            left *= _level;
            right *= _level;
            buffer[offset + i] = left;
            buffer[offset + i + 1] = right;
            _position += 1;
        }
        return count;
    }

    private float NextNoise()
    {
        _noiseState ^= _noiseState << 13;
        _noiseState ^= _noiseState >> 7;
        _noiseState ^= _noiseState << 17;
        return (float)((_noiseState & 0xFFFFFF) / 8388607.5 - 1.0);
    }
}
