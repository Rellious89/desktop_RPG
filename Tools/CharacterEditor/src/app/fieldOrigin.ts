import type { ActorDocument } from "./types";
import type { FieldOriginDisplay } from "../components/common/FieldRow";

type Overrides = ActorDocument["overrides"];
type Anatomy = NonNullable<Overrides["anatomy"]>;
type Production = NonNullable<Overrides["production"]>;
type View = NonNullable<Overrides["view"]>;
type PixelStyle = NonNullable<Overrides["pixelStyle"]>;

/**
 * Derives inherited-vs-override badge state directly from whether an
 * override path is present on the authored actor document. The integrated
 * spec states this rule in prose ("Missing override means inherit" —
 * section 4), so this does not need to depend on resolveActor()'s
 * `fieldOrigins` map format.
 */
export function anatomyOrigin(actor: ActorDocument, key: keyof Anatomy): FieldOriginDisplay {
  return actor.overrides.anatomy?.[key] !== undefined ? "override" : "inherited";
}

export function productionOrigin(actor: ActorDocument, key: keyof Production): FieldOriginDisplay {
  return actor.overrides.production?.[key] !== undefined ? "override" : "inherited";
}

export function viewOrigin(actor: ActorDocument, key: keyof View): FieldOriginDisplay {
  return actor.overrides.view?.[key] !== undefined ? "override" : "inherited";
}

export function pixelStyleOrigin(actor: ActorDocument, key: keyof PixelStyle): FieldOriginDisplay {
  return actor.overrides.pixelStyle?.[key] !== undefined ? "override" : "inherited";
}
