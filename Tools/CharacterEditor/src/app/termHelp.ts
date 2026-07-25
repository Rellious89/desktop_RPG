/**
 * Short in-UI explanations for specialized terms, grounded in
 * ProjectDocs/CharacterEditor/02_claude-ux-validation-research.md section 0
 * and ProjectDocs/DesignRules. Every inheritable/specialized field links here
 * via the (?) affordance instead of inventing new wording.
 */
export interface TermHelpEntry {
  termKo: string;
  termEn: string;
  helpKo: string;
  helpEn: string;
}

export const TERM_HELP: Record<string, TermHelpEntry> = {
  stature: {
    termKo: "Stature 등급",
    termEn: "Stature class",
    helpKo:
      "체격의 '크기 계급'만 나타냅니다. 종족 자체가 균일하게 큰지(Species Scale)와는 별개의 값입니다.",
    helpEn:
      "The size class of the body only — kept separate from Species Scale, which represents a uniformly larger species/individual.",
  },
  targetLogicalHeightPx: {
    termKo: "목표 논리 높이(px)",
    termEn: "Target logical height (px)",
    helpKo:
      "머리끝부터 접지점(또는 부유형은 바닥 투영점)까지의 논리 픽셀 높이입니다. 일반 액터는 65~75px 권장, 벗어나면 승인된 예외가 필요합니다.",
    helpEn:
      "Logical pixel height from head to ground/floor-projection contact. General actors target 65-75px; values outside that range need an approved exception.",
  },
  buildClass: {
    termKo: "Build 등급",
    termEn: "Build class",
    helpKo: "골격/근육량 인상만 나타냅니다. 몸통 너비 등급과 조합해 실루엣을 결정합니다.",
    helpEn: "Skeletal/muscle impression only. Combines with torso width to shape the silhouette.",
  },
  proportionTemplateId: {
    termKo: "비율 템플릿",
    termEn: "Proportion template",
    helpKo: "머리:몸 비율(예: 약 2.5등신) 유형입니다. 같은 세계/종족 내 다른 액터와 비교해 급격히 다르면 경고가 뜹니다.",
    helpEn: "Head-to-body ratio family (e.g. ~2.5 heads tall). Large mismatches vs. same-world/species actors trigger a warning.",
  },
  speciesScale: {
    termKo: "Species Scale",
    termEn: "Species Scale",
    helpKo:
      "이 개체/종족이 표준 개체 대비 전신을 균일하게 얼마나 키우는지 나타내는 배율입니다. 단순히 키 목표치를 바꾼 것과 혼동하면 경고가 뜹니다(Stature-Species Scale 충돌 규칙).",
    helpEn:
      "Whole-body multiplier for a genuinely larger species/individual. Conflating this with a simple height-target change triggers the stature/species-scale-conflation warning.",
  },
  headSize: {
    termKo: "머리 크기 등급",
    termEn: "Head size class",
    helpKo: "동급 휴머노이드 대비 머리 크기입니다.",
    helpEn: "Head size relative to comparable humanoids.",
  },
  handSize: {
    termKo: "손 크기 등급",
    termEn: "Hand size class",
    helpKo: "동급 휴머노이드 대비 손 크기입니다.",
    helpEn: "Hand size relative to comparable humanoids.",
  },
  footSize: {
    termKo: "발 크기 등급",
    termEn: "Foot size class",
    helpKo: "동급 휴머노이드 대비 발 크기입니다.",
    helpEn: "Foot size relative to comparable humanoids.",
  },
  torsoWidth: {
    termKo: "몸통 너비 등급",
    termEn: "Torso width class",
    helpKo: "Normal Build와 Wide Torso가 함께 있으면 실루엣 불일치 경고가 뜹니다.",
    helpEn: "Normal build combined with wide torso raises a silhouette-mismatch warning.",
  },
  pivotRule: {
    termKo: "Pivot 규칙",
    termEn: "Pivot rule",
    helpKo:
      "전방 디딤발 접지점을 기준으로 하는 일반 규칙과, 발 접지가 없는 부유형(예: Specter류) 액터를 위한 바닥 투영점 규칙 중 하나입니다.",
    helpEn:
      "Either the forward-planted-foot contact point (ground actors) or the floor-projection point (floating actors with no foot contact, e.g. Specter-like monsters).",
  },
  ppu: {
    termKo: "PPU (Pixels Per Unit)",
    termEn: "PPU (Pixels Per Unit)",
    helpKo: "프로젝트 전역 상수(200)입니다. 캐릭터마다 다르면 씬 안에서 상대 크기가 어긋나므로 MVP에서는 변경할 수 없습니다.",
    helpEn: "Project-wide constant (200). Different values per character break relative scale in-scene, so MVP does not allow overriding it.",
  },
  unityVisualScale: {
    termKo: "Unity 표시 배율",
    termEn: "Unity visual scale",
    helpKo:
      "기본값은 1.0입니다. 체격 보정 용도로 이 값을 바꾸는 것은 정책상 금지되며(BlackCatMage 0.35 실험 기각 전례), 예외 시 사유가 필요합니다.",
    helpEn:
      "Defaults to 1.0. Using this to compensate for body-size differences is against policy (see the rejected BlackCatMage 0.35 experiment); overriding requires a reason.",
  },
  largeMotionCanvas: {
    termKo: "대형 모션 캔버스",
    termEn: "Large-motion canvas",
    helpKo: "512×512 이외의 값은 아직 공통 규칙으로 확정되지 않은 실험적 설정입니다.",
    helpEn: "Any value other than 512x512 is an experimental setting not yet confirmed as a common rule.",
  },
  weaponFamily: {
    termKo: "무기 계열",
    termEn: "Weapon family",
    helpKo: "액터/세계 단위로 허용된 무기 계열만 선택할 수 있습니다. 비허용 계열은 승인된 예외가 있어야 내보낼 수 있습니다.",
    helpEn: "Only weapon families allowed for this actor/world can be picked. Disallowed families need an approved exception to export.",
  },
  weaponSizeRatio: {
    termKo: "무기 크기 비율",
    termEn: "Weapon size ratio",
    helpKo: "캐릭터 신장 대비 무기 전체 길이 비율입니다. 값이 크면 512 캔버스 점유율 경고가 뜰 수 있습니다.",
    helpEn: "Weapon length as a ratio of character height. Large values may trigger the large-weapon-canvas warning.",
  },
  productionLayers: {
    termKo: "생산 레이어 정책",
    termEn: "Production layer policy",
    helpKo: "Character/Outfit → Weapon → Effect 순서의 계획 메타데이터입니다. 현재 파이프라인은 프레임당 단일 PNG이며, 이 값은 실제 레이어 파일 존재를 강제하지 않습니다.",
    helpEn: "Planning metadata only (Character/Outfit → Weapon → Effect order). The current pipeline ships one flat PNG per frame; this field does not require actual layered files.",
  },
  approvedException: {
    termKo: "승인된 예외",
    termEn: "Approved exception",
    helpKo: "특정 규칙 위반을 사유와 함께 기록해 Export를 막지 않도록 허용합니다. 사유는 Export된 JSON/Markdown에도 그대로 남습니다.",
    helpEn: "Records a rule violation with a reason so it no longer blocks export. The reason is preserved verbatim in both exports.",
  },
};
