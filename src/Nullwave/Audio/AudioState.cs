namespace Nullwave.Audio;

public sealed class AudioState
{
    public const int SpectrumBars = 48;

    public float Time;
    public float Rms;
    /// <summary>Unclamped RMS of the analysed window (linear), for dynamics in dB.</summary>
    public float Loudness;
    public float Bass;
    public float LowMid;
    public float Mid;
    public float High;
    public float Air;
    public float Energy;
    public float Flux;
    public float BeatPulse;
    public float DownPulse;
    public bool Beat;
    /// <summary>Per-instrument onsets (one frame each) and their decaying pulses.</summary>
    public bool KickHit, SnareHit, HatHit;
    public float KickPulse, SnarePulse, HatPulse;
    /// <summary>0 = tonal, 1 = noise-like (cymbals, distortion, risers).</summary>
    public float Flatness;
    public bool Downbeat;
    public float Bpm = 126f;
    public float Confidence;
    public int BeatCount;
    public int BarCount;
    public bool External;
    /// <summary>Continuous beat position (integer part = beat index) from the tracker or the score.</summary>
    public double BeatPosition;
    /// <summary>True when <see cref="BeatPosition"/> follows a confident tempo/phase lock.</summary>
    public bool BeatLocked;
    /// <summary>Which beat index modulo 4 is the downbeat.</summary>
    public int DownbeatOffset;
    /// <summary>True when capturing one application instead of the whole system mix.</summary>
    public bool AppCapture;
    public bool Quiet;
    public string SourceName = "GENERATIVE SCORE";
    public string DeviceName = "";
    public string SpotifyTrack = "";
    public SpotifyPlaybackState SpotifyStatus = SpotifyPlaybackState.NotRunning;
    public float[] Spectrum = new float[SpectrumBars];
    public float[] Bands = new float[6];

    public void ClearBeatFlags()
    {
        Beat = false;
        Downbeat = false;
        KickHit = SnareHit = HatHit = false;
    }

    public void CopyBands()
    {
        Bands[0] = Bass;
        Bands[1] = LowMid;
        Bands[2] = Mid;
        Bands[3] = High;
        Bands[4] = Air;
        Bands[5] = Rms;
    }

    public float Band(int index)
    {
        return index switch
        {
            0 => Bass,
            1 => LowMid,
            2 => Mid,
            3 => High,
            4 => Air,
            _ => Rms,
        };
    }
}
