# PartyConfig Table Runtime Foundation — Phase 10A-1

## Data correction (needs review)

The authored `PartyConfig.csv` row read `Default,3,1,기본 파티 설정` (capital **D**), while the work order specified `default` for both the data and the code constant. Every id comparison in this project is `StringComparison.Ordinal`, so the two are different keys and the lookup would never resolve. Per the decision taken before implementation, **the CSV was corrected to `default`** and `PartyConfigIds.Default` uses the same spelling. `party_config_id` also uses the standard id rule (`TableDataFieldRules.IdPatternText`), which does not accept uppercase — `Default` would have failed validation outright.

`PartyConfig.csv` and its `.meta` were not committed on `save-system` (they existed only in the working tree of the main checkout), so they are included in this commit.

## Scope

### Runtime — `Assets/Scripts/Party/`

- `PartyConfigDefinition`: `ConfigId`, `BaseCapacity`, `Enabled`, plus `IsValid` (id present **and** capacity ≥ 1). `BaseCapacity` returns the authored value unchanged — nothing is clamped or repaired at runtime, because the importer already rejects out-of-range values instead of correcting them.
- `PartyConfigCatalog`: active-definition list in authored CSV order, `Find` by ordinal id, `Count`, and `MarkDirty`. It drops empty slots, definitions without an id, definitions whose capacity is below the minimum, and duplicate ids (first authored row wins), logging each. **Lookups change nothing** — only an in-memory validity cache is built; no serialized value, save document, or file is touched.
- `PartyKeys.cs`: `PartyConfigIds.Default = "default"` and `PartyConfigRules.MinimumBaseCapacity = 1`, so the table validator and the runtime check read the same constant instead of two copies drifting apart.
- No party save data, roster, join/leave service, or UI was added — this phase stops at "the table can be read".

### TableData pipeline

Extended the existing thirteen-table pipeline to fourteen, following the Building/Recruitment pattern exactly:

- `TableDataPaths`: fixed input path `Assets/TableData/Game/PartyConfig.csv`, output folder `Assets/Generated/TableData/PartyConfig`, asset prefix, catalog name, and both path builders.
- `TableDataColumns.PartyConfig`: exactly `party_config_id, base_capacity, enabled, memo` in that order. No `display_order` and no name reference — this table has neither, and invented columns are not added.
- `PartyConfigRow` and the snapshot list/`Ordinal` dictionary.
- `TableDataValidator.ValidatePartyConfigs`: required standard id, ordinal duplicate rejection, `base_capacity` via `TryReadIntAtLeast(..., 1, ...)` (a bad value is an **error**, never silently raised to the minimum), and the existing `enabled` 0/1 rule. `memo` is never read into the snapshot. Unknown columns and header-order changes are handled by the unchanged shared header policy.
- Output-side checks: duplicate-generated, output-path conflict, and orphan reporting for the PartyConfig folder, all gated on the scope's selected-folder set like every other domain.
- `TableDataRebuildScope.PartyConfigTable` with `IncludesPartyConfigTable`, its own targets struct, `RebuildPartyConfigTable`, `WritePartyConfig`, folder creation, and serialized-layout verification (field names plus `int` / `bool` type checks). The narrow scope reads **nothing** from other domains — this table references no other table, so there is no reference to relink.
- `Tools/Keybuddy/Table Data/Rebuild (PartyConfig only)` menu entry; the full `Rebuild` includes PartyConfig.

### Generated assets

```
Assets/Generated/TableData/PartyConfig/PartyConfig_default.asset   (configId: default, baseCapacity: 3, enabled: 1)
Assets/Generated/TableData/PartyConfig/PartyConfigCatalog.asset    (configs: [PartyConfig_default])
```

### Two existing guard tests updated

`BuildingTableOutputTests.EveryDeclaredScope_IsSupported` and `CharacterTableOutputTests` (declared-scope list and `AllOutputFolders`) enumerate every rebuild scope and generated folder on purpose, so that adding a scope forces a conscious review. They were extended with `PartyConfigTable` / `PartyConfigOutputFolder`, with the reason recorded inline: this table points at nothing, so it cannot silently erase a reference.

## Verification

- Unity 2022.3.62f3 focused EditMode, filter `PartyConfigTableTests` + the two affected guard suites: **69 passed, 0 failed** (22 of them the new PartyConfig tests).
- Unity script compilation: **0 C# errors**; only the four pre-existing `DesktopWindow` unused-field warnings (`TransparentWindowController.alwaysOnTop`, `TransparentWindowController.logLayoutHitTests`, `GlobalMouseWheelForwarder.useWheelForwarding`, `GlobalKeyboardHook.useGlobalHook`).
- **Narrow rebuild isolation proven empirically**: ran `Rebuild (PartyConfig only)` headlessly on a clone, then `diff -rq` of the whole `Assets/Generated` tree against the repository. The only difference was the newly created `PartyConfig` folder — **not one byte of any other generated domain changed**. The run reported "새로 만든 에셋 2개, 갱신한 에셋 0개", validating all fourteen tables with 0 errors.
- `git diff --check`: passed.
- `SaveData.CurrentSaveVersion` remains **2**; `SaveData.cs` was not touched, and no `partyCharacterIds` field or v2→v3 migration was added.
- All Unity work ran on APFS clones in a scratchpad; `diff -r` confirmed each clone's `Assets/Scripts`, `Assets/Editor`, `Assets/Generated`, and `Assets/TableData` matched the repository exactly. No `persistentDataPath` access, no whole-suite EditMode run, no PlayMode, no Sol verification, no scene/prefab/localization edits, and no remote push.

## Focused test coverage

| Work-order check | Test |
| --- | --- |
| CSV path and exact header | `Paths_AreTheAgreedLocations`, `Schema_IsExactlyTheAgreedColumns`, `Schema_HasNoInventedColumns` |
| Real `default / 3 / enabled` row parses | `LiveCsv_HasExactlyTheDefaultRowWithCapacityThree`, `LiveCsv_HasNoErrors` |
| Empty id, duplicate id | `Validation_RejectsEmptyAndDuplicateIds` |
| Zero / negative / non-integer capacity rejected | `Validation_RejectsBadCapacityWithoutSilentlyFixingIt` (0, -1, 2.5, "three", empty), `Validation_AcceptsTheMinimumCapacity` |
| Only enabled rows in the catalog | `GeneratedAssets_OnlyContainTheEnabledRows`, `Catalog_DoesNotFilterByEnabledItself` |
| Id lookup, and lookups change nothing | `Catalog_LookupIsOrdinalAndChangesNothing`, `Catalog_DropsEmptyInvalidAndDuplicateEntries` |
| Generated asset and catalog values | `GeneratedAssets_MatchTheLiveCsv`, `SerializedLayout_MatchesWhatTheImporterWrites` |
| PartyConfig-only rebuild leaves other generated domains alone | `Scope_PartyConfigOnlyWritesItsOwnFolder`, `Scope_FullRebuildStillIncludesPartyConfig`, plus the `diff -rq` evidence above |
| Code constant matches the table | `LiveCsv_IdMatchesTheCodeConstantExactly`, `IdFormat_UsesTheStandardRule` |

The rejection cases run the importer's own `ValidatePartyConfigs` over in-memory tables, so no CSV or project file is written by the tests.
