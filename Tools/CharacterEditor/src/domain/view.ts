import {
  FACING_VALUES,
  LIGHT_DIRECTION_VALUES,
  PROJECTION_VALUES,
  type Facing,
  type InheritableSpec,
  type LightDirection,
  type Projection,
} from "../schema";

/**
 * View direction — the projection, screen facing and key-light direction a
 * design master must be drawn in.
 *
 * KeyBuddy production rule: every first-generation character and monster
 * master is a front-biased three-quarter view facing screen-right, lit from
 * the upper-left. Worlds carry that as their default and actors inherit it.
 *
 * The values were already merged into the export's `resolved` block, but
 * nothing printed them into the character sheet Markdown, so an image
 * generator working from the sheet alone had no direction to follow — that is
 * how MoleMiner ended up mastered facing screen-left. Resolution therefore
 * always produces all three values and always names where each came from;
 * a gap is never exported as "unknown".
 */

/** Project-wide fallback, used only when neither the actor nor its world
 * authored a value. Matches the KeyBuddy master production rule. */
export const PROJECT_DEFAULT_VIEW = {
  projection: "three-quarter",
  facing: "screen-right",
  lightDirection: "upper-left",
} as const satisfies ResolvedView;

/** Stands in for a document ID in `fieldOrigins` when the project fallback
 * supplied the value. */
export const PROJECT_DEFAULT_DOCUMENT_ID = "keybuddy-project-default";

/** Choices offered in the World Template form, in presentation order — the
 * project-standard value first where one exists. */
export const PROJECTION_OPTIONS = PROJECTION_VALUES;
export const FACING_OPTIONS = ["screen-right", "screen-left"] as const;
export const LIGHT_DIRECTION_OPTIONS = ["upper-left", "upper-right", "upper-center"] as const;

export const VIEW_KEYS = ["projection", "facing", "lightDirection"] as const;
export type ViewKey = (typeof VIEW_KEYS)[number];

export type ResolvedView = { projection: Projection; facing: Facing; lightDirection: LightDirection };
export type ViewOriginSource = "world" | "actor" | "default";

const ALLOWED: Record<ViewKey, readonly string[]> = {
  projection: PROJECTION_VALUES,
  facing: FACING_VALUES,
  lightDirection: LIGHT_DIRECTION_VALUES,
};

/** Whether a raw value is one of the values the schema allows. Hand-edited and
 * externally generated JSON reaches the domain without passing zod, so the
 * validator re-checks rather than trusting the type. */
export function isAllowedViewValue(key: ViewKey, value: unknown): boolean {
  return typeof value === "string" && ALLOWED[key].includes(value);
}

export type ViewResolution = {
  view: ResolvedView;
  /** Keys that no document authored and that fell back to the project default. */
  fellBackToDefault: ViewKey[];
};

/** Fills any missing or unusable field from the project default. */
export function resolveView(view: Partial<ResolvedView> | undefined): ViewResolution {
  const resolved = { ...PROJECT_DEFAULT_VIEW } as ResolvedView;
  const fellBackToDefault: ViewKey[] = [];
  for (const key of VIEW_KEYS) {
    const value = view?.[key];
    if (isAllowedViewValue(key, value)) (resolved as Record<ViewKey, string>)[key] = value as string;
    else fellBackToDefault.push(key);
  }
  return { view: resolved, fellBackToDefault };
}

/** Returns a spec whose view carries all three fields, so an export's
 * `resolved` block never omits a direction. Mirrors applyResolvedScale. */
export function applyResolvedView(spec: InheritableSpec): InheritableSpec {
  return { ...spec, view: resolveView(spec.view).view };
}

export const VIEW_ORIGIN_LABELS: Record<ViewOriginSource, string> = {
  world: "World Template",
  actor: "Actor Override",
  default: "Project Default",
};

export const VIEW_ORIGIN_LABELS_KO: Record<ViewOriginSource, string> = {
  world: "World Template",
  actor: "액터 재정의",
  default: "프로젝트 기본값",
};

const PROJECTION_PHRASES: Record<Projection, string> = {
  "three-quarter": "front-biased three-quarter full-body view",
  side: "full-body side view",
  front: "full-body front view",
};

const LIGHT_PHRASES: Record<LightDirection, string> = {
  "upper-left": "from the upper-left",
  "upper-right": "from the upper-right",
  "upper-center": "from directly above",
};

/**
 * The literal instruction to paste into a design-master image prompt. Kept as
 * one generated sentence so the sheet never describes the direction twice in
 * words that could drift apart, and never leaves it ambiguous.
 */
export function masterImageDirectionPrompt(view: ResolvedView): string {
  return `${PROJECTION_PHRASES[view.projection]}, facing ${view.facing}, with lighting ${LIGHT_PHRASES[view.lightDirection]}.`;
}
