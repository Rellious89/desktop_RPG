import { describe, expect, it } from "vitest";
import animalJson from "../../../../ProjectDocs/CharacterEditor/Data/worlds/ANIMAL-LAND-01/v1.world.json";
import fantasiaJson from "../../../../ProjectDocs/CharacterEditor/Data/worlds/HUMAN-FANTASY-01/v1.world.json";
import undeadJson from "../../../../ProjectDocs/CharacterEditor/Data/worlds/UNDEAD-WORLD-01/v1.world.json";
import elfJson from "../../../../ProjectDocs/CharacterEditor/Data/actors/ElfGuardian.character.json";
import venomJson from "../../../../ProjectDocs/CharacterEditor/Data/actors/VenomCultist.character.json";
import elfExport from "../../../../ProjectDocs/CharacterEditor/Exports/ElfGuardian/ElfGuardian.character.json";
import venomExport from "../../../../ProjectDocs/CharacterEditor/Exports/VenomCultist/VenomCultist.character.json";
import { parseActor, parseWorld } from "../domain";

describe("checked-in samples", () => {
  it("parses every world and actor with executable Schema v1", () => {
    expect([animalJson, fantasiaJson, undeadJson].map(parseWorld).map((w) => w.worldId)).toEqual(["ANIMAL-LAND-01", "HUMAN-FANTASY-01", "UNDEAD-WORLD-01"]);
    expect([elfJson, venomJson].map(parseActor).map((a) => a.actorId)).toEqual(["ElfGuardian", "VenomCultist"]);
  });
  it("imports both generated export envelopes", () => {
    expect(parseActor(elfExport).actorId).toBe("ElfGuardian");
    expect(parseActor(venomExport).actorId).toBe("VenomCultist");
  });
  it("locks ElfGuardian's independent scale semantics", () => {
    const envelope = elfExport as typeof elfExport & { resolved: { anatomy: { targetLogicalHeightPx: number; speciesScale: number }; production: { unityVisualScale: number } } };
    expect(envelope.resolved.anatomy.targetLogicalHeightPx).toBe(91);
    expect(envelope.resolved.anatomy.speciesScale).toBe(1);
    expect(envelope.resolved.production.unityVisualScale).toBe(1);
  });
});
