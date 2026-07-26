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

export const PROJECTION_VALUES = ["side", "three-quarter", "front"] as const;
export const FACING_VALUES = ["screen-left", "screen-right"] as const;
export const LIGHT_DIRECTION_VALUES = ["upper-left", "upper-right", "upper-center"] as const;

/** How a design master is framed and lit.
 *
 * Every field is optional in the document because worlds authored before the
 * light-direction field existed carry only two of the three, and a legacy
 * import may carry none. Reading is always done through
 * `resolveView()` (src/domain/view.ts), which fills gaps from the world and
 * then from the project defaults and reports which one supplied each value.
 * The same pattern as `anatomy.targetPhysicalHeightPx`: optional to author,
 * guaranteed once resolved. */
export const viewSchema = z.object({
  projection: z.enum(PROJECTION_VALUES).optional(),
  facing: z.enum(FACING_VALUES).optional(),
  /** Key-light direction of the master, kept next to facing because the two
   * are read together when a master image is generated. `pixelStyle.lighting`
   * stays as the free-text rendering note. */
  lightDirection: z.enum(LIGHT_DIRECTION_VALUES).optional(),
});

/** Pixel density presets. The block size is the number of image pixels per
 * logical pixel; it controls detail, not how physically large the character
 * is. Target physical height stays the same across presets. */
export const DENSITY_PRESETS = { "standard-3x3": 3, "detail-2x2": 2 } as const;
export type DensityPresetId = keyof typeof DENSITY_PRESETS | "custom";
export const densityPresetSchema = z.enum(["standard-3x3", "detail-2x2", "custom"]);

export const pixelStyleSchema = z.object({
  styleId: id, logicalBlockPx: canvas,
  /** Selector for logicalBlockPx. Absent on pre-density documents; derived
   * from logicalBlockPx when read. */
  densityPreset: densityPresetSchema.optional(),
  outline: z.string().min(1), lighting: z.string().min(1),
});

export const anatomySchema = z.object({
  stature: z.enum(["tiny", "short", "average", "tall", "very-tall"]),
  /** Authoritative body size: the character's height in image pixels of the
   * production base, independent of pixel density. Absent on pre-density
   * documents, where it is back-calculated as
   * `targetLogicalHeightPx × logicalBlockPx.widthPx`. */
  targetPhysicalHeightPx: z.number().positive().optional(),
  /** Height in logical pixels. Derived from targetPhysicalHeightPx and the
   * density block, and written back so older readers keep working. */
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

export type Projection = (typeof PROJECTION_VALUES)[number];
export type Facing = (typeof FACING_VALUES)[number];
export type LightDirection = (typeof LIGHT_DIRECTION_VALUES)[number];
export type WorldTemplateV1 = z.infer<typeof worldSchema>;
export type ActorDocumentV1 = z.infer<typeof actorSchema>;
export type InheritableSpec = z.infer<typeof inheritableSpecSchema>;
export type EvidenceRef = z.infer<typeof evidenceSchema>;
export type ApprovedException = z.infer<typeof approvedExceptionSchema>;

