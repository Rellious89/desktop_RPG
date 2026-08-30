# Character Story Quest Foundation — Phase 13B

## Local commits

- `f7d3c21c` `feat(quest): add character story quest table foundation`
- `a5c4d761` `feat(quest): persist story quest lifecycle in save v8`
- `f633a914` `feat(quest): include story progress in defeat transaction`
- `d8f02216` `fix(quest): wire story quest runtime bootstrap`

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

## Phase 13B wiring supplement

- Ran `Tools/Keybuddy/Table Data/Rebuild (Character Story Quest only)` in the open Unity Editor. It created both Generated folders, all three quest assets, all four objective assets, and both catalogs.
- Added exactly one `CharacterStoryQuestService` GameObject to `Assets/Scenes/desktopScene_ReSize.unity`, wired to the generated Quest Catalog, generated Objective Catalog, and the existing `StageVisualRoot` CharacterRoster.
- The service now disables itself with an error when any required reference is null; a focused scene test also rejects a missing service or missing catalogs.
- `DUNGEON_ENTER_COUNT` now observes `DungeonEntryService.DungeonEntered`, emitted by `FieldModeManager` only after a successful mode transition. `DungeonEnterRequested` remains a pre-transition request and is no longer used for quest progress.
- Recruitment acquisition now creates the root quest inside its existing one-save transaction and restores quest state if that save fails.
- Unity registered the pre-existing `09_Quest` localization assets in the three Addressables localization groups during the rebuild. This is required: generated quest definitions resolve their title/description through Shared Data GUID `11805744adb144cd3bb37f325635e0d9`, while the matching en/ko locale table asset GUIDs are registered in their locale groups. No localized title/description text was changed.

## UI handoff

The next UI phase should consume `CharacterStoryQuestService.GetSnapshot(characterId)` and show:

- all / owned characters' active quest or graduation state;
- active objective progress by objective ID and required value from the catalog;
- a completion button only when `ReadyToComplete` is true, wired to `TryConfirmComplete(characterId)`;
- a graduation marker when `Graduated` is true.

No prefab or production quest UI was created. The runtime scene wiring above was added; localization strings were not edited.

## Verification actually run

- `git diff --check`: passed before commits.
- Added narrow EditMode coverage for immediate state evaluation and multi-objective AND/progress clamping in `Assets/Editor/Quest/Tests/CharacterStoryQuestServiceTests.cs`; execution was blocked by the Unity project lock described below.
- Unity narrow EditMode invocation was attempted with the `CharacterStoryQuest` filter. Unity exited before compilation/tests because another Unity instance already has this project open, and therefore did not create the requested XML result.
- The generated `Assembly-CSharp.csproj` was also checked with `dotnet build --no-restore`; it could not start compilation because Unity's ignored `Temp/obj/Assembly-CSharp/project.assets.json` was not present. No package restore was run.
- Compile error count: not verified by Unity batch runner (not reported as zero).
- Full EditMode, PlayMode, and Sol suites were not run.

### Supplement verification actually run in the open Unity Editor

- `Tools/Keybuddy/Table Data/Rebuild (Character Story Quest only)`: completed successfully.
- Focused EditMode tests were individually run from Unity Test Runner and passed (3 / 3): root immediate evaluation, multi-objective AND/progress clamping, and `desktopScene_ReSize` service/catalog/roster wiring.
- The focused runner surfaced no Unity C# compile error (otherwise these tests could not have run). This is not a project-wide compilation audit; the batch runner remained unavailable because the project was already open.
- The approved-entry and acquisition-save-boundary changes were source-inspected: `NotifyDungeonEntered` is called only after `FieldModeManager.ApplyMode(FieldMode.Dungeon, dungeon)`, and recruitment captures/restores the quest mutation around its existing one-save action. They were not separately executed as part of the three focused tests.

## Safety / repository state

- No remote push was performed.
- No actual persistent-data-path save was read or written by this work.
- Generated quest assets and both catalogs were rebuilt successfully in the open Unity Editor.
