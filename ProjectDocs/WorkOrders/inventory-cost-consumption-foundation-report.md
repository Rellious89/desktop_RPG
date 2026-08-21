# KeyBuddy 9.1단계 아이템·재화 비용 소비 기반 완료 보고

## 커밋

- 기준: `cfdbfb1d` (`던전 UI 프리팹 보상 아이콘 간격 수정`)
- 구현·테스트: `f766e85d` (`Add atomic inventory cost spending`)
- 원격 푸시: 수행하지 않음

## 구현 결과

- `InventoryCostRequest`가 재화와 여러 아이템 비용을 읽기 전용 요청으로 표현한다. 아이템 정의와 정확한 Item ID를 모두 지원하고, 동일 ID는 최초 등장 순서를 유지하며 합산한다.
- `InventoryManager.EvaluateCost`는 저장 데이터나 캐시를 변경하지 않고 전체 지불 가능 여부와 `InvalidRequest`, `UnknownItem`, `InsufficientCurrency`, `InsufficientItem` 실패 정보를 반환한다.
- `InventoryManager.TrySpendCost`는 모든 요구를 먼저 검증한 뒤 재화와 아이템을 함께 차감한다. 하나라도 부족하면 전체가 무변경이며, 성공 시 저장과 `InventoryChanged`는 각각 최대 한 번만 발생한다.
- 전량 소비한 아이템 항목은 제거하고, 부분 소비한 항목과 나머지 저장 목록의 기존 순서는 유지한다. 비용 소비에서는 `RewardApplied`를 발생시키지 않는다.
- 음수 비용, 양수 비용의 빈 ID, 미등록 아이템 및 합산 오버플로를 거부한다. 수량 0인 항목은 완전한 no-op으로 처리하여 빈 ID나 미등록 ID여도 무시한다.
- 기존 `TrySpendCurrency`와 회복소용 재화 소비 경계는 변경하지 않았다.
- SaveData 필드와 `CurrentSaveVersion = 2`를 유지했으며 마이그레이션을 추가하지 않았다.

## 변경 파일

- `Assets/Scripts/Inventory/InventoryCost.cs`
- `Assets/Scripts/Inventory/InventoryCost.cs.meta`
- `Assets/Scripts/Inventory/InventoryManager.cs`
- `Assets/Editor/Inventory/Tests/InventoryCostTests.cs`
- `Assets/Editor/Inventory/Tests/InventoryCostTests.cs.meta`

씬, UI 프리팹, Item·Dungeon·Monster CSV, Generated TableData, 던전 보상·세션·툴팁 및 회복소 코드는 변경하지 않았다. 기준점의 사용자 UI 변경을 포함한 보호 파일 166개의 SHA-256이 모두 일치했다.

## 검증

- 집중 EditMode: `93 / 93` 통과, failure/skip/inconclusive 0
  - 신규 비용 테스트: 56
  - 기존 인벤토리 보상 적용 회귀 테스트: 37
  - 회복소가 사용하는 기존 `TrySpendCurrency` 경계의 충분·부족·0·음수·무저장 차감 동작과 `RewardApplied` 비발생 포함
- Unity 컴파일 오류: 0
- `git diff --check`: 통과
- 테스트는 `SaveSystem` 저장 호출을 메모리 카운터로 대체해 실제 `Application.persistentDataPath`를 사용하지 않았다.
- 전체 EditMode는 지시에 따라 후속 경량 단계 통합 검증 시점으로 미뤘다.

## 제외 범위

아이템 사용 UI·효과, 툴팁 Bottom, 마을 건물, 건설 UI, 상점, 대장간, 판매·환불 및 SaveData v3는 구현하지 않았다.
