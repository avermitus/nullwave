using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Nullwave.Audio;

/// <summary>
/// Per-app output routing (the "App volume and device preferences" page in Windows Settings),
/// through the undocumented Windows.Media.Internal.AudioPolicyConfig factory that EarTrumpet
/// uses. The buffered Spotify mode sends Spotify to a silent output (e.g. Steam Streaming
/// Speakers), captures it there and replays it 0.75 s later through the game. Routing is always
/// cleared again on exit, on source switch, and at the next start if the game crashed.
/// Windows 10 21H2+ / Windows 11 only; every call fails soft.
/// </summary>
public static class AppAudioRouter
{
    private const string DevInterfaceAudioRender = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const string MmDevApiToken = @"\\?\SWD#MMDEVAPI#";
    private static readonly Guid FactoryIid21H2 = new("ab3d4648-e242-459f-b02f-541c70306324");
    // IUnknown (3) + IInspectable (3) + 19 methods, then Set / Get / ClearAll
    private const int SlotSet = 25;
    private const int SlotGet = 26;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetFn(IntPtr self, uint processId, int flow, int role, IntPtr deviceId);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetFn(IntPtr self, uint processId, int flow, int role, out IntPtr deviceId);

    [DllImport("combase.dll")] private static extern int RoInitialize(int initType);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string source, uint length, out IntPtr hstring);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr hstring);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    private static IntPtr _factory;
    private static bool _tried;
    private static readonly object Gate = new();
    public static string LastError { get; private set; } = "";

    public static bool IsSupported => Environment.OSVersion.Version.Build >= 19044 && Factory() != IntPtr.Zero;

    private static IntPtr Factory()
    {
        lock (Gate)
        {
            if (_tried) return _factory;
            _tried = true;
            try
            {
                RoInitialize(1); // MTA; RPC_E_CHANGED_MODE is fine
                const string cls = "Windows.Media.Internal.AudioPolicyConfig";
                if (WindowsCreateString(cls, (uint)cls.Length, out var h) != 0) return IntPtr.Zero;
                try
                {
                    var iid = FactoryIid21H2;
                    int hr = RoGetActivationFactory(h, ref iid, out var f);
                    if (hr == 0) _factory = f;
                    else LastError = $"AudioPolicyConfig unavailable (0x{hr:X8})";
                }
                finally { WindowsDeleteString(h); }
            }
            catch (Exception ex)
            {
                LastError = "AudioPolicyConfig: " + ex.Message;
            }
            return _factory;
        }
    }

    private static T Slot<T>(IntPtr f, int slot) where T : Delegate
    {
        IntPtr vtable = Marshal.ReadIntPtr(f);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
    }

    /// <summary>Routes an app's output to an endpoint (MMDevice.ID), or back to the system default when null.</summary>
    public static bool SetAppEndpoint(int processId, string? endpointId)
    {
        var f = Factory();
        if (f == IntPtr.Zero) return false;
        IntPtr h = IntPtr.Zero;
        try
        {
            if (!string.IsNullOrWhiteSpace(endpointId))
            {
                string packed = MmDevApiToken + endpointId + DevInterfaceAudioRender;
                if (WindowsCreateString(packed, (uint)packed.Length, out h) != 0) return false;
            }
            var set = Slot<SetFn>(f, SlotSet);
            int hr1 = set(f, (uint)processId, 0 /* eRender */, 1 /* eMultimedia */, h);
            int hr2 = set(f, (uint)processId, 0, 0 /* eConsole */, h);
            if (hr1 != 0 || hr2 != 0) LastError = $"SetPersistedDefaultAudioEndpoint 0x{hr1:X8}/0x{hr2:X8}";
            return hr1 == 0 && hr2 == 0;
        }
        catch (Exception ex)
        {
            LastError = "Route: " + ex.Message;
            return false;
        }
        finally
        {
            if (h != IntPtr.Zero) WindowsDeleteString(h);
        }
    }

    /// <summary>The endpoint an app is pinned to ("" = follows the system default), or null on failure.</summary>
    public static string? GetAppEndpoint(int processId)
    {
        var f = Factory();
        if (f == IntPtr.Zero) return null;
        try
        {
            var get = Slot<GetFn>(f, SlotGet);
            int hr = get(f, (uint)processId, 0, 1, out var h);
            if (hr != 0) { LastError = $"GetPersistedDefaultAudioEndpoint 0x{hr:X8}"; return null; }
            if (h == IntPtr.Zero) return "";
            string s = Marshal.PtrToStringUni(WindowsGetStringRawBuffer(h, out uint len), (int)len) ?? "";
            WindowsDeleteString(h);
            return s;
        }
        catch (Exception ex)
        {
            LastError = "Route query: " + ex.Message;
            return null;
        }
    }

    private static readonly string[] SilentNames =
        ["steam streaming speakers", "cable input", "vb-audio", "voicemeeter", "virtual audio", "dummy output"];

    /// <summary>An active render endpoint nobody listens to (virtual cable / streaming sink), never the default device.</summary>
    public static MMDevice? FindSilentEndpoint(MMDeviceEnumerator enumerator)
    {
        try
        {
            string defaultId = "";
            try { defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; } catch { }
            var all = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (string key in SilentNames)
                for (int i = 0; i < all.Count; i++)
                {
                    var d = all[i];
                    if (d.ID == defaultId) continue;
                    if (d.FriendlyName.Contains(key, StringComparison.OrdinalIgnoreCase)) return d;
                }
        }
        catch (Exception ex)
        {
            LastError = "Endpoint scan: " + ex.Message;
        }
        return null;
    }

    // ---------------------------------------------------------------- crash safety
    private static string MarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nullwave", "routed_app.txt");

    /// <summary>Remembers which app was rerouted so a crashed session can be undone at next start.</summary>
    public static void WriteMarker(string appName)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, appName);
        }
        catch { }
    }

    public static void ClearMarker()
    {
        try { if (File.Exists(MarkerPath)) File.Delete(MarkerPath); } catch { }
    }

    /// <summary>At start-up: if a previous run left an app rerouted (crash), send it back to the default device.</summary>
    public static void RecoverFromCrash(MMDeviceEnumerator enumerator)
    {
        try
        {
            if (!File.Exists(MarkerPath)) return;
            string app = File.ReadAllText(MarkerPath).Trim();
            if (app.Length > 0)
                foreach (var a in AudioApps.List(enumerator))
                    if (a.Name.Equals(app, StringComparison.OrdinalIgnoreCase))
                        SetAppEndpoint(a.ProcessId, null);
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(app))
            {
                SetAppEndpoint(p.Id, null);
                p.Dispose();
            }
            ClearMarker();
        }
        catch { }
    }
}
