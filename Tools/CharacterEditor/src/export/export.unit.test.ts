import { describe, expect, it } from "vitest";
import { elfGuardian, fantasia, venomCultist } from "../data";
import { buildExport, exportJson, exportMarkdown, serializeActor } from ".";
import { parseActor } from "../domain";

describe("deterministic exports", () => {
  it("is stable, LF-terminated and importable from envelope", () => {
    const envelope = buildExport(elfGuardian, fantasia, [venomCultist]);
    expect(exportJson(envelope)).toBe(exportJson(buildExport(elfGuardian, fantasia, [venomCultist])));
    expect(exportJson(envelope).endsWith("\n")).toBe(true);
    expect(parseActor(JSON.parse(exportJson(envelope))).actorId).toBe("ElfGuardian");
    expect(serializeActor(elfGuardian)).toContain('"schemaVersion": "1.0.0"');
  });
  it("includes origins, interpretations, diagnostics, exceptions and comparison in Markdown", () => {
    const envelope = buildExport(elfGuardian, fantasia, [venomCultist]);
    const markdown = exportMarkdown(envelope);
    expect(markdown).toContain("Target logical height | 91px");
    expect(markdown).toContain("Species scale | 1");
    expect(markdown).toContain("Unity visual scale: 1");
    expect(markdown).toContain("Approved Exceptions");
    expect(markdown).toContain("Comparison Snapshot (vs VenomCultist)");
    expect(markdown).toContain("| Stature | tall | average | Mismatch | — |");
    expect(markdown).toContain("| Proportion | humanoid-sd-2.5-head | humanoid-sd-2.5-head | Match | — |");
    expect(exportJson(envelope)).toContain('"matches": false');
    expect(exportJson(envelope)).toContain('"matches": true');
    expect(markdown.endsWith("\n")).toBe(true);
  });
});
