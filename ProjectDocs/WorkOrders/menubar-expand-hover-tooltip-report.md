# 메뉴바 확장 + Hover Tooltip 개편 완료 보고서

작업일: 2026-08-07 (1차), 2026-08-07 (2차 - 툴팁 미출력 수정 + 자동 접힘)
대상 씬: `Assets/Scenes/desktopScene_ReSize.unity` 의 `Canvas/tgl_Panel`
대상 프리팹: `Assets/Art/UI/Prefab/HoverTooltip.prefab`

씬과 프리팹 파일은 **건드리지 않았다**(Unity 에디터가 프로젝트 락을 잡고 있다).

---

# 2차: 툴팁 미출력 원인과 수정

## 1. 원인 - 컴포넌트가 한 개도 붙어 있지 않았다

씬 파일을 GUID로 직접 세어 보니 이렇다.

```text
MenuBarExpander        1개   ← 붙음
HoverTooltipController 1개   ← 붙음 (프리팹/Delay=1 정상 할당)
HoverTooltipTrigger    0개   ← 하나도 안 붙음
```

1차 보고서 6장 체크리스트의 4번(버튼 5개에 `HoverTooltipTrigger` 추가)이 적용되지 않았다.
`HoverTooltipTrigger`가 `IPointerEnterHandler`를 구현한 유일한 컴포넌트이므로, **버튼 위에 마우스를
올려도 호출될 대상 자체가 존재하지 않았다.**

증상이 정확히 이 원인과 맞아떨어진다.

- 반응 없음 → PointerEnter를 받을 컴포넌트가 없음
- 아무리 기다려도 안 뜸 → 대기 코루틴을 시작시키는 쪽이 없음
- **Error/Exception 없음** → 없는 컴포넌트는 오류를 남기지 않는다

메뉴 확장(`MenuBarExpander`)만 정상 동작한 것도 같은 이유다. 그쪽은 컴포넌트가 붙어 있었다.

지시서의 확인 항목 8가지를 실제로 점검한 결과는 이렇다. 1번이 유일한 원인이었고 나머지는 모두 정상이었다.

| # | 점검 항목 | 결과 |
|---|---|---|
| 1 | Trigger의 OnPointerEnter/Exit가 호출되는가 | **아니오 - 컴포넌트 자체가 없었다 (원인)** |
| 2 | Trigger가 Controller를 찾는가 | 정상 (부모 tgl_Panel에서 찾음) |
| 3 | Controller에 프리팹이 할당됐는가 | 정상 (`HoverTooltip.prefab`) |
| 4 | Delay 코루틴이 시작되는가 | 정상 (요청이 오면 시작됨) |
| 5 | btnArea 활성화 이후 연결이 유효한가 | 정상 |
| 6 | 위치/Canvas/Sorting 문제인가 | 아니오 (레이캐스트/좌표 모두 정상) |
| 7 | 생성 직후 비활성화/제거되는가 | 아니오 |
| 8 | Raycast/EventSystem 구조 문제인가 | 아니오 - 아래 참조 |

8번은 실제 레이캐스트를 찍어서 확인했다. 버튼 위에서 히트가 정상적으로 잡히고, 그 히트에서
부모로 올라가면 트리거가 있어야 할 자리가 나온다.

```text
최상단 히트 = sp_deco, 상위 HoverTooltipTrigger = btn_change, 총 3개 히트
```

## 2. 수정 - 붙이는 일을 코드가 대신한다

버튼마다 손으로 붙이는 방식은 **하나만 빠뜨려도 오류 없이 그 버튼만 조용히 안 뜨고, 전부 빠뜨리면
기능이 아예 없는 것처럼 보인다.** 같은 실패가 다시 나오지 않도록 `HoverTooltipController`가 대상
영역 안의 `Button`마다 `HoverTooltipTrigger`를 직접 보장한다.

```csharp
[SerializeField] private Transform menuRoot;          // 비우면 MenuBarExpander의 펼침 영역을 쓴다
[SerializeField] private bool autoAttachTriggers = true;
```

- `menuRoot`를 비워 두면 같은 오브젝트의 `MenuBarExpander.ExpandedRoot`(= btnArea)를 그대로 쓴다.
  **씬에서 추가로 연결할 것이 없다.**
- 이미 붙어 있는 트리거는 건드리지 않으므로, 버튼별로 문구를 지정해 둔 트리거는 그대로 유지된다.
- 나중에 하위 메뉴 버튼이 이 영역 아래에 생겨도 같은 규칙으로 함께 잡힌다.
- 영역을 못 찾거나 Button이 하나도 없으면 **경고를 남긴다** - 조용히 아무것도 안 하는 상태를 없앤다.

검증에서 사용자의 현재 씬(트리거 0개)을 **그대로 열어** 확인했고, 실행 시 5개가 자동으로 붙었다.

## 3. 자동 접힘 (5초)

`MenuBarExpander`에 추가했다.

```csharp
[SerializeField] private float autoCollapseDelay = 5f;   // 0 이하면 자동 접힘 안 함
```

**"펼친 뒤 5초"가 아니라 "메뉴에서 마우스가 벗어난 뒤 5초"다.** 무조건 5초를 세면 버튼을 고르려고
마우스를 올려둔 사용자의 메뉴가 접힌다.

판정은 새 컴포넌트 `MenuPointerRegion`이 맡는다. btnArea에 하나만 붙으면 되는데, EventSystem이
포인터가 들어간 오브젝트에서 **부모 쪽으로 올라가며 Enter/Exit를 모두 보내기** 때문이다
(`StandaloneInputModule`의 `Send Pointer Hover To Parent`가 켜져 있는 것을 씬에서 확인했다).

- 안쪽 버튼 사이를 오갈 때는 btnArea에 Exit가 오지 않는다 → 타이머가 계속 멈춰 있다
- 영역 밖으로 나갈 때만 Exit가 온다 → 그때부터 5초를 센다
- 버튼 클릭은 그동안 포인터가 메뉴 안에 있으므로 같은 규칙에 자연히 포함된다
- 하위 메뉴가 btnArea 아래에 생겨도 그대로 포함된다

이벤트가 아니라 **상태**(`PointerInside`)로 둔 이유가 여기 있다. Enter는 들어올 때 한 번만 오므로,
이벤트만 세면 버튼 위에 마우스를 가만히 올려둔 사용자의 메뉴가 접혀 버린다.

`MenuPointerRegion`도 `MenuBarExpander`가 Awake에서 직접 붙이므로 씬 작업이 없다.

외부에서 타이머를 되돌릴 필요가 생기면(단축키 등) `NotifyMenuActivity()`를 부르면 된다.

## 4. 필드 이동 시 즉시 접힘

```csharp
[SerializeField] private bool collapseOnFieldMove = true;
[SerializeField] private FieldModeManager fieldModeManager;   // 비우면 씬에서 찾는다
```

**이동 로직은 옮기지도 복제하지도 않았다.** `FieldModeManager.FieldModeChanged`를 구독만 한다.

이 이벤트는 전환이 **받아들여졌을 때만** 발행된다(거부된 전환에서는 발행되지 않는다). 그래서
"실제로 이동한 경우"와 정확히 일치하고, 버튼을 가로채거나 `FieldModeUIController`를 고칠 필요가 없다.
`FieldModeUIController`는 한 줄도 바꾸지 않았다.

- ON → 이동 성공 직후 `Collapse()`
- OFF → 메뉴 유지, 이후 자동 접힘 규칙(5초)에 따라 접힘

## 5. 툴팁과 자동 접힘의 충돌 방지

메뉴가 접히면 btnArea가 꺼지고 → 안쪽 버튼의 `HoverTooltipTrigger.OnDisable`이 돌고 →
컨트롤러의 예약과 표시를 모두 거둔다. 접힘 경로가 자동 접힘이든, 필드 이동이든, 직접 호출이든
전부 `SetExpanded(false)` 하나를 지나므로 예외가 없다.

반대로 툴팁 대기(1초)가 자동 접힘(5초)을 방해하지도 않는다. 툴팁은 마우스가 메뉴 위에 있을 때만
뜨는데, 그동안은 자동 접힘 타이머가 애초에 돌지 않는다.

## 6. 수정 파일 (2차)

| 파일 | 내용 |
|---|---|
| `HoverTooltipController.cs` | `menuRoot` + `autoAttachTriggers` 추가, Awake에서 트리거 자동 부착 |
| `MenuBarExpander.cs` | `autoCollapseDelay`, `collapseOnFieldMove`, `fieldModeManager` 추가, `MenuPointerRegion` 자동 부착, `ExpandedRoot` 공개 |
| `MenuPointerRegion.cs` | **신규** - 메뉴 영역 위에 마우스가 있는지만 들고 있는 컴포넌트 |
| `HoverTooltipTrigger.cs` | 변경 없음 |
| `LocalizedTMPText.cs` | 변경 없음(1차의 `TextReference` 프로퍼티 그대로) |

**에디터에서 추가로 해야 할 연결 작업은 없다.** 필요하면 `tgl_Panel`의
`HoverTooltipController`/`MenuBarExpander` Inspector에서 Delay(1초)와 자동 접힘 시간(5초),
`Collapse On Field Move`를 조절하면 된다.

## 7. 확인 결과 - 실제 EventSystem 입력 경로

지난번 지적대로 `OnPointerEnter`를 직접 부르는 방식은 쓰지 않았다. `StandaloneInputModule`의
`inputOverride`에 좌표를 공급해서 **실제 경로를 그대로 태웠다.**

```text
FakeMouseInput(BaseInput) → StandaloneInputModule.Process()
  → EventSystem.RaycastAll()          ← 실제 히트 판정
  → HandlePointerExitAndEnter()       ← 실제 부모 버블링
  → ExecuteEvents.Execute(pointerEnterHandler)   ← 실제 핸들러 호출
```

합성된 것은 **마우스 좌표의 출처 하나뿐**이고, 히트 판정·버블링·핸들러 호출·클릭 판정은 전부 실제
코드가 돈다. 클릭도 `GetMouseButtonDown/Up`을 통한 실제 누름/뗌으로 처리했다.

**사용자의 현재 씬을 그대로 열었다**(컴포넌트를 추가하지 않았다). 즉 "추가 연결 없이 동작하는가"를
같이 확인한 셈이다.

환경: Unity 2022.3.62f3, macOS, batchmode(그래픽 켬), Screen 640x480, **scaleFactor 0.444**, Locale `en`.

| # | 항목 | 결과 | 근거 |
|---|---|---|---|
| 1 | btn_menubar 클릭 → btnArea 확장 | PASS | 실제 마우스 클릭 |
| 2 | 실제 마우스 Hover 1초 후 Tooltip | PASS | 0.5초 숨김 / 1.3초 표시 |
| 3 | Tooltip 위치가 버튼 바로 위 | PASS | 툴팁 아래변 48.0 / 버튼 윗변 44.4, x중심차 0.00px |
| 4 | 버튼별 문구 정상 출력 | PASS | 4개 버튼 모두 다른 문구 |
| 5 | 1초 이전 Exit 시 미출력 | PASS | 0.4초 후 이탈 → 표시 안 됨 |
| 6 | 출력 후 Exit 시 즉시 제거 | PASS | |
| 7 | 빠른 이동 시 중복 없음 | PASS | 인스턴스 1개 / 표시 1개 |
| 8 | 조작 없으면 5초 후 자동 접힘 | PASS | 이탈 3초 열림 → 5.7초 접힘 |
| 9 | Hover 중에는 접히지 않음 | PASS | **7초 Hover 유지 후에도 열림** |
| 10 | 접힘 후 btn_menubar 즉시 복원 | PASS | |
| 11 | 접힐 때 Tooltip/예약 제거 | PASS | 접히기 전 표시 True → 접힌 뒤 False |
| 12 | collapseOnFieldMove ON → 즉시 접힘 | PASS | 마우스를 메뉴 위에 둔 상태에서도 접힘 |
| 13 | OFF → 유지, 이후 5초 규칙 | PASS | 이동 직후 유지, 5.6초 뒤 접힘 |
| 14 | 기존 버튼 기능 유지 | PASS | 실제 클릭 → 4개 패널 모두 열림 |
| 15 | 신규 Error/Exception 없음 | PASS | 0건 |

핵심 증거 한 줄:

```text
자동으로 붙은 HoverTooltipTrigger 수 = 5   (씬에 저장된 것은 0개였다)
PointerEnter 도달: 최상단 히트=sp_deco, 상위 HoverTooltipTrigger=btn_change, 총 3개 히트
```

## 8. 검증 환경에서만 필요했던 조치

batchmode에는 창 포커스가 없어 `EventSystem.isFocused`가 false가 되고,
`StandaloneInputModule.Process()`가 통째로 조기 반환한다(입력이 아예 처리되지 않는다).
하네스에서 포커스만 되돌려 준 뒤 진행했다. **제품 코드의 문제가 아니며 수정하지 않았다** - 실제
빌드/에디터에서는 창에 포커스가 있으므로 발생하지 않는다.

`-nographics`에서도 UI 레이캐스트가 동작하지 않아 그래픽을 켠 batchmode로 돌렸다.

## 9. 확인하지 못한 것

- **물리 마우스로 커서를 실제로 움직인 경우.** 좌표의 출처만 합성이고 그 뒤 경로는 전부 실제
  코드지만, OS 커서 이동 자체는 재현하지 않았다.
- **Windows 빌드.** macOS에서 확인했으므로 `WindowInputRegion`의 네이티브 클릭 관통 경로는
  검증되지 않았다. 툴팁은 입력을 받지 않고 렌더링만 하므로 이 층과 무관하다.

## 10. 범위 밖 참고

- `btn_ReturnTown`은 마을 상태에서 `FieldModeUIController`가 꺼 두므로 btnArea에는 항상 4개가
  보인다. 툴팁은 이 비활성 전환을 `OnDisable`로 처리하므로 남지 않는다.
- 옛 `Canvas/ControlDock/btnArea`에 같은 이름의 버튼 5개가 비활성으로 남아 있다. 정리 여부는
  별도 판단이 필요하다.
- 1차에서 보고한 영어 Key 30 오타(`ReturnTwon`)는 이번에 수정된 것을 확인했다.

---

# 1차: 최초 구현 (참고)

## 구현 구조

| 파일 | 붙일 위치 | 역할 |
|---|---|---|
| `MenuBarExpander.cs` | `tgl_Panel` | btn_menubar ↔ btnArea 활성 상태 전환 |
| `HoverTooltipController.cs` | `tgl_Panel` | 툴팁 인스턴스 1개 소유, 대기시간/표시/위치 |
| `HoverTooltipTrigger.cs` | 메뉴 버튼 | Hover 진입/이탈 알림 + 문구 참조 |
| `LocalizedTMPText.cs` | (수정) | `TextReference` 읽기 전용 프로퍼티 추가 |

`ModalPanel`, `ModalPanelOpener`, `PopupPanelManager`, `FieldModeUIController`, 각 패널 UI 로직은
**한 줄도 바꾸지 않았다.**

## 메뉴 확장

두 오브젝트가 이미 같은 화면 위치에 있어 좌표를 만질 필요가 없었다.

```text
btn_menubar   앵커/피벗 (1,0)   anchoredPosition (-120, 30)
panel         앵커/피벗 (1,0)   anchoredPosition (-120, 30)   ← btnArea의 부모
```

상태를 바꾸는 경로가 `SetExpanded(bool)` 하나뿐이라 둘 다 켜지거나 둘 다 꺼진 중간 상태가 없다.

## 툴팁 위치

버튼 `RectTransform`의 월드 코너에서 위쪽 변 중앙을 구하고, 툴팁 피벗을 아래-가운데(0.5, 0)로
고정해 그 지점에 붙인다. 여백만 부모 로컬 단위(= Canvas 기준 해상도 픽셀)로 더한다.

```csharp
target.GetWorldCorners(targetCorners);
Vector3 topCenter = (targetCorners[1] + targetCorners[2]) * 0.5f;
tooltipRect.position = topCenter;
tooltipRect.anchoredPosition += new Vector2(0f, verticalOffset);
```

## 문구는 하드코딩하지 않는다

메뉴 버튼 5개는 이미 각자의 라벨(`lb_*`)에 `LocalizedTMPText`와 Table/Key를 들고 있다.
라벨은 꺼져 있지만 참조 자체는 유효하다.

```text
btn_change      → 01_UI / Key 3   "캐릭터 교체"
btn_inventory   → 01_UI / Key 5   "가방"
btn_Recovery    → 01_UI / Key 12  "회복소"
btn_Dungeon     → 01_UI / Key 20  "던전"
btn_ReturnTown  → 01_UI / Key 30  "마을로"
```

`HoverTooltipTrigger`는 Inspector의 Tooltip Text를 비워 두면 이 참조를 그대로 재사용한다.

## 로딩 직후 Hover 시 문구가 비는 문제

로컬라이징 로드는 비동기라 게임 시작 직후에는 빈 문자열이다. 문구를 **대기시간이 끝난 뒤에**
읽고, 그래도 비어 있으면 그때 한 번만 동기로 받아온다(`GetLocalizedString()`).

## Raycast

프리팹을 수정하는 대신, 인스턴스를 만들 때 컨트롤러가 안쪽 모든 `Graphic`의 `raycastTarget`을 끈다.

## 1차 작업에서 발견해 제거한 것

`btn_menubar`에 `btn_change`에서 복사되며 따라온 `ModalPanelOpener`(→ `pn_CharacterSwap`)가 남아
있었다. 제거되었음을 2차에서 확인했다.
