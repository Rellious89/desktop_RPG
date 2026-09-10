# 보류 작업: 인벤토리 자유 슬롯 배치

목적: 다음 작업자가 추가 분석 없이 슬롯 이동/교환 구현을 재개할 수 있도록 현재 계약과 구현 순서를 고정한다.

## 현재 구조

- 저장 보유 데이터는 `List<InventoryItemState>`이며 현재 항목은 `itemId + count`만 가진다.
- `SaveDataNormalizer`가 null 항목을 제거하고, 수량 0 제거 및 같은 ID 압축은 기존 인벤토리 계약에 따른다. UI는 목록 순서대로 압축 표시한다.
- 현재 UI는 배치된 슬롯 수만 표시하며, 슬롯 초과 보유분은 경고 후 표시하지 않는다.

## 1차 확정 요구사항

- 같은 ID는 하나의 스택만 유지한다.
- 드래그 이동은 스택 전체를 옮긴다.
- 빈 슬롯으로 이동할 수 있다.
- 점유 슬롯에 드롭하면 두 슬롯의 `slotIndex`를 쌍방 교환한다.
- 전량 소비 후 해당 칸은 빈 칸으로 남긴다.
- 새 아이템 획득 시 첫 빈칸에 넣는다.
- 정렬은 후속 작업이다. 안정적 압축(stable compaction)으로 기존 상대 순서를 유지한다.
- 정렬 버튼은 후속 UI 작업이며, 현재 이동/교환에는 추가 UI가 필요 없다.

## 권장 데이터 구조와 저장

`InventoryItemState`에 `slotIndex`를 추가한다. 빈 칸을 저장 항목으로 만들지 말고, 아이템 항목만 고정 슬롯 위치와 함께 저장한다. 로드 시 같은 ID/중복/0 수량은 정규화하고, 유효한 슬롯은 보존한다.

현재 `SaveData.CurrentSaveVersion`은 8이므로 슬롯 위치 도입은 **v8 → v9 명시적 마이그레이션**으로 처리한다. v8의 압축 목록 순서를 `slotIndex = 0..n-1`로 옮기고, 0 수량 제거·동일 ID 병합 후 충돌 없이 첫 유효 위치부터 배치한다. v9 단계는 `SaveMigrationRunner.CreateDefaultSteps()`에 등록하고 복사/정규화/직렬화 테스트를 함께 갱신한다.

32칸을 초과하는 데이터는 버리지 않는다. 0~31이 모두 차 있으면 새 아이템에 32 이상에서 가장 작은 빈 `slotIndex`를 부여해 저장에 보존한다. UI는 32칸만 표시하고 초과 항목을 경고한다.

저장 실패 시 이동 전 슬롯 상태와 저장 메타데이터를 함께 복원하여 화면과 메모리 위치를 롤백한다. 상점 판매 등록 드래그는 일반 인벤토리 이동보다 먼저 분기한다(`InventorySlotView`/`InventorySellDragPreview` → 판매 패널 대상 판정). 판매 영역이 유효하면 판매 등록만 수행하고 슬롯 이동/교환을 실행하지 않는다.

## 권장 구현 단계와 모델

1. **데이터/서비스 — Terra/high**: `InventoryItemState.slotIndex`, v8→v9 migration, 정규화, 스택/이동/교환/첫 빈칸/소비 후 빈칸 서비스와 저장 실패 롤백을 구현한다.
2. **UI — Terra/medium**: 슬롯별 `slotIndex` 연결, 드래그 시작/대상 판정, 판매 등록 우선 분기, 이동 결과 갱신을 연결한다. 이동/교환을 위해 새 버튼은 만들지 않는다.
3. **정렬 — Luna/medium**: 후속 안정적 압축 서비스와 정렬 버튼 연결을 별도 작업으로 진행한다.

## 핵심 테스트

- 같은 ID 획득이 단일 스택으로 합쳐지고 새 획득이 첫 빈칸을 사용한다.
- 빈 슬롯 이동, 점유 슬롯 쌍방 교환, 자기 슬롯 드롭, 잘못된 슬롯/ID가 원자적으로 무변경이다.
- 전량 소비가 해당 칸을 빈 칸으로 만들고 다른 슬롯/상대 순서를 바꾸지 않는다.
- v8 목록이 v9에서 순서대로 슬롯에 놓이고, 중복/0 수량/32칸 초과 항목이 보존 정책대로 처리된다.
- 32칸 초과 상태에서 새 아이템이 32 이상 인덱스에 보존되며 기존 데이터가 손상되지 않는다.
- 저장 실패/예외 시 아이템 위치·수량·목록·저장 메타데이터가 이동 전 상태로 복원된다.
- 판매 대상에 드롭하면 판매 등록만 되고 일반 이동/교환은 일어나지 않는다.
- 안정적 정렬이 동일 상대 순서를 유지한다(후속 정렬 단계).

## 재개용 코드 경로

- 데이터/저장: `Assets/Scripts/Common/SaveData.cs`, `Assets/Scripts/Common/SaveDataNormalizer.cs`, `Assets/Scripts/Common/SaveMigrationRunner.cs`, `Assets/Scripts/Inventory/InventoryManager.cs`
- 인벤토리 UI/드래그: `Assets/Scripts/Common/Inventory/InventoryPanel.cs`, `Assets/Scripts/Common/Inventory/InventorySlotView.cs`, `Assets/Scripts/Common/Inventory/InventorySellDragPreview.cs`
- 판매 등록/거래: `Assets/Scripts/Shop/UI/ShopPanel.cs`, `Assets/Scripts/Shop/ShopSellSession.cs`, `Assets/Scripts/Shop/ShopTradeService.cs`
- 회귀 테스트 기준: `Assets/Editor/Inventory/Tests/`, `Assets/Editor/Shop/Tests/`, `Assets/Editor/Common/Tests/SaveMigrationTests.cs`

## 보류 사유

우선순위 조정과 토큰 절약을 위해 슬롯 배치 구현을 보류했다. 다음 재개 시 이 문서의 1차 요구사항과 단계 순서를 기준으로 바로 구현한다.
