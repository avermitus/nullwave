# TASK-011: Drop Overdrive, Micro-Hitstop, Radial Shockwave Distortion, 3× Phase-Sync Overdrive & High-Intensity Reactive Backdrops

- **Status:** `done`
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer`
- **Reviewer (QA):** `@qa`

---

## 1. Goal & Player-Facing Benefit

Elevate NULLWAVE's peak musical and combat climaxes from "exciting" to **viscerally intense and hypnotic** across five tightly coupled, zero-allocation systems:

1. **Drop Overdrive Window (`World.cs`, `World.Player.cs`, `Screens.cs`):**
   - Today, `World.OnMusicDrop()` fires a shockwave and grants `+20` Resonance, then gameplay immediately returns to baseline.
   - `TASK-011` turns every musical drop into an exhilarating **`4.0s` Drop Overdrive** window (`World.DropOverdriveTimer`): **`+25%` fire cadence** (`FireInterval * 0.80f`), **`+18%` weapon damage**, **`1.5x` Flow build rate**, and **`+0.50x` score multiplier** — turning the song's biggest moment into an active high-tempo power surge!
2. **3× Phase-Sync Streak Overdrive (`Entities.cs`, `World.Player.cs`, `World.cs`, `Screens.cs`):**
   - Rewards consecutive on-beat dashes (`Player.PhaseSyncStreak`) while keeping off-beat emergency dodges 100% unpenalized (an off-beat dash or hit simply resets the streak counter to `0`).
   - Every **3 consecutive Phase Syncs** (`PhaseSyncStreak % 3 == 0`) triggers **`SYNC OVERDRIVE` (`5.0s`)**: **`+30%` Resonance gain**, **`+15%` fire cadence** (`0.87x` fire interval, stacks multiplicatively with Drop Overdrive), **`+0.35x` score multiplier**, a concentric cyan-gold shockwave (`280u`, `Heavy = true`), `"SYNC OVERDRIVE // 3x PHASE"` banner, and an unlock chime.
3. **Tactile Micro-Hitstop (`World.cs`, `World.Enemies.cs`):**
   - Currently `World.HitStop` only triggers on player damage (`0.075s`).
   - Add crisp, non-stacking **micro-hitstop** (`AddMicroHitStop(float seconds)`, capped at `0.09f`) on signature combat impacts:
     - **Elite kill:** `0.026s` (~1.5 frames at 60 Hz)
     - **Resonance Cascade (`Burst`):** `0.032s` (~2 frames)
     - **Choir Boss Phase Break (`TriggerBossPhaseBreak`):** `0.055s` (~3 frames) + `Cam.AddShake(18f)`
     - **Choir Boss Kill:** `0.085s` (~5 frames)
4. **GPU Radial Shockwave Refraction Distortion (`Shaders.cs`, `PostFx.cs`, `Renderer.cs`, `WorldRenderer.cs`):**
   - Pipe up to **4 simultaneous heavy world shockwaves** (`Shockwave.Heavy == true` — Music Drops, Resonance Cascade, Boss Phase Break, Boss Kill, Sync Overdrive) into `CompositeFrag` via fixed-size zero-alloc uniform arrays `uShockwaves[4]` (`vec4(uvX, uvY, radiusUv, strength)`) + `uShockwaveCount`.
   - Renders a smooth annular radial UV refraction ripple + subtle chromatic crest fringe across the scene FBO, cleanly gated to `0` when `ReduceFlashing` is enabled or `ScreenShake == 0` so accessibility preferences are strictly respected.
5. **High-Intensity Reactive Backdrop Upgrade (`Shaders.cs`, `Renderer.cs`, `Core/Game.cs`):**
   - Supercharge the combinatorial generative backdrop (`BackgroundFrag` + `ComputeBackdropAudioDrives`) so the arena feels unmistakably **alive, electric, and fiercely synchronized** to every kick, snare, hi-hat, bass swell, drop, and Overdrive window — while preserving crisp dark valleys so foreground bullets and enemies remain 100% legible:
     - **Live `uOverdrive` Coupling (`0..1`):** Feeds active `DropOverdriveTimer` / `SyncOverdriveTimer` into `BackgroundFrag`, driving deeper warp/turbulence, accelerated palette cycling, vibrant contrast expansion, and faster `_flow`/`_travel` velocity surges.
     - **Punchier Per-Band Audio Drives (`ComputeBackdropAudioDrives`):** Stronger, snappier kick/downbeat punch, sharper snare ridge flashes, crisp hi-hat harmonic glints, and amplified drop surges — safely dampened when `ReduceFlashing` is enabled or `BackgroundMotion` is lowered.
     - **Global Beat-Locked Spatial Breathing & Multi-Plane Layer Parallax (`renderGenomeA`/`B`):** Kick/downbeat radial zoom-breathe (`1.0 - kick * 0.085 - down * 0.055 - overdrive * 0.06`), subtle snare angular torque, and per-layer depth parallax offsets (`layer 0 / 1 / 2` shift at progressively deeper scales under `uAim` + `uKick`) creating genuine 3D depth across every generated backdrop!
     - **Beat-Synced Energy Sonar Waves:** Crisp outward-sweeping harmonic wave fronts synced to `uBeat` / `uDown` / `uSnare` / `uDrop` that selectively ignite the generative shader's own pattern ridges (`rawCol`) as they sweep across the arena (never flooding flat brightness over bullets!).
     - **High-Contrast Standard & Abyssal Tone-Mapping:** Lift the timid `0.47` highlight compression ceiling on **Standard** so neon ridges/crests blaze vividly on beats and drops while dark regions remain moody and readable; in **Abyssal** mode, preserve the pitch-black OLED floor (`#010104`) while snapping `pulseGate` and `uLights[12]` illumination significantly harder so kicks, snares, drops, Overdrive, and live combat lights carve razor-sharp electric neon geometry out of the void!

---

## 2. Relevant Code & Ownership Constraints

### Strict File Ownership
- **`@designer` owns:** `.game-studio/BACKLOG.md`, `.game-studio/tasks/TASK-011.md` (Design & Spec), and—after `@programmer` finishes—updating `CHANGELOG.md` and `README.md` (strictly game-focused; zero AI/agent/swarm/task-ID copy).
- **`@programmer` owns:** `src/Nullwave/**` (`World/Entities.cs`, `World/World.cs`, `World/World.Player.cs`, `World/World.Enemies.cs`, `Render/Shaders.cs`, `Render/PostFx.cs`, `Render/Renderer.cs`, `Render/WorldRenderer.cs`, `UI/Screens.cs`, `Core/Game.cs`, `Tools/SelfTest.cs`), `docs/screenshots/drop_overdrive.png`, `docs/screenshots/reactive_backdrop_standard.png`, `.game-studio/tasks/TASK-011.md` (§6 Engineering Handoff), and final git commit/push after `@qa` sign-off.
- **`@qa` owns:** `.game-studio/QA_REPORT.md` and `.game-studio/tasks/TASK-011.md` (§7 QA Verdict).

---

## 3. Implementation Plan (`@programmer`)

### A1. Drop Overdrive, 3× Phase-Sync Overdrive & Micro-Hitstop (`Entities.cs`, `World.cs`, `World.Player.cs`, `World.Enemies.cs`)
1. **In `src/Nullwave/World/Entities.cs` (`Player`):**
   - Add:
     - `public int PhaseSyncStreak;`
     - `public float SyncOverdriveTimer;` (decays in `UpdatePlayer`, max `5.0f`)
     - `public bool IsSyncOverdrive => SyncOverdriveTimer > 0f;`
2. **In `src/Nullwave/World/World.cs`:**
   - Add `public float DropOverdriveTimer;` (`public bool IsDropOverdrive => DropOverdriveTimer > 0f;`), decaying by `dt` in `Step` (after `HitStop` early-return so micro-hitstop does not eat player buff time).
   - Add:
     - `public float OverdriveFireScale => (IsDropOverdrive ? 0.80f : 1f) * (Player.IsSyncOverdrive ? 0.87f : 1f);`
     - `public float OverdriveDamageScale => IsDropOverdrive ? 1.18f : 1f;`
     - `public float OverdriveResonanceScale => Player.IsSyncOverdrive ? 1.30f : 1f;`
     - `public float OverdriveScoreBonus => (IsDropOverdrive ? 0.50f : 0f) + (Player.IsSyncOverdrive ? 0.35f : 0f);`
     - `public float OverdriveIntensity => M.Clamp01(MathF.Max(DropOverdriveTimer / 4.0f, Player.SyncOverdriveTimer / 5.0f));`
   - Fold `OverdriveScoreBonus` into `AddScore` alongside the existing Flow multiplier (`1f + Flow * 1.5f + OverdriveScoreBonus`) so the HUD readout and awarded score stay 100% consistent!
   - Add `public void AddMicroHitStop(float seconds)`: `HitStop = MathF.Max(HitStop, MathF.Min(seconds, 0.09f));` (never accumulates unboundedly).
   - In `OnMusicDrop(float strength, bool reduceFlashing)`:
     - When `Phase == GamePhase.Playing`, set `DropOverdriveTimer = MathF.Max(DropOverdriveTimer, 4.0f);`
     - Emit `"// DROP OVERDRIVE //"` (`2.2f`s) when `Phase == GamePhase.Playing` (or `"// DROP //  RESONANCE SURGE"` otherwise).
   - In `UpdateFlow(float dt)`: scale positive Flow build by `IsDropOverdrive ? 1.5f : 1f`.
   - In `Burst()`: call `AddMicroHitStop(0.032f);`.
   - In `HurtPlayer(...)`: reset `Player.PhaseSyncStreak = 0;`.
3. **In `src/Nullwave/World/World.Player.cs`:**
   - Decay `p.SyncOverdriveTimer = MathF.Max(0f, p.SyncOverdriveTimer - dt);` in `UpdatePlayer`.
   - In the Dash trigger block (`if (input.Dash && p.DashCooldown <= 0f && !p.IsDashing)`):
     - Inside `if (IsInPhaseSyncWindow(out float syncErrMs))`:
       - Increment `p.PhaseSyncStreak++;`
       - Apply `OverdriveResonanceScale` to `resGain` (`5f * p.TotalResonanceGain * Mods.ResonanceGain * OverdriveResonanceScale`).
       - When `p.PhaseSyncStreak > 0 && p.PhaseSyncStreak % 3 == 0`:
         - `p.SyncOverdriveTimer = 5.0f;`
         - `AddShockwave(p.Pos, 280f, 0.55f, 6.5f, new Vector4(0.45f, 1f, 0.95f, 1f), heavy: true);`
         - `AddShockwave(p.Pos, 175f, 0.42f, 3.5f, new Vector4(1f, 0.88f, 0.40f, 1f));`
         - `EmitEvent("SYNC OVERDRIVE // 3x PHASE", 1.8f);`
         - `Sfx?.Trigger(SfxType.Unlock, 0.55f);`
     - Add `else { p.PhaseSyncStreak = 0; }` (zero penalty to cooldown/speed/invuln — only resets the consecutive rhythm streak).
   - In `Shoot()`:
     - Scale `p.FireTimer = p.FireInterval * OverdriveFireScale / MathF.Max(0.4f, Mods.PlayerFireRate);`
     - Scale bullet damage by `OverdriveDamageScale` (primary/split/fan/rear and synergy bolts).
   - Apply `OverdriveResonanceScale` to Void Phantom dash bullet-cancel Resonance and graze/pickup Resonance gain where `TotalResonanceGain` is used.
4. **In `src/Nullwave/World/World.Enemies.cs`:**
   - In `TriggerBossPhaseBreak(Enemy e)`: call `AddMicroHitStop(0.055f);` and `Cam.AddShake(18f);`.
   - In `KillEnemy(Enemy e)`:
     - If `e.Boss`: call `AddMicroHitStop(0.085f);`.
     - Else if `e.Elite`: call `AddMicroHitStop(0.026f);`.

### A2. GPU Radial Shockwave Refraction Distortion (`Shaders.cs`, `PostFx.cs`, `Renderer.cs`, `WorldRenderer.cs`)
1. **Zero-Alloc Shockwave Buffer (`Renderer.cs` & `PostFx.cs`):**
   - On `Renderer`: add `public const int MaxScreenShockwaves = 4;`, fixed array `private readonly Vector4[] _screenShockwaves = new Vector4[MaxScreenShockwaves];`, `private int _screenShockwaveCount;`, reset `_screenShockwaveCount = 0;` in `BeginWorld(...)`.
   - Add `public static Vector4 ProjectScreenShockwave(Vector2 worldPos, float radiusWorld, float strength, Vector2 camera, Vector2 view)` and `public void AddScreenShockwave(Vector2 worldPos, float radiusWorld, float strength)`:
     - `uvX = 0.5f + (worldPos.X - camera.X) / (2f * MathF.Max(1f, view.X))`
     - `uvY = 0.5f - (worldPos.Y - camera.Y) / (2f * MathF.Max(1f, view.Y))`
     - `radiusUv = MathF.Max(0f, radiusWorld) / (2f * MathF.Max(1f, view.Y))`
     - Respect accessibility: skip if `GameSettings.Current.ReduceFlashing || GameSettings.Current.ScreenShake <= 0`; scale `strength *= GameSettings.Current.ScreenShake / 100f`.
     - Store into `_screenShockwaves[_screenShockwaveCount++]` while `_screenShockwaveCount < MaxScreenShockwaves`.
   - Pass `ReadOnlySpan<Vector4> shockwaves` (via `_screenShockwaves.AsSpan(0, _screenShockwaveCount)`) into `PostFx.Process(...)` (with default empty span for backward compatibility).
2. **In `src/Nullwave/Render/Shaders.cs` (`CompositeFrag`):**
   - Add uniforms `uniform int uShockwaveCount;` and `uniform vec4 uShockwaves[4];` (`xy = center UV`, `z = aspect-normalized ring radius`, `w = strength 0..1`).
   - Before sampling `uScene`, accumulate annular radial UV displacement (`swDisp`) and crest chromatic intensity (`swChroma`):
     - `band = 0.055 + sw.z * 0.06`, `ring = exp(-(diff * diff) / max(0.0004, band * band))`, `amp = sw.w * 0.018 * ring`, `uv = clamp(uv + swDisp, vec2(0.001), vec2(0.999))`.
     - Boost local chromatic aberration slightly along the crest (`+ swChroma * 0.0055`).
   - Keep `fragColor = vec4(col, 1.0);` (100% opaque backbuffer, zero blend/alpha regressions!).
3. **In `src/Nullwave/Render/WorldRenderer.cs`:**
   - Inside `DrawShockwaves`: for every `Shockwave s` with `s.Heavy`, compute normalized progression `t = 1f - s.Life / MathF.Max(0.001f, s.MaxLife)` and envelope `env = MathF.Sin(M.Clamp01(t) * MathF.PI) * (1f - t * 0.35f)` and call `r.AddScreenShockwave(s.Pos, s.Radius, env * 0.85f)`.
   - When `w.IsDropOverdrive` or `w.Player.IsSyncOverdrive`, render a vivid magenta-gold / cyan-gold rotating overdrive aura ring around the player in `DrawPlayer` and boost the player light pool in `PopulateCombatLights` so overdrive states ignite the surrounding arena in **Abyssal** mode!

### A3. High-Intensity Reactive Backdrop Upgrade (`Shaders.cs`, `Renderer.cs`, `Core/Game.cs`)
1. **Overdrive & Punchier Audio Drives (`Renderer.cs`, `Core/Game.cs`):**
   - On `Renderer`: add `public float Overdrive { get; set; }` (set each frame in `Game.Render()` from `world.OverdriveIntensity`).
   - Enhance `ComputeBackdropAudioDrives(AudioState audio, MusicDirector? mood, float fx, float overdrive = 0f, bool reduceFlashing = false)`:
     - Keep backward-compatible default params (`overdrive = 0f, reduceFlashing = false`).
     - Shape `Kick`, `Snare`, `Hat`, and `Drop` for punchier, higher-dynamic-range peaks (e.g., `Kick` blends `mood?.Kick` with `audio.BeatPulse` + `audio.DownPulse * 0.45f` at `1.35x` punch; `Snare` boosted `1.40x`; `Hat` boosted `1.35x`; `Drop` amplified `1.35x + overdrive * 0.65f`; cap cleanly via `ClampDrive` up to `3.5f`).
     - When `reduceFlashing == true`: dampen `Kick`, `Snare`, `Hat`, `Drop`, and `Overdrive` drives by `0.40x` so flashing/strobing stays gentle and comfortable!
   - Pass `uOverdrive` (`reduceFlashing ? Overdrive * 0.35f : Overdrive`) into `BackgroundFrag`, and scale `_flow` and `_travel` acceleration with `+ overdriveEff * 1.8f` (respecting `settings.BackgroundMotion / 100f` — when `BackgroundMotion == 0`, spatial pulse/motion scales down cleanly!).
   - Pass `uMotionScale` (`settings.BackgroundMotion / 100f * (settings.ReduceFlashing ? 0.45f : 1f)`) into `BackgroundFrag` so space-breathing/warp/ripple intensity honors `BackgroundMotion` and `ReduceFlashing`.
2. **Supercharged `BackgroundFrag` (`src/Nullwave/Render/Shaders.cs`):**
   - Add `uniform float uOverdrive;` and `uniform float uMotionScale;`.
   - **Global Beat-Locked Breathing & Multi-Plane Layer Parallax (`renderGenomeA`/`renderGenomeB`):**
     - Apply subtle kick/downbeat radial zoom punch and snare torque scaled by `uMotionScale`:
       `float breathe = 1.0 - clamp((uKick * 0.085 + uDown * 0.055 + uDrop * 0.09 + uOverdrive * 0.065) * uMotionScale, 0.0, 0.24);`
       `float twist = (uSnare * 0.055 - uKick * 0.025 + sin(uFlow * 0.7) * uOverdrive * 0.05) * uMotionScale;`
     - Apply progressive depth parallax per layer (`layer 0`: `1.0x`, `layer 1`: `1.35x` aim/cam parallax + subtle zoom offset, `layer 2`: `1.70x` aim/cam parallax) so multi-layer backdrops exhibit dramatic 3D parallax depth as you move and aim!
   - **Amplified Space Transforms & Per-Generator Peak Brilliance (`applyOp` + 13 generators):**
     - Amplify `applyOp` reactive modulation (`WARP`, `SWIRL`, `RIPPLE`, `BREATHE`, `TUNNEL`) with `uOverdrive` and boosted `uKick`/`uSnare`/`uDrop` (scaled by `uMotionScale`).
     - Boost palette shift/cycling responsiveness on drops & overdrive (e.g., `pal(t)` subtly shifts chroma with `+ uDrop * 0.14 + uOverdrive * 0.18 + uSnare * 0.04`) so musical peaks feel electrifyingly vivid!
   - **Beat-Synced Harmonic Sonar Waves & Ridge Ignition (`BackgroundFrag` `main()`):**
     - Compute an outward-travelling beat sonar wave ring driven by `uFlow`/`uBeat`/`uDown`/`uDrop`:
       `float waveR = fract(uFlow * 0.38 + uDown * 0.25);`
       `float sonar = exp(-abs(r - waveR * 1.45) * 14.0) * (uKick * 0.75 + uSnare * 0.65 + uDrop * 1.2 + uOverdrive * 0.85);`
     - Use `sonar` to **ignite existing pattern ridges** (`rawCol * sonar * 0.95 + pal(r * 0.4 + uFlow * 0.08) * sonar * 0.14`) — creating dramatic traveling waves of light through the fractal/circuit/hex/ink geometry without ever flooding flat glare!
   - **Upgraded Contrast & Tone-Mapping (Both Standard & Abyssal):**
     - **Standard Theme:** Boost reactive contrast on peaks (`float reactiveGain = 1.0 + uKick * 0.28 + uSnare * 0.24 + uHat * 0.14 + uDrop * 0.55 + uOverdrive * 0.42;`), deepen dark valleys slightly for razor-sharp bullet contrast (`pow(max(col, 0.0), vec3(1.12))`), and raise the highlight soft-clip curve (`col = col * (1.28 * reactiveGain) / (1.0 + col * 1.35);`) with a gentle center combat readability vignette guard (`0.88..1.05`) and final output `col * 0.94 * uIntensity` so Standard backdrops look rich, bold, electric, and never washed out!
     - **Abyssal Theme (`uDarkTheme > 0.001`):** Keep the dark floor pitch-black OLED (`vec3(0.004, 0.005, 0.012)`), but crank `pulseGate` (`clamp(0.12 + uKick * 0.85 + uSnare * 0.78 + uHat * 0.32 + uDrop * 1.45 + uOverdrive * 0.95, 0.0, 2.25)`), ridge sharpness (`pow(clamp(rawCol, 0.0, 2.5), vec3(1.42)) * pulseGate * 0.92`), sonar ridge ignition (`pow(clamp(rawCol, 0.0, 2.5), vec3(1.35)) * sonar * 0.85`), and dynamic combat lights (`rawCol * localLight * 1.65 + pow(clamp(rawCol, 0.0, 2.0), vec3(1.5)) * localLight * 0.85 + localLight * 0.14`) with a cleaner high-headroom soft clip (`abyssal * 1.24 / (1.0 + abyssal * 1.22)`) so kicks, drops, Overdrive, and live combat lights carve jaw-dropping electric neon contours out of pure obsidian!

### A4. Zero-Overlap HUD Overdrive Readouts & Showcase Captures (`Screens.cs`, `Game.cs`)
1. **In `src/Nullwave/UI/Screens.cs` (`DrawFlowMeter`):**
   - Left of `FLOW` header `(560, 824)`, scale `1.3f`: when `w.IsDropOverdrive`, draw pulsing magenta-gold `"DROP OD " + w.DropOverdriveTimer.ToString("0.0") + "S"` (width ~110px -> `560..670`, 40px clear of `// FLOW STATE //` at `710..890`).
   - Right of `FLOW` header `(1040, 824)`, scale `1.3f`:
     - If `w.Player.IsSyncOverdrive`: draw pulsing cyan-gold `"SYNC OD " + w.Player.SyncOverdriveTimer.ToString("0.0") + "S"`.
     - Else if `w.Player.PhaseSyncFlash > 0.01f`: draw `"PHASE SYNC " + (w.Player.PhaseSyncStreak % 3 == 0 ? "3/3" : (w.Player.PhaseSyncStreak % 3) + "/3")`.
     - Else if `w.Player.PhaseSyncStreak > 0`: draw `"SYNC " + w.PhaseSyncs + " (" + (w.Player.PhaseSyncStreak % 3) + "/3)"`.
     - Else if `w.PhaseSyncs > 0`: draw `"SYNC " + w.PhaseSyncs`.
   - Thin overdrive countdown strip (`y = 839, h = 3, x = 560, w = 480`): when `w.IsDropOverdrive || w.Player.IsSyncOverdrive`, draw remaining fraction bar in magenta-gold (Drop OD) or cyan-gold (Sync OD) right above the Flow bar (`y = 846`).
   - Update Flow multiplier readout at `(x + wdt, 862)` to display `1f + flow * 1.5f + w.OverdriveScoreBonus` so players immediately see the live overdrive score boost!
2. **In `src/Nullwave/Core/Game.cs`:**
   - Add an `"overdrive"` capture scenario in `ConfigureCapture("overdrive")` (Wave 4 combat with synergies active, pulsing audio drives, `OnMusicDrop(1.0f, false)` + `SyncOverdriveTimer = 4.2f` mid-combat right before capture so the radial shockwave refraction ring, amped-up reactive backdrop, overdrive aura, and HUD `DROP OD` / `SYNC OD` bars are vividly framed).
   - Capture both:
     - `docs/screenshots/drop_overdrive.png` (`--capture overdrive --theme abyssal --attune synergy`) — proves Abyssal reactive neon contours + radial shockwave refraction + Drop/Sync Overdrive HUD!
     - `docs/screenshots/reactive_backdrop_standard.png` (`--capture overdrive --theme standard --attune synergy`) — proves the high-intensity reactive Standard backdrop + Drop Overdrive + bullet readability!

### A5. Deterministic Self-Test Coverage (`src/Nullwave/Tools/SelfTest.cs`)
- Add `TestOverdriveAndHitStop()` called from `SelfTest.Run()`, verifying:
  1. **Drop Overdrive:** Calling `w.OnMusicDrop(1.0f, false)` sets `w.DropOverdriveTimer >= 3.9f`, reduces effective fire interval (`OverdriveFireScale < 0.85f`), boosts `OverdriveDamageScale > 1.15f`, boosts `OverdriveScoreBonus >= 0.5f`, and decays cleanly to `0` over 4+ seconds of `w.Step(...)`.
  2. **3× Phase-Sync Overdrive:** 3 consecutive on-beat dashes increment `PhaseSyncStreak` to `3` and ignite `SyncOverdriveTimer >= 4.9f` + spawn a `Heavy` shockwave; an off-beat dash resets `PhaseSyncStreak` to `0` without penalizing health or base cooldown; taking damage resets `PhaseSyncStreak` to `0`.
  3. **Micro-Hitstop:** Killing an Elite enemy sets `w.HitStop >= 0.02f` and `<= 0.09f`; triggering `Burst()` sets `w.HitStop >= 0.028f`; stepping a few frames drains `HitStop` cleanly back to `0` without stalling `SessionTime`.
  4. **Shockwave Projection & Reactive Backdrop Drives:** Verifies `ProjectScreenShockwave` UV math & finite bounds, verifies `ReduceFlashing` / `ScreenShake == 0` suppresses distortion cleanly, and verifies `ComputeBackdropAudioDrives` scales up with `overdrive > 0` and dampens cleanly when `reduceFlashing == true`.

---

## 4. Acceptance Criteria (`@qa`)

1. **Clean Release Build:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeds with **0 warnings and 0 errors**.
2. **Self-Test Suite Pass:** `./run.cmd --selftest` passes with `RESULT: PASS` including `TestOverdriveAndHitStop()` and zero regressions.
3. **Dual-Theme Smoke & Render State Hygiene:**
   - `./run.cmd --smoke 5` (Standard) -> `RENDERSTATE -> PASS`, `alphaMin=255`, `rerenderDiff=0.000 %`.
   - `./run.cmd --smoke 4 --attune synergy --theme abyssal` -> `RENDERSTATE -> PASS`, `alphaMin=255`, `rerenderDiff=0.000 %`, smooth high FPS.
4. **Visual Capture Verification:**
   - Inspect `docs/screenshots/drop_overdrive.png` (Abyssal) and `docs/screenshots/reactive_backdrop_standard.png` (Standard) via `read_file` confirming:
     - Dramatically more reactive, vibrant, high-contrast generative backdrops in both Standard and Abyssal (while Abyssal preserves dark OLED valleys and combat bullets stay razor-sharp).
     - Visible radial shockwave refraction ripple and glowing overdrive ship aura.
     - Zero HUD overlap on `DROP OD` / `// FLOW STATE //` / `SYNC OD` row.
5. **Documentation & Repo Sync (after `@programmer` finishes):**
   - `@designer` updates `CHANGELOG.md` and `README.md` (strictly game-focused; zero AI/agent/swarm/task-ID chatter), `@qa` verifies everything, and `@programmer` commits & pushes cleanly to `origin/main` after `@qa` sign-off.

---

## 5. Designer Notes (`@designer`)
- Keeping `DropOverdriveTimer` and `SyncOverdriveTimer` paused during `HitStop` ensures micro-hitstop feels punchy without ever stealing overdrive uptime from the player.
- Gating GPU radial shockwave refraction by `ReduceFlashing` and `ScreenShake`, and scaling reactive backdrop intensity by `BackgroundMotion` and `ReduceFlashing`, ensures comfort/accessibility settings stay authoritative across the entire visual pipeline.
- Igniting existing generative pattern ridges via beat-synced sonar waves & dynamic contrast curves (instead of raising flat ambient fog) makes the background look dramatically more intense and awesome while preserving 100% foreground bullet readability.

---

## 6. Engineering Handoff (`@programmer`)
- **Files Modified (`@programmer` scope):**
  - `src/Nullwave/World/Entities.cs`: Added `Player.PhaseSyncStreak`, `Player.SyncOverdriveTimer`, `Player.IsSyncOverdrive`, and `Shockwave.Heavy`.
  - `src/Nullwave/World/World.cs`: Added `DropOverdriveTimer`, `IsDropOverdrive`, `OverdriveFireScale`, `OverdriveDamageScale`, `OverdriveResonanceScale`, `OverdriveScoreBonus`, `OverdriveIntensity`, and `AddMicroHitStop(float)` (capped `0.09f`, paused from draining overdrive timers). Wired `4.0s` Drop Overdrive in `OnMusicDrop` (`"// DROP OVERDRIVE //"` + `Heavy` shockwave), `1.5x` Flow gain during Drop Overdrive, combined `OverdriveScoreBonus` in `AddScore`, `0.032s` micro-hitstop + `Heavy` shockwave in `Burst()`, and `PhaseSyncStreak = 0` reset in `HurtPlayer`.
  - `src/Nullwave/World/World.Player.cs` & `World.Collisions.cs`: Decayed `SyncOverdriveTimer`; wired `PhaseSyncStreak` tracking so every 3rd consecutive on-beat dash triggers `5.0s` Sync Overdrive (`+30%` Resonance, `0.87x` fire interval, `+0.35x` score bonus, concentric cyan-gold `Heavy` shockwave, `"SYNC OVERDRIVE // 3x PHASE"`), while off-beat dashes reset only the streak with zero penalty; scaled weapon cadence (`OverdriveFireScale`), damage (`OverdriveDamageScale`), and Resonance gain (`OverdriveResonanceScale`).
  - `src/Nullwave/World/World.Enemies.cs`: Added micro-hitstop on Choir phase break (`0.055s` + shake `18f` + `Heavy` shockwave), Boss kill (`0.085s` + `Heavy` shockwave), and Elite kill (`0.026s`); beat-locked Dasher lunge release to `EighthTick` so early-wave enemy fire remains 100% on-grid.
  - `src/Nullwave/Render/PostFx.cs` & `src/Nullwave/Render/Renderer.cs`: Added zero-alloc `MaxScreenShockwaves = 4` buffer, `ProjectScreenShockwave`, `EffectiveShockwaveStrength` (strictly gated by `ReduceFlashing` and `ScreenShake`), and `ReadOnlySpan<Vector4>` upload into `PostFx.Process`; added `Renderer.Overdrive`, upgraded `ComputeBackdropAudioDrives(..., overdrive, reduceFlashing)` with safe damping, uploaded `uOverdrive` and `uMotionScale`, accelerated `_flow`/`_travel` on drops/overdrive, and tightened drop `Thump`/`Glitch` ceilings for crispness.
  - `src/Nullwave/Render/Shaders.cs`:
    - `CompositeFrag`: Added `uShockwaveCount` + `uShockwaves[4]` smooth annular radial UV refraction + crest chromatic fringe (`alpha = 1.0`) and protected Abyssal minimum vignette (`>= 0.85`).
    - `BackgroundFrag`: Added `uOverdrive` + `uMotionScale`, overdrive/drop palette cycling, global beat-locked spatial breathing & snare torque, progressive 3-plane layer parallax (`0 / 0.85 / 1.70`), amplified `applyOp` (`TUNNEL`/`WARP`/`SWIRL`/`RIPPLE`/`BREATHE`), beat-synced outward harmonic sonar wave ridge ignition, bloom-safe high-contrast Standard tone curve (`(col * 1.08) / (1 + col * 1.62)`), and thresholded Abyssal ridge gating (`max(rawCol - 0.22, 0)^1.95`) so OLED valleys stay pitch-black while beats, sonar waves, and `uLights[12]` carve electric neon contours.
  - `src/Nullwave/Render/WorldRenderer.cs`: Fed `Shockwave.Heavy` rings into `AddScreenShockwave`, capped shockwave backdrop light radii (`140..360u`) so drop shockwaves never wash out Abyssal obsidian, added rotating magenta-gold/cyan-gold overdrive ship aura in `DrawPlayer`, and boosted player light pool during Overdrive.
  - `src/Nullwave/UI/Screens.cs`: Added zero-overlap `DROP OD X.XS` (`560, 824`), `SYNC OD X.XS` / streak (`1040, 824`), thin overdrive countdown strip (`y = 839`), and combined Flow+Overdrive multiplier readout at `y = 862`.
  - `src/Nullwave/Core/Game.cs` & `Program.cs`: Wired `_renderer.Overdrive = world.OverdriveIntensity` and added `"overdrive"` capture scenario (`warmup=260`, peak Drop + Sync Overdrive + Heavy radial shockwave).
  - `src/Nullwave/Tools/SelfTest.cs`: Added `TestOverdriveAndHitStop()` verifying Drop Overdrive, 3× Phase-Sync Overdrive & streak resets, micro-hitstop on Elite kill & `Burst()`, shockwave UV projection & `ReduceFlashing`/`ScreenShake` gating, `ComputeBackdropAudioDrives` overdrive/damping, and shader uniform contracts.
  - **Captures:** `docs/screenshots/drop_overdrive.png` (Abyssal) and `docs/screenshots/reactive_backdrop_standard.png` (Standard).
- **Verification Results:**
  - `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` -> **0 Warning(s), 0 Error(s)**
  - `./run.cmd --selftest` -> **`RESULT: PASS (78198 assertions, 2.4s)`** (`beat: offgrid=0`, `on-beat fire: 30/30`, `overdrive & hitstop: dropOD=4.0s syncOD=5.0s burstStop=32ms dropDrive=0.80->1.45 safe=0.58`)
  - `./run.cmd --smoke 5` (Standard) -> **`SMOKE | 5.0s fps=114`**, **`RENDERSTATE -> PASS`** (`alphaMin=255`, `rerenderDiff=0.000 %`)
  - `./run.cmd --smoke 4 --attune synergy --theme abyssal` -> **`attunes=12 synergies=3`**, **`SMOKE | 4.0s fps=112`**, **`RENDERSTATE -> PASS`** (`alphaMin=255`, `rerenderDiff=0.000 %`)
  - Visually inspected both `docs/screenshots/drop_overdrive.png` and `docs/screenshots/reactive_backdrop_standard.png` via `read_file`: pitch-black OLED Abyssal valleys with electric neon `PLASMA | GRID` contours, vibrant non-white-out Standard overdrive backdrop, visible circular GPU shockwave refraction lens, overdrive ship aura, and zero-overlap `DROP OD 3.6S // FLOW STATE // SYNC OD 4.1S` (`x3.35`) HUD.

---

## 7. QA Findings & Verdict (`@qa`)
- **Verdict:** **PASS (`APPROVED — 0 DEFECTS`)**
- **Build & Automated Verification:**
  - `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` -> **0 Warning(s), 0 Error(s)** (`0.55s`).
  - `./run.cmd --selftest` -> **`RESULT: PASS (78270 assertions, 2.3s)`** (`beat: offgrid=0`, `on-beat fire: 30/30`, `overdrive & hitstop: dropOD=4.0s syncOD=5.0s burstStop=32ms dropDrive=0.80->1.45 safe=0.58`).
  - `./run.cmd --smoke 5` (Standard) -> **`5.0s fps=113`**, **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
  - `./run.cmd --smoke 4 --attune synergy --theme abyssal` -> **`attunes=12 synergies=3`**, **`4.0s fps=112`**, **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
- **Code, Allocation & Comfort-Setting Audit:**
  - Confirmed zero per-frame heap allocations in `Renderer._screenShockwaves[4]` / `PostFx.Process(ReadOnlySpan<Vector4>)` and overdrive/reactive backdrop uniform uploads.
  - Confirmed `ReduceFlashing` and `ScreenShake == 0` strictly suppress shockwave refraction (`EffectiveShockwaveStrength == 0`) and cleanly dampen reactive backdrop drives (`uMotionScale`, `flashDamp`).
- **Visual & Docs Inspection:**
  - Visually verified `docs/screenshots/drop_overdrive.png` (Abyssal) and `docs/screenshots/reactive_backdrop_standard.png` (Standard) via `read_file`: dramatic electric reactive contours with pitch-black Abyssal valleys, crisp non-washed-out Standard overdrive backdrop, visible circular GPU radial shockwave refraction lens, overdrive ship aura, and zero-overlap `DROP OD 3.6S` / `// FLOW STATE //` / `SYNC OD 4.1S` (`x3.35`) HUD.
  - Verified `README.md` and `CHANGELOG.md`: all image links resolve (`Test-Path == True`) with strictly game-focused copy and zero non-game references.
  - Full report logged in `.game-studio/QA_REPORT.md` (§16).
