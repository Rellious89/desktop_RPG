import { ZodError } from "zod";
import type { DomainApi } from "./domainApi";
import type { ActorDocument, ParseResult, WorldTemplate } from "./types";

/**
 * The single integration seam between this UI (src/app, src/components) and
 * the Codex-owned src/domain and src/export modules. Every import here is
 * dynamic specifically so that:
 *   - vitest UI tests never need these modules to exist (they inject a
 *     mock DomainApi directly and never call loadRealDomainApi()).
 *   - the production app can start, catch a failed/missing dynamic import,
 *     and show a clear status banner instead of a white screen.
 *
 * Function names and signatures are taken verbatim from src/domain/index.ts
 * and src/export/index.ts (read on 2026-07-25 after Codex's Wave 2 delivery):
 *   parseWorld(input), parseActor(input) — zod .parse(), throws ZodError
 *   resolveActor(actor, world) — throws if worldRef doesn't pin this world
 *   validateActor(actor, world, references: ActorReference[])
 *   compareActors(draft, reference, world)
 *   serializeActor(actor): string
 *   buildExport(actor, world, references: ActorDocument[])
 *   exportJson(envelope), exportMarkdown(envelope)
 */

function formatZodError(error: unknown, label: string): string[] {
  if (error instanceof ZodError) {
    return error.issues.map((issue) => `${issue.path.join(".") || "(root)"}: ${issue.message}`);
  }
  return [error instanceof Error ? error.message : `${label} 파싱 중 알 수 없는 오류가 발생했습니다.`];
}

function toParseResult<T>(label: string, run: () => T): ParseResult<T> {
  try {
    return { success: true, data: run() };
  } catch (error) {
    return { success: false, errors: formatZodError(error, label) };
  }
}

export async function loadRealDomainApi(): Promise<DomainApi> {
  const domainModule = await import("../domain");

  const required = [
    "parseWorld",
    "parseActor",
    "resolveActor",
    "validateActor",
    "compareActors",
    "serializeActor",
    "buildExport",
    "exportJson",
    "exportMarkdown",
  ] as const;
  const missing = required.filter((name) => typeof (domainModule as Record<string, unknown>)[name] !== "function");
  if (missing.length > 0) {
    throw new Error(`src/domain 모듈에서 다음 함수를 찾지 못했습니다: ${missing.join(", ")}`);
  }

  return {
    parseWorld: (input) => toParseResult<WorldTemplate>("World Template", () => domainModule.parseWorld(input)),
    parseActor: (input) => toParseResult<ActorDocument>("Actor", () => domainModule.parseActor(input)),
    resolveActor: (actor, world) => domainModule.resolveActor(actor, world),
    validateActor: (actor, world, references) => domainModule.validateActor(actor, world, references ?? []),
    compareActors: (draft, reference, world) => domainModule.compareActors(draft, reference, world),
    serializeActor: (actor) => domainModule.serializeActor(actor),
    buildExport: (actor, world, references) => domainModule.buildExport(actor, world, references ?? []),
    exportJson: (envelope) => domainModule.exportJson(envelope),
    exportMarkdown: (envelope) => domainModule.exportMarkdown(envelope),
  };
}
