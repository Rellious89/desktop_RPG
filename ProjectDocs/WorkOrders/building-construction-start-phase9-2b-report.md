# KeyBuddy 9.2B 완료 보고서

## 기준과 커밋

- 브랜치: `save-system`
- 시작 기준: `e42746e5` (`Document building popup foundation`)
- 비용 판정·팝오버 위치: `e27c76c9` (`Add building affordability warning and popover placement`)
- 건축 시작 저장 연결: `7493e72d` (`Add persisted building construction start`)
- 완료 보고서: 이 문서를 추가한 커밋
- 구현 모델: Claude Opus 4.6 사용 확인
- 원격 푸시: 수행하지 않음

## 비용 상태와 팝오버

`BuildingPopupPanel`은 팝업 개방, Definition 재바인딩, `InventoryChanged`, 확인 직전에
`BuildingDefinition.ToCostRequest()`와 `InventoryManager.EvaluateCost()`로 비용을 다시 판정한다.

- 모든 비용 충족: `lb_warningMSG` 비활성, `btn_confirm` 활성
- 재화·아이템 부족 또는 잘못된 비용: 경고 활성, 확인 비활성
- 판정은 저장·차감·건축 상태 생성을 수행하지 않는다.
- 저장 실패처럼 잔액과 무관한 시작 실패도 팝업을 유지하고 경고와 비활성 확인 버튼으로 표시한다.
- 닫을 때 경고는 초기 상태로 정리된다.

사용자 선행 작업인 `lb_warningMSG`, `01_UI / 41`, 폰트·프리팹·씬 변경을 첫 구현 커밋에 보존했다.
경고 Text의 기본 상태는 비활성이고 Raycast Target은 꺼져 있다.

팝업 루트는 이동하지 않고 `dialog_BuildingPopup/bg`만 이동한다. 후보 순서는 버튼 바깥의
오른쪽 위, 왼쪽 위, 오른쪽 아래, 왼쪽 아래이며 마지막에 Canvas 경계와 여백으로 제한한다.
비용 상태와 레이아웃을 먼저 확정한 뒤 실제 크기로 배치한다. 열린 동안 버튼 또는 Canvas 크기가
달라지면 `LateUpdate`에서 다시 계산하므로 월드 앵커와 창 크기 변화를 따라간다.

## 저장 구조와 버전

`SaveData`에 다음 목록을 추가했다.

```text
buildingConstructions: List<BuildingConstructionSaveState>

BuildingConstructionSaveState
- buildingId
- startedAtUtc
- completeAtUtc
```

- `SaveData.CurrentSaveVersion`은 `2`로 유지한다.
- 예전 저장의 누락 필드는 빈 목록으로 정규화한다.
- 목록 내부 null 항목만 제거한다.
- 알 수 없는 Building ID, 중복 ID, 기존 순서는 보존한다.
- 조회는 목록이나 항목을 만들지 않는다.
- 마이그레이션 작업 사본은 목록과 세 필드를 깊게 복사한다.
- 두 시각은 기존 `SaveData.TimestampFormat`(`o`, UTC)을 사용한다.
- 완료 여부와 진행률은 저장하지 않는다. 완료 시각 비교와 후속 처리는 9.2C 범위다.

## 원자적 건축 시작

`BuildingConstructionService`는 UI와 분리된 순수 C# 경계이며 저장 문서, 저장 함수, UTC 시계를
주입받는다. Building ID 비교는 Ordinal 완전 일치다.

시작 흐름은 다음과 같다.

```text
정의·ID 확인
→ 중복 및 재진입 차단
→ 비용 권위 재판정
→ 비용을 메모리에서만 차감
→ 시작·완료 UTC 건축 기록 추가
→ 비용과 기록이 함께 있는 SaveData를 한 번 저장
→ 성공 후 InventoryChanged 1회와 ConstructionStarted 1회
```

- 비용이 0이어도 건축 기록을 남기기 위해 저장 1회, `InventoryChanged` 1회가 발생한다.
- 빠른 다중 클릭과 시작 이벤트 내부 재진입은 비용 차감 전에 차단한다.
- 기존 기록이 있으면 완료 시각이 지났더라도 다시 시작하지 않는다.
- 성공 후 팝업을 정상 `Close()` 경로로 닫고 `btn_Build_Inn`을 숨긴다.
- 재실행 후 저장 기록이 있으면 건축 버튼을 다시 표시하지 않는다.
- `btn_Open_Inn`과 `InnVisual`은 변경하지 않는다.

### 저장 실패 복구

`InventoryManager.TrySpendCost()`는 이제 저장 실패를 성공으로 반환하지 않는다.
`InventoryCostFailureReason.SaveFailed`를 반환하고 `InventoryChanged` 및 `RewardApplied`를 발생시키지 않는다.

복합 트랜잭션용 `TrySpendCostWithoutSave()`는 차감 전 재화와 아이템 목록의 정확한 스냅샷을 담은
`InventoryCostReceipt`를 반환한다. 실패 시 다음을 원래 상태로 복구한다.

- 재화
- 아이템 목록 순서
- null 및 중복 항목
- Item ID와 수량
- 기존 `InventoryItemState` 객체 identity
- 임시 건축 기록

따라서 전량 소비로 목록에서 제거됐던 아이템도 저장 실패 시 원래 위치와 객체로 복원된다.
성공한 저장 한 번 안에는 차감된 비용과 건축 기록이 함께 존재한다.

## 알림과 화면 상태

- 건축 시작 성공 시에만 `01_UI / 42`로 토스트를 한 번 요청한다.
- Key 42 한국어: `건설을 시작했습니다.`
- 영어 번역은 보류 정책에 따라 같은 한국어 임시값을 사용한다.
- 표시 문구를 코드에 하드코딩하지 않았다.
- 비용 부족 토스트는 추가하지 않았다.
- Dungeon 전환 또는 필드 전환 연출 중에는 팝업과 건축 상호작용 UI를 닫는다.

## 변경 파일

런타임·저장:

- `Assets/Scripts/Common/SaveData.cs`
- `Assets/Scripts/Common/SaveDataNormalizer.cs`
- `Assets/Scripts/Common/SaveMigrationRunner.cs`
- `Assets/Scripts/Inventory/InventoryCost.cs`
- `Assets/Scripts/Inventory/InventoryManager.cs`
- `Assets/Scripts/Building/BuildingConstructionService.cs` 및 `.meta`
- `Assets/Scripts/Building/UI/BuildingPopupPanel.cs`
- `Assets/Scripts/Building/UI/BuildingPopupPlacement.cs` 및 `.meta`
- `Assets/Scripts/Building/UI/TownBuildingInteractionController.cs`
- `Assets/Scripts/Common/LayoutModeController.cs` (macOS 격리 컴파일용 기존 플랫폼 호출 가드)

에셋·Localization·씬:

- `Assets/Art/UI/Prefab/dialog/dialog_BuildingPopup.prefab`
- `TableData/Localization/01_UI.csv`
- `Assets/Localization/Tables/01_UI/01_UI Shared Data.asset`
- `Assets/Localization/Tables/01_UI/01_UI_en.asset`
- `Assets/Localization/Tables/01_UI/01_UI_ko-KR.asset`
- `Assets/Fonts/Mulmaru/Mulmaru SDF.asset`
- `Assets/Scenes/desktopScene_ReSize.unity`

테스트:

- `Assets/Editor/Building/Tests/BuildingPopupPlacementTests.cs` 및 `.meta`
- `Assets/Editor/Building/Tests/BuildingConstructionServiceTests.cs` 및 `.meta`
- `Assets/Editor/Building/Tests/BuildingConstructionFlowTests.cs` 및 `.meta`
- `Assets/Editor/Common/Tests/BuildingConstructionSaveTests.cs` 및 `.meta`
- `Assets/Editor/Building/Tests/BuildingPopupPanelTests.cs`
- `Assets/Editor/Building/Tests/TownBuildingInteractionTests.cs`
- `Assets/Editor/Common/Tests/SaveMigrationTests.cs`
- `Assets/Editor/Inventory/Tests/InventoryCostTests.cs`
- `Assets/Editor/TableData/Tests/BuildingTableTests.cs`

첫 커밋에는 사용자의 선행 프리팹·폰트·씬·Localization 변경이 함께 포함되어 있다. 이를 되돌리거나
재생성하지 않았다. 두 번째 커밋의 대상 씬 변경은 `InventoryManager`와 `01_UI / 42` 직렬화 참조
13줄 추가뿐이며 시각·레이아웃 값은 변경하지 않았다.

## 검증 결과

- 격리 프로젝트 집중 EditMode: 216/216 통과
- 실패 0, 스킵 0, inconclusive 0
- Unity 2022.3.62f3 컴파일 오류 0
- 대상 씬 EditMode 로드 및 Inspector 배선 스모크 통과
- 비용 1,999 / 2,000 / 2,190, 아이템 부족, 실시간 갱신 검증 통과
- 팝오버 네 후보, Canvas 경계, 경고 높이, 버튼 이동 추적 검증 통과
- 단일 저장, zero-cost, 중복·재진입, 저장 실패 완전 롤백 검증 통과
- 예전 v2 저장, v0→v1→v2, 깊은 복사, 미래 버전 보호 검증 통과
- `git diff --check` 통과
- 작업 트리 clean
- Sol High 통합 읽기 전용 관문: PASS, 차단 결함 없음

전체 EditMode와 대상 씬 PlayMode는 지시대로 실행하지 않았다. 테스트는 회사명 `RellTestGuard`,
제품명 `KeyBuddy_92B_CheckpointB`인 임시 복제 프로젝트에서 실행했다. 실제 저장 파일은 실행 전후
다음 값이 동일했다.

- `playerprogress.json`: SHA-256 `d96de95999c8fccd8f5800da6e2b5f3d88ac0b702f7a35aebca7ae7307700ba3`, mtime `1787384959`
- `playerprogress.json.bak`: SHA-256 `b8710f3bd3eea1221d3b1e63a9809aecde9927ea98f545d12641afe83b1a67d2`, mtime `1787384835`

## 보류 사항과 9.2C 경계

수동 확인 항목:

- 실제 창 크기별 오른쪽/왼쪽/위/아래 팝오버 위치
- 부족 경고 활성 시 실제 레이아웃 높이
- 성공 직후 건축 버튼 숨김과 Key 42 토스트 1회
- Windows 네이티브 클릭 관통

9.2C에서 다음을 구현한다.

- 현재 시각 기반 건축 완료 판정
- 건축 완료 연출과 토스트
- `InnVisual` 완료 상태 반영
- `btn_Open_Inn` 활성화
- 용병 모집 기능 해금 경계

건축 진행률 UI, 취소·환불, 건축 완료 처리, 여관 기능, 영어 번역, 건축물 아이콘은 이번 단계에서
구현하지 않았다.
