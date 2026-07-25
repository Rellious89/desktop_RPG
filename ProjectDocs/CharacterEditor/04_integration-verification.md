# Character Editor MVP — Integration Verification

> Wave 3 integration owner report, 2026-07-25 (Asia/Seoul)

## Outcome

The Character Editor data and UI tracks integrate successfully. The full automated suite passes, the production bundle builds, the Vite app serves over loopback, checked-in Schema v1 data/exports parse, and a real-module UI acceptance test completes the bundled ElfGuardian edit/compare/export path. No Unity files were changed.

## Environment

- macOS / arm64
- Node `v24.16.0`
- npm `11.13.0`
- Vite `8.1.5`
- App URL: `http://127.0.0.1:5173/`
- Orca dispatch verified with `orca orchestration dispatch-show --task task_7bb21c663ba6 --json`; dispatch `ctx_e4c2f1553bc6` was assigned to this worker.

## Exact commands and results

From `Tools/CharacterEditor`:

```bash
npm test -- --run
```

Final result: 13 test files passed, 45 tests passed (after adding the real-module application acceptance); no failures.

```bash
npm run build
```

Final result: TypeScript project build and Vite production build succeeded. Vite transformed 138 modules and generated `dist/index.html` plus CSS/JS assets.

```bash
npm run dev
curl -fsS -D - http://127.0.0.1:5173/
```

Result: Vite reported ready on `127.0.0.1:5173`; the live server returned HTTP `200 OK`, the expected `KeyBuddy Character Editor` HTML, and the React entry module.

Data invariant check:

```bash
jq -r '.authored.actorId, .resolved.anatomy.targetLogicalHeightPx, .resolved.anatomy.speciesScale, .resolved.production.unityVisualScale' \
  ProjectDocs/CharacterEditor/Exports/ElfGuardian/ElfGuardian.character.json
```

Result:

```text
ElfGuardian
91
1
1
```

## Verified flows

### Integrated UI

The real-module `App.ui.test.tsx` acceptance drives the application (not a mocked domain API) through:

1. Library load with all three worlds (`ANIMAL-LAND-01`, `HUMAN-FANTASY-01`, `UNDEAD-WORLD-01`).
2. Opening the bundled ElfGuardian actor.
3. Reading `targetLogicalHeightPx=91` and `speciesScale=1.0` in Body & Proportions.
4. Opening Comparison, selecting VenomCultist, and observing logical-height delta `+21 (+30.0%)` while species-scale delta remains zero.
5. Returning to the editor and opening Export Preview.
6. Confirming both JSON and Markdown download actions are available.

Component tests separately verify world version editing/download, inherited/override/reset presentation, field help/policy locks, validation exception entry/removal, JSON paste/import errors, comparison empty/reference states, and blocked/unblocked export preview.

### Persistence and round trip

- Actor draft save/load round-trips through project-namespaced `localStorage` keys.
- World drafts use immutable `{worldId, version}` keys.
- Authored actor JSON imports through Schema v1.
- A resolved export envelope imports back to its `authored` actor.
- Deterministic JSON re-export is stable and LF/trailing-newline normalized.
- Markdown and JSON exports include resolved values, field origins, diagnostics, interpretations, approved exceptions, and comparison.
- Unsafe path IDs (`..`, separators) are rejected.

### Validation and comparison

Automated tests verify:

- Wrong `staff` family for ElfGuardian: blocking `weapon-family-not-allowed`.
- Unity visual scale `0.9`: blocking `unity-scale-not-one`; a valid active exception unblocks export while preserving the diagnostic.
- `normal` build with `very-broad` torso: `normal-build-wide-torso` warning.
- Weapon occupancy above 60%: `large-weapon-canvas` warning (ElfGuardian sample is 65%).
- PPU other than 200: non-overridable blocking `ppu-not-200`.
- Floating actor with forward-foot pivot: non-overridable `floating-pivot-mismatch`.
- Stature/species-scale conflation, proportion mismatch, extremity delta, missing constraints, large-motion canvas exception, and pinned-world mismatch.
- Comparison includes stature/height, build/proportion, head/hand/foot/torso, species scale, weapon occupancy, base canvas, and large-motion canvas.

### Canonical templates and samples

- Three world JSON documents parse with executable Zod Schema v1 and carry Low Companion v1, 3×3 logical blocks, 512×512 base canvas, PPU 200, Unity visual scale 1, screen-right three-quarter defaults, and ordered production layers.
- ElfGuardian authored data and export parse, preserve aliases `LeafGlaiveElf`/`Elfguardian`, use 91px, species scale 1, Unity scale 1, glaive/polearm restrictions, and record legacy 1.3 only as historical evidence.
- VenomCultist authored data and export parse, use 70px, species scale 1, Unity scale 1, dagger-only restriction, and measured pivot evidence.

## Browser acceptance limitation

The in-app Browser skill was initialized and browser discovery was attempted as required, but the Orca runtime returned no available browser backends (`agent.browsers.list()` returned `[]`). Therefore no screenshot or native click session was possible in this dispatch. UI acceptance used the live loopback HTTP response plus the 26 jsdom UI tests, including the real-module application flow described above; a human should perform a short visual smoke test in the in-app browser or Safari/Chrome when a browser backend is available.

## Integration changes

- Added `Tools/CharacterEditor/README.md` with launch, use, persistence/import/export, test, and limitation guidance.
- Added a real-domain/data/persistence application acceptance to `Tools/CharacterEditor/src/app/App.ui.test.tsx`.
- Added this verification report.
- No implementation defect required production-code changes during Wave 3; the merged Wave 2 application passed tests/build before and after acceptance coverage.

## Deferred features

- Tauri/native packaging and native file dialogs.
- Direct repository writes and unrestricted filesystem service.
- Windows packaging and Windows acceptance.
- Silhouette overlays and real-time actor rendering.
- Repository asset auto-discovery and existing-asset backfill.
- Schema migrations beyond v1 and multi-level species template inheritance.
- Multi-user approval audit.
- AI/image generation, Unity asset generation, animation editing, and motion authoring.

## Recommended final manual smoke test

When a browser surface is available: launch with `npm run dev`, open ElfGuardian, visually inspect responsive layout and Korean text, edit/save/reload a draft, paste the checked-in ElfGuardian export through JSON Import, approve/remove a Unity-scale exception, download JSON/Markdown, and verify filenames in Finder. This is the only remaining verification activity; it does not block the automated MVP result above.
