# Copper Axe Barbarian — Visual Density Prototype 01

## Status

- These images are visual-direction prototypes, not master character assets.
- The current Barbarian design and the existing CatKnight are comparison references, not locked art standards.
- The three generated poses are for silhouette and motion-readability review. They are not animation-ready frames and must not be imported as a final sprite sequence.

## Product assumptions used

- KeyBuddy is a small desktop companion that is usually viewed near the bottom of the monitor.
- Pixel art is selected, but logical resolution and pixel density are not locked.
- Equipment is divided into two systems:
  - `Weapon`: replaceable independently from the character costume.
  - `Outfit Set`: replaces the complete visual model from head to foot while reusing the same animation set.
- A future outfit set must preserve the shared animation anchors even when its silhouette changes.

## Candidates

### LOW — simplified

- Compact, deliberately simplified body and material rendering.
- Large symbolic tattoo, simple armor masses and highly readable weapons.
- Best candidate for inexpensive weapon swapping and full-outfit replacement.
- Lowest cost for cutout, sprite-swap or hybrid animation.
- Would require the rest of KeyBuddy's character art to move toward the same simplified direction.

Files:

- `low-simple-prototype-sheet.png`
- `low-simple-source-chroma.png`
- `low-simple-transparent-raw.png`

### MID — 3px cluster

- Retains the muscular identity, tattoo, leather, fur and metal distinctions at companion scale.
- Better balance between character appeal and future modular production than the high-density version.
- Still needs a simplified joint/gear boundary design before animation production.

Files:

- `mid-prototype-sheet.png`
- `mid-source-chroma.png`
- `mid-transparent-raw.png`

### HIGH — source density

- Preserves the most anatomy and material detail.
- At a normalized 210px body height, much of the detail advantage over MID becomes small.
- Highest cleanup cost and most sensitive to rotation, joint seams, equipment overlays and generated-frame scale drift.

Files:

- `high-prototype-sheet.png`
- `high-source-chroma.png`
- `high-transparent-raw.png`

## Comparison output

- `comparison-at-210px.png`: 1x viewing-size comparison.
- `comparison-at-210px-2x.png`: nearest-neighbor 2x inspection copy.
- Every figure is normalized to a 210px visible body height. This compares readability, not final Unity PPU or world scale.

## Preliminary observation

- LOW creates a clearly different art direction and the strongest modular-production advantage.
- MID and HIGH look substantially closer to each other at the intended display size than they do when enlarged.
- HIGH therefore needs to prove a gameplay-visible advantage before accepting its higher production cost.
- The next motion prototype should use no more than the best two candidates selected from this sheet.

## Next gate

For each selected candidate only:

1. Lock one Idle master pose and shared body anchors.
2. Separate the weapon from the outfit set.
3. Produce a short breathing Idle test.
4. Produce a chest-tap test.
5. Replace the axes with one alternate weapon set without changing body scale.
6. Test one short attack at the actual in-game display size.

Do not decide the final PerfectPixel, cutout or frame-by-frame pipeline before this gate is reviewed.

## Generation note

- The sheets were generated with the built-in image generation tool on a flat chroma-key background.
- The background was removed locally and alpha was hardened for comparison.
- `build_previews.py` applies the comparison transforms and can rebuild the two comparison images.
- The original first LOW attempt is retained as `low-prototype-sheet.png`; it reduced pixel density without simplifying the underlying design and is not the recommended LOW candidate.

