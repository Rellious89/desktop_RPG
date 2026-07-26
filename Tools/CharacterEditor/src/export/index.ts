import type { ActorDocumentV1, InheritableSpec, WorldTemplateV1 } from "../schema";
import { compareActors } from "../domain/comparison";
import { resolveActor } from "../domain/resolve";
import { resolveScale } from "../domain/scale";
import { validateActor } from "../domain/validation";
import { VIEW_ORIGIN_LABELS, masterImageDirectionPrompt, resolveView } from "../domain/view";
import type { ActorExportEnvelope, FieldOrigins } from "../domain/types";
import { stableJson } from "./stable";

export function serializeActor(actor: ActorDocumentV1): string { return stableJson(actor); }

export function buildExport(actor: ActorDocumentV1, world: WorldTemplateV1, references: ActorDocumentV1[] = []): ActorExportEnvelope {
  const { resolved, fieldOrigins } = resolveActor(actor, world);
  const diagnostics = validateActor(actor, world, references.map((ref) => ({ actor: ref, world })));
  const weaponRatio = actor.equipment.weapon?.lengthToBodyRatio;
  const scale = resolveScale(resolved.anatomy, resolved.pixelStyle);
  const worldScale = resolveScale(world.defaults.anatomy, world.defaults.pixelStyle);
  return {
    schemaVersion: "1.0.0", documentKind: "actor-export", authored: actor, resolved, fieldOrigins,
    calculated: {
      // Compared in physical pixels so the figure does not move when pixel
      // density changes.
      heightFromWorldBaselinePercent: ((scale.targetPhysicalHeightPx / worldScale.targetPhysicalHeightPx) - 1) * 100,
      targetPhysicalHeightPx: scale.targetPhysicalHeightPx,
      targetLogicalHeightPx: scale.targetLogicalHeightPx,
      logicalPixelBlockPx: scale.blockPx,
      densityPreset: scale.densityPreset,
      effectivePhysicalHeightPx: scale.effectivePhysicalHeightPx,
      physicalHeightResidualPx: scale.roundingResidualPx,
      weaponLengthLogicalPx: weaponRatio ? scale.targetLogicalHeightPx * weaponRatio : null,
      weaponLengthPhysicalPx: weaponRatio ? scale.targetPhysicalHeightPx * weaponRatio : null,
      weaponOccupancyPercent: actor.equipment.weapon?.estimatedOccupancyPercent ?? null,
      // The direction the design master must be drawn in. Duplicated out of
      // `resolved.view` + `fieldOrigins` into one flat block so downstream
      // tools (PerfectPixel) read a single canonical value with its origin
      // rather than re-deriving the inheritance chain.
      canonicalView: canonicalView(resolved.view, fieldOrigins),
    },
    interpretations: [
      "Species scale is independent of stature and logical height.",
      "Unity visual scale is a runtime display transform and is normally 1.0.",
      "Canvas pixels, logical silhouette pixels, and logical pixel block size are distinct measurements.",
      "Target physical height is the authoritative body size; logical height is derived as physical height divided by the pixel density block.",
      "Changing pixel density changes detail, not how large the character is.",
    ], diagnostics,
    comparison: references[0] ? compareActors(actor, references[0], world) : undefined,
  };
}

export type CanonicalView = {
  projection: string; facing: string; lightDirection: string;
  /** Which document supplied the values: the strongest origin among the three
   * (actor override beats world, world beats project fallback). */
  origin: "world" | "actor" | "default";
  originLabel: string;
  masterImageDirection: string;
};

function canonicalView(view: InheritableSpec["view"], fieldOrigins: FieldOrigins): CanonicalView {
  const resolved = resolveView(view).view;
  const sources = (["projection", "facing", "lightDirection"] as const).map((k) => fieldOrigins[`view.${k}`]?.source ?? "default");
  const origin = sources.includes("actor") ? "actor" : sources.includes("world") ? "world" : "default";
  return { ...resolved, origin, originLabel: VIEW_ORIGIN_LABELS[origin], masterImageDirection: masterImageDirectionPrompt(resolved) };
}

export const exportJson = (envelope: ActorExportEnvelope): string => stableJson(envelope);

export function exportMarkdown(e: ActorExportEnvelope): string {
  const a = e.authored, r = e.resolved, w = a.equipment.weapon;
  const origin = (path: string) => e.fieldOrigins[path]?.source === "actor" ? "Override" : "World";
  const view = e.calculated.canonicalView as CanonicalView;
  const rows = e.diagnostics.map((d) => `- **${d.severity.toUpperCase()}** \`${d.ruleId}\`: ${d.message}${d.exceptionApproved ? " — Approved exception" : ""}`).join("\n") || "- None";
  const exceptions = a.approvedExceptions.filter((x) => x.active).map((x) => `- \`${x.ruleId}\`: ${x.reason}`).join("\n") || "- None";
  const comparisons = e.comparison?.metrics.map((m) => `| ${m.label} | ${m.draft} | ${m.reference} | ${(m.matches ?? m.draft === m.reference) ? "Match" : "Mismatch"} | ${m.absoluteDelta === undefined ? "—" : `${m.absoluteDelta >= 0 ? "+" : ""}${m.absoluteDelta}${m.percentDelta === undefined ? "" : ` (${m.percentDelta.toFixed(1)}%)`}`} |`).join("\n");
  return `# ${a.actorId} — Character Sheet

- Display name: ${a.displayName.ko ? `${a.displayName.ko} / ` : ""}${a.displayName.en}
- Type: ${a.actorType} · World: \`${a.worldRef.worldId}\` v${a.worldRef.version}
- Aliases: ${a.aliases.length ? a.aliases.join(", ") : "None"}
- Species: ${a.identity.species} · Role: ${a.identity.role} · Status: ${a.identity.status}
- Concept: ${a.identity.concept}

## View & Direction

- Projection: ${view.projection}
- Facing: ${view.facing}
- Light direction: ${view.lightDirection}
- Origin: ${view.originLabel}

Master image direction:
${view.masterImageDirection}

## Body & Proportions

| Field | Value | Origin |
|---|---:|---|
| Stature | ${r.anatomy.stature} | ${origin("anatomy.stature")} |
| Target physical height | ${e.calculated.targetPhysicalHeightPx}px | ${origin("anatomy.targetPhysicalHeightPx")} |
| Pixel density | ${e.calculated.densityPreset} (${e.calculated.logicalPixelBlockPx}×${e.calculated.logicalPixelBlockPx}) | ${origin("pixelStyle.densityPreset")} |
| Target logical height | ${r.anatomy.targetLogicalHeightPx}px | Derived |
| Build | ${r.anatomy.build} | ${origin("anatomy.build")} |
| Proportion | ${r.anatomy.proportionTemplateId} | ${origin("anatomy.proportionTemplateId")} |
| Species scale | ${r.anatomy.speciesScale} | ${origin("anatomy.speciesScale")} |
| Head / hand / foot / torso | ${r.anatomy.headSize} / ${r.anatomy.handSize} / ${r.anatomy.footSize} / ${r.anatomy.torsoWidth} | mixed |

## Look

- Physical traits: ${a.physicalTraits.join("; ") || "None"}
- Hair / eyes / skin: ${a.appearance.hair ?? "N/A"} / ${a.appearance.eyes ?? "N/A"} / ${a.appearance.skin ?? "N/A"}
- Clothing: ${a.appearance.clothing.join("; ") || "None"}
- Materials: ${a.appearance.materials.join("; ") || "None"}
- Decorations: ${a.appearance.decorations.join("; ") || "None"}
- Invariants: ${a.constraints.invariants.join("; ") || "None"}
- Forbidden: ${a.constraints.forbidden.join("; ") || "None"}

## Weapon & Equipment

- Weapon: ${w ? `${w.family}, ${w.sizeClass}, count ${w.count}` : "None"}
- Hands: ${w ? `${w.mainHand} / ${w.offHand}` : "N/A"}
- Structure: ${w?.structure ?? "N/A"}
- Allowed families: ${a.equipment.allowedWeaponFamilies.join(", ") || "None"}
- Secondary: ${a.equipment.secondary.join(", ") || "None"}

## Production & Canvas

- Base canvas: ${r.production.baseCanvas.widthPx}×${r.production.baseCanvas.heightPx}
- Large-motion canvas: ${r.production.largeMotionCanvas.policy === "explicit" ? `${r.production.largeMotionCanvas.widthPx}×${r.production.largeMotionCanvas.heightPx}` : "same as base"}
- Logical block: ${r.pixelStyle.logicalBlockPx.widthPx}×${r.pixelStyle.logicalBlockPx.heightPx} (${e.calculated.densityPreset})
- Body height: ${e.calculated.targetPhysicalHeightPx}px physical → ${e.calculated.targetLogicalHeightPx} logical px${e.calculated.physicalHeightResidualPx ? ` (lands on ${e.calculated.effectivePhysicalHeightPx}px)` : ""}
- Pivot: ${r.production.pivotRule}${r.production.pivot ? ` (${r.production.pivot.xNormalized}, ${r.production.pivot.yNormalized}; ${r.production.pivot.source})` : ""}
- PPU: ${r.production.pixelsPerUnit}
- Unity visual scale: ${r.production.unityVisualScale}
- Layers: ${r.production.layers.join(" → ")}

## Validation Summary

${rows}

## Approved Exceptions

${exceptions}
${e.comparison ? `
## Comparison Snapshot (vs ${e.comparison.referenceActorId})

| Metric | Actor | Reference | Result | Delta |
|---|---:|---:|:---:|---:|
${comparisons}
` : ""}
## Calculated Values and Interpretations

- Height relative to world baseline: ${Number(e.calculated.heightFromWorldBaselinePercent).toFixed(1)}%
- Weapon logical length estimate: ${e.calculated.weaponLengthLogicalPx ?? "N/A"}
${e.interpretations.map((x) => `- ${x}`).join("\n")}

## Schema

- schemaVersion: \`${e.schemaVersion}\`
- worldTemplateRef: \`${a.worldRef.worldId}\` v${a.worldRef.version}
`;
}

export { stableJson } from "./stable";
