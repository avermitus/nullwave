# TASK-013: Lookahead Audio Buffer & Local Track Player

- **Status:** `done`
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer`
- **Reviewer (QA):** `@qa`

---

## 1. Goal & Player-Facing Benefit

Give NULLWAVE true **future-sight musical synchronization** and zero-dependency **local music playback**:

1. **Sample-Accurate Lookahead Audio Buffer (`~0.75s` ahead of speaker playback):**
   - Buffers **both** the built-in `SynthScore` and the new **Local Track Player** (`0.75s` = `36,000` stereo frames at `48 kHz`) while mixing game SFX **post-delay** so gunfire, dashes, and impacts remain 100% zero-latency.
   - Live WASAPI system / per-app Spotify loopback stays completely real-time (`LookaheadSeconds = 0`, untouched).
   - Compensates for FFT window + frame latency so kicks, snares, and hi-hats snap on the **exact frame** they reach the speakers.
2. **Pre-Drop Tension & Exact-Frame Beat-1 Drop Coupling (`DropAnticipation`):**
   - Scans the `0.75s` lookahead window ahead of playback to compute `AudioState.DropAnticipation` (`0..1`), rising smoothly as an energy/bass drop approaches playback.
   - Feeds `DropAnticipation` into the reactive backdrop shader (`uAnticipation` — inward spatial convergence & contracting tension rings, safely dampened by `ReduceFlashing` / `BackgroundMotion`), a subtle contracting telegraph ring around the ship, and `MusicDirector` so **Drop Overdrive** fires right on beat 1 of the drop!
3. **Zero-Dependency Local Track Player (`--track <path>` + Drag-Onto-Exe):**
   - Uses NAudio's built-in `AudioFileReader` + `WdlResamplingSampleProvider` / `MonoToStereoSampleProvider` (zero new NuGet packages; supports `.mp3`, `.wav`, `.aiff`, `.m4a`, etc. on Windows) with seamless looping and graceful fallback to `SynthScore` if missing/unreadable.
   - Displays `TRACK // <NAME>` and lookahead timing (`+0.75S`) cleanly inside the existing top-right audio HUD slot with **zero HUD overlap** and zero extra Settings rows.

---

## 2. Relevant Code & Ownership Constraints

### Strict File Ownership
- **`@designer` owns:** `.game-studio/BACKLOG.md`, `.game-studio/tasks/TASK-013.md` (Design & Spec), and—after `@programmer` finishes—updating `CHANGELOG.md` and `README.md` (strictly game-focused; zero AI/agent/swarm/task-ID copy).
- **`@programmer` owns:** `src/Nullwave/**` (`Audio/LookaheadAudioBuffer.cs`, `Audio/LocalTrackSource.cs`, `Audio/AudioState.cs`, `Audio/AudioEngine.cs`, `Audio/MusicDirector.cs`, `Render/Renderer.cs`, `Render/Shaders.cs`, `Render/WorldRenderer.cs`, `UI/Screens.cs`, `Core/Game.cs`, `Program.cs`, `Tools/SelfTest.cs`), optional showcase capture if warranted, `.game-studio/tasks/TASK-013.md` (§6 Engineering Handoff), and final git commit/push after `@qa` sign-off.
- **`@qa` owns:** `.game-studio/QA_REPORT.md` and `.game-studio/tasks/TASK-013.md` (§7 QA Verdict).

---

## 3. Implementation Plan (`@programmer`)

### A1. Sample-Accurate Lookahead Audio Buffer (`src/Nullwave/Audio/LookaheadAudioBuffer.cs`)
- Create `public sealed class LookaheadAudioBuffer : ISampleProvider`:
  - Fixed `48 kHz` stereo IEEE float (`WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)`).
  - Default `DefaultLookaheadSeconds = 0.75f` (`LookaheadFrames = 36000`).
  - Pre-allocated ring buffers (zero per-frame heap allocations on the audio or render threads):
    - `_stereoRing` (`float[RingFrames * 2]`, e.g., `RingFrames = LookaheadFrames + 16384` for safe FFT lookback + lookahead headroom).
    - `_monoRing` (`float[RingFrames]`) storing `(L + R) * 0.5f` for lock-free analysis reads.
    - Scratch read buffer `_pullBuf` (`float[8192]`) reused in `Read(...)` (grows only if a caller passes a larger chunk, never per-frame).
  - **Pre-fill (`Prime()`):** On construction / source swap (`SetSource(ISampleProvider source)`), pulls `LookaheadFrames` from upstream into `_stereoRing` and `_monoRing` before playback starts so the playback cursor is immediately `LookaheadFrames` behind the write cursor.
  - **`Read(float[] buffer, int offset, int count)` (Audio Thread):**
    - Copies delayed stereo frames from `_playCursor` to `buffer`, pulls the exact same number of fresh stereo frames from upstream into `_writeCursor`, updates `_monoRing`, and publishes `_writeCursor`, `_playCursor`, and `_totalPlayedFrames` with `Volatile.Write`.
  - **Analysis & Lookahead Queries (Game Thread, lock-free / zero-alloc):**
    - `public float LookaheadSeconds { get; }` (`LookaheadFrames / 48000f`).
    - `public long PlayedFrames => Volatile.Read(ref _totalPlayedFrames);`
    - `public bool CopyPlaybackWindow(Span<float> monoOut, int latencyCompensationFrames = 1024)`: copies `monoOut.Length` mono samples centered on `_playCursor + latencyCompensationFrames` (compensating for half-FFT + ~1 frame render latency so kicks/snares/hats snap on the exact frame they hit the speakers!).
    - `public int CopyNewPlaybackMono(ref long lastFrame, Span<float> monoOut)`: feeds monotonic playback-aligned mono chunks into `BeatTracker` (never skipping or replaying beats).
    - `public float ComputeDropAnticipation(out float futurePeakRms, out float currentRms)`:
      - Splits the `[0..LookaheadFrames]` future window into e.g. 6 buckets (~`125 ms` each) and computes RMS of each future bucket vs the current playback window (`~250 ms` around `_playCursor`).
      - Converts to dB (`20 * log10(rms + 1e-5)`). When a future bucket exceeds current playback dB by `>= 4.5 dB` (with meaningful absolute signal `futurePeakRms > 0.04f`), computes a smooth rising **`DropAnticipation`** (`0..1`) weighted by both jump strength and proximity to playback (`1.0` right as the drop reaches `0.0s`, `0.25..0.5` when `~0.75s` away)! Returns `0f` during steady/flat passages or silence.

### A2. Zero-Dependency Local Track Player (`src/Nullwave/Audio/LocalTrackSource.cs`)
- Create `public sealed class LocalTrackSource : ISampleProvider, IDisposable`:
  - Opens `path` using `NAudio.Wave.AudioFileReader(path)` (built into `NAudio 2.2.1` — zero new NuGet packages).
  - Normalizes to `48 kHz`, 2-channel IEEE float:
    - If `reader.WaveFormat.SampleRate != 48000`, wrap with `new WdlResamplingSampleProvider(source, 48000)`.
    - If `channels == 1`, wrap with `new MonoToStereoSampleProvider(source)`.
    - If `channels > 2`, downmix cleanly to stereo in a tiny zero-alloc `ISampleProvider` wrapper.
  - **Seamless Looping:** In `Read(float[] buffer, int offset, int count)`, if upstream returns fewer samples than requested (`EOF`), reset `_reader.Position = 0` and continue filling (guarded by a max-loop count of 2 so an empty/0-byte audio file returns silence instead of hanging).
  - Properties: `public string FilePath { get; }`, `public string TrackName { get; }` (sanitized `Path.GetFileNameWithoutExtension(path)`).
  - Clean `Dispose()` releasing `AudioFileReader`.

### A3. `AudioState` & `AudioEngine` Integration (`AudioState.cs`, `AudioEngine.cs`)
1. **In `src/Nullwave/Audio/AudioState.cs`:**
   - Add:
     - `public float LookaheadSeconds;`
     - `public float DropAnticipation;`
     - `public string LocalTrackName = "";`
     - `public bool LocalTrackActive;`
2. **In `src/Nullwave/Audio/AudioEngine.cs`:**
   - Route music playback inside `AudioProvider` through `LookaheadAudioBuffer`:
     - Music chain: `(LocalTrackSource ?? SynthScore)` -> `LookaheadAudioBuffer` (`0.75s`) -> `MusicVolume` -> mix with **zero-latency** `Sfx.Mix(...)` -> soft clamp!
   - Add `public bool LoadLocalTrack(string path)` and `public void UnloadLocalTrack()`:
     - Validates `File.Exists(path)`, instantiates `LocalTrackSource`, swaps `_lookahead.SetSource(track)` atomically (under a lightweight swap lock shared only on source change, never contending per-sample), resets `_analyzer`, `_tracker`, and `_external = false`.
     - On any error (missing file, unsupported codec, corrupt header), sets `LastError`, keeps `SynthScore` active, and returns `false` without throwing.
   - In `AudioEngine.Update(float dt)`:
     - **Case 1 — Local Track Active (`_localTrack != null`):**
       - Suppresses external WASAPI loopback override (`_external = false`), sets `Synth.TargetLevel = 0`.
       - Analyzes playback-aligned FFT window from `_lookahead.CopyPlaybackWindow(...)` via `_analyzer.Analyze(...)` and feeds `_tracker` via `_lookahead.CopyNewPlaybackMono(...)` + `ApplyTracker(dt)`.
       - Sets `State.LocalTrackActive = true`, `State.LocalTrackName = _localTrack.TrackName`, `State.External = false`, `State.SourceName = "TRACK // " + _localTrack.TrackName`, `State.LookaheadSeconds = _lookahead.LookaheadSeconds`, `State.DropAnticipation = M.ExpSmooth(State.DropAnticipation, _lookahead.ComputeDropAnticipation(out _, out _), 14f, dt)`.
     - **Case 2 — Live External Loopback Active (`_external == true`):**
       - 100% unchanged real-time path! Explicitly sets `State.LocalTrackActive = false`, `State.LocalTrackName = ""`, `State.LookaheadSeconds = 0f`, `State.DropAnticipation = 0f`.
     - **Case 3 — Built-in `SynthScore` Active (`!_external && _localTrack == null`):**
       - When `_output != null` and `_lookahead.PlayedFrames > 0` and `!_quiet`: analyzes `_lookahead` (`State.LookaheadSeconds = _lookahead.LookaheadSeconds`, `State.DropAnticipation = ...`) alongside `SynthVisual` beat/BPM stability so kicks/snares/hats snap cleanly to speaker playback; in headless/no-output mode (`--smoke` / `--capture` / `--selftest`), falls back gracefully to `_synthVisual.Advance(dt, State)` with `State.LookaheadSeconds = _lookahead.LookaheadSeconds` and `State.DropAnticipation = 0f` (unless driven synthetically).
   - Dispose `_localTrack` in `AudioEngine.Dispose()`.

### A4. Exact-Frame Drop Snap & Anticipation Visual Coupling (`MusicDirector.cs`, `Renderer.cs`, `Shaders.cs`, `WorldRenderer.cs`, `Core/Game.cs`)
1. **In `src/Nullwave/Audio/MusicDirector.cs`:**
   - Add `public float Anticipation { get; private set; }` (smoothed from `a.DropAnticipation`; track recent peak `_recentAnticipation` decaying over `~0.6s`).
   - In drop detection (`Update`): when `a.LookaheadSeconds > 0.05f && _recentAnticipation > 0.45f`, treat `_recentAnticipation` as a valid `primed` signal alongside `_buildRaw` / `_quietTime` / `Breakdown` and relax the minimum `_age` guard slightly (e.g., `_age > 2.5f` when lookahead-primed vs `8f` for live loopback) so a strong local-track drop triggers `DropTriggered = true` right on beat 1!
2. **In `src/Nullwave/Render/Renderer.cs` & `Shaders.cs` (`BackgroundFrag`):**
   - Upload `uAnticipation` (`settings.ReduceFlashing ? audio.DropAnticipation * 0.35f : audio.DropAnticipation`) into `BackgroundFrag`.
   - In `BackgroundFrag`:
     - Before the drop explodes outward, apply subtle **inward convergence tension** scaled by `uMotionScale`:
       `float inhale = 1.0 + clamp(uAnticipation * 0.075 * uMotionScale, 0.0, 0.12);`
     - Add a crisp **contracting pre-drop tension ring** (`float pullR = (1.0 - uAnticipation * 0.78) * 1.15; float tensionRing = exp(-abs(r - pullR) * 18.0) * uAnticipation;`) that selectively highlights pattern ridges in violet-cyan as it pulls inward toward the arena center!
3. **In `src/Nullwave/Render/WorldRenderer.cs`:**
   - When `audio.DropAnticipation > 0.25f` and `!GameSettings.Current.ReduceFlashing`: draw a delicate contracting anticipation halo around the player (`radius = M.Lerp(210f, 36f, audio.DropAnticipation)`) so pilots feel the exact countdown into **Drop Overdrive**!

### A5. CLI Entry, Drag-Onto-Exe & Zero-Overlap HUD (`Program.cs`, `Core/Game.cs`, `UI/Screens.cs`)
1. **In `src/Nullwave/Program.cs`:**
   - Parse `--track <path>` -> `Game.TrackOverride = args[++i]`.
   - Support drag-onto-exe / direct file arg: if a positional arg doesn't start with `--`, isn't consumed by a previous flag, and has a recognized audio extension (`.mp3`, `.wav`, `.flac`, `.ogg`, `.m4a`, `.aiff`, `.wma`) or `File.Exists(a)`, set `Game.TrackOverride = a`.
2. **In `src/Nullwave/Core/Game.cs`:**
   - Add `public static string? TrackOverride;`.
   - On startup (after `_audio` is created), if `!string.IsNullOrWhiteSpace(TrackOverride)`:
     - Call `_audio.LoadLocalTrack(TrackOverride)`. If it returns `false`, set `_toast = "TRACK LOAD FAILED // USING SCORE"; _toastTimer = 4.5f;`.
3. **In `src/Nullwave/UI/Screens.cs`:**
   - Top-right HUD (`1542, 72` & `1542, 93`) and Pause/Title audio readout:
     - When `audio.LocalTrackActive`: `SourceLabel(audio)` returns `"LOCAL TRACK (+0.75S)"` (formatted from `LookaheadSeconds`) and line 2 (`1542, 93`, same slot as Spotify track!) renders `"TRACK // " + FormatTrack(audio.LocalTrackName, 28)`.
     - When `!audio.External && !audio.LocalTrackActive && audio.LookaheadSeconds > 0.01f`: `SourceLabel(audio)` returns `"GENERATIVE SCORE (+0.75S)"`.
     - Zero overlap with `BPM`, spectrum bars (`y = 188`), or Pause card (`SettingsMenu.cs` row count unchanged!).

### A6. Deterministic Self-Test Coverage (`src/Nullwave/Tools/SelfTest.cs`)
- Add `TestLookaheadAudioBuffer()` called from `SelfTest.Run()`, verifying:
  1. **Sample-Accurate Delay:** Impulse fed into `LookaheadAudioBuffer` (`0.75s` = `36,000` frames at `48 kHz`) produces exact `0` output for the first `36,000` frames and emerges at frame `36,000` (`±0` frames).
  2. **Pre-Drop `DropAnticipation` & Exact-Frame Drop Snap:** Synthetic quiet build followed by a loud 128 BPM bass drop fed through `LookaheadAudioBuffer` + `SpectrumAnalyzer` + `MusicDirector` proves `DropAnticipation > 0.5f` *before* the drop reaches the playback cursor and `MusicDirector.DropTriggered` fires right as the drop lands on playback!
  3. **Real `.wav` Local Track Round-Trip, Looping & Fallback:** Writes a temporary stereo `.wav` via `WaveFileWriter`, loads it via `LocalTrackSource` + `AudioEngine.LoadLocalTrack(tempPath)` (headless `enableOutput: false, enableCapture: false`), verifies `LocalTrackActive == true`, `LookaheadSeconds >= 0.74f`, non-zero samples, seamless looping past EOF, clean disposal + temp file deletion, and verifies `LoadLocalTrack("does_not_exist_12345.wav")` returns `false` and leaves `LocalTrackActive == false` cleanly.

---

## 4. Acceptance Criteria (`@qa`)

1. **Clean Release Build:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeds with **0 warnings and 0 errors** (zero new NuGet packages added).
2. **Self-Test Suite Pass:** `./run.cmd --selftest` passes with `RESULT: PASS` including `TestLookaheadAudioBuffer()` and zero regressions.
3. **Dual-Theme Smoke & Render State Hygiene:**
   - `./run.cmd --smoke 5` (Standard) -> `RENDERSTATE -> PASS`, `alphaMin=255`, `rerenderDiff=0.000 %`.
   - `./run.cmd --smoke 4 --attune synergy --theme abyssal` -> `RENDERSTATE -> PASS`, `alphaMin=255`, `rerenderDiff=0.000 %`, high FPS.
   - Verify `--track <missing.wav>` falls back cleanly to `SynthScore` without crash.
4. **Zero-Alloc & Comfort Audit:**
   - Verify zero per-frame heap allocations in `LookaheadAudioBuffer.Read`, `CopyPlaybackWindow`, `ComputeDropAnticipation`, and `uAnticipation` upload.
   - Verify live WASAPI loopback remains real-time (`LookaheadSeconds == 0`, `DropAnticipation == 0`) and `ReduceFlashing` dampens `uAnticipation` cleanly.
5. **Documentation & Repo Sync (after `@programmer` finishes):**
   - `@designer` updates `CHANGELOG.md` and `README.md` (strictly game-focused; zero AI/agent/swarm/task-ID chatter), `@qa` verifies everything, and `@programmer` commits & pushes cleanly to `origin/main` after `@qa` sign-off.

---

## 5. Designer Notes (`@designer`)
- Mixing game SFX **after** `LookaheadAudioBuffer` is the golden rule of rhythm-action audio: players get exact-frame musical foresight (`0.75s` lookahead) with **0 ms added input-to-SFX latency** on shots, dashes, and kills.
- Reusing the exact `(1542, 93)` track subtitle slot for `TRACK // <NAME>` keeps the HUD minimalist and guarantees zero layout regressions across Title, Combat, and Pause screens.

---

## 6. Engineering Handoff (`@programmer`)
- **Files Added / Modified (`@programmer` scope):**
  - `src/Nullwave/Audio/LookaheadAudioBuffer.cs` *(new)*: Zero-alloc `48 kHz` stereo lookahead ring buffer (`0.75s` = `36,000` frames, `52,384`-frame ring headroom), atomic `SetSource`, exact-frame `CopyPlaybackWindow(Span<float>, int latencyCompensationFrames = 1024)`, monotonic `CopyNewPlaybackMono(ref long, Span<float>)`, and 6-bucket RMS dB-jump `ComputeDropAnticipation(out float, out float)` (`>= 4.5 dB`, proximity-weighted `0..1`).
  - `src/Nullwave/Audio/LocalTrackSource.cs` *(new)*: Zero-dependency local file player using NAudio's built-in `AudioFileReader` + `MonoToStereoSampleProvider` / `StereoDownmixProvider` + `WdlResamplingSampleProvider` normalizing `.wav`/`.mp3`/`.m4a`/`.aiff`/etc. to `48 kHz` stereo float with bounded seamless looping and clean `Dispose()`.
  - `src/Nullwave/Audio/AudioState.cs` & `src/Nullwave/Audio/AudioEngine.cs`: Added `LookaheadSeconds`, `DropAnticipation`, `LocalTrackName`, `LocalTrackActive`; routed music (`LocalTrackSource ?? SynthScore`) through `LookaheadAudioBuffer` while mixing `Sfx` **post-delay** (zero added input-to-SFX latency); added `LoadLocalTrack(path)` / `UnloadLocalTrack()` with graceful fallback to `SynthScore`; preserved live WASAPI / Spotify loopback as untouched real-time (`LookaheadSeconds = 0`, `DropAnticipation = 0`).
  - `src/Nullwave/Audio/MusicDirector.cs`: Added `Anticipation` + decaying `_recentAnticipation` so lookahead-primed local tracks trigger `DropTriggered` right on beat 1 (relaxing `_age` guard from `8s` to `2.5s` when lookahead-primed).
  - `src/Nullwave/Render/Renderer.cs`, `Shaders.cs`, & `WorldRenderer.cs`: Uploaded `uAnticipation` (dampened `0.35x` when `ReduceFlashing` is enabled) into `BackgroundFrag` for inward spatial inhale (`uMotionScale`-scaled) and a contracting violet-cyan tension ring that selectively highlights pattern ridges in both Standard and Abyssal; added a delicate contracting anticipation halo around the player when `DropAnticipation > 0.25f` and `!ReduceFlashing`.
  - `src/Nullwave/Program.cs`, `Core/Game.cs`, & `UI/Screens.cs`: Added `--track <path>` CLI flag + positional audio file / drag-onto-exe support (`Game.TrackOverride`, failure toast `"TRACK LOAD FAILED // USING SCORE"`); updated Title, HUD (`1542, 72/93`), and Pause readouts to show `LOCAL TRACK (+0.75S)` / `TRACK // <NAME>` and `GENERATIVE SCORE (+0.75S)` with zero HUD overlap and zero new `SettingsMenu.cs` rows.
  - `src/Nullwave/Tools/SelfTest.cs`: Added `TestLookaheadAudioBuffer()` proving exact `36,000`-frame impulse delay (`±0` frames), pre-drop `DropAnticipation` (`1.00`) + beat-1 `MusicDirector` drop snap (`3.73s`), real `44.1 kHz` `.wav` load/resample/loop/cleanup via `LocalTrackSource` + `AudioEngine`, missing-file fallback, and `uAnticipation` shader contract.
- **Verification Results:**
  - `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` -> **0 Warning(s), 0 Error(s)** (zero new NuGet packages)
  - `./run.cmd --selftest` -> **`RESULT: PASS (78354 assertions, 2.7s)`** (`lookahead audio: delay=36000f (0.75s) preDropAnt=1.00 dropSnap=3.73s wavLoop=PASS`)
  - `./run.cmd --smoke 5` (Standard) -> **`SMOKE | 5.0s fps=113`**, **`RENDERSTATE -> PASS`** (`alphaMin=255`, `rerenderDiff=0.000 %`)
  - `./run.cmd --smoke 4 --attune synergy --theme abyssal` -> **`attunes=12 synergies=3`**, **`SMOKE | 4.0s fps=114`**, **`RENDERSTATE -> PASS`** (`alphaMin=255`, `rerenderDiff=0.000 %`)
  - `./run.cmd --smoke 2 --track does_not_exist_12345.wav` -> clean fallback (`audio=GENERATIVE SCORE`), **`RENDERSTATE -> PASS`**.

---

## 7. QA Findings & Verdict (`@qa`)
- **Verdict:** **PASS (`APPROVED — 0 DEFECTS`)**
- **Build & Automated Verification:**
  - `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` -> **0 Warning(s), 0 Error(s)** (`0.60s`, zero new NuGet packages).
  - `./run.cmd --selftest` -> **`RESULT: PASS (78333 assertions, 2.6s)`** (`lookahead audio: delay=36000f (0.75s) preDropAnt=1.00 dropSnap=3.73s wavLoop=PASS`).
  - `./run.cmd --smoke 5` (Standard) -> **`5.0s fps=118`**, **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
  - `./run.cmd --smoke 4 --attune synergy --theme abyssal` -> **`attunes=12 synergies=3`**, **`4.0s fps=116`**, **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
  - `./run.cmd --smoke 2 --track does_not_exist_12345.wav` -> clean fallback to `GENERATIVE SCORE`, **`RENDERSTATE -> PASS`**.
- **Code, Allocation & Comfort Audit:**
  - Confirmed zero per-call heap allocations in `LookaheadAudioBuffer.Read`, `CopyPlaybackWindow`, `CopyNewPlaybackMono`, `ComputeDropAnticipation`, `StereoDownmixProvider`, and `uAnticipation` upload; SFX mixed strictly post-delay (`0 ms` added latency).
  - Confirmed live WASAPI/Spotify loopback stays real-time (`LookaheadSeconds = 0`, `DropAnticipation = 0`), `ReduceFlashing` dampens `uAnticipation` (`0.35x`) and disables the contracting player halo, and `SettingsMenu.cs` is untouched.
  - Verified `README.md` and `CHANGELOG.md`: all image links resolve (`Test-Path == True`) with strictly game-focused copy and zero non-game references.
  - Full report logged in `.game-studio/QA_REPORT.md` (§17).
