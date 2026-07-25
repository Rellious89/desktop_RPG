import { useRef, useState, type ChangeEvent } from "react";

export interface ImportDialogProps {
  onImportRaw: (raw: unknown) => void;
  errors?: string[];
  onCancel: () => void;
}

/**
 * Accepts either a Schema v1 authored actor/world document or a resolved
 * export envelope, per spec section 5 ("Import accepts Schema v1 authored
 * actors and resolved export envelopes"). This component only reads the
 * file/text and does JSON.parse (plain syntax parsing, not domain logic) —
 * the parent decides which of parseActor/parseWorld to try and surfaces the
 * real validation result back through `errors`.
 */
export function ImportDialog({ onImportRaw, errors, onCancel }: ImportDialogProps) {
  const [pastedText, setPastedText] = useState("");
  const [syntaxError, setSyntaxError] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  function tryImport(text: string) {
    try {
      const parsed = JSON.parse(text);
      setSyntaxError(null);
      onImportRaw(parsed);
    } catch (error) {
      setSyntaxError(error instanceof Error ? error.message : "JSON 구문을 해석할 수 없습니다.");
    }
  }

  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      if (typeof reader.result === "string") {
        tryImport(reader.result);
      }
    };
    reader.readAsText(file);
    event.target.value = "";
  }

  return (
    <div className="ce-card">
      <div className="ce-card-header">
        <div>
          <h2 className="ce-card-title">JSON 가져오기</h2>
          <p className="ce-card-subtitle">
            Schema v1 액터/World 템플릿 JSON 또는 이전에 내보낸 Export 파일을 가져올 수 있습니다.
            불러온 뒤 직접 JSON을 편집할 필요 없이 폼에서 이어서 편집합니다.
          </p>
        </div>
        <button type="button" className="ce-btn ce-btn--secondary" onClick={onCancel}>
          취소
        </button>
      </div>

      <div className="ce-field">
        <label className="ce-field-label" htmlFor="import-file">
          파일에서 가져오기
        </label>
        <input id="import-file" ref={fileInputRef} type="file" accept=".json,application/json" onChange={handleFileChange} />
      </div>

      <div className="ce-field" style={{ marginTop: "var(--ce-space-3)" }}>
        <label className="ce-field-label" htmlFor="import-paste">
          또는 JSON 붙여넣기
        </label>
        <textarea
          id="import-paste"
          value={pastedText}
          onChange={(event) => setPastedText(event.target.value)}
          style={{ minHeight: "10em", fontFamily: "ui-monospace, monospace" }}
        />
        <button
          type="button"
          className="ce-btn ce-btn--primary ce-btn--sm"
          style={{ marginTop: "var(--ce-space-2)", alignSelf: "flex-start" }}
          onClick={() => tryImport(pastedText)}
        >
          가져오기
        </button>
      </div>

      {syntaxError && (
        <div className="ce-validation-item ce-validation-item--error" style={{ marginTop: "var(--ce-space-3)" }}>
          <strong>JSON 구문 오류</strong>
          <p className="ce-validation-message">{syntaxError}</p>
        </div>
      )}

      {errors && errors.length > 0 && (
        <div className="ce-validation-item ce-validation-item--error" style={{ marginTop: "var(--ce-space-3)" }}>
          <strong>가져오기 실패</strong>
          <ul>
            {errors.map((error, index) => (
              <li key={index} className="ce-validation-message">
                {error}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
