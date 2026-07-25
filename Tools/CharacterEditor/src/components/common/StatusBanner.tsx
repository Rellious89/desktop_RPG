import type { ReactNode } from "react";

export function StatusBanner({
  tone,
  title,
  children,
}: {
  tone: "info" | "error";
  title: string;
  children?: ReactNode;
}) {
  return (
    <div className="ce-card" role={tone === "error" ? "alert" : "status"}>
      <p className="ce-card-title" style={{ fontSize: "var(--ce-font-size-base)" }}>
        {tone === "error" ? "⚠ " : "ℹ "}
        {title}
      </p>
      {children && <div className="ce-field-hint">{children}</div>}
    </div>
  );
}
