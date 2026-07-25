import type { ActorDocumentV1, InheritableSpec, WorldTemplateV1 } from "../schema";

export type FieldOrigin = { source: "world" | "actor"; documentId: string; version: number };
export type FieldOrigins = Record<string, FieldOrigin>;
export type ResolvedActor = ActorDocumentV1 & { resolved: InheritableSpec; fieldOrigins: FieldOrigins };
export type Severity = "error" | "warning" | "info";
export type Diagnostic = {
  ruleId: string; severity: Severity; message: string; path?: string;
  overridable: boolean; exceptionApproved: boolean; blocksExport: boolean;
};
export type ComparisonMetric = {
  key: string; label: string; draft: string | number; reference: string | number;
  matches?: boolean;
  absoluteDelta?: number; percentDelta?: number;
};
export type ActorComparison = { referenceActorId: string; metrics: ComparisonMetric[]; diagnostics: Diagnostic[] };
export type ActorExportEnvelope = {
  schemaVersion: "1.0.0"; documentKind: "actor-export";
  authored: ActorDocumentV1; resolved: InheritableSpec; fieldOrigins: FieldOrigins;
  calculated: Record<string, unknown>; interpretations: string[]; diagnostics: Diagnostic[];
  comparison?: ActorComparison;
};
export type ActorReference = { actor: ActorDocumentV1; world: WorldTemplateV1 };
