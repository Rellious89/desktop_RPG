import type { ActorDocument, ComparisonResult } from "../../app/types";
import { Badge } from "../common/Badge";

export interface ComparisonViewProps {
  actor: ActorDocument;
  candidates: ActorDocument[];
  selectedReferenceId: string | null;
  comparison: ComparisonResult | null;
  onSelectReference: (actorId: string) => void;
  onBack: () => void;
}

/**
 * The domain's ComparisonMetric contract documents an explicit `matches`
 * boolean per metric (true = identical, false = differs), computed by the
 * domain via strict equality for text metrics and zero-delta for numeric
 * ones. The metric type is widened locally (rather than editing domain
 * types) so the UI trusts that field the moment the domain ships it, while
 * falling back to an equivalent local computation if it is absent so
 * string-valued metrics (e.g. stature "tall" vs "average") aren't
 * mis-flagged as matching just because they have no numeric delta.
 */
type MetricRow = ComparisonResult["metrics"][number] & { matches?: boolean };

function resolveMatches(row: MetricRow): boolean {
  if (row.matches !== undefined) return row.matches;
  return row.absoluteDelta !== undefined ? row.absoluteDelta === 0 : row.draft === row.reference;
}

function flagTone(matches: boolean) {
  return matches ? ("success" as const) : ("info" as const);
}

function flagLabel(matches: boolean) {
  return matches ? "일치" : "참고";
}

function deltaText(row: MetricRow, matches: boolean): string {
  if (row.absoluteDelta !== undefined) {
    return `${row.absoluteDelta > 0 ? "+" : ""}${row.absoluteDelta}${
      row.percentDelta !== undefined ? ` (${row.percentDelta > 0 ? "+" : ""}${row.percentDelta.toFixed(1)}%)` : ""
    }`;
  }
  return matches ? "동일" : "다름";
}

export function ComparisonView({
  actor,
  candidates,
  selectedReferenceId,
  comparison,
  onSelectReference,
  onBack,
}: ComparisonViewProps) {
  return (
    <div className="ce-stack">
      <div className="ce-card">
        <div className="ce-card-header">
          <div>
            <h2 className="ce-card-title">Comparison</h2>
            <p className="ce-card-subtitle">
              {actor.displayName.ko || actor.actorId}를 같은 세계의 기존 액터와 비교합니다. 브리프
              요구대로 텍스트/숫자 비교이며, 실루엣 오버레이는 향후 기능입니다.
            </p>
          </div>
          <button type="button" className="ce-btn ce-btn--secondary" onClick={onBack}>
            돌아가기
          </button>
        </div>

        {candidates.length === 0 ? (
          <p className="ce-empty-state">
            같은 세계에 비교할 수 있는 Master/Active 상태의 기존 액터가 아직 없습니다. Export를 계속
            진행할 수 있습니다.
          </p>
        ) : (
          <>
            <div className="ce-field" style={{ maxWidth: 360 }}>
              <label className="ce-field-label" htmlFor="comparison-reference">
                비교 대상 (같은 세계, Master/Active만 표시)
              </label>
              <select
                id="comparison-reference"
                value={selectedReferenceId ?? ""}
                onChange={(event) => onSelectReference(event.target.value)}
              >
                <option value="" disabled>
                  선택하세요
                </option>
                {candidates.map((candidate) => (
                  <option key={candidate.actorId} value={candidate.actorId}>
                    {candidate.displayName.ko || candidate.displayName.en} ({candidate.actorId})
                  </option>
                ))}
              </select>
            </div>

            {comparison && (
              <div className="ce-table-scroll" style={{ marginTop: "var(--ce-space-4)" }}>
                <table className="ce-table">
                  <thead>
                    <tr>
                      <th>지표</th>
                      <th>Draft</th>
                      <th>Reference</th>
                      <th>Δ</th>
                      <th>플래그</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(comparison.metrics as MetricRow[]).map((row) => {
                      const matches = resolveMatches(row);
                      return (
                        <tr key={row.key}>
                          <td>{row.label}</td>
                          <td>{row.draft}</td>
                          <td>{row.reference}</td>
                          <td>{deltaText(row, matches)}</td>
                          <td>
                            <Badge tone={flagTone(matches)}>{flagLabel(matches)}</Badge>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
