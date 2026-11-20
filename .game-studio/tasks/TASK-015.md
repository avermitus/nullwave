# TASK-015: Map-Anchored Beat-Animated 3D Backdrops & Galaga Enemy Entry

- **Status:** `done` — map-anchored backdrop kept (since replaced by TASK-016 worlds); **Galaga entry reverted** after owner playtest ("makes it impossible to play")
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer` (streamlined ownership for quota conservation: also updates `CHANGELOG.md`, `README.md`, and `BACKLOG.md` Done after `@qa` PASS)
- **Reviewer (QA):** `@qa`

---

## 1. Goal & Player-Facing Benefit

1. **Map-Anchored (World-Locked) 3D Camera (`Shaders.cs`, `Renderer.cs`):**
   - Anchor all 5 true-3D scenes (`LIQUID 3D`, `CYBER TUNNEL 3D`, `VOID HORIZON 3D`, `CATHEDRAL 3D`, `ORBITAL GYROID 3D`) directly to **arena world coordinates (`uCam`)** so the 3D environment stays physically locked to the map as the ship flies over/through it (zero `uAim` wobble/swimming, zero 2D generators, zero 2D UV ops).
2. **Bold, Intense Beat-Animated 3D Reactivity (`Shaders.cs`):**
   - Remove the dim `0.48/0.42` brightness cap that made TASK-014 feel weak.
   - Animate the 3D geometry vividly to the beat: kick-pumping 3D pillar/metaball/gyroid/grid motion & extrusion, snare-flashing emissive ribs & rings, hi-hat specular glints, beat-synced sonar waves sweeping across the 3D surfaces, and dramatic **Drop Overdrive** / `uAnticipation` lighting surges — preserving deep dark valleys between glowing 3D forms so bright bullets, lasers, and ships remain crisp and unmistakable in both **Standard** (vibrant & luminous) and **Abyssal** (pitch-black OLED void with electric neon 3D rims + `uLights[12]`).
3. **Galaga-Style Enemy Zoom-In & Settle-Before-Fire (`Entities.cs`, `World.Enemies.cs`, `WorldRenderer.cs`):**
   - Off-screen enemies zoom/swoop rapidly onto the visible viewport (`~2.2x` entry speed, never firing off-screen), brake cleanly into an on-screen combat margin, and hold for a brief **`0.55s` telegraph settle** (with a crisp contracting lock-on/charge ring) before opening fire on the beat grid.

---

## 2. Implementation Plan (`@programmer`)

### A1. Map-Anchored, Bold Beat-Animated 3D Backdrops (`Shaders.cs`, `Renderer.cs`, `BackdropGenome.cs`)
- Keep `BackdropGenome.Is3DOnly(g)` (5/5 true-3D scenes, `Layers.Count == 1`, `Ops = [0,0,0,0]`).
- In `Shaders.cs` (`BackgroundFrag`):
  - Keep perspective ray `vec3 rd = normalize(vec3(screenPos, 1.45));` (no `uAim` pan/tilt wobble on UV!).
  - Anchor the 3D ray origin / world space directly to `uCam` (`vec2 mapPos = uCam * 0.0022;`), so flying north/south/east/west moves smoothly past fixed 3D pillars, infinite horizon grids, metaball clusters, tunnel ribs, and gyroid rings bolted to the arena map!
  - Animate 3D structures dynamically to `uFlow`, `uKick`, `uSnare`, `uHat`, `uBass`, `uDrop`, `uOverdrive`, `uAnticipation` (scaled safely by `uMotionScale` / `ReduceFlashing`):
    - **`LIQUID 3D`:** World-anchored metaball cluster grid (`mod` in world XY) pulsing/morphing to `uBass`+`uKick` with rich iridescent Fresnel rims & snare/drop core flashes.
    - **`CYBER TUNNEL 3D`:** World-offset 3D perspective polygonal tunnel whose glowing neon ribs pulse & march along Z on `uFlow`+`uKick`, with snare ring strobes and vivid Drop Overdrive overdrive glow.
    - **`VOID HORIZON 3D`:** World-anchored (`mapPos`) twin-plane 3D perspective floor + vaulted ceiling grid with bold glowing lattice lines, beat-pulsing 3D horizon wave crests, and sweeping sonar light bands.
    - **`CATHEDRAL 3D`:** World-anchored (`mapPos`) 3D raymarched colonnade of fluted obsidian monolith pillars whose glowing emissive trims & vertical light ribs surge on kicks/snares/drops.
    - **`ORBITAL GYROID 3D`:** World-anchored (`mapPos`) 3D gyroid sculpture & rotating concentric orbital rings spinning smoothly with `uFlow` and flaring brightly on kicks, hi-hats, and drops.
  - **Bold, Vivid Contrast (No Weak/Dim Cap):**
    - **Standard:** Remove the timid `0.48` cap and harsh center dimming; use a rich, luminous ACES-friendly curve (`col = col * (1.28 + reactiveBoost) / (1.0 + col * 1.15)`) with crisp dark obsidian valleys (`#050712`) between glowing 3D surfaces so the backdrop looks genuinely awesome while bright white-cored bullets & ship pop cleanly!
    - **Abyssal:** Preserve pitch-black OLED void valleys (`vec3(0.004, 0.005, 0.012)`) while letting beat-lit 3D Fresnel rims, emissive ribs, sonar sweeps, and `uLights[12]` blaze brightly (`<= 0.82` on peak drops) out of the darkness!

### A2. Galaga-Style Enemy Zoom-In & Settle-Before-Fire (`Entities.cs`, `World.Enemies.cs`, `WorldRenderer.cs`)
- In `src/Nullwave/World/Entities.cs` (`Enemy`):
  - Add `public byte EntryState;` (`0 = Zooming`, `1 = Settling`, `2 = Combat`), `public float EntrySettleTimer;`, `public Vector2 EntryTarget;`.
- In `src/Nullwave/World/World.Enemies.cs`:
  - On non-boss spawn (`SpawnEnemy`): compute visible camera half-extents (`halfW = 780f / MathF.Max(0.4f, Cam.Zoom)`, `halfH = 440f / MathF.Max(0.4f, Cam.Zoom)`). If the enemy spawns outside the visible viewport margin (`MathF.Abs(pos.X - Cam.Position.X) > halfW - 70f || MathF.Abs(pos.Y - Cam.Position.Y) > halfH - 70f`), set `e.EntryState = 0`, pick a crisp on-screen entry point inside `[Cam.Pos ± (halfW - 110, halfH - 95)]` (clamped to arena & nudged away from the player by `>= 220u`), and set `e.EntrySettleTimer = 0.55f`. Bosses and enemies already inside the viewport start in `EntryState = 2` (or `1` with `0.35s` settle if freshly spawned).
  - In `UpdateEnemies(dt, audio)`:
    - **`EntryState == 0 (Zooming)`:** Steer swiftly toward an updated on-screen target (`~2.2x` role cruise speed, min `480 u/s`) with brief entry motion trail particles. **Skip weapon firing completely** (`e.Telegraph = 0f;`). Once inside the on-screen combat rect (`MathF.Abs(e.Pos.X - Cam.Position.X) <= halfW - 85f && MathF.Abs(e.Pos.Y - Cam.Position.Y) <= halfH - 85f`, or within `55u` of `EntryTarget`, or after a `2.8s` safety timeout), transition to `EntryState = 1` and set `e.EntrySettleTimer = 0.55f`.
    - **`EntryState == 1 (Settling)`:** Brake smoothly (`e.Vel *= MathF.Exp(-dt * 7.5f)`), track facing toward the player, ramp `e.Telegraph = M.Clamp01(1f - e.EntrySettleTimer / 0.55f)`, and **hold fire** until `e.EntrySettleTimer <= 0f` -> transition to `EntryState = 2 (Combat)` (with `e.FireBeats` primed cleanly so the first shot fires right on the next eligible beat!).
    - **`EntryState == 2 (Combat)`:** Normal AI & beat-locked firing; also guard non-boss firing so off-screen enemies that drift far off-screen zoom back in rather than sniping blindly off-screen (Boss exempt).
- In `src/Nullwave/Render/WorldRenderer.cs`:
  - Render a sleek contracting Galaga-style lock-on ring around enemies in `EntryState == 1 (Settling)` and bright swoop tail streaks during `EntryState == 0 (Zooming)`.

### A3. Self-Tests, Captures & Streamlined Docs (`SelfTest.cs`, `CHANGELOG.md`, `README.md`)
- Update `Test3DBackdropAndReadability()` in `SelfTest.cs` to assert:
  - 100% of genomes satisfy `BackdropGenome.Is3DOnly(g)` across all 5 3D scenes (`0` 2D ops/generators).
  - `BackgroundFrag` anchors 3D camera to `uCam` (`mapPos`) without `uAim` UV swimming, and includes beat-reactive 3D motion + `uDarkTheme` / `uLights` / `uOverdrive` / `uAnticipation`.
- Add `TestGalagaEnemyEntry()` in `SelfTest.cs` proving:
  - Off-screen spawned non-boss enemy starts in `EntryState == 0`, zooms toward screen at boosted speed, fires **0** bullets while off-screen/zooming, enters `EntryState == 1 (Settling)` once on-screen (`EntrySettleTimer > 0`, fires **0** bullets while settling), and transitions to `EntryState == 2 (Combat)` after `0.55s` where it fires normally on the beat grid!
- Re-capture `docs/screenshots/drop_overdrive.png` (Abyssal) and `docs/screenshots/reactive_backdrop_standard.png` (Standard) and inspect both via `read_file` to confirm vivid, bold, beat-reactive 3D worlds + crisp ship/bullet contrast!
- Update `CHANGELOG.md` and `README.md` (strictly game-focused — zero AI/agent/swarm/task-ID copy), then tag `@qa`.

---

## 3. Acceptance Criteria (`@qa`)
1. Release build: **0 warnings, 0 errors**.
2. `./run.cmd --selftest`: **PASS** including `Test3DBackdropAndReadability()` & `TestGalagaEnemyEntry()` (`beat: offgrid=0`, `on-beat fire` pass).
3. `./run.cmd --smoke 5` (Standard) & `--smoke 4 --attune synergy --theme abyssal`: **RENDERSTATE -> PASS**, `alphaMin=255`, `rerenderDiff=0.000 %`, high FPS.
4. Visual inspection of `docs/screenshots/drop_overdrive.png` & `docs/screenshots/reactive_backdrop_standard.png`: bold, vibrant, map-anchored 3D worlds (not dim/weak, zero 2D patterns), clear ship/bullet readability, zero HUD overlap.
5. `CHANGELOG.md` & `README.md` strictly game-focused; after `@qa` PASS, `@programmer` flips `TASK-015` `Done` in `BACKLOG.md`, commits, and pushes to `origin/main`.

---

## 4. Engineering Handoff (`@programmer`)
Implemented by Claude Code (the plan above was written before the owner's credits ran out; the build differs where noted).

- **Backdrop (`Shaders.BackgroundFrag`, `Renderer.cs`, `BackdropGenome.cs`):** instead of offsetting the old five scenes by `uCam`, every pixel casts a ray from a perspective eye through the arena plane: `onFloor = (uCam + ndc * uView)`, eye height = `2 * uView.y`, eye offset by the world `Oblique` (0.42) so z = 0 lands exactly on the 2D world projection. Six new worlds live below the floor (`mapPrism`, `mapSea`, `mapCubes`, `mapGyroid`, `mapCanyon`, `mapRings`) plus five atmospheres (`atmosphere()`), all scene units = game units / 100. No aim tilt is applied to the backdrop (it never swims with the cursor).
- **Beat motion:** `uBeatPos` (AudioState.BeatPosition mod 256) gives `SNAP` (eased quarter-turn step per beat) and `HOP` (snap-up / fall per beat, falls back to the kick pulse when the tempo is not locked via `uBeatLock`). `uWaves[6]` are world-anchored shockwaves: the renderer adds one per beat at the camera (downbeat 0.85, beat 0.4, scaled by Music FX and energy), one per drop onset (1.6), and `WorldRenderer.DrawShockwaves` forwards heavy / large game shockwaves once each (`Shockwave.BackdropPulsed`). `uAnticipation` sinks and desaturates the world before a drop.
- **Readability:** the 0.45 / 0.39 highlight caps were replaced by a soft shoulder `c * 1.15 / (1 + 0.9c)`; Abyssal keeps a ridge/pulse gate plus combat lights.
- **Galaga entry (`World.Enemies.cs`, `Entities.cs`, `World.cs`, `World.Collisions.cs`, `WorldRenderer.cs`):** `Enemy.Entry` (0 dive, 1 lock-on, 2 combat), `EntryTimer`, `EntrySlot` (camera-relative so it lands on screen while you fly), `EntryCurl`, `OffscreenTime`. Director spawns (`spawning == true`, non-boss) call `BeginEntry`; capture/child spawns skip it. Dive speed `min(1600, 280 + 3.4 * dist)` with a fading perpendicular curl; settle `EntrySettleSeconds = 0.55`, then `FireBeats = min(FireBeats, 0.5)`. Combat: a screen-keeping steer in `UpdateEnemyBrain`, fire gated by `OnScreen(pos, -60)`, re-dive after 2.2 s off screen. Contact damage skipped while diving. `World.ViewAspect` is set each frame from the renderer.
- **Tools:** `--capture arrival`.

## 5. QA Findings & Verdict (`@qa`)
- Release build: 0 warnings, 0 errors.
- `--selftest`: PASS (73,977 assertions). `galaga: settle=0.82s dive=1411u/s hold=0.55s firstShot=0.55s after lock onBeat=True shotsWhileArriving=0 returns=True`; `backdrops: 1000 generated, 1000 distinct, 6/6 worlds, 5/5 atmospheres`.
- Captures: 12-seed sheet (since replaced: the grid worlds were removed in TASK-016), 8 s reels of all six worlds, combat / arrival / speed reels. Cube frames 0.12 s apart across a beat show the quarter-turn tumble and swell.
- GPU (`NULLWAVE_BENCH=1`, 1600×900): 1.6–2.7 ms/frame for five worlds, 4.5–5.8 ms for Cube Swarm.
- Abyssal theme checked on one combat capture (darker, lit around the fight).
- Not yet verified: owner playtest with real music.

## 5. QA Findings & Verdict (`@qa`)
*(To be filled by `@qa`.)*
