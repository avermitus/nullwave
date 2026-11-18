# TASK-007: Aim-Reactive 3D Backdrop Parallax & Camera Tilt, Richer Laser/Bullet Trails, and Expanded Laser Choreography

- **Status:** `done`
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer`
- **Reviewer (QA):** `@qa`

---

## 1. Goal & Player-Facing Benefit

1. **Aim-Reactive 3D Backdrop Parallax & Subtle Camera Tilt (`Renderer.cs`, `Shaders.cs`, `WorldRenderer.cs`, `Game.cs`):**
   - NULLWAVE already has a full perspective 3D backdrop mesh pass (`Renderer.Backdrop`, `SetBackdropCamera`, `Models.Column/Ring/HaloSegment/Knot/Ico/Octa/Crystal`) and an oblique orthographic world mesh pass (`Renderer.Oblique`), but `Renderer.Backdrop` has sat idle since V3 and the world oblique angle has been static.
   - `TASK-007` brings the arena depth to life:
     - Tracks a smoothly damped normalized `AimTilt` vector (`[-1, 1]`) from the player's aim direction and cursor offset.
     - Reactivates the 3D perspective backdrop (`WorldRenderer.DrawBackdrop3D`) with floating monolith columns, orbital torus rings, geometric knots, and crystalline shards in `z = -450..-2400` that subtly orbit, tilt, and parallax with `AimTilt` while pulsing with audio energy and beats.
     - Applies subtle aim-reactive oblique tilt (`ObliqueVec`) to 3D world meshes and `uAim` parallax to `Shaders.BackgroundFrag`, giving ships and monoliths tactile 3D depth as you aim around the screen while keeping `z = 0` collision and mouse unprojection 100% invariant.
2. **Richer Laser & Bullet Trails (`WorldRenderer.cs`):**
   - Upgrade **player bolts** and **enemy bullets** (`Orb`, `Needle`, `Shard`) with stronger dark contrast halos, elongated velocity-stretched core streaks, longer dual-layer comet ribbon tails, and trailing energy nodes so fast projectiles pop with razor-sharp arcade readability.
   - Upgrade **laser beams** (`DrawLasers`) from 3-layer lines to 4-layer HDR beams (wide bloom sheath, saturated neon body, beat-pulsing harmonic side rails, white-hot center), high-speed travelling energy chevrons/nodes along the beam, a glowing emitter flare at `Origin`, and an endpoint impact spark flare at `End`.
3. **Expanded Laser Choreography (`World.cs` `UpdateSongLasers` & `World.Enemies.cs`):**
   - Increase song-laser variety and presence (raising the active laser cap to `24`, triggering high-energy patterns from `0.55f` intensity on a 2-bar cadence from Wave 3+, expanding Drop `STARBURST` to **8 beams**, Build `SCAN` to **dual opposing sweeps**, and High-energy patterns to **4-beam `LASER GATE`**, **3-beam `LASER CROSS`**, and **4-beam rotating `PRISM CAGE`**), while strictly preserving `Breakdown > 0.5f` quietness and `Warmup >= beat * 2f` telegraphs.
   - Enrich **THE CHOIR** boss laser patterns across Phases II–IV (`World.Enemies.cs`) and add dual/cross laser choreography on Wave 3+ **Prism** elites.

---

## 2. Relevant Code & Ownership Constraints

### Strict File Ownership
- **`@designer` owns:** `.game-studio/BACKLOG.md`, `.game-studio/tasks/TASK-007.md` (Design & Task Specification), and (once `@programmer` finishes coding & captures) updating `CHANGELOG.md` and `README.md` (strictly game-focused).
- **`@programmer` owns:** `src/Nullwave/**` (`Render/Renderer.cs`, `Render/Shaders.cs`, `Render/WorldRenderer.cs`, `World/World.cs`, `World/World.Enemies.cs`, `Core/Game.cs`, `Tools/SelfTest.cs`), updating `docs/screenshots/latest_*.png` captures if requested, `.game-studio/tasks/TASK-007.md` (§6), and final Git commit/push after `@qa` sign-off.
- **`@qa` owns:** `.game-studio/QA_REPORT.md` and `.game-studio/tasks/TASK-007.md` (§7).

---

## 3. Implementation Plan (`@programmer`)

### A1. Aim-Reactive 3D Backdrop Parallax & Camera Tilt (`Renderer.cs`, `Shaders.cs`, `WorldRenderer.cs`, `Game.cs`)
1. **Smoothed `AimTilt` & `ObliqueVec` (`src/Nullwave/Render/Renderer.cs`):**
   - Add pure static helpers on `Renderer` (testable headlessly without a GL context):
     - `public static Vector2 ClampAimTilt(Vector2 raw)` -> clamps `X` and `Y` to `[-1f, 1f]` (handling non-finite inputs safely).
     - `public static Vector2 ComputeObliqueVec(Vector2 aimTilt)` -> returns `new Vector2(-aimTilt.X * 0.14f, Oblique + aimTilt.Y * 0.10f)`.
     - `public static Matrix4x4 BuildWorldMeshMatrix(Vector2 camera, Vector2 view, Vector2 obliqueVec)` -> builds the orthographic oblique projection matching `WorldMeshMatrix()` with `M31 = obliqueVec.X * ix` and `M32 = obliqueVec.Y * iy`.
     - `public static Vector2 LiftPoint(Vector2 pos, float z, Vector2 obliqueVec)` -> `new Vector2(pos.X + z * obliqueVec.X, pos.Y - z * obliqueVec.Y)` so 2D lifted overlays (`Lift(pos, z)`) match 3D oblique meshes pixel-for-pixel at every aim angle!
   - Add `public Vector2 AimTilt { get; set; }`, `public Vector2 ObliqueVec => ComputeObliqueVec(AimTilt);`, and `public Vector2 Lift(Vector2 p, float z) => LiftPoint(p, z, ObliqueVec);` on `Renderer`.
   - Update `WorldMeshMatrix()` to call `BuildWorldMeshMatrix(_camera, _view, ObliqueVec)` and update `_meshShader.SetVec3("uViewDir", new Vector3(-oblique.X, oblique.Y, 1f))` in `Render(...)`.
   - In `Game.Render()` (`src/Nullwave/Core/Game.cs`):
     - Compute target normalized aim offset from `world.Player.AimDir` blended with cursor screen offset (`(_mouseUi - (800, 450)) / (800, 450)` when `_input != null` and `_state == AppState.Playing`), clamped via `Renderer.ClampAimTilt(...)`, and smoothly damp `_aimTilt` using `M.Lerp(_aimTilt, targetAim, 1f - MathF.Exp(-_frameDt * 6.5f))` (or direct aim in capture mode so captures show deterministic tilt), setting `_renderer.AimTilt = _aimTilt;`.
2. **Shader Aim Parallax (`src/Nullwave/Render/Shaders.cs` & `Renderer.cs`):**
   - Add `uniform vec2 uAim;` to `Shaders.BackgroundFrag` (`Shaders.cs` ~line 80).
   - Apply subtle aim parallax in `BackgroundFrag.main()`: `uv += uCam * 0.00004 + uAim * 0.038;` and subtle aim-reactive directional accent wash.
   - Set `_background.SetVec2("uAim", AimTilt);` in `Renderer.Render(...)`.
3. **Reactivate Perspective 3D Backdrop (`src/Nullwave/Render/WorldRenderer.cs`):**
   - Add `DrawBackdrop3D(Renderer r, GameWorld w, Palette p, AudioState audio, float time)` called at the start of `WorldRenderer.Draw(...)`:
     - Set camera via `r.SetBackdropCamera(eye, target, 54f, fogColor)` where `eye` subtly orbits/tracks `r.Camera * 0.16f + r.AimTilt * new Vector2(220f, -150f)` at `z = 1150f` looking toward `target` (offset softly by `AimTilt * new Vector2(-140f, 95f)`), keeping `Renderer.SetBackdropCamera`'s `Matrix4x4.CreateScale(-1f, 1f, 1f)` x-mirror intact per `HANDOFF.md`.
     - Populate `r.Backdrop` (zero heap allocations — use deterministic math/loops, no per-frame arrays) with 3D geometric architecture in `z = -450..-2400`:
       - Deep-space monolith `Models.Column` pillars arranged in layered parallax rings, subtly bobbing and glowing with `w.Bass` / `audio.BeatPulse`.
       - Giant rotating `Models.Ring` / `Models.HaloSegment` orbital structures framing the horizon.
       - Floating `Models.Crystal` / `Models.Octa` / `Models.Knot` accents reacting to `w.Energy` and `w.BeatPulse`.
     - Respect `GameSettings.Current.BackgroundIntensity`: scale `r.BackdropBrightness = 0.36f * (GameSettings.Current.BackgroundIntensity / 100f)` (capped safely so foreground combat readability remains razor-sharp).

### A2. Richer Laser & Bullet Trails (`src/Nullwave/Render/WorldRenderer.cs`)
1. **`DrawBullets` (`WorldRenderer.cs`):**
   - **Player Bolts:** Add a wider soft outer bloom sheath (`b.Radius * 3.8f`), an elongated velocity-stretched core streak (`b.Pos - dir * tailLen` to `b.Pos`), a bright white-hot nose, and a subtle trailing plasma spark node behind high-speed/heavy shots.
   - **Enemy Bullets:** Strengthen the dark contrast halo (`b.Radius * 2.25f`, alpha `0.58f`) for crisp readability over the 3D backdrop, extend dual-layer comet ribbon tails (`Math.Clamp(b.Vel.Length() * 0.145f, 28f, 120f) * (1f + onBeat * 0.7f)`), and add crisp trailing shimmer nodes along the ribbon tail for `Needle`, `Shard`, and `Orb` shots.
2. **`DrawLasers` (`WorldRenderer.cs`):**
   - **Warmup Telegraph:** Add pulsing outer boundary guide rails alongside the dashed center line and a rotating target/emitter lock ring at `Origin`.
   - **Active Beams (4-Layer HDR + Travelling Chevrons + Flares):**
     - Layer 1: Wide outer bloom sheath (`l.Width * 3.4f`, low alpha `0.06f * a`).
     - Layer 2: Saturated mid body (`l.Width * 1.25f`, `0.26f * a`).
     - Layer 3: Beat-pulsing harmonic side rails offset by `±M.Perp(dir) * (l.Width * 0.36f * (1f + w.BeatPulse * 0.35f))`.
     - Layer 4: White-hot inner core (`MathF.Max(2.5f, l.Width * 0.18f)`).
     - **Travelling Energy Chevrons / Nodes:** 8 high-speed directional chevrons (`V`-shaped line pairs + core dots) streaming from `Origin` toward `End` at `1250 u/s`.
     - **Emitter & Endpoint Flares:** Glowing concentric pulse rings + cross-flares at `l.Origin` and `end`.

### A3. Expanded Laser Choreography (`World.cs` & `World.Enemies.cs`)
1. **In `src/Nullwave/World/World.cs` (`UpdateSongLasers`):**
   - Raise active laser limit from `16` to `24` (`if (Lasers.Count > 24 || _cueBreakdown > 0.5f) ...`).
   - **Drop (`_cueDrop`):** Upgrade `STARBURST` from 6 beams to **8 beams** (`for (int i = 0; i < 8; i++)`), keeping `Warmup = beat * 2f`.
   - **Build (`_cueBuild > 0.5f && _songBar % 2 == 0`):** Fire **dual opposing `SCAN` sweeps** converging across the player's neighborhood (`Warmup = beat * 2f`).
   - **High Energy (`_cueIntensity > 0.55f && _songBar % 2 == 0`, with Wave >= 3 using full patterns and Wave 2 alternating):**
     - Cycle cleanly across 3 distinct high-energy choreographies:
       - `LASER GATE`: **4 parallel beams** with clear navigable lanes (`300u` spacing).
       - `LASER CROSS`: **3-beam symmetric star/cross** through the player's neighborhood (`60°` spacing).
       - `PRISM CAGE`: **4 rotating boundary beams** framing an arena box around the player (`spin = ±0.16f`).
   - **Invariant Check:** `Breakdown > 0.5f` MUST still emit `0` lasers, and every song laser MUST still use `Warmup >= beat * 2f` so `TestSongLasers` passes 100%.
2. **In `src/Nullwave/World/World.Enemies.cs` (`BossAttack` & `FireEnemy`):**
   - In `BossAttack`:
     - Pattern 1: In Phase II+ (`phase >= 1`), fire twin mirrored sweeping Choir beams (`±0.55f` sweep) instead of a single beam; in Phase III+ (`phase >= 2`), fire 3 radial sweeping beams (`Lasers.Count < 28`).
     - Pattern 2: Keep the gap-ring + sweeping cross beam (`phase >= 1` adds an opposing secondary sweep).
     - Pattern 3 (`phase >= 2`): Fire twin crossing beams (`±0.9f` sweep) alongside the ring + fan.
   - In `FireEnemy` (`EnemyRole.Prism`): On Wave 3+ (or Elite), project twin opposing `560`-unit rotating laser blades (`180°` apart) so `TestNewEnemies` (`MathF.Abs(l.Length - 560f) < 1f`) continues to pass cleanly while Prism enemies look far more threatening.

### A4. Deterministic Self-Test Suite (`src/Nullwave/Tools/SelfTest.cs`)
- Add `TestAimCameraAndLaserVisuals()` called from `SelfTest.Run()`, verifying:
  1. `Renderer.ClampAimTilt` bounds extreme and NaN values to `[-1, 1]`.
  2. `Renderer.ComputeObliqueVec` and `Renderer.BuildWorldMeshMatrix` preserve exact `z = 0` invariance (`Vector4.Transform(new Vector4(x, y, 0, 1), mat)` is independent of `AimTilt`) and `Renderer.LiftPoint(pos, z, oblique)` matches the oblique matrix projection of `(pos.X, pos.Y, z)` across non-zero `AimTilt` values.
  3. Expanded `SongLasers` behavior: Drop fires **8** starburst beams, Build fires **2** opposing scan sweeps, High-energy (`0.65f` intensity) fires Gate/Cross/Prism Cage patterns, and Breakdown (`1.0f`) fires **0** song lasers.
  4. Choir boss Phase II+ (`BossPhase >= 1`) spawns multi-beam Choir laser volleys cleanly within caps.

---

## 4. Acceptance Criteria (`@qa`)

1. **Clean Release Build:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeds with **0 warnings and 0 errors**.
2. **Self-Test Suite Pass:** `./run.cmd --selftest` passes with `RESULT: PASS` and new assertions from `TestAimCameraAndLaserVisuals()`, plus zero regressions in `TestSongLasers`, `TestBossPhaseChoreography`, `TestNewEnemies`, and `TestModels`.
3. **Smoke Run Pass:** `./run.cmd --smoke 5` passes with `RENDERSTATE -> PASS` and strong FPS (verify 3D backdrop pass + richer trails do not cause stalls or leaks).
4. **Visual Capture Verification:**
   - Run `./run.cmd --capture combat --out screenshots/combat.png`, `--capture boss --out screenshots/boss.png`, `--capture laser --out screenshots/laser.png`, `--capture songlasers --out screenshots/songlasers.png`, and `--capture speed --out screenshots/speed.png`, and inspect with `read_file` to verify the 3D backdrop depth, subtle aim tilt, 4-layer HDR lasers with chevrons/flares, and richer bullet trails look clean and readable.
5. **Documentation & Repo Sync (after `@programmer` finishes):**
   - `@designer` updates `CHANGELOG.md` and `README.md` (covering Spotify integration, `--mods`/`--attune`/`--seed`, aim-reactive 3D backdrop parallax, richer trails, and expanded laser choreography — strictly game-focused), `@qa` verifies everything, and `@programmer` commits & pushes cleanly to `origin/main` once `@qa` signs off.

---

## 5. Designer Notes (`@designer`)
- Keeping `DrawBackdrop3D` brightness scaled around `0.36 * BackgroundIntensity` and bounded in `z = -450..-2400` with exponential fog guarantees the new 3D parallax architecture enhances depth without competing with foreground bullets and enemies.
- Making `WorldRenderer.Lift` delegate to `r.Lift(p, z)` ensures monolith crystal/arc overlays and enemy health bars track the subtle aim-reactive oblique lean cleanly without drifting off their 3D meshes.

---

## 6. Engineering Handoff (`@programmer`)

### Summary of Changes
1. **Aim-Reactive 3D Backdrop Parallax & Subtle Camera Tilt (`src/Nullwave/Render/Renderer.cs`, `Shaders.cs`, `WorldRenderer.cs`, `Core/Game.cs`):**
   - Added pure static math helpers `ClampAimTilt`, `ComputeObliqueVec`, `BuildWorldMeshMatrix`, and `LiftPoint` plus `AimTilt`, `ObliqueVec`, and `Lift(pos, z)` on `Renderer` (`Renderer.cs`). Verified `z = 0` ground footprints and `ScreenToWorld` remain 100% invariant across all aim angles, while 3D world meshes and lifted 2D overlays lean subtly toward the aim vector.
   - Smoothly damped `_aimTilt` inside `Game.Update(dt)` (`Game.cs`, ensuring `Game.Render()` stays 100% idempotent on double-renders) and passed `AimTilt` to `Renderer`.
   - Added `uniform vec2 uAim;` parallax (`+ uAim * 0.038`) and subtle directional accent wash to `Shaders.BackgroundFrag` (`Shaders.cs`).
   - Reactivated the perspective 3D backdrop (`WorldRenderer.DrawBackdrop3D`) in `WorldRenderer.cs` with zero per-frame allocations: 18 deep-space monolith `Column`s in two rings, 3 rotating orbital `Ring`s, 12 `HaloSegment`s, 6 `Crystal`/`Octa` accents, and 2 `Knot` sculptures in `z = -760..-2200` orbiting with `AimTilt` and pulsing with audio (`BackdropBrightness = 0.22f * BackgroundIntensity / 100f`). Updated `WorldRenderer.Lift` to delegate to `r.Lift`.
2. **Richer Laser & Bullet Trails (`src/Nullwave/Render/WorldRenderer.cs`):**
   - Upgraded `DrawBullets`: player bolts have wider outer bloom (`b.Radius * 3.8f`), elongated velocity-stretched core streaks, white-hot nose, and trailing plasma spark nodes; enemy bullets have stronger dark contrast halos (`b.Radius * 2.25f`, `0.58f`), longer dual-layer comet ribbon tails (`28..120u`), and dual trailing shimmer nodes.
   - Upgraded `DrawLasers`: warmup telegraphs gain pulsing outer boundary rails + rotating emitter lock arc; active beams render as 4-layer HDR beams (bloom sheath, saturated neon body, beat-pulsing harmonic side rails, white-hot core) with 8 high-speed travelling directional chevrons (`1250 u/s`) and concentric emitter/endpoint flares.
3. **Expanded Laser Choreography (`src/Nullwave/World/World.cs`, `World/World.Enemies.cs`):**
   - Updated `World.UpdateSongLasers`: raised cap to `24`, widened high-energy cadence (`> 0.55f` every 2 bars on Wave 3+), expanded Drop `STARBURST` to **8 beams**, Build `SCAN` to **dual opposing sweeps**, and High-energy patterns to 4-beam `LASER GATE`, 3-beam `LASER CROSS` (`60°`), and 4-beam rotating `PRISM CAGE` — preserving `Breakdown > 0.5f` = `0` lasers and `Warmup = beat * 2f`.
   - Updated `World.Enemies.cs`: Choir Phase II+ fires mirrored/3-beam sweeping laser volleys (`BossAttack`) and Wave 3+ / Elite `Prism` enemies project twin opposing `560`-unit rotating laser blades.
4. **Deterministic SelfTest & Refreshed Captures (`src/Nullwave/Tools/SelfTest.cs`, `docs/screenshots/latest_*.png`):**
   - Added `TestAimCameraAndLaserVisuals()` to `SelfTest.Run()`.
   - Refreshed `docs/screenshots/latest_combat.png`, `latest_boss.png`, `latest_speed.png`, `latest_pause.png`, and added `latest_title.png`, `latest_songlasers.png`.

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
    aim & lasers: oblique=(-0.12,0.36) dropBeams=8 buildBeams=2 highVolleys=8 choirMaxLasers=4
  RESULT: PASS (67677 assertions, 2.6s)
  ```
- **Smoke & Live GL State Verification (`--smoke 5`):**
  ```text
  ./run.cmd --smoke 5
  SMOKE | 5.0s fps=64 wave=1 score=2415 enemies=2 bullets=2 bpm=124.9 audio=Headphones (Realtek(R) Audio) capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```
- **Visual Capture Verification:**
  - Visually inspected `screenshots/combat.png`, `screenshots/boss.png`, `screenshots/laser.png`, `screenshots/songlasers.png`, and `screenshots/speed.png` with `read_file`; confirmed balanced 3D backdrop depth, crisp 4-layer HDR lasers with chevrons/flares, 8-beam Starburst, richer bullet tails, and zero HUD overlap.

---

## 7. QA Findings & Verdict (`@qa`)

- **Verdict:** **PASS (`APPROVED — 0 DEFECTS`)** — Full details recorded in `.game-studio/QA_REPORT.md` (§12).
- **Independent Build Verification:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeded in `0.57s` with **0 warnings, 0 errors**.
- **Independent SelfTest Verification:** `./run.cmd --selftest` passed with **`RESULT: PASS (67677 assertions, 2.3s)`**, verifying `TestAimCameraAndLaserVisuals()` (`ClampAimTilt`, `z = 0` invariance across `AimTilt`, `LiftPoint` projection parity, 8-beam Drop `STARBURST`, 2-beam Build `SCAN`, 4-beam/3-beam High-energy Gate/Cross/Cage, `Breakdown == 0`, and Choir Phase II+ multi-beam sweeps).
- **Independent Smoke & Live GL State Verification:** `./run.cmd --smoke 5` passed at **`64 fps`** with **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
- **Code & Hot-Path Review:** Confirmed zero per-frame heap allocations in `WorldRenderer.DrawBackdrop3D`, `DrawBullets`, and `DrawLasers`, and idempotent `_aimTilt` damping in `Game.Update(dt)`.
- **Visual Captures & Docs Verification:** Visually inspected `screenshots/combat.png`, `boss.png`, `laser.png`, `songlasers.png`, `speed.png`, `docs/screenshots/latest_title.png`, and `docs/screenshots/latest_songlasers.png` with zero HUD overlap; verified all 24 links in `README.md` / `CHANGELOG.md` resolve on disk and contain zero non-game content.
