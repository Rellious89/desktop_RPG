import type { ActorDocument, InheritableSpec, WorldTemplate } from "../../app/types";
import { resetAnatomyOverride, setAnatomyOverride, setPhysicalTraits } from "../../app/actorDraft";
import { anatomyOrigin } from "../../app/fieldOrigin";
import { FieldRow } from "../common/FieldRow";
import { TagListEditor } from "../common/TagListEditor";

type Anatomy = InheritableSpec["anatomy"];

const STATURE_OPTIONS: Anatomy["stature"][] = ["tiny", "short", "average", "tall", "very-tall"];
const BUILD_OPTIONS: Anatomy["build"][] = ["slender", "normal", "broad", "muscular", "massive", "non-humanoid"];
const SIZE_OPTIONS: Anatomy["headSize"][] = ["xs", "s", "m", "l", "xl"];
const TORSO_OPTIONS: Anatomy["torsoWidth"][] = ["narrow", "normal", "broad", "very-broad"];

export interface BodySectionProps {
  actor: ActorDocument;
  world: WorldTemplate;
  resolved: InheritableSpec;
  onChangeActor: (updater: (actor: ActorDocument) => ActorDocument) => void;
}

export function BodySection({ actor, world, resolved, onChangeActor }: BodySectionProps) {
  const anatomy = resolved.anatomy;
  const worldDefaults = world.defaults.anatomy;

  const impliedScale =
    worldDefaults.targetLogicalHeightPx > 0 ? anatomy.targetLogicalHeightPx / worldDefaults.targetLogicalHeightPx : null;
  const scaleDeltaPercent =
    impliedScale !== null && anatomy.speciesScale > 0
      ? Math.round(((impliedScale - anatomy.speciesScale) / anatomy.speciesScale) * 1000) / 10
      : null;

  return (
    <div className="ce-card">
      <h3 className="ce-card-title">Body &amp; Proportions</h3>
      <p className="ce-card-subtitle">
        Stature(신장 목표치)와 Species Scale(종족 배율)을 분리해서 관리합니다. 두 값이 설명 없이
        어긋나면 저장소에서 실제로 반복됐던 "scale 과부하" 문제가 재발합니다.
      </p>

      <div className="ce-form-grid" style={{ marginTop: "var(--ce-space-3)" }}>
        <FieldRow
          label="Stature 등급"
          required
          termKey="stature"
          origin={anatomyOrigin(actor, "stature")}
          baselineLabel={`World 기본값: ${worldDefaults.stature}`}
          onReset={() => onChangeActor((current) => resetAnatomyOverride(current, "stature"))}
        >
          <select
            value={anatomy.stature}
            onChange={(event) =>
              onChangeActor((current) => setAnatomyOverride(current, "stature", event.target.value as Anatomy["stature"]))
            }
          >
            {STATURE_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </FieldRow>

        <FieldRow
          label="목표 논리 높이(px)"
          required
          termKey="targetLogicalHeightPx"
          origin={anatomyOrigin(actor, "targetLogicalHeightPx")}
          baselineLabel={`World 기본값: ${worldDefaults.targetLogicalHeightPx}px (권장 65~75px)`}
          onReset={() => onChangeActor((current) => resetAnatomyOverride(current, "targetLogicalHeightPx"))}
          hint={
            anatomy.targetLogicalHeightPx < 65 || anatomy.targetLogicalHeightPx > 75
              ? "권장 범위(65~75px)를 벗어났습니다 — Validation 패널에서 승인된 예외를 등록하세요."
              : undefined
          }
        >
          <input
            type="number"
            value={anatomy.targetLogicalHeightPx}
            onChange={(event) =>
              onChangeActor((current) =>
                setAnatomyOverride(current, "targetLogicalHeightPx", Number(event.target.value)),
              )
            }
          />
        </FieldRow>

        <FieldRow
          label="Build 등급"
          required
          termKey="buildClass"
          origin={anatomyOrigin(actor, "build")}
          baselineLabel={`World 기본값: ${worldDefaults.build}`}
          onReset={() => onChangeActor((current) => resetAnatomyOverride(current, "build"))}
        >
          <select
            value={anatomy.build}
            onChange={(event) =>
              onChangeActor((current) => setAnatomyOverride(current, "build", event.target.value as Anatomy["build"]))
            }
          >
            {BUILD_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </FieldRow>

        <FieldRow
          label="비율 템플릿"
          required
          termKey="proportionTemplateId"
          origin={anatomyOrigin(actor, "proportionTemplateId")}
          baselineLabel={`World 기본값: ${worldDefaults.proportionTemplateId}`}
          onReset={() => onChangeActor((current) => resetAnatomyOverride(current, "proportionTemplateId"))}
        >
          <select
            value={anatomy.proportionTemplateId}
            onChange={(event) =>
              onChangeActor((current) => setAnatomyOverride(current, "proportionTemplateId", event.target.value))
            }
          >
            {world.allowedProportionTemplates.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </FieldRow>

        <FieldRow
          label="Species Scale"
          required
          termKey="speciesScale"
          origin={anatomyOrigin(actor, "speciesScale")}
          baselineLabel={`World 기본값: ${worldDefaults.speciesScale}`}
          onReset={() => onChangeActor((current) => resetAnatomyOverride(current, "speciesScale"))}
          hint={
            scaleDeltaPercent !== null && Math.abs(scaleDeltaPercent) > 10
              ? `참고: 목표 높이로 역산한 배율은 ${impliedScale?.toFixed(2)}이며 Species Scale과 ${scaleDeltaPercent}% 차이가 납니다. 저장 시 자동 검증됩니다.`
              : impliedScale !== null
                ? `참고: 목표 높이로 역산한 배율 ${impliedScale.toFixed(2)} — Species Scale과 정합적입니다.`
                : undefined
          }
        >
          <input
            type="number"
            step="0.05"
            min="0.05"
            value={anatomy.speciesScale}
            onChange={(event) =>
              onChangeActor((current) => setAnatomyOverride(current, "speciesScale", Number(event.target.value)))
            }
          />
        </FieldRow>

        <FieldRow
          label="몸통 너비 등급"
          required
          termKey="torsoWidth"
          origin={anatomyOrigin(actor, "torsoWidth")}
          baselineLabel={`World 기본값: ${worldDefaults.torsoWidth}`}
          onReset={() => onChangeActor((current) => resetAnatomyOverride(current, "torsoWidth"))}
        >
          <select
            value={anatomy.torsoWidth}
            onChange={(event) =>
              onChangeActor((current) =>
                setAnatomyOverride(current, "torsoWidth", event.target.value as Anatomy["torsoWidth"]),
              )
            }
          >
            {TORSO_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </FieldRow>

        <FieldRow
          label="머리 크기 등급"
          required
          termKey="headSize"
          origin={anatomyOrigin(actor, "headSize")}
          baselineLabel={`World 기본값: ${worldDefaults.headSize}`}
          onReset={() => onChangeActor((current) => resetAnatomyOverride(current, "headSize"))}
        >
          <select
            value={anatomy.headSize}
            onChange={(event) =>
              onChangeActor((current) =>
                setAnatomyOverride(current, "headSize", event.target.value as Anatomy["headSize"]),
              )
            }
          >
            {SIZE_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </FieldRow>

        <FieldRow
          label="손 크기 등급"
          required
          termKey="handSize"
          origin={anatomyOrigin(actor, "handSize")}
          baselineLabel={`World 기본값: ${worldDefaults.handSize}`}
          onReset={() => onChangeActor((current) => resetAnatomyOverride(current, "handSize"))}
        >
          <select
            value={anatomy.handSize}
            onChange={(event) =>
              onChangeActor((current) =>
                setAnatomyOverride(current, "handSize", event.target.value as Anatomy["handSize"]),
              )
            }
          >
            {SIZE_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </FieldRow>

        <FieldRow
          label="발 크기 등급"
          required
          termKey="footSize"
          origin={anatomyOrigin(actor, "footSize")}
          baselineLabel={`World 기본값: ${worldDefaults.footSize}`}
          onReset={() => onChangeActor((current) => resetAnatomyOverride(current, "footSize"))}
        >
          <select
            value={anatomy.footSize}
            onChange={(event) =>
              onChangeActor((current) =>
                setAnatomyOverride(current, "footSize", event.target.value as Anatomy["footSize"]),
              )
            }
          >
            {SIZE_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </FieldRow>

        <FieldRow
          label="부유형 액터"
          termKey="pivotRule"
          origin={anatomyOrigin(actor, "isFloatingActor")}
          baselineLabel={`World 기본값: ${worldDefaults.isFloatingActor ? "예" : "아니오"}`}
          onReset={() => onChangeActor((current) => resetAnatomyOverride(current, "isFloatingActor"))}
          hint="발 접지가 없는 액터(예: Specter류)는 켜고, Production 섹션에서 Pivot 규칙을 ground-projection으로 맞추세요."
        >
          <label className="ce-row" style={{ fontWeight: 400 }}>
            <input
              type="checkbox"
              checked={anatomy.isFloatingActor}
              onChange={(event) =>
                onChangeActor((current) => setAnatomyOverride(current, "isFloatingActor", event.target.checked))
              }
            />
            발 접지 없이 부유함
          </label>
        </FieldRow>
      </div>

      <FieldRow label="신체 특징 태그" htmlFor="actor-physical-traits" hint="예: 뾰족귀, 문신, 흉터">
        <TagListEditor
          id="actor-physical-traits"
          values={actor.physicalTraits}
          onChange={(values) => onChangeActor((current) => setPhysicalTraits(current, values))}
          placeholder="특징 입력 후 Enter"
        />
      </FieldRow>
    </div>
  );
}
