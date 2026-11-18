namespace Nullwave.Audio;

public sealed class SynthVisual
{
    public const float Tempo = 126f;
    public float Bpm => Tempo;
    public float BeatPhase { get; private set; }
    public int BeatIndex { get; private set; }
    private float _time;

    public SynthVisual()
    {
        BeatPhase = 0.94f;
    }

    public void Reset()
    {
        BeatPhase = 0;
        BeatIndex = 0;
    }

    public void Advance(float dt, AudioState state)
    {
        _time += dt;
        float oldPhase = BeatPhase;
        BeatPhase += dt * Tempo / 60f;
        bool beat = false;
        while (BeatPhase >= 1f)
        {
            BeatPhase -= 1f;
            BeatIndex++;
            beat = true;
        }

        state.BeatPosition = BeatIndex + BeatPhase;
        state.BeatLocked = true;
        state.DownbeatOffset = 0;
        float phase = BeatPhase;
        float kick = MathF.Exp(-phase * 11f);
        float eighthPhase = (phase * 2f) % 1f;
        float hat = MathF.Exp(-eighthPhase * 18f);
        float sixteenthPhase = (phase * 4f) % 1f;
        float tick = MathF.Exp(-sixteenthPhase * 26f);
        float arp = MathF.Exp(-eighthPhase * 9f);
        float pad = 0.42f + 0.12f * MathF.Sin(_time * 0.37f);
        float bar = (BeatIndex / 4) % 4;

        state.Bass = MClamp(0.18f + kick * 0.78f + 0.10f * MathF.Sin(_time * 3.1f));
        state.LowMid = MClamp(0.14f + kick * 0.26f + arp * 0.25f);
        state.Mid = MClamp(0.10f + arp * 0.42f + 0.10f * MathF.Sin(_time * 5.3f + bar));
        state.High = MClamp(0.08f + hat * 0.42f + tick * 0.16f);
        state.Air = MClamp(0.035f + hat * 0.16f + 0.03f * MathF.Sin(_time * 8.7f));
        state.Rms = MClamp(0.10f + kick * 0.22f + hat * 0.05f);
        state.Loudness = state.Rms * 0.25f;
        state.Flux = kick * 0.55f + tick * 0.2f;
        state.Energy = MClamp(0.18f + kick * 0.62f + arp * 0.2f + hat * 0.12f);
        state.BeatPulse = MathF.Max(state.BeatPulse * 0.84f, beat ? 1f : 0f);
        state.Bpm = Tempo;
        state.Confidence = 1f;
        state.External = false;
        state.SourceName = state.Quiet ? "SILENT SIGNAL" : "GENERATIVE SCORE";

        for (int i = 0; i < AudioState.SpectrumBars; i++)
        {
            float t = i / (float)(AudioState.SpectrumBars - 1);
            float low = MathF.Exp(-t * 4.2f);
            float mid = MathF.Exp(-MathF.Abs(t - 0.42f) * 9f);
            float high = MathF.Exp(-MathF.Abs(t - 0.78f) * 11f);
            float wobble = 0.62f + 0.38f * MathF.Sin(_time * (1.7f + t * 4.1f) + i * 0.8f);
            float v = low * (0.30f + kick * 0.9f) + mid * (0.15f + arp * 0.55f) + high * (0.1f + hat * 0.5f);
            state.Spectrum[i] = MClamp(v * wobble);
        }

        if (beat)
        {
            state.Beat = true;
            state.BeatCount = BeatIndex;
            if (BeatIndex % 4 == 0)
            {
                state.Downbeat = true;
                state.BarCount++;
                state.DownPulse = 1f;
            }
        }
        else if (oldPhase > phase)
        {
            state.Beat = true;
        }

        state.DownPulse = MathF.Max(state.DownPulse * 0.9f, state.Downbeat ? 1f : 0f);
        state.CopyBands();
    }

    private static float MClamp(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
