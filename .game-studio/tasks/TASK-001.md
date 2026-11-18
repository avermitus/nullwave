# TASK-001: Render State Hygiene, Flight Recorder Telemetry & Personal Best Callouts, and Studio Visual Assets

- **Status:** `done`
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer`
- **Owner (Art):** `@artist`
- **Reviewer (QA):** `@qa`

---

## 1. Goal & Player-Facing Benefit

1. **Render & Recorder State Hygiene (`PostFx.cs`, `Renderer.cs`, `.gitignore`):**
   - Eliminate the documented OpenGL blend-state and active-texture leak in `PostFx.Process` (`src/Nullwave/Render/PostFx.cs`) and preserve opaque backbuffer destination alpha (`1.0`) during UI rendering in `Renderer.Render` (`src/Nullwave/Render/Renderer.cs`) so multi-pass rendering, screenshots, and video recording never suffer fromHUD ghosting or translucent alpha artifacts.
   - Ensure `.gitignore` explicitly lists `capture/*.mp4` and `capture/*.wav` per `HANDOFF.md` §8.
2. **Flight Recorder Run Telemetry & Personal Best Callouts (`Meta.cs`, `Game.cs`, `Screens.cs`):**
   - Give players immediate celebratory feedback when they set a new record (`NEW BEST SCORE`, `NEW BEST WAVE`, `NEW BEST CHAIN`) on the `SIGNAL LOST` (`GameOver`) screen instead of silently overwriting `MetaProgress` before rendering.
   - Persist `BestCombo` and a rolling `RecentRuns` flight-recorder history (last 8 runs with seed code, wave, score, max combo, movement name, duration, daily/modifier tags) in `MetaProgress`, and surface a **RECENT SIGNALS** telemetry strip on the `ARCHIVE` screen (`Screens.DrawArchive`) plus lifetime `BEST CHAIN` telemetry so players can track their mastery across sessions.
3. **NULLWAVE Studio Visual Assets (`docs/assets/`, `.game-studio/ASSET_MANIFEST.md`):**
   - Establish official high-craft visual assets for NULLWAVE's documentation and studio identity (Title/Key Art banner, The Choir Boss emblem, and Pilot Flow State insignia) matched to `Palette.cs` neon vector-mesh and iridescent fluid aesthetics.

---

## 2. Relevant Code, Assets & Ownership Constraints

### Strict File Ownership
- **`@designer` owns:** `.game-studio/PROJECT.md`, `.game-studio/BACKLOG.md`, `.game-studio/tasks/TASK-001.md` (Design & Task Specification sections).
- **`@programmer` owns:** `src/Nullwave/**`, `.gitignore`, and the **Engineering Handoff** section of `.game-studio/tasks/TASK-001.md`.
- **`@artist` owns:** `docs/assets/**` and `.game-studio/ASSET_MANIFEST.md`.
- **`@qa` owns:** `.game-studio/QA_REPORT.md` and the **QA Findings & Verdict** section of `.game-studio/tasks/TASK-001.md`.

### Key Source Files for Engineering (`@programmer`)
- `src/Nullwave/Render/PostFx.cs` — `Process(...)` (~lines 55–137)
- `src/Nullwave/Render/Renderer.cs` — `Render(...)` (~lines 207–335)
- `src/Nullwave/World/Meta.cs` — `MetaProgress`, `RunSummary` recording, `RecentRuns`, `BestCombo`, record flags
- `src/Nullwave/Core/Game.cs` — `EnterGameOver()`, `PrepareCaptureMeta()`, passing new-record state to `Screens.DrawGameOver`
- `src/Nullwave/UI/Screens.cs` — `DrawGameOver(...)` and `DrawArchive(...)`
- `src/Nullwave/Tools/SelfTest.cs` — deterministic self-test assertions covering the new features and state invariants
- `.gitignore` — explicit `capture/*.mp4` and `capture/*.wav` rules

---

## 3. Implementation Plan

### Part A — Engineering Scope (`@programmer`)

#### A1. OpenGL Blend-State & Texture Unit Hygiene (`PostFx.cs` & `Renderer.cs`)
1. In `src/Nullwave/Render/PostFx.cs`:
   - At the end of `PostFx.Process(...)` (after the composite fullscreen draw), restore a clean, deterministic OpenGL baseline state:
     - Unbind bloom/scene textures on `Texture3`, `Texture2`, `Texture1`, `Texture0` and restore `ActiveTexture(TextureUnit.Texture0)`.
     - Re-enable `EnableCap.Blend` and set standard alpha blending (`BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha`) — or `BlendFuncSeparate` with alpha preservation — and unbind VAO (`BindVertexArray(0)`).
   - Expose a lightweight, testable state contract or helper (e.g., `PostFx.BaselineState` struct/constants or verification hook) so `SelfTest.cs` can deterministically verify the post-process/renderer blend configuration headlessly.
2. In `src/Nullwave/Render/Renderer.cs`:
   - In the final UI pass of `Renderer.Render(...)` (lines 325–333), use `BlendFuncSeparate` so RGB blends normally (`SrcAlpha, OneMinusSrcAlpha` for `_uiAlpha`, and `SrcAlpha, One` for `_uiGlow`) while destination Alpha stays `1.0` (`One, OneMinusSrcAlpha` or `Zero, One` after opaque composite), and restore standard alpha blend at the end of `Render(...)`.
   - Ensure zero per-frame heap allocations are introduced on the render path.
3. In `.gitignore`:
   - Add explicit `capture/*.mp4` and `capture/*.wav` entries alongside the existing rules so `HANDOFF.md` §8 is satisfied verbatim.

#### A2. Flight Recorder Telemetry (`RecentRuns`), `BestCombo`, & Personal Best Callouts (`Meta.cs`, `Game.cs`, `Screens.cs`)
1. **Data Model (`src/Nullwave/World/Meta.cs`):**
   - Add `public int BestCombo { get; set; }` to `MetaProgress` (updated in `RecordRun` via `Math.Max(BestCombo, summary.MaxCombo)`).
   - Define a serializable `RunRecord` class (e.g., `SeedCode`, `Daily`, `Wave`, `Score`, `MaxCombo`, `Kills`, `Grazes`, `Duration`, `MovementName`, `Attunements`, `ModifierCount`, `PlayedUtc`) and add `public List<RunRecord> RecentRuns { get; set; } = new();` to `MetaProgress` (initialized safely on `Load` so existing `save.json` files deserialize with zero issues).
   - In `RecordRun(RunSummary summary)`:
     - Before mutating `BestScore`, `BestWave`, and `BestCombo`, compute whether this run achieved a personal best: e.g., `bool newBestScore = summary.Score > BestScore && summary.Score > 0`, `bool newBestWave = summary.Wave > BestWave && summary.Wave > 0`, `bool newBestCombo = summary.MaxCombo > BestCombo && summary.MaxCombo > 0`.
     - Expose the last run's record breakdown (e.g., via a `RunRecordFlags` / `LastRunRecords` property on `MetaProgress` or return value/out parameter) so `Game` and `Screens.DrawGameOver` can highlight which records were broken on that run.
     - Prepend/append the new `RunRecord` to `RecentRuns` and cap `RecentRuns` at **8 entries** (most recent first or chronological with newest easily accessible).
2. **UI & Capture Polish (`src/Nullwave/Core/Game.cs` & `src/Nullwave/UI/Screens.cs`):**
   - In `Game.PrepareCaptureMeta()`, populate `BestCombo` and 2–3 representative `RecentRuns` entries so `--capture archive` and `--capture gameover` showcase the new telemetry cleanly without touching the user's real save file.
   - In `Screens.DrawGameOver(...)`:
     - Highlight broken records (`NEW BEST` / `NEW RECORD` callout banner or badge on `WAVE`, `SCORE`, `CHAIN` stats when a personal best is set on that run) and display `BEST CHAIN` alongside `BEST WAVE` and `BEST SCORE`.
   - In `Screens.DrawArchive(...)`:
     - Include `BEST CHAIN` in the top career summary line (`y = 134`).
     - Render a concise, high-readability **RECENT SIGNALS** telemetry line/strip in the Archive footer area (fitting cleanly within the `1600x900` layout without overlapping the 2x8 modifier grid at `y = 176..792`) showing the most recent recorded runs (e.g., Seed code, Wave, Score, Chain/Movement).

#### A3. Deterministic SelfTest Suite (`src/Nullwave/Tools/SelfTest.cs`)
- Add a dedicated test method (e.g., `TestRunTelemetryAndPostFxState()` or extending `TestMeta()` + adding `TestPostFxAndTelemetry()`) called from `SelfTest.Run()` that deterministically asserts:
  1. `MetaProgress.RecordRun` accurately tracks `BestCombo`, populates and caps `RecentRuns` at 8 entries, persists and reloads `RecentRuns` and `BestCombo` across JSON serialization in a temp directory, and accurately flags new personal bests (`Score`, `Wave`, `MaxCombo`) on progressive runs.
  2. `PostFx` / `Renderer` baseline blend-state invariants hold.

---

### Part B — Art Scope (`@artist`)

Using the available `image-generator` subagent, generate **3 cohesive visual assets** inspired by `src/Nullwave/Render/Palette.cs` (deep void navy `#030510`, electric cyan `#20F0FF`, hyper magenta `#FF38B8`, warning crimson `#FF4052`, solar gold `#FFD866`, crisp ice white `#E6F5FF`) and the procedural 3D vector-mesh + liquid/fractal shader aesthetic shown in `docs/screenshots/`:

| # | Asset Filename | Purpose | Target Resolution / Aspect | Visual Brief & Style |
|---|---|---|---|---|
| 1 | `docs/assets/nullwave_key_art.png` | Official Studio Key Art / Title Banner | `16:9` widescreen (`1600x900` or generator native 16:9) | Sleek geometric cyan/white interceptor spacecraft banking at high speed through an iridescent dark-void nebula and liquid metaball arena, firing twin neon bolts toward towering obsidian-and-magenta Choir monoliths while six starburst laser beams converge on the beat. |
| 2 | `docs/assets/choir_boss_emblem.png` | Boss Insignia ("THE CHOIR") | `1:1` square (`1024x1024` or generator native 1:1) | High-contrast vector-style geometric boss emblem representing **THE CHOIR**: a sacred-geometry polyhedral core surrounded by a crown of glowing audio-spectrum pillars, concentric phase halos, and orbiting crystalline shards in crimson, magenta, and gold over a dark void background. |
| 3 | `docs/assets/pilot_flow_insignia.png` | Archive & Flow State Pilot Crest | `1:1` square (`1024x1024` or generator native 1:1) | Minimalist futuristic arcade pilot crest for `// FLOW STATE //` and `ARCHIVE OF THE SIGNAL`: a stylized angular delta-wing interceptor silhouette framed by a circular phase-locked beat grid, soundwave arcs, and cyan/gold resonance chevrons on a deep obsidian background. |

**Art Deliverables:**
- Save verified PNG files in `docs/assets/`.
- Visually inspect each generated image with `read_file` to verify crispness, palette harmony, and absence of garbled text/artifacts.
- Record full provenance (purpose, reference style, verified dimensions, perspective, transparency, palette, file format, verified path, and exact prompt) in `.game-studio/ASSET_MANIFEST.md`.

---

## 4. Acceptance Criteria (`@qa`)

1. **Clean Release Build:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeds with **0 warnings and 0 errors**.
2. **Self-Test Suite Pass:** `./run.cmd --selftest` passes with `RESULT: PASS` and an increased assertion count covering `RecentRuns` persistence/capping, `BestCombo`, personal-best record detection, and `PostFx`/`Renderer` state invariants.
3. **Smoke Run Pass:** `./run.cmd --smoke 5` completes cleanly with stable frame rate and zero exceptions.
4. **Code & State Hygiene Inspection:**
   - `PostFx.Process` restores blend and texture unit state cleanly.
   - `.gitignore` includes `capture/*.mp4` and `capture/*.wav`.
   - No per-frame allocations added on hot rendering/simulation paths.
   - Existing save files remain backward-compatible.
5. **Asset Verification:**
   - All 3 PNG files exist under `docs/assets/`, are non-empty valid images, and match their entries in `.game-studio/ASSET_MANIFEST.md`.

---

## 5. Designer Notes (`@designer`)
- Incorporates `@programmer`'s technical audit (#6 on board):
  - `PostFx.Process` restores a known baseline on exit (blend on, `SrcAlpha/OneMinusSrcAlpha`, tex units 3..0 unbound, `Texture0` active, VAO 0) and `Renderer.Render` UI pass uses `BlendFuncSeparate` so backbuffer alpha remains `1.0`.
  - Archive screen vertical space (`y = 176..792` used by the 2x8 modifier grid; divider at `y = 798`) is respected: keep the `RECENT SIGNALS` strip concise in the header/footer band so there is zero overlap with modifier rows.

---

## 6. Engineering Handoff (`@programmer`)

### Summary of Changes
1. **OpenGL Blend-State & Texture Unit Hygiene (`src/Nullwave/Render/BlendMode.cs`, `PostFx.cs`, `Renderer.cs`, `Program.cs`):**
   - Added `BlendMode` (`Alpha`, `Additive`, `LegacyAlpha`, `LegacyAdditive`) and `RenderBaseline` (`BlendMode.cs`) to express separate RGB/alpha blend functions (`glBlendFuncSeparate`) and the deterministic baseline GL contract (`Blend=true`, `DepthTest=false`, `BlendMode.Alpha`, `ActiveUnit=0`, `SamplerUnits=4` unbound, `VertexArray=0`).
   - Updated `PostFx.Process` (`PostFx.cs`) to call `PostFx.RestoreBaseline(_gl)` after composite draw, unbinding texture units `Texture3..Texture0`, restoring active unit `Texture0`, re-enabling blend with `BlendMode.Alpha`, and unbinding VAO `0`. Added `PostFx.VerifyBaseline(GL, out string)` and `Renderer.VerifyBaselineState(out string)` for live GL readback checks in smoke mode.
   - Updated `Renderer.Render` (`Renderer.cs`) UI pass to use `BlendMode.Alpha.Apply(_gl)` (`SrcAlpha, OneMinusSrcAlpha, One, OneMinusSrcAlpha`) for `_uiAlpha` and `BlendMode.Additive.Apply(_gl)` (`SrcAlpha, One, Zero, One`) for `_uiGlow`, then restore baseline via `PostFx.RestoreBaseline(_gl)` so backbuffer alpha remains `255` (`1.0`) and multi-render-per-frame paths never leak state.
   - Added `CheckRenderState()` at the end of `--smoke` (`Program.cs`) to verify live GL baseline state, `alphaMin == 255`, and `0.000%` pixel drift across an immediate double-render.
2. **`.gitignore` Hygiene (`.gitignore`):**
   - Added explicit `capture/*.mp4` and `capture/*.wav` rules per `HANDOFF.md` §8.
3. **Flight Recorder Telemetry (`RecentRuns`), `BestCombo`, & Personal Best Callouts (`src/Nullwave/World/Meta.cs`, `Core/Game.cs`, `UI/Screens.cs`):**
   - Added `RunRecords` (`[Flags]` enum: `None`, `Score`, `Wave`, `Combo`), `RecordBreak` struct (`Records`, `PreviousScore`, `PreviousWave`, `PreviousCombo`), and serializable `RunRecord` (`Meta.cs`).
   - Added `BestCombo`, `RecentRuns` (capped at `RecentRunCap = 8`, newest first, sanitized on `Load` for backward compatibility with older or hand-edited `save.json` files), and `[JsonIgnore] LastRecords` (`CompareWithBests` evaluated before bests update; legacy saves with `Runs > 0 && BestCombo == 0 && RecentRuns.Count == 0` adopt their first chain baseline without a false-positive record banner).
   - Updated `Game.EnterGameOver()` (`Game.cs`) to capture `_records = _meta.LastRecords`, trigger the unlock chime on new personal bests, and pass `_records` to `Screens.DrawGameOver(...)`. Updated `Game.PrepareCaptureMeta()` with `BestCombo = 36` and 3 representative `RecentRuns` entries.
   - Updated `Screens.DrawGameOver(...)` (`Screens.cs`) to highlight broken `WAVE`, `SCORE`, and `CHAIN` stats in accent color with a pulsing boxed `NEW BEST` tag, display `BEST CHAIN` in the career bests line, and render `Screens.RecordBanner(...)` with exact record deltas (e.g., `NEW RECORD // CHAIN +6`).
   - Updated `Screens.DrawArchive(...)` (`Screens.cs`) to show `BEST CHAIN` in the top summary bar (`y = 134`) and render the `RECENT SIGNALS //` strip (`y = 846`) with daily tag (`D`), seed code, wave, score, chain, and record star (`*`) without overlapping the 2x8 modifier grid (`y = 176..792`).
4. **Deterministic SelfTest Coverage (`src/Nullwave/Tools/SelfTest.cs`):**
   - Added `TestRunTelemetry()` and `TestRenderStateContract()` to `SelfTest.Run()`, raising total assertions from `69,058` to **`69,215`**.

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
    telemetry: history=8/8 bestChain=33 lastBanner="NEW RECORDS // SCORE +974,999 // CHAIN +14"
    render state: baseline=SrcAlpha/OneMinusSrcAlpha+One/OneMinusSrcAlpha units=4 legacyTranslucentSamples=19/21
  RESULT: PASS (69215 assertions, 2.5s)
  ```
- **Smoke & Live GL State Verification (`--smoke 5`):**
  ```text
  ./run.cmd --smoke 5
  NULLWAVE | GL 4.1.0 NVIDIA 617.42 | NVIDIA GeForce RTX 5060 Laptop GPU/PCIe/SSE2
  AUDIO | capture=True output=True device=Edge error=
  SMOKE | 5.0s fps=115 wave=1 score=677 enemies=3 bullets=4 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```
- **Capture Visual Verification (`--capture archive` & `--capture gameover`):**
  - Verified both screenshots (`screenshots/archive.png` and `screenshots/gameover.png`) visually; layout, typography, `RECENT SIGNALS //` strip, and `NEW BEST` / `NEW RECORD // CHAIN +6` callouts render cleanly with zero overlap.

---

## 7. Art Handoff (`@artist`)
*(Summary recorded in `.game-studio/ASSET_MANIFEST.md` and on the swarm board.)*

---

## 8. QA Findings & Verdict (`@qa`)

- **Verdict:** **PASS (`APPROVED — 0 DEFECTS`)** — Full details recorded in `.game-studio/QA_REPORT.md`.
- **Independent Build Verification:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeded in `0.58s` with **0 warnings, 0 errors**.
- **Independent SelfTest Verification:** `./run.cmd --selftest` passed with **`RESULT: PASS (69215 assertions, 2.3s)`**, verifying `TestRunTelemetry()` (personal-best detection, `BestCombo`, `RecentRuns` cap at 8, JSON persistence, legacy-save baseline adoption) and `TestRenderStateContract()` (`BlendMode.Alpha` and `BlendMode.Additive` destination alpha `1.0` preservation).
- **Independent Smoke & Live GL State Verification:** `./run.cmd --smoke 5` passed at **`114 fps`** with **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
- **Code & Hot-Path Review:** Confirmed zero per-frame heap allocations on `PostFx.Process` / `Renderer.Render`, clean unbinding of texture units `Texture3..Texture0` with `ActiveTexture(Texture0)` and `VAO 0`, backward-compatible `MetaProgress.Load`, and explicit `capture/*.mp4` and `capture/*.wav` rules in `.gitignore`.
- **Visual & Asset Verification:** Visually inspected `screenshots/archive.png`, `screenshots/gameover.png`, and all 3 studio PNG assets in `docs/assets/` (`nullwave_key_art.png` `1600x900`, `choir_boss_emblem.png` `1024x1024`, `pilot_flow_insignia.png` `1024x1024`), confirming exact checksum and dimension match against `.game-studio/ASSET_MANIFEST.md`.
