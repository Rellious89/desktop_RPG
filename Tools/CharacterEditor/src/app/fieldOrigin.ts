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

/** Body size is one field spread over two keys: the authored physical height
 * and its logical mirror. Documents written before the split carry only the
 * mirror, so either key means the actor overrode its size. */
export function bodyHeightOrigin(actor: ActorDocument): FieldOriginDisplay {
  const anatomy = actor.overrides.anatomy;
  return anatomy?.targetPhysicalHeightPx !== undefined || anatomy?.targetLogicalHeightPx !== undefined
    ? "override"
    : "inherited";
}

/** Likewise for density: the preset and the block size are set together, but a
 * hand-authored document may carry only the block. */
export function densityOrigin(actor: ActorDocument): FieldOriginDisplay {
  const pixelStyle = actor.overrides.pixelStyle;
  return pixelStyle?.logicalBlockPx !== undefined || pixelStyle?.densityPreset !== undefined
    ? "override"
    : "inherited";
}
