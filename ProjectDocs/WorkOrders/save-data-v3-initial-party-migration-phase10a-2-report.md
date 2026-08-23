# KeyBuddy 10A-2 — SaveData v3 initial party migration

`SaveData` v3 separates owned character state (`characters`) from the ordered deployed party (`partyCharacterIds`).

- `v2 -> v3` ignores any accidental v2 party value and selects at most three first-occurring, non-empty character IDs from the existing `characters` order. The step reads no catalog or PartyConfig asset; unknown IDs are preserved verbatim.
- Normalization keeps party order while removing null/empty IDs, later Ordinal duplicates, and IDs without an owned character state. Migration working copies and character-reset rollback include the party list.
- Focused EditMode coverage passed: migration 90, save integration 21, reset 21, and affected version checks 151 (283 total). Unity C# compile errors: 0.

## 10B boundary

This phase neither applies PartyConfig capacity nor changes roster, swap, recovery, scene, or UI behavior. Party join/leave/swap rules and runtime consumption begin in 10B.
