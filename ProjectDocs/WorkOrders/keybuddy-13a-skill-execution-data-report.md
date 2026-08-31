# KeyBuddy 13A — 스킬 실행 데이터 기반 완료 보고서

작성일: 2026-08-31  
대상 경로: `/Users/rellious/Rell_Dev/desktopRPG/desktop_RPG`  
브랜치: `save-system`  
구현 커밋: `2a17f9368aedd6edde6077b8f24921513dcd8183` (`feat: add skill attack motion execution data`)

## 완료 범위

- `Skill.csv`에 `cooldown_seconds`, `motion_key` 실행 컬럼을 추가했다. 기존 `skill_type`, `behavior_key`, `display_order`의 의미와 순서는 보존했다.
- `cooldown_seconds`는 InvariantCulture 기준의 유한한 `float` 0 이상만 허용한다. `behavior_key=attack_motion`에서는 0보다 커야 한다.
- `motion_key`는 `AttackMotionDefinition` 에셋명과 Ordinal 완전 일치로만 해석한다. 없거나, 같은 이름이 여러 개이거나, 프레임이 하나도 없으면 오류이며 임의의 폴백은 없다.
- 검증된 `AttackMotionDefinition` 참조와 cooldown은 `SkillRow`을 거쳐 Generated `SkillDefinition`의 직렬화 필드에 직접 기록된다. 런타임은 문자열 키로 공격 모션을 재탐색하지 않는다.
- 프로덕션 `Skill.csv`와 `CharacterSkill.csv`는 행을 추가하지 않았다. 헤더만 있는 두 표는 정상 검증·Rebuild 상태를 유지한다.

## 수정 구조

| 영역 | 파일 | 변경 |
| --- | --- | --- |
| CSV 스키마 | `Assets/TableData/Game/Skill.csv`, `Assets/Editor/TableData/TableDataCsvReader.cs` | 두 실행 컬럼과 순서 추가 |
| 중간 데이터·검증 | `TableDataRows.cs`, `TableDataFieldRules.cs`, `TableDataAssetIndex.cs`, `TableDataValidator.cs` | 유한 cooldown 파싱, 공격 모션 정확 일치 인덱스·참조 검증 |
| Generated 기록 | `TableDataRebuilder.cs`, `Assets/Scripts/Skill/SkillDefinition.cs` | cooldown 및 해석된 `AttackMotionDefinition` 직접 참조 |
| 집중 시험 | `Assets/Editor/TableData/Tests/SkillTableTests.cs` | 빈 표, 정상 공격 모션 행, cooldown 오류, motion_key 필수/미존재/중복/무프레임, Generated 참조 기록 |

## 검증 결과

- Unity 2022.3.62f3에서 원본이 열려 있는 상태를 확인했다. 원본을 건드리지 않도록 `/private/tmp`의 격리 복사본에서 검증했다.
- EditMode 집중 시험: `SkillTableTests` + `CharacterSkillTableTests` **56/56 통과**. CharacterSkill의 요구 레벨·순서·관계 계약 회귀가 없다.
- Rebuild 경계 시험: `CharacterOriginWorldRebuildTests` **1/1 통과**.
- Unity C# 컴파일 오류: **0건** (`error CS` / compilation failure 없음).
- `git diff --check`: 통과.

## Generated / 저장 / 작업 트리

- Generated 범위: `Assets/Generated/TableData/Skill` 및 `Assets/Generated/TableData/CharacterSkill`만 검증했다. 현재 CSV가 비어 있으므로 이 두 Generated 도메인의 GUID·내용 변경은 **0건**이다. 다른 Generated 파일은 건드리지 않았다.
- SaveData: 이 브랜치의 시작 상태에서 `SaveData.CurrentSaveVersion`은 이미 **v8**이었다. 이번 작업은 `SaveData`·마이그레이션·실제 `persistentDataPath`에 접근하거나 변경하지 않았으므로 **v8 그대로**다. 작업 지시의 “v6 유지” 표기는 현재 브랜치 소스와 일치하지 않으며, 버전을 임의로 되돌리지는 않았다.
- 이 보고서 커밋 후 작업 트리는 깨끗한 상태여야 한다. 원격 푸시는 하지 않았다.

## 다음 단계

다음 단계는 **13B Motion Editor Skills 작업공간**이다. 사용자 선행 작업은 아직 없다.
