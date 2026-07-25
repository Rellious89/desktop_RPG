import "../../app/uiTestCleanup";
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { ComparisonView } from "./ComparisonView";
import { makeActor } from "../../app/testFixtures";
import type { ComparisonResult } from "../../app/types";

/** Comparison fixtures use the domain-owned explicit `matches` contract. */
type MetricInput = ComparisonResult["metrics"][number];

function comparisonWith(metrics: MetricInput[]): ComparisonResult {
  return { referenceActorId: "Ref", metrics, diagnostics: [] };
}

describe("ComparisonView", () => {
  it("shows a graceful empty state when there is no same-world reference actor", () => {
    render(
      <ComparisonView
        actor={makeActor()}
        candidates={[]}
        selectedReferenceId={null}
        comparison={null}
        onSelectReference={vi.fn()}
        onBack={vi.fn()}
      />,
    );
    expect(screen.getByText(/비교할 수 있는 Master\/Active 상태의 기존 액터가 아직 없습니다/)).toBeInTheDocument();
  });

  it("lists candidates and renders comparison rows with deltas and flags once one is picked", () => {
    const reference = makeActor({
      actorId: "VenomCultist",
      displayName: { en: "Venom Cultist", ko: "독단검 이교도" },
      identity: { ...makeActor().identity, status: "master" },
    });
    const onSelect = vi.fn();
    const metrics: MetricInput[] = [
      { key: "height", label: "Logical height", draft: 91, reference: 70, absoluteDelta: 21, percentDelta: 30 },
      { key: "speciesScale", label: "Species scale", draft: 1, reference: 1, absoluteDelta: 0, percentDelta: 0 },
    ];
    render(
      <ComparisonView
        actor={makeActor()}
        candidates={[reference]}
        selectedReferenceId="VenomCultist"
        comparison={{ referenceActorId: "VenomCultist", metrics, diagnostics: [] }}
        onSelectReference={onSelect}
        onBack={vi.fn()}
      />,
    );

    expect(screen.getByRole("option", { name: /독단검 이교도/ })).toBeInTheDocument();
    expect(screen.getByText("+21 (+30.0%)")).toBeInTheDocument();
    expect(screen.getByText("0 (0.0%)")).toBeInTheDocument();
    expect(screen.getByText("참고")).toBeInTheDocument();
    expect(screen.getByText("일치")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/비교 대상/), { target: { value: "VenomCultist" } });
    expect(onSelect).toHaveBeenCalledWith("VenomCultist");
  });

  it("flags a differing text metric (stature: tall vs average) as 다름/참고, trusting an explicit matches=false", () => {
    const metrics: MetricInput[] = [
      { key: "stature", label: "Stature", draft: "tall", reference: "average", matches: false },
    ];
    render(
      <ComparisonView
        actor={makeActor()}
        candidates={[makeActor({ actorId: "Ref" })]}
        selectedReferenceId="Ref"
        comparison={comparisonWith(metrics)}
        onSelectReference={vi.fn()}
        onBack={vi.fn()}
      />,
    );

    expect(screen.getByText("다름")).toBeInTheDocument();
    expect(screen.getByText("참고")).toBeInTheDocument();
    expect(screen.queryByText("동일")).not.toBeInTheDocument();
  });

  it("flags a matching text metric (build: normal vs normal) as 동일/일치, trusting an explicit matches=true", () => {
    const metrics: MetricInput[] = [
      { key: "build", label: "Build", draft: "normal", reference: "normal", matches: true },
    ];
    render(
      <ComparisonView
        actor={makeActor()}
        candidates={[makeActor({ actorId: "Ref" })]}
        selectedReferenceId="Ref"
        comparison={comparisonWith(metrics)}
        onSelectReference={vi.fn()}
        onBack={vi.fn()}
      />,
    );

    expect(screen.getByText("동일")).toBeInTheDocument();
    expect(screen.getByText("일치")).toBeInTheDocument();
  });

  it("flags a differing large-motion canvas metric (1024x1024 vs same-as-base) as 다름/참고", () => {
    const metrics: MetricInput[] = [
      {
        key: "largeMotionCanvas",
        label: "Large-motion canvas",
        draft: "1024×1024",
        reference: "same-as-base",
        matches: false,
      },
    ];
    render(
      <ComparisonView
        actor={makeActor()}
        candidates={[makeActor({ actorId: "Ref" })]}
        selectedReferenceId="Ref"
        comparison={comparisonWith(metrics)}
        onSelectReference={vi.fn()}
        onBack={vi.fn()}
      />,
    );

    expect(screen.getByText("다름")).toBeInTheDocument();
    expect(screen.getByText("참고")).toBeInTheDocument();
  });

  it("falls back to strict-equality when the domain omits an explicit matches field on a text metric", () => {
    const metrics: MetricInput[] = [
      // Legacy/imported metric without `matches`: strict-equality fallback remains safe.
      { key: "proportion", label: "Proportion", draft: "standard", reference: "standard" },
      // Legacy/imported metric without `matches`: differing strings remain a mismatch.
      { key: "stature", label: "Stature", draft: "tall", reference: "average" },
    ];
    render(
      <ComparisonView
        actor={makeActor()}
        candidates={[makeActor({ actorId: "Ref" })]}
        selectedReferenceId="Ref"
        comparison={comparisonWith(metrics)}
        onSelectReference={vi.fn()}
        onBack={vi.fn()}
      />,
    );

    expect(screen.getByText("동일")).toBeInTheDocument();
    expect(screen.getByText("일치")).toBeInTheDocument();
    expect(screen.getByText("다름")).toBeInTheDocument();
    expect(screen.getByText("참고")).toBeInTheDocument();
  });

  it("keeps numeric delta formatting and zero/nonzero flag behavior when matches is not explicitly provided", () => {
    const metrics: MetricInput[] = [
      { key: "height", label: "Logical height", draft: 91, reference: 70, absoluteDelta: 21, percentDelta: 30 },
      { key: "speciesScale", label: "Species scale", draft: 1, reference: 1, absoluteDelta: 0 },
    ];
    render(
      <ComparisonView
        actor={makeActor()}
        candidates={[makeActor({ actorId: "Ref" })]}
        selectedReferenceId="Ref"
        comparison={comparisonWith(metrics)}
        onSelectReference={vi.fn()}
        onBack={vi.fn()}
      />,
    );

    expect(screen.getByText("+21 (+30.0%)")).toBeInTheDocument();
    expect(screen.getByText("참고")).toBeInTheDocument();
    expect(screen.getByText("0")).toBeInTheDocument();
    expect(screen.getByText("일치")).toBeInTheDocument();
  });
});
