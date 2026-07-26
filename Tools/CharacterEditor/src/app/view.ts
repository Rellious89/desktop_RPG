/**
 * UI-facing seam for the view-direction rule, mirroring src/app/scale.ts.
 *
 * Like the scale math these are pure functions with no I/O, and forms need
 * them to *render*: a world authored before the light-direction field existed
 * carries only two of the three values, so the read-only panel would have
 * nothing to show until a resolve round-trip completed.
 */
export {
  FACING_OPTIONS,
  LIGHT_DIRECTION_OPTIONS,
  PROJECTION_OPTIONS,
  PROJECT_DEFAULT_VIEW,
  VIEW_KEYS,
  VIEW_ORIGIN_LABELS,
  VIEW_ORIGIN_LABELS_KO,
  isAllowedViewValue,
  masterImageDirectionPrompt,
  resolveView,
} from "../domain/view";
export type { ResolvedView, ViewKey, ViewOriginSource } from "../domain/view";
export type { Facing, LightDirection, Projection } from "../schema";

import { VIEW_KEYS, isAllowedViewValue, type ViewOriginSource } from "../domain/view";
import type { ActorDocument, WorldTemplate } from "./types";

/** Which document supplies an actor's direction. Same override-presence rule
 * as src/app/fieldOrigin.ts, and the same precedence as resolveActor's
 * fieldOrigins: an unusable value does not count as authored. */
export function viewOriginOf(actor: ActorDocument, world: WorldTemplate): ViewOriginSource {
  if (VIEW_KEYS.some((k) => isAllowedViewValue(k, actor.overrides.view?.[k]))) return "actor";
  if (VIEW_KEYS.some((k) => isAllowedViewValue(k, world.defaults.view?.[k]))) return "world";
  return "default";
}
