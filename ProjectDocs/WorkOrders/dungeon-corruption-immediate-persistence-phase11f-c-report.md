# 11F-C — 처치 즉시 던전 오염도 반영 및 영속화 보고서

## 커밋

- `875114a5 Persist dungeon corruption on each defeat`

## 구현 결과

- `DungeonSessionTracker.HandleMonsterDefeated()`는 유효한 처치를 결과 원장에 기록한 뒤, 자동 캐릭터 교체 전의 현재 캐릭터 ID에 오염도를 즉시 적용한다.
- `DungeonCorruptionSettlementService`는 단일 처치용 `TryApplyDefeat()` API로 전환했다. `corruption_gain_per_defeat`의 `double` 값을 그대로 더하고, 기본 오염도 하한 및 최대 오염도 상한을 적용한다.
- 실제 오염도 변화가 있을 때만 처치당 `SaveSystem.Save()`를 정확히 한 번 호출한다. 증가량 0, 미보유/알 수 없는 캐릭터, 최대 오염도 상태에서는 저장하지 않는다.
- 저장 실패 또는 예외에서는 해당 처치의 오염도와 SaveData 메타데이터를 롤백한다. Tracker는 저장 예외를 기록하지만 전투·보상 흐름을 중단하지 않는다.
- 마을 귀환 시 캐릭터별 처치 원장으로 오염도를 다시 정산하던 경로를 제거했다. 귀환은 세션 스냅샷, 결과 시간·총 처치·보상 원장, FIFO, `SessionCompleted`만 유지하므로 중복 오염도 증가나 추가 저장이 없다.

## 수정 파일

- `Assets/Scripts/Dungeon/DungeonCorruptionSettlementService.cs`
- `Assets/Scripts/Dungeon/DungeonSessionTracker.cs`
- `Assets/Editor/Dungeon/Tests/DungeonCorruptionSettlementServiceTests.cs`

## 검증

- 집중 EditMode:
  - `DungeonCorruptionSettlementServiceTests`: 13/13 통과
  - `DungeonSessionTrackerTests`: 52/52 통과
- Unity C# 컴파일 오류: 0
- `git diff --check`: 통과
- SaveData v6 유지. SaveData 필드·버전·마이그레이션 변경 없음.
- CSV·Generated·씬·프리팹·Localization 변경 없음.

## 안전 및 작업 상태

- 실제 `persistentDataPath`에는 접근하지 않았다.
- 원격 푸시는 수행하지 않았다.
- 이 보고서 커밋 전 작업 트리는 구현 커밋 기준 clean이었다.
