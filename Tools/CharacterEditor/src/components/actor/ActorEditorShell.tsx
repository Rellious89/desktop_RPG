import { useState } from "react";
import type { ActorDocument, InheritableSpec, RuleId, ValidationDiagnostic, WorldTemplate } from "../../app/types";
import { IdentitySection } from "./IdentitySection";
import { BodySection } from "./BodySection";
import { LookSection } from "./LookSection";
import { EquipmentSection } from "./EquipmentSection";
import { ProductionSection } from "./ProductionSection";
import { ValidationPanel } from "./ValidationPanel";

type SectionId = "identity" | "body" | "look" | "equipment" | "production";

const SECTIONS: { id: SectionId; label: string }[] = [
  { id: "identity", label: "Identity" },
  { id: "body", label: "Body & Proportions" },
  { id: "look", label: "Look" },
  { id: "equipment", label: "Weapon & Equipment" },
  { id: "production", label: "Production" },
];

function sectionForFieldPath(fieldPath: string): SectionId {
  if (fieldPath.startsWith("anatomy") || fieldPath.startsWith("physicalTraits")) return "body";
  if (fieldPath.startsWith("constraints") || fieldPath.startsWith("appearance")) return "look";
  if (fieldPath.startsWith("equipment")) return "equipment";
  if (fieldPath.startsWith("production") || fieldPath.startsWith("view") || fieldPath.startsWith("pixelStyle"))
    return "production";
  return "identity";
}

export interface ActorEditorShellProps {
  actor: ActorDocument;
  world: WorldTemplate;
  resolved: InheritableSpec;
  diagnostics: ValidationDiagnostic[];
  onChangeActor: (updater: (actor: ActorDocument) => ActorDocument) => void;
  onApproveException: (ruleId: RuleId, reason: string) => void;
  onRemoveException: (ruleId: RuleId) => void;
  onSaveDraft: () => void;
  onGoCompare: () => void;
  onGoExport: () => void;
  draftSavedAt?: string | null;
}

export function ActorEditorShell({
  actor,
  world,
  resolved,
  diagnostics,
  onChangeActor,
  onApproveException,
  onRemoveException,
  onSaveDraft,
  onGoCompare,
  onGoExport,
  draftSavedAt,
}: ActorEditorShellProps) {
  const [section, setSection] = useState<SectionId>("identity");

  return (
    <div className="ce-stack">
      <div className="ce-card">
        <div className="ce-card-header">
          <div>
            <h2 className="ce-card-title">
              {actor.displayName.ko || actor.displayName.en || actor.actorId || "새 액터"}
            </h2>
            <p className="ce-card-subtitle">
              {world.displayName.ko ?? world.displayName.en} ({world.worldId}) 소속 ·{" "}
              {actor.actorType === "character" ? "Character" : "Monster"}
              {draftSavedAt && ` · 마지막 자동 저장 ${new Date(draftSavedAt).toLocaleTimeString("ko-KR")}`}
            </p>
          </div>
          <div className="ce-btn-group">
            <button type="button" className="ce-btn ce-btn--secondary" onClick={onSaveDraft}>
              초안 저장
            </button>
            <button type="button" className="ce-btn ce-btn--secondary" onClick={onGoCompare}>
              같은 세계 액터와 비교
            </button>
          </div>
        </div>
      </div>

      <div className="ce-editor-grid">
        <div>
          <nav className="ce-section-nav" aria-label="액터 편집 섹션">
            {SECTIONS.map((entry) => (
              <button
                key={entry.id}
                type="button"
                aria-selected={section === entry.id}
                onClick={() => setSection(entry.id)}
              >
                {entry.label}
              </button>
            ))}
          </nav>

          {section === "identity" && <IdentitySection actor={actor} world={world} onChangeActor={onChangeActor} />}
          {section === "body" && (
            <BodySection actor={actor} world={world} resolved={resolved} onChangeActor={onChangeActor} />
          )}
          {section === "look" && <LookSection actor={actor} onChangeActor={onChangeActor} />}
          {section === "equipment" && <EquipmentSection actor={actor} onChangeActor={onChangeActor} />}
          {section === "production" && (
            <ProductionSection actor={actor} world={world} resolved={resolved} onChangeActor={onChangeActor} />
          )}
        </div>

        <div className="ce-sticky-sidebar">
          <ValidationPanel
            diagnostics={diagnostics}
            approvedExceptions={actor.approvedExceptions}
            onApproveException={onApproveException}
            onRemoveException={onRemoveException}
            onNavigateToField={(fieldPath) => setSection(sectionForFieldPath(fieldPath))}
            onExport={onGoExport}
          />
        </div>
      </div>
    </div>
  );
}
