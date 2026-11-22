using System.Diagnostics;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.Render;
using Nullwave.Tools;
using Nullwave.World;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Nullwave;

internal static class Program
{
    private static IWindow? _window;
    private static GL? _gl;
    private static IInputContext? _input;
    private static Renderer? _renderer;
    private static Game? _game;
    private static Stopwatch _clock = new();
    private static double _lastTime;
    private static double _lastFrameEnd;

    // Windows' default timer only wakes a sleeping thread every ~15.6 ms, which capped the game at
    // about 64 fps. Ask for 1 ms precision while the game runs.
    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);
    private static bool _timerRaised;

    /// <summary>Sleeps most of the wait, then spins the last stretch so frames land on time.</summary>
    internal static void WaitUntil(Stopwatch clock, double targetSeconds)
    {
        while (true)
        {
            double remainingMs = (targetSeconds - clock.Elapsed.TotalSeconds) * 1000.0;
            if (remainingMs <= 0) return;
            if (remainingMs > 2.0) Thread.Sleep((int)(remainingMs - 1.0));
            else Thread.SpinWait(200);
        }
    }
    private static string _mode = "play";
    private static string _captureScenario = "combat";
    private static string _captureOut = "";
    private static float _smokeSeconds = 8f;
    private static float _recordSeconds = 30f;
    private static string _recordVideo = Path.Combine("capture", "video.mp4");
    private static string _recordWav = Path.Combine("capture", "audio.wav");
    private static double _recordFps = 60;
    private static Process? _ffmpeg;
    private static long _framesWritten;
    private static double _recordStart;
    private static bool _recordArmed;
    private static float _recordWait;
    private static int _recordWave;
    private static byte[]? _frameBuffer;
    private static int _captureTargetFrames = 480;
    private static int _frame;
    private static bool _saved;
    private static int _forceStyle = -1;
    private static float _reelSeconds;
    private static Process? _reelEncoder;
    private static int _reelFrames;
#if DEBUG
    private static ClaudeDebugBridge? _bridge;
#endif

    private static void Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--selftest") { _mode = "selftest"; }
            else if (a == "--map")
            {
                if (i + 1 >= args.Length || !MapCatalog.TryParse(args[++i], out var map))
                {
                    Console.Error.WriteLine("Invalid --map. Choose arena, halo or cathedral.");
                    Environment.ExitCode = 2;
                    return;
                }
                Game.MapOverride = map;
            }
            else if (a == "--audiotest") { _mode = "audiotest"; }
            else if (a == "--capture")
            {
                _mode = "capture";
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) _captureScenario = args[++i];
            }
            else if (a == "--out" && i + 1 < args.Length) _captureOut = args[++i];
            else if (a == "--smoke")
            {
                _mode = "smoke";
                Game.SmokeMode = true;
                if (i + 1 < args.Length && float.TryParse(args[i + 1], out float s)) { _smokeSeconds = s; i++; }
            }
            else if (a == "--record")
            {
                _mode = "record";
                if (i + 1 < args.Length && float.TryParse(args[i + 1], out float s)) { _recordSeconds = s; i++; }
            }
            else if (a == "--video" && i + 1 < args.Length) _recordVideo = args[++i];
            else if (a == "--wave" && i + 1 < args.Length && int.TryParse(args[i + 1], out int wv)) { _recordWave = wv; i++; }
            else if (a == "--wav" && i + 1 < args.Length) _recordWav = args[++i];
            else if (a == "--app" && i + 1 < args.Length) Game.AppOverride = args[++i];
            else if (a == "--mods" && i + 1 < args.Length) Game.ModsOverride = args[++i];
            else if (a == "--attune" && i + 1 < args.Length) Game.AttuneOverride = args[++i];
            else if (a == "--seed" && i + 1 < args.Length) Game.SeedOverride = args[++i];
            else if (a == "--theme" && i + 1 < args.Length && GameSettings.TryParseTheme(args[i + 1], out var th)) { Game.ThemeOverride = th; i++; }
            else if (a == "--track" && i + 1 < args.Length) { Game.TrackOverride = args[++i]; }
            else if (a == "--max-fx") { Game.MaxFx = true; }
            else if (a == "--list-apps") { _mode = "listapps"; }
            else if (a == "--beatbench") { _mode = "beatbench"; }
            else if (a == "--style" && i + 1 < args.Length && int.TryParse(args[i + 1], out int st)) { _forceStyle = st; i++; }
            else if (a == "--reel" && i + 1 < args.Length && float.TryParse(args[i + 1], out float rs)) { _reelSeconds = rs; i++; }
            else if (!a.StartsWith("--") && IsAudioTrackArg(a)) { Game.TrackOverride = a; }
        }

        if (_mode == "selftest")
        {
            Environment.ExitCode = SelfTest.Run();
            return;
        }
        if (_mode == "audiotest")
        {
            Environment.ExitCode = AudioTest.Run(Game.AppOverride);
            return;
        }
        if (args.Contains("--musicprobe"))
        {
            int ix = Array.IndexOf(args, "--musicprobe");
            float secs = ix + 1 < args.Length && float.TryParse(args[ix + 1], out float v) ? v : 15f;
            int tx = Array.IndexOf(args, "--track");
            Environment.ExitCode = MusicProbe.Run(secs, tx >= 0 && tx + 1 < args.Length ? args[tx + 1] : null);
            return;
        }
        if (args.Contains("--buffertest"))
        {
            Environment.ExitCode = BufferTest.Run();
            return;
        }
        if (args.Contains("--routecheck"))
        {
            // read-only: where is each process of the app currently routed?
            int ix = Array.IndexOf(args, "--routecheck");
            string app = ix + 1 < args.Length ? args[ix + 1] : "spotify";
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName(app))
            {
                string? route = Nullwave.Audio.AppAudioRouter.GetAppEndpoint(proc.Id);
                Console.WriteLine($"ROUTECHECK | {app} pid={proc.Id} route='{(route == null ? "(query failed)" : route.Length == 0 ? "default" : route)}'");
                proc.Dispose();
            }
            string marker = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nullwave", "routed_app.txt");
            Console.WriteLine($"ROUTECHECK | crash marker present={File.Exists(marker)}");
            return;
        }
        if (args.Contains("--routeprobe"))
        {
            int ix = Array.IndexOf(args, "--routeprobe");
            Environment.ExitCode = RouteProbe.Run(ix + 1 < args.Length ? args[ix + 1] : null);
            return;
        }
        if (args.Contains("--muteprobe"))
        {
            Environment.ExitCode = MuteProbe.Run();
            return;
        }
        if (args.Contains("--sfxdump"))
        {
            string dir = string.IsNullOrEmpty(_captureOut) ? Path.Combine("capture", "sfx") : _captureOut;
            Directory.CreateDirectory(dir);
            foreach (SfxType type in Enum.GetValues<SfxType>())
            {
                var (peak, rms, finite) = SfxProbe.Render(type, Path.Combine(dir, type.ToString().ToLowerInvariant() + ".wav"));
                Console.WriteLine($"  {type,-10} peak {peak:0.000} rms {rms:0.0000} {(finite ? "" : "NON-FINITE")}");
            }
            SfxProbe.RenderShowcase(Path.Combine(dir, "showcase.wav"));
            Console.WriteLine($"  Preview: {Path.Combine(dir, "showcase.wav")}");
            return;
        }
        if (_mode == "beatbench")
        {
            if (args.Contains("--dynamics"))
            {
                foreach (float g in new[] { 0.05f, 1f, 6f, 20f })
                {
                    var d = BeatBench.MeasureDynamics(g);
                    Console.WriteLine($"gain {g,5}: std bass {d.bassStd:0.000} mid {d.midStd:0.000} high {d.highStd:0.000} energy {d.energyStd:0.000} pinned {d.pinned:P0} kicks {d.kicks} snares {d.snares} hats {d.hats}");
                }
                return;
            }
            Environment.ExitCode = BeatBench.Run();
            return;
        }
        if (_mode == "listapps")
        {
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            Console.WriteLine($"Per-app capture supported: {ProcessLoopbackCapture.IsSupported}");
            foreach (var app in AudioApps.List(enumerator))
                Console.WriteLine($"  {app.Name,-20} pid={app.ProcessId,-7} peak={app.Peak:0.000}  \"{app.Display}\"");
            return;
        }

        bool capture = _mode == "capture";
        bool record = _mode == "record";
        bool offscreen = capture || record;
        _captureTargetFrames = capture ? _captureScenario.ToLowerInvariant() switch
        {
            "title" => 4,
            "archive" => 4,
            "boss" => 24,
            "songboss" => 200,
            "laser" => 12,
            "combat" => 24,
            "attune" => 4,
            "pause" => 4,
            "gameover" => 6,
            _ => 8,
        } : 0;
        _captureOut = string.IsNullOrWhiteSpace(_captureOut)
            ? Path.Combine("screenshots", _captureScenario + ".png")
            : _captureOut;

        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(1600, 900),
            Title = capture ? "NULLWAVE // CAPTURE" : record ? "NULLWAVE // RECORD" : "NULLWAVE",
            VSync = !offscreen,
            IsVisible = !offscreen,
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 1)),
            WindowBorder = WindowBorder.Fixed,
        };
        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Resize += OnResize;
        _window.Render += OnRender;
        _window.Closing += OnClosing;
#if DEBUG
        _window.Update += OnUpdate;
#endif
        _window.Run();
    }

    private static void OnLoad()
    {
        if (OperatingSystem.IsWindows())
        {
            try { _timerRaised = timeBeginPeriod(1) == 0; } catch { _timerRaised = false; }
        }
        _gl = GL.GetApi(_window);
        _window!.VSync = _mode == "capture" || _mode == "record" ? false : true;
        _renderer = new Renderer(_gl, _window.FramebufferSize.X, _window.FramebufferSize.Y);
        _input = _window.CreateInput();
        Console.WriteLine($"NULLWAVE | GL {_gl.GetStringS(StringName.Version)} | {_gl.GetStringS(StringName.Renderer)}");
        _game = new Game(_window, _renderer, _mode == "capture" ? null : new InputState(_input), _mode == "capture");
        if (_mode == "capture")
        {
            _game.ConfigureCapture(_captureScenario);
            int warmup = _captureScenario.ToLowerInvariant() switch
            {
                "title" => 120,
                "archive" => 60,
                "boss" => 240,
                "laser" => 26,
                "combat" => 420,
                "synergy" => 260,
                "overdrive" => 260,
                "roster" => 40,
                "songlasers" => 150,
                "attune" => 1,
                "pause" => 1,
                "gameover" => 2,
                _ => 180,
            };
            _game.CaptureWarmup(warmup);
            if (_forceStyle >= 0) _renderer.ForceBackdrop(_forceStyle);
        }
        if (_mode == "smoke") _game.StartSmokeRun(0x5A1E5EEDUL);
        if (_mode == "smoke" && _forceStyle >= 0) _renderer.ForceBackdrop(_forceStyle);
        if (_mode == "record")
        {
            _game.StartSmokeRun(0x5A1E5EEDUL);
            if (_recordWave > 1) _game.ActiveWorld?.BeginWave(_recordWave);
            StartEncoder();
        }
        Console.WriteLine($"AUDIO | capture={_game.Audio.CaptureAvailable} output={_game.Audio.OutputAvailable} device={_game.Audio.State.DeviceName} error={_game.Audio.LastError}");
#if DEBUG
        if (_mode is "play" or "smoke")
        {
            _bridge = new ClaudeDebugBridge(_window);
            _bridge.Start();
        }
#endif
        _clock = Stopwatch.StartNew();
        _lastTime = 0;
        _lastFrameEnd = 0;
    }

    private static bool IsAudioTrackArg(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return false;
        string ext = Path.GetExtension(arg).ToLowerInvariant();
        if (ext is ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" or ".aiff" or ".wma") return true;
        return File.Exists(arg);
    }

    private static void StartReel()
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(_captureOut)) ?? ".";
        Directory.CreateDirectory(dir);
        var psi = new ProcessStartInfo("ffmpeg",
            $"-y -v error -f rawvideo -pix_fmt rgba -s {_renderer!.Width}x{_renderer.Height} -r 30 -i - -vf vflip,scale=960:-2 -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart \"{_captureOut}\"")
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        _reelEncoder = Process.Start(psi);
    }

    private static void OnResize(Vector2D<int> size)
    {
        if (_window == null) return;
        _renderer?.Resize(_window.FramebufferSize.X, _window.FramebufferSize.Y);
    }

    private static void OnRender(double _)
    {
        if (_game == null || _renderer == null) return;
        double now = _clock.Elapsed.TotalSeconds;
        if (_mode != "capture" && _lastFrameEnd > 0)
        {
            double target = 1.0 / Math.Clamp(GameSettings.Current.FrameRateCap, 30, 480);
            if (now - _lastFrameEnd < target)
            {
                WaitUntil(_clock, _lastFrameEnd + target);
                now = _clock.Elapsed.TotalSeconds;
            }
        }
        _lastFrameEnd = now;
        float dt = (float)Math.Min(0.08, Math.Max(0.0005, now - _lastTime));
        _lastTime = now;
        _frame++;

        if (_mode == "capture" && Environment.GetEnvironmentVariable("NULLWAVE_BENCH") == "1")
        {
            // uncapped GPU timing: render 240 frames with a full pipeline flush each frame
            var sw = Stopwatch.StartNew();
            double worst = 0;
            for (int i = 0; i < 240; i++)
            {
                double t0 = sw.Elapsed.TotalMilliseconds;
                _game.Update(1f / 60f);
                _game.Render();
                _gl!.Finish();
                worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds - t0);
            }
            Console.WriteLine($"BENCH | {sw.Elapsed.TotalMilliseconds / 240:0.00} ms/frame avg, worst {worst:0.00} ms | {_renderer.BackdropName}");
            _window!.Close();
            return;
        }

        if (_mode == "capture" && _reelSeconds > 0f)
        {
            // reel: stream the capture to an mp4 at 30 fps of simulated time
            _game.Update(1f / 60f);
            _game.Render();
            if (_frame % 2 == 0)
            {
                if (_reelEncoder == null) StartReel();
                _frameBuffer ??= new byte[_renderer.Width * _renderer.Height * 4];
                _renderer.ReadPixelsInto(_frameBuffer);
                _reelEncoder!.StandardInput.BaseStream.Write(_frameBuffer, 0, _frameBuffer.Length);
                _reelFrames++;
            }
            if (_reelFrames >= (int)(_reelSeconds * 30f))
            {
                _reelEncoder!.StandardInput.Close();
                _reelEncoder.WaitForExit();
                Console.WriteLine($"REEL | {_captureOut} frames={_reelFrames} backdrop={_renderer.BackdropName}");
                _window!.Close();
            }
            return;
        }

        if (_mode == "capture")
        {
            _game.Update(1f / 60f);
            _game.Render();
            if (_frame >= _captureTargetFrames && !_saved)
            {
                _saved = true;
                _renderer.SaveScreenshot(_captureOut);
                Console.WriteLine($"CAPTURE | {_captureScenario} -> {_captureOut} frame={_frame} size={_renderer.Width}x{_renderer.Height}");
                Console.WriteLine($"BACKDROP | {_renderer.BackdropName}");
                var dir = _game.Director;
                Console.WriteLine($"MOOD | intensity={dir.Intensity:0.00} build={dir.Build:0.00} breakdown={dir.Breakdown:0.00} drop={dir.DropPulse:0.00} hue={dir.MoodHue:0.00} kick={dir.Kick:0.00} worldFlash={(_game.ActiveWorld ?? _game.DemoWorld)?.Flash:0.00}");
                _window!.Close();
            }
            return;
        }

        if (_mode == "record")
        {
            RecordTick();
            return;
        }

        _game.Update(dt);
        _game.Render();
#if DEBUG
        // backbuffer is complete here; Silk.NET swaps right after this callback returns
        _bridge?.OnRender(_game, _renderer);
#endif

        if (_mode == "smoke" && now >= _smokeSeconds)
        {
            var w = _game.ActiveWorld;
            Console.WriteLine($"SMOKE | {now:0.0}s fps={_game.Fps:0} wave={w?.Wave} score={w?.Score} enemies={w?.Enemies.Count} bullets={w?.EnemyBullets.Count} bpm={_game.Audio.State.Bpm:0.0} audio={_game.Audio.State.SourceName} capture={_game.Audio.CaptureAvailable}");
            CheckRenderState();
            _window!.Close();
        }
    }

    /// <summary>
    /// End-of-smoke render hygiene check: GL state after Render() must be the baseline, the backbuffer
    /// must be opaque, and rendering the same frame a second time (no Update in between) must not
    /// pick up anything from the first pass (the old recorder "ghost HUD" path).
    /// </summary>
    private static void CheckRenderState()
    {
        if (_game == null || _renderer == null) return;
        bool stateOk = _renderer.VerifyBaselineState(out string state);
        var first = new byte[_renderer.Width * _renderer.Height * 4];
        var second = new byte[first.Length];
        _renderer.ReadPixelsInto(first);
        _game.Render();
        bool secondOk = _renderer.VerifyBaselineState(out string state2);
        _renderer.ReadPixelsInto(second);
        int alphaMin = 255;
        long diff = 0;
        for (int i = 0; i < first.Length; i += 4)
        {
            alphaMin = Math.Min(alphaMin, Math.Min(first[i + 3], second[i + 3]));
            if (first[i] != second[i] || first[i + 1] != second[i + 1] || first[i + 2] != second[i + 2]) diff++;
        }
        double diffFrac = diff / (double)(first.Length / 4);
        bool ok = stateOk && secondOk && alphaMin == 255;
        Console.WriteLine($"RENDERSTATE | after1={state} after2={state2} alphaMin={alphaMin} rerenderDiff={diffFrac:P3} -> {(ok ? "PASS" : "FAIL")}");
        if (!ok) Environment.ExitCode = 1;
    }

    private static void StartEncoder()
    {
        if (_renderer == null) return;
        int w = _renderer.Width;
        int h = _renderer.Height;
        string? dir = Path.GetDirectoryName(Path.GetFullPath(_recordVideo));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
        };
        foreach (string arg in new[]
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "rawvideo", "-pixel_format", "rgba", "-video_size", $"{w}x{h}",
            "-framerate", _recordFps.ToString("0"), "-i", "pipe:0",
            "-vf", "vflip",
            "-c:v", "h264_nvenc", "-preset", "p4", "-rc", "vbr", "-cq", "21", "-b:v", "0",
            "-pix_fmt", "yuv420p", "-movflags", "+faststart",
            _recordVideo,
        }) psi.ArgumentList.Add(arg);
        try
        {
            _ffmpeg = Process.Start(psi);
            Console.WriteLine($"RECORD | encoder {w}x{h}@{_recordFps:0} -> {_recordVideo}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RECORD | encoder failed: {ex.Message}");
        }
    }

    private static void RecordTick()
    {
        if (_game == null || _renderer == null || _ffmpeg == null) return;
        if (_frameBuffer == null || _frameBuffer.Length != _renderer.Width * _renderer.Height * 4)
            _frameBuffer = new byte[_renderer.Width * _renderer.Height * 4];

        if (!_recordArmed)
        {
            _game.Update(1f / 60f);
            _game.Render();
            _recordWait += 1f / 60f;
            Thread.Sleep(2);
            if (_game.Audio.State.External || _recordWait > 45f)
            {
                _recordArmed = true;
                _recordStart = _clock.Elapsed.TotalSeconds;
                _framesWritten = 0;
                bool tap = _game.Audio.StartRecording(_recordWav);
                Console.WriteLine($"RECORD | armed wait={_recordWait:0.0}s external={_game.Audio.State.External} bpm={_game.Audio.State.Bpm:0.0} source={_game.Audio.State.SourceName} tap={tap} err={_game.Audio.LastError}");
            }
            return;
        }

        double elapsed = _clock.Elapsed.TotalSeconds - _recordStart;
        long target = (long)(elapsed * _recordFps);
        int pending = (int)Math.Min(3, target - _framesWritten);
        if (pending <= 0)
        {
            Thread.Sleep(1);
            return;
        }

        var stdin = _ffmpeg.StandardInput.BaseStream;
        _game.Update(1f / 60f);
        _game.Render();
        _renderer.ReadPixelsInto(_frameBuffer);
        for (int i = 0; i < pending; i++)
        {
            stdin.Write(_frameBuffer, 0, _frameBuffer.Length);
            _framesWritten++;
        }

        if (elapsed >= _recordSeconds)
        {
            stdin.Flush();
            _ffmpeg.StandardInput.Close();
            _ffmpeg.WaitForExit(20000);
            _game.Audio.StopRecording();
            Console.WriteLine($"RECORD | done frames={_framesWritten} seconds={elapsed:0.0} external={_game.Audio.State.External} bpm={_game.Audio.State.Bpm:0.0} wave={_game.ActiveWorld?.Wave} score={_game.ActiveWorld?.Score} audio={_game.Audio.State.SourceName}");
            _window!.Close();
        }
    }

#if DEBUG
    private static void OnUpdate(double dt) => _bridge?.OnUpdate(dt);
#endif

    private static void OnClosing()
    {
        if (_timerRaised)
        {
            try { timeEndPeriod(1); } catch { }
            _timerRaised = false;
        }
#if DEBUG
        _bridge?.Dispose();
#endif
        _game?.Dispose();
        _renderer?.Dispose();
        _input?.Dispose();
    }
}
