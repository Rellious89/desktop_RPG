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

    // The 91 logical px override is 273 physical px at the world's 3x3 density,
    // against a 210px (70 logical) world default -> override badge + baseline.
    expect(screen.getByText("World 기본값: 210px (70 logical @ 3×3)")).toBeInTheDocument();
    expect(screen.getByDisplayValue("273")).toBeInTheDocument();

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

    // Inherits 70 logical px at 3x3 -> 210 physical px in the authored field.
    const heightInput = screen.getByDisplayValue("210") as HTMLInputElement;
    fireEvent.change(heightInput, { target: { value: "273" } });

    expect(latestActor.overrides.anatomy?.targetPhysicalHeightPx).toBe(273);
    expect(latestActor.overrides.anatomy?.targetLogicalHeightPx).toBe(91);
  });

  it("keeps the physical height and recomputes the logical height when the density changes", () => {
    const world = makeWorld();
    const actor = makeActor({ overrides: {} });
    const resolved = resolvedFromWorldAndOverrides(world, actor);
    let latestActor: ActorDocument = actor;
    const onChangeActor = vi.fn((updater: (a: ActorDocument) => ActorDocument) => {
      latestActor = updater(latestActor);
    });

    render(<BodySection actor={actor} world={world} resolved={resolved} onChangeActor={onChangeActor} />);

    fireEvent.change(screen.getByLabelText("픽셀 밀도"), { target: { value: "detail-2x2" } });

    // 210px stays 210px; only the grid gets finer, so 70 logical px becomes 105.
    expect(latestActor.overrides.anatomy?.targetPhysicalHeightPx).toBe(210);
    expect(latestActor.overrides.anatomy?.targetLogicalHeightPx).toBe(105);
    expect(latestActor.overrides.pixelStyle?.logicalBlockPx).toEqual({ widthPx: 2, heightPx: 2 });
    expect(latestActor.overrides.pixelStyle?.densityPreset).toBe("detail-2x2");
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
