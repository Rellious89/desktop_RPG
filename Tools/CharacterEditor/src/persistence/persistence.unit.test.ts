import { describe, expect, it } from "vitest";
import { elfGuardian, fantasia } from "../data";
import { actorDraftKey, assertSafeId, importActorJson, loadActorDraft, saveActorDraft } from ".";

describe("persistence", () => {
  it("round-trips drafts", () => {
    const values = new Map<string, string>();
    const storage = { getItem: (k: string) => values.get(k) ?? null, setItem: (k: string, v: string) => { values.set(k, v); }, removeItem() {}, clear() {}, key() { return null; }, length: 0 } as Storage;
    saveActorDraft(elfGuardian, storage);
    expect(values.has(actorDraftKey("ElfGuardian"))).toBe(true);
    expect(loadActorDraft("ElfGuardian", storage)?.worldRef.worldId).toBe(fantasia.worldId);
  });
  it("imports authored/envelope JSON and rejects unsafe IDs", () => {
    expect(importActorJson(JSON.stringify(elfGuardian)).actorId).toBe("ElfGuardian");
    expect(assertSafeId("ElfGuardian")).toBe("ElfGuardian");
    expect(() => assertSafeId("../ElfGuardian")).toThrow();
    expect(() => assertSafeId("bad/name")).toThrow();
  });
});
