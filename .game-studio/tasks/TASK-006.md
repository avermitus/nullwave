# TASK-006: Local Spotify Integration & Hardcore Riddim Drop-Trailer Recorder Hooks

- **Status:** `done`
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer`
- **Reviewer (QA):** `@qa`
- **Parallel Workstream:** `@trailer` owns `capture/riddim_tracks/**`, `capture/find_riddim_drops.py`, and `capture/make_riddim_trailer.py` (separate file ownership; no edits to `src/` or `.game-studio/`).

---

## 1. Goal & Player-Facing Benefit

1. **Zero-Config Local Spotify Now-Playing Integration (`Audio/SpotifyBridge.cs`, `AudioEngine.cs`, `AudioState.cs`, `Screens.cs`, `Meta.cs`):**
   - NULLWAVE's core pitch is playing alongside your own music, and Spotify is one of the primary target apps (`--app spotify`). Previously, NULLWAVE only showed the process name (`APP / SPOTIFY`), with no awareness of what track was currently playing or whether Spotify was paused/playing an ad.
   - `TASK-006` adds a pure-C# **Local Spotify Bridge** (`SpotifyBridge.cs`) requiring zero external NuGet packages and zero OAuth/API keys. It inspects running `Spotify` desktop and browser web-player window titles to parse live `Artist`, `Title`, `SpotifyPlaybackState`, and `DisplayTrack` (`"ARTIST - TITLE"`), enriches `AudioState` (`SourceName`, `SpotifyTrack`, `SpotifyStatus`), displays a clean `SPOTIFY // <TRACK>` readout on the Title screen, in-game HUD, and Pause overlay, and records the active `Track` on `RunRecord` in `save.json` (backward-compatible).
2. **Hardcore Recorder & Showcase Preset Hooks (`Program.cs`, `Core/Game.cs`, `World/Meta.cs`, `World/Pilot.cs`):**
   - Add `--mods <hard|all|csv>`, `--attune <showcase|csv>`, and `--seed <code>` CLI flags for `--record` and `--smoke` so high-intensity runs (15–16 stacked modifiers at `x25.00`, showcase attunement loadouts, and custom seeds) can be launched and recorded deterministically.
   - Upgrade `Pilot.Think(World)` to favor on-beat dashes inside `World.IsInPhaseSyncWindow(out _)` for non-emergency dodges, ensuring automated/recorded runs naturally showcase **Phase Sync** (`±65 ms`), **Live Attunement Loadout Strip**, and **Choir 4-Phase (`BLIND CHOIR`)** boss choreography.

---

## 2. Relevant Code & Ownership Constraints

### Strict File Ownership
- **`@designer` owns:** `.game-studio/BACKLOG.md` and `.game-studio/tasks/TASK-006.md`.
- **`@programmer` owns:** `src/Nullwave/**` (`Audio/SpotifyBridge.cs`, `Audio/AudioEngine.cs`, `Audio/AudioState.cs`, `World/Meta.cs`, `World/World.cs`, `World/Pilot.cs`, `UI/Screens.cs`, `Core/Game.cs`, `Program.cs`, `Tools/SelfTest.cs`) and the **Engineering Handoff** section of `.game-studio/tasks/TASK-006.md`.
- **`@trailer` owns:** `capture/riddim_tracks/**`, `capture/find_riddim_drops.py`, `capture/make_riddim_trailer.py`, and `capture/nullwave_hardcore_riddim_trailer*` (do not edit `src/` or `.game-studio/`).
- **`@qa` owns:** `.game-studio/QA_REPORT.md` and the **QA Findings & Verdict** section of `.game-studio/tasks/TASK-006.md`.

---

## 3. Implementation Plan (`@programmer`)

### A1. Pure-C# Local Spotify Bridge (`src/Nullwave/Audio/SpotifyBridge.cs`)
1. Define:
   - `public enum SpotifyPlaybackState { NotRunning, Idle, Playing, Ad }`
   - `public readonly record struct SpotifyTrackInfo(SpotifyPlaybackState State, string Artist, string Title, string Source, bool IsWebPlayer)` with computed:
     - `public bool IsPlaying => State == SpotifyPlaybackState.Playing;`
     - `public string DisplayTrack` -> `Artist` and `Title` non-empty: `$"{Artist} - {Title}"`; else `Title` when playing; else `""`.
2. Implement `public static SpotifyTrackInfo ParseWindowTitle(string? rawTitle, bool isBrowserProcess = false)` (pure, deterministic, testable):
   - **Desktop Spotify (`isBrowserProcess == false`):**
     - Empty/whitespace, `"Spotify"`, `"Spotify Free"`, `"Spotify Premium"` -> `SpotifyPlaybackState.Idle`.
     - `"Advertisement"` (case-insensitive) -> `SpotifyPlaybackState.Ad`.
     - Splits on the first `" - "` (or `" – "` / `" — "`) when both `Artist` and `Title` sides are non-empty -> `Playing` (`Artist`, `Title`), preserving any `" - "` inside song titles (e.g. `"Svdden Death - Behemoth - VIP"` -> Artist `"Svdden Death"`, Title `"Behemoth - VIP"`).
     - Otherwise non-empty string -> `Playing` (`Artist = ""`, `Title = trimmed`).
   - **Browser Spotify Web Player (`isBrowserProcess == true`):**
     - Must contain `"Spotify"` (case-insensitive) as a suffix/prefix/web-player marker (`" - Spotify"`, `"| Spotify"`, `"Spotify – Web Player"`, etc.); unrelated browser tabs return `NotRunning`.
     - Strip trailing browser suffix (`" - Google Chrome"`, `" - Microsoft Edge"`, `" — Mozilla Firefox"`, `" - Brave"`, `" - Opera"`).
     - `"Spotify – Web Player..."` / `"Spotify - Web Player..."` / `"Spotify"` -> `Idle`; `"Advertisement"` -> `Ad`.
     - Parse `"Artist - Title"` (or `"Title • Artist"` web player format) into `Playing` with `IsWebPlayer = true`.
3. Implement `SpotifyBridge.Poll()` / `Update(float dt)`:
   - Throttled polling (`~1.25s` interval) wrapped safely in `try/catch` (zero exceptions if processes exit mid-enumeration; always `Dispose()` enumerated `Process` instances).
   - Checks `Process.GetProcessesByName("Spotify")` first for a non-empty `MainWindowTitle` (preferring an active playing window), and falls back to common browser process names (`chrome`, `msedge`, `firefox`, `brave`, `opera`) if Spotify desktop is not playing.
   - Support `public void SetOverrideForTest(SpotifyTrackInfo? info)` so tests can deterministically exercise live state propagation without requiring Spotify open.

### A2. AudioEngine & AudioState Integration (`Audio/AudioEngine.cs` & `Audio/AudioState.cs`)
1. **In `src/Nullwave/Audio/AudioState.cs`:**
   - Add `public string SpotifyTrack = "";` and `public SpotifyPlaybackState SpotifyStatus = SpotifyPlaybackState.NotRunning;`.
2. **In `src/Nullwave/Audio/AudioEngine.cs`:**
   - Expose `public SpotifyBridge Spotify { get; } = new();`.
   - Call `Spotify.Update(dt)` in `AudioEngine.Update(float dt)` and copy `State.SpotifyStatus = Spotify.Current.State;` and `State.SpotifyTrack = Spotify.Current.DisplayTrack;`.
   - When external audio is active (`_external`):
     - Check if Spotify is the captured app (`_app != null && _app.Name.Contains("spotify", StringComparison.OrdinalIgnoreCase)`) or if Spotify is playing while using system loopback (`_app == null && Spotify.Current.IsPlaying`).
     - When true and `!string.IsNullOrEmpty(State.SpotifyTrack)`, enrich `State.SourceName = "SPOTIFY // " + State.SpotifyTrack;` (keeping `State.DeviceName` intact).

### A3. UI Readout & Run History (`UI/Screens.cs`, `World/Meta.cs`, `World/World.cs`, `Core/Game.cs`)
1. **Coordinate-Safe UI Display (`src/Nullwave/UI/Screens.cs`):**
   - Helper `FormatTrack(string track, int maxChars)` returning uppercase truncated text (`track[..(maxChars - 2)] + ".."`).
   - **Title (`DrawTitle`):** When `!string.IsNullOrEmpty(audio.SpotifyTrack)`, render `SPOTIFY // <TRACK>` right-aligned at `(1536, 748)` (`size 1.4f`, `p.Accent`, max ~38 chars) directly above `AUDIO // ...` (`1536, 774`), staying clear of the centered menu (`x = 580..1020`).
   - **In-Game HUD (`DrawHud`):** `BPM` is at `(1542, 40)`, `AUDIO //` at `(1542, 72)`, Spectrum top is `y = 114` (`baseline 188, height 74`). When `!string.IsNullOrEmpty(audio.SpotifyTrack)`, render `SPOTIFY // <TRACK>` right-aligned at `(1542, 93)` (`size 1.25f`, `p.Accent`, max ~28 chars -> `x >= 1270`, completely clear of the Boss Bar `x = 380..1220` and above Spectrum `y = 114`).
   - **Pause Overlay (`DrawPause`):** Expand Pause card height slightly (`h = 524`, `y = 214..738`) and move the footer line (`AUDIO // ...` and `P / START // RESUME`) to `y = 706`. When `!string.IsNullOrEmpty(audio.SpotifyTrack)`, render `SPOTIFY // <TRACK>` at `(546, 686)` (`size 1.35f`, `p.Accent`, max ~42 chars) cleanly between `DrawPauseAttunements` (`y = 612..674`) and the `y = 706` footer.
2. **Run Telemetry (`World/World.cs`, `World/Meta.cs`, `Core/Game.cs`):**
   - Add `public string Track = "";` to `RunSummary` and `public string Track { get; set; } = "";` to `RunRecord` (populated in `Game.EnterGameOver()` or `World.Die()` from `_audio.State.SpotifyTrack`; defaults to `""` in older `save.json` files).

### A4. Hard-Modifier, Attunement & Seed CLI Hooks + On-Beat Pilot (`Meta.cs`, `Program.cs`, `Game.cs`, `Pilot.cs`)
1. **`ModifierCatalog.ResolvePreset(string? spec)` (`src/Nullwave/World/Meta.cs`):**
   - `"hard"` -> returns all 15 modifier IDs in `ModifierCatalog.All` **except** `"quiet-signal"` (so synth/audio fallback remains usable unless explicitly requested).
   - `"all"` -> returns all 16 modifier IDs in `ModifierCatalog.All` (including `"quiet-signal"`).
   - Comma-separated string -> splits, trims, validates against `ModifierCatalog.Get(id) != null`, and returns matching IDs.
2. **CLI Overrides (`src/Nullwave/Program.cs` & `src/Nullwave/Core/Game.cs`):**
   - Parse `--mods <hard|all|csv>`, `--attune <showcase|csv>`, and `--seed <code>` in `Program.Main` and store on `Game.ModsOverride`, `Game.AttuneOverride`, `Game.SeedOverride`.
   - In `Game.StartSmokeRun(ulong seed)`:
     - If `SeedOverride` parses via `SeedCode.TryParse`, use `_meta.ResolveSeed` / parsed seed.
     - Start run (`StartRun(effectiveSeed, false)`).
     - If `ModsOverride` is set, resolve via `ModifierCatalog.ResolvePreset` and apply a `RunConfig` with those `SelectedModifiers` (without mutating the player's saved `save.json`!).
     - If `AttuneOverride` is set (`"showcase"` -> `["split", "split", "fan", "pierce", "rapid", "rapid", "homing"]`, or CSV of valid `AttunementCatalog` IDs), apply them to `_world.Player` immediately.
     - Log the active smoke/record configuration (`mods`, `attunes`, `seed`) concisely to stdout.
3. **Phase-Sync-Aware Pilot (`src/Nullwave/World/Pilot.cs`):**
   - Keep emergency survival dashes immediate (`closestBulletSq < 44f * 44f` or `bossDist < 120f`).
   - For medium-range threats (`44f..95f`) or tactical flow dashes (`hasThreat` and `w.Flow < 0.92f`), trigger `dash = true` when `w.IsInPhaseSyncWindow(out _)` is true so recorded/smoke runs showcase **Phase Sync** (`±65 ms`) naturally!

### A5. Deterministic Self-Test Assertions (`src/Nullwave/Tools/SelfTest.cs`)
- Add `TestSpotifyIntegration()` and `TestHardModifierRecordPreset()` called from `SelfTest.Run()`:
  1. `TestSpotifyIntegration`:
     - Verify `SpotifyBridge.ParseWindowTitle` across desktop Idle (`""`, `"Spotify"`, `"Spotify Premium"`), Ad (`"Advertisement"`), Playing (`"Svdden Death - Behemoth"`, hyphenated `"Artist - Track - VIP"`), and Web Player (`"Marauda - Blunder - Spotify - Google Chrome"`, `"Megalodon • Mercy - Spotify — Mozilla Firefox"`, `"Spotify – Web Player: Music for everyone"`, unrelated `"YouTube - Google Chrome"` -> `NotRunning`).
     - Verify `RunRecord` preserves `Track` across `MetaProgress` save/load and handles legacy JSON without `Track`.
  2. `TestHardModifierRecordPreset`:
     - Verify `ModifierCatalog.ResolvePreset("hard")` returns 15 IDs (excluding `"quiet-signal"`) and `ModifierCatalog.Build(hard)` produces `ScoreMultiplier == 25f` (clamped cap) and `BossPhases == 4` (`blind-choir`), `ResolvePreset("all")` returns 16 IDs (`SynthDisabled == true`), and CSV filtering ignores unknown IDs.
     - Verify `Pilot.Think` triggers an on-beat Phase Sync dash when a medium-range bullet (`dist = 65u`) is present at `BeatPhase = 0.01f` and holds dash at `BeatPhase = 0.50f` until the beat window.

---

## 4. Acceptance Criteria (`@qa`)

1. **Clean Release Build:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeds with **0 warnings and 0 errors**.
2. **Self-Test Suite Pass:** `./run.cmd --selftest` passes with `RESULT: PASS` and new assertions from `TestSpotifyIntegration()` and `TestHardModifierRecordPreset()`.
3. **Smoke & Hard-Preset Smoke Verification:**
   - `./run.cmd --smoke 5` passes with `RENDERSTATE -> PASS`.
   - `./run.cmd --smoke 4 --mods hard --attune showcase --seed 001N4C4M` runs cleanly (`wave >= 1`, `BossPhases == 4` / `x25.00` modifiers active, attunement loadout visible, zero exceptions, `RENDERSTATE -> PASS`).
4. **Visual Capture Check:**
   - `--capture combat` and `--capture pause` (plus `--capture title`) show zero HUD/UI overlap.
5. **Trailer Readiness Handoff:**
   - Confirm `--record` accepts `--mods hard --attune showcase --wave 4` cleanly so `@trailer` can proceed to Phase 2 recording.

---

## 5. Designer Notes (`@designer`)
- Placing `SPOTIFY // <TRACK>` at `(1542, 93)` on the HUD keeps it nested between `AUDIO // ...` (`y = 72`) and the Spectrum top (`y = 114`), while keeping its left edge (`x >= 1270`) completely clear of the Choir Boss Bar (`x = 380..1220`).
- Keeping `--mods` / `--attune` overrides strictly transient on the `RunConfig` inside `StartSmokeRun` guarantees `--record` and `--smoke` never mutate the user's real `save.json` modifier selection.

---

## 6. Engineering Handoff (`@programmer`)

### Summary of Changes
1. **Pure-C# Local Spotify Bridge (`src/Nullwave/Audio/SpotifyBridge.cs`, `AudioEngine.cs`, `AudioState.cs`):**
   - Added `SpotifyBridge`, `SpotifyPlaybackState` (`NotRunning`, `Idle`, `Playing`, `Ad`), and `SpotifyTrackInfo` (`SpotifyBridge.cs`) with zero extra NuGet packages or OAuth keys.
   - Implemented deterministic `SpotifyBridge.ParseWindowTitle(string? rawTitle, bool isBrowserProcess = false)` handling desktop Spotify (`Idle`, `Ad`, `Artist - Title` including hyphenated song titles) and browser Web Player tabs (`chrome`, `msedge`, `firefox`, `brave`, `opera` in both `"Artist - Title - Spotify"` and `"Title • Artist - Spotify"` formats), plus throttled `Poll()` (`1.25s`), `Update(dt)`, and `SetOverrideForTest`.
   - Added `SpotifyTrack` and `SpotifyStatus` to `AudioState.cs` and wired `AudioEngine.Spotify` (`AudioEngine.cs`), enriching `State.SourceName = "SPOTIFY // " + State.SpotifyTrack` when Spotify is captured or playing over system loopback.
2. **Spotify Now-Playing UI & Run Telemetry (`src/Nullwave/UI/Screens.cs`, `World/World.cs`, `World/Meta.cs`, `Core/Game.cs`):**
   - Added `Screens.FormatTrack(string? track, int maxChars)` and rendered `SPOTIFY // <TRACK>` on Title (`1536, 748`), HUD (`1542, 93` between `AUDIO //` and Spectrum), and Pause (`546, 686` with card height `524` and footer at `y = 706`).
   - Added `Track` on `RunSummary` (`World.cs`) and `RunRecord` (`Meta.cs`), populated in `Game.EnterGameOver()` (`Game.cs`) and backward-compatible with older `save.json` files.
3. **Hardcore Recorder Flags & On-Beat Phase Sync Pilot (`src/Nullwave/World/Meta.cs`, `Program.cs`, `Core/Game.cs`, `World/Pilot.cs`):**
   - Added `ModifierCatalog.ResolvePreset(string? spec)` (`"hard"` = 15 modifiers excluding `"quiet-signal"`, `"all"` = all 16, or CSV).
   - Added `--mods <hard|all|csv>`, `--attune <showcase|csv>`, and `--seed <code>` CLI flags in `Program.cs` and transient application inside `Game.StartSmokeRun` (without mutating `save.json` or `_meta.SelectedModifiers`).
   - Upgraded `Pilot.Think(World)` (`Pilot.cs`) to execute emergency dodges immediately (`<44u` bullet or `<120u` boss) while timing tactical dodges (`44..95u` or flow dashes) inside `World.IsInPhaseSyncWindow(out _)` so smoke/recorded runs showcase Phase Sync naturally.
4. **Deterministic SelfTest Coverage (`src/Nullwave/Tools/SelfTest.cs`):**
   - Added `TestSpotifyIntegration()` and `TestHardModifierRecordPreset()` to `SelfTest.Run()`.

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
    spotify: desktop="Svdden Death - Behemoth" web="Megalodon - Mercy"
    hard preset: count=15 mult=x25.00 bossPhases=4 pilotSync=True
  RESULT: PASS (67666 assertions, 2.6s)
  ```
- **Standard & Hard-Preset Smoke Verification:**
  ```text
  ./run.cmd --smoke 5
  SMOKECONFIG | seed=00Z9DUE9 mods=3 mult=x1.66 bossPhases=3 attunes=0
  SMOKE | 5.0s fps=119 wave=1 score=2885 enemies=2 bullets=2 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS

  ./run.cmd --smoke 4 --mods hard --attune showcase --seed 001N4C4M
  SMOKECONFIG | seed=001N4C4M mods=15 mult=x25.00 bossPhases=4 attunes=7
  SMOKE | 4.0s fps=114 wave=1 score=43337 enemies=2 bullets=0 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```
- **Visual Capture Verification:**
  - Visually inspected `--capture title`, `--capture combat` (showing `SYNC 4` from Pilot's on-beat Phase Sync dashes), and `--capture pause` with `read_file`; zero HUD/UI overlap.

---

## 7. QA Findings & Verdict (`@qa`)

- **Verdict:** **PASS (`APPROVED — 0 DEFECTS`)** — Full details recorded in `.game-studio/QA_REPORT.md` (§10).
- **Independent Build Verification:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeded in `0.60s` with **0 warnings, 0 errors**.
- **Independent SelfTest Verification:** `./run.cmd --selftest` passed with **`RESULT: PASS (67666 assertions, 2.7s)`**, verifying `TestSpotifyIntegration()` (desktop/web title parsing, `FormatTrack`, `RunRecord.Track` JSON persistence & legacy fallback) and `TestHardModifierRecordPreset()` (`hard`=15 mods/`x25.00`/`BossPhases=4`, `all`=16, CSV filtering, and Phase-Sync-aware `Pilot.Think` dash timing).
- **Independent Standard & Hard-Preset Smoke Verification:**
  - `./run.cmd --smoke 5` -> **PASS (`121 fps`, `RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`)**.
  - `./run.cmd --smoke 4 --mods hard --attune showcase --seed 001N4C4M` -> **PASS (`SMOKECONFIG | seed=001N4C4M mods=15 mult=x25.00 bossPhases=4 attunes=7`, `109 fps`, `RENDERSTATE -> PASS`)**.
- **Code & Hot-Path Review:** Confirmed `SpotifyBridge` throttles process polling (`1.25s`) and disposes all `Process` handles, and `--mods`/`--attune` overrides remain transient without mutating `save.json`.
- **Visual & Trailer Readiness Verification:** Visually inspected `screenshots/title.png`, `screenshots/combat.png` (`SYNC 4` visible), and `screenshots/pause.png` with zero HUD/UI overlap. `--mods hard --attune showcase` is cleared for `@trailer` Phase 2 recording.
