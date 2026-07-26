import type { ActorDocument, RuleId } from "./types";

/**
 * Pure, UI-owned helpers for constructing and editing ActorDocument drafts.
 * These only assign plain object fields — they never merge with a world
 * template, compute derived values, or decide validity. That logic stays in
 * the Codex-owned domain module and is reached exclusively through the
 * DomainApi (resolveActor/validateActor/etc). Field names/nesting mirror
 * src/schema/v1.ts (actorSchema) exactly.
 */

type Overrides = ActorDocument["overrides"];
type Anatomy = NonNullable<Overrides["anatomy"]>;
type Production = NonNullable<Overrides["production"]>;
type View = NonNullable<Overrides["view"]>;
type PixelStyle = NonNullable<Overrides["pixelStyle"]>;
type Identity = ActorDocument["identity"];
type Appearance = ActorDocument["appearance"];
type Constraints = ActorDocument["constraints"];
type Equipment = ActorDocument["equipment"];
type Weapon = NonNullable<Equipment["weapon"]>;

function touch(actor: ActorDocument): ActorDocument {
  return { ...actor, updatedAt: new Date().toISOString(), revision: actor.revision + 1 };
}

export function createBlankActor(
  worldRef: { worldId: string; version: number },
  actorType: ActorDocument["actorType"] = "character",
): ActorDocument {
  const now = new Date().toISOString();
  return {
    schemaVersion: "1.0.0",
    documentKind: "actor",
    revision: 1,
    updatedAt: now,
    actorId: "",
    displayName: { en: "", ko: "" },
    aliases: [],
    actorType,
    worldRef,
    identity: {
      species: "",
      sex: "unknown",
      ageGroup: "unknown",
      role: "",
      concept: "",
      status: "concept",
    },
    overrides: {},
    physicalTraits: [],
    appearance: { clothing: [], materials: [], palette: [], decorations: [] },
    constraints: { invariants: [], forbidden: [] },
    equipment: { secondary: [], allowedWeaponFamilies: [] },
    approvedExceptions: [],
    evidence: [],
  };
}

export function setTopLevel(
  actor: ActorDocument,
  patch: Partial<Pick<ActorDocument, "actorId" | "displayName" | "aliases" | "actorType" | "resourceFolderPath">>,
): ActorDocument {
  return touch({ ...actor, ...patch });
}

export function setIdentity(actor: ActorDocument, patch: Partial<Identity>): ActorDocument {
  return touch({ ...actor, identity: { ...actor.identity, ...patch } });
}

export function setAnatomyOverride<K extends keyof Anatomy>(actor: ActorDocument, key: K, value: Anatomy[K]): ActorDocument {
  return touch({
    ...actor,
    overrides: { ...actor.overrides, anatomy: { ...actor.overrides.anatomy, [key]: value } },
  });
}

export function resetAnatomyOverride(actor: ActorDocument, key: keyof Anatomy): ActorDocument {
  const next: Partial<Anatomy> = { ...(actor.overrides.anatomy ?? {}) };
  delete next[key];
  const hasRemaining = Object.keys(next).length > 0;
  return touch({ ...actor, overrides: { ...actor.overrides, anatomy: hasRemaining ? (next as Anatomy) : undefined } });
}

export function setProductionOverride<K extends keyof Production>(
  actor: ActorDocument,
  key: K,
  value: Production[K],
): ActorDocument {
  return touch({
    ...actor,
    overrides: { ...actor.overrides, production: { ...actor.overrides.production, [key]: value } },
  });
}

export function resetProductionOverride(actor: ActorDocument, key: keyof Production): ActorDocument {
  const next: Partial<Production> = { ...(actor.overrides.production ?? {}) };
  delete next[key];
  const hasRemaining = Object.keys(next).length > 0;
  return touch({
    ...actor,
    overrides: { ...actor.overrides, production: hasRemaining ? (next as Production) : undefined },
  });
}

export function setViewOverride<K extends keyof View>(actor: ActorDocument, key: K, value: View[K]): ActorDocument {
  return touch({ ...actor, overrides: { ...actor.overrides, view: { ...actor.overrides.view, [key]: value } } });
}

export function resetViewOverride(actor: ActorDocument, key: keyof View): ActorDocument {
  const next: Partial<View> = { ...(actor.overrides.view ?? {}) };
  delete next[key];
  const hasRemaining = Object.keys(next).length > 0;
  return touch({ ...actor, overrides: { ...actor.overrides, view: hasRemaining ? (next as View) : undefined } });
}

export function setPixelStyleOverride<K extends keyof PixelStyle>(
  actor: ActorDocument,
  key: K,
  value: PixelStyle[K],
): ActorDocument {
  return touch({
    ...actor,
    overrides: { ...actor.overrides, pixelStyle: { ...actor.overrides.pixelStyle, [key]: value } },
  });
}

/**
 * Body size and pixel density move together, so they are written together.
 *
 * `targetPhysicalHeightPx` is the authored size; `targetLogicalHeightPx` is its
 * mirror at the current density, written so readers that predate the split (and
 * the schema's required field) still see the right number. The caller computes
 * both from domain/scale — this helper only assigns them.
 *
 * Pinning the physical height matters most when the density is changed on an
 * actor that never authored one: without the pin, the document would be
 * re-read at the new block on the next resolve and the character would shrink.
 */
export function setBodyScale(
  actor: ActorDocument,
  scale: {
    targetPhysicalHeightPx: number;
    targetLogicalHeightPx: number;
    densityPreset?: NonNullable<PixelStyle["densityPreset"]>;
    blockPx?: number;
  },
): ActorDocument {
  const pixelStyle: Partial<PixelStyle> = { ...actor.overrides.pixelStyle };
  if (scale.densityPreset && scale.blockPx) {
    pixelStyle.densityPreset = scale.densityPreset;
    pixelStyle.logicalBlockPx = { widthPx: scale.blockPx, heightPx: scale.blockPx };
  }
  return touch({
    ...actor,
    overrides: {
      ...actor.overrides,
      pixelStyle: Object.keys(pixelStyle).length > 0 ? (pixelStyle as PixelStyle) : undefined,
      anatomy: {
        ...actor.overrides.anatomy,
        targetPhysicalHeightPx: scale.targetPhysicalHeightPx,
        targetLogicalHeightPx: scale.targetLogicalHeightPx,
      },
    },
  });
}

/** Drops the height override and inherits the world's body size again. Both
 * heights go together — a lone logical override would contradict the world's
 * physical height at the next density change. */
export function resetBodyHeightOverride(actor: ActorDocument): ActorDocument {
  const next: Partial<Anatomy> = { ...(actor.overrides.anatomy ?? {}) };
  delete next.targetPhysicalHeightPx;
  delete next.targetLogicalHeightPx;
  const hasRemaining = Object.keys(next).length > 0;
  return touch({ ...actor, overrides: { ...actor.overrides, anatomy: hasRemaining ? (next as Anatomy) : undefined } });
}

/**
 * Returns to the world's pixel density. Any pinned physical height is kept —
 * the body must not resize — so the caller supplies the logical mirror
 * recomputed at the world's block.
 */
export function resetDensityOverride(actor: ActorDocument, worldLogicalHeightPx: number): ActorDocument {
  const pixelStyle: Partial<PixelStyle> = { ...(actor.overrides.pixelStyle ?? {}) };
  delete pixelStyle.densityPreset;
  delete pixelStyle.logicalBlockPx;
  const anatomy = actor.overrides.anatomy;
  return touch({
    ...actor,
    overrides: {
      ...actor.overrides,
      pixelStyle: Object.keys(pixelStyle).length > 0 ? (pixelStyle as PixelStyle) : undefined,
      anatomy: anatomy?.targetPhysicalHeightPx === undefined
        ? anatomy
        : { ...anatomy, targetLogicalHeightPx: worldLogicalHeightPx },
    },
  });
}

export function resetPixelStyleOverride(actor: ActorDocument, key: keyof PixelStyle): ActorDocument {
  const next: Partial<PixelStyle> = { ...(actor.overrides.pixelStyle ?? {}) };
  delete next[key];
  const hasRemaining = Object.keys(next).length > 0;
  return touch({
    ...actor,
    overrides: { ...actor.overrides, pixelStyle: hasRemaining ? (next as PixelStyle) : undefined },
  });
}

export function setPhysicalTraits(actor: ActorDocument, traits: string[]): ActorDocument {
  return touch({ ...actor, physicalTraits: traits });
}

export function setAppearance(actor: ActorDocument, patch: Partial<Appearance>): ActorDocument {
  return touch({ ...actor, appearance: { ...actor.appearance, ...patch } });
}

export function setConstraints(actor: ActorDocument, patch: Partial<Constraints>): ActorDocument {
  return touch({ ...actor, constraints: { ...actor.constraints, ...patch } });
}

export function setEquipment(actor: ActorDocument, patch: Partial<Equipment>): ActorDocument {
  return touch({ ...actor, equipment: { ...actor.equipment, ...patch } });
}

export function setWeapon(actor: ActorDocument, patch: Partial<Weapon> | undefined): ActorDocument {
  if (patch === undefined) {
    return touch({ ...actor, equipment: { ...actor.equipment, weapon: undefined } });
  }
  const base: Weapon =
    actor.equipment.weapon ?? {
      family: "",
      sizeClass: "medium",
      mainHand: "none",
      offHand: "none",
      direction: "",
      structure: "",
      count: 0,
    };
  return touch({ ...actor, equipment: { ...actor.equipment, weapon: { ...base, ...patch } } });
}

export function upsertApprovedException(actor: ActorDocument, ruleId: RuleId, reason: string): ActorDocument {
  const others = actor.approvedExceptions.filter((exception) => exception.ruleId !== ruleId);
  return touch({
    ...actor,
    approvedExceptions: [...others, { ruleId, reason, active: true, approvedAt: new Date().toISOString() }],
  });
}

export function removeApprovedException(actor: ActorDocument, ruleId: RuleId): ActorDocument {
  return touch({
    ...actor,
    approvedExceptions: actor.approvedExceptions.filter((exception) => exception.ruleId !== ruleId),
  });
}
