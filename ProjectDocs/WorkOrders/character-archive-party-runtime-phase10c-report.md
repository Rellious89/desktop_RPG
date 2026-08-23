# Character Archive Party Runtime — Phase 10C

## Commits

- 10C-A: `f6e73a79` — Character Archive browse panel baseline
- 10C-B: `4ffae807` — Party-slot drag, composition, and archive UI refresh
- 10C-C: `HEAD` — This single implementation/report commit; it is recorded as the final commit in the delivery handoff.

## Runtime boundary

- `CharacterRoster.Entries` now follows `SaveData.partyCharacterIds` order and contains only owned, catalog-resolved, playable party members.
- Unknown, unowned, empty, duplicate, or unplayable party IDs are preserved in SaveData and excluded only from runtime Entries.
- A party refresh retains the active character while it remains in Entries; otherwise it applies the first available party member through the normal Runtime Actor path.
- Recruitment refreshes ownership without adding the character to `partyCharacterIds`.
- Character Swap therefore reads only the deployed party through `CharacterRoster.Entries`; dungeon access reads the highest deployed-party level through `IPartyCharacterLevelSource`.
- Recovery intentionally remains ownership-scoped: `CharacterRosterRecoveryAdapter` uses `CharacterRoster.OwnedCharacters`, so a non-party owned character remains recoverable.

## Verification

- Unity C# compilation: passed with zero compiler errors.
- Focused EditMode only (not the full suite):
  - `CharacterEditor.Tests.CharacterRosterCatalogTests`
  - `DungeonEditor.Tests.DungeonAccessTests`
  - 67 passed, 0 failed after the final focused additions.
- Coverage includes party order, invalid/unowned party IDs, non-party owned exclusion, retained/fallback active selection, no auto-join on recruitment, recovery ownership scope, and non-party high-level dungeon bypass prevention.
- `git diff --check`: passed.

## Compatibility and scope

- Save format remains `SaveData` v3; no migration or schema change was made.
- Scene changes: none.
- Prefab changes in 10C-C: none. (10C-B added only the PartyConfigCatalog reference to the existing Character Archive panel prefab.)
- No actual persistent-data save path was read or written during verification; tests injected in-memory SaveData and the targeted service tests use in-memory storage.
- No remote push was performed.
