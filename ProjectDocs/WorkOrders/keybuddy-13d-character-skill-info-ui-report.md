# KeyBuddy 13D — 캐릭터 정보 및 해금 스킬 목록 UI 연결 완료 보고서

작업일: 2026-09-02

브랜치: `save-system`

기준 커밋: `3d603a73`

구현·테스트 커밋: `00c15c0a` (`feat: connect character info and unlocked skill UI`)

## 1. 완료 범위

- `CharacterInfo`에 전용 `CharacterInfoController`를 추가했다. 이름, 레벨, 소속, Base Idle 프리뷰,
  해금 스킬 목록은 이 컨트롤러가 소유한다.
- 독립 `list_Skill.prefab`에는 전용 `SkillListItemView`를 추가했다. 행 뷰는 이름·설명 로컬라이즈,
  아이콘, 정적 쿨타임 표기만 담당한다.
- `CharacterArchivePanel`은 선택한 `CharacterDefinition`과 현재 `SaveData`를 전용 컨트롤러에 전달하는
  필드 하나와 최소 연결만 가진다.
- 런타임 `Find`나 경로 탐색을 추가하지 않았다. 카탈로그와 UI 참조는 모두 프리팹 Inspector 직렬화
  참조로 저장했다. 에디터 재연결용 `CharacterInfoUiPrefabSetup`도 함께 남겼다.

## 2. 기본 정보와 로컬라이즈

- 캐릭터 이름은 기존 `CharacterNameBinding`과 `CharacterDefinition.LocalizedName` 계약을 재사용한다.
- 레벨은 전달받은 저장 문서에서 Ordinal 완전 일치하는 첫 캐릭터 상태를 읽고, 1 미만은 1로 표시한다.
  미보유·문서 없음·상태 없음도 안전하게 `Lv. 1`로 폴백한다.
- 소속은 `CharacterDefinition.OriginWorld.LocalizedName`을 직접 구독한다.
- 스킬 이름·설명과 제목 형식(01_UI 키 95)도 `StringChanged`를 구독한다. 비동기 로드 전에는 숫자 키를
  동기 조회해 노출하지 않으며, Locale 변경 때 열린 화면이 다시 갱신된다.
- 비활성화와 재활성화 때 이름·소속·제목·스킬 행 구독을 해제/재구성해 중복 구독을 남기지 않는다.

## 3. 캐릭터 모델 프리뷰

- `MotionProfile.BaseIdle.Frames`와 `AnimationFps`를 사용해 `CharacterModel/sp_model`의 `Image`를
  `Time.unscaledDeltaTime` 기준으로 무한 반복한다.
- 선택 캐릭터가 달라지면 첫 프레임부터 재시작한다.
- 페이지가 비활성이면 `Update`가 돌지 않고 수동 진행 함수도 상태를 바꾸지 않는다.
- Base Idle, 프레임 배열 또는 배열 안의 Sprite가 비어 있으면 이미지를 숨기고 Sprite를 비운다.
  반복 오류 로그는 발생시키지 않는다.
- Idle Event, 공격 실행, 전투 액터, 카메라와 RenderTexture는 변경하지 않았다.

## 4. 스킬 목록 규칙

- 기존 `CharacterCatalog`, `SkillCatalog`, `CharacterSkillCatalog`, `SaveData`,
  `CharacterSkillUnlockService`를 사용한다.
- 선택 캐릭터의 구조적으로 유효하고 정식 스킬 카탈로그로 해석되는 관계만 전체 수에 포함한다.
- 실제 해금된 스킬만 표시한다. 잠긴 스킬은 분모에는 포함되지만 행을 만들지 않는다.
- 정렬은 `CharacterSkill.display_order` 오름차순, 동률이면 `skill_id` Ordinal 오름차순이다.
- 제목의 `{0}`은 해금 수, `{1}`은 유효한 전체 등록 수다.
- 0개면 런타임 행을 모두 숨기고 키 96의 `lb_empty`만 표시한다. 1개 이상이면 빈 문구를 숨긴다.
- 샘플 `list_Skill`은 비활성 템플릿이며 런타임 건수로 세지 않는다. 생성 행은 풀에 보관해 캐릭터
  전환·재바인딩·재개방 때 재사용하므로 중복 클론이 누적되지 않는다.
- 쿨타임은 실시간 잔여 시간이 아니라 데이터의 초 값이며 `0.###s` 형식으로 유효 소수를 보존한다.
- `Skill.Icon`이 null이면 행 프리팹의 `placeholderIcon`을 사용한다. 현재 커밋 샘플의 `icon_key`와
  실제 Sprite는 null이므로 프리팹의 임시 표시 상태를 그대로 유지하며, 이후 아이콘 데이터가 들어오면
  같은 바인딩 경로가 실제 Sprite를 표시한다.
- `SkillInfo`의 `ScrollRect`를 세로 전용/Clamped로 연결하고, Content에 `VerticalLayoutGroup`과
  `ContentSizeFitter(Vertical=PreferredSize)`를 저장해 항목 수에 따라 높이가 확장되게 했다.

## 5. 기존 기능과 데이터 안전성

- `QuestInfo` 전용 컨트롤러와 페이지 계층은 유지했다. 씬 프리팹 인스턴스에 `CharacterInfoController`
  하나와 `CharacterStoryQuestUiController` 하나가 각각 올바른 페이지에 존재하는 것을 검사한다.
- 닫기, 우측 패널 접기/열기, 선택 캐릭터 변경 흐름은 기존 `CharacterArchivePanel.RefreshContents`를
  유지하고 선택 전달만 추가했다.
- `AutoAttackSkillRuntime`, 모션 실행, 쿨다운 저장 정책은 수정하지 않았다.
- `SaveData` 버전, 필드, 정규화와 마이그레이션 코드는 수정하지 않았다.
- CSV, Generated, Localization 데이터는 수정하지 않았다. 이미 커밋된 CatKnight 샘플을 0행으로
  기대하던 기존 테스트만 현재 데이터 계약에 맞췄다.
- `PlayerProgressSkillUnlockTests`의 테스트 문서는 v4 고정 파티 슬롯도 함께 구성하도록 보정했다.
  제품 저장 코드나 실제 저장 파일에는 손대지 않았다.

## 6. 변경 파일

### 런타임 및 프리팹

- `Assets/Scripts/CharacterArchive/CharacterInfoController.cs`
- `Assets/Scripts/CharacterArchive/SkillListItemView.cs`
- `Assets/Scripts/CharacterArchive/CharacterArchivePanel.cs`
- `Assets/Art/UI/Prefab/panel/pn_CharacterArchive.prefab`
- `Assets/Art/UI/Prefab/Skill/list_Skill.prefab`

### 에디터 연결 및 테스트

- `Assets/Editor/CharacterArchive/CharacterInfoUiPrefabSetup.cs`
- `Assets/Editor/CharacterArchive/Tests/CharacterInfoControllerTests.cs`
- `Assets/Editor/CharacterArchive/Tests/CharacterInfoPrefabTests.cs`
- `Assets/Editor/Skill/Tests/CharacterSkillUnlockTests.cs`
- `Assets/Editor/TableData/Tests/CharacterSkillTableTests.cs`
- `Assets/Editor/TableData/Tests/CharacterTableOutputTests.cs`
- `Assets/Editor/TableData/Tests/SkillTableTests.cs`
- 위 신규 C# 파일의 Unity `.meta` 파일 5개

## 7. 검증 결과

원본 Unity Editor와의 프로젝트 잠금 충돌을 피하기 위해 `/tmp/keybuddy-13d.QYcjL1/project` 격리 복제에서
Unity `2022.3.62f3` 배치 EditMode 검증을 실행했다. 원본 Editor를 종료하지 않았고 실제
`persistentDataPath`에는 접근하지 않았다.

| 검증 | 결과 |
| --- | ---: |
| `CharacterInfoControllerTests` | 10 / 10 통과 |
| `CharacterInfoPrefabTests` | 3 / 3 통과 |
| `CharacterArchive` 전체 필터(신규 테스트 포함, QuestInfo 회귀 포함) | 36 / 36 통과 |
| `Skill` 전체 필터(Unlock/AutoAttack/PlayerProgress/TableData 포함) | 110 / 110 통과 |
| `CharacterSkillTableTests;CharacterNameBindingTests;MotionEditorSkillsTests` | 28 / 28 통과 |
| Unity C# 컴파일 | 오류 0건 |
| `git diff --cached --check` | 통과 |
| SaveData/마이그레이션 변경 | 없음 |

필터끼리 일부 테스트가 겹치므로 위 숫자를 단순 합산한 총계로 보고하지 않는다. 집중 검증에는 다음
경계가 포함된다.

- 0개 empty 상태, 해금/전체 수, 미보유와 레벨 하한/상한
- 잠긴 행 미표시, 유효 관계 분모, display order/skill id 정렬
- null 아이콘 placeholder 유지, 쿨타임 소수 표기
- 이름·설명·소속·제목 Locale 콜백과 비활성 구독 해제
- Base Idle 진행·루프·선택 변경 초기화·비활성 정지·깨진 프레임 안전 처리
- 재바인딩 풀 재사용과 클론 미중복
- CharacterInfo/QuestInfo 공존 및 기존 CharacterArchive 회귀

## 8. 수동 확인 절차

1. `Assets/Scenes/desktopScene_ReSize.unity`를 열고 Play Mode에 진입한다.
2. 용병명부를 열어 캐릭터를 선택하고 우측 `CharacterInfo`의 이름, `Lv. N`, 소속을 확인한다.
3. 레벨 5 미만 CatKnight에서는 제목이 `스킬 정보 (0/1)`이고 빈 목록 문구만 보이는지 확인한다.
4. 레벨 5 이상 CatKnight에서는 제목이 `스킬 정보 (1/1)`이며 `고양이어택`, 설명, `10s`가 한 행으로
   보이는지 확인한다. 현재 null 아이콘은 프리팹 임시 표시를 유지해야 한다.
5. 다른 캐릭터와 CatKnight를 반복 선택하고 패널을 닫았다 다시 열어 행이 중복되지 않는지 확인한다.
6. `Time.timeScale = 0` 상태에서도 Base Idle이 계속 재생되고, 캐릭터를 바꾸면 첫 프레임부터 시작하는지
   확인한다.
7. Locale을 한국어/영어로 전환해 캐릭터 이름, 소속, 제목, 스킬 이름과 설명이 열린 화면에서 갱신되는지
   확인한다.
8. `CharacterInfo`와 `QuestInfo`를 전환하고 우측 접기/열기 및 닫기가 기존처럼 동작하는지 확인한다.
9. 스킬 수가 늘어난 테스트 데이터에서는 Content 높이와 세로 스크롤 범위가 행 수에 맞게 늘어나는지
   확인한다.

## 9. 다음 권장 작업

- `Skill.csv`의 `icon_key`와 대응 Sprite를 연결해 현재 임시 아이콘을 실제 스킬 아이콘으로 교체한다.
- 실제 저장 레벨 5 전후와 두 개 이상의 스킬 관계를 사용해 Play Mode 시각 QA를 한 번 수행한다.
- 필요하면 긴 번역 문자열 기준으로 행 높이와 설명 줄바꿈을 아트 해상도에서 미세 조정한다.

원격 push는 수행하지 않았다.
