/**
 * Reference-only catalog of weapon families for UI labels and picker
 * grouping. This is NOT the validation source of truth — validateActor()
 * (Codex) decides what is actually allowed for a given actor/world.
 *
 * Confirmed entries come from actual approved Master Design briefs.
 * Speculative entries are extrapolated from Concept-status actors whose
 * Master Design has not been approved yet (see Wave 1 research, risk #7).
 */
export interface WeaponCatalogEntry {
  id: string;
  labelKo: string;
  labelEn: string;
  status: "confirmed" | "speculative";
  evidence: string;
}

export const WEAPON_CATALOG: WeaponCatalogEntry[] = [
  {
    id: "one-handed-axe-dual",
    labelKo: "쌍도끼 (한손 도끼 x2)",
    labelEn: "Dual one-handed axes",
    status: "confirmed",
    evidence: "CopperAxeBarbarian / Barbarian brief — 양손에 동일 계열 한손 도끼",
  },
  {
    id: "glaive",
    labelKo: "글레이브",
    labelEn: "Glaive",
    status: "confirmed",
    evidence: "LeafGlaiveElf / ElfGuardian brief — 곧은 창대의 한쪽 곡선 날 글레이브",
  },
  {
    id: "staff",
    labelKo: "지팡이",
    labelEn: "Staff",
    status: "confirmed",
    evidence: "BlackCatMage / CatMage brief — 목재 지팡이, 붉은 마법석",
  },
  {
    id: "dagger",
    labelKo: "단검",
    labelEn: "Dagger",
    status: "confirmed",
    evidence: "VenomCultist brief — 한손 독 단검",
  },
  {
    id: "bow",
    labelKo: "활",
    labelEn: "Bow",
    status: "speculative",
    evidence: "FoxArcher (Concept, Animal Land) — Master Design 승인 전",
  },
  {
    id: "greatsword",
    labelKo: "양손검",
    labelEn: "Greatsword",
    status: "speculative",
    evidence: "DragonWarrior (Concept, Animal Land) — Master Design 승인 전",
  },
  {
    id: "unarmed",
    labelKo: "무기 없음",
    labelEn: "Unarmed",
    status: "confirmed",
    evidence: "다수 액터의 기본 상태",
  },
];
