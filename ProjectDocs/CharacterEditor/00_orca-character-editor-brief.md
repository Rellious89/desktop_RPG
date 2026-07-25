# Character Editor MVP — Orca Orchestration Brief

## Objective

Build a standalone local application, working title **Character Editor**, that combines reusable world templates with
per-character inputs, validates missing or conflicting design decisions, compares the new actor against existing actors,
and exports a complete character sheet as versioned JSON and readable Markdown.

This MVP is a specification editor, not an AI image generator. The exported sheet will be given to Codex later to create
concept art and a logical-pixel master image.

## Product background

The current pipeline overloaded one `scale` value with several different meanings:

- stature / height
- build / body mass
- body-proportion template
- uniform species or creature scale
- Unity Transform display scale

This caused Elfguardian (`1.3`) to become larger than VenomCultist (`1.0`) not only in height but also in head, hands,
feet, and torso mass. A taller normal humanoid must not automatically be treated as a uniformly enlarged giant.

The editor must separate at least:

- Stature class and target logical height
- Build
- Sex
- Age group
- Proportion template
- Species scale
- Head, hand, foot, and torso-width classes
- Physical traits
- Unity visual scale (normally `1.0`)

World templates provide defaults. Individual characters and monsters inherit those defaults and override only explicit
exceptions.

Current worlds:

- Animal Land / 애니멀랜드
- Fantasia / 판타지아
- World of the Dead / 망자들의 세계

Known reference actors include Elfguardian, VenomCultist, Specter, CatKnight, CatMage/BlackCatMage, and
CopperAxeBarbarian/Barbarian. Inspect the repository and use the actual canonical names and data.

## Source material to inspect

Treat repository evidence as the source of truth and report conflicts between documents and assets:

- `ProjectDocs/ArtPipeline`
- `ProjectDocs/DesignRules`
- `ProjectDocs/ArtPipeline/Characters`
- `ProjectDocs/ArtPipeline/Enemies`
- `Assets/Art/Character`
- `Assets/Art/Enemy`
- character/enemy Motion Profile data relevant to canvas, pivot, PPU, and animation constraints

Preserve unrelated user changes.

## MVP features

### World templates

Create, edit, save, and version defaults for:

- world ID and display name
- approved view and facing
- pixel style and logical-pixel block size
- default species/proportion templates
- default logical height and build
- outline and lighting rules
- default canvas and large-motion canvas policy
- PPU and Unity visual scale defaults

### Actor sheet

Collect:

- ID, display name, Character/Monster type, world, species, sex, age group, role, one-line concept
- stature, target logical height, build, proportion template, species scale
- head/hand/foot size, torso width, physical traits
- hair, eyes, skin, clothing, materials, palette, decorations
- invariants and forbidden elements
- weapon family, size, main/off hand, direction, structure, secondary equipment
- base and large-motion canvas, logical-pixel density, pivot rule, PPU, Unity visual scale
- production-layer policy: Character/Outfit + Weapon + Effect

Weapon families are character-restricted. For example, Elfguardian uses polearms and CatMage uses staffs.

### Inheritance and UX

- Selecting a world applies its defaults.
- Clearly distinguish inherited values from explicit actor overrides.
- Do not require direct JSON editing.
- Explain specialized terms in the UI.
- Guide the user through required fields.
- Show all missing fields, conflicts, warnings, and approved exceptions before export.

### Comparison

Compare the draft actor with an existing same-world reference actor:

- stature and logical height
- build and proportion template
- head, hand, foot, and torso-width classes
- species scale
- weapon occupancy
- default and large-motion canvas

Text/numeric comparison is sufficient for MVP. Silhouette overlay is a future feature.

### Validation

At minimum detect:

- missing required inputs
- Stature and Species Scale being conflated
- Normal build conflicting with excessive torso width
- unexpected proportion-template mismatch within one world/species
- large weapons likely exceeding a 512 canvas
- Unity visual scale differing from `1.0`
- weapon family not allowed for the actor
- excessive head/hand/foot differences between otherwise equivalent humanoids

Distinguish blocking errors from overridable warnings. Record approved exceptions in exported data.

### Persistence and export

Support:

- `{CharacterId}.character.json`
- `{CharacterId}.character.md`
- JSON import/edit/re-export
- schema version
- referenced world-template ID and version
- calculated values, interpretations, warnings, and approved exceptions in Markdown

Include initial templates and sample sheets for Elfguardian and VenomCultist.

## Standalone application constraints

This is not a Unity EditorWindow. Choose a maintainable standalone local app stack after inspecting the repository and
available runtimes.

Priorities:

1. Easy local launch on macOS
2. No external server or account
3. Project-local data files
4. Reasonable future Windows support
5. Non-technical form-based UI
6. Low packaging and maintenance burden

Reversible technical decisions can be made autonomously and documented. Raise a decision gate only for choices with a
material long-term cost.

## Out of scope

- AI/LLM/image-generation API integration
- PerfectPixel automation
- Unity asset generation
- motion animation editing
- real-time character rendering
- universal equipment-combination system
- complex particle/VFX implementation

## Required Orca orchestration

Use real Orca orchestration provenance: `task-create`, `dispatch --inject`, and worker `worker_done` messages. Verify tasks
and dispatches exist. Do not merely open two terminals and send untracked prompts.

### Wave 1 — parallel research/design

Dispatch two independent tasks in parallel. They must write separate reports and must not edit implementation files.

**Codex worker**

- audit repository rules and actual actor data
- identify the current source of truth and conflicts
- propose Schema v1 and template/inheritance model
- compare standalone technology choices and recommend one
- propose storage and testing strategy

**Claude worker**

- design user flow and screen structure
- define required inputs, calculations, validations, and warning copy
- design comparison UX
- provide a representative Markdown export
- review product risks and missing requirements

### Synthesis gate

After both `worker_done` messages, the coordinator creates one integrated MVP specification covering:

- technology stack and app location
- Schema v1
- UI flow
- inheritance and validation rules
- storage/export paths
- test strategy
- explicit file ownership for implementation

Resolve reversible differences autonomously. Use a decision gate only for a material irreversible conflict.

### Wave 2 — parallel implementation

Dispatch non-overlapping implementation ownership, adjusted to the selected stack. A recommended split is:

**Codex worker**: data model, schema, persistence, JSON/Markdown export, unit tests.

**Claude worker**: application UI, forms, inherited/override presentation, comparison view, validation display, UI tests.

Workers must not concurrently edit the same files. Use isolated worktrees if needed and designate one integration owner.

### Wave 3 — integration and verification

The integration owner must:

- integrate both implementation branches/worktrees
- build and launch the app
- verify create/save/load/edit/export flows
- verify sample world templates
- verify Elfguardian and VenomCultist samples
- run tests
- document launch and usage
- list deferred features

Do not claim completion based only on compilation.

## Required deliverables

- runnable Character Editor MVP
- versioned Schema v1
- world-template samples
- Elfguardian sample
- VenomCultist sample
- JSON and Markdown exports
- launch/user documentation
- automated test results
- repository audit and integrated design reports
- deferred-feature list

