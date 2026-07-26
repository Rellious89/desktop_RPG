import type { ActorDocumentV1, InheritableSpec, WorldTemplateV1 } from "../schema";
import type { FieldOrigins, ResolvedActor } from "./types";
import { applyResolvedScale } from "./scale";

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
  const resolved = applyResolvedScale(merge<InheritableSpec>(world.defaults, actor.overrides));
  const origins: FieldOrigins = {};
  for (const path of leafPaths(world.defaults)) origins[path] = { source: "world", documentId: world.worldId, version: world.revision };
  for (const path of leafPaths(actor.overrides)) origins[path] = { source: "actor", documentId: actor.actorId, version: actor.revision };
  attachDerivedScaleOrigins(origins);
  return { ...actor, resolved, fieldOrigins: origins };
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

