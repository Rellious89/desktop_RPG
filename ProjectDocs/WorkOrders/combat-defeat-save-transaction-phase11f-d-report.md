# 11F-D — 전투 처치 단일 저장 트랜잭션

## 구현 커밋

- `28e9fec1 Route defeat effects through one save transaction`

## 변경 내용

- `DefeatRewardDistributor`가 `MonsterEncounterQueue.MonsterDefeated`의 단일 조정자가 되었다. 처치 직전의 캐릭터와 던전을 한 번 캡처하고, 보상·킬/경험치·행동력·오염도를 메모리에 적용한 뒤 `SaveSystem.Save()`를 정확히 한 번 호출한다.
- 저장 성공 뒤에만 인벤토리/보상 알림, 성장 알림, 캐릭터 상태 알림과 행동력 소진 자동 교체를 수행한다.
- 저장 실패·예외에서는 오염도, 행동력, 진행도/표시 캐시, 인벤토리와 저장 메타데이터를 역순으로 복구한다. 성공 토스트와 자동 교체는 발생하지 않는다.
- `DungeonSessionTracker`는 더 이상 `MonsterDefeated`를 직접 구독하거나 오염도를 저장하지 않는다. 조정자가 넘긴 불변 캐릭터 ID로 원장을 기록하며, 실제 보상은 저장 성공 후 `RewardApplied`에서 기록한다.
- `PlayerProgress`와 `CharacterRoster`의 처치용 `AnyTargetDefeated` 직접 저장 구독을 제거했다. 오디오·콤보·표시 등 다른 소비자와 이벤트 자체는 유지했다.
- `desktopScene_ReSize`의 `DefeatRewardDistributor`에 Inventory, PlayerProgress, CharacterRoster, Tracker, FieldModeManager, Character/Corruption 카탈로그를 Inspector 참조로 연결했다.

## 검증

- `CombatDefeatTransactionTests`: 2/2 통과
  - 유효 처치 1회의 재화·킬·EXP·행동력·소수 오염도 동시 반영 및 저장 1회
  - `AnyTargetDefeated` 이후 중복 반영 없음
  - 저장 실패 시 값·알림 전체 롤백
- `DefeatRewardTests`: 25/25 통과
- `DungeonSessionTrackerTests`: 52/52 통과
- `DungeonCorruptionSettlementServiceTests`: 14/14 통과
- Unity C# 컴파일 오류 0, `git diff --check` 통과.

## 범위 확인

- SaveData는 v6 유지.
- CSV, Generated, Localization, 프리팹은 변경하지 않았다.
- 씬 변경은 `desktopScene_ReSize.unity`의 명시적 참조 연결만 포함한다.
- 실제 `persistentDataPath`는 사용하지 않았고, 원격 푸시는 수행하지 않았다.
