import { createContext, useContext } from "react";
import type {
  ActorDocument,
  ActorReference,
  ComparisonResult,
  ExportEnvelope,
  ParseResult,
  ResolvedActor,
  ValidationDiagnostic,
  WorldTemplate,
} from "./types";

/**
 * UI-facing wrapper around the real domain functions in src/domain and
 * src/export. Signatures mirror the real ones (see src/domain/index.ts,
 * src/domain/*.ts, src/export/index.ts) with one deliberate ergonomic
 * change: parseWorld/parseActor return a ParseResult instead of throwing,
 * so import/save flows can branch without try/catch at every call site.
 * That's the only behavior added here — no merge/validation/export logic
 * is reimplemented.
 */
export interface DomainApi {
  parseWorld(input: unknown): ParseResult<WorldTemplate>;
  parseActor(input: unknown): ParseResult<ActorDocument>;
  resolveActor(actor: ActorDocument, world: WorldTemplate): ResolvedActor;
  validateActor(
    actor: ActorDocument,
    world: WorldTemplate,
    references?: ActorReference[],
  ): ValidationDiagnostic[];
  compareActors(draft: ActorDocument, reference: ActorDocument, world: WorldTemplate): ComparisonResult;
  serializeActor(actor: ActorDocument): string;
  buildExport(
    actor: ActorDocument,
    world: WorldTemplate,
    references?: ActorDocument[],
  ): ExportEnvelope;
  exportJson(envelope: ExportEnvelope): string;
  exportMarkdown(envelope: ExportEnvelope): string;
}

export const DomainApiContext = createContext<DomainApi | null>(null);

export function useDomainApi(): DomainApi {
  const api = useContext(DomainApiContext);
  if (!api) {
    throw new Error(
      "useDomainApi() called outside a DomainApiContext.Provider. Wrap the tree in <DomainApiContext.Provider value={...}>.",
    );
  }
  return api;
}
