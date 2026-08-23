# Recruitment Candidate Resolution — Phase 9.3E

## Scope

- Added `RecruitmentCandidateResolutionService`: acquire adds one initial `CharacterSaveState` and clears the pending id in one save; return clears the pending id and halves only remaining time in one save.
- Both commands reject missing pending ids, duplicate ownership, invalid catalog candidates, and reentry without saving. A false or throwing save restores pending/timing/list shape and save metadata.
- Connected only the existing Result `btn_confirm` and `btn_cancel` through `RecruitmentUiController`; successful actions refresh the official roster, character selection, and recovery surfaces, then show `01_UI / 48` or `01_UI / 49` using the localized character name.
- The acquisition refresh rebuilds catalog-backed entries without changing the active combat character. The recovery adapter rereads roster entries so a newly acquired character is immediately available to recovery UI.

## Verification

- Unity 2022.3.62f3 focused EditMode filter `RecruitmentEditor.Tests`: **64 passed, 0 failed**.
- Unity script compilation: **0 C# errors**; two pre-existing unused-field warnings (`GlobalKeyboardHook.useGlobalHook`, `GlobalMouseWheelForwarder.useWheelForwarding`).
- `git diff --check`: passed.
- No persistent data path was read, no whole-suite/EditMode Run All/PlayMode/Sol or cross-review run was used, and no CSV, localization, generated assets, prefab visuals, or remote push were changed.

## Scene / prefab scope

- Scene-only wiring in `Assets/Scenes/desktopScene_ReSize.unity`: existing Recruitment controller gets the existing `01_UI` keys 48 and 49.
- No recruitment prefab asset or authored layout/visual property was changed.
