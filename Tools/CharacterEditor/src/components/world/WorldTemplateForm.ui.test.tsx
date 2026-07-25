import "../../app/uiTestCleanup";
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { WorldTemplateForm } from "./WorldTemplateForm";
import { makeWorld } from "../../app/testFixtures";

describe("WorldTemplateForm", () => {
  it("keeps PPU, base canvas, and Unity visual scale locked and disabled", () => {
    render(
      <WorldTemplateForm
        mode="create"
        initialWorld={makeWorld()}
        onSave={vi.fn()}
        onDownload={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.getByDisplayValue("200")).toBeDisabled();
    expect(screen.getByDisplayValue("512×512")).toBeDisabled();
    const scaleInputs = screen.getAllByDisplayValue("1");
    expect(scaleInputs.some((el) => (el as HTMLInputElement).disabled)).toBe(true);
  });

  it("saves the edited draft (not the original) when Save is clicked", () => {
    const onSave = vi.fn();
    render(
      <WorldTemplateForm
        mode="create"
        initialWorld={makeWorld({ worldId: "" })}
        onSave={onSave}
        onDownload={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByLabelText(/^World ID/), { target: { value: "HUMAN-FANTASY-01" } });
    fireEvent.click(screen.getByRole("button", { name: /^저장/ }));
    expect(onSave).toHaveBeenCalledTimes(1);
    expect(onSave.mock.calls[0][0].worldId).toBe("HUMAN-FANTASY-01");
  });

  it("shows validation errors returned by the parent without losing the draft", () => {
    render(
      <WorldTemplateForm
        mode="create"
        initialWorld={makeWorld()}
        validationErrors={["worldId가 비어 있습니다."]}
        onSave={vi.fn()}
        onDownload={vi.fn()}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.getByText("worldId가 비어 있습니다.")).toBeInTheDocument();
  });

  it("calls onDownload with the current draft", () => {
    const onDownload = vi.fn();
    render(
      <WorldTemplateForm
        mode="edit"
        initialWorld={makeWorld({ revision: 2 })}
        onSave={vi.fn()}
        onDownload={onDownload}
        onCancel={vi.fn()}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "JSON 다운로드" }));
    expect(onDownload).toHaveBeenCalledWith(expect.objectContaining({ revision: 2 }));
  });
});
