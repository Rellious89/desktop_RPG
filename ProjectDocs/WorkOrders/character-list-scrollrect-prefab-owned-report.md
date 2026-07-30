# 캐릭터 리스트 ScrollRect 구조를 프리팹 소유로 전환 완료 보고서

작업일: 2026-07-30
대상: `Assets/Scripts/Common/CharacterSwap/CharacterSwapPanel.cs`

프리팹 파일은 건드리지 않았다(Unity 에디터가 프로젝트 락을 잡고 있다).

## 1. 제거한 런타임 자동 구성 (7항목 전부)

`EnsureScrollStructure()`, `MoveLayoutSettingsToContent()`, `EnsureChild()`를 통째로 삭제했다.
사라진 동작은 다음과 같다.

| 지시서 항목 | 삭제한 코드 |
| --- | --- |
| Viewport 생성 금지 | `EnsureChild(listRoot, "Viewport")` + `Stretch()` |
| Content 생성 금지 | `EnsureChild(viewport, "Content")` + 앵커/피벗/크기 대입 |
| ScrollRect AddComponent 금지 | `listRoot.gameObject.AddComponent<ScrollRect>()` |
| `ScrollRect.viewport` 자동 지정 금지 | `scrollRect.viewport = viewport` |
| `ScrollRect.content` 자동 지정 금지 | `scrollRect.content = content` |
| 구조 재구성/오브젝트 생성 금지 | 템플릿 `SetParent(content)` 재배치, `RectMask2D` 자동 추가 |
| 프리팹 설정 그대로 사용 | `horizontal=false / vertical=true / movementType=Clamped` 강제 대입, `ContentSizeFitter` 자동 추가, `list`의 Vertical Layout Group 강제 비활성화, Layout 설정 복사 |

이름 기반 자동 탐색(`transform.Find` / `FindDeepChild`)도 이 패널에서 전부 없앴다.

## 2. 새 참조 구조

```csharp
[SerializeField] private Button swapButton;
[SerializeField] private ScrollRect characterScrollRect;   // list의 ScrollRect
[SerializeField] private RectTransform content;            // 비우면 ScrollRect.content를 읽어 쓴다
[SerializeField] private CharacterSwapListItem itemTemplate;
```

- 항목 부모 결정 우선순위: **(1) `characterScrollRect.content` → (2) Inspector의 `content` → (3) 경고**.
  둘 다 **읽기만** 하고 `characterScrollRect.content`에 값을 써넣지 않으므로, 에디터에서 ScrollRect의
  Content를 바꾸면 다음 갱신부터 그 값이 그대로 반영된다(검증 11번).
- 참조가 빠지면 자동 보정하지 않고 패널을 열 때 한 번만 진단을 남긴다.
  - ScrollRect 미연결 → 오류
  - `viewport` 또는 `content` 미설정 → 지시서에 지정된 문구 그대로:
    `CharacterSwapPanel: ScrollRect Viewport 또는 Content가 설정되지 않았습니다.`
  - Content/템플릿/교체 버튼 미연결 → 각각 오류
- 필드 이름은 `itemTemplate`을 유지했다. 지시서 예시는 `characterItemPrefab`이었지만, 실제로 연결되는
  대상은 프리팹 에셋이 아니라 **Content 아래에 배치된 `list_Character` 인스턴스**이고, 이름을 바꾸면
  프리팹에 이미 저장된 참조가 끊긴다.

유지된 기능: 리스트 동적 생성, 초상화/이름/레벨/행동력 표시, 선택 처리, 현재 캐릭터 비교, 교체 버튼,
목록 갱신, 캐릭터 수가 적을 때 표시. 목록 갱신은 지금까지대로 **생성한 항목만** 정리하고
Viewport/Content/ScrollRect는 건드리지 않는다.

## 3. 에디터에서 확인한 현재 프리팹 상태

```text
list      [Image, Mask, ScrollRect, VerticalLayoutGroup(비활성)]
└ viewport  [RectMask2D]  (스트레치)
  └ content [VerticalLayoutGroup]  (스트레치, ContentSizeFitter 없음)
    └ list_Character  (중첩 프리팹 인스턴스, 비활성)  ← 템플릿
```

- `ScrollRect.Viewport` → `viewport`, `ScrollRect.Content` → `content` 모두 연결되어 있다(검증 1~3번 ✓).
- 템플릿 `list_Character`가 `content` 아래에 비활성으로 배치되어 있다(검증 4번 ✓).
  **비활성 상태를 유지해야 한다** - 이제 코드가 템플릿을 강제로 끄지 않으므로, 켜 두면 런타임에 빈
  행이 하나 더 보인다.

## 4. 프리팹에서 손봐야 할 값 3가지 (코드가 더 이상 보정하지 않는다)

지금까지는 위에서 삭제한 코드가 런타임에 이 값들을 덮어써서 동작했다. 이제 프리팹 값이 그대로
쓰이므로, **아래 세 가지를 에디터에서 맞추지 않으면 리스트가 그려지기는 해도 스크롤되지 않는다.**

| # | 대상 | 현재 값 | 필요한 값 | 이유 |
| --- | --- | --- | --- | --- |
| 1 | `list`의 ScrollRect | Horizontal = **On**, Vertical = **Off**, Movement Type = Elastic | Horizontal Off, Vertical **On** (Movement Type은 취향, 이전 동작은 Clamped) | Content가 세로로 쌓이는데 세로 스크롤이 꺼져 있다 |
| 2 | `content`의 RectTransform | 스트레치 (0,0)~(1,1) | Anchor (0,1)~(1,1), Pivot (0.5, 1) | 위에서 아래로 쌓이고 높이가 늘어날 수 있어야 한다 |
| 3 | `content` | ContentSizeFitter **없음** | ContentSizeFitter 추가, Vertical Fit = Preferred Size | Content 높이가 항목 수에 따라 늘어나야 스크롤 범위가 생긴다 |

`list`의 Vertical Layout Group은 이미 비활성이라 그대로 두면 된다(활성화하면 Viewport 크기를 강제로
배치해 스크롤이 깨진다). `content`의 Vertical Layout Group은 Child Control Height를 끄고 Child Control/
Force Expand Width를 켜 두는 것이 이전 런타임 설정과 같다.

## 5. 검증 상태

### 이 환경에서 확인한 것

- `Assets/Scripts` 전체를 Unity와 같은 정의(`UNITY_STANDALONE_WIN` 포함)로 Roslyn 컴파일 → **오류 0건**.
- 삭제 후 `EnsureScrollStructure` / `MoveLayoutSettingsToContent` / `EnsureChild` / `listRoot` /
  `structureReady` 참조가 코드에 하나도 남지 않은 것을 grep으로 확인.
- 프리팹 YAML로 계층·ScrollRect 참조·컴포넌트 활성 상태·템플릿 부모를 확인(3장).

### 확인하지 못한 것

- **Play Mode 실행 전체**(에디터가 프로젝트 락을 잡고 있어 실행할 수 없다). 검증 6~11번은 에디터에서
  직접 확인해야 한다 - 특히 4장의 세 값을 맞춘 뒤 스크롤을 확인해야 한다.

## 6. 구현하지 않은 것

- 프리팹 값 수정(위 4장) - 프리팹은 이제 사람이 관리하는 영역이라 코드/파일로 손대지 않았다.
- 인벤토리 패널의 슬롯 구조는 이번 범위가 아니다(원래부터 프리팹에 배치된 8칸을 그대로 쓴다).
