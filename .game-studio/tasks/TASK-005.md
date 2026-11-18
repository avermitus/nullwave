# TASK-005: Choir Boss Phase-Transition Choreography & Audio-Reactive Telegraph Polish

- **Status:** `done`
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer`
- **Owner (Art):** `@artist`
- **Reviewer (QA):** `@qa`

---

## 1. Goal & Player-Facing Benefit

1. **Choir Boss Phase-Transition Choreography & Fair Bullet-Clear Ring (`Entities.cs`, `World.cs`, `World.Enemies.cs`):**
   - Every 5th wave spawns **THE CHOIR** across 3 phases (or 4 phases with the `BLIND CHOIR` modifier). Previously, crossing a boss HP threshold silently incremented `BossPhase`, granted `1.1s` invulnerability, and spawned 3 Swarm units, while leaving dense Phase I/II enemy bullets lingering around the arena and providing no boss attack telegraph or distinct phase-break visual choreography.
   - `TASK-005` upgrades Choir phase shifts into landmark rhythmic encounters:
     - **Bullet-Clear Shockwave Ring:** Crossing a phase threshold clears enemy bullets within a generous `580` radius around the boss (converting cleared bullets into spark particles so phase transitions feel fair and dramatic, never cheap-shotting the player).
     - **Phase-Break Choreography & Invuln Pulse:** Tracks `PhaseBreakTimer` (`1.15s` dramatic shift window), `PhaseClearRadius` / expanding ring, `HitFlash`, `Flash`, audio cue, and clean `BossAttackBeats` reset (`1.25f`) so the new phase launches cleanly on the beat.
     - **Audio-Reactive Boss Attack Telegraph:** Computes `e.Telegraph` (`0..1`) over the final beat before `BossAttackBeats` triggers, giving players a clear visual + HUD warning before each Choir volley.
2. **Boss Bar Subtitles, Phase Pips & World Telegraph Polish (`Screens.cs`, `WorldRenderer.cs`, `Game.cs`):**
   - Extract `Screens.DrawBossBar(...)` with **phase-specific subtitles** (`PHASE I // OVERTURE`, `PHASE II // HARMONIC CONVERGENCE`, `PHASE III // NULL RESONANCE`, `PHASE IV // BLIND CHOIR`), phase divider pips showing completed/active/remaining phases, an audio-reactive border pulse (`BeatPulse` + `Telegraph`), and a `"PHASE SHIFT"` callout during `BossInvuln` / `PhaseBreakTimer`.
   - Enhance `WorldRenderer.DrawBoss(...)` with a beat-synced telegraph ring (`e.Telegraph > 0.05f`), a dramatic expanding phase-break shockwave halo during `PhaseBreakTimer > 0`, and a 4th-phase overdrive halo + accelerated shard ring when `e.BossPhase >= 3` (`BLIND CHOIR`).
3. **Studio Visual Asset (`docs/assets/choir_phase_choreography.png`, `.game-studio/ASSET_MANIFEST.md`):**
   - Create a cohesive `1600x900` (`16:9` opaque RGBA `A=255`) schematic/choreography plate illustrating `THE CHOIR // PHASE-TRANSITION CHOREOGRAPHY & TELEGRAPH MATRIX` across Phases I–IV.

---

## 2. Relevant Code, Assets & Ownership Constraints

### Strict File Ownership
- **`@designer` owns:** `.game-studio/BACKLOG.md` and `.game-studio/tasks/TASK-005.md` (Design & Task Specification sections).
- **`@programmer` owns:** `src/Nullwave/**` (`World/Entities.cs`, `World/World.cs`, `World/World.Enemies.cs`, `Render/WorldRenderer.cs`, `UI/Screens.cs`, `Core/Game.cs`, `Tools/SelfTest.cs`) and the **Engineering Handoff** section of `.game-studio/tasks/TASK-005.md`.
- **`@artist` owns:** `docs/assets/**` (`docs/assets/choir_phase_choreography.png`) and `.game-studio/ASSET_MANIFEST.md`.
- **`@qa` owns:** `.game-studio/QA_REPORT.md` and the **QA Findings & Verdict** section of `.game-studio/tasks/TASK-005.md`.

---

## 3. Implementation Plan

### Part A — Engineering Scope (`@programmer`)

#### A1. Boss Phase Model & Transition State (`World/Entities.cs` & `World/World.Enemies.cs`)
1. **In `src/Nullwave/World/Entities.cs` (`Enemy`):**
   - Add:
     - `public float PhaseBreakTimer;` (`> 0` during a phase transition burst, decaying to `0` in `UpdateBoss`)
     - `public float PhaseBreakDuration = 1.15f;`
     - `public int HighestTriggeredPhase;` (monotonic guard ensuring a phase transition never re-triggers for the same phase)
     - `public float BossHealthDisplay = 1f;` (optional smoothed ghost bar frac matching the player's `HealthDisplay` feel, decaying smoothly toward `HealthFrac`)
2. **In `src/Nullwave/World/World.Enemies.cs` (`UpdateBoss` & helpers):**
   - Add a clean static/public helper for phase metadata and threshold evaluation:
     - `public static string BossPhaseSubtitle(int phase, int totalPhases)` returning:
       - `0` -> `"OVERTURE"`
       - `1` -> `"HARMONIC CONVERGENCE"`
       - `2` -> `"NULL RESONANCE"`
       - `3+` -> `"BLIND CHOIR"`
     - `public static int ComputeDesiredBossPhase(float healthFrac, int totalPhases)` computing the phase index `0 .. totalPhases - 1` cleanly across `totalPhases` (`3` normally, `4` when `BLIND CHOIR` is active).
   - Add `public const float BossPhaseClearRadius = 580f;` (and `TriggerBossPhaseTransition(Enemy e, int newPhase)`):
     - Decays `e.PhaseBreakTimer = MathF.Max(0f, e.PhaseBreakTimer - dt)` and smoothly tracks `e.BossHealthDisplay`.
     - When `desiredPhase > e.HighestTriggeredPhase`:
       - Updates `e.HighestTriggeredPhase = desiredPhase;` and `e.BossPhase = desiredPhase;`
       - Sets `e.BossInvuln = 1.15f; e.PhaseBreakTimer = e.PhaseBreakDuration; e.HitFlash = 1f; e.BossAttackBeats = 1.25f;`
       - **Fair Bullet-Clear Ring:** Iterates `EnemyBullets` backwards (`for (int i = EnemyBullets.Count - 1; i >= 0; i--)`) and clears bullets within `BossPhaseClearRadius` (`580f`) of `e.Pos` (returning them to `_bulletPool` via `ReturnBullet` and spawning brief spark particles, zero heap allocations).
       - **Choreography Visuals & Audio:**
         - Triggers dual concentric shockwaves (`AddShockwave(e.Pos, BossPhaseClearRadius, 0.85f, 12f, M.Hsv(e.Hue, 0.65f, 1f, 1f), true)` + white inner ring `AddShockwave(e.Pos, 320f, 0.55f, 4f, Vector4.One)`).
         - Emits `$"THE CHOIR // {BossPhaseSubtitle(e.BossPhase, phases)}"` for `2.0f` seconds.
         - Triggers `Sfx?.Trigger(SfxType.Boss, 0.9f)`, `Cam.AddShake(12f)`, `Flash = MathF.Max(Flash, 0.35f)`, and spawns up to 3 Swarm escorts (`Enemies.Count < 28`).
   - **Boss Attack Telegraph (`UpdateBoss`):**
     - When `e.BossInvuln <= 0`: compute `e.Telegraph = e.BossAttackBeats <= 1.0f ? M.Clamp01(1f - e.BossAttackBeats + BeatPhase * 0.5f) : 0f;` (and reset `e.Telegraph = 0f` when firing or during `BossInvuln > 0`) so players can read impending Choir patterns.

#### A2. World Boss Rendering & Telegraph Polish (`src/Nullwave/Render/WorldRenderer.cs`)
- In `DrawBoss(Renderer r, GameWorld w, Enemy e, Vector4 hue, float scale, float flash, float time, AudioState audio)`:
  - Render an **audio-reactive boss telegraph ring** when `e.Telegraph > 0.04f && GameSettings.Current.BeatTelegraphs`: a closing harmonic ring from `R * 2.1f` down to `R * 1.15f` that brightens with `e.Telegraph` and `w.BeatPulse`.
  - Render **phase-break choreography** when `e.PhaseBreakTimer > 0f`: expanding twin rings and radial phase ticks radiating outward from `e.Pos` proportional to `1f - e.PhaseBreakTimer / e.PhaseBreakDuration`.
  - Support **Phase IV (`e.BossPhase >= 3`, `BLIND CHOIR`)**: render a 3rd counter-tilted `BossHalo` (`R * 1.82f`) in crimson-gold so 4-phase Choir fights have an unmistakable apex silhouette.

#### A3. Boss Bar Subtitles & Phase Pips (`src/Nullwave/UI/Screens.cs`)
- Refactor the inline boss bar block in `Screens.DrawHud` into `private static void DrawBossBar(Renderer r, GameWorld w, Palette p, Enemy boss, float time)`:
  - **Bounds Check (Verified):** `bx = 380, by = 34, bw = 840, bh = 18` (`x = 380..1220`, clear of top-left `x = 56..520` and top-right `BPM`/`AUDIO` at `x ~ 1380..1542`).
  - **Top Title (`y = 10`):** `"THE CHOIR // PHASE " + Roman(boss.BossPhase + 1) + " OF " + Roman(phases) + " // " + World.BossPhaseSubtitle(boss.BossPhase, phases)`
  - **Health Bar (`bx = 380, by = 34, bw = 840, bh = 18`):**
    - Audio-reactive border glow modulated by `w.BeatPulse` and `boss.Telegraph`.
    - Draining ghost chunk (`boss.BossHealthDisplay`) behind `boss.HealthFrac` so burst damage and phase breaks read clearly.
    - Clean vertical phase divider lines at `1/phases .. (phases-1)/phases`.
  - **Lower Phase Pips & Status Strip (`y = 58`):**
    - Centered phase pips (`phases` compact pill bars: completed phases dimmed, current phase pulsing bright `p.Danger` / `p.Accent`, upcoming phases dark framed).
    - When `boss.PhaseBreakTimer > 0.01f`, pulse `"// PHASE SHIFT //"` beside/under the bar; otherwise when `boss.Telegraph > 0.55f`, show a subtle `"INCOMING VOLLEY"` telegraph accent indicator.

#### A4. Capture Harness & Deterministic Self-Tests (`Core/Game.cs` & `Tools/SelfTest.cs`)
1. **In `src/Nullwave/Core/Game.cs` (`ConfigureCapture("boss")`):**
   - Advance the spawned Choir boss past its spawn timer (`boss.Spawning = false`), drop its HP into Phase II (e.g., `boss.Hp = boss.MaxHp * 0.54f`), and step `1` frame (or trigger the phase transition cleanly) so `--capture boss` deterministically showcases `PHASE II // HARMONIC CONVERGENCE`, the phase pips, ghost-health drain, and active Choir bullet/halo choreography!
2. **In `src/Nullwave/Tools/SelfTest.cs` (`TestBossPhaseChoreography`):**
   - Call `TestBossPhaseChoreography()` from `SelfTest.Run()`, verifying:
     1. `BossPhaseSubtitle` returns distinct, non-empty subtitles for phases `0..3` across 3-phase and 4-phase modes.
     2. **3-Phase Choir Transition:** Spawn a Wave 5 boss, spawn enemy bullets both inside (`dist < 580`) and outside (`dist > 700`) `BossPhaseClearRadius`, drop boss HP below `2/3` (`0.60 * MaxHp`), and step the world:
        - `BossPhase == 1`, `HighestTriggeredPhase == 1`, `PhaseBreakTimer > 0`, `BossInvuln > 0`.
        - Nearby enemy bullets inside `BossPhaseClearRadius` were cleared while far bullets outside survived.
        - Shockwave & phase-shift event text were emitted.
        - Stepping again at the same HP does **not** re-trigger phase 1 (`HighestTriggeredPhase` monotonic guard).
     3. **4-Phase `BLIND CHOIR` Transition:** Construct a world with `"blind-choir"` modifier (`Mods.BossPhases == 4`), verify 4 phase thresholds (`0.75`, `0.50`, `0.25`) transition through `BossPhase == 3` (`"BLIND CHOIR"`).

---

### Part B — Art Scope (`@artist`)

Create **1 new high-craft visual asset** (`docs/assets/choir_phase_choreography.png`) and append its full entry to `.game-studio/ASSET_MANIFEST.md`:

| # | Asset Filename | Purpose | Target Resolution | Visual Brief & Style |
|---|---|---|---|---|
| 7 | `docs/assets/choir_phase_choreography.png` | Choir Boss Phase Choreography & Telegraph Matrix (`TASK-005`) | `1600x900` (16:9 PNG, opaque RGBA `A=255`) | Technical boss encounter schematic & HUD choreography plate titled `THE CHOIR // PHASE-TRANSITION CHOREOGRAPHY & TELEGRAPH MATRIX`. Features a 4-stage progression across `PHASE I // OVERTURE`, `PHASE II // HARMONIC CONVERGENCE`, `PHASE III // NULL RESONANCE`, and `PHASE IV // BLIND CHOIR` showing the 3D faceted icosphere core, expanding spectrum pillar crown (`10..16` pillars), concentric rotating phase halos (`1..3` halos), orbiting crystalline shards, the `580U` crimson/white **Bullet-Clear Shockwave Ring**, closing beat-telegraph rings, and the top-center segmented Boss Health Bar with phase pips and ghost-drain telemetry in `Palette.cs` deep void navy (`#030510`), warning crimson (`#FF4052`), hyper magenta (`#FF38B8`), electric cyan (`#20F0FF`), and solar gold (`#FFD866`). |

**Art Deliverables:**
- Generate `docs/assets/choir_phase_choreography.png` (`1600x900`, opaque `RGBA` `A=255`).
- Visually inspect `docs/assets/choir_phase_choreography.png` with `read_file` to confirm clarity, alignment, and palette fidelity.
- Append full provenance (purpose, reference style, dimensions, perspective, transparency, palette, file format, verified path, SHA-256, and prompt/synthesis specification) to `.game-studio/ASSET_MANIFEST.md`.

---

## 4. Acceptance Criteria (`@qa`)

1. **Clean Release Build:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeds with **0 warnings and 0 errors**.
2. **Self-Test Suite Pass:** `./run.cmd --selftest` passes with `RESULT: PASS` and new assertions from `TestBossPhaseChoreography()`, with zero regressions in existing suites.
3. **Smoke Run Pass:** `./run.cmd --smoke 5` completes cleanly with `RENDERSTATE -> PASS` and high FPS.
4. **Visual Capture Verification:**
   - Run `./run.cmd --capture boss --out screenshots/boss.png` and inspect with `read_file` to confirm the updated Boss Health Bar (`PHASE II // HARMONIC CONVERGENCE`), phase pips, and Choir phase-transition/telegraph choreography render cleanly with zero HUD overlap.
5. **Asset Verification:**
   - Confirm `docs/assets/choir_phase_choreography.png` exists, is a valid `1600x900` opaque PNG, and is accurately cataloged in `.game-studio/ASSET_MANIFEST.md`.

---

## 5. Designer Notes (`@designer`)
- Clearing enemy bullets within `580` units on a Choir phase shift turns every phase break into a clean rhythmic reset ("breath before the storm") and prevents unavoidable overlap between old and new phase bullet patterns.
- Keeping all phase-bar subtitles, pips, and status labels within `x = 380..1220, y = 10..74` leaves the top-left score/wave cluster (`x = 56..520`) and top-right BPM/spectrum cluster (`x = 1118..1542, y = 40..188`) completely unobstructed.

---

## 6. Engineering Handoff (`@programmer`)

### Summary of Changes
1. **Choir Boss Phase Model & Fair Bullet-Clear Choreography (`src/Nullwave/World/Entities.cs`, `World/World.Enemies.cs`):**
   - Added `PhaseBreakTimer`, `PhaseBreakDuration` (`1.15f`), `HighestTriggeredPhase` (monotonic guard), and `BossHealthDisplay` (smoothed ghost HP drain) on `Enemy` (`Entities.cs`).
   - Added `World.BossPhaseClearRadius = 580f`, `World.BossPhaseSubtitle(int phase, int totalPhases)` (`OVERTURE`, `HARMONIC CONVERGENCE`, `NULL RESONANCE`, `BLIND CHOIR`), `World.ComputeDesiredBossPhase(float healthFrac, int totalPhases)`, and `TriggerBossPhaseTransition(Enemy e, int newPhase, int totalPhases)` (`World.Enemies.cs`).
   - On phase transitions (`desiredPhase > e.HighestTriggeredPhase`), clears enemy bullets within `580f` of `e.Pos` (returning them to `_bulletPool` via `ReturnBullet` + spark particles), spawns dual concentric phase-break shockwaves (`580f` hue ring + `320f` white ring), sets `BossInvuln = 1.15f`, `PhaseBreakTimer = 1.15f`, `HitFlash = 1f`, resets `BossAttackBeats = 1.25f`, emits `THE CHOIR // <SUBTITLE>`, triggers `SfxType.Boss`, screen shake, flash, and up to 3 Swarm escorts.
   - Computed `e.Telegraph` over the final beat before `BossAttackBeats` fires (when `BossInvuln <= 0f`).
2. **World Boss Rendering & Phase IV Halo (`src/Nullwave/Render/WorldRenderer.cs`):**
   - Updated `DrawBoss` to render a closing beat-synced telegraph ring (`e.Telegraph > 0.04f && GameSettings.Current.BeatTelegraphs`), expanding twin phase-break shockwave halos with 12 rotating radial ticks (`e.PhaseBreakTimer > 0f`), and a 3rd counter-tilted `BossHalo` + 18 faster orbiting shards when `e.BossPhase >= 3` (`BLIND CHOIR`).
3. **Boss Bar Subtitles, Ghost Drain & Phase Pips (`src/Nullwave/UI/Screens.cs`, `Core/Game.cs`):**
   - Extracted `Screens.DrawBossBar` (`x = 380..1220, y = 10..74`) displaying `"THE CHOIR // PHASE <Roman> OF <Roman> // <SUBTITLE>"`, smoothed ghost-health drain (`BossHealthDisplay`), audio-reactive border pulse, vertical phase dividers, centered phase pips (`y = 58`), and `"// PHASE SHIFT //"` / `"INCOMING VOLLEY"` status indicator.
   - Updated `Game.CaptureWarmup` for `CaptureScenario == "boss"` to cleanly showcase Phase II (`HARMONIC CONVERGENCE`), ghost drain, and phase-shift choreography during `--capture boss`.
4. **Deterministic SelfTest Coverage (`src/Nullwave/Tools/SelfTest.cs`):**
   - Added `TestBossPhaseChoreography()` to `SelfTest.Run()`, raising total assertions from `69,333` to **`69,349`**.

### Verification Evidence
- **Release Build:**
  ```text
  ./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo
  Build succeeded.
      0 Warning(s)
      0 Error(s)
  ```
- **Self-Test Suite (`--selftest`):**
  ```text
  ./run.cmd --selftest
  ...
    boss choreography: clearRadius=580 phase=2/3 teleMax=1.00 blindPhase=4/4
  RESULT: PASS (69349 assertions, 2.4s)
  ```
- **Smoke & Live GL State Verification (`--smoke 5`):**
  ```text
  ./run.cmd --smoke 5
  SMOKE | 5.0s fps=114 wave=1 score=482 enemies=3 bullets=4 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```
- **Visual Capture Verification (`--capture boss`):**
  - Visually inspected `screenshots/boss.png` with `read_file`; confirmed `THE CHOIR // PHASE II OF III // HARMONIC CONVERGENCE`, segmented HP bar, phase pips, `// PHASE SHIFT //`, and dual expanding clear-ring shockwaves render cleanly with zero HUD overlap.

---

## 7. Art Handoff (`@artist`)
*(Summary recorded in `.game-studio/ASSET_MANIFEST.md` and on the swarm board.)*

---

## 8. QA Findings & Verdict (`@qa`)

- **Verdict:** **PASS (`APPROVED — 0 DEFECTS`)** — Full details recorded in `.game-studio/QA_REPORT.md` (§9).
- **Independent Build Verification:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeded in `0.54s` with **0 warnings, 0 errors**.
- **Independent SelfTest Verification:** `./run.cmd --selftest` passed with **`RESULT: PASS (69349 assertions, 2.4s)`** (up from `69,333`, verifying `TestBossPhaseChoreography()` across 3-phase and 4-phase `BLIND CHOIR` threshold transitions, `580u` fair bullet-clear ring, dual shockwaves, monotonic `HighestTriggeredPhase` guard, and final-beat boss telegraph ramp).
- **Independent Smoke & Live GL State Verification:** `./run.cmd --smoke 5` passed at **`115 fps`** with **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
- **Code & Hot-Path Review:** Confirmed zero per-frame heap allocations in `TriggerBossPhaseTransition` (backward pool-returning loop), `WorldRenderer.DrawBoss`, and `Screens.DrawBossBar`.
- **Visual & Asset Verification:** Visually inspected `screenshots/boss.png` and `docs/assets/choir_phase_choreography.png` (`1600x900`, `1,669,230 bytes`, SHA-256 `3774F5D5973223A59E7B82E9F77B8CD5B85B33901E557211AFFD5EE999D6A3FF`), confirming zero HUD overlap and exact match with `.game-studio/ASSET_MANIFEST.md`.
