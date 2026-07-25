import type { ActorDocument, WorldTemplate } from "./types";

/**
 * Wraps src/persistence (Codex-owned) for localStorage drafts, JSON import
 * parsing, and file download. Loaded via dynamic import for the same reason
 * as realDomainApi.ts (UI tests never need it; production degrades to a
 * status banner if it's missing).
 *
 * src/persistence exposes single-item save/load (saveActorDraft, etc.) but
 * no "list everything" helper, so listActorDraftIds()/listWorldDrafts()
 * enumerate localStorage keys directly. The key prefixes below are copied
 * verbatim from src/persistence/index.ts's actorDraftKey/worldDraftKey
 * (`keybuddy-character-editor:v1:actor:` / `:world:`) since only a
 * per-item key builder is exported, not a scan/list function — if Codex
 * changes that prefix, this enumeration needs to move in lockstep.
 */

const ACTOR_DRAFT_PREFIX = "keybuddy-character-editor:v1:actor:";
const WORLD_DRAFT_PREFIX = "keybuddy-character-editor:v1:world:";

export interface WorldDraftRef {
  worldId: string;
  version: number;
}

export interface PersistenceApi {
  saveActorDraft(actor: ActorDocument): void;
  loadActorDraft(actorId: string): ActorDocument | null;
  deleteActorDraft(actorId: string): void;
  listActorDraftIds(): string[];
  saveWorldDraft(world: WorldTemplate): void;
  loadWorldDraft(worldId: string, version: number): WorldTemplate | null;
  listWorldDrafts(): WorldDraftRef[];
  importActorJson(text: string): ActorDocument;
  importWorldJson(text: string): WorldTemplate;
  downloadText(filename: string, text: string, mime?: string): void;
}

function listKeysWithPrefix(prefix: string): string[] {
  const keys: string[] = [];
  for (let i = 0; i < window.localStorage.length; i += 1) {
    const key = window.localStorage.key(i);
    if (key && key.startsWith(prefix)) keys.push(key);
  }
  return keys;
}

export async function loadRealPersistenceApi(): Promise<PersistenceApi> {
  const module = await import("../persistence");

  return {
    saveActorDraft: (actor) => module.saveActorDraft(actor),
    loadActorDraft: (actorId) => {
      try {
        return module.loadActorDraft(actorId);
      } catch {
        return null;
      }
    },
    deleteActorDraft: (actorId) => window.localStorage.removeItem(module.actorDraftKey(actorId)),
    listActorDraftIds: () =>
      listKeysWithPrefix(ACTOR_DRAFT_PREFIX).map((key) => key.slice(ACTOR_DRAFT_PREFIX.length)),

    saveWorldDraft: (world) => module.saveWorldDraft(world),
    loadWorldDraft: (worldId, version) => {
      try {
        return module.loadWorldDraft(worldId, version);
      } catch {
        return null;
      }
    },
    listWorldDrafts: () =>
      listKeysWithPrefix(WORLD_DRAFT_PREFIX)
        .map((key) => key.slice(WORLD_DRAFT_PREFIX.length))
        .map((rest) => {
          const lastColon = rest.lastIndexOf(":v");
          if (lastColon < 0) return null;
          const worldId = rest.slice(0, lastColon);
          const version = Number(rest.slice(lastColon + 2));
          return Number.isFinite(version) ? { worldId, version } : null;
        })
        .filter((entry): entry is WorldDraftRef => entry !== null),

    importActorJson: (text) => module.importActorJson(text),
    importWorldJson: (text) => module.importWorldJson(text),
    downloadText: (filename, text, mime) => module.downloadText(filename, text, mime),
  };
}
