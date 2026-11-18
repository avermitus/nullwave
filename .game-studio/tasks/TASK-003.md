# TASK-003: Interactive Seed Entry & Seed History Replay in Archive + Seed Archive Console Asset

- **Status:** `done`
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer`
- **Owner (Art):** `@artist`
- **Reviewer (QA):** `@qa`

---

## 1. Goal & Player-Facing Benefit

1. **Interactive Seed History Replay & Custom 8-Character Seed Entry (`Rng.cs`, `Meta.cs`, `InputState.cs`, `Game.cs`, `Screens.cs`):**
   - `MetaProgress` already records `RecentRuns` (last 8 runs) and `SeedRecords` (last 40 unique seeds with `BestScore`, `BestWave`, `Runs`), and `SeedCode` converts seeds to 8-character codes (`0123456789ABCDEFGHJKLMNPQRSTUVWXYZ`), but players previously had **no way to select a past seed from the Archive or type a custom 8-character seed code** to replay a favorite arena layout, palette, and enemy spawn sequence.
   - `TASK-003` turns the Archive into an interactive **Signal Decoder & Seed Replay Console**: players can cycle through recorded seeds from `RecentRuns` / `SeedRecords`, inspect that seed's historical `BEST WAVE`, `BEST SCORE`, and `RUNS`, or type/edit any custom 8-character seed code (`0-9`, `A-Z`) and launch a run directly with that seed and their active Signal Modifiers.
2. **Seed Archive Console Studio Visual Asset (`docs/assets/seed_archive_console.png`, `.game-studio/ASSET_MANIFEST.md`):**
   - Expand NULLWAVE's visual documentation with a high-craft `1600x900` (`16:9` opaque RGBA `A=255`) technical illustration of the `ARCHIVE OF THE SIGNAL // SEED REPLAY & SIGNAL DECODER` console interface, 8-character base-34 seed waveform decoder, and seed history telemetry in `Palette.cs` neon vector-mesh style.

---

## 2. Relevant Code, Assets & Ownership Constraints

### Strict File Ownership
- **`@designer` owns:** `.game-studio/BACKLOG.md` and `.game-studio/tasks/TASK-003.md` (Design & Task Specification sections).
- **`@programmer` owns:** `src/Nullwave/**` (`Core/Rng.cs`, `World/Meta.cs`, `Core/InputState.cs`, `Core/Game.cs`, `UI/Screens.cs`, `Tools/SelfTest.cs`) and the **Engineering Handoff** section of `.game-studio/tasks/TASK-003.md`.
- **`@artist` owns:** `docs/assets/**` (`docs/assets/seed_archive_console.png`) and `.game-studio/ASSET_MANIFEST.md`.
- **`@qa` owns:** `.game-studio/QA_REPORT.md` and the **QA Findings & Verdict** section of `.game-studio/tasks/TASK-003.md`.

---

## 3. Implementation Plan

### Part A — Engineering Scope (`@programmer`)

#### A1. `SeedCode` Normalization, Parsing & Exact Seed Resolution (`Core/Rng.cs` & `World/Meta.cs`)
1. **In `src/Nullwave/Core/Rng.cs` (`SeedCode`):**
   - `SeedCode.Alphabet` is `"0123456789ABCDEFGHJKLMNPQRSTUVWXYZ"` (34 chars; `I` and `O` are omitted to avoid confusion with `1` and `0`).
   - Add `public const int CodeLength = 8;`.
   - Add `public static char NormalizeChar(char ch)`:
     - Converts lowercase to uppercase; maps `'I' -> '1'` and `'O' -> '0'`; returns the character if it is in `Alphabet`, or `'\0'` otherwise.
   - Add `public static string NormalizeCode(string? raw)` / `public static bool TryParse(string? raw, out ulong seed, out string canonicalCode)`:
     - Strips optional `"NW-"` prefix and whitespace/hyphens, normalizes characters through `NormalizeChar`, returns `false` if zero valid characters remain, and otherwise computes `seed = FromCode(cleaned)` and `canonicalCode = ToCode(seed)` (guaranteed 8-character canonical string).
     - Note: For any 8-character canonical code `c != "00000000"`, verify `ToCode(FromCode(c)) == c`.
2. **In `src/Nullwave/World/Meta.cs` (`RunRecord`, `SeedRecord`, `MetaProgress`):**
   - Add `public ulong SeedValue { get; set; }` to `RunRecord` and `SeedRecord` (populated in `RunRecord.From` and `MetaProgress.RecordRun` with `summary.Seed`; defaults to `0` when loading legacy saves).
   - Add `public ulong ResolveSeed(string? code)` on `MetaProgress`:
     - Normalizes `code`; if a matching `RunRecord` or `SeedRecord` has `SeedValue != 0`, returns that exact 64-bit `SeedValue`; otherwise returns `SeedCode.FromCode(normalized)`.
   - Add `public List<string> GetReplayableSeeds()` (or a zero-alloc / cached helper) returning distinct 8-character seed codes from `RecentRuns` (newest first) followed by `SeedRecords` (ordered by `LastPlayedUtc` descending).
   - Add `public bool TryGetSeedInfo(string? code, out int bestWave, out long bestScore, out int runs, out bool daily)` checking `SeedRecords` and `RecentRuns` so the Archive UI can display historical stats for any selected or typed seed.

#### A2. Alphanumeric Key & Backspace Polling (`src/Nullwave/Core/InputState.cs`)
1. Expand `PollKeys` (or add a dedicated `SeedCharKeys` array) in `InputState.cs` to poll `Key.Number0..Key.Number9`, `Key.Keypad0..Key.Keypad9`, `Key.A..Key.Z`, `Key.Backspace`, `Key.L`, `Key.C`.
2. Expose:
   - `public bool BackspacePressed { get; private set; }`
   - `public bool LPressed { get; private set; }`
   - `public bool CPressed { get; private set; }`
   - A clean helper such as `public int ReadTypedSeedChars(Span<char> destination)` (or `public char TypedSeedChar { get; private set; }`) that maps newly pressed alphanumeric keys (`WasPressed`) through `SeedCode.NormalizeChar` into uppercase `SeedCode` characters.

#### A3. Interactive Archive Seed Selection, Custom Entry & Direct Launch (`Game.cs` & `Screens.cs`)
1. **Archive Seed State & Testable API in `Game.cs`:**
   - Maintain Archive seed state on `Game`:
     - `public string SelectedSeedCode { get; private set; } = "";` (empty = no seed pinned / standard mode; 8-char code = target seed selected)
     - `public bool IsEditingSeed { get; private set; }`
     - `public string SeedEditBuffer { get; private set; } = "";`
   - Expose deterministic methods on `Game` (usable by both `UpdateArchive()` and `SelfTest.cs`):
     - `public bool CycleArchiveSeed(int dir = 1)` — steps forward/backward through `_meta.GetReplayableSeeds()`, updating `SelectedSeedCode` (and exits edit mode).
     - `public void BeginSeedEdit()` — enters custom seed edit mode (`IsEditingSeed = true`, `SeedEditBuffer = ""` or current `SelectedSeedCode`).
     - `public void TypeSeedChar(char ch)` / `public void BackspaceSeedEdit()` — appends normalized char (up to 8 chars) or removes the last char in `SeedEditBuffer`.
     - `public bool CommitSeedEdit()` — if `SeedCode.TryParse(SeedEditBuffer, out _, out string canonical)` succeeds, sets `SelectedSeedCode = canonical`, `IsEditingSeed = false`, and returns `true`; if empty, clears `SelectedSeedCode` and exits edit mode.
     - `public void CancelSeedEdit()` — sets `IsEditingSeed = false`.
     - `public bool LaunchSelectedSeed()` — when `_archiveReturn != AppState.Paused` and `SelectedSeedCode` is non-empty (or if `IsEditingSeed` commits a valid code), resolves `ulong seed = _meta.ResolveSeed(SelectedSeedCode)` and calls `StartRun(seed, daily: false)`!
2. **Input Handling in `Game.UpdateArchive()`:**
   - **When `IsEditingSeed == true`:**
     - Do **not** process `W/A/S/D` modifier grid navigation so typing letters (`W`, `A`, `S`, `D`, `R`, `N`, `C`, `L`, etc.) only feeds `TypeSeedChar(...)`.
     - `BackspacePressed` calls `BackspaceSeedEdit()`.
     - `ConfirmPressed` (`Enter`) calls `CommitSeedEdit()`.
     - `BackPressed` (`Esc`) calls `CancelSeedEdit()` (stays in Archive).
   - **When `IsEditingSeed == false`:**
     - Existing modifier grid navigation (`Up/Down/Left/Right`, `Space`/`Confirm` to toggle modifier, `Esc`/`Tab` to leave Archive) works unchanged.
     - `RPressed` cycles through recorded seeds (`CycleArchiveSeed(1)`).
     - `CPressed` (when `_archiveReturn != AppState.Paused`) opens custom seed entry (`BeginSeedEdit()`).
     - `LPressed` (when `_archiveReturn != AppState.Paused` and `SelectedSeedCode.Length > 0`) launches the run immediately via `LaunchSelectedSeed()`.
     - `NPressed` clears `SelectedSeedCode = ""` (returns to random seed).
     - Clicking the Seed Decoder panel or a `RECENT SIGNALS //` entry with the mouse can also select/cycle/edit for mouse convenience.
3. **UI Layout in `Screens.DrawArchive` (`src/Nullwave/UI/Screens.cs`):**
   - **Coordinate Audit (Verified from `Screens.cs`):**
     - `"ARCHIVE OF THE SIGNAL"` at `(64, 54)`, size `4.4f` occupies `x = 64..619, y = 54..85`.
     - `"ESC / B // BACK TO ..."` right-aligned at `(1534, 64)`, size `1.55f` occupies `x = 1255..1534, y = 64..75`.
     - Subtitle `"UNLOCKED SIGNALS CHANGE THE RULES..."` is at `y = 112`.
     - Thus, the top-center header bay **`x = 646..1234, y = 46..98` (`588px x 52px`)** is completely open!
   - **Render the `SIGNAL DECODER // SEED REPLAY` Box (`x = 646..1234, y = 46..98`):**
     - Draw a sleek framed console box (`UiRect` + `UiFrame` with `p.Primary` / `p.Accent` border, highlighted when `IsEditingSeed || SelectedSeedCode.Length > 0`).
     - **Top row inside box (`y = 54`):**
       - When `IsEditingSeed`: `"CUSTOM SEED // [" + SeedEditBuffer.PadRight(8, '_') + "]"` with a pulsing cursor and right-aligned `"ENTER OK // ESC CANCEL"`.
       - When `SelectedSeedCode.Length > 0`: `"TARGET SEED // " + SelectedSeedCode` in `p.Accent` (glow) and right-aligned `"[L] LAUNCH  [R] NEXT  [C] TYPE"`.
       - When idle (`SelectedSeedCode == ""`): `"TARGET SEED // RANDOM"` in `p.Dim` and right-aligned `"[R] CYCLE HISTORY  [C] CUSTOM SEED"`.
     - **Bottom row inside box (`y = 76`):**
       - When a seed code is active (selected or previewed while typing) and `meta.TryGetSeedInfo(code, out int bw, out long bs, out int runs, out bool daily)` is true:
         - Render `"RECORD // BEST WAVE " + bw.ToString("00") + "   BEST " + FormatScore(bs) + "   RUNS " + runs + (daily ? "   [DAILY]" : "")`.
       - When a valid custom code has never been played:
         - Render `"UNEXPLORED SIGNAL // PRESS L TO LAUNCH WITH ACTIVE MODIFIERS"`.
       - When idle:
         - Render `"REPLAY A RECORDED SIGNAL OR ENTER ANY 8-CHAR SEED CODE"`.
   - **Highlight in `DrawRecentSignals` (`y = 846`):**
     - When `SelectedSeedCode` matches `meta.RecentRuns[i].Seed`, draw a subtle highlight frame around that entry in the bottom `RECENT SIGNALS //` strip so cycling with `R` visually steps across the flight-recorder strip!
4. **Capture Harness Update (`Game.PrepareCaptureMeta` & `Game.ConfigureCapture`):**
   - In `Game.PrepareCaptureMeta()`, populate `_meta.SeedRecords` for the 3 capture `RecentRuns` seeds (`0x5EEDC0DEUL`, daily seed, `0xC0FFEE1234UL`) with matching `SeedValue`, `BestScore`, `BestWave`, and `Runs`.
   - In `Game.ConfigureCapture("archive")`, call `CycleArchiveSeed(1)` so `--capture archive` deterministically showcases the active `TARGET SEED` box with historical telemetry and the highlighted `RECENT SIGNALS //` entry.

#### A4. Deterministic Self-Test Suite (`src/Nullwave/Tools/SelfTest.cs`)
- Add `TestSeedReplayAndEntry()` in `SelfTest.cs` and call it from `SelfTest.Run()`:
  1. Verify `SeedCode.NormalizeChar` (`'i' -> '1'`, `'o' -> '0'`, lowercase -> uppercase, invalid -> `'\0'`) and `SeedCode.TryParse` / `NormalizeCode` round-tripping (`ToCode(FromCode(canonical)) == canonical`).
  2. Verify `MetaProgress.GetReplayableSeeds()`, `TryGetSeedInfo(...)`, and `ResolveSeed(...)` accurately return recorded seeds, historical best wave/score/runs, and exact 64-bit `SeedValue` across save/load.
  3. Verify `Game` seed selection & custom entry state machine headlessly (or via a temp `MetaProgress` / `SeedCode` workflow): cycling recorded seeds, entering custom seed characters (`TypeSeedChar`, `BackspaceSeedEdit`, `CommitSeedEdit`), and launching a run (`StartRun` / `LaunchSelectedSeed`) sets `ActiveWorld.Seed` to the exact target seed.

---

### Part B — Art Scope (`@artist`)

Create **1 new high-craft visual asset** (`docs/assets/seed_archive_console.png`) and append its full entry to `.game-studio/ASSET_MANIFEST.md`:

| # | Asset Filename | Purpose | Target Resolution | Visual Brief & Style |
|---|---|---|---|---|
| 5 | `docs/assets/seed_archive_console.png` | Seed Archive & Signal Decoder Console Plate (`TASK-003`) | `1600x900` (16:9 PNG, opaque RGBA `A=255`) | Technical sci-fi UI/schematic plate titled `ARCHIVE OF THE SIGNAL // SEED DECODER & FLIGHT RECORDER`. Features a central 8-character base-34 seed waveform decoder (`001N4C4M // 0x5EEDC0DE`) projecting a 3D wireframe arena topology preview (`SCATTER`, `HENGE`, `CITY`, `SPIRAL`, `LANES`) with extruded monoliths and palette harmonic rings, flanked by a flight-recorder seed history table (`W12 84,500 CH36*`, `DAILY W09 41,250`, `W06 18,900`) and base-34 character matrix in `Palette.cs` deep void navy (`#030510`), electric cyan (`#20F0FF`), hyper magenta (`#FF38B8`), and solar gold (`#FFD866`). |

**Art Deliverables:**
- Generate `docs/assets/seed_archive_console.png` (`1600x900`, opaque `RGBA` `A=255`).
- Visually inspect `docs/assets/seed_archive_console.png` with `read_file` to confirm visual clarity, alignment, and palette fidelity.
- Append full provenance (purpose, reference style, dimensions, perspective, transparency, palette, file format, verified path, SHA-256, and prompt/synthesis specification) to `.game-studio/ASSET_MANIFEST.md`.

---

## 4. Acceptance Criteria (`@qa`)

1. **Clean Release Build:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeds with **0 warnings and 0 errors**.
2. **Self-Test Suite Pass:** `./run.cmd --selftest` passes with `RESULT: PASS` and new assertions from `TestSeedReplayAndEntry()`.
3. **Smoke Run Pass:** `./run.cmd --smoke 5` completes cleanly with `RENDERSTATE -> PASS` and high FPS.
4. **Visual Capture Verification:**
   - Run `./run.cmd --capture archive --out screenshots/archive.png` and inspect with `read_file` to confirm the `TARGET SEED //` decoder panel in the top header (`x = 646..1234, y = 46..98`) and the highlighted `RECENT SIGNALS //` entry at `y = 846` render cleanly with zero text overlap.
5. **Asset Verification:**
   - Confirm `docs/assets/seed_archive_console.png` exists, is a valid `1600x900` opaque PNG, and is accurately cataloged in `.game-studio/ASSET_MANIFEST.md`.

---

## 5. Designer Notes (`@designer`)
- Placing the `SIGNAL DECODER` box in the open top-center header bay (`x = 646..1234, y = 46..98`) avoids crowding the 2x8 modifier grid (`y = 176..792`) or the 2-line footer (`y = 818` and `y = 846`), while visually linking to the highlighted chip in the `RECENT SIGNALS //` strip at the bottom.
- Disabling `W/A/S/D` grid movement while `IsEditingSeed == true` ensures typing hexadecimal/base-34 letters (`A`, `D`, `S`, `W`, `R`, `N`, `C`, `L`) never accidentally moves the modifier cursor or triggers hotkeys until `Enter` or `Esc` is pressed.

---

## 6. Engineering Handoff (`@programmer`)

### Summary of Changes
1. **`SeedCode` Normalization, Parsing & Exact Seed Resolution (`src/Nullwave/Core/Rng.cs`, `World/Meta.cs`):**
   - Added `SeedCode.CodeLength = 8`, `SeedCode.NormalizeChar(char)` (uppercase base-34 check with `I -> 1` and `O -> 0`), `SeedCode.NormalizeCode(string?)`, and `SeedCode.TryParse(string?, out ulong seed, out string canonicalCode)` (`Rng.cs`).
   - Added `public ulong SeedValue { get; set; }` to `SeedRecord` and `RunRecord` (`Meta.cs`), populated in `RunRecord.From` and `MetaProgress.RecordRun`.
   - Added `MetaProgress.GetReplayableSeeds()` (distinct 8-char codes from `RecentRuns` newest-first followed by `SeedRecords` by `LastPlayedUtc` descending), `MetaProgress.TryGetSeedInfo(...)`, and `MetaProgress.ResolveSeed(string?)` (returning the exact recorded 64-bit `SeedValue` when available, or decoding via `SeedCode.FromCode`).
2. **Alphanumeric Key & Backspace Polling (`src/Nullwave/Core/InputState.cs`):**
   - Expanded `PollKeys` to include `Key.Backspace`, `Key.Number0..Key.Number9`, `Key.Keypad0..Key.Keypad9`, and `Key.A..Key.Z`.
   - Added `CPressed`, `LPressed`, `BackspacePressed`, and `ReadTypedSeedChars(Span<char> destination)` mapping newly pressed alphanumeric keys through `SeedCode.NormalizeChar`.
3. **Interactive Archive Seed Selection, Custom Entry & Direct Launch (`src/Nullwave/Core/Game.cs`, `UI/Screens.cs`):**
   - Added `SelectedSeedCode`, `IsEditingSeed`, and `SeedEditBuffer` on `Game` along with `CycleArchiveSeed`, `BeginSeedEdit`, `TypeSeedChar`, `BackspaceSeedEdit`, `CommitSeedEdit`, `CancelSeedEdit`, `ClearSelectedSeed`, and `LaunchSelectedSeed`.
   - Updated `Game.UpdateArchive()` so `IsEditingSeed == true` suppresses `W/A/S/D` modifier grid movement and feeds typed characters into `SeedEditBuffer` (`Backspace` deletes, `Enter` commits, `Esc` cancels), while idle Archive mode supports `R` (cycle history), `C` (custom 8-char seed entry), `L` (launch run with selected seed), and `N` (clear target seed).
   - Added `DrawArchiveSeedDecoder(...)` in `Screens.DrawArchive` (`x = 646..1234, y = 46..98`) displaying `TARGET SEED // <code|RANDOM>` or `CUSTOM SEED // [........]`, action hints, and historical `RECORD // WAVE ... BEST ... RUNS ...` (or `UNEXPLORED SIGNAL`), and updated `DrawRecentSignals` (`y = 846`) to frame the active seed in the bottom strip.
   - Updated `Game.PrepareCaptureMeta()` and `Game.ConfigureCapture("archive")` to pre-select the top recorded seed so `--capture archive` deterministically showcases the new UI.
4. **Deterministic SelfTest Coverage (`src/Nullwave/Tools/SelfTest.cs`):**
   - Added `TestSeedReplayAndEntry()` to `SelfTest.Run()`, raising total assertions from `69,275` to **`69,312`**.

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
    seed replay: replayable=2 top=TNTVJNLW (W09 38,400 runs=2) custom=C0FFEE11
  RESULT: PASS (69312 assertions, 2.5s)
  ```
- **Smoke & Live GL State Verification (`--smoke 5`):**
  ```text
  ./run.cmd --smoke 5
  SMOKE | 5.0s fps=115 wave=1 score=676 enemies=3 bullets=4 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```
- **Visual Capture Verification (`--capture archive`):**
  - Visually inspected `screenshots/archive.png` with `read_file`; confirmed the `TARGET SEED // 0111T20E` decoder panel in the top header (`x = 646..1234, y = 46..98`) and the highlighted `0111T20E W12 84,500 CH36*` chip in `RECENT SIGNALS //` (`y = 846`) render cleanly with zero overlap.

---

## 7. Art Handoff (`@artist`)
*(Summary recorded in `.game-studio/ASSET_MANIFEST.md` and on the swarm board.)*

---

## 8. QA Findings & Verdict (`@qa`)

- **Verdict:** **PASS (`APPROVED — 0 DEFECTS`)** — Full details recorded in `.game-studio/QA_REPORT.md` (§7).
- **Independent Build Verification:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeded in `0.59s` with **0 warnings, 0 errors**.
- **Independent SelfTest Verification:** `./run.cmd --selftest` passed with **`RESULT: PASS (69312 assertions, 2.4s)`** (up from `69,275`, verifying `TestSeedReplayAndEntry()` across `SeedCode.NormalizeChar`, `TryParse`, `GetReplayableSeeds`, `TryGetSeedInfo`, 64-bit `ResolveSeed` round-tripping, and identical replayed `GameWorld` arena obstacle layouts).
- **Independent Smoke & Live GL State Verification:** `./run.cmd --smoke 5` passed at **`114 fps`** with **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
- **Code & Hot-Path Review:** Confirmed `SeedCode.TryParse` and `InputState.ReadTypedSeedChars` use stack-allocated spans (`stackalloc char[8]`) with zero per-frame heap allocations, and `IsEditingSeed` cleanly isolates custom seed typing from `W/A/S/D` modifier navigation.
- **Visual & Asset Verification:** Visually inspected `screenshots/archive.png` and `docs/assets/seed_archive_console.png` (`1600x900`, `1,488,350 bytes`, SHA-256 `1FD00AB430C317CE34F0BD87F0B0E68B700D0B6B346DB8F6D6C8EBEAD2016C66`), confirming zero UI overlap and exact match with `.game-studio/ASSET_MANIFEST.md`.
