# 몬스터 처치 보상 연결(테스트용) 완료 보고서

작업일: 2026-07-30

씬과 프리팹 파일은 건드리지 않았다(작업 중 Unity 에디터가 프로젝트 락을 잡고 있다). 컴포넌트를
붙이는 작업은 7장 체크리스트로 넘긴다.

## 1. 보상 획득 이벤트

기존 `Target.AnyTargetDefeated(string targetId)` 하나만 구독한다. 이 이벤트는 `Target.Defeat()`
안에서 내구도가 0이 되어 처치가 확정되는 순간에만 발생하므로, 키 입력·공격 시작·타격·데미지 적용·
피격·애니메이션 종료로는 보상이 나가지 않는다. Target의 처치 흐름은 전혀 손대지 않았다.

같은 이벤트를 구독하는 시스템이 이제 네 개다(`PlayerProgress` 경험치/킬카운트, `SessionKillCounter`,
`AudioManager`, `CharacterRoster` 행동력). 여기에 보상 지급이 다섯 번째로 붙는다.

### 중복 처치 이벤트 방어 - 판정 규칙을 한 곳으로 모았다

지난 작업에서 `CharacterRoster` 안에 직접 넣었던 "같은 프레임 + 같은 targetId면 중복" 판정을
`Assets/Scripts/Common/DefeatEventFilter.cs`로 뽑아냈다. 행동력과 보상이 **각자 자기 필터를 하나씩**
들고 쓴다 - 하나를 공유하면 먼저 처리한 쪽이 다른 쪽의 이벤트를 삼켜서 둘이 서로에게 영향을 준다.

판정 근거는 그대로다: Target은 `IsDefeated`로 처치당 한 번만 이벤트를 보내고, 같은 대상이 다시
죽으려면 Fade-out → 리젠 대기 → Fade-in을 거쳐야 하므로 최소 여러 프레임이 걸린다. 서로 다른
몬스터가 같은 프레임에 죽는 경우는 id가 다르므로 각각 정상 처리된다.

### 행동력과의 독립성

행동력 소비(`CharacterRoster`)와 보상 지급(`TestDefeatRewardDistributor`)은 같은 이벤트를 각자
구독할 뿐 서로의 결과를 보지 않는다. 따라서 **행동력이 0이 되는 마지막 처치에서도 보상은 그대로
지급된다.**

## 2. 보상 처리 구조

```text
Target.AnyTargetDefeated
        ↓
TestDefeatRewardDistributor      (중복 판정 → 아이템 순번 결정 → 토스트)
        ↓
InventoryManager.ApplyReward(재화, 아이템, 수량)
        ├─ 재화 증가        ┐
        └─ 아이템 수량 증가 ┘ 메모리에서 한 번에 변경
        ↓
SaveSystem.Save() 1회 + InventoryChanged 1회
```

`InventoryManager`의 변경 메서드를 "메모리 값만 고치는 내부 메서드 + 마지막에 `SaveAndNotify` 한 번"
구조로 정리했다.

| 공개 메서드 | 내부 |
| --- | --- |
| `AddCurrency` / `SetCurrency` | `ApplyCurrencyDelta` / `ApplyCurrencyValue` |
| `AddItem` | `ApplyItemDelta` |
| **`ApplyReward`** (신규) | `ApplyCurrencyDelta` + `ApplyItemDelta` → 저장 1회 |

- 기존 `AddCurrency()` / `AddItem()` / `SetCurrency()` / `ClearInventory()`의 동작은 그대로다
  (값이 실제로 바뀐 경우에만 저장/알림).
- `ApplyReward`는 저장과 `InventoryChanged`를 **보상 1회당 정확히 한 번**만 낸다. 두 메서드를 따로
  부르면 "재화만 오른 중간 상태"가 화면에 한 번 그려지는데, 보상은 한 덩어리이므로 그렇게 나누지 않는다.
- 값 변경은 전부 메모리에서 끝낸 뒤 마지막에 한 번 저장하므로, 저장이 실패해도 일부만 기록된 파일이
  남지 않는다(기존 저장 파일이 그대로 유지되고 오류 로그가 남는다).
- 두 변경을 합칠 때 `||`가 아니라 `|=`를 쓴다 - `||`면 재화가 바뀐 순간 아이템 지급이 통째로 생략된다.

## 3. 보상 테스트 컴포넌트

`Assets/Scripts/Inventory/TestDefeatRewardDistributor.cs` (신규)

| 필드 | 기본값 | 설명 |
| --- | --- | --- |
| `Inventory Manager` | 비움 | 비워두면 실행 시 `InventoryManager.Instance`를 쓴다 |
| `Currency Per Defeat` | 100 | 처치 1회당 재화 |
| `Item Cycle` | 비움 | 지급 순서대로 등록(빨강 → 파랑 → 노랑) |
| `Item Count Per Defeat` | 1 | 한 번에 줄 수량 |
| `Reward Toast Format` | `획득: +{0} 재화 / {1} x{2}` | {0}=재화 {1}=아이템 이름 {2}=수량 |
| `Currency Only Toast Format` | `획득: +{0} 재화` | 아이템 목록이 비었을 때 |

동작:

- `OnEnable()`에서 구독, `OnDisable()`에서 해제.
- 처치마다 재화 + 아이템을 `ApplyReward` 한 번으로 지급.
- 아이템은 등록 순서대로 순환하고, **순환 인덱스는 실행 중에만 쓰고 저장하지 않는다**(앱을 다시
  켜면 첫 아이템부터 시작).
- 목록에 빈 항목이 있으면 그 회차는 아이템 없이 재화만 지급하고 경고를 남긴다 - 빈 항목을 건너뛰면
  인덱스가 밀려 순환 순서가 어긋나기 때문이다.

### 지시서와 다르게 한 점 (1건)

지시서는 구성 필드로 `빨간/파란/노란 포션 ItemDefinition` **세 개**를 들었는데, 순서가 명시적이고
나중에 종류를 바꾸기 쉽도록 **순서 있는 목록 하나(`Item Cycle`)** 로 만들었다. 지급 결과(빨강 →
파랑 → 노랑 → 반복)는 지시서와 동일하다. 세 개의 개별 필드가 더 낫다면 바꾸는 데 몇 줄이면 된다.

### 기존 연결 지점 재사용 검토

같은 이벤트를 구독하는 기존 컴포넌트를 먼저 확인했지만, 재사용하기 적절한 곳이 없어 새로 만들었다.

- `PlayerProgress`: 경험치/킬카운트 저장 필드의 소유자. 인벤토리 필드를 여기서 건드리면 저장 문서의
  필드 소유가 흐트러진다.
- `CharacterRoster`: 행동력 소유자. 보상을 여기에 넣으면 "행동력과 보상은 독립"이라는 요구와 반대가 된다.
- `RewardToast`: `PlayerProgress` 이벤트를 토스트로 넘기는 전용 브리지라 인벤토리 보상 지급 지점이 아니다.

## 4. 인벤토리 UI 갱신

기존 경로 그대로다. `ApplyReward` → `InventoryChanged` → 열려 있는 `InventoryPanel`이
`RefreshContents()`로 재화와 슬롯을 다시 그린다.

- 패널이 열려 있으면 재화·아이템 슬롯·수량이 즉시 갱신된다.
- 패널이 닫혀 있으면 데이터만 저장되고, 다음에 열 때 `OnEnable` → `RefreshContents`가 최신값을 그린다.
- 슬롯 구조(`sp_ItemIcon` / `lb_count`)와 표시 규칙은 바꾸지 않았다.
- 인벤토리 표시 순서는 지금까지대로 **획득 순서**다. 카탈로그 순서(현재 파랑/빨강/노랑)와는 무관하며,
  빨강 → 파랑 → 노랑 순으로 획득하면 그 순서대로 슬롯에 채워진다.

## 5. 획득 피드백

기존 `ToastManager`를 재사용한다. 처치 1회당 **정확히 한 번** 표시한다(타격·키 입력에는 반응하지 않는다).

```text
획득: +100 재화 / 빨간 포션 x1
```

`ToastManager.Instance`가 없으면 같은 내용을 `Debug.Log`로 남긴다 - 토스트 스택이 없는 구성에서도
값을 확인할 수 있다.

토스트 문구는 기존 `RewardToast`("+{amount} EXP", "레벨 업!")와 같이 평문 문자열이다. 테스트용
보상이라 이번에는 로컬라이징 대상에 넣지 않았다(로컬라이징 가이드 10장 마지막 규칙).

## 6. 저장

- 보상 1회당 `SaveSystem.Save()` 1회. 저장되는 값은 `SaveData.currency`와 `SaveData.items`뿐이다.
- 캐릭터/경험치/행동력/킬카운트 필드는 이 경로에서 읽지도 쓰지도 않는다(각 소유자가 자기 구독에서
  따로 갱신한다).
- 같은 아이템은 항상 저장 항목 하나에 수량으로 누적된다(`ApplyItemDelta`가 유일한 추가 경로).

## 7. Unity 에디터에서 해야 하는 연결 작업

씬에 이미 `InventoryManager` GameObject가 있고 Item Catalog에 포션 3종이 등록되어 있는 것을 확인했다.

1. 그 **`InventoryManager` 오브젝트에 `TestDefeatRewardDistributor`를 추가**한다.
2. `Item Cycle`에 3칸을 만들고 **순서대로** 넣는다.

   | # | 에셋 |
   | --- | --- |
   | 0 | `Assets/Data/Items/Item_RedPotion.asset` |
   | 1 | `Assets/Data/Items/Item_BluePotion.asset` |
   | 2 | `Assets/Data/Items/Item_YellowPotion.asset` |

   (Item Catalog의 등록 순서는 파랑/빨강/노랑이지만 지급 순서와는 무관하다 - 순서를 정하는 것은
   이 `Item Cycle` 목록뿐이다.)
3. `Inventory Manager` 필드는 비워 둬도 되고, 같은 오브젝트의 것을 넣어 두면 더 명확하다.
4. `Currency Per Defeat`가 100인지 확인한다.

## 8. 검증 상태

### 이 환경에서 확인한 것

- `Assets/Scripts` 전체를 Unity와 같은 정의(`UNITY_STANDALONE_WIN` 포함)로 Roslyn 컴파일 → **오류 0건**.
- `Target.AnyTargetDefeated`가 `Defeat()` 안에서 단 한 번만 발생하는지 코드로 재확인.
- 씬의 `InventoryManager` 배치와 Item Catalog 등록 상태, 포션 3종의 itemId/아이콘 연결 확인.

### 확인하지 못한 것

- **Play Mode 실행 전체**(에디터가 프로젝트 락을 잡고 있어 실행할 수 없다). 지시서의 검증 1~11번은
  에디터에서 직접 확인해야 한다.

### 검증 항목별 참고

- 1번(인벤토리 비우기): `InventoryManager` 컨텍스트 메뉴 `Debug - Clear Inventory`.
- 9번(행동력 0인 마지막 처치): 행동력이 1 남은 상태에서 처치하면 행동력 0 + 보상 정상 지급이어야 한다.
- 10번(중복 이벤트): 콘솔에 `[DefeatEventFilter] ... 중복 발생` 경고가 **뜨지 않으면** 정상이다
  (떴다면 실제 이중 호출이 있었고, 그래도 한 번만 지급된 것이다).
- 11번(캐릭터 교체): 재화/아이템은 캐릭터별이 아닌 전역 저장 값이라 교체와 무관하다.

## 9. 구현하지 않은 것

지시서의 제외 항목(랜덤 드랍 확률, 몬스터별 보상 테이블, 아이템 사용/효과, 상점, 판매, 장비,
보상 정산 화면, 재화 소비처) 전부. 추가로:

- 아이템 순환 인덱스 저장(테스트용이라 실행 중에만 유지)
- 보상 토스트 로컬라이징
