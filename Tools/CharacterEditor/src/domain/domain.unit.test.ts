import { describe, expect, it } from "vitest";
import { elfGuardian, fantasia, venomCultist } from "../data";
import { parseActor, parseWorld } from "./parse";
import { removeOverride, resolveActor } from "./resolve";
import { canExport, RULE_IDS, validateActor } from "./validation";
import { compareActors } from "./comparison";

describe("Schema v1 parsing", () => {
  it("parses bundled documents and rejects unknown versions", () => {
    expect(parseWorld(fantasia).worldId).toBe("HUMAN-FANTASY-01");
    expect(parseActor(elfGuardian).actorId).toBe("ElfGuardian");
    expect(() => parseActor({ ...elfGuardian, schemaVersion: "2.0.0" })).toThrow();
  });
});

describe("resolution and origins", () => {
  it("keeps height separate from Unity scale and links it to species scale", () => {
    const result = resolveActor(elfGuardian, fantasia);
    expect(result.resolved.anatomy.targetLogicalHeightPx).toBe(91);
    // 273px against Fantasia's 210px baseline. The document says 1.0, which is
    // the old independent-scale reading; the height wins and the scale follows.
    expect(result.resolved.anatomy.speciesScale).toBeCloseTo(1.3, 10);
    expect(result.resolved.anatomy.sizeAuthority).toBe("physical-height");
    expect(result.resolved.production.unityVisualScale).toBe(1);
    expect(result.fieldOrigins["anatomy.targetLogicalHeightPx"].source).toBe("actor");
    expect(result.fieldOrigins["production.pixelsPerUnit"].source).toBe("world");
  });
  it("deletes an override to reset inheritance without mutating input", () => {
    const reset = removeOverride(elfGuardian, "anatomy.targetLogicalHeightPx");
    expect(resolveActor(reset, fantasia).resolved.anatomy.targetLogicalHeightPx).toBe(70);
    expect(elfGuardian.overrides.anatomy?.targetLogicalHeightPx).toBe(91);
  });
  it("rejects the wrong pinned version", () => expect(() => resolveActor({ ...elfGuardian, worldRef: { ...elfGuardian.worldRef, version: 2 } }, fantasia)).toThrow());
});

describe("stable validations", () => {
  const ids = (actor = elfGuardian, refs = [venomCultist]) => validateActor(actor, fantasia, refs.map((ref) => ({ actor: ref, world: fantasia }))).map((d) => d.ruleId);
  it("recognizes sample exceptions and reports the legacy size disagreement", () => {
    const diagnostics = validateActor(elfGuardian, fantasia);
    expect(diagnostics.find((d) => d.ruleId === RULE_IDS.largeMotion)?.exceptionApproved).toBe(true);
    // 273px vs. a recorded scale of 1.0 — a migration to report, not to block.
    expect(diagnostics.find((d) => d.ruleId === RULE_IDS.sizeConflict)?.severity).toBe("warning");
    expect(canExport(diagnostics)).toBe(true);
  });
  it("stops warning once the recorded scale matches the height", () =>
    expect(ids({ ...elfGuardian, overrides: { ...elfGuardian.overrides, anatomy: { ...elfGuardian.overrides.anatomy, speciesScale: 1.3 } } }))
      .not.toContain(RULE_IDS.sizeConflict));
  it("detects normal/wide torso", () => expect(ids({ ...venomCultist, overrides: { ...venomCultist.overrides, anatomy: { ...venomCultist.overrides.anatomy, torsoWidth: "very-broad" } } }, [])).toContain(RULE_IDS.torso));
  it("blocks a non-1 Unity scale unless excepted", () => {
    const bad = { ...venomCultist, overrides: { ...venomCultist.overrides, production: { ...venomCultist.overrides.production, unityVisualScale: 0.9 } } };
    expect(canExport(validateActor(bad, fantasia))).toBe(false);
    const approved = { ...bad, approvedExceptions: [{ ruleId: RULE_IDS.unity, reason: "Approved runtime presentation exception", active: true }] };
    expect(canExport(validateActor(approved, fantasia))).toBe(true);
  });
  it("blocks forbidden weapons and immutable PPU", () => {
    const staff = { ...elfGuardian, equipment: { ...elfGuardian.equipment, weapon: { ...elfGuardian.equipment.weapon!, family: "staff" } } };
    expect(ids(staff, [])).toContain(RULE_IDS.weapon);
    const ppu = { ...venomCultist, overrides: { ...venomCultist.overrides, production: { ...venomCultist.overrides.production, pixelsPerUnit: 100 } } };
    const result = validateActor(ppu, fantasia).find((d) => d.ruleId === RULE_IDS.ppu)!;
    expect(result.blocksExport).toBe(true); expect(result.overridable).toBe(false);
  });
  it("detects weapon occupancy, constraints and floating pivot mismatch", () => {
    expect(ids(elfGuardian, [])).toContain(RULE_IDS.canvas);
    const bad = { ...venomCultist, constraints: { invariants: [], forbidden: [] }, overrides: { ...venomCultist.overrides, anatomy: { ...venomCultist.overrides.anatomy, isFloatingActor: true }, production: { ...venomCultist.overrides.production, pivotRule: "forward-foot-contact" as const } } };
    expect(ids(bad, [])).toEqual(expect.arrayContaining([RULE_IDS.constraints, RULE_IDS.floating]));
  });
  it("detects same-species proportion and extremity deltas", () => {
    const reference = { ...venomCultist, actorId: "CultistRef", overrides: { ...venomCultist.overrides, anatomy: { ...venomCultist.overrides.anatomy, proportionTemplateId: "other-template", headSize: "xl" as const } } };
    expect(ids(venomCultist, [reference])).toContain(RULE_IDS.proportion);
    const equivalent = { ...reference, overrides: { ...reference.overrides, anatomy: { ...reference.overrides.anatomy, proportionTemplateId: "humanoid-sd-2.5-head" } } };
    expect(ids(venomCultist, [equivalent])).toContain(RULE_IDS.extremity);
  });
});

describe("comparison", () => {
  it("reports string matches and mismatches explicitly", () => {
    const comparison = compareActors(elfGuardian, venomCultist, fantasia);
    expect(comparison.metrics.find((m) => m.key === "stature")).toMatchObject({ draft: "tall", reference: "average", matches: false });
    expect(comparison.metrics.find((m) => m.key === "torsoWidth")).toMatchObject({ draft: "normal", reference: "normal", matches: true });
    expect(comparison.metrics.find((m) => m.key === "largeMotionCanvas")).toMatchObject({ draft: "1024×1024", reference: "same-as-base", matches: false });
    expect(comparison.metrics.find((m) => m.key === "proportion")).toMatchObject({ draft: "humanoid-sd-2.5-head", reference: "humanoid-sd-2.5-head", matches: true });
  });
  it("preserves numeric delta behavior", () => {
    const comparison = compareActors(elfGuardian, venomCultist, fantasia);
    expect(comparison.metrics.find((m) => m.key === "height")?.absoluteDelta).toBe(21);
    expect(comparison.metrics.find((m) => m.key === "height")?.percentDelta).toBe(30);
    // ElfGuardian is 1.3× Fantasia's baseline, VenomCultist is exactly 1.0×.
    expect(comparison.metrics.find((m) => m.key === "speciesScale")?.absoluteDelta).toBeCloseTo(0.3, 10);
    expect(comparison.metrics.map((m) => m.key)).toEqual(expect.arrayContaining(["stature", "build", "headSize", "weaponOccupancy", "baseCanvas", "largeMotionCanvas"]));
  });
});
