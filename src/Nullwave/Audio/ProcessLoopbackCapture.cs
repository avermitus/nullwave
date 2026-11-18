using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using IAudioClient = NAudio.CoreAudioApi.Interfaces.IAudioClient;

namespace Nullwave.Audio;

/// <summary>
/// Captures the audio of one application (and its child processes) using WASAPI process
/// loopback (Windows 10 build 20348+ / Windows 11). Only that app is heard: the game's own
/// synth and other apps are excluded, so beat detection follows exactly the chosen track.
/// NAudio 2.2.1 ships the parameter structs but no capture class, so activation is done here.
/// </summary>
public sealed class ProcessLoopbackCapture : IWaveIn
{
    private const string VirtualDevice = "VAD\\Process_Loopback";
    private static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    private readonly int _processId;
    private AudioClient? _client;
    private AudioCaptureClient? _captureClient;
    private Thread? _thread;
    private volatile bool _running;
    private readonly AutoResetEvent _event = new(false);

    public WaveFormat WaveFormat { get; set; }
    public int ProcessId => _processId;
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public static bool IsSupported => Environment.OSVersion.Version.Build >= 20348;

    public ProcessLoopbackCapture(int processId)
    {
        _processId = processId;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    }

    public void StartRecording()
    {
        if (_running) return;
        var client = Activate(_processId);
        // Process loopback has no mix format; ask for float and let WASAPI convert.
        const AudioClientStreamFlags flags = AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback
            | (AudioClientStreamFlags)0x80000000 /* AUTOCONVERTPCM */ | (AudioClientStreamFlags)0x08000000 /* SRC_DEFAULT_QUALITY */;
        try
        {
            client.Initialize(AudioClientShareMode.Shared, flags, 2_000_000, 0, WaveFormat, Guid.Empty);
        }
        catch (COMException)
        {
            client.Dispose();
            client = Activate(_processId);
            WaveFormat = new WaveFormat(48000, 16, 2);
            client.Initialize(AudioClientShareMode.Shared, flags, 2_000_000, 0, WaveFormat, Guid.Empty);
        }
        client.SetEventHandle(_event.SafeWaitHandle.DangerousGetHandle());
        _captureClient = client.AudioCaptureClient;
        _client = client;
        client.Start();
        _running = true;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "nullwave-app-capture", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void CaptureLoop()
    {
        Exception? error = null;
        int frameBytes = WaveFormat.BlockAlign;
        byte[] buffer = new byte[48000 * frameBytes];
        try
        {
            while (_running)
            {
                _event.WaitOne(100);
                var cc = _captureClient;
                if (cc == null) break;
                int packet = cc.GetNextPacketSize();
                while (packet > 0 && _running)
                {
                    IntPtr data = cc.GetBuffer(out int frames, out AudioClientBufferFlags bufferFlags);
                    int bytes = frames * frameBytes;
                    if (bytes > buffer.Length) buffer = new byte[bytes];
                    if ((bufferFlags & AudioClientBufferFlags.Silent) != 0) Array.Clear(buffer, 0, bytes);
                    else Marshal.Copy(data, buffer, 0, bytes);
                    cc.ReleaseBuffer(frames);
                    if (bytes > 0) DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytes));
                    packet = cc.GetNextPacketSize();
                }
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }
        _running = false;
        RecordingStopped?.Invoke(this, new StoppedEventArgs(error));
    }

    public void StopRecording()
    {
        _running = false;
        _event.Set();
        if (_thread != null && Thread.CurrentThread != _thread) _thread.Join(500);
        _thread = null;
        try { _client?.Stop(); } catch { /* client may already be gone */ }
    }

    public void Dispose()
    {
        StopRecording();
        _client?.Dispose();
        _client = null;
        _captureClient = null;
        _event.Dispose();
    }

    // ------------------------------------------------------------ activation

    private static AudioClient Activate(int processId)
    {
        var p = new ActivationParams { ActivationType = 1, TargetProcessId = (uint)processId, ProcessLoopbackMode = 0 };
        int size = Marshal.SizeOf<ActivationParams>();
        IntPtr paramsPtr = Marshal.AllocHGlobal(size);
        IntPtr variantPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariantBlob>());
        try
        {
            Marshal.StructureToPtr(p, paramsPtr, false);
            Marshal.StructureToPtr(new PropVariantBlob { Vt = 65 /* VT_BLOB */, CbSize = (uint)size, PBlobData = paramsPtr }, variantPtr, false);
            var handler = new CompletionHandler();
            // Activation must happen on an MTA thread; the thread pool is MTA.
            var activation = Task.Run(() =>
            {
                ActivateAudioInterfaceAsync(VirtualDevice, IidAudioClient, variantPtr, handler, out _);
                if (!handler.Done.Wait(TimeSpan.FromSeconds(3)))
                    throw new TimeoutException("process loopback activation timed out");
            });
            activation.GetAwaiter().GetResult();
            if (handler.Result < 0 || handler.Client == null)
                throw new COMException("process loopback activation failed", handler.Result);
            return new AudioClient((IAudioClient)handler.Client);
        }
        finally
        {
            Marshal.FreeHGlobal(variantPtr);
            Marshal.FreeHGlobal(paramsPtr);
        }
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr activationParams,
        IActivateCompletionHandler completionHandler,
        out IActivateOperation activationOperation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationParams
    {
        public int ActivationType;
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariantBlob
    {
        public ushort Vt;
        public ushort R1, R2, R3;
        public uint CbSize;
        public IntPtr PBlobData;
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateCompletionHandler
    {
        void ActivateCompleted(IActivateOperation operation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateOperation
    {
        void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject { }

    [ComVisible(true)]
    private sealed class CompletionHandler : IActivateCompletionHandler, IAgileObject
    {
        public readonly ManualResetEventSlim Done = new(false);
        public int Result = -1;
        public object? Client;

        public void ActivateCompleted(IActivateOperation operation)
        {
            try
            {
                operation.GetActivateResult(out Result, out Client);
            }
            catch (Exception ex)
            {
                Result = ex.HResult;
            }
            Done.Set();
        }
    }
}

/// <summary>An application that currently owns an audio session on the default output.</summary>
public sealed record AudioApp(int ProcessId, string Name, string Display, float Peak);

public static class AudioApps
{
    /// <summary>
    /// Lists apps with audio sessions, one entry per executable, targeting the oldest process of
    /// that name (the browser / launcher root) so the whole process tree is captured.
    /// </summary>
    // other copies of the game and system hosts (mic/call sessions) are never a music source
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase) { "Nullwave", "svchost", "audiodg", "System" };

    public static List<AudioApp> List(MMDeviceEnumerator enumerator)
    {
        var result = new Dictionary<string, AudioApp>(StringComparer.OrdinalIgnoreCase);
        int own = Environment.ProcessId;
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            try
            {
                var s = sessions[i];
                int pid = (int)s.GetProcessID;
                if (pid == 0 || pid == own || s.IsSystemSoundsSession) continue;
                if (s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
                string name;
                try { name = Process.GetProcessById(pid).ProcessName; } catch { continue; }
                if (Ignored.Contains(name)) continue;
                float peak = s.AudioMeterInformation.MasterPeakValue;
                if (result.TryGetValue(name, out var existing))
                {
                    if (peak > existing.Peak) result[name] = existing with { Peak = peak };
                    continue;
                }
                result[name] = new AudioApp(RootProcess(name, pid), name, Pretty(name, s.DisplayName), peak);
            }
            catch
            {
                // session vanished while enumerating
            }
        }
        return result.Values.OrderByDescending(a => a.Peak).ThenBy(a => a.Display).ToList();
    }

    public static AudioApp? Find(MMDeviceEnumerator enumerator, string name)
    {
        foreach (var app in List(enumerator))
            if (app.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || app.Display.Equals(name, StringComparison.OrdinalIgnoreCase))
                return app;
        // not playing yet: still allow targeting a running process by name
        var procs = Process.GetProcessesByName(name);
        if (procs.Length == 0) return null;
        return new AudioApp(RootProcess(name, procs[0].Id), procs[0].ProcessName, Pretty(procs[0].ProcessName, ""), 0f);
    }

    public static bool IsAlive(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch { return false; }
    }

    private static int RootProcess(string name, int fallback)
    {
        int best = fallback;
        DateTime bestStart = DateTime.MaxValue;
        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                if (p.StartTime < bestStart) { bestStart = p.StartTime; best = p.Id; }
            }
            catch
            {
                // access denied for elevated processes
            }
        }
        return best;
    }

    private static string Pretty(string processName, string displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith('@')) return displayName.Trim();
        return processName.ToLowerInvariant() switch
        {
            "spotify" => "Spotify",
            "chrome" => "Chrome",
            "msedge" => "Edge",
            "firefox" => "Firefox",
            "discord" => "Discord",
            "vlc" => "VLC",
            "applemusic" or "itunes" => "Apple Music",
            "foobar2000" => "foobar2000",
            _ => processName,
        };
    }
}
