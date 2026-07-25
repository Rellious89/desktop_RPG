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

- Target logical height: `91px`
- Species scale: `1.0`
- Unity visual scale: `1.0`
- Legacy `1.3`: historical production-sizing evidence only

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

