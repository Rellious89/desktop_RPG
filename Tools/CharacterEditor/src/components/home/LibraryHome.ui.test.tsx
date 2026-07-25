import "../../app/uiTestCleanup";
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { LibraryHome } from "./LibraryHome";
import { makeActor, makeWorld } from "../../app/testFixtures";

describe("LibraryHome", () => {
  it("renders bundled worlds/actors and an empty-state message for drafts", () => {
    render(
      <LibraryHome
        worlds={[makeWorld()]}
        actors={[makeActor()]}
        drafts={[]}
        onOpenWorld={vi.fn()}
        onNewWorld={vi.fn()}
        onOpenActor={vi.fn()}
        onNewActor={vi.fn()}
        onOpenDraft={vi.fn()}
        onDeleteDraft={vi.fn()}
        onImport={vi.fn()}
      />,
    );
    expect(screen.getByText("판타지아")).toBeInTheDocument();
    expect(screen.getByText("나뭇잎 글레이브 엘프")).toBeInTheDocument();
    expect(screen.getByText("저장된 초안이 없습니다. 액터를 편집하면 자동으로 저장됩니다.")).toBeInTheDocument();
  });

  it("wires up primary actions and per-item callbacks", () => {
    const onNewWorld = vi.fn();
    const onNewActor = vi.fn();
    const onImport = vi.fn();
    const onOpenActor = vi.fn();
    const onDeleteDraft = vi.fn();

    render(
      <LibraryHome
        worlds={[]}
        actors={[makeActor()]}
        drafts={[makeActor({ updatedAt: "2026-07-25T00:00:00.000Z" })]}
        onOpenWorld={vi.fn()}
        onNewWorld={onNewWorld}
        onOpenActor={onOpenActor}
        onNewActor={onNewActor}
        onOpenDraft={vi.fn()}
        onDeleteDraft={onDeleteDraft}
        onImport={onImport}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "+ 새 World 템플릿" }));
    fireEvent.click(screen.getByRole("button", { name: "+ 새 액터" }));
    fireEvent.click(screen.getByRole("button", { name: "JSON 가져오기" }));
    expect(onNewWorld).toHaveBeenCalledTimes(1);
    expect(onNewActor).toHaveBeenCalledTimes(1);
    expect(onImport).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getAllByRole("button", { name: "열기" })[0]);
    expect(onOpenActor).toHaveBeenCalledWith(expect.objectContaining({ actorId: "ElfGuardian" }));

    fireEvent.click(screen.getByRole("button", { name: "삭제" }));
    expect(onDeleteDraft).toHaveBeenCalledWith("ElfGuardian");
  });
});
