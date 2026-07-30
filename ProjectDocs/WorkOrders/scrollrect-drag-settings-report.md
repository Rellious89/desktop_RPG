# ScrollRect 클릭&드래그 스크롤 허용 설정 완료 보고서

작업일: 2026-07-30

프리팹/씬 파일은 건드리지 않았다(Unity 에디터가 프로젝트 락을 잡고 있다).

## 1. 구현한 것

`Assets/Scripts/Common/ScrollRectDragSettings.cs` (신규) 하나뿐이다. 기존 스크립트는 변경하지 않았다.

```csharp
[SerializeField] private bool allowPointerDrag = true;      // Inspector: Allow Pointer Drag
[SerializeField] private ScrollRect targetScrollRect;       // 비우면 부모에서 찾는다
public bool AllowPointerDrag { get; set; }                  // 런타임 토글
```

| 설정 | 클릭&드래그 | 휠 |
| --- | --- | --- |
| Allow Pointer Drag = On (기본값) | O | O |
| Allow Pointer Drag = Off | **X** | **O** |

기본값 On은 이미 있는 다른 스크롤 UI에 붙였을 때 동작이 바뀌지 않게 하기 위한 값이다. 스크롤 UI마다
컴포넌트를 하나씩 붙여 개별로 설정하며, 전역에서 강제하지 않는다.

## 2. 구현 방식 선택 - 왜 Viewport 차단 방식인가

지시서가 제시한 두 방식 중 **(2) Viewport에 드래그 차단 컴포넌트**를 골랐다. (1) ScrollRect 상속은 이
프로젝트에서 두 가지 실질적인 문제가 있다.

1. **에디터 연결이 끊긴다.** 상속 컴포넌트로 바꾸려면 프리팹의 ScrollRect를 제거하고 새 컴포넌트를
   붙여야 하는데, 그러면 Viewport/Content 연결과 Movement Type / Scroll Sensitivity 같은 값을 다시
   지정해야 한다. 지시서의 "유지해야 하는 기존 설정" 첫 항목과 정면으로 부딪힌다.
2. **Inspector에 설정이 보이지 않는다.** Unity의 `ScrollRectEditor`는
   `[CustomEditor(typeof(ScrollRect), true)]`로 파생 클래스까지 담당하고, `OnInspectorGUI`가 자기가
   아는 프로퍼티만 그린다(`DrawPropertiesExcluding`을 쓰지 않는다). 즉 상속 클래스에 필드를 추가해도
   `Allow Pointer Drag`가 Inspector에 나타나지 않아, 전용 Editor 스크립트를 하나 더 만들어야 한다.
   (Unity 2022.3.62f3의 `com.unity.ugui/Editor/UI/ScrollRectEditor.cs`에서 확인했다.)

## 3. 동작 원리

- Unity EventSystem은 드래그 대상을 "포인터가 누른 오브젝트에서 **위로 올라가며** 만나는 첫
  `IDragHandler`"로 정한다. 이 컴포넌트를 Viewport(= ScrollRect 오브젝트의 자식, 리스트 항목의 조상)에
  두면 ScrollRect보다 먼저 잡히므로 드래그 이벤트가 ScrollRect까지 올라가지 않는다.
- On일 때는 받은 이벤트를 ScrollRect의 같은 메서드로 **그대로 넘겨준다** → 기존 드래그 동작이 유지된다.
  ScrollRect의 드래그 처리는 `eventData.pointerDrag`를 보지 않고 버튼/좌표만 쓰므로 전달만으로 충분하다.
- 처리하는 인터페이스는 지시서가 요구한 네 가지 전부다:
  `IInitializePotentialDragHandler`, `IBeginDragHandler`, `IDragHandler`, `IEndDragHandler`.
- **휠은 손대지 않는다.** 휠은 `IScrollHandler`로 전달되고 이 컴포넌트는 그 인터페이스를 구현하지
  않으므로, 휠 이벤트는 지금까지대로 ScrollRect까지 올라간다. `Scroll Sensitivity = 0`이나
  `ScrollRect.enabled = false`는 휠까지 막으므로 쓰지 않았다.
- 드래그 도중에 설정이 Off로 바뀌면 ScrollRect가 드래그 상태에 갇히지 않도록 종료를 한 번 넘겨준다.
- 참조를 못 찾으면(Viewport가 아닌 엉뚱한 곳에 붙인 경우) On이어도 드래그를 넘길 곳이 없어 "조용히
  막힌" 상태가 되므로 오류를 남긴다.

## 4. 패널 드래그와의 관계

영향이 없다. 패널 이동은 타이틀 영역(`bg/top/bg_top`)의 `PanelDragHandle`이 담당하고, 그 오브젝트는
이 컴포넌트의 조상이 아니라 계층의 다른 가지에 있다. 리스트 안에서 드래그를 막아도 타이틀 드래그는
그대로 동작하고, 반대로 타이틀 드래그가 리스트 스크롤을 건드리지도 않는다.

## 5. 알려진 경계 (정직하게 남긴다)

리스트 항목이 Viewport를 다 덮지 않아 **ScrollRect 오브젝트 자신의 배경(`list`의 Image)이 직접 눌리는
경우**에는 이벤트가 Viewport를 거치지 않으므로 차단되지 않는다. EventSystem이 `list`에서 곧바로
ScrollRect를 찾기 때문이다.

다만 그 상황은 Content가 Viewport보다 작아 **스크롤할 것이 없는 경우**라, Movement Type이 Clamped면
화면이 실제로 움직이지 않는다(Elastic이면 약간 튕긴다). 완전히 막아야 한다면 Viewport에 알파 0
Image를 두어 그 영역의 레이캐스트를 Viewport가 받게 하면 되지만, 프리팹 구조를 바꾸지 않는다는
이번 범위에서는 하지 않았다.

## 6. Unity 에디터에서 해야 하는 연결 작업

`pn_CharacterSwap` 프리팹의 **`bg/list/viewport`** 에 `ScrollRectDragSettings`를 추가한다.

| 설정 | 값 |
| --- | --- |
| Allow Pointer Drag | **Off** |
| Target Scroll Rect | 비워 둬도 된다(부모 `list`의 ScrollRect를 찾는다). 명시하려면 `list`의 ScrollRect 연결 |

다른 스크롤 UI에 쓸 때도 같은 방식으로 그 UI의 Viewport에 붙이고 `Allow Pointer Drag`만 정하면 된다.

변경하지 않은 것: ScrollRect의 Viewport/Content 연결, Content Size Fitter, Vertical Layout Group,
Scroll Sensitivity, 스크롤 위치 유지, `ScrollRectInitialPosition`, `PanelDragHandle`,
`WindowInputRegion`, 스크롤바 미사용 상태.

## 7. 검증 상태

### 이 환경에서 확인한 것

- `Assets/Scripts` 전체를 Unity와 같은 정의(`UNITY_STANDALONE_WIN` 포함)로 Roslyn 컴파일 → **오류 0건**.
  Editor도 현재 빌드 타깃이 Windows라 같은 경로로 컴파일된다.
- 기존 스크립트 변경 없음(이 컴포넌트는 완전히 독립적이다).
- Unity 설치본의 `ScrollRectEditor.cs`에서 파생 클래스 필드가 그려지지 않는 것을 확인(2장 근거).
- 프리팹 계층에서 `viewport`가 `list`(ScrollRect)의 자식이고 `content`/항목의 조상인 것을 확인.

### 확인하지 못한 것

- **Play Mode 및 Windows 빌드 실행 전체**(에디터가 프로젝트 락을 잡고 있다). 지시서 검증 1~9번은
  에디터/빌드에서 직접 확인해야 한다.
- 검증 2번(휠 스크롤)은 앞선 보고서에서 지적한 프리팹 값(ScrollRect Vertical = On, `content` 앵커/피벗,
  ContentSizeFitter, Child Force Expand Height = Off)이 맞춰진 뒤에야 의미가 있다 - 그 전에는 스크롤
  자체가 성립하지 않는다.

## 8. 구현하지 않은 것

- 스크롤바 추가(지시서대로 제외)
- 프리팹 값 수정
- 터치 입력 구분(현재는 포인터 드래그 전체가 대상이다 - 데스크톱 전용 앱이라 구분할 이유가 없다)
