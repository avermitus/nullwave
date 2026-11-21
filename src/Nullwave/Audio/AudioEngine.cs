using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Nullwave.Core;

namespace Nullwave.Audio;

public sealed class AudioEngine : IDisposable
{
    private const int CaptureRate = 48000;
    private const int FftSize = SpectrumAnalyzer.FftSize;
    private const int RingSize = CaptureRate * 6;

    public AudioState State { get; } = new();
    public SoundBank Sfx { get; } = new();
    public SynthScore Synth { get; }
    public LookaheadAudioBuffer Lookahead => _lookahead;
    public bool LocalTrackActive => _localTrack != null;
    public SpotifyBridge Spotify { get; } = new();
    public bool CaptureAvailable => _capture != null;
    public bool OutputAvailable => _output != null;
    public float OtherAppPeak => _otherPeak;
    public string LastError { get; private set; } = "";
    /// <summary>The app being captured, or null for whole-system loopback.</summary>
    public AudioApp? CapturedApp => _app;
    /// <summary>App the player chose; kept even while that app is closed so capture re-attaches.</summary>
    public string PreferredApp => _preferredApp;

    private readonly bool _enableOutput;
    private readonly SynthVisual _synthVisual = new();
    private readonly SpectrumAnalyzer _analyzer = new();
    private readonly MusicAnalyzer _music = new();
    // song structure is read from the buffered audio 0.75 s before it plays, so a new section's
    // world can land on the section's first beat
    private readonly MusicAnalyzer _structure = new();
    private readonly AudioState _structState = new();
    private readonly float[] _aheadWindow = new float[MusicAnalyzer.WindowSize];
    private bool _sectionPending;
    private long _sectionDueFrame;
    private const float OutputLatencySeconds = 0.11f;   // WaveOutEvent DesiredLatency
    private double _sectionDueBeat;
    private int _sectionDueLabel;
    private bool _sectionDueRepeat;
    private readonly float[] _musicWindow = new float[MusicAnalyzer.WindowSize];
    private readonly float[] _scoreMags = new float[FftSize / 2];
    private readonly Fft _scoreFft = new(FftSize);
    private string _lastTrack = "";
    private string _trackKey = "";
    private float _chapterClock;
    /// <summary>Length of a "chapter" when the source has no track titles (built-in score, system audio).</summary>
    public const float ChapterSeconds = 180f;
    private BeatTracker _tracker = new(CaptureRate);
    private int _trackerRead = -1;
    private readonly float[] _trackerChunk = new float[4096];
    private int _trackerCrossed;
    private float _pulse;
    private readonly AudioProvider _provider;
    private readonly LookaheadAudioBuffer _lookahead;
    private LocalTrackSource? _localTrack;
    private long _lookaheadTrackerFrame = -1;
    // Buffered app mode: the app is routed to a silent output, captured there and replayed
    // through the lookahead buffer 0.75 s late, so the game sees the music before you hear it.
    private bool _bufferWanted = true;
    private LiveStreamSource? _live;
    private bool _liveFeeding;
    private float _liveStale;
    private readonly List<int> _routedPids = new();
    private string _routedApp = "";
    private float _bufferRetry;
    private float _bufferCheck;
    private bool _exitHooked;
    private readonly float[] _stereoScratch = new float[16384];
    private WaveOutEvent? _output;
    private IWaveIn? _capture;
    private AudioApp? _app;
    private string _preferredApp = "";
    private float _appPoll;
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _captureDevice;
    private WaveFormat? _captureFormat;
    private string _captureName = "";
    private readonly float[] _ring = new float[RingSize];
    private readonly float[] _analysis = new float[FftSize];
    private readonly float[] _capturePeakBuffer = new float[4096];
    private readonly ConcurrentQueue<byte[]> _recordQueue = new();
    private WaveFileWriter? _recordWriter;
    private int _writeIndex;
    private int _samplesAvailable;
    private long _lastDataTick;
    private volatile float _capturePeak;
    private float _otherPeak;
    private float _sessionPoll;
    private float _externalOnTimer;
    private float _externalOffTimer;
    private float _captureStaleTimer;
    private bool _external;
    private float _bpm = 126f;
    private readonly float[] _beatIntervals = new float[16];
    private int _beatIntervalIndex;
    private int _beatIntervalCount;
    private float _lastBeatTime = -10;
    private bool _quiet;
    private double _time;
    private bool _disposed;

    public AudioEngine(bool enableOutput = true, bool enableCapture = true)
    {
        _enableOutput = enableOutput;
        Synth = new SynthScore();
        _lookahead = new LookaheadAudioBuffer(Synth);
        _provider = new AudioProvider(_lookahead, Sfx);
        State.LookaheadSeconds = _lookahead.LookaheadSeconds;
        if (enableOutput && enableCapture)
        {
            try
            {
                _enumerator ??= new MMDeviceEnumerator();
                AppAudioRouter.RecoverFromCrash(_enumerator);
            }
            catch { }
        }
        if (enableOutput) StartOutput();
        if (enableCapture) StartCaptureDefault();
    }

    /// <summary>Whether a captured music app (Spotify) should be delayed 0.75 s through the game.</summary>
    public void SetAppBuffering(bool on)
    {
        _bufferWanted = on;
        if (!on) DisengageBuffer();
        _bufferRetry = 0f;
    }

    public bool AppBuffered => _live != null;

    private bool BufferCandidate =>
        _bufferWanted && _enableOutput && _output != null && _localTrack == null && _app != null &&
        _app.Name.Contains(BufferAppName, StringComparison.OrdinalIgnoreCase) && AudioApps.IsAlive(_app.ProcessId);

    /// <summary>Which captured app gets buffered (Spotify; tests use a stand-in player).</summary>
    public string BufferAppName { get; set; } = "spotify";
    public int LiveUnderruns => _live?.Underruns ?? 0;
    public long LiveFramesWritten => _live?.FramesWritten ?? 0;
    public IReadOnlyList<int> RoutedPids => _routedPids;

    private void EngageBuffer()
    {
        if (_live != null || _app == null) return;
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            if (!AppAudioRouter.IsSupported) { State.BufferNote = "SPOTIFY BUFFER NEEDS WINDOWS 11"; return; }
            var silent = AppAudioRouter.FindSilentEndpoint(_enumerator);
            if (silent == null) { State.BufferNote = "SPOTIFY BUFFER: NO SILENT OUTPUT (INSTALL STEAM OR VB-CABLE)"; return; }
            AppAudioRouter.WriteMarker(_app.Name);
            _routedPids.Clear();
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(_app.Name))
            {
                if (AppAudioRouter.SetAppEndpoint(p.Id, silent.ID)) _routedPids.Add(p.Id);
                p.Dispose();
            }
            if (_routedPids.Count == 0)
            {
                AppAudioRouter.ClearMarker();
                State.BufferNote = "SPOTIFY BUFFER: ROUTING FAILED";
                return;
            }
            _routedApp = _app.Name;
            if (!_exitHooked)
            {
                _exitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => DisengageBuffer();
            }
            _live = new LiveStreamSource();
            _liveFeeding = false;
            _liveStale = 0f;
            _provider.LiveMode = true;
            State.BufferNote = "";
            ResetForNewSource();
        }
        catch (Exception ex)
        {
            LastError = "Buffer: " + ex.Message;
            DisengageBuffer();
        }
    }

    /// <summary>Sends the app back to the normal output and returns to the score / local track.</summary>
    private void DisengageBuffer()
    {
        if (_live == null && _routedPids.Count == 0) return;
        foreach (int pid in _routedPids) AppAudioRouter.SetAppEndpoint(pid, null);
        // anything spawned since (Spotify restarts its audio process) is cleared by name
        if (_routedApp.Length > 0)
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(_routedApp))
            {
                if (!_routedPids.Contains(p.Id)) AppAudioRouter.SetAppEndpoint(p.Id, null);
                p.Dispose();
            }
        _routedPids.Clear();
        _routedApp = "";
        AppAudioRouter.ClearMarker();
        _live = null;
        _liveFeeding = false;
        _provider.LiveMode = false;
        _lookahead.SetSource(_localTrack != null ? _localTrack : Synth);
        _lookaheadTrackerFrame = -1;
    }

    /// <summary>Loads a local audio file (.wav/.mp3/.m4a/.aiff/etc.) into the lookahead player.</summary>
    public bool LoadLocalTrack(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            LastError = "Local track: path is empty";
            return false;
        }
        try
        {
            var next = new LocalTrackSource(path);
            DisengageBuffer();
            var prev = _localTrack;
            _localTrack = next;
            _lookahead.SetSource(next);
            prev?.Dispose();
            ResetMusic();
            _external = false;
            _externalOnTimer = 0f;
            _externalOffTimer = 0f;
            _analyzer.Reset();
            ResetBeatTracking();
            _lookaheadTrackerFrame = -1;
            Synth.TargetLevel = 0f;
            State.External = false;
            State.LocalTrackActive = true;
            State.LocalTrackName = next.TrackName;
            State.SourceName = "TRACK // " + next.TrackName;
            State.LookaheadSeconds = _lookahead.LookaheadSeconds;
            State.DropAnticipation = 0f;
            LastError = "";
            return true;
        }
        catch (Exception ex)
        {
            LastError = "Local track: " + ex.Message;
            return false;
        }
    }

    /// <summary>Stops local track playback and seamlessly restores the built-in SynthScore.</summary>
    public void UnloadLocalTrack()
    {
        var prev = _localTrack;
        if (prev == null) return;
        _localTrack = null;
        _lookahead.SetSource(Synth);
        prev.Dispose();
        _analyzer.Reset();
        ResetMusic();
        ResetBeatTracking();
        _lookaheadTrackerFrame = -1;
        State.LocalTrackActive = false;
        State.LocalTrackName = "";
        State.SourceName = "GENERATIVE SCORE";
        State.LookaheadSeconds = _lookahead.LookaheadSeconds;
        State.DropAnticipation = 0f;
    }

    public void Update(float dt)
    {
        if (_disposed) return;
        UpdateCore(dt);
        UpdateTrack(Math.Clamp(dt, 0.0005f, 0.1f));
    }

    /// <summary>Works out which track is playing and flags a new one (song records, run recap).</summary>
    private void UpdateTrack(float dt)
    {
        string title, key;
        bool song;
        if (_localTrack != null)
        {
            title = _localTrack.TrackName;
            song = true;
            key = "local:" + title + "#" + _localTrack.LoopCount;
        }
        else if (State.External && !string.IsNullOrEmpty(State.SpotifyTrack))
        {
            title = State.SpotifyTrack;
            song = true;
            key = "spotify:" + title;
        }
        else
        {
            // no titles to go by: chapters of a few minutes
            title = State.External ? (string.IsNullOrEmpty(State.DeviceName) ? "SYSTEM AUDIO" : State.DeviceName.ToUpperInvariant()) : "GENERATIVE SCORE";
            song = false;
            key = "chapter:" + (State.External ? "ext" : "score") + "#" + (int)(_chapterClock / ChapterSeconds);
        }
        _chapterClock += dt;
        if (key != _trackKey)
        {
            bool first = _trackKey.Length == 0;
            _trackKey = key;
            State.TrackTime = 0f;
            State.TrackIndex++;
            if (!first) State.TrackChanged = true;
        }
        else State.TrackTime += dt;
        State.TrackTitle = title;
        State.TrackIsSong = song;
    }

    private void UpdateCore(float dt)
    {
        DrainRecording();
        dt = Math.Clamp(dt, 0.0005f, 0.1f);
        _time += dt;
        State.Time = (float)_time;
        State.ClearBeatFlags();
        State.AppBuffered = false;
        Spotify.Update(dt);
        State.SpotifyStatus = Spotify.Current.State;
        State.SpotifyTrack = Spotify.Current.DisplayTrack;
        if (State.SpotifyTrack != _lastTrack)
        {
            if (_lastTrack.Length > 0 && State.SpotifyTrack.Length > 0) ResetMusic();
            _lastTrack = State.SpotifyTrack;
        }

        // Case 1: Local track loaded into the sample-accurate 0.75 s lookahead player.
        if (_localTrack != null)
        {
            _external = false;
            _externalOnTimer = 0f;
            Synth.TargetLevel = 0f;
            State.External = false;
            State.LocalTrackActive = true;
            State.LocalTrackName = _localTrack.TrackName;
            State.SourceName = "TRACK // " + _localTrack.TrackName;
            State.LookaheadSeconds = _lookahead.LookaheadSeconds;
            State.Quiet = _quiet;
            if (_lookahead.CopyPlaybackWindow(_analysis))
            {
                _analyzer.Analyze(_analysis, FftSize, CaptureRate, dt, State);
                FeedLookaheadTracker();
                ApplyTracker(dt);
                float targetAnt = _lookahead.ComputeDropAnticipation(out _, out _);
                State.DropAnticipation = M.ExpSmooth(State.DropAnticipation, targetAnt, 14f, dt);
                if (_lookahead.CopyPlaybackWindow(_musicWindow))
                    _music.Analyze(_musicWindow, _analyzer.Magnitudes, CaptureRate, dt, State);
                AnalyzeStructureAhead(dt);
            }
            else
            {
                State.Bpm = _bpm;
                State.DropAnticipation = M.ExpSmooth(State.DropAnticipation, 0f, 10f, dt);
                DecayPulses(dt);
            }
            return;
        }

        if (_preferredApp.Length > 0 && _live != null)
        {
            _appPoll -= dt;
            if (_appPoll <= 0f) { _appPoll = 2f; MaintainAppCapture(); }
        }
        _bufferRetry -= dt;
        _bufferCheck -= dt;
        if (_bufferCheck <= 0f)
        {
            _bufferCheck = 0.5f;
            bool candidate = BufferCandidate;
            if (_live == null && candidate && _bufferRetry <= 0f) { _bufferRetry = 5f; EngageBuffer(); }
            else if (_live != null && !candidate) DisengageBuffer();
        }
        if (_live != null)
        {
            UpdateBuffered(dt);
            return;
        }

        if (_enableOutput)
        {
            _sessionPoll -= dt;
            if (_sessionPoll <= 0)
            {
                _sessionPoll = 0.35f;
                _otherPeak = PollOtherApps();
            }
        }
        else
        {
            _otherPeak = 0;
        }

        if (_preferredApp.Length > 0)
        {
            _appPoll -= dt;
            if (_appPoll <= 0f)
            {
                _appPoll = 2f;
                MaintainAppCapture();
            }
        }

        bool recentData = RecentCaptureData();
        bool desiredExternal;
        if (_app != null)
            // process loopback only hears the target app, so its own level is the signal
            desiredExternal = recentData && _capturePeak > 0.0012f;
        else if (_enableOutput)
            desiredExternal = _otherPeak > 0.00025f && recentData;
        else
            desiredExternal = recentData && _capturePeak > 0.0012f;

        if (desiredExternal) _externalOnTimer += dt; else _externalOnTimer = 0;
        if (!desiredExternal) _externalOffTimer += dt; else _externalOffTimer = 0;

        if (!_external && _externalOnTimer > 0.55f && recentData)
        {
            _external = true;
            _externalOnTimer = 0;
            _analyzer.Reset();
            ResetBeatTracking();
        }
        else if (_external && _externalOffTimer > 2.2f)
        {
            _external = false;
            _externalOffTimer = 0;
            _synthVisual.Reset();
            ResetBeatTracking();
        }

        if (_external)
        {
            _captureStaleTimer = recentData ? 0 : _captureStaleTimer + dt;
            bool analyzed = false;
            if (_captureStaleTimer < 1.2f)
                analyzed = AnalyzeCapture(dt);
            if (analyzed)
            {
                State.External = true;
                State.LocalTrackActive = false;
                State.LocalTrackName = "";
                State.LookaheadSeconds = 0f;
                State.DropAnticipation = 0f;
                bool spotifyActive = (_app != null && _app.Name.Contains("spotify", StringComparison.OrdinalIgnoreCase)) ||
                                     (_app == null && Spotify.Current.IsPlaying);
                State.SourceName = spotifyActive && !string.IsNullOrEmpty(State.SpotifyTrack)
                    ? "SPOTIFY // " + State.SpotifyTrack
                    : (string.IsNullOrWhiteSpace(State.DeviceName) ? "SYSTEM LOOPBACK" : State.DeviceName);
                State.Quiet = _quiet;
                ApplyTracker(dt);
                AnalyzeMusicFromCapture(dt);
                return;
            }
            _external = false;
            _synthVisual.Reset();
        }

        _synthVisual.Advance(dt, State);
        if (_enableOutput)
        {
            // the stand-in analysis only fakes chords/melody/drums for captures and tests; in the
            // real game these come from the music actually playing (filled in just below), and
            // silence means no wells, rails or chord colours
            State.MelodyConfidence = 0f;
            State.HarmLow = State.HarmMid = State.HarmHigh = 0f;
            State.PercLow = State.PercMid = State.PercHigh = 0f;
            State.ChordChanged = false;
            State.ChordRoot = -1;
        }
        State.External = false;
        State.LocalTrackActive = false;
        State.LocalTrackName = "";
        State.SourceName = "GENERATIVE SCORE";
        State.LookaheadSeconds = _lookahead.LookaheadSeconds;
        if (_output != null && _lookahead.PlayedFrames > 0 && !_quiet)
        {
            float targetAnt = _lookahead.ComputeDropAnticipation(out _, out _);
            State.DropAnticipation = M.ExpSmooth(State.DropAnticipation, targetAnt, 14f, dt);
        }
        else
        {
            State.DropAnticipation = 0f;
        }
        State.Quiet = _quiet;
        State.Bpm = _bpm;
        State.DeviceName = _captureName;
        State.BarCount = State.BeatCount / 4;
        DecayPulses(dt);
        if (_output != null && !_quiet && _lookahead.PlayedFrames > MusicAnalyzer.WindowSize && _lookahead.CopyPlaybackWindow(_musicWindow))
        {
            _scoreFft.Magnitudes(_musicWindow.AsSpan(_musicWindow.Length - FftSize, FftSize), _scoreMags);
            _music.Analyze(_musicWindow, _scoreMags, CaptureRate, dt, State);
            AnalyzeStructureAhead(dt);
        }
        if (!_enableOutput || _quiet || _external)
            Synth.TargetLevel = 0;
        else
            Synth.TargetLevel = 1;
    }

    /// <summary>Buffered app: analyse the delayed playback window exactly like a local track.</summary>
    private void UpdateBuffered(float dt)
    {
        var live = _live!;
        bool recent = RecentCaptureData() && _capturePeak > 0.0008f;
        _liveStale = recent ? 0f : _liveStale + dt;
        // the app paused: let the built-in score fill the gap; switch back the moment music returns
        if (_liveFeeding && _liveStale > 2.2f)
        {
            _liveFeeding = false;
            _lookahead.SetSource(Synth);
            ResetBeatTracking();
            _analyzer.Reset();
            _lookaheadTrackerFrame = -1;
        }
        else if (!_liveFeeding && recent)
        {
            _liveFeeding = true;
            live.Clear();
            _lookahead.SetSource(live);
            ResetBeatTracking();
            _analyzer.Reset();
            _lookaheadTrackerFrame = -1;
        }
        _provider.LiveMode = _liveFeeding;
        Synth.TargetLevel = _liveFeeding || _quiet ? 0f : 1f;
        State.External = _liveFeeding;
        State.LocalTrackActive = false;
        State.LocalTrackName = "";
        State.AppBuffered = true;
        State.LookaheadSeconds = _lookahead.LookaheadSeconds;
        State.Quiet = _quiet;
        State.DeviceName = _captureName;
        string name = !string.IsNullOrEmpty(State.SpotifyTrack) ? State.SpotifyTrack : _captureName.ToUpperInvariant();
        State.SourceName = _liveFeeding ? "SPOTIFY // " + name + " (+0.75S)" : "SPOTIFY BUFFERED // PAUSED";
        LastWindowPeak = 0f;
        if (_lookahead.CopyPlaybackWindow(_analysis))
        {
            float pk = 0f;
            for (int i = 0; i < _analysis.Length; i += 4) pk = MathF.Max(pk, MathF.Abs(_analysis[i]));
            LastWindowPeak = pk;
            _analyzer.Analyze(_analysis, FftSize, CaptureRate, dt, State);
            FeedLookaheadTracker();
            ApplyTracker(dt);
            float targetAnt = _lookahead.ComputeDropAnticipation(out _, out _);
            State.DropAnticipation = M.ExpSmooth(State.DropAnticipation, targetAnt, 14f, dt);
            if (_liveFeeding && _lookahead.CopyPlaybackWindow(_musicWindow))
            {
                _music.Analyze(_musicWindow, _analyzer.Magnitudes, CaptureRate, dt, State);
                AnalyzeStructureAhead(dt);
            }
        }
        else
        {
            State.Bpm = _bpm;
            State.DropAnticipation = M.ExpSmooth(State.DropAnticipation, 0f, 10f, dt);
            DecayPulses(dt);
        }
    }

    /// <summary>Peak of the playback window the game analysed this frame (diagnostics).</summary>
    public float LastWindowPeak { get; private set; }

    private void DecayPulses(float dt)
    {
        if (!State.Beat)
            State.BeatPulse *= MathF.Exp(-dt * 4.2f);
        if (!State.Downbeat)
            State.DownPulse *= MathF.Exp(-dt * 3.4f);
    }

    private void UpdateBpm(float dt)
    {
        if (!State.Beat) return;
        if (_lastBeatTime > 0)
        {
            float interval = State.Time - _lastBeatTime;
            if (interval > 0.28f && interval < 1.35f)
            {
                _beatIntervals[_beatIntervalIndex] = interval;
                _beatIntervalIndex = (_beatIntervalIndex + 1) % _beatIntervals.Length;
                _beatIntervalCount = Math.Min(_beatIntervalCount + 1, _beatIntervals.Length);
            }
        }
        _lastBeatTime = State.Time;
        if (_beatIntervalCount < 3) return;
        Span<float> sorted = stackalloc float[_beatIntervalCount];
        _beatIntervals.AsSpan(0, _beatIntervalCount).CopyTo(sorted);
        sorted.Sort();
        float median = sorted[sorted.Length / 2];
        float bpm = Math.Clamp(60f / MathF.Max(0.2f, median), 55f, 190f);
        _bpm = M.ExpSmooth(_bpm, bpm, 2.5f, dt);
    }

    /// <summary>
    /// Replaces the onset-as-beat flags from the spectrum analyser with the tempo/phase tracker:
    /// beats are predicted on a locked grid, so hats and vocals no longer count as beats.
    /// </summary>
    private void ApplyTracker(float dt)
    {
        _pulse *= MathF.Exp(-dt * 4.5f);
        State.Beat = false;
        State.Downbeat = false;
        State.BeatLocked = _tracker.Locked;
        if (_tracker.Locked)
        {
            _bpm = _tracker.Bpm;
            State.BeatPosition = _tracker.Position;
            State.DownbeatOffset = _tracker.DownbeatOffset;
            long index = (long)Math.Floor(_tracker.Position);
            State.BeatCount = (int)index;
            State.BarCount = (int)(index / 4);
            if (_trackerCrossed > 0)
            {
                State.Beat = true;
                _pulse = 1f;
                if (((index - State.DownbeatOffset) % 4 + 4) % 4 == 0)
                {
                    State.Downbeat = true;
                    State.DownPulse = 1f;
                }
            }
        }
        _trackerCrossed = 0;
        State.BeatPulse = _pulse;
        State.Bpm = _bpm;
        State.Confidence = _tracker.Confidence;
        if (!State.Downbeat) State.DownPulse *= MathF.Exp(-dt * 3.4f);
    }

    /// <summary>Feeds every new captured sample to the beat tracker, in order.</summary>
    private void FeedTracker(int write)
    {
        int rate = _captureFormat?.SampleRate ?? CaptureRate;
        if (_trackerRate != rate)
        {
            _trackerRate = rate;
            _tracker = new BeatTracker(rate);
            _trackerRead = -1;
        }
        if (_trackerRead < 0) _trackerRead = write;
        int count = (write - _trackerRead + RingSize) % RingSize;
        if (count > rate)
        {
            // fell behind (hitch or source switch): skip ahead rather than replay stale audio
            _trackerRead = (write - 2048 + RingSize) % RingSize;
            count = 2048;
        }
        while (count > 0)
        {
            int n = Math.Min(count, _trackerChunk.Length);
            for (int i = 0; i < n; i++)
                _trackerChunk[i] = Volatile.Read(ref _ring[(_trackerRead + i) % RingSize]);
            _trackerCrossed += _tracker.Push(_trackerChunk.AsSpan(0, n));
            _trackerRead = (_trackerRead + n) % RingSize;
            count -= n;
        }
    }

    private void FeedLookaheadTracker()
    {
        if (_trackerRate != CaptureRate)
        {
            _trackerRate = CaptureRate;
            _tracker = new BeatTracker(CaptureRate);
            _lookaheadTrackerFrame = -1;
        }
        for (int iter = 0; iter < 4; iter++)
        {
            int n = _lookahead.CopyNewPlaybackMono(ref _lookaheadTrackerFrame, _trackerChunk);
            if (n <= 0) break;
            _trackerCrossed += _tracker.Push(_trackerChunk.AsSpan(0, n));
        }
    }

    private int _trackerRate = CaptureRate;

    private void ResetBeatTracking()
    {
        _tracker.Reset();
        _trackerRead = -1;
        _lookaheadTrackerFrame = -1;
        _trackerCrossed = 0;
        State.BeatLocked = false;
        _beatIntervalCount = 0;
        _beatIntervalIndex = 0;
        _lastBeatTime = -10;
        State.BeatCount = 0;
        State.BarCount = 0;
        State.BeatPulse = 0;
        State.DownPulse = 0;
    }

    private bool AnalyzeCapture(float dt)
    {
        if (_capture == null) return false;
        int n = FftSize;
        int available = Volatile.Read(ref _samplesAvailable);
        if (available < n) return false;
        int write = Volatile.Read(ref _writeIndex);
        int start = (write - n + RingSize) % RingSize;
        for (int i = 0; i < n; i++)
            _analysis[i] = Volatile.Read(ref _ring[(start + i) % RingSize]);
        bool ok = _analyzer.Analyze(_analysis, n, CaptureRate, dt, State);
        FeedTracker(write);
        if (ok)
        {
            State.DeviceName = _captureName;
            State.Bpm = _bpm;
        }
        return ok;
    }

    private bool RecentCaptureData()
    {
        if (_capture == null) return false;
        long tick = Volatile.Read(ref _lastDataTick);
        return tick != 0 && Environment.TickCount64 - tick < 1400;
    }

    private void StartOutput()
    {
        try
        {
            _output = new WaveOutEvent { DesiredLatency = 110, NumberOfBuffers = 3 };
            _output.Init(new SampleToWaveProvider(_provider));
            _output.Play();
            State.SourceName = "GENERATIVE SCORE";
        }
        catch (Exception ex)
        {
            _output?.Dispose();
            _output = null;
            LastError = "Audio output: " + ex.Message;
        }
    }

    private void StartCaptureDefault()
    {
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            StartCapture(device);
        }
        catch (Exception ex)
        {
            LastError = "Loopback capture: " + ex.Message;
            State.DeviceName = "";
        }
    }

    private void StartCapture(MMDevice device)
    {
        DisengageBuffer();
        StopCapture();
        _app = null;
        State.AppCapture = false;
        try
        {
            _captureDevice = device;
            _captureName = device.FriendlyName;
            State.DeviceName = _captureName;
            var capture = new WasapiLoopbackCapture(device);
            _captureFormat = capture.WaveFormat;
            capture.DataAvailable += OnCaptureData;
            capture.RecordingStopped += OnCaptureStopped;
            capture.StartRecording();
            _capture = capture;
            Volatile.Write(ref _writeIndex, 0);
            Volatile.Write(ref _samplesAvailable, 0);
            Volatile.Write(ref _lastDataTick, 0);
            State.DeviceName = _captureName;
        }
        catch (Exception ex)
        {
            LastError = "Loopback start: " + ex.Message;
            StopCapture();
        }
    }

    private void StopCapture()
    {
        var capture = _capture;
        _capture = null;
        if (capture == null) return;
        try
        {
            capture.DataAvailable -= OnCaptureData;
            capture.RecordingStopped -= OnCaptureStopped;
            capture.StopRecording();
            capture.Dispose();
        }
        catch
        {
            // ignored - device may already be gone
        }
    }

    private void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            LastError = "Capture stopped: " + e.Exception.Message;
    }

    private void OnCaptureData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        var format = _captureFormat;
        if (format == null) return;
        int channels = Math.Max(1, format.Channels);
        int frames;
        float peak = 0;
        int write = _writeIndex;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            frames = e.BytesRecorded / 4 / channels;
            var floats = MemoryMarshal.Cast<byte, float>(new ReadOnlySpan<byte>(e.Buffer, 0, e.BytesRecorded));
            var live = _live;
            int st = 0;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++) sum += floats[f * channels + c];
                float s = sum / channels;
                peak = MathF.Max(peak, MathF.Abs(s));
                _ring[write] = s;
                write = (write + 1) % RingSize;
                if (live != null)
                {
                    _stereoScratch[st++] = floats[f * channels];
                    _stereoScratch[st++] = channels > 1 ? floats[f * channels + 1] : floats[f * channels];
                    if (st >= _stereoScratch.Length) { live.Write(_stereoScratch.AsSpan(0, st)); st = 0; }
                }
            }
            if (live != null && st > 0) live.Write(_stereoScratch.AsSpan(0, st));
        }
        else if (format.BitsPerSample == 16)
        {
            frames = e.BytesRecorded / 2 / channels;
            var live = _live;
            int st = 0;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0, l = 0, r = 0;
                for (int c = 0; c < channels; c++)
                {
                    int idx = (f * channels + c) * 2;
                    short v = (short)(e.Buffer[idx] | (e.Buffer[idx + 1] << 8));
                    float x = v / 32768f;
                    sum += x;
                    if (c == 0) l = x;
                    if (c == 1) r = x;
                }
                if (channels == 1) r = l;
                float s = sum / channels;
                peak = MathF.Max(peak, MathF.Abs(s));
                _ring[write] = s;
                write = (write + 1) % RingSize;
                if (live != null)
                {
                    _stereoScratch[st++] = l;
                    _stereoScratch[st++] = r;
                    if (st >= _stereoScratch.Length) { live.Write(_stereoScratch.AsSpan(0, st)); st = 0; }
                }
            }
            if (live != null && st > 0) live.Write(_stereoScratch.AsSpan(0, st));
        }
        else
        {
            return;
        }
        Volatile.Write(ref _writeIndex, write);
        Volatile.Write(ref _samplesAvailable, Math.Min(RingSize, _samplesAvailable + frames));
        _capturePeak = peak;
        Volatile.Write(ref _lastDataTick, Environment.TickCount64);
        if (_recordWriter != null && _recordQueue.Count < 512)
            _recordQueue.Enqueue(new ReadOnlySpan<byte>(e.Buffer, 0, e.BytesRecorded).ToArray());
    }

    public bool Recording => _recordWriter != null;

    public bool StartRecording(string path)
    {
        StopRecording();
        var format = _captureFormat;
        if (format == null)
        {
            LastError = "Record: loopback format unavailable";
            return false;
        }
        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _recordWriter = new WaveFileWriter(path, format);
            return true;
        }
        catch (Exception ex)
        {
            LastError = "Record: " + ex.Message;
            _recordWriter = null;
            return false;
        }
    }

    public void StopRecording()
    {
        var writer = _recordWriter;
        _recordWriter = null;
        DrainRecording();
        try
        {
            writer?.Flush();
            writer?.Dispose();
        }
        catch
        {
            // ignored - file may already be closed
        }
    }

    private void DrainRecording()
    {
        var writer = _recordWriter;
        if (writer == null)
        {
            while (_recordQueue.TryDequeue(out _)) { }
            return;
        }
        while (_recordQueue.TryDequeue(out var block))
        {
            try
            {
                writer.Write(block, 0, block.Length);
            }
            catch
            {
                break;
            }
        }
    }

    private float PollOtherApps()
    {
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            float peak = 0;
            uint own = (uint)Environment.ProcessId;
            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    var session = sessions[i];
                    if (session.GetProcessID == own) continue;
                    if (session.State != AudioSessionState.AudioSessionStateActive) continue;
                    peak = MathF.Max(peak, session.AudioMeterInformation.MasterPeakValue);
                }
                catch
                {
                    // session disappeared between enumeration and read
                }
            }
            return peak;
        }
        catch (Exception ex)
        {
            LastError = "Session meter: " + ex.Message;
            return 0;
        }
    }

    /// <summary>Sources the F3 key cycles through: whole system first, then each app with audio.</summary>
    public List<AudioApp?> ListSources()
    {
        var list = new List<AudioApp?> { null };
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            if (ProcessLoopbackCapture.IsSupported)
                list.AddRange(AudioApps.List(_enumerator));
        }
        catch (Exception ex)
        {
            LastError = "App list: " + ex.Message;
        }
        return list;
    }

    /// <summary>Advances to the next audio source and returns a label for the toast.</summary>
    public string CycleSource()
    {
        var sources = ListSources();
        int current = 0;
        if (_app != null)
            for (int i = 1; i < sources.Count; i++)
                if (sources[i]!.Name.Equals(_app.Name, StringComparison.OrdinalIgnoreCase)) current = i;
        var next = sources[(current + 1) % sources.Count];
        if (next == null)
        {
            _preferredApp = "";
            StartCaptureDefault();
            ResetForNewSource();
            return "ALL SYSTEM AUDIO" + (sources.Count == 1 ? " (NO APPS PLAYING)" : "");
        }
        return SelectApp(next) ? "APP // " + next.Display.ToUpperInvariant() : "APP CAPTURE FAILED // " + LastError.ToUpperInvariant();
    }

    /// <summary>Capture a specific app by process or display name (e.g. "spotify"). Remembered if it is not running yet.</summary>
    public bool SelectApp(string name)
    {
        _preferredApp = name.Trim();
        if (_preferredApp.Length == 0) return false;
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            var app = AudioApps.Find(_enumerator, _preferredApp);
            if (app == null)
            {
                State.DeviceName = "WAITING FOR " + _preferredApp.ToUpperInvariant();
                return false;
            }
            return SelectApp(app);
        }
        catch (Exception ex)
        {
            LastError = "App select: " + ex.Message;
            return false;
        }
    }

    public bool SelectApp(AudioApp app)
    {
        if (!ProcessLoopbackCapture.IsSupported)
        {
            LastError = "per-app capture needs Windows 10 build 20348 or Windows 11";
            return false;
        }
        if (_app == null || !_app.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase)) DisengageBuffer();
        StopCapture();
        _preferredApp = app.Name;
        try
        {
            var capture = new ProcessLoopbackCapture(app.ProcessId);
            _captureFormat = capture.WaveFormat;
            capture.DataAvailable += OnCaptureData;
            capture.RecordingStopped += OnCaptureStopped;
            capture.StartRecording();
            _captureFormat = capture.WaveFormat;
            _capture = capture;
            _app = app;
            _captureName = app.Display;
            State.DeviceName = app.Display;
            State.AppCapture = true;
            Volatile.Write(ref _writeIndex, 0);
            Volatile.Write(ref _samplesAvailable, 0);
            Volatile.Write(ref _lastDataTick, 0);
            ResetForNewSource();
            return true;
        }
        catch (Exception ex)
        {
            LastError = "App capture: " + ex.Message;
            _app = null;
            State.AppCapture = false;
            StartCaptureDefault();
            return false;
        }
    }

    /// <summary>Re-attaches when the chosen app is closed and reopened.</summary>
    private void MaintainAppCapture()
    {
        if (_app != null && AudioApps.IsAlive(_app.ProcessId)) return;
        if (_app != null)
        {
            string waiting = _preferredApp;
            StartCaptureDefault();
            _preferredApp = waiting;
            State.DeviceName = "WAITING FOR " + waiting.ToUpperInvariant();
        }
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            var app = AudioApps.Find(_enumerator, _preferredApp);
            if (app != null) SelectApp(app);
        }
        catch
        {
            // try again on the next poll
        }
    }

    private void ResetForNewSource()
    {
        _external = false;
        _externalOnTimer = 0;
        _analyzer.Reset();
        ResetBeatTracking();
        ResetMusic();
    }

    /// <summary>A new song or source: chords, key and section memory start over.</summary>
    private void ResetMusic()
    {
        _music.Reset();
        _structure.Reset();
        _sectionPending = false;
        State.SongEpoch++;
        State.Key = -1;
        State.ChordRoot = -1;
    }

    /// <summary>
    /// Buffered sources: run structure detection on the newest buffered audio (ahead of what is
    /// heard) and hand each section change to the game when playback reaches its first beat.
    /// </summary>
    private void AnalyzeStructureAhead(float dt)
    {
        _music.Structure = false;
        float ahead = _lookahead.LookaheadSeconds;
        long written = _lookahead.WrittenFrames;
        if (_lookahead.CopyAheadWindow(_aheadWindow))
        {
            _structure.WindowEndFrame = written;
            _structState.ClearBeatFlags();
            _structState.BeatLocked = State.BeatLocked;
            _structState.DownbeatOffset = State.DownbeatOffset;
            _structState.BeatPosition = State.BeatPosition + ahead * State.Bpm / 60.0;
            _structure.Analyze(_aheadWindow, ReadOnlySpan<float>.Empty, CaptureRate, dt, _structState);
            if (_structState.SectionChanged)
            {
                if (_structState.SectionCorrected)
                {
                    // corrections apply straight away
                    _sectionPending = false;
                    State.SectionChanged = true;
                    State.SectionRepeat = _structState.SectionRepeat;
                    State.SectionCorrected = true;
                    State.SectionLabel = _structState.SectionLabel;
                }
                else
                {
                    // pinned to the audio frame where the section's first beat starts: it fires when
                    // playback reaches that frame (plus the sound card's output latency)
                    _sectionPending = true;
                    _sectionDueBeat = _structure.LastBoundaryBeatIndex;
                    _sectionDueFrame = _structure.LastBoundaryFrame + (long)(OutputLatencySeconds * CaptureRate);
                    _sectionDueLabel = _structState.SectionLabel;
                    _sectionDueRepeat = _structState.SectionRepeat;
                }
            }
        }
        if (_sectionPending && _lookahead.PlayedFrames >= _sectionDueFrame)
        {
            _sectionPending = false;
            State.SectionChanged = true;
            State.SectionRepeat = _sectionDueRepeat;
            State.SectionLabel = _sectionDueLabel;
        }
        if (!State.SectionChanged) State.SectionLabel = _sectionPending ? State.SectionLabel : _structState.SectionLabel;
        State.SectionCount = _structState.SectionCount;
        State.Section = _structState.Section;
    }

    /// <summary>Beat index on which the last structure-ahead section starts (diagnostics).</summary>
    public double LastSectionStartBeat => _structure.LastBoundaryBeatIndex;
    public float PlaybackSeconds => _lookahead.PlayedFrames / (float)CaptureRate;

    private void AnalyzeMusicFromCapture(float dt)
    {
        if (_capture == null) return;
        int n = MusicAnalyzer.WindowSize;
        if (Volatile.Read(ref _samplesAvailable) < n) return;
        int write = Volatile.Read(ref _writeIndex);
        int start = (write - n + RingSize) % RingSize;
        for (int i = 0; i < n; i++) _musicWindow[i] = Volatile.Read(ref _ring[(start + i) % RingSize]);
        _music.Structure = true;
        _music.Analyze(_musicWindow, _analyzer.Magnitudes, _captureFormat?.SampleRate ?? CaptureRate, dt, State);
    }

    public void CycleDevice()
    {
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            var endpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var list = new List<MMDevice>();
            for (int i = 0; i < endpoints.Count; i++) list.Add(endpoints[i]);
            if (list.Count == 0) return;
            int current = -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i].ID == _captureDevice?.ID) current = i;
            var next = list[(current + 1) % list.Count];
            StartCapture(next);
            _external = false;
            _analyzer.Reset();
            ResetBeatTracking();
            State.DeviceName = next.FriendlyName;
        }
        catch (Exception ex)
        {
            LastError = "Device cycle: " + ex.Message;
        }
    }

    /// <summary>Music (built-in score) and effects gain, 0..1, applied in the output mixer.</summary>
    public void SetVolumes(float music, float sfx)
    {
        _provider.MusicVolume = Math.Clamp(music, 0f, 1f);
        _provider.SfxVolume = Math.Clamp(sfx, 0f, 1f);
    }

    public void SetQuiet(bool quiet)
    {
        _quiet = quiet;
        State.Quiet = quiet;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopRecording();
        DisengageBuffer();
        StopCapture();
        try
        {
            _output?.Stop();
            _output?.Dispose();
        }
        catch
        {
            // ignored
        }
        _localTrack?.Dispose();
        _localTrack = null;
        _captureDevice?.Dispose();
        _enumerator?.Dispose();
    }

    private sealed class AudioProvider : ISampleProvider
    {
        private readonly ISampleProvider _music;
        private readonly SoundBank _sfx;
        public AudioProvider(ISampleProvider music, SoundBank sfx)
        {
            _music = music;
            _sfx = sfx;
        }
        public volatile float MusicVolume = 1f;
        public volatile float SfxVolume = 1f;
        /// <summary>Replaying a buffered app: it plays at its own level (Spotify's volume rules), not the score's.</summary>
        public volatile bool LiveMode;
        public WaveFormat WaveFormat => _music.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int read = _music.Read(buffer, offset, count);
            float music = LiveMode ? 1f : MusicVolume;
            if (music < 0.999f)
                for (int i = offset; i < offset + count; i++) buffer[i] *= music;
            // SFX always mixes post-delay so gameplay hits/dashes have zero added latency.
            _sfx.Mix(buffer, offset, count, SfxVolume);
            for (int i = offset; i < offset + count; i++)
                buffer[i] = Math.Clamp(buffer[i], -0.95f, 0.95f);
            return read;
        }
    }
}
