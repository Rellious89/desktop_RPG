# Character Editor MVP — Integrated Specification

> Coordinator synthesis, 2026-07-25. This document reconciles the Orca Wave 1 Codex and Claude reports and is the implementation contract for Waves 2–3.

## 1. Decisions

- Build a project-local TypeScript application at `Tools/CharacterEditor` with React, Vite, Zod, Vitest, React Testing Library, and Playwright where practical.
- Run locally in the browser through a loopback-only Vite process. MVP persistence uses browser import/download plus localStorage drafts; checked-in canonical samples and exports live under `ProjectDocs/CharacterEditor`. A native shell and unrestricted filesystem service are deferred.
- Schema version is `1.0.0`. World templates are immutable versioned documents. Actors pin `worldRef.worldId + version`, store sparse overrides, and export a fully resolved snapshot plus field origins and diagnostics.
- Repository design IDs and runtime names are both retained. For the required sample, canonical `actorId` is `ElfGuardian`, matching the shipped asset folder and user-facing brief request; `LeafGlaiveElf` and `Elfguardian` are aliases. No Unity assets are renamed. Equivalent alias treatment applies to `BlackCatMage/CatMage` and `CopperAxeBarbarian/Barbarian` when added later.
- Legacy bare `scale` is never imported into a current scale field automatically. ElfGuardian's historical `1.3` is evidence of relative production sizing only. The sample uses `stature=tall`, `targetLogicalHeightPx=91`, `speciesScale=1.0`, and `unityVisualScale=1.0`. VenomCultist uses `stature=average`, `targetLogicalHeightPx=70`, `speciesScale=1.0`, and `unityVisualScale=1.0`.
- PPU 200 and Unity visual scale 1.0 are global policies represented in each template for reproducibility. A non-1 Unity scale is an overridable blocking diagnostic. A non-200 PPU is non-overridable blocking in MVP.
- Production layers are planning metadata with the required ordered policy `character-or-outfit`, `weapon`, `effect`; the editor does not require existing layered art files.
- Comparison is optional when no same-world reference exists. All comparison flags reuse the domain validation/comparison rules.

These choices are reversible and introduce no external service, account, asset rename, or packaging commitment; no user decision gate is required.

## 2. Source of truth

1. Schema-valid Character Editor JSON after adoption.
2. Approved `ProjectDocs/DesignRules` global/world rules.
3. Approved actor briefs and master measurements.
4. Unity import metadata and Motion Profiles for observed runtime/import facts only.
5. Asset paths/names as shipped identity evidence.
6. Attempts, prototypes, and PerfectPixel history as non-authoritative evidence.

Conflicts are retained as aliases/evidence and surfaced as diagnostics. They are never silently resolved into creative facts.

## 3. Data locations

```text
Tools/CharacterEditor/                         app source and tests
ProjectDocs/CharacterEditor/Schema/
  character-editor-v1.schema.json             published Schema v1
ProjectDocs/CharacterEditor/Data/worlds/
  {WorldId}/v{version}.world.json              immutable templates
ProjectDocs/CharacterEditor/Data/actors/
  {ActorId}.character.json                     authored sparse actor documents
ProjectDocs/CharacterEditor/Exports/{ActorId}/
  {ActorId}.character.json                     resolved export envelope
  {ActorId}.character.md                       deterministic readable export
```

Filenames reject separators, traversal, empty IDs, and case-insensitive collisions. JSON/Markdown use stable ordering, UTF-8, LF, and a trailing newline.

## 4. Schema v1 contract

Documents share `schemaVersion`, `documentKind`, `revision`, and RFC 3339 timestamps.

World templates contain `worldId`, localized display names, status, description, approved view/facing, pixel style/block size, genuine world defaults, allowed species/proportion IDs, outline/lighting, canvas policies, PPU, Unity scale, production layers, and evidence. Actor-restricted weapon families do not belong to world defaults.

Actor documents contain:

- identity: `actorId`, localized display name, aliases, Character/Monster, pinned world ref, species, sex, age group, role, concept, and status;
- sparse inheritable overrides: view, pixel style, anatomy (`stature`, logical height, build, proportion, species scale, head/hand/foot size, torso width), and production (base/large-motion canvas, pivot rule/optional normalized pivot, PPU, Unity scale, ordered layers);
- actor-only data: physical traits, hair/eyes/skin, clothing/materials/palette/decorations, invariants/forbidden elements, weapon family/size/hands/direction/structure/secondary equipment and actor-level allowed weapon families;
- approved exceptions and evidence.

Missing override means inherit. Objects merge by known schema path, scalars replace, and arrays replace as a whole. `null` is not a reset marker. Reset-to-inherited deletes the override path.

The export envelope contains `authored`, `resolved`, `fieldOrigins`, `calculated`, `interpretations`, `diagnostics`, and optional `comparison`. Exporting never mutates authored overrides.

## 5. UI flow

One responsive application provides:

1. library/home with bundled world and actor samples plus new/import actions;
2. world template form with version/save/download;
3. actor editor sections for Identity, Body & Proportions, Look, Weapon & Equipment, and Production;
4. sticky validation summary with field navigation and warning approval/reason entry;
5. same-world comparison table;
6. JSON and Markdown export preview/download.

Every inheritable field shows a World badge or Override badge, the inherited baseline, and Reset to world. Specialized terms have short help text. Locked policy fields clearly explain why they are constrained. Direct JSON editing is not required.

Drafts autosave to localStorage. Import accepts Schema v1 authored actors and resolved export envelopes, validates them, and loads the authored portion for editing. Unknown schema versions fail with a clear message; automated migration is deferred until a second version exists.

## 6. Validation and comparison

Stable rule IDs and severities:

- `required-field` error, not overridable;
- `stature-species-scale-conflation` warning when a non-1 species scale appears to duplicate relative height without species-wide evidence;
- `normal-build-wide-torso` warning;
- `proportion-mismatch` warning for comparable same-world/species actors;
- `large-weapon-canvas` warning when estimated occupancy exceeds the 512 safe policy;
- `unity-scale-not-one` error, overridable with reason;
- `weapon-family-not-allowed` error, overridable with reason;
- `extremity-size-delta` warning for otherwise equivalent humanoids;
- `large-motion-canvas-exception` warning;
- `ppu-not-200` error, not overridable;
- `floating-pivot-mismatch` error, not overridable;
- `missing-design-constraints` warning;
- `actor-id-alias-conflict` informational warning.

Approved exceptions require a non-empty reason, reference a rule ID, remain visible with the original diagnostic, and are included in both exports. Errors block export unless that specific rule is overridable and has an active exception. Warnings remain exportable but visible.

Comparison reports stature/height, build/proportion, head/hand/foot/torso, species scale, weapon occupancy, base canvas, and large-motion canvas. Numeric deltas include absolute and percentage values where meaningful.

## 7. Initial data

Create three world templates:

- `ANIMAL-LAND-01` / 애니멀랜드 / Animal Land;
- `HUMAN-FANTASY-01` / 판타지아 / Fantasia;
- `UNDEAD-WORLD-01` / 망자들의 세계 / World of the Dead.

All use Low Companion v1, approximately 3×3 logical blocks, 512×512 base canvas, same-as-base large-motion default, PPU 200, Unity scale 1.0, screen-right slight three-quarter where applicable, and the three planning layers. Do not invent unsupported anatomy defaults; use conservative general humanoid defaults only where required for a usable template and mark provenance.

Required samples:

- `ElfGuardian`: Character, Fantasia, elf, tall, 91px, normal/slender humanoid proportions, species scale 1.0, Unity scale 1.0, glaive/polearm only, weapon ratio about 1.2, aliases/evidence for LeafGlaiveElf and legacy 1.3, and the observed large-motion canvas exception.
- `VenomCultist`: Monster, Fantasia, living human cultist, average, 70px, normal humanoid, species scale 1.0, Unity scale 1.0, dagger only, 512 base canvas, measured pivot evidence.

The sample pair must produce meaningful height/weapon/canvas deltas without an erroneous species-scale delta.

## 8. Public domain API

The UI consumes only these domain APIs and does not duplicate logic:

```ts
parseWorld(input)
parseActor(input)
resolveActor(actor, world)
validateActor(actor, world, references?)
compareActors(draft, reference)
serializeActor(actor)
buildExport(actor, world, references?)
exportJson(envelope)
exportMarkdown(envelope)
```

## 9. Wave 2 file ownership

Codex owns exclusively:

- `Tools/CharacterEditor/src/domain/**`
- `Tools/CharacterEditor/src/schema/**`
- `Tools/CharacterEditor/src/persistence/**`
- `Tools/CharacterEditor/src/export/**`
- `Tools/CharacterEditor/src/data/**`
- `Tools/CharacterEditor/src/**/*.unit.test.ts`
- `ProjectDocs/CharacterEditor/Schema/**`
- `ProjectDocs/CharacterEditor/Data/**`
- `ProjectDocs/CharacterEditor/Exports/**`

Claude owns exclusively:

- `Tools/CharacterEditor/src/app/**`
- `Tools/CharacterEditor/src/components/**`
- `Tools/CharacterEditor/src/styles/**`
- `Tools/CharacterEditor/src/**/*.ui.test.tsx`

Coordinator/integration owner owns exclusively:

- `Tools/CharacterEditor/package.json`, lockfile, configs, `index.html`, `src/main.tsx`, and shared `src/index.css`
- launch/user documentation and final integration wiring
- dependency installation, build, test, launch, browser acceptance, and final report

Workers may read but must not edit another owner's paths. Any missing API or config request is escalated to the Coordinator.

## 10. Acceptance tests

- Install and launch on macOS without account or remote service.
- Create/edit/version each world template and download/re-import it.
- Create an actor from a world, see inherited versus overridden values, reset an override, save draft, reload, edit, and export.
- Import a JSON actor/export and re-export deterministic JSON and Markdown.
- Validate every required rule and approved exception behavior.
- Compare ElfGuardian with VenomCultist and inspect all required metrics.
- Confirm forbidden staff for ElfGuardian and non-1 Unity scale produce export-blocking diagnostics; confirm wide torso/normal build and oversized weapon warnings.
- Run unit, UI, and build tests; run Playwright if browser dependencies are locally available, otherwise execute equivalent browser acceptance manually and report the limitation.

## 11. Deferred

Tauri/native packaging, unrestricted direct repository writes, silhouette overlays, repository asset auto-discovery, schema migrations beyond v1, multi-level species template inheritance, collaborative approval audit, AI/image generation, Unity asset generation, animation editing, and real-time rendering.
