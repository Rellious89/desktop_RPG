# Character Story Quest Reward — Phase 13D

## 결과

- 구현 커밋: `f77ee4d0` (`서사 퀘스트 완료 보상 적용`)
- `CharacterStoryQuest.csv`의 보상 두 슬롯을 `Currency`/`Item`/빈 값·`None` 계약으로 읽고 검증한다.
  - `Currency`는 `jewel`만 허용한다.
  - 보상 수량은 양수여야 하고, 같은 타입이 두 슬롯에 중복되면 오류다.
  - 빈 값과 `None` 슬롯은 남은 대상/수량 편집 흔적까지 무시하며 Generated Definition에도 남기지 않는다.
- Generated 퀘스트 보상:
  - `CatKnight_10001`: jewel x100
  - `CatKnight_10002`: jewel x150
  - `CatKnight_10003`: jewel x200, Item 50001 x3
- 완료 확정은 인벤토리 보상·퀘스트 상태를 무저장 메모리 트랜잭션으로 적용한 후 `SaveSystem.Save`를 정확히 한 번 호출한다.
  - 저장이 `false`이거나 예외를 던지면 퀘스트 상태와 인벤토리 목록(원래 목록/항목 참조와 순서 포함)을 모두 복원한다.
  - 저장 성공 뒤에만 `InventoryChanged`, `RewardApplied`, 획득 토스트를 한 번 보낸다.
  - 이미 완료된 퀘스트는 재호출해도 보상·저장·알림을 반복하지 않는다.
- 명부 `QuestInfo > ObjectiveScroll > Content > QuestReward`를 연결했다. 재화·아이템 영역은 존재하는 보상만 켜며, 아이템은 기존 `InventorySlotView`를 그대로 사용해 아이콘·수량·호버 툴팁 경로를 공유한다.

## 변경 파일

- `Assets/Scripts/Quest/CharacterStoryQuestDefinition.cs`
- `Assets/Scripts/Quest/CharacterStoryQuestService.cs`
- `Assets/Scripts/Inventory/InventoryManager.cs`
- `Assets/Scripts/CharacterArchive/CharacterStoryQuestUiController.cs`
- `Assets/Editor/TableData/CharacterStoryQuestTablePipeline.cs`
- `Assets/Generated/TableData/CharacterStoryQuest/Quest_CatKnight_10001.asset`
- `Assets/Generated/TableData/CharacterStoryQuest/Quest_CatKnight_10002.asset`
- `Assets/Generated/TableData/CharacterStoryQuest/Quest_CatKnight_10003.asset`
- `Assets/Art/UI/Prefab/panel/pn_CharacterArchive.prefab`
- `Assets/Editor/CharacterArchive/CharacterStoryQuestUiPrefabSetup.cs`
- 집중 EditMode 테스트와 프리팹 검증: `Assets/Editor/Quest/Tests/CharacterStoryQuestRewardTransactionTests.cs`, `Assets/Editor/TableData/Tests/CharacterStoryQuestRewardTableTests.cs`, `Assets/Editor/CharacterArchive/Tests/CharacterStoryQuestArchivePrefabTests.cs`

## 검증

- `git diff --check`: 통과.
- 활성 편집기를 건드리지 않도록 `/private/tmp/desktop-rpg-phase13d-isolated` 복제본에서 Unity `2022.3.62f3`로 컴파일했다. C# 컴파일 오류는 0건이다.
- 집중 EditMode 8건이 모두 통과했다.
  - 완료 트랜잭션: 3건(성공·저장 실패·저장 예외/중복 지급 차단)
  - CSV/Generated 보상 계약: 2건
  - 명부 보상 프리팹 wiring/`InventorySlotView` 툴팁 경로: 3건
- 정적 확인으로 세 Generated asset의 보상 포인터·수량 및 `SaveData.CurrentSaveVersion == 8`을 확인했다.

## 안전 경계

- 원격 푸시를 하지 않았다.
- 실제 `persistentDataPath`를 읽거나 쓰지 않았다. 새 트랜잭션 테스트는 `SaveSystem.data`와 저장 이음매를 메모리 객체로 교체한다.
- `SaveData.CurrentSaveVersion`은 8을 유지했다.
