# TASK-010: Attunement Synergy Evolutions ("Harmonic Overclock")

- **Status:** `done`
- **Owner (Design):** `@designer`
- **Owner (Engineering):** `@programmer`
- **Reviewer (QA):** `@qa`

---

## 1. Goal & Player-Facing Benefit

1. **Complementary Attunement Synergy Evolutions (`Attunements.cs`, `World.Player.cs`, `World.Collisions.cs`, `World.cs`):**
   - Currently, NULLWAVE's 12 Attunements scale linearly (`+bolts`, `+damage`, `+fire rate`, `+homing`, etc.), without rewarding cohesive two-line build archetypes.
   - `TASK-010` introduces **Harmonic Overclock** — 4 signature **Synergy Evolutions** that awaken automatically when complementary Attunement pairs both reach **Stack 2+**:
     1. **`SEEKER SALVO`** (`SPL 2+` & `HOM 2+`) — Fires twin flanking **Seeker Micro-Bolts** (`±38°` launch arcs, `1.75x` homing turn rate, `640u` lock range) with magenta-cyan plasma trails.
     2. **`PRISM LANCE`** (`PRC 2+` & `RIC 2+`) — Wall and monolith **ricochets** supercharge bolts (`1.35x` damage, `+1` bonus pierce, `+12%` velocity up to `2400`, `+0.35s` life) into refractive gold-white prism lances with bounce spark bursts.
     3. **`RAIL CADENCE`** (`HVY 2+` & `RPD 2+`) — Every **4th shot** fires a hyper-velocity centerline **Rail Bolt** (`2350` speed, `2.1x` damage, `1.45x` radius, `+2` bonus pierce) with thick solar-gold/white plasma rails.
     4. **`VOID PHANTOM`** (`PHS 2+` & `GRZ 2+`) — Expands dash absorption (`+55%`) and graze radius (`+25%`), and every **Dash** releases a **Void Shockwave** (`180u` damage ring `35 dmg`, `135u` bullet-cancel ring converting cleared enemy bullets into `+2` Resonance each).
2. **In-Run Celebration, Draft Preview & Zero-Overlap HUD/Pause Badges (`World.cs`, `WorldRenderer.cs`, `Screens.cs`, `Core/Game.cs`):**
   - Triggering a new synergy during an Attunement draft fires a dramatic `"OVERCLOCK // <SYNERGY NAME>"` banner, golden shockwave, and unlock chime.
   - Attunement draft cards highlight `"SYNERGY // <NAME>"` whenever selecting that card will immediately awaken an evolution.
   - Zero-allocation `SynergyFlags` bitmask displays compact overclock badges above the HUD Attunement Loadout Strip, inside the `SIGNAL HELD` Pause header, and on `RunSummary` telemetry — shining in both **Standard** and **Abyssal** themes.

---

## 2. Relevant Code & Ownership Constraints

### Strict File Ownership
- **`@designer` owns:** `.game-studio/BACKLOG.md`, `.game-studio/tasks/TASK-010.md` (Design & Spec), and—after `@programmer` finishes—updating `CHANGELOG.md` and `README.md` (strictly game-focused; zero AI/agent/swarm/task-ID chatter).
- **`@programmer` owns:** `src/Nullwave/**` (`World/Attunements.cs`, `World/Entities.cs`, `World/World.cs`, `World/World.Player.cs`, `World/World.Collisions.cs`, `Render/WorldRenderer.cs`, `UI/Screens.cs`, `Core/Game.cs`, `Tools/SelfTest.cs`), `docs/screenshots/synergy_overclock.png`, `.game-studio/tasks/TASK-010.md` (§6 Engineering Handoff), and final git commit/push after `@qa` sign-off.
- **`@qa` owns:** `.game-studio/QA_REPORT.md` and `.game-studio/tasks/TASK-010.md` (§7 QA Verdict).

---

## 3. Implementation Plan (`@programmer`)

### A1. Synergy Definitions & Zero-Alloc Bitmask Query (`World/Attunements.cs`, `World/Entities.cs`)
1. **In `src/Nullwave/World/Attunements.cs`:**
   - Define:
     ```csharp
     [Flags]
     public enum SynergyFlags
     {
         None = 0,
         SeekerSalvo = 1 << 0, // split >= 2 && homing >= 2
         PrismLance  = 1 << 1, // pierce >= 2 && ricochet >= 2
         RailCadence = 1 << 2, // heavy >= 2 && rapid >= 2
         VoidPhantom = 1 << 3, // phase >= 2 && grazer >= 2
     }
     ```
   - Define `SynergyDef` (`SynergyFlags Flag`, `string Id`, `string Tag` (`"SLV"`, `"LNC"`, `"RAL"`, `"VPH"`), `string Name`, `string FirstId`, `string SecondId`, `int MinStacks = 2`, `string Desc`, `float Hue`) and `public static readonly SynergyDef[] All` on `SynergyCatalog`.
   - Implement zero-allocation helpers on `SynergyCatalog`:
     - `public static SynergyFlags Evaluate(Player player)` -> computes active `SynergyFlags` bitmask directly from `Player` stack fields (`0` allocations).
     - `public static int CountActive(SynergyFlags flags)` -> `BitOperations.PopCount((uint)flags)`.
     - `public static SynergyDef? WouldUnlock(Player player, string attunementId)` -> returns the first `SynergyDef` not currently active on `player` that becomes active if `attunementId` gains `+1` stack (used by `DrawAttunement` draft cards!).
     - `public static int GetActive(Player player, Span<int> catalogIndices)` -> writes active `SynergyCatalog.All` indices into a caller `stackalloc int[4]` span without allocating.
2. **In `src/Nullwave/World/Entities.cs` (`Player` & `Bullet`):**
   - On `Player`: add `public int ShotCounter;`, `public SynergyFlags ActiveSynergies;` (synced whenever attunements are applied), and `public float VoidPhantomFlash;` (decays in `UpdatePlayer` for visual aura).
   - On `Bullet`: add `public byte SynergyKind;` (`0 = normal`, `1 = SeekerSalvo`, `2 = PrismLance (supercharged)`, `3 = RailCadence`) — reset to `0` in `Bullet.Reset()`, zero allocations!

### A2. Gameplay Mutations (`World/World.Player.cs`, `World/World.Collisions.cs`, `World/World.cs`)
1. **Sync & Activation Celebration (`World/World.cs` `ChooseAttunement` & `AttunementCatalog.Apply`):**
   - In `AttunementCatalog.Apply(Player player, string id)`: refresh `player.ActiveSynergies = SynergyCatalog.Evaluate(player);`.
   - In `World.ChooseAttunement(int index)`: snapshot `SynergyFlags before = Player.ActiveSynergies;` before `Apply`, then check `SynergyFlags unlocked = Player.ActiveSynergies & ~before;`. When `unlocked != SynergyFlags.None`:
     - Emit `"OVERCLOCK // " + def.Name` for `2.6f`s (replacing the plain attunement toast), spawn a golden-cyan shockwave (`AddShockwave(Player.Pos, 380f, 0.75f, 6f, new Vector4(1f, 0.85f, 0.35f, 1f))`), burst 28 sparks, and trigger `Sfx?.Trigger(SfxType.Unlock, 0.85f);`.
2. **In `src/Nullwave/World/World.Player.cs`:**
   - **Seeker Salvo (`Shoot`):** When `(p.ActiveSynergies & SynergyFlags.SeekerSalvo) != 0`, fire 2 additional flanking Seeker bolts at `baseAngle ± 0.66f` (`±38°`) with `damageScale = 0.62f`, `homing = p.HomingLevel * 3.85f`, `life = p.BulletLife * 1.25f`, `SynergyKind = 1`, `Hue = 0.84f`. In `UpdateBullets`, give `SynergyKind == 1` bolts an extended seek radius (`640f` vs `460f`).
   - **Rail Cadence (`Shoot`):** Increment `p.ShotCounter++`. When `(p.ActiveSynergies & SynergyFlags.RailCadence) != 0 && p.ShotCounter % 4 == 0`, fire a centerline hyper-velocity **Rail Bolt** (`speed = 2350f`, `radius = p.BulletRadius * 1.45f`, `damage = p.Damage * 2.1f`, `pierce = p.PierceLevel + 2`, `SynergyKind = 3`, `Hue = 0.09f`) + extra forward muzzle sparks.
   - **Void Phantom (`UpdatePlayer` dash trigger):** Decay `p.VoidPhantomFlash = MathF.Max(0f, p.VoidPhantomFlash - dt * 3f);`. When dash starts and `(p.ActiveSynergies & SynergyFlags.VoidPhantom) != 0`:
     - Set `p.VoidPhantomFlash = 1f;`
     - Emit a violet-cyan shockwave (`AddShockwave(p.Pos, 180f, 0.38f, 4f, new Vector4(0.75f, 0.45f, 1f, 1f))`).
     - Damage enemies within `180f` of `p.Pos` for `35f` (`DamageEnemy(e, 35f, p.Pos)`).
     - Clear enemy bullets within `135f` of `p.Pos` (backward loop via `ReturnBullet`), awarding `+2f * p.TotalResonanceGain * Mods.ResonanceGain` Resonance and `1` spark per cleared bullet.
   - **Prism Lance (`UpdateBullets` wall ricochet + `ResolveCollisions` obstacle ricochet):**
     - Add a shared helper `SuperchargePrismLance(Bullet b)`: when `(Player.ActiveSynergies & SynergyFlags.PrismLance) != 0`, after a wall or monolith bounce (`b.Bounces--`):
       - `b.Damage *= 1.35f;` (cap e.g. `<= Player.Damage * 4f`), `b.Pierce += 1;`, `b.Life = MathF.Min(b.MaxLife * 1.5f, b.Life + 0.35f);`, boost speed by `1.12f` (capped at `2400f`), set `b.SynergyKind = 2; b.Hue = 0.13f;`, and burst 6 gold-white prism sparks at `b.Pos`.
3. **In `src/Nullwave/World/World.Collisions.cs`:**
   - When `(p.ActiveSynergies & SynergyFlags.VoidPhantom) != 0`: expand `absorbRadius = p.Radius + 40f` (vs `26f`) and `grazeRadius = hitRadius + 34f` (vs `27f`).
   - Call `SuperchargePrismLance(b)` inside the monolith bounce block (`if (b.Bounces > 0)`).

### A3. Visual Polish, HUD/Pause Synergy Badges & Capture Preset (`WorldRenderer.cs`, `Screens.cs`, `Game.cs`)
1. **In `src/Nullwave/Render/WorldRenderer.cs` (`DrawBullets` & `DrawPlayer`):**
   - In `DrawBullets` for `PlayerBullets`: branch cleanly on `b.SynergyKind`:
     - `1 (SeekerSalvo)`: vivid hyper-magenta/cyan outer glow + curved comet streak (`tail = 1.45x`).
     - `2 (PrismLance)`: refractive solar-gold + white-hot core streak + twin parallel refraction hairlines.
     - `3 (RailCadence)`: thick hyper-rail solar-amber/white core (`tail = 1.85x`) + bright plasma rails + larger nose flare.
   - In `DrawPlayer`: when `player.VoidPhantomFlash > 0.02f` (or `(player.ActiveSynergies & SynergyFlags.VoidPhantom) != 0 && player.IsDashing`), render a crisp violet-cyan rotating phantom halo around the ship (`r.Arc` / `r.Circle`) and feed a bonus violet light pulse into `PopulateCombatLights` so it pops vividly in **Abyssal** mode!
2. **In `src/Nullwave/UI/Screens.cs` (`DrawHudAttunements`, `DrawPauseAttunements`, `DrawAttunement`):**
   - **`DrawHudAttunements`:** When `player.ActiveSynergies != SynergyFlags.None`, draw a compact golden **Overclock Synergy Pill Row** directly above the attunement strip at `y = (count > 6 ? 688f : 712f)` (`x = 56`, `h = 18f`, e.g. `SLV`, `LNC`, `RAL`, `VPH` framed in pulsing solar gold `#FFD866` with `*` overclock mark — zero overlap with `INTEGRITY` at `y = 770` or top-left HUD `y <= 224`).
   - **`DrawPauseAttunements`:** When `player.ActiveSynergies != SynergyFlags.None`, render a compact centered `"OVERCLOCK // SLV · RAL"` badge at `x = 800, y = 612` (cleanly between `"ATTUNEMENTS // XX"` at `x = 546` and `"XX / 12 SIGNALS"` at `x = 1054`, zero extra height and zero overlap with Spotify/Audio footer!).
   - **`DrawAttunement`:** On each draft card, check `var syn = SynergyCatalog.WouldUnlock(w.Player, offer.Id);`; if non-null, render a crisp solar-gold `"OVERCLOCK // " + syn.Name` tag on the card (e.g. above the stack bar at `y + ch - 66`) so players immediately spot synergy completions during wave drafts!
3. **In `src/Nullwave/Core/Game.cs`:**
   - Support `--attune synergy` preset in `ApplyAttuneOverride` (applying `split,split,homing,homing,heavy,heavy,rapid,rapid,phase,phase,grazer,grazer` so multiple evolutions are active immediately) and add a `"synergy"` capture scenario in `ConfigureCapture("synergy")` (wave 4 combat with `SeekerSalvo`, `RailCadence`, and `VoidPhantom` active, firing mid-combat) so `./run.cmd --capture synergy --theme abyssal --out docs/screenshots/synergy_overclock.png` produces a crisp showcase capture!

### A4. Deterministic Self-Test Coverage (`src/Nullwave/Tools/SelfTest.cs`)
- Add `TestAttunementSynergies()` called from `SelfTest.Run()`, verifying:
  1. **Evaluation & `WouldUnlock`:** Fresh `Player` has `SynergyFlags.None`; stacking `split` x2 + `homing` x1 still yields `None`, `WouldUnlock(p, "homing")` returns `SeekerSalvo`, adding 2nd `homing` activates `SeekerSalvo` (`CountActive == 1`); similarly verify `PrismLance`, `RailCadence`, and `VoidPhantom` (`CountActive == 4` when all 4 pairs are Stack 2+).
  2. **Seeker Salvo Firing:** Shooting with `SeekerSalvo` spawns `SynergyKind == 1` bolts with higher homing turn rate than baseline bolts.
  3. **Rail Cadence 4th Shot:** Firing 4 volleys with `RailCadence` spawns a `SynergyKind == 3` hyper-rail bolt on shot 4 with `Vel.Length() >= 2200f` and `Damage > Player.Damage * 1.9f`.
  4. **Prism Lance Ricochet Supercharge:** Firing a `PrismLance` bolt into an arena wall or monolith supercharges it (`SynergyKind == 2`, increased `Damage`, bonus `Pierce`).
  5. **Void Phantom Dash Shockwave:** Dashing with `VoidPhantom` clears nearby enemy bullets inside `135u`, grants Resonance, and damages an enemy inside `180u`.

---

## 4. Acceptance Criteria (`@qa`)

1. **Clean Release Build:** `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` succeeds with **0 warnings and 0 errors**.
2. **Self-Test Suite Pass:** `./run.cmd --selftest` passes with `RESULT: PASS` including `TestAttunementSynergies()` and zero regressions in existing suites.
3. **Dual-Theme Smoke & Synergy Preset Pass:**
   - `./run.cmd --smoke 5` -> `RENDERSTATE -> PASS`.
   - `./run.cmd --smoke 4 --attune synergy --theme abyssal` -> runs cleanly with active evolutions, high FPS, `RENDERSTATE -> PASS`, zero allocations on hot paths.
4. **Visual Capture Verification:**
   - Inspect `docs/screenshots/synergy_overclock.png` (and `--capture pause --attune synergy` / `--capture attune` spot-check if desired) via `read_file` to confirm crisp synergy projectile visuals (`SeekerSalvo` / `RailCadence` / `VoidPhantom`), clean golden overclock badges above `INTEGRITY`, and zero HUD/Pause overlap.
5. **Documentation & Repo Sync (after `@programmer` finishes):**
   - `@designer` updates `CHANGELOG.md` and `README.md` (strictly game-focused; zero AI/agent/swarm/task-ID chatter), `@qa` verifies everything, and `@programmer` commits & pushes cleanly to `origin/main` after `@qa` sign-off.

---

## 5. Designer Notes (`@designer`)
- Triggering evolutions automatically at Stack 2+ of both complementary lines keeps pacing snappy (zero extra menu interruptions) while the draft-card `"OVERCLOCK // <NAME>"` preview makes mid-run build planning immediately readable.
- Using a 1-byte `SynergyKind` field on pooled `Bullet` instances and a `[Flags] SynergyFlags` bitmask on `Player` keeps the entire system strictly zero-allocation during combat and rendering.

---

## 6. Engineering Handoff (`@programmer`)
- **Files Modified (`@programmer` scope):**
  - `src/Nullwave/World/Attunements.cs`: Added `[Flags] SynergyFlags`, `SynergyDef`, and zero-allocation `SynergyCatalog` (`All`, `Evaluate`, `CountActive`, `FirstMatch`, `WouldUnlock`, `GetActive`) covering all 4 Stack-2+ evolutions (`SEEKER SALVO` / `SLV`, `PRISM LANCE` / `LNC`, `RAIL CADENCE` / `RAL`, `VOID PHANTOM` / `VPH`); synced `player.ActiveSynergies` inside `AttunementCatalog.Apply`.
  - `src/Nullwave/World/Entities.cs`: Added `Player.ShotCounter`, `Player.ActiveSynergies`, `Player.VoidPhantomFlash`, and pooled `Bullet.SynergyKind` (`0=normal, 1=SeekerSalvo, 2=PrismLance, 3=RailCadence`, cleared in `Bullet.Reset()`).
  - `src/Nullwave/World/World.cs`: Added synergy unlock detection in `ChooseAttunement` triggering `"OVERCLOCK // <NAME>"` banner (`2.6s`), golden shockwave (`380u`), 28 spark burst, and unlock chime.
  - `src/Nullwave/World/World.Player.cs` & `src/Nullwave/World/World.Collisions.cs`:
    - `SeekerSalvo`: twin flanking `±38°` micro-seekers (`0.62x` dmg, `3.85x` homing, `1.25x` life, `640u` seek range, `SynergyKind=1`).
    - `RailCadence`: every 4th shot (`ShotCounter % 4 == 0`) launches centerline hyper-velocity rail bolt (`2350` speed, `2.1x` dmg, `1.45x` radius, `+2` pierce, `SynergyKind=3`) + forward muzzle sparks.
    - `PrismLance`: shared `SuperchargePrismLance(Bullet)` called on arena-wall and monolith bounces (`1.35x` dmg capped `4x`, `+1` pierce, `+0.35s` life, `1.12x` speed capped `2400`, `SynergyKind=2`, 6 prism sparks).
    - `VoidPhantom`: expanded dash absorb (`+40u`) and graze (`+34u`) radii; dashing emits `180u` shockwave dealing `35` dmg and cancels enemy bullets inside `135u` for `+2` scaled Resonance each.
  - `src/Nullwave/Render/WorldRenderer.cs`: Added distinct high-contrast rendering for all 3 synergy projectile types (`SeekerSalvo` hyper-magenta/cyan ribbons, `PrismLance` solar-gold refractive streaks + twin hairlines, `RailCadence` thick solar-amber/white rails + nose flare), rotating violet-cyan `VoidPhantom` dash halo, and bonus violet-cyan Abyssal `uLights[]` flare.
  - `src/Nullwave/UI/Screens.cs`: Added zero-alloc solar-gold Overclock pill row (`*SLV`, `*LNC`, `*RAL`, `*VPH`) above HUD attunements (`y = 712` / `688`), centered Pause `"OVERCLOCK // SLV/RAL/VPH"` readout at `(800, 612)`, and draft-card `"OVERCLOCK // <NAME>"` preview banner via `SynergyCatalog.WouldUnlock`.
  - `src/Nullwave/Core/Game.cs` & `src/Nullwave/Program.cs`: Added `--attune synergy` preset (`ApplyAttuneOverride`) and `"synergy"` capture scenario (`warmup=260`).
  - `src/Nullwave/Tools/SelfTest.cs`: Added `TestAttunementSynergies()` verifying `Evaluate`/`WouldUnlock`/`GetActive` (4/4), Seeker Salvo firing & boosted homing, Rail Cadence 4th-shot hyper-rail, Prism Lance wall-bounce supercharge, and Void Phantom dash shockwave bullet-cancel/Resonance/enemy-damage.
  - **Captures:** `docs/screenshots/synergy_overclock.png` (Abyssal), plus local spot-checks `screenshots/synergy_pause.png` and `screenshots/synergy_standard.png`.
- **Verification Results:**
  - `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` -> **0 Warning(s), 0 Error(s)**
  - `./run.cmd --selftest` -> **`RESULT: PASS (76600 assertions)`** (`synergies: active=4/4 seekers=2 railSpeed=2350 prismDmg=8.0->10.8 voidRes=+13.5`)
  - `./run.cmd --smoke 5` (Standard) -> **`SMOKE | 5.0s fps=114`**, **`RENDERSTATE -> PASS`** (`alphaMin=255`, `rerenderDiff=0.000 %`)
  - `./run.cmd --smoke 4 --attune synergy --theme abyssal` -> **`attunes=12 synergies=3`**, **`SMOKE | 4.0s fps=112`**, **`RENDERSTATE -> PASS`** (`alphaMin=255`, `rerenderDiff=0.000 %`)
  - Visually inspected `docs/screenshots/synergy_overclock.png`, `screenshots/synergy_pause.png`, and `screenshots/synergy_standard.png` via `read_file`: vivid curved magenta `SeekerSalvo` arcs, blazing solar-amber `RailCadence` bolt, clean solar-gold `*SLV *RAL *VPH` HUD row & centered Pause `OVERCLOCK // SLV/RAL/VPH` badge with zero overlap.

---

## 7. QA Findings & Verdict (`@qa`)
- **Verdict:** **PASS (`APPROVED — 0 DEFECTS`)**
- **Build & Automated Verification:**
  - `./.dotnet/dotnet.exe build src/Nullwave/Nullwave.csproj -c Release --nologo` -> **0 Warning(s), 0 Error(s)** (`0.56s`).
  - `./run.cmd --selftest` -> **`RESULT: PASS (76467 assertions, 2.4s)`** (`synergies: active=4/4 seekers=2 railSpeed=2350 prismDmg=8.0->10.8 voidRes=+13.5`).
  - `./run.cmd --smoke 5` (Standard) -> **`5.0s fps=115`**, **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
  - `./run.cmd --smoke 4 --attune synergy --theme abyssal` -> **`attunes=12 synergies=3`**, **`4.0s fps=112`**, **`RENDERSTATE | after1=baseline after2=baseline alphaMin=255 rerenderDiff=0.000 % -> PASS`**.
- **Code & Allocation Audit:**
  - Confirmed zero per-frame heap allocations across `SynergyFlags` / `SynergyCatalog`, pooled `Bullet.SynergyKind` (`0..3`, cleared in `Reset()`), `WorldRenderer.DrawBullets`/`PopulateCombatLights`, and `stackalloc int[4]` HUD Overclock pills in `Screens.cs`.
- **Visual & Docs Inspection:**
  - Visually verified `docs/screenshots/synergy_overclock.png`, `screenshots/synergy_pause.png`, and `screenshots/synergy_standard.png` via `read_file`: vivid curved magenta `SeekerSalvo` trails, thick solar-amber `RailCadence` hyper-rail, clean solar-gold `*SLV *RAL *VPH` HUD row above `INTEGRITY`, and centered Pause `OVERCLOCK // SLV/RAL/VPH` with zero overlap in both themes.
  - Verified `README.md` and `CHANGELOG.md`: all image links resolve (`Test-Path == True`) with strictly game-focused copy and zero non-game references.
  - Full report logged in `.game-studio/QA_REPORT.md` (§15).
