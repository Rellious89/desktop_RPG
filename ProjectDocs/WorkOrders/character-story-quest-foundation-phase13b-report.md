# Character Story Quest Foundation — Phase 13B

## Local commits

- `f7d3c21c` `feat(quest): add character story quest table foundation`
- `a5c4d761` `feat(quest): persist story quest lifecycle in save v8`
- `f633a914` `feat(quest): include story progress in defeat transaction`

## Delivered scope

- Corrected the CatKnight three-step CSV chain: canonical quest IDs, full previous IDs, and `CatKnight_10003` as `is_final=1`. Localization text remains untouched.
- Added independent Character Story Quest / Objective definitions and catalogs.
- Added a dedicated table pipeline that parses both CSV files, validates IDs, chains, roots, cycles, final-node contracts, condition targets, duplicate target IDs, localization references, and generates only:
  - `Assets/Generated/TableData/CharacterStoryQuest`
  - `Assets/Generated/TableData/CharacterStoryQuestObjective`
  Existing Generated domains are never written by this rebuild.
- Added `SaveData` v8 story state: active quest, per-objective progress, completed IDs, ready-to-complete, and graduation. Migration `v7 -> v8`, normalization, deep migration copy, reset rollback, character removal cleanup, and all-reset cleanup include this state.
- Added the runtime read-only snapshot and explicit `TryConfirmComplete(characterId)` API. Objectives are ANDed; ready state never auto-completes; confirmation atomically advances to the next quest or records graduation.
- Added level evaluation on activation/state change, accepted dungeon-entry counting per occupied saved party slot, and defeat/stamina counting inside the existing single defeat save transaction. Quest progress uses the actual stamina reduction and rolls back with rewards, EXP, stamina, corruption, and save metadata on a failed save.

## UI handoff

The next UI phase should consume `CharacterStoryQuestService.GetSnapshot(characterId)` and show:

- all / owned characters' active quest or graduation state;
- active objective progress by objective ID and required value from the catalog;
- a completion button only when `ReadyToComplete` is true, wired to `TryConfirmComplete(characterId)`;
- a graduation marker when `Graduated` is true.

No scene, prefab, or production quest UI was created. Localization assets were not edited.

## Verification actually run

- `git diff --check`: passed before commits.
- Unity narrow EditMode invocation was attempted with the `CharacterStoryQuest` filter. Unity exited before compilation/tests because another Unity instance already has this project open, and therefore did not create the requested XML result.
- The generated `Assembly-CSharp.csproj` was also checked with `dotnet build --no-restore`; it could not start compilation because Unity's ignored `Temp/obj/Assembly-CSharp/project.assets.json` was not present. No package restore was run.
- Compile error count: not verified by Unity batch runner (not reported as zero).
- Full EditMode, PlayMode, and Sol suites were not run.

## Safety / repository state

- No remote push was performed.
- No actual persistent-data-path save was read or written by this work.
- Generated quest assets were not rebuilt because Unity was already open; the new pipeline will create them deterministically from the two CSVs when run in the editor.
