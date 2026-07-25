import type { ActorDocument } from "../../app/types";
import { setEquipment, setWeapon } from "../../app/actorDraft";
import { WEAPON_CATALOG } from "../../app/weaponCatalog";
import { FieldRow } from "../common/FieldRow";
import { TagListEditor } from "../common/TagListEditor";
import { Badge } from "../common/Badge";

type Weapon = NonNullable<ActorDocument["equipment"]["weapon"]>;

const SIZE_CLASS_OPTIONS: Weapon["sizeClass"][] = ["small", "medium", "large", "oversized"];
const HAND_OPTIONS: Weapon["mainHand"][] = ["anatomical-right", "anatomical-left", "both", "none"];

export interface EquipmentSectionProps {
  actor: ActorDocument;
  onChangeActor: (updater: (actor: ActorDocument) => ActorDocument) => void;
}

export function EquipmentSection({ actor, onChangeActor }: EquipmentSectionProps) {
  const weapon = actor.equipment.weapon;
  const catalogEntry = weapon ? WEAPON_CATALOG.find((entry) => entry.id === weapon.family) : undefined;

  return (
    <div className="ce-card">
      <h3 className="ce-card-title">Weapon &amp; Equipment</h3>
      <p className="ce-card-subtitle">
        무기 계열은 이 액터의 허용 목록(아래 "이 액터에 허용된 무기 계열")에 있어야 Export가
        차단되지 않습니다. 카탈로그는 참고용이며 실제 허용 여부는 Validation 패널을 따릅니다.
      </p>

      <FieldRow label="무기 보유" hint="끄면 무기 없이 저장됩니다 (equipment.weapon 없음).">
        <label className="ce-row" style={{ fontWeight: 400 }}>
          <input
            type="checkbox"
            checked={!!weapon}
            onChange={(event) =>
              onChangeActor((current) =>
                event.target.checked
                  ? setWeapon(current, {
                      family: "",
                      sizeClass: "medium",
                      mainHand: "anatomical-right",
                      offHand: "none",
                      direction: "",
                      structure: "",
                      count: 1,
                    })
                  : setWeapon(current, undefined),
              )
            }
          />
          이 액터는 무기를 사용합니다
        </label>
      </FieldRow>

      {weapon && (
        <>
          <div className="ce-form-grid" style={{ marginTop: "var(--ce-space-3)" }}>
            <FieldRow label="무기 계열" required termKey="weaponFamily" htmlFor="weapon-family">
              <input
                id="weapon-family"
                type="text"
                list="weapon-family-catalog"
                value={weapon.family}
                onChange={(event) => onChangeActor((current) => setWeapon(current, { family: event.target.value }))}
              />
              <datalist id="weapon-family-catalog">
                {WEAPON_CATALOG.map((entry) => (
                  <option key={entry.id} value={entry.id}>
                    {entry.labelKo}
                  </option>
                ))}
              </datalist>
            </FieldRow>
            <FieldRow label="크기 등급" required htmlFor="weapon-size-class">
              <select
                id="weapon-size-class"
                value={weapon.sizeClass}
                onChange={(event) =>
                  onChangeActor((current) =>
                    setWeapon(current, { sizeClass: event.target.value as Weapon["sizeClass"] }),
                  )
                }
              >
                {SIZE_CLASS_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </FieldRow>
            <FieldRow label="주손" htmlFor="weapon-main-hand">
              <select
                id="weapon-main-hand"
                value={weapon.mainHand}
                onChange={(event) =>
                  onChangeActor((current) => setWeapon(current, { mainHand: event.target.value as Weapon["mainHand"] }))
                }
              >
                {HAND_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </FieldRow>
            <FieldRow label="보조손" htmlFor="weapon-off-hand">
              <select
                id="weapon-off-hand"
                value={weapon.offHand}
                onChange={(event) =>
                  onChangeActor((current) => setWeapon(current, { offHand: event.target.value as Weapon["offHand"] }))
                }
              >
                {HAND_OPTIONS.map((option) => (
                  <option key={option} value={option}>
                    {option}
                  </option>
                ))}
              </select>
            </FieldRow>
            <FieldRow label="개수" required htmlFor="weapon-count">
              <input
                id="weapon-count"
                type="number"
                min="0"
                value={weapon.count}
                onChange={(event) => onChangeActor((current) => setWeapon(current, { count: Number(event.target.value) }))}
              />
            </FieldRow>
            <FieldRow
              label="무기 크기 비율(신장 대비)"
              termKey="weaponSizeRatio"
              htmlFor="weapon-ratio"
              hint="예: 글레이브 약 1.2, 단검 약 0.3"
            >
              <input
                id="weapon-ratio"
                type="number"
                step="0.05"
                min="0"
                value={weapon.lengthToBodyRatio ?? ""}
                onChange={(event) =>
                  onChangeActor((current) =>
                    setWeapon(current, {
                      lengthToBodyRatio: event.target.value === "" ? undefined : Number(event.target.value),
                    }),
                  )
                }
              />
            </FieldRow>
            <FieldRow
              label="예상 캔버스 점유율(%)"
              termKey="weaponSizeRatio"
              htmlFor="weapon-occupancy"
              hint="60%를 초과하면 large-weapon-canvas 경고가 뜹니다."
            >
              <input
                id="weapon-occupancy"
                type="number"
                min="0"
                max="100"
                value={weapon.estimatedOccupancyPercent ?? ""}
                onChange={(event) =>
                  onChangeActor((current) =>
                    setWeapon(current, {
                      estimatedOccupancyPercent: event.target.value === "" ? undefined : Number(event.target.value),
                    }),
                  )
                }
              />
            </FieldRow>
          </div>

          {catalogEntry && (
            <p className="ce-field-hint" style={{ marginTop: "var(--ce-space-2)" }}>
              카탈로그 근거: {catalogEntry.evidence}{" "}
              <Badge tone={catalogEntry.status === "confirmed" ? "success" : "warning"}>
                {catalogEntry.status === "confirmed" ? "확인됨" : "추정(Concept)"}
              </Badge>
            </p>
          )}

          <FieldRow label="방향/비대칭 메모" required htmlFor="weapon-direction">
            <input
              id="weapon-direction"
              type="text"
              value={weapon.direction}
              onChange={(event) => onChangeActor((current) => setWeapon(current, { direction: event.target.value }))}
            />
          </FieldRow>

          <FieldRow label="구조 메모" required htmlFor="weapon-structure">
            <textarea
              id="weapon-structure"
              value={weapon.structure}
              onChange={(event) => onChangeActor((current) => setWeapon(current, { structure: event.target.value }))}
            />
          </FieldRow>
        </>
      )}

      <FieldRow label="보조 장비" htmlFor="weapon-secondary">
        <TagListEditor
          id="weapon-secondary"
          values={actor.equipment.secondary}
          onChange={(values) => onChangeActor((current) => setEquipment(current, { secondary: values }))}
          placeholder="보조 장비 입력 후 Enter"
        />
      </FieldRow>

      <FieldRow
        label="이 액터에 허용된 무기 계열"
        required
        htmlFor="weapon-allowed"
        hint="여기에 없는 무기 계열을 선택하면 weapon-family-not-allowed 오류가 발생합니다."
      >
        <TagListEditor
          id="weapon-allowed"
          values={actor.equipment.allowedWeaponFamilies}
          onChange={(values) => onChangeActor((current) => setEquipment(current, { allowedWeaponFamilies: values }))}
          placeholder="무기 계열 ID 입력 후 Enter"
        />
      </FieldRow>
    </div>
  );
}
