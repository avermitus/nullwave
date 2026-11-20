# TASK-016: Organic Music-Shaped Worlds, Spotify 0.75 s Buffer & Combat Readability

- **Status:** `review` (implemented by Claude Code; awaiting owner playtest)
- **Owner (Engineering):** Claude (Claude Code session)

## 1. Owner feedback
1. Backdrops with many 3D items evenly spaced from each other (prism city, cube swarm, ring engines) did not look good.
2. The background should react to and mimic the music closely.
3. Add a buffer to Spotify like the local-track lookahead.
4. Hard to see the ship, bullets and enemies against the background.

## 2. What changed
- **Worlds (`Shaders.BackgroundFrag`, `BackdropGenome`):** grid worlds removed. Six organic worlds: SPECTRAL SEA (`seaH`: 8 golden-angle swell trains, one per spectrum slice), MOLTEN CRUST (`crust`: domain-warped fbm plates, fissures each tied to a band), SOUND DUNES (`dunesH`: ridged octaves scaled by bass/low-mid/mid/high), GYROID CAVERN (noise-warped so it never repeats), STORM NEBULA (`renderNebula`: 30-step volumetric, depth = frequency, snare lightning), AURORA ABYSS (`renderAurora`: 7 depth sheets along noise contours, depth = frequency). Atmospheres: CLEAR, MOTES, FOG BANKS, EMBERS (scan grid removed).
- **Music drive:** `Renderer.ComputeBackdropSpectrum` folds the 48 analyser bars into 16 bands = 0.55·level + 2.2·(level − own 0.8 s average), instant attack, 9/s release; uploaded as `uSpec[16]` and scaled by Music FX × hype. Shader globals SB/SM/SH = lows/mids/highs. Exposure = 0.75 + 0.45·energy + hit pumps.
- **Readability:** `Renderer.AddBackdropFocus` → `uFocus[16]` dark pools (ship first, radius 260, 0.75; on-screen enemies R·4, 0.55; boss R·5, 0.7); calm shoulder `c·0.95/(1+1.25c)` + 18 % desaturation; floor plate alpha 0.28 → 0.40; ship hull 0.16 → 0.42 grey-blue with dark backing disc and rim ring; enemy bullet halo r·2.25/0.58 → r·2.8/0.72; enemies get a dark disc.
- **Spotify buffer (`Audio/AppAudioRouter.cs`, `LiveStreamSource.cs`, `AudioEngine.cs`):** findings — muting or zeroing an app's Windows session also silences per-app loopback (`--muteprobe`: normal 0.02 / muted 0 / vol0 0), so it cannot be muted and replayed. Instead Spotify is routed (per-app default endpoint via `Windows.Media.Internal.AudioPolicyConfig`, IID ab3d4648…, vtable slots 25/26) to an active silent endpoint (Steam Streaming Speakers / VB-CABLE / Voicemeeter / dummy), still captured by process loopback at full level (`--muteprobe` routed capture 0.50), pushed into a 60 ms-cushion FIFO, and played through `LookaheadAudioBuffer` (0.75 s) with `LiveMode` gain 1. `UpdateBuffered` analyses the playback window like a local track; if Spotify goes silent for 2.2 s the score takes over until audio returns. Routing is cleared on source switch / setting off / Dispose / ProcessExit, and `RecoverFromCrash` clears it at the next start from `%LOCALAPPDATA%/Nullwave/routed_app.txt`. Setting: `GameSettings.BufferSpotify` (default on).

## 3. Verification
- Build 0 warnings / 0 errors; `--selftest` PASS (73,991 assertions) incl. `TestMusicDrivenBackdrop` (hat burst moves treble band 0.31 → 1.20, bass unchanged) and `TestLiveStreamBuffer`.
- `--buffertest` PASS: ffplay stand-in routed to Steam Streaming Speakers, 457,920 frames captured, 0 underruns, replay delay 0.87 s (0.75 s + 60 ms cushion), 120 BPM detected, route restored to default.
- `--routeprobe spotify`: route set and restored on the real Spotify (it was not playing, so capture of real Spotify audio is unverified).
- Captures: 12-world sheet `docs/screenshots/music_worlds.png`, 6-world motion grid, combat/arrival/speed reels. GPU (`NULLWAVE_BENCH=1`, 1600×900): sea 4.0, crust 4.8, dunes 4.7, gyroid 5.2, nebula 5.0, aurora 2.7 ms/frame.

## 4. Known limits
- Spotify buffering needs an active silent output device; without one it stays live and the settings note says why.
- Uses an undocumented Windows interface; a future Windows update could change its layout (calls fail soft).
