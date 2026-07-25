import "../../app/uiTestCleanup";
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { BodySection } from "./BodySection";
import { makeActor, makeWorld, resolvedFromWorldAndOverrides } from "../../app/testFixtures";
import type { ActorDocument } from "../../app/types";

describe("BodySection", () => {
  it("marks an overridden target height field and lets the user reset it back to inherited", () => {
    const world = makeWorld();
    const actor = makeActor();
    const resolved = resolvedFromWorldAndOverrides(world, actor);
    let latestActor: ActorDocument = actor;
    const onChangeActor = vi.fn((updater: (a: ActorDocument) => ActorDocument) => {
      latestActor = updater(latestActor);
    });

    render(<BodySection actor={actor} world={world} resolved={resolved} onChangeActor={onChangeActor} />);

    // 91px override differs from the 70px world default -> override badge + baseline note.
    expect(screen.getByText("World 기본값: 70px (권장 65~75px)")).toBeInTheDocument();
    expect(screen.getByDisplayValue("91")).toBeInTheDocument();

    const resetButtons = screen.getAllByRole("button", { name: /기본값으로/ });
    fireEvent.click(resetButtons[0]);
    expect(onChangeActor).toHaveBeenCalled();
    // After resetting stature, that key should no longer be present as an override.
    expect(latestActor.overrides.anatomy?.stature).toBeUndefined();
  });

  it("edits a field by calling onChangeActor with an updater that sets the override", () => {
    const world = makeWorld();
    const actor = makeActor({ overrides: {} });
    const resolved = resolvedFromWorldAndOverrides(world, actor);
    let latestActor: ActorDocument = actor;
    const onChangeActor = vi.fn((updater: (a: ActorDocument) => ActorDocument) => {
      latestActor = updater(latestActor);
    });

    render(<BodySection actor={actor} world={world} resolved={resolved} onChangeActor={onChangeActor} />);

    const heightInput = screen.getByDisplayValue("70") as HTMLInputElement;
    fireEvent.change(heightInput, { target: { value: "91" } });

    expect(latestActor.overrides.anatomy?.targetLogicalHeightPx).toBe(91);
  });

  it("adds a physical trait tag", () => {
    const world = makeWorld();
    const actor = makeActor({ physicalTraits: [] });
    const resolved = resolvedFromWorldAndOverrides(world, actor);
    let latestActor: ActorDocument = actor;
    const onChangeActor = vi.fn((updater: (a: ActorDocument) => ActorDocument) => {
      latestActor = updater(latestActor);
    });

    render(<BodySection actor={actor} world={world} resolved={resolved} onChangeActor={onChangeActor} />);

    const traitInput = screen.getByPlaceholderText("특징 입력 후 Enter");
    fireEvent.change(traitInput, { target: { value: "뾰족귀" } });
    fireEvent.keyDown(traitInput, { key: "Enter" });

    expect(latestActor.physicalTraits).toContain("뾰족귀");
  });
});
