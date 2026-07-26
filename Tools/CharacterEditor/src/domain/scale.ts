import { DENSITY_PRESETS, type DensityPresetId, type InheritableSpec } from "../schema";

/**
 * Physical body size and pixel density are independent.
 *
 * `anatomy.targetPhysicalHeightPx` is how tall the character is in image
 * pixels of the production base. `pixelStyle.logicalBlockPx` is how many image
 * pixels make up one logical pixel — detail, not size. Logical height is the
 * quotient of the two and is therefore derived, never authored:
 *
 *     logical = round(physical / block)
 *
 * A 195px character is 65 logical px at 3×3 Standard and 98 at 2×2 Detail. It
 * is the same size on screen either way; only the pixel grid gets finer.
 *
 * Documents written before this split carry only `targetLogicalHeightPx`. They
 * are back-calculated as `logical × block`, which round-trips exactly, so
 * existing resources resolve to the values they always had.
 */

export type ScaleResolution = {
  /** Image pixels per logical pixel. */
  blockPx: number;
  densityPreset: DensityPresetId;
  /** Authoritative body height in image pixels. */
  targetPhysicalHeightPx: number;
  /** Derived: round(targetPhysicalHeightPx / blockPx). */
  targetLogicalHeightPx: number;
  /** Height actually reachable on the block grid: targetLogicalHeightPx × blockPx. */
  effectivePhysicalHeightPx: number;
  /** effectivePhysicalHeightPx − targetPhysicalHeightPx. Non-zero when the
   * requested height is not a multiple of the block. */
  roundingResidualPx: number;
  /** Whether the document authored a physical height or it was back-calculated. */
  authoredFrom: "physical" | "logical";
  /** logicalBlockPx is square. The production pipeline requires this. */
  squareBlock: boolean;
};

export function presetForBlock(blockPx: number): DensityPresetId {
  const match = (Object.entries(DENSITY_PRESETS) as [keyof typeof DENSITY_PRESETS, number][])
    .find(([, size]) => size === blockPx);
  return match ? match[0] : "custom";
}

export function blockForPreset(preset: DensityPresetId, fallbackPx: number): number {
  return preset === "custom" ? fallbackPx : DENSITY_PRESETS[preset];
}

/** Reads the density block from a pixel style. Falls back to the width when the
 * block is not square; `squareBlock` reports the discrepancy. */
export function blockSizeOf(pixelStyle: InheritableSpec["pixelStyle"]): number {
  return pixelStyle.logicalBlockPx.widthPx;
}

export function resolveScale(
  anatomy: Pick<InheritableSpec["anatomy"], "targetLogicalHeightPx" | "targetPhysicalHeightPx">,
  pixelStyle: InheritableSpec["pixelStyle"],
): ScaleResolution {
  const blockPx = blockSizeOf(pixelStyle);
  const squareBlock = pixelStyle.logicalBlockPx.widthPx === pixelStyle.logicalBlockPx.heightPx;
  const authoredFrom = anatomy.targetPhysicalHeightPx ? "physical" : "logical";
  const targetPhysicalHeightPx = anatomy.targetPhysicalHeightPx ?? anatomy.targetLogicalHeightPx * blockPx;
  const targetLogicalHeightPx = blockPx > 0 ? Math.round(targetPhysicalHeightPx / blockPx) : anatomy.targetLogicalHeightPx;
  const effectivePhysicalHeightPx = targetLogicalHeightPx * blockPx;
  return {
    blockPx,
    densityPreset: pixelStyle.densityPreset ?? presetForBlock(blockPx),
    targetPhysicalHeightPx,
    targetLogicalHeightPx,
    effectivePhysicalHeightPx,
    roundingResidualPx: effectivePhysicalHeightPx - targetPhysicalHeightPx,
    authoredFrom,
    squareBlock,
  };
}

/** Returns a spec whose anatomy carries both heights, consistent with the
 * resolved density. Existing resources are unchanged by this: back-calculation
 * reproduces their authored logical height exactly. */
export function applyResolvedScale(spec: InheritableSpec): InheritableSpec {
  const scale = resolveScale(spec.anatomy, spec.pixelStyle);
  return {
    ...spec,
    pixelStyle: { ...spec.pixelStyle, densityPreset: scale.densityPreset },
    anatomy: {
      ...spec.anatomy,
      targetPhysicalHeightPx: scale.targetPhysicalHeightPx,
      targetLogicalHeightPx: scale.targetLogicalHeightPx,
    },
  };
}

/** Logical height a physical height would produce at a given density, for UI
 * previews of a density change before it is applied. */
export function logicalHeightAt(targetPhysicalHeightPx: number, blockPx: number): number {
  return blockPx > 0 ? Math.round(targetPhysicalHeightPx / blockPx) : 0;
}
