import type { ActorDocumentV1, WorldTemplateV1 } from "../schema";

const updatedAt = "2026-07-25T00:00:00+09:00";
const commonDefaults: WorldTemplateV1["defaults"] = {
  view: { projection: "three-quarter", facing: "screen-right" },
  pixelStyle: { styleId: "low-companion-v1", logicalBlockPx: { widthPx: 3, heightPx: 3 }, outline: "Thick stepped near-black outline", lighting: "Upper-left" },
  anatomy: { stature: "average", targetLogicalHeightPx: 70, build: "normal", proportionTemplateId: "humanoid-sd-2.5-head", speciesScale: 1, headSize: "m", handSize: "m", footSize: "m", torsoWidth: "normal", isFloatingActor: false },
  production: { baseCanvas: { widthPx: 512, heightPx: 512 }, largeMotionCanvas: { policy: "same-as-base" }, pivotRule: "forward-foot-contact", pixelsPerUnit: 200, unityVisualScale: 1, layers: ["character-or-outfit", "weapon", "effect"] },
};

const world = (worldId: string, en: string, ko: string, description: string, species: string[]): WorldTemplateV1 => ({
  schemaVersion: "1.0.0", documentKind: "world-template", revision: 1, updatedAt, worldId,
  displayName: { en, ko }, status: "active", description, defaults: structuredClone(commonDefaults),
  allowedSpecies: species, allowedProportionTemplates: ["humanoid-sd-2.5-head", "anthropomorphic-sd", "floating-spirit"],
  evidence: [{ path: "ProjectDocs/DesignRules/world-setting-rules.md", claim: `Registered world ${worldId}`, status: "approved" }, { path: "ProjectDocs/DesignRules/character-sprite-and-animator-rules.md", claim: "Low Companion v1, 512 canvas and PPU 200", status: "approved" }],
});

export const animalLand = world("ANIMAL-LAND-01", "Animal Land", "애니멀랜드", "Medieval fantasy world of anthropomorphic animal peoples.", ["cat-folk", "dog-folk", "dragon-folk"]);
export const fantasia = world("HUMAN-FANTASY-01", "Fantasia", "판타지아", "Medieval fantasy world of human kingdoms, mercenaries, and elf forest groups.", ["human", "elf"]);
export const undeadWorld = world("UNDEAD-WORLD-01", "World of the Dead", "망자들의 세계", "Dark fantasy world of undead, spirits, curses, and ancient weapons.", ["specter", "skeleton", "zombie", "spirit"]);

export const elfGuardian: ActorDocumentV1 = {
  schemaVersion: "1.0.0", documentKind: "actor", revision: 1, updatedAt, actorId: "ElfGuardian",
  displayName: { en: "Leaf Glaive Elf", ko: "나뭇잎 글레이브 엘프" }, aliases: ["LeafGlaiveElf", "Elfguardian"], actorType: "character",
  worldRef: { worldId: fantasia.worldId, version: 1 },
  identity: { species: "elf", sex: "unknown", ageGroup: "adult", role: "Glaive warrior / long-reach melee", concept: "A blond elf warrior in light forest clothing wielding a straight-shaft leaf glaive.", status: "master" },
  overrides: {
    anatomy: { stature: "tall", targetLogicalHeightPx: 91, build: "slender", proportionTemplateId: "humanoid-sd-2.5-head", speciesScale: 1, headSize: "m", handSize: "m", footSize: "m", torsoWidth: "normal" },
    pixelStyle: { outline: "One logical pixel, dark brown-green" },
    production: { largeMotionCanvas: { policy: "explicit", widthPx: 1024, heightPx: 1024 } },
  },
  physicalTraits: ["Long pointed ears", "Tall slender humanoid stature"],
  appearance: { hair: "Blond", skin: "Light", clothing: ["Light forest cloth", "Leaf decorations"], materials: ["Cloth", "Wood", "Metal"], palette: [{ role: "hair", value: "golden blond" }, { role: "clothing", value: "leaf green" }], decorations: ["Leaf motifs"] },
  constraints: { invariants: ["Long exposed pointed ears", "Straight white glaive shaft", "One curved forward blade", "Blunt yellow rear ornament"], forbidden: ["Bow", "Shield", "Heavy plate", "Double-ended blade", "Weapon longer than 1.25 body heights"] },
  equipment: { weapon: { family: "glaive", sizeClass: "large", mainHand: "both", offHand: "both", direction: "Curved blade screen-right/up", structure: "One straight shaft, one front blade, blunt rear ornament", count: 1, lengthToBodyRatio: 1.2, estimatedOccupancyPercent: 65 }, secondary: [], allowedWeaponFamilies: ["glaive", "polearm"] },
  approvedExceptions: [{ ruleId: "large-motion-canvas-exception", reason: "Existing ElfGuardian attack frames are approved 1024×1024 observed assets.", active: true }],
  evidence: [
    { path: "ProjectDocs/ArtPipeline/Characters/LeafGlaiveElf/01_character-brief.md", claim: "Design identity, 91px target and glaive constraints", status: "approved" },
    { path: "ProjectDocs/ArtPipeline/Characters/LeafGlaiveElf/03_master-measurements.md", claim: "Legacy actual scale 1.3 is historical production sizing only; it is not species or Unity scale", status: "historical" },
    { path: "Assets/Art/Character/ElfGuardian", claim: "Shipped resource folder and canonical editor ID", status: "observed" },
  ], resourceFolderPath: "Assets/Art/Character/ElfGuardian",
};

export const venomCultist: ActorDocumentV1 = {
  schemaVersion: "1.0.0", documentKind: "actor", revision: 1, updatedAt, actorId: "VenomCultist",
  displayName: { en: "Venom Cultist", ko: "독단검 이교도" }, aliases: [], actorType: "monster", worldRef: { worldId: fantasia.worldId, version: 1 },
  identity: { species: "human", sex: "unknown", ageGroup: "adult", role: "Poison-dagger cultist", concept: "A living human cultist hidden in a dark-purple hood, carrying one poisoned dagger.", status: "active" },
  overrides: { anatomy: { stature: "average", targetLogicalHeightPx: 70, build: "normal", proportionTemplateId: "humanoid-sd-2.5-head", speciesScale: 1, headSize: "m", handSize: "m", footSize: "m", torsoWidth: "normal" }, production: { pivot: { xNormalized: 0.5234375, yNormalized: 0.2578125, source: "measured" } }, pixelStyle: { outline: "One logical pixel, near-black purple" } },
  physicalTraits: ["Living human", "Face almost fully hidden in hood shadow"],
  appearance: { eyes: "Hidden", skin: "Mostly hidden", clothing: ["Deep dark-purple hooded robe"], materials: ["Cloth", "Bone", "Metal", "Poison"], palette: [{ role: "robe", value: "dark purple" }, { role: "poison", value: "green" }], decorations: ["Large bone-white skull pendant"] },
  constraints: { invariants: ["Hood shadow hides face", "One centered skull pendant", "Exactly one short poisoned dagger"], forbidden: ["Undead body", "Second weapon", "Staff", "Shield", "Heavy armor"] },
  equipment: { weapon: { family: "dagger", sizeClass: "small", mainHand: "anatomical-right", offHand: "none", direction: "Held forward", structure: "One short poison-coated dagger", count: 1, estimatedOccupancyPercent: 35 }, secondary: ["Skull pendant"], allowedWeaponFamilies: ["dagger"] },
  approvedExceptions: [], evidence: [{ path: "ProjectDocs/ArtPipeline/Enemies/VenomCultist/01_monster-brief.md", claim: "Identity, 70px, 512 canvas, scale 1.0", status: "approved" }, { path: "ProjectDocs/ArtPipeline/Enemies/VenomCultist/03_master-measurements.md", claim: "Measured pivot 0.5234375 / 0.2578125", status: "approved" }],
  resourceFolderPath: "Assets/Art/Enemy/VenomCultist",
};

export const bundledWorlds = [animalLand, fantasia, undeadWorld];
export const bundledActors = [elfGuardian, venomCultist];

