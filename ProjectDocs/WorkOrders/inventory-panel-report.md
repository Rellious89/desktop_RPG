# 인벤토리 UI 기능 연결 완료 보고서

작업일: 2026-07-30
대상: `Assets/Art/UI/Prefab/panel/pn_Inventory.prefab`, `Assets/Art/UI/Prefab/Inventory/list_item.prefab`

기존 UI 레이아웃과 배치는 변경하지 않았다. 씬/프리팹 파일도 건드리지 않았으므로, 컴포넌트를 붙이는
작업은 8장 체크리스트로 넘긴다(작업 중 Unity 에디터가 프로젝트 락을 잡고 있다).

## 0. 에디터에서 확인한 실제 구조

지시서의 이름과 실제 프리팹 이름이 다른 곳이 있어, **실제 이름 기준**으로 구현했다.

```text
pn_Inventory            (씬 인스턴스는 이미 비활성)
├ bg
│ ├ top
│ │ ├ bg_top > lb_title
│ │ └ btn_close
│ └ list                (Grid Layout Group 63x63 / 간격 10, Mask, Image)
│   └ list_item x 8     (list_item 프리팹의 중첩 인스턴스 - 이미 8칸 배치됨)
└ bot
  ├ bg_line
  └ currency
    ├ bg_currency > sp_currencyIcon
    └ lb_currency        ← 재화 표시 텍스트

list_item
├ sp_ItemIcon           (지시서의 sp_itemIcon)
└ lb_count              (지시서의 lb_StackCount, 현재 텍스트는 자리표시자 '{0}')
```

`list` 아래에 **슬롯 8칸이 이미 배치**되어 있으므로 지시서대로 이 개수를 초기 슬롯 수로 쓴다.
런타임에 슬롯을 만들거나 지우지 않는다.

## 1. 패널 열기/닫기 - 캐릭터 교체 패널과 같은 모달 처리

"캐릭터 교체 패널과 동일한 모달 UI 처리 방식을 사용한다"는 요구에 맞춰, 두 패널이 **같은 코드**를
쓰도록 공통 베이스 클래스를 뽑았다.

`Assets/Scripts/Common/ModalPanel.cs` (신규)

- `Open()` / `Close()`
- `btn_close` 자동 탐색 + 연결
- 전체 화면 InputBlocker 생성/켜기/끄기 (`OnDisable`에서 반드시 꺼진다)
- Windows 클릭 관통 예외 등록/해제 (`TransparentWindowController.SetModalClickableRect`)
- 파생 클래스 훅: `OnModalOpened()` → `RefreshContents()` → 입력 차단 순서

`CharacterSwapPanel`은 이 베이스를 상속하도록 바꿨다. 직렬화 필드 이름(`closeButton`,
`inputBlocker`, `inputBlockerColor`)이 그대로라 프리팹에 연결된 값은 유지된다. 리스트 구성/선택/교체
로직은 한 줄도 바꾸지 않았다.

### 함께 고친 버그: 두 패널이 InputBlocker를 공유하던 문제

이전 구현은 차단막 이름이 `InputBlocker` 고정이라 `Panel_UI` 아래에서 **두 패널이 같은 오브젝트를
공유**했다. 한쪽을 닫으면 다른 쪽의 입력 차단까지 꺼지는 상태였다(패널이 하나뿐일 때는 드러나지
않았다). 이제 `InputBlocker_<패널 이름>`으로 패널마다 따로 만든다.

모달이 동시에 두 개 열리는 경로는 없다 - 열린 패널의 차단막이 ControlDock 전체를 덮으므로 다른
패널을 여는 버튼을 누를 수 없다.

## 2. 재화 표시

`InventoryPanel.RefreshCurrency`가 `lb_currency`에 그린다.

```csharp
inventory.Currency.ToString("N0", CultureInfo.InvariantCulture)
```

`InvariantCulture`를 명시했으므로 실행 환경의 지역 설정(점/공백 구분자)과 무관하게 항상 쉼표다.

| 값 | 표시 |
| --- | --- |
| 0 | `0` |
| 1000 | `1,000` |
| 12345 | `12,345` |
| 1000000 | `1,000,000` |

재화는 `SaveData.currency`(전역 값 하나)이며 아이템 슬롯에 표시하지 않는다. 경험치/레벨/행동력과
연결된 코드는 없다.

## 3. 아이템 목록 표시

- 배치된 8칸을 앞에서부터 채우고 남는 칸은 빈 슬롯으로 만든다.
- 같은 아이템은 저장 항목 하나에 수량으로 누적되므로(4장) 슬롯이 나뉘지 않는다.
- 수량은 1이어도 그대로 표시한다(`InventorySlotView.countFormat`으로 조정 가능).
- 빈 슬롯은 아이콘/수량 **GameObject를 끄고** 값도 비워 슬롯 배경만 남긴다. 슬롯 자신(`list_item`)은
  끄지 않으므로 격자 배치가 밀리지 않는다.
- 런타임 복제본을 만들지 않으므로 몇 번 갱신해도 슬롯이 중복 생성되지 않는다.
- 표시 순서는 **획득 순서**다 - 처음 획득할 때 저장 목록 뒤에 추가되고 그 뒤로 자리가 바뀌지 않아
  저장/불러오기를 거쳐도 순서가 유지된다.
- 보유 종류가 8칸보다 많으면 넘치는 만큼은 표시하지 않고 경고를 한 번만 남긴다(슬롯 확장은 범위 밖).

주의: 프리팹의 `sp_ItemIcon` / `lb_count`는 **GameObject 자체가 비활성 상태로 저장**되어 있다.
컴포넌트의 `enabled`만 켜면 화면에 나오지 않으므로 `SetItem`/`SetEmpty`가 GameObject를 함께
전환한다(프리팹의 기본 상태는 비활성 그대로 두어도 된다).

아이콘 아트가 없는 아이템(`ItemDefinition.icon`이 빔)은 수량만 표시된다 - 스프라이트 없는 Image가
흰 사각형으로 그려지지 않도록 그 경우에는 Image 컴포넌트만 꺼 둔다.

## 4. 데이터 구조

기존 프로젝트에는 인벤토리/아이템 구조가 없었다(검색으로 확인). 캐릭터 쪽과 같은 분담으로 새로 만들었다.

| 구분 | 위치 | 내용 |
| --- | --- | --- |
| 아이템 정의 | `Assets/Scripts/Inventory/ItemDefinition.cs` (ScriptableObject) | itemId / displayName / icon |
| 보유 데이터 | `SaveData.currency`, `SaveData.items` (`InventoryItemState`: itemId + count) | 재화와 수량 |
| 관리 | `Assets/Scripts/Inventory/InventoryManager.cs` | 추가/조회/전체 목록/저장 |

- UI는 씬 오브젝트나 프리팹 상태를 인벤토리의 근거로 삼지 않는다 - 저장 데이터가 유일한 기준이다.
- 재화는 아이템 목록과 분리된 전역 값이다.
- 카탈로그에 없는 itemId가 저장 파일에 있으면 표시만 건너뛰고 **저장 값은 지우지 않는다**(경고 1회).
- 카탈로그에 없는 아이템을 `AddItem`으로 넣으려 하면 오류를 남기고 막는다 - 저장은 됐는데 화면에
  안 보여 "사라진 것처럼" 되는 상황을 만들지 않기 위함이다.

## 5. 저장

- 저장 경로는 `InventoryManager.SaveAndNotify` 하나뿐이고, **값이 실제로 바뀐 경우에만** 호출된다.
  매 프레임/입력마다 저장하는 경로가 존재하지 않는다.
- 기존 공유 문서(`SaveSystem.Data`)에 필드만 추가했다. 캐릭터/경험치/행동력 필드는 건드리지 않는다.
- 예전 저장 파일에 인벤토리 항목이 없으면 재화 0, 아이템 없음으로 시작한다
  (`currency`는 값 타입이라 자동 0, `items`는 `SaveSystem`이 빈 목록으로 보정).
- 저장 실패 시 오류를 남긴다 - 이번 실행에는 적용되지만 재실행하면 되돌아간다는 사실을 감추지 않는다.

## 6. 테스트용 진입점 (정식 UI 비노출)

`InventoryManager`의 Inspector 컨텍스트 메뉴에만 있다.

| 메뉴 | 동작 |
| --- | --- |
| `Debug - Add Currency` | `Debug Currency Amount`(기본 1000)만큼 재화 추가 |
| `Debug - Set Currency To Zero` | 재화 0 |
| `Debug - Add Item` | `Debug Item`을 `Debug Item Count`만큼 추가(반복 누르면 수량 누적 확인) |
| `Debug - Add One Of Every Item` | 카탈로그의 모든 아이템을 1개씩 추가(여러 종류 + 빈 슬롯 확인) |
| `Debug - Clear Inventory` | 재화 0 + 아이템 전체 삭제 |

패널이 열린 상태에서 실행하면 `InventoryChanged`로 그 자리에서 갱신된다.

테스트용 아이템 정의 3종을 만들어 두었다(아이콘 없음, 이름으로 임시임이 드러나게).

```text
Assets/Data/Items/Item_TestItemA.asset
Assets/Data/Items/Item_TestItemB.asset
Assets/Data/Items/Item_TestItemC.asset
```

## 7. 추가/수정한 파일

### 신규

| 파일 | 역할 |
| --- | --- |
| `Assets/Scripts/Common/ModalPanel.cs` | 모달 패널 공통 처리(열기/닫기/입력 차단/클릭 관통) |
| `Assets/Scripts/Inventory/ItemDefinition.cs` | 아이템 정의 에셋 |
| `Assets/Scripts/Inventory/InventoryManager.cs` | 재화·아이템 소유자, 저장, 개발용 진입점 |
| `Assets/Scripts/Common/Inventory/InventoryPanel.cs` | 인벤토리 패널 표시 |
| `Assets/Scripts/Common/Inventory/InventorySlotView.cs` | 슬롯 한 칸 표시 |
| `Assets/Data/Items/Item_TestItem*.asset` | 테스트용 아이템 정의 3종 |

### 수정

| 파일 | 변경 |
| --- | --- |
| `Assets/Scripts/Common/SaveData.cs` | `currency`, `items`(`InventoryItemState`) 추가 |
| `Assets/Scripts/Common/SaveSystem.cs` | `items` null 보정 추가 |
| `Assets/Scripts/Common/CharacterSwap/CharacterSwapPanel.cs` | `ModalPanel` 상속으로 전환(리스트/선택 로직은 그대로) |
| `Assets/Scripts/Common/ModalPanelOpener.cs` | `CharacterSwapPanelOpener.cs`를 `.meta`와 함께 이름 변경(guid 유지), 대상 타입을 `ModalPanel`로 일반화 |

`CharacterSwapPanelOpener` → `ModalPanelOpener`는 guid(`d9c06ac5…`)를 유지했으므로 씬의
`btn_change` 연결이 끊기지 않는다. 필드 이름(`panel`)도 그대로라 참조 대상도 유지된다.

## 8. Unity 에디터에서 해야 하는 연결 작업

> 스크립트가 컴파일된 뒤에 진행한다. 참조 항목은 대부분 비워 둬도 이름으로 자동 탐색된다.

1. **`list_item` 프리팹**에 `InventorySlotView`를 추가한다. References는 비워 둬도 된다
   (`sp_ItemIcon` / `lb_count`를 이름으로 찾는다). 프리팹에 붙이면 `list` 아래 8칸에 모두 적용된다.
2. **`pn_Inventory` 프리팹 루트**에 `InventoryPanel`을 추가한다. References는 비워 둬도 된다
   (`list` / `lb_currency`를 이름으로 찾는다).
3. 씬에 **`InventoryManager`**를 배치한다(예: `DesktopStage`). Item Catalog에
   `Assets/Data/Items/`의 테스트 아이템 3종을 등록하고, Debug Item에 그중 하나를 넣는다.
4. **`btn_inventory`**(Button이 붙어 있는 안쪽 오브젝트, go 1542749132)에 `ModalPanelOpener`를 추가하고
   Panel에 씬의 `pn_Inventory` 인스턴스를 연결한다.
5. `btn_change`의 컴포넌트가 `Modal Panel Opener`로 보이고 Panel 연결이 남아 있는지 확인한다.
   `pn_CharacterSwap` 프리팹의 `CharacterSwapPanel`도 Close Button 연결이 유지됐는지 함께 본다.
6. `lb_count`의 자리표시자 텍스트(`{0}`)는 런타임에 덮어쓰므로 그대로 둬도 된다.

## 9. 검증 상태

### 이 환경에서 확인한 것

- `Assets/Scripts` 전체를 Unity와 같은 정의(`UNITY_STANDALONE_WIN` 포함)로 Roslyn 컴파일 → **오류 0건**.
- 프리팹/씬 YAML을 읽어 실제 오브젝트 이름과 슬롯 개수(8), Grid Layout 설정, `pn_Inventory`
  인스턴스가 이미 비활성인 것을 확인.
- 이름 변경한 스크립트의 guid가 유지되어 씬 참조가 그대로임을 확인.

### 확인하지 못한 것

- **Play Mode 실행 전체**(에디터가 프로젝트 락을 잡고 있어 실행할 수 없다).
- **Windows 클릭 관통 예외**는 Win32 경로라 **Windows 빌드에서만** 실제 검증된다. 이 등록이 없으면
  패널이 보이기만 하고 버튼이 눌리지 않는다. 캐릭터 교체 패널과 같은 코드를 쓰므로 그쪽이 정상이면
  인벤토리도 같이 동작한다.

## 10. 이번 단계에서 구현하지 않은 것

지시서의 제외 항목(보상 연동, 사용/장착/장비, 판매, 상점, 강화, 상세 정보창, 페이지, 슬롯 확장,
드랍 확률, 보상 정산, 재화 소비처) 전부. 추가로:

- 아이템 아이콘 아트(정의 에셋의 icon은 비어 있다)
- 아이템 이름 표시와 로컬라이징(슬롯에는 아이콘 + 수량만 표시한다)
- 인벤토리 정렬 규칙(임시로 획득 순서 고정)
