import { useState } from "react";
import type { ActorDocument, WorldTemplate } from "../../app/types";
import { DENSITY_OPTIONS, DENSITY_PRESETS, logicalHeightAt, resolveScale, type DensityPresetId } from "../../app/scale";

/** New actors pick a pixel density up front. Existing actors keep whatever
 * their world already uses — this choice never reaches them. */
export interface NewActorWorldPickerProps {
  worlds: WorldTemplate[];
  onPick: (
    world: WorldTemplate,
    actorType: ActorDocument["actorType"],
    densityPreset: DensityPresetId,
  ) => void;
  onCancel: () => void;
}

export function NewActorWorldPicker({ worlds, onPick, onCancel }: NewActorWorldPickerProps) {
  const [density, setDensity] = useState<Record<string, DensityPresetId>>({});

  return (
    <div className="ce-card">
      <div className="ce-card-header">
        <div>
          <h2 className="ce-card-title">새 액터 만들기</h2>
          <p className="ce-card-subtitle">
            먼저 이 액터가 속할 World를 선택하세요. 선택 즉시 World의 기본값이 상속됩니다.
          </p>
        </div>
        <button type="button" className="ce-btn ce-btn--secondary" onClick={onCancel}>
          취소
        </button>
      </div>

      {worlds.length === 0 ? (
        <p className="ce-empty-state">먼저 World 템플릿을 하나 만들어야 합니다.</p>
      ) : (
        <div className="ce-library-grid">
          {worlds.map((world) => {
            const key = `${world.worldId}-${world.revision}`;
            const worldScale = resolveScale(world.defaults.anatomy, world.defaults.pixelStyle);
            const picked = density[key] ?? worldScale.densityPreset;
            const blockPx = picked === "custom" ? worldScale.blockPx : DENSITY_PRESETS[picked];
            return (
              <article className="ce-card" key={key}>
                <strong>{world.displayName.ko ?? world.displayName.en}</strong>
                <p className="ce-field-hint">
                  {world.worldId} · rev.{world.revision}
                </p>
                <label className="ce-field-label" htmlFor={`density-${key}`}>
                  픽셀 밀도
                </label>
                <select
                  id={`density-${key}`}
                  value={picked}
                  onChange={(event) =>
                    setDensity((current) => ({ ...current, [key]: event.target.value as DensityPresetId }))
                  }
                >
                  {DENSITY_OPTIONS.map((option) => (
                    <option key={option.id} value={option.id}>
                      {option.label}
                      {option.id === worldScale.densityPreset ? " (World 기본)" : ""}
                    </option>
                  ))}
                  {worldScale.densityPreset === "custom" && <option value="custom">World 기본 (custom)</option>}
                </select>
                <p className="ce-field-hint">
                  물리 높이 {worldScale.targetPhysicalHeightPx}px 유지 →{" "}
                  {logicalHeightAt(worldScale.targetPhysicalHeightPx, blockPx)} logical px
                </p>
                <div className="ce-btn-group" style={{ marginTop: "var(--ce-space-2)" }}>
                  <button
                    type="button"
                    className="ce-btn ce-btn--secondary ce-btn--sm"
                    onClick={() => onPick(world, "character", picked)}
                  >
                    Character 만들기
                  </button>
                  <button
                    type="button"
                    className="ce-btn ce-btn--secondary ce-btn--sm"
                    onClick={() => onPick(world, "monster", picked)}
                  >
                    Monster 만들기
                  </button>
                </div>
              </article>
            );
          })}
        </div>
      )}
    </div>
  );
}
