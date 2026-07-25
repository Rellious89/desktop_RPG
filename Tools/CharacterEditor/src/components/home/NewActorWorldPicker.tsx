import type { ActorDocument, WorldTemplate } from "../../app/types";

export interface NewActorWorldPickerProps {
  worlds: WorldTemplate[];
  onPick: (world: WorldTemplate, actorType: ActorDocument["actorType"]) => void;
  onCancel: () => void;
}

export function NewActorWorldPicker({ worlds, onPick, onCancel }: NewActorWorldPickerProps) {
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
          {worlds.map((world) => (
            <article className="ce-card" key={`${world.worldId}-${world.revision}`}>
              <strong>{world.displayName.ko ?? world.displayName.en}</strong>
              <p className="ce-field-hint">
                {world.worldId} · rev.{world.revision}
              </p>
              <div className="ce-btn-group" style={{ marginTop: "var(--ce-space-2)" }}>
                <button
                  type="button"
                  className="ce-btn ce-btn--secondary ce-btn--sm"
                  onClick={() => onPick(world, "character")}
                >
                  Character 만들기
                </button>
                <button
                  type="button"
                  className="ce-btn ce-btn--secondary ce-btn--sm"
                  onClick={() => onPick(world, "monster")}
                >
                  Monster 만들기
                </button>
              </div>
            </article>
          ))}
        </div>
      )}
    </div>
  );
}
