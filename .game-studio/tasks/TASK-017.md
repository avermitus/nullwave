# TASK-017: Music Understanding (first-beat sections)

- **Status:** `review` (implemented by Claude Code; awaiting owner playtest)
- **Owner (Engineering):** Claude (Claude Code session)

## 1. Owner feedback
1. Analyse the music beyond the 16-band spectrum (phase 1 of the plan: drums vs. melody, harmony, structure, timbre; phase 2 = AI stems + neural beats, TASK-018).
2. "Galaga arrivals feel really bad: any time I fly away they all gather in one corner trying to get to me. Make the enemies fight to stay on screen; each enemy should have its own type of movement."

## 2. Music analysis (`Audio/MusicAnalyzer.cs`)
- **HPSS** on 72 log-frequency bins of the 2048-pt spectrum: harmonic = 9-frame time median, percussive = 7-bin frequency median, soft (Wiener) masks; per-register (low < 250 Hz < mid < 2 kHz < high) energies in dB measured against their own running mean (drums ~0.5 s, melody ~3 s) → `PercLow/Mid/High`, `HarmLow/Mid/High` (0.35 = usual level), `Percussive` ratio.
- **Chroma** from a separate 8192-pt FFT (100 Hz–2.2 kHz, semitone-weighted bins) → 24 triad templates (`ChordRoot`, `ChordMinor`, `ChordConfidence`, `ChordChanged` after 0.18 s stable) and Krumhansl–Schmuckler key on a ~12 s chroma memory (`Key`, first guess after 6 s, change only after 20 s heard, 6 s stable, r > 0.65, margin 0.05).
- **Timbre:** spectral centroid → `Brightness`; 8 DCT coefficients of 16 log bands, short (0.5 s) vs. long (4 s) average distance → `TimbreNovelty`, `TextureChanged` (> 2.4× running mean, 4 s cooldown).
- **Structure:** one 20-d vector per beat (chroma + timbre). Boundary when the last 4 beats differ from the 8 before by > mean + 2.5σ (and ≥ 16 beats since the last); labelled 4 beats later by sliding 4-beat cosine match against every earlier section (repeat if > 0.97; measured new 0.885 vs repeat 0.991). `SectionChanged/Label/Repeat/Count`; `SongEpoch` bumps on source/track change.
- Engine runs it on every path (local track & buffered Spotify on the delayed playback window, live capture on the ring, built-in score on its playback). ~0.53 ms/frame.

## 3. Music → visuals
- Shader: `uPerc`, `uHarm` (registers), `uChordShift` (palette phase; `Renderer.ChordShiftFor` = circle-of-fifths distance from the key's root × 0.32/12, +0.05 minor; smoothed 3.5/s), `uBright`. Sea swells per register (bass notes / melody / hats), crust magma flares on drums, dune contours on snares, gyroid walls on bass notes, nebula lightning on snares, aurora depths on melody per register, exposure on kick drums, flow clock on melody.
- Game: `SectionChanged` → `Renderer.ShowSection` (`SectionWorldMemory`: new section = new world, repeat = its old world); `KeyChanged` → new world (unless a section changed it < 8 s ago); `TextureChanged` → world shockwave; drop rerolls and the 22 s title reroll only until the song's structure is known.

## 4. Enemy movement — reverted
The screen-holding movement (and TASK-015's Galaga arrivals) was reverted on owner feedback ("makes it impossible to play"): `World.Enemies.cs` and `World.Collisions.cs` are back to the committed TASK-014 state; the `Entry*`/`Move*` fields, `World.ViewHalf/OnScreen`, arrival drawing, `--capture arrival` and their tests were removed.

## 4b. First-beat section detection (owner: "sections are recognised ~3 s late")
- Per-beat features are raw (unsmoothed) chroma + timbre + loudness + drum share.
- Novelty = 1 − max cosine to any of the previous 16 beats (nearest twin), tested on every beat (3σ on bar starts, 5σ elsewhere) with warm-up-free, outlier-capped statistics. Backup: last 4 vs. previous 8 beats on bar starts (labelled directly from 4 beats).
- Beat grid re-aligned to the kick (16-bin histogram of the percussive low band over beat phase).
- Provisional label from the single beat (repeat if > 0.975), confirmed 4 beats later with the nearest-twin test (one-off fills are taken back) and a 4-beat label re-check; both reported as `SectionCorrected`.
- Buffered sources: a second analyzer runs on the newest buffered audio (`CopyAheadWindow`); each section start is pinned to its audio frame and fired when playback reaches it (+ 0.11 s output latency).
- Measured (local-track run of the synthetic song): section start heard at 12.11 s, world change at 12.12 s (another run 12.53 s); returning section within one beat. Live system audio: about one beat late by construction.

## 5. Verification
- Build 0 warnings / 0 errors; `--selftest` PASS (74,406 assertions). New: `TestMusicAnalysis` (synthetic song: chords 100 %, key C major, kick drum part 4.0× vs. gaps, hats 1.7× on/off-beat, sections located on their exact first beats 24 and 48 and reported when that beat ends, returning A recognised as repeat, a one-beat fill taken back), `TestMusicToWorlds`.
- `--musicprobe` on the owner's live Spotify (read-only system capture): kick-drum part swings 0.15–1.0 each second, melody 0.2–0.9, chords tracked (G–C–Am–E), texture change and a section change detected, song memory reset on track change, no false key changes after tuning.

## 6. Not verified / next
- Owner playtest. Section repeat recognition has only been proven on the synthetic song; real songs vary.
- TASK-018 (phase 2): AI stems (Demucs-class ONNX on GPU via DirectML) and a neural beat tracker, buffered sources only, with automatic fallback to this analysis.
