# TASK-014: Stationary 3D-Only Reactive Backdrops & Combat Readability

- **Status:** `done`
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer`
- **Reviewer (QA):** `@qa`

---

## 1. Goal & Player-Facing Benefit

Directly addresses player feedback: *"make the background stay stationary compared to the ship; it's very hard to tell the difference between the ship, bullets, and the background; also no 2D background."*

1. **100% Stationary Relative to Ship (`Shaders.cs`, `Renderer.cs`):**
   - Lock `BackgroundFrag` strictly to screen-centered `uv = screenPos`.
   - Remove all `uCam` panning, `uAim` shift/parallax, `uVel`/`uTravel` drift, global `rot`/`twist`/`breathe`/`inhale` UV warping, and layer `pShift`. The 3D environment stays rock-steady behind the ship while its *lighting, emissive ribs, Fresnel rims, and depth glow* pulse vividly to kicks, snares, hats, drops, and Overdrive in place.
2. **Zero 2D Backgrounds — 5 Genuine 3D-Only Scenes (`BackdropGenome.cs`, `Shaders.cs`):**
   - Retire all flat 2D generators (`INK`, `CELLS`, `FRACTAL`, `CIRCUIT`, `MOIRE`, `STREAMS`, `GRID`, `LIQUID` 2D layers, `PLASMA`, `AURORA`, `HEX`) and disable all 2D space-warping ops (`Ops = [0, 0, 0, 0]`, `Layers.Count == 1`).
   - Every generated backdrop is a single **true 3D raymarched or 3D perspective scene** (`GeneratorCount = 5`):
     - `0: LIQUID 3D` — Stationary 3D raymarched obsidian/metallic metaball sculpture with beat-lit Fresnel contours & specular glints (no camera orbit/spin).
     - `1: CYBER TUNNEL 3D` — Stationary 3D perspective polygonal chamber receding into obsidian depth fog, with stationary neon ribs whose intensity/rim lighting pulses on beats/drops (no forward motion or sway).
     - `2: VOID HORIZON 3D` — Stationary 3D perspective twin-plane architectural floor + ceiling grid receding to the horizon with beat-lit depth attenuation and snare rim waves.
     - `3: CATHEDRAL 3D` — Stationary 3D raymarched fluted monolith colonnade receding in true perspective with dramatic depth fog and beat-pulsing architectural rim light.
     - `4: ORBITAL GYROID 3D` — Stationary 3D raymarched concentric torus rings & gyroid sculpture suspended in deep 3D space with hi-hat specular glints and kick rim glow.
3. **Unmistakable Figure-Ground Separation (Ship, Enemies & Bullets Pop):**
   - Keep 3D backdrop luminance **strictly below the bloom threshold** (`<= 0.34` resting / `<= 0.48` peak drops in **Standard**; `<= 0.14` resting / `<= 0.38` peak drops + `uLights[12]` in **Abyssal**), paired with dark-navy/obsidian atmospheric depth fog and a gentle center combat-zone readability well so the bright 3D player ship, enemies, lasers, and bullets separate instantaneously in both themes.

---

## 2. File Ownership
- **`@designer`:** `.game-studio/BACKLOG.md`, `.game-studio/tasks/TASK-014.md`, `CHANGELOG.md`, `README.md`.
- **`@programmer`:** `src/Nullwave/Render/BackdropGenome.cs`, `src/Nullwave/Render/Shaders.cs`, `src/Nullwave/Render/Renderer.cs`, `src/Nullwave/Tools/SelfTest.cs`, `docs/screenshots/drop_overdrive.png`, `docs/screenshots/reactive_backdrop_standard.png`, `TASK-014.md` §6, git commit/push after `@qa` PASS.
- **`@qa`:** `.game-studio/QA_REPORT.md`, `TASK-014.md` §7.

---

## 3. Implementation Plan (`@programmer`)

1. **`src/Nullwave/Render/BackdropGenome.cs`:**
   - Set `GeneratorCount = 5`, `GeneratorNames = { "LIQUID 3D", "CYBER TUNNEL 3D", "VOID HORIZON 3D", "CATHEDRAL 3D", "ORBITAL GYROID 3D" }`.
   - Always generate `Layers.Count == 1`, `l.Ops = [0, 0, 0, 0]`, `l.Blend = 0`, `l.Gain = 1f`, picking `Generator` in `0..4` (different from `previous` on reroll), with cool/deep palette parameters.
   - Add `public static bool Is3DOnly(BackdropGenome g)` returning `g.Layers.Count == 1 && g.Layers[0].Generator >= 0 && g.Layers[0].Generator < GeneratorCount && g.Layers[0].Ops.All(o => o == 0)`.
2. **`src/Nullwave/Render/Shaders.cs` (`BackgroundFrag`):**
   - Lock `vec2 uv = screenPos;` — **zero** `uCam`, `uAim`, `uVel`, `uTravel`, `rot`, `twist`, `breathe`, `inhale`, or `pShift` displacement of `uv`!
   - Implement the 5 stationary true-3D raymarched / 3D perspective scenes (`genLiquid3D`, `genCyberTunnel3D`, `genVoidHorizon3D`, `genCathedral3D`, `genOrbitalGyroid3D`) with fixed perspective camera rays `vec3 rd = normalize(vec3(uv, 1.5))`, real 3D lighting (normals / depth fog / Fresnel rims / specular), and stationary beat/snare/hat/drop/overdrive/anticipation lighting pulses (no drifting geometry or sliding floors).
   - Keep `uMix` backdrop transition as a smooth radial fade/reveal (`mix(next, rawCol, wipe)`) without blinding rings.
   - Enforce bloom-safe figure-ground luminance ceiling:
     - **Standard:** deep dark-navy/indigo fog (`vec3(0.015, 0.02, 0.045)`), subtle center combat readability well (`0.72 + 0.28 * smoothstep(0.15, 0.95, r)`), subdued highlight curve capping output `<= 0.48` even on peak drops (well below `BrightFrag`'s `0.62` bloom threshold!).
     - **Abyssal (`uDarkTheme > 0.5`):** pitch-black OLED floor (`vec3(0.004, 0.005, 0.012)`), crisp beat-lit 3D specular rims + `uLights[12]` illuminating the stationary 3D surfaces around combat, capped `<= 0.42` (well below `0.72` Abyssal bloom threshold!).
   - Respect `uIntensity`, `ReduceFlashing` (dampened lighting pulses), and zero per-frame allocations.
3. **`src/Nullwave/Render/Renderer.cs`:**
   - Keep existing uniform uploads clean (no crash if unused uniforms are optimized out by GLSL; `_background.Set*` ignores `-1` locations). Update `ForceBackdrop(int seed)` to remain deterministic across the 5 3D scenes.
4. **`src/Nullwave/Tools/SelfTest.cs`:**
   - Add `Test3DBackdropAndReadability()` (and update any old 13-generator backdrop assertions in `SelfTest.cs` if present):
     - Generate 50 random `BackdropGenome` instances and assert `BackdropGenome.Is3DOnly(g)` is true for 100% of them and all 5 3D scenes are reachable.
     - Assert `Shaders.BackgroundFrag` locks `uv = screenPos` without `uCam`/`uAim` UV panning, removes legacy 2D generators (`genInk`/`genPlasma`/`genTruchet`/`genHex`), and preserves `uDarkTheme`, `uLights`, `uOverdrive`, `uAnticipation`.
5. **Captures & Verification:**
   - Re-capture `docs/screenshots/drop_overdrive.png` (`--capture overdrive --theme abyssal --attune synergy`) and `docs/screenshots/reactive_backdrop_standard.png` (`--capture overdrive --theme standard --attune synergy`) and visually inspect both via `read_file` to prove genuine 3D depth + unmistakable ship/enemy/bullet contrast!
   - Run Release build (`0w/0e`), `./run.cmd --selftest`, `./run.cmd --smoke 5` (Standard), and `./run.cmd --smoke 4 --attune synergy --theme abyssal`.

---

## 4. Acceptance Criteria (`@qa`)
1. Release build: **0 warnings, 0 errors**.
2. `./run.cmd --selftest`: **PASS** including `Test3DBackdropAndReadability()`.
3. `./run.cmd --smoke 5` (Standard) & `--smoke 4 --attune synergy --theme abyssal`: **RENDERSTATE -> PASS**, `alphaMin=255`, `rerenderDiff=0.000 %`, high FPS.
4. Visual inspection of `docs/screenshots/drop_overdrive.png` & `docs/screenshots/reactive_backdrop_standard.png`: genuine stationary 3D depth (zero flat 2D patterns), unmistakable ship/bullet/enemy readability in both themes, zero HUD overlap.
5. `CHANGELOG.md` & `README.md` updated cleanly (strictly game-focused), then committed & pushed to `origin/main`.

---

## 5. Engineering Handoff (`@programmer`)
- **Changed files:**
  - `src/Nullwave/Render/BackdropGenome.cs`: Locked `GeneratorCount = 5`, `MaxLayers = 1`, `OpCount = 0`, `GeneratorNames = { "LIQUID 3D", "CYBER TUNNEL 3D", "VOID HORIZON 3D", "CATHEDRAL 3D", "ORBITAL GYROID 3D" }`, cool/deep architectural palette parameters, consecutive scene switching, and `BackdropGenome.Is3DOnly(g)`.
  - `src/Nullwave/Render/Shaders.cs`: Replaced `BackgroundFrag` with 100% stationary screen-centered `vec2 uv = screenPos;` (retired all `uCam`, `uAim`, `uVel`, `uTravel`, `uSpeed`, `pShift`, `rot`/`twist`/`breathe`/`inhale` UV warping, all 11 flat 2D generators, and `applyOp`). Implemented all 5 stationary true-3D scenes (`genLiquid3D`, `genCyberTunnel3D`, `genVoidHorizon3D`, `genCathedral3D`, `genOrbitalGyroid3D`) with in-place beat/snare/hat/drop/overdrive/anticipation lighting pulses, center combat readability well, and strict bloom-safe luminance ceilings (`<= 0.48` Standard, `<= 0.42` Abyssal + `uLights[12]`).
  - `src/Nullwave/Render/Renderer.cs`: Removed retired `uCam`/`uTravel`/`uSpeed`/`uVel`/`uAim` backdrop uploads and unused `_travel` field; capped backdrop `uIntensity` to `1.05`.
  - `src/Nullwave/Tools/SelfTest.cs`: Updated `TestBackdropVariety()` & `TestBackdropAudioReactivity()`, and added `Test3DBackdropAndReadability()` verifying 100% 3D-only single-layer genomes, all 5 3D scenes reachable, `vec2 uv = screenPos;`, and complete retirement of legacy 2D generators/camera UV panning.
  - `docs/screenshots/drop_overdrive.png` (`CYBER TUNNEL 3D`, Abyssal) & `docs/screenshots/reactive_backdrop_standard.png` (`VOID HORIZON 3D`, Standard): Re-captured and visually verified genuine 3D depth + unmistakable ship/enemy/bullet contrast + zero HUD overlap.
- **Verification:**
  - Release build: **0 Warning(s), 0 Error(s)**.
  - `./run.cmd --selftest`: **`RESULT: PASS (74143 assertions, 2.5s)`** (`3d backdrop: scenes=5/5 stationaryUv=true 2dGenerators=0`).
  - `./run.cmd --smoke 5` (Standard): **`5.0s fps=114`**, **`RENDERSTATE -> PASS`** (`alphaMin=255`, `rerenderDiff=0.000 %`).
  - `./run.cmd --smoke 4 --attune synergy --theme abyssal`: **`4.0s fps=113`**, **`RENDERSTATE -> PASS`** (`alphaMin=255`, `rerenderDiff=0.000 %`).

## 6. QA Findings & Verdict (`@qa`)
- **Verdict:** **PASS (`APPROVED — 0 DEFECTS`)**
- **Build & Automated Verification:**
  - `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` -> **0 Warning(s), 0 Error(s)** (`0.60s`).
  - `./run.cmd --selftest` -> **`RESULT: PASS (74143 assertions, 2.6s)`** (`backdrops: 1000 generated, 1000 distinct, 5/5 3D scenes, 0 2D ops`; `3d backdrop: scenes=5/5 stationaryUv=true 2dGenerators=0`).
  - `./run.cmd --smoke 5` (Standard) -> **`5.0s fps=119`**, **`RENDERSTATE -> PASS`** (`alphaMin=255`, `rerenderDiff=0.000 %`).
  - `./run.cmd --smoke 4 --attune synergy --theme abyssal` -> **`4.0s fps=110`**, **`RENDERSTATE -> PASS`** (`alphaMin=255`, `rerenderDiff=0.000 %`).
- **Visual & Docs Inspection:**
  - Visually verified `docs/screenshots/drop_overdrive.png` (Abyssal `CYBER TUNNEL 3D`) and `docs/screenshots/reactive_backdrop_standard.png` (Standard `VOID HORIZON 3D`) via `read_file`: genuine stationary 3D depth with zero 2D clutter, unmistakable ship/enemy/bullet contrast in both themes, and zero HUD overlap.
  - Verified `README.md` & `CHANGELOG.md`: all image links resolve (`Test-Path == True`) with strictly game-focused copy and zero non-game references. Logged in `.game-studio/QA_REPORT.md` (§18).
