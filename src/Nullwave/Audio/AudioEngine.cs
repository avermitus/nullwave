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
    private BeatTracker _tracker = new(CaptureRate);
    private int _trackerRead = -1;
    private readonly float[] _trackerChunk = new float[4096];
    private int _trackerCrossed;
    private float _pulse;
    private readonly AudioProvider _provider;
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
        _provider = new AudioProvider(Synth, Sfx);
        if (enableOutput) StartOutput();
        if (enableCapture) StartCaptureDefault();
    }

    public void Update(float dt)
    {
        if (_disposed) return;
        DrainRecording();
        dt = Math.Clamp(dt, 0.0005f, 0.1f);
        _time += dt;
        State.Time = (float)_time;
        State.ClearBeatFlags();
        Spotify.Update(dt);
        State.SpotifyStatus = Spotify.Current.State;
        State.SpotifyTrack = Spotify.Current.DisplayTrack;

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
                bool spotifyActive = (_app != null && _app.Name.Contains("spotify", StringComparison.OrdinalIgnoreCase)) ||
                                     (_app == null && Spotify.Current.IsPlaying);
                State.SourceName = spotifyActive && !string.IsNullOrEmpty(State.SpotifyTrack)
                    ? "SPOTIFY // " + State.SpotifyTrack
                    : (string.IsNullOrWhiteSpace(State.DeviceName) ? "SYSTEM LOOPBACK" : State.DeviceName);
                State.Quiet = _quiet;
                ApplyTracker(dt);
                return;
            }
            _external = false;
            _synthVisual.Reset();
        }

        _synthVisual.Advance(dt, State);
        State.External = false;
        State.Quiet = _quiet;
        State.Bpm = _bpm;
        State.DeviceName = _captureName;
        State.BarCount = State.BeatCount / 4;
        DecayPulses(dt);
        if (!_enableOutput || _quiet || _external)
            Synth.TargetLevel = 0;
        else
            Synth.TargetLevel = 1;
    }

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

    private int _trackerRate = CaptureRate;

    private void ResetBeatTracking()
    {
        _tracker.Reset();
        _trackerRead = -1;
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
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++) sum += floats[f * channels + c];
                float s = sum / channels;
                peak = MathF.Max(peak, MathF.Abs(s));
                _ring[write] = s;
                write = (write + 1) % RingSize;
            }
        }
        else if (format.BitsPerSample == 16)
        {
            frames = e.BytesRecorded / 2 / channels;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int idx = (f * channels + c) * 2;
                    short v = (short)(e.Buffer[idx] | (e.Buffer[idx + 1] << 8));
                    sum += v / 32768f;
                }
                float s = sum / channels;
                peak = MathF.Max(peak, MathF.Abs(s));
                _ring[write] = s;
                write = (write + 1) % RingSize;
            }
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
        _captureDevice?.Dispose();
        _enumerator?.Dispose();
    }

    private sealed class AudioProvider : ISampleProvider
    {
        private readonly SynthScore _synth;
        private readonly SoundBank _sfx;
        public AudioProvider(SynthScore synth, SoundBank sfx)
        {
            _synth = synth;
            _sfx = sfx;
        }
        public volatile float MusicVolume = 1f;
        public volatile float SfxVolume = 1f;
        public WaveFormat WaveFormat => _synth.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int read = _synth.Read(buffer, offset, count);
            float music = MusicVolume;
            if (music < 0.999f)
                for (int i = offset; i < offset + count; i++) buffer[i] *= music;
            _sfx.Mix(buffer, offset, count, SfxVolume);
            for (int i = offset; i < offset + count; i++)
                buffer[i] = Math.Clamp(buffer[i], -0.95f, 0.95f);
            return read;
        }
    }
}
