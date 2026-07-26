import "../../app/uiTestCleanup";
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { ViewDirectionPanel } from "./ViewDirectionPanel";
import { makeActor, makeWorld, resolvedFromWorldAndOverrides } from "../../app/testFixtures";
import { viewOriginOf } from "../../app/view";

const resolvedOf = (world = makeWorld(), actor = makeActor()) => resolvedFromWorldAndOverrides(world, actor);

describe("ViewDirectionPanel", () => {
  it("shows the effective direction and the master prompt, read-only", () => {
    render(<ViewDirectionPanel resolved={resolvedOf()} origin="world" />);

    expect(screen.getByDisplayValue("three-quarter")).toBeDisabled();
    expect(screen.getByDisplayValue("screen-right")).toBeDisabled();
    expect(screen.getByDisplayValue("upper-left")).toBeDisabled();
    expect(screen.getByDisplayValue("World Template")).toBeDisabled();
    expect(screen.getByText(
      "front-biased three-quarter full-body view, facing screen-right, with lighting from the upper-left.",
    )).toBeInTheDocument();
  });

  it("fills a world that predates the light-direction field from the project default", () => {
    const world = makeWorld();
    world.defaults.view = { projection: "three-quarter", facing: "screen-right" };
    render(<ViewDirectionPanel resolved={resolvedOf(world)} origin={viewOriginOf(makeActor(), world)} />);

    expect(screen.getByDisplayValue("upper-left")).toBeInTheDocument();
    expect(screen.getByDisplayValue("World Template")).toBeInTheDocument();
  });

  it("flags the project fallback when no document records a direction", () => {
    const world = makeWorld();
    world.defaults.view = {};
    render(<ViewDirectionPanel resolved={resolvedOf(world)} origin={viewOriginOf(makeActor(), world)} />);

    expect(screen.getByDisplayValue("프로젝트 기본값")).toBeInTheDocument();
    expect(screen.getByText(/World Template에 방향 값이 없어/)).toBeInTheDocument();
  });
});
