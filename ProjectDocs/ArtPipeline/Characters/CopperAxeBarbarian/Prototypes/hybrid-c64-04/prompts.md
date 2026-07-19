# Image generation prompt

Mode: built-in image generation with two local reference images. The output was generated on a chroma-key background and made transparent locally.

## Reference roles

- Image 1: `class-lineup-03/low-c-lineup-transparent.png` — primary identity, order, silhouette, palette, pose, and two-head proportion reference
- Image 2: `class-lineup-03/low-b-lineup-transparent.png` — secondary readable-detail and pixel-density reference

## Final prompt

```text
Use case: stylized-concept
Asset type: KeyBuddy pixel-art character lineup prototype, hybrid C64 density test
Primary request: Create one clean lineup containing exactly the same six characters from the reference images. Preserve the cute two-head-tall mascot proportions and compact desktop-companion appeal of Image 1, but use the clearer equipment readability and higher pixel-information density of Image 2. This is not a taller B-proportion lineup; every character must remain short, round, and about two heads tall like Image 1.
Input images: Image 1 is the primary reference for character identities, exact order, silhouettes, poses, proportions, palette, and cute expression style. Image 2 is a secondary reference only for the amount of readable detail and crisp pixel clustering appropriate to a roughly 64-logical-pixel-tall character.
Scene/backdrop: perfectly flat solid #00ff00 chroma-key background for local background removal; no floor or shadows.
Subjects, exactly left to right:
1. muscular copper-skinned male barbarian with braided black hair, beard, tattoos, one shoulder guard secured by a diagonal leather chest strap, and one axe in each hand;
2. biped black cat mage in a dark purple robe holding one wooden staff;
3. short chubby male cleric wearing a ceremonial mitre-like crown and holding a closed holy book;
4. woman in a navy police-style uniform and cap holding one small handgun pointed safely downward, with generic insignia and no real logos;
5. woman in a black-and-white maid dress and headband, both hands empty;
6. friendly orange quadruped tiger with no clothing and no weapon.
Style/medium: deliberately authored crisp pixel art; cute RPG mascot sprites; chunky readable pixel clusters; hard staircase edges; one-logical-pixel dark outline; compact limited palette around 10–18 colors per character; clear face and gear separation; no soft painting.
Composition/framing: one horizontal lineup; exactly six complete isolated full-body characters; equal visual scale and spacing; neutral idle stance; no overlap; generous outer padding.
Logical-pixel intent: design each character as though its visible body height were approximately 64 logical pixels, then enlarged with nearest-neighbor scaling. Preserve crisp uniform pixel blocks. Do not simulate detail by tiny noisy pixels.
Constraints: preserve the six identities, exact order, main colors, equipment, and C-style two-head proportions; keep both barbarian axes fully visible; keep every character fully inside the image; no added characters; no labels; no text; no watermark.
Avoid: B-style taller 2.5-head anatomy; realistic anatomy; anti-aliasing; blur; gradients; subpixel details; mixed pixel sizes; noisy single-pixel speckles; cropped weapons; extra props; shadows.
Background constraints: the background must be one perfectly uniform #00ff00 color with no gradients, texture, reflections, floor plane, cast shadow, or lighting variation. Do not use #00ff00 anywhere in the characters.
```
