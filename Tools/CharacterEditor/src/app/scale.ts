/**
 * UI-facing seam for the physical-height/pixel-density rule, mirroring how
 * src/app/types.ts re-exports the Schema v1 contract.
 *
 * These are the one part of src/domain the UI reaches statically rather than
 * through DomainApi: they are pure arithmetic with no I/O, and forms need them
 * to *render* — a document authored before the split carries no physical
 * height, so the field would have nothing to show until a resolve round-trip
 * completed. Re-exporting through one module keeps the math defined exactly
 * once (src/domain/scale.ts) while components keep importing only from
 * src/app.
 */
export { blockForPreset, logicalHeightAt, presetForBlock, resolveScale } from "../domain/scale";
export type { ScaleResolution } from "../domain/scale";
export {
  displaySpeciesScale, physicalHeightFromScale, resolveSize, sizeRelationText,
  speciesScaleFromHeight, worldBasePhysicalHeightPx,
} from "../domain/size";
export type { SizeResolution } from "../domain/size";
export type { SizeAuthority } from "../schema";
export { DENSITY_PRESETS } from "../schema";
export type { DensityPresetId } from "../schema";

/** Density choices offered in the UI, in presentation order. `custom` is not
 * offered — it only appears when a hand-authored document uses another block. */
export const DENSITY_OPTIONS = [
  { id: "standard-3x3", label: "3×3 Standard", note: "KeyBuddy 기본" },
  { id: "detail-2x2", label: "2×2 Detail", note: "같은 크기, 더 촘촘한 픽셀" },
] as const;
