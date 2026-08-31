# Character Story Quest Scroll Stability — Phase 13E

## Implementation commit

- `a30a17cc` `fix: stabilize character story quest objective scroll`

## Delivered scope

- `CharacterStoryQuestUiController` retains runtime QuestType and QuestDescription text instances as per-panel pools. Repeated or same-frame refreshes update indexed instances, and surplus lines are disabled instead of deferred for destruction.
- Re-entrant localization refreshes coalesce into one synchronous final pass. The panel therefore renders current data immediately while ending in one deterministic line set.
- Objective child layout groups are rebuilt before the top-level Content. Content is then rebuilt and canvas layout is flushed so ScrollRect receives the updated height.
- Scroll position is preserved across ordinary refreshes. It returns to the top only when the selected character or active quest changes.
- `ObjectiveScroll/Viewport` now has a fully transparent raycast-target `Image` while retaining `RectMask2D`; empty viewport space now delivers wheel and drag input to ScrollRect.
- The prefab setup utility applies the same viewport input Graphic to future regenerated prefab wiring. Reward hierarchy and `InventorySlotView` references were left unchanged.

## Verification

- Focused Unity EditMode suite: `CharacterArchiveEditorTests` — **23 passed, 0 failed**.
  - Includes 2-objective → 1-objective → 2-objective line-pool coverage, repeated-pass duplicate checks, variable-height Content rebuild / ScrollRect movement, prefab scroll references, mask, raycast Graphic, and existing reward-slot wiring assertions.
- Unity batch compilation completed before the suite with **0 C# compile errors**.
- `git diff --check`: passed.
- `SaveData.CurrentSaveVersion` remains **8**.

## Safety and repository state

- Test execution used a temporary detached worktree so the currently open Unity project was not touched.
- No remote push was performed.
- No actual `persistentDataPath` was read or written.
- At report creation, the implementation commit is present locally on `save-system`; this report is committed separately next.
