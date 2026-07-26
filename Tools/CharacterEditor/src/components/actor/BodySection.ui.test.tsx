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
    // The size fields commit on blur, not per keystroke — see CommittedNumberInput.
    fireEvent.blur(heightInput);

    expect(latestActor.overrides.anatomy?.targetPhysicalHeightPx).toBe(273);
    expect(latestActor.overrides.anatomy?.targetLogicalHeightPx).toBe(91);
    expect(latestActor.overrides.anatomy?.speciesScale).toBeCloseTo(1.3, 10);
    expect(latestActor.overrides.anatomy?.sizeAuthority).toBe("physical-height");
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

  /** The world fixture is 70 logical px at 3×3 — a 210px baseline body. */
  function renderBody() {
    const world = makeWorld();
    const actor = makeActor({ overrides: {} });
    let latestActor: ActorDocument = actor;
    const onChangeActor = vi.fn((updater: (a: ActorDocument) => ActorDocument) => {
      latestActor = updater(latestActor);
    });
    render(
      <BodySection
        actor={actor}
        world={world}
        resolved={resolvedFromWorldAndOverrides(world, actor)}
        onChangeActor={onChangeActor}
      />,
    );
    return { get latestActor() { return latestActor; } };
  }

  it("recomputes the physical height when the species scale is edited", () => {
    const ctx = renderBody();

    const scaleInput = screen.getByLabelText("Species Scale");
    fireEvent.change(scaleInput, { target: { value: "1.38" } });
    fireEvent.blur(scaleInput);

    const anatomy = ctx.latestActor.overrides.anatomy;
    expect(anatomy?.targetPhysicalHeightPx).toBe(290); // round(210 × 1.38)
    expect(anatomy?.targetLogicalHeightPx).toBe(97);
    expect(anatomy?.speciesScale).toBe(1.38);
    expect(anatomy?.sizeAuthority).toBe("species-scale");
  });

  it("recomputes the species scale when the physical height is edited", () => {
    const ctx = renderBody();

    const heightInput = screen.getByLabelText("목표 물리 높이(px)");
    fireEvent.change(heightInput, { target: { value: "290" } });
    fireEvent.blur(heightInput);

    const anatomy = ctx.latestActor.overrides.anatomy;
    expect(anatomy?.speciesScale).toBeCloseTo(290 / 210, 12);
    expect(anatomy?.targetPhysicalHeightPx).toBe(290);
    expect(anatomy?.sizeAuthority).toBe("physical-height");
  });

  it("shows the relation between the two fields", () => {
    const world = makeWorld();
    const actor = makeActor(); // 91 logical px at 3×3 = 273 physical px
    render(
      <BodySection
        actor={actor}
        world={world}
        resolved={resolvedFromWorldAndOverrides(world, actor)}
        onChangeActor={vi.fn()}
      />,
    );
    expect(screen.getByText("210px × 1.3 = 273px")).toBeInTheDocument();
  });

  it("does not write a half-typed number over the paired field", () => {
    const ctx = renderBody();

    const heightInput = screen.getByLabelText("목표 물리 높이(px)");
    // Clearing the field to retype it must not commit an empty or partial size.
    fireEvent.change(heightInput, { target: { value: "" } });
    fireEvent.change(heightInput, { target: { value: "2" } });
    fireEvent.change(heightInput, { target: { value: "29" } });
    expect(ctx.latestActor.overrides.anatomy).toBeUndefined();

    fireEvent.change(heightInput, { target: { value: "290" } });
    fireEvent.blur(heightInput);
    expect(ctx.latestActor.overrides.anatomy?.targetPhysicalHeightPx).toBe(290);
  });

  it("restores the canonical value when the field is left unusable", () => {
    const ctx = renderBody();

    const scaleInput = screen.getByLabelText("Species Scale") as HTMLInputElement;
    fireEvent.change(scaleInput, { target: { value: "" } });
    fireEvent.blur(scaleInput);

    expect(ctx.latestActor.overrides.anatomy).toBeUndefined();
    expect(scaleInput.value).toBe("1");
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
