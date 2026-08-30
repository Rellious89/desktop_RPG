# KeyBuddy Localization CSV 일괄 갱신 Editor Tool 보고서

## 결과

- 메뉴 경로: `Tools > Localize Update`
- 작업 브랜치/HEAD 시작점: `save-system` / `b98897a7`
- 원격 푸시: 수행하지 않음.
- 실제 `persistentDataPath`: 접근하거나 사용하지 않음.
- SaveData: `CurrentSaveVersion = 8` 유지.

## 구현 내용

- `TableData/Localization`의 `*.csv`를 파일명 순서로 동적 탐색하고, CSV 파일명과 동일한 `StringTableCollection`을 찾는다.
- Scan은 CSV와 Localization 에셋을 변경하지 않는 비교만 수행한다. 신규 Key, 기존 Key의 en/ko-KR 값 변경, CSV에 없는 에셋 전용 Key를 각각 집계한다.
- Unity 기본 CSV mapping이 만드는 `English(en)`/`Korean (South Korea)(ko-KR)` locale 헤더를 사용한다. `Key`, 선택 `Id`, 제공된 en/ko-KR 열만 허용하며 중복 Key/명시 Id, 잘못된 헤더/CSV, 지원 외 locale, Collection 부재, 기존 Key의 Id 불일치를 차단한다.
- `Update Selected`는 모든 선택 대상을 먼저 재비교한다. CSV 해시 또는 비교 결과가 stale이면 어떤 테이블도 적용하지 않고 재스캔을 요구한다.
- 반영은 `UnityEditor.Localization.Plugins.CSV.Csv.ImportInto`와 기본 `ColumnMapping`을 사용한다. `createUndo: true`, 하나의 Undo 그룹, `removeMissingEntries: false`로 실행해 CSV 누락 Key를 보존한다. 반영 후 SaveAssets/Refresh와 자동 재스캔을 수행한다.
- CSV 원본을 만들거나 수정하지 않는다.

## 수정 파일

- `Assets/Editor/Localization/LocalizationBulkUpdateService.cs`
- `Assets/Editor/Localization/LocalizationBulkUpdateWindow.cs`
- `Assets/Editor/Localization/Tests/LocalizationBulkUpdateServiceTests.cs`

## 검증

- Unity 2022.3.62f3 전용 EditMode 테스트: 5/5 통과.
  - 변경 없는 CSV의 신규/변경 0
  - 신규 Key와 기존 locale 변경의 Merge, SharedData ID 및 Collection GUID 유지
  - CSV 누락 Key 보존
  - 중복 Key, 중복 헤더, 지원 외 locale, Collection 부재 차단
  - 선택 해제 테이블 미변경
  - 스캔 후 CSV 변경 시 stale 차단
- Unity C# 컴파일 오류: 0.
- `git diff --check`: 통과.
- 테스트는 `Assets/__LocalizationBulkUpdateTests/<guid>`와 OS 임시 CSV에서 실행하고 TearDown으로 정리했다. 실제 `Assets/Localization/Tables` 및 `TableData/Localization` 데이터는 변경하지 않았다.

## 수동 확인

1. Unity에서 `Tools > Localize Update`를 연다.
2. **Scan**을 눌러 테이블별 신규/변경/에셋 전용 수와 오류 상태를 확인한다.
3. 필요한 행만 체크한 뒤 **Update Selected**를 누른다.
4. 완료 후 자동 Scan 결과와 Console 요약을 확인하고, 필요하면 Unity Undo로 한 묶음의 반영을 되돌린다.
