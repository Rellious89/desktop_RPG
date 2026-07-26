# KeyBuddy Character Editor

A standalone, project-local specification editor for KeyBuddy world templates and actors. It separates stature, logical height, build, proportions, species scale, and Unity display scale; validates conflicts; compares same-world actors; and exports deterministic Schema v1 JSON and Markdown. It does not generate images or modify Unity assets.

## Requirements

- Node.js 20 or newer (verified with Node 24.16.0)
- npm
- A current desktop browser

No account, remote service, or Unity Editor session is required. The development server listens only on `127.0.0.1`.

## Launch

From the repository root:

```bash
cd Tools/CharacterEditor
npm install
npm run dev
```

Open the printed loopback URL, normally <http://127.0.0.1:5173/>. Stop the server with `Ctrl+C`.

For a production build:

```bash
cd Tools/CharacterEditor
npm run build
npm run preview
```

## Use

The Library contains three bundled world templates and the ElfGuardian and VenomCultist samples.

1. Open a bundled actor, or select **+ 새 액터** and choose a world and actor type.
2. Complete the Identity, Body & Proportions, Look, Weapon & Equipment, and Production sections.
3. Inheritable fields show whether the value comes from the world or an actor override. Use **World 기본값으로 되돌리기** to remove an override.
4. Review the Validation panel. Blocking errors prevent export. An overridable error may be approved only with a reason of at least 10 characters; the diagnostic and exception remain in exports.
5. Use **같은 세계 액터와 비교** to compare size, proportions, extremities, species scale, weapon occupancy, and canvases.
6. Select **Export 미리보기**, review Markdown/JSON, and download both files.

Actor edits autosave to browser `localStorage` after a short delay. **초안 저장** saves immediately. Drafts appear on the Library screen under **내 초안** and remain local to that browser profile.

Use **JSON 가져오기** to paste or choose a Schema v1 actor, world template, or actor export envelope. Export-envelope import extracts the authored actor so it can be edited and exported again. Unknown schema versions and invalid fields are rejected with field-level errors.

## Data and exports

Checked-in canonical examples live at:

```text
ProjectDocs/CharacterEditor/Schema/
ProjectDocs/CharacterEditor/Data/worlds/
ProjectDocs/CharacterEditor/Data/actors/
ProjectDocs/CharacterEditor/Exports/
```

Browser downloads do not write into those repository folders automatically. Move reviewed downloads into the appropriate project folder through Finder/Git as a deliberate step.

Important ElfGuardian semantics:

- Target physical height: `273px` (the authoritative body size)
- Target logical height: `91px` — derived as `273 ÷ 3` at 3×3 Standard density
- Species scale: `1.0`
- Unity visual scale: `1.0`
- Legacy `1.3`: historical production-sizing evidence only

## Body size and pixel density

Body size and pixel density are separate settings.

- **Target physical height** is how tall the character is, in image pixels of the
  production base. This is what you author.
- **Pixel density** is how many image pixels make up one logical pixel:
  `3×3 Standard` (KeyBuddy default) or `2×2 Detail`.
- **Target logical height** is derived — `physical ÷ block` — and is shown
  read-only. A 195px character is 65 logical px at 3×3 and 98 at 2×2. It is the
  same size on screen either way; only the pixel grid gets finer.

New actors pick a density on the **+ 새 액터** screen. Choosing one other than
the world default records an override that pins the world's physical height, so
the new actor matches its peers in size.

Existing documents carry only a logical height. They are back-calculated as
`logical × block`, which round-trips exactly, so no existing resource changes
size. Both heights are written on save, so tools that read only
`targetLogicalHeightPx` keep working.

## View and direction

Every first-generation KeyBuddy master is drawn the same way:

```text
Projection:      three-quarter (front-biased)
Facing:          screen-right
Light direction: upper-left
```

The World Template owns those three values under **승인 시점 & 픽셀 스타일**, and
every actor in the world inherits them; there is no per-actor direction control.
Actors show the inherited values read-only in the **Production** section and in
**Export 미리보기**.

Exports record the resolved direction so the design master can be generated from
the character sheet alone:

- JSON — `resolved.view` carries all three values, `fieldOrigins["view.*"]`
  names the document each came from, and `calculated.canonicalView` repeats them
  flat with an `origin` for downstream tools (PerfectPixel).
- Markdown — a `## View & Direction` section with the three values, the origin,
  and a `Master image direction:` sentence to paste into an image prompt.

A document authored before these fields existed inherits from its world and then
from the project default above; the fallback is recorded as
`origin: "default"` and raised as a warning, never exported as `unknown`.
Loading an old document does not rewrite it — the values appear when you export.

## Test

```bash
cd Tools/CharacterEditor
npm test -- --run
npm run build
```

The test suite covers executable schemas, inheritance/origins, every validation family, approved exceptions, comparison, local draft/import round trips, deterministic exports, form behavior, and a real-module application flow from the Library through comparison and export preview.

## Current limitations

- Direct unrestricted repository writes and native file dialogs are deferred; use browser download/import.
- Data remains local to one browser profile unless exported.
- No schema migration beyond `1.0.0`.
- No silhouette overlay, repository asset discovery, image generation, Unity asset generation, animation editing, or real-time rendering.
- Windows packaging is not yet verified; the Vite/React stack is cross-platform, but acceptance was performed on macOS.

