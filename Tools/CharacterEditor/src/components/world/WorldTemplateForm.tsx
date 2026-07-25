import { useState } from "react";
import type { InheritableSpec, WorldTemplate } from "../../app/types";
import { FieldRow } from "../common/FieldRow";
import { TagListEditor } from "../common/TagListEditor";

type Anatomy = InheritableSpec["anatomy"];
type ViewSpec = InheritableSpec["view"];
type Production = InheritableSpec["production"];

const STATUS_OPTIONS: WorldTemplate["status"][] = ["concept", "active", "hold"];
const PROJECTION_OPTIONS: ViewSpec["projection"][] = ["side", "three-quarter", "front"];
const FACING_OPTIONS: ViewSpec["facing"][] = ["screen-right", "screen-left"];
const STATURE_OPTIONS: Anatomy["stature"][] = ["tiny", "short", "average", "tall", "very-tall"];
const BUILD_OPTIONS: Anatomy["build"][] = ["slender", "normal", "broad", "muscular", "massive", "non-humanoid"];
const SIZE_OPTIONS: Anatomy["headSize"][] = ["xs", "s", "m", "l", "xl"];
const TORSO_OPTIONS: Anatomy["torsoWidth"][] = ["narrow", "normal", "broad", "very-broad"];
const LARGE_MOTION_POLICY_OPTIONS: Production["largeMotionCanvas"]["policy"][] = ["same-as-base", "explicit"];
const PIVOT_RULE_OPTIONS: Production["pivotRule"][] = [
  "forward-foot-contact",
  "ground-projection",
  "actor-origin-custom",
];

export interface WorldTemplateFormProps {
  mode: "create" | "edit";
  initialWorld: WorldTemplate;
  sourceNote?: string;
  validationErrors?: string[];
  onSave: (world: WorldTemplate) => void;
  onDownload: (world: WorldTemplate) => void;
  onCancel: () => void;
}

export function WorldTemplateForm({
  mode,
  initialWorld,
  sourceNote,
  validationErrors,
  onSave,
  onDownload,
  onCancel,
}: WorldTemplateFormProps) {
  const [world, setWorld] = useState<WorldTemplate>(initialWorld);

  function patch(partial: Partial<WorldTemplate>) {
    setWorld((current) => ({ ...current, ...partial }));
  }

  function patchAnatomy(partial: Partial<Anatomy>) {
    setWorld((current) => ({
      ...current,
      defaults: { ...current.defaults, anatomy: { ...current.defaults.anatomy, ...partial } },
    }));
  }

  function patchView(partial: Partial<ViewSpec>) {
    setWorld((current) => ({
      ...current,
      defaults: { ...current.defaults, view: { ...current.defaults.view, ...partial } },
    }));
  }

  function patchPixelStyle(partial: Partial<InheritableSpec["pixelStyle"]>) {
    setWorld((current) => ({
      ...current,
      defaults: { ...current.defaults, pixelStyle: { ...current.defaults.pixelStyle, ...partial } },
    }));
  }

  const anatomy = world.defaults.anatomy;
  const view = world.defaults.view;
  const pixelStyle = world.defaults.pixelStyle;
  const production = world.defaults.production;

  return (
    <div className="ce-stack">
      <div className="ce-card">
        <div className="ce-card-header">
          <div>
            <h2 className="ce-card-title">
              {mode === "create" ? "새 World 템플릿" : `World 템플릿 편집 — rev.${world.revision}`}
            </h2>
            {sourceNote && <p className="ce-card-subtitle">{sourceNote}</p>}
          </div>
          <div className="ce-btn-group">
            <button type="button" className="ce-btn ce-btn--secondary" onClick={onCancel}>
              취소
            </button>
            <button type="button" className="ce-btn ce-btn--secondary" onClick={() => onDownload(world)}>
              JSON 다운로드
            </button>
            <button type="button" className="ce-btn ce-btn--primary" onClick={() => onSave(world)}>
              저장 (rev.{world.revision})
            </button>
          </div>
        </div>

        {validationErrors && validationErrors.length > 0 && (
          <div className="ce-validation-item ce-validation-item--error">
            <strong>저장하기 전에 다음을 확인하세요</strong>
            <ul>
              {validationErrors.map((error, index) => (
                <li key={index} className="ce-validation-message">
                  {error}
                </li>
              ))}
            </ul>
          </div>
        )}

        <fieldset className="ce-fieldset" style={{ marginTop: "var(--ce-space-4)" }}>
          <legend>기본 정보</legend>
          <div className="ce-form-grid">
            <FieldRow label="World ID" required htmlFor="world-id">
              <input
                id="world-id"
                type="text"
                value={world.worldId}
                disabled={mode === "edit"}
                placeholder="예: HUMAN-FANTASY-01"
                onChange={(event) => patch({ worldId: event.target.value })}
              />
            </FieldRow>
            <FieldRow label="표시 이름 (한국어)" htmlFor="world-name-ko">
              <input
                id="world-name-ko"
                type="text"
                value={world.displayName.ko ?? ""}
                onChange={(event) => patch({ displayName: { ...world.displayName, ko: event.target.value } })}
              />
            </FieldRow>
            <FieldRow label="표시 이름 (영어)" required htmlFor="world-name-en">
              <input
                id="world-name-en"
                type="text"
                value={world.displayName.en}
                onChange={(event) => patch({ displayName: { ...world.displayName, en: event.target.value } })}
              />
            </FieldRow>
            <FieldRow label="상태" required htmlFor="world-status">
              <select
                id="world-status"
                value={world.status}
                onChange={(event) => patch({ status: event.target.value as WorldTemplate["status"] })}
              >
                {STATUS_OPTIONS.map((status) => (
                  <option key={status} value={status}>
                    {status}
                  </option>
                ))}
              </select>
            </FieldRow>
          </div>
          <FieldRow label="한 문장 설명" required htmlFor="world-description">
            <textarea
              id="world-description"
              value={world.description}
              onChange={(event) => patch({ description: event.target.value })}
            />
          </FieldRow>
        </fieldset>

        <fieldset className="ce-fieldset ce-panel-section">
          <legend>승인 시점 &amp; 픽셀 스타일</legend>
          <div className="ce-form-grid">
            <FieldRow label="승인 시점(투영)" required htmlFor="world-view-projection">
              <select
                id="world-view-projection"
                value={view.projection}
                onChange={(event) => patchView({ projection: event.target.value as ViewSpec["projection"] })}
              >
                {PROJECTION_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </FieldRow>
            <FieldRow label="기본 화면 진행 방향" required htmlFor="world-facing">
              <select
                id="world-facing"
                value={view.facing}
                onChange={(event) => patchView({ facing: event.target.value as ViewSpec["facing"] })}
              >
                {FACING_OPTIONS.map((facing) => (
                  <option key={facing} value={facing}>
                    {facing}
                  </option>
                ))}
              </select>
            </FieldRow>
            <FieldRow label="픽셀 스타일 ID" required htmlFor="world-pixel-style-id">
              <input
                id="world-pixel-style-id"
                type="text"
                value={pixelStyle.styleId}
                onChange={(event) => patchPixelStyle({ styleId: event.target.value })}
              />
            </FieldRow>
            <FieldRow label="논리 픽셀 블록 크기(px)" required>
              <div className="ce-row">
                <input
                  type="number"
                  aria-label="블록 너비"
                  value={pixelStyle.logicalBlockPx.widthPx}
                  onChange={(event) =>
                    patchPixelStyle({
                      logicalBlockPx: { ...pixelStyle.logicalBlockPx, widthPx: Number(event.target.value) },
                    })
                  }
                />
                <input
                  type="number"
                  aria-label="블록 높이"
                  value={pixelStyle.logicalBlockPx.heightPx}
                  onChange={(event) =>
                    patchPixelStyle({
                      logicalBlockPx: { ...pixelStyle.logicalBlockPx, heightPx: Number(event.target.value) },
                    })
                  }
                />
              </div>
            </FieldRow>
            <FieldRow label="외곽선 규칙" required htmlFor="world-outline">
              <input
                id="world-outline"
                type="text"
                value={pixelStyle.outline}
                onChange={(event) => patchPixelStyle({ outline: event.target.value })}
              />
            </FieldRow>
            <FieldRow label="광원 방향" required htmlFor="world-light">
              <input
                id="world-light"
                type="text"
                value={pixelStyle.lighting}
                onChange={(event) => patchPixelStyle({ lighting: event.target.value })}
              />
            </FieldRow>
          </div>
        </fieldset>

        <fieldset className="ce-fieldset ce-panel-section">
          <legend>기본 체형/비율</legend>
          <p className="ce-card-subtitle">
            여기서 정한 값이 모든 신규 액터의 상속 기본값이 됩니다. 액터가 오버라이드하지 않는 한
            이 값을 그대로 물려받습니다.
          </p>
          <div className="ce-form-grid" style={{ marginTop: "var(--ce-space-3)" }}>
            <FieldRow label="기본 Stature 등급" required termKey="stature" htmlFor="world-stature">
              <select
                id="world-stature"
                value={anatomy.stature}
                onChange={(event) => patchAnatomy({ stature: event.target.value as Anatomy["stature"] })}
              >
                {STATURE_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </FieldRow>
            <FieldRow
              label="기본 논리 높이(px)"
              required
              termKey="targetLogicalHeightPx"
              htmlFor="world-height"
              hint="권장 범위 65~75px"
            >
              <input
                id="world-height"
                type="number"
                value={anatomy.targetLogicalHeightPx}
                onChange={(event) => patchAnatomy({ targetLogicalHeightPx: Number(event.target.value) })}
              />
            </FieldRow>
            <FieldRow label="기본 Build 등급" required termKey="buildClass" htmlFor="world-build">
              <select
                id="world-build"
                value={anatomy.build}
                onChange={(event) => patchAnatomy({ build: event.target.value as Anatomy["build"] })}
              >
                {BUILD_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </FieldRow>
            <FieldRow label="기본 비율 템플릿" required termKey="proportionTemplateId" htmlFor="world-proportion">
              <input
                id="world-proportion"
                type="text"
                value={anatomy.proportionTemplateId}
                onChange={(event) => patchAnatomy({ proportionTemplateId: event.target.value })}
              />
            </FieldRow>
            <FieldRow label="기본 Species Scale" required termKey="speciesScale" htmlFor="world-species-scale">
              <input
                id="world-species-scale"
                type="number"
                step="0.05"
                min="0.05"
                value={anatomy.speciesScale}
                onChange={(event) => patchAnatomy({ speciesScale: Number(event.target.value) })}
              />
            </FieldRow>
            <FieldRow label="기본 몸통 너비 등급" required termKey="torsoWidth" htmlFor="world-torso">
              <select
                id="world-torso"
                value={anatomy.torsoWidth}
                onChange={(event) => patchAnatomy({ torsoWidth: event.target.value as Anatomy["torsoWidth"] })}
              >
                {TORSO_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </FieldRow>
            <FieldRow label="기본 머리 크기 등급" required htmlFor="world-head-size">
              <select
                id="world-head-size"
                value={anatomy.headSize}
                onChange={(event) => patchAnatomy({ headSize: event.target.value as Anatomy["headSize"] })}
              >
                {SIZE_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </FieldRow>
            <FieldRow label="기본 손 크기 등급" required htmlFor="world-hand-size">
              <select
                id="world-hand-size"
                value={anatomy.handSize}
                onChange={(event) => patchAnatomy({ handSize: event.target.value as Anatomy["handSize"] })}
              >
                {SIZE_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </FieldRow>
            <FieldRow label="기본 발 크기 등급" required htmlFor="world-foot-size">
              <select
                id="world-foot-size"
                value={anatomy.footSize}
                onChange={(event) => patchAnatomy({ footSize: event.target.value as Anatomy["footSize"] })}
              >
                {SIZE_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </FieldRow>
          </div>
          <FieldRow label="허용 종족 목록" required htmlFor="world-allowed-species">
            <TagListEditor
              id="world-allowed-species"
              values={world.allowedSpecies}
              onChange={(values) => patch({ allowedSpecies: values })}
              placeholder="종족 입력 후 Enter"
            />
          </FieldRow>
          <FieldRow label="허용된 비율 템플릿 목록" htmlFor="world-allowed-proportions">
            <TagListEditor
              id="world-allowed-proportions"
              values={world.allowedProportionTemplates}
              onChange={(values) => patch({ allowedProportionTemplates: values })}
              placeholder="템플릿 ID 입력 후 Enter"
            />
          </FieldRow>
        </fieldset>

        <fieldset className="ce-fieldset ce-panel-section">
          <legend>캔버스 &amp; 전역 정책</legend>
          <div className="ce-form-grid">
            <FieldRow label="기본 캔버스" origin="locked" hint="프로젝트 전역 상수 (512×512)">
              <input type="text" value={`${production.baseCanvas.widthPx}×${production.baseCanvas.heightPx}`} disabled />
            </FieldRow>
            <FieldRow
              label="대형 모션 캔버스 정책"
              termKey="largeMotionCanvas"
              htmlFor="world-large-motion"
              hint={
                production.largeMotionCanvas.policy !== "same-as-base"
                  ? "실험적 설정 — 아직 공통 규칙으로 확정되지 않았습니다."
                  : undefined
              }
            >
              <select
                id="world-large-motion"
                value={production.largeMotionCanvas.policy}
                onChange={(event) =>
                  setWorld((current) => ({
                    ...current,
                    defaults: {
                      ...current.defaults,
                      production: {
                        ...current.defaults.production,
                        largeMotionCanvas: { policy: event.target.value as Production["largeMotionCanvas"]["policy"] },
                      },
                    },
                  }))
                }
              >
                {LARGE_MOTION_POLICY_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </FieldRow>
            <FieldRow label="기본 Pivot 규칙" required termKey="pivotRule" htmlFor="world-pivot-rule">
              <select
                id="world-pivot-rule"
                value={production.pivotRule}
                onChange={(event) =>
                  setWorld((current) => ({
                    ...current,
                    defaults: {
                      ...current.defaults,
                      production: { ...current.defaults.production, pivotRule: event.target.value as Production["pivotRule"] },
                    },
                  }))
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
            <FieldRow label="Unity 표시 배율 기본값" origin="locked" termKey="unityVisualScale" hint="정책상 1.0 고정">
              <input type="number" value={production.unityVisualScale} disabled />
            </FieldRow>
          </div>
        </fieldset>

        <fieldset className="ce-fieldset ce-panel-section">
          <legend>생산 레이어 정책</legend>
          <FieldRow
            label="레이어 순서"
            origin="locked"
            termKey="productionLayers"
            hint="계획 메타데이터입니다. 현재 파이프라인은 프레임당 단일 PNG이며, 실제 레이어 파일 존재를 강제하지 않습니다."
          >
            <p className="ce-field-hint" style={{ margin: 0 }}>
              {production.layers.join(" → ")}
            </p>
          </FieldRow>
        </fieldset>
      </div>
    </div>
  );
}
