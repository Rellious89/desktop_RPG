# 네이티브 마우스 입력 영역(WindowInputRegion) 완료 보고서

작업일: 2026-07-30

씬과 프리팹 파일은 건드리지 않았다(Unity 에디터가 프로젝트 락을 잡고 있다). 컴포넌트를 붙이는
작업은 5장 체크리스트로 넘긴다.

## 1. 신규 컴포넌트

`Assets/Scripts/DesktopWindow/WindowInputRegion.cs`

```csharp
[SerializeField] private bool receiveMouseInput = false;   // Inspector 표시: Receive Mouse Input
```

| 설정 | 동작 |
| --- | --- |
| Off (기본값) | 이 영역은 마우스 입력을 통과시킨다(바탕화면/다른 앱으로 넘어간다) |
| On | 이 영역에서 마우스 입력을 받는다(클릭 관통을 막는다) |

- `OnEnable`에서 컨트롤러에 자신을 등록하고, `OnDisable`에서 해제한다. 컴포넌트를 끄거나 GameObject를
  끄면(패널이 닫히면) 둘 다 `OnDisable`을 거치므로 자동으로 해제된다.
- 컨트롤러가 늦게 초기화된 경우를 대비해 `Start`에서 한 번 더 등록을 시도한다(중복 등록은 무시된다).
- Windows 빌드가 아닌 환경에서는 등록만 되고 아무 효과가 없다(클릭 관통 자체가 없다).

이 컴포넌트는 Unity UI 클릭을 처리하지 않는다 - 투명 데스크톱 창의 네이티브 마우스 관통 영역만 정한다.

## 2. 부모 하나로 자식 전체를 커버하는 근거

네이티브 판정은 **화면 좌표 사각형 하나**와 커서 좌표를 비교하는 것뿐이다. 따라서 부모 UI에 하나만
붙이고 켜면, 그 사각형 안에 들어 있는 자식(닫기 버튼, 아이템 슬롯, 내부 버튼 등)이 모두 함께 입력을
받는다. 자식마다 붙일 필요가 없다.

세부 영역만 따로 켜고 싶을 때는 그 자식에 하나 더 붙이면 된다 - 등록은 여러 개가 동시에 유효하므로
서로 간섭하지 않는다.

## 3. TransparentWindowController 변경

### 단일 필드 → 다중 등록

| 이전 | 이후 |
| --- | --- |
| `modalClickableRect` (RectTransform 하나) | `List<WindowInputRegion> inputRegions` |
| `SetModalClickableRect(rect)` | `RegisterInputRegion(region)` / `UnregisterInputRegion(region)` |
| `modalUiScreenRect` + `hasModalUiScreenRect` | `cachedInputRegionRects[]` + `cachedInputRegionCount` |

`SetModalClickableRect`는 제거했다(호출부는 `ModalPanel` 하나뿐이었다).

### 판정

```text
커서가 등록된 입력 영역 중 하나에 포함  → 클릭 관통 안 함
어느 영역에도 포함되지 않음            → (기존) ControlDock 영역 판정 → 그 밖이면 클릭 관통
```

Layout Mode에서의 판정 경로(등록된 배치 그룹 기준)는 손대지 않았다.

### 스레드 경계와 GC

기존 그룹 좌표 캐시와 같은 규칙을 그대로 따랐다.

- RectTransform 좌표 계산은 **메인 스레드의 Update에서만** 한다
  (`RecomputeInputRegionScreenRects`, 기존 `RecomputeControlDockScreenRect` 옆).
- 훅 스레드가 호출하는 `IsScreenPointClickThrough` → `IsPointInsideAnyInputRegion`은 Unity API를
  전혀 부르지 않고 계산된 화면 좌표 사각형 배열만 비교한다.
- 등록 목록(`List`)과 캐시 배열은 재사용한다. 캐시 배열은 등록 영역 수가 늘어난 순간에만 커지고
  (줄이지 않음) 원소는 제자리에서 덮어쓰므로 **매 프레임 GC 할당이 없다.**
- 원소를 모두 채운 뒤에 개수를 발행한다 - 훅 스레드가 아직 채우지 않은 칸을 읽지 않는다.
  캐시 배열 참조는 읽는 쪽에서 지역 변수로 먼저 잡고 개수를 배열 길이로 자른다 - 메인 스레드가 같은
  순간에 배열을 더 큰 것으로 교체해도 범위를 벗어나지 않는다.
- `Receive Mouse Input`이 꺼졌거나 계층상 비활성인 영역은 매 프레임 판정에서 건너뛴다 - 해제를
  놓쳐도 그 자리가 계속 클릭을 잡아먹지 않고 여기서 회복된다.

## 4. ModalPanel과의 관계

`ModalPanel`에서 클릭 관통 등록 코드를 **없앴다**. 등록은 패널 루트에 붙인 `WindowInputRegion`이
자기 활성 상태에 맞춰 스스로 하므로, 패널이 열리면 등록되고 닫히면 해제된다(패널과 같은
GameObject이기 때문).

- 열릴 때: `WindowInputRegion` 등록(컴포넌트가 스스로) + InputBlocker 활성화(ModalPanel)
- 닫힐 때: `WindowInputRegion` 해제(컴포넌트가 스스로) + InputBlocker 비활성화(ModalPanel)

InputBlocker(Unity UI 이벤트가 뒤쪽 UI로 가지 않게 막음)와 WindowInputRegion(네이티브 마우스 관통
제어)은 서로 다른 층을 담당하므로 둘 다 유지했다. 열기/닫기/InputBlocker 동작 자체는 바꾸지 않았다.

패널 루트에 `Receive Mouse Input`이 켜진 `WindowInputRegion`이 없으면 패널이 열릴 때 **경고를 한 번
남긴다** - Windows에서 "보이는데 안 눌리는" 증상의 원인을 로그에서 바로 알 수 있게 하기 위함이다.
자동으로 붙이지는 않는다(입력을 받을 영역은 씬에서 명시적으로 정하는 값이다).

## 5. Unity 에디터에서 해야 하는 연결 작업

### 5-1. ControlDock (현재 클릭이 안 되는 원인)

확인 결과 `controlDockRect`에는 `dock_btn`(ControlDock 전체 400x50)이 연결되어 있고,
`btn_character`와 `btn_inventory`는 그 **아래로 뻗어 나온 별도 영역**(각각 200x60, 앵커 (1,0))이라
기존 클릭 영역 밖이었다. 그래서 클릭이 도달하지 않았다.

다음 두 오브젝트에 `WindowInputRegion`을 추가하고 **Receive Mouse Input = On**으로 설정한다.

| 오브젝트 | 씬 fileID | 크기 |
| --- | --- | --- |
| `ControlDock/btn_character` (바깥쪽) | 328121025 | 200x60 |
| `ControlDock/btn_inventory` (바깥쪽) | 2005767649 | 200x60 |

바깥쪽(부모) 오브젝트에 붙이면 내부의 실제 Button(`btn_change` / 안쪽 `btn_inventory`)과 텍스트가
모두 같은 영역에 포함된다. `controlDockRect`(dock_btn) 설정은 그대로 둔다 - 기존 클릭 영역을 유지한다.

### 5-2. 패널

두 패널 프리팹 **루트**에 `WindowInputRegion`을 추가하고 **Receive Mouse Input = On**으로 설정한다.

- `Assets/Art/UI/Prefab/panel/pn_CharacterSwap.prefab`
- `Assets/Art/UI/Prefab/panel/pn_Inventory.prefab`

패널 루트는 씬에서 비활성 상태로 시작하므로 열릴 때만 등록되고 닫히면 해제된다. 두 패널이 동시에
열리면 두 영역이 모두 등록되어 서로를 덮어쓰지 않는다.

## 6. 검증 상태

### 이 환경에서 확인한 것

- **Windows 정의(`UNITY_STANDALONE_WIN`)로 Roslyn 컴파일 → 오류 0건.** Editor도 현재 빌드 타깃이
  Windows라 같은 경로로 컴파일된다.
- 씬 좌표를 읽어 `controlDockRect`가 `dock_btn`이고 `btn_character`/`btn_inventory`가 그 영역 밖이라는
  것을 수치로 확인(이번 문제의 직접 원인).
- 훅 스레드 경로(`IsScreenPointClickThrough`)에 Unity API 호출이 새로 들어가지 않았음을 확인.

### 확인하지 못한 것

- **Play Mode 및 Windows 빌드 실행 전체**(에디터가 프로젝트 락을 잡고 있다). 지시서 검증 1~13번은
  Windows 빌드에서 직접 확인해야 한다 - 클릭 관통은 Win32 `WS_EX_TRANSPARENT` 경로라 macOS
  에디터에서는 이 코드가 아예 실행되지 않는다.

### 함께 발견한 기존 문제 (이번 작업과 무관, 고치지 않음)

macOS 등 **비Windows 타깃**으로 컴파일하면 이번 변경과 무관한 기존 오류가 하나 있다.

```text
Assets/Scripts/Common/LayoutModeController.cs(102): TransparentWindowController에
'SaveOverlayPlacement' 정의가 없습니다
```

`SaveOverlayPlacement()`가 `#if UNITY_STANDALONE_WIN` 안에만 선언되어 있는데 `LayoutModeController`는
플랫폼 가드 없이 호출한다. HEAD 커밋에도 같은 상태이므로 이번 작업이 만든 문제가 아니고, 지시서의
검증 항목(Editor + Windows Standalone)에도 영향이 없다. 다른 공개 메서드들처럼 시그니처를 가드 밖으로
빼고 본문만 가드하면 해결된다.

## 7. 구현하지 않은 것

- UI 디자인/패널 배치 변경
- 다중 모달을 위한 Unity UI 쪽 입력 차단 구조 확장(InputBlocker는 현재 구조 유지)
- 비Windows 플랫폼의 네이티브 마우스 관통 구현
