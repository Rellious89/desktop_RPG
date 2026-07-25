import type { ReactNode } from "react";
import { Badge } from "./Badge";
import { TermHelpButton } from "./TermHelpButton";

export type FieldOriginDisplay = "inherited" | "override" | "locked" | "none";

export interface FieldRowProps {
  label: string;
  htmlFor?: string;
  required?: boolean;
  termKey?: string;
  origin?: FieldOriginDisplay;
  baselineLabel?: string;
  onReset?: () => void;
  hint?: string;
  children: ReactNode;
}

/**
 * The reusable "inherited vs. override" field wrapper described in the
 * Wave 1 research (section 7): every inheritable field shows a World/Override
 * badge, the inherited baseline for comparison, and a reset-to-world control.
 * Locked fields (PPU, base canvas, ...) show a policy badge instead and never
 * expose a reset control since they cannot be freely overridden.
 */
export function FieldRow({
  label,
  htmlFor,
  required,
  termKey,
  origin = "none",
  baselineLabel,
  onReset,
  hint,
  children,
}: FieldRowProps) {
  return (
    <div className={`ce-field${origin === "locked" ? " ce-field--locked" : ""}`}>
      <div className="ce-field-label-row">
        <label className="ce-field-label" htmlFor={htmlFor}>
          {label}
          {required && (
            <span className="ce-required-mark" aria-label="필수 항목">
              *
            </span>
          )}
          {termKey && <TermHelpButton termKey={termKey} />}
        </label>
        <span className="ce-field-badges">
          {origin === "inherited" && <Badge tone="inherited">상속: World</Badge>}
          {origin === "override" && (
            <>
              <Badge tone="override">재정의됨</Badge>
              {onReset && (
                <button type="button" className="ce-btn ce-btn--ghost ce-btn--sm" onClick={onReset}>
                  ↺ 기본값으로
                </button>
              )}
            </>
          )}
          {origin === "locked" && <Badge tone="locked">정책 고정</Badge>}
        </span>
      </div>
      {children}
      {hint && <div className="ce-field-hint">{hint}</div>}
      {baselineLabel && origin === "override" && (
        <div className="ce-field-baseline">{baselineLabel}</div>
      )}
    </div>
  );
}
