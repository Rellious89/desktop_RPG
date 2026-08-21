# KeyBuddy 8.1단계 던전 보상 공용 툴팁 완료 보고

## 커밋

- 기준: `407f7431` (`Refine inventory layout and tooltip placement`)
- 던전 대표 보상 데이터: `928fbf79` (`Add dungeon representative reward data`)
- 공용 툴팁 연결: `306973fb` (`Reuse item tooltips across dungeon reward views`)
- 포커스 패널 위 표시 유지: `743f6b5c` (`Keep visible item tooltips above focused panels`)
- 원격 푸시: 수행하지 않음

## 구현 결과

- `Dungeon.csv`의 생산 던전 1~6에 `50002|50003|50004`, 7~9에 `50000|50002|50003|50004`를 대표 보상으로 등록했다.
- 공식 TableData Rebuild는 격리 클론에서 실행하고 `Dungeon_1`~`Dungeon_9` 생성 에셋만 반영했다. 대표 보상은 표시용 정적 데이터이며 드롭, 지급, 세션 원장에는 영향을 주지 않는다.
- `ItemTooltipController`가 `MonoBehaviour` 소유자를 받아 인벤토리 슬롯, 던전 주요 보상, 던전 결과 보상이 `Panel_UI`의 단일 컨트롤러와 단일 툴팁 인스턴스를 공유한다.
- 인벤토리는 현재 보유 수량을, 던전 결과는 세션의 `long` 획득 수량을 표시한다. 던전 주요 보상은 수량 텍스트와 오브젝트를 비워 숨긴다.
- 두 던전 보상 View는 Enter에서 표시하고 Exit, Clear, 재바인딩, 비활성화에서 자기 소유 툴팁만 종료한다. 이전 소유자의 늦은 Exit는 새 소유자의 표시를 닫지 않는다.
- 표시 중인 툴팁은 `LateUpdate`에서 필요한 경우에만 마지막 형제로 복원한다. 클릭은 내용을 재바인딩하거나 숨기지 않으며 실제 Pointer Exit와 데이터 수명주기는 기존대로 유지한다.
- 보상 재지급, 세션 Consume, 추가 저장은 구현하지 않았다. SaveData 및 saveVersion 2도 변경하지 않았다.

## 변경 파일

- 데이터 및 생성 결과: `Dungeon.csv`, `Dungeon_1.asset`~`Dungeon_9.asset`
- 런타임: `ItemTooltipController.cs`, `ItemTooltipView.cs`, `InventorySlotView.cs`, `DungeonRewardPreviewView.cs`, `DungeonResultRewardItemView.cs`, `DungeonResultPanel.cs`
- 테스트: `ItemTooltipTests.cs`, `DungeonTableTests.cs`

사용자 제작 `pn_Inventory.prefab`, `item_ToolTip.prefab`, `desktopScene_ReSize.unity` 및 UI 크기·위치·폰트·색상·레이아웃은 변경하지 않았다. `pn_Inventory`에는 중복 컨트롤러가 없고 대상 씬의 `Panel_UI`에 공용 컨트롤러가 하나만 존재하는 배선을 테스트로 확인했다.

## 검증

- 집중 EditMode: `101 / 101` 통과, failure/skip/inconclusive 0
- TableData EditMode: `227 / 227` 통과, failure/skip/inconclusive 0
- Unity 컴파일 오류: 0
- `git diff --check`: 통과
- Dungeon 외 Generated 파일 88개: 기준 SHA-256과 byte-identical
- Dungeon 1~9 `.meta`와 GUID, DungeonCatalog 및 테스트 던전 에셋: 기준과 동일
- 사용자 씬과 툴팁·인벤토리 프리팹: 기준 SHA-256과 동일
- 집중 테스트는 격리 프로젝트에서 실행했으며 실제 `persistentDataPath`를 사용하는 PlayMode는 실행하지 않았다.

## 남은 수동 검증

- 인벤토리 Hover 중 좌·우 클릭 및 다른 패널 포커스 후에도 툴팁이 가려지지 않는지 확인한다.
- 던전 주요 보상 Hover에서 수량이 숨겨지고, 던전 결과 Hover에서 실제 획득 수량이 표시되는지 확인한다.
- 세 화면 모두 포인터가 아이콘 밖으로 나가면 즉시 종료되는지 확인한다.
- Windows 실제 클릭 관통은 Windows 환경에서 확인한다.
