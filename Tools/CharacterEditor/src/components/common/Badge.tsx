import type { ReactNode } from "react";

export type BadgeTone =
  | "inherited"
  | "override"
  | "locked"
  | "error"
  | "warning"
  | "info"
  | "success"
  | "neutral";

export function Badge({ tone, children }: { tone: BadgeTone; children: ReactNode }) {
  return <span className={`ce-badge ce-badge--${tone}`}>{children}</span>;
}
