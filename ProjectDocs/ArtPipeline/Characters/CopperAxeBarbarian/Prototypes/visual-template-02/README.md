# KeyBuddy LOW-A / LOW-B / LOW-C Template Prototype

## Status

- These are body-template candidates, not final character masters.
- All three use the same barbarian identity, outfit, palette and weapon concept.
- The candidates compare body proportion and proposed logical body height only.
- No animation pipeline has been selected by this prototype.

## Shared equipment model

- `Weapon`: rendered and replaced independently from the character outfit.
- `Outfit Set`: replaces the complete head-to-foot visual model while reusing the same animation anchors and animation set.
- Each outfit set must preserve the shared root, feet, hands and weapon anchors.

## Candidate definitions

### LOW-A

- Approximate body height: 80 logical pixels.
- Proportion: compact three-head-tall adventurer.
- Strongest adult warrior impression and most room for class detail.
- Highest animation and outfit cleanup cost among the three LOW candidates.
- Least mascot-like candidate.

Files:

- `low-a-candidate-source.png`
- `low-a-logical-80h.png`
- `low-a-logical-80h-preview.png`

### LOW-B

- Approximate body height: 64 logical pixels.
- Proportion: 2.5-head-tall companion.
- Large head and hands while preserving a clear warrior body.
- Best current balance between CatKnight-like charm, readable equipment and manageable production.
- Preliminary recommended candidate for the next motion prototype.

Files:

- `low-b-candidate-source.png`
- `low-b-logical-64h.png`
- `low-b-logical-64h-preview.png`

### LOW-C

- Approximate body height: 48 logical pixels.
- Proportion: two-head-tall mascot.
- Strongest desktop-companion identity and lowest theoretical animation burden.
- Small decorations cannot carry character identity at this scale.
- Tattoos, muscles, fur and harness details must be converted into large symbols and color blocks.
- The automatic 48px reduction is a stress test, not finished LOW-C pixel art.

Files:

- `low-c-candidate-source.png`
- `low-c-logical-48h.png`
- `low-c-logical-48h-preview.png`

## Comparison files

- `comparison-body-proportion-210px.png`: CatKnight and A/B/C normalized to the same 210px visible height.
- `comparison-body-proportion-210px-2x.png`: nearest-neighbor 2x inspection copy.
- `comparison-logical-density.png`: proposed logical body heights enlarged with integer nearest-neighbor scaling.
- `low-abc-source-chroma.png`: built-in image generation output.
- `low-abc-transparent.png`: locally extracted transparent sheet.

## Review order

1. Use `comparison-body-proportion-210px.png` to choose the preferred body proportion.
2. Use `comparison-logical-density.png` to judge how much detail survives the proposed logical height.
3. Do not select C solely from the automatic reduction quality. If C's proportion wins, redraw it specifically for 48px rather than shrinking B or A.
4. After one template is selected, create one cleaned master Idle sprite before testing animation.

## Next gate after selection

1. Clean one final Idle master on the chosen logical grid.
2. Define root, feet, hands and weapon anchors.
3. Separate both axes from the outfit set.
4. Produce a breathing Idle test.
5. Produce a chest-tap test.
6. Swap to one alternate weapon set without changing the body or anchors.
7. Produce one short attack test at the actual game display size.

## Production note

- The built-in image generation tool produced the three candidates together to preserve palette and identity.
- The flat chroma-key background was removed locally.
- `build_candidates.py` separates candidates, creates proposed logical-size stress tests and rebuilds the comparison images.

