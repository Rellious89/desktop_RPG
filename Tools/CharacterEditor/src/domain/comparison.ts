import type { ActorDocumentV1, WorldTemplateV1 } from "../schema";
import { resolveActor } from "./resolve";
import { validateActor } from "./validation";
import type { ActorComparison, ComparisonMetric } from "./types";

const metric = (key: string, label: string, draft: string | number, reference: string | number): ComparisonMetric => {
  const matches = draft === reference;
  if (typeof draft !== "number" || typeof reference !== "number") return { key, label, draft, reference, matches };
  return { key, label, draft, reference, matches, absoluteDelta: draft - reference, percentDelta: reference ? ((draft - reference) / reference) * 100 : undefined };
};

export function compareActors(draft: ActorDocumentV1, reference: ActorDocumentV1, world: WorldTemplateV1): ActorComparison {
  if (draft.worldRef.worldId !== reference.worldRef.worldId) throw new Error("Comparison requires actors from the same world");
  const d = resolveActor(draft, world).resolved, r = resolveActor(reference, world).resolved;
  const dc = d.production.largeMotionCanvas, rc = r.production.largeMotionCanvas;
  const metrics = [
    metric("stature", "Stature", d.anatomy.stature, r.anatomy.stature),
    metric("height", "Logical height", d.anatomy.targetLogicalHeightPx, r.anatomy.targetLogicalHeightPx),
    metric("build", "Build", d.anatomy.build, r.anatomy.build),
    metric("proportion", "Proportion", d.anatomy.proportionTemplateId, r.anatomy.proportionTemplateId),
    ...(["headSize", "handSize", "footSize", "torsoWidth"] as const).map((k) => metric(k, k, d.anatomy[k], r.anatomy[k])),
    metric("speciesScale", "Species scale", d.anatomy.speciesScale, r.anatomy.speciesScale),
    metric("weaponOccupancy", "Weapon occupancy", draft.equipment.weapon?.estimatedOccupancyPercent ?? 0, reference.equipment.weapon?.estimatedOccupancyPercent ?? 0),
    metric("baseCanvas", "Base canvas", `${d.production.baseCanvas.widthPx}×${d.production.baseCanvas.heightPx}`, `${r.production.baseCanvas.widthPx}×${r.production.baseCanvas.heightPx}`),
    metric("largeMotionCanvas", "Large-motion canvas", dc.policy === "explicit" ? `${dc.widthPx}×${dc.heightPx}` : "same-as-base", rc.policy === "explicit" ? `${rc.widthPx}×${rc.heightPx}` : "same-as-base"),
  ];
  return { referenceActorId: reference.actorId, metrics, diagnostics: validateActor(draft, world, [{ actor: reference, world }]) };
}
