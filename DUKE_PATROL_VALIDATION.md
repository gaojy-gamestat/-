# Duke patrol validation — 2026-11-27

Base: 93ec91c7 (origin/main). Unity 2022.3.62f3, original 第一关 scene.

## Confirmed defects and changes
- Serialized route points contained negative Y coordinates. Runtime clamping sent these points to the image edge. Replaced with corridor/room routes, and rejected invalid or blocked clicks in the route editor.
- A blocked movement step disabled the entire patroller. Replaced with collision-checked floor-constrained detours, segment smoothing, and retry when no route exists. Plan before walking with extra clearance.
- The living-room door's default landing intersected the sofa boundary. Door landings now search nearby free floor space when obstructed.
- Door positions previously changed while Duke remained visible. Retained existing door pairs and added a 0.25-second fade out / 0.25-second fade in; transfer occurs between these phases. No door animation, camera or UI layout changes.
- Retained current artwork frames and foreground masks for sitting/sleeping.

## Final verification
PASS: compilation and actual Editor Play mode, timeScale=1.
PASS: temporary kitchen obstacle produces a detour; every detour segment stays on floor and outside blocking geometry.
PASS: two complete living room → kitchen → bedroom → living room loops (176.18 seconds total).
PASS: 12 door transfers occur during the transition; sampled opacity below 0.35 at each transfer.
PASS: no blocked-route warning, assertion failure or exception during the final two-loop run.

Native Editor test entry: DukeRouteValidation.Run (opens the original scene and exits Editor after testing).
Results: Library/duke-route-result.txt; runtime log: Library/duke-route-play.log.
Screenshots: Library/duke-route-door-*.png (actual Game frames during doorway transitions).

NOT_VERIFIED: dual-instance multiplayer; continuous traversal without portals. Existing room portals remain necessary for this scene layout.
If furniture completely seals the floor route, Duke waits and retries rather than disabling patrol, walking through furniture or jumping to the destination.
