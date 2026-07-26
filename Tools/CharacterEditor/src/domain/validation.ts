import type { ActorDocumentV1, ApprovedException, WorldTemplateV1 } from "../schema";
import { resolveActor } from "./resolve";
import { resolveScale } from "./scale";
import type { ActorReference, Diagnostic } from "./types";

export const RULE_IDS = {
  required: "required-field", conflation: "stature-species-scale-conflation",
  torso: "normal-build-wide-torso", proportion: "proportion-mismatch",
  canvas: "large-weapon-canvas", unity: "unity-scale-not-one", weapon: "weapon-family-not-allowed",
  extremity: "extremity-size-delta", largeMotion: "large-motion-canvas-exception", ppu: "ppu-not-200",
  floating: "floating-pivot-mismatch", constraints: "missing-design-constraints", alias: "actor-id-alias-conflict",
  densityBlock: "non-square-density-block", densityResidual: "physical-height-not-block-multiple",
  densityBackCalc: "density-override-without-physical-height",
} as const;

const activeException = (exceptions: ApprovedException[], ruleId: string) =>
  exceptions.some((e) => e.active && e.ruleId === ruleId && e.reason.trim().length >= 10);

function diagnostic(actor: ActorDocumentV1, ruleId: string, severity: Diagnostic["severity"], message: string, overridable: boolean, path?: string): Diagnostic {
  const exceptionApproved = overridable && activeException(actor.approvedExceptions, ruleId);
  return { ruleId, severity, message, overridable, exceptionApproved, blocksExport: severity === "error" && !exceptionApproved, path };
}

const ranks: Record<string, number> = { xs: 0, s: 1, m: 2, l: 3, xl: 4 };

export function validateActor(actor: ActorDocumentV1, world: WorldTemplateV1, references: ActorReference[] = []): Diagnostic[] {
  let resolved;
  try { resolved = resolveActor(actor, world).resolved; }
  catch (error) { return [diagnostic(actor, RULE_IDS.required, "error", (error as Error).message, false, "worldRef")]; }
  const d: Diagnostic[] = [];
  const a = resolved.anatomy, p = resolved.production;
  if (!actor.actorId || !actor.identity.species || !actor.identity.role || !actor.identity.concept)
    d.push(diagnostic(actor, RULE_IDS.required, "error", "One or more required identity fields are missing.", false));
  // Compare physical heights, not logical ones: logical height moves with pixel
  // density, so a density change must not make this heuristic fire or stop firing.
  const scale = resolveScale(a, resolved.pixelStyle);
  const worldScale = resolveScale(world.defaults.anatomy, world.defaults.pixelStyle);
  const heightRatio = worldScale.targetPhysicalHeightPx > 0
    ? scale.targetPhysicalHeightPx / worldScale.targetPhysicalHeightPx : 0;
  if (Math.abs(a.speciesScale - 1) > 0.05 && Math.abs(heightRatio - a.speciesScale) < 0.1)
    d.push(diagnostic(actor, RULE_IDS.conflation, "warning", "Species scale appears derived from actor height; confirm species-wide evidence.", true, "anatomy.speciesScale"));
  if (!scale.squareBlock)
    d.push(diagnostic(actor, RULE_IDS.densityBlock, "error", `Logical block is ${resolved.pixelStyle.logicalBlockPx.widthPx}×${resolved.pixelStyle.logicalBlockPx.heightPx}; production requires a square block.`, false, "pixelStyle.logicalBlockPx"));
  if (scale.roundingResidualPx !== 0)
    d.push(diagnostic(actor, RULE_IDS.densityResidual, "warning", `Target physical height ${scale.targetPhysicalHeightPx}px is not a multiple of the ${scale.blockPx}px block; production will land on ${scale.effectivePhysicalHeightPx}px (${scale.targetLogicalHeightPx} logical px).`, true, "anatomy.targetPhysicalHeightPx"));
  // `resolved` always carries a physical height, so ask the source documents
  // whether anyone actually authored one.
  const authoredPhysical = actor.overrides.anatomy?.targetPhysicalHeightPx !== undefined
    || world.defaults.anatomy.targetPhysicalHeightPx !== undefined;
  const densityOverridden = actor.overrides.pixelStyle?.logicalBlockPx !== undefined || actor.overrides.pixelStyle?.densityPreset !== undefined;
  if (densityOverridden && !authoredPhysical)
    d.push(diagnostic(actor, RULE_IDS.densityBackCalc, "warning", `Pixel density is overridden but no target physical height is recorded; height was back-calculated as ${scale.targetPhysicalHeightPx}px from the logical height at ${scale.blockPx}px per logical pixel. Set the physical height to pin the body size.`, true, "anatomy.targetPhysicalHeightPx"));
  if (a.build === "normal" && ["broad", "very-broad"].includes(a.torsoWidth))
    d.push(diagnostic(actor, RULE_IDS.torso, "warning", "Normal build conflicts with a broad torso width.", true, "anatomy.torsoWidth"));
  if (actor.equipment.weapon?.estimatedOccupancyPercent && actor.equipment.weapon.estimatedOccupancyPercent > 60)
    d.push(diagnostic(actor, RULE_IDS.canvas, "warning", `Estimated weapon occupancy is ${actor.equipment.weapon.estimatedOccupancyPercent}%.`, true, "equipment.weapon"));
  if (p.unityVisualScale !== 1)
    d.push(diagnostic(actor, RULE_IDS.unity, "error", `Unity visual scale is ${p.unityVisualScale}; project policy is 1.0.`, true, "production.unityVisualScale"));
  const family = actor.equipment.weapon?.family;
  if (family && !actor.equipment.allowedWeaponFamilies.includes(family))
    d.push(diagnostic(actor, RULE_IDS.weapon, "error", `${family} is not allowed for ${actor.actorId}.`, true, "equipment.weapon.family"));
  if (p.pixelsPerUnit !== 200)
    d.push(diagnostic(actor, RULE_IDS.ppu, "error", `PPU is ${p.pixelsPerUnit}; project policy is 200.`, false, "production.pixelsPerUnit"));
  if (a.isFloatingActor && p.pivotRule === "forward-foot-contact")
    d.push(diagnostic(actor, RULE_IDS.floating, "error", "Floating actors must use ground-projection or a custom actor origin.", false, "production.pivotRule"));
  const large = p.largeMotionCanvas;
  if (large.policy === "explicit" && (large.widthPx !== p.baseCanvas.widthPx || large.heightPx !== p.baseCanvas.heightPx))
    d.push(diagnostic(actor, RULE_IDS.largeMotion, "warning", "Large-motion canvas differs from the base 512 policy.", true, "production.largeMotionCanvas"));
  if (!actor.constraints.invariants.length || !actor.constraints.forbidden.length)
    d.push(diagnostic(actor, RULE_IDS.constraints, "warning", "Invariant or forbidden design constraints are empty.", false, "constraints"));
  if (actor.resourceFolderPath && !actor.resourceFolderPath.split("/").at(-1)?.toLowerCase().includes(actor.actorId.toLowerCase()))
    d.push(diagnostic(actor, RULE_IDS.alias, "info", `Actor ID differs from resource folder ${actor.resourceFolderPath}.`, false, "actorId"));

  for (const ref of references) {
    if (ref.actor.actorId === actor.actorId || ref.actor.worldRef.worldId !== actor.worldRef.worldId) continue;
    const rr = resolveActor(ref.actor, ref.world).resolved;
    if (ref.actor.identity.species === actor.identity.species && rr.anatomy.proportionTemplateId !== a.proportionTemplateId)
      d.push(diagnostic(actor, RULE_IDS.proportion, "warning", `${ref.actor.actorId} uses proportion ${rr.anatomy.proportionTemplateId}.`, true, "anatomy.proportionTemplateId"));
    const equivalent = ref.actor.identity.species === actor.identity.species && rr.anatomy.build === a.build && rr.anatomy.proportionTemplateId === a.proportionTemplateId;
    if (equivalent && ["headSize", "handSize", "footSize"].some((k) => Math.abs(ranks[a[k as keyof typeof a] as string] - ranks[rr.anatomy[k as keyof typeof rr.anatomy] as string]) >= 2))
      d.push(diagnostic(actor, RULE_IDS.extremity, "warning", `Extremity sizes differ substantially from ${ref.actor.actorId}.`, true, "anatomy"));
  }
  return d;
}

export const canExport = (diagnostics: Diagnostic[]) => !diagnostics.some((d) => d.blocksExport);

