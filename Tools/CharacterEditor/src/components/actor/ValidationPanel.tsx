import { useState } from "react";
import type { ApprovedException, RuleId, Severity, ValidationDiagnostic } from "../../app/types";
import { Badge } from "../common/Badge";

export interface ValidationPanelProps {
  diagnostics: ValidationDiagnostic[];
  approvedExceptions: ApprovedException[];
  onApproveException: (ruleId: RuleId, reason: string) => void;
  onRemoveException: (ruleId: RuleId) => void;
  onNavigateToField?: (fieldPath: string) => void;
  onExport: () => void;
}

function severityLabel(severity: Severity): string {
  if (severity === "error") return "Blocking";
  if (severity === "warning") return "Warning";
  return "Info";
}

function severityBadgeTone(severity: Severity) {
  if (severity === "error") return "error" as const;
  if (severity === "warning") return "warning" as const;
  return "info" as const;
}

function ExceptionForm({
  ruleId,
  existingReason,
  onSubmit,
  onCancel,
}: {
  ruleId: RuleId;
  existingReason?: string;
  onSubmit: (reason: string) => void;
  onCancel: () => void;
}) {
  const [reason, setReason] = useState(existingReason ?? "");
  const trimmedLength = reason.trim().length;

  return (
    <div className="ce-exception-form">
      <label className="ce-field-hint" htmlFor={`exception-reason-${ruleId}`}>
        승인 사유 (최소 10자, Export된 JSON/Markdown에 그대로 남습니다)
      </label>
      <textarea
        id={`exception-reason-${ruleId}`}
        value={reason}
        onChange={(event) => setReason(event.target.value)}
      />
      <div className="ce-btn-group">
        <button
          type="button"
          className="ce-btn ce-btn--primary ce-btn--sm"
          disabled={trimmedLength < 10}
          onClick={() => onSubmit(reason.trim())}
        >
          예외 등록
        </button>
        <button type="button" className="ce-btn ce-btn--secondary ce-btn--sm" onClick={onCancel}>
          취소
        </button>
      </div>
      {trimmedLength > 0 && trimmedLength < 10 && (
        <p className="ce-field-hint">사유는 최소 10자 이상 입력하세요 ({trimmedLength}/10).</p>
      )}
    </div>
  );
}

export function ValidationPanel({
  diagnostics,
  approvedExceptions,
  onApproveException,
  onRemoveException,
  onNavigateToField,
  onExport,
}: ValidationPanelProps) {
  const [openExceptionFor, setOpenExceptionFor] = useState<RuleId | null>(null);

  const blockingCount = diagnostics.filter((d) => d.blocksExport).length;
  const warningCount = diagnostics.filter((d) => d.severity === "warning").length;
  const infoCount = diagnostics.filter((d) => d.severity === "info").length;
  const activeExceptionCount = approvedExceptions.filter((e) => e.active).length;

  const canExport = blockingCount === 0;

  function findException(ruleId: RuleId): ApprovedException | undefined {
    return approvedExceptions.find((exception) => exception.ruleId === ruleId && exception.active);
  }

  return (
    <div className="ce-card" aria-label="Validation">
      <h3 className="ce-card-title" style={{ fontSize: "var(--ce-font-size-base)" }}>
        Validation
      </h3>
      <div className="ce-summary-counts" style={{ marginTop: "var(--ce-space-2)" }}>
        <Badge tone="error">Blocking {blockingCount}</Badge>
        <Badge tone="warning">Warning {warningCount}</Badge>
        {infoCount > 0 && <Badge tone="info">Info {infoCount}</Badge>}
        {activeExceptionCount > 0 && <Badge tone="success">승인된 예외 {activeExceptionCount}</Badge>}
      </div>

      {diagnostics.length === 0 ? (
        <p className="ce-empty-state" style={{ marginTop: "var(--ce-space-3)" }}>
          문제가 발견되지 않았습니다.
        </p>
      ) : (
        <ul className="ce-validation-list" style={{ marginTop: "var(--ce-space-3)" }}>
          {diagnostics.map((diagnostic) => {
            const resolvedException = diagnostic.exceptionApproved ? findException(diagnostic.ruleId) : undefined;
            const isResolved = diagnostic.severity === "error" && !diagnostic.blocksExport;
            return (
              <li
                key={diagnostic.ruleId}
                className={`ce-validation-item ce-validation-item--${diagnostic.severity}${isResolved ? " ce-validation-item--resolved" : ""}`}
              >
                <div className="ce-row--between ce-row">
                  <span className="ce-row">
                    <Badge tone={severityBadgeTone(diagnostic.severity)}>{severityLabel(diagnostic.severity)}</Badge>
                    <code className="ce-field-hint">{diagnostic.ruleId}</code>
                  </span>
                  {diagnostic.path && onNavigateToField && (
                    <button
                      type="button"
                      className="ce-btn ce-btn--ghost ce-btn--sm"
                      onClick={() => onNavigateToField(diagnostic.path!)}
                    >
                      바로가기
                    </button>
                  )}
                </div>
                <p className="ce-validation-message">{diagnostic.message}</p>

                {diagnostic.exceptionApproved ? (
                  <div className="ce-validation-actions">
                    <Badge tone="success">승인된 예외</Badge>
                    {resolvedException && <span className="ce-field-hint">{resolvedException.reason}</span>}
                    <button
                      type="button"
                      className="ce-btn ce-btn--ghost ce-btn--sm"
                      onClick={() => onRemoveException(diagnostic.ruleId)}
                    >
                      예외 해제
                    </button>
                  </div>
                ) : (
                  diagnostic.overridable &&
                  diagnostic.severity !== "info" && (
                    <div className="ce-validation-actions">
                      {openExceptionFor === diagnostic.ruleId ? (
                        <ExceptionForm
                          ruleId={diagnostic.ruleId}
                          onSubmit={(reason) => {
                            onApproveException(diagnostic.ruleId, reason);
                            setOpenExceptionFor(null);
                          }}
                          onCancel={() => setOpenExceptionFor(null)}
                        />
                      ) : (
                        <button
                          type="button"
                          className="ce-btn ce-btn--secondary ce-btn--sm"
                          onClick={() => setOpenExceptionFor(diagnostic.ruleId)}
                        >
                          예외 등록
                        </button>
                      )}
                    </div>
                  )
                )}
              </li>
            );
          })}
        </ul>
      )}

      <button
        type="button"
        className="ce-btn ce-btn--primary"
        style={{ marginTop: "var(--ce-space-4)", width: "100%" }}
        disabled={!canExport}
        onClick={onExport}
        title={canExport ? undefined : "Blocking 항목을 모두 해결하거나 승인된 예외를 등록해야 합니다."}
      >
        Export 미리보기 ({blockingCount === 0 ? "가능" : `Blocking ${blockingCount}건`})
      </button>
    </div>
  );
}
