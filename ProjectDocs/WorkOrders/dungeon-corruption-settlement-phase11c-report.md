# KeyBuddy 11C — 던전 세션 파티 오염도 정산

## 커밋

- 참가자 스냅샷: `bc6e3470` — `Track dungeon corruption participants`
- 마을 귀환 정산: `77ee3ed3` — `Apply dungeon corruption on town return`

## 구현 결과

- 던전 입장 시 SaveData v4 고정 파티 슬롯의 점유 ID를 순서대로 복사한다. 빈 값과 null은 제외하고 Ordinal 중복은 첫 ID만 남긴다. 완료 스냅샷은 이 독립 복사본을 보존하며, 중복 입장 요청과 이후 파티 변경은 이를 바꾸지 않는다.
- 마을 복귀 시 총 오염도는 `floor(elapsedSeconds / corruptionIntervalSeconds) * corruptionGainPerInterval`로 계산한다. 유효하지 않은 시간은 0이고 산술 초과는 `long.MaxValue`로 포화한다.
- 참가자 수로 나눈 동일한 `double` 값을 모든 유효한 참가자에게 적용한다. 입장 뒤 파티에서 빠졌어도 저장 상태와 정의가 남아 있으면 정산하며, 삭제되었거나 정의가 없는 ID는 생성·재분배 없이 건너뛴다.
- 적용 전 값은 유한값으로 정리하고 Character BaseCorruption 하한 및 default CorruptionConfig MaxCorruption 상한을 적용한다.
- 여러 참가자 변경은 SaveSystem.Save() 한 번으로 기록한다. 저장 false 또는 예외에서는 참가자 오염도와 SaveData 메타데이터를 함께 롤백한다. Tracker는 예외를 오류로 기록하되 던전 완료와 결과 이벤트 흐름은 계속한다.
- DungeonSessionTracker는 이미 메모리에 로드된 SaveData만 읽어 세션 참가자를 캡처한다. 이 관찰 경로는 실제 persistentDataPath를 열지 않는다.
- 대상 씬의 기존 DungeonSessionTracker에 CharacterCatalog와 CorruptionConfigCatalog 참조만 연결했다. Tools > Reset의 캐릭터 읽기 전용 정보에 오염도 원시값을 표시한다.

## 수정 파일

- `Assets/Scripts/Dungeon/DungeonSessionLedger.cs`
- `Assets/Scripts/Dungeon/DungeonCorruptionSettlementService.cs`
- `Assets/Scripts/Dungeon/DungeonSessionTracker.cs`
- `Assets/Scripts/Common/SaveSystem.cs`
- `Assets/Editor/Dungeon/Tests/DungeonSessionLedgerTests.cs`
- `Assets/Editor/Dungeon/Tests/DungeonCorruptionSettlementServiceTests.cs`
- `Assets/Editor/Dungeon/Tests/DungeonSessionTrackerTests.cs`
- `Assets/Editor/Save/SaveResetWindow.cs`
- `Assets/Scenes/desktopScene_ReSize.unity`

## 집중 검증

- EditMode: `SaveMigrationTests`, `DungeonSessionLedgerTests`, `DungeonSessionTrackerTests`, `DungeonCorruptionSettlementServiceTests`
- 결과: **208/208 통과**, 실패 0
- Unity C# 컴파일 오류: **0** (집중 EditMode 실행 중 확인)
- `git diff --check`: 통과
- SaveData: **v5 유지**
- CSV, Generated 에셋, 프리팹: 변경 없음
- 씬: `desktopScene_ReSize.unity`의 기존 Tracker Catalog 참조 두 개만 변경
- 실제 persistentDataPath: 사용하지 않음
- 원격 푸시: 하지 않음
