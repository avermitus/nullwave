using System.Diagnostics;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.Render;
using Nullwave.Tools;
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

    private static void Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--selftest") { _mode = "selftest"; }
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
            else if (a == "--list-apps") { _mode = "listapps"; }
            else if (a == "--beatbench") { _mode = "beatbench"; }
            else if (a == "--style" && i + 1 < args.Length && int.TryParse(args[i + 1], out int st)) { _forceStyle = st; i++; }
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
        if (args.Contains("--sfxdump"))
        {
            string dir = Path.Combine("capture", "sfx");
            Directory.CreateDirectory(dir);
            foreach (SfxType type in Enum.GetValues<SfxType>())
            {
                var (peak, rms, finite) = SfxProbe.Render(type, Path.Combine(dir, type.ToString().ToLowerInvariant() + ".wav"));
                Console.WriteLine($"  {type,-10} peak {peak:0.000} rms {rms:0.0000} {(finite ? "" : "NON-FINITE")}");
            }
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
        _window.Run();
    }

    private static void OnLoad()
    {
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
                "roster" => 40,
                "songlasers" => 150,
                "attune" => 1,
                "pause" => 1,
                "gameover" => 2,
                _ => 180,
            };
            _game.CaptureWarmup(warmup);
            if (_forceStyle >= 0) _renderer.ForceBackdrop(_forceStyle, 1234 + _forceStyle);
        }
        if (_mode == "smoke") _game.StartSmokeRun(0x5A1E5EEDUL);
        if (_mode == "smoke" && _forceStyle >= 0) _renderer.ForceBackdrop(_forceStyle, 77 + _forceStyle);
        if (_mode == "record")
        {
            _game.StartSmokeRun(0x5A1E5EEDUL);
            if (_recordWave > 1) _game.ActiveWorld?.BeginWave(_recordWave);
            StartEncoder();
        }
        Console.WriteLine($"AUDIO | capture={_game.Audio.CaptureAvailable} output={_game.Audio.OutputAvailable} device={_game.Audio.State.DeviceName} error={_game.Audio.LastError}");
        _clock = Stopwatch.StartNew();
        _lastTime = 0;
        _lastFrameEnd = 0;
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
            double frameTime = now - _lastFrameEnd;
            double target = 1.0 / 120.0;
            if (frameTime < target)
            {
                double wait = (target - frameTime) * 1000.0;
                if (wait >= 1.5) Thread.Sleep((int)wait);
                else Thread.SpinWait(350);
                now = _clock.Elapsed.TotalSeconds;
            }
        }
        _lastFrameEnd = now;
        float dt = (float)Math.Min(0.08, Math.Max(0.0005, now - _lastTime));
        _lastTime = now;
        _frame++;

        if (_mode == "capture")
        {
            _game.Update(1f / 60f);
            _game.Render();
            if (_frame >= _captureTargetFrames && !_saved)
            {
                _saved = true;
                _renderer.SaveScreenshot(_captureOut);
                Console.WriteLine($"CAPTURE | {_captureScenario} -> {_captureOut} frame={_frame} size={_renderer.Width}x{_renderer.Height}");
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

        if (_mode == "smoke" && now >= _smokeSeconds)
        {
            var w = _game.ActiveWorld;
            Console.WriteLine($"SMOKE | {now:0.0}s fps={_game.Fps:0} wave={w?.Wave} score={w?.Score} enemies={w?.Enemies.Count} bullets={w?.EnemyBullets.Count} bpm={_game.Audio.State.Bpm:0.0} audio={_game.Audio.State.SourceName} capture={_game.Audio.CaptureAvailable}");
            _window!.Close();
        }
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

    private static void OnClosing()
    {
        _game?.Dispose();
        _renderer?.Dispose();
        _input?.Dispose();
    }
}
