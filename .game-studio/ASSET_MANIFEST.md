# NULLWAVE — Asset Manifest (`.game-studio/ASSET_MANIFEST.md`)

- **Task:** `TASK-001` through `TASK-005` (Studio Visual Assets, Attunement Matrix Sheet, Seed Archive Console, Phase Sync Diagram & Choir Phase Choreography)
- **Owner:** `@artist` (Art Director / Asset Creator)
- **Date:** `2026-11-18`
- **Status:** `Verified`

---

## 1. Visual Identity & Reference Style Audit

All studio visual assets are anchored to NULLWAVE's procedural 3D vector-mesh and audio-reactive shader pipeline:

- **Palette Source (`src/Nullwave/Render/Palette.cs`):**
  - **Void Navy (`BackA` / Deep Backdrop):** `#030510` / `#080B1E`
  - **Electric Cyan (`Primary`, BaseHue ~0.52):** `#20F0FF`
  - **Hyper Magenta (`Secondary`, +0.46 hue shift):** `#FF38B8`
  - **Warning Crimson (`Danger`, +0.93 hue shift):** `#FF4052`
  - **Solar Gold (`Accent`, +0.11 hue shift):** `#FFD866`
  - **Crisp Ice White (`Text` / Core Highlight):** `#E6F5FF` (`new Vector4(0.90f, 0.96f, 1f, 1f)`)
  - **Dim Slate (`Dim` / Wireframe Grid):** `#70809E` (`new Vector4(0.44f, 0.50f, 0.62f, 1f)`)
- **Geometry & Shader Reference (`src/Nullwave/Render/Models.cs`, `Shaders.cs`, `docs/screenshots/`):**
  - `docs/screenshots/v9_roster.png` — Oblique 3D procedural meshes (`Player` swept-wing interceptor with octahedral canopy and twin engine pods, `Drifter`, `Orbiter`, `Sniper`, `Prism`, `Splitter`, and extruded polygonal `Monolith` obstacles with bevelled crowns and glowing ribs) over domain-warped aurora/nebula waves.
  - `docs/screenshots/v6_drop.png` — Raymarched iridescent liquid metaballs (`BackgroundFrag` `LIQUID` style) with cyan/magenta Fresnel rims.
  - `docs/screenshots/v7_speed.png` — High-velocity `// FLOW STATE //` peripheral speed lines, twin thruster light trails, and ghosted ship afterimages.
  - `docs/screenshots/v9_starburst.png` — Converging `// STARBURST //` downbeat song laser beams with HDR bloom and Voronoi `SHATTER` cells.
- **Engine Import & Ground Rules (`HANDOFF.md` §9):**
  - NULLWAVE's runtime executable is strictly procedural (`Pure C#, no engine, no model assets — keep it that way`).
  - Assets in `docs/assets/` serve as official studio key art, README/documentation banners, and emblem plates. All files are forced opaque (`alpha = 255` across every pixel) matching `Renderer.SaveScreenshot` and the `TASK-001` `BlendMode` backbuffer alpha invariant.
- **Tooling & Provenance Note:**
  - Direct invocation of the `image-generator` subagent from within a native swarm worker returned `WorkflowError: failed to resolve agent "image-generator": subagent "image-generator" not found or not allowed to be invoked` because `invoke_subagent` is scoped to the top-level host session.
  - Following the Game Studio Director fallback protocol, `@artist` built a dedicated C# HDR procedural 3D vector-mesh + domain-warped shader renderer (`AssetGen`) using NULLWAVE's exact `Models.cs` vertex coordinates, oblique projection (`Renderer.Oblique`), `Palette.cs` colors, `PixelFont.cs` glyphs, multi-pass separable Gaussian bloom, and `PngWriter.cs` lossless RGBA output. Both the exact `image-generator` prompts and the procedural shader/mesh specifications are documented below.

---

## 2. Verified Asset Catalog

| # | Verified File Path | Purpose | Dimensions | Aspect | Format / Alpha | File Size | SHA-256 Checksum |
|---|---|---|---|---|---|---|---|
| 1 | `docs/assets/nullwave_key_art.png` | Official Studio Key Art / Title Hero Banner (`TASK-001`) | `1600 x 900` | `16:9` | `PNG` (8-bit RGBA, opaque `A=255`) | `2,584,364 bytes` | `E42CA87C72CFDD78C8D3403ED696C23D396749FDB4C08F92651DE5E59716B5BB` |
| 2 | `docs/assets/choir_boss_emblem.png` | Boss Insignia (`THE CHOIR` — Wave 5/10/15 Boss, `TASK-001`) | `1024 x 1024` | `1:1` | `PNG` (8-bit RGBA, opaque `A=255`) | `1,638,371 bytes` | `1B294ADB98A8FAD0C2C0025E60CB3C3DD33E3B9D26D4A1A62867D499A4363A9A` |
| 3 | `docs/assets/pilot_flow_insignia.png` | Pilot Archive & `// FLOW STATE //` Crest (`TASK-001`) | `1024 x 1024` | `1:1` | `PNG` (8-bit RGBA, opaque `A=255`) | `1,149,649 bytes` | `75F381416C21DE79A86C3E8738AB85D2415EA5D1C82E7E79D9117CEA32F41845` |
| 4 | `docs/assets/attunement_matrix_sheet.png` | Attunement Signal Matrix Reference Sheet (`TASK-002`) | `1600 x 900` | `16:9` | `PNG` (8-bit RGBA, opaque `A=255`) | `1,208,729 bytes` | `B4A0DAFB49B0FD16F12B76AA80B6A7C1C14B6DBC3827340134476B2F3B55B5C3` |
| 5 | `docs/assets/seed_archive_console.png` | Seed Archive & Signal Decoder Console Plate (`TASK-003`) | `1600 x 900` | `16:9` | `PNG` (8-bit RGBA, opaque `A=255`) | `1,488,350 bytes` | `1FD00AB430C317CE34F0BD87F0B0E68B700D0B6B346DB8F6D6C8EBEAD2016C66` |
| 6 | `docs/assets/phase_sync_diagram.png` | Phase Sync Rhythm Synergy Telemetry Plate (`TASK-004`) | `1600 x 900` | `16:9` | `PNG` (8-bit RGBA, opaque `A=255`) | `1,260,574 bytes` | `7424E96E7F8FFF469402FAF4581F780C1E6FC0FBC0EA273AE2D5954479258802` |
| 7 | `docs/assets/choir_phase_choreography.png` | Choir Boss Phase Choreography & Telegraph Matrix (`TASK-005`) | `1600 x 900` | `16:9` | `PNG` (8-bit RGBA, opaque `A=255`) | `1,669,230 bytes` | `3774F5D5973223A59E7B82E9F77B8CD5B85B33901E557211AFFD5EE999D6A3FF` |

---

## 3. Detailed Asset Specifications

### Asset 1: Official Studio Key Art (`docs/assets/nullwave_key_art.png`)
- **Verified Path:** `docs/assets/nullwave_key_art.png`
- **Purpose:** Primary 16:9 hero banner and title key art for NULLWAVE studio documentation and release showcase.
- **Reference Style:** `docs/screenshots/v9_roster.png`, `docs/screenshots/v9_starburst.png`, `docs/screenshots/v6_drop.png`, `src/Nullwave/Render/Models.cs` (`BuildPlayer`, `BuildBossCore`, `BuildBossPillar`, `BuildBossHalo`, `Monolith`).
- **Dimensions & Aspect:** `1600x900` (`16:9` widescreen).
- **Perspective:** Oblique top-down 3D orthographic projection (`Renderer.Oblique` z-lift = `0.45`) with dynamic `0.32 rad` strafe bank on the player interceptor.
- **Transparency:** Opaque (`RGBA`, `alpha = 255` on all `1,440,000` pixels).
- **Palette:** `#030510` (Void Navy), `#20F0FF` (Electric Cyan), `#FF38B8` (Hyper Magenta), `#FF4052` (Warning Crimson), `#FFD866` (Solar Gold), `#E6F5FF` (Ice White).
- **Animation / Engine Import Requirements:** Static 16:9 hero plate (`docs/assets/`); no runtime texture import required.
- **Exact Prompt / Visual Specification:**
  > `"Official 16:9 key art hero banner (1600x900) for 'NULLWAVE', a sleek neon twin-stick audio-reactive vector-mesh bullet hell game. Lower-left: a crystalline ice-white and electric-cyan (#20F0FF) swept-wing interceptor spacecraft with a glowing octahedral canopy, twin hyper-magenta (#FF38B8) thruster trails, and three flow-state afterimages banking at high speed while firing twin neon cyan laser salvos. Upper-right: towering 'THE CHOIR' boss — a faceted crimson/gold polyhedral icosphere core crowned with 20 radial audio-spectrum crystal pillars, concentric neon gold (#FFD866) and magenta torus halos, and orbiting octahedral shards surrounded by enemy squadrons (Drifter, Orbiter, Prism, Sniper, Splitter), extruded polygonal monoliths, and six converging crimson/magenta starburst song laser beams over a deep void navy (#030510) domain-warped FBM liquid metaball nebula. Includes crisp geometric 'NULLWAVE // AUDIO-REACTIVE TWIN-STICK BULLET HELL //' title framing."`

---

### Asset 2: The Choir Boss Emblem (`docs/assets/choir_boss_emblem.png`)
- **Verified Path:** `docs/assets/choir_boss_emblem.png`
- **Purpose:** High-contrast square boss emblem representing **THE CHOIR** (the multi-phase harmonic boss encountered every 5 waves).
- **Reference Style:** `src/Nullwave/Render/Models.cs` (`BuildBossCore`, `BuildBossPillar`, `BuildBossHalo`, `BuildBossShard`) and `src/Nullwave/Render/WorldRenderer.cs` boss crown rendering.
- **Dimensions & Aspect:** `1024x1024` (`1:1` square).
- **Perspective:** Centered radial orthographic emblem with 3D directional Phong + Fresnel rim shading on the central geodesic icosphere and decagonal frustum collar.
- **Transparency:** Opaque (`RGBA`, `alpha = 255` on all `1,048,576` pixels).
- **Palette:** `#030510` (Void Navy), `#FF4052` (Warning Crimson), `#FF38B8` (Hyper Magenta), `#FFD866` (Solar Gold), `#E6F5FF` (Ice White).
- **Animation / Engine Import Requirements:** Static 1:1 emblem crest (`docs/assets/`).
- **Exact Prompt / Visual Specification:**
  > `"High-contrast 1:1 square (1024x1024) vector-mesh boss emblem representing 'THE CHOIR' from NULLWAVE: a sacred-geometry polyhedral icosphere core and decagonal frustum collar surrounded by a radial crown of 24 glowing audio-spectrum crystal pillars capped with floating octahedra, concentric phase-locked torus halos with 72 degree ticks, a 12-point sacred-geometry star chord lattice, and 8 orbiting crystalline shards in warning crimson (#FF4052), hyper magenta (#FF38B8), and solar gold (#FFD866) over a deep void background (#030510) with Chladni standing-wave interference contours. Framed with corner brackets and centered 'THE CHOIR // HARMONIC SOVEREIGN OF THE SIGNAL' geometric pixel typography."`

---

### Asset 3: Pilot Flow State & Archive Insignia (`docs/assets/pilot_flow_insignia.png`)
- **Verified Path:** `docs/assets/pilot_flow_insignia.png`
- **Purpose:** Official pilot crest for `ARCHIVE OF THE SIGNAL` and `// FLOW STATE //` mastery telemetry introduced in `TASK-001`.
- **Reference Style:** `src/Nullwave/Render/Models.cs` (`BuildPlayer`, `BuildThruster`), `docs/screenshots/v7_speed.png`, and `src/Nullwave/UI/Screens.cs` (`DrawArchive`).
- **Dimensions & Aspect:** `1024x1024` (`1:1` square).
- **Perspective:** Top-down zenithal crest view (`-PI/2` vertical heading) of the NULLWAVE player interceptor framed within a hexagonal shield and circular 16-beat PLL dial.
- **Transparency:** Opaque (`RGBA`, `alpha = 255` on all `1,048,576` pixels).
- **Palette:** `#030510` (Void Navy), `#20F0FF` (Electric Cyan), `#FFD866` (Solar Gold), `#FF38B8` (Hyper Magenta), `#E6F5FF` (Ice White).
- **Animation / Engine Import Requirements:** Static 1:1 crest plate (`docs/assets/`).
- **Exact Prompt / Visual Specification:**
  > `"Minimalist futuristic 1:1 square (1024x1024) arcade pilot crest for '// FLOW STATE //' and 'ARCHIVE OF THE SIGNAL' in NULLWAVE: a stylized angular delta-wing interceptor silhouette with glowing octahedral canopy, wingtip emitters, and twin hyper-magenta (#FF38B8) afterburner plumes pointing vertically upward, framed by a hexagonal shield, a circular 16-beat phase-locked loop (PLL) timing dial with diamond downbeat nodes, dual symmetrical Fourier audio-spectrum soundwave arcs on the left and right hemispheres, and triple ascending cyan (#20F0FF) and solar-gold (#FFD866) resonance chevrons over a deep obsidian (#030510) polar radar grid."`

---

### Asset 4: Attunement Signal Matrix Reference Sheet (`docs/assets/attunement_matrix_sheet.png`)
- **Verified Path:** `docs/assets/attunement_matrix_sheet.png`
- **Task:** `TASK-002` (Live Attunement Loadout Strip on HUD & Pause Overlay)
- **Purpose:** Technical 4x3 schematic and visual reference plate showcasing all 12 NULLWAVE Attunement upgrade lines (`AttunementCatalog.All`), their 3-letter HUD tags (`SPL`, `FAN`, `PRC`, `RPD`, `HVY`, `LNG`, `HOM`, `RIC`, `RER`, `SPH`, `PHS`, `GRZ`), geometric vector-mesh diagrams, stack capacities (`4/4` Core, `3/3` Archive Unlocks), and a live bottom mockup of the `HUD & PAUSE LOADOUT STRIP //`.
- **Reference Style:** `src/Nullwave/World/Attunements.cs`, `src/Nullwave/UI/Screens.cs` (`DrawHud`, `DrawPause`, `DrawAttune`), `src/Nullwave/Render/Palette.cs`, and `src/Nullwave/Render/PixelFont.cs`.
- **Dimensions & Aspect:** `1600x900` (`16:9` widescreen).
- **Perspective:** 2D orthographic technical schematic plate with 4x3 bevelled obsidian glass cards and glowing vector-mesh diagrams over a 40px blueprint grid and domain-warped nebula wash.
- **Transparency:** Opaque (`RGBA`, `alpha = 255` on all `1,440,000` pixels).
- **Palette:** `#030510` (Void Navy), `#080B1E` (Deep Obsidian), `#20F0FF` (Electric Cyan), `#FF38B8` (Hyper Magenta), `#FF4052` (Warning Crimson), `#FFD866` (Solar Gold), `#E6F5FF` (Ice White), `#70809E` (Dim Slate).
- **Animation / Engine Import Requirements:** Static 16:9 documentation and design reference plate (`docs/assets/`).
- **Exact Prompt / Visual Specification:**
  > `"A structured 16:9 widescreen (1600x900) 4x3 technical schematic and arcade signal-matrix plate for NULLWAVE showcasing all 12 Attunement upgrade lines: SPL // SPLIT (twin angled bolts + center bolt, 4/4), FAN // FAN (wide 5-bolt fan spread arc, 4/4), PRC // PIERCE (spearhead bolt piercing two enemy rings, 4/4), RPD // RAPID (high-cadence twin bolt stream + speed chevrons, 4/4), HVY // HEAVY (enlarged octahedral core projectile + concentric shockwaves, 4/4), LNG // LONG WAVE (dual-phase harmonic sine carrier wave, 4/4), HOM // HOMING (twin curved homing arcs locking onto target reticle, 3/3), RIC // RICOCHET (bolt bouncing off monolith prism wall with specular flare, 3/3), RER // REAR ARRAY (ship firing forward bolt + twin backward rear bolts, 3/3), SPH // SIPHON (three repair crystal shards above a 3-segment integrity bar, 3/3), PHS // PHASE DRIVE (three dash phase afterimages + cooldown vector, 3/3), and GRZ // GRAZER (enemy comet bullets skimming a ship's resonance halo with graze sparks, 3/3). Each card displays its 3-letter tag, name, segmented stack pips, and effect summary, with a 'HUD & PAUSE LOADOUT STRIP //' preview bar along the bottom over a deep void navy (#030510) blueprint grid."`

---

### Asset 5: Seed Archive & Signal Decoder Console Plate (`docs/assets/seed_archive_console.png`)
- **Verified Path:** `docs/assets/seed_archive_console.png`
- **Task:** `TASK-003` (Interactive Seed Entry & Seed History Replay in Archive)
- **Purpose:** Technical sci-fi UI/schematic plate showcasing the `ARCHIVE OF THE SIGNAL // SEED DECODER & FLIGHT RECORDER` console interface, 8-character base-34 seed register (`001N4C4M // 0x5EEDC0DE`), deterministic 3D wireframe arena topology projection (`SCATTER`, `HENGE`, `CITY`, `SPIRAL`, `LANES`), flight-recorder seed history table (`RecentRuns` / `SeedRecords`), and base-34 character matrix (`I->1, O->0`).
- **Reference Style:** `src/Nullwave/Core/Rng.cs` (`SeedCode`), `src/Nullwave/World/Meta.cs` (`SeedRecord`, `RunRecord`, `MetaProgress`), `src/Nullwave/World/World.Director.cs` (5 arena layouts), `src/Nullwave/UI/Screens.cs` (`DrawArchive`), and `src/Nullwave/Render/Palette.cs`.
- **Dimensions & Aspect:** `1600x900` (`16:9` widescreen).
- **Perspective:** Multi-panel technical console schematic with a central 3D oblique wireframe arena topology projection (`Renderer.Oblique` extruded monoliths + player interceptor) over a 40px blueprint grid.
- **Transparency:** Opaque (`RGBA`, `alpha = 255` on all `1,440,000` pixels).
- **Palette:** `#030510` (Void Navy), `#080B1E` (Deep Obsidian), `#20F0FF` (Electric Cyan), `#FF38B8` (Hyper Magenta), `#FF4052` (Warning Crimson), `#FFD866` (Solar Gold), `#E6F5FF` (Ice White), `#70809E` (Dim Slate).
- **Animation / Engine Import Requirements:** Static 16:9 studio documentation and design schematic plate (`docs/assets/`).
- **Exact Prompt / Visual Specification:**
  > `"Technical 16:9 widescreen (1600x900) sci-fi UI schematic plate for NULLWAVE titled 'ARCHIVE OF THE SIGNAL // SEED DECODER & FLIGHT RECORDER'. Top bay: 8-character base-34 target seed register ('001N4C4M // SEED 0X5EEDC0DE') in individual neon character cells with byte-weight bars, dual-phase harmonic waveform synthesizer, hotkey controls ('[L] LAUNCH SEED [R] CYCLE HISTORY [C] CUSTOM SEED [N] RANDOM'), and verified signal record readout ('BEST WAVE: 12 (SPIRAL), BEST SCORE: 84,500, BEST CHAIN: X36*, RUNS: 04'). Left bay: Flight Recorder seed archive table listing recorded runs and active signal modifiers ('METRONOME X1.15', 'GLASS CHOIR X1.40'). Center bay: 3D oblique wireframe arena topology preview with 5 extruded polygonal monoliths, resonance rings, banked player interceptor, and 5 layout mini-schematics ('SCATTER', 'HENGE', 'CITY', 'SPIRAL', 'LANES'). Right bay: 34-key Base-34 alphabet matrix ('I->1, O->0') highlighting '0, 1, N, 4, C, M' plus seed palette hex swatches ('#20F0FF', '#FF38B8', '#FFD866', '#FF4052'). Bottom strip: 'RECENT SIGNALS //' bar with '001N4C4M W12 84,500 CH36*' highlighted."`

---

### Asset 7: Choir Boss Phase Choreography & Telegraph Matrix (`docs/assets/choir_phase_choreography.png`)
- **Verified Path:** `docs/assets/choir_phase_choreography.png`
- **Task:** `TASK-005` (Choir Boss Phase-Transition Choreography & Audio-Reactive Telegraph Polish)
- **Purpose:** Technical boss encounter schematic & HUD choreography plate illustrating `THE CHOIR // PHASE-TRANSITION CHOREOGRAPHY & TELEGRAPH MATRIX` across Phases I–IV (`OVERTURE`, `HARMONIC CONVERGENCE`, `NULL RESONANCE`, `BLIND CHOIR`), including the live Boss Bar HUD mockup, progressive 3D vector-mesh silhouettes, `580u` bullet-clear shockwave ring, and beat-locked attack telegraph specs.
- **Reference Style:** `src/Nullwave/World/Entities.cs` (`Enemy`), `src/Nullwave/World/World.Enemies.cs` (`UpdateBoss`, `BossPhaseSubtitle`, `BossPhaseClearRadius`), `src/Nullwave/Render/WorldRenderer.cs` (`DrawBoss`), `src/Nullwave/UI/Screens.cs` (`DrawBossBar`), and `src/Nullwave/Render/Palette.cs`.
- **Dimensions & Aspect:** `1600x900` (`16:9` widescreen).
- **Perspective:** Multi-panel technical schematic with top HUD Boss Bar telemetry mockup (`x = 380..1220`), 4 orthographic radial Choir phase progression cards, and 3 bottom tactical mechanics panels over a 40px blueprint grid.
- **Transparency:** Opaque (`RGBA`, `alpha = 255` on all `1,440,000` pixels).
- **Palette:** `#030510` (Void Navy), `#080B1E` (Deep Obsidian), `#FF4052` (Warning Crimson), `#FF38B8` (Hyper Magenta), `#20F0FF` (Electric Cyan), `#FFD866` (Solar Gold), `#E6F5FF` (Ice White), `#70809E` (Dim Slate).
- **Animation / Engine Import Requirements:** Static 16:9 studio documentation and design schematic plate (`docs/assets/`).
- **Exact Prompt / Visual Specification:**
  > `"Technical 16:9 widescreen (1600x900) boss encounter schematic and HUD choreography plate for NULLWAVE titled 'THE CHOIR // PHASE-TRANSITION CHOREOGRAPHY & TELEGRAPH MATRIX'. Top bay: 'BOSS BAR HUD //' replica showing 'THE CHOIR // PHASE II OF IV // HARMONIC CONVERGENCE', 54% crimson HP fill, 76% solar-gold ghost drain chunk, 4-phase vertical dividers, centered phase pips, '// PHASE SHIFT //', and 'INCOMING VOLLEY' indicators alongside 'CLEAR RADIUS: 580U', 'BREAK INVULN: 1.15S', and 'BEAT RESET: 1.25B'. Center bay: 4 progressive silhouette cards for 'PHASE I // OVERTURE' (10 pillars, 1 halo, 4 shards), 'PHASE II // HARMONIC CONVERGENCE' (12 pillars, 2 halos, 6 shards + 580U crimson/cyan Bullet-Clear Shockwave Ring), 'PHASE III // NULL RESONANCE' (14 pillars, 2 halos, 8 shards + starburst lasers), and 'PHASE IV // BLIND CHOIR' (16 pillars, 3rd counter-tilted crimson-gold halo, 10 shards + crossfire). Bottom bay: 3 tactical spec panels detailing '01 // 580U FAIR BULLET-CLEAR RING', '02 // BEAT-LOCKED ATTACK TELEGRAPH', and '03 // CHOIR PHASE ARCHITECTURE'."`

---

## 4. Visual QA Inspection Summary (`@artist`)

All seven PNG files were visually inspected via `read_file`:
1. **`docs/assets/nullwave_key_art.png` (`1600x900`):** Verified crisp 3D vector-mesh silhouette on the banking player interceptor, clean separation around the top-left `NULLWAVE` title panel and top-right `BPM 128 // MOVEMENT V : CHOIR` telemetry badge, vibrant 6-beam starburst convergence, and cohesive cyan/magenta/gold palette matching `v9_roster.png` and `v9_starburst.png`.
2. **`docs/assets/choir_boss_emblem.png` (`1024x1024`):** Verified radial symmetry across all 24 spectrum pillars, 10 frustum segments, and 8 orbiting shards, balanced HDR bloom on the central icosphere singularity, and dead-center alignment of `// BOSS INSIGNIA : WAVE V / X / XV //` and `THE CHOIR` typography with zero geometry overlap.
3. **`docs/assets/pilot_flow_insignia.png` (`1024x1024`):** Verified sharp delta-wing interceptor geometry, clean 16-beat PLL dial ticks, symmetrical left/right spectrum waveform arcs, ascending flow chevrons, and centered `ARCHIVE OF THE SIGNAL` / `// FLOW STATE //` inscriptions.
4. **`docs/assets/attunement_matrix_sheet.png` (`1600x900`):** Verified all 12 Attunement cards (`SPL`, `FAN`, `PRC`, `RPD`, `HVY`, `LNG`, `HOM`, `RIC`, `RER`, `SPH`, `PHS`, `GRZ`), crisp individual segmented stack pips (`4/4` for Core row 1–6, `3/3` for Unlock row 7–12), distinct geometric vector diagrams in each cell, complete punctuation (`+1`, `+14%`, `&`), and the bottom `HUD & PAUSE LOADOUT STRIP //` badge preview bar.
5. **`docs/assets/seed_archive_console.png` (`1600x900`):** Verified the 8-character Base-34 seed register (`001N4C4M`), harmonic waveform synthesizer, 7-row Flight Recorder seed table, 3D oblique wireframe arena topology projection with 5 extruded monoliths and 5 movement mini-maps (`SCATTER`, `HENGE`, `CITY`, `SPIRAL`, `LANES`), 34-character Base-34 key matrix (`I->1, O->0`), `#` hex palette swatches, and bottom `RECENT SIGNALS //` highlight strip with zero text collisions.
6. **`docs/assets/phase_sync_diagram.png` (`1600x900`):** Verified the PLL beat-phase oscilloscope with `±65MS` sync windows and unpenalized off-beat zones, the 3D oblique barrel-roll dash trajectory with 5 fading afterimage ghosts, `195U` shockwave ring, and dodged crimson enemy comet bullets, the 4 synergy telemetry cards (`+5.0% FLOW`, `+5.0 RESONANCE`, `-18% RECHARGE`, `0.30S INVULN`), and the bottom HUD Flow Meter readout bar with zero text overlap.
7. **`docs/assets/choir_phase_choreography.png` (`1600x900`):** Verified the top `BOSS BAR HUD //` replica (`THE CHOIR // PHASE II OF IV // HARMONIC CONVERGENCE`, ghost drain, phase pips, `// PHASE SHIFT //`, `INCOMING VOLLEY`), all 4 progressive Choir phase cards (`10 -> 12 -> 14 -> 16` pillars, `1 -> 2 -> 2 -> 3` halos, `4 -> 6 -> 8 -> 10` shards, `580U` clear ring, starburst lasers), and all 3 bottom mechanic panels with generous border margins and zero text overlap.
