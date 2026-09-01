# KeyBuddy 13C — 자동 공격 스킬 런타임 완료 보고서

## 1. 완료 범위

- 구현 커밋: `3c00df24` (`feat: add automatic attack skill runtime`)
- 샘플 계약 테스트 보정: `b1c9d1f8` (`test: align skill unlock sample coverage`)
- 작업 기준: 원본 프로젝트 `/Users/rellious/Rell_Dev/desktopRPG/desktop_RPG`, 브랜치 `save-system`, 시작 HEAD `bdaa6ac4`
- 원격 푸시는 수행하지 않았다.

## 2. 런타임 규칙

`AutoAttackSkillRuntime`이 선택 순서와 실행 세션 한정 쿨다운을 소유한다. 저장 파일이나 씬을 찾지 않고, 주입받은 현재 캐릭터 카탈로그·SkillCatalog·CharacterSkillCatalog·이미 로드된 SaveData와 시험 가능한 단조 시간 공급자만 사용한다.

- 실제 전투 캐릭터는 `CharacterRoster.Instance.Current` 하나만 근거로 삼는다.
- 해금은 `CharacterSkillUnlockService`를 재사용한다. 보유하지 않은 캐릭터, 현재 카탈로그에 없는 캐릭터, 요구 레벨 미달, 관계의 캐릭터/스킬 참조 누락·ID 불일치는 실행 대상이 아니다.
- Generated 카탈로그에 들어온 활성 관계/스킬 중 `behavior_key=attack_motion`, 유한한 양수 cooldown, 프레임이 있는 직접 `AttackMotionDefinition` 참조만 실행한다. 런타임 `motion_key` 문자열 탐색은 없다.
- 준비된 후보는 `CharacterSkill.display_order` 오름차순, 같은 값이면 `skill_id` Ordinal 오름차순이다. 앞 후보가 쿨다운이면 다음 준비 후보를 사용한다.
- 쿨다운 키는 `character_id + skill_id`이며 최초 관찰은 준비 상태다. 선택 조회는 소비하지 않고 모션이 `PlayerCharacterAnimator`의 active 재생 상태에 실제로 들어간 직후 시작 시각을 기록한다.
- 시간은 `Time.realtimeSinceStartupAsDouble`을 사용하므로 `timeScale`과 무관하다. 테스트는 `ITimeSource` 가짜 시간을 사용한다.
- 캐릭터 교체·GameObject 비활성화·모션 프로필 교체는 런타임 쿨다운 객체를 지우지 않는다. 돌아오면 같은 실행 세션의 경과 시간이 반영되며 프로세스 재실행 시에는 초기화된다. 저장·마이그레이션은 추가하지 않았다.

## 3. 공격 연결과 회귀 보호

`PlayerCharacterAnimator`의 기존 입력 → 공격 세션 → 공격 사이클 경로 한 곳에만 연결했다.

- 새 사이클 시작 시 준비 스킬을 먼저 선택하고, 없으면 기존 콤보 티어 풀 및 랜덤 일반 공격 선택을 그대로 사용한다.
- 스킬도 기존 `IAttackMotion`/`AttackMotionDefinition` 재생 파이프라인을 그대로 탄다. FPS, 프레임, Direct/Accumulated Input, Cast/Hit cue, 사운드, 이펙트, 오버레이, 발사체, 공격 이동을 복제하지 않았다.
- `Strike()`와 `HitPoint`는 기존 공통 경로 하나뿐이다. 스킬 전용 피해/처치/보상 이벤트나 저장 경로를 추가하지 않았다.
- `AttackStarted` 구독자가 필드 전환·캐릭터 교체·비활성화로 공격을 취소하면 선택해 둔 모션을 다시 열지 않고 쿨다운도 소비하지 않는다.
- 공격 중 Direct 대기열과 Accumulated 이월 입력이 새 사이클을 열 때도 같은 선택 경계를 사용한다. 스킬이 준비되지 않았거나 실행 데이터가 깨졌으면 입력 모드는 기존 일반 공격 흐름으로 폴백한다.

## 4. 씬 연결

변경 씬은 `Assets/Scenes/desktopScene_ReSize.unity` 하나다. `PlayerCharacter`의 기존 `PlayerCharacterAnimator`에 아래 Generated 카탈로그 참조 두 개만 명시적으로 연결했다.

- `Assets/Generated/TableData/Skill/SkillCatalog.asset`
- `Assets/Generated/TableData/CharacterSkill/CharacterSkillCatalog.asset`

새 이름 기반 Find 폴백, 새 GameObject, CSV/Localization/Generated 데이터 변경은 없다. 캐릭터 카탈로그는 별도 Inspector 복제 없이 실제 `CharacterRoster.Catalog`를 재사용한다.

## 5. 검증

원본 Unity가 열려 있어 원본 프로젝트에서는 Unity를 실행하지 않았다. 현재 변경 파일을 `/tmp/keybuddy13c.HYeKB6` 격리 프로젝트로 복사하고 원본과 바이트 일치를 확인한 뒤 Unity 2022.3.62f3 EditMode에서 검증했다. 결과 XML과 로그도 `/tmp`에만 기록했으며 실제 `persistentDataPath`는 접근하지 않았다.

| 묶음 | 결과 | 집중 범위 |
|---|---:|---|
| `AutoAttackSkillRuntimeTests` + `PlayerCharacterAnimatorTests` + `CharacterRosterCatalogTests` | **64/64 통과** | 레벨 미달/경계, 최초 우선 발동, 시작 시점 소비, 10초 경계, 다중 우선순위, 캐릭터별 독립 쿨다운과 교체 중 경과, 누락 안전 폴백, 일반 공격 경로, HitPoint 1회, AttackStarted/HitPoint 재진입 취소, 자동 캐릭터 교체 |
| `CharacterSkillUnlockServiceTests` + `AutoAttackSkillRuntimeTests` | **32/32 통과** | 기존 해금 계약 25개와 자동 스킬 정책 7개, 현재 프로덕션 CatKnight 샘플 직접 참조 |

- Unity C# 컴파일 오류: **0**
- `git diff --check`: **통과**
- 검증한 현재 샘플: `CatKnight` / `catknight_skill_01` / 필요 레벨 5 / cooldown 10초 / `CatKnight_Skill_01`
- SaveData: `CurrentSaveVersion = 8` 유지, 필드·마이그레이션·저장 호출 추가 없음

## 6. 수동 확인 절차

1. CatKnight 레벨을 5 미만으로 두고 공격 키를 눌러 기존 일반 공격만 나오는지 확인한다.
2. 레벨 5 이상에서 첫 유효 공격 입력에 `CatKnight_Skill_01`이 일반 공격보다 먼저 나오는지 확인한다.
3. 스킬 시작 후 10초 동안 공격 입력이 기존 일반 공격/콤보로 이어지는지 확인한다.
4. 10초가 지난 뒤 다음 유효 공격 입력에서 스킬이 다시 발동하는지 확인한다.
5. 다른 캐릭터로 교체해 그 캐릭터의 스킬/쿨다운이 독립인지 확인하고, CatKnight로 돌아와 교체 중 경과 시간이 반영됐는지 확인한다.
