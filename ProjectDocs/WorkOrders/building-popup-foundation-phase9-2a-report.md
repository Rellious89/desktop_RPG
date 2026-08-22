# KeyBuddy 9.2A 완료 보고서

## 기준과 커밋

- 브랜치: `save-system`
- 시작 기준: `5dc4c5bf` (`Add building popup UI and localization`)
- 테이블 기반: `1191bf6c` (`Add building table runtime foundation`)
- 팝업·앵커 연결: `a182654f` (`Connect inn building popup and anchor UI`)
- 완료 보고서: 이 문서를 추가한 커밋
- 원격 푸시: 수행하지 않음

## Building 스키마와 검증

`Assets/TableData/Game/Building.csv`의 런타임 컬럼은 다음과 같다.

```text
building_id
name_category
name_key
function_category
function_key
build_time
cost_currency_id
cost_currency_amount
cost_item_ids
cost_item_counts
display_order
enabled
memo
```

`$building_name`, `$function_description`, `$build_time`, `$cost_currency`, `$cost_item`은 작업자 확인용이며 런타임 데이터로 읽지 않는다.

검증 규칙:

- Building ID는 필수이며 Ordinal 기준 중복을 허용하지 않는다.
- 활성 행의 건물 이름과 해금 기능 Localization 참조는 필수이며 실제 Shared/String Table 키의 존재를 확인한다.
- 번역 내용이나 영어·한국어 문자열 동일 여부는 검사하지 않는다.
- `build_time`은 0 이상의 정수다.
- 재화 ID와 수량은 함께 입력하며, 지정한 재화는 Currency 테이블의 활성 행이어야 하고 수량은 0 이상이다.
- 아이템 ID와 수량 목록은 함께 입력하며 길이가 같아야 한다. 아이템은 Item 테이블의 활성 행이어야 하고 수량은 1 이상이다.
- 아이템 비용 두 목록이 모두 비어 있는 행은 정상이다.
- `enabled=1`인 행만 카탈로그에 넣고 `display_order`, `building_id` 순으로 결정적으로 정렬한다.
- Building-only Rebuild도 전체 아홉 테이블을 검증하되, Building 출력 폴더만 쓴다. Currency와 Item 생성 에셋은 참조 연결을 위해 읽기만 한다.

## 런타임·생성 구조

추가한 런타임 경계:

- `BuildingDefinition`: ID, 두 Localization 참조, 건축 시간, 재화 비용, 아이템 비용, 표시 순서, 활성 여부를 보관한다.
- `BuildingCatalog`: 활성 건물을 결정적 순서로 제공하고 ID 조회를 지원한다.
- `BuildingDefinition.ToCostRequest()`: 정적 비용을 기존 `InventoryCostRequest`로 변환하기만 한다. 비용 판정·차감·저장·알림은 수행하지 않는다.

생성 결과:

- `Assets/Generated/TableData/Building/Building_1.asset`
  - GUID `1e0e90600454446e8980563a44db5bef`
  - 여관, 건축 시간 60초, `jewel` 2,000, 아이템 비용 없음, 표시 순서 10, 활성
- `Assets/Generated/TableData/Building/BuildingCatalog.asset`
  - GUID `d0ec875d301594c0cb66130069403d35`

## 팝업 데이터 조립

`BuildingPopupPanel`은 기존 `ModalPanel`을 상속하고 하나의 `BuildingDefinition`을 바인딩한다.

- 건물 이름: `BuildingDefinition.LocalizedName` (`07_Building / 1`)
- 해금 기능: `BuildingDefinition.LocalizedFunctionName` (`01_UI / 1001`)
- 설명 형식: `01_UI / 40`
- 재화 이름: 연결된 `CurrencyDefinition.LocalizedName`
- 시간: 누적 `HH:mm:ss`; 60초는 `00:01:00`, 90,000초는 `25:00:00`
- 비용: InvariantCulture 천 단위 형식과 현지화된 이름을 조합하여 `2,000 주얼` 형태로 표시

네 Localization 참조를 각각 구독하므로 열린 상태에서 Locale이 바뀌면 다시 조립한다. 닫기, 비활성화, 재바인딩, 파괴 시 구독을 대칭 해제한다. 코드에는 `해금 기능`, `소요 시간`, `비용`, `주얼` 같은 표시 문자열을 하드코딩하지 않았다.

비용 포매터는 여러 비용 조각을 순서대로 조립할 수 있다. 현재 생산 데이터에는 아이템 비용이 없으므로 팝업은 재화 조각만 채운다.

`btn_confirm`은 보이지만 런타임에서 항상 `interactable=false`이며 리스너가 없다. 비용 판정·차감, 건축 시작, 저장, 토스트는 발생하지 않는다. `btn_cancle`과 ESC는 기존 `ModalPanel.Close()` 경로로 닫는다.

## 월드 앵커와 필드 정책

`TownBuildingInteractionController`를 항상 활성인 `FieldSystem`에 연결했다. 핵심 참조는 모두 Inspector에 직렬화했으며 이름 검색에 의존하지 않는다.

`LateUpdate`에서 다음 순서로 위치를 계산한다.

```text
InnSlot/UIAnchor 월드 좌표
→ Stage 카메라 WorldToScreenPoint
→ Canvas 렌더 모드에 맞는 이벤트 카메라 선택
→ RectTransformUtility.ScreenPointToLocalPointInRectangle
→ btn_Build_Inn anchoredPosition
```

현재 Screen Space Overlay Canvas에는 변환 카메라로 `null`을 사용한다. 카메라 뒤, 화면 밖, Town 이외 모드, 필드 전환 연출 중에는 상호작용 UI를 숨긴다. Town 이탈 또는 전환 시작 시 열린 건축 팝업은 정상 `Close()` 경로로 닫는다. Town 복귀와 전환 완료 뒤에는 현재 앵커 위치로 다시 표시한다.

`btn_Open_Inn`은 계속 비활성이다. `btn_Build_Inn` 클릭은 `Building_1` 바인딩과 팝업 열기만 수행한다.

## 변경 파일

테이블·런타임 기반:

- `Assets/Editor/TableData/TableDataCsvReader.cs`
- `Assets/Editor/TableData/TableDataMenu.cs`
- `Assets/Editor/TableData/TableDataPaths.cs`
- `Assets/Editor/TableData/TableDataRebuilder.cs`
- `Assets/Editor/TableData/TableDataRows.cs`
- `Assets/Editor/TableData/TableDataValidator.cs`
- `Assets/Editor/TableData/Tests/BuildingTableTests.cs` 및 `.meta`
- `Assets/Editor/TableData/Tests/BuildingTableOutputTests.cs` 및 `.meta`
- `Assets/Editor/TableData/Tests/CharacterTableOutputTests.cs`
- `Assets/Editor/TableData/Tests/ItemTableTests.cs`
- `Assets/Scripts/Building.meta`
- `Assets/Scripts/Building/BuildingDefinition.cs` 및 `.meta`
- `Assets/Scripts/Building/BuildingCatalog.cs` 및 `.meta`
- `Assets/Generated/TableData/Building.meta`
- `Assets/Generated/TableData/Building/Building_1.asset` 및 `.meta`
- `Assets/Generated/TableData/Building/BuildingCatalog.asset` 및 `.meta`

팝업·앵커 연결:

- `Assets/Scripts/Building/BuildingInfoFormatter.cs` 및 `.meta`
- `Assets/Scripts/Building/UI.meta`
- `Assets/Scripts/Building/UI/BuildingPopupPanel.cs` 및 `.meta`
- `Assets/Scripts/Building/UI/TownBuildingInteractionController.cs` 및 `.meta`
- `Assets/Editor/Building.meta`
- `Assets/Editor/Building/Tests.meta`
- `Assets/Editor/Building/Tests/EditModeLifecycle.cs` 및 `.meta`
- `Assets/Editor/Building/Tests/BuildingInfoFormatterTests.cs` 및 `.meta`
- `Assets/Editor/Building/Tests/BuildingPopupPanelTests.cs` 및 `.meta`
- `Assets/Editor/Building/Tests/TownBuildingInteractionTests.cs` 및 `.meta`
- `Assets/Art/UI/Prefab/dialog/dialog_BuildingPopup.prefab`
- `Assets/Scenes/desktopScene_ReSize.unity`

사용자가 작성한 `Building.csv`와 Localization 에셋은 읽기만 했고 수정하지 않았다.

## 검증 결과

- Building TableData 집중 테스트: 60/60 통과
- 팝업·포매터·앵커·씬 스모크 집중 테스트: 65/65 통과
- 마지막 통합 집중 실행: 125/125 통과, 실패·스킵·inconclusive 0
- Unity 2022.3.62f3 컴파일 오류 0
- 대상 씬 EditMode 로드 스모크 통과
- `git diff --check` 통과
- `SaveData.CurrentSaveVersion`: 2 유지
- Building-only 공식 Rebuild: 정의 1개와 카탈로그 1개 생성
- 기준 보호 목록 241개 비교: 의도한 팝업 프리팹과 대상 씬만 변경, 나머지 239개 동일
- 기존 Building 이외 Generated 파일 96개 byte-identical
- 프리팹과 씬 diff는 컴포넌트 및 직렬화 참조 추가뿐이며 기존 RectTransform, 스프라이트, 폰트, 재질, 색상, 레이아웃, 활성 상태를 변경하지 않음

### 저장 경로 안전 기록

검증 중 격리 프로젝트에서 지시 범위를 벗어난 전체 EditMode 실행이 한 차례 수행되었다. Unity의 `Application.persistentDataPath`가 프로젝트 복제 위치와 무관하게 같은 사용자 경로를 사용해 실제 `playerprogress.json`과 `.bak`이 안전 저장 경로에 의해 갱신되는 문제가 확인되었다.

즉시 추가 Unity 실행을 중단하고 다음과 같이 복구했다.

- 주 파일: 실행 직전 메타데이터 보존 복사본으로 복구
  - MD5 `ff3fd837be24deaed2ea9f73f6629252`
  - SHA-256 `fabf6b1c76d1c1786df1c6456273459c7b740fe8486415a66891919c24b7876a`
  - mtime `1787375776` 복구
- `.bak`: 실행 전에 출력·확인한 원문으로 복구
  - SHA-256 `925f3eefcd9cd329f9a2c4fb06a344dde278a10c3bb4e5d413ef7f193d1fc602`
  - 내부 `lastSavedAtUtc`에 대응하는 mtime `1787373823`으로 복구

그 뒤 실제 두 파일을 별도 안전 복사하고 Building 관련 125개만 다시 실행했다. 실행 전후 두 파일의 SHA-256과 mtime이 모두 동일했다. 대상 씬 PlayMode는 실행하지 않았다. 전체 스위트가 실제 저장 경로에 접근하는 기존 검증 결함은 별도 후속 정리 대상이며, 이번 단계의 통과 근거로 사용하지 않는다.

## 보류 사항과 다음 단계

- 영어 번역 품질은 수정하거나 검사하지 않았다.
- 건축물 아이콘 컬럼·카탈로그를 추가하지 않았고 현재 임시 아이콘을 유지했다.
- Windows 네이티브 클릭 관통과 실제 화면 위치는 macOS EditMode에서 완전히 확인할 수 없어 수동 검증 항목으로 남긴다.
- 다음 9.2B는 `BuildingDefinition.ToCostRequest()`를 이용한 실시간 비용 충족 판정, 확인 버튼 상태, 부족 토스트, 원자적 차감, 건축 시작 알림, 진행 상태·완료 시각 저장 기반을 연결한다.
