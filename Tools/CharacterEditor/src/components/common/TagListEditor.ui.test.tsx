import "../../app/uiTestCleanup";
import { useState } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { TagListEditor } from "./TagListEditor";

function Harness({ initial = [] }: { initial?: string[] }) {
  const [values, setValues] = useState(initial);
  return (
    <>
      <TagListEditor id="tags" values={values} onChange={setValues} placeholder="태그" />
      <output aria-label="태그 값">{JSON.stringify(values)}</output>
    </>
  );
}

function input() {
  return screen.getByPlaceholderText("태그");
}

function values() {
  return JSON.parse(screen.getByLabelText("태그 값").textContent ?? "[]") as string[];
}

function compose(value: string, key: "Enter" | "," = "Enter") {
  fireEvent.compositionStart(input());
  fireEvent.change(input(), { target: { value } });
  fireEvent.keyDown(input(), { key, code: key === "Enter" ? "Enter" : "Comma", keyCode: 229, isComposing: true });
}

describe("TagListEditor IME behavior", () => {
  it("commits one English tag with Enter", () => {
    render(<Harness />);
    fireEvent.change(input(), { target: { value: "barefoot" } });
    fireEvent.keyDown(input(), { key: "Enter" });
    expect(values()).toEqual(["barefoot"]);
  });

  it("does not commit 맨발 while composition Enter is confirming text", () => {
    render(<Harness />);
    compose("맨발");
    expect(values()).toEqual([]);
    expect(input()).toHaveValue("맨발");
  });

  it("commits only 맨발 after composition ends and the next Enter", () => {
    render(<Harness />);
    compose("맨발");
    fireEvent.compositionEnd(input(), { data: "발" });
    fireEvent.keyDown(input(), { key: "Enter" });
    expect(values()).toEqual(["맨발"]);
    expect(values()).not.toContain("발");
  });

  it("does not split the last syllable from 머리삔", () => {
    render(<Harness />);
    compose("머리삔");
    fireEvent.compositionEnd(input(), { data: "삔" });
    fireEvent.keyDown(input(), { key: "Enter" });
    expect(values()).toEqual(["머리삔"]);
    expect(values()).not.toContain("삔");
  });

  it("does not treat a comma as a delimiter during composition", () => {
    render(<Harness />);
    compose("천 질감", ",");
    expect(values()).toEqual([]);
    expect(input()).toHaveValue("천 질감");
  });

  it("commits the latest completed composition value on blur", () => {
    render(<Harness />);
    compose("분홍색 머리핀");
    fireEvent.compositionEnd(input(), { data: "핀" });
    fireEvent.blur(input());
    expect(values()).toEqual(["분홍색 머리핀"]);
  });

  it("retains comma commit for non-IME input", () => {
    render(<Harness />);
    fireEvent.change(input(), { target: { value: "cloth" } });
    fireEvent.keyDown(input(), { key: ",", code: "Comma" });
    expect(values()).toEqual(["cloth"]);
  });

  it("prevents duplicate tags", () => {
    render(<Harness initial={["맨발"]} />);
    fireEvent.change(input(), { target: { value: "맨발" } });
    fireEvent.keyDown(input(), { key: "Enter" });
    expect(values()).toEqual(["맨발"]);
  });

  it("removes an existing tag", () => {
    render(<Harness initial={["맨발", "머리삔"]} />);
    fireEvent.click(screen.getByRole("button", { name: "맨발 삭제" }));
    expect(values()).toEqual(["머리삔"]);
  });
});
