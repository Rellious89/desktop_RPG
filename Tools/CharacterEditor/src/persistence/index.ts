import type { ActorDocumentV1, WorldTemplateV1 } from "../schema";
import { parseActor, parseWorld } from "../domain";

const PREFIX = "keybuddy-character-editor:v1";
export const actorDraftKey = (actorId: string) => `${PREFIX}:actor:${actorId}`;
export const worldDraftKey = (worldId: string, version: number) => `${PREFIX}:world:${worldId}:v${version}`;

export function saveActorDraft(actor: ActorDocumentV1, storage: Storage = localStorage): void {
  storage.setItem(actorDraftKey(actor.actorId), JSON.stringify(actor));
}
export function loadActorDraft(actorId: string, storage: Storage = localStorage): ActorDocumentV1 | null {
  const value = storage.getItem(actorDraftKey(actorId)); return value ? parseActor(JSON.parse(value)) : null;
}
export function saveWorldDraft(world: WorldTemplateV1, storage: Storage = localStorage): void {
  storage.setItem(worldDraftKey(world.worldId, world.revision), JSON.stringify(world));
}
export function loadWorldDraft(worldId: string, version: number, storage: Storage = localStorage): WorldTemplateV1 | null {
  const value = storage.getItem(worldDraftKey(worldId, version)); return value ? parseWorld(JSON.parse(value)) : null;
}
export function importActorJson(text: string): ActorDocumentV1 { return parseActor(JSON.parse(text)); }
export function importWorldJson(text: string): WorldTemplateV1 { return parseWorld(JSON.parse(text)); }
export function downloadText(filename: string, text: string, mime = "application/json"): void {
  const url = URL.createObjectURL(new Blob([text], { type: `${mime};charset=utf-8` }));
  const anchor = document.createElement("a"); anchor.href = url; anchor.download = filename; anchor.click(); URL.revokeObjectURL(url);
}
export function assertSafeId(value: string): string {
  if (!/^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(value) || value.includes("..")) throw new Error("Unsafe file ID");
  return value;
}

