# Upstream issue triage (this fork)

Internal log of issues in [Blecki/dwarfcorp](https://github.com/Blecki/dwarfcorp)
that no longer affect this fork, or that we've decided not to fix.

The upstream tracker has been silent since mid-2020 and its last master push
was January 2024. There is no upstream to comment on — this file is just a
record for our own planning.

## Dissolved by the FNA + MonoGame Content Builder migration

Issues that are about building on Linux/Mac with XNA/Mono, or native lib
compatibility, are moot here. Our build targets FNA (SDL2) + MGCB, and the
whole XNA/MXA/Mono workflow was deleted in `311ce35ff`.

| Upstream | Title | Our status |
|---|---|---|
| [#542](https://github.com/Blecki/dwarfcorp/issues/542) | `[Linux] TextureDataFromStream exception due to missing libpng.15.so.15` | N/A — FNA uses SDL2_image; no libpng 1.5 dependency |
| [#773](https://github.com/Blecki/dwarfcorp/issues/773) | `Linux crashes in sentry error reporting service` | N/A — Mono-specific `TypeLoadException`, different runtime path |
| [#856](https://github.com/Blecki/dwarfcorp/issues/856) | `Build environment on macOS and Linux using Mono` | Obsolete — our `README.md` documents the replacement flow |
| [#1047](https://github.com/Blecki/dwarfcorp/issues/1047) | `failure to start dwarfcorp on alpine linux` | N/A for us — Alpine/Mono-specific; FNA native libs ship Windows-only here |
| [#1057](https://github.com/Blecki/dwarfcorp/issues/1057) | `can this project be built more easily?` | **Solved** — this is exactly the motivation for the migration |
| [#692](https://github.com/Blecki/dwarfcorp/issues/692) | `XNA: art stops rendering when paused and clicked out of screen` | N/A — explicitly XNA-specific; FNA uses a different focus/pause model |

## Window-focus cluster — needs one manual repro pass, probably moot

Upstream reports on SDL2/FNA focus semantics differ from XNA's. Drop in the
"probably fixed by FNA" bucket, but we should do a 5-min repro of each once.

| Upstream | Title | Our status |
|---|---|---|
| [#882](https://github.com/Blecki/dwarfcorp/issues/882) | `Camera: After minimizing, camera will break out of world` | Unverified — test after Phase C |
| [#947](https://github.com/Blecki/dwarfcorp/issues/947) | `Low Repro Crash: Minimizing while generating advanced map causes purple screen of death` | Unverified |
| [#953](https://github.com/Blecki/dwarfcorp/issues/953) | `Low Repro Crash: Purple Screen of Death while starting advanced game - Terrain2D` | Unverified — not obviously window-focus, but same cluster in upstream |

## Project-management / wishlist / production labels — ignore

None of these describe a bug. They're milestone checklists, tracking
performance across OS groups, or feature pitches that require design work we
aren't doing.

- `Production` label: [#190](https://github.com/Blecki/dwarfcorp/issues/190), [#759](https://github.com/Blecki/dwarfcorp/issues/759), [#770](https://github.com/Blecki/dwarfcorp/issues/770), [#802](https://github.com/Blecki/dwarfcorp/issues/802), [#803](https://github.com/Blecki/dwarfcorp/issues/803), [#805](https://github.com/Blecki/dwarfcorp/issues/805)
- `Tracking` label: [#651](https://github.com/Blecki/dwarfcorp/issues/651), [#710](https://github.com/Blecki/dwarfcorp/issues/710), [#711](https://github.com/Blecki/dwarfcorp/issues/711), [#712](https://github.com/Blecki/dwarfcorp/issues/712), [#758](https://github.com/Blecki/dwarfcorp/issues/758)
- `NAB` / wishlist: [#857](https://github.com/Blecki/dwarfcorp/issues/857), [#914](https://github.com/Blecki/dwarfcorp/issues/914)

## Fixed or mitigated in this fork

| Upstream | What we did |
|---|---|
| [#1016](https://github.com/Blecki/dwarfcorp/issues/1016) (`Null Reference while creating initial embarkment`) | Root cause was `Pumpking.cs` missing from `DwarfCorpFNA.csproj` — `EntityFactory` attribute never registered, `CreateEntity("Pumpking")` threw `KeyNotFoundException`. Fixed in `248676481` |
| [#1049](https://github.com/Blecki/dwarfcorp/issues/1049) (`Filtering dwarves in colony screen`) | Claimed fixed upstream in commit `45a1c3966 Fix bug in colony screen` (predates this fork). Not re-verified |
| [#1057](https://github.com/Blecki/dwarfcorp/issues/1057) (`project be built more easily`) | Solved by the FNA + MGCB migration in commit `248676481` |

## Still open and worth a look

Priority list for a follow-up "Phase C" bug hunt, same ranking as the plan
that produced this doc.

| # | Issue | Where | Notes |
|---|---|---|---|
| 1 | [#1000](https://github.com/Blecki/dwarfcorp/issues/1000) / [#1006](https://github.com/Blecki/dwarfcorp/issues/1006) | `WorldManager-Loading.cs`, serialization | Save-file crashes, tagged "Very common" |
| 2 | [#1018](https://github.com/Blecki/dwarfcorp/issues/1018) / [#1041](https://github.com/Blecki/dwarfcorp/issues/1041) | `VoxelListPrimitive.cs` | NullRef during voxel mesh build. Already wrapped in try/catch by commit `4366d3f` (in-fork); the root cause is a stale handle during build |
| 3 | [#1005](https://github.com/Blecki/dwarfcorp/issues/1005) / [#1027](https://github.com/Blecki/dwarfcorp/issues/1027) | `WorldGeneratorPreview.cs` | NullRef in `RegneratePreviewTexture` / `PreparePreview` |
| 4 | [#915](https://github.com/Blecki/dwarfcorp/issues/915) / [#923](https://github.com/Blecki/dwarfcorp/issues/923) / [#961](https://github.com/Blecki/dwarfcorp/issues/961) / [#1036](https://github.com/Blecki/dwarfcorp/issues/1036) | Bloom / LoadContent | Cluster around GPU device reset; may already be mitigated by FNA's device-loss behavior |
| 5 | [#981](https://github.com/Blecki/dwarfcorp/issues/981) | Saving | Autosave disabled after a manual save |
| 6 | [#999](https://github.com/Blecki/dwarfcorp/issues/999) / [#987](https://github.com/Blecki/dwarfcorp/issues/987) / [#1029](https://github.com/Blecki/dwarfcorp/issues/1029) | Save IO / deserialize | Same save-file cluster as #1 |
