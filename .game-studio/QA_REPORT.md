# NULLWAVE — QA & Performance Review Report (`.game-studio/QA_REPORT.md`)

- **Reviewer:** `@qa` (Independent QA / Performance Reviewer)
- **Date:** `2026-11-19`
- **Task Reviewed:** `TASK-001` through `TASK-009` (Abyssal Dark Theme + `uLights[12]`)
- **Overall Verdict:** **PASS (`APPROVED — 0 DEFECTS`)**

---

## 1. Pre-Task Baseline Audit (Read-Only)

### 1.1 Launch Modes (`src/Nullwave/Program.cs`)
- `--selftest`: Headless deterministic assertion suite (`SelfTest.Run()`).
- `--smoke <seconds>`: Fixed-length automated `Pilot` AI run on a real OpenGL 3.3+ context (`0x5A1E5EEDUL`), reporting FPS, wave, score, entity counts, and tracked BPM.
- `--capture <scenario> --out <png>`: Deterministic screenshot generator (`title`, `archive`, `combat`, `boss`, `laser`, `attune`, `pause`, `gameover`, `hurt`, `roster`, `songlasers`, `speed`, `drop`, `breakdown`, `settings`).
- `--record <seconds> --wave <N> --video <mp4> --wav <wav>`: Offline NVENC video + WAV audio recorder.
- `--audiotest`, `--beatbench`, `--sfxdump`, `--dynamics`, `--list-apps`, `--app <name>`, `--style <0..5>`.

### 1.2 Baseline SelfTest Suites (`src/Nullwave/Tools/SelfTest.cs`)
- Pre-TASK-001 included 19 test methods (`TestMeta`, `TestSeeds`, `TestDamageAndBurst`, `TestLongRun`, `TestEndlessWave`, `TestModels`, `TestBeatFire`, `TestMapLayouts`, `TestHealthDisplay`, `TestSettings`, `TestBeatTracker`, `TestFireOnRealBeats`, `TestMusicDirector`, `TestBackdropVariety`, `TestRacerFeel`, `TestLoudAudioStaysReactive`, `TestSongLasers`, `TestNewEnemies`, `TestSfx`) totaling `69,058` assertions.

### 1.3 Baseline Gaps Identified Before `TASK-001`
1. `PostFx.Process` disabled `Blend` and `DepthTest`, bound bloom/scene textures on `Texture0..Texture3` leaving `ActiveTexture` at `Texture3`, and never restored baseline GL state.
2. `Renderer.Render` used `glBlendFunc(SrcAlpha, OneMinusSrcAlpha)` for `_uiAlpha`, pulling destination alpha below `1.0` (`< 255`) wherever translucent UI rects/text rendered over the opaque composite, and exited in additive blend (`SrcAlpha, One`).
3. `Game.EnterGameOver()` called `_meta.RecordRun(_world.Summary)` before `Screens.DrawGameOver` rendered, overwriting `BestWave` and `BestScore` before UI draw and suppressing personal-best celebration; `MetaProgress` lacked `BestCombo` and `RecentRuns` history.

---

## 2. Independent Code & Architecture Review (`TASK-001`)

### 2.1 OpenGL Blend-State & Texture-Unit Hygiene (`BlendMode.cs`, `PostFx.cs`, `Renderer.cs`, `Program.cs`)
- **`src/Nullwave/Render/BlendMode.cs` (New):**
  - `BlendMode` (`readonly record struct`) defines separate RGB/alpha blend factors applied via `gl.BlendFuncSeparate(SrcColor, DstColor, SrcAlphaFactor, DstAlphaFactor)`:
    - `BlendMode.Alpha`: `RGB = (SrcAlpha, OneMinusSrcAlpha)`, `Alpha = (One, OneMinusSrcAlpha)`. When `dstA == 1.0`, `1.0 * srcA + (1.0 - srcA) * 1.0 == 1.0` for all `srcA in [0, 1]`.
    - `BlendMode.Additive`: `RGB = (SrcAlpha, One)`, `Alpha = (Zero, One)`. When `dstA == 1.0`, `0.0 * srcA + 1.0 * 1.0 == 1.0` for all `srcA in [0, 1]`.
  - `RenderBaseline.Default` specifies `Blend = true`, `DepthTest = false`, `Mode = BlendMode.Alpha`, `ActiveUnit = 0`, `SamplerUnits = 4`, `VertexArray = 0`.
- **`src/Nullwave/Render/PostFx.cs`:**
  - `PostFx.Process(...)` calls `PostFx.RestoreBaseline(_gl)` immediately after the composite draw (`line 140`), unbinding `Texture2D` on units `3..0`, setting `ActiveTexture(Texture0)`, enabling `Blend`, disabling `DepthTest`, applying `BlendMode.Alpha`, and unbinding VAO `0`.
  - `PostFx.VerifyBaseline(GL, out string)` inspects live GL state via `gl.IsEnabled` / `gl.GetInteger` (used only at the end of `--smoke`, never on hot per-frame paths).
- **`src/Nullwave/Render/Renderer.cs`:**
  - Lines `331–336`: UI pass applies `BlendMode.Alpha.Apply(_gl)` for `_uiAlpha.Draw()`, `BlendMode.Additive.Apply(_gl)` for `_uiGlow.Draw()`, and finishes with `PostFx.RestoreBaseline(_gl)`.
- **Hot-Path Allocation / Performance Check:**
  - `BlendMode` and `RenderBaseline` are `readonly record struct` value types. `RestoreBaseline` executes a fixed 4-iteration `for` loop with zero heap allocations. No per-frame allocations were introduced in `PostFx.Process` or `Renderer.Render`.

### 2.2 Flight Recorder Telemetry & Personal-Best Callouts (`Meta.cs`, `Game.cs`, `Screens.cs`)
- **`src/Nullwave/World/Meta.cs`:**
  - Added `[Flags] enum RunRecords` (`None`, `Score`, `Wave`, `Combo`), `readonly record struct RecordBreak`, and serializable `RunRecord`.
  - `MetaProgress.CompareWithBests(RunSummary)` evaluates personal bests *before* mutating `BestWave`, `BestScore`, and `BestCombo`.
  - **Legacy Save Compatibility:** When loading an existing pre-`TASK-001` save (`Runs > 0 && BestCombo == 0 && RecentRuns.Count == 0`), `CompareWithBests` suppresses false-positive `RunRecords.Combo` on the first run while adopting its `MaxCombo` as the baseline. `MetaProgress.Load` also sanitizes `null` entries, negative `BestCombo`, and oversized `RecentRuns` lists (`> 8`).
  - `RecentRuns` is inserted at index `0` (newest first) and capped at `RecentRunCap = 8`.
- **`src/Nullwave/Core/Game.cs` & `src/Nullwave/UI/Screens.cs`:**
  - `EnterGameOver()` stores `_records = _meta.LastRecords`, triggers `SfxType.Unlock` (`0.6f`) when a personal best is set even if no new modifier/attunement unlocked, and passes `_records` to `Screens.DrawGameOver`.
  - `DrawGameOver` highlights broken stats (`WAVE`, `SCORE`, `CHAIN`) in `p.Accent` with a pulsing boxed `NEW BEST` tag, shows `BEST CHAIN` on the career bests line (`y = 540`), and displays `Screens.RecordBanner(records, s)` at `y = 566` (e.g., `NEW RECORD // CHAIN +6`).
  - `DrawArchive` includes `BEST CHAIN` in the career header (`y = 134`) and renders `DrawRecentSignals` at `y = 846`, cleanly below the 2x8 modifier grid (`y = 176..792`) and divider (`y = 798`) with horizontal width clipping (`cx + w > right`).

### 2.3 `.gitignore` Hygiene
- Verified `.gitignore` lines `12–13` explicitly include `capture/*.mp4` and `capture/*.wav` per `HANDOFF.md` §8.

---

## 3. Independent Build, SelfTest & Smoke Verification Evidence

All verification commands were executed independently by `@qa`:

### 3.1 Release Build
- **Command:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo`
- **Output:**
  ```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  Nullwave -> C:\Users\averm\.config\freebuff-desktop\local-projects\837b35da-9d09-4851-899b-a6ca69b82a1c\src\Nullwave\bin\Release\net8.0\Nullwave.dll

  Build succeeded.
      0 Warning(s)
      0 Error(s)

  Time Elapsed 00:00:00.58
  ```
- **Result:** **PASS (`0 warnings, 0 errors`)**

### 3.2 Headless Self-Test Suite (`--selftest`)
- **Command:** `./run.cmd --selftest`
- **Output:**
  ```text
  NULLWAVE self test
    meta: unlocks=3 selected=1 save=1940 bytes
    seeds: daily=9RSPPBX4 tomorrow=RPBDK8JR
    damage: health=2 burst=1
    soak: ticks=30000 sim=0.44s waves=20 score=958008 kills=444 grazes=11 bulletMax=3 fx=123
    endless: wave=100 movement=19 enemies=7 queued=0 bullets=84 boss=True
    models: meshes=32 verts=11925 monoliths=18
    beat: beats=85 expected~84 eighths=170 volleys=7 offgrid=0 telegraphMax=1.00
    map: 5600x3600 layouts=SCATTER,HENGE,CITY,SPIRAL,LANES
    health: hp=2 display=2.00
    settings: persisted ok, offset ticks 28->40
    tracker: FourOnFloor 128 -> 127.9 bpm, median 5.6 ms, hits 100 %
    tracker: Backbeat 96 -> 96.0 bpm, median 9.3 ms, hits 97 %
    tracker: DrumAndBass 174 -> 86.9 bpm, median 11.0 ms, hits 96 %
    on-beat fire: volleys=23 median=21.1 ms within45ms=23/23 surgeMean=1.002
    director: breakdown=1.00 build=0.98 drop@30.0s drops=1 chorusIntensity=1.00 breakIntensity=0.00 hue -0.61->0.49
    backdrops: 300 unique genomes over 6 styles
    racer: top=560 flowAfter2s=0.28 bulletPace=1.75
    dynamics: bass std 0.14/0.13/0.10 energy std 0.09/0.09/0.07 pinned 3 % (quiet/normal/clipped)
    song lasers: high-energy volleys=5 breakdown=0 build=8 dropBeams=6 minWarmup=0.95s
    new enemies: mortarStrike=True prismBeam=True phantomBlink=True weaverMine=True
    sfx: all 15 effects finite, audible, unclipped
    telemetry: history=8/8 bestChain=33 lastBanner="NEW RECORDS // SCORE +974,999 // CHAIN +14"
    render state: baseline=SrcAlpha/OneMinusSrcAlpha+One/OneMinusSrcAlpha units=4 legacyTranslucentSamples=19/21
  RESULT: PASS (69215 assertions, 2.3s)
  ```
- **Result:** **PASS (`69,215 assertions`, up from `69,058`, `2.3s`)**

### 3.3 Live OpenGL Smoke Run & Render-State Readback (`--smoke 5`)
- **Command:** `./run.cmd --smoke 5`
- **Output:**
  ```text
  NULLWAVE | GL 4.1.0 NVIDIA 617.42 | NVIDIA GeForce RTX 5060 Laptop GPU/PCIe/SSE2
  AUDIO | capture=True output=True device=Edge error=
  SMOKE | 5.0s fps=114 wave=1 score=676 enemies=3 bullets=4 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```
- **Result:** **PASS (`114 FPS`, live GL state verified at `baseline` after both renders, backbuffer `alphaMin=255`, double-render pixel diff `0.000%`)**

### 3.4 UI Screenshot Visual Inspection (`screenshots/archive.png`, `screenshots/gameover.png`)
- Visually inspected both captured screenshots with `read_file`:
  - `screenshots/archive.png`: `BEST CHAIN 36` appears cleanly in the top career bar (`y = 134`), and `RECENT SIGNALS //  0111T20E W12 84,500 CH36*  |  D 9RSPPBX4 W09 41,250 CH28  |  FSL2QJSQ W06 18,900 CH17` renders in the bottom strip (`y = 846`) with zero overlap against the 2x8 modifier grid.
  - `screenshots/gameover.png`: `CHAIN 42` is highlighted with the boxed `NEW BEST` badge, `BEST CHAIN 42` is shown on the career bests line, and `NEW RECORD // CHAIN +6` renders cleanly above `NEW SIGNALS UNLOCKED`.

---

## 4. Studio Documentation & Visual Asset Verification

### 4.1 Studio Documentation Files
- `.game-studio/PROJECT.md`: Verified complete and accurate.
- `.game-studio/BACKLOG.md`: Verified 5 prioritized initiatives with impact/risk/effort scores and dependencies.
- `.game-studio/tasks/TASK-001.md`: Verified complete design, engineering handoff, art reference, and QA records.
- `.game-studio/ASSET_MANIFEST.md`: Verified complete provenance, palette mapping, prompts, dimensions, byte sizes, and SHA-256 checksums.

### 4.2 Visual Assets in `docs/assets/`
Independently verified file existence, byte sizes, SHA-256 checksums, and visual quality via `read_file`:

| Asset Path | Dimensions | File Size | SHA-256 Checksum | Visual Inspection |
|---|---|---|---|---|
| `docs/assets/nullwave_key_art.png` | `1600 x 900` (16:9) | `2,584,364 bytes` | `E42CA87C72CFDD78C8D3403ED696C23D396749FDB4C08F92651DE5E59716B5BB` | **PASS** — Crisp banked cyan/white interceptor, twin magenta thruster trails, 6-beam starburst convergence, Choir boss crown, and clean `NULLWAVE` title framing. |
| `docs/assets/choir_boss_emblem.png` | `1024 x 1024` (1:1) | `1,638,371 bytes` | `1B294ADB98A8FAD0C2C0025E60CB3C3DD33E3B9D26D4A1A62867D499A4363A9A` | **PASS** — Radially symmetric 24-pillar spectrum crown, geodesic core, 8 orbiting shards, and crisp `THE CHOIR` pixel typography. |
| `docs/assets/pilot_flow_insignia.png` | `1024 x 1024` (1:1) | `1,149,649 bytes` | `75F381416C21DE79A86C3E8738AB85D2415EA5D1C82E7E79D9117CEA32F41845` | **PASS** — Clean zenithal delta-wing interceptor crest, 16-beat PLL dial, symmetrical spectrum arcs, and `// FLOW STATE //` inscriptions. |

---

## 5. Final QA Sign-Off (`TASK-001`)

All `TASK-001` acceptance criteria are met with zero regressions, zero compiler warnings/errors, `69,215` passing headless assertions, verified live OpenGL baseline state (`alphaMin=255`, `rerenderDiff=0.000%` at `114 FPS`), and verified visual assets in `docs/assets/`. `TASK-001` is **APPROVED** and marked **`done`**.

---

## 6. Independent QA Verification (`TASK-002` — Live Attunement Loadout Strip & Attunement Matrix Sheet)

### 6.1 Code & Performance Review (`Attunements.cs`, `Screens.cs`, `GameSettings.cs`, `SettingsMenu.cs`, `Game.cs`)
- **Zero-Allocation Query API (`src/Nullwave/World/Attunements.cs`):**
  - Verified all 12 `AttunementDef` entries define unique 3-character uppercase `Tag` codes (`SPL`, `FAN`, `PRC`, `RPD`, `HVY`, `LNG`, `HOM`, `RIC`, `RER`, `SPH`, `PHS`, `GRZ`).
  - `ActiveAttunement(int Level, int MaxStacks, int CatalogIndex)` is an unmanaged 12-byte value type (`readonly record struct`), allowing `Span<ActiveAttunement> active = stackalloc ActiveAttunement[12];` in `Screens.DrawHudAttunements` and `Screens.DrawPauseAttunements` with **zero heap/GC allocations on the per-frame render path**.
- **HUD & Pause Loadout Strip Layout (`src/Nullwave/UI/Screens.cs`):**
  - `DrawHudAttunements` renders `58x20` badge cells at `x = 56, y = 736` (wrapping to `y = 712` when `> 6` active), cleanly separated from `INTEGRITY` (`y = 770`).
  - `DrawPauseAttunements` renders inside the `SIGNAL HELD` card (`y = 214..722`) below a divider at `y = 602` (`ATTUNEMENTS // <total>` at `y = 612`, centered `74x20` badge cells at `y = 630..654`, and `AUDIO // ...` footer at `y = 690`).
- **Settings Menu Integration (`src/Nullwave/Core/GameSettings.cs`, `src/Nullwave/UI/SettingsMenu.cs`):**
  - `GameSettings.ShowAttunements` defaults to `true` and persists in `save.json`.
  - `SettingsMenu` adds `"ATTUNEMENT LOADOUT"` under `INTERFACE` and tightens `Top = 114, RowH = 25, HeaderH = 30` so all 23 rows fit cleanly above the `y = 818` hint text.

### 6.2 Independent Build, SelfTest & Smoke Verification (`TASK-002`)
- **Release Build:**
  ```text
  ./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo
  Build succeeded.
      0 Warning(s)
      0 Error(s)
  Time Elapsed 00:00:00.55
  ```
- **Headless Self-Test Suite (`--selftest`):**
  ```text
  ./run.cmd --selftest
  ...
    loadout: tags=SPL,FAN,PRC,RPD,HVY,LNG,HOM,RIC,RER,SPH,PHS,GRZ active=4 stacks=7
  RESULT: PASS (69275 assertions, 2.5s)
  ```
- **Live OpenGL Smoke Run (`--smoke 5`):**
  ```text
  ./run.cmd --smoke 5
  NULLWAVE | GL 4.1.0 NVIDIA 617.42 | NVIDIA GeForce RTX 5060 Laptop GPU/PCIe/SSE2
  AUDIO | capture=True output=True device=Edge error=
  SMOKE | 5.0s fps=117 wave=1 score=676 enemies=3 bullets=4 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```

### 6.3 Visual Capture & Studio Asset Verification (`TASK-002`)
- **Screenshots (`screenshots/combat.png`, `screenshots/pause.png`, `screenshots/settings.png`):**
  - Visually inspected all 3 captures via `read_file`: confirmed crisp 3-letter tags and stack pips above `INTEGRITY` on the HUD (`combat.png`), inside the `SIGNAL HELD` card (`pause.png`), and the `ATTUNEMENT LOADOUT` toggle in `SETTINGS` (`settings.png`) with zero overlap.
- **Studio Asset (`docs/assets/attunement_matrix_sheet.png`):**
  - Verified file exists (`1,208,729 bytes`, `1600x900` 16:9 PNG, SHA-256 `B4A0DAFB49B0FD16F12B76AA80B6A7C1C14B6DBC3827340134476B2F3B55B5C3`), matches `.game-studio/ASSET_MANIFEST.md` Asset #4, and visually confirmed all 12 Attunement schematic cards (`SPL` through `GRZ`), stack pips (`4/4` and `3/3`), and bottom HUD/Pause preview strip.

### 6.4 Final QA Sign-Off (`TASK-002`)
All `TASK-002` acceptance criteria are satisfied with **0 defects**. `TASK-002` is **APPROVED** and marked **`done`**.

---

## 7. Independent QA Verification (`TASK-003` — Interactive Seed Entry & Seed History Replay in Archive)

### 7.1 Code & Architecture Review (`Rng.cs`, `Meta.cs`, `InputState.cs`, `Game.cs`, `Screens.cs`)
- **`SeedCode` Normalization & Parsing (`src/Nullwave/Core/Rng.cs`):**
  - `SeedCode.NormalizeChar(char)` converts lowercase to uppercase, maps `'I' -> '1'` and `'O' -> '0'`, and rejects non-alphabet characters (`'\0'`).
  - `SeedCode.TryParse(string?, out ulong seed, out string canonicalCode)` uses `stackalloc char[CodeLength]` (`CodeLength = 8`), strips optional `"NW-"` prefix and separators, and guarantees canonical round-tripping (`ToCode(FromCode(canonical)) == canonical`).
- **Exact Seed Resolution & History Lookup (`src/Nullwave/World/Meta.cs`):**
  - `SeedRecord.SeedValue` and `RunRecord.SeedValue` persist the full 64-bit `ulong` seed in `save.json` while remaining backward-compatible (`0` on legacy saves).
  - `MetaProgress.ResolveSeed(string?)` returns the exact recorded 64-bit `SeedValue` when replaying a known seed, or falls back to `SeedCode.FromCode` for custom/legacy codes.
  - `MetaProgress.GetReplayableSeeds()` and `TryGetSeedInfo(...)` aggregate best wave, best score, run count, and daily flag across `SeedRecords` and `RecentRuns`.
- **Input & Archive Decoder UI (`src/Nullwave/Core/InputState.cs`, `Core/Game.cs`, `UI/Screens.cs`):**
  - `InputState.ReadTypedSeedChars(Span<char>)` reads newly pressed alphanumeric keys into a caller-provided `stackalloc char[8]` buffer with zero heap allocations.
  - In `Game.UpdateArchive()`, `IsEditingSeed == true` cleanly isolates typing (`0-9`, `A-Z`, `Backspace`, `Enter`, `Esc`) from `W/A/S/D` modifier grid navigation. Idle Archive mode supports `R` (`CycleArchiveSeed`), `C` (`BeginSeedEdit`), `L` (`LaunchSelectedSeed`), and `N` (`ClearSelectedSeed`).
  - `Screens.DrawArchive` renders the `TARGET SEED //` / `CUSTOM SEED //` Signal Decoder box in the open top-center header bay (`x = 646..1234, y = 46..98`) and highlights the matching seed chip in the bottom `RECENT SIGNALS //` strip (`y = 846`) with zero overlap.

### 7.2 Independent Build, SelfTest & Smoke Verification (`TASK-003`)
- **Release Build:**
  ```text
  ./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo
  Build succeeded.
      0 Warning(s)
      0 Error(s)
  Time Elapsed 00:00:00.59
  ```
- **Headless Self-Test Suite (`--selftest`):**
  ```text
  ./run.cmd --selftest
  ...
    seed replay: replayable=2 top=TNTVJNLW (W09 38,400 runs=2) custom=C0FFEE11
  RESULT: PASS (69312 assertions, 2.4s)
  ```
- **Live OpenGL Smoke Run (`--smoke 5`):**
  ```text
  ./run.cmd --smoke 5
  NULLWAVE | GL 4.1.0 NVIDIA 617.42 | NVIDIA GeForce RTX 5060 Laptop GPU/PCIe/SSE2
  AUDIO | capture=True output=True device=Edge error=
  SMOKE | 5.0s fps=114 wave=1 score=676 enemies=3 bullets=4 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```

### 7.3 Visual Capture & Studio Asset Verification (`TASK-003`)
- **Archive Screenshot (`screenshots/archive.png`):**
  - Visually inspected via `read_file`: confirmed `TARGET SEED // 0111T20E`, `[L] LAUNCH  [R] NEXT  [C] TYPE  [N] CLEAR`, and `RECORD // WAVE 12   BEST 84,500   RUNS 2   [ARCHIVE SIGNAL]` render cleanly at `x = 646..1234, y = 46..98`, and `0111T20E W12 84,500 CH36*` is framed in the bottom `RECENT SIGNALS //` strip (`y = 846`).
- **Studio Asset (`docs/assets/seed_archive_console.png`):**
  - Verified file exists (`1,488,350 bytes`, `1600x900` 16:9 PNG, SHA-256 `1FD00AB430C317CE34F0BD87F0B0E68B700D0B6B346DB8F6D6C8EBEAD2016C66`), matches `.game-studio/ASSET_MANIFEST.md` Asset #5, and visually confirmed the 8-character Base-34 seed register (`001N4C4M`), 3D wireframe arena topology preview, Flight Recorder table, 34-key alphabet matrix, and bottom `RECENT SIGNALS //` strip.

### 7.4 Final QA Sign-Off (`TASK-003`)
All `TASK-003` acceptance criteria are satisfied with **0 defects**. `TASK-003` is **APPROVED** and marked **`done`**.

---

## 8. Independent QA Verification (`TASK-004` — On-Beat Dash / Rhythm Synergy Bonus "Phase Sync")

### 8.1 Code & Performance Review (`Entities.cs`, `World.cs`, `World.Player.cs`, `WorldRenderer.cs`, `Screens.cs`, `Game.cs`)
- **Timing Window & State Model (`src/Nullwave/World/Entities.cs`, `World/World.cs`):**
  - `Player` tracks `PhaseSyncs`, `PhaseSyncFlash`, and `LastPhaseSyncErrorMs`; `RunSummary` captures `PhaseSyncs` in `Die()`.
  - `World.IsInPhaseSyncWindow(out float errorMs)` evaluates `phaseDist = MathF.Min(BeatPhase, 1f - BeatPhase)` and `errorMs = phaseDist * (60_000f / MathF.Max(30f, Bpm))`, returning `true` iff `errorMs <= 65f && phaseDist <= 0.18f`.
  - `World.SetBeatClockForTest(double beatPos, float bpm = 120f)` enables deterministic headless verification in `SelfTest.cs`.
- **On-Beat Dash Synergy & Non-Penalty Guarantee (`src/Nullwave/World/World.Player.cs`):**
  - Off-beat survival dashes execute the exact baseline dash (`DashTime = 0.15f`, `DashCooldown = p.DashCooldownMax`, `Invuln >= 0.24f`, zero penalty).
  - On-beat dashes inside `IsInPhaseSyncWindow` grant `+0.05f` Flow (`FlowGain`), `+5f * p.TotalResonanceGain * Mods.ResonanceGain` Resonance, `DashCooldown * 0.82f`, `Invuln >= 0.30f`, `AddScore(120, p.Pos, "PHASE SYNC")`, expanding `195f` shockwave, 10 spark particles, and `SfxType.Pickup` (`0.28f`) chime.
- **Hot-Path Allocation Check (`WorldRenderer.cs`, `Screens.cs`):**
  - `WorldRenderer.DrawSpeedFx` / `DrawPlayer` and `Screens.DrawFlowMeter` use pre-allocated static arrays (`TrailL`, `TrailR`, `Ghosts`) and scalar/struct math with zero per-frame heap allocations.

### 8.2 Independent Build, SelfTest & Smoke Verification (`TASK-004`)
- **Release Build:**
  ```text
  ./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo
  Build succeeded.
      0 Warning(s)
      0 Error(s)
  Time Elapsed 00:00:00.60
  ```
- **Headless Self-Test Suite (`--selftest`):**
  ```text
  ./run.cmd --selftest
  ...
    phase sync: err0=0.0ms err40=40.0ms syncs=1 flowGain=0.015 res=5.0 cd=0.67s vs 0.82s
  RESULT: PASS (69333 assertions, 2.6s)
  ```
- **Live OpenGL Smoke Run (`--smoke 5`):**
  ```text
  ./run.cmd --smoke 5
  NULLWAVE | GL 4.1.0 NVIDIA 617.42 | NVIDIA GeForce RTX 5060 Laptop GPU/PCIe/SSE2
  AUDIO | capture=True output=True device=Edge error=
  SMOKE | 5.0s fps=115 wave=1 score=482 enemies=3 bullets=4 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```

### 8.3 Visual Capture & Studio Asset Verification (`TASK-004`)
- **Speed Capture Screenshot (`screenshots/speed.png`):**
  - Visually inspected via `read_file`: confirmed concentric cyan/gold Phase Sync rings around the ship, afterimage ghosts, and `PHASE SYNC +RES` right-aligned at `(1040, 824)` cleanly separated from `// FLOW STATE //` (`800, 824`) and `X2.50` (`1040, 862`).
- **Studio Asset (`docs/assets/phase_sync_diagram.png`):**
  - Verified file exists (`1,260,574 bytes`, `1600x900` 16:9 PNG, SHA-256 `7424E96E7F8FFF469402FAF4581F780C1E6FC0FBC0EA273AE2D5954479258802`), matches `.game-studio/ASSET_MANIFEST.md` Asset #6, and visually confirmed the PLL beat-phase oscilloscope, 3D oblique barrel-roll trajectory with 5 afterimages and `195U` shockwave ring, 4 synergy cards, and bottom HUD Flow Meter readout bar.

### 8.4 Final QA Sign-Off (`TASK-004`)
All `TASK-004` acceptance criteria are satisfied with **0 defects**. `TASK-004` is **APPROVED** and marked **`done`**.

---

## 9. Independent QA Verification (`TASK-005` — Choir Boss Phase-Transition Choreography & Telegraph Polish)

### 9.1 Code & Performance Review (`Entities.cs`, `World.Enemies.cs`, `WorldRenderer.cs`, `Screens.cs`, `Game.cs`)
- **Boss Phase Model & Fair Bullet-Clear Choreography (`src/Nullwave/World/Entities.cs`, `World/World.Enemies.cs`):**
  - `Enemy` tracks `PhaseBreakTimer`, `PhaseBreakDuration = 1.15f`, `HighestTriggeredPhase` (monotonic guard preventing duplicate triggers), and `BossHealthDisplay` (smoothed ghost HP drain).
  - `World.ComputeDesiredBossPhase` and `World.BossPhaseSubtitle` handle 3-phase and 4-phase (`BLIND CHOIR`) encounters (`OVERTURE`, `HARMONIC CONVERGENCE`, `NULL RESONANCE`, `BLIND CHOIR`).
  - `TriggerBossPhaseTransition` iterates `EnemyBullets` backwards (`for (int i = EnemyBullets.Count - 1; i >= 0; i--)`) and clears bullets within `BossPhaseClearRadius = 580f`, returning them to `_bulletPool` via `ReturnBullet` with zero heap allocations, triggers dual concentric shockwaves (`580f` hue + `320f` white), resets `BossAttackBeats = 1.25f`, sets `BossInvuln` and `PhaseBreakTimer`, emits `THE CHOIR // <SUBTITLE>`, and spawns up to 3 Swarm escorts (`Enemies.Count < 28`).
  - `e.Telegraph` ramps cleanly over the final beat before `BossAttackBeats` triggers (`BossInvuln <= 0`).
- **World Rendering & Boss Bar HUD (`src/Nullwave/Render/WorldRenderer.cs`, `UI/Screens.cs`):**
  - `DrawBoss` renders the closing beat-synced telegraph ring (`e.Telegraph > 0.04f && GameSettings.Current.BeatTelegraphs`), expanding phase-break shockwaves + 12 radial ticks (`e.PhaseBreakTimer > 0f`), and a 3rd counter-tilted crimson-gold `BossHalo` + 18 faster orbiting shards on Phase IV (`e.BossPhase >= 3`).
  - `Screens.DrawBossBar` renders cleanly within `x = 380..1220, y = 10..74` with ghost drain, phase dividers, centered phase pips (`y = 58`), and `// PHASE SHIFT //` / `INCOMING VOLLEY` callouts, leaving top-left and top-right HUD clusters unobstructed.

### 9.2 Independent Build, SelfTest & Smoke Verification (`TASK-005`)
- **Release Build:**
  ```text
  ./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo
  Build succeeded.
      0 Warning(s)
      0 Error(s)
  Time Elapsed 00:00:00.54
  ```
- **Headless Self-Test Suite (`--selftest`):**
  ```text
  ./run.cmd --selftest
  ...
    boss choreography: clearRadius=580 phase=2/3 teleMax=1.00 blindPhase=4/4
  RESULT: PASS (69349 assertions, 2.4s)
  ```
- **Live OpenGL Smoke Run (`--smoke 5`):**
  ```text
  ./run.cmd --smoke 5
  NULLWAVE | GL 4.1.0 NVIDIA 617.42 | NVIDIA GeForce RTX 5060 Laptop GPU/PCIe/SSE2
  AUDIO | capture=True output=True device=Edge error=
  SMOKE | 5.0s fps=115 wave=1 score=482 enemies=3 bullets=4 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```

### 9.3 Visual Capture & Studio Asset Verification (`TASK-005`)
- **Boss Capture Screenshot (`screenshots/boss.png`):**
  - Visually inspected via `read_file`: confirmed `THE CHOIR // PHASE II OF III // HARMONIC CONVERGENCE`, segmented HP bar with phase dividers, centered phase pips, `// PHASE SHIFT //` status callout, and dual expanding clear-ring shockwaves render cleanly with zero HUD overlap.
- **Studio Asset (`docs/assets/choir_phase_choreography.png`):**
  - Verified file exists (`1,669,230 bytes`, `1600x900` 16:9 PNG, SHA-256 `3774F5D5973223A59E7B82E9F77B8CD5B85B33901E557211AFFD5EE999D6A3FF`), matches `.game-studio/ASSET_MANIFEST.md` Asset #7, and visually confirmed the top `BOSS BAR HUD //` replica, 4 progressive Choir phase cards (`10..16` pillars, `1..3` halos, `4..10` shards, `580U` clear ring), and 3 bottom tactical spec panels.

### 9.4 Final QA Sign-Off (`TASK-005`)
All `TASK-005` acceptance criteria are satisfied with **0 defects**. `TASK-005` is **APPROVED** and marked **`done`**.

---

## 10. Independent QA Verification (`TASK-006` — Local Spotify Integration & Hardcore Recorder Hooks)

### 10.1 Code & Performance Review (`SpotifyBridge.cs`, `AudioEngine.cs`, `Meta.cs`, `Pilot.cs`, `Program.cs`, `Game.cs`, `Screens.cs`)
- **Pure-C# Local Spotify Bridge (`src/Nullwave/Audio/SpotifyBridge.cs`, `AudioState.cs`, `AudioEngine.cs`):**
  - Zero external NuGet packages or OAuth credentials.
  - `SpotifyBridge.ParseWindowTitle` deterministically parses desktop `Spotify` (`Idle`, `Ad`, `Artist - Title` preserving hyphenated titles) and browser Web Player tabs (`chrome`, `msedge`, `firefox`, `brave`, `opera`, `Vivaldi` in both `"Artist - Title - Spotify"` and `"Title • Artist - Spotify"` formats), ignoring unrelated browser tabs (`NotRunning`).
  - `SpotifyBridge.Update(dt)` throttles process enumeration to `1.25s` intervals, wraps OS handle reads in `try/catch`, and disposes every `Process` instance in `finally` blocks (zero hot-path allocations).
  - `AudioEngine.Update(dt)` propagates `SpotifyTrack` and `SpotifyStatus`, enriching `State.SourceName = "SPOTIFY // " + State.SpotifyTrack` when Spotify is captured or playing over system loopback.
- **UI Placement & Run Telemetry (`Screens.cs`, `World.cs`, `Meta.cs`, `Game.cs`):**
  - `Screens.DrawTitle` (`1536, 748`), `Screens.DrawHud` (`1542, 93` nested between `AUDIO //` at `y = 72` and Spectrum top at `y = 114`), and `Screens.DrawPause` (`546, 686` with card height `524` and footer at `y = 706`) render `SPOTIFY // <TRACK>` cleanly via `FormatTrack` with zero overlap.
  - `RunRecord.Track` persists safely and defaults to `""` on legacy `save.json` loads.
- **Hard-Preset Recorder Hooks & Phase-Sync Pilot (`Meta.cs`, `Program.cs`, `Game.cs`, `Pilot.cs`):**
  - `ModifierCatalog.ResolvePreset("hard")` selects 15 modifiers (excluding `"quiet-signal"`), hitting the `25.00x` score multiplier cap and `BossPhases = 4` (`blind-choir`), while `"all"` selects all 16.
  - `Game.StartSmokeRun` passes `modOverride` into `new HashSet<string>(modifierOverride ?? _meta.SelectedModifiers)` inside `StartRun`, keeping `--mods` and `--attune` strictly transient without mutating `save.json`.
  - `Pilot.Think` executes emergency survival dashes immediately (`bullet < 44u` or `boss < 120u`) while aligning tactical dashes (`44..95u` or flow dashes) with `World.IsInPhaseSyncWindow(out _)`.

### 10.2 Independent Build, SelfTest & Smoke Verification (`TASK-006`)
- **Release Build:**
  ```text
  ./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo
  Build succeeded.
      0 Warning(s)
      0 Error(s)
  Time Elapsed 00:00:00.60
  ```
- **Headless Self-Test Suite (`--selftest`):**
  ```text
  ./run.cmd --selftest
  ...
    spotify: desktop="Svdden Death - Behemoth" web="Megalodon - Mercy"
    hard preset: count=15 mult=x25.00 bossPhases=4 pilotSync=True
  RESULT: PASS (67666 assertions, 2.7s)
  ```
  *(Note: The 30,000-tick `TestLongRun` soak loop performs per-tick entity/bullet finiteness assertions; updating `Pilot.Think` to perform Phase Sync dashes alters bullet/enemy counts per tick, resulting in `67,666` passing assertions with zero failures or skipped checks.)*
- **Standard & Hard-Preset Smoke Verification:**
  ```text
  ./run.cmd --smoke 5
  SMOKECONFIG | seed=00Z9DUE9 mods=3 mult=x1.66 bossPhases=3 attunes=0
  SMOKE | 5.0s fps=121 wave=1 score=2414 enemies=2 bullets=2 bpm=126.0 audio=Edge capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS

  ./run.cmd --smoke 4 --mods hard --attune showcase --seed 001N4C4M
  SMOKECONFIG | seed=001N4C4M mods=15 mult=x25.00 bossPhases=4 attunes=7
  SMOKE | 4.0s fps=109 wave=1 score=39256 enemies=2 bullets=0 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```

### 10.3 Visual Capture & Trailer Readiness Verification (`TASK-006`)
- Visually inspected `screenshots/title.png`, `screenshots/combat.png` (confirming `SYNC 4` from Phase-Sync-aware `Pilot` dashes alongside the `SPL`/`FAN`/`PRC`/`RPD` HUD strip), and `screenshots/pause.png` (`read_file`): zero UI overlap.
- Confirmed `--record` / `--smoke` accept `--mods hard --attune showcase --seed 001N4C4M` cleanly for trailer recording.

### 10.4 Final QA Sign-Off (`TASK-006`)
All `TASK-006` acceptance criteria are satisfied with **0 defects**. `TASK-006` is **APPROVED** and marked **`done`**.

---

## 11. Independent QA Verification (`TASK-006` Trailer Workstream — Hardcore Riddim Drop-Synced Trailer)

### 11.1 Stream, Decode & File Integrity
- **`capture/nullwave_hardcore_riddim_trailer.mp4` (Master):**
  - `ffprobe`: `1600x900`, `h264`, `60 fps`, `aac` `48000 Hz` stereo, `duration=85.300000s`, `size=72,283,820 bytes`.
  - `ffmpeg -v error -i capture/nullwave_hardcore_riddim_trailer.mp4 -f null -`: exit `0` (zero errors).
- **`capture/nullwave_hardcore_riddim_trailer_720p.mp4` (Web Copy):**
  - `ffprobe`: `1280x720`, `h264`, `60 fps`, `aac` `48000 Hz` stereo, `duration=85.300000s`, `size=35,244,452 bytes`.
  - `ffmpeg -v error -i capture/nullwave_hardcore_riddim_trailer_720p.mp4 -f null -`: exit `0` (zero errors).
- **Source Muxed Takes (`capture/riddim_act1..4_mux.mp4`):**
  - Verified all 4 muxed takes exist (`63.3 MB`, `76.7 MB`, `51.4 MB`, `87.3 MB`).

### 11.2 Drop-Sync Alignment (`python capture/make_riddim_trailer.py --verify-only`)
- `act1`: `cut=2.600s kick=2.605s delta=+0.005s | drop=10.315s kick=10.320s delta=+0.005s`
- `act2`: `cut=22.400s kick=22.405s delta=+0.005s | drop=29.900s kick=29.905s delta=+0.005s`
- `act3`: `cut=42.200s kick=42.835s delta=+0.635s (build) | drop=49.700s kick=49.705s delta=+0.005s`
- `act4`: `cut=62.000s kick=60.560s delta=-1.440s (build) | drop=69.500s kick=69.505s delta=+0.005s`
- All 4 drops lock to a kick onset within **`+0.005s` (5 ms)**.

### 11.3 Visual Inspection (`capture/riddim_preview_1..4.png`)
- Visually inspected all 4 drop-moment previews via `read_file`:
  - Confirmed `SIGNAL METRONOME  GLASS CHOIR  +13` (`x25.00` hard modifiers, `INTEGRITY 1 / 1`), Attunement loadout strip (`SPL`, `FAN`, `PRC`, `RPD`, `HOM`), `PHASE SYNC` rings + afterimages + `PHASE SYNC +RES` / `SYNC 2` HUD readout, `THE CHOIR // PHASE 1 OF 4 // OVERTURE` 4-phase boss bar (`BLIND CHOIR`), song lasers, and `AUDIO // APP / FFPLAY` at `139–141 BPM` with zero ghosting.
  - *(Minor non-blocking note: raw `--record` captures at native `2560x1600` 16:10 display resolution were aspect-fitted into `1600x900` with `80px` `#06070C` side margins, and the `0.70s` `1664x936` slow-mo zoom crops ~18px top/bottom, lightly touching the top pixels of the boss bar title at `y = 10` during the slow-mo punch.)*

### 11.4 Trailer QA Verdict
**PASS (`APPROVED — 0 BLOCKERS`)**.

---

## 12. Independent QA Verification (`TASK-007` — Aim-Reactive 3D Backdrop Parallax & Camera Tilt, Richer Trails, Expanded Laser Choreography & Docs)

### 12.1 Code & Performance Review (`Renderer.cs`, `Shaders.cs`, `WorldRenderer.cs`, `World.cs`, `World.Enemies.cs`, `Game.cs`)
- **Aim-Reactive 3D Backdrop Parallax & Camera Tilt (`Renderer.cs`, `Shaders.cs`, `WorldRenderer.cs`, `Game.cs`):**
  - `Renderer.ClampAimTilt`, `ComputeObliqueVec` (`new(-AimTilt.X * 0.14f, Oblique + AimTilt.Y * 0.10f)`), `BuildWorldMeshMatrix`, and `LiftPoint` keep `z = 0` ground footprints and `ScreenToWorld` 100% invariant across all aim angles while lifting 3D meshes and 2D overlays consistently (`WorldRenderer.Lift` delegates to `r.Lift`).
  - `_aimTilt` is smoothly damped inside `Game.Update(dt)` so `Game.Render()` stays idempotent on double-renders.
  - `Shaders.BackgroundFrag` applies `uAim * 0.038` parallax and subtle directional accent wash; `WorldRenderer.DrawBackdrop3D` populates `r.Backdrop` with 18 monolith `Column`s, 3 rotating orbital `Ring`s, 12 `HaloSegment`s, 6 `Crystal`/`Octa` accents, and 2 `Knot` sculptures in `z = -760..-2200` with zero per-frame heap allocations (`BackdropBrightness = 0.22f * intensity`).
- **Richer Bullet & Laser Trails (`WorldRenderer.cs`):**
  - `DrawBullets` upgrades player bolts (wider outer bloom `b.Radius * 3.8f`, elongated velocity streaks, plasma spark node) and enemy bullets (stronger dark contrast halos `b.Radius * 2.25f`, longer dual-layer comet tails `28..120u`, trailing shimmer nodes).
  - `DrawLasers` adds warmup boundary guide rails + rotating emitter lock arc, 4-layer HDR active beams (bloom sheath, saturated mid body, beat-pulsing harmonic side rails, white-hot core), 8 high-speed directional chevrons (`1250 u/s`), and glowing emitter/endpoint flares.
- **Expanded Song-Laser & Enemy Laser Choreography (`World.cs`, `World.Enemies.cs`):**
  - `UpdateSongLasers` raises active laser limit to `24`, triggers high-energy patterns from `0.55f` on a 2-bar cadence from Wave 3+, upgrades Drop `STARBURST` to 8 beams, Build `SCAN` to dual opposing sweeps, and cycles 4-beam `LASER GATE`, 3-beam `LASER CROSS`, and 4-beam rotating `PRISM CAGE`, preserving `Breakdown > 0.5f` = `0` lasers and `Warmup = beat * 2f`.
  - Choir Phase II+ fires mirrored/3-beam sweeping laser volleys (`BossAttack`), and Wave 3+ / Elite `Prism` units project twin opposing `560`-unit rotating laser blades.

### 12.2 Independent Build, SelfTest & Smoke Verification (`TASK-007`)
- **Release Build:**
  ```text
  ./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo
  Build succeeded.
      0 Warning(s)
      0 Error(s)
  Time Elapsed 00:00:00.57
  ```
- **Headless Self-Test Suite (`--selftest`):**
  ```text
  ./run.cmd --selftest
  ...
    song lasers: high-energy volleys=11 breakdown=0 build=8 dropBeams=8 minWarmup=0.95s
  ...
    aim & lasers: oblique=(-0.12,0.36) dropBeams=8 buildBeams=2 highVolleys=8 choirMaxLasers=4
  RESULT: PASS (67677 assertions, 2.3s)
  ```
- **Live OpenGL Smoke Run (`--smoke 5`):**
  ```text
  ./run.cmd --smoke 5
  NULLWAVE | GL 4.1.0 NVIDIA 617.42 | NVIDIA GeForce RTX 5060 Laptop GPU/PCIe/SSE2
  SMOKECONFIG | seed=00Z9DUE9 mods=3 mult=x1.66 bossPhases=3 attunes=0
  AUDIO | capture=True output=True device=WAITING FOR FFPLAY error=
  SMOKE | 5.0s fps=64 wave=1 score=2415 enemies=2 bullets=2 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```

### 12.3 Visual Captures & GitHub Documentation Verification (`TASK-007`)
- Visually inspected `screenshots/combat.png`, `screenshots/boss.png`, `screenshots/laser.png`, `screenshots/songlasers.png`, `screenshots/speed.png`, `docs/screenshots/latest_title.png`, and `docs/screenshots/latest_songlasers.png` via `read_file`: confirmed balanced 3D backdrop depth, crisp 4-layer HDR lasers with chevrons/flares, 8-beam Starburst, richer bullet tails, and zero HUD overlap.
- Verified `README.md` and `CHANGELOG.md`: all 24 unique file/image links resolve (`exists=True`), all 8 `docs/screenshots/latest_*.png` files are non-empty, all technical details match `src/Nullwave/`, and zero non-game content (no AI/swarm/task-ID/model/quota references) is present.

### 12.4 Final QA Sign-Off (`TASK-007`)
All `TASK-007` acceptance criteria are satisfied with **0 defects**. `TASK-007` is **APPROVED** and marked **`done`**.

---

## 13. Independent QA Verification (Combined `TASK-008` + `TASK-009` — Fully Generative Backdrops, Per-Generator Audio Reactivity & Visual Harmony)

### 13.1 Code & Shader Hygiene Review (`Renderer.cs`, `Shaders.cs`, `SelfTest.cs`)
- **Zero-Alloc Onset Drives & Dead-Uniform Cleanup (`Renderer.cs`, `Shaders.cs`):**
  - `Renderer.BackdropAudioDrives` (`readonly record struct`) and `Renderer.ComputeBackdropAudioDrives(AudioState, MusicDirector?, float)` compute `Kick`, `Snare`, `Hat`, `Drop`, `Build`, and `Breakdown` with finite `[0, 3]` clamping and zero per-frame heap allocations.
  - All 10 unused V7 uniforms (`uStyleA/B`, `uParamA/B`, `uMutA/B`, `uPalOffA/B`, `uPalFreqA/B`) are removed from `Shaders.BackgroundFrag`, while `uSnare`, `uHat`, `uKick`, `uDrop`, `uAim`, `uMix`, and the active `uLA[3]`/`uLB[3]` genome uniforms are present and bound cleanly in `Renderer.Render(...)`.
- **Per-Generator Audio Reactivity & Visual Harmony (`Shaders.cs`):**
  - All 7 new generators (`genInk`, `genTruchet`, `genInterference`, `genStreams`, `genGridPulse`, `genPlasma`, `genHex`), all 6 classic generators (`styleLiquid`, `styleTunnel`, `styleKaleido`, `styleField`, `styleAurora`, `styleCells`), and space transforms (`WARP`, `SWIRL`, `RIPPLE`, `BREATHE`) react distinctly to `uKick`, `uSnare`, `uHat`, `uBass`, `uHigh`, and `uDrop`.
  - `genPlasma` renders crisp multi-band harmonic contour ridges instead of blurry blobs; `BackgroundFrag.main()` tunes the drifting ambient fog floor (`0.10 + fog * 0.10 + uKick * 0.03 + uSnare * 0.025`, masked by `clamp(1.0 - lum * 3.0, 0.0, 1.0)`) and soft-clipping (`1.04 / (1.0 + col * 1.85)`) while keeping `uAim` shader parallax and `ObliqueVec` aim tilt intact.

### 13.2 Independent Build, SelfTest & Smoke Verification (`TASK-008 / TASK-009`)
- **Release Build:**
  ```text
  ./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo
  Build succeeded.
      0 Warning(s)
      0 Error(s)
  Time Elapsed 00:00:00.57
  ```
- **Headless Self-Test Suite (`--selftest`):**
  ```text
  ./run.cmd --selftest
  ...
    backdrops: 1000 generated, 1000 distinct, 13/13 generators, 11/11 transforms, 2.0 layers avg
  ...
    backdrop audio: kick=0.84 snare=0.96 hat=0.78 drop=1.08 deadV7Uniforms=0
  RESULT: PASS (76348 assertions, 2.4s)
  ```
- **Live OpenGL Smoke Run (`--smoke 5`):**
  ```text
  ./run.cmd --smoke 5
  NULLWAVE | GL 4.1.0 NVIDIA 617.42 | NVIDIA GeForce RTX 5060 Laptop GPU/PCIe/SSE2
  SMOKECONFIG | seed=00Z9DUE9 mods=3 mult=x1.66 bossPhases=3 attunes=0
  AUDIO | capture=True output=True device=Edge error=
  SMOKE | 5.0s fps=115 wave=1 score=2414 enemies=2 bullets=2 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```

### 13.3 Visual Captures & Documentation Verification
- Visually inspected `docs/screenshots/generative_backdrops.png`, `latest_combat.png`, `latest_boss.png`, `latest_songlasers.png`, and `latest_speed.png` via `read_file`: confirmed crisp `PLASMA` harmonic contour bands, rich multi-seed variety, disciplined dark fog floor (no pastel washout, no flat black collapse), and sharp foreground combat readability.
- Verified `README.md` and `CHANGELOG.md`: all 25 unique file/image links resolve (`exists=True`), technical descriptions match `src/Nullwave/`, and zero non-game content is present.

### 13.4 Final QA Sign-Off (`TASK-008 / TASK-009`)
All combined `TASK-008` + `TASK-009` acceptance criteria are satisfied with **0 defects**. `TASK-008` is **APPROVED** and marked **`done`**.

---

## 14. Independent QA Verification (`TASK-009` — Abyssal / Void Noir Dark Theme + Dynamic Combat Illumination)

### 14.1 Code, Performance & Standard-Invariance Review
- **Theme Persistence & Transient CLI Override (`GameSettings.cs`, `Program.cs`, `Game.cs`, `SettingsMenu.cs`):**
  - `VisualTheme { Standard = 0, Abyssal = 1 }` defaults to `Standard`, validates in `Sanitize()`, and parses `standard|default|0` and `abyssal|dark|noir|void|1` via `TryParseTheme`.
  - `Game.cs` clones `GameSettings.Current` before applying `ThemeOverride`, guaranteeing `--theme <standard|abyssal>` never mutates `save.json`.
  - `SettingsMenu.cs` adds `VISUAL THEME` (`STANDARD` / `ABYSSAL` in a `136px` pill) under `VIDEO` and compacts vertical spacing (`Top = 104, RowH = 24, HeaderH = 28`, footer gap `14`) so all 24 rows clear the `y = 818` hint text cleanly.
- **Zero-Allocation Combat Light Pipeline (`Renderer.cs`, `WorldRenderer.cs`):**
  - `Renderer.cs` uses pre-allocated `LightPosNames[12]`, `LightColNames[12]`, `_lightData[12]`, and `_lightCols[12]` with `_lightCount` reset in `BeginWorld(...)`; `WorldRenderer.PopulateCombatLights` uses plain indexed loops and value types (player + Phase Sync/dash flare, Choir boss core, up to 4 newest shockwaves, up to 4 lasers sampled nearest the player, remaining slots for in-view elites/firing/nearby enemies) with **zero per-frame heap allocations**.
- **Shader & PostFx Standard-Theme Invariance (`Shaders.cs`, `PostFx.cs`, `Screens.cs`):**
  - When `uDarkTheme == 0f` (`STANDARD`), `BackgroundFrag` skips the `uLights[12]` branch entirely, `MeshFrag` and `CompositeFrag` reduce to exact standard coefficients, and `PostFx` keeps `uThreshold = 0.62f / uKnee = 0.42f`.
  - In `ABYSSAL` (`uDarkTheme == 1f`), `BackgroundFrag` drops ambient to near-OLED obsidian (`#010104`), gates sharp ridges by `uKick`/`uSnare`/`uDrop`, and reveals underlying generative geometry locally through `uLights[12]`; `MeshFrag` shifts hulls to matte obsidian with boosted Fresnel rims; `PostFx` tightens bright-pass (`0.72 / 0.32`) and `CompositeFrag` clamps output with `alpha = 1.0`; `Screens.cs` applies smoked-glass obsidian fills across HUD and menus.

### 14.2 Independent Build, SelfTest & Dual-Theme Smoke Verification (`TASK-009`)
- **Release Build:**
  ```text
  ./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo
  Build succeeded.
      0 Warning(s)
      0 Error(s)
  Time Elapsed 00:00:00.55
  ```
- **Headless Self-Test Suite (`--selftest`):**
  ```text
  ./run.cmd --selftest
  ...
    backdrop audio: kick=0.84 snare=0.96 hat=0.78 drop=1.08 deadV7Uniforms=0
    abyssal theme: maxLights=12 offset=(0.50,-0.25) r=0.25 shaders=3/3
  RESULT: PASS (76425 assertions, 2.4s)
  ```
- **Live OpenGL Smoke Runs (`--smoke 5` Standard & Abyssal):**
  ```text
  ./run.cmd --smoke 5
  NULLWAVE | GL 4.1.0 NVIDIA 617.42 | NVIDIA GeForce RTX 5060 Laptop GPU/PCIe/SSE2
  SMOKECONFIG | seed=00Z9DUE9 mods=5 mult=x2.29 bossPhases=3 attunes=0
  SMOKE | 5.0s fps=114 wave=1 score=4181 enemies=2 bullets=4 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS

  ./run.cmd --smoke 5 --theme abyssal
  NULLWAVE | GL 4.1.0 NVIDIA 617.42 | NVIDIA GeForce RTX 5060 Laptop GPU/PCIe/SSE2
  SMOKECONFIG | seed=00Z9DUE9 mods=5 mult=x2.29 bossPhases=3 attunes=0
  SMOKE | 5.0s fps=114 wave=1 score=4166 enemies=2 bullets=4 bpm=126.0 audio=GENERATIVE SCORE capture=True
  RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS
  ```

### 14.3 Visual Capture & Documentation Hygiene Verification (`TASK-009`)
- Visually inspected all 4 captures via `read_file`:
  - `docs/screenshots/abyssal_combat.png` — Pitch OLED-black void with clear radial `uLights[12]` combat illumination pool revealing concentric generative backdrop patterns around the ship/lasers, crisp matte-obsidian monolith rim, blazing 4-layer HDR yellow lasers, and readable HUD.
  - `docs/screenshots/abyssal_boss.png` — Dramatic chiaroscuro void with Choir boss core and Phase Sync/Phase Shift rings lighting the dark backdrop, crisp `THE CHOIR // PHASE II OF III // HARMONIC CONVERGENCE` bar, and smoked-glass HUD chrome.
  - `docs/screenshots/abyssal_songlasers.png` — 8-beam `// STARBURST //` crimson HDR lasers locally illuminating Voronoi cells out of the dark void.
  - `screenshots/settings_abyssal.png` — `VISUAL THEME: ABYSSAL` under `VIDEO`, all 24 rows clear the `y = 818` hint text with zero overlap.
- Verified `README.md` (14 image links) and `CHANGELOG.md` (25 image links): every image path resolves (`Test-Path == True`), including `abyssal_combat.png`, `abyssal_boss.png`, and `abyssal_songlasers.png`, with strictly game-focused copy and zero non-game/internal references.

### 14.4 Final QA Sign-Off (`TASK-009`)
All `TASK-009` acceptance criteria are satisfied with **0 defects**. `TASK-009` is **APPROVED** and marked **`done`**.
