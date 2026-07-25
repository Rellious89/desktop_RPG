import type { ActorDocument, WorldTemplate } from "./types";

/**
 * Adapts whatever the Codex-owned src/data module exports into
 * { worlds, actors }. Rather than guessing exact export names (not
 * specified by the integrated spec), this scans every exported value
 * (including one level of array/object nesting) for documents carrying the
 * schema's own `documentKind` discriminator — "world-template" or "actor" —
 * which section 4 of the spec guarantees every document has. This makes the
 * loader resilient to naming choices Codex makes for src/data's exports.
 */

type UnknownRecord = Record<string, unknown>;

function isDocumentKind(value: unknown, kind: string): value is UnknownRecord {
  return (
    !!value &&
    typeof value === "object" &&
    (value as UnknownRecord).documentKind === kind
  );
}

function collectDocuments(namespace: UnknownRecord): {
  worlds: WorldTemplate[];
  actors: ActorDocument[];
} {
  const worlds: WorldTemplate[] = [];
  const actors: ActorDocument[] = [];
  const seen = new Set<unknown>();

  const visit = (value: unknown, depth: number) => {
    if (!value || typeof value !== "object" || seen.has(value) || depth > 3) return;
    seen.add(value);

    if (isDocumentKind(value, "world-template")) {
      worlds.push(value as unknown as WorldTemplate);
      return;
    }
    if (isDocumentKind(value, "actor")) {
      actors.push(value as unknown as ActorDocument);
      return;
    }
    if (Array.isArray(value)) {
      value.forEach((entry) => visit(entry, depth + 1));
      return;
    }
    Object.values(value as UnknownRecord).forEach((entry) => visit(entry, depth + 1));
  };

  Object.values(namespace).forEach((entry) => visit(entry, 0));
  return { worlds, actors };
}

export interface SampleLibrary {
  worlds: WorldTemplate[];
  actors: ActorDocument[];
}

export async function loadSampleLibrary(): Promise<SampleLibrary> {
  const dataModule = (await import("../data")) as UnknownRecord;
  const library = collectDocuments(dataModule);
  if (library.worlds.length === 0 && library.actors.length === 0) {
    throw new Error(
      "src/data에서 world-template/actor 문서를 찾지 못했습니다. Codex 워커의 샘플 데이터가 아직 반영되지 않았을 수 있습니다.",
    );
  }
  return library;
}
