import { describe, expect, it } from "vitest";
import { animalLand } from "../data";
import type { ActorDocumentV1, WorldTemplateV1 } from "../schema";
import { buildExport, exportJson, exportMarkdown } from "../export";
import { resolveActor } from "./resolve";
import { validateActor } from "./validation";
import { PROJECT_DEFAULT_VIEW, masterImageDirectionPrompt, resolveView } from "./view";

/**
 * The MoleMiner regression: the world's screen-right direction resolved
 * correctly all along, but nothing carried it into the character sheet, so the
 * design master was generated facing screen-left and every animation followed.
 */

const moleMiner: ActorDocumentV1 = {
  schemaVersion: "1.0.0", documentKind: "actor", revision: 1, updatedAt: "2026-07-25T00:00:00+09:00",
  actorId: "MoleMiner", displayName: { en: "MoleMiner", ko: "광부두더지" }, aliases: ["MoleMiner"],
  actorType: "monster", worldRef: { worldId: "ANIMAL-LAND-01", version: 1 },
  identity: { species: "두더지", sex: "unknown", ageGroup: "adult", role: "물리공격", concept: "날카로운 손톱과 안경, 커다란 백팩.", status: "concept" },
  overrides: { anatomy: { targetLogicalHeightPx: 83, build: "broad", speciesScale: 1.32, headSize: "l", handSize: "l", torsoWidth: "broad" } },
  physicalTraits: ["뾰족한 주둥이", "동그란 안경", "백팩"],
  appearance: { hair: "뾰족한 주둥이", eyes: "동그란 안경", skin: "짙은 갈색", clothing: ["광부용 조끼"], materials: ["가죽"], palette: [], decorations: ["백팩", "곡괭이"] },
  constraints: { invariants: ["뾰족한 손톱", "동그란 안경", "백팩"], forbidden: ["없음"] },
  equipment: { secondary: [], allowedWeaponFamilies: ["주먹"] },
  approvedExceptions: [], evidence: [],
};

const worldFacing = (facing: "screen-left" | "screen-right"): WorldTemplateV1 => {
  const world = structuredClone(animalLand);
  world.defaults.view = { ...world.defaults.view, facing };
  return world;
};

/** A world saved before the view fields existed at all. */
const legacyWorld = (): WorldTemplateV1 => {
  const world = structuredClone(animalLand);
  world.defaults.view = {};
  return world;
};

describe("view direction resolution", () => {
  it("inherits the world's screen-right direction with a world origin", () => {
    const { resolved, fieldOrigins } = resolveActor(moleMiner, animalLand);
    expect(resolved.view).toEqual({ projection: "three-quarter", facing: "screen-right", lightDirection: "upper-left" });
    expect(fieldOrigins["view.facing"]).toEqual({ source: "world", documentId: "ANIMAL-LAND-01", version: 1 });
    expect(fieldOrigins["view.lightDirection"].source).toBe("world");
  });

  it("inherits a world that is set to screen-left", () => {
    const { resolved, fieldOrigins } = resolveActor(moleMiner, worldFacing("screen-left"));
    expect(resolved.view.facing).toBe("screen-left");
    expect(fieldOrigins["view.facing"].source).toBe("world");
  });

  it("falls back to the project default and says so, instead of exporting a gap", () => {
    const { resolved, fieldOrigins } = resolveActor(moleMiner, legacyWorld());
    expect(resolved.view).toEqual(PROJECT_DEFAULT_VIEW);
    expect(fieldOrigins["view.facing"]).toEqual({ source: "default", documentId: "keybuddy-project-default", version: 0 });
    expect(resolveView({}).fellBackToDefault).toEqual(["projection", "facing", "lightDirection"]);
  });

  it("prefers an actor override over the world value", () => {
    const actor = structuredClone(moleMiner);
    actor.overrides.view = { facing: "screen-left" };
    const { resolved, fieldOrigins } = resolveActor(actor, animalLand);
    expect(resolved.view.facing).toBe("screen-left");
    expect(fieldOrigins["view.facing"].source).toBe("actor");
    expect(fieldOrigins["view.projection"].source).toBe("world");
  });

  it("writes an unambiguous master direction sentence", () => {
    expect(masterImageDirectionPrompt(PROJECT_DEFAULT_VIEW)).toBe(
      "front-biased three-quarter full-body view, facing screen-right, with lighting from the upper-left.");
  });
});

describe("view direction validation", () => {
  it("does not block a normal inherited actor and states the inherited direction", () => {
    const d = validateActor(moleMiner, animalLand);
    expect(d.some((x) => x.blocksExport)).toBe(false);
    const inherited = d.find((x) => x.ruleId === "view-direction-inherited");
    expect(inherited?.message).toContain("facing screen-right");
    expect(inherited?.message).toContain("World Template");
  });

  it("blocks export when a document carries a facing outside the allowed values", () => {
    const world = structuredClone(animalLand);
    (world.defaults.view as Record<string, unknown>).facing = "left";
    const blocking = validateActor(moleMiner, world).filter((x) => x.blocksExport);
    expect(blocking.map((x) => x.ruleId)).toContain("view-direction-invalid-value");
    expect(blocking[0].message).toContain("screen-left, screen-right");
  });

  it("warns, without blocking, when the world records no direction at all", () => {
    const d = validateActor(moleMiner, legacyWorld());
    const fallback = d.find((x) => x.ruleId === "view-direction-fallback-used");
    expect(fallback?.severity).toBe("warning");
    expect(fallback?.message).toContain("facing screen-right");
    expect(d.some((x) => x.blocksExport)).toBe(false);
  });

  it("warns when the resolved direction opposes the master production rule", () => {
    const d = validateActor(moleMiner, worldFacing("screen-left"));
    expect(d.find((x) => x.ruleId === "view-facing-opposes-master-rule")?.message)
      .toContain("resolves to facing screen-left");
    expect(d.find((x) => x.ruleId === "world-view-differs-from-master-rule")?.severity).toBe("warning");
    expect(d.some((x) => x.blocksExport)).toBe(false);
  });
});

describe("view direction in exports", () => {
  it("records the resolved direction and its origin in the JSON", () => {
    const json = JSON.parse(exportJson(buildExport(moleMiner, animalLand)));
    expect(json.resolved.view).toEqual({ projection: "three-quarter", facing: "screen-right", lightDirection: "upper-left" });
    expect(json.fieldOrigins["view.facing"].source).toBe("world");
    expect(json.calculated.canonicalView).toEqual({
      projection: "three-quarter", facing: "screen-right", lightDirection: "upper-left",
      origin: "world", originLabel: "World Template",
      masterImageDirection: "front-biased three-quarter full-body view, facing screen-right, with lighting from the upper-left.",
    });
  });

  it("prints a View & Direction section Codex can follow", () => {
    const markdown = exportMarkdown(buildExport(moleMiner, animalLand));
    expect(markdown).toContain(`## View & Direction

- Projection: three-quarter
- Facing: screen-right
- Light direction: upper-left
- Origin: World Template

Master image direction:
front-biased three-quarter full-body view, facing screen-right, with lighting from the upper-left.`);
  });

  it("labels an actor override and a project fallback distinctly", () => {
    const actor = structuredClone(moleMiner);
    actor.overrides.view = { facing: "screen-left" };
    expect(exportMarkdown(buildExport(actor, animalLand)))
      .toContain("- Facing: screen-left\n- Light direction: upper-left\n- Origin: Actor Override");
    expect(exportMarkdown(buildExport(moleMiner, legacyWorld()))).toContain("- Origin: Project Default");
  });
});
