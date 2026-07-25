import { useRef, useState, type CompositionEvent, type KeyboardEvent } from "react";

export interface TagListEditorProps {
  id: string;
  values: string[];
  onChange: (values: string[]) => void;
  placeholder?: string;
}

/** Reusable add/remove chip-list input for arrays like invariant elements,
 * forbidden elements, aliases, palette colors, and personality keywords. */
export function TagListEditor({ id, values, onChange, placeholder }: TagListEditorProps) {
  const [draft, setDraft] = useState("");
  const draftRef = useRef("");
  const isComposingRef = useRef(false);

  function updateDraft(value: string) {
    draftRef.current = value;
    setDraft(value);
  }

  function commit(value = draftRef.current) {
    const trimmed = value.trim();
    if (trimmed && !values.includes(trimmed)) {
      onChange([...values, trimmed]);
    }
    updateDraft("");
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Enter" || event.key === ",") {
      if (isComposingRef.current || event.nativeEvent.isComposing || event.keyCode === 229) return;
      event.preventDefault();
      commit();
    }
  }

  function handleCompositionStart() {
    isComposingRef.current = true;
  }

  function handleCompositionEnd(event: CompositionEvent<HTMLInputElement>) {
    isComposingRef.current = false;
    updateDraft(event.currentTarget.value);
  }

  return (
    <div>
      {values.length > 0 && (
        <div className="ce-tag-input" style={{ marginBottom: "var(--ce-space-2)" }}>
          {values.map((value) => (
            <span className="ce-tag" key={value}>
              {value}
              <button
                type="button"
                aria-label={`${value} 삭제`}
                onClick={() => onChange(values.filter((entry) => entry !== value))}
              >
                ×
              </button>
            </span>
          ))}
        </div>
      )}
      <input
        id={id}
        type="text"
        value={draft}
        placeholder={placeholder}
        onChange={(event) => updateDraft(event.target.value)}
        onKeyDown={handleKeyDown}
        onCompositionStart={handleCompositionStart}
        onCompositionEnd={handleCompositionEnd}
        onBlur={(event) => commit(event.currentTarget.value)}
      />
    </div>
  );
}
