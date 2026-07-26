import type { InheritableSpec, SizeAuthority } from "../schema";
import { blockSizeOf, logicalHeightAt } from "./scale";

/**
 * Species scale and target physical height are two views of one number.
 *
 * `speciesScale` is height relative to the world's baseline body; it says how
 * tall the character is *for this world* and nothing else — build, proportion
 * template, torso width and head/hand/foot classes are separate design data and
 * are never touched here. `targetPhysicalHeightPx` is the same fact in absolute
 * image pixels:
 *
 *     physical = round(worldBase × speciesScale)
 *     speciesScale = physical / worldBase
 *
 * They used to be authored independently, so a document could claim
 * `210px baseline, 290px target, scale 1.0` — three numbers describing two
 * different sizes. Now whichever one the user last edited is authoritative and
 * the other is derived from it, recorded as `anatomy.sizeAuthority`.
 *
 * Two rounding rules keep round-trips stable:
 *
 *   - Physical height is the production output, so it is stored as a whole pixel.
 *   - Species scale keeps full float precision in the document. The UI shows it
 *     rounded to three decimals, but the rounded value is never written back —
 *     otherwise 290 → 1.381 → 289.99 → 290 → … would drift.
 */

export const SPECIES_SCALE_DISPLAY_DECIMALS = 3;

/** Half a pixel: below this the two fields agree as closely as whole-pixel
 * storage allows, so a difference this small is rounding, not a conflict. */
const CONFLICT_TOLERANCE_PX = 0.5;

export type SizeInput = {
  targetPhysicalHeightPx?: number;
  targetLogicalHeightPx?: number;
  speciesScale?: number;
  sizeAuthority?: SizeAuthority;
};

export type SizeResolution = {
  /** Whole pixels — the production output size. */
  targetPhysicalHeightPx: number;
  /** Full precision; round only for display. */
  speciesScale: number;
  sizeAuthority: SizeAuthority;
  /** True when the document carried no `sizeAuthority` and it was inferred. */
  authorityInferred: boolean;
  worldBasePhysicalHeightPx: number;
  /** Set when the document authored both fields and they disagreed. The
   * physical height wins; this records what was replaced so the migration can
   * be reported rather than applied silently. */
  conflict: { documentSpeciesScale: number; resolvedSpeciesScale: number } | null;
};

export const physicalHeightFromScale = (worldBasePhysicalHeightPx: number, speciesScale: number): number =>
  Math.round(worldBasePhysicalHeightPx * speciesScale);

export const speciesScaleFromHeight = (worldBasePhysicalHeightPx: number, targetPhysicalHeightPx: number): number =>
  worldBasePhysicalHeightPx > 0 ? targetPhysicalHeightPx / worldBasePhysicalHeightPx : 1;

/** Display rounding. Never store this — see the module comment. */
export const displaySpeciesScale = (speciesScale: number): number =>
  Number(speciesScale.toFixed(SPECIES_SCALE_DISPLAY_DECIMALS));

const positive = (value: number | undefined): number | undefined =>
  typeof value === "number" && Number.isFinite(value) && value > 0 ? value : undefined;

/**
 * Reconciles the pair against the world baseline.
 *
 * Migration order for documents with no `sizeAuthority` (the values are the
 * merged world+actor ones, so "authored" means someone supplied it):
 *
 *   1. a physical height exists → it wins and the scale is back-calculated
 *   2. only a scale exists → the physical height is computed from it
 *   3. neither exists → the world baseline, scale 1
 *   4. both exist but disagree → the physical height wins and `conflict` records it
 */
export function resolveSize(input: SizeInput, blockPx: number, worldBasePhysicalHeightPx: number): SizeResolution {
  const base = positive(worldBasePhysicalHeightPx) ?? 0;
  const authoredPhysical =
    positive(input.targetPhysicalHeightPx) ??
    (positive(input.targetLogicalHeightPx) !== undefined && blockPx > 0
      ? (input.targetLogicalHeightPx as number) * blockPx
      : undefined);
  const authoredScale = positive(input.speciesScale);

  const sizeAuthority: SizeAuthority =
    input.sizeAuthority ??
    (authoredPhysical !== undefined ? "physical-height" : authoredScale !== undefined ? "species-scale" : "physical-height");

  let targetPhysicalHeightPx: number;
  let speciesScale: number;
  if (sizeAuthority === "species-scale") {
    speciesScale = authoredScale ?? 1;
    targetPhysicalHeightPx = base > 0 ? physicalHeightFromScale(base, speciesScale) : (authoredPhysical ?? 0);
  } else {
    targetPhysicalHeightPx = Math.round(authoredPhysical ?? base);
    speciesScale = base > 0 ? speciesScaleFromHeight(base, targetPhysicalHeightPx) : (authoredScale ?? 1);
  }

  // Only a document that supplied both can be in conflict; a derived value
  // never disagrees with the one it came from.
  const conflict =
    sizeAuthority === "physical-height" &&
    authoredPhysical !== undefined &&
    authoredScale !== undefined &&
    base > 0 &&
    Math.abs(authoredScale * base - targetPhysicalHeightPx) >= CONFLICT_TOLERANCE_PX
      ? { documentSpeciesScale: authoredScale, resolvedSpeciesScale: speciesScale }
      : null;

  return {
    targetPhysicalHeightPx,
    speciesScale,
    sizeAuthority,
    authorityInferred: input.sizeAuthority === undefined,
    worldBasePhysicalHeightPx: base,
    conflict,
  };
}

/** The world's own baseline body height in physical pixels — the 1.0 of
 * species scale for every actor in that world. */
export function worldBasePhysicalHeightPx(worldDefaults: InheritableSpec): number {
  const blockPx = blockSizeOf(worldDefaults.pixelStyle);
  const anatomy = worldDefaults.anatomy;
  return Math.round(anatomy.targetPhysicalHeightPx ?? anatomy.targetLogicalHeightPx * blockPx);
}

/**
 * Returns a spec whose anatomy carries a consistent
 * physical height / species scale / authority triple. Runs before
 * `applyResolvedScale`, which then derives the logical height from whatever
 * physical height this settled on.
 */
export function applyResolvedSize(spec: InheritableSpec, worldBasePx: number): InheritableSpec {
  const size = resolveSize(spec.anatomy, blockSizeOf(spec.pixelStyle), worldBasePx);
  return {
    ...spec,
    anatomy: {
      ...spec.anatomy,
      targetPhysicalHeightPx: size.targetPhysicalHeightPx,
      speciesScale: size.speciesScale,
      sizeAuthority: size.sizeAuthority,
    },
  };
}

/** The one-line relation shown between the two fields and exported alongside
 * them, so a reader can check the arithmetic without recomputing it. */
export function sizeRelationText(size: Pick<SizeResolution, "worldBasePhysicalHeightPx" | "speciesScale" | "targetPhysicalHeightPx">): string {
  return `${size.worldBasePhysicalHeightPx}px × ${displaySpeciesScale(size.speciesScale)} = ${size.targetPhysicalHeightPx}px`;
}

/** Logical height for a physical height at a density. Re-exported here so size
 * callers do not have to reach into two modules. */
export { logicalHeightAt };
