# KeyBuddy 8단계 아이템 툴팁 완료 보고

## 커밋

- 기준: `f8726b59` (`feat: add item tooltip UI and icons`)
- 구현: `9fa347a5` (`Add inventory item description tooltips`)
- 원격 푸시: 수행하지 않음

## 구현 결과

- Item 파이프라인이 `description_category`와 `description_key`를 정식 입력으로 읽는다.
- 활성 아이템은 이름·설명 참조를 모두 요구하며, 같은 카테고리와 `description_key = name_key + 10000` 규칙, 숫자 범위와 실제 Localization Entry 존재를 검증한다.
- `ItemDefinition`에 `LocalizedDescription`과 `HasLocalizedDescription`을 추가했다. SaveData 및 saveVersion 2는 변경하지 않았다.
- `Item_50000`~`Item_50004`에 설명 참조와 사용자 아이콘을 연결했다. ItemCatalog 순서와 모든 Generated `.meta`/GUID는 유지했다.
- `ItemTooltipView`가 아이콘, 현지화 이름, 실제 보유 수량, 현지화 설명을 표시한다. Locale 변경 시 열린 툴팁을 갱신하고 숨김·재바인딩·파괴 시 구독을 해제한다.
- 이름은 `DisplayName`과 Item ID 순으로 폴백하고, 설명은 빈 문자열로 폴백한다. 수량 템플릿에 정확한 `{0}` 토큰이 없거나 형식이 잘못되면 숫자만 표시한다.
- `ItemTooltipController`는 프리팹 인스턴스 하나를 `Panel_UI` 아래에서 재사용한다. 모든 Graphic Raycast Target을 끄고, 슬롯 오른쪽 우선·왼쪽 대체·Canvas 상하 경계 보정을 적용한다.
- `InventorySlotView`가 `ItemDefinition`과 수량을 보관하며 Enter/Exit, `SetEmpty`, 슬롯·패널 비활성화, 내용 변경 시 stale 툴팁을 정리한다.

## 변경 파일

- TableData: `TableDataCsvReader.cs`, `TableDataPaths.cs`, `TableDataRows.cs`, `TableDataValidator.cs`, `TableDataRebuilder.cs`
- Runtime: `ItemDefinition.cs`, `InventoryPanel.cs`, `InventorySlotView.cs`, 신규 `ItemTooltipView.cs`, `ItemTooltipController.cs`
- Prefab 배선: `item_ToolTip.prefab`, `pn_Inventory.prefab`
- Generated: `Assets/Generated/TableData/Item/Item_50000.asset`~`Item_50004.asset`
- Tests: `ItemTableTests.cs`, `ItemTooltipTests.cs`

사용자 프리팹의 RectTransform, Layout, 폰트, 머티리얼, 색상, 이미지와 비활성 Bottom 값은 바꾸지 않았다. 두 프리팹 diff는 컴포넌트와 Inspector 참조 추가뿐이다. `Item.csv`, `04_Item` CSV·String Table, `desktopScene_ReSize.unity`, 폰트와 사용자 스프라이트도 변경하지 않았다.

## 검증

- 집중 EditMode: `53 / 53` 통과, failure/skip/inconclusive 0
- Luna 보정 집중 EditMode: `34 / 34` 통과, failure/skip/inconclusive 0
- Unity 컴파일 오류: 0
- 전체 EditMode: `803 / 804` 통과, skip/inconclusive 0
  - 유일한 실패 `DungeonPanelAccessTests.ProductionPrefab_RootSizeRemains164x40`은 사용자 UI 리스케일 커밋 `5f46b99c`에서 `item_dungeonList`가 `78x20`으로 변경된 뒤 남은 기존 기대값이다. 이번 범위에서 프리팹이나 해당 테스트를 수정하지 않았다.
- `git diff --check`: 통과
- Generated 변경 범위: Item 정의 5개만 변경. World, Currency, Monster, Dungeon, Character, Skill, CharacterSkill과 ItemCatalog는 byte-identical이다.
- Item 5개와 ItemCatalog의 `.meta` 해시 및 GUID는 기준점과 동일하다.

전체 EditMode 실행 중 기존 테스트 경로가 실제 `persistentDataPath/playerprogress.json`을 한 차례 갱신했다. 실행 전 백업으로 즉시 복원했으며 최종 파일과 백업의 SHA-256은 모두 `8bbf5b957e12b581f3a685bfe9a6cc16fffeee5ef18646aa2f2713ea992916be`로 byte-identical임을 확인했다. 이후 보정 검증은 격리 클론의 집중 테스트만 실행해 실제 저장 파일을 변경하지 않았다.

## 남은 수동 검증과 8C

- 실제 Play Mode에서 Hover 진입·이탈 체감, 마지막 열/행 위치 전환, 긴 번역 높이 재계산을 확인한다.
- Windows 실제 클릭 관통은 Windows에서 확인한다.
- 후속 8C의 던전 보상 미리보기와 던전 귀환 결과 아이템은 동일한 `ItemTooltipController`와 `ItemTooltipView`를 재사용하되, 이번 단계에서는 연결하지 않았다.
