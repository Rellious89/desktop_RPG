# KeyBuddy 13G — CharacterInfo SkillInfo 스크롤/리스트 레이아웃 보정 보고

작성일: 2026-09-02

## 원인과 결과

`pn_CharacterArchive > pn_right > CharacterInfo > SkillInfo`의 실제 런타임 행은
`Assets/Art/UI/Prefab/Skill/list_Skill.prefab`을 템플릿으로 하여
`SkillInfo/Viewport/Content/list_SkillInfo` 아래에 복제된다. 행 루트의 본래 `RectTransform` 높이는
48이었지만, 부모 `VerticalLayoutGroup`이 소비할 `LayoutElement` 높이 계약이 없었다. 따라서 부모가
자식 높이를 제어하는 순간 행이 0으로 계산될 수 있었고 아이콘, 이름/설명, 쿨타임이 겹쳤다.

행 프리팹 루트에 `LayoutElement`를 추가해 `minHeight`와 `preferredHeight`를 모두 기존 디자인 높이인
48로 선언했다. 행 높이의 소유자는 이제 프리팹이며, `list_SkillInfo`의 `VerticalLayoutGroup`이 이 값을
소비한다. 컨트롤러는 행 `sizeDelta`를 강제하지 않는다.

## 최종 레이아웃 소유권

- `SkillInfo`는 세로 전용 `ScrollRect`이며, `Viewport`는 기존 마스크/표시 영역을 유지한다.
- `Viewport/Content`는 위쪽 anchor와 위쪽 pivot을 사용하며, `VerticalLayoutGroup`과
  `ContentSizeFitter(Vertical Preferred Size)`가 상위 콘텐츠 높이를 계산한다.
- `Content/list_SkillInfo`는 위쪽 기준으로 정렬했다. 이 컨테이너의 `VerticalLayoutGroup`은 자식 높이를
  제어하고, `ContentSizeFitter(Vertical Preferred Size)`가 빈 상태 또는 활성 행들의 합계와 spacing으로
  높이를 계산한다.
- 비활성 `list_Skill`은 템플릿만 담당하며 레이아웃 계산에 포함되지 않는다. 활성 `_Runtime` 복제본만
  목록의 행이 된다. 0개면 `lb_empty`만 보이고, 1개 이상이면 풀의 복제본을 재사용한다.

## 변경 파일

- `Assets/Art/UI/Prefab/Skill/list_Skill.prefab`
  - 기존 48px 시각 디자인을 그대로 사용해 루트 `LayoutElement`의 최소/선호 높이를 48로 선언했다.
- `Assets/Art/UI/Prefab/panel/pn_CharacterArchive.prefab`
  - `list_SkillInfo`를 상단 pivot 및 상단 기준 위치로 정규화했다.
- `Assets/Editor/CharacterArchive/Tests/CharacterInfoPrefabTests.cs`
  - 행의 양수 최소/선호 높이, 상단 기준 Content 계약, 행 컨테이너의 자식 세로 크기 제어를 검증한다.

`CharacterInfoController`의 풀링, 패널 재오픈/캐릭터 전환 시 재사용, 비활성 템플릿 처리 방식은 변경하지
않았다. 저장 데이터 버전, 마이그레이션, CSV/Generated/Localization, 씬 파일은 변경하지 않았다.

## 검증

원본 프로젝트는 Unity로 열지 않았다. `/private/tmp/keybuddy-13g-unity.yHv5tA`에 `Assets`, `Packages`,
`ProjectSettings`만 복사한 격리 Unity 프로젝트에서 Unity 2022.3.62f3 EditMode를 실행했다.

- 집중 회귀: 14/14 통과
  - `CharacterInfoPrefabTests`: 4/4
  - `CharacterInfoControllerTests`: 10/10
- Unity C# 컴파일 오류: 0건.
- 13F의 전역 저장소 격리 fixture가 같은 실행에 적용되어 제품 `persistentDataPath`의
  `playerprogress.json` 및 `.bak` 존재 여부, SHA-256, UTC 수정 시각이 실행 전후 동일함을 확인했다.
- `git diff --check`: 통과.

## 수동 확인 항목

- 스킬 0개: `lb_empty`만 보이고 스크롤 콘텐츠가 무너지지 않는지 확인한다.
- 스킬 1개 및 복수: 각 행이 48px 높이와 3px spacing을 유지하고 이름/설명/쿨타임이 겹치지 않는지 확인한다.
- Viewport를 넘는 스킬 수: 세로 ScrollRect가 실제로 이동하는지 확인한다.
- 패널 재오픈, 캐릭터 전환, CharacterInfo/QuestInfo 전환: `_Runtime` 행이 중복되지 않고 풀을 재사용하는지
  확인한다.

## 다음 작업 권장

Unity 에디터에서 실제 CharacterInfo 패널을 열어, 로컬라이즈된 긴 스킬 설명에서도 48px 행 디자인이 의도대로
유지되는지 한 차례 시각 확인한다.
