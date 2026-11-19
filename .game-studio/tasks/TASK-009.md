# TASK-009: Abyssal / Void Noir Dark Theme + Dynamic Combat Illumination (`uLights[]`)

- **Status:** `done`
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer`
- **Reviewer (QA):** `@qa`

---

## 1. Goal & Player-Facing Benefit

1. **Optional `ABYSSAL / VOID NOIR` Visual Theme (`GameSettings.cs`, `SettingsMenu.cs`, `Program.cs`, `Palette.cs`, `Shaders.cs`, `PostFx.cs`, `Screens.cs`):**
   - Adds a selectable `VISUAL THEME` (`STANDARD` / `ABYSSAL`) in **SETTINGS** (persisted in `save.json`, defaulting to `STANDARD` so the existing look stays 100% unchanged) plus a `--theme <standard|abyssal>` CLI override for captures, smoke tests, and trailers.
   - In **`ABYSSAL`** mode, NULLWAVE transforms into an OLED-grade dark-synth chiaroscuro arena:
     - The generative backdrop ambient floor drops to near-black `#010104` obsidian (`~0.015`), keeping the void pitch-dark between beats while **`uKick` / `uSnare` / `uDrop` transients** ignite razor-thin bioluminescent contour ridges.
     - 3D enemy hulls and monoliths shift to matte obsidian (`#05070E`) with heightened Fresnel/neon specular rims and guaranteed threat-silhouette readability (enemy outline halos, mortar strike rings, Choir phase-break shockwaves, and monolith boundaries stay crisp).
     - Composite grading deepens vignette, raises bloom threshold (`0.72` vs `0.62`) + contrast, and shifts HUD panels to low-glare smoked glass with crisp hairlines so bullets, comet tails, and 4-layer HDR lasers burn like glowing filaments against pitch darkness.
2. **Dynamic Combat Illumination in the Void (`uLights[12]` in `Renderer.cs`, `WorldRenderer.cs`, `Shaders.cs`):**
   - Collects up to **12 live combat light sources** per frame with **zero heap allocations** (player interceptor + dash/Phase Sync flare, Choir boss core, active laser midpoints, and expanding shockwaves) and uploads them to `BackgroundFrag` (`uLights[12]`, `uLightCols[12]`, `uLightCount`).
   - In `ABYSSAL` mode, these local light pools illuminate the dark generative shader geometry right around the ship, laser beams, boss, and explosions — turning the dark void into a living, interactive echolocation field!

---

## 2. Relevant Code & Ownership Constraints

### Strict File Ownership
- **`@designer` owns:** `.game-studio/BACKLOG.md`, `.game-studio/tasks/TASK-009.md` (Design & Spec), and—after `@programmer` finishes—updating `CHANGELOG.md` and `README.md` (strictly game-focused).
- **`@programmer` owns:** `src/Nullwave/**` (`Core/GameSettings.cs`, `UI/SettingsMenu.cs`, `UI/Screens.cs`, `Program.cs`, `Core/Game.cs`, `Render/Palette.cs`, `Render/Renderer.cs`, `Render/PostFx.cs`, `Render/Shaders.cs`, `Render/WorldRenderer.cs`, `Tools/SelfTest.cs`), `docs/screenshots/abyssal_*.png`, `.game-studio/tasks/TASK-009.md` (§6 Engineering Handoff), and final git commit/push after `@qa` sign-off.
- **`@qa` owns:** `.game-studio/QA_REPORT.md` and `.game-studio/tasks/TASK-009.md` (§7 QA Verdict).

---

## 3. Implementation Plan (`@programmer`)

### A1. Theme Setting, CLI Flag & Persistence (`GameSettings.cs`, `SettingsMenu.cs`, `Program.cs`, `Game.cs`)
1. **In `src/Nullwave/Core/GameSettings.cs`:**
   - Add `public enum VisualTheme { Standard = 0, Abyssal = 1 }` (or `string`/`int` enum serialized cleanly in JSON; keep backward-compatible so legacy `save.json` defaults to `VisualTheme.Standard`).
   - Add `public VisualTheme Theme { get; set; } = VisualTheme.Standard;` under `// video` and clamp it in `Sanitize()` (`if (!Enum.IsDefined(Theme)) Theme = VisualTheme.Standard;`).
   - Add convenience helper `[JsonIgnore] public bool IsAbyssal => Theme == VisualTheme.Abyssal;` and static parser `public static bool TryParseTheme(string? raw, out VisualTheme theme)` accepting `"standard"` / `"default"` / `"0"` and `"abyssal"` / `"dark"` / `"noir"` / `"void"` / `"1"`.
2. **In `src/Nullwave/UI/SettingsMenu.cs`:**
   - Add a `VIDEO` toggle row `"VISUAL THEME"` (displaying `"ABYSSAL"` when `IsAbyssal` else `"STANDARD"`, hint: `"STANDARD NEON OR ABYSSAL OLED-BLACK VOID WITH DYNAMIC COMBAT LIGHTING."`).
   - **Layout Guard (verified from `SettingsMenu.cs`):** Adding a 24th row with current `Top = 114, RowH = 25, HeaderH = 30` would push bottom to `y = 820`, colliding with the hint line at `y = 818`. Tighten constants to `Top = 104, RowH = 24, HeaderH = 28` (and footer gap `14`) so all 24 rows finish around `y = 778` with generous clearance above `y = 818`.
3. **In `src/Nullwave/Program.cs` & `Core/Game.cs`:**
   - Parse `--theme <standard|abyssal>` in `Program.Main` and store in `public static VisualTheme? ThemeOverride;` on `Game`.
   - Apply `ThemeOverride` transiently on `GameSettings.Current.Theme` in `Game` constructor (after loading settings / creating capture settings) without mutating the user's `save.json`.

### A2. Zero-Allocation Dynamic Combat Light Pool (`Renderer.cs` & `WorldRenderer.cs`)
1. **In `src/Nullwave/Render/Renderer.cs`:**
   - Define `public const int MaxBackdropLights = 12;` and preallocated arrays `_lightData = new Vector4[MaxBackdropLights]` (`x, y, radius, intensity` in aspect-corrected backdrop UV space) and `_lightCols = new Vector3[MaxBackdropLights]`, plus `private int _lightCount;`.
   - Add pure static projection helper (testable headlessly in `SelfTest.cs`):
     ```csharp
     public static Vector4 ProjectBackdropLight(
         Vector2 worldPos, float worldRadius, float intensity,
         Vector2 camera, Vector2 view)
     ```
     - Computes `lx = (worldPos.X - camera.X) / (2f * MathF.Max(1f, view.Y))`, `ly = -(worldPos.Y - camera.Y) / (2f * MathF.Max(1f, view.Y))`, `lr = MathF.Max(0.02f, worldRadius / (2f * MathF.Max(1f, view.Y)))`, `lw = Math.Clamp(intensity, 0f, 3f)` (with finite checks).
   - Add `public void AddBackdropLight(Vector2 worldPos, float worldRadius, Vector3 color, float intensity)` (no-op if `_lightCount >= MaxBackdropLights` or `intensity <= 0.01f`; resets `_lightCount = 0` inside `BeginWorld(...)`).
   - Upload `uDarkTheme` (`settings.IsAbyssal ? 1f : 0f`), `uLightCount` (`_lightCount`), `uLights[0..count-1]`, and `uLightCols[0..count-1]` to `_background` in `Renderer.Render(...)`.
2. **In `src/Nullwave/Render/WorldRenderer.cs`:**
   - Add `PopulateCombatLights(Renderer r, GameWorld w, Palette p)` called inside `WorldRenderer.Draw(...)` (zero allocations):
     1. **Player ship light:** centered at `w.Player.Pos`, radius `290f + w.Player.PhaseSyncFlash * 150f + (w.Player.IsDashing ? 90f : 0f)`, color `p.Player` (shifting bright cyan/gold on Phase Sync), intensity `0.95f + w.Player.PhaseSyncFlash * 0.65f`.
     2. **Choir Boss core light (if alive):** centered at `boss.Pos`, radius `440f + boss.PhaseBreakTimer * 260f`, color `M.Hsv(boss.Hue, 0.8f, 1f)`, intensity `1.25f + w.BeatPulse * 0.4f`.
     3. **Active Shockwaves (up to 4 newest):** centered at `s.Pos`, radius `MathF.Max(140f, s.Radius * 1.15f)`, color `s.Color`, intensity `0.9f * (s.Life / s.MaxLife)`.
     4. **Active Firing Lasers (up to 4):** sampled at the point on the laser nearest `w.Player.Pos` (clamped along `[0, l.Length]`), radius `240f + l.Width * 3f`, color `M.Hsv(l.Hue, 0.75f, 1f)`, intensity `0.85f * l.Alpha`.
     5. **Remaining slots:** nearest active enemies/elites or firing muzzle flashes so combat clusters glow organically in the dark void.

### A3. `uDarkTheme` Shader Pipeline (`Shaders.cs`, `PostFx.cs`, `Renderer.cs`, `WorldRenderer.cs`, `Screens.cs`)
1. **`Shaders.BackgroundFrag` (`Shaders.cs`):**
   - Add `uniform float uDarkTheme;`, `uniform int uLightCount;`, `uniform vec4 uLights[12];`, `uniform vec3 uLightCols[12];`.
   - Keep `uDarkTheme <= 0.001` **100% identical** to existing `STANDARD` output!
   - When `uDarkTheme > 0.001`:
     - Compute `rawCol` from `renderGenomeA/B`.
     - Evaluate local combat illumination `localLight` from `uLights[0..uLightCount-1]` using `screenPos = (vUv - 0.5) * vec2(aspect, 1.0)` (exact match with `ProjectBackdropLight`).
     - Compute transient gate `float pulseGate = clamp(0.10 + uKick * 0.42 + uSnare * 0.36 + uDrop * 0.85, 0.0, 1.35);`.
     - Crush ambient backdrop fog to near-OLED obsidian (`vec3 voidBase = vec3(0.004, 0.005, 0.012) + ambient * (0.014 + fog * 0.016);`).
     - Reveal crisp high-contrast pattern contours only via audio transients (`pow(clamp(rawCol, 0.0, 2.0), vec3(1.55)) * pulseGate * 0.62`) **plus** dynamic combat illumination (`rawCol * localLight * 1.15 + localLight * 0.11`)!
     - Mix cleanly between `standardCol` and `abyssalCol` via `uDarkTheme`.
2. **`Shaders.MeshFrag` & `WorldRenderer.cs`:**
   - Add `uniform float uDarkTheme;` to `MeshFrag` (uploaded by `Renderer.Render`).
   - When `uDarkTheme > 0.5`: darken diffuse/hemi hull shading (`hemi *= 0.32`, `diff *= 0.55`) toward matte obsidian while boosting Fresnel specular rim (`fres * uRim * 2.15`) and emissive `vGlow * uGlowScale * 1.25` so enemy ships, monolith edges, and Choir crowns read as sleek dark-obsidian silhouettes with razor-sharp neon outlines.
   - In `WorldRenderer.DrawArena`: in Abyssal mode, deepen the arena floor plate to obsidian (`rgba(0.002, 0.003, 0.008, 0.52)`) and brighten the beat-synced floor sonar ripple (`0.11f * (1f - ripple)`) so the arena feels like echolocation on glass.
   - Preserve guaranteed readability on threat rings, mortar telegraphs, enemy halos, monolith outlines, and Choir phase-break rings.
3. **`PostFx.cs` & `Shaders.CompositeFrag`:**
   - Pass `bool darkTheme` (or `float darkTheme` inside `PostMood`) to `PostFx.Process`:
     - In Abyssal mode, tighten bright-pass threshold (`uThreshold = 0.72f`, `uKnee = 0.32f` vs Standard `0.62f / 0.42f`) so dark void pixels never pick up muddy bloom haze while bullets and 4-layer lasers flare like bright filaments.
     - In `CompositeFrag`, add `uniform float uDarkTheme;`: subtly deepen vignette, bump micro-contrast (`* 1.07`), and keep true OLED blacks crisp (`alpha = 1.0`).
4. **Smoked-Glass HUD Chrome (`Screens.cs`):**
   - In `Screens.cs` (`DrawCard`, `DrawTitle`, `DrawArchive`, `DrawPause`, `DrawGameOver`, `DrawHealthBar`, `DrawMinimap`), when `GameSettings.Current.IsAbyssal` is true, use deeper smoked-obsidian card fills (`rgba(0.002, 0.003, 0.007, 0.94)`) and crisp, restrained hairline borders so the UI matches the Abyssal void cohesively without dimming text legibility.

### A4. Deterministic Self-Tests & Verification Captures (`SelfTest.cs` & `docs/screenshots/`)
1. **In `src/Nullwave/Tools/SelfTest.cs`:**
   - Add `TestAbyssalThemeAndLighting()` called from `SelfTest.Run()`, verifying:
     1. `GameSettings.Theme` defaults to `VisualTheme.Standard`, `TryParseTheme` handles `"abyssal"`, `"noir"`, `"standard"`, invalid strings, and `MetaProgress` save/load persists `VisualTheme.Abyssal` cleanly while legacy saves without `"Theme"` default to `Standard`.
     2. `Renderer.ProjectBackdropLight` maps camera center to `(0, 0)`, inverts world `+Y` to negative screen `Y`, scales radius proportionally to `view.Y`, and clamps non-finite/negative values safely.
     3. `Shaders.BackgroundFrag`, `Shaders.MeshFrag`, and `Shaders.CompositeFrag` all declare `uniform float uDarkTheme;`, and `BackgroundFrag` declares `uLights[12]`, `uLightCols[12]`, `uLightCount`.
2. **Visual Captures (`docs/screenshots/`):**
   - Capture and visually inspect:
     - `./run.cmd --capture combat --theme abyssal --out docs/screenshots/abyssal_combat.png`
     - `./run.cmd --capture boss --theme abyssal --out docs/screenshots/abyssal_boss.png`
     - `./run.cmd --capture songlasers --theme abyssal --out docs/screenshots/abyssal_songlasers.png`
     - `./run.cmd --capture settings --theme abyssal --out screenshots/settings_abyssal.png` (confirming 24 rows fit cleanly above `y = 818` with zero overlap!)
   - Also run standard `--smoke 5` and `--smoke 5 --theme abyssal` to verify both themes maintain high FPS, `alphaMin=255`, and `rerenderDiff=0.000%`.

---

## 4. Acceptance Criteria (`@qa`)

1. **Clean Release Build:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeds with **0 warnings and 0 errors**.
2. **Self-Test Suite Pass:** `./run.cmd --selftest` passes with `RESULT: PASS` including `TestAbyssalThemeAndLighting()` and zero regressions.
3. **Dual-Theme Smoke & GL Hygiene Pass:**
   - `./run.cmd --smoke 5` -> `RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`.
   - `./run.cmd --smoke 5 --theme abyssal` -> `RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS` with smooth FPS and zero per-frame light-array allocations.
4. **Visual & Contrast Readability Verification:**
   - Visually inspect `docs/screenshots/abyssal_combat.png`, `docs/screenshots/abyssal_boss.png`, `docs/screenshots/abyssal_songlasers.png`, and `screenshots/settings_abyssal.png` via `read_file`: verify true OLED-dark chiaroscuro, visible local light pools around ship/boss/lasers, crisp threat silhouettes/telegraphs, and clean 24-row `SettingsMenu` spacing with zero text collision.
   - Verify `STANDARD` theme captures remain unchanged.
5. **Documentation & Repo Sync (after `@programmer` finishes):**
   - `@designer` updates `CHANGELOG.md` and `README.md` (strictly game-focused; zero AI/agent/swarm/task-ID chatter), `@qa` verifies everything, and `@programmer` commits & pushes cleanly to `origin/main` after `@qa` sign-off.

---

## 5. Designer Notes (`@designer`)
- `uDarkTheme` acts as a clean single switch across `BackgroundFrag`, `MeshFrag`, and `CompositeFrag`, guaranteeing `STANDARD` mode stays pixel-faithful while `ABYSSAL` delivers dramatic obsidian chiaroscuro.
- Putting the player ship, Choir boss core, active shockwaves, and nearest laser points into `uLights[12]` means the pitch-black generative backdrop literally illuminates around the fight—rewarding movement, Phase Sync dashes, and boss clashes visually at zero GC cost.

---

## 6. Engineering Handoff (`@programmer`)
- **Files Modified (`@programmer` scope):**
  - `src/Nullwave/Core/GameSettings.cs`: Added `VisualTheme { Standard = 0, Abyssal = 1 }`, `GameSettings.Theme` (default `Standard`, persisted in `save.json`, clamped in `Sanitize()`), `IsAbyssal`, and `TryParseTheme(...)` (`standard`/`default`/`0` and `abyssal`/`dark`/`noir`/`void`/`1`).
  - `src/Nullwave/UI/SettingsMenu.cs`: Added `"VISUAL THEME"` toggle under `VIDEO` displaying `STANDARD` / `ABYSSAL` in a `136px` pill; tightened layout to `Top = 104, RowH = 24, HeaderH = 28` (footer gap `14`) so all 24 rows clear `y = 818` cleanly; added smoked-obsidian panel styling in Abyssal mode.
  - `src/Nullwave/Program.cs` & `src/Nullwave/Core/Game.cs`: Added `--theme <standard|abyssal>` CLI flag -> transient `Game.ThemeOverride`, cloned onto `GameSettings.Current` so CLI overrides never mutate `save.json`.
  - `src/Nullwave/Render/Renderer.cs`: Added `MaxBackdropLights = 12`, preallocated static uniform name arrays + `_lightData`/`_lightCols` (zero per-frame allocations), pure static `ProjectBackdropLight(...)`, `AddBackdropLight(...)`, reset in `BeginWorld(...)`; uploaded `uDarkTheme`, `uLightCount`, `uLights[0..count-1]`, `uLightCols[0..count-1]` to `_background`, `uDarkTheme` to `_meshShader`, and `DarkTheme` into `PostMood`.
  - `src/Nullwave/Render/WorldRenderer.cs`: Added zero-alloc `PopulateCombatLights(...)` (player ship + Phase Sync/dash flare, Choir boss core, up to 4 newest shockwaves, up to 4 lasers sampled nearest the player, remaining slots for in-view elites/firing/nearby enemies); deepened Abyssal arena floor plate (`alpha = 0.52`) and brightened beat sonar ripple (`0.11f * (1 - ripple)`).
  - `src/Nullwave/Render/Shaders.cs` & `src/Nullwave/Render/PostFx.cs`: Added `uDarkTheme` across `BackgroundFrag`, `MeshFrag`, and `CompositeFrag` (`uDarkTheme == 0` stays 100% identical to Standard); in Abyssal mode, crushed backdrop ambient floor to near-OLED obsidian (`~0.015` on `#010104`), gated ridges by `uKick`/`uSnare`/`uDrop`, and revealed underlying generative geometry locally through `uLights[12]`; shifted `MeshFrag` hulls to matte obsidian with boosted Fresnel rims (`2.15x`) + emissive (`1.25x`); tightened `PostFx` bright-pass (`0.72 / 0.32`) and `CompositeFrag` micro-contrast/vignette (`alpha = 1.0`).
  - `src/Nullwave/UI/Screens.cs`: Applied smoked-obsidian glass fills and crisp restrained hairline borders to Title, Archive, Seed Decoder, Pause, Game Over, Attunement badges, Health Bar, and Minimap in Abyssal mode.
  - `src/Nullwave/Tools/SelfTest.cs`: Added `TestAbyssalThemeAndLighting()` covering theme defaults, `TryParseTheme`, `Sanitize()`, save/load + legacy JSON compatibility, `ProjectBackdropLight` math/clamping, and `uDarkTheme`/`uLights[12]` shader contracts.
  - **Captures:** `docs/screenshots/abyssal_combat.png`, `docs/screenshots/abyssal_boss.png`, `docs/screenshots/abyssal_songlasers.png`, and `screenshots/settings_abyssal.png` (ignored temp capture for settings layout review).
- **Verification Results:**
  - `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` -> **0 Warning(s), 0 Error(s)**
  - `./run.cmd --selftest` -> **`RESULT: PASS (76340 assertions, 2.7s)`** (`abyssal theme: maxLights=12 offset=(0.50,-0.25) r=0.25 shaders=3/3`)
  - `./run.cmd --smoke 5` (Standard) -> **`SMOKE | 5.0s fps=115`**, **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**
  - `./run.cmd --smoke 5 --theme abyssal` -> **`SMOKE | 5.0s fps=113`**, **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**
  - Visually inspected all 4 captures via `read_file`: pitch OLED-black void with clear local `uLights[12]` illumination pools around ship/boss/lasers, crisp matte-obsidian monolith rims, blazing 4-layer HDR lasers, and clean 24-row `SettingsMenu` clearance above `y = 818`.

---

## 7. QA Findings & Verdict (`@qa`)
- **Verdict:** **PASS (`APPROVED — 0 DEFECTS`)**
- **Build & Automated Verification:**
  - `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` -> **0 Warning(s), 0 Error(s)** (`0.55s`).
  - `./run.cmd --selftest` -> **`RESULT: PASS (76425 assertions, 2.4s)`** (`abyssal theme: maxLights=12 offset=(0.50,-0.25) r=0.25 shaders=3/3`).
  - `./run.cmd --smoke 5` (Standard) -> **`5.0s fps=114`**, **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
  - `./run.cmd --smoke 5 --theme abyssal` -> **`5.0s fps=114`**, **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`** (0 fps penalty vs Standard).
- **Code, Allocation & Invariance Audit:**
  - Confirmed zero per-frame heap allocations in `Renderer.AddBackdropLight`, `WorldRenderer.PopulateCombatLights`, and `uLights[12]` / `uLightCols[12]` uniform uploads.
  - Confirmed `uDarkTheme == 0f` keeps `STANDARD` rendering identical across `BackgroundFrag`, `MeshFrag`, `CompositeFrag`, and `PostFx`.
  - Confirmed `--theme` CLI override clones `GameSettings.Current` transiently without mutating `save.json`.
- **Visual & Docs Inspection:**
  - Visually verified `docs/screenshots/abyssal_combat.png`, `docs/screenshots/abyssal_boss.png`, `docs/screenshots/abyssal_songlasers.png`, and `screenshots/settings_abyssal.png` via `read_file`: OLED-black void contrast, clear local `uLights[12]` illumination pools, matte-obsidian monolith rims, blazing 4-layer HDR lasers, smoked-glass HUD panels, and clean 24-row `SettingsMenu` clearance above `y = 818`.
  - Verified `README.md` and `CHANGELOG.md`: all image links resolve (`Test-Path == True`) with strictly game-focused copy and zero non-game references.
  - Full report logged in `.game-studio/QA_REPORT.md` (§14).
