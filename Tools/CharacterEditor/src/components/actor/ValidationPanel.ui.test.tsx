import "../../app/uiTestCleanup";
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { ValidationPanel } from "./ValidationPanel";
import { makeDiagnostic } from "../../app/testFixtures";

describe("ValidationPanel", () => {
  it("disables export while a blocking diagnostic is present", () => {
    const onExport = vi.fn();
    render(
      <ValidationPanel
        diagnostics={[makeDiagnostic({ ruleId: "unity-scale-not-one", severity: "error", overridable: true })]}
        approvedExceptions={[]}
        onApproveException={vi.fn()}
        onRemoveException={vi.fn()}
        onExport={onExport}
      />,
    );
    const exportButton = screen.getByRole("button", { name: /Export 미리보기/ });
    expect(exportButton).toBeDisabled();
    expect(screen.getByText("Blocking 1")).toBeInTheDocument();
  });

  it("requires at least 10 characters before an exception reason can be submitted", () => {
    const onApprove = vi.fn();
    render(
      <ValidationPanel
        diagnostics={[makeDiagnostic({ ruleId: "unity-scale-not-one", severity: "error", overridable: true })]}
        approvedExceptions={[]}
        onApproveException={onApprove}
        onRemoveException={vi.fn()}
        onExport={vi.fn()}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "예외 등록" }));
    const textarea = screen.getByLabelText(/승인 사유/);
    const submit = screen.getByRole("button", { name: "예외 등록" });
    fireEvent.change(textarea, { target: { value: "too short" } });
    expect(submit).toBeDisabled();

    fireEvent.change(textarea, { target: { value: "충분히 긴 승인 사유입니다" } });
    expect(submit).not.toBeDisabled();
    fireEvent.click(submit);
    expect(onApprove).toHaveBeenCalledWith("unity-scale-not-one", "충분히 긴 승인 사유입니다");
  });

  it("enables export and shows the reason once every blocking diagnostic has an approved exception", () => {
    render(
      <ValidationPanel
        diagnostics={[
          makeDiagnostic({
            ruleId: "unity-scale-not-one",
            severity: "error",
            overridable: true,
            blocksExport: false,
            exceptionApproved: true,
          }),
        ]}
        approvedExceptions={[{ ruleId: "unity-scale-not-one", reason: "실험 목적 유지", active: true }]}
        onApproveException={vi.fn()}
        onRemoveException={vi.fn()}
        onExport={vi.fn()}
      />,
    );
    expect(screen.getByRole("button", { name: /Export 미리보기/ })).not.toBeDisabled();
    expect(screen.getByText("승인된 예외 1")).toBeInTheDocument();
    expect(screen.getByText("실험 목적 유지")).toBeInTheDocument();
  });

  it("lets the user remove an approved exception", () => {
    const onRemove = vi.fn();
    render(
      <ValidationPanel
        diagnostics={[
          makeDiagnostic({
            ruleId: "weapon-family-not-allowed",
            severity: "error",
            overridable: true,
            blocksExport: false,
            exceptionApproved: true,
          }),
        ]}
        approvedExceptions={[{ ruleId: "weapon-family-not-allowed", reason: "임시 리스킨 테스트", active: true }]}
        onApproveException={vi.fn()}
        onRemoveException={onRemove}
        onExport={vi.fn()}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "예외 해제" }));
    expect(onRemove).toHaveBeenCalledWith("weapon-family-not-allowed");
  });
});
