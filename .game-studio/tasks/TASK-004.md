# TASK-004: On-Beat Dash / Rhythm Synergy Bonus ("Phase Sync") + Phase Sync Telemetry Asset

- **Status:** `done`
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer`
- **Owner (Art):** `@artist`
- **Reviewer (QA):** `@qa`

---

## 1. Goal & Player-Facing Benefit

1. **On-Beat Dash / Rhythm Synergy Bonus ("Phase Sync") (`World.cs`, `World.Player.cs`, `Entities.cs`, `WorldRenderer.cs`, `Screens.cs`, `Game.cs`):**
   - In NULLWAVE, enemies fire on a phase-locked beat grid (`AdvanceBeatClock`) and bullets surge with `BeatPhase`, but the player's own movement mechanics were previously rhythm-agnostic.
   - `TASK-004` introduces **Phase Sync**: a purely positive rhythm-synergy bonus triggered whenever the player initiates a **Dash** within a forgiving on-beat timing window (`±65 ms` of the beat boundary `BeatPhase ~ 0.0 / 1.0`, capped at `0.18` beat phase).
   - **Player Benefit:** Dashing on the beat rewards musical mastery with an immediate **Flow boost (`+0.05`)**, **Resonance surge (`+5` base Resonance)**, **18% faster Dash cooldown recharge (`x0.82`)**, a **score bonus (`+120`)**, a crisp **`PHASE SYNC`** floating callout and expanding harmonic shockwave ring in world space, afterimage ghosting, and a live HUD/Flow-meter rhythm sync indicator — while off-beat emergency survival dashes remain 100% unpenalized.
2. **Phase Sync Rhythm Telemetry Visual Asset (`docs/assets/phase_sync_diagram.png`, `.game-studio/ASSET_MANIFEST.md`):**
   - Expand NULLWAVE's studio asset library with a `1600x900` (`16:9` opaque RGBA `A=255`) technical schematic plate illustrating the `PHASE SYNC // ON-BEAT DASH RHYTHM TELEMETRY` system (PLL beat-phase waveform window `±65 ms`, interceptor barrel-roll dash trajectory with afterimages, expanding harmonic shockwave ring, and `+FLOW / +RESONANCE / -18% CD` synergy vectors) in `Palette.cs` neon vector-mesh style.

---

## 2. Relevant Code, Assets & Ownership Constraints

### Strict File Ownership
- **`@designer` owns:** `.game-studio/BACKLOG.md` and `.game-studio/tasks/TASK-004.md` (Design & Task Specification sections).
- **`@programmer` owns:** `src/Nullwave/**` (`World/World.cs`, `World/World.Player.cs`, `World/Entities.cs`, `Render/WorldRenderer.cs`, `UI/Screens.cs`, `Core/Game.cs`, `Tools/SelfTest.cs`) and the **Engineering Handoff** section of `.game-studio/tasks/TASK-004.md`.
- **`@artist` owns:** `docs/assets/**` (`docs/assets/phase_sync_diagram.png`) and `.game-studio/ASSET_MANIFEST.md`.
- **`@qa` owns:** `.game-studio/QA_REPORT.md` and the **QA Findings & Verdict** section of `.game-studio/tasks/TASK-004.md`.

---

## 3. Implementation Plan

### Part A — Engineering Scope (`@programmer`)

#### A1. Phase Sync Timing Window & State Model (`World/Entities.cs` & `World/World.cs`)
1. **In `src/Nullwave/World/Entities.cs` (`Player`):**
   - Add:
     - `public int PhaseSyncs;` (total on-beat dashes in the current run)
     - `public float PhaseSyncFlash;` (`0..1` visual decay timer set to `1f` when Phase Sync triggers, decaying at `dt * 2.6f` in `UpdatePlayer`)
     - `public float LastPhaseSyncErrorMs;` (timing delta in `ms` of the most recent Phase Sync, e.g. `0..65f`, useful for HUD telemetry and `SelfTest`)
2. **In `src/Nullwave/World/World.cs` (`RunSummary` & `World`):**
   - Add `public int PhaseSyncs;` to `RunSummary` and `World` (copied into `Summary.PhaseSyncs` in `Die()`).
   - Define the Phase Sync timing window constants and evaluation helper on `World`:
     - `public const float PhaseSyncWindowMs = 65f;`
     - `public const float PhaseSyncMaxPhase = 0.18f;`
     - `public bool IsInPhaseSyncWindow(out float errorMs)`:
       - Computes distance to the nearest beat boundary in `[0, 0.5]`:
         `float phaseDist = MathF.Min(BeatPhase, 1f - BeatPhase);`
       - Converts to milliseconds using current `Bpm` (`float beatMs = 60_000f / MathF.Max(30f, Bpm); errorMs = phaseDist * beatMs;`).
       - Returns `errorMs <= PhaseSyncWindowMs && phaseDist <= PhaseSyncMaxPhase`.
   - Expose a deterministic test helper on `World` (e.g. `public void SetBeatClockForTest( double beatPos, float bpm = 120f)` that updates `_beatPos`, `Bpm`, and `BeatPhase = (float)(beatPos - Math.Floor(beatPos))`) so `SelfTest.cs` can test exact beat phases without coupling to internal private fields.

#### A2. On-Beat Dash Execution & Synergy Rewards (`src/Nullwave/World/World.Player.cs`)
1. In `UpdatePlayer(float dt, InputFrame input, AudioState audio)`:
   - Decay `p.PhaseSyncFlash = MathF.Max(0f, p.PhaseSyncFlash - dt * 2.6f);`.
   - Inside `if (input.Dash && p.DashCooldown <= 0f && !p.IsDashing)`:
     - After setting baseline dash parameters (`p.IsDashing = true`, `p.DashTime = 0.15f`, `p.DashCooldown = p.DashCooldownMax`, `p.Invuln = MathF.Max(p.Invuln, 0.24f)`), check `if (IsInPhaseSyncWindow(out float syncErrMs))`:
       - **Increment Counters:** `PhaseSyncs++; p.PhaseSyncs++; p.PhaseSyncFlash = 1f; p.LastPhaseSyncErrorMs = syncErrMs;`
       - **Flow Synergy Boost:** `FlowGain(0.05f);` (`+5%` Flow meter).
       - **Resonance Synergy Boost:** `float resGain = 5f * p.TotalResonanceGain * Mods.ResonanceGain; p.Resonance = MathF.Min(p.ResonanceMax, p.Resonance + resGain);`
       - **Cooldown & Invuln Synergy:** `p.DashCooldown *= 0.82f;` (18% faster cooldown recharge) and `p.Invuln = MathF.Max(p.Invuln, 0.30f);`
       - **Score & Floating Callout:** `AddScore(120, p.Pos, "PHASE SYNC", new Vector4(0.45f, 1f, 0.95f, 1f));`
       - **Visual & Audio Burst:**
         - `AddShockwave(p.Pos, 195f, 0.42f, 3.6f, new Vector4(0.45f, 1f, 0.95f, 1f));`
         - Spawn 10 extra cyan/gold spark particles along `-p.DashDir`.
         - `Sfx?.Trigger(SfxType.Pickup, 0.28f);` (crisp harmonic chime layered over the dash whoosh).
     - When `IsInPhaseSyncWindow` is `false`, the dash executes with its exact baseline values (zero penalty for off-beat survival dodges).

#### A3. World & HUD Visual Polish (`Render/WorldRenderer.cs`, `UI/Screens.cs`, `Core/Game.cs`)
1. **In `src/Nullwave/Render/WorldRenderer.cs` (`DrawSpeedFx` & `DrawPlayer`):**
   - In `DrawSpeedFx`: enable ship afterimage ghosting whenever `player.PhaseSyncFlash > 0.15f` (`bool ghosting = (speed > 0.75f && (w.Flow > 0.45f || player.IsDashing)) || player.PhaseSyncFlash > 0.15f;`).
   - In `DrawPlayer`: when `player.PhaseSyncFlash > 0.02f`, draw a crisp expanding/rotating cyan-gold harmonic phase ring around the ship (`r.Circle(pos, 28f + (1f - player.PhaseSyncFlash) * 18f, 2f * player.PhaseSyncFlash, new Vector4(0.45f, 1f, 0.95f, 0.75f * player.PhaseSyncFlash), 28, true)`) and boost thruster/hull rim glow.
2. **In `src/Nullwave/UI/Screens.cs` (`DrawFlowMeter`):**
   - In `DrawFlowMeter` (bottom center, `x = 560..1040, y = 824..862`):
     - When `w.Player.PhaseSyncFlash > 0.01f`, pulse a right-aligned or centered `"PHASE SYNC +RES"` / `"SYNC x" + w.PhaseSyncs` callout badge at `y = 824` (or right-aligned at `x = 1040, y = 824` where only the centered `"FLOW"` label sits at `x = 800, y = 824`) in bright cyan/gold, and when `w.PhaseSyncs > 0` and `PhaseSyncFlash <= 0.01f`, display a subtle `"SYNC " + w.PhaseSyncs` counter at `(x + wdt, 824)` (`x = 1040, y = 824`, right-aligned above the flow bar) so players can see their run's Phase Sync count without overlapping `"FLOW"` (`x = 800, y = 824`) or `"x1.00"` (`x = 1040, y = 862`)!
3. **In `src/Nullwave/Core/Game.cs` (`ConfigureCapture`):**
   - In `ConfigureCapture("speed")`, set `_world.PhaseSyncs = 4; _world.Player.PhaseSyncs = 4; _world.Player.PhaseSyncFlash = 0.85f;` (and in `UpdatePlaying` when `_speedDemo` is true, keep `PhaseSyncFlash` pinned at `0.85f`) so `--capture speed` deterministically showcases the Phase Sync ship ring and HUD `PHASE SYNC` readout.

#### A4. Deterministic Self-Test Suite (`src/Nullwave/Tools/SelfTest.cs`)
- Add `TestPhaseSync()` in `SelfTest.cs` and call it from `SelfTest.Run()`:
  1. **Window Boundary Checks:** Verify `IsInPhaseSyncWindow(out float errMs)` at 120 BPM (`500 ms` beat period):
     - `BeatPhase = 0.00f` (`0 ms`) -> `true`, `errMs < 1f`.
     - `BeatPhase = 0.08f` (`40 ms`) -> `true`, `errMs ~ 40f`.
     - `BeatPhase = 0.92f` (`40 ms` before beat) -> `true`, `errMs ~ 40f`.
     - `BeatPhase = 0.50f` (`250 ms` mid-beat) -> `false`, `errMs ~ 250f`.
     - `BeatPhase = 0.25f` (`125 ms`) -> `false`.
  2. **Off-Beat vs On-Beat Dash Comparison:**
     - Construct two deterministic worlds (`wOff` and `wOn`) with identical seeds.
     - Step `wOff` at `BeatPhase = 0.50f` with `Dash = true`: verify `wOff.Player.IsDashing == true`, `wOff.PhaseSyncs == 0`, `wOff.Player.PhaseSyncFlash == 0f`, `wOff.Player.Resonance == 0f`, and `wOff.Player.DashCooldown` equals baseline `DashCooldownMax - dt`.
     - Step `wOn` at `BeatPhase = 0.01f` with `Dash = true`: verify `wOn.Player.IsDashing == true`, `wOn.PhaseSyncs == 1`, `wOn.Player.PhaseSyncs == 1`, `wOn.Player.PhaseSyncFlash > 0.9f`, `wOn.Flow >= wOff.Flow + 0.045f`, `wOn.Player.Resonance >= 4.9f`, `wOn.Player.DashCooldown < wOff.Player.DashCooldown`, `wOn.Score > wOff.Score`, and a `"PHASE SYNC"` float text + shockwave were spawned.

---

### Part B — Art Scope (`@artist`)

Create **1 new high-craft visual asset** (`docs/assets/phase_sync_diagram.png`) and append its full entry to `.game-studio/ASSET_MANIFEST.md`:

| # | Asset Filename | Purpose | Target Resolution | Visual Brief & Style |
|---|---|---|---|---|
| 6 | `docs/assets/phase_sync_diagram.png` | Phase Sync Rhythm Synergy Telemetry Plate (`TASK-004`) | `1600x900` (16:9 PNG, opaque RGBA `A=255`) | Technical sci-fi schematic and arcade HUD plate titled `PHASE SYNC // ON-BEAT DASH RHYTHM SYNERGY`. Features: (1) a top/central PLL beat-phase oscilloscope timeline (`120 BPM // 500 MS PERIOD`) highlighting the glowing cyan/gold `±65 MS PHASE SYNC WINDOW` around the beat peak versus off-beat regions; (2) a central 3D oblique vector-mesh illustration of the Player Interceptor executing a 360-degree barrel-roll dash through enemy bullet trajectories with 5 fading neon afterimage ghosts and a concentric expanding `PHASE SYNC` shockwave ring; and (3) bottom/side telemetry readout cards detailing the 4 synergy bonuses (`+5.0% FLOW SURGE`, `+5.0 RESONANCE CHARGE`, `-18% DASH COOLDOWN`, `0.30S PHASE INVULN`) in `Palette.cs` deep void navy (`#030510`), electric cyan (`#20F0FF`), hyper magenta (`#FF38B8`), and solar gold (`#FFD866`). |

**Art Deliverables:**
- Generate `docs/assets/phase_sync_diagram.png` (`1600x900`, opaque `RGBA` `A=255`).
- Visually inspect `docs/assets/phase_sync_diagram.png` with `read_file` to confirm clarity, alignment, and palette fidelity.
- Append full provenance (purpose, reference style, dimensions, perspective, transparency, palette, file format, verified path, SHA-256, and prompt/synthesis specification) to `.game-studio/ASSET_MANIFEST.md`.

---

## 4. Acceptance Criteria (`@qa`)

1. **Clean Release Build:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeds with **0 warnings and 0 errors**.
2. **Self-Test Suite Pass:** `./run.cmd --selftest` passes with `RESULT: PASS` and new assertions from `TestPhaseSync()`, with zero regressions in existing suites (`TestRacerFeel`, `TestDamageAndBurst`, `TestBeatFire`, `TestLongRun`).
3. **Smoke Run Pass:** `./run.cmd --smoke 5` completes cleanly with `RENDERSTATE -> PASS` and high FPS.
4. **Visual Capture Verification:**
   - Run `./run.cmd --capture speed --out screenshots/speed.png` and inspect with `read_file` to confirm the Phase Sync ship aura and HUD Flow-meter sync indicator render cleanly with zero text overlap.
5. **Asset Verification:**
   - Confirm `docs/assets/phase_sync_diagram.png` exists, is a valid `1600x900` opaque PNG, and is accurately cataloged in `.game-studio/ASSET_MANIFEST.md`.

---

## 5. Designer Notes (`@designer`)
- Keeping Phase Sync strictly additive (rewarding on-beat dashes without penalizing off-beat dashes) ensures bullet-hell survival responsiveness is never compromised while giving rhythm-minded players a skill-expressive way to build Flow and Resonance faster.
- Placing the HUD `PHASE SYNC` / `SYNC <N>` indicator right-aligned at `(1040, 824)` above the right end of the Flow bar (`x = 560..1040, y = 846`) pairs naturally with the centered `FLOW` label at `(800, 824)` and the multiplier `x1.00` at `(1040, 862)` with zero overlap.

---

## 6. Engineering Handoff (`@programmer`)

### Summary of Changes
1. **Phase Sync Timing Window & State Model (`src/Nullwave/World/Entities.cs`, `World/World.cs`):**
   - Added `PhaseSyncs`, `PhaseSyncFlash`, and `LastPhaseSyncErrorMs` on `Player` (`Entities.cs`), plus `PhaseSyncs` on `World` and `RunSummary` (`World.cs`, populated in `Die()`).
   - Added `World.PhaseSyncWindowMs = 65f`, `World.PhaseSyncMaxPhase = 0.18f`, `World.IsInPhaseSyncWindow(out float errorMs)` (checking nearest beat boundary distance in milliseconds and phase), and `World.SetBeatClockForTest(double beatPos, float bpm = 120f)` (`World.cs`).
2. **On-Beat Dash Synergy Rewards (`src/Nullwave/World/World.Player.cs`):**
   - Decayed `p.PhaseSyncFlash` at `dt * 2.6f` in `UpdatePlayer`.
   - When initiating a dash inside `IsInPhaseSyncWindow(out float syncErrMs)`, triggers Phase Sync: increments `PhaseSyncs` & `p.PhaseSyncs`, sets `p.PhaseSyncFlash = 1f` and `p.LastPhaseSyncErrorMs = syncErrMs`, grants `FlowGain(0.05f)` and `5f * p.TotalResonanceGain * Mods.ResonanceGain` Resonance, reduces `p.DashCooldown *= 0.82f`, extends `p.Invuln = MathF.Max(p.Invuln, 0.30f)`, awards `AddScore(120, p.Pos, "PHASE SYNC", ...)` + `AddShockwave(...)`, spawns 10 cyan/gold spark particles, and plays a harmonic chime (`SfxType.Pickup`, `0.28f`). Off-beat survival dashes retain exact baseline behavior with zero penalty.
3. **World & HUD Visual Polish (`src/Nullwave/Render/WorldRenderer.cs`, `UI/Screens.cs`, `Core/Game.cs`):**
   - Updated `WorldRenderer.DrawSpeedFx` to trigger ship afterimage ghosting when `player.PhaseSyncFlash > 0.15f` and `WorldRenderer.DrawPlayer` to boost hull/thruster glow and render concentric expanding cyan/gold Phase Sync rings around the ship (`WorldRenderer.cs`).
   - Updated `Screens.DrawFlowMeter` (`Screens.cs`) to render the pulsing right-aligned `"PHASE SYNC +RES"` indicator at `(1040, 824)` when `PhaseSyncFlash > 0.01f`, or subtle `"SYNC <N>"` counter when `w.PhaseSyncs > 0`, cleanly separated from centered `// FLOW STATE //` (`800, 824`) and `x2.50` (`1040, 862`).
   - Updated `Game.ConfigureCapture("speed")` and `_speedDemo` in `UpdatePlaying` (`Game.cs`) so `--capture speed` deterministically showcases the Phase Sync rings, afterimage ghosts, and HUD callout.
4. **Deterministic SelfTest Coverage (`src/Nullwave/Tools/SelfTest.cs`):**
   - Added `TestPhaseSync()` to `SelfTest.Run()`, raising total assertions from `69,312` to **`69,333`**.

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
    phase sync: err0=0.0ms err40=40.0ms syncs=1 flowGain=0.015 res=5.0 cd=0.67s vs 0.82s
  RESULT: PASS (69333 assertions, 2.4s)
  ```
- **Smoke & Live GL State Verification (`--smoke 5`):**
  ```text
  ./run.cmd --smoke 5
  SMOKE | 5.0s fps=114 wave=1 score=482 enemies=3 bullets=4 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```
- **Visual Capture Verification (`--capture speed`):**
  - Visually inspected `screenshots/speed.png` with `read_file`; confirmed the concentric cyan/gold Phase Sync rings around the ship, afterimage ghosts, and `PHASE SYNC +RES` indicator at `(1040, 824)` render cleanly with zero overlap.

---

## 7. Art Handoff (`@artist`)
*(Summary recorded in `.game-studio/ASSET_MANIFEST.md` and on the swarm board.)*

---

## 8. QA Findings & Verdict (`@qa`)

- **Verdict:** **PASS (`APPROVED — 0 DEFECTS`)** — Full details recorded in `.game-studio/QA_REPORT.md` (§8).
- **Independent Build Verification:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeded in `0.60s` with **0 warnings, 0 errors**.
- **Independent SelfTest Verification:** `./run.cmd --selftest` passed with **`RESULT: PASS (69333 assertions, 2.6s)`** (up from `69,312`, verifying `TestPhaseSync()` across `BeatPhase` window boundaries `0.00 / 0.08 / 0.92 / 0.50 / 0.25`, off-beat vs on-beat dash comparison, Flow/Resonance/Cooldown/Score/Visual bonuses, and `RunSummary.PhaseSyncs`).
- **Independent Smoke & Live GL State Verification:** `./run.cmd --smoke 5` passed at **`115 fps`** with **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
- **Code & Hot-Path Review:** Confirmed zero per-frame heap allocations in `UpdatePlayer`, `WorldRenderer.DrawSpeedFx`/`DrawPlayer`, and `Screens.DrawFlowMeter`, and verified off-beat survival dashes remain 100% unpenalized.
- **Visual & Asset Verification:** Visually inspected `screenshots/speed.png` and `docs/assets/phase_sync_diagram.png` (`1600x900`, `1,260,574 bytes`, SHA-256 `7424E96E7F8FFF469402FAF4581F780C1E6FC0FBC0EA273AE2D5954479258802`), confirming zero UI overlap and exact match with `.game-studio/ASSET_MANIFEST.md`.
