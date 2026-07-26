import { describe, expect, it } from "vitest";
import { elfGuardian, fantasia, venomCultist } from "../data";
import { buildExport } from "../export";
import { resolveActor } from "./resolve";
import { applyResolvedScale, logicalHeightAt, presetForBlock, resolveScale } from "./scale";
import { RULE_IDS, validateActor } from "./validation";

const spec = (blockPx: number, anatomy: { targetLogicalHeightPx: number; targetPhysicalHeightPx?: number }) => ({
  pixelStyle: {
    styleId: "test", logicalBlockPx: { widthPx: blockPx, heightPx: blockPx },
    outline: "o", lighting: "l",
  },
  anatomy,
});

describe("physical height and pixel density", () => {
  it("back-calculates a physical height for documents authored before the split", () => {
    const { anatomy, pixelStyle } = spec(3, { targetLogicalHeightPx: 65 });
    const scale = resolveScale(anatomy, pixelStyle);
    expect(scale.authoredFrom).toBe("logical");
    expect(scale.targetPhysicalHeightPx).toBe(195);
    // The whole point of back-calculation: the logical height it reproduces is
    // the one the document already had, so no existing resource changes size.
    expect(scale.targetLogicalHeightPx).toBe(65);
    expect(scale.roundingResidualPx).toBe(0);
  });

  it("keeps the body the same size across densities and only moves the logical height", () => {
    const standard = resolveScale(spec(3, { targetLogicalHeightPx: 65, targetPhysicalHeightPx: 195 }).anatomy, spec(3, { targetLogicalHeightPx: 65 }).pixelStyle);
    const detail = resolveScale(spec(2, { targetLogicalHeightPx: 65, targetPhysicalHeightPx: 195 }).anatomy, spec(2, { targetLogicalHeightPx: 65 }).pixelStyle);
    expect(standard.targetPhysicalHeightPx).toBe(detail.targetPhysicalHeightPx);
    expect(standard.targetLogicalHeightPx).toBe(65);
    expect(detail.targetLogicalHeightPx).toBe(98);
    expect(standard.densityPreset).toBe("standard-3x3");
    expect(detail.densityPreset).toBe("detail-2x2");
  });

  it("reports the residual when the requested height is not a multiple of the block", () => {
    // 195 is not divisible by 2, so 2x2 production lands one pixel taller.
    const detail = resolveScale(spec(2, { targetLogicalHeightPx: 65, targetPhysicalHeightPx: 195 }).anatomy, spec(2, { targetLogicalHeightPx: 65 }).pixelStyle);
    expect(detail.effectivePhysicalHeightPx).toBe(196);
    expect(detail.roundingResidualPx).toBe(1);
  });

  it("labels an unrecognized block as custom", () => {
    expect(presetForBlock(3)).toBe("standard-3x3");
    expect(presetForBlock(2)).toBe("detail-2x2");
    expect(presetForBlock(4)).toBe("custom");
  });

  it("is idempotent, so re-resolving a normalized spec does not drift", () => {
    const once = applyResolvedScale({ ...spec(3, { targetLogicalHeightPx: 65 }) } as never);
    const twice = applyResolvedScale(once);
    expect(twice).toEqual(once);
  });

  it("previews the logical height a density would produce", () => {
    expect(logicalHeightAt(195, 3)).toBe(65);
    expect(logicalHeightAt(195, 2)).toBe(98);
    expect(logicalHeightAt(195, 0)).toBe(0);
  });
});

describe("existing resources are unaffected", () => {
  it("resolves ElfGuardian to the heights it has always had", () => {
    const resolved = resolveActor(elfGuardian, fantasia).resolved;
    expect(resolved.anatomy.targetLogicalHeightPx).toBe(91);
    expect(resolved.anatomy.targetPhysicalHeightPx).toBe(273);
    expect(resolved.pixelStyle.densityPreset).toBe("standard-3x3");
  });

  it("attributes the derived physical height to whoever authored the logical one", () => {
    const origins = resolveActor(elfGuardian, fantasia).fieldOrigins;
    expect(origins["anatomy.targetPhysicalHeightPx"].source).toBe("actor");
    expect(origins["pixelStyle.densityPreset"].source).toBe("world");
  });

  it("raises no new diagnostics for a pre-split actor", () => {
    const ids = validateActor(elfGuardian, fantasia).map((d) => d.ruleId);
    expect(ids).not.toContain(RULE_IDS.densityResidual);
    expect(ids).not.toContain(RULE_IDS.densityBlock);
    expect(ids).not.toContain(RULE_IDS.densityBackCalc);
  });

  it("reports the world-relative size in physical px, unchanged at equal density", () => {
    const envelope = buildExport(elfGuardian, fantasia, [venomCultist]);
    expect(envelope.calculated.targetPhysicalHeightPx).toBe(273);
    expect(envelope.calculated.logicalPixelBlockPx).toBe(3);
    // 91/70 and 273/210 are the same ratio.
    expect(envelope.calculated.heightFromWorldBaselinePercent).toBeCloseTo(30, 5);
  });
});

describe("a Detail-density actor in a Standard world", () => {
  const detailActor = {
    ...elfGuardian,
    actorId: "ElfGuardianDetail",
    overrides: {
      ...elfGuardian.overrides,
      anatomy: { ...elfGuardian.overrides.anatomy, targetPhysicalHeightPx: 273, targetLogicalHeightPx: 137 },
      pixelStyle: { densityPreset: "detail-2x2" as const, logicalBlockPx: { widthPx: 2, heightPx: 2 } },
    },
  };

  it("stays the same size as its Standard-density peers", () => {
    const resolved = resolveActor(detailActor, fantasia).resolved;
    expect(resolved.anatomy.targetPhysicalHeightPx).toBe(273);
    expect(resolved.anatomy.targetLogicalHeightPx).toBe(137);
    expect(resolved.pixelStyle.logicalBlockPx).toEqual({ widthPx: 2, heightPx: 2 });
    const envelope = buildExport(detailActor, fantasia, []);
    expect(envelope.calculated.heightFromWorldBaselinePercent).toBeCloseTo(30, 5);
  });

  it("warns when a density override has no pinned physical height to hold the size", () => {
    const unpinned = {
      ...detailActor,
      overrides: {
        ...detailActor.overrides,
        anatomy: { ...elfGuardian.overrides.anatomy },
      },
    };
    const ids = validateActor(unpinned, fantasia).map((d) => d.ruleId);
    expect(ids).toContain(RULE_IDS.densityBackCalc);
  });
});
