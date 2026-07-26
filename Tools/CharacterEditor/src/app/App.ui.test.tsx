import "./uiTestCleanup";
import { describe, expect, it } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { App } from "./App";

/**
 * Wave 2 dispatched the UI (Claude) and the domain/schema/data implementation
 * (Codex) in parallel, so src/domain, src/export, and src/data may not exist
 * yet when this test runs. App.tsx loads them via a dynamic import specifically
 * so a missing/failed module degrades to a visible status banner instead of a
 * white screen or an uncaught exception — this test locks in that contract.
 * Once Codex's modules land, this test still passes (it only asserts the app
 * never crashes and always ends up in a visible, non-blank state).
 */
describe("App", () => {
  it("shows a loading state and then resolves to either the library or a clear connection error, without crashing", async () => {
    render(<App />);

    expect(screen.getByText(/불러오는 중입니다/)).toBeInTheDocument();

    await waitFor(
      () => {
        const stillLoading = screen.queryByText(/불러오는 중입니다/);
        expect(stillLoading).not.toBeInTheDocument();
      },
      { timeout: 5000 },
    );

    const errorBanner = screen.queryByText(/도메인 모듈을 연결하지 못했습니다/);
    const library = screen.queryByText("라이브러리");
    expect(errorBanner ?? library).not.toBeNull();
  });

  it("opens the real ElfGuardian sample, compares it with VenomCultist, and previews export", async () => {
    window.localStorage.clear();
    render(<App />);
    await screen.findByText("라이브러리");

    expect(screen.getByText(/ANIMAL-LAND-01/)).toBeInTheDocument();
    expect(screen.getAllByText(/HUMAN-FANTASY-01/).length).toBeGreaterThan(0);
    expect(screen.getByText(/UNDEAD-WORLD-01/)).toBeInTheDocument();
    const elfCard = screen.getByText(/ElfGuardian/).closest("article");
    expect(elfCard).not.toBeNull();
    fireEvent.click(within(elfCard!).getByRole("button", { name: "열기" }));

    expect(await screen.findByDisplayValue("ElfGuardian")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Body & Proportions" }));
    // 91 logical px at the world's 3x3 density is authored as 273 physical px;
    // the logical height is shown as a derived read-only value.
    expect(screen.getByDisplayValue("273")).toBeInTheDocument();
    expect(screen.getByText("273 ÷ 3 = 91 logical px")).toBeInTheDocument();
    // 273px against Fantasia's 210px baseline, so the linked scale reads 1.3.
    const speciesField = screen.getByText("Species Scale").closest(".ce-field");
    expect(speciesField?.querySelector("input")).toHaveValue(1.3);
    expect(screen.getByText("210px × 1.3 = 273px")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "같은 세계 액터와 비교" }));
    fireEvent.change(screen.getByLabelText(/비교 대상/), { target: { value: "VenomCultist" } });
    expect(await screen.findByText("Logical height")).toBeInTheDocument();
    expect(screen.getByText("+21 (+30.0%)")).toBeInTheDocument();
    // Same 30% size gap expressed in physical px, the density-independent metric.
    expect(screen.getByText("Physical height")).toBeInTheDocument();
    expect(screen.getByText("+63 (+30.0%)")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "돌아가기" }));

    fireEvent.click(screen.getByRole("button", { name: /Export 미리보기/ }));
    expect(await screen.findByRole("heading", { name: "Export 미리보기" })).toBeInTheDocument();
    expect(screen.getByText("JSON 다운로드")).toBeInTheDocument();
    expect(screen.getByText("Markdown 다운로드")).toBeInTheDocument();
  });
});
