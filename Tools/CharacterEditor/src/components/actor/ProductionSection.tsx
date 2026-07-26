import type { ActorDocument, InheritableSpec, WorldTemplate } from "../../app/types";
import { resetProductionOverride, setProductionOverride } from "../../app/actorDraft";
import { productionOrigin } from "../../app/fieldOrigin";
import { viewOriginOf } from "../../app/view";
import { FieldRow } from "../common/FieldRow";
import { ViewDirectionPanel } from "../common/ViewDirectionPanel";

type Production = InheritableSpec["production"];

const LARGE_MOTION_POLICY_OPTIONS: Production["largeMotionCanvas"]["policy"][] = ["same-as-base", "explicit"];
const PIVOT_RULE_OPTIONS: Production["pivotRule"][] = [
  "forward-foot-contact",
  "ground-projection",
  "actor-origin-custom",
];

export interface ProductionSectionProps {
  actor: ActorDocument;
  world: WorldTemplate;
  resolved: InheritableSpec;
  onChangeActor: (updater: (actor: ActorDocument) => ActorDocument) => void;
}

export function ProductionSection({ actor, world, resolved, onChangeActor }: ProductionSectionProps) {
  const production = resolved.production;
  const worldDefaults = world.defaults.production;
  const isExplicitLargeMotion = production.largeMotionCanvas.policy === "explicit";

  return (
    <div className="ce-stack">
      <ViewDirectionPanel resolved={resolved} origin={viewOriginOf(actor, world)} />

    <div className="ce-card">
      <h3 className="ce-card-title">Production &amp; Canvas</h3>
      <p className="ce-card-subtitle">
        캔버스/PPU/Unity 배율은 대부분 프로젝트 전역 정책입니다. 대부분의 액터는 이 섹션을 건드릴 필요가
        없습니다.
      </p>

      <div className="ce-form-grid" style={{ marginTop: "var(--ce-space-3)" }}>
        <FieldRow label="기본 캔버스" origin="locked" hint="프로젝트 전역 상수">
          <input type="text" value={`${production.baseCanvas.widthPx}×${production.baseCanvas.heightPx}`} disabled />
        </FieldRow>

        <FieldRow
          label="대형 모션 캔버스 정책"
          termKey="largeMotionCanvas"
          origin={productionOrigin(actor, "largeMotionCanvas")}
          baselineLabel={`World 기본값: ${worldDefaults.largeMotionCanvas.policy}`}
          onReset={() => onChangeActor((current) => resetProductionOverride(current, "largeMotionCanvas"))}
          hint={
            isExplicitLargeMotion
              ? "실험적 설정입니다 — 아직 공통 규칙으로 확정되지 않았으며 기존 리소스와 혼용하지 마세요."
              : undefined
          }
        >
          <select
            value={production.largeMotionCanvas.policy}
            onChange={(event) =>
              onChangeActor((current) =>
                setProductionOverride(current, "largeMotionCanvas", {
                  policy: event.target.value as Production["largeMotionCanvas"]["policy"],
                  widthPx: event.target.value === "explicit" ? (production.largeMotionCanvas.widthPx ?? 1024) : undefined,
                  heightPx: event.target.value === "explicit" ? (production.largeMotionCanvas.heightPx ?? 1024) : undefined,
                }),
              )
            }
          >
            {LARGE_MOTION_POLICY_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </FieldRow>

        {isExplicitLargeMotion && (
          <FieldRow label="대형 모션 캔버스 크기 (px)" required>
            <div className="ce-row">
              <input
                type="number"
                aria-label="대형 모션 캔버스 너비"
                value={production.largeMotionCanvas.widthPx ?? 0}
                onChange={(event) =>
                  onChangeActor((current) =>
                    setProductionOverride(current, "largeMotionCanvas", {
                      ...production.largeMotionCanvas,
                      widthPx: Number(event.target.value),
                    }),
                  )
                }
              />
              <input
                type="number"
                aria-label="대형 모션 캔버스 높이"
                value={production.largeMotionCanvas.heightPx ?? 0}
                onChange={(event) =>
                  onChangeActor((current) =>
                    setProductionOverride(current, "largeMotionCanvas", {
                      ...production.largeMotionCanvas,
                      heightPx: Number(event.target.value),
                    }),
                  )
                }
              />
            </div>
          </FieldRow>
        )}

        <FieldRow
          label="Pivot 규칙"
          required
          termKey="pivotRule"
          origin={productionOrigin(actor, "pivotRule")}
          baselineLabel={`World 기본값: ${worldDefaults.pivotRule}`}
          onReset={() => onChangeActor((current) => resetProductionOverride(current, "pivotRule"))}
          hint="부유형(isFloatingActor, Body 섹션) 액터는 ground-projection 또는 actor-origin-custom을 선택하세요."
        >
          <select
            value={production.pivotRule}
            onChange={(event) =>
              onChangeActor((current) =>
                setProductionOverride(current, "pivotRule", event.target.value as Production["pivotRule"]),
              )
            }
          >
            {PIVOT_RULE_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </FieldRow>

        <FieldRow label="PPU" origin="locked" termKey="ppu" hint="프로젝트 전역 상수 (200)">
          <input type="number" value={production.pixelsPerUnit} disabled />
        </FieldRow>

        <FieldRow
          label="Unity 표시 배율"
          required
          termKey="unityVisualScale"
          origin={productionOrigin(actor, "unityVisualScale")}
          baselineLabel={`World 기본값: ${worldDefaults.unityVisualScale}`}
          onReset={() => onChangeActor((current) => resetProductionOverride(current, "unityVisualScale"))}
          hint={
            production.unityVisualScale !== 1
              ? "1.0이 아닌 값은 체격 보정 용도로 정책상 금지됩니다 — Validation 패널에서 승인된 예외가 필요합니다."
              : undefined
          }
        >
          <input
            type="number"
            step="0.05"
            value={production.unityVisualScale}
            onChange={(event) =>
              onChangeActor((current) =>
                setProductionOverride(current, "unityVisualScale", Number(event.target.value)),
              )
            }
          />
        </FieldRow>
      </div>

      <FieldRow
        label="정규화 Pivot (선택)"
        origin={productionOrigin(actor, "pivot")}
        onReset={() => onChangeActor((current) => resetProductionOverride(current, "pivot"))}
        hint="비워두면 기본 규칙을 그대로 따릅니다. 실측했다면 measured, 계획값이면 planned를 선택하세요."
      >
        <div className="ce-row">
          <input
            type="number"
            step="0.001"
            min="0"
            max="1"
            aria-label="Pivot X"
            value={production.pivot?.xNormalized ?? ""}
            onChange={(event) => {
              const xNormalized = event.target.value === "" ? undefined : Number(event.target.value);
              if (xNormalized === undefined) return;
              onChangeActor((current) =>
                setProductionOverride(current, "pivot", {
                  xNormalized,
                  yNormalized: production.pivot?.yNormalized ?? 0,
                  source: production.pivot?.source ?? "planned",
                }),
              );
            }}
          />
          <input
            type="number"
            step="0.001"
            min="0"
            max="1"
            aria-label="Pivot Y"
            value={production.pivot?.yNormalized ?? ""}
            onChange={(event) => {
              const yNormalized = event.target.value === "" ? undefined : Number(event.target.value);
              if (yNormalized === undefined) return;
              onChangeActor((current) =>
                setProductionOverride(current, "pivot", {
                  xNormalized: production.pivot?.xNormalized ?? 0.5,
                  yNormalized,
                  source: production.pivot?.source ?? "planned",
                }),
              );
            }}
          />
          <select
            aria-label="Pivot 출처"
            value={production.pivot?.source ?? "planned"}
            onChange={(event) =>
              onChangeActor((current) =>
                setProductionOverride(current, "pivot", {
                  xNormalized: production.pivot?.xNormalized ?? 0.5,
                  yNormalized: production.pivot?.yNormalized ?? 0,
                  source: event.target.value as "measured" | "planned",
                }),
              )
            }
          >
            <option value="planned">planned</option>
            <option value="measured">measured</option>
          </select>
        </div>
      </FieldRow>

      <FieldRow
        label="생산 레이어 정책"
        origin="locked"
        termKey="productionLayers"
        hint="계획 메타데이터입니다. 현재 파이프라인은 프레임당 단일 PNG이며, 실제 레이어 파일 존재를 강제하지 않습니다."
      >
        <p className="ce-field-hint" style={{ margin: 0 }}>
          {production.layers.join(" → ")}
        </p>
      </FieldRow>
    </div>
    </div>
  );
}
