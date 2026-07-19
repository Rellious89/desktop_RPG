# Hybrid C64 prototype

This prototype tests **LOW-C's two-head mascot proportions on a 64px logical grid**.

It is intentionally separate from the earlier LOW-B and LOW-C definitions:

- body proportion: LOW-C (about two heads tall)
- logical character height: 64px canvas, roughly 60px visible content
- equipment and face readability target: LOW-B level
- display scaling: integer nearest-neighbor only

## What “logical resolution” means

Logical resolution is the number of real source pixels used to describe the sprite. A 64px-tall logical sprite displayed at 5x becomes 320px tall on screen, but it still contains only 64 rows of original pixel information. Each logical pixel becomes a clean 5×5 display block.

The generated concept source is not itself a true 64px master. The `logical-clean-64h` files are automated conversion tests using area reduction, a limited palette, and hard alpha edges. They are useful for choosing a direction, but final production sprites should be cleaned or authored directly on the selected logical grid.

## Main files

- `hybrid-c64-lineup-source-chroma.png`: built-in image generation output
- `hybrid-c64-lineup-transparent.png`: locally removed background
- `comparison-hybrid-c64-source-vs-logical.png`: clean concept vs actual 64px test
- `comparison-c48-vs-hybrid-c64-logical.png`: previous C48 vs hybrid C64
- `hybrid-c64-*-logical-clean-64h.png`: per-character 64px test sprites
- `hybrid-c64-*-logical-clean-64h-preview.png`: 6x nearest-neighbor inspection images
- `hybrid-c64-*-logical-nearest-64h*.png`: unfiltered nearest-resize controls

The `logical-clean` set is the recommended comparison set. The nearest-only
control retains a few chroma fringes and unstable micro-colors from the generated
source, while the clean set limits the palette and hardens the alpha edge.

## Reading the result

If the C64 row keeps the C lineup's charm while making faces, hands, weapons, and costume borders easier to read, this is the strongest candidate for the KeyBuddy base template. If it still feels too dense at the actual in-game display size, the next test should change palette/detail rules rather than reduce the grid immediately.
