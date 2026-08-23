# Character Archive Party Runtime — Phase 10C-E

- Drag preview: list cards and deployed-party cards now create one transparent, non-raycast preview under the root Canvas and remove it on every end/cancel path.
- Remove button: occupied enabled slots show `btn_remove` whenever the saved party contains two or more members; active/recovery restrictions remain in the existing click path.
- Verification: focused `CharacterEditor.Tests.PartySlotViewTests` passed 3/3; Unity C# compilation completed with zero errors; `git diff --check` passed.
- Compatibility: SaveData v3 unchanged; no persistent-data access or remote push; no scene/prefab layout changes.
