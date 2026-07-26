import { useEffect, useState } from "react";

export interface CommittedNumberInputProps {
  /** Canonical value; re-syncs the field whenever it changes from outside. */
  value: number;
  /** Called only with a finite value the user has finished typing. */
  onCommit: (value: number) => void;
  min?: number;
  step?: number;
  id?: string;
  "aria-label"?: string;
}

/**
 * A number field that reports its value on blur or Enter rather than on every
 * keystroke.
 *
 * The two body-size fields recompute each other, so committing mid-keystroke
 * would be destructive: clearing "290" to retype it would momentarily read as
 * empty and overwrite the opposite field with a zero, and every intermediate
 * digit ("2", "29") would recompute a size the user never asked for. Keeping
 * the half-typed text local until it is a finished number avoids that without
 * blocking normal typing.
 */
export function CommittedNumberInput({ value, onCommit, min, step, id, ...rest }: CommittedNumberInputProps) {
  const [draft, setDraft] = useState(String(value));

  // Follow the canonical value when something else changes it — the paired
  // field, a density change, a reset to the world default.
  useEffect(() => setDraft(String(value)), [value]);

  function commit() {
    const parsed = Number(draft);
    if (draft.trim() === "" || !Number.isFinite(parsed) || (min !== undefined && parsed < min)) {
      setDraft(String(value)); // unusable input: restore rather than write a bad size
      return;
    }
    if (parsed !== value) onCommit(parsed);
    else setDraft(String(value));
  }

  return (
    <input
      id={id}
      type="number"
      min={min}
      step={step}
      value={draft}
      onChange={(event) => setDraft(event.target.value)}
      onBlur={commit}
      onKeyDown={(event) => {
        if (event.key === "Enter") {
          event.preventDefault();
          commit();
        }
      }}
      aria-label={rest["aria-label"]}
    />
  );
}
