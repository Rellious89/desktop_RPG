import type { ReactNode } from "react";

export interface Breadcrumb {
  label: string;
  onClick?: () => void;
}

export function AppShell({
  breadcrumbs,
  children,
}: {
  breadcrumbs: Breadcrumb[];
  children: ReactNode;
}) {
  return (
    <div className="ce-app">
      <header className="ce-app-header">
        <div className="ce-app-title">
          <h1>KeyBuddy Character Editor</h1>
          <span>월드 템플릿 &amp; 액터 시트</span>
        </div>
        <nav className="ce-breadcrumbs" aria-label="이동 경로">
          {breadcrumbs.map((crumb, index) => (
            <span key={`${crumb.label}-${index}`}>
              {index > 0 && <span aria-hidden="true"> / </span>}
              {crumb.onClick ? (
                <button type="button" onClick={crumb.onClick}>
                  {crumb.label}
                </button>
              ) : (
                <span aria-current="page">{crumb.label}</span>
              )}
            </span>
          ))}
        </nav>
      </header>
      <main className="ce-app-main">{children}</main>
    </div>
  );
}
