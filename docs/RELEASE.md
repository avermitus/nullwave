# NULLWAVE RELEASE

The `RELEASE` branch and pinned RELEASE Pane are the combined integration workspace.
The GitHub repository remains `avermitus/nullwave`.

## Consolidated on 2026-11-21

- Started from `main` at `878de14` (v0.1.0), which already includes the music-driven gameplay, asset pipeline, cyber-tribal geometry and textures, and completed polish pass.
- Preserved and merged the pending original and game-polish-pass work. Kept the newer atlas rendering and current menus where older polish overlapped them.
- Merged main-4's electronic audio voices, generated sound layers, import tooling, and audio tests.
- Merged bunching-up-fix's enemy spacing and regression tests.
- All local branches existing at consolidation are ancestors of RELEASE. Original Pane files and indexes were left intact. Pending work was preserved as snapshot commits and merged into RELEASE.
- SVP 4 upscale and my-game are separate projects and were excluded.

## Future work

Develop features in their own Panes. Commit the finished work, then merge its branch into
`RELEASE` from the RELEASE Pane. Resolve overlap, build and run `--selftest`, then run
`git push origin RELEASE`. New edits in other Panes are not copied automatically.
Do not switch branches in another agent's working Pane.

## Verification

- .NET 8 Release build: zero warnings and zero errors.
- `--selftest`: PASS, 93,181 assertions, including electronic audio and enemy spacing.
- No existing branch was force-pushed or deleted; `main` remains unchanged.
