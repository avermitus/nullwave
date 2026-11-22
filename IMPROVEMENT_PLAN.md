# NULLWAVE polish pass

This pass focuses on two things a player should be able to trust in a crowded wave: a deliberate button tap and a readable status display.

## Track A (C# systems and performance, Sol)

1. Highest priority: Game.UpdatePlaying samples dash and burst once before a loop that can run zero to five simulation ticks. Retain real-player pressed edges across zero-step updates, deliver once on the first eligible tick, and expire pending input on pause, death, modal transitions and restart. Inspect hit-stop handling for swallowed presses. Keep held movement/fire/focus live and preserve pilot/capture and existing audio timing. Do not change dash cooldown, sync windows, invulnerability, enemy timings or damage balance.
2. Add a focused deterministic regression check covering zero ticks followed by a tick, catch-up ticks and a transition/reset. Preserve existing selftests. Keep the input path allocation-free.
3. Own Core/Game.cs, Core/InputState.cs, World/World.cs and Tools/SelfTest*.cs plus a small Core input helper if needed. Expose the current 4.1 s combo timeout as a named public constant on World (ComboDuration) for the HUD. Build Release and report checks.

## Track B (HUD, feedback and polish, Terra)

1. Own UI/Screens.cs only. Give evasive dash a compact readiness indicator based on Player.DashCooldown and DashCooldownMax. Make availability readable by shape or text as well as color. Distinguish this from the existing on-beat sync feedback.
2. Replace the chain's barely visible final fade as the only timing cue with a compact draining bar while the chain is active. Use World.ComboDuration (Track A supplies it). Verify its spacing with active signal modifiers and hype.
3. Eliminate the per-draw LINQ/string.Join modifier caption construction for an unchanged run/loadout. Verify ownership and mutability before caching. Respect current settings, pixel font, palettes, logical viewport, health display and existing beat/phase cues. Review gameplay HUD and boss telegraph contrast at full and reduced effects. Build Release and report.

## Director verification

Review the combined C# diff, rebuild src/Nullwave/Nullwave.csproj in Release with warnings treated as errors, run the existing headless selftest, and try the built-in smoke scenario. In a run test quick dash/burst taps, pause/resume, action cooldown, chain refresh and expiry, busy backgrounds and the boss HUD. Existing hit-stop, knockback, sparks, dash audio and beat-sync rewards already signal impacts; preserve their timings.
