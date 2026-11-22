using System.Numerics;
using Nullwave.Audio;
using Nullwave.Core;
using Nullwave.Util;
using Silk.NET.OpenGL;

namespace Nullwave.Render;

public sealed class Renderer : IDisposable
{
    public const int DesignWidth = 1600;
    public const int DesignHeight = 900;

    private readonly GL _gl;
    private readonly Shader _worldShader;
    private readonly Shader _uiShader;
    private readonly Shader _background;
    private readonly Batch _worldAlpha;
    private readonly Batch _worldGlow;
    private readonly Batch _uiAlpha;
    private readonly Batch _uiGlow;
    private readonly Shader _meshShader;
    private readonly AtlasAssets _atlas;
    private readonly MeshBatch _backdropMeshes;
    private readonly MeshBatch _worldMeshes;
    private Matrix4x4 _backdropViewProj = Matrix4x4.Identity;
    private Vector3 _backdropEye = new(0, 0, 1000);
    private Vector3 _fogColor = new(0.01f, 0.012f, 0.03f);

    /// <summary>Screen-space lift per unit of height for the world mesh pass (pseudo 3/4 view).</summary>
    public const float Oblique = 0.42f;
    private readonly uint _fullscreenVao;
    private readonly uint _emptyVao;
    private GlFbo _sceneA;
    private GlFbo _sceneB;
    private bool _useA = true;
    private Vector2 _camera;
    private Vector2 _view;
    private float _zoom = 1f;
    private float _flow;
    private float _lastRenderTime = -1f;
    public PostFx Post { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public Palette Palette { get; set; } = Palette.FromSeed(1);
    public Vector2 Camera => _camera;
    public Vector2 ViewExtents => _view;
    public float Zoom => _zoom;
    public float EmissiveIntensity { get; set; } = 2.4f;
    public MeshBatch Backdrop => _backdropMeshes;
    public MeshBatch Meshes => _worldMeshes;
    public float BackdropBrightness { get; set; } = 0.5f;
    /// <summary>Song-structure drivers (set by the game each frame); null = neutral.</summary>
    public MusicDirector? Mood { get; set; }
    /// <summary>Player speed 0..1 and screen-space direction, so the backdrop rushes with the ship.</summary>
    public float PlayerSpeed { get; set; }
    public Vector2 PlayerDirection { get; set; }
    /// <summary>Flow state 0..1: pushes speed lines and backdrop rush.</summary>
    public float Flow { get; set; }
    /// <summary>Performance hype 0..1 (set by the game): scales every music-driven effect.</summary>
    public float Hype { get; set; } = 0.5f;
    /// <summary>Effect multiplier from hype: 0.55x when struggling, up to 1.45x when on fire.</summary>
    public float HypeScale => 0.55f + Hype * 0.9f;
    /// <summary>Smoothed normalized aim/cursor offset in [-1, 1] driving 3D backdrop parallax and subtle oblique tilt.</summary>
    public Vector2 AimTilt { get; set; }
    public Vector2 ObliqueVec => ComputeObliqueVec(AimTilt);

    public const int MaxBackdropLights = 12;
    public const int MaxScreenShockwaves = 4;

    private static readonly string[] LightPosNames =
    [
        "uLights[0]", "uLights[1]", "uLights[2]", "uLights[3]",
        "uLights[4]", "uLights[5]", "uLights[6]", "uLights[7]",
        "uLights[8]", "uLights[9]", "uLights[10]", "uLights[11]",
    ];

    private static readonly string[] LightColNames =
    [
        "uLightCols[0]", "uLightCols[1]", "uLightCols[2]", "uLightCols[3]",
        "uLightCols[4]", "uLightCols[5]", "uLightCols[6]", "uLightCols[7]",
        "uLightCols[8]", "uLightCols[9]", "uLightCols[10]", "uLightCols[11]",
    ];

    private readonly Vector4[] _lightData = new Vector4[MaxBackdropLights];
    private readonly Vector3[] _lightCols = new Vector3[MaxBackdropLights];
    private int _lightCount;
    private readonly Vector4[] _screenShockwaves = new Vector4[MaxScreenShockwaves];
    private int _screenShockwaveCount;

    public const int MaxBackdropFocus = 16;
    private static readonly string[] FocusNames = Enumerable.Range(0, MaxBackdropFocus).Select(i => $"uFocus[{i}]").ToArray();
    private static readonly string[] SpecNames = Enumerable.Range(0, 16).Select(i => $"uSpec[{i}]").ToArray();
    private readonly Vector4[] _focus = new Vector4[MaxBackdropFocus];
    private int _focusCount;
    private readonly float[] _spec = new float[16];
    private readonly float[] _specAvg = new float[16];
    public int BackdropFocusCount => _focusCount;
    /// <summary>Last 16-band spectrum sent to the backdrop (diagnostics / tests).</summary>
    public IReadOnlyList<float> BackdropSpectrum => _spec;

    /// <summary>
    /// Darkens the backdrop in a soft pool around a map position (the ship, enemies, the boss) so
    /// what you fight always sits on a calm, dark patch of the world. Strength 0..1.
    /// </summary>
    public void AddBackdropFocus(Vector2 worldPos, float worldRadius, float strength)
    {
        if (_focusCount >= MaxBackdropFocus || !float.IsFinite(strength) || strength <= 0.01f) return;
        var f = ProjectBackdropLight(worldPos, worldRadius, 1f, _camera, _view);
        _focus[_focusCount++] = new Vector4(f.X, f.Y, f.Z, Math.Clamp(strength, 0f, 0.95f));
    }

    /// <summary>
    /// 48 spectrum bars -> 16 bands, each mixed between its level and how far it sits above its own
    /// recent average, so every band moves the world (not just the always-loud bass).
    /// </summary>
    public static void ComputeBackdropSpectrum(float[] bars, float[] avg, float[] output, float dt)
    {
        for (int b = 0; b < 16; b++)
        {
            float v = 0f;
            for (int k = 0; k < 3; k++)
            {
                int i = b * 3 + k;
                float x = i < bars.Length && float.IsFinite(bars[i]) ? bars[i] : 0f;
                v += x;
            }
            v /= 3f;
            avg[b] = M.ExpSmooth(avg[b], v, 0.8f, dt);
            float target = Math.Clamp(v * 0.55f + (v - avg[b]) * 2.2f, 0f, 1.2f);
            // instant attack, quick release: the world snaps to each hit and settles between them
            output[b] = target > output[b] ? target : M.ExpSmooth(output[b], target, 9f, dt);
        }
    }

    private float _chordShift;
    private double _dropBeat = -1e6;
    private float _rollSign = 1f;
    private float _snareRoll;
    private float _letterbox;
    private float _screenRoll;
    /// <summary>This frame's EDM show values (diagnostics and tests).</summary>
    public EdmFx LastEdmFx { get; private set; }
    /// <summary>Frames rendered so far (captures start their scripts on the first one).</summary>
    public long RenderedFrames { get; private set; }

    /// <summary>What the EDM show does this frame: kaleidoscope, camera lift and dive, kick dip, hue steps,
    /// pump and strobe; camera roll and letterbox in the composite.</summary>
    public readonly record struct EdmFx(float Kaleido, float Vertigo, float Dive, float CamKick,
        float HueStep, float Pump, float Strobe, float Roll, float Letterbox);

    /// <summary>
    /// The EDM show for a frame. <paramref name="dropBeats"/> is beats since the last drop. Build-ups fold the
    /// world into a kaleidoscope, lift the camera away and close letterbox bars; the drop slams the camera
    /// down, pulses its first bar on the beat, steps the hue every beat and pumps for four bars.
    /// musicFx scales the show, camFx the camera moves; reduceFlashing removes the strobe and hue steps
    /// and halves the rest.
    /// </summary>
    public static EdmFx EdmFxFor(float dropBeats, double beatPos, float build,
        float overdrive, float snareRoll, float musicFx, float camFx, bool reduceFlashing)
    {
        static float Fin(float v, float fallback) => float.IsFinite(v) ? v : fallback;
        static float Ss(float e0, float e1, float x)
        {
            float u = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
            return u * u * (3f - 2f * u);
        }
        // above 100 % the moves and the pump grow; the strobe and hue steps stay at their 100 % level
        float show = Math.Clamp(Fin(musicFx, 0f), 0f, 3f), showC = MathF.Min(1f, show);
        float cam = Math.Clamp(Fin(camFx, 0f), 0f, 3f), camC = MathF.Min(1f, cam);
        float since = Math.Clamp(Fin(dropBeats, 1000f), 0f, 1000f);
        if (!double.IsFinite(beatPos)) beatPos = 0.0;
        float window = since < 16f ? 1f - since / 16f : 0f;   // the drop's first four bars
        float b = Math.Clamp(Fin(build, 0f), 0f, 1f);
        float od = Math.Clamp(Fin(overdrive, 0f), 0f, 1f);
        float safe = reduceFlashing ? 0.5f : 1f;

        float kaleido = Ss(0.25f, 0.85f, b) * 0.85f * showC * safe;
        float vertigo = Ss(0.1f, 1f, b) * cam;
        float dive = 0.42f * MathF.Exp(-since * 1.1f) * cam;
        float camKick = 0.07f * cam;
        double step = Math.Floor(beatPos) * 0.13;
        float hue = !reduceFlashing && window > 0f ? (float)(step - Math.Floor(step)) * showC : 0f;
        float pump = (window * 0.9f + od * 0.3f) * MathF.Min(show, 2f) * safe;
        // a tinted pulse on each beat of the drop's first bar: at most four, and under 3 a second even
        // at 174 BPM (photosensitivity guidance), never a full white-out
        double frac = beatPos - Math.Floor(beatPos);
        float strobe = !reduceFlashing && since < 4f && frac < 0.12 ? 0.3f * showC * (1f - since / 4f) : 0f;
        // the drop rocks the frame left and right on the beat, settling over a couple of bars
        float roll = (0.05f * MathF.Exp(-since * 0.8f) * MathF.Cos(since * MathF.PI) + Fin(snareRoll, 0f)) * cam * safe;
        float letterbox = Ss(0.2f, 0.9f, b) * 0.06f * camC;
        return new EdmFx(kaleido, vertigo, dive, camKick, hue, pump, strobe, roll, letterbox);
    }

    /// <summary>Where the composite samples the scene for a screen point (0..1, y down): undoes the camera roll.</summary>
    public static Vector2 UnrollScreen(Vector2 n, float roll, float aspect)
    {
        if (!float.IsFinite(roll) || MathF.Abs(roll) < 1e-6f) return n;
        float cx = (n.X - 0.5f) * aspect, cy = 0.5f - n.Y;
        float cr = MathF.Cos(roll), sr = MathF.Sin(roll), k = 1f / EdgeFit(roll, aspect);
        float rx = (cr * cx + sr * cy) * k, ry = (-sr * cx + cr * cy) * k;
        return new Vector2(rx / aspect + 0.5f, 0.5f - ry);
    }

    /// <summary>How far a rolled frame is scaled up so its corners stay inside the picture (exact for any aspect).</summary>
    private static float EdgeFit(float roll, float aspect)
    {
        float r = MathF.Abs(roll);
        return MathF.Cos(r) + MathF.Sin(r) * MathF.Max(aspect, 1f / MathF.Max(aspect, 1e-3f));
    }

    /// <summary>The inverse of <see cref="UnrollScreen"/>: where a point of the scene shows up on screen.</summary>
    public static Vector2 RollScreen(Vector2 n, float roll, float aspect)
    {
        if (!float.IsFinite(roll) || MathF.Abs(roll) < 1e-6f) return n;
        float cx = (n.X - 0.5f) * aspect, cy = 0.5f - n.Y;
        float cr = MathF.Cos(roll), sr = MathF.Sin(roll), k = EdgeFit(roll, aspect);
        float rx = (cr * cx - sr * cy) * k, ry = (sr * cx + cr * cy) * k;
        return new Vector2(rx / aspect + 0.5f, 0.5f - ry);
    }
    private readonly SectionWorldMemory _sections = new();
    public int SectionWorldCount => _sections.Count;
    public BackdropGenome CurrentBackdrop => _genB ?? _genA;

    /// <summary>
    /// Palette phase shift for the chord being played, relative to the song's key, laid out on the
    /// circle of fifths: the home chord is the world's own colours, neighbouring chords tint it a
    /// little, distant chords more. Minor chords lean cooler.
    /// </summary>
    public static float ChordShiftFor(int chordRoot, bool minor, int key)
    {
        if (chordRoot < 0) return 0f;
        int keyRoot = key >= 0 ? key % 12 : 0;
        int rel = ((chordRoot - keyRoot) % 12 + 12) % 12;
        int fifths = (rel * 7) % 12;                  // position on the circle of fifths
        if (fifths > 6) fifths -= 12;                 // -5..6, home = 0
        return fifths / 12f * 0.32f + (minor ? 0.05f : 0f);
    }

    /// <summary>
    /// A new song section starts. A section heard before brings back the world it had; a new one
    /// gets a brand-new world. Memory is per song (<paramref name="songEpoch"/>).
    /// </summary>
    public void ShowSection(int label, bool repeat, int songEpoch)
    {
        var current = _genB ?? _genA;
        var next = _sections.Choose(label, repeat, songEpoch, current, () => BackdropGenome.Random(_genRng, current));
        if (next != current) TransitionTo(next);
    }

    private void TransitionTo(BackdropGenome g)
    {
        if (_genB != null) _genA = _genB;
        _genB = g;
        _mix = 0f;
    }

    public const int MaxBackdropWaves = 6;
    private static readonly string[] WaveNames =
        ["uWaves[0]", "uWaves[1]", "uWaves[2]", "uWaves[3]", "uWaves[4]", "uWaves[5]"];
    // world x, world y, birth time, strength
    private readonly Vector4[] _waves = new Vector4[MaxBackdropWaves];
    private int _waveCount;
    private long _lastBeatIndex = long.MinValue;
    private float _lastDrop;
    public int BackdropWaveCount => _waveCount;

    /// <summary>
    /// Sends a shockwave through the 3D backdrop world from a map position: it rolls outward
    /// through the geometry (towers lift, the sea swells, cubes are tossed) and stays anchored
    /// to where it started.
    /// </summary>
    public void PulseBackdrop(Vector2 worldPos, float strength)
    {
        if (!float.IsFinite(strength) || strength <= 0.02f || !float.IsFinite(worldPos.X) || !float.IsFinite(worldPos.Y)) return;
        float now = MathF.Max(0f, _lastRenderTime);
        int slot = _waveCount;
        if (_waveCount >= MaxBackdropWaves)
        {
            // replace the oldest
            slot = 0;
            for (int i = 1; i < _waveCount; i++)
                if (_waves[i].Z < _waves[slot].Z) slot = i;
        }
        else _waveCount++;
        _waves[slot] = new Vector4(worldPos.X, worldPos.Y, now, Math.Clamp(strength, 0f, 2f));
    }

    public int BackdropLightCount => _lightCount;
    public int ScreenShockwaveCount => _screenShockwaveCount;
    /// <summary>Live Drop/Sync Overdrive intensity (0..1) amplifying reactive backdrop surges.</summary>
    public float Overdrive { get; set; }

    public static Vector4 ProjectScreenShockwave(
        Vector2 worldPos, float radiusWorld, float strength,
        Vector2 camera, Vector2 view)
    {
        float vx = MathF.Max(1f, float.IsFinite(view.X) ? view.X : 800f);
        float vy = MathF.Max(1f, float.IsFinite(view.Y) ? view.Y : 450f);
        float dx = float.IsFinite(worldPos.X) && float.IsFinite(camera.X) ? worldPos.X - camera.X : 0f;
        float dy = float.IsFinite(worldPos.Y) && float.IsFinite(camera.Y) ? worldPos.Y - camera.Y : 0f;
        float r = float.IsFinite(radiusWorld) ? MathF.Max(0f, radiusWorld) : 0f;
        float w = float.IsFinite(strength) ? Math.Clamp(strength, 0f, 1.5f) : 0f;
        return new Vector4(
            0.5f + dx / (2f * vx),
            0.5f - dy / (2f * vy),
            r / (2f * vy),
            w);
    }

    public static float EffectiveShockwaveStrength(float strength, bool reduceFlashing, int screenShakePercent)
    {
        if (reduceFlashing || screenShakePercent <= 0 || !float.IsFinite(strength) || strength <= 0f) return 0f;
        return Math.Clamp(strength * (Math.Clamp(screenShakePercent, 0, GameSettings.MaxScreenShake) / 100f), 0f, 2.5f);
    }

    public void AddScreenShockwave(Vector2 worldPos, float radiusWorld, float strength)
    {
        if (_screenShockwaveCount >= MaxScreenShockwaves) return;
        var s = GameSettings.Current;
        float eff = EffectiveShockwaveStrength(strength, s.ReduceFlashing, s.ScreenShake);
        if (eff <= 0.005f) return;
        _screenShockwaves[_screenShockwaveCount++] = ProjectScreenShockwave(worldPos, radiusWorld, eff, _camera, _view);
    }

    public static Vector4 ProjectBackdropLight(
        Vector2 worldPos, float worldRadius, float intensity,
        Vector2 camera, Vector2 view)
    {
        float denom = 2f * MathF.Max(1f, float.IsFinite(view.Y) ? view.Y : 450f);
        float dx = float.IsFinite(worldPos.X) && float.IsFinite(camera.X) ? worldPos.X - camera.X : 0f;
        float dy = float.IsFinite(worldPos.Y) && float.IsFinite(camera.Y) ? worldPos.Y - camera.Y : 0f;
        float r = float.IsFinite(worldRadius) ? MathF.Max(0f, worldRadius) : 0f;
        float w = float.IsFinite(intensity) ? Math.Clamp(intensity, 0f, 3f) : 0f;
        return new Vector4(
            dx / denom,
            -dy / denom,
            MathF.Max(0.02f, r / denom),
            w);
    }

    public void AddBackdropLight(Vector2 worldPos, float worldRadius, Vector3 color, float intensity)
    {
        if (_lightCount >= MaxBackdropLights || !float.IsFinite(intensity) || intensity <= 0.01f) return;
        _lightData[_lightCount] = ProjectBackdropLight(worldPos, worldRadius, intensity, _camera, _view);
        _lightCols[_lightCount] = color;
        _lightCount++;
    }

    public readonly record struct BackdropAudioDrives(
        float Kick, float Snare, float Hat, float Drop, float Build, float Breakdown);

    private static float ClampDrive(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, 3.5f) : 0f;

    public static BackdropAudioDrives ComputeBackdropAudioDrives(
        AudioState audio, MusicDirector? mood, float fx, float overdrive = 0f, bool reduceFlashing = false)
    {
        float scale = float.IsFinite(fx) ? MathF.Max(0f, fx) : 0f;
        float od = float.IsFinite(overdrive) ? Math.Clamp(overdrive, 0f, 1f) : 0f;
        float damp = reduceFlashing ? 0.40f : 1f;
        float s = scale * damp;
        float kick = (mood?.Kick ?? audio.BeatPulse) * s + od * 0.28f * s;
        float snare = audio.SnarePulse * s + od * 0.22f * s;
        float hat = audio.HatPulse * s + od * 0.16f * s;
        float drop = (mood?.DropPulse ?? 0f) * s + od * 0.65f * s;
        // Preserve legacy cap of 3.0 when overdrive is 0 for existing extreme-input contract
        float cap = od > 0f ? 3.5f : 3f;
        float Clamp(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, cap) : 0f;
        return new BackdropAudioDrives(
            Kick: Clamp(kick),
            Snare: Clamp(snare),
            Hat: Clamp(hat),
            Drop: Clamp(drop),
            Build: Clamp((mood?.Build ?? 0f) * s),
            Breakdown: Clamp((mood?.Breakdown ?? 0f) * scale));
    }

    public static Vector2 ClampAimTilt(Vector2 raw) => new(
        float.IsFinite(raw.X) ? Math.Clamp(raw.X, -1f, 1f) : 0f,
        float.IsFinite(raw.Y) ? Math.Clamp(raw.Y, -1f, 1f) : 0f);

    public static Vector2 ComputeObliqueVec(Vector2 aimTilt)
    {
        aimTilt = ClampAimTilt(aimTilt);
        return new Vector2(-aimTilt.X * 0.14f, Oblique + aimTilt.Y * 0.10f);
    }

    public static Matrix4x4 BuildWorldMeshMatrix(Vector2 camera, Vector2 view, Vector2 obliqueVec)
    {
        float ix = 1f / MathF.Max(1f, view.X);
        float iy = 1f / MathF.Max(1f, view.Y);
        return new Matrix4x4(
            ix, 0, 0, 0,
            0, -iy, 0, 0,
            obliqueVec.X * ix, obliqueVec.Y * iy, -1f / 4000f, 0,
            -camera.X * ix, camera.Y * iy, 0, 1);
    }

    public static Vector2 LiftPoint(Vector2 pos, float z, Vector2 obliqueVec) =>
        new(pos.X + z * obliqueVec.X, pos.Y - z * obliqueVec.Y);

    public Vector2 Lift(Vector2 pos, float z) => LiftPoint(pos, z, ObliqueVec);
    public BackdropGenome Backdrop0 => _genA;
    private BackdropGenome _genA = BackdropGenome.Random(new Random());
    /// <summary>Description of the backdrop currently on screen (diagnostics overlay).</summary>
    public string BackdropName => (_genB ?? _genA).Name;
    private BackdropGenome? _genB;
    private float _mix;
    private readonly Random _genRng = new();

    /// <summary>Starts a transition to a brand new random backdrop (ignored while one is running).</summary>
    public void RerollBackdrop()
    {
        if (_genB != null) return;
        _genB = BackdropGenome.Random(_genRng, _genA);
        _mix = 0f;
        _sections.Replace(_genB);
    }

    /// <summary>Pins a specific style (capture/debug).</summary>
    public void ForceBackdrop(int seed)
    {
        _genA = BackdropGenome.Random(new Random(seed));
        _genB = null;
        _mix = 0f;
    }

    /// <summary>Replaces the backdrop immediately (new run).</summary>
    public void ResetBackdrop()
    {
        _genA = BackdropGenome.Random(_genRng, _genA);
        _genB = null;
        _mix = 0f;
    }

    private void SetGenome(string suffix, BackdropGenome g)
    {
        string arr = "uL" + suffix;
        _background.SetInt("uCount" + suffix, g.Layers.Count);
        _background.SetVec4("uGlob" + suffix, g.Global);
        _background.SetInt("uAtmo" + suffix, g.Atmosphere);
        for (int i = 0; i < g.Layers.Count; i++)
        {
            var l = g.Layers[i];
            string n = arr + "[" + i + "].";
            for (int k = 0; k < 4; k++)
            {
                _background.SetInt(n + "op" + k, l.Ops[k]);
                _background.SetVec4(n + "p" + k, l.OpParams[k]);
            }
            _background.SetInt(n + "gen", l.Generator);
            _background.SetVec4(n + "ga", l.GenA);
            _background.SetVec4(n + "gb", l.GenB);
            _background.SetInt(n + "blend", l.Blend);
            _background.SetFloat(n + "gain", l.Gain);
            _background.SetVec3(n + "po", l.PaletteOffset);
            _background.SetVec3(n + "pf", l.PaletteFrequency);
        }
    }

    public Renderer(GL gl, int width, int height)
    {
        _gl = gl;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _worldShader = new Shader(_gl, Shaders.ColorVert, Shaders.ColorFrag, "world");
        _uiShader = new Shader(_gl, Shaders.UiVert, Shaders.ColorFrag, "ui");
        _background = new Shader(_gl, Shaders.FullscreenVert, Shaders.BackgroundFrag, "background");
        _worldAlpha = new Batch(_gl, 131072);
        _worldGlow = new Batch(_gl, 131072);
        _uiAlpha = new Batch(_gl, 65536);
        _uiGlow = new Batch(_gl, 32768);
        _meshShader = new Shader(_gl, Shaders.MeshVert, Shaders.MeshFrag, "mesh");
        _atlas = new AtlasAssets(_gl);
        _backdropMeshes = new MeshBatch(_gl, 196608);
        _worldMeshes = new MeshBatch(_gl, 196608);
        _fullscreenVao = _gl.GenVertexArray();
        _emptyVao = _gl.GenVertexArray();
        Post = new PostFx(_gl);
        _sceneA = new GlFbo(_gl, Width, Height, true, true);
        _sceneB = new GlFbo(_gl, Width, Height, true, true);
        _gl.Enable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        BlendMode.Alpha.Apply(_gl);
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;
        Width = width;
        Height = height;
        _sceneA.Dispose();
        _sceneB.Dispose();
        _sceneA = new GlFbo(_gl, Width, Height, true, true);
        _sceneB = new GlFbo(_gl, Width, Height, true, true);
        Post.EnsureSize(width, height);
    }

    public void BeginWorld(Vector2 camera, float zoom, float viewHeight = 900f)
    {
        _camera = camera;
        _zoom = Math.Max(0.2f, zoom);
        float halfH = viewHeight * 0.5f / _zoom;
        float halfW = halfH * Width / Math.Max(1f, Height);
        _view = new Vector2(halfW, halfH);
        _worldAlpha.Clear();
        _worldGlow.Clear();
        _worldMeshes.Clear();
        _backdropMeshes.Clear();
        _lightCount = 0;
        _screenShockwaveCount = 0;
        _focusCount = 0;
    }

    /// <summary>Perspective camera for the background 3D layer (its own space, z up).</summary>
    public void SetBackdropCamera(Vector3 eye, Vector3 target, float fovDegrees, Vector3 fogColor)
    {
        _backdropEye = eye;
        _fogColor = fogColor;
        float aspect = Width / (float)Math.Max(1, Height);
        var view = Matrix4x4.CreateLookAt(eye, target, new Vector3(0, -1, 0));
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fovDegrees * MathF.PI / 180f, aspect, 20f, 12000f);
        // Game space has y pointing down the screen, so mirror x to keep the backdrop un-flipped.
        _backdropViewProj = view * proj * Matrix4x4.CreateScale(-1f, 1f, 1f);
    }

    /// <summary>Orthographic oblique projection matching the 2D world camera exactly at z = 0.</summary>
    private Matrix4x4 WorldMeshMatrix() => BuildWorldMeshMatrix(_camera, _view, ObliqueVec);

    public void BeginUi()
    {
        _uiAlpha.Clear();
        _uiGlow.Clear();
    }

    public Vector2 ScreenToWorld(Vector2 mouse, Vector2 windowSize)
    {
        float nx = windowSize.X <= 0 ? 0.5f : mouse.X / windowSize.X;
        float ny = windowSize.Y <= 0 ? 0.5f : mouse.Y / windowSize.Y;
        // aim at what is drawn under the cursor, even while the camera is rolled
        var n = UnrollScreen(new Vector2(nx, ny), _screenRoll, Width / (float)Math.Max(1, Height));
        return _camera + new Vector2((n.X - 0.5f) * 2f * _view.X, (n.Y - 0.5f) * 2f * _view.Y);
    }

    public Vector2 WorldToUi(Vector2 world)
    {
        var n = new Vector2(
            0.5f + (world.X - _camera.X) / MathF.Max(0.001f, _view.X * 2f),
            0.5f + (world.Y - _camera.Y) / MathF.Max(0.001f, _view.Y * 2f));
        n = RollScreen(n, _screenRoll, Width / (float)Math.Max(1, Height));
        return new Vector2(n.X * DesignWidth, n.Y * DesignHeight);
    }

    public bool InView(Vector2 world, float margin = 140f)
    {
        return MathF.Abs(world.X - _camera.X) < _view.X + margin && MathF.Abs(world.Y - _camera.Y) < _view.Y + margin;
    }

    public void Render(AudioState audio, float time, float flash, float aberration, int damage)
    {
        var scene = _useA ? _sceneA : _sceneB;
        scene.Bind();
        _gl.Disable(EnableCap.Blend);

        // "Flow time" runs faster when the music is louder, so the liquid surges with the track
        // instead of jumping: every motion in the backdrop is driven by this smooth clock.
        float frameDt = _lastRenderTime < 0 ? 1f / 60f : Math.Clamp(time - _lastRenderTime, 0f, 0.1f);
        _lastRenderTime = time;
        var settings = GameSettings.Current;
        var mood = Mood;
        float fx = settings.MusicFx / 100f * HypeScale;
        float overdriveRaw = float.IsFinite(Overdrive) ? Math.Clamp(Overdrive, 0f, 1f) : 0f;
        float overdriveEff = settings.ReduceFlashing ? overdriveRaw * 0.35f : overdriveRaw;
        float motionScale = settings.BackgroundMotion / 100f * (settings.ReduceFlashing ? 0.45f : 1f);
        var drives = ComputeBackdropAudioDrives(audio, mood, fx, overdriveRaw, settings.ReduceFlashing);
        float flashDamp = settings.ReduceFlashing ? 0.40f : 1f;
        float punchKick = ClampDrive(drives.Kick * 1.35f + audio.DownPulse * 0.45f * fx * flashDamp);
        float punchSnare = ClampDrive(drives.Snare * 1.40f);
        float punchHat = ClampDrive(drives.Hat * 1.35f);
        float punchDrop = ClampDrive(drives.Drop * 1.35f);
        // breakdowns slow the liquid right down, drops and Overdrive make it surge
        _flow += frameDt * (0.38f + audio.Energy * 1.3f + (audio.HarmLow + audio.HarmMid) * 0.6f + audio.BeatPulse * 0.8f + punchDrop * 4.4f + overdriveEff * 1.8f) * (1f - drives.Breakdown * 0.65f) * settings.BackgroundMotion / 100f;

        _background.Use();
        _background.SetVec2("uResolution", Width, Height);
        _background.SetFloat("uTime", time);
        _background.SetFloat("uFlow", _flow);
        _background.SetFloat("uIntensity", settings.BackgroundIntensity / 100f * Math.Clamp(1f - drives.Breakdown * 0.35f + (mood?.DropGlow ?? 0f) * fx * 0.10f + overdriveEff * 0.05f, 0.2f, 1.05f));
        _background.SetFloat("uDrop", punchDrop);
        _background.SetFloat("uBuild", drives.Build);
        _background.SetFloat("uKick", punchKick);
        _background.SetFloat("uSnare", punchSnare);
        _background.SetFloat("uHat", punchHat);
        _background.SetFloat("uOverdrive", overdriveEff);
        _background.SetFloat("uMotionScale", motionScale);
        float anticipation = float.IsFinite(audio.DropAnticipation) ? Math.Clamp(audio.DropAnticipation, 0f, 1f) : 0f;
        _background.SetFloat("uAnticipation", settings.ReduceFlashing ? anticipation * 0.35f : anticipation);
        _background.SetFloat("uBass", audio.Bass);
        _background.SetFloat("uLowMid", audio.LowMid);
        _background.SetFloat("uMid", audio.Mid);
        _background.SetFloat("uHigh", audio.High);
        _background.SetFloat("uEnergy", audio.Energy);
        _background.SetFloat("uBeat", audio.BeatPulse);
        _background.SetFloat("uDown", audio.DownPulse);
        _background.SetFloat("uSeed", Palette.BaseHue * 31.7f);
        _background.SetVec3("uColA", Palette.BackA);
        _background.SetVec3("uColB", Palette.BackB);
        _background.SetVec3("uColC", Palette.BackC);
        _background.SetVec3("uPrim", new Vector3(Palette.Primary.X, Palette.Primary.Y, Palette.Primary.Z));
        _background.SetVec3("uSec", new Vector3(Palette.Secondary.X, Palette.Secondary.Y, Palette.Secondary.Z));
        float darkTheme = settings.IsAbyssal ? 1f : 0f;
        _background.SetFloat("uDarkTheme", darkTheme);
        _background.SetInt("uLightCount", _lightCount);
        for (int i = 0; i < _lightCount; i++)
        {
            _background.SetVec4(LightPosNames[i], _lightData[i]);
            _background.SetVec3(LightColNames[i], _lightCols[i]);
        }
        if (_genB != null)
        {
            _mix += frameDt / 1.8f;
            if (_mix >= 1f) { _genA = _genB; _genB = null; _mix = 0f; }
        }
        SetGenome("A", _genA);
        SetGenome("B", _genB ?? _genA);
        _background.SetFloat("uMix", _genB != null ? _mix : 0f);

        // map-anchored camera: the 3D world is seen through the same camera as the fight
        _background.SetVec2("uCam", _camera);
        _background.SetVec2("uView", _view);
        _background.SetFloat("uOblique", Oblique);
        double beatPos = double.IsFinite(audio.BeatPosition) ? audio.BeatPosition : 0.0;
        _background.SetFloat("uBeatPos", (float)(((beatPos % 256.0) + 256.0) % 256.0));
        _background.SetFloat("uBeatLock", audio.BeatLocked || !audio.External ? 1f : 0.3f);

        // every beat sends a wave through the world from wherever you are; downbeats and drops hit harder
        long beatIndex = (long)Math.Floor(beatPos);
        if (beatIndex != _lastBeatIndex)
        {
            if (_lastBeatIndex != long.MinValue && beatIndex > _lastBeatIndex && motionScale > 0f)
            {
                bool down = ((beatIndex - audio.DownbeatOffset) % 4 + 4) % 4 == 0;
                PulseBackdrop(_camera, (down ? 0.85f : 0.4f) * Math.Clamp(fx, 0.3f, 1.4f) * (0.6f + audio.Energy * 0.6f));
            }
            _lastBeatIndex = beatIndex;
        }
        if (beatPos < _dropBeat) _dropBeat = -1e6;   // the beat clock restarted (new track)
        if (punchDrop > 0.9f && _lastDrop <= 0.9f)
        {
            PulseBackdrop(_camera, 1.6f);
            _dropBeat = beatPos;
            _rollSign = -_rollSign;
        }
        _lastDrop = punchDrop;
        // the EDM show: lasers with the loudness; kaleidoscope, lift-away and letterbox through builds;
        // a slam, strobe, hue steps and pump on drops; snares nudge the roll left and right
        if (audio.SnareHit) _snareRoll = _snareRoll > 0f ? -0.01f : 0.01f;
        _snareRoll *= MathF.Exp(-frameDt * 6f);
        var edm = EdmFxFor((float)Math.Min(1000.0, beatPos - _dropBeat), beatPos, mood?.Build ?? 0f,
            overdriveEff, _snareRoll, fx, settings.CameraFx / 100f, settings.ReduceFlashing);
        LastEdmFx = edm;
        int live = 0;
        for (int i = 0; i < _waveCount; i++)
        {
            float age = time - _waves[i].Z;
            if (age > 3.5f || age < -1f) continue;
            _waves[live] = _waves[i];
            _background.SetVec4(WaveNames[live], _waves[i].X * 0.01f, _waves[i].Y * 0.01f, MathF.Max(0f, age), _waves[i].W * motionScale);
            live++;
        }
        _waveCount = live;
        _background.SetInt("uWaveCount", live);

        // the music's spectrum shapes the world; focus pools keep the fight readable
        ComputeBackdropSpectrum(audio.Spectrum, _specAvg, _spec, frameDt);
        float specScale = Math.Clamp(0.6f + fx * 0.5f, 0.4f, 1.4f);
        for (int i = 0; i < 16; i++) _background.SetFloat(SpecNames[i], _spec[i] * specScale);
        float musicFx = Math.Clamp(fx, 0f, 1.4f);
        _background.SetVec3("uPerc", new Vector3(audio.PercLow, audio.PercMid, audio.PercHigh) * musicFx);
        _background.SetVec3("uHarm", new Vector3(audio.HarmLow, audio.HarmMid, audio.HarmHigh) * MathF.Min(1f, 0.4f + musicFx * 0.6f));
        float chordTarget = ChordShiftFor(audio.ChordRoot, audio.ChordMinor, audio.Key) * MathF.Min(1f, musicFx);
        _chordShift = M.ExpSmooth(_chordShift, chordTarget, 3.5f, frameDt);
        _background.SetFloat("uChordShift", _chordShift);
        _background.SetFloat("uBright", audio.Brightness);
        _background.SetFloat("uKaleido", edm.Kaleido);
        _background.SetFloat("uVertigo", edm.Vertigo);
        _background.SetFloat("uDive", edm.Dive);
        _background.SetFloat("uCamKick", edm.CamKick);
        _background.SetFloat("uHueStep", edm.HueStep);
        _background.SetFloat("uPump", edm.Pump);
        _background.SetFloat("uStrobe", edm.Strobe);
        _background.SetInt("uFocusCount", _focusCount);
        for (int i = 0; i < _focusCount; i++) _background.SetVec4(FocusNames[i], _focus[i]);
        _gl.BindVertexArray(_fullscreenVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        if (_backdropMeshes.Count > 0)
        {
            _gl.Clear(ClearBufferMask.DepthBufferBit);
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Less);
            _meshShader.Use();
            _atlas.Bind(_meshShader, EmissiveIntensity);
            _meshShader.SetMatrix("uViewProj", _backdropViewProj);
            _meshShader.SetInt("uPerspective", 1);
            _meshShader.SetVec3("uEye", _backdropEye);
            _meshShader.SetVec3("uLightDir", new Vector3(-0.35f, -0.55f, 0.75f));
            _meshShader.SetVec3("uViewDir", new Vector3(0, 0.3f, 1f));
            _meshShader.SetVec3("uRimColor", new Vector3(Palette.Primary.X, Palette.Primary.Y, Palette.Primary.Z));
            _meshShader.SetVec3("uFill", Palette.BackB);
            _meshShader.SetFloat("uRim", 0.18f + audio.BeatPulse * 0.25f);
            _meshShader.SetFloat("uBrightness", BackdropBrightness);
            _meshShader.SetFloat("uGlowScale", 0.5f + audio.Energy * 0.3f);
            _meshShader.SetVec3("uFogColor", _fogColor);
            _meshShader.SetFloat("uFogNear", 1800f);
            _meshShader.SetFloat("uFogFar", 6200f);
            _meshShader.SetFloat("uDarkTheme", darkTheme);
            _backdropMeshes.Draw();
            _gl.Disable(EnableCap.DepthTest);
        }

        _worldShader.Use();
        _worldShader.SetVec2("uCam", _camera);
        _worldShader.SetVec2("uView", _view);
        _worldAlpha.Draw();

        if (_worldMeshes.Count > 0)
        {
            _gl.Clear(ClearBufferMask.DepthBufferBit);
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Less);
            _meshShader.Use();
            _atlas.Bind(_meshShader, EmissiveIntensity);
            _meshShader.SetMatrix("uViewProj", WorldMeshMatrix());
            _meshShader.SetInt("uPerspective", 0);
            _meshShader.SetVec3("uLightDir", new Vector3(-0.45f, -0.6f, 0.85f));
            var ob = ObliqueVec;
            _meshShader.SetVec3("uViewDir", new Vector3(-ob.X, ob.Y, 1f));
            _meshShader.SetVec3("uRimColor", new Vector3(Palette.Accent.X, Palette.Accent.Y, Palette.Accent.Z) * 0.8f);
            _meshShader.SetVec3("uFill", Palette.BackB * 1.5f);
            _meshShader.SetFloat("uRim", 0.42f + audio.BeatPulse * 0.25f);
            _meshShader.SetFloat("uBrightness", 1f);
            _meshShader.SetFloat("uGlowScale", 1f);
            _meshShader.SetFloat("uDarkTheme", darkTheme);
            _worldMeshes.Draw();
            _gl.Disable(EnableCap.DepthTest);
            _worldShader.Use();
        }

        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        _worldGlow.Draw();

        var postMood = WithSpeed(BuildPostMood(mood, fx, settings.ReduceFlashing));
        postMood.DarkTheme = darkTheme;
        _letterbox = M.ExpSmooth(_letterbox, edm.Letterbox, edm.Letterbox > _letterbox ? 2.5f : 14f, frameDt);
        _screenRoll = edm.Roll * _rollSign;
        postMood.Roll = _screenRoll;
        postMood.Letterbox = _letterbox;
        float bloomAmt = (1.18f + audio.Energy * 0.32f + MathF.Min(drives.Drop, 1.2f) * 0.55f) * settings.Bloom / 100f;
        Post.Process(scene.Texture, Width, Height, time, bloomAmt,
            (aberration + drives.Build * 0.45f + MathF.Min(drives.Drop, 1.2f) * 0.45f) * settings.Aberration / 100f,
            settings.ReduceFlashing ? MathF.Min(flash, 0.15f) : flash, audio.BeatPulse, damage, postMood,
            _screenShockwaves.AsSpan(0, _screenShockwaveCount));

        // Post.Process leaves RenderBaseline.Default bound to the default framebuffer. The composite is
        // opaque (alpha 1); the UI blends colour normally but must not pull destination alpha below 1,
        // or screenshots/recorder frames read back translucent.
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
        _gl.Enable(EnableCap.Blend);
        _uiShader.Use();
        _uiShader.SetVec2("uSize", DesignWidth, DesignHeight);
        BlendMode.Alpha.Apply(_gl);
        _uiAlpha.Draw();
        BlendMode.Additive.Apply(_gl);
        _uiGlow.Draw();
        // exit in the same baseline PostFx guarantees, so a second Render() in one frame starts clean
        PostFx.RestoreBaseline(_gl);

        _useA = !_useA;
        RenderedFrames++;
    }

    /// <summary>Live GL state check after <see cref="Render"/> (smoke/tools only; glGet stalls the pipeline).</summary>
    public bool VerifyBaselineState(out string detail) => PostFx.VerifyBaseline(_gl, out detail);

    /// <summary>
    /// Maps the song's state to grading: breakdowns go cool, dark and desaturated with a heavy
    /// vignette; builds tighten the vignette and add contrast; drops burst saturated and wide open.
    /// </summary>
    private PostMood WithSpeed(PostMood m)
    {
        // speed lines appear past ~55% speed and roar in flow state
        m.SpeedLines = M.Clamp01((PlayerSpeed - 0.55f) * 2.2f) * (0.5f + Flow * 0.9f);
        m.SpeedDir = PlayerDirection;
        m.Thump += Flow * 0.004f * PlayerSpeed;
        return m;
    }

    private static PostMood BuildPostMood(MusicDirector? m, float fx, bool reduceFlashing)
    {
        if (m == null || fx <= 0f) return PostMood.Neutral;
        float intensity = m.Intensity, breakdown = m.Breakdown, build = m.Build, drop = m.DropPulse, glow = m.DropGlow;
        var mood = new PostMood
        {
            Hue = m.MoodHue * 0.55f * fx,
            Saturation = 1f + fx * (intensity * 0.25f - breakdown * 0.55f + drop * 0.5f + glow * 0.15f),
            Contrast = 1f + fx * (build * 0.15f + intensity * 0.08f - breakdown * 0.1f + drop * 0.1f),
            Thump = MathF.Min(0.018f, fx * (m.Kick * 0.008f + drop * 0.016f)),
            Glitch = reduceFlashing ? 0f : fx * MathF.Min(0.42f, m.Sparkle * 0.25f * intensity + drop * 0.42f),
            Vignette = Math.Clamp(1f + fx * (breakdown * 0.45f + build * 0.35f - m.Kick * 0.25f - drop * 0.6f), 0.2f, 1.8f),
            Tint = Vector3.Lerp(Vector3.One, new Vector3(0.82f, 0.9f, 1.1f), breakdown * fx * 0.8f),
        };
        mood.Saturation = MathF.Max(0.15f, mood.Saturation);
        return mood;
    }

    public void Line(Vector2 a, Vector2 b, float width, Vector4 color, bool glow = false)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 0.0001f) return;
        Vector2 n = new(-d.Y / len * width * 0.5f, d.X / len * width * 0.5f);
        var batch = glow ? _worldGlow : _worldAlpha;
        batch.AddQuad(a + n, b + n, b - n, a - n, color);
    }

    public void GradientLine(Vector2 a, Vector2 b, float width, Vector4 ca, Vector4 cb, bool glow = false)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 0.0001f) return;
        Vector2 n = new(-d.Y / len * width * 0.5f, d.X / len * width * 0.5f);
        var batch = glow ? _worldGlow : _worldAlpha;
        batch.AddQuadGradient(a + n, b + n, b - n, a - n, ca, cb, cb, ca);
    }

    public void Polyline(IReadOnlyList<Vector2> points, float width, Vector4 color, bool closed = false, bool glow = false)
    {
        if (points.Count < 2) return;
        for (int i = 0; i < points.Count - 1; i++) Line(points[i], points[i + 1], width, color, glow);
        if (closed) Line(points[^1], points[0], width, color, glow);
    }

    public void FillPolygon(IReadOnlyList<Vector2> points, Vector4 color, bool glow = false)
    {
        if (points.Count < 3) return;
        var batch = glow ? _worldGlow : _worldAlpha;
        for (int i = 1; i < points.Count - 1; i++)
            batch.AddTriangle(points[0], points[i], points[i + 1], color);
    }

    public void FillCircle(Vector2 center, float radius, Vector4 color, int segments = 24, bool glow = false)
    {
        if (radius <= 0) return;
        var batch = glow ? _worldGlow : _worldAlpha;
        float step = M.Tau / segments;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * step, a1 = (i + 1) * step;
            batch.AddTriangle(center, center + M.FromAngle(a0, radius), center + M.FromAngle(a1, radius), color);
        }
    }

    public void Circle(Vector2 center, float radius, float width, Vector4 color, int segments = 32, bool glow = false)
    {
        if (radius <= 0) return;
        Vector2 prev = center + new Vector2(radius, 0);
        for (int i = 1; i <= segments; i++)
        {
            Vector2 cur = center + M.FromAngle(M.Tau * i / segments, radius);
            Line(prev, cur, width, color, glow);
            prev = cur;
        }
    }

    public void Arc(Vector2 center, float radius, float start, float sweep, float width, Vector4 color, int segments = 24, bool glow = false)
    {
        Vector2 prev = center + M.FromAngle(start, radius);
        for (int i = 1; i <= segments; i++)
        {
            Vector2 cur = center + M.FromAngle(start + sweep * i / segments, radius);
            Line(prev, cur, width, color, glow);
            prev = cur;
        }
    }

    public void Rect(Vector2 center, Vector2 size, float angle, Vector4 color, bool glow = false)
    {
        Vector2 hx = M.FromAngle(angle, size.X * 0.5f);
        Vector2 hy = M.FromAngle(angle + MathF.PI * 0.5f, size.Y * 0.5f);
        Vector2 a = center - hx - hy;
        Vector2 b = center + hx - hy;
        Vector2 c = center + hx + hy;
        Vector2 d = center - hx + hy;
        (glow ? _worldGlow : _worldAlpha).AddQuad(a, b, c, d, color);
    }

    public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector4 color, bool glow = false)
    {
        (glow ? _worldGlow : _worldAlpha).AddQuad(a, b, c, d, color);
    }

    public void Triangle(Vector2 a, Vector2 b, Vector2 c, Vector4 color, bool glow = false)
    {
        (glow ? _worldGlow : _worldAlpha).AddTriangle(a, b, c, color);
    }

    public void Capsule(Vector2 a, Vector2 b, float radius, Vector4 color, bool glow = false)
    {
        Line(a, b, radius * 2f, color, glow);
        FillCircle(a, radius, color, 12, glow);
        FillCircle(b, radius, color, 12, glow);
    }

    public void UiQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector4 color)
    {
        _uiAlpha.AddQuad(a, b, c, d, color);
    }

    public void UiRect(float x, float y, float w, float h, Vector4 color, bool glow = false)
    {
        (glow ? _uiGlow : _uiAlpha).AddQuad(new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h), color);
    }

    public void UiFrame(float x, float y, float w, float h, float thickness, Vector4 color, bool glow = false)
    {
        UiRect(x, y, w, thickness, color, glow);
        UiRect(x, y + h - thickness, w, thickness, color, glow);
        UiRect(x, y, thickness, h, color, glow);
        UiRect(x + w - thickness, y, thickness, h, color, glow);
    }

    public void UiLine(float x1, float y1, float x2, float y2, float width, Vector4 color, bool glow = false)
    {
        float dx = x2 - x1, dy = y2 - y1;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f) return;
        float nx = -dy / len * width * 0.5f;
        float ny = dx / len * width * 0.5f;
        (glow ? _uiGlow : _uiAlpha).AddQuad(
            new Vector2(x1 + nx, y1 + ny), new Vector2(x2 + nx, y2 + ny),
            new Vector2(x2 - nx, y2 - ny), new Vector2(x1 - nx, y1 - ny), color);
    }

    public void UiCircle(float cx, float cy, float r, float width, Vector4 color, int segments = 24, bool glow = false)
    {
        float px = cx + r, py = cy;
        for (int i = 1; i <= segments; i++)
        {
            float a = M.Tau * i / segments;
            float x = cx + MathF.Cos(a) * r;
            float y = cy + MathF.Sin(a) * r;
            UiLine(px, py, x, y, width, color, glow);
            px = x;
            py = y;
        }
    }

    public int UiTextWidth(string text, float size, float tracking = 1f) => PixelFont.Width(text, size, tracking);

    public void UiText(string text, float x, float y, float size, Vector4 color, bool glow = false, float tracking = 1f)
    {
        text = PixelFont.Normalize(text);
        float cursor = x;
        bool shadow = size >= 2 && !glow;
        for (int ci = 0; ci < text.Length; ci++)
        {
            char ch = text[ci];
            var glyph = PixelFont.Get(ch);
            if (glyph != null)
            {
                for (int row = 0; row < 7; row++)
                {
                    string line = glyph[row];
                    int col = 0;
                    while (col < 5)
                    {
                        if (line[col] != '#')
                        {
                            col++;
                            continue;
                        }
                        int run = 1;
                        while (col + run < 5 && line[col + run] == '#') run++;
                        if (shadow)
                            _uiAlpha.AddQuad(
                                new Vector2(cursor + col * size + size * 0.14f, y + row * size + size * 0.18f),
                                new Vector2(cursor + (col + run) * size + size * 0.14f, y + row * size + size * 0.18f),
                                new Vector2(cursor + (col + run) * size + size * 0.14f, y + (row + 1) * size + size * 0.18f),
                                new Vector2(cursor + col * size + size * 0.14f, y + (row + 1) * size + size * 0.18f),
                                new Vector4(0, 0, 0, 0.65f));
                        var batch = glow ? _uiGlow : _uiAlpha;
                        batch.AddQuad(
                            new Vector2(cursor + col * size, y + row * size),
                            new Vector2(cursor + (col + run) * size, y + row * size),
                            new Vector2(cursor + (col + run) * size, y + (row + 1) * size),
                            new Vector2(cursor + col * size, y + (row + 1) * size),
                            color);
                        col += run;
                    }
                }
            }
            cursor += 6 * size * tracking;
        }
    }

    public void UiTextCentered(string text, float centerX, float y, float size, Vector4 color, bool glow = false, float tracking = 1f)
    {
        float w = UiTextWidth(text, size, tracking);
        UiText(text, centerX - w * 0.5f, y, size, color, glow, tracking);
    }

    public void UiTextRight(string text, float right, float y, float size, Vector4 color, bool glow = false, float tracking = 1f)
    {
        float w = UiTextWidth(text, size, tracking);
        UiText(text, right - w, y, size, color, glow, tracking);
    }

    public byte[] ReadPixels(out int width, out int height)
    {
        width = Width;
        height = Height;
        byte[] data = new byte[width * height * 4];
        ReadPixelsInto(data);
        return data;
    }

    public void ReadPixelsInto(byte[] data)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        unsafe
        {
            fixed (byte* ptr = data)
                _gl.ReadPixels(0, 0, (uint)Width, (uint)Height, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
        }
    }

    private GlFbo? _smallFbo;

    /// <summary>Downscales the finished frame on the GPU and reads it back as RGB (for highlight clips).</summary>
    public unsafe bool BlitSmall(byte[] rgb, int w, int h)
    {
        if (rgb.Length < w * h * 3) return false;
        if (_smallFbo == null || _smallFbo.Width != w || _smallFbo.Height != h)
        {
            _smallFbo?.Dispose();
            _smallFbo = new GlFbo(_gl, w, h, false, false);
        }
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _smallFbo.Fbo);
        _gl.BlitFramebuffer(0, 0, Width, Height, 0, 0, w, h, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _smallFbo.Fbo);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* ptr = rgb)
            _gl.ReadPixels(0, 0, (uint)w, (uint)h, PixelFormat.Rgb, PixelType.UnsignedByte, ptr);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return true;
    }

    public void SaveScreenshot(string path)
    {
        var pixels = ReadPixels(out int w, out int h);
        // UI blending leaves destination alpha < 1; the window ignores it but image viewers do not
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        PngWriter.Write(path, w, h, pixels);
    }

    public void Dispose()
    {
        _worldAlpha.Dispose();
        _worldGlow.Dispose();
        _uiAlpha.Dispose();
        _uiGlow.Dispose();
        _worldShader.Dispose();
        _uiShader.Dispose();
        _background.Dispose();
        _atlas.Dispose();
        _meshShader.Dispose();
        _backdropMeshes.Dispose();
        _worldMeshes.Dispose();
        _sceneA.Dispose();
        _sceneB.Dispose();
        Post.Dispose();
        _gl.DeleteVertexArray(_fullscreenVao);
        _gl.DeleteVertexArray(_emptyVao);
    }
}
