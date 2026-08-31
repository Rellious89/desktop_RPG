# KeyBuddy 13B — Motion Editor Skills 작업공간 완료 보고서

작성일: 2026-08-31  
대상 경로: `/Users/rellious/Rell_Dev/desktopRPG/desktop_RPG`  
브랜치: `save-system`  
구현 커밋: `1be86f192779c24ef2c6a63ef9c8d82bf49c5f73` (`feat: add skill workspace to motion editor`)

## 완료 범위

- Character Motion Editor에 `Skills` 작업공간/내비게이션을 추가했다.
- 선택한 CharacterMotionProfile에 연결된 CharacterDefinition의 `character_id`로 CharacterSkillCatalog를 필터링한다. 항목은 `display_order` 오름차순, `skill_id` Ordinal, 관계 Pair ID 순으로 안정 정렬한다.
- 각 스킬에 이름(ko-KR 에디터 테이블 직접 조회, 미선택 로케일을 건드리지 않는 안전 폴백), `skill_id`, 필요 레벨, cooldown, `motion_key`(연결된 AttackMotionDefinition 에셋명), 모션 참조 연결 상태를 표시한다.
- 연결된 스킬은 기존 Attack Editor 및 Preview의 동일한 `AttackMotionDefinition` 경로를 사용한다. 프레임/FPS, Cast·Hit cue, 사운드, 이펙트, 오버레이, 발사체, Direct·Accumulated Input 편집과 프리뷰를 별도 시스템 없이 그대로 재사용한다.
- 모션이 없거나 아직 CSV 행이 없는 경우에도 deterministic 기본 키 `<characterId>_Skill_<skillId>`를 제안하고, 기존 AttackMotion 에셋 경로 관례(`Assets/Data/MotionProfiles/Characters/<characterId>`)에 새 에셋을 만들 수 있게 했다.
- 생성 전 프로젝트 전체의 AttackMotionDefinition 이름을 검사한다. 같은 이름이 하나면 명시적으로 기존 에셋을 선택하거나 새 이름을 고르게 하고, 여러 개면 모호한 `motion_key`로 연결하지 못하게 막으며, 어떤 경우에도 덮어쓰지 않는다.
- Skills UI는 Generated SkillDefinition을 직접 수정하거나 CSV를 변경하지 않는다. 생성한 모션의 정확한 에셋명을 복사 가능한 `motion_key`로 보여 주고, Skill.csv 입력 후 Table Data Rebuild가 13A 직접 참조를 기록하도록 안내한다.
- 현재처럼 Skill.csv/CharacterSkill.csv가 비어 있으면 안전한 빈 상태와 첫 모션 계획 생성 UI를 표시한다. Overview/Idle/Idle Events/Attacks T1–T3/Movement/Hit/Defeat 기존 작업공간은 유지했다.

## 집중 검증

- Unity 2022.3.62f3 검증은 `/private/tmp/keybuddy-13b-unity.hVfNKy` 격리 복사본에서 수행했다. 원본 프로젝트를 Unity로 열거나 실제 `persistentDataPath`에 접근하지 않았다.
- `CharacterEditor.Tests.MotionEditorSkillsTests`: **4/4 통과**
  - 빈 입력 안전성
  - 캐릭터 필터 및 `display_order → skill_id` 정렬
  - 결정적 스킬 모션 키 제안
  - 기존 Attack T1/T2/T3 풀 매핑 회귀
- `TableDataEditor.Tests.SkillTableTests`: **34/34 통과**
- `TableDataEditor.Tests.CharacterSkillTableTests`: **22/22 통과**
- Unity C# 컴파일 오류: **0건**.
- `git diff --check`: 통과.

## 저장·작업 트리·푸시

- SaveData와 마이그레이션은 수정하지 않았다. `SaveData.CurrentSaveVersion`은 기존 **v8** 그대로다.
- 프로덕션 Skill.csv/CharacterSkill.csv 행과 Generated Skill/CharacterSkill 에셋은 수정하지 않았다.
- 이 보고서 커밋 후 작업 트리는 깨끗해야 한다.
- 원격 푸시는 하지 않았다.

## 다음 사용자 선행 작업: CatKnight 샘플 스킬 1개와 모션 제작

1. Motion Editor에서 **Characters → CatKnight → Skills**를 연다. 빈 목록이면 `Planned skill_id`에 예를 들어 `catknight_arc_slash`를 입력하고 `Create Planned Skill Motion Asset`을 누른다. 제안/생성 키 `CatKnight_Skill_catknight_arc_slash`를 복사하고, 기존 Attack 편집기에서 프레임·FPS·Hit/Cast cue·연출·입력 방식을 제작한다.
2. `Skill.csv`에 한 행을 추가한다. 필수 입력은 `skill_id`, `name_category`, `name_key`, `skill_type`, `behavior_key`(공격 모션이면 `attack_motion`), `cooldown_seconds`(attack_motion이면 0보다 큼), `motion_key`(위에서 복사한 정확한 에셋명), `display_order`, `enabled`다. 필요하면 `description_category`, `description_key`, `icon_key`, `memo`도 채운다.
3. `CharacterSkill.csv`에 같은 `character_id`인 `CatKnight`, 같은 `skill_id`, `required_character_level`, `display_order`, `enabled`를 넣는다. 표시 전용 `$character_name`, `$skill_name`, `memo`는 CSV 규칙에 맞게 채운다.
4. Table Data Rebuild를 실행한다. 이후 **CatKnight → Skills**에서 연결 상태가 `connected`이고 `motion_key`가 생성한 이름인지 확인한 뒤, 그 항목에서 동일한 Attack 편집/프리뷰로 계속 다듬는다.
