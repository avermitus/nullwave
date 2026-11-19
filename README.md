# NULLWAVE

A native Windows twin-stick bullet hell in pure C# (OpenGL via Silk.NET, audio via NAudio, no game engine, no external model assets) that listens to the music on your PC — the whole system mix or a single app you pick — and builds the fight, the visuals, and the pacing from it.

![NULLWAVE Combat](docs/screenshots/latest_combat.png)

## Features

- **Music-Driven Combat & Laser Choreography:** Real-time log-spectral-flux beat tracking and song-structure analysis (`MusicDirector`) detect tempo, downbeats, builds, breakdowns, and drops — locking enemy volleys to the beat, surging projectile motion, triggering drop shockwaves, and firing 4-layer HDR **Song Lasers** (`STARBURST`, `SCAN`, `LASER GATE`, `LASER CROSS`, `PRISM CAGE`) with travelling directional chevrons.
- **Per-App Loopback & Local Spotify Now-Playing:** Captures system mix or isolates a single application's audio session via Windows WASAPI process loopback (`F3` in game or `--app <name>`), automatically detects live Spotify desktop/Web Player `Artist - Title` playback, and falls back to a built-in generative synth score when silent.
- **Fully Generative Animated Backdrops, Abyssal Theme & Aim-Reactive 3D Tilt:** Lit procedural 3D vector-mesh ships, 11 enemy roles, extruded monoliths, and **THE CHOIR** boss with subtle aim-reactive oblique tilt rendered over a combinatorial GPU backdrop synthesizer (**1–3 layers × 11 motion/space transforms × 13 audio-reactive generators** driven by kick, snare, hi-hat, bass, and drop transients), plus an optional **ABYSSAL / VOID NOIR** dark theme in **SETTINGS** (`--theme abyssal`) with OLED-black contrast and dynamic combat light pools around ships, bosses, shockwaves, and lasers.
- **Racer Flow & Phase Sync:** Momentum-based interceptor handling with **FLOW** multipliers (`// FLOW STATE //`) and **Phase Sync** — dashing within `±65 ms` of a beat grants bonus Flow, Resonance, faster dash recharge, and score without penalizing off-beat survival dodges.
- **Endless Replayability & Flight Recorder:** 12 stackable **Attunements** with a live HUD/Pause loadout strip, 16 unlockable **Archive** modifiers, Daily Signals, **NEW BEST** personal-best callouts, a rolling 8-run flight recorder, and interactive 8-character **Seed Replay / Custom Seed Entry**.

## Controls

| Action | Keyboard + Mouse | Gamepad |
|---|---|---|
| **Move** | `W A S D` / Arrow Keys | Left Stick / D-Pad |
| **Aim** | Mouse Cursor | Right Stick |
| **Fire** | `Left Click` | `RT` |
| **Dash** *(on-beat = Phase Sync)* | `Space` | `A` |
| **Focus** *(precision slow)* | `Shift` | `LB` |
| **Resonance Cascade** | `E` | `X` |
| **Pause / Back** | `P` / `Esc` | `Start` / `B` |
| **Diagnostics / Fullscreen / Audio Source** | `F1` / `F2` / `F3` | — |

## Build and run

Requires Windows 10 (build 20348+) or Windows 11 and the .NET 8 SDK.

```powershell
# either use a system-wide .NET 8 SDK, or install a private copy into .dotnet/
powershell -ExecutionPolicy Bypass -File tools/dotnet-install.ps1 -Channel 8.0 -InstallDir .dotnet

./run.cmd                 # play
./run.cmd --theme abyssal # launch in Abyssal / Void Noir dark theme
./run.cmd --app spotify   # drive the game from one app's audio
./run.cmd --list-apps     # list active audio sessions
./run.cmd --selftest      # headless assertion suite
./run.cmd --beatbench     # beat tracker accuracy on synthetic songs
./run.cmd --sfxdump       # render every sound effect to capture/sfx/*.wav

# optional smoke / video capture overrides
./run.cmd --smoke 5 --mods hard --attune showcase --seed 001N4C4M
./run.cmd --record 30 --wave 4 --mods hard --attune showcase --app spotify --theme abyssal
```

`run.cmd` expects the SDK at `.dotnet/dotnet.exe`; with a system SDK use `dotnet run --project src/Nullwave/Nullwave.csproj -c Release -- <args>`.

## Screenshots

![Generative Animated Backdrops](docs/screenshots/generative_backdrops.png)

| Abyssal Dark Theme — Dynamic Lighting | Abyssal Dark Theme — Choir Boss |
|---|---|
| ![Abyssal Combat](docs/screenshots/abyssal_combat.png) | ![Abyssal Boss](docs/screenshots/abyssal_boss.png) |
| **Abyssal Dark Theme — Starburst Lasers** | **Title Screen** |
| ![Abyssal Song Lasers](docs/screenshots/abyssal_songlasers.png) | ![Title](docs/screenshots/latest_title.png) |
| **Standard Theme — Combat & Loadout** | **HDR Song Lasers & Starburst** |
| ![Combat](docs/screenshots/latest_combat.png) | ![Song Lasers](docs/screenshots/latest_songlasers.png) |
| **Phase Sync & Flow State** | **Archive Seed Decoder & History** |
| ![Speed](docs/screenshots/latest_speed.png) | ![Archive](docs/screenshots/latest_archive.png) |
| **Signal Held Pause Overlay** | **Signal Lost & Personal Bests** |
| ![Pause](docs/screenshots/latest_pause.png) | ![Game Over](docs/screenshots/latest_gameover.png) |
| **Full Enemy Roster** | **Starburst Laser Choreography** |
| ![Roster](docs/screenshots/v9_roster.png) | ![Starburst](docs/screenshots/v9_starburst.png) |

## Documentation

- [CHANGELOG.md](CHANGELOG.md) — Complete version history from V1 through the latest updates with visual comparisons.
- [HANDOFF.md](HANDOFF.md) — Engineering architecture, audio/rendering pipeline details, recorder usage, and technical notes.
