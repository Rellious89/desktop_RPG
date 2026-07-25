# VenomCultist — Character Sheet

- Display name: 독단검 이교도 / Venom Cultist
- Type: monster · World: `HUMAN-FANTASY-01` v1
- Aliases: None
- Species: human · Role: Poison-dagger cultist · Status: active
- Concept: A living human cultist hidden in a dark-purple hood, carrying one poisoned dagger.

## Body & Proportions

| Field | Value | Origin |
|---|---:|---|
| Stature | average | Override |
| Target logical height | 70px | Override |
| Build | normal | Override |
| Proportion | humanoid-sd-2.5-head | Override |
| Species scale | 1 | Override |
| Head / hand / foot / torso | m / m / m / normal | mixed |

## Look

- Physical traits: Living human; Face almost fully hidden in hood shadow
- Hair / eyes / skin: N/A / Hidden / Mostly hidden
- Clothing: Deep dark-purple hooded robe
- Materials: Cloth; Bone; Metal; Poison
- Decorations: Large bone-white skull pendant
- Invariants: Hood shadow hides face; One centered skull pendant; Exactly one short poisoned dagger
- Forbidden: Undead body; Second weapon; Staff; Shield; Heavy armor

## Weapon & Equipment

- Weapon: dagger, small, count 1
- Hands: anatomical-right / none
- Structure: One short poison-coated dagger
- Allowed families: dagger
- Secondary: Skull pendant

## Production & Canvas

- Base canvas: 512×512
- Large-motion canvas: same as base
- Logical block: 3×3
- Pivot: forward-foot-contact (0.5234375, 0.2578125; measured)
- PPU: 200
- Unity visual scale: 1
- Layers: character-or-outfit → weapon → effect

## Validation Summary

- None

## Approved Exceptions

- None

## Comparison Snapshot (vs ElfGuardian)

| Metric | Actor | Reference | Delta |
|---|---:|---:|---:|
| Stature | average | tall | — |
| Logical height | 70 | 91 | -21 (-23.1%) |
| Build | normal | slender | — |
| Proportion | humanoid-sd-2.5-head | humanoid-sd-2.5-head | — |
| headSize | m | m | — |
| handSize | m | m | — |
| footSize | m | m | — |
| torsoWidth | normal | normal | — |
| Species scale | 1 | 1 | +0 (0.0%) |
| Weapon occupancy | 35 | 65 | -30 (-46.2%) |
| Base canvas | 512×512 | 512×512 | — |
| Large-motion canvas | same-as-base | 1024×1024 | — |

## Calculated Values and Interpretations

- Height relative to world baseline: 0.0%
- Weapon logical length estimate: N/A
- Species scale is independent of stature and logical height.
- Unity visual scale is a runtime display transform and is normally 1.0.
- Canvas pixels, logical silhouette pixels, and logical pixel block size are distinct measurements.

## Schema

- schemaVersion: `1.0.0`
- worldTemplateRef: `HUMAN-FANTASY-01` v1

