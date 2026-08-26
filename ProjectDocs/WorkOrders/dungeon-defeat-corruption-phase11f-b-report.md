# 11F-B — 처치 기반 던전 오염도 누적 전환 보고서

## 커밋

- `e59ac65c Replace dungeon corruption interval with defeat gain`
- `1719caf9 Attribute dungeon corruption to defeating characters`

## 구현 결과

- `Dungeon.csv`의 사용자 작성 `corruption_gain_per_defeat` 값을 `double` 런타임 계약으로 전환했다. 빈 값, 음수, NaN, Infinity, 비숫자는 TableData 검증에서 거부하고 0 및 소수값은 허용한다.
- `DungeonDefinition`과 생성 Dungeon 에셋 9개를 처치당 오염도 필드로 갱신했다. Dungeon 전용 Rebuild 범위와 메뉴를 추가했으며, World/Monster/Item 생성 참조가 각각 하나인지 사전 확인한다.
- 세션 원장에 캐릭터별 처치 수를 추가했다. 총 처치 수, 경과시간, 보상, 참가자 스냅샷과 FIFO 계약은 유지된다.
- 몬스터 처치 이벤트에서 자동 캐릭터 교체 전의 `CharacterRoster.Instance.Current.CharacterId`를 함께 기록한다. 현재 캐릭터를 찾지 못해도 총 처치 및 보상 원장은 유지된다.
- 귀환 정산은 체류시간·참가자 수를 사용하지 않고 캐릭터별 `처치 수 × 처치당 오염도`만 적용한다. 변경된 캐릭터가 여러 명이어도 저장은 한 번이며, 실패·예외에서는 오염도와 저장 메타데이터를 롤백한다.

## 변경 파일

- 런타임: `DungeonDefinition`, `DungeonSessionLedger`, `DungeonSessionTracker`, `DungeonCorruptionSettlementService`
- TableData: CSV 계약/검증/행 모델/Rebuild 메뉴 및 Dungeon 전용 Rebuild 범위
- 생성 에셋: `Assets/Generated/TableData/Dungeon/Dungeon_1.asset` ~ `Dungeon_9.asset`만 갱신
- 집중 테스트: Dungeon 테이블, 세션 원장, 오염도 정산 테스트

## 검증

- Dungeon 전용 Rebuild: 오류 0건, Dungeon 에셋 9개와 DungeonCatalog만 갱신.
- 집중 EditMode:
  - `DungeonCorruptionSettlementServiceTests`: 13/13 통과
  - `DungeonSessionLedgerTests`: 55/55 통과
  - `DungeonTableTests`: 20/20 통과
- Unity C# 컴파일 오류: 0
- `git diff --check`: 통과
- SaveData: v6 유지. SaveData 필드·마이그레이션 변경 없음.
- 씬·프리팹·Localization 변경 없음. Dungeon 외 Generated 도메인 변경 없음.

## 안전 및 작업 상태

- 실제 `persistentDataPath`에는 접근하지 않았다.
- 원격 푸시는 수행하지 않았다.
- 이 보고서 커밋 전 작업 트리는 구현 커밋 기준 clean이었다.

## 수동 확인 항목

1. 아무 몬스터도 처치하지 않고 던전에서 귀환하면 오염도가 변하지 않는지 확인한다.
2. 몬스터를 처치한 현재 전투 캐릭터만 오염도가 증가하는지 확인한다.
3. 행동력 소진으로 자동 교체되기 직전 처치가 이전 캐릭터에게 귀속되는지 확인한다.
4. 던전 결과의 시간·총 처치·보상 표시가 기존과 동일한지 확인한다.
