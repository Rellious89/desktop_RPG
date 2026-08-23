# Recruitment UI State — Phase 9.3D-B

## Scope completed

- Kept recruitment UI hidden outside Town and during transitions, and kept `btn_Open_Inn` hidden while this controller owns the status UI.
- Preserved status priority: unreadable and locked states hide UI; a restored pending candidate shows the result before Waiting/Ready; a Ready state shows the draw standby.
- Progress uses UTC timestamps with a clamped total-duration fraction, realtime refresh, `HH:mm:ss` countdown, and percentage display.
- Draw is protected against duplicate/stale clicks and only invokes the candidate draw service when the current state is Ready with no pending candidate.
- Result display resolves the character catalog portrait, localized character name and origin world name, and New marker; Confirm/Cancel remain disabled.
- Scene wiring adds the controller and only the minimum references to the existing UIAnchor and authored prefab instances. No prefab layout or appearance overrides were changed.

## Verification

- Unity 2022.3.62f3 focused EditMode filter `RecruitmentEditor.Tests`: 57 passed, 0 failed.
- Unity compile: 0 C# errors (two pre-existing unused-field warnings only).
- `git diff --check`: passed.
- No `persistentDataPath` access, SaveData/version edits, generated/localization/CSV edits, remote push, or prefab-asset changes were made.
