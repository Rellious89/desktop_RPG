import type { ActorDocument, InheritableSpec, WorldTemplate } from "../../app/types";
import {
  resetAnatomyOverride, resetBodySizeOverride, resetDensityOverride, setAnatomyOverride,
  setBodyScale, setPhysicalTraits,
} from "../../app/actorDraft";
import { anatomyOrigin, bodyHeightOrigin, densityOrigin } from "../../app/fieldOrigin";
import {
  DENSITY_OPTIONS, DENSITY_PRESETS, displaySpeciesScale, logicalHeightAt, physicalHeightFromScale,
  resolveScale, resolveSize, sizeRelationText, speciesScaleFromHeight, worldBasePhysicalHeightPx,
} from "../../app/scale";
import { CommittedNumberInput } from "../common/CommittedNumberInput";
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
  // Physical height is the authored size; logical height is what it becomes on
  // this actor's pixel grid. Compare sizes in physical px so the ratio does not
  // move when an actor picks a different density than its world.
  const scale = resolveScale(anatomy, resolved.pixelStyle);
  const worldScale = resolveScale(world.defaults.anatomy, world.defaults.pixelStyle);

  // Species scale and physical height describe one size. Whichever the user
  // edits is written together with the other, so the pair can never drift apart.
  const worldBasePx = worldBasePhysicalHeightPx(world.defaults);
  const size = resolveSize(anatomy, scale.blockPx, worldBasePx);

  /** Commits a new body size from either direction, keeping the density fixed —
   * density changes detail, never size. */
  const commitSize = (targetPhysicalHeightPx: number, speciesScale: number, sizeAuthority: "species-scale" | "physical-height") =>
    onChangeActor((current) =>
      setBodyScale(current, {
        targetPhysicalHeightPx,
        targetLogicalHeightPx: logicalHeightAt(targetPhysicalHeightPx, scale.blockPx),
        speciesScale,
        sizeAuthority,
      }),
    );

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
          label="목표 물리 높이(px)"
          required
          termKey="targetPhysicalHeightPx"
          origin={bodyHeightOrigin(actor)}
          baselineLabel={`World 기본값: ${worldScale.targetPhysicalHeightPx}px (${worldScale.targetLogicalHeightPx} logical @ ${worldScale.blockPx}×${worldScale.blockPx})`}
          onReset={() => onChangeActor((current) => resetBodySizeOverride(current))}
          hint={
            "게임에서 캐릭터 신체가 차지할 실제 픽셀 높이입니다. 값을 수정하면 Species Scale이 자동 역산됩니다." +
            (scale.roundingResidualPx !== 0
              ? ` ${scale.blockPx}px 블록의 배수가 아니라 논리 높이가 ${scale.targetLogicalHeightPx}로 반올림됩니다 — 논리 높이만 읽는 도구는 ${scale.effectivePhysicalHeightPx}px로 해석합니다.`
              : "")
          }
        >
          <CommittedNumberInput
            aria-label="목표 물리 높이(px)"
            min={1}
            value={size.targetPhysicalHeightPx}
            onCommit={(targetPhysicalHeightPx) =>
              commitSize(
                Math.round(targetPhysicalHeightPx),
                // Full precision: the displayed 3-decimal value is never what
                // gets stored, so re-editing cannot walk the height off by a px.
                speciesScaleFromHeight(worldBasePx, Math.round(targetPhysicalHeightPx)),
                "physical-height",
              )
            }
          />
        </FieldRow>

        <FieldRow label="크기 관계 — 자동 계산" termKey="speciesScale">
          <output className="ce-derived-value">{sizeRelationText(size)}</output>
        </FieldRow>

        <FieldRow
          label="픽셀 밀도"
          required
          termKey="densityPreset"
          origin={densityOrigin(actor)}
          baselineLabel={`World 기본값: ${worldScale.blockPx}×${worldScale.blockPx}`}
          onReset={() =>
            onChangeActor((current) =>
              resetDensityOverride(current, logicalHeightAt(scale.targetPhysicalHeightPx, worldScale.blockPx)),
            )
          }
          hint={`이 밀도에서 목표 논리 높이는 ${scale.targetLogicalHeightPx}px입니다. 밀도를 바꿔도 물리 높이 ${scale.targetPhysicalHeightPx}px는 유지됩니다.`}
        >
          <select
            aria-label="픽셀 밀도"
            value={scale.densityPreset}
            onChange={(event) => {
              const preset = event.target.value as keyof typeof DENSITY_PRESETS;
              const blockPx = DENSITY_PRESETS[preset];
              onChangeActor((current) =>
                setBodyScale(current, {
                  targetPhysicalHeightPx: scale.targetPhysicalHeightPx,
                  targetLogicalHeightPx: logicalHeightAt(scale.targetPhysicalHeightPx, blockPx),
                  densityPreset: preset,
                  blockPx,
                }),
              );
            }}
          >
            {DENSITY_OPTIONS.map((option) => (
              <option key={option.id} value={option.id}>
                {option.label} ({option.note}) → {logicalHeightAt(scale.targetPhysicalHeightPx, DENSITY_PRESETS[option.id])} logical px
              </option>
            ))}
            {scale.densityPreset === "custom" && (
              <option value="custom">
                custom {resolved.pixelStyle.logicalBlockPx.widthPx}×{resolved.pixelStyle.logicalBlockPx.heightPx}
              </option>
            )}
          </select>
        </FieldRow>

        <FieldRow
          label="목표 논리 높이(px) — 자동 계산"
          termKey="targetLogicalHeightPx"
          hint={
            scale.targetLogicalHeightPx < 65 || scale.targetLogicalHeightPx > 75
              ? "3×3 기준 권장 범위(65~75px)를 벗어났습니다 — 다른 밀도를 쓰는 중이라면 물리 높이로 판단하세요."
              : undefined
          }
        >
          <output className="ce-derived-value">
            {scale.targetPhysicalHeightPx} ÷ {scale.blockPx} = {scale.targetLogicalHeightPx} logical px
          </output>
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
          onReset={() => onChangeActor((current) => resetBodySizeOverride(current))}
          hint={`월드 기준 신체 높이(${worldBasePx}px)에 대한 상대 신장입니다. 값을 수정하면 목표 물리 높이가 자동 계산됩니다. 체형과 등신은 변경하지 않습니다.`}
        >
          <CommittedNumberInput
            aria-label="Species Scale"
            step={0.05}
            min={0.05}
            value={displaySpeciesScale(size.speciesScale)}
            onCommit={(speciesScale) =>
              commitSize(physicalHeightFromScale(worldBasePx, speciesScale), speciesScale, "species-scale")
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
