# KeyBuddy 13H — SkillInfo 목록 호버/스크롤 위치 튐 수정 보고

작성일: 2026-09-02

## 원인

`pn_CharacterArchive > pn_right > CharacterInfo > SkillInfo`의 실제 계층은 다음과 같다.

```
SkillInfo (ScrollRect)
└─ Viewport
   └─ Content                 ← 제목과 list_SkillInfo를 배치하는 VerticalLayoutGroup
      └─ list_SkillInfo       ← lb_empty와 48px list_Skill 행을 배치하는 VerticalLayoutGroup
```

13G 이후 `ScrollRect.content`가 `Content`가 아니라 그 자식인 `list_SkillInfo`를 가리키고 있었다.
하지만 `Content`의 `VerticalLayoutGroup`은 `list_SkillInfo`의 세로 위치를 배치한다. 따라서 휠 입력으로
ScrollRect가 자식의 `anchoredPosition`을 바꾸면, 다음 레이아웃 갱신에서 부모 그룹이 그 위치를 다시
배치했다. 스크롤과 부모 레이아웃이 같은 RectTransform 위치를 동시에 소유한 것이 호버/스크롤 시
목록이 튀는 직접 원인이다.

추적 결과 SkillInfo 행에는 `EventTrigger`, `IPointerEnter/Exit` 구현, 툴팁, 형제 순서 변경,
`SetAsLastSibling`, transform 이동/스케일, `Canvas.ForceUpdateCanvases` 호출이 없다. 행 `Button`의
Sprite Swap은 시각 상태만 바꾸며 행의 RectTransform 크기나 순서를 바꾸지 않는다.
`CharacterInfoController.Update`도 프리뷰 이미지만 진행하고 SkillInfo를 재구성하거나 스크롤 위치를
만지지 않는다.

## 수정과 레이아웃 소유권

- `ScrollRect.content`를 Viewport의 직접 자식인 상위 `Content`로 수정했다.
- `Content`의 `VerticalLayoutGroup + ContentSizeFitter(Vertical Preferred Size)`는 제목과
  `list_SkillInfo`의 합계 높이를 소유한다.
- `list_SkillInfo`의 `VerticalLayoutGroup + ContentSizeFitter(Vertical Preferred Size)`는 빈 상태 또는
  활성 스킬 행들의 합계 높이를 소유한다.
- `list_Skill.prefab`의 `LayoutElement`는 기존 계약대로 행 높이 48px을 소유한다. 행의 기존 절대 배치와
  시각 디자인은 변경하지 않았다.

즉 크기 계산은 `행 → list_SkillInfo → Content` 방향으로만 전파되고, ScrollRect는 가장 바깥의
`Content`만 이동한다. 부모 레이아웃과 ScrollRect가 더 이상 같은 위치를 되돌려 쓰지 않는다.

`CharacterInfoController`는 `anchoredPosition` 또는 normalized position을 설정하지 않는다. 실제 데이터
재구성, 단순 재바인드, 호버, 휠 입력 모두에서 스크롤을 강제로 상단으로 보내지 않는 정책을 유지한다.
행 수가 줄어 현재 위치가 유효 범위를 벗어날 때만 `ScrollRect`의 Clamped 동작이 가능한 가장 가까운
위치로 제한한다. 패널의 처음 열림은 저작된 상단 기준 Content 위치에서 시작한다.

## 변경 파일

- `Assets/Art/UI/Prefab/panel/pn_CharacterArchive.prefab`
  - SkillInfo ScrollRect의 Content 참조를 상위 `Content`로 변경했다.
- `Assets/Editor/CharacterArchive/CharacterInfoUiPrefabSetup.cs`
  - UI 재설정 메뉴를 다시 실행해도 같은 올바른 Content 참조를 만든다.
- `Assets/Editor/CharacterArchive/Tests/CharacterInfoPrefabTests.cs`
  - ScrollRect Content의 직접 Viewport 자식 계약, 중첩 레이아웃 소유권, 8개 행의 반복 레이아웃 및
    포인터 진입 후 Content/행 위치와 크기 불변 계약을 검증한다. 테스트는 실제로 중간
    `verticalNormalizedPosition`으로 이동한 뒤 검증한다.

SaveData/마이그레이션, CSV/Generated/Localization, 씬, 관련 없는 프리팹은 변경하지 않았다.

## 검증

원본 Unity 프로젝트는 열지 않았다. `/private/tmp/keybuddy-13h-unity.gXW0EJ`에 `Assets`, `Packages`,
`ProjectSettings`만 복사한 격리 Unity 프로젝트에서 Unity 2022.3.62f3 EditMode를 실행했다.

- `CharacterInfoPrefabTests`: 5/5 통과
  - 실제 8행에서 중간 스크롤 위치, 반복 레이아웃, Button pointer enter 전후 Content 높이/위치와 행
    위치/크기 불변을 확인했다.
- `CharacterInfoControllerTests`: 10/10 통과
- `SaveSystemStorageIsolationTests`: 4/4 통과
- Unity C# 컴파일 오류: 0건.
- 13F 전역 저장소 격리 fixture가 모든 EditMode 실행에 적용됐다. 제품 `persistentDataPath`에는 접근하거나
  수정하지 않았다.
- `git diff --check`: 통과.

## 수동 확인 항목

- 스킬 0개, 1개, Viewport를 넘는 복수 행에서 `lb_empty`/48px 행/3px spacing이 유지되는지 확인한다.
- 복수 행을 중간까지 스크롤한 뒤 각 행 위로 마우스를 이동하고 휠을 연속 입력해도 현재 위치가 튀거나
  상단으로 돌아가지 않는지 확인한다.
- 스크롤 중 캐릭터 전환, 패널 재열기, CharacterInfo와 QuestInfo 전환 뒤에도 `_Runtime` 행이 중복되지
  않고 풀을 재사용하는지 확인한다.
- 스크롤된 상태에서 실제 해금 데이터가 바뀌는 경우, 위치를 강제로 초기화하지 않고 Clamped 범위 안에서
  유지되는지 확인한다.

## 다음 작업 권장

Unity 에디터에서 실제 장시간 휠 입력으로 SkillInfo 스크롤 감도와 체감 이동량을 한 차례 확인한다.
