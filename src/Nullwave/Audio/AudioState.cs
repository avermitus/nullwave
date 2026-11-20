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
    /// <summary>Active lookahead buffer depth in seconds (0 for real-time live loopback).</summary>
    public float LookaheadSeconds;
    /// <summary>0..1 rising tension ramp when a future energy/bass drop approaches speaker playback.</summary>
    public float DropAnticipation;
    public string LocalTrackName = "";
    /// <summary>True while a captured app (Spotify) is replayed 0.75 s late through the game.</summary>
    public bool AppBuffered;
    /// <summary>Why app buffering is unavailable ("" when fine).</summary>
    public string BufferNote = "";
    public bool LocalTrackActive;
    public float[] Spectrum = new float[SpectrumBars];

    // ---- musical understanding (MusicAnalyzer)
    /// <summary>Drums only (percussive part), per register, 0..1: low = kicks/toms, mid = snares, high = hats/cymbals.</summary>
    public float PercLow, PercMid, PercHigh;
    /// <summary>Melody / pads / bass notes only (harmonic part), per register, 0..1.</summary>
    public float HarmLow, HarmMid, HarmHigh;
    /// <summary>0 = all tonal, 1 = all drums.</summary>
    public float Percussive;
    /// <summary>12 pitch classes (C, C#, ... B), 0..1.</summary>
    public float[] Chroma = new float[12];
    /// <summary>Current chord root 0..11 (C = 0), or -1 before any chord is heard.</summary>
    public int ChordRoot = -1;
    public bool ChordMinor;
    public float ChordConfidence;
    /// <summary>Seconds since the chord last changed.</summary>
    public float ChordAge;
    /// <summary>Song key: 0..11 major, 12..23 minor (root = Key % 12), or -1 until known.</summary>
    public int Key = -1;
    public float KeyConfidence;
    /// <summary>Spectral brightness 0 (dark, bassy) .. 1 (bright, airy).</summary>
    public float Brightness;
    /// <summary>How different the sound's texture is from the last few seconds, 0..1.</summary>
    public float TimbreNovelty;
    /// <summary>Section index since the song started, and its label (a returning chorus reuses its earlier label).</summary>
    public int Section, SectionLabel, SectionCount;
    /// <summary>Bumped whenever the source or track changes (section labels start over).</summary>
    public int SongEpoch;
    /// <summary>One-frame events.</summary>
    public bool ChordChanged, KeyChanged, TextureChanged, SectionChanged, SectionRepeat;
    /// <summary>The section change is a correction of an earlier quick call (taken back or relabelled).</summary>
    public bool SectionCorrected;
    /// <summary>Lead melody pitch (MIDI note number, fractional) and how sure the tracker is (0..1).</summary>
    public float MelodyPitch = 60f;
    public float MelodyConfidence;
    /// <summary>One-frame: the melody moved to a new note.</summary>
    public bool MelodyNoteChanged;
    /// <summary>
    /// The current "track" for setlists and song records: the Spotify title, the local track (each
    /// loop counts as a new play), or for the built-in score and untitled system audio a 3-minute chapter.
    /// </summary>
    public string TrackTitle = "";
    /// <summary>True when <see cref="TrackTitle"/> names a real song (records are kept for it).</summary>
    public bool TrackIsSong;
    /// <summary>Seconds since the current track started.</summary>
    public float TrackTime;
    /// <summary>Counts track starts since launch.</summary>
    public int TrackIndex;
    /// <summary>One-frame: a new track started.</summary>
    public bool TrackChanged;
    public float[] Bands = new float[6];

    public void ClearBeatFlags()
    {
        Beat = false;
        Downbeat = false;
        KickHit = SnareHit = HatHit = false;
        ChordChanged = KeyChanged = TextureChanged = SectionChanged = SectionRepeat = SectionCorrected = MelodyNoteChanged = TrackChanged = false;
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
