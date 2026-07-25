/**
 * UI-facing type aliases for the real Schema v1 / domain contract that
 * Codex's src/schema and src/domain now export. This app path consumes
 * those types directly (no more speculative shadow types) and only renames
 * a few for readability in this codebase's components/tests.
 */
export type {
  WorldTemplateV1 as WorldTemplate,
  ActorDocumentV1 as ActorDocument,
  InheritableSpec,
  EvidenceRef,
  ApprovedException,
} from "../schema";

export type {
  FieldOrigin,
  FieldOrigins,
  ResolvedActor,
  Severity,
  Diagnostic as ValidationDiagnostic,
  ComparisonMetric,
  ActorComparison as ComparisonResult,
  ActorExportEnvelope as ExportEnvelope,
  ActorReference,
} from "../domain";

/** Rule IDs are plain strings in the real Diagnostic type (see
 * src/domain/validation.ts RULE_IDS); kept as an alias rather than a union
 * so it stays structurally compatible. */
export type RuleId = string;

/** UI ergonomic wrapper: the real parseWorld/parseActor throw on invalid
 * input (they're thin zod .parse() calls). DomainApi's implementation
 * catches that and normalizes it into this shape so import/save flows can
 * branch without try/catch at every call site — no validation logic lives
 * here, only exception-to-result translation. */
export type ParseResult<T> = { success: true; data: T } | { success: false; errors: string[] };
