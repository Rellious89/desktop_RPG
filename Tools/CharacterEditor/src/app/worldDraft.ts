import type { WorldTemplate } from "./types";

/** World templates are immutable versioned documents (spec section 1). The
 * real schema (src/schema/v1.ts) tracks this via a single `revision`
 * counter rather than a separate version field, so "editing" always starts
 * a draft pinned to the next revision rather than mutating in place. */
export function createBlankWorld(): WorldTemplate {
  const now = new Date().toISOString();
  return {
    schemaVersion: "1.0.0",
    documentKind: "world-template",
    revision: 1,
    updatedAt: now,
    worldId: "",
    displayName: { en: "", ko: "" },
    status: "concept",
    description: "",
    defaults: {
      view: { projection: "three-quarter", facing: "screen-right" },
      pixelStyle: {
        styleId: "low-companion-v1",
        logicalBlockPx: { widthPx: 3, heightPx: 3 },
        outline: "",
        lighting: "",
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
    },
    allowedSpecies: [],
    allowedProportionTemplates: ["humanoid-sd-2.5-head"],
    evidence: [],
  };
}

export function draftNextRevisionOf(world: WorldTemplate): WorldTemplate {
  return { ...world, revision: world.revision + 1, updatedAt: new Date().toISOString() };
}
