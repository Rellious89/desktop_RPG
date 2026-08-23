# Recruitment Pool Exhaustion UI — Phase 9.3F

## Scope

- Added `RecruitmentCandidateSelector.HasEligibleCandidate`: a read-only "is anyone left to draw" question that reuses the **exact same** filter rules as `CollectEligible`. Both now call the shared `PassesEntryRules` / `PassesCharacterRules` helpers, so the exhaustion verdict and the draw result cannot disagree. It rolls no random number and touches neither the save document nor the recruitment cycle.
- The new API allocates nothing per call: it walks the catalog's already-built `Entries` list and filters by `BelongsTo` inline instead of calling `EntriesFor` (which builds a fresh `List` on every call), and it skips the duplicate-character `HashSet` — dropping the dedup cannot change the answer, because the dedup check only runs *after* the per-entry rules, so the first surviving entry of any character always survives too.
- Ownership is answered by a single reusable `SaveDataOwnership` adapter held in one static field, which scans `SaveSystem.Data.characters` on demand with `StringComparison.Ordinal`. No list, `HashSet`, or ownership object is constructed per frame, and because nothing is cached the verdict flips in the **same frame** a character is registered.
- Extracted the screen decision into the pure static `RecruitmentUiController.ResolveState(phase, pendingCharacterId, hasEligibleCandidate)`, which encodes the required priority: hidden → pending result → exhausted → progress → standby.
- Centralized every show/hide into a single `Apply(RecruitmentUiState)` method. `dialog_Recruitment_Impossible` turning on necessarily turns Progress, Standby, Result, and `btn_Open_Inn` off, and the town / field-transition gate and every unreadable path now route through `Apply(Hidden)`, which hides the exhaustion dialog as well. `TryPosition` places the new root at the same projected inn anchor as the other three.
- Acquiring the last remaining candidate flips the screen to the exhaustion dialog immediately, because `Acquire` already calls `Refresh` after a successful save and the ownership adapter reads the updated document. Returning a candidate leaves no ownership, so that character becomes eligible again and the normal Progress/Standby state returns.

## Verification

- Unity 2022.3.62f3 focused EditMode filter `RecruitmentEditor.Tests`: **78 passed, 0 failed** (was 57 before this phase; 21 new cases).
- Unity script compilation: **0 C# errors**; only the four pre-existing `DesktopWindow` unused-field warnings (`TransparentWindowController.alwaysOnTop`, `TransparentWindowController.logLayoutHitTests`, `GlobalMouseWheelForwarder.useWheelForwarding`, `GlobalKeyboardHook.useGlobalHook`).
- Scene wiring proved by loading `desktopScene_ReSize.unity` headlessly and reading the controller's serialized fields: `exhaustedRoot` → `dialog_Recruitment_Impossible` (alongside the three existing roots).
- `git diff --check`: passed. `SaveData.CurrentSaveVersion` remains **2** (`Assets/Scripts/Common/SaveData.cs` untouched).
- Ran on an APFS clone of the project in a scratchpad; the throwaway wiring-check script existed only in the clone. `diff -r` confirmed the clone's `Assets/Scripts`, `Assets/Scenes`, and `Assets/Editor` matched the repository exactly apart from the generated `.meta` for the new test file, which was copied back.
- No whole-suite run, no PlayMode, no Sol verification, no `persistentDataPath` access, no localization/CSV/generated-asset edits, no prefab-asset changes, and no remote push.

## Focused test coverage

| Work-order check | Test |
| --- | --- |
| Eligible unowned character left → Progress/Standby unchanged | `RecruitmentUiStateTests.Waiting_WithCandidatesLeft_StaysOnProgress`, `Ready_WithCandidatesLeft_StaysOnStandby` |
| All candidates owned and non-duplicable → exhaustion UI | `NoCandidateLeft_ReplacesBothProgressAndStandby`, `RecruitmentSelectionTests.HasEligibleCandidate_AllOwnedWithoutDuplicates_IsFalse` |
| Owned but duplicate recruitment allowed → no exhaustion UI | `HasEligibleCandidate_OwnedButDuplicateAllowed_StaysTrue` |
| Pending candidate outranks exhaustion | `PendingCandidate_OutranksExhaustion`, `PendingCandidate_OutranksProgressAndStandby` |
| Returning a candidate restores the normal state | `ReturningTheCandidate_GoesBackToTheNormalRecruitmentState` |
| The verdict never rolls, saves, or changes the cycle | `HasEligibleCandidate_DoesNotTouchSaveData` |
| Outside town / during transition / locked hides the exhaustion UI too | `NonRunningPhases_HideEverything` for Locked/NotInitialized/Unreadable; the town and transition gate is upstream of `ResolveState` and routes to the same `Apply(Hidden)` |
| Exhaustion filter agrees with the draw filter under every ownership step | `HasEligibleCandidate_AlwaysAgreesWithCollectEligible` |

## Scene / localization scope

- Scene-only change in `Assets/Scenes/desktopScene_ReSize.unity`: **one added line**, the `exhaustedRoot` Inspector reference on the existing `RecruitmentUiController`.
- No authored layout, anchor, size, or visual property of `dialog_Recruitment_Impossible` or any other UI object was changed, and no localization entry was touched — `01_UI / 51` and its `lb_Recruitment_Impossible` label were already authored.
