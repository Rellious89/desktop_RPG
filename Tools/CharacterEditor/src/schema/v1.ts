import { z } from "zod";

export const SCHEMA_VERSION = "1.0.0" as const;
const id = z.string().min(1).regex(/^[A-Za-z0-9][A-Za-z0-9._-]*$/, "Use a path-safe ASCII ID");
const timestamp = z.string().datetime({ offset: true });
const localizedName = z.object({ en: z.string().min(1), ko: z.string().optional() });
const sizeClass = z.enum(["xs", "s", "m", "l", "xl"]);
const canvas = z.object({ widthPx: z.number().int().positive(), heightPx: z.number().int().positive() });

export const evidenceSchema = z.object({
  path: z.string().min(1), claim: z.string().min(1),
  status: z.enum(["approved", "observed", "historical", "conflicting"]),
});

export const approvedExceptionSchema = z.object({
  ruleId: z.string().min(1), reason: z.string().trim().min(10),
  approvedBy: z.string().optional(), approvedAt: timestamp.optional(), active: z.boolean().default(true),
});

export const viewSchema = z.object({
  projection: z.enum(["side", "three-quarter", "front"]),
  facing: z.enum(["screen-left", "screen-right"]),
});

export const pixelStyleSchema = z.object({
  styleId: id, logicalBlockPx: canvas,
  outline: z.string().min(1), lighting: z.string().min(1),
});

export const anatomySchema = z.object({
  stature: z.enum(["tiny", "short", "average", "tall", "very-tall"]),
  targetLogicalHeightPx: z.number().positive(),
  build: z.enum(["slender", "normal", "broad", "muscular", "massive", "non-humanoid"]),
  proportionTemplateId: id,
  speciesScale: z.number().positive(),
  headSize: sizeClass, handSize: sizeClass, footSize: sizeClass,
  torsoWidth: z.enum(["narrow", "normal", "broad", "very-broad"]),
  isFloatingActor: z.boolean().default(false),
});

export const productionSchema = z.object({
  baseCanvas: canvas,
  largeMotionCanvas: z.object({
    policy: z.enum(["same-as-base", "explicit"]),
    widthPx: z.number().int().positive().optional(), heightPx: z.number().int().positive().optional(),
  }).superRefine((value, ctx) => {
    if (value.policy === "explicit" && (!value.widthPx || !value.heightPx))
      ctx.addIssue({ code: "custom", message: "Explicit canvas requires widthPx and heightPx" });
  }),
  safeMarginPx: z.number().nonnegative().optional(),
  pivotRule: z.enum(["forward-foot-contact", "ground-projection", "actor-origin-custom"]),
  pivot: z.object({
    xNormalized: z.number().min(0).max(1), yNormalized: z.number().min(0).max(1),
    source: z.enum(["measured", "planned"]),
  }).optional(),
  pixelsPerUnit: z.number().positive(), unityVisualScale: z.number().positive(),
  layers: z.array(z.enum(["character-or-outfit", "weapon", "effect"])).length(3),
});

export const inheritableSpecSchema = z.object({ view: viewSchema, pixelStyle: pixelStyleSchema, anatomy: anatomySchema, production: productionSchema });
export const inheritableOverridesSchema = z.object({
  view: viewSchema.partial().optional(),
  pixelStyle: pixelStyleSchema.partial().optional(),
  anatomy: anatomySchema.partial().optional(),
  production: z.object({
    baseCanvas: canvas.optional(), largeMotionCanvas: productionSchema.shape.largeMotionCanvas.optional(),
    safeMarginPx: z.number().nonnegative().optional(), pivotRule: productionSchema.shape.pivotRule.optional(),
    pivot: productionSchema.shape.pivot.optional(), pixelsPerUnit: z.number().positive().optional(),
    unityVisualScale: z.number().positive().optional(), layers: productionSchema.shape.layers.optional(),
  }).optional(),
});

const documentMeta = {
  schemaVersion: z.literal(SCHEMA_VERSION), revision: z.number().int().positive(), updatedAt: timestamp,
};

export const worldSchema = z.object({
  ...documentMeta, documentKind: z.literal("world-template"), worldId: id,
  displayName: localizedName, status: z.enum(["concept", "active", "hold"]),
  description: z.string().min(1), defaults: inheritableSpecSchema,
  allowedSpecies: z.array(z.string().min(1)).default([]),
  allowedProportionTemplates: z.array(id).default([]), evidence: z.array(evidenceSchema).default([]),
});

export const actorSchema = z.object({
  ...documentMeta, documentKind: z.literal("actor"), actorId: id, displayName: localizedName,
  aliases: z.array(z.string().min(1)).default([]), actorType: z.enum(["character", "monster"]),
  worldRef: z.object({ worldId: id, version: z.number().int().positive() }),
  identity: z.object({
    species: z.string().min(1), sex: z.enum(["female", "male", "intersex", "none", "unknown"]),
    ageGroup: z.enum(["child", "adolescent", "adult", "elder", "ageless", "unknown"]),
    role: z.string().min(1), concept: z.string().min(1), status: z.enum(["concept", "active", "hold", "master"]),
  }),
  overrides: inheritableOverridesSchema,
  physicalTraits: z.array(z.string().min(1)),
  appearance: z.object({
    hair: z.string().optional(), eyes: z.string().optional(), skin: z.string().optional(),
    clothing: z.array(z.string().min(1)), materials: z.array(z.string().min(1)),
    palette: z.array(z.object({ role: z.string().min(1), value: z.string().min(1) })),
    decorations: z.array(z.string().min(1)),
  }),
  constraints: z.object({ invariants: z.array(z.string().min(1)), forbidden: z.array(z.string().min(1)) }),
  equipment: z.object({
    weapon: z.object({
      family: z.string().min(1), sizeClass: z.enum(["small", "medium", "large", "oversized"]),
      mainHand: z.enum(["anatomical-left", "anatomical-right", "both", "none"]),
      offHand: z.enum(["anatomical-left", "anatomical-right", "both", "none"]),
      direction: z.string().min(1), structure: z.string().min(1), count: z.number().int().nonnegative(),
      lengthToBodyRatio: z.number().positive().optional(), estimatedOccupancyPercent: z.number().nonnegative().optional(),
    }).optional(),
    secondary: z.array(z.string().min(1)), allowedWeaponFamilies: z.array(z.string().min(1)),
  }),
  approvedExceptions: z.array(approvedExceptionSchema), evidence: z.array(evidenceSchema),
  resourceFolderPath: z.string().optional(),
});

export type WorldTemplateV1 = z.infer<typeof worldSchema>;
export type ActorDocumentV1 = z.infer<typeof actorSchema>;
export type InheritableSpec = z.infer<typeof inheritableSpecSchema>;
export type EvidenceRef = z.infer<typeof evidenceSchema>;
export type ApprovedException = z.infer<typeof approvedExceptionSchema>;

