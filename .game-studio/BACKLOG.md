# NULLWAVE — Prioritized Studio Backlog

Candidates are scored on a 1–5 scale for **Player Impact** (higher is better), **Implementation Risk** (lower is safer), and **Effort** (1 = small/focused, 5 = large rewrite), with explicit dependencies.

| Rank | ID | Initiative | Player Impact | Risk | Effort | Dependencies | Status |
|---:|---|---|---:|---:|---:|---|---|
| **1** | `TASK-001` | **Render State Hygiene + Flight Recorder Telemetry & Personal Best Callouts + Studio Visual Assets** | **5 / 5** | **1 / 5** | **2 / 5** | None | **Done** |
| **2** | `TASK-002` | **Live Attunement Loadout Strip on HUD & Pause Overlay** | **4 / 5** | **1 / 5** | **2 / 5** | `TASK-001` | **Done** |
| **3** | `TASK-003` | **Interactive Seed Entry & Seed History Replay in Archive** | **4 / 5** | **2 / 5** | **2 / 5** | `TASK-001` | **Done** |
| **4** | `TASK-004` | **On-Beat Dash / Rhythm Synergy Bonus ("Phase Sync")** | **4 / 5** | **2 / 5** | **3 / 5** | None | **Done** |
| **5** | `TASK-005` | **Choir Boss Phase-Transition Choreography & Audio-Reactive Telegraph Polish** | **3 / 5** | **2 / 5** | **3 / 5** | None | **Done** |
| **6** | `TASK-006` | **Local Spotify Integration & Hardcore Riddim Drop-Trailer Recorder Hooks** | **5 / 5** | **2 / 5** | **2 / 5** | `TASK-002`, `TASK-004`, `TASK-005` | **Done** |
| **7** | `TASK-007` | **Aim-Reactive 3D Backdrop Parallax & Camera Tilt, Richer Laser/Bullet Trails, and Expanded Laser Choreography** | **5 / 5** | **2 / 5** | **3 / 5** | `TASK-005` | **Done** |
| **8** | `TASK-008` | **Fully Generative Animated Backdrops + Per-Generator Audio Reactivity & Visual Harmony (TASK-008 + TASK-009)** | **5 / 5** | **2 / 5** | **3 / 5** | `TASK-007` | **Done** |
| **9** | `TASK-009` | **Abyssal / Void Noir Dark Theme + Dynamic Combat Illumination (`uLights[]`)** | **5 / 5** | **2 / 5** | **2 / 5** | `TASK-008` | **Done** |
| **10** | `TASK-010` | **Attunement Synergy Evolutions ("Harmonic Overclock")** | **5 / 5** | **2 / 5** | **3 / 5** | `TASK-002` | **Done** |
| **11** | `TASK-011` | **Drop Overdrive, Micro-Hitstop, Radial Shockwave Distortion, 3× Phase-Sync Overdrive & High-Intensity Reactive Backdrops** | **5 / 5** | **2 / 5** | **3 / 5** | `TASK-004`, `TASK-009` | **Done** |
| **12** | `TASK-012` | **Frame-Pacing Telemetry (`P95/P99` + Alloc Guard) & Seed PB Pace Splits** | **4 / 5** | **1 / 5** | **2 / 5** | `TASK-003` | Proposed |
| **13** | `TASK-013` | **Lookahead Audio Buffer & Local Track Player** | **5 / 5** | **2 / 5** | **3 / 5** | `TASK-011` | **Done** |
| **14** | `TASK-014` | **Stationary 3D-Only Reactive Backdrops & Combat Readability** | **5 / 5** | **2 / 5** | **3 / 5** | `TASK-011` | **Done** |
| **15** | `TASK-015` | **Map-Anchored Beat-Animated 3D Backdrops & Galaga Enemy Entry** | **5 / 5** | **2 / 5** | **3 / 5** | `TASK-014` | **Done (Galaga entry reverted)** |
| **16** | `TASK-016` | **Organic Music-Shaped Worlds, Spotify 0.75 s Buffer & Combat Readability** | **5 / 5** | **3 / 5** | **3 / 5** | `TASK-015` | **Review** |
| **17** | `TASK-017` | **Music Understanding (Drums/Melody, Chords, Key, Timbre, First-Beat Sections)** | **5 / 5** | **3 / 5** | **3 / 5** | `TASK-016` | **Review** |
| **18** | `TASK-018` | **Phase 2: AI Stem Separation & Neural Beat Tracking (ONNX, buffered sources only)** | **4 / 5** | **4 / 5** | **4 / 5** | `TASK-017` | Proposed |
| **19** | `TASK-019` | **Bass Gravity Wells, Melody Rails & Hunting Enemy Movement** | **5 / 5** | **3 / 5** | **3 / 5** | `TASK-017` | **Review** |
| **20** | `TASK-020` | **Setlists, Song Records, Instrument Ships, In-Key Guns, THE DROP, Run Recap, Tutorial, Rumble & the EDM Show** | **5 / 5** | **3 / 5** | **4 / 5** | `TASK-019` | **Review** |

---

## Detailed Backlog Breakdown

### 1. `TASK-001` — Render State Hygiene + Flight Recorder Telemetry & Personal Best Callouts + Studio Visual Assets
- **Problem:**
  1. `PostFx.Process` leaves OpenGL state dirty (`Blend` disabled, `ActiveTexture` left on `Texture3` with bloom textures bound on units 1–3), and `Renderer.Render` leaves additive blend active at frame end while UI alpha blending corrupts destination alpha (`< 1.0`) in the backbuffer.
  2. In `Game.EnterGameOver()`, `_meta.RecordRun(_world.Summary)` mutates `BestWave` and `BestScore` *before* `Screens.DrawGameOver()` renders, so achieving a new personal best is silently swallowed without celebration. Furthermore, `MetaProgress` never persists `BestCombo` or a rolling log of recent runs (`RecentRuns`), so players cannot review their recent signal history or see personal-best badges on `SIGNAL LOST` and `ARCHIVE`.
  3. The repository lacks official studio key art and vector/insignia plates under `docs/assets/` with a tracked `.game-studio/ASSET_MANIFEST.md`.
- **Solution (Vertical Slice):**
  - **Engineering (`@programmer`):**
    - Fix `PostFx.cs` and `Renderer.cs` blend/texture state hygiene (restore clean baseline state at the end of `PostFx.Process`, use `BlendFuncSeparate` for UI passes so destination alpha stays `1.0`, and restore standard alpha blend at frame exit) + add explicit `capture/*.mp4` and `capture/*.wav` rules to `.gitignore`.
    - Add `BestCombo`, `RecentRuns` (capped at 8 `RunRecord` entries), and personal-best detection (`RecordRunReport` / flags for New Best Score, New Best Wave, New Best Chain) to `MetaProgress`, surfacing celebratory `"NEW RECORD"` badges on `SIGNAL LOST` (`DrawGameOver`) and a `"RECENT SIGNALS"` flight-recorder telemetry readout in `ARCHIVE` (`DrawArchive`).
    - Add deterministic unit/integration assertions in `SelfTest.cs`.
  - **Art (`@artist`):**
    - Generate 3 cohesive NULLWAVE visual assets (`nullwave_key_art.png`, `choir_boss_emblem.png`, `pilot_flow_insignia.png`) in `docs/assets/` matching `Palette.cs` and document them in `.game-studio/ASSET_MANIFEST.md`.
  - **QA (`@qa`):**
    - Independent build, `--selftest`, `--smoke 5`, diff review, and asset verification in `.game-studio/QA_REPORT.md`.

---

### 2. `TASK-002` — Live Attunement Loadout Strip on HUD & Pause Overlay
- **Player Benefit:** During long runs (Waves 6+), players accumulate multiple stacks across 12 Attunements (`SPLIT`, `FAN`, `PIERCE`, `HOMING`, etc.) but have no visual readout of their current build on the HUD or `SIGNAL HELD` pause screen.
- **Scope:** Add a compact, non-intrusive attunement badge strip on the Pause card and optional HUD readout showing active attunement codes and stack pips (`1..4`).
- **Risk / Effort:** Low risk, UI-only read of `Player` attunement levels (`AttunementCatalog.GetLevel`).

---

### 3. `TASK-003` — Interactive Seed Entry & Seed History Replay in Archive
- **Player Benefit:** `MetaProgress.SeedRecords` already records the last 40 seeds played with `BestScore` and `BestWave`, and `SeedCode` converts 8-character codes (`0-9A-Z`), but players cannot type or select a favorite seed from the UI to replay a specific arena/palette seed.
- **Scope:** Allow selecting a seed from `RecentRuns` / `SeedRecords` in the Archive or entering an 8-character seed code on the Title screen.
- **Risk / Effort:** Low-to-moderate UI state addition in `Game.cs` and `Screens.cs`.

---

### 4. `TASK-004` — On-Beat Dash / Rhythm Synergy Bonus ("Phase Sync")
- **Player Benefit:** Enemies fire on the beat and bullets surge with `BeatPhase`, but player actions are currently rhythm-agnostic. Rewarding dashes timed within `±65ms` of `BeatTick` with a small Flow boost (`+0.04`), Resonance bonus (`+4`), and a crisp visual ring deepens the musical connection without punishing off-beat survival dodges.
- **Scope:** Optional positive-only rhythm window check in `World.Player.cs` when `BeatLocked` is active, with a subtle `PHASE SYNC` float text and `SelfTest` verification.

---

### 5. `TASK-005` — Choir Boss Phase-Transition Choreography & Audio-Reactive Telegraph Polish
- **Player Benefit:** Every 5th wave features **THE CHOIR** across 3–4 phases (`Mods.BossPhases`). Adding distinct phase-break shockwaves, brief bullet-clear rings on phase transitions, and phase-specific sub-titles (`PHASE II // HARMONIC CONVERGENCE`, `PHASE III // NULL RESONANCE`) elevates boss encounters into landmark run milestones.
- **Scope:** `World.Enemies.cs` boss phase transition hook + `Screens.cs` boss bar subtitle polish.


## Opt-in map experiment (2026-11-21)

HALO CIRCUIT and CATHEDRAL implemented on experiment/maps; arena remains default. Shared polygon geometry drives rendering, movement, navigation, spawning and minimap. Title MAP selection and --map support are available. See [TASK-MAPS-EXPERIMENT](tasks/TASK-MAPS-EXPERIMENT.md) for evidence and scope. Parent independent review/integration is pending; human balance testing remains follow-up.
