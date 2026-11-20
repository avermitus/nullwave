# TASK-019: Bass Gravity Wells, Melody Rails & Hunting Enemy Movement

- **Status:** `review` (implemented by Claude Code; awaiting owner playtest)
- **Owner (Engineering):** Claude (Claude Code session)

## 1. Owner feedback
"Undo Galaga movement, makes it impossible to play" (done in TASK-017), then: "Bring back each enemy having a different movement, and make sure the enemies are always trying to get to you. Enemies are also too slow. Options 2, 3 sound good" (Bass Gravity Wells, Melody Rails).

## 2. Enemy movement (`World/World.Enemies.cs`)
- `UpdateEnemyBrain` rewritten: every role pursues the player in its own style (see CHANGELOG). `EnemyBaseSpeed` = 175 (old base 100). `ChaseBoost(dist)` = 1 + clamp((dist − 300)/300, 0, 3.4). Separation push between enemies. `Enemy.MovePhase` gives each enemy its own handedness/offset.
- Verified (`TestEnemiesChase`): fleeing at top speed for 3 s from all 11 roles, average distance 641 u, worst 946 u (Mortar).

## 3. Bass Gravity Wells (`World/World.MusicFields.cs`)
- Spawn: `ChordChanged` or 1.2 s of held bass (`HarmLow` > 0.5), 2.5 s cooldown, max 3, 380–600 u ahead of the ship, ≥ 520 u apart. Strength follows `HarmLow`; dies after 10 s or 1.5 s without bass.
- `WellPull`: a = min(2400, 4e7·s/(d²+90²))·(1 − d/R); enemy bullets ×1, player bullets ×0.45, ship ×0.22. Core (26 + 22·s) swallows enemy bullets.
- Slingshot: skimming the ring (0.25–0.85 R, radial fraction < 0.55, speed > 300) charges; leaving converts charge > 0.35 into `SlingBoost` (+40 % top speed, decays 0.6/s) with Flow/score.
- `OnMusicDrop` → `CollapseWells`: heavy shockwave, clears bullets within 380 + 260·s, damages enemies (45 + 70·s).
- Verified (`TestGravityWells`): strength 0.92, radius 417, passing bullet bent 32°, core-aimed bullet swallowed, slingshot 0.99, drop collapses and damages.

## 4. Melody Rails
- Melody pitch (`MusicAnalyzer.DetectMelody`): harmonic-product spectrum over 180–1100 Hz on the 8192-pt FFT, parabolic refinement, 50 ms note hold → `MelodyPitch`, `MelodyConfidence`, `MelodyNoteChanged`. `TestMelodyPitch`: 98 %.
- Rail grows while confidence > 0.32 and `HarmMid` > 0.25: head 820 u/s from 140 u ahead of the ship, turning ±1.7 rad/s by (pitch − running centre)/6, steered back from the arena edge, a point every 34 u coloured by the note; stops after 0.4 s of no melody or 4,200 u; points live 5.5 s; max 2 rails.
- Riding: within 48 u of a segment and moving along it (|cos| > 0.55, speed > 150) → `RailBoost` (+45 % top speed), Flow. Enemy bullets within 30 u of a rail point get `Tuned` + the note's hue (spatial hash, 64 u cells) and pass through a riding ship.
- Verified (`TestMelodyRails`): 41 points, 70° turn when the tune climbs, ride speed 792 (top 560), tuned bullet passes a rider, untuned one hits.

## 5. Other
- `SynthVisual` fakes chords (I–vi–IV–V), bass, drums and an on/off melody motif so captures and tests exercise the features without audio.
- Smoke run 120 fps. Self-test PASS (73,218 assertions).

## 6. Not verified
- Owner playtest; balance of well/rail frequency on real songs; difficulty of the faster hunting enemies.
