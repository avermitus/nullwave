# TASK-002: Live Attunement Loadout Strip on HUD & Pause Overlay + Attunement Matrix Visual Sheet

- **Status:** `done`
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer`
- **Owner (Art):** `@artist`
- **Reviewer (QA):** `@qa`

---

## 1. Goal & Player-Facing Benefit

1. **Live Attunement Loadout Strip on HUD & Pause Overlay (`Attunements.cs`, `Screens.cs`, `Game.cs`, `GameSettings.cs`):**
   - During a run, players draft up to 12 distinct Attunement upgrade lines (`SPLIT`, `FAN`, `PIERCE`, `RAPID`, `HEAVY`, `LONG WAVE`, `HOMING`, `RICOCHET`, `REAR ARRAY`, `SIPHON`, `PHASE DRIVE`, `GRAZER`), each stacking up to `3` or `4` levels. Previously, once an attunement card was chosen, there was **no in-game HUD or Pause readout** showing which attunements or stack levels were currently active on the ship.
   - `TASK-002` adds a zero-allocation active-attunement query API and renders a crisp, non-intrusive **Live Attunement Loadout Strip** (short 3-letter signal tags + visual stack pips `1..4`) on both the **in-game HUD** (`Screens.DrawHud`) and the **`SIGNAL HELD` Pause Overlay** (`Screens.DrawPause`), along with a `ShowAttunements` interface setting so players have complete build visibility at a glance.
2. **Attunement Matrix Schematic Sheet (`docs/assets/attunement_matrix_sheet.png`, `.game-studio/ASSET_MANIFEST.md`):**
   - Expand NULLWAVE's studio visual library with a high-craft `1600x900` (or `1024x1024`) schematic/insignia plate showcasing all 12 Attunement upgrade lines, their 3-letter signal codes, vector icons, and stack pips in `Palette.cs` neon vector-mesh style.

---

## 2. Relevant Code, Assets & Ownership Constraints

### Strict File Ownership
- **`@designer` owns:** `.game-studio/BACKLOG.md` and `.game-studio/tasks/TASK-002.md` (Design & Task Specification sections).
- **`@programmer` owns:** `src/Nullwave/**` (`World/Attunements.cs`, `UI/Screens.cs`, `Core/Game.cs`, `Core/GameSettings.cs`, `UI/SettingsMenu.cs`, `Tools/SelfTest.cs`) and the **Engineering Handoff** section of `.game-studio/tasks/TASK-002.md`.
- **`@artist` owns:** `docs/assets/**` (`docs/assets/attunement_matrix_sheet.png`) and `.game-studio/ASSET_MANIFEST.md`.
- **`@qa` owns:** `.game-studio/QA_REPORT.md` and the **QA Findings & Verdict** section of `.game-studio/tasks/TASK-002.md`.

---

## 3. Implementation Plan

### Part A — Engineering Scope (`@programmer`)

#### A1. Zero-Allocation Active Attunement Query API (`src/Nullwave/World/Attunements.cs`)
1. Add a concise 3-letter `Tag` property to `AttunementDef` in `AttunementCatalog.All`:
   - `split` -> `"SPL"`, `fan` -> `"FAN"`, `pierce` -> `"PRC"`, `rapid` -> `"RPD"`, `heavy` -> `"HVY"`, `long` -> `"LNG"`, `homing` -> `"HOM"`, `ricochet` -> `"RIC"`, `rear` -> `"RER"`, `siphon` -> `"SPH"`, `phase` -> `"PHS"`, `grazer` -> `"GRZ"`.
2. Define a `readonly record struct ActiveAttunement(string Id, string Tag, string Name, int Level, int MaxStacks, int CatalogIndex)` (or equivalent value struct).
3. Add a zero-allocation query helper on `AttunementCatalog`, e.g.:
   - `public static int GetActive(Player player, Span<ActiveAttunement> destination)` which iterates `All`, writes entries with `level > 0` into `destination` (allocated via `stackalloc ActiveAttunement[12]` by callers), and returns the count of active distinct attunements (`0..12`).
   - Optionally expose a formatting or summary helper if useful for deterministic `SelfTest` assertions.

#### A2. HUD & Pause Loadout Strip Rendering (`src/Nullwave/UI/Screens.cs`)
1. **Coordinate & Overlap Audit (Verified from `Screens.cs`):**
   - **In `DrawHud`:**
     - Top-left uses `y = 38..200` (`WAVE`, `MOVEMENT`, `SCORE`, `CHAIN`, `SIGNAL <mods>`) and `y = 224` when `debug` is true.
     - Bottom-left ship cluster starts at `y = 770` (`INTEGRITY` header at `y = 770`, health bar at `y = 790..806`, `RESONANCE` header at `y = 836`, resonance bar at `y = 856..864`).
     - The band directly above `INTEGRITY` at `x = 56, y = 716..754` is completely unoccupied across all states (`FlowMeter` is centered at `x = 560..1040, y = 824..862`; `Toasts` are right-aligned at `x = 1540`).
     - **HUD Placement:** When `GameSettings.Current.ShowAttunements` is true and `player.AttunementCount > 0`, draw the compact **HUD Attunement Loadout Strip** above `INTEGRITY` starting at `x = 56` (e.g., `y = 736`, or wrapping cleanly into 2 rows at `y = 710` and `y = 736` if more than 6 distinct attunements are active). Each badge cell renders its 3-letter `Tag` (colored by `M.Hsv((p.BaseHue + entry.CatalogIndex * 0.055f) % 1f, 0.62f, 1f)`) and `MaxStacks` small pip rectangles (`filled` up to `Level`, `dim` for remaining stack capacity).
   - **In `DrawPause`:**
     - Pause card is centered at `x = 500, w = 600` (`x = 500..1100`).
     - Menu items `0..3` occupy `y = 388..562` (bottom selection frame at `y = 599`).
     - **Pause Placement:** Expand or use the lower section of the Pause card (`y = 608..700`, extending card height from `500` to `516` if helpful so margins stay generous):
       - Draw a subtle horizontal divider at `y = 608` (`x = 546..1054`).
       - Render `ATTUNEMENTS // <totalStacks>` header or centered loadout badge row(s) at `y = 618..664` (showing active attunement badges with `Tag` + stack pips, or a dim `"NO ATTUNEMENTS ACTIVE"` label when `player.AttunementCount == 0`).
       - Place the `AUDIO // ...` and `P / START // RESUME` footer line cleanly below it (e.g. `y = 682`).

#### A3. Settings Integration & Capture Scenarios (`GameSettings.cs`, `SettingsMenu.cs`, `Game.cs`)
1. **Settings (`GameSettings.cs` & `SettingsMenu.cs`):**
   - Add `public bool ShowAttunements { get; set; } = true;` under `// interface` in `GameSettings.cs`.
   - If adding a toggle row (`"ATTUNEMENT LOADOUT"` or `"SHOW ATTUNEMENTS"`) to `SettingsMenu.cs`:
     - **Important Layout Note:** `SettingsMenu` currently has 22 rows + 3 section headers (`Top = 122, RowH = 26, HeaderH = 32`), which ends at `y = 806` right above the hint line at `y = 818`. If adding a 23rd row to `SettingsMenu`, tighten `Top` (e.g. `112`), `RowH` (e.g. `25`), or `HeaderH` (e.g. `28`) so the final `"BACK"` row finishes well above `y = 818` with zero text collision!
2. **Capture Scenario Polish (`Game.cs`):**
   - In `Game.ConfigureCapture(string scenario)` for `scenario == "pause"` (around line 246), apply a representative multi-attunement build to `_world.Player` (e.g., `split` x2, `fan` x1, `pierce` x1, `rapid` x2, `homing` x1) so `--capture pause` deterministically showcases the new Pause Attunement Loadout Strip alongside `--capture combat`.

#### A4. Deterministic Self-Test Assertions (`src/Nullwave/Tools/SelfTest.cs`)
- Add `TestAttunementLoadout()` in `SelfTest.cs` and call it from `SelfTest.Run()`:
  1. Verify every `AttunementDef` in `AttunementCatalog.All` has a non-empty, unique 3-character `Tag`.
  2. Verify `AttunementCatalog.GetActive` returns `0` on a fresh `Player`, and after applying multiple stacks across several attunements (including `split`, `homing`, `grazer`), returns the exact active count, preserves catalog order, and reports exact `Level` and `MaxStacks` matching `Player.AttunementCount`.
  3. Verify `GameSettings.ShowAttunements` defaults to `true` and persists across save/load.

---

### Part B — Art Scope (`@artist`)

Create **1 new high-craft visual asset** (`docs/assets/attunement_matrix_sheet.png`) and append its full entry to `.game-studio/ASSET_MANIFEST.md`:

| # | Asset Filename | Purpose | Target Resolution | Visual Brief & Style |
|---|---|---|---|---|
| 4 | `docs/assets/attunement_matrix_sheet.png` | Attunement Signal Matrix Reference Sheet (`TASK-002`) | `1600x900` (16:9 PNG, opaque RGBA `A=255`) | A structured 4x3 technical schematic / arcade signal-matrix plate showcasing all 12 NULLWAVE Attunements (`SPL // SPLIT`, `FAN // FAN`, `PRC // PIERCE`, `RPD // RAPID`, `HVY // HEAVY`, `LNG // LONG WAVE`, `HOM // HOMING`, `RIC // RICOCHET`, `RER // REAR ARRAY`, `SPH // SIPHON`, `PHS // PHASE DRIVE`, `GRZ // GRAZER`). Each cell features a distinct geometric vector-mesh emblem/diagram (e.g., twin diverging bolts, wide fan arc, piercing spearhead, rapid pulse chevron, heavy core, long wave sine, homing orbital lock, ricochet prism bounce, rear array twin vectors, siphon repair crystal, phase drive afterimage, grazer resonance halo), its 3-letter tag, rarity/unlock badge, and glowing stack pips (`4/4` or `3/3`) over a deep void navy (`#030510`) background with `Palette.cs` neon cyan, magenta, and gold framing. |

**Art Deliverables:**
- Generate `docs/assets/attunement_matrix_sheet.png` (using the established C# HDR vector-mesh + shader renderer pipeline in scratch if `image-generator` subagent remains host-restricted).
- Visually inspect `docs/assets/attunement_matrix_sheet.png` with `read_file` to confirm legibility, layout alignment, and palette consistency.
- Document full provenance (purpose, reference style, dimensions, perspective, transparency, palette, file format, verified path, SHA-256, and prompt/synthesis specification) in `.game-studio/ASSET_MANIFEST.md`.

---

## 4. Acceptance Criteria (`@qa`)

1. **Clean Release Build:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeds with **0 warnings and 0 errors**.
2. **Self-Test Suite Pass:** `./run.cmd --selftest` passes with `RESULT: PASS` and new assertions from `TestAttunementLoadout()`.
3. **Smoke Run Pass:** `./run.cmd --smoke 5` completes cleanly with `RENDERSTATE -> PASS` and high FPS.
4. **Visual Capture Verification:**
   - Run `./run.cmd --capture combat --out screenshots/combat.png` and `./run.cmd --capture pause --out screenshots/pause.png` (and `--capture settings` if `SettingsMenu` was updated) and inspect with `read_file` to confirm the Live Attunement Loadout Strip renders cleanly on both HUD and Pause with zero overlap.
5. **Asset Verification:**
   - Confirm `docs/assets/attunement_matrix_sheet.png` exists, is a valid `1600x900` (or `1024x1024`) opaque PNG, and is accurately cataloged in `.game-studio/ASSET_MANIFEST.md`.

---

## 5. Designer Notes (`@designer`)
- Using `stackalloc ActiveAttunement[12]` with `AttunementCatalog.GetActive(player, span)` ensures zero GC pressure in `DrawHud` and `DrawPause`.
- Keep HUD badge heights compact (`~18–20px`) at `x = 56, y = 736` (wrapping to `y = 712` if `> 6` active attunements) so there is generous breathing room above `INTEGRITY` (`y = 770`).

---

## 6. Engineering Handoff (`@programmer`)

### Summary of Changes
1. **Zero-Allocation Active Attunement Query API (`src/Nullwave/World/Attunements.cs`):**
   - Added 3-character `Tag` (`SPL`, `FAN`, `PRC`, `RPD`, `HVY`, `LNG`, `HOM`, `RIC`, `RER`, `SPH`, `PHS`, `GRZ`) to `AttunementDef` across all 12 entries in `AttunementCatalog.All`.
   - Defined `readonly record struct ActiveAttunement(int Level, int MaxStacks, int CatalogIndex)` (12-byte unmanaged value type so `stackalloc ActiveAttunement[12]` compiles cleanly with zero managed references) with computed properties `Id`, `Tag`, and `Name` reading from `AttunementCatalog.All[CatalogIndex]`.
   - Added `AttunementCatalog.GetActive(Player player, Span<ActiveAttunement> destination)` which writes active attunements in deterministic catalog order with zero heap allocations and returns the active count.
2. **Live Attunement Loadout Strip on HUD & Pause Overlay (`src/Nullwave/UI/Screens.cs`):**
   - Added `DrawHudAttunements(Renderer r, Palette p, World.Player player)` called in `Screens.DrawHud` when `GameSettings.Current.ShowAttunements && player.AttunementCount > 0`. Renders compact `58x20` badge cells above `INTEGRITY` (`x = 56, y = 736`, wrapping cleanly to `y = 712` when `> 6` active).
   - Added `DrawPauseAttunements(Renderer r, Palette p, World.Player player)` called in `Screens.DrawPause` below a divider at `y = 602` (`ATTUNEMENTS // <totalStacks>` header, `XX / 12 SIGNALS` counter, centered `74x20` badge rows at `y = 630..654` or `"NO ATTUNEMENTS ACTIVE"` when `0`), with `AUDIO // ...` and `P / START // RESUME` cleanly positioned at `y = 690`.
   - Shared `DrawAttunementBadge(...)` renders a dark panel, top color bar, hue-offset frame (glowing when `Level >= MaxStacks`), 3-letter `Tag`, and `MaxStacks` (`3` or `4`) stack pips (`lit` up to `Level`, `dim` for remaining capacity).
3. **Settings Toggle & Capture Scenario Polish (`src/Nullwave/Core/GameSettings.cs`, `UI/SettingsMenu.cs`, `Core/Game.cs`):**
   - Added `public bool ShowAttunements { get; set; } = true;` to `GameSettings`.
   - Added `"ATTUNEMENT LOADOUT"` toggle under `INTERFACE` in `SettingsMenu` and tightened `Top = 114, RowH = 25, HeaderH = 30` so all 23 rows fit with generous margin above the `y = 818` hint line.
   - Updated `Game.ConfigureCapture("pause")` to apply 5 distinct attunements (`split` x2, `fan` x1, `pierce` x1, `rapid` x2, `homing` x1; 7 total stacks) so `--capture pause` deterministically showcases both the HUD and Pause loadout strips.
4. **Deterministic SelfTest Coverage (`src/Nullwave/Tools/SelfTest.cs`):**
   - Added `TestAttunementLoadout()` to `SelfTest.Run()`, raising total assertions from `69,215` to **`69,275`**.

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
    loadout: tags=SPL,FAN,PRC,RPD,HVY,LNG,HOM,RIC,RER,SPH,PHS,GRZ active=4 stacks=7
  RESULT: PASS (69275 assertions, 2.5s)
  ```
- **Smoke & Live GL State Verification (`--smoke 5`):**
  ```text
  ./run.cmd --smoke 5
  SMOKE | 5.0s fps=118 wave=1 score=675 enemies=3 bullets=4 bpm=96.2 audio=Edge capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```
- **Visual Capture Verification (`--capture combat`, `--capture pause`, `--capture settings`):**
  - Visually inspected `screenshots/combat.png`, `screenshots/pause.png`, and `screenshots/settings.png` with `read_file`; confirmed crisp badge rendering and zero text/element overlap across all 3 screens.

---

## 7. Art Handoff (`@artist`)
*(Summary recorded in `.game-studio/ASSET_MANIFEST.md` and on the swarm board.)*

---

## 8. QA Findings & Verdict (`@qa`)

- **Verdict:** **PASS (`APPROVED — 0 DEFECTS`)** — Full details recorded in `.game-studio/QA_REPORT.md` (§6).
- **Independent Build Verification:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeded in `0.55s` with **0 warnings, 0 errors**.
- **Independent SelfTest Verification:** `./run.cmd --selftest` passed with **`RESULT: PASS (69275 assertions, 2.5s)`** (up from `69,215`, verifying `TestAttunementLoadout()` across all 12 3-character tags, catalog-ordered `GetActive` span queries, stack counts, and `ShowAttunements` persistence).
- **Independent Smoke & Live GL State Verification:** `./run.cmd --smoke 5` passed at **`117 fps`** with **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
- **Code & Hot-Path Review:** Confirmed `ActiveAttunement` is an unmanaged 12-byte value struct queried via `stackalloc ActiveAttunement[12]` with zero per-frame heap allocations.
- **Visual & Asset Verification:** Visually inspected `screenshots/combat.png`, `screenshots/pause.png`, `screenshots/settings.png`, and `docs/assets/attunement_matrix_sheet.png` (`1600x900`, `1,208,729 bytes`, SHA-256 `B4A0DAFB49B0FD16F12B76AA80B6A7C1C14B6DBC3827340134476B2F3B55B5C3`), confirming zero UI overlap and exact match with `.game-studio/ASSET_MANIFEST.md`.
