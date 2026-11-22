# NULLWAVE — Studio Project Discovery & Architecture Reference

## 1. Executive Summary
**NULLWAVE** is a native Windows twin-stick audio-reactive bullet hell written in pure C# (.NET 8) with zero external game engine and zero external 3D model assets. It captures live system mix or per-application audio via WASAPI loopback (or falls back to an internal procedural generative synthesizer) and constructs its combat pacing, enemy firing grids, song-laser choreography, post-processing mood grading, and procedural raymarched/fractal backdrops in real time from the music signal.

- **Genre:** Endless Audio-Driven Twin-Stick Bullet Hell / Arcade Score Attack
- **Target Platform:** Windows 10 (build 20348+ for per-app process loopback) & Windows 11 (`net8.0-windows`, `win-x64`)
- **Framework / Dependencies:**
  - `Silk.NET.Windowing`, `Silk.NET.OpenGL`, `Silk.NET.Input` (`2.21.0`) — Windowing, OpenGL 3.3+ context, gamepad/keyboard/mouse input
  - `NAudio` (`2.2.1`) + custom P/Invoke `ProcessLoopbackCapture` — WASAPI system and process-isolated loopback capture + procedural SFX/music output
- **Build & Verification Baseline:**
  - Release build: `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` (0 warnings, 0 errors)
  - Headless self-test suite: `./run.cmd --selftest` (19 deterministic test suites, ~58k assertions)
  - Smoke run: `./run.cmd --smoke 5` (~64 FPS fixed-timestep simulation with `Pilot` AI)

---

## 2. Controls & Input Mapping

| Action | Keyboard + Mouse | Gamepad | Notes |
|---|---|---|---|
| **Move** | `W A S D` / Arrow Keys | Left Stick / D-Pad | Momentum-based racer handling (top speed `560`, accel `6.5`, glide `2.6`) |
| **Aim** | Mouse Cursor | Right Stick | Screen-to-world oblique unprojection |
| **Fire** | Left Mouse Button / `Space` | Right Trigger (`RT`) | Continuous twin-bolt + attunement pattern fire |
| **Dash** | `Space` / Right Mouse Button | `A` / Right Shoulder | `1700` speed burst for `0.15s` with `0.24s` invulnerability + barrel roll |
| **Focus** | `Left Shift` | Left Trigger / `LB` | Reduces movement speed by up to `60%` for precision dodging; tightens crosshair |
| **Resonance Cascade (Burst)** | `E` / Middle Mouse | `X` / `Y` | Consumes `100` Resonance: clears enemy bullets in `760` radius, damages enemies/monoliths, fires 32 radial bolts, grants `0.7s` invulnerability |
| **Pause / Back** | `Esc` / `P` | `Start` / `B` | Opens `SIGNAL HELD` pause overlay or navigates back |
| **Archive** | `Tab` (on GameOver / Title) | `Select` / Menu | Opens `ARCHIVE OF THE SIGNAL` modifier selection & career telemetry |
| **Diagnostics / Fullscreen / Audio Source** | `F1` / `F2` / `F3` | — | `F1` toggles entity/FPS debug overlay; `F2` toggles fullscreen; `F3` cycles WASAPI audio source (`System` -> active audio apps) |

---

## 3. Core Gameplay Loop & Systems

1. **Signal Selection (Title / Archive):**
   - Players launch a **New Signal** (random 8-character base-32 seed), **Daily Signal** (deterministic date seed), or configure unlocked **Signal Modifiers** in the **Archive** (16 modifiers ranging from `METRONOME` `x1.15` to `NULLWAVE` `x1.60`, stacking multiplicatively up to `x25.00`).
2. **Wave & Movement Progression (`World.cs`, `World.Director.cs`):**
   - Fixed `60 Hz` deterministic simulation inside a `5600 x 3600` arena (scaled by `ArenaScale`).
   - Five procedural obstacle layouts (`SCATTER`, `HENGE`, `CITY`, `SPIRAL`, `LANES`) populate extruded 3D monoliths clear of the player.
   - Every 5th wave spawns **THE CHOIR** (multi-phase boss with spectrum pillar crown, phase halos, orbiting shards, and sweeping laser arrays).
   - Clearing a boss advances the **Movement** (`I: DRIFT`, `II: CROSSFIRE`, `III: BLOOM`, `IV: SPIN`, `V: SHATTER`, `VI: DOUBLE TIME`, `VII+: OVERDRIVE`), adding global arena rules and shifting the seed palette.
3. **Beat-Locked Combat (`AdvanceBeatClock`, `World.Enemies.cs`):**
   - Enemy roles (`Drifter`, `Orbiter`, `Sniper`, `Dasher`, `Turret`, `Splitter`, `Swarm`, `Weaver`, `Mortar`, `Phantom`, `Prism`) fire strictly on their musical subdivision (`EighthTick` / `BeatTick` / `DownbeatTick`) phase-locked to the tracked music via a PLL + continuous `BeatPosition` follower.
   - All enemy bullets and movement pulse with `BeatSurge`, normalized to an average speed multiplier of `1.0` so musical intensity enhances feel without distorting baseline difficulty.
4. **Flow, Hype & Resonance (`UpdateFlow`, `UpdateHype`):**
   - **Flow (`0..1`):** Builds when flying above `70%` top speed, grazing bullets (`+0.03`), and scoring kills (`+0.035`); drains when slow or hit (`x0.35`). Grants up to `x2.50` score multiplier (`1 + 1.5 * Flow`) and triggers `// FLOW STATE //` above `0.85`.
   - **Hype (`0..1`, Levels `0..4`):** Combines Flow (`45%`), Combo (`30%`), and time since last hit (`25%`) to scale visual reactivity (`Renderer.HypeScale` from `0.55x` to `1.45x`).
5. **Attunement Draft (`Attunements.cs`):**
   - Between waves, players draft 1 of 3 procedural **Attunement Cards** across 12 upgrade lines (`SPLIT`, `FAN`, `PIERCE`, `RAPID`, `HEAVY`, `LONG WAVE`, `HOMING`, `RICOCHET`, `REAR ARRAY`, `SIPHON`, `PHASE DRIVE`, `GRAZER`).
6. **Meta Progression (`Meta.cs`):**
   - Stored at `%LOCALAPPDATA%\Nullwave\save.json` with atomic temp-file writes and corrupt-save backup recovery. Tracks lifetime totals, best wave/score/grazes, per-seed records (capped at 40), modifier combinations, and unlocks new Modifiers and Attunements after each run.

---

## 4. Rendering & Visual Pipeline

1. **Procedural 3D Meshes (`Render/Mesh.cs`, `Render/Models.cs`):**
   - Built at startup via `MeshBuilder` primitives (loft, prism, frustum, icosphere, torus, slab) with 4 material slots (`Hull`, `Trim`, `Glow`, `Core`) recolored per draw in a CPU-transformed `MeshBatch`.
   - Rendered using an oblique orthographic projection (`Renderer.Oblique = 0.42f`) with directional + rim lighting and depth testing.
2. **Six-Style Procedural Backdrop Shader (`Render/BackdropGenome.cs`, `Shaders.BackgroundFrag`):**
   - Styles: `0: LIQUID` (raymarched metaballs), `1: HYPERSPACE` (polygon tunnel), `2: KALEIDOSCOPE` (Kali-set fractal), `3: FIELD` (raymarched SDF repetition), `4: AURORA` (flowing ribbons), `5: SHATTER` (Voronoi cells).
   - Smooth 1.8s radial bloom transitions (`_genA` -> `_genB`) on new runs, movement changes, and musical drops.
3. **Scene Draw Order (`Renderer.Render`):**
   - Bind scene FBO (`_sceneA` / `_sceneB` ping-pong, RGBA + Depth)
   - Fullscreen procedural `BackgroundFrag` (blend off)
   - Optional perspective `_backdropMeshes` pass (depth clear + depth test)
   - 2D `_worldAlpha` batch (arena floor plate, shadows, rings)
   - Oblique 3D `_worldMeshes` pass (monoliths, player ship, enemies, boss crown; depth clear + depth test)
   - 2D `_worldGlow` additive batch (`SrcAlpha, One` — bullets, lasers, shockwaves, particles)
   - `PostFx.Process`: 3-level downsample + separable Gaussian bloom pyramid + `CompositeFrag` (YIQ mood hue rotation, saturation/contrast grading, radial kick thump, glitch slices, speed lines, chromatic aberration, vignette) onto backbuffer (`FBO 0`)
   - UI passes (`_uiAlpha` standard alpha blend, `_uiGlow` additive blend) in `1600 x 900` virtual design space.

---

## 5. Audio & Music Intelligence Pipeline

- **Capture (`AudioEngine.cs`, `ProcessLoopbackCapture.cs`):**
  - Captures system-wide WASAPI loopback or process-specific audio via Windows 10/11 `ActivateAudioInterfaceAsync` (`AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS` on an MTA thread with `IAgileObject` completion handler).
  - Falls back seamlessly to `SynthScore` (procedural generative synth soundtrack) when external audio is silent, unless `QUIET SIGNAL` modifier is active.
- **Adaptive Spectrum Analyzer (`SpectrumAnalyzer.cs`):**
  - 48-bar logarithmic spectrum with per-band adaptive dB ceiling/floor normalization (prevents brickwalled/loud masters from pinning bars at 100%), spectral flatness, and dedicated `KickHit`, `SnareHit`, and `HatHit` onset detectors.
- **Beat Tracker (`BeatTracker.cs`):**
  - Fixed-hop log-spectral flux, autocorrelation tempo estimation with `x2/x4` harmonic scoring and 120 BPM prior, 14-beat comb filter for phase alignment with flip hysteresis, and bass-weighted downbeat voting. Achieves 8/9 accuracy (3–20 ms median error) on the synthetic `--beatbench` suite.
- **Music Director (`MusicDirector.cs`):**
  - Tracks structural dynamics in dB over a ~60s sliding window to classify **Intensity**, **Build**, **Breakdown**, and **Drop** (triggered when level clears recent peak after a build/quiet stretch within 150 ms of a beat). Drives both `PostMood` visual grading and `World.UpdateSongLasers` (`STARBURST`, `SCAN`, `LASER GATE`, `LASER CROSS`).

---

## 6. Current Technical State & Known Gotchas

1. **`PostFx.cs` OpenGL State & Backbuffer Alpha Leak (`Render/PostFx.cs`, `Render/Renderer.cs`):**
   - `PostFx.Process` disables `Blend` and `DepthTest`, binds textures across `Texture0..Texture3` leaving `ActiveTexture` set to `Texture3`, and exits without restoring baseline GL state.
   - `Renderer.Render` finishes with `_uiGlow.Draw()` under additive blend (`SrcAlpha, One`), and the UI pass uses `BlendFunc(SrcAlpha, OneMinusSrcAlpha)` which reduces backbuffer destination alpha `< 1.0` whenever semi-transparent UI elements draw over opaque pixels (`SaveScreenshot` manually overwrites alpha to `255` on CPU, whereas multi-render paths or raw backbuffer reads see altered state).
2. **Archive & Post-Run Telemetry Gap (`World/Meta.cs`, `Core/Game.cs`, `UI/Screens.cs`):**
   - `EnterGameOver()` calls `_meta.RecordRun(_world.Summary)` before `DrawGameOver` renders, immediately overwriting `BestWave` and `BestScore` so the player is never told when they achieved a new personal best (score, wave, or combo chain).
   - `MetaProgress` aggregates lifetime totals and per-seed high scores, but discards individual `RunSummary` telemetry (`MaxCombo`, `Duration`, `MovementName`, `Attunements`, `Daily`, `Wave`, `Score`) and does not track `BestCombo`, leaving the `ARCHIVE` and `SIGNAL LOST` screens without recent run history or personal-best callouts.
3. **`.gitignore` Capture Exclusions (`HANDOFF.md` §8):**
   - `.gitignore` excludes `/capture/`, `*.mp4`, and `*.wav`, while `HANDOFF.md` §8 explicitly notes adding `capture/*.mp4` and `capture/*.wav` rules for clarity.
4. **Process Termination Safety:**
   - Never use `pkill -f` or broad pattern kills on Windows; always let `--selftest` and `--smoke` terminate naturally or kill by exact PID if ever needed.
5. **Static Framebuffer Rule:**
   - `Program.cs` uses a static `_frameBuffer` for `--record`; never allocate per-frame pixel buffers on hot paths.


## Opt-in map experiment (2026-11-21)

HALO CIRCUIT and CATHEDRAL implemented on experiment/maps; arena remains default. Shared polygon geometry drives rendering, movement, navigation, spawning and minimap. Title MAP selection and --map support are available. See [TASK-MAPS-EXPERIMENT](tasks/TASK-MAPS-EXPERIMENT.md) for evidence and scope. Parent independent review/integration is pending; human balance testing remains follow-up.
