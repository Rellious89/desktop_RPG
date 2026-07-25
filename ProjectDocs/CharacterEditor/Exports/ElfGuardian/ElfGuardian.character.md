# ElfGuardian — Character Sheet

- Display name: 나뭇잎 글레이브 엘프 / Leaf Glaive Elf
- Type: character · World: `HUMAN-FANTASY-01` v1
- Aliases: LeafGlaiveElf, Elfguardian
- Species: elf · Role: Glaive warrior / long-reach melee · Status: master
- Concept: A blond elf warrior in light forest clothing wielding a straight-shaft leaf glaive.

## Body & Proportions

| Field | Value | Origin |
|---|---:|---|
| Stature | tall | Override |
| Target logical height | 91px | Override |
| Build | slender | Override |
| Proportion | humanoid-sd-2.5-head | Override |
| Species scale | 1 | Override |
| Head / hand / foot / torso | m / m / m / normal | mixed |

## Look

- Physical traits: Long pointed ears; Tall slender humanoid stature
- Hair / eyes / skin: Blond / N/A / Light
- Clothing: Light forest cloth; Leaf decorations
- Materials: Cloth; Wood; Metal
- Decorations: Leaf motifs
- Invariants: Long exposed pointed ears; Straight white glaive shaft; One curved forward blade; Blunt yellow rear ornament
- Forbidden: Bow; Shield; Heavy plate; Double-ended blade; Weapon longer than 1.25 body heights

## Weapon & Equipment

- Weapon: glaive, large, count 1
- Hands: both / both
- Structure: One straight shaft, one front blade, blunt rear ornament
- Allowed families: glaive, polearm
- Secondary: None

## Production & Canvas

- Base canvas: 512×512
- Large-motion canvas: 1024×1024
- Logical block: 3×3
- Pivot: forward-foot-contact
- PPU: 200
- Unity visual scale: 1
- Layers: character-or-outfit → weapon → effect

## Validation Summary

- **WARNING** `large-weapon-canvas`: Estimated weapon occupancy is 65%.
- **WARNING** `large-motion-canvas-exception`: Large-motion canvas differs from the base 512 policy. — Approved exception

## Approved Exceptions

- `large-motion-canvas-exception`: Existing ElfGuardian attack frames are approved 1024×1024 observed assets.

## Comparison Snapshot (vs VenomCultist)

| Metric | Actor | Reference | Delta |
|---|---:|---:|---:|
| Stature | tall | average | — |
| Logical height | 91 | 70 | +21 (30.0%) |
| Build | slender | normal | — |
| Proportion | humanoid-sd-2.5-head | humanoid-sd-2.5-head | — |
| headSize | m | m | — |
| handSize | m | m | — |
| footSize | m | m | — |
| torsoWidth | normal | normal | — |
| Species scale | 1 | 1 | +0 (0.0%) |
| Weapon occupancy | 65 | 35 | +30 (85.7%) |
| Base canvas | 512×512 | 512×512 | — |
| Large-motion canvas | 1024×1024 | same-as-base | — |

## Calculated Values and Interpretations

- Height relative to world baseline: 30.0%
- Weapon logical length estimate: 109.2
- Species scale is independent of stature and logical height.
- Unity visual scale is a runtime display transform and is normally 1.0.
- Canvas pixels, logical silhouette pixels, and logical pixel block size are distinct measurements.

## Schema

- schemaVersion: `1.0.0`
- worldTemplateRef: `HUMAN-FANTASY-01` v1

