import "../../app/uiTestCleanup";
import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { ExportPreview } from "./ExportPreview";
import { makeActor, makeDiagnostic, resolvedFromWorldAndOverrides, makeWorld } from "../../app/testFixtures";
import type { ExportEnvelope } from "../../app/types";

function makeEnvelope(diagnostics: ExportEnvelope["diagnostics"]): ExportEnvelope {
  const actor = makeActor();
  const world = makeWorld();
  return {
    schemaVersion: "1.0.0",
    documentKind: "actor-export",
    authored: actor,
    resolved: resolvedFromWorldAndOverrides(world, actor),
    fieldOrigins: {},
    calculated: {},
    interpretations: [],
    diagnostics,
  };
}

describe("ExportPreview", () => {
  it("hides download actions and explains the block when a blocking diagnostic remains", () => {
    const envelope = makeEnvelope([makeDiagnostic({ blocksExport: true })]);
    render(
      <ExportPreview
        envelope={envelope}
        jsonText="{}"
        markdownText="# ElfGuardian"
        onDownloadJson={vi.fn()}
        onDownloadMarkdown={vi.fn()}
        onBack={vi.fn()}
      />,
    );
    expect(screen.getByText("Export가 차단되었습니다.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "JSON 다운로드" })).not.toBeInTheDocument();
  });

  it("switches between Markdown and JSON tabs and triggers downloads", () => {
    const envelope = makeEnvelope([]);
    const onDownloadJson = vi.fn();
    const onDownloadMarkdown = vi.fn();
    render(
      <ExportPreview
        envelope={envelope}
        jsonText='{"actorId":"ElfGuardian"}'
        markdownText="# ElfGuardian sheet"
        onDownloadJson={onDownloadJson}
        onDownloadMarkdown={onDownloadMarkdown}
        onBack={vi.fn()}
      />,
    );

    expect(screen.getByText("# ElfGuardian sheet")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "JSON" }));
    expect(screen.getByText('{"actorId":"ElfGuardian"}')).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "JSON 다운로드" }));
    expect(onDownloadJson).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole("button", { name: "Markdown 다운로드" }));
    expect(onDownloadMarkdown).toHaveBeenCalledTimes(1);
  });

  it("shows the effective direction being exported, and its origin", () => {
    const envelope = makeEnvelope([]);
    envelope.fieldOrigins = { "view.facing": { source: "world", documentId: "HUMAN-FANTASY-01", version: 1 } };
    render(
      <ExportPreview
        envelope={envelope}
        jsonText="{}"
        markdownText="# ElfGuardian"
        onDownloadJson={vi.fn()}
        onDownloadMarkdown={vi.fn()}
        onBack={vi.fn()}
      />,
    );
    expect(screen.getByDisplayValue("screen-right")).toBeDisabled();
    expect(screen.getByDisplayValue("upper-left")).toBeDisabled();
    expect(screen.getByDisplayValue("World Template")).toBeInTheDocument();
  });
});
