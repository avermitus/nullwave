# TASK-008: Fully Generative Animated Backdrops + Per-Generator Audio Reactivity & Visual Harmony (Combined TASK-008 & TASK-009)

- **Status:** `done`
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer`
- **Reviewer (QA):** `@qa`

---

## 1. Goal & Player-Facing Benefit

1. **Fully Generative Multi-Layer Animated Backdrops (TASK-008 Foundation, already in working tree):**
   - Replaces the V7 6-style fixed backdrop roster and TASK-007's 3D bobbing backdrop shapes (`WorldRenderer.DrawBackdrop3D` removed; 3D mesh backdrop pass stays wired but idle) with a combinatorial **1–3 layer generative backdrop engine** (`BackdropGenome.cs` + `Shaders.BackgroundFrag`).
   - Each layer chains `0–4` continuous-parameter space/motion transforms (`SPIN`, `TUNNEL`, `FOLD`, `WARP`, `SWIRL`, `MIRROR`, `SPIRAL`, `STREAM`, `RIPPLE`, `TILES`, `BREATHE`) into one of **13 animated pattern generators** (`INK`, `CELLS`, `FRACTAL`, `CIRCUIT`, `MOIRE`, `STREAMS`, `GRID`, `LIQUID`, `FIELD`, `PLASMA`, `AURORA`, `HEX`, `HYPERSPACE`), blended via `add / screen / lighten / modulate / difference` with a coherent per-backdrop cosine palette family and seamless `1.8s` radial transitions.
   - Preserves aim-reactive shader parallax (`uAim`) and subtle oblique 3D world-mesh camera tilt (`Renderer.ObliqueVec`, `z = 0` invariant).
2. **Per-Generator Audio Reactivity, Crispness & Visual Harmony (TASK-009 Polish):**
   - Clean up the 10 dead V7 shader uniforms left behind in `Shaders.BackgroundFrag` and upload real-time drum/hat onset pulses (`uSnare`, `uHat`) alongside `uKick`, `uBass`, `uHigh`, and `uDrop`.
   - Upgrade all **13 generators** and key space transforms (`WARP`, `SWIRL`, `RIPPLE`, `BREATHE`) so every generated backdrop reacts visibly and distinctly to kicks, snares, hi-hats, bass, highs, and musical drops — especially transforming `genPlasma` from soft blurry blobs into crisp harmonic contour bands.
   - Tune the drifting ambient fog floor and highlight soft-clipping in `BackgroundFrag.main()` so sparse line generators never sink to flat black and dense multi-layer combinations never wash out into murky pastel haze — keeping foreground bullets, 4-layer HDR lasers, and enemies razor-sharp.

---

## 2. Relevant Code & Ownership Constraints

### Strict File Ownership
- **`@designer` owns:** `.game-studio/BACKLOG.md`, `.game-studio/tasks/TASK-008.md` (Design & Spec), and—after `@programmer` finishes coding & captures—updating `CHANGELOG.md` and `README.md` (strictly game-focused; zero AI/agent/swarm/task-ID chatter).
- **`@programmer` owns:** `src/Nullwave/**` (`Render/BackdropGenome.cs`, `Render/Renderer.cs`, `Render/Shaders.cs`, `Tools/SelfTest.cs`), `HANDOFF.md` (if needed), refreshing `docs/screenshots/`, `.game-studio/tasks/TASK-008.md` (§6 Engineering Handoff), and final `git add/commit/push` to `origin/main` after `@qa` sign-off.
- **`@qa` owns:** `.game-studio/QA_REPORT.md` and `.game-studio/tasks/TASK-008.md` (§7 QA Findings & Verdict).

---

## 3. Implementation Plan (`@programmer`)

### A1. Dead-Uniform Cleanup & Onset Drive Uniforms (`Renderer.cs` & `Shaders.cs`)
1. **In `src/Nullwave/Render/Shaders.cs` (`BackgroundFrag`):**
   - Remove the 10 leftover unused V7 uniforms (`uStyleA`, `uStyleB`, `uParamA`, `uParamB`, `uMutA`, `uMutB`, `uPalOffA`, `uPalOffB`, `uPalFreqA`, `uPalFreqB`) while keeping `uMix` and the active `uLA[3]` / `uLB[3]` / `uCountA/B` / `uGlobA/B` uniforms.
   - Add `uniform float uSnare;` and `uniform float uHat;` next to `uniform float uKick;` so every generator and motion transform can react to percussion and hi-hat transients.
2. **In `src/Nullwave/Render/Renderer.cs`:**
   - Add a pure, allocation-free static struct and helper (headlessly testable in `SelfTest.cs`):
     ```csharp
     public readonly record struct BackdropAudioDrives(
         float Kick, float Snare, float Hat, float Drop, float Build, float Breakdown);

     public static BackdropAudioDrives ComputeBackdropAudioDrives(
         AudioState audio, MusicDirector? mood, float fx)
     ```
     - Sanitizes `fx = float.IsFinite(fx) ? MathF.Max(0f, fx) : 0f`, computes:
       - `Kick = ClampDrive((mood?.Kick ?? audio.BeatPulse) * fx)`
       - `Snare = ClampDrive(audio.SnarePulse * fx)`
       - `Hat = ClampDrive(audio.HatPulse * fx)`
       - `Drop = ClampDrive((mood?.DropPulse ?? 0f) * fx)`
       - `Build = ClampDrive((mood?.Build ?? 0f) * fx)`
       - `Breakdown = ClampDrive((mood?.Breakdown ?? 0f) * fx)`
       (with finite clamping e.g. `[0f, 3f]` so extreme settings/NaN inputs never blow out shader math).
   - Call `ComputeBackdropAudioDrives(audio, mood, fx)` in `Renderer.Render(...)` and upload `uKick`, `uSnare`, `uHat`, `uDrop`, `uBuild` (and use `drives.Drop` / `drives.Breakdown` for `_flow`, `_travel`, and `uIntensity`).

### A2. Per-Generator Audio Reactivity & Crispness Across All 13 Generators (`Shaders.cs`)
1. **Upgrade the 7 New Generators (`Shaders.cs` lines ~394–491):**
   - **`genInk` (`0: INK`):** Add `uKick`/`uDrop` domain-fold expansion, `uSnare` ridge/contour highlight along high-gradient ink folds, and `uHat` fine grain/ribbon sheen.
   - **`genTruchet` (`3: CIRCUIT`):** Add `uSnare` bright arc surge along active circuit segments, `uHat` crisp high-frequency dash sparkle, and `uDrop` full-trace bloom.
   - **`genInterference` (`4: MOIRE`):** Add `uSnare` wave-phase kick & bright crest flash, `uHat` fine harmonic overtone interference, and `uDrop` radiant crest expansion.
   - **`genStreams` (`5: STREAMS`):** Add `uHat` particle density & glint modulation, `uSnare` velocity-streak flash pulses, and `uDrop` hyperspeed streak elongation.
   - **`genGridPulse` (`6: GRID`):** Add `uSnare` orthogonal lattice wave flash, `uHat` glowing grid-intersection node glints (`exp(-length(f) * ...)`), and `uDrop` grid bloom.
   - **`genPlasma` (`9: PLASMA` — High Priority Crispness Upgrade):** Replace the soft low-contrast `0.25 + 0.35 * sin(...)` wash with **crisp harmonic contour bands** (`abs(fract(phase) - 0.5)` sharp ridges via `exp(-ridge * ...)` over a darker silk under-glow), adding `uSnare` phase-jump shift, `uKick`/`uDrop` ridge flare, and `uHat` fine contour shimmer so `PLASMA` reads as razor-sharp synth-wave interference rather than blurry blobs.
   - **`genHex` (`11: HEX`):** Add `uSnare` expanding radial hex-ring shockwave, `uHat` crystalline facet shimmer inside active cells, and `uDrop` full-lattice surge.
2. **Add Subtle `uSnare` & `uHat` Accents to the 6 Classic Generators (`Shaders.cs`):**
   - **`styleCells` (`1: CELLS`):** `uSnare` cell-flash threshold boost + `uHat` sharp Voronoi fracture rim highlight.
   - **`styleKaleido` (`2: FRACTAL`):** `uSnare` fold flare + `uHat` high-frequency lace edge sparkle.
   - **`styleLiquid` (`7: LIQUID`):** `uSnare` rim Fresnel kick + `uHat` specular surface sparkle.
   - **`styleField` (`8: FIELD`):** `uSnare` glowing geometry rim accent + `uHat` distant corridor shimmer.
   - **`styleAurora` (`10: AURORA`):** `uSnare` ribbon core flare + `uHat` vertical veil shimmer.
   - **`styleTunnel` (`12: HYPERSPACE`):** `uSnare` ring-flash pulse + `uHat` wall panel glint.
3. **Add `uDrop` & `uSnare` Energy to Space Transforms (`applyOp`):**
   - **`op 4 (WARP)`:** Scale warp displacement slightly with `uBass`, `uSnare`, and `uDrop`.
   - **`op 5 (SWIRL)`:** Add subtle `uSnare` twist impulse and `uDrop` vortex pull.
   - **`op 9 (RIPPLE)`:** Boost radiating ring amplitude with `uKick`, `uSnare`, and `uDrop`.
   - **`op 11 (BREATHE)`:** Add subtle `uDrop` zoom push alongside `uKick` beat breathing.

### A3. Fog Floor & Foreground Contrast Harmony (`Shaders.cs` `main()`)
- Tune the ambient `fog` floor in `BackgroundFrag.main()`:
  - Keep sparse line generators (`GRID`, `CIRCUIT`, `STREAMS`, `HEX`) cleanly off pure black with a deep, moody chromatic fog floor (`~0.10 + fog * 0.10 + uKick * 0.03 + uSnare * 0.025`, masked by `clamp(1.0 - lum * 3.0, 0.0, 1.0)`) instead of washing dark scenes into flat murky/pastel haze.
  - Keep highlight soft-clipping (`col = col * 1.04 / (1.0 + col * 1.85)`) and overall intensity disciplined so foreground enemies, dark bullet halos, comet tails, and 4-layer HDR lasers pop cleanly across all 13 generators and multi-layer stacks.
  - Preserve `uAim` shader parallax (`uv += uCam * 0.00004 + uAim * 0.038;` and `aimWash`) and `Renderer.ObliqueVec` aim camera tilt intact.

### A4. Self-Test Assertions & Refreshed Captures (`SelfTest.cs` & `docs/screenshots/`)
1. **In `src/Nullwave/Tools/SelfTest.cs`:**
   - Preserve all existing `TestBackdropVariety()` checks (`1,000` distinct genomes, `13/13` generators, `11/11` transforms, layer/raymarch/remap/consecutive rules).
   - Add deterministic assertions (inside `TestBackdropVariety()` or a dedicated `TestBackdropAudioReactivity()`) verifying:
     - `Renderer.ComputeBackdropAudioDrives` maps `Kick`, `Snare`, `Hat`, `Drop`, `Build`, `Breakdown` accurately from `AudioState` + `MusicDirector`, scales cleanly by `fx`, handles `fx == 0` / `mood == null` / non-finite values safely.
     - `Shaders.BackgroundFrag` contains `uniform float uSnare;`, `uniform float uHat;`, `uniform float uKick;`, `uniform float uDrop;`, `uniform vec2 uAim;`, and contains **none** of the 10 dead V7 uniforms (`uStyleA`, `uStyleB`, `uParamA`, `uParamB`, `uMutA`, `uMutB`, `uPalOffA`, `uPalOffB`, `uPalFreqA`, `uPalFreqB`).
2. **Captures & Contact Sheet (`docs/screenshots/`):**
   - Refresh `docs/screenshots/generative_backdrops.png` (clean multi-seed contact sheet showing the polished generative backdrops including crisp `PLASMA`, `HEX`, `GRID`, `CIRCUIT`, `INK`, `STREAMS`, etc.) and refresh `docs/screenshots/latest_combat.png`, `docs/screenshots/latest_boss.png`, `docs/screenshots/latest_songlasers.png`, and `docs/screenshots/latest_speed.png` so GitHub reflects the polished generative backdrops in live combat.

---

## 4. Acceptance Criteria (`@qa`)

1. **Clean Release Build:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeds with **0 warnings and 0 errors**.
2. **Self-Test Suite Pass:** `./run.cmd --selftest` passes with `RESULT: PASS`, preserving `TestBackdropVariety` (`1,000` distinct genomes, `13/13` generators, `11/11` transforms) plus new `ComputeBackdropAudioDrives` and shader uniform hygiene assertions.
3. **Smoke & Live GL State Pass:** `./run.cmd --smoke 5` passes with `RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS` and smooth FPS (no shader compilation errors or warnings).
4. **Visual Capture Verification:**
   - Inspect `docs/screenshots/generative_backdrops.png`, `latest_combat.png`, `latest_boss.png`, `latest_songlasers.png`, and `latest_speed.png` via `read_file` to confirm crisp `PLASMA` contours, clean fog floor (not pure black, not washed-out pastel), rich visual variety across seeds, and high foreground combat readability.
5. **Documentation & Repo Sync (after `@programmer` finishes):**
   - `@designer` updates `CHANGELOG.md` and `README.md` (strictly game-focused; zero AI/agent/swarm/task-ID chatter), `@qa` verifies everything, and `@programmer` commits and pushes cleanly to `origin/main` after `@qa` sign-off.

---

## 5. Designer Notes (`@designer`)
- Keeping `genPlasma` sharp (contour ridges over a deep glow) solves the biggest visual weak spot among the 13 generators so `PLASMA` feels as deliberate and arcade-crisp as `CIRCUIT`, `HEX`, and `FRACTAL`.
- Separating drum/cymbal accents (`uKick` = scale/breathe/flare, `uSnare` = phase snap/lattice flash, `uHat` = high-frequency shimmer/node glints) makes every generated backdrop feel genuinely choreographed to the drum groove rather than pulsing uniformly.
- Note from working-tree TASK-008: `WorldRenderer.DrawBackdrop3D` was intentionally replaced by the combinatorial generative backdrop engine while keeping `uAim` shader parallax and `Renderer.ObliqueVec` aim camera tilt active.

---

## 6. Engineering Handoff (`@programmer`)
- **Files Modified (`@programmer` scope):**
  - `src/Nullwave/Render/Renderer.cs`: Added allocation-free `BackdropAudioDrives` record struct + `ComputeBackdropAudioDrives(AudioState, MusicDirector?, float)` with safe `[0, 3]` finite clamping; wired `uKick`, `uSnare`, `uHat`, `uDrop`, `uBuild`, and `drives.Breakdown` into `Renderer.Render(...)` and `Post.Process(...)`.
  - `src/Nullwave/Render/Shaders.cs`:
    - Removed all 10 unused V7 uniforms (`uStyleA/B`, `uParamA/B`, `uMutA/B`, `uPalOffA/B`, `uPalFreqA/B`) while keeping `uMix`, `uAim`, and active `uLA[3]`/`uLB[3]` genome uniforms.
    - Added `uniform float uSnare;` and `uniform float uHat;` next to `uKick`.
    - Upgraded all 7 new generators (`genInk`, `genTruchet`, `genInterference`, `genStreams`, `genGridPulse`, `genPlasma`, `genHex`) with distinct `uKick`/`uSnare`/`uHat`/`uBass`/`uHigh`/`uDrop` responses — replacing `genPlasma`'s blurry cosine blobs with crisp multi-frequency harmonic contour bands + `uKick`/`uDrop` ridge flare, `uSnare` phase kick, and `uHat` secondary shimmer.
    - Added subtle `uSnare` and `uHat` accents across all 6 classic generators (`styleLiquid`, `styleTunnel`, `styleKaleido`, `styleField`, `styleAurora`, `styleCells`) and `uDrop`/`uSnare` energy to `applyOp` (`WARP`, `SWIRL`, `RIPPLE`, `BREATHE`).
    - Tuned `BackgroundFrag.main()` drifting ambient fog floor (`0.10 + fog * 0.10 + uKick * 0.03 + uSnare * 0.025`, masked by `clamp(1.0 - lum * 3.0, 0.0, 1.0)`) and soft-clipping (`col * 1.04 / (1.0 + col * 1.85)`) while keeping `uAim` shader parallax and `ObliqueVec` aim tilt intact.
  - `src/Nullwave/Tools/SelfTest.cs`: Preserved all `TestBackdropVariety()` checks (`1,000` unique genomes, `13/13` generators, `11/11` transforms) and added `TestBackdropAudioReactivity()` verifying `ComputeBackdropAudioDrives` scaling/fallback/clamping and `BackgroundFrag` active onset uniforms + zero dead V7 uniforms.
  - `docs/screenshots/`: Refreshed `generative_backdrops.png` (4×3 contact sheet across 12 curated seeds covering all 13 generators including crisp `PLASMA`), plus `latest_combat.png`, `latest_boss.png`, `latest_songlasers.png`, and `latest_speed.png`.
- **Verification Results:**
  - `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` -> **0 Warning(s), 0 Error(s)**
  - `./run.cmd --selftest` -> **`RESULT: PASS (76374 assertions, 2.5s)`** (`backdrops: 1000 generated, 1000 distinct, 13/13 generators, 11/11 transforms, 2.0 layers avg`; `backdrop audio: kick=0.84 snare=0.96 hat=0.78 drop=1.08 deadV7Uniforms=0`)
  - `./run.cmd --smoke 5` -> **`SMOKE | 5.0s fps=116`** & **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**
  - Visual inspection via `read_file`: verified crisp contour bands on `PLASMA` (`STREAM+WARP>PLASMA | FOLD>GRID`), balanced moody fog floor with zero pastel washout, and razor-sharp foreground readability across all 4 combat captures and all 12 contact-sheet tiles.

---

## 7. QA Findings & Verdict (`@qa`)

- **Verdict:** **PASS (`APPROVED — 0 DEFECTS`)** — Full details recorded in `.game-studio/QA_REPORT.md` (§13).
- **Independent Build Verification:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeded in `0.57s` with **0 warnings, 0 errors**.
- **Independent SelfTest Verification:** `./run.cmd --selftest` passed with **`RESULT: PASS (76348 assertions, 2.4s)`**, confirming `TestBackdropVariety` (`1000 generated, 1000 distinct, 13/13 generators, 11/11 transforms, 2.0 layers avg`) and `TestBackdropAudioReactivity` (`kick=0.84 snare=0.96 hat=0.78 drop=1.08 deadV7Uniforms=0`).
- **Independent Smoke & Live GL State Verification:** `./run.cmd --smoke 5` passed at **`115 fps`** with **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
- **Code & Shader Review:** Confirmed zero per-frame heap allocations in `ComputeBackdropAudioDrives`, removal of all 10 dead V7 uniforms, active `uSnare`/`uHat`/`uKick`/`uDrop`/`uAim` bindings, crisp contour-ridge `genPlasma`, and balanced fog floor + soft-clipping in `BackgroundFrag.main()`.
- **Visual Captures & Docs Verification:** Visually inspected `docs/screenshots/generative_backdrops.png`, `latest_combat.png`, `latest_boss.png`, `latest_songlasers.png`, and `latest_speed.png`; verified all 25 links in `README.md` / `CHANGELOG.md` resolve on disk with zero non-game content.
