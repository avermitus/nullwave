# Changelog

All notable gameplay, rendering, audio-intelligence, and UI updates to **NULLWAVE** are documented in chronological order from the original V1 foundation through the latest feature and polish release.

---

## Latest Updates — Lookahead Audio & Local Track Player, Drop Overdrive, High-Intensity Backdrops, Harmonic Overclock & Abyssal Theme

### Rendering, Visual Themes & Visual Effects
- **High-Intensity Reactive Backdrop Upgrade:** Supercharged the real-time combinatorial GPU backdrop synthesizer across all 13 generators and space transforms with live **Overdrive** visual coupling (`Drop Overdrive` & `Sync Overdrive`), punchier kick/snare/hi-hat/drop audio drives, beat-locked global spatial breathing and snare angular torque, progressive **3-plane layer parallax** (`layer 0 / 1 / 2`), overdrive palette cycling, and beat-synced outward **harmonic sonar waves** that ignite existing pattern ridges without flooding flat glare. Upgraded **Standard** tone-mapping for bolder neon peaks with crisp dark valleys, and tightened **Abyssal** ridge gating so kicks, drops, sonar waves, and dynamic combat lights carve razor-sharp electric neon contours out of pitch-black OLED obsidian — cleanly dampened when `REDUCE FLASHING` is enabled or `BACKGROUND MOTION` is lowered.
- **GPU Radial Shockwave Refraction Distortion:** Heavy combat shockwaves (musical drops, **Resonance Cascade**, **THE CHOIR** phase breaks and kills, and **Sync Overdrive**) now project up to **4 simultaneous annular screen-space refraction rings** with subtle chromatic crest fringing in the post-processing composite pass — gated automatically by `REDUCE FLASHING` and `SCREEN SHAKE` settings.
- **Abyssal / Void Noir Dark Theme & Dynamic Combat Illumination:** Added a selectable **VISUAL THEME** (`STANDARD` / `ABYSSAL`) in **SETTINGS** (and `--theme <standard|abyssal>` CLI override) while keeping `STANDARD` untouched. In **ABYSSAL** mode, the arena drops to near-OLED obsidian (`#010104`) between beats, audio transients (`kick`, `snare`, `drop`) ignite razor-thin bioluminescent contours, and up to **12 live combat light pools** (player interceptor + Phase Sync flare, **THE CHOIR** core, active shockwaves, and laser beams) dynamically illuminate the underlying generative shader geometry right around the fight, paired with matte-obsidian 3D hulls, heightened Fresnel rims, tighter bloom threshold, brighter beat sonar ripples, and smoked-glass HUD panels.
- **Fully Generative Multi-Layer Animated Backdrops:** Replaced the fixed 6-style backdrop roster with a combinatorial real-time GPU backdrop synthesizer (`BackdropGenome` + `BackgroundFrag`). Every run, act shift, and musical drop assembles a brand-new backdrop from scratch out of **1–3 layers**, each chaining **0–4 motion/space transforms** (`SPIN`, `TUNNEL`, `FOLD`, `WARP`, `SWIRL`, `MIRROR`, `SPIRAL`, `STREAM`, `RIPPLE`, `TILES`, `BREATHE`) into **13 animated pattern generators** (`INK`, `CELLS`, `FRACTAL`, `CIRCUIT`, `MOIRE`, `STREAMS`, `GRID`, `LIQUID`, `FIELD`, `PLASMA`, `AURORA`, `HEX`, `HYPERSPACE`) with randomized blend modes, per-backdrop cosine palette families, and seamless `1.8s` radial bloom transitions.
- **Per-Generator Drum, Snare & Hi-Hat Reactivity:** Connected real-time kick, snare, hi-hat, bass, high, and drop drives directly into all 13 generators and space transforms (`WARP`, `SWIRL`, `RIPPLE`, `BREATHE`), upgrading `PLASMA` into crisp multi-band harmonic contour ridges and tuning the drifting ambient fog floor and highlight soft-clipping so foreground combat stays razor-sharp against every generated world.
- **Aim-Reactive Parallax & Subtle Camera Tilt:** Added smoothed aim-driven oblique camera lean across 3D world meshes and lifted overlays (keeping `z = 0` collision footprints and mouse aiming exact) paired with subtle aim parallax in the generative backdrop shader.
- **Richer Bullet & Laser Trails:** Upgraded player bolts with wider bloom sheaths, velocity-stretched core streaks, and plasma spark nodes; strengthened enemy bullet contrast halos with longer dual-layer comet ribbon tails and shimmer nodes; and upgraded laser beams with warmup boundary guide rails, 4-layer HDR active beams (bloom sheath, neon body, beat-pulsing harmonic side rails, white-hot core), 8 high-speed directional chevrons, and glowing emitter/endpoint flares.
- **Composite & UI Alpha Hygiene:** Clean OpenGL blend-state and texture-unit restoration after post-processing, plus alpha-preserving UI blending so screenshots and captured video frames remain fully opaque.

### Gameplay, Audio & Choreography
- **Lookahead Audio Buffer (`+0.75s`) & Local Track Player:**
  - **Built-In Local Track Playback (`--track <path>` / Drag-Onto-Exe):** Play local `.mp3`, `.wav`, `.m4a`, or `.aiff` music files directly inside NULLWAVE with automatic `48 kHz` stereo resampling, seamless looping, clean fallback to the built-in generative score if a file is missing, and a `LOCAL TRACK (+0.75S)` / `TRACK // <NAME>` readout on the Title screen, HUD, and Pause overlay.
  - **Sample-Accurate `0.75s` Lookahead & Zero-Latency SFX:** Buffers both the local track player and built-in generative score `0.75s` ahead of speaker playback while mixing all combat SFX post-delay (zero added input-to-SFX latency), compensating for FFT/frame latency so kicks, snares, and hi-hats snap on the exact frame they hit your speakers. Live WASAPI system and per-app Spotify loopback stay real-time (`0 ms` delay).
  - **Pre-Drop Tension & Beat-1 Drop Snap:** Scans the `0.75s` lookahead window ahead of playback to detect incoming energy drops, driving an inward spatial convergence and contracting violet-cyan tension ring across the reactive backdrop and around your ship (dampened cleanly by `REDUCE FLASHING` and `BACKGROUND MOTION`) before snapping **Drop Overdrive** right onto beat 1 of the drop.
- **Drop Overdrive, 3× Phase-Sync Overdrive & Tactile Micro-Hitstop:**
  - **Drop Overdrive (`4.0s`):** Every musical drop now ignites a **Drop Overdrive** surge (`// DROP OVERDRIVE //`), granting **`+25%` fire cadence**, **`+18%` weapon damage**, **`1.5x` Flow gain**, and **`+0.50x` score multiplier**, with a rotating ship overdrive aura and live `DROP OD` HUD countdown bar.
  - **3× Phase-Sync Streak Overdrive (`5.0s`):** Chaining **3 consecutive on-beat dashes** triggers **`SYNC OVERDRIVE // 3x PHASE`**, awarding **`+30%` Resonance gain**, **`+15%` fire cadence** (stacking multiplicatively with Drop Overdrive), **`+0.35x` score multiplier**, a concentric cyan-gold shockwave, and a live `SYNC OD` HUD countdown bar (combined Flow+Overdrive multiplier up to `x3.35`). Off-beat emergency dodges remain completely unpenalized and only reset the streak counter.
  - **Tactile Micro-Hitstop:** Added crisp, bounded impact hitstop on **Elite kills** (`26 ms`), **Resonance Cascade** (`32 ms`), **THE CHOIR** phase breaks (`55 ms` + camera punch), and **Boss kills** (`85 ms`), paused cleanly so micro-hitstop never drains active Overdrive timers.
- **Harmonic Overclock (Attunement Synergy Evolutions):** Stacking complementary Attunement pairs to **Stack 2+** automatically awakens 4 signature combat evolutions, accompanied by an `"OVERCLOCK // <NAME>"` celebration shockwave, unlock chime, draft-card preview banners (`OVERCLOCK // <NAME>`), a solar-gold HUD pill row (`*SLV`, `*LNC`, `*RAL`, `*VPH`), and a centered Pause header badge:
  - **`SEEKER SALVO` (`SPL 2+` & `HOM 2+`):** Launches twin flanking micro-seekers (`±38°` arcs, high homing turn rate, `640`-unit lock range) with vivid magenta-cyan plasma ribbons.
  - **`PRISM LANCE` (`PRC 2+` & `RIC 2+`):** Wall and monolith ricochets supercharge bolts (`1.35x` damage, `+1` bonus pierce, `+12%` speed up to `2400`, `+0.35s` lifetime) into refractive solar-gold lances with prism spark bursts.
  - **`RAIL CADENCE` (`HVY 2+` & `RPD 2+`):** Every 4th volley fires a centerline hyper-velocity rail bolt (`2350` speed, `2.1x` damage, `1.45x` radius, `+2` pierce) with thick solar-amber plasma rails.
  - **`VOID PHANTOM` (`PHS 2+` & `GRZ 2+`):** Expands dash absorption and graze radii while releasing a `180`-unit damaging dash shockwave (`35` dmg) and a `135`-unit bullet-cancel ring that converts cleared enemy bullets into Resonance (`+2` base each), surrounded by a violet-cyan phantom halo and Abyssal light flare.
- **Expanded Song-Laser & Enemy Laser Choreography:** Expanded Drop `STARBURST` to **8 beams**, Build `SCAN` to **dual opposing sweeps**, and high-energy patterns (2-bar cadence from Wave 3+) to **4-beam `LASER GATE`**, **3-beam `LASER CROSS`**, and **4-beam rotating `PRISM CAGE`** while keeping breakdowns quiet and 2-beat warnings intact. **THE CHOIR** fires multi-beam sweeps across Phases II–IV, and Wave 3+ / Elite **Prism** units project twin opposing laser blades.
- **Zero-Config Local Spotify Now-Playing Integration:** Automatically detects live `Artist - Title` playback from Spotify desktop or browser Web Player tabs (zero API keys required), enriching audio source readouts and displaying `SPOTIFY // <TRACK>` on the Title screen, HUD, and Pause overlay while logging played tracks in run history.
- **Phase Sync (On-Beat Dash Synergy):** Initiating a dash within `±65 ms` of the beat boundary triggers **Phase Sync**, granting `+5%` Flow, `+5` base Resonance, an `18%` faster dash cooldown recharge, `+120` bonus score, `0.30s` invulnerability, a cyan/gold expanding harmonic shockwave ring, ship afterimages, and a `PHASE SYNC +RES` readout above the Flow meter. Off-beat emergency dodges remain completely unpenalized.
- **Choir Boss Phase-Transition Choreography:** Crossing phase thresholds on **THE CHOIR** (across 3 standard phases or 4 phases with **BLIND CHOIR**) triggers a `580`-unit fair bullet-clear shockwave ring that converts nearby enemy bullets into sparks, dual concentric phase-break shockwaves, beat-synced attack telegraph rings around the boss, and a 3rd counter-tilted halo + accelerated shard ring in Phase IV.
- **Interactive Archive Seed Replay & Custom Entry:** In **ARCHIVE OF THE SIGNAL**, cycle through recorded run seeds (`R`), enter any custom 8-character base-34 seed code (`C`, automatically normalizing `I → 1` and `O → 0`), inspect historical best wave/score/run counts in the Signal Decoder console, and launch directly into a run (`L`) with active modifiers.
- **Preset & Showcase CLI Options:** Added `--mods <hard|all|csv>`, `--attune <showcase|synergy|csv>`, and `--seed <code>` options for smoke runs and video recording alongside Phase-Sync-aware autopilot dash timing.

### HUD, UI & Progression
- **Personal-Best Callouts & Flight Recorder (`SIGNAL LOST` & `ARCHIVE`):** Persisted `BestCombo` (`BEST CHAIN`) and a rolling 8-run `RECENT SIGNALS //` history strip in the Archive. Game Over compares your run before updating career records, highlighting broken records with pulsing `NEW BEST` stat tags and delta banners (`NEW RECORD // SCORE +... // WAVE +... // CHAIN +...`).
- **Live Attunement Loadout Strip:** Compact 3-letter signal badges (`SPL`, `FAN`, `PRC`, `RPD`, `HVY`, `LNG`, `HOM`, `RIC`, `RER`, `SPH`, `PHS`, `GRZ`) with stack pips (`1..4`) rendered above `INTEGRITY` on the HUD and inside the `SIGNAL HELD` pause overlay, with an `ATTUNEMENT LOADOUT` toggle in **SETTINGS**.
- **Boss Health Bar Subtitles & Phase Pips:** Extracted boss bar with phase-specific subtitles (`OVERTURE`, `HARMONIC CONVERGENCE`, `NULL RESONANCE`, `BLIND CHOIR`), smoothed ghost-health drain, phase divider pips, audio-reactive border glow, and `// PHASE SHIFT //` / `INCOMING VOLLEY` status callouts.

![Generative Animated Backdrops Contact Sheet](docs/screenshots/generative_backdrops.png)

| Drop Overdrive & Shockwave Refraction (Abyssal) | High-Intensity Reactive Backdrop (Standard) |
|---|---|
| ![Drop Overdrive Abyssal](docs/screenshots/drop_overdrive.png) | ![Reactive Backdrop Standard](docs/screenshots/reactive_backdrop_standard.png) |
| **Harmonic Overclock — Synergy Evolutions** | **Abyssal Theme — Dynamic Combat Illumination** |
| ![Harmonic Overclock](docs/screenshots/synergy_overclock.png) | ![Abyssal Combat](docs/screenshots/abyssal_combat.png) |
| **Abyssal Theme — Choir Boss Chiaroscuro** | **Abyssal Theme — Starburst Lasers** |
| ![Abyssal Boss](docs/screenshots/abyssal_boss.png) | ![Abyssal Song Lasers](docs/screenshots/abyssal_songlasers.png) |
| **Title Screen** | **Combat HUD & Generative Backdrop** |
| ![Latest Title](docs/screenshots/latest_title.png) | ![Latest Combat](docs/screenshots/latest_combat.png) |
| **HDR Song Lasers & Starburst** | **The Choir Phase Choreography** |
| ![Latest Song Lasers](docs/screenshots/latest_songlasers.png) | ![Latest Boss](docs/screenshots/latest_boss.png) |
| **Phase Sync & Flow State** | **Signal Held Pause Overlay** |
| ![Latest Speed](docs/screenshots/latest_speed.png) | ![Latest Pause](docs/screenshots/latest_pause.png) |
| **Archive Seed Decoder & Flight Recorder** | **Signal Lost Personal-Best Callouts** |
| ![Latest Archive](docs/screenshots/latest_archive.png) | ![Latest Game Over](docs/screenshots/latest_gameover.png) |

---

## V9 — New Enemies, Adaptive Audio, Hype & Song Lasers

- **Four New Enemy Roles:** Added **Weaver** (serpentine approach, lays slow drifting mines on the beat), **Mortar** (lobs telegraphed area strikes on downbeats that detonate two beats later), **Phantom** (blinks to flanks on beats before firing), and **Prism** (projects a 560-unit rotating laser blade) joining from waves 4, 6, 6, and 8.
- **Adaptive Spectrum Analyzer:** Rewrote `SpectrumAnalyzer` in dB with per-band adaptive ceilings and slow floors, log-spectral flux, spectral flatness, and separate kick/snare/hat onset detectors so heavily mastered tracks stay dynamic instead of pinning at max.
- **Layered Synth SFX:** Rewrote `SoundBank` with per-voice filters, layered percussion/impact synthesis, per-trigger pitch variation, Schroeder stereo reverb, and a soft-clipped output bus.
- **Hype System, Comet Trails & Song Lasers:** Added the `HYPE` meter (scaling music reactivity from `0.55x` to `1.45x`), two-layer speed-scaled bullet comet tails, and downbeat-synchronized **Song Lasers** (`STARBURST` on drops, `SCAN` on builds, `LASER GATE` / `LASER CROSS` on high-energy choruses, and quiet space during breakdowns).

| Enemy Roster (All 11 Roles) | Bullet Comet Trails | Starburst Song Lasers |
|---|---|---|
| ![V9 Roster](docs/screenshots/v9_roster.png) | ![V9 Trails](docs/screenshots/v9_trails.png) | ![V9 Starburst](docs/screenshots/v9_starburst.png) |

---

## V8 — Combat Readability Pass

- **Backdrop & Foreground Separation:** Added dark-region ambient palette washes, highlight soft-clipping, a softer vignette, and lowered arena floor plate alpha (`0.28`).
- **Target Clarity at High Speed:** Increased non-boss enemy collision radii (`1.3x`) and 3D model scale (`1.4x`), lit hulls with hue tinting, added threat halos + outline rings, and constrained speed lines to screen edges.

![V8 Readability Comparison](docs/screenshots/v8_compare.png)

---

## V7 — Procedural Backdrops & Racer Flow

- **Six Procedural Backdrop Genomes:** Expanded `BackgroundFrag` to six distinct real-time shaders (`LIQUID` metaballs, `HYPERSPACE` polygon tunnel, `KALEIDOSCOPE` Kali-set fractal, `FIELD` raymarched geometry flythrough, `AURORA` ribbons, and `SHATTER` Voronoi) with smooth `1.8s` radial transitions on new runs, movement shifts, and musical drops.
- **Racer Handling & FLOW Meter:** Raised interceptor top speed to `560` with momentum acceleration/glide and `1700` dash bursts, velocity-leading camera zoom, faster enemy projectiles (`1.75x`), twin engine light trails, ship afterimages, starburst kills, and the `FLOW` score multiplier (`x2.50` / `// FLOW STATE //`).

| Six Procedural Backdrop Styles | Flow State & Speed FX |
|---|---|
| ![V7 Backdrop Styles](docs/screenshots/v7_styles_sheet.png) | ![V7 Speed](docs/screenshots/v7_speed.png) |

---

## V6 — Music Director

- **Real-Time Song Structure Analysis:** Added `MusicDirector` to track loudness in dB over a ~1-minute rolling range and detect **Intensity**, **Builds**, **Breakdowns**, and **Drops**.
- **Audio-Driven Grading & World Drops:** Connected song sections to YIQ hue rotation, saturation/contrast grading, radial kick thump, glitch slices, camera push-ins, ambient flow-field motes, and `OnMusicDrop` arena shockwaves (+20 Resonance).

| Breakdown Grading | Drop Surge & Shockwave |
|---|---|
| ![V6 Breakdown](docs/screenshots/v6_breakdown.png) | ![V6 Drop](docs/screenshots/v6_drop.png) |

---

## V5 — Beat Tracker & Beat-Synced Motion

- **Autocorrelation Beat Tracker:** Replaced naive transient counting with fixed-hop log-spectral flux autocorrelation, harmonic tempo scoring, a 14-beat comb phase filter with flip hysteresis, continuous `BeatPosition` tracking, and bass-voted downbeat detection.
- **On-Beat Combat Cadence:** Phase-locked the world clock to `BeatPosition`, added `BeatSurge` motion pulsing (normalized to `1.0x` average speed), and synchronized enemy firing telegraphs and volleys strictly to the beat grid.

![V5 Beat-Locked Combat](docs/screenshots/v5_combat.png)

---

## V4 — Full Settings Menu

- **Interactive Settings Screen:** Added full keyboard, gamepad, and mouse click/drag configuration across **Audio** (master/music/SFX volumes, audio source cycling, `±200 ms` beat latency offset, beat motion), **Video** (fullscreen, backdrop intensity/motion, bloom, screen shake, chromatic aberration, music FX, reduce flashing, FPS counter), and **Interface** (minimap, threat arrows, enemy health bars, beat telegraphs, crosshair size).

![V4 Settings Menu](docs/screenshots/v4_settings.png)

---

## V3 — Liquid Nebula Backdrop

- **Fluid Metaball Shader:** Replaced the V2 3D equalizer border pylons with a domain-warped flowing nebula and raymarched iridescent liquid metaball backdrop shader driven smoothly by bass, per-band energy, highs, and beat pulses.

![V3 Liquid Nebula Boss Fight](docs/screenshots/v3_boss.png)

---

## V2 — Procedural 3D Renderer, Bigger Arenas & Per-App Loopback

- **Pure-C# Procedural 3D Meshes:** Built `MeshBuilder` (`loft`, `prism`, `frustum`, `icosphere`, `torus`, `slab`) and an oblique orthographic 3D mesh renderer for the banking/barrel-rolling interceptor, 7 distinct enemy geometries, extruded polygon monoliths, and **THE CHOIR** boss with spectrum pillar crown and phase halos.
- **Expanded Multi-Layout Arenas:** Grew the arena from `3000×2000` to `5600×3600` across five procedural layouts (`SCATTER`, `HENGE`, `CITY`, `SPIRAL`, `LANES`).
- **HUD Overhaul & Per-App Audio Capture:** Added the segmented `INTEGRITY` bar with ghost-damage drain, under-ship health pips, enemy health bars, minimap, off-screen threat arrows, and Windows 10/11 WASAPI process-loopback capture (`F3` / `--app`) to isolate music from any single application.

| V2 3D Combat | V2 Choir Boss |
|---|---|
| ![V2 Combat](docs/screenshots/v2_combat.png) | ![V2 Boss](docs/screenshots/v2_boss.png) |

---

## V1 — Foundation

- **Core Twin-Stick Loop:** Launched the pure-C# OpenGL/NAudio foundation with fixed `60 Hz` simulation, WASAPI system loopback + generative fallback synth score, 2D layered vector monoliths, diamond integrity pips, 12 stackable Attunements, 16 unlockable Archive modifiers, Daily Signals, and local JSON progression.

![V1 Foundation Combat](docs/screenshots/v1_combat.png)
