import "../../app/uiTestCleanup";
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { FieldRow } from "./FieldRow";

describe("FieldRow", () => {
  it("shows an inherited badge and no reset control when origin is inherited", () => {
    render(
      <FieldRow label="목표 논리 높이(px)" origin="inherited">
        <input defaultValue={70} />
      </FieldRow>,
    );
    expect(screen.getByText("상속: World")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /기본값으로/ })).not.toBeInTheDocument();
  });

  it("shows an override badge with a working reset control", () => {
    const onReset = vi.fn();
    render(
      <FieldRow label="목표 논리 높이(px)" origin="override" onReset={onReset} baselineLabel="World 기본값: 70px">
        <input defaultValue={91} />
      </FieldRow>,
    );
    expect(screen.getByText("재정의됨")).toBeInTheDocument();
    expect(screen.getByText("World 기본값: 70px")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /기본값으로/ }));
    expect(onReset).toHaveBeenCalledTimes(1);
  });

  it("marks locked fields with a policy badge and no reset control", () => {
    render(
      <FieldRow label="PPU" origin="locked">
        <input value={200} disabled readOnly />
      </FieldRow>,
    );
    expect(screen.getByText("정책 고정")).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("renders a required marker when required", () => {
    render(
      <FieldRow label="Actor ID" required>
        <input defaultValue="" />
      </FieldRow>,
    );
    expect(screen.getByLabelText("필수 항목")).toBeInTheDocument();
  });
});
