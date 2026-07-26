import type { InheritableSpec } from "../../app/types";
import { VIEW_ORIGIN_LABELS_KO, masterImageDirectionPrompt, resolveView, type ViewOriginSource } from "../../app/view";
import { FieldRow } from "./FieldRow";

export interface ViewDirectionPanelProps {
  /** The actor's resolved spec, or a world template's defaults. */
  resolved: InheritableSpec;
  /** Which document supplied the direction. */
  origin: ViewOriginSource;
}

/**
 * Read-only display of the direction the design master must be drawn in.
 *
 * There is no per-actor direction control: the project keeps one direction per
 * world and every actor inherits it. What actors do need is to *see* the value
 * they inherit, because the design master is generated from this sheet, and a
 * master drawn the other way round has to be redrawn — the whole animation set
 * follows whichever way the master faces.
 */
export function ViewDirectionPanel({ resolved, origin }: ViewDirectionPanelProps) {
  const view = resolveView(resolved.view).view;
  const originLabel = VIEW_ORIGIN_LABELS_KO[origin];

  return (
    <div className="ce-card">
      <h3 className="ce-card-title">시점 &amp; 방향</h3>
      <p className="ce-card-subtitle">
        이 액터는 {originLabel}에서 {view.facing} 방향을 상속합니다. 디자인 마스터도 같은 방향으로
        생성해야 합니다 — PerfectPixel은 마스터 방향을 그대로 보존하므로, 마스터가 반대로 그려지면
        애니메이션 전체가 반대 방향이 됩니다.
      </p>

      <div className="ce-form-grid" style={{ marginTop: "var(--ce-space-3)" }}>
        <FieldRow label="유효 시점" origin="locked" hint="World Template에서 상속">
          <input type="text" value={view.projection} readOnly disabled />
        </FieldRow>
        <FieldRow label="유효 화면 방향" origin="locked" hint="World Template에서 상속">
          <input type="text" value={view.facing} readOnly disabled />
        </FieldRow>
        <FieldRow label="유효 광원 방향" origin="locked" hint="World Template에서 상속">
          <input type="text" value={view.lightDirection} readOnly disabled />
        </FieldRow>
        <FieldRow
          label="출처"
          origin="locked"
          hint={
            origin === "default"
              ? "World Template에 방향 값이 없어 프로젝트 기본값을 사용했습니다. World Template에 명시하세요."
              : undefined
          }
        >
          <input type="text" value={originLabel} readOnly disabled />
        </FieldRow>
      </div>

      <FieldRow label="Master image direction" origin="locked" hint="이미지 생성 프롬프트에 그대로 사용하세요.">
        <pre className="ce-code-block">{masterImageDirectionPrompt(view)}</pre>
      </FieldRow>
    </div>
  );
}
