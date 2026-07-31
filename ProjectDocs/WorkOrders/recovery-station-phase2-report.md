# 회복소 MVP 2단계 (UI·패널·캐릭터 상태·드래그&드롭) 구현 완료 보고서

작업일: 2026-07-31
범위: 회복소 패널 제어 / 슬롯 3개 View / 하단·슬롯 버튼 / 캐릭터 교체 목록 상태·판정 분리 /
드래그&드롭. 1단계(도메인·저장·재화·시간) 위에 얹었다.
전제: 씬(`desktopScene.unity`), 프리팹, 스프라이트, 로컬라이징 에셋, UI 레이아웃은 **한 줄도 변경하지
않았다** — `diff -r`로 `Assets/Scenes`, `Assets/Art`, `Assets/Localization`이 무변경임을 확인했다(6절).

> **씬 연결은 아직 없다.** 이번 단계 산출물은 "에디터에서 연결하면 동작하는 컴포넌트"까지다.
> 실제로 화면에서 쓰려면 **6절 체크리스트**를 사용자가 Editor에서 수행해야 한다. 연결 전에도 기존
> 게임은 회복소가 없던 때와 동일하게 동작한다.
>
> ⚠ **상태 문구 end-to-end 검증은 미완료다.** 명세가 요구하는 최종 문구는 Recovering = `회복 중`,
> RecoveryComplete = `합류 대기`인데, `01_UI` String Table의 key 10/11에는 아직 `회복중 {0}` /
> `회복완료`가 들어 있다. 이번 단계에서 로컬라이징 자산을 직접 수정하지 않기로 했으므로
> (Coordinator 결정), **자산이 6-4절 19번대로 수정되기 전까지 화면에 명세 문구가 그대로 나오는지는
> 확인되지 않은 상태다.** 코드 쪽 경로(상태 → 참조 → 표시)와 fallback 문구가 명세 값인지는 자동
> 검증했다(P18).

---

## 1. 변경 파일

### 신규 (스크립트 6개, 전부 `Assets/Scripts/Recovery/UI/`)

| 파일 | 붙는 위치 | 책임 |
| --- | --- | --- |
| `RecoveryStationPanel.cs` | `pn_RecoveryStation` 루트 | `ModalPanel` 파생. 슬롯 갱신, 하단 버튼, 드롭 중계, 닫을 때 Pending 정리 |
| `RecoveryStationSlotView.cs` | `list_RecoverySlot_1/2/3` 루트 | 슬롯 한 칸 표시 + `IDropHandler` |
| `CharacterRecoveryDragSource.cs` | `list_Character` 프리팹 | 드래그 시작 판정, ScrollRect 전달, 고스트 수명 |
| `RecoveryDragGhost.cs` | (런타임 객체) | 고스트 생성/이동/정리. MonoBehaviour 아님 |
| `RecoveryStationOpener.cs` | `btn_RecoveryStation` | 회복소 + (닫혀 있으면) 교체 패널 복합 열기 |
| `RecoveryTimeFormat.cs` | — | 남은 시간 문자열 서식 단일 지점 |

각 `.cs`의 `.meta`와 폴더 메타 `Assets/Scripts/Recovery/UI.meta`도 함께 추가했다(총 7개).
프로젝트 전체 8705개 메타의 GUID가 모두 유일함을 확인했다.

### 수정 (3개)

| 파일 | 변경 |
| --- | --- |
| `Assets/Scripts/Common/CharacterSwap/CharacterSwapListItem.cs` | `DisplayState`에 `Recovering`/`RecoveryComplete` 추가, 상태별 Localized 참조 2개 추가, `Refresh`가 **교체 가능/드래그 가능을 따로** 받도록 변경, 드래그 직후 클릭 억제, 상태 텍스트 이름 폴백(`lb_status`, **이 항목 내부에서만 탐색**), 고스트 복제 원본 노출 |
| `Assets/Scripts/Common/CharacterSwap/CharacterSwapPanel.cs` | 표시 상태 판정을 `ResolveDisplayState`로 분리(1단계의 임시 Exhausted 매핑 대체), 교체/드래그 판정 분리 계산, `RecoveryService.SlotsChanged` 구독, `RequestRefresh()` 정적 진입점 |
| `Assets/Scripts/Common/CharacterSwap/CharacterSwapListItem.cs` (계속) | 상태별 **fallback 문구** 5개를 serialized 필드로 추가(기본값이 명세 값). 참조가 비어 있을 때만 쓰이며 매핑은 `GetStateFallbackText` 한 곳뿐 |

**1단계 도메인 코드는 건드리지 않았다.** (검토 중 추가했던 `RecoveryStation.TryGetSlotForCharacter`는
상태 문구에서 남은 시간을 빼기로 결정하면서 다시 제거했다 — 2.3절.)

---

## 2. 컴포넌트 책임과 UI 상태 전이

### 2.1 책임 분담

```
btn_RecoveryStation ─ RecoveryStationOpener ─┬─> pn_CharacterSwap (닫혀 있을 때만 Open)
                                             └─> pn_RecoveryStation (항상 Open, 나중에 열어 맨 앞)

pn_RecoveryStation ─ RecoveryStationPanel (ModalPanel 파생)
   ├ 슬롯 3개  ─ RecoveryStationSlotView (IDropHandler)
   └ 하단 버튼 ─ btn_StartRecovery / btn_cancel / btn_JoinParty

pn_CharacterSwap ─ CharacterSwapPanel
   └ 항목      ─ CharacterSwapListItem + CharacterRecoveryDragSource
                                          └─ RecoveryDragGhost (드래그 중에만 존재)
```

**규칙은 전부 1단계 도메인이 소유한다.** UI 컴포넌트는 판정을 복사하지 않고
`RecoveryStation`/`CharacterRoster`에 물어본 결과만 그린다. 판정을 UI에 복제하면 도메인과 화면이
갈라지기 때문이다.

### 2.2 슬롯 상태 전이표

`list_RecoverySlot_N` 안의 오브젝트가 상태마다 어떻게 되는지:

| 상태 | `list_EmptySlot_Recovery` | `list_RecoveryCharacter` | `lb_Timer` | `lb_time` 내용 | 슬롯 내 `btn_JoinParty` |
| --- | --- | --- | --- | --- | --- |
| Empty | **활성** | 비활성 | — | — | — |
| Pending | 비활성 | **활성** | **활성** | 시작하면 걸릴 **총 시간**(흐르지 않음) | 비활성 |
| Recovering | 비활성 | **활성** | **활성** | **남은 시간**(흐름) | 비활성 |
| RecoveryComplete | 비활성 | **활성** | **비활성** | — | **활성** |

**`lb_Timer`와 `lb_time`의 관계(중요):** 실제 프리팹에서 `lb_time`은 `lb_Timer`의 **자식**이다
(`Status/lb_Timer/lb_time`). 따라서 `lb_Timer`를 끄면 시간 값도 함께 사라진다. 그래서 완료 상태에서만
`lb_Timer`를 끄고, 그 자리에 `btn_JoinParty`를 켜는 방식으로 고정했다.

**예상 시간과 실제 타이머의 구분:** 둘 다 `lb_time`에 표시되지만 성격이 다르다.
- Pending은 아직 재화를 내지 않아 **시작 시각 자체가 없다**. 그래서 패널의 주기 갱신 대상에서
  제외되고 값이 흐르지 않는다(`RecoveryStationPanel.HasRecoveringSlot()`이 Recovering만 센다).
- Recovering만 주기 갱신 대상이며 `RecoverySlotView.Remaining`을 다시 읽어 값이 줄어든다.

### 2.3 상태 문구와 남은 시간의 책임 분리

**상태 문구에는 상태만 담는다.** 남은 시간은 회복소 슬롯의 `lb_time`이 **유일하게** 담당한다.
초기 구현에서는 교체 목록의 `회복중 {0}`에 남은 시간을 채워 넣었는데, 그러면 같은 값을 두 곳에서
서로 다른 주기로 갱신하게 되고 목록에도 초 단위 갱신이 필요해진다. Coordinator 결정에 따라 그
경로를 전부 제거했다(`Refresh`의 시간 인자, 목록의 주기 갱신 `Update`,
`RecoveryStation.TryGetSlotForCharacter`, `RecoveryTimeFormat`의 TimeSpan 오버로드).

따라서 **key 10의 `{0}`은 제거되어야 한다**(6-4절 19번).

### 2.4 갱신 주기 / 할당 억제

- 기본 갱신은 **이벤트 기반**이다: `RecoveryService.SlotsChanged`, `RecoveryCompleted`,
  `CharacterRoster.CharacterStateChanged`, 그리고 패널이 열릴 때의 `RefreshContents`.
- 남은 시간만 `RecoveryStationPanel.Update`가 **0.25초 주기**(Inspector 조절 가능)로 갱신한다.
  회복 중인 슬롯이 하나도 없으면 타이머 자체를 돌리지 않는다.
- 슬롯은 **표시 초가 바뀔 때만** 문자열을 다시 만든다(`lastDisplayedSeconds` 캐시). 초상화/이름도
  캐릭터가 바뀔 때만 다시 설정한다.
- 교체 목록에는 주기 갱신이 **없다**. 상태 문구는 상태가 바뀔 때(이벤트)만 바뀌면 충분하다.

### 2.5 0 경계에서 상태/시간이 어긋나지 않는 근거

`RecoverySlotView`(1단계 구조체)는 **상태와 남은 시간을 같은 스냅샷**으로 돌려준다.
`RefreshRemainingTime()`은 매번 그 스냅샷을 다시 읽고, 상태가 직전 표시와 다르면 시간만 고치지 않고
**전체 갱신으로 넘어간다**. 그래서 "Recovering인데 00:00", "완료됐는데 타이머가 남아 있음" 같은
어긋난 화면이 만들어지지 않는다. 남은 시간은 **올림 초**로 표시하므로 0.4초 남았을 때 `00:00`이
보이지 않는다.

---

## 3. 요구사항별 구현

### A. 패널 제어

기존 규칙을 그대로 재사용했다 — **중복 팝업 시스템을 만들지 않았다.**

| 항목 | 처리 |
| --- | --- |
| 열기/닫기/닫기 버튼 | `ModalPanel`(`closeButton`, `Open`/`Close`) |
| ESC / 포커스 순서 | `PopupPanelManager` (ModalPanel의 OnEnable/OnDisable에서 자동 등록/해제) |
| Windows 클릭 관통 | 프리팹 루트에 이미 있는 `WindowInputRegion` |
| 패널 이동 | 프리팹 루트에 이미 있는 `PanelDragHandle` (7절 5번 참고) |
| 독립 이동/포커스/닫기 | 두 패널이 서로를 닫지 않는다. `RecoveryStationOpener`만 함께 **연다** |

**닫을 때 Pending 정리:** `OnModalClosed`에서 `station.ClearPending()`만 부른다.
Recovering/RecoveryComplete는 건드리지 않는다(1단계 `ClearPending`의 계약).
`ModalPanel.OnDisable`이 유일한 통로이므로 **닫기 버튼 / ESC / 코드 Close / SetActive(false)** 가
전부 같은 정리 지점을 지난다 — P17에서 네 경로를 모두 확인했다.

**재진입 안전:** `OnModalClosed`는 **먼저 이벤트 구독을 끊고** 그 다음에 `ClearPending()`을 부른다.
그래서 `ClearPending`이 발생시키는 `SlotsChanged`가 닫히는 중인 패널로 되돌아오지 않는다.
추가로 `closing` 플래그가 `RefreshContents`/`Update`를 막는다. 버튼은 `RemoveListener` 후
`AddListener`라 여러 번 열고 닫아도 리스너가 쌓이지 않는다.

### B. 복합 열기

```csharp
// RecoveryStationOpener.OpenPanels()
if (characterSwapPanel != null && !characterSwapPanel.gameObject.activeSelf) characterSwapPanel.Open();
if (recoveryPanel != null) recoveryPanel.Open();
```

- **이미 열린 교체 패널은 `Open()`조차 부르지 않는다.** `ModalPanel.Open()`은 이미 열린 패널을 맨
  앞으로 올리고 `RefreshContents()`로 선택을 초기화하므로, 부르면 사용자의 선택 상태와 포커스가
  강제로 바뀐다. 위치는 `PanelDragHandle`이 소유하므로 어차피 유지되지만, 포커스/선택까지 지키려면
  호출 자체를 하지 않아야 한다.
- **열기 순서가 교체 → 회복소인 이유**: 마지막에 연 패널이 활성 패널이 되므로 회복소가 앞에 온다.
- `btn_change`의 기존 `ModalPanelOpener`는 손대지 않았다 → 교체 버튼은 지금까지대로 교체 패널만 연다.
- 재화 부족으로 회복소를 닫을 때 교체 패널은 그대로 둔다(P20에서 확인).

### C. 슬롯 View

`RecoveryStationSlotView`가 슬롯 하나를 담당하고, **슬롯 번호는 Inspector의 `Slot Index`로 명시**한다.
계층 순서나 이름 파싱으로 추정하지 않는다 — 슬롯마다 `list_RecoveryCharacter`, `lb_time`,
`btn_JoinParty` 같은 **같은 이름의 자식이 반복**되므로 자동 탐색은 서로의 참조를 물어올 수 있다.
패널이 시작할 때 `Slot Index` 중복을 검사해 오류로 드러낸다.

표시 필드는 실제 계층 그대로 연결한다(초상화 `sp_portrait`, 이름 `lb_name`, 레벨 `lb_level`,
행동력 `lb_percent` + `ProgressBarView`).

### D. 버튼

| 버튼 | 활성 조건 | 동작 |
| --- | --- | --- |
| `btn_StartRecovery` (시작) | `PendingCount >= 1` | `StartRecovery()` |
| `btn_cancel` (취소) | `PendingCount >= 1` | `ClearPending()` — 진행/완료 슬롯 무영향 |
| `btn_JoinParty` (하단, 전체 합류) | 완료 슬롯 >= 1 | `JoinAllCompleted()` — Recovering 유지 |
| 슬롯 내 `btn_JoinParty` | 그 슬롯이 RecoveryComplete | `TryJoin(slotIndex)` — 한 명만 |

`StartRecovery()` 결과 처리:

| 코드 | 처리 |
| --- | --- |
| `Success` | 슬롯 재그리기 + 교체 목록 갱신 |
| `InsufficientFunds` | **정상 `Close()` 경로로 회복소만 닫는다** → `OnModalClosed`가 Pending 전부 삭제. 교체 패널 유지. 재화/캐릭터/슬롯은 1단계가 이미 무변경을 보장 |
| 그 외(`InvalidCharacterState` 등) | 부분 성공 없음 — 상태 그대로 두고 화면만 최신화. **스펙 밖 팝업을 만들지 않고** 로그만 남긴다 |

### E. 교체 목록 상태/판정 분리

**두 규칙을 하나의 `interactable`로 묶지 않는다.** `CharacterSwapListItem.Refresh`가 `canSwap`과
`canDragToRecovery`를 **별도 인자**로 받아, 전자는 `Button.interactable`에, 후자는
`CharacterRecoveryDragSource.enabled`에 적용한다.

| 상태 | 교체(`GetSwapBlockReason`) | 드래그(`CanRegister`) |
| --- | --- | --- |
| Active(전투 중) | 불가 | **불가** |
| Available (최대치) | 가능 | **불가**(회복할 것이 없음) |
| Available (일부 소모) | 가능 | **가능** |
| Exhausted (0) | 불가 | **가능** |
| Recovering | 불가 | 불가 |
| RecoveryComplete | 불가 | 불가 |

**상태 문구는 상태만 담는다** — 남은 시간은 회복소 슬롯 `lb_time`의 책임이며 문구에 섞지 않는다(2.3절).

표시 상태는 `CharacterSwapPanel.ResolveDisplayState`가 **회복 상태를 먼저** 보고 정한다 —
1단계에서 `InRecovery`를 임시로 `Exhausted`로 매핑하던 것을 이 판정이 대체한다.
`DisplayState`에 `Recovering`/`RecoveryComplete` 두 값만 추가했고(최소 확장),
프리팹 리소스/스프라이트는 바꾸지 않았다(회복 상태는 전용 배경색 하나로 구분).

**문구 참조가 비어 있을 때(fallback):** 기존 컴포넌트 관례대로 **오류 로그를 한 번 남긴다**. 다만
예전의 하드코딩된 `<Missing Localization>` 대신 **Inspector에서 바꿀 수 있는 상태별 fallback 문구**를
두고 기본값을 명세 값(`회복 중` / `합류 대기` 등)으로 설정했다. 상태 → 문구 매핑은
`GetStateFallbackText` 한 곳뿐이라 같은 문자열이 코드 여러 곳에 흩어지지 않는다. 문구의 원천은
어디까지나 String Table이며, 이 값은 참조를 연결하기 전까지만 쓰이는 임시값이다.

**상태 텍스트 자동 탐색 범위:** `lb_state` → `lb_status` 순으로 찾되, **그 리스트 항목 자신의 자식
안에서만** 탐색한다(`FindChildComponent`가 `transform`에서 시작). 같은 이름을 다른 영역에서 전역
탐색하지 않는다.

**회복소가 없는 씬에서도 안전하다:** `RecoveryService.Station`이 null이면 `canDrag = false`,
표시 상태는 기존 3종만 나오며 예외가 없다(P03에서 확인).

### F. 드래그 & 드롭

**드래그 제스처 판정은 3-상태 기계다** (`CharacterRecoveryDragSource`)

```
OnBeginDrag
  좌클릭 아님 / 등록 불가 / 세로 우세          -> Scroll    (ScrollRect에 Begin 전달)
  가로 우세 + 가로 이동 >= 임계값(12px)         -> Recovery  (고스트 생성)
  가로 우세 + 가로 이동 <  임계값               -> Undecided (아무 것도 전달하지 않고 보류)

OnDrag (Undecided인 동안에만 재평가)
  가로 우세 + 가로 이동 >= 임계값               -> Recovery  (고스트 생성)
  세로 우세로 바뀜                              -> Scroll    (여기서 Begin을 전달한 뒤 Drag 전달)
  그 외                                         -> Undecided 유지
```

`Undecided`가 필요한 이유: Begin 한 번만 보고 확정하면 **천천히 가로로 끄는 제스처**가 임계값에 닿기
전에 스크롤로 굳어져 회복 드래그가 영영 시작되지 않는다(EventSystem의 기본 드래그 임계값을 넘긴 첫
프레임의 이동량이 12px보다 작을 수 있다). 판정을 보류했다가 누적 이동량으로 다시 본다.

**한 번 Recovery나 Scroll로 정해지면 그 드래그가 끝날 때까지 바꾸지 않는다.** 중간에 갈아타면
ScrollRect에 Begin 없이 Drag가 가거나 End가 두 번 가는 상태가 만들어진다.

**등록 가능 여부는 드래그를 시작하려는 시점에 다시 확인**한다(목록을 그린 뒤 상태가 바뀌었을 수 있다).

**ScrollRect 보존:** Unity는 드래그 대상을 "누른 오브젝트에서 위로 올라가며 만나는 첫 `IDragHandler`"로
정한다. 드래그 컴포넌트가 리스트 항목(ScrollRect보다 아래)에 있으므로, 아무 처리도 하지 않으면
**세로 스크롤이 죽는다**. 그래서 기존 `ScrollRectDragSettings`와 **같은 전달 방식**을 쓴다.

**Begin/Drag/End 균형:** ScrollRect로 전달한 Begin에는 반드시 End가 정확히 한 번 대응한다.
정상 종료뿐 아니라 **취소 경로(리스트 재생성 / 패널 닫기 / 컴포넌트 비활성 / 드롭 성공으로 인한
비활성화)** 에서도 `OnEndDrag`를 한 번 전달한다 — 전달에 쓸 `PointerEventData`를 보관해 두고 쓰며,
이벤트가 없거나 ScrollRect가 없으면 호출하지 않는다(예외 방지). 취소가 End를 보낸 뒤 정상 `OnEndDrag`가
와도 중복으로 보내지 않는다. **회복 드래그는 Begin을 전달한 적이 없으므로 취소해도 End를 보내지 않는다.**

**관성을 임의로 죽이지 않는다.** 예전 구현은 취소할 때 항상 `StopMovement()`를 불렀는데, 그러면
사용자가 스크롤을 튕겨 둔 상태에서 패널이 닫힐 때 스크롤이 부자연스럽게 멈춘다. 지금은 End를 정상적으로
넘기고 ScrollRect가 자기 규칙대로 관성을 처리하게 둔다.

**클릭 vs 드래그:** 클릭에는 `OnBeginDrag` 자체가 오지 않으므로 기존 선택 동작이 그대로다. 회복 드래그가
시작되면 그 제스처의 클릭만 삼킨다. 억제는 **드래그가 끝난 프레임까지만** 유효하다
(`ShouldSuppressClick`) — EventSystem은 같은 프레임 안에서 `OnEndDrag`보다 **먼저** 클릭을 처리하므로
즉시 지우면 이번 제스처의 클릭이 통과하고, 반대로 "소비할 때까지 유지"하면 제스처의 클릭이 발생하지
않았을 때 표시가 남아 **다음 사용자 클릭**을 잡아먹는다. 프레임 번호로 만료시켜 두 문제를 모두 없앴다.
취소 경로와 `OnDisable`/`OnDestroy`에서도 같은 만료를 적용한다.

**고스트**
- 원본은 **움직이지 않는다**(Layout Group이 리스트를 다시 배치해 화면이 튀는 것을 막는다).
- 최상위 Canvas 아래 별도 오브젝트. 원본의 초상화(`mask_portrait`, 마스크째)와 이름(`lb_name`)을
  **복제**해서 쓴다 — 새 스프라이트/디자인 없음.
- `CanvasGroup.blocksRaycasts = false`, `interactable = false`, 복제본의 **모든 `Graphic.raycastTarget = false`**,
  복제본의 `Selectable` 비활성 → 드롭 대상을 가리지 않는다.
- Canvas 기준으로 스케일을 맞춰 원본과 같은 크기로 보인다.
- 드래그 종료/`OnDisable`/`OnDestroy` 어느 경로로 끝나도 정리된다. `Dispose()`는 **먼저 비활성화한 뒤**
  `Destroy`하므로 한 프레임도 남지 않는다.

**드롭** — `RecoveryStationSlotView.OnDrop`
- `eventData.pointerDrag`에서 드래그 원본을 찾고 `IsDraggingToRecovery`가 true일 때만 처리한다
  (스크롤로 넘긴 드래그가 슬롯 위에서 끝나도 등록되지 않는다).
- 수락 판정은 전적으로 `TryAddPendingToSlot`이 한다 — 차 있는 슬롯/회복 중/완료/이미 대기 중인
  캐릭터는 그쪽에서 거부되고 **아무 상태도 바뀌지 않는다**.
- 성공해도 **재화 차감·타이머 시작 없음**. 성공 즉시 교체 목록을 다시 그려 같은 캐릭터를 한 번 더
  끌어오지 못하게 한다.
- **재진입 경로:** 드롭이 성공하면 그 캐릭터는 더 이상 드래그 대상이 아니므로 목록 갱신이
  `CharacterRecoveryDragSource.enabled = false`를 만든다. 비활성 컴포넌트에는 `OnEndDrag`가 오지
  않으므로, `OnDisable → CancelDrag`가 고스트 파괴·플래그 해제·클릭 억제 만료를 대신 처리한다
  (P24에서 확인). 실패 드롭에서는 아무 상태도 바뀌지 않아 드래그가 그대로 이어진다.

**범위 밖:** Windows 투명창에서 패널 사이 공백을 지날 때 드래그가 끊기는 문제는 이번 범위가 아니며,
8절에 수동 확인 항목으로만 남겼다.

---

## 4. 자동 검증

Unity Editor가 프로젝트 락을 잡고 있으므로 APFS 클론에서 batchmode로 실행했다. 검증 하네스는
클론에만 두고 저장소에는 넣지 않았다.

### 4.1 Edit Mode (1단계 도메인 회귀)

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath <clone> \
  -executeMethod RecoveryVerify.RecoveryVerification.Run -logFile <clone>/final-edit.log
```

| 항목 | 결과 |
| --- | --- |
| 종료 코드 | **0** |
| `error CS` | **0** |
| 결과 | **TOTAL: 237, PASSED: 237, FAILED: 0** (1단계와 동일 — 회귀 없음) |

### 4.2 Play Mode (실제 씬 + 실제 프리팹 + 2단계 UI)

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath <clone> \
  -executeMethod RecoveryVerify.RecoveryPlayModeVerification.Setup -logFile <clone>/pm-p2c.log
```

| 항목 | 결과 |
| --- | --- |
| 종료 코드 | **0** |
| 결과 | **TOTAL: 270, PASSED: 270, FAILED: 0** (1단계 90 + 2단계 180) |
| 예기치 않은 예외/Assert | **0건** |

씬에는 아직 회복소 UI가 없으므로, 하네스가 **실제 프리팹**(`pn_RecoveryStation`,
`list_RecoverySlot_1`, `pn_CharacterSwap`, `list_Character`)을 런타임에 인스턴스화하고 7절
체크리스트대로 컴포넌트를 붙여 **"연결이 끝난 상태"를 재현**한 뒤 검증했다.

| 구역 | 검증 내용 | 대응 요구 |
| --- | --- | --- |
| P13 | 회복 버튼 → 두 패널 / swap 버튼 → swap만 / 이미 열린 교체 패널 위치·상태 유지 / 독립 닫기 | 검증 1, 10 |
| P14 | Empty→Pending 전이, `lb_Timer` 활성, 예상 총 시간 표시, 내부 합류 버튼 비활성, 재화 차감 0, 저장 슬롯 미기록 | 검증 3 |
| P15 | 드롭 거부 규칙 5종(이미 Pending 슬롯 / 동일 캐릭터 / Active / 최대치 / Exhausted는 수락) | 검증 2, 3 |
| P16 | 시작 → Recovering 표시, 완료 → `lb_Timer` 비활성 + 합류 버튼 활성 + 슬롯 계속 점유 + 자동 합류 없음 | 검증 5, 6 |
| P17 | 버튼 활성 규칙, 취소, **닫기 4경로(Close/ESC/SetActive/버튼) 모두 Pending 삭제 + 진행·완료 유지** | 검증 4 |
| P18 | Recovering/RecoveryComplete 선택·교체·드래그 불가, Active 드래그 불가, 최대치=교체O·드래그X, 소진=교체X·드래그O, DisplayState 매핑 4종 | 검증 2, 8 |
| P19 | 세로 제스처→스크롤, 임계값 미만→스크롤, 가로→고스트 생성, 고스트 raycast off 전부, 드롭 수락, 실패 드롭 무변화, 종료·비활성 시 고스트 정리, ScrollRect 생존 | 검증 9 |
| P20 | 재화 부족 → 회복소만 닫힘 / 교체 패널 유지 / 재화·슬롯·행동력 무변화 / 대기 전부 삭제 | 검증 5 |
| P21 | 슬롯별 합류 / 전체 합류 시 Recovering 유지 / 버튼 활성 전이 | 검증 7 |
| P22 | **드래그 상태 기계** — Begin 6px→보류(전달 0), Drag 13px→회복 드래그+고스트, 세로 제스처 Begin/Drag/End = 1/2/1, 보류→스크롤 확정 시 Begin 1, 보류로 끝난 제스처 전달 0, 전달 중 비활성 시 End 정확히 1, 취소 후 정상 End가 와도 중복 없음, 회복 드래그 취소에는 End 0 | 보정 F1·F3 |
| P23 | **클릭 억제 수명** — 드래그 중 억제, 종료 프레임까지 억제, 만료 프레임이 `int.MaxValue`가 아님, 만료 후 다음 클릭 통과, 취소 경로도 만료 | 보정 F2 |
| P24 | **드롭 재진입** — 성공 드롭으로 source가 disable돼도 고스트·플래그·억제 정리, ScrollRect End 0 / 실패 드롭은 상태 무변화·드래그 유지·ScrollRect 콜백 0 | 보정 추가검토 |
| P01~P12 | 1단계 회귀(기존 공격·보상·행동력·교체·저장·시작 캐릭터 선택) | 검증 11 |

### 4.3 무결성 확인

```bash
diff -r Assets/Scripts <clone>/Assets/Scripts        # 완전 동일(메타 포함)
diff -r Assets/Scenes  <clone>/Assets/Scenes         # 무변경
diff -r Assets/Art     <clone>/Assets/Art            # 무변경 (프리팹/스프라이트)
diff -r Assets/Localization <clone>/Assets/Localization  # 무변경
grep -rh "^guid: " --include="*.meta" Assets/ | sort | uniq -d   # 중복 0 (8705/8705)
```

사용자의 실제 저장 파일(`~/Library/Application Support/Rell/desktop_RPG/playerprogress.json`)은
검증 전 백업하고 **바이트 단위로 동일하게 복원**했다.

---

## 5. 자동 검증이 닿지 않는 항목 (실제 Editor / Windows 수동 확인 필요)

자동 검증은 "컴포넌트를 체크리스트대로 연결했을 때의 동작"을 확인한다. 아래는 실제 씬 연결과
사람 눈/손이 필요한 항목이다.

| # | 확인할 것 | 왜 자동으로 못 하나 |
| --- | --- | --- |
| 1 | 실제 씬에서 `btn_RecoveryStation`을 눌렀을 때 두 패널이 뜨고 겹침/위치가 자연스러운지 | 레이아웃·시각 판단 |
| 2 | 슬롯 3개가 `content` 안에서 세로로 정렬되어 보이는지(VerticalLayoutGroup + ContentSizeFitter) | 시각 판단 |
| 3 | 마우스로 실제 끌었을 때 고스트가 손끝을 따라오는 느낌과 임계값(12px)이 적절한지 | 조작감 |
| 4 | 리스트를 **세로로 스크롤**하면서 항목을 가로로 끌어내는 제스처가 자연스럽게 갈리는지, 12px 임계값이 적절한지 | 실제 입력 장치 필요 |
| 4b | 회복 드래그 직후 **다음 클릭**으로 항목이 정상 선택되는지(프레임 경계 동작) | 프레임 경계는 하네스가 한 프레임 안에서 돌아 직접 재현 불가 — 만료 프레임 값으로 대신 검증했다 |
| 5 | `lb_time`/`lb_Timer` 문구가 잘리거나 겹치지 않는지(ContentSizeFitter) | 시각 판단 |
| 6 | **Windows 빌드**에서 패널이 클릭되는지(`WindowInputRegion`), 패널 사이 공백을 지날 때 드래그가 끊기는지 | macOS에서 Win32 후크/투명창 검증 불가 |
| 7 | 30초 실시간 대기 후 실제로 한 칸 오르고 타이머가 줄어드는지 | 하네스는 저장 시각을 조작해 완료를 만든다 |
| 8 | 상태 문구가 실제로 "회복중 …"/"회복완료"로 보이는지 | 프리팹의 Localized 참조가 비어 있어(7절 8번) 연결 후에만 확인 가능 |
| 9 | ESC 연타·패널 여러 개 동시 조작 시 포커스 순서 | 사람 조작 패턴 |

---

## 6. Inspector / Editor 연결 체크리스트

**순서대로** 수행한다. 컴포넌트와 serialized field 단위로 적었다.
**같은 이름의 자식이 슬롯마다 반복되므로 반드시 슬롯별로 직접 연결한다** — 자동 탐색에 맡기지 않는다.

### 6-1. 1단계 잔여 작업 (아직 안 했다면 먼저)

| # | 작업 |
| --- | --- |
| 1 | `Create > Recovery > Recovery Balance Table` → `Assets/Data/Recovery/RecoveryBalanceTable.asset` (기본값 `default / Jewel / 100 / 30 / 3` 그대로) |
| 2 | 씬 관리자 오브젝트에 `Recovery Service` 컴포넌트 추가 → `Balance Table` = 1번 에셋 |
| 3 | `btn_switching`의 **`Stamina Refill Test Button` 컴포넌트 제거** → 그 다음 오브젝트 제거 여부 결정 → 마지막에 `Assets/Scripts/Common/StaminaRefillTestButton.cs` 파일 삭제 (**컴포넌트를 먼저 떼야 Missing Script가 남지 않는다**). 이 버튼은 1단계에서 이미 무동작이라 새 opener와 충돌하지 않는다 |
| 4 | `CharacterRoster`의 `Override Stamina On Start`가 꺼져 있는지 확인 |

### 6-2. 슬롯 프리팹 (`list_RecoverySlot_1`)

| # | 작업 |
| --- | --- |
| 5 | `list_RecoverySlot_1` 프리팹 루트에 **`Recovery Station Slot View`** 컴포넌트 추가 |
| 6 | 같은 프리팹의 `list_RecoveryCharacter`와 `list_EmptySlot_Recovery` 루트에 남아 있는 **`Character Swap List Item` 컴포넌트를 제거**한다(교체 리스트에서 복제할 때 딸려온 잔재다. `Progress Bar View`는 행동력 막대에 쓰이므로 남긴다) |
| 7 | 5번 컴포넌트의 필드를 연결한다 — **모두 이 슬롯 안의 오브젝트로** |

```
Recovery Station Slot View
  Slot Index          = 0                                    (슬롯 1번)
  Empty Root          = list_EmptySlot_Recovery               (GameObject)
  Character Root      = list_RecoveryCharacter                (GameObject)
  Portrait Image      = list_RecoveryCharacter/mask_portrait/sp_portrait          (Image)
  Name Text           = list_RecoveryCharacter/sp_name/lb_name                    (TextMeshProUGUI)
  Level Text          = list_RecoveryCharacter/sp_name/lb_level                   (TextMeshProUGUI)
  Stamina Value Text  = list_RecoveryCharacter/sp_stamina/Progress/bg_Bar/lb_percent (TextMeshProUGUI)
  Stamina Bar         = list_RecoveryCharacter (루트의 Progress Bar View)          (ProgressBarView)
  Timer Root          = list_RecoveryCharacter/Status/lb_Timer                    (GameObject)
  Time Text           = list_RecoveryCharacter/Status/lb_Timer/lb_time            (TextMeshProUGUI)
  Join Button         = list_RecoveryCharacter/Status/btn_JoinParty               (Button)  ★슬롯 안의 것
```

> ★ `btn_JoinParty`는 **패널 하단에도 같은 이름**이 있다. 반드시 슬롯 내부의 것을 연결한다.

### 6-3. 회복소 패널 (`pn_RecoveryStation`)

| # | 작업 |
| --- | --- |
| 8 | `pn_RecoveryStation`의 `list/viewport/content` 아래에 `list_RecoverySlot_1` 프리팹을 **3개** 배치하고 이름을 `list_RecoverySlot_1 / _2 / _3`으로 둔다 |
| 9 | 각 인스턴스의 `Slot Index`를 **0 / 1 / 2**로 서로 다르게 지정한다(중복이면 시작 시 오류 로그가 뜬다) |
| 10 | `pn_RecoveryStation` **루트**에 **`Recovery Station Panel`** 컴포넌트 추가 |
| 11 | 필드 연결 |

```
Recovery Station Panel
  Close Button           = top/btn_close                       (Button, ModalPanel 상속 필드)
  Block Background Input = Off                                 (다중 패널이므로 꺼 둔다)
  Slots (size 3)         = [0] list_RecoverySlot_1 의 SlotView
                           [1] list_RecoverySlot_2 의 SlotView
                           [2] list_RecoverySlot_3 의 SlotView
  Start Recovery Button  = bottom/btn_StartRecovery                 (Button)
  Cancel Pending Button  = bottom/btn_cancel                   (Button)
  Join All Button        = bottom/btn_JoinParty                (Button)   ★하단의 것
  Time Refresh Interval  = 0.25
```

| # | 작업 |
| --- | --- |
| 12 | 루트의 `Window Input Region`이 `Receive Mouse Input = On`인지 확인(이미 붙어 있다) |
| 13 | 루트의 `Panel Drag Handle` → `Target Panel = pn_RecoveryStation`(루트) 확인. **참고:** 교체 패널은 핸들이 `bg_top`에 있는데 회복소는 **루트**에 있어, 지금 구조에서는 패널 아무 곳이나 끌어도 패널이 움직이고 **슬롯 목록의 세로 스크롤과 경쟁**한다. 교체 패널과 동일하게 만들려면 이 컴포넌트를 `bg/top/bg_top`으로 옮기는 것을 권한다(이번 단계에서는 프리팹을 수정하지 않았다) |
| 14 | 패널 오브젝트를 **비활성(SetActive off)** 상태로 둔다(다른 패널과 동일) |

### 6-4. 캐릭터 교체 리스트 (`list_Character` 프리팹)

| # | 작업 |
| --- | --- |
| 15 | **`list_Character` 프리팹 에셋**에 **`Character Recovery Drag Source`** 컴포넌트를 추가한다(프리팹에 추가하면 `pn_CharacterSwap` 안의 템플릿 인스턴스에도 자동 반영된다) |
| 16 | 필드 연결 |

```
Character Recovery Drag Source
  Horizontal Start Distance = 12
  Scroll Rect  = (비워두면 부모에서 자동으로 찾는다. 명시하려면 pn_CharacterSwap/bg/list 의 ScrollRect)
  Ghost Canvas = (비워두면 상위 루트 Canvas를 쓴다)
  Ghost Alpha  = 0.8
```

| # | 작업 |
| --- | --- |
| 17 | 같은 프리팹의 `Character Swap List Item`에서 **`Drag Source`** 필드에 15번 컴포넌트를 연결한다(비워두면 같은 GameObject에서 자동으로 찾으므로 생략 가능) |
| 18 | 같은 컴포넌트의 **`State Text`** 를 `Status/bg_status/lb_status`에 연결한다. **현재 프리팹은 이 필드가 비어 있고 자동 탐색 이름(`lb_state`)과 실제 이름(`lb_status`)이 달라 상태 문구가 표시되지 않는 상태다**(코드에 `lb_status` 폴백을 넣었으므로 연결하지 않아도 동작하지만, 명시 연결을 권장한다) |
| 19 | 상태 문구 Localized 참조 5개를 `01_UI` 카테고리 키로 연결한다(현재 전부 비어 있다) |

```
State Ready Text             = 01_UI / key 7   (Ready)
State In Use Text            = 01_UI / key 8   (InUse)
State Exhausted Text         = 01_UI / key 9   (Exhausted)
State Recovering Text        = 01_UI / key 10  (Recovering)
State Recovery Complete Text = 01_UI / key 11  (RecoveryComplete)
```

| # | 작업 |
| --- | --- |
| 20 | **String Table 값 수정 (필수 — 이걸 해야 명세 문구가 화면에 나온다).** 로컬라이징 워크플로대로 Google Spreadsheet에서 고친 뒤 `TableData/Localization/01_UI.csv`를 내려받아 Unity에서 CSV(Merge) Import 한다 |

| key | 항목 | 현재 ko-KR | **수정 후 ko-KR** | 현재 en | **수정 후 en** |
| --- | --- | --- | --- | --- | --- |
| 10 | Recovering | `회복중 {0}` | **`회복 중`** | `Recovering {0}` | **`Recovering`** |
| 11 | RecoveryComplete | `회복완료` | **`합류 대기`** | `RecoveryComplete` | **`Awaiting Join`** |

> ⚠ **key 10의 `{0}`은 반드시 제거한다.** 상태 문구에 남은 시간을 넣지 않기로 했으므로(2.3절)
> 코드가 `{0}`을 채우지 않는다. 남겨 두면 화면에 `회복중 {0}`이 그대로 보인다.
>
> key 7/8/9(`교체가능` / `소환중` / `교체불가`)는 기존 값을 유지한다 — 명세가 지정한 문구가 없고
> 이미 쓰이던 값이다.
>
> **19번과 20번은 함께 해야 한다.** 코드는 참조가 비어 있을 때만 fallback 문구
> (`회복 중` / `합류 대기`)를 쓰므로, 참조만 연결하고 테이블 값을 고치지 않으면 옛 문구가 표시된다.

### 6-5. 열기 버튼 (`btn_RecoveryStation`)

| # | 작업 |
| --- | --- |
| 21 | 씬의 `btn_RecoveryStation`에 **`Recovery Station Opener`** 컴포넌트 추가 |
| 22 | 필드 연결 |

```
Recovery Station Opener
  Recovery Panel        = pn_RecoveryStation 의 RecoveryStationPanel   (비활성 오브젝트 그대로)
  Character Swap Panel  = pn_CharacterSwap  의 CharacterSwapPanel      (비활성 오브젝트 그대로)
```

| # | 작업 |
| --- | --- |
| 23 | `btn_RecoveryStation`의 `Button.OnClick()`은 **비워 둔다**(현재 비어 있음). Opener가 코드로 등록하므로 Inspector 이벤트를 추가하면 두 번 열린다 |
| 24 | `btn_change`의 기존 `Modal Panel Opener`(→ `pn_CharacterSwap`)는 **그대로 둔다** |

---

## 7. 3단계(알림) 연결에 필요한 이벤트/API 상태

2단계에서 알림은 구현하지 않았다. 3단계가 쓸 수 있는 상태는 다음과 같다.

| 항목 | 상태 |
| --- | --- |
| `RecoveryService.RecoveryCompleted` (슬롯 번호, 캐릭터) | **준비됨.** 동시 완료 시 (완료 시각, 슬롯 번호) 오름차순 보장 |
| `RecoveryService.SlotsChanged` / `StaminaStepChanged` | **준비됨.** 2단계 UI가 이미 구독해 동작을 검증했다 |
| 알림 문구 key 19 (`{0}(이)가 회복을 완료하였습니다.`) | 테이블에 **이미 존재**. `SystemNotificationDefinition` 에셋만 만들면 된다 |
| `SystemNotificationManager` 연동 | 2단계 시점 **미착수** → **3단계에서 완료**(`RecoveryCompletionNotifier`) |
| **1회성 보장** | 2단계 시점 ⚠ **미해결**(아래 참고) → **3단계에서 해결됨** |

> **⚠ 위 표는 2단계 완료 시점의 사실이다. 3단계에서 다음과 같이 해결됐다.**
>
> **2단계 당시 상태:** `completionReported`가 런타임 전용이라, 합류 전에 앱을 다시 켜면 같은 회복
> 주기의 완료 알림이 재발생할 수 있었다. 영속 notified marker가 필요하며 3단계 알림 설계가 그것을
> 소유해야 한다고 남겨 두었다(1단계 보고서 8-6절, 당시 코드의 `TODO(3단계 알림)` 주석).
>
> **3단계 해결:** `RecoverySlotSaveState.completionNotified`(저장되는 per-cycle marker)가 도입되어
> 재시작·중복 구독·`OnEnable` 반복·오프라인 완료를 가로질러 회복 주기당 알림 1회를 보장한다.
> 그에 따라 `completionReported`는 "같은 실행 안에서 도메인 이벤트를 슬롯당 한 번만 내보내는 guard"로
> 역할이 좁혀졌고, 코드의 `TODO(3단계 알림)` 주석은 제거됐다.
> 자세한 내용은 **`recovery-station-phase3-report.md` 2.1~2.2절**과 그 보고서의 요구사항 대응표 5)를 참고한다.

---

## 8. 미해결 위험 / 후속 논의

### 8-1. `PanelDragHandle` 위치 차이 (권장 조치 있음)

`pn_CharacterSwap`은 `bg_top`(타이틀바)에 핸들이 있는데 `pn_RecoveryStation`은 **루트**에 있다.
루트에 있으면 슬롯 목록 위에서 끈 드래그도 패널 이동으로 잡혀 **세로 스크롤과 경쟁**한다.
프리팹을 수정하지 않는 범위라 코드로 해결하지 않았고, 체크리스트 13번에 `bg_top`으로 옮기는 것을
권장 사항으로 적었다.

### 8-2. 상태 문구가 스펙 표기와 다르다

6-4절 19번 참고. 데이터 쪽에서 결정할 사항으로 남겼다.

### 8-3. 슬롯 프리팹이 1개만 있다

`list_RecoverySlot_1`만 존재하며 `_2 / _3`은 체크리스트 8번에서 사용자가 배치한다. 배치 전에는
연결된 슬롯 수만큼만 표시된다(패널이 시작 시 경고를 남긴다).

### 8-4. 회복소 슬롯 목록의 드롭 대상

슬롯 루트(`list_RecoverySlot_N`)에는 자체 `Image`가 없어 레이캐스트를 직접 받지 않는다. 빈 슬롯일 때는
`list_EmptySlot_Recovery`의 `Image`가 맞고 그 이벤트가 슬롯 루트로 **버블링**되어 `OnDrop`이 불린다.
`list_RecoveryCharacter`도 루트에 `Image`가 있어 동일하다. 다만 두 자식 모두 `Raycast Target`이 꺼져
있으면 드롭이 도달하지 않으므로, 실제 연결 후 한 번 확인하는 것이 좋다(수동 항목 5번과 함께).

### 8-5. Windows 투명창의 드래그 끊김

스펙상 이번 범위 밖. 패널 사이 공백은 클릭 관통 영역이라 드래그가 끊길 수 있다. 5절 6번 수동 확인
항목으로 남겼다.

### 8-6. 컴포넌트 이름

작업 지시서는 슬롯 컴포넌트를 `RecoverySlotView`로 불렀지만, 1단계에 이미
`Recovery.RecoverySlotView` **구조체**가 있어 이름이 충돌한다. 그래서 MonoBehaviour는
**`RecoveryStationSlotView`**로 지었다(구조체는 UI에 넘기는 읽기 전용 스냅샷이라 이름을 유지하는
편이 맞다).

---

## 9. 코드리뷰 보정 기록 (2단계 초판 이후)

### Finding 1 — 느린 가로 드래그가 임계값에 도달하지 못함

**문제:** `OnBeginDrag`에서 제스처를 한 번만 판정했다. EventSystem의 기본 드래그 임계값을 넘긴 첫
프레임의 이동량이 `horizontalStartDistance`(12px)보다 작으면 그 자리에서 스크롤로 확정되고,
이후 `OnDrag`에서 다시 보지 않았다. **빠르게 튕기듯 끌면 성공하고 천천히 끌면 실패**했다.

**수정:** `Undecided` 상태를 도입한 3-상태 기계로 바꿨다(3절 F). 가로 우세지만 임계값 미만이면
아무 것도 전달하지 않고 보류했다가, `OnDrag`에서 누적 이동량으로 재평가해 임계값에 닿는 순간 회복
드래그를 시작한다. 세로 우세로 바뀌면 그때 ScrollRect에 Begin을 전달한다. 한 번 정해지면 그 드래그
동안 바꾸지 않고, 보류 상태로 끝난 제스처는 전달한 것이 없으므로 정리만 한다.

**검증:** P22 (Begin 6px → 보류·전달 0, Drag 13px → 회복 드래그+고스트, 보류→스크롤 확정 시 Begin 1).

### Finding 2 — 드래그 후 다음 정상 클릭이 삼켜짐

**문제:** 회복 드래그를 시작할 때 `suppressNextClick = true`로 두고 `ConsumeClickSuppression()`이
불릴 때만 지웠다. 그런데 EventSystem은 드래그 제스처의 `Button.onClick`을 대개 발생시키지 않으므로
그 표시가 소비되지 않고 남아, **다음에 사용자가 새로 누르는 클릭**이 첫 소비 대상이 되어 삼켜졌다.

**수정:** 소비 방식을 버리고 **프레임 번호로 만료**시킨다. 드래그 중에는 `int.MaxValue`,
드래그가 끝나면 `Time.frameCount`로 낮춘다 — 즉 **끝난 프레임까지만** 억제한다. 즉시 지우지 않는
이유는 EventSystem이 같은 프레임 안에서 `OnEndDrag`보다 **먼저** 클릭을 처리하기 때문이다
(`ProcessMousePress` → `ProcessDrag` 순서). 취소 경로와 `OnDisable`/`OnDestroy`에서도 같은 만료를
적용한다. 메서드 이름도 실제 의미에 맞게 `ShouldSuppressClick()`으로 바꿨다.

**검증:** P23. 프레임 경계 자체는 하네스가 한 프레임 안에서 돌아 직접 재현할 수 없어, 만료 프레임
값이 `int.MaxValue`가 아니라 종료 프레임과 같다는 것과, 만료가 지난 상태에서 억제가 풀리는 것을
확인했다(실제 다음 프레임 클릭은 5절 4b 수동 항목).

### Finding 3 — 전달 중 비활성화 시 ScrollRect End 누락

**문제:** `CancelDrag`가 `StopMovement()`만 부르고 `OnEndDrag`를 전달하지 않았다.
`ScrollRectDragSettings`는 "전달을 시작했으면 End를 반드시 넘긴다"는 규칙을 지키는데 여기서 깨졌다 —
리스트 재생성/패널 닫기/컴포넌트 비활성 시 ScrollRect 내부의 dragging 상태가 남을 수 있었다.

**수정:** Begin을 전달할 때 쓴 `PointerEventData`를 보관하고, 취소 시 `OnEndDrag`를 **정확히 한 번**
전달한 뒤 정리한다. 이벤트나 ScrollRect가 없으면 호출하지 않는다(`OnEndDrag(null)`은 예외를 낸다).
정상 종료와 취소가 중복 End를 보내지 않으며, 회복 드래그 취소에는 (Begin을 보낸 적이 없으므로)
End를 보내지 않는다. **`StopMovement()` 무조건 호출은 제거**했다 — 관성을 임의로 죽이지 않고
ScrollRect가 자기 규칙대로 처리하게 둔다.

**검증:** P22(전달 중 비활성 → End 정확히 1, 취소 후 정상 End가 와도 1 유지, 회복 드래그 취소 → End 0).

### 추가 검토 — 드롭 재진입

드롭이 성공하면 목록 갱신이 `dragSource.enabled = false`를 만들고, 비활성 컴포넌트에는 `OnEndDrag`가
오지 않는다. `OnDisable → CancelDrag`가 고스트·플래그·클릭 억제를 모두 정리하는 것을 확인했다.
`eventData.pointerDrag`는 드롭 시점에 아직 유효하므로 성공을 방해하지 않는다. 실패 드롭에서는 아무
상태도 바뀌지 않고 드래그가 그대로 이어진다. (P24)

### 상태 문구 관련 (Coordinator 결정 반영)

- 상태 문구에서 **남은 시간을 분리**했다. 목록의 `Refresh`에서 시간 인자를 제거하고, 그 때문에만
  존재하던 목록의 주기 갱신 `Update`, `RecoveryStation.TryGetSlotForCharacter`,
  `RecoveryTimeFormat`의 TimeSpan 오버로드를 함께 제거했다. 남은 시간은 회복소 슬롯 `lb_time`의
  단독 책임이다.
- 참조 누락 시의 표시를 하드코딩된 `<Missing Localization>` 대신 **Inspector에서 바꿀 수 있는 상태별
  fallback 문구**로 바꾸고 기본값을 명세 값(`회복 중` / `합류 대기`)으로 두었다. 매핑은
  `GetStateFallbackText` 한 곳뿐이다. 오류 로그는 기존 관례대로 그대로 남긴다.
- `lb_status` 자동 탐색은 **그 리스트 항목 자신의 자식 안에서만** 수행한다.
- **자산이 6-4절 20번대로 수정되기 전까지 end-to-end 정확 문구 검증은 미완료다**(문서 상단 경고 참고).
