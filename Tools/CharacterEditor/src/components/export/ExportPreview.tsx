import { useState } from "react";
import type { ExportEnvelope } from "../../app/types";
import { Badge } from "../common/Badge";
import { ViewDirectionPanel } from "../common/ViewDirectionPanel";

export interface ExportPreviewProps {
  envelope: ExportEnvelope;
  jsonText: string;
  markdownText: string;
  onDownloadJson: () => void;
  onDownloadMarkdown: () => void;
  onBack: () => void;
}

type Tab = "json" | "markdown";

export function ExportPreview({
  envelope,
  jsonText,
  markdownText,
  onDownloadJson,
  onDownloadMarkdown,
  onBack,
}: ExportPreviewProps) {
  const [tab, setTab] = useState<Tab>("markdown");

  const blockingCount = envelope.diagnostics.filter((d) => d.blocksExport).length;
  const warningCount = envelope.diagnostics.filter((d) => d.severity === "warning").length;
  const exceptionCount = envelope.diagnostics.filter((d) => d.exceptionApproved).length;
  // The direction is the one exported value a reviewer cannot recover from the
  // rendered image later, so it is shown before the raw payload, not buried in it.
  const viewOrigin = envelope.fieldOrigins["view.facing"]?.source ?? "default";

  return (
    <div className="ce-stack">
      <div className="ce-card">
        <div className="ce-card-header">
          <div>
            <h2 className="ce-card-title">Export 미리보기</h2>
            <p className="ce-card-subtitle">
              {envelope.authored.actorId}.character.json / .character.md — Export는 항상
              모든 누락/충돌/경고/승인된 예외를 함께 기록합니다.
            </p>
          </div>
          <button type="button" className="ce-btn ce-btn--secondary" onClick={onBack}>
            편집으로 돌아가기
          </button>
        </div>

        <div className="ce-summary-counts">
          <Badge tone={blockingCount > 0 ? "error" : "success"}>Blocking {blockingCount}</Badge>
          <Badge tone="warning">Warning {warningCount}</Badge>
          {exceptionCount > 0 && <Badge tone="success">승인된 예외 {exceptionCount}</Badge>}
        </div>

        {blockingCount > 0 ? (
          <div className="ce-validation-item ce-validation-item--error" style={{ marginTop: "var(--ce-space-3)" }}>
            <strong>Export가 차단되었습니다.</strong>
            <p className="ce-validation-message">
              편집 화면의 Validation 패널에서 Blocking 항목을 해결하거나 승인된 예외를 등록하세요.
            </p>
          </div>
        ) : (
          <div className="ce-btn-group" style={{ marginTop: "var(--ce-space-3)" }}>
            <button type="button" className="ce-btn ce-btn--primary" onClick={onDownloadJson}>
              JSON 다운로드
            </button>
            <button type="button" className="ce-btn ce-btn--primary" onClick={onDownloadMarkdown}>
              Markdown 다운로드
            </button>
          </div>
        )}

        <div className="ce-tabs" style={{ marginTop: "var(--ce-space-4)" }}>
          <button type="button" aria-selected={tab === "markdown"} onClick={() => setTab("markdown")}>
            Markdown
          </button>
          <button type="button" aria-selected={tab === "json"} onClick={() => setTab("json")}>
            JSON
          </button>
        </div>

        <pre className="ce-code-block">{tab === "markdown" ? markdownText : jsonText}</pre>
      </div>

      <ViewDirectionPanel resolved={envelope.resolved} origin={viewOrigin} />
    </div>
  );
}
