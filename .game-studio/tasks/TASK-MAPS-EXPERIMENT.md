# TASK-MAPS-EXPERIMENT: connected room maps

Status: implemented; ready for parent independent review.
Branch: experiment/maps, based on RELEASE 6aec9e4.
Ownership: Claude authored the initial slice; Codex continued as sole implementation writer after the usage-limit handoff. Existing reviewer is read-only; parent owns independent review/integration. No new agents, dependencies, assets or paid services.

## Design and implementation

Two opt-in footprints: HALO CIRCUIT (outer loop with core shortcut) and CATHEDRAL (pillared nave, chapels, apse, cloister). Open arena remains the default. Title MAP row supports keyboard, controller and mouse cycling with a footprint preview. CLI --map arena|halo|cathedral applies to demo, new runs, retry, smoke, captures and recording.

MapGeometry traces the authored shape into polygon loops; the same loops drive floor quads, solid edges, swept body movement, first-wall projectile/laser tests, safe placement, navigation and minimap. The backdrop remains visible outside the footprint. Standard enemies share a 48-radius flow field; bosses use a 96-radius field. Link clearance, lookahead and recovery respect body size. Recovery chooses a route with forward progress rather than oscillating between adjacent cells. Full movement/contact/spacing corrections remain swept. Pickups slide at walls; magnets require sight. Wells require accessible cores, and rails stop/turn before walls. Map footprints survive movement changes and burst damage.

Destructible arena monoliths are omitted in experimental maps; permanent geometry provides cover without invalidating the navigation graph. Arena layouts retain their original behavior. Smoke uses a disposable save, keeping automated QA separate from the player's records.

## Review findings addressed

- Radius-blind navigation: radius-filtered links plus separate boss field and actual-radius capsule checks. Tested room-to-room travel for 18, 44.93 and 96 radius at scales 1 and 0.82.
- Vertex parity: half-open ray crossings, checked against brute floor classification around every vertex at both scales.
- Final movement clearance: validate candidate clearance after push-out; test high-speed sweeps and attempted entry into narrowed Cathedral boss lanes.
- Projection: TryProjectToFloor exposes impossible margins; NearestNodeBrute/NearestWalkableNode return failure when no required-clearance node exists. Projection remains explicitly global placement after local correction fails; it is not used for ordinary movement corrections.
- Parser: catalog aliases/display names accepted; unsupported names and numeric values rejected with Arena output.

## Acceptance and evidence

Release build: zero warnings/errors. Final selftest PASS: 267,980 assertions in 34.9 seconds. Full selftest includes existing arena regressions plus map geometry, seeds, all-role spawns, actual player/enemy/boss final positions, room routing, boss integration, wave completion, pickup drift, rail walls, projectile consumption/ricochet, laser clipping/re-extension and movement transitions. See .pane/scratch/maps-build.log and maps-selftest-final.log for the current counts/results.

Rendered smoke: HALO 64 FPS, CATHEDRAL 66 FPS over 5 seconds on NVIDIA RTX 5060 Laptop GPU. Both render-state checks PASS (baseline GL state, opaque output, stable rerender).

Inspected images in .pane/scratch: halo-overview.png, cathedral-overview.png, halo-gameplay.png, cathedral-gameplay.png, halo-title.png. Distinct footprints, floor/void contrast, walls, room joins and minimaps are readable. Scripted rendering and automated pilots were used; no manual interactive playtesting is claimed.

## How to try / limits

See README's Experimental room maps section. Use the title MAP row or --map halo|cathedral|arena. Existing scores/seed history are shared; seed codes alone do not identify map choice. Selection is session-only, intentionally defaulting to arena next launch. Directional threat arrows remain straight directions; the minimap shows routes. Boundary-only SegmentClear/CapsuleClear require floor endpoints; callers validate placement. Human tuning of encounter feel and long-run balance remains follow-up work.
