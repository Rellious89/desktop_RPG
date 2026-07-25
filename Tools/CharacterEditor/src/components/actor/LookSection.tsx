import { useState } from "react";
import type { ActorDocument } from "../../app/types";
import { setAppearance, setConstraints } from "../../app/actorDraft";
import { FieldRow } from "../common/FieldRow";
import { TagListEditor } from "../common/TagListEditor";

export interface LookSectionProps {
  actor: ActorDocument;
  onChangeActor: (updater: (actor: ActorDocument) => ActorDocument) => void;
}

function PaletteEditor({
  entries,
  onChange,
}: {
  entries: { role: string; value: string }[];
  onChange: (entries: { role: string; value: string }[]) => void;
}) {
  const [role, setRole] = useState("");
  const [value, setValue] = useState("");

  function add() {
    if (!role.trim() || !value.trim()) return;
    onChange([...entries, { role: role.trim(), value: value.trim() }]);
    setRole("");
    setValue("");
  }

  return (
    <div>
      {entries.length > 0 && (
        <div className="ce-tag-input" style={{ marginBottom: "var(--ce-space-2)" }}>
          {entries.map((entry, index) => (
            <span className="ce-tag" key={`${entry.role}-${index}`}>
              {entry.role}: {entry.value}
              <button
                type="button"
                aria-label={`${entry.role} 팔레트 항목 삭제`}
                onClick={() => onChange(entries.filter((_, i) => i !== index))}
              >
                ×
              </button>
            </span>
          ))}
        </div>
      )}
      <div className="ce-row">
        <input
          type="text"
          placeholder="역할 (예: hair)"
          value={role}
          onChange={(event) => setRole(event.target.value)}
        />
        <input
          type="text"
          placeholder="값 (예: golden blond)"
          value={value}
          onChange={(event) => setValue(event.target.value)}
        />
        <button type="button" className="ce-btn ce-btn--secondary ce-btn--sm" onClick={add}>
          추가
        </button>
      </div>
    </div>
  );
}

export function LookSection({ actor, onChangeActor }: LookSectionProps) {
  const appearance = actor.appearance;
  const constraints = actor.constraints;

  return (
    <div className="ce-card">
      <h3 className="ce-card-title">Look</h3>
      <p className="ce-card-subtitle">
        외형은 세계관 기본값을 상속하지 않고 항상 액터별로 직접 기록합니다.
      </p>

      <div className="ce-form-grid" style={{ marginTop: "var(--ce-space-3)" }}>
        <FieldRow label="헤어/머리 특징" htmlFor="look-hair">
          <input
            id="look-hair"
            type="text"
            value={appearance.hair ?? ""}
            onChange={(event) => onChangeActor((current) => setAppearance(current, { hair: event.target.value }))}
          />
        </FieldRow>
        <FieldRow label="눈 색" htmlFor="look-eyes">
          <input
            id="look-eyes"
            type="text"
            value={appearance.eyes ?? ""}
            onChange={(event) => onChangeActor((current) => setAppearance(current, { eyes: event.target.value }))}
          />
        </FieldRow>
        <FieldRow label="피부/털 색" htmlFor="look-skin">
          <input
            id="look-skin"
            type="text"
            value={appearance.skin ?? ""}
            onChange={(event) => onChangeActor((current) => setAppearance(current, { skin: event.target.value }))}
          />
        </FieldRow>
      </div>

      <FieldRow label="의상" required htmlFor="look-clothing">
        <TagListEditor
          id="look-clothing"
          values={appearance.clothing}
          onChange={(values) => onChangeActor((current) => setAppearance(current, { clothing: values }))}
          placeholder="의상 요소 입력 후 Enter"
        />
      </FieldRow>

      <FieldRow label="재질 키워드" htmlFor="look-materials">
        <TagListEditor
          id="look-materials"
          values={appearance.materials}
          onChange={(values) => onChangeActor((current) => setAppearance(current, { materials: values }))}
          placeholder="재질 입력 후 Enter"
        />
      </FieldRow>

      <FieldRow label="팔레트 (역할: 값)" hint="예: hair: golden blond">
        <PaletteEditor
          entries={appearance.palette}
          onChange={(entries) => onChangeActor((current) => setAppearance(current, { palette: entries }))}
        />
      </FieldRow>

      <FieldRow label="장식" htmlFor="look-decorations">
        <TagListEditor
          id="look-decorations"
          values={appearance.decorations}
          onChange={(values) => onChangeActor((current) => setAppearance(current, { decorations: values }))}
          placeholder="장식 입력 후 Enter"
        />
      </FieldRow>

      <FieldRow
        label="절대 불변 요소"
        required
        htmlFor="look-invariants"
        hint="다음 단계(컨셉 아트) 작업자가 반드시 지켜야 할 요소입니다. 비어 있으면 경고가 뜹니다."
      >
        <TagListEditor
          id="look-invariants"
          values={constraints.invariants}
          onChange={(values) => onChangeActor((current) => setConstraints(current, { invariants: values }))}
          placeholder="불변 요소 입력 후 Enter"
        />
      </FieldRow>

      <FieldRow label="금지 요소" required htmlFor="look-forbidden" hint="비어 있으면 경고가 뜹니다.">
        <TagListEditor
          id="look-forbidden"
          values={constraints.forbidden}
          onChange={(values) => onChangeActor((current) => setConstraints(current, { forbidden: values }))}
          placeholder="금지 요소 입력 후 Enter"
        />
      </FieldRow>
    </div>
  );
}
