# TASK-020: Setlists, Song Records, Instrument Ships, In-Key Guns, THE DROP, Run Recap, Tutorial, Rumble & the EDM Show

- **Status:** `review` (implemented by Claude Code; awaiting owner playtest)
- **Owner (Engineering):** Claude (Claude Code session)

## 1. Owner request
"What else should we add to the game." → eight suggestions, then: "Set a task list and start all of them, one by one. Then add more crazy, EDM like reactive 3D background, and camera effects".

## 2. Fixes first
- Wave 1 eases in: `World.EarlyWaveEase(wave)` = lerp(0.72, 1, (wave − 1)/4) on enemy speed.
- Gravity wells pull the ship ×0.06 (was ×0.22); bullets unchanged.
- When the built-in score is audible, `SynthVisual`'s stand-in melody/chord/drum features are zeroed so the real analysis drives everything.
- `CheckWaveEnd` only runs while `Phase == Playing`, and `Step` returns right after `UpdateSetlist` if the phase changed. Before this, a stage-clear upgrade pick or a won set was flipped to `Intermission` when the wave emptied in the same step (found by `TestSetlistRuns`).

## 3. Tracks, setlists, records (`World/World.Setlist.cs`, `World/Meta.cs`, `Audio/AudioEngine.cs`)
- `AudioEngine.UpdateTrack`: `TrackIndex` / `TrackTitle` / `TrackIsSong` / `TrackChanged`. Keys: `spotify:<title>`, `local:<file>#<loop>`, else a new chapter every 180 s (`chapter:score|ext#n`).
- `TrackStat` per track (score, kills, hits, duration, `Rank` = S: 0 hits & ≥ 22 kills/min; A: ≤ 1 hit & ≥ 12; B: ≤ 2 hits; C).
- Setlist (`RunConfig.Setlist`, title menu `SETLIST`, length 2–8 in settings): a new `TrackIndex` → `FinishTrack`, `StartTrack`, `AdvanceStage` (stage bonus, field swept, upgrade pick). Final stage: THE DROP spawns on the first drop cue or after 45 s; killing it → `CompleteSet` (victory, `Summary.SetComplete`).
- `MetaProgress.RecordSong`: songs ≥ 30 s only, keyed by upper-cased title, best score/rank/kills, fewest hits, plays; cap 400 (oldest evicted). Toast on a new best.

## 4. Instrument ships (`World/World.Ships.cs`)
- `ShipKind { Classic, Drums, Bass, Lead }`, chosen with Left/Right on the title screen (`MetaProgress.SelectedShip`).
- `ShotStyle` scales the classic volley (so every attunement still applies). Drums: kick slug (2.6× dmg, pierce +1), snare fan of 3, hat needle, eighth-note pulse after 0.6 s without drums. Bass: one slow orb (speed ×0.34, dmg 4.5× bass weight) on `BeatTick` or a bass note, 0.18 s cooldown, bursts (r 135) on hit or at end of life. Lead: 18-segment, 900 u beam bending ±0.9 rad with (pitch − running centre)/7, dps = Damage·16·0.85·power.
- Onsets are read once per audio frame (`audio.Time` changes), since the sim can step several times per frame.

## 5. In-key guns (`World/World.Notes.cs`, `Audio/SoundBank.cs`)
- `ChordTone(root, minor, degree, octave)` near C5 (roots ≥ F# fold down an octave). Kill notes climb an arpeggio with the combo; boss kills play four notes; hits play a soft pluck (0.09 s cooldown).
- `SoundBank.PlayNote(midi, gain, pan, delay, timbre)`: FM bell / pluck; the delay (negative age) quantises each note to the next sixteenth on the beat clock.

## 6. THE DROP (`World/World.SongBoss.cs`)
- Every 10th wave and the setlist finale. Orbits above the ship; kick → ring (12 + 2·phase), snare → aimed fan (5–7), every other hat → needles, beat-grid ring when the audio has no onsets; melody → a laser that tracks the pitch. `SectionChanged` → phase = section label mod 4, 1 s invulnerability, clears nearby bullets.

## 7. Run recap, clips, tutorial, rumble
- `RunTimeline`: a sample every 0.5 s, hits, drops (≥ 4 s apart), track starts, best 6 s window by score gain. Game over shows per-track ranks and the timeline.
- `ClipRecorder`: 426×240 at 20 fps, 6 s ring via a GPU blit; the best moment is kept 3 s after it happens; `C` encodes an MP4 with ffmpeg (or saves a PNG still).
- `Coach`: 6 tutorial steps (MOVE 900 u, 3 kills, 2 dashes, Flow 0.6, a Phase Sync, a Cascade) with Tab to skip; one-time tips for wells, rails, the Choir, THE DROP, Overdrive, stages and ships.
- `Game.RumbleFor`: low motor = kick/bass/hurt/dash, high motor = snare/hats/hurt, scaled by `Rumble` %.

## 8. The EDM show (`Render/Shaders.cs`, `Render/Renderer.cs`, `Render/PostFx.cs`, `Core/Game.cs`)
- `Renderer.EdmFxFor(dropBeats, beatPos, energy, build, breakdown, overdrive, snareRoll, musicFx, camFx, reduceFlashing)` → `EdmFx` (pure, tested).
- Background (`BackgroundFrag`): `stageLasers` (3×3 emitter cells of 9 scene units around the camera, 4-beam fans, lit set and angle change per beat, closest-approach glow, faded out above z = −1.4); kaleidoscope on build-ups (6 segments, turning with the beat, readability pools still on real positions); eye height ×(1 + 0.6·build) on builds, ×(1 − 0.42·e^(−1.1·beats)) on the drop, kick dip; hue step 0.13 per beat for 4 bars; beat-locked pump; tinted pulse on the beat in the drop's first bar.
- Composite: roll (0.05 rad, rocking with cos(π·beats), decaying; snares nudge ±0.01) scaled by the exact edge fit cos r + sin r·max(a, 1/a) so no frame edge shows; translucent letterbox bars (≤ 6 %) with a glowing edge. `Renderer.ScreenToWorld`/`WorldToUi` apply the same transform, so aim stays exact.
- World camera: bounce on every kick (+3.5 %), pull back through a build (−5 %), slam in on the drop (+6 %), all × `CameraFx`.
- Photosensitivity: pulses are tinted, ≤ 4 per drop, < 3 Hz at 174 BPM; `ReduceFlashing` removes pulse and hue steps and halves the rest.
- Capture scenarios `edm` and `edmbackdrop` script a 6 s build-up into a drop (start on the first rendered frame).

## 9. Verification
- `--selftest`: PASS (71,952 assertions, 5.1 s). New suites: `TestSetlistRuns`, `TestSongRecords`, `TestInstrumentShips`, `TestInKeyNotes`, `TestSongBoss`, `TestRunRecap`, `TestRumbleAndCoach`, `TestEdmShow`.
- Bench (`NULLWAVE_BENCH=1 --capture drop|combat`): 2.8–3.0 ms/frame average at 1600×900 (RTX 5060 laptop).
- Reels: `capture/v15/edm.mp4`, `capture/v15/edm_bg.mp4`; contact sheet `docs/screenshots/edm_show.png`.
- Debug build (with the playtest bridge) compiles.

## 10. Not verified
- Owner playtest of everything above: ship feel and balance, THE DROP's difficulty, setlist pacing on real playlists, how the EDM show feels at full CAMERA FX, rumble on a real pad, clip saving with the owner's ffmpeg.
