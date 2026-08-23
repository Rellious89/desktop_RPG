# KeyBuddy 10D — 파티·비파티 용병 자연 행동력 회복 보고서

## 커밋

- 10D-A: `551cd5d6af6d8cbc787e0e55eab67f487bad8add` — `Add passive stamina recovery foundation`
- 10D-B: `454e5b72828010677eea528241ff538c17feaeae` — `Connect passive stamina recovery runtime`

## 적용 내용

- 회복소의 `secondsPerStamina`를 100% 기준으로 사용한다.
  - 출전 파티원: 30% (`partyPassiveRecoveryEfficiencyPercent = 30`)
  - 보유 비파티원: 10% (`nonPartyPassiveRecoveryEfficiencyPercent = 10`)
  - 회복소 등록 캐릭터: 자연 회복 제외
- `CharacterSaveState`에 마지막 자연 회복 UTC와 정수 잔여 진행 분자를 추가했다. UTC ticks와 효율 백분율을 공통 분모로 계산하므로 부동소수점 누적 오차가 없다.
- 기준 시각이 없는 기존 캐릭터는 첫 Tick의 현재 UTC만 기록하며 과거 시간을 소급하지 않는다. 최대 행동력, 회복소 체류, 효율 0에서는 잔여 진행을 지워 시간 비축을 막는다. 시계가 과거로 이동하면 기준 시각을 뒤로 옮기지 않아 중복 회복하지 않는다.
- 저장 전용/카탈로그 미정의 캐릭터는 로스터의 유효 보유 목록에 없으므로 자연 회복이 건드리지 않으며, 저장 항목도 보존한다.

## 저장·이벤트 정책

- 한 Tick에서 여러 캐릭터가 회복되어도 자연 회복 저장 호출은 최대 한 번이다.
- 실제 행동력 증가가 없으면 저장하지 않는다. 기준 시각/잔여 진행은 메모리에 유지되어 다음 기존 저장 경로에 함께 보존된다.
- 저장 실패 또는 예외 시 그 Tick에서 바뀐 행동력, UTC 기준 시각, 잔여 진행을 모두 롤백하고 이벤트를 보내지 않는다.
- 저장 성공 뒤 증가한 캐릭터에만 `CharacterRoster.CharacterStateChanged`를 요청한다. 기존 HUD, 캐릭터 교체 목록, 회복소 목록, 용병 명부/파티 카드의 갱신 흐름을 그대로 재사용한다.
- `RecoveryService.OnEnable`은 기존 회복소 오프라인 Tick 뒤 자연 회복 Tick을 즉시 한 번 실행하고, 이후 기존 unscaled Tick 간격에서 두 계산을 순서대로 실행한다.

## 수정 파일

- `Assets/Scripts/Recovery/RecoveryBalance.cs`
- `Assets/Scripts/Recovery/RecoveryBalanceTable.cs`
- `Assets/Scripts/Recovery/PassiveStaminaRecoveryService.cs`
- `Assets/Scripts/Recovery/RecoveryService.cs`
- `Assets/Data/RecoveryStation/RecoveryBalanceTable.asset`
- `Assets/Scripts/Common/SaveData.cs`
- `Assets/Scripts/Common/SaveMigrationRunner.cs`
- `Assets/Editor/Recovery/Tests/PassiveStaminaRecoveryServiceTests.cs`
- `Assets/Editor/Common/Tests/SaveMigrationTests.cs`

## 집중 검증

- 10D-A: 자연 회복/깊은 복사 집중 EditMode 10건 통과.
- 10D-B: 런타임 진입점과 중복 Tick·회복소 퇴소 경계까지 포함한 집중 EditMode 12건 통과.
- Unity C# 컴파일 오류: 0.
- `git diff --check`: 통과.
- 전체 EditMode, PlayMode, Sol, 실제 `persistentDataPath` 검증은 실행하지 않았다.

## 범위 및 상태

- SaveData 버전은 v3를 유지했다. 마이그레이션 단계는 추가하지 않았고 깊은 복사만 새 필드에 맞춰 확장했다.
- 씬·프리팹 변경은 없다.
- 원격 푸시는 하지 않았다.

## 수동 확인 권장

1. 행동력이 부족한 파티원이 시간 경과 후 자연 회복되는지
2. 비파티 보유 용병이 파티원보다 느리게 회복되는지
3. 회복소 등록 캐릭터에게 자연 회복이 중복되지 않는지
4. 재실행 뒤 오프라인 경과가 반영되는지
5. 회복 직후 행동력 UI가 갱신되는지
