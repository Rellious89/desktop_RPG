import { useEffect, useRef, useState } from "react";
import { TERM_HELP } from "../../app/termHelp";

/**
 * The "(?)" affordance required by the integrated spec section 5:
 * "Specialized terms have short help text." Text is sourced from
 * src/app/termHelp.ts, grounded in the Wave 1 UX research rather than
 * invented ad hoc per component.
 */
export function TermHelpButton({ termKey }: { termKey: string }) {
  const entry = TERM_HELP[termKey];
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLSpanElement>(null);

  useEffect(() => {
    if (!open) return;
    function handlePointerDown(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false);
      }
    }
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }
    document.addEventListener("mousedown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [open]);

  if (!entry) return null;

  return (
    <span className="ce-help-popover" ref={containerRef}>
      <button
        type="button"
        className="ce-help-trigger"
        aria-expanded={open}
        aria-label={`${entry.termKo} 용어 설명`}
        onClick={() => setOpen((value) => !value)}
      >
        ?
      </button>
      {open && (
        <span className="ce-help-bubble" role="tooltip">
          <strong>
            {entry.termKo} · {entry.termEn}
          </strong>
          {entry.helpKo}
        </span>
      )}
    </span>
  );
}
