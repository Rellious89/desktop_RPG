# Character Editor MVP — Repository and Schema Research

> Wave 1 Codex report, 2026-07-25. Research only; no implementation files were changed.

## Executive recommendation

Build the MVP as a project-local TypeScript web application under `Tools/CharacterEditor`, using Vite + React, Zod for the executable Schema v1, and Vitest/React Testing Library. Launch it on macOS with an npm script that starts a loopback-only Vite process and opens the browser; it requires no account or remote service. Keep the domain, validation, resolution, comparison, and export layers framework-independent so a later Tauri shell can add native file dialogs and Windows packaging without changing the data format.

Use repository-managed JSON files as the canonical editor data. World templates provide version-pinned defaults; actor documents store identity plus explicit overrides, never a copied resolved object. At load/export time, resolve `world defaults -> actor overrides`, retain field-level origin metadata in memory, calculate comparisons and diagnostics, and export a complete immutable snapshot with both overrides and resolved values. Never reuse the legacy `scale` field: `stature`, `targetLogicalHeightPx`, `build`, `proportionTemplateId`, `speciesScale`, and `unityVisualScale` are independent.

## Audit scope and evidence hierarchy

The audit covered all Markdown in `ProjectDocs/ArtPipeline` and `ProjectDocs/DesignRules`, actor/enemy folders beneath `Assets/Art`, and serialized profiles beneath `Assets/Data/MotionProfiles`. Relevant runtime code was read only to understand ownership of Pivot/PPU versus preview transforms.

Recommended source-of-truth hierarchy, by kind:

1. **New editor JSON**: canonical structured intent after adoption. Schema-valid files in `ProjectDocs/CharacterEditor/Data` own world defaults and editor actor specifications.
2. **Approved design rules**: canonical global production constraints. In particular, `ProjectDocs/DesignRules/character-sprite-and-animator-rules.md` owns 512×512 base frames, PPU 200, Low Companion v1, Actor Origin semantics, logical height guidance, and per-actor Pivot allowance. `ProjectDocs/DesignRules/world-setting-rules.md` owns registered world IDs and membership.
3. **Actor brief and approved measurement document**: canonical actor design intent and measured master facts. Examples are `ProjectDocs/ArtPipeline/Characters/LeafGlaiveElf/01_character-brief.md` and `03_master-measurements.md`, and the corresponding VenomCultist/Specter documents.
4. **Unity import metadata and Motion Profiles**: canonical facts about the currently imported/runtime asset, not design intent. PNG `.meta` files own PPU/Pivot actually used by Unity; Motion Profiles own animation clips and monster preview offset/scale.
5. **PNG/folder/file names**: canonical evidence of what is currently shipped, but not a semantic character specification. Assets can be stale, rejected, or historical.
6. **PerfectPixel input/attempt/prototype documents**: process history and evidence only. They must not silently override approved briefs/rules. Prototype lineups are explicitly demoted by `ProjectDocs/ArtPipeline/resource-production-workflow.md`.

When layers disagree, the editor must not guess silently. Store `evidence[]` and `aliases[]`, flag the disagreement, and require an approved exception or data correction. A future repository importer should be advisory and must not overwrite hand-authored JSON.

## Canonical worlds and actors

### Worlds

The actual registered world IDs come from `ProjectDocs/DesignRules/world-setting-rules.md`:

| World ID | Display name | Current documented members relevant here |
|---|---|---|
| `ANIMAL-LAND-01` | 애니멀랜드 / Animal Land | `BlackCatMage`, `CatKnight` |
| `HUMAN-FANTASY-01` | 판타지아 / Fantasia | `CopperAxeBarbarian`, `LeafGlaiveElf`, `VenomCultist` |
| `UNDEAD-WORLD-01` | 망자들의 세계 / World of the Dead | `Specter` |

The Desktop is a shared hub, not currently a registered connected-world template. Do not invent a Desktop world record for MVP samples.

### Actor identity and aliases

Use the documented design ID as the editor `actorId` where an approved brief exists; retain runtime/resource names as aliases and evidence paths.

| Editor actor ID | Type/world | Repository aliases and evidence | Known structured facts |
|---|---|---|---|
| `LeafGlaiveElf` | Character / `HUMAN-FANTASY-01` | Asset folder `Assets/Art/Character/ElfGuardian`; filenames `Elfguardian-*`; profile `Assets/Data/MotionProfiles/Characters/ElfGuardian/ElfGuardian_MotionProfile.asset`; brief `ProjectDocs/ArtPipeline/Characters/LeafGlaiveElf/01_character-brief.md` | Elf, glaive warrior, slight 3/4 screen-right; 91px body target; weapon about 1.2× body height; historical `Actual scale: 1.3` |
| `VenomCultist` | Monster / `HUMAN-FANTASY-01` | Names align across docs, `Assets/Art/Enemy/VenomCultist`, and monster profile | Living human cultist; 70px; 512 canvas; measured pivot 0.5234375/0.2578125; historical scale 1.0; one dagger |
| `Specter` | Monster / `UNDEAD-WORLD-01` | Names align across docs, asset folder, and profile | 56px; measured pivot 0.5/0.296875; floats 6 logical px over origin; historical scale 0.8; no weapon |
| `BlackCatMage` | Character / `ANIMAL-LAND-01` | Current shipped folder/profile are `CatMage`; frames are `BlackCatMage-frame-*`; docs live at `ProjectDocs/ArtPipeline/Characters/BlackCatMage` | Black cat mage, staff-only; screen-right 3/4; Low Companion v1; 86px hat-included exception documented globally |
| `CopperAxeBarbarian` | Character / `HUMAN-FANTASY-01` | Current shipped folder/profile/frame prefix are `Barbarian`; brief uses `CopperAxeBarbarian` | Human male, muscular two-head proportion, two one-handed axes; 79px large-build exception documented globally |
| `CatKnight` | Character / `ANIMAL-LAND-01` | Names align at folder/profile level; frame prefix is `Cat-knight` | Existing cat knight; no audited actor brief in the requested character-doc set, so detailed anatomy/specification is unknown |

The sample requested colloquially as “Elfguardian” should therefore be exported as `LeafGlaiveElf.character.json`, with `aliases: ["ElfGuardian", "Elfguardian"]`. Renaming Unity assets is out of scope and unnecessary.

## Conflicts and data-quality findings

1. **One `scale` has at least three incompatible meanings.** LeafGlaiveElf `Actual scale: 1.3`, VenomCultist `1.0`, and Specter `0.8` appear in measurement documents as relative actor size. Monster Motion Profiles separately contain `preview.actorScale` (VenomCultist/Specter 1.0; HyenaRaider 0.9), while the production rules reject routine Unity Transform/VisualRoot scaling and mention an abandoned high-density `VisualRoot Scale 0.35` experiment. These values cannot populate `speciesScale` or `unityVisualScale` automatically.
2. **LeafGlaiveElf violates the newer general-height guidance unless explicitly excepted.** Its approved target is 91px, versus the global normal range 65–75px. The global document names Barbarian 79px and BlackCatMage 86px exceptions but does not explicitly name LeafGlaiveElf. Migration should create a warning requiring an approved exception, not reinterpret 1.3 as uniform species enlargement.
3. **The world document says Fantasia's current main intelligent species is human but lists an elf.** This is a documentation inconsistency, though the same section assigns LeafGlaiveElf to that world. Actor membership is usable; the world species copy needs correction or an exception.
4. **Working/design IDs differ from shipped/runtime IDs.** LeafGlaiveElf/ElfGuardian, BlackCatMage/CatMage, and CopperAxeBarbarian/Barbarian must be modeled as aliases. Folder names alone are not canonical identity.
5. **Canvas policy and actual assets differ by motion.** Global rules declare 512×512 per frame, but ElfGuardian attack and attack_T2 PNGs are 1024×1024 while its idle frames are 512×512. The editor needs `baseCanvas` plus `largeMotionCanvas` and must flag 1024 as a recorded/approved exception, not overwrite the global default.
6. **Pivot documentation and current imported metadata can disagree.** VenomCultist's approved measurement is 0.5234375/0.2578125, but sampling current art metadata shows common imported values around 0.5/0.078; the editor must distinguish `plannedPivot`, `measuredPivot`, and optional `observedUnityImport`, with the former two represented by a rule plus contact point rather than a universal numeric default.
7. **PPU is not owned by Motion Profiles.** `Assets/Scripts/Character/PlayerCharacterAnimator.cs` explicitly says Pivot/PPU live in sprite import settings. Sampled key actor PNG metadata uses PPU 200. Monster profile `actorScale` and `actorOffset` are preview/layout tuning, not production sprite specification.
8. **Facing is two concepts.** Approved master view/facing is screen-right slight 3/4 for the audited briefs. PerfectPixel `Facing direction: Not set` is a generator dropdown strategy, not an unknown actor facing. Store `view.facing = screen-right`; keep generator settings out of Schema v1.
9. **Canvas is not logical density.** 512 physical pixels, roughly 3×3 output pixel blocks, and 56/70/79/86/91 logical silhouette heights are distinct. The schema must name and type all three.
10. **Asset names can be historical/rejected.** LeafGlaiveElf master files include rejected comparison versions, and the rules say prototype LOW-B/LOW-C sheets are only ideation references. Importing the latest filename lexically would be unsafe.

## Schema v1 proposal

Use two versioned document kinds with a shared vocabulary. JSON field names use lower camel case, IDs use stable ASCII, dimensions are integer pixels, normalized pivots are decimal 0–1, and ratios are explicit (never a bare `scale`). Unknown is represented by field absence during drafting, never magic strings such as `Not set`.

```ts
type SchemaVersion = "1.0.0";
type DocumentMeta = {
  schemaVersion: SchemaVersion;
  documentKind: "world-template" | "actor";
  revision: number;                 // monotonically increases on explicit save/version
  updatedAt: string;                // RFC 3339
};

type WorldTemplateV1 = DocumentMeta & {
  documentKind: "world-template";
  worldId: string;                  // e.g. HUMAN-FANTASY-01
  displayName: { ko: string; en: string };
  status: "concept" | "active" | "hold";
  description: string;
  defaults: Partial<InheritableActorSpec>;
  allowedSpecies?: string[];
  allowedProportionTemplates?: string[];
  evidence: EvidenceRef[];
};

type ActorDocumentV1 = DocumentMeta & {
  documentKind: "actor";
  actorId: string;
  displayName: { ko?: string; en: string };
  aliases: string[];
  actorType: "character" | "monster";
  worldRef: { worldId: string; revision: number };
  identity: {
    species: string; sex: "female" | "male" | "intersex" | "none" | "unknown";
    ageGroup: "child" | "adolescent" | "adult" | "elder" | "ageless" | "unknown";
    role: string; concept: string;
  };
  overrides: DeepPartial<InheritableActorSpec>;
  physicalTraits: string[];
  appearance: {
    hair?: string; eyes?: string; skin?: string; clothing: string[];
    materials: string[]; palette: PaletteEntry[]; decorations: string[];
  };
  constraints: { invariants: string[]; forbidden: string[] };
  equipment: EquipmentSpec;
  approvedExceptions: ApprovedException[];
  evidence: EvidenceRef[];
};

type InheritableActorSpec = {
  view: { projection: "side" | "three-quarter" | "front"; facing: "screen-left" | "screen-right" };
  pixelStyle: { styleId: string; logicalBlockPx: { width: number; height: number }; outline: string; lighting: string };
  anatomy: {
    stature: "tiny" | "short" | "average" | "tall" | "very-tall";
    targetLogicalHeightPx: number;
    build: "slender" | "normal" | "broad" | "muscular" | "massive" | "non-humanoid";
    proportionTemplateId: string;
    speciesScale: number;            // uniform species-wide multiplier; default 1, exceptional use only
    headSize: "xs" | "s" | "m" | "l" | "xl";
    handSize: "xs" | "s" | "m" | "l" | "xl";
    footSize: "xs" | "s" | "m" | "l" | "xl";
    torsoWidth: "narrow" | "normal" | "broad" | "very-broad";
  };
  production: {
    baseCanvas: { widthPx: number; heightPx: number };
    largeMotionCanvas: { policy: "same-as-base" | "explicit"; widthPx?: number; heightPx?: number };
    safeMarginPx?: number;
    pivotRule: "forward-foot-contact" | "actor-origin-custom";
    pivot?: { xNormalized: number; yNormalized: number; source: "measured" | "planned" };
    pixelsPerUnit: number;
    unityVisualScale: number;         // Transform/VisualRoot scale; normally exactly 1
    layers: Array<"character-or-outfit" | "weapon" | "effect">;
  };
};

type EquipmentSpec = {
  weapon?: {
    family: string; sizeClass: "small" | "medium" | "large" | "oversized";
    mainHand: "anatomical-left" | "anatomical-right" | "both" | "none";
    offHand: "anatomical-left" | "anatomical-right" | "both" | "none";
    direction: string; structure: string; count: number;
    lengthToBodyRatio?: number;
  };
  secondary: string[];
  allowedWeaponFamilies: string[];   // actor restriction, not a world default
};

type EvidenceRef = { path: string; claim: string; status: "approved" | "observed" | "historical" | "conflicting" };
type ApprovedException = { ruleId: string; reason: string; approvedBy: string; approvedAt: string };
type PaletteEntry = { role: string; value: string };
```

Export adds a derived envelope rather than mutating authored overrides:

```ts
type ActorExportV1 = {
  authored: ActorDocumentV1;
  resolved: InheritableActorSpec;
  fieldOrigins: Record<string, { source: "world" | "actor"; documentId: string; revision: number }>;
  calculated: Record<string, unknown>;
  interpretations: string[];
  diagnostics: Diagnostic[];
};
```

`calculated` should initially contain body/canvas occupancy estimates, weapon-span estimate when possible, effective layer policy, and comparison deltas. It must never contain new creative decisions.

### Required fields and validation phases

Block export when IDs, type, pinned world reference, species, sex, age group, role, concept, stature, height, build, proportion, all four size classes, appearance essentials, constraints arrays, equipment rules, canvas, pivot rule, PPU, or Unity scale are missing. Empty arrays are valid only where “none” is meaningful and explicitly selected.

Run validation in four phases:

1. JSON/schema/type validation.
2. Resolution validation (world revision exists, override path is allowed, resolved required fields exist).
3. Semantic validation (the brief's scale conflation, normal-build/torso conflict, species/proportion mismatch, canvas occupancy, Unity scale, weapon allowance, equivalent-humanoid extremity deltas).
4. Repository-policy validation (512/PPU 200/Low Companion guidance, aliases, known observed import conflicts).

Errors block export. Warnings require either correction or an `approvedException` referencing the diagnostic rule ID. Informational comparison notes need no approval. Approved exceptions remain visible in JSON and Markdown; they do not suppress the underlying diagnostic from history.

## Template inheritance and versioning

- A world template contains only genuine same-world defaults. Do not place actor-restricted weapon families, individual sex/age, or measured Pivot values in it.
- Actor documents store `worldRef.worldId + revision` and a sparse `overrides` tree. Missing means inherit. Explicit `null` is forbidden except in narrowly nullable schema fields, preventing accidental deletion of defaults.
- Resolution is a schema-aware deep merge by field path. Objects merge; scalar values replace; arrays replace as a whole. This avoids ambiguous concatenation of palettes, layers, and forbidden elements.
- The editor shows every resolved field with an origin badge and supports “reset to inherited,” which deletes that override path.
- Saving a changed template increments `revision` and writes a new versioned file. Existing actors stay pinned. Opening an actor can offer an explicit upgrade preview showing changed inherited fields; upgrading changes the pin and actor revision.
- Actor exports embed the fully resolved snapshot and origin map, so an old export remains reproducible even if templates later change.
- Template selection may suggest a species/proportion preset, but Schema v1 should not add a second inheritance chain. Species/proportion templates are stable IDs/enums referenced by a world default or actor override; multi-level inheritance can be revisited after MVP.

Initial world defaults shared across all three templates should reflect the current global production rule: Low Companion v1, approximately 3×3 logical block, 512×512 base, same-as-base large-motion policy, PPU 200, unityVisualScale 1, forward-foot-contact pivot, and the three production layers. View/facing may default to 3/4 screen-right where supported, but actor-approved view remains overridable. Do not seed speculative species/anatomy defaults where the repository lacks evidence.

## Standalone technology decision

Environment check on 2026-07-25 found Node 24.16/npm 11.13, Python 3.9.6, .NET 9.0.303, Swift 6.3.2, and Unity 2022.3.62f3. The repository has no existing non-Unity application manifest.

| Choice | Strengths | Costs/risks | Decision |
|---|---|---|---|
| Vite + React + TypeScript, loopback browser | Installed modern Node; fastest form UX; cross-platform; excellent schema/test libraries; no account/server beyond a local process; simple source distribution | Browser cannot freely write arbitrary project paths without a picker; Node must be installed for dev launch | **MVP recommendation**; use explicit import/download and documented project-local save/export workflow, or a tiny local file adapter guarded to the data root |
| Tauri + React | Native window/filesystem and lighter than Electron; future macOS/Windows packaging | Rust/toolchain and signing/packaging add immediate maintenance; not present in repo | Keep as a future shell around the same frontend/domain modules |
| Electron | Mature native filesystem, easiest JS packaging | Large bundle/runtime and packaging burden for a small local form editor | Reject for MVP priorities |
| Python + desktop/web UI | Python exists and local filesystem is easy | System Python 3.9 is old; non-technical polished forms and Windows packaging are less straightforward | Reject unless team has a strong Python ownership preference |
| SwiftUI | Excellent macOS-native experience | Weak Windows future; duplicates domain/UI effort | Reject due to explicit Windows priority |
| .NET/Avalonia | .NET is installed and cross-platform desktop is viable | New ecosystem in this repo, larger initial UI/packaging burden than web stack | Viable fallback, not preferred |

For safety, any local file adapter must bind only to `127.0.0.1`, reject path traversal/symlinks escaping the configured root, write atomically through a sibling temporary file + rename, and never expose Unity `Assets` for writes. If this adapter is deferred, the browser download/import flow still satisfies standalone editing, while a provided `npm run export -- --input <file>` command can validate and place artifacts project-locally.

## Project-local locations

```text
Tools/CharacterEditor/                         application source
ProjectDocs/CharacterEditor/Data/
  worlds/{WorldId}/v{revision}.world.json      immutable template versions
  actors/{ActorId}.character.json              current authored actor document
ProjectDocs/CharacterEditor/Exports/
  {ActorId}/{ActorId}.character.json            resolved versioned export
  {ActorId}/{ActorId}.character.md              readable export
ProjectDocs/CharacterEditor/Schema/
  character-editor-v1.schema.json               generated/published JSON Schema
```

Keep authoring data under `ProjectDocs`, not `Assets`, because the editor is a specification tool and must not trigger Unity asset imports. Use case-sensitive canonical IDs in filenames and reject separators, `..`, whitespace-only IDs, and case-insensitive collisions. Exports should be deterministic (stable key order, LF newline, trailing newline) so Git review is meaningful. Do not store absolute machine paths.

The requested pair `{CharacterId}.character.json/.md` refers to export basenames. The JSON export should include the resolved snapshot envelope; the authored source remains the sparse actor file in `Data/actors`.

## Test strategy

### Unit and contract tests (Vitest)

- Parse valid/invalid world, actor, and export fixtures; assert schema version rejection and useful field paths.
- Resolve nested defaults, scalar replacement, array replacement, reset-to-inherited, missing world revision, and template upgrade preview.
- Assert all required semantic diagnostics, severity, stable rule IDs, approved-exception behavior, and no false conflation of stature/species/unity scale.
- Test deterministic JSON and Markdown snapshots, escaping, Korean/English Unicode, aliases, round-trip import/edit/re-export, and no derived values written into authored overrides.
- Test path validation, traversal/case collision rejection, atomic write behavior, and local-adapter root confinement.

### Repository evidence fixtures

- `LeafGlaiveElf`: tall/91px actor, polearm/glaive allowance, 1.2 weapon ratio, 1024 large-motion exception, aliases for ElfGuardian.
- `VenomCultist`: 70px/512/PPU 200, measured pivot, one dagger, world Fantasia, unity scale 1.
- Negative fixtures: ElfGuardian with `speciesScale: 1.3` inferred from legacy scale; normal build + very-broad torso; staff assigned to Elf; non-1 Unity scale; oversized weapon on 512; unapproved 91px global-height exception.
- Comparison fixture: LeafGlaiveElf vs VenomCultist in the same world, verifying stature/height/build/proportion/extremity/species/weapon occupancy/canvas deltas without claiming they are equivalent humanoids.

### UI and end-to-end tests

- React Testing Library: inherited badges, override/reset control, field help, all-errors summary, warning approval form, and comparison table.
- Playwright: create from each world, complete actor, save/reload, import existing JSON, change world with confirmation, compare reference, block invalid export, approve warning, export both formats, re-import and re-export identically.
- Manual acceptance on macOS: clean checkout install/launch, no internet/account, Korean text, Finder-visible project-local exports, restart persistence. Before Wave 3 completion, repeat the packaged/launch path on Windows or document it as unverified rather than claiming support.

## Recommended implementation ownership split

To avoid overlapping files:

**Codex/data owner**

- `Tools/CharacterEditor/src/domain/**`
- `Tools/CharacterEditor/src/schema/**`
- `Tools/CharacterEditor/src/persistence/**`
- `Tools/CharacterEditor/src/export/**`
- corresponding unit/contract tests and all sample data under `ProjectDocs/CharacterEditor/Data/**`
- published JSON Schema and exporter fixtures

**Claude/UI owner**

- `Tools/CharacterEditor/src/app/**`
- `Tools/CharacterEditor/src/components/**`
- `Tools/CharacterEditor/src/styles/**`
- UI tests and Playwright specs

**Integration owner (single writer)**

- root app scaffolding/config (`package.json`, Vite/TypeScript config, entry HTML/main file)
- dependency wiring between UI and domain APIs
- launch scripts, README/user guide, final sample export generation, build/launch/E2E verification

Agree on exported TypeScript interfaces/functions before parallel implementation: `parseActor`, `resolveActor`, `validateActor`, `compareActors`, `saveActor`, and `exportActor`. UI code should consume these APIs and never duplicate merge or validation rules. The data owner should not edit UI paths; the UI owner should treat sample JSON as read-only fixtures and request schema changes through the integration owner.

## Decisions still requiring evidence, not a gate

These are reversible and can be completed during synthesis/implementation: exact UI framework component library (prefer none initially), whether MVP uses browser downloads plus CLI placement or a guarded loopback file adapter, and the initial enum vocabulary for stature/proportion templates. The repository does not justify inventing anatomy values for samples; missing Elf/VenomCultist head/hand/foot/torso decisions should remain visible blocking inputs for a human rather than fabricated defaults.
