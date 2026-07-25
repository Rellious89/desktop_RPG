import type { ActorDocument, WorldTemplate } from "../../app/types";
import { Badge } from "../common/Badge";

export interface LibraryHomeProps {
  worlds: WorldTemplate[];
  actors: ActorDocument[];
  drafts: ActorDocument[];
  onOpenWorld: (world: WorldTemplate) => void;
  onNewWorld: () => void;
  onOpenActor: (actor: ActorDocument) => void;
  onNewActor: () => void;
  onOpenDraft: (draft: ActorDocument) => void;
  onDeleteDraft: (actorId: string) => void;
  onImport: () => void;
}

function worldStatusTone(status: WorldTemplate["status"]) {
  if (status === "active") return "success" as const;
  if (status === "hold") return "neutral" as const;
  return "info" as const;
}

function actorStatusTone(status: ActorDocument["identity"]["status"]) {
  if (status === "active" || status === "master") return "success" as const;
  if (status === "hold") return "neutral" as const;
  return "info" as const;
}

export function LibraryHome({
  worlds,
  actors,
  drafts,
  onOpenWorld,
  onNewWorld,
  onOpenActor,
  onNewActor,
  onOpenDraft,
  onDeleteDraft,
  onImport,
}: LibraryHomeProps) {
  return (
    <div className="ce-stack">
      <div className="ce-card">
        <div className="ce-card-header">
          <div>
            <h2 className="ce-card-title">라이브러리</h2>
            <p className="ce-card-subtitle">
              번들로 제공되는 World 템플릿과 액터 샘플에서 시작하거나, 새 항목을 만들거나, JSON을
              가져올 수 있습니다. 직접 JSON을 편집할 필요는 없습니다.
            </p>
          </div>
          <div className="ce-btn-group">
            <button type="button" className="ce-btn ce-btn--secondary" onClick={onNewWorld}>
              + 새 World 템플릿
            </button>
            <button type="button" className="ce-btn ce-btn--primary" onClick={onNewActor}>
              + 새 액터
            </button>
            <button type="button" className="ce-btn ce-btn--secondary" onClick={onImport}>
              JSON 가져오기
            </button>
          </div>
        </div>
      </div>

      <section className="ce-card">
        <h3 className="ce-card-title" style={{ fontSize: "var(--ce-font-size-base)" }}>
          번들 World 템플릿
        </h3>
        {worlds.length === 0 ? (
          <p className="ce-empty-state">아직 로드된 World 템플릿이 없습니다.</p>
        ) : (
          <div className="ce-library-grid" style={{ marginTop: "var(--ce-space-3)" }}>
            {worlds.map((world) => (
              <article className="ce-card" key={`${world.worldId}-${world.revision}`}>
                <div className="ce-row--between ce-row">
                  <strong>{world.displayName.ko ?? world.displayName.en}</strong>
                  <Badge tone={worldStatusTone(world.status)}>{world.status}</Badge>
                </div>
                <p className="ce-field-hint">
                  {world.worldId} · {world.displayName.en} · rev.{world.revision}
                </p>
                <p className="ce-field-hint">
                  {world.defaults.view.projection} · {world.defaults.view.facing} · PPU{" "}
                  {world.defaults.production.pixelsPerUnit}
                </p>
                <button
                  type="button"
                  className="ce-btn ce-btn--secondary ce-btn--sm"
                  style={{ marginTop: "var(--ce-space-2)" }}
                  onClick={() => onOpenWorld(world)}
                >
                  열기
                </button>
              </article>
            ))}
          </div>
        )}
      </section>

      <section className="ce-card">
        <h3 className="ce-card-title" style={{ fontSize: "var(--ce-font-size-base)" }}>
          번들 액터 샘플
        </h3>
        {actors.length === 0 ? (
          <p className="ce-empty-state">아직 로드된 액터 샘플이 없습니다.</p>
        ) : (
          <div className="ce-library-grid" style={{ marginTop: "var(--ce-space-3)" }}>
            {actors.map((actor) => (
              <article className="ce-card" key={actor.actorId}>
                <div className="ce-row--between ce-row">
                  <strong>{actor.displayName.ko ?? actor.displayName.en}</strong>
                  <Badge tone={actorStatusTone(actor.identity.status)}>{actor.identity.status}</Badge>
                </div>
                <p className="ce-field-hint">
                  {actor.actorId}
                  {actor.aliases.length > 0 ? ` (별칭: ${actor.aliases.join(", ")})` : ""}
                </p>
                <p className="ce-field-hint">
                  {actor.actorType === "character" ? "Character" : "Monster"} · {actor.worldRef.worldId} ·{" "}
                  {actor.identity.role}
                </p>
                <button
                  type="button"
                  className="ce-btn ce-btn--secondary ce-btn--sm"
                  style={{ marginTop: "var(--ce-space-2)" }}
                  onClick={() => onOpenActor(actor)}
                >
                  열기
                </button>
              </article>
            ))}
          </div>
        )}
      </section>

      <section className="ce-card">
        <h3 className="ce-card-title" style={{ fontSize: "var(--ce-font-size-base)" }}>
          내 초안 (이 브라우저에 저장됨)
        </h3>
        {drafts.length === 0 ? (
          <p className="ce-empty-state">저장된 초안이 없습니다. 액터를 편집하면 자동으로 저장됩니다.</p>
        ) : (
          <div className="ce-library-grid" style={{ marginTop: "var(--ce-space-3)" }}>
            {drafts.map((draft) => (
              <article className="ce-card" key={draft.actorId}>
                <strong>{draft.actorId || "(ID 미입력)"}</strong>
                <p className="ce-field-hint">
                  마지막 저장: {new Date(draft.updatedAt).toLocaleString("ko-KR")}
                </p>
                <div className="ce-btn-group" style={{ marginTop: "var(--ce-space-2)" }}>
                  <button
                    type="button"
                    className="ce-btn ce-btn--secondary ce-btn--sm"
                    onClick={() => onOpenDraft(draft)}
                  >
                    이어서 편집
                  </button>
                  <button
                    type="button"
                    className="ce-btn ce-btn--danger ce-btn--sm"
                    onClick={() => onDeleteDraft(draft.actorId)}
                  >
                    삭제
                  </button>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}
