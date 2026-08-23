# KeyBuddy 10B — Party composition service

## Changes

- Added `Assets/Scripts/Party/PartyCompositionService.cs` and isolated EditMode coverage in `Assets/Editor/Party/Tests/PartyCompositionServiceTests.cs`.
- The service reads `PartyConfigIds.Default`, supports a narrow future capacity-bonus provider (currently zero), and changes only `SaveData.partyCharacterIds`.
- Join, leave, replace, and move use one-save transactions. A false or throwing save restores the original party list and save metadata; nested calls return `Reentrant`.

## Result codes

`Success`, `NoSaveData`, `ConfigurationMissing`, `ConfigurationInvalid`, `InvalidCharacterId`, `NotOwned`, `AlreadyInParty`, `NotInParty`, `CapacityReached`, `MinimumPartySize`, `InRecovery`, `InvalidIndex`, `NoChange`, `SaveFailed`, `Reentrant`, and `InvalidPartyData`.

## Verification

- `PartyCompositionServiceTests`: 13 / 13 passed.
- Unity C# compile errors: 0. `git diff --check`: passed.
- `SaveData.CurrentSaveVersion` remains 3. No scene, prefab, localization, CSV, generated asset, roster/UI, recovery behavior, or persistent user save-path changes were made.

Commit hash is recorded by the single Git commit that contains this report.
