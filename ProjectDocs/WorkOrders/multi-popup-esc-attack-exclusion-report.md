# 다중 팝업 · ESC 닫기 · 공격 제외 키 테이블 완료 보고서

작업일: 2026-07-30

씬/프리팹 파일은 건드리지 않았다(Unity 에디터가 프로젝트 락을 잡고 있다). 컴포넌트를 붙이는 작업은
8장 체크리스트로 넘긴다.

## 1. 다중 패널 활성화

패널을 열 때 다른 패널을 닫는 코드는 원래부터 없었고, 실제로 하나만 열리게 만든 원인은 **전체 화면
InputBlocker**였다(먼저 열린 패널의 차단막이 ControlDock 버튼과 다른 패널 클릭을 모두 먹었다).

`ModalPanel`에 패널별 옵션을 추가했다.

```csharp
[SerializeField] private bool blockBackgroundInput = false;   // Inspector: Block Background Input
```

- **기본값 Off** → 차단막을 아예 만들지 않는다. `pn_CharacterSwap` / `pn_Inventory`는 새 필드가
  기본값으로 들어가므로 별도 설정 없이 Off다.
- On으로 켠 경우에만 기존 차단막 로직(패널별 이름 `InputBlocker_<패널 이름>`, OnDisable에서 확실히
  해제)이 그대로 동작한다 - 정말로 하나만 떠야 하는 경고/확인창용으로 남겼다.

이미 열려 있는 패널의 버튼을 다시 누르면 `ModalPanel.Open()`이 새로 만들지 않고 그 패널을 활성 패널로
올린다(중복 인스턴스 생성 없음).

## 2. 활성 순서 관리 - `PopupPanelManager`

`Assets/Scripts/Common/PopupPanelManager.cs` (신규). Panel_UI에 하나 둔다.

```text
목록 앞쪽   = 가장 오래전에 활성화된 패널
목록 마지막 = 현재 활성 패널(= 가장 앞에 보이고, ESC로 가장 먼저 닫힌다)
```

활성화 처리는 `FocusPanel(ModalPanel)` **한 메서드**만 거친다.

```text
1. 목록에서 기존 위치 제거
2. 목록 마지막에 추가
3. panel.transform.SetAsLastSibling()   (Panel_UI 안에서만 순서 변경)
```

목록 하나가 표시 순서와 닫기 순서를 함께 결정하므로 두 순서가 어긋날 수 없다. `SetAsLastSibling`은
패널의 부모(Panel_UI) 안에서만 동작해서 ToastLayer/HUD 등 다른 시스템 UI와의 상대 순서는 영향받지 않는다.

`FocusPanel`이 호출되는 경우:

| 경우 | 호출 지점 |
| --- | --- |
| 패널이 새로 열림 | `ModalPanel.OnEnable` → `Register` |
| 이미 열린 패널의 버튼을 다시 누름 | `ModalPanel.Open` |
| 패널 내부(자식 UI 포함) 클릭 | `PopupPanelManager.UpdateFocusFromClick` |
| 패널 타이틀 드래그 시작 | `PanelDragHandle.OnPointerDown` |

`PanelDragHandle`은 예전에 직접 `SetAsLastSibling`을 호출했는데, 그러면 관리자 목록과 화면 순서가
어긋난다 - 대상이 `ModalPanel`이고 관리자가 있으면 `FocusPanel`에 맡기도록 바꿨다(ModalPanel이 없는
UI에 붙인 경우에는 예전처럼 직접 옮긴다).

### 자식 UI 클릭도 부모 패널을 활성화하는 방법

루트의 `IPointerDownHandler`만으로는 부족하다 - 패널 안의 Button/ScrollRect가 포인터 이벤트를 먼저
소비하면 루트 핸들러가 호출되지 않는다. 그래서 관리자가 마우스 버튼이 눌린 프레임에 **UI 레이캐스트를
직접 쏘고**, 가장 앞에 맞은 오브젝트에서 부모 쪽으로 올라가며 `ModalPanel`을 찾아 활성화한다.

- 버튼/리스트/슬롯을 눌러도, 뒤쪽 패널의 노출된 영역을 눌러도 같은 판정이 적용된다.
- `RaycastAll` 결과는 앞에 있는 것부터 정렬되므로 겹친 영역에서는 화면상 앞의 패널만 잡힌다.
- `PointerEventData`와 결과 리스트를 재사용해서 클릭마다 할당이 생기지 않는다.
- 이미 활성 패널이면 목록을 건드리지 않는다.

## 3. 등록 / 해제

| 시점 | 처리 |
| --- | --- |
| `ModalPanel.OnEnable` | `Register` → 등록 + 활성 패널 지정 |
| 클릭 / 드래그 시작 | `FocusPanel` → 순서 갱신 |
| `ModalPanel.OnDisable` | `Unregister` (닫기 버튼으로 닫아도 이 경로를 지난다) |
| `ModalPanel.OnDestroy` | `Unregister` (파괴 경로 안전망) |

`FocusPanel`이 "제거 후 추가"라 중복 등록이 생기지 않는다. `CloseTopPanel`은 닫기 직전에 파괴됐거나
이미 비활성인 항목을 걷어내므로, 그런 패널이 ESC 대상이 되어 "눌렀는데 아무 것도 안 닫히는" 상황이
생기지 않는다.

## 4. ESC로 패널 닫기

```csharp
if (!GlobalKeyboardHook.WasExcludedKeyDownThisFrame(closeTopPanelKey)) return;
if (!TransparentWindowController.HasWindowFocus) return;
CloseTopPanel();
```

- **포커스 판정**: `TransparentWindowController.HasWindowFocus`(신규)를 쓴다. Windows 빌드에서는
  `GetForegroundWindow()`와 우리 창 핸들을 직접 비교한다 - 이 창은 보더리스/투명/Always On Top으로
  스타일을 바꿔 놓기 때문에 `Application.isFocused`를 그대로 신뢰하기 어렵고, 네이티브 상태가 유일하게
  확실한 근거다. 창 핸들을 아직 못 찾았거나(시작 직후) 에디터/다른 플랫폼이면 `Application.isFocused`로
  대체한다.
- 다른 프로그램에 포커스가 있을 때 누른 ESC로는 패널이 닫히지 않는다(전역 후크가 키를 잡아내더라도
  포커스 검사에서 걸러진다).
- 열린 패널이 없으면 아무 일도 하지 않는다(`CloseTopPanel`이 false를 반환하고 끝).
- **KeyDown 1회당 패널 하나**: 아래 5장의 자동 반복 필터가 근거다.

## 5. 공격 제외 키 테이블

`Assets/Scripts/DesktopWindow/AttackInputExclusionTable.cs` (신규 ScriptableObject) +
`Assets/Data/Input/AttackInputExclusionTable.asset` (신규, 초기 등록 키 = `Escape`).

```csharp
bool IsExcludedFromAttack(KeyCode key);
IReadOnlyList<KeyCode> ExcludedKeys { get; }
```

### 공통 입력 분류 단계에서 걸러낸다

제외는 공격 처리의 마지막 단계가 아니라 **키를 식별하는 단계**에서 일어난다.

```text
키 입력 발생(WH_KEYBOARD_LL 콜백)
  → vkCode로 실제 키 식별
  → 제외 키 vkCode 목록과 비교
  → 제외 키면 pendingAnyKey를 올리지 않는다(공격 입력 이벤트가 아예 만들어지지 않는다)
  → 일반 키면 지금까지대로 pendingAnyKey 설정
```

`AnyKeyDownThisFrame`에 애초에 포함되지 않으므로, 공격/콤보/누적 충전/Charge Movement/행동력 소모
어디에도 `if (key == Escape)` 같은 코드가 없다. 캐릭터별·모션별 예외 처리 경로도 없어서 모든 캐릭터에
자동으로 같게 적용된다.

### GlobalKeyboardHook 변경

| 이전 | 이후 |
| --- | --- |
| `ExcludedKey` (KeyCode 하나) | 테이블 에셋 + `RegisterExcludedKey(KeyCode)`의 합집합 |
| `ExcludedKeyDownThisFrame` (bool 하나) | `WasExcludedKeyDownThisFrame(KeyCode)` |
| `cachedExcludedVkCode` (int 하나) | `ExclusionSnapshot`(vkCode/PendingDown/Held 배열 묶음) |

- `RegisterExcludedKey`는 자기 단축키를 Inspector에 들고 있는 컴포넌트용이다 -
  `TransparentWindowController`의 창 배치 모드 키(F9)가 이 경로로 옮겨졌다. 데이터로 관리하는 UI
  단축키는 테이블 에셋에 넣는다.
- **자동 반복(auto-repeat)을 제외 키만 걸러낸다.** Windows는 키를 누르고 있으면 WM_KEYDOWN을 반복
  전송하므로, 그대로 두면 ESC를 한 번 길게 누르는 것으로 열린 패널이 전부 닫힌다. 제외 키에 대해서만
  WM_KEYUP까지 추적해 "처음 눌린 순간" 한 번만 신호를 낸다. **일반 키(공격 입력)의 반복 처리는 전혀
  바꾸지 않았다.**
- 스레드 경계는 기존 규칙 그대로다: 훅 스레드는 Unity API를 부르지 않고 vkCode 배열 비교와
  `Interlocked`만 쓴다. vkCode/pending/held 배열을 한 객체(`ExclusionSnapshot`)로 묶어 참조 하나만
  교체하므로, 목록이 바뀌는 순간에도 훅이 길이가 다른 배열을 섞어 읽지 않는다. 목록 변경은 시작
  시점의 몇 번뿐이라 매 프레임 할당이 없다.
- Virtual Key 변환 지원 범위에 `Escape`(0x1B)를 추가했다. 변환할 수 없는 키를 테이블에 넣으면
  Windows 빌드에서 제외되지 않으므로 등록 시 경고를 남긴다.
- 에디터/비Windows/진단 모드 경로는 `Input.GetKeyDown`으로 제외 키를 확인한다(이쪽은 자동 반복이
  이미 걸러져 있다).

### ESC 두 판정의 분리

| 규칙 | 포커스 조건 | 근거 |
| --- | --- | --- |
| 공격 입력 제외 | **무관** | 훅이 키를 식별하는 단계에서 제외 |
| 패널 닫기 | 이 앱에 포커스가 있을 때만 | `HasWindowFocus` |

따라서 다른 프로그램에서 ESC를 눌러도 KeyBuddy 패널이 닫히지도, 공격이 일어나지도 않는다.

## 6. WindowInputRegion과의 관계 (변경 없음)

이미 패널 루트마다 `WindowInputRegion`을 붙이고 컨트롤러가 **여러 영역을 동시에 등록**하는 구조라
그대로 두면 된다.

- `Panel_UI` 전체를 하나의 입력 영역으로 만들지 않았다(그렇게 하면 화면 전체가 마우스 통과되지 않는다).
- 활성 패널 각각의 RectTransform만 등록되고, 패널이 닫히면 그 영역만 해제된다.
- 두 패널이 함께 열리면 두 영역이 모두 유효해서 둘 다 클릭된다.
- 패널 외부는 기존대로 마우스 통과다.

## 7. 추가/수정한 파일

### 신규

| 파일 | 역할 |
| --- | --- |
| `Assets/Scripts/Common/PopupPanelManager.cs` | 활성 순서 목록, 클릭 포커스 판정, ESC 닫기 |
| `Assets/Scripts/DesktopWindow/AttackInputExclusionTable.cs` | 공격 제외 키 테이블(ScriptableObject) |
| `Assets/Data/Input/AttackInputExclusionTable.asset` | 초기 등록 키 = Escape |

### 수정

| 파일 | 변경 |
| --- | --- |
| `Assets/Scripts/Common/ModalPanel.cs` | `blockBackgroundInput` 옵션(기본 Off), 관리자 등록/해제, `Open`에서 활성 패널로 올리기, `OnDestroy` 안전망 |
| `Assets/Scripts/Common/PanelDragHandle.cs` | 순서 변경을 `PopupPanelManager.FocusPanel`에 위임 |
| `Assets/Scripts/DesktopWindow/GlobalKeyboardHook.cs` | 제외 키 다중화 + 테이블 연결 + 자동 반복 필터 |
| `Assets/Scripts/DesktopWindow/TransparentWindowController.cs` | `HasWindowFocus` 추가, 제외 키 API 이전 |
| `Assets/Scripts/DesktopWindow/Win32Interop.cs` | `WM_KEYUP`/`WM_SYSKEYUP`, `GetForegroundWindow` 추가 |

## 8. Unity 에디터에서 해야 하는 연결 작업

1. **`Panel_UI`에 `PopupPanelManager` 추가.** `Close Top Panel Key`는 기본값 `Escape` 그대로 둔다.
2. **`GlobalKeyboardHook`(DesktopStage)의 `Attack Input Exclusions`** 에
   `Assets/Data/Input/AttackInputExclusionTable.asset`을 연결한다.
   (연결하지 않으면 F9만 제외되고 ESC가 공격 입력으로 흘러간다.)
3. 두 패널 프리팹(`pn_CharacterSwap`, `pn_Inventory`)의 `Block Background Input`이 **Off**인지 확인한다
   (새 필드라 기본값 Off로 들어온다).
4. 두 패널 루트의 `WindowInputRegion` / `Receive Mouse Input = On`은 그대로 유지한다.

## 9. 검증 상태

### 이 환경에서 확인한 것

- Windows 정의(`UNITY_STANDALONE_WIN`)로 `Assets/Scripts` 전체 Roslyn 컴파일 → **오류 0건**.
  Editor도 현재 빌드 타깃이 Windows라 같은 경로로 컴파일된다.
- 비Windows 정의로도 컴파일해 이번 변경이 새 오류를 만들지 않는 것을 확인했다(기존 문제 1건만 남는다 -
  아래 참고).
- `ExcludedKey`/`ExcludedKeyDownThisFrame`의 옛 호출부가 프로젝트에 남아 있지 않은 것을 grep으로 확인.
- 훅 콜백에 Unity API 호출이 새로 들어가지 않았음을 확인(배열 비교와 `Interlocked`뿐).

### 확인하지 못한 것

- **Play Mode 및 Windows 빌드 실행 전체**(에디터가 프로젝트 락을 잡고 있다). 지시서의 검증 항목
  전부(다중 패널 / 활성 순서 / ESC / 공격 제외 / 추가 10개)는 Windows 빌드에서 확인해야 한다 -
  전역 후크와 포커스 판정 모두 Win32 경로라 macOS 에디터에서는 실행되지 않는다.

### 알아둘 특성

- `GlobalKeyboardHook`에 실행 순서를 지정하지 않았으므로, 같은 프레임 안에서 후크 Update보다 먼저
  도는 컴포넌트는 한 프레임 전의 입력 값을 볼 수 있다. 값은 후크 Update마다 새로 대입되어 한 번의
  누름이 두 번 관측되지는 않으므로(ESC가 패널 두 개를 닫는 일은 없다) 기존 동작을 바꾸지 않기 위해
  실행 순서는 손대지 않았다.
- 비Windows 타깃 컴파일 오류(`LayoutModeController` → `SaveOverlayPlacement`)는 이번 작업과 무관한
  기존 문제이며 HEAD 커밋에도 있다. 지시서 검증 범위(Editor + Windows Standalone)에는 영향이 없다.

## 10. 구현하지 않은 것

지시서의 제외 목록 전부(단축키 재지정 UI, 커스텀 바인딩, 조합 단축키, 충돌 검사, 텍스트 입력 상태
판정, 고급 포커스 보호, 팝업 자동 정렬/겹침 방지, 다중 모니터 배치, 동일 패널 다중 인스턴스).
