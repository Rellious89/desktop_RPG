import type { ActorDocument, InheritableSpec, ValidationDiagnostic, WorldTemplate } from "./types";

/**
 * Test-only fixture builders for UI tests (*.ui.test.tsx). Field names and
 * nesting mirror the real Schema v1 (src/schema/v1.ts) exactly. These are
 * not used by production code and do not reimplement the domain's actual
 * merge/validation logic — tests inject already-resolved/validated props
 * directly rather than exercising the real resolveActor/validateActor.
 */

const COMMON_DEFAULTS: InheritableSpec = {
  view: { projection: "three-quarter", facing: "screen-right", lightDirection: "upper-left" },
  pixelStyle: {
    styleId: "low-companion-v1",
    logicalBlockPx: { widthPx: 3, heightPx: 3 },
    outline: "Thick stepped near-black outline",
    lighting: "Upper-left",
  },
  anatomy: {
    stature: "average",
    targetLogicalHeightPx: 70,
    build: "normal",
    proportionTemplateId: "humanoid-sd-2.5-head",
    speciesScale: 1,
    headSize: "m",
    handSize: "m",
    footSize: "m",
    torsoWidth: "normal",
    isFloatingActor: false,
  },
  production: {
    baseCanvas: { widthPx: 512, heightPx: 512 },
    largeMotionCanvas: { policy: "same-as-base" },
    pivotRule: "forward-foot-contact",
    pixelsPerUnit: 200,
    unityVisualScale: 1,
    layers: ["character-or-outfit", "weapon", "effect"],
  },
};

export function makeWorld(overrides: Partial<WorldTemplate> = {}): WorldTemplate {
  return {
    schemaVersion: "1.0.0",
    documentKind: "world-template",
    revision: 1,
    updatedAt: "2026-07-25T00:00:00+09:00",
    worldId: "HUMAN-FANTASY-01",
    displayName: { en: "Fantasia", ko: "판타지아" },
    status: "active",
    description: "인간 왕국들이 전쟁하는 판타지 세계",
    defaults: structuredClone(COMMON_DEFAULTS),
    allowedSpecies: ["human", "elf"],
    allowedProportionTemplates: ["humanoid-sd-2.5-head"],
    evidence: [],
    ...overrides,
  };
}

export function makeActor(overrides: Partial<ActorDocument> = {}): ActorDocument {
  return {
    schemaVersion: "1.0.0",
    documentKind: "actor",
    revision: 1,
    updatedAt: "2026-07-25T00:00:00+09:00",
    actorId: "ElfGuardian",
    displayName: { en: "Leaf Glaive Elf", ko: "나뭇잎 글레이브 엘프" },
    aliases: ["LeafGlaiveElf"],
    actorType: "character",
    worldRef: { worldId: "HUMAN-FANTASY-01", version: 1 },
    identity: {
      species: "elf",
      sex: "unknown",
      ageGroup: "adult",
      role: "글레이브 전사",
      concept: "숲의 가벼운 천옷을 두른 금발 엘프 전사",
      status: "master",
    },
    overrides: {
      anatomy: { stature: "tall", targetLogicalHeightPx: 91, speciesScale: 1 },
    },
    physicalTraits: ["뾰족귀"],
    appearance: {
      hair: "금발",
      skin: "밝은 피부",
      clothing: ["나뭇잎 장식 천옷"],
      materials: ["천", "가죽"],
      palette: [{ role: "hair", value: "golden blond" }],
      decorations: [],
    },
    constraints: {
      invariants: ["금발", "완전히 곧은 흰 창대"],
      forbidden: ["활과 화살통", "방패"],
    },
    equipment: {
      weapon: {
        family: "glaive",
        sizeClass: "large",
        mainHand: "both",
        offHand: "both",
        direction: "화면 오른쪽 위",
        structure: "직선 창대, 한쪽 곡선 날",
        count: 1,
        lengthToBodyRatio: 1.2,
      },
      secondary: [],
      allowedWeaponFamilies: ["glaive"],
    },
    approvedExceptions: [],
    evidence: [],
    ...overrides,
  };
}

export function resolvedFromWorldAndOverrides(world: WorldTemplate, actor: ActorDocument): InheritableSpec {
  const o = actor.overrides;
  return {
    view: { ...world.defaults.view, ...o.view },
    pixelStyle: { ...world.defaults.pixelStyle, ...o.pixelStyle },
    anatomy: { ...world.defaults.anatomy, ...o.anatomy },
    production: {
      ...world.defaults.production,
      ...o.production,
      largeMotionCanvas: { ...world.defaults.production.largeMotionCanvas, ...o.production?.largeMotionCanvas },
    },
  };
}

export function makeDiagnostic(overrides: Partial<ValidationDiagnostic> = {}): ValidationDiagnostic {
  return {
    ruleId: "required-field",
    severity: "error",
    message: "필수 항목이 비어 있습니다.",
    overridable: false,
    exceptionApproved: false,
    blocksExport: true,
    ...overrides,
  };
}
