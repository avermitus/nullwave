# NULLWAVE — handoff

Native Windows twin-stick bullet hell written in pure C# (no engine, no model assets) that captures
loopback audio from **any** app and drives its level design, pacing, and visuals from that signal.
It also has an offline recorder used to capture video of live play.

Read the "Gotchas" section before touching the recorder — most of it cost real debugging time.

---

## 1. State

The game is finished and playable: title, archive, combat, pause, attunement cards, game over, HUD,
spectrum, boss bar, fixed 60 Hz sim, 16 modifiers, 12 attunements, bosses every 5 waves, local save.

Current verification (re-measured 2026-11-18):

| check | result |
|---|---|
| `--selftest` | `PASS (71952 assertions, 5.1s)` (2026-11-20) |
| `--smoke 5` | 64 fps |
| `--audiotest` | loopback capture works, BPM tracked correctly |
| Release build | 0 warnings / 0 errors |

The project is a git repository with a private GitHub remote. Last commit: `c473322`; the work in
TASK-016 to TASK-020 is not committed yet.

---

## 1b. V2 visual + gameplay pass (2026-11-17)

Pre-change sources are backed up at `_backup/Nullwave-pre-v2/` (there is still no git).

| area | what changed | where |
|---|---|---|
| 3D renderer | Procedural lit meshes (no model assets). `MeshBuilder` primitives (loft, prism, frustum, icosphere, torus, slab), material slots (Hull/Trim/Glow/Core) recoloured per draw, CPU-transformed `MeshBatch`. Scene FBOs now have a depth buffer. | `Render/Mesh.cs`, `Render/Renderer.cs`, `Shaders.MeshVert/MeshFrag` |
| Backdrop (V3) | Equaliser visuals removed. `BackgroundFrag` is now a fluid shader: a domain-warped flowing nebula plus raymarched liquid metaballs (glassy, iridescent rims) that merge and split. Bass inflates the blobs, each band drives one blob, highs ripple the surface, beats flare the rims, and a "flow clock" in `Renderer.Render` speeds up with energy so motion surges without jumping. No frame feedback any more. Border pylons and the boss crown use a smooth wave instead of per-bin bars. | `Render/Shaders.cs`, `Render/Renderer.cs` |
| Per-app audio | WASAPI process loopback (Win10 20348+ / Win11): capture one app and its child processes, excluding the game's own synth. F3 cycles: all system audio -> each app with an audio session. Choice is saved (`save.json` `AudioApp`) and re-attaches when the app restarts. `--app spotify` sets it from the command line (`--app system` clears it); `--list-apps` prints candidates. NAudio 2.2.1 has the structs but no capture class, so activation is P/Invoked in `ProcessLoopbackCapture.cs`. | `Audio/ProcessLoopbackCapture.cs`, `Audio/AudioEngine.cs` |
| Models | Player interceptor (banks into strafes, barrel-rolls on dash, thrusters scale with speed); 7 enemy designs + Choir boss (spectrum pillar crown, halos per phase, orbiting shards); elite gold ring; monoliths extruded from each obstacle's polygon; spectrum pylons on the border. | `Render/Models.cs`, `Render/WorldRenderer.cs` |
| On-beat fire | World beat clock is now phase-locked to detected beats (PLL nudged by `audio.Beat`, bar-aligned on downbeats) instead of a free grid at the same BPM. Enemies count down in eighth notes and only fire on their role's rhythm (drifters 1+3, snipers backbeat, turrets downbeat, orbiters/swarm off-beats). A closing ring telegraphs each shot; turret/splitter rings surge forward on every beat. | `World.cs` `AdvanceBeatClock`, `World.Enemies.cs` |
| Bigger maps | Arena 3000x2000 -> 5600x3600. Five layouts by movement: SCATTER, HENGE, CITY, SPIRAL, LANES. Spawns arrive 660-980 units from the player (edge spawns would be too far now). Boss offsets are fixed distances. | `World.Director.cs` |
| HUD | Segmented INTEGRITY health bar with a draining "ghost" chunk and critical pulse; health pips under the ship; enemy health bars; minimap with layout name; off-screen threat arrows. | `UI/Screens.cs` |
| Settings | SETTINGS on the title and pause menus. Audio: master/music/effects volume, audio source, beat offset (+/-200 ms latency compensation applied to the enemy fire grid). Video: fullscreen, background intensity/motion, bloom, screen shake, chromatic aberration, FPS counter. Interface: minimap, threat arrows, enemy health bars, beat telegraphs, crosshair size. Saved in `save.json` under `Settings`; read everywhere through `GameSettings.Current`. Keyboard, pad and mouse (click/drag sliders). Capture scenario `settings`. | `Core/GameSettings.cs`, `UI/SettingsMenu.cs` |
| Beat tracking (V5) | The old detector treated every transient as a beat (hats, vocals), took every 4th as a downbeat and estimated tempo from the median gap: on the bench it got tempo right on 2/9 songs. `BeatTracker` replaces it for captured audio: fixed-hop log spectral flux, tempo by autocorrelation scored with x2/x4 multiples, 1/2 and 1/4 subdivisions and a 120 BPM prior, phase by a 14-beat comb on a kick/snare-weighted envelope with flip hysteresis (12% margin, 10 votes), continuous predicted `BeatPosition`, bass-voted downbeat. `--beatbench` = 8/9 synthetic songs (median 3-20 ms); the miss is an extreme syncopated hip-hop case. Needs ~4 s of music to lock; until then the world free-runs at the last tempo. Downbeat 1 vs 3 is often ambiguous (affects only downbeat-only fire). | `Audio/BeatTracker.cs`, `Tools/BeatBench.cs`, `AudioEngine.ApplyTracker` |
| Beat feel (V5) | World clock follows `audio.BeatPosition` (extrapolated between frames, snaps on re-lock). Every enemy bullet and enemy moves with `World.BeatSurge`: a surge on the beat that relaxes, normalised to average speed 1.0 (difficulty unchanged), scaled by the BEAT MOTION setting. All roles fire on beats only (DOUBLE TIME adds off-beats). Models kick and bullets flash on the beat. End-to-end self-test: 27/27 volleys within 45 ms of a synthetic song's beats. | `World.cs`, `World.Player.cs`, `World.Enemies.cs` |
| Music director (V6) | `MusicDirector` reads song structure from loudness in dB (Energy saturates on loud masters): Intensity within the song's own ~1 min range, Build (rising level + highs), Breakdown (sustained quiet), Drop (instant level clears the peak of the previous 0.5-3.5 s by a quarter of the range, after a build/quiet stretch, within 150 ms of a beat, 10 s cooldown, 8 s warm-up), Brightness (spectral centroid), MoodHue, Kick, Sparkle. Drives: composite grading (YIQ hue rotate, saturation, contrast, tint, vignette), kick radial thump blur, glitch slices (off with REDUCE FLASHING), camera punch/push-in/release, backdrop burst/ripple/slow-down, ambient flow-field motes, and `World.OnMusicDrop` (shockwave, flash, +20 resonance). Settings: MUSIC FX 0-150%, REDUCE FLASHING. Capture scenarios `drop`, `breakdown`; capture prints a MOOD line. Screenshots are now forced opaque (UI blending left alpha < 1, which viewers showed as grey). | `Audio/MusicDirector.cs`, `Render/Renderer.BuildPostMood`, `Shaders.CompositeFrag`, `WorldRenderer.DrawMotes` |
| Random backdrops (V7) | `BackgroundFrag` now holds six styles (LIQUID metaballs, HYPERSPACE polygon tunnel, KALEIDOSCOPE Kali-set fractal, FIELD raymarched flythrough of repeated tori/boxes/octahedra, AURORA ribbons, SHATTER voronoi). Each backdrop is a `BackdropGenome` from an unseeded `Random`: style, 4 params, mutators (mirror, swirl, kick zoom), random cosine palette. New genome on every run, act change, drop, and every 22 s on the title; transitions bloom outward from the centre over 1.8 s. `uTravel` advances with player speed + flow, so tunnels/fields rush with the ship. `--style N` pins a style for capture/smoke. All styles ~115 fps. | `Render/BackdropGenome.cs`, `Shaders.BackgroundFrag`, `Renderer.RerollBackdrop` |
| Racer feel (V7) | Ship top speed 312 -> 560 with momentum (accel 6.5, glide 2.6), dash 1700; player shots 1550 speed. Camera leads 0.24 s of velocity and zooms out up to 14% at speed. Enemy fire ~half the bullets at `World.BulletPace` = 1.75x speed, life capped 4.2 s; beat surge amplitude lowered to match. Enemies move 1.35x, dashers 1100; spawns 950-1350 from the player. FLOW meter (`World.Flow`): builds above 70% speed, on grazes (+0.03) and kills (+0.035), drains when slow, x0.35 on hit; score x(1 + 1.5 flow); "FLOW STATE" at 0.85. Visuals: composite speed lines, twin engine light trails, ship afterimages in flow/dash, starburst kills. Capture scenario `speed`. | `World.Player.cs`, `World.cs UpdateFlow`, `WorldRenderer.DrawSpeedFx` |
| Readability (V8) | Backdrop gets a palette ambient wash where it is dark, a highlight soft-clip (`col*1.05/(1+col*1.8)`), milder vignette; arena floor plate alpha 0.28. Enemies: collision radius x1.3 (non-boss), model scale 1.4, hue-lit hulls, threat halo + outline ring. Camera BaseZoom 1.0, speed zoom-out 7% (min 0.82). Speed lines only at the screen edges. Capture scenario `roster` = one of each enemy around a parked ship. | `Shaders.BackgroundFrag`, `WorldRenderer.DrawEnemies` |
| Audio analysis (V9) | `SpectrumAnalyzer` rewritten: every band in dB normalised to its own adaptive range (fast-attack ceiling, slow floor, min 6 dB), log-spectral flux, separate kick/snare/hat onset detectors (`AudioState.KickHit/SnareHit/HatHit` + pulses) that punch through their bands, spectral flatness, adaptive spectrum bars. Clipped 20x audio: 3% frames pinned (was 88%), bass std 0.10 (was 0.017). `--beatbench --dynamics` measures it. | `Audio/SpectrumAnalyzer.cs` |
| SFX (V9) | `SoundBank` rewritten: per-voice filter state, layered synthesis (pitch-drop shots, inharmonic metal hits, sub+rumble+crackle explosions, band-passed whoosh dash, FM bells/zaps), per-trigger pitch variation, Schroeder stereo reverb, soft-clipped bus. `--sfxdump` writes every effect to `capture/sfx/*.wav` with peak/rms. | `Audio/SoundBank.cs`, `Tools/SfxProbe.cs` |
| Hype + trails (V9) | `World.Hype` (flow, combo, time since hit) scales every music effect via `Renderer.HypeScale` (0.55x..1.45x); HYPE readout and level-up callouts. Enemy bullets have two-layer comet tails scaled by speed. | `World.cs` UpdateHype, `WorldRenderer.DrawBullets` |
| Enemies + song lasers (V9) | New roles Weaver (mines), Mortar (`Strike` telegraphs, detonate 2 beats later), Phantom (teleports on beats), Prism (560-unit rotating blade); join from waves 4/6/6/8. Song lasers on downbeats from director cues (`World.SetMusicCue`): drop STARBURST, build SCAN, high-energy GATE/CROSS, none in breakdowns; 2-beat warnings. Capture scenarios `roster` (all 11 enemies), `songlasers`. | `World.Enemies.cs`, `World.cs` UpdateSongLasers |
| Generative backdrops (V10) | `BackdropGenome` now assembles 1–3 layers of random transform chains (11 ops) feeding 13 animated generators with random blend/gain/palette; uploaded as `Layer` uniform structs and interpreted by `applyOp`/`runGen` in `BackgroundFrag`. Raymarched generators base-layer only. TASK-007's `DrawBackdrop3D` removed. Tools: `--capture backdrop`, `--reel N`, `--style N` (backdrop seed), `NULLWAVE_BENCH=1`. See `.game-studio/tasks/TASK-008.md`. | `Render/BackdropGenome.cs`, `Shaders.BackgroundFrag` |
| Gravity wells, melody rails, hunting enemies (V14) | `World/World.MusicFields.cs`: `GravityWell` (spawn on `ChordChanged`/held `HarmLow`, `WellPull` bends bullets, core absorbs, ship slingshot via `SlingCharge/SlingBoost`, `CollapseWells` on `OnMusicDrop`) and `MelodyRail` (grown from `MelodyPitch`/`MelodyConfidence`, riding → `RailBoost`, crossing bullets get `Bullet.Tuned` and pass a riding ship; spatial hash). Player top speed × (1 + 0.45 rail + 0.4 sling). `UpdateEnemyBrain`: per-role chase styles, `EnemyBaseSpeed` 175, `ChaseBoost(dist)` up to 4.4×. `SynthVisual` now fakes chords/bass/melody for captures and tests. See `.game-studio/tasks/TASK-019.md`. | `World/World.MusicFields.cs`, `World/World.Enemies.cs`, `Render/WorldRenderer.cs` |
| Music understanding (V13) | `Audio/MusicAnalyzer.cs`: HPSS (drums vs. melody, `PercLow..High`/`HarmLow..High`), chroma → chord (`ChordRoot/Minor`) and key (`Key`), timbre (`Brightness`, `TextureChanged`), beat-synchronous structure (`SectionChanged/Label/Repeat`, per `SongEpoch`). Renderer: `uPerc`/`uHarm`/`uChordShift`/`uBright`; `SectionWorldMemory` brings a section's world back when it repeats. Sections: first-beat nearest-twin novelty on a kick-aligned grid; buffered sources run a second `MusicAnalyzer` on `LookaheadAudioBuffer.CopyAheadWindow` and fire at the boundary's audio frame. Galaga arrivals / screen-holding enemies were reverted (owner: unplayable). See `.game-studio/tasks/TASK-017.md`. | `Audio/MusicAnalyzer.cs`, `Audio/AudioEngine.cs`, `Render/SectionWorldMemory.cs` |
| Music-shaped 3D worlds + Spotify buffer (V12) | Backdrop = raymarched organic world under the arena seen through the game camera (`uCam`/`uView`/`uOblique`): 6 worlds × 4 atmospheres, geometry driven by `uSpec[16]` (Renderer.ComputeBackdropSpectrum), `uBeatPos`, world-anchored `uWaves`; readability pools `uFocus[16]` under ship/enemies. Spotify buffer: `AppAudioRouter` (undocumented AudioPolicyConfig, as EarTrumpet) routes Spotify to a silent output, `LiveStreamSource` feeds the 0.75 s `LookaheadAudioBuffer`; routing undone on switch/exit/crash. See `.game-studio/tasks/TASK-015.md`, `TASK-016.md`. | `Shaders.BackgroundFrag`, `Audio/AppAudioRouter.cs`, `Audio/AudioEngine.cs` |
| Tests | New suites: model mesh validity, beat-grid firing (0 off-grid volleys), layouts, health display. New capture scenario `hurt`. | `Tools/SelfTest.cs` |

Re-measured after V2/V3 (V3 also: `--audiotest --app self` PASS; `--audiotest --app <silent app>` correctly FAILs with external=False, which proves isolation): `--selftest` PASS (58054 assertions), `--smoke 15` 64 fps, 0 warnings.

**V2 gotchas**
* World meshes use an oblique orthographic matrix (`Renderer.Oblique` = screen lift per unit of height). Collision is the z = 0 footprint; use `Lift()` in `WorldRenderer` to place 2D overlays on top of a model.
* Draw order inside the scene: background shader -> backdrop meshes (depth) -> 2D alpha batch (floor plate, shadows) -> world meshes (depth cleared) -> 2D glow batch (bullets, hitbox, FX stay on top).
* The backdrop mesh pass (`Renderer.Backdrop`, perspective camera) is still wired but nothing draws into it since V3. The backdrop camera mirrors x because game y points down the screen; do not "fix" the `CreateScale(-1,1,1)`.
* Process-loopback activation must run on an MTA thread and the completion handler must implement IAgileObject; there is no mix format, so the capture asks for 48k float with AUTOCONVERTPCM.
* `Models.Monolith(o)` is cached per obstacle via `ConditionalWeakTable`.

## 1c. Round 7 (2026-11-20): setlists, ships, THE DROP, recap, tutorial, rumble, EDM show

See `.game-studio/tasks/TASK-020.md` for the details and `CHANGELOG.md` for the player-facing list.

| feature | where |
|---|---|
| track detection, setlist stages, song records | `Audio/AudioEngine.cs` (`UpdateTrack`), `World/World.Setlist.cs`, `World/Meta.cs` |
| instrument ships | `World/World.Ships.cs` (`ShipKind`, `ShotStyle`, `FireWeapon`) |
| in-key notes | `World/World.Notes.cs`, `Audio/SoundBank.cs` (`PlayNote`) |
| THE DROP | `World/World.SongBoss.cs` |
| run recap, highlight clips | `World/RunTimeline.cs`, `Render/ClipRecorder.cs`, `UI/Screens.cs` (`DrawRecap`) |
| tutorial & tips, rumble | `Core/Coach.cs`, `Core/Game.cs` (`RumbleFor`), `Core/InputState.cs` (`SetRumble`) |
| EDM show | `Render/Renderer.cs` (`EdmFxFor`, `UnrollScreen`), `Render/Shaders.cs` (`stageLasers`, kaleidoscope, roll, letterbox) |

Release builds while the game is running: build to another folder (`-p:OutDir=...`), because the running
game locks `bin/Release`.

---

## 2. Environment

| item | value |
|---|---|
| OS | Windows, Git Bash available |
| .NET | 8.0.428, vendored at `.dotnet/dotnet.exe` |
| GPU | NVIDIA RTX 5060 Laptop |
| GL | 4.1.0 NVIDIA 617.42 |
| ffmpeg | 9.0.2 gyan build, on `PATH` (`h264_nvenc` verified working) |
| Python | `python` on PATH, stdlib only (no numpy/PIL) |
| Node | `C:\Program Files\nodejs\node` if needed |

## 3. Build and run

```bash
# build (incremental, what to use for compile checks)
./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo

# run the game normally
./run.cmd
```

`run.cmd` just forwards arguments:

```bat
@echo off
cd /d "%~dp0"
".dotnet\dotnet.exe" run --project "src\Nullwave\Nullwave.csproj" -c Release -- %*
```

### Modes

| command | what it does |
|---|---|
| `./run.cmd` | play |
| `./run.cmd --selftest` | headless assertion suite (~7.6k checks) |
| `./run.cmd --audiotest` | loopback capture + BPM detection report |
| `./run.cmd --smoke 20` | fixed-length run, prints fps/wave/bpm |
| `./run.cmd --capture <scenario> --out screenshots/x.png` | deterministic screenshot |
| `./run.cmd --record 50 --wave 8 --video out.mp4 --wav out.wav` | offline recorder |

Capture scenarios: `title, archive, combat, boss, laser, attune, pause, gameover`, plus `backdrop`,
`drop`, `breakdown`, `roster`, `speed`, `songlasers`, `overdrive`, and `edm` / `edmbackdrop` (a scripted
six-second build-up into a drop; use `--reel 10 --out x.mp4`). Captures run ~3 s of updates before the
first rendered frame; scripted scenarios start their clock on `Renderer.RenderedFrames`.

---

## 4. Source layout

```
src/Nullwave/
  Program.cs            entry point, window, all launch modes, recorder
  Core/                 Game.cs (orchestrator), InputState, MathX, Rng
  Audio/                AudioEngine (WASAPI loopback + tap), AudioState, Fft,
                        SpectrumAnalyzer, SoundBank (synths), SynthScore (generative
                        fallback), AudioTest, SynthVisual
  World/                World.cs + partials (Player/Enemies/Collisions/Director),
                        Entities, Attunements, Meta, Pilot
  Render/               Renderer, WorldRenderer, PostFx, Batch, GlFbo, Shader(s),
                        PixelFont, Palette
  UI/Screens.cs         every screen and the HUD
  Tools/SelfTest.cs     assertion suite
  Util/PngWriter.cs
tools/dotnet-install.ps1
capture/                recorder output and helper scripts
screenshots/            still captures
```

Key APIs if you write more tooling: `Game.StartSmokeRun(ulong seed)`, `Game.ActiveWorld`,
`World.BeginWave(int)` (public), `Game.Audio`, `Renderer.Width/Height`, `Renderer.ReadPixelsInto(byte[])`.

---

## 5. The recorder (`--record`)

Why it exists: **ffmpeg cannot capture system audio on this machine.** `ffmpeg -list_devices true -f
dshow -i dummy` lists microphones only — there is no Stereo Mix or loopback dshow device. So instead
of screen-capturing, the game reads its own GL backbuffer and taps its own WASAPI loopback stream.

Pipeline:

1. `Program.cs` spawns `ffmpeg` reading `rawvideo rgba` from stdin at 1600×900/60, encoding
   `h264_nvenc` (`-preset p4 -rc vbr -cq 21`), `+faststart`.
2. `AudioEngine.StartRecording(path)` opens a parallel WAV tap (`pcm_f32le`, 48 kHz, stereo) fed from
   the same `OnCaptureData` callback, drained from `Update`/`Dispose`.
3. On each tick the game renders, `Renderer.ReadPixelsInto(_frameBuffer)` (static buffer — do not
   allocate per frame, it is 345 MB/s of traffic), writes frames to ffmpeg's stdin.
4. Frames are gated by **wall clock**, not tick count: target frames = `elapsed * 60`, write up to 3
   duplicates to catch up. Without this the video drifts short against the audio.
5. On finish: close stdin, wait for ffmpeg, `StopRecording()`, write the WAV.

**Arming:** the recorder waits for `Game.Audio.State.External == true` (i.e. it hears something that
isn't its own output) or 45 s. So the correct order is: **start the audio source first, then launch
the game** — it arms in ~0.6 s and records the real signal.

**Mux** (the captured video has no audio track of its own):

```bash
ffmpeg -y -i take.mp4 -i take.wav -c:v copy -c:a aac -b:a 192k -shortest \
  -movflags +faststart take_mux.mp4
```

The recorder runs with an invisible window (`offscreen` flag), so it never steals focus or occludes
anything.

---

## 6. Gotchas

* **Never kill servers or processes by pattern.** `pkill -f` / `pgrep -f` can match the agent's own
  command line. Kill by PID. On Windows a bash `kill` may silently fail on a native Python process —
  use `taskkill //F //PID <pid>` (double slashes in Git Bash).
* **No loopback dshow device** — that is *why* the recorder exists. Don't try `-f dshow` for system audio.
* **Recorder ghosting:** `PostFx` composites without resetting blend state. Rendering twice per captured
  frame left the previous HUD visible. The recorder renders exactly once per frame; the underlying
  blend-state leak is still in `PostFx.cs` (~line 94) and only affects multi-render-per-frame paths.
* **Frame pacing:** tick-count pacing drifts. Gate on wall clock and duplicate frames to catch up.
* **`_frameBuffer` must stay static.** Allocating the RGBA buffer per frame costs 345 MB/s of GC churn
  at 1600×900/60.

---

## 7. Verification

```bash
./run.cmd --selftest
./run.cmd --audiotest
./run.cmd --smoke 5

# captured media integrity
ffmpeg -v error -i capture/take_mux.mp4 -f null -        # must be silent, exit 0
```

---

## 8. Open work

1. **Human playtest is unverified.** Mouse aim, controller input, and a real external-app loopback
   test (outside the browser) have never been exercised by a person. Round 7 adds: ship feel and
   balance, THE DROP's difficulty, setlist pacing on a real playlist, the EDM show at full CAMERA FX,
   rumble on a real pad, and clip saving with ffmpeg.
2. **No version control.** Consider `git init` and an initial commit before further changes. `.gitignore`
   currently covers `.dotnet/`, `bin/`, `obj/`, `build/`, `screenshots/`, `*.user`, `*.log`, `tools/nuget/`
   — it does **not** exclude `capture/*.mp4` or `capture/*.wav`, which can grow to hundreds of MB.
   Ignore those before committing.

---

## 9. Ground rules that were followed

* Pure C#, no engine, no model assets — keep it that way.
* Endless replayability and audio-reactivity from any app are the core pitch; don't regress them.
* Prefer editing the existing code over adding parallel paths; the recorder lives entirely in
  `Program.cs` plus the tap in `AudioEngine.cs`.
