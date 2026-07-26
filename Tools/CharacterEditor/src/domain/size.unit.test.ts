import { describe, expect, it } from "vitest";
import { animalLand } from "../data";
import type { ActorDocumentV1, WorldTemplateV1 } from "../schema";
import { buildExport, exportJson, exportMarkdown } from "../export";
import { resolveActor } from "./resolve";
import { RULE_IDS, validateActor } from "./validation";
import {
  displaySpeciesScale, physicalHeightFromScale, resolveSize, speciesScaleFromHeight, worldBasePhysicalHeightPx,
} from "./size";
import { logicalHeightAt } from "./scale";

/**
 * Species scale and target physical height describe one size against the
 * world's baseline body. The world here is Animal Land at 70 logical px on a
 * 3×3 block — a 210px baseline, the number used throughout the spec examples.
 */

const BASE = 210;

/** Animal Land with its baseline body pinned at exactly 210 physical px. */
const world210 = (): WorldTemplateV1 => {
  const world = structuredClone(animalLand);
  world.defaults.anatomy.targetPhysicalHeightPx = BASE;
  world.defaults.anatomy.targetLogicalHeightPx = 70;
  world.defaults.anatomy.speciesScale = 1;
  return world;
};

const actorWith = (anatomy: Record<string, unknown>): ActorDocumentV1 => ({
  schemaVersion: "1.0.0", documentKind: "actor", revision: 1, updatedAt: "2026-07-26T00:00:00+09:00",
  actorId: "SizeProbe", displayName: { en: "Size Probe" }, aliases: [], actorType: "monster",
  worldRef: { worldId: "ANIMAL-LAND-01", version: 1 },
  identity: { species: "두더지", sex: "unknown", ageGroup: "adult", role: "probe", concept: "size fixture", status: "concept" },
  overrides: { anatomy: anatomy as never },
  physicalTraits: ["probe"],
  appearance: { clothing: [], materials: [], palette: [], decorations: [] },
  constraints: { invariants: ["probe"], forbidden: ["none"] },
  equipment: { secondary: [], allowedWeaponFamilies: [] },
  approvedExceptions: [], evidence: [],
});

const resolvedAnatomy = (anatomy: Record<string, unknown>, world = world210()) =>
  resolveActor(actorWith(anatomy), world).resolved.anatomy;

describe("species scale ↔ physical height", () => {
  it("computes the world baseline from the world's own body", () => {
    expect(worldBasePhysicalHeightPx(world210().defaults)).toBe(BASE);
  });

  it("1. scale 1.0 against a 210px baseline is 210px", () => {
    const a = resolvedAnatomy({ speciesScale: 1, sizeAuthority: "species-scale" });
    expect(a.targetPhysicalHeightPx).toBe(210);
    expect(a.speciesScale).toBe(1);
  });

  it("2. scale 1.38 is 290px", () => {
    expect(physicalHeightFromScale(BASE, 1.38)).toBe(290);
    expect(resolvedAnatomy({ speciesScale: 1.38, sizeAuthority: "species-scale" }).targetPhysicalHeightPx).toBe(290);
  });

  it("3. 290px is scale ≈1.381", () => {
    expect(displaySpeciesScale(speciesScaleFromHeight(BASE, 290))).toBe(1.381);
    const a = resolvedAnatomy({ targetPhysicalHeightPx: 290, targetLogicalHeightPx: 97, sizeAuthority: "physical-height" });
    expect(displaySpeciesScale(a.speciesScale)).toBe(1.381);
    // Full precision is what gets stored, not the 3-decimal display value.
    expect(a.speciesScale).toBeCloseTo(290 / 210, 12);
  });

  it("4. 290px at 3×3 is 97 logical px", () => {
    expect(logicalHeightAt(290, 3)).toBe(97);
    expect(resolvedAnatomy({ targetPhysicalHeightPx: 290, targetLogicalHeightPx: 97 }).targetLogicalHeightPx).toBe(97);
  });

  it("5. switching to 2×2 gives 145 logical px and keeps the size and scale", () => {
    const world = world210();
    const actor = actorWith({ targetPhysicalHeightPx: 290, targetLogicalHeightPx: 97, sizeAuthority: "physical-height" });
    actor.overrides.pixelStyle = { densityPreset: "detail-2x2", logicalBlockPx: { widthPx: 2, heightPx: 2 } };
    const a = resolveActor(actor, world).resolved.anatomy;
    expect(a.targetLogicalHeightPx).toBe(145);
    expect(a.targetPhysicalHeightPx).toBe(290);
    expect(displaySpeciesScale(a.speciesScale)).toBe(1.381);
  });

  it("6. round-tripping the scale never walks the height", () => {
    let scale = 1.38;
    let physical = physicalHeightFromScale(BASE, scale);
    for (let i = 0; i < 20; i++) {
      // Re-reading a saved document must reproduce the same pair every time.
      const resolved = resolveSize({ speciesScale: scale, sizeAuthority: "species-scale" }, 3, BASE);
      physical = resolved.targetPhysicalHeightPx;
      scale = resolved.speciesScale;
      expect(physical).toBe(290);
    }
    expect(scale).toBe(1.38);
  });

  it("7. round-tripping the height keeps full scale precision", () => {
    let physical = 290;
    let scale = speciesScaleFromHeight(BASE, physical);
    for (let i = 0; i < 20; i++) {
      const resolved = resolveSize(
        { targetPhysicalHeightPx: physical, speciesScale: scale, sizeAuthority: "physical-height" }, 3, BASE);
      physical = resolved.targetPhysicalHeightPx;
      scale = resolved.speciesScale;
      expect(physical).toBe(290);
    }
    expect(scale).toBeCloseTo(290 / 210, 12);
    // Feeding the *displayed* value back must not move the height either.
    expect(physicalHeightFromScale(BASE, displaySpeciesScale(scale))).toBe(290);
  });
});

describe("legacy documents", () => {
  it("8. height-only: the scale is back-calculated", () => {
    const size = resolveSize({ targetLogicalHeightPx: 97 }, 3, BASE);
    expect(size.sizeAuthority).toBe("physical-height");
    expect(size.authorityInferred).toBe(true);
    expect(size.targetPhysicalHeightPx).toBe(291);
    expect(displaySpeciesScale(size.speciesScale)).toBe(1.386);
    expect(size.conflict).toBeNull();
  });

  it("9. scale-only: the height is computed", () => {
    const size = resolveSize({ speciesScale: 1.38 }, 3, BASE);
    expect(size.sizeAuthority).toBe("species-scale");
    expect(size.targetPhysicalHeightPx).toBe(290);
    expect(size.conflict).toBeNull();
  });

  it("9b. neither value: the world baseline at scale 1", () => {
    const size = resolveSize({}, 3, BASE);
    expect(size.targetPhysicalHeightPx).toBe(BASE);
    expect(size.speciesScale).toBe(1);
  });

  it("10. both present but disagreeing: the height wins and the swap is recorded", () => {
    const size = resolveSize({ targetPhysicalHeightPx: 290, speciesScale: 1.0 }, 3, BASE);
    expect(size.targetPhysicalHeightPx).toBe(290);
    expect(displaySpeciesScale(size.speciesScale)).toBe(1.381);
    expect(size.conflict).toEqual({ documentSpeciesScale: 1.0, resolvedSpeciesScale: 290 / 210 });

    const diagnostics = validateActor(actorWith({ targetPhysicalHeightPx: 290, targetLogicalHeightPx: 97, speciesScale: 1 }), world210());
    const warning = diagnostics.find((d) => d.ruleId === RULE_IDS.sizeConflict);
    expect(warning?.severity).toBe("warning");
    expect(warning?.blocksExport).toBe(false);
    expect(warning?.message).toContain("1.381");
  });

  it("10b. agreeing values raise nothing", () => {
    expect(resolveSize({ targetPhysicalHeightPx: 290, speciesScale: 290 / 210 }, 3, BASE).conflict).toBeNull();
    // Whole-pixel storage means the stored scale can only ever be this close.
    expect(resolveSize({ targetPhysicalHeightPx: 290, speciesScale: 1.381 }, 3, BASE).conflict).toBeNull();
  });
});

describe("11. a changed world baseline follows sizeAuthority", () => {
  const taller = (): WorldTemplateV1 => {
    const world = world210();
    world.defaults.anatomy.targetPhysicalHeightPx = 240;
    world.defaults.anatomy.targetLogicalHeightPx = 80;
    return world;
  };

  it("species-scale keeps the ratio and moves the height", () => {
    const anatomy = { speciesScale: 1.38, targetPhysicalHeightPx: 290, targetLogicalHeightPx: 97, sizeAuthority: "species-scale" };
    expect(resolvedAnatomy(anatomy).targetPhysicalHeightPx).toBe(290);
    const moved = resolvedAnatomy(anatomy, taller());
    expect(moved.speciesScale).toBe(1.38);
    expect(moved.targetPhysicalHeightPx).toBe(331); // round(240 × 1.38)
  });

  it("physical-height keeps the absolute size and moves the ratio", () => {
    const anatomy = { targetPhysicalHeightPx: 290, targetLogicalHeightPx: 97, speciesScale: 290 / 210, sizeAuthority: "physical-height" };
    const moved = resolvedAnatomy(anatomy, taller());
    expect(moved.targetPhysicalHeightPx).toBe(290);
    expect(displaySpeciesScale(moved.speciesScale)).toBe(1.208); // 290 / 240
  });
});

describe("12. species scale is height only", () => {
  it("leaves build, proportion, torso and extremity classes untouched", () => {
    const at = (speciesScale: number) =>
      resolvedAnatomy({
        speciesScale, sizeAuthority: "species-scale",
        build: "broad", proportionTemplateId: "humanoid-sd-2.5-head",
        torsoWidth: "broad", headSize: "l", handSize: "l", footSize: "m",
      });
    const small = at(1.0), large = at(1.9);
    expect(small.targetPhysicalHeightPx).toBe(210);
    expect(large.targetPhysicalHeightPx).toBe(399);
    for (const key of ["build", "proportionTemplateId", "torsoWidth", "headSize", "handSize", "footSize"] as const) {
      expect(large[key]).toBe(small[key]);
    }
  });
});

describe("13. exports state one consistent size", () => {
  it("agrees across resolved, calculated, JSON and Markdown", () => {
    const actor = actorWith({ speciesScale: 1.38, sizeAuthority: "species-scale" });
    const envelope = buildExport(actor, world210());
    const json = JSON.parse(exportJson(envelope));

    expect(json.resolved.anatomy.targetPhysicalHeightPx).toBe(290);
    expect(json.resolved.anatomy.sizeAuthority).toBe("species-scale");
    expect(json.calculated.speciesScaleDisplay).toBe(1.38);
    expect(json.calculated.worldBasePhysicalHeightPx).toBe(210);
    expect(json.calculated.sizeRelation).toBe("210px × 1.38 = 290px");
    expect(json.calculated.targetPhysicalHeightPx).toBe(290);

    const markdown = exportMarkdown(envelope);
    expect(markdown).toContain("| Species scale | 1.38 |");
    expect(markdown).toContain("| Size relation | 210px × 1.38 = 290px | Scale authored |");
    expect(markdown).toContain("- Body size: 210px × 1.38 = 290px (authored as species scale)");
    expect(markdown).toContain("Target physical height already includes species scale.");
  });
});
