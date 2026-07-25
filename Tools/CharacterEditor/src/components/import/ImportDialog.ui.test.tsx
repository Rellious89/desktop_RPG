import "../../app/uiTestCleanup";
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { ImportDialog } from "./ImportDialog";

describe("ImportDialog", () => {
  it("parses pasted JSON and forwards the parsed value", () => {
    const onImportRaw = vi.fn();
    render(<ImportDialog onImportRaw={onImportRaw} onCancel={vi.fn()} />);

    fireEvent.change(screen.getByLabelText("또는 JSON 붙여넣기"), {
      target: { value: '{"documentKind":"actor","identity":{"actorId":"ElfGuardian"}}' },
    });
    fireEvent.click(screen.getByRole("button", { name: "가져오기" }));

    expect(onImportRaw).toHaveBeenCalledWith({
      documentKind: "actor",
      identity: { actorId: "ElfGuardian" },
    });
  });

  it("shows a local syntax error for invalid JSON instead of crashing", () => {
    const onImportRaw = vi.fn();
    render(<ImportDialog onImportRaw={onImportRaw} onCancel={vi.fn()} />);

    fireEvent.change(screen.getByLabelText("또는 JSON 붙여넣기"), { target: { value: "{not valid json" } });
    fireEvent.click(screen.getByRole("button", { name: "가져오기" }));

    expect(screen.getByText("JSON 구문 오류")).toBeInTheDocument();
    expect(onImportRaw).not.toHaveBeenCalled();
  });

  it("renders parent-supplied validation errors", () => {
    render(
      <ImportDialog
        onImportRaw={vi.fn()}
        onCancel={vi.fn()}
        errors={["actorId가 비어 있습니다.", "worldRef.version이 숫자가 아닙니다."]}
      />,
    );
    expect(screen.getByText("actorId가 비어 있습니다.")).toBeInTheDocument();
    expect(screen.getByText("worldRef.version이 숫자가 아닙니다.")).toBeInTheDocument();
  });
});
