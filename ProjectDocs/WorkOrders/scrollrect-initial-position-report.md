# ScrollRect 최초 위치 초기화 컴포넌트 완료 보고서

작업일: 2026-07-30

프리팹/씬 파일은 건드리지 않았다(Unity 에디터가 프로젝트 락을 잡고 있다).

## 1. 구현한 것

`Assets/Scripts/Common/ScrollRectInitialPosition.cs` (신규) 하나뿐이다.
`CharacterSwapPanel`은 **한 줄도 바꾸지 않았다** - 이 기능은 패널이 아니라 스크롤 UI 쪽에 붙는다.

```csharp
[SerializeField] private bool  applyInitialPosition = true;
[SerializeField, Range(0f,1f)] private float initialHorizontalNormalizedPosition = 0f;
[SerializeField, Range(0f,1f)] private float initialVerticalNormalizedPosition = 1f;
public void ResetToInitialPosition();
public bool HasAppliedInitialPosition { get; }
```

- `[RequireComponent(typeof(ScrollRect))]` - 같은 GameObject의 ScrollRect만 다룬다. 전역에서 모든
  ScrollRect를 건드리지 않고, 값도 컴포넌트마다 따로 갖는다.
- 목록 갱신에는 전혀 관여하지 않는다(리스트 생성 이벤트를 구독하지 않는다) - 그래서 항목이 다시
  만들어져도 사용자가 보던 위치가 유지된다.

## 2. 최초 1회 / 재오픈 유지 / 씬 재시작 구분

| 상황 | 동작 | 근거 |
| --- | --- | --- |
| 최초 오픈 | 초기 위치 적용 | `hasAppliedInitialPosition == false` |
| 닫고 재오픈 | 아무 것도 하지 않음(마지막 위치 유지) | OnEnable 첫 줄에서 완료 표시를 보고 즉시 반환 |
| 씬 재시작 | 다시 적용 | 완료 표시가 **인스턴스 필드**다(정적 값/저장 값 없음) |
| 명시적 리셋 | 즉시 초기 위치로 | `ResetToInitialPosition()` |

`ResetToInitialPosition()`은 `applyInitialPosition` 설정과 무관하게 항상 동작한다(명시적 호출이 우선).
오브젝트가 꺼져 있을 때 호출하면 레이아웃 계산을 기다릴 수 없으므로 완료 표시만 해제해서, 다음에
켜질 때 최초 적용 경로를 다시 타게 한다.

## 3. 레이아웃 계산 이후에 적용하는 방식

항목이 만들어지고 Content 크기가 확정되기 전에 normalized position을 넣으면 이후 레이아웃 계산에서
되돌아간다. 그래서 두 단계로 적용한다.

```text
1. OnEnable          -> 즉시 한 번 적용(첫 프레임에 엉뚱한 위치가 잠깐 보이는 것 방지)
2. 코루틴: yield null -> 같은 프레임의 항목 생성/레이아웃 요청이 끝난 뒤
3. Canvas.ForceUpdateCanvases()
4. LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content)
5. Canvas.ForceUpdateCanvases()
6. 다시 적용 + velocity 0으로 초기화
7. 이때 비로소 "최초 적용 완료" 기록
```

- 완료 기록은 **2단계까지 끝난 뒤에만** 한다. 1번만 하고 패널이 곧바로 닫힌 경우에는 제대로 적용된
  것이 아니므로 다음 오픈에서 다시 시도한다.
- `velocity = Vector2.zero`를 함께 넣는다 - 남아 있던 관성이 있으면 방금 맞춘 위치가 다음 프레임에
  밀려난다.
- 가로/세로 값은 ScrollRect의 Horizontal/Vertical 체크와 무관하게 둘 다 적용한다. 그 체크는 사용자
  입력(드래그/휠) 허용 여부이고, 위치 자체는 어느 축이든 의미가 있다.

## 4. 캐릭터 교체 UI 적용 (에디터 작업)

`pn_CharacterSwap` 프리팹의 **`bg/list`**(ScrollRect가 붙어 있는 오브젝트)에 `ScrollRectInitialPosition`을
추가하고 다음과 같이 설정한다.

| 설정 | 값 |
| --- | --- |
| Apply Initial Position | On |
| Initial Horizontal Normalized Position | 0 |
| Initial Vertical Normalized Position | **1** (Unity 기준 세로 최상단) |

"최초 1회만 적용"과 "재오픈 시 위치 유지"는 별도 설정이 아니라 컴포넌트의 기본 동작이다(2장).

## 5. 먼저 맞춰야 하는 프리팹 값 - 이것 없이는 이 컴포넌트만으로 증상이 사라지지 않는다

지난 작업에서 런타임 자동 보정 코드를 제거했기 때문에, 지금 프리팹에는 **세로 스크롤이 성립하지 않는
값들이 남아 있다.** `verticalNormalizedPosition`은 "Content가 Viewport보다 클 때" 의미가 있는 값이라,
아래를 맞추기 전에는 초기 위치를 넣어도 화면이 달라지지 않는다.

| # | 대상 | 현재 값 | 필요한 값 | 이유 |
| --- | --- | --- | --- | --- |
| 1 | `list`의 ScrollRect | Horizontal On / **Vertical Off** | Horizontal Off / **Vertical On** | 세로 리스트인데 세로 입력이 꺼져 있다 |
| 2 | `content` RectTransform | 스트레치 (0,0)~(1,1), Pivot (0.5,0.5) | Anchor (0,1)~(1,1), Pivot (0.5,1) | 위에서 아래로 자라야 한다 |
| 3 | `content` | ContentSizeFitter **없음** | 추가 + Vertical Fit = Preferred Size | Content 높이가 항목 수에 따라 커져야 스크롤 범위가 생긴다 |
| 4 | `content`의 Vertical Layout Group | Child Force Expand **Height = On** | Height = **Off** (Width는 On 유지) | 켜져 있으면 남는 공간을 항목 사이에 나눠 넣어 간격이 벌어진다. 이전 런타임 코드가 이 값을 Off로 덮어쓰고 있었다 |

`content`의 Child Alignment는 이미 Upper Center(=1)라 그대로 두면 된다. `list`의 Vertical Layout Group도
이미 비활성이다.

**증상 판별에 도움이 되는 점**: 1~4를 맞춘 뒤에도 "최초 오픈에서 가운데부터 보이는" 현상이 남는다면
그때는 스크롤 위치 문제이고 이 컴포넌트가 처리한다. 반대로 1~4를 맞추기 전이라면 스크롤 자체가
성립하지 않는 상태이므로, 이 컴포넌트를 붙여도 화면은 그대로다.

## 6. 검증 상태

### 이 환경에서 확인한 것

- `Assets/Scripts` 전체를 Unity와 같은 정의(`UNITY_STANDALONE_WIN` 포함)로 Roslyn 컴파일 → **오류 0건**.
- `CharacterSwapPanel` 및 다른 기존 스크립트에 변경 없음(이 컴포넌트는 완전히 독립적이다).
- 프리팹 YAML로 `content`의 Layout Group 값(Child Alignment = Upper Center, Child Force Expand Height =
  On), ContentSizeFitter 부재, ScrollRect의 Horizontal/Vertical 값을 확인(5장 표의 근거).

### 확인하지 못한 것

- **Play Mode 실행 전체**(에디터가 프로젝트 락을 잡고 있어 실행할 수 없다). 지시서 검증 1~8번은
  에디터에서 직접 확인해야 하며, 5장을 먼저 맞춘 뒤에 확인해야 의미가 있다.

## 7. 구현하지 않은 것

- 프리팹 값 수정(5장) - 프리팹은 사람이 관리하는 영역이라 파일로 손대지 않았다.
- 스크롤 위치의 저장/복원(앱 재실행 후 위치 기억)
- 다른 스크롤 UI에 실제로 컴포넌트를 붙이는 작업(구조만 열어 두었다 - 인벤토리 슬롯 격자는 스크롤이
  없어 대상이 아니다)
