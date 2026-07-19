# KeyBuddy LOW-B vs LOW-C Multi-Character Lineup

## Status

- These images compare art direction across several character types.
- They are not final character masters or animation-ready sprites.
- Small religious, uniform and costume details are provisional concept choices.
- The character order is identical in B and C.

## Lineup

1. Dual-axe Barbarian
2. Black Cat Mage with staff
3. Short chubby Cleric with ceremonial headpiece and holy book
4. Female Police Officer with a downward-held handgun
5. Unarmed Female Maid
6. Quadruped Tiger

## LOW-B observation

- Proposed logical body height: 64px.
- Preserves occupation, outfit and held-item information most reliably.
- Faces remain cute while bodies still communicate age, build and profession.
- Better space for weapon variation and full-outfit-set differences.
- Reads more like a compact RPG cast than a pure mascot cast.

## LOW-C observation

- Proposed logical body height: 48px.
- Creates the strongest unified desktop-companion identity.
- Humans, anthropomorphic animals and quadruped animals coexist naturally in the same style.
- Class recognition remains strong in the generated source lineup.
- The 48px logical stress test loses small surface details, but the large silhouettes and props survive.
- A final C asset must be designed directly for 48px instead of being reduced from B artwork.

## New option revealed by the lineup

The comparison suggests a possible hybrid candidate:

- `LOW-C proportion + LOW-B 64px logical height`

This would keep C's two-head-tall mascot appeal while retaining more room for faces, weapons and outfit-set details. It is not yet generated or selected; it should be tested only if neither pure B nor pure C is clearly preferred.

## Equipment implications

- B provides more room for visually distinct weapons and outfit materials.
- C requires weapons to use very different silhouettes rather than small decorations.
- C outfit sets should communicate identity through head shape, dominant color and one large accessory.
- Both styles can use the planned `Weapon` and `Full Outfit Set` replacement model.

## Comparison files

- `comparison-low-b-vs-c-source-210px.png`: generated source art normalized to a 210px visible height.
- `comparison-low-b-vs-c-logical.png`: B at 64px and C at 48px, enlarged with integer nearest-neighbor scaling.
- `low-b-lineup-source-chroma.png`: original built-in generation result for B.
- `low-c-lineup-source-chroma.png`: original built-in generation result for C.
- `low-b-lineup-transparent.png`: transparent B sheet.
- `low-c-lineup-transparent.png`: transparent C sheet.

Each character is also saved separately as:

- `low-{b|c}-{character}-source.png`
- `low-{b|c}-{character}-logical-{64|48}h.png`
- `low-{b|c}-{character}-logical-{64|48}h-preview.png`

## Review gate

Choose one of the following before producing animation:

1. LOW-B proportion at 64px.
2. LOW-C proportion at 48px.
3. One additional hybrid test: LOW-C proportion at 64px.

After the body template is selected, create only one cleaned master character and verify Idle, weapon replacement and one attack before expanding the roster.

## Production note

- The built-in image generation tool created B first.
- The B lineup and the existing C barbarian were used as references to create the corresponding C lineup.
- Chroma-key removal, character separation, logical resizing and comparison boards were performed locally.
- `build_lineup_comparison.py` rebuilds the individual assets and comparison boards.

