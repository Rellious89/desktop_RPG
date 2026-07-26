import type { ActorDocumentV1, InheritableSpec, WorldTemplateV1 } from "../schema";
import type { FieldOrigins, ResolvedActor } from "./types";
import { applyResolvedScale } from "./scale";
import { applyResolvedSize, worldBasePhysicalHeightPx } from "./size";
import { PROJECT_DEFAULT_DOCUMENT_ID, VIEW_KEYS, applyResolvedView, isAllowedViewValue } from "./view";

const isObject = (v: unknown): v is Record<string, unknown> => !!v && typeof v === "object" && !Array.isArray(v);

function merge<T>(base: T, overrides: unknown): T {
  if (!isObject(base) || !isObject(overrides)) return (overrides === undefined ? base : overrides) as T;
  const result: Record<string, unknown> = { ...base };
  for (const [key, value] of Object.entries(overrides)) {
    if (value === undefined) continue;
    result[key] = isObject(value) && isObject(result[key]) ? merge(result[key], value) : value;
  }
  return result as T;
}

function leafPaths(value: unknown, prefix = ""): string[] {
  if (!isObject(value)) return prefix ? [prefix] : [];
  return Object.entries(value).flatMap(([key, child]) => leafPaths(child, prefix ? `${prefix}.${key}` : key));
}

export function resolveActor(actor: ActorDocumentV1, world: WorldTemplateV1): ResolvedActor {
  if (actor.worldRef.worldId !== world.worldId || actor.worldRef.version !== world.revision)
    throw new Error(`Actor pins ${actor.worldRef.worldId} v${actor.worldRef.version}, not ${world.worldId} v${world.revision}`);
  // Size first: species scale and physical height are two views of one number,
  // and which one wins can change the physical height. Scale resolution then
  // derives the logical height from whatever physical height survived.
  const merged = merge<InheritableSpec>(world.defaults, actor.overrides);
  const resolved = applyResolvedView(
    applyResolvedScale(applyResolvedSize(merged, worldBasePhysicalHeightPx(world.defaults))),
  );
  const origins: FieldOrigins = {};
  for (const path of leafPaths(world.defaults)) origins[path] = { source: "world", documentId: world.worldId, version: world.revision };
  for (const path of leafPaths(actor.overrides)) origins[path] = { source: "actor", documentId: actor.actorId, version: actor.revision };
  attachDerivedScaleOrigins(origins);
  attachDerivedSizeOrigins(origins, resolved);
  attachViewOrigins(origins, actor, world);
  return { ...actor, resolved, fieldOrigins: origins };
}

/** Whichever of the two size fields was derived points at the document that
 * supplied the one it was derived from, so the UI does not badge a computed
 * value as if someone had typed it. */
function attachDerivedSizeOrigins(origins: FieldOrigins, resolved: InheritableSpec): void {
  const authority = resolved.anatomy.sizeAuthority;
  const from = authority === "species-scale" ? "anatomy.speciesScale" : "anatomy.targetPhysicalHeightPx";
  const derived = authority === "species-scale" ? "anatomy.targetPhysicalHeightPx" : "anatomy.speciesScale";
  if (origins[from]) {
    origins[derived] = origins[from];
    origins["anatomy.targetLogicalHeightPx"] = origins[from];
  }
  if (!origins["anatomy.sizeAuthority"] && origins[from]) origins["anatomy.sizeAuthority"] = origins[from];
}

/** applyResolvedView guarantees a value for every view field, so every field
 * also gets an origin — including `default` for the ones the project fallback
 * supplied. Computed from the source documents rather than from leafPaths so a
 * value the schema rejects is attributed to the fallback that replaced it, not
 * to the document that wrote it. */
function attachViewOrigins(origins: FieldOrigins, actor: ActorDocumentV1, world: WorldTemplateV1): void {
  for (const key of VIEW_KEYS) {
    const path = `view.${key}`;
    if (isAllowedViewValue(key, actor.overrides.view?.[key]))
      origins[path] = { source: "actor", documentId: actor.actorId, version: actor.revision };
    else if (isAllowedViewValue(key, world.defaults.view?.[key]))
      origins[path] = { source: "world", documentId: world.worldId, version: world.revision };
    else origins[path] = { source: "default", documentId: PROJECT_DEFAULT_DOCUMENT_ID, version: 0 };
  }
}

/** applyResolvedScale fills in fields no document may have authored, and makes
 * logical height a function of physical height. Point each derived field at
 * whichever document actually supplied the value it was computed from. */
function attachDerivedScaleOrigins(origins: FieldOrigins): void {
  const physical = origins["anatomy.targetPhysicalHeightPx"];
  const logical = origins["anatomy.targetLogicalHeightPx"];
  if (physical) origins["anatomy.targetLogicalHeightPx"] = physical;
  else if (logical) origins["anatomy.targetPhysicalHeightPx"] = logical;

  if (!origins["pixelStyle.densityPreset"]) {
    const block = origins["pixelStyle.logicalBlockPx.widthPx"];
    if (block) origins["pixelStyle.densityPreset"] = block;
  }
}

export function removeOverride(actor: ActorDocumentV1, path: string): ActorDocumentV1 {
  const copy = structuredClone(actor);
  const keys = path.split(".");
  let node: Record<string, unknown> = copy.overrides as Record<string, unknown>;
  for (let i = 0; i < keys.length - 1; i++) {
    const next = node[keys[i]];
    if (!isObject(next)) return copy;
    node = next;
  }
  delete node[keys.at(-1)!];
  return copy;
}

