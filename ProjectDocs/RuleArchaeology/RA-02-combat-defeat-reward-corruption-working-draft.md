# Rule Archaeology Working Draft — RA-02 Combat / Defeat / Reward / Corruption

> 정식 KeyBuddy Rule Registry나 DesignRule이 아닌 조사 기록이다. 현재 코드·테스트·데이터를 변경하지 않는다.

## 조사 상태

| 항목 | 값 |
| --- | --- |
| Area ID | RA-02 |
| Base commit | `7f47233b952c37f2907aa365abe84de5b83ad809` |
| 시작 시 HEAD | `7f47233b952c37f2907aa365abe84de5b83ad809` (base와 동일) |
| Status | Complete |
| 완료 checkpoint | RA-02-A, RA-02-B, RA-02-C |
| 다음 재개 위치 | 사용자 검토 후, RA-03 또는 RA-02 재검토 |

## 근거 표기

- `T`: 현재 EditMode 테스트
- `C`: 현재 런타임/Editor 구현
- `D`: 현재 데이터·설정
- `W`: 현재 WorkOrder
- `G`: Git history

## RA-02-A — 전투 입력부터 공격 판정

### 조사 완료 범위

- Global keyboard input, PlayerCharacterAnimator, AttackMotionDefinition, AttackHitCue
- ComboManager, TargetCombatController, Target, MonsterEncounterQueue
- PlayerCharacterAnimator/AutoAttackSkillRuntime/Defeat 관련 테스트와 attack animation DesignRule/WorkOrder

### 발견 Rule

#### RA02-A-001 — 새 공격 시작은 전투 허용·공격 가능한 Current Target·행동 가능 캐릭터가 동시에 있을 때만 가능하다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 새 키 입력은 `combatEnabled`, `Target.HasAttackableTarget`, `CharacterRoster.CurrentCharacterCanAct`가 모두 참일 때만 공격으로 등록된다. 마을/전환 중, target 부재·퇴장·대기 중, 행동 불가 상태에서는 새 공격 세션이나 대기열이 생기지 않는다. 이미 진행 중인 cycle의 Recovery는 즉시 자르지 않고 마무리한다.
- C: `Assets/Scripts/Character/PlayerCharacterAnimator.cs:38-42,182-189,537-552,612-630`.
- T: `Assets/Editor/Character/Tests/PlayerCharacterAnimatorTests.cs`의 cancellation/attack session 시나리오.
- W: `ProjectDocs/DesignRules/attack-animation-rules.md`; `ProjectDocs/WorkOrders/party-stamina-auto-switch-phase10f-report.md`.
- 현재 영향 범위: Desktop keyboard input, field transition, target lifecycle, stamina/party state.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — 공격 시작 gate 회귀 검증.
- Skill 후보: 아니오.

#### RA02-A-002 — 공격 모션은 cycle 시작 시 한 번 결정되며 Direct와 Accumulated 입력은 서로 다른 입력-타격 계약을 가진다

- Status: CONFIRMED
- Priority: Structural
- 규칙: Direct Input은 입력 1회와 hit 1회를 pending queue로 대응시키며, Accumulated Input은 required input에 도달할 때 한 번 strike한다. 현재 cycle의 모션은 charging/windup/recovery 중 교체하지 않으며, combo tier 변경은 다음 cycle부터 반영한다.
- C: `Assets/Scripts/Character/PlayerCharacterAnimator.cs:15-32,44-56,699-757,759-795`; `Assets/Scripts/Character/AttackMotionDefinition.cs`.
- T: `PlayerCharacterAnimatorTests.cs`; `AutoAttackSkillRuntimeTests.cs`의 일반 공격 fallback 및 공통 strike 경로.
- W: `ProjectDocs/DesignRules/attack-animation-rules.md`; `ProjectDocs/WorkOrders/keybuddy-13a-skill-execution-data-report.md`.
- 현재 영향 범위: 모든 attack motion asset, combo, queued input, skill motion 재생.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — motion 입력모드/필수 frame 계약.
- Skill 후보: 아니오.

#### RA02-A-003 — 자동 공격 스킬은 기존 공격 cycle을 대체하지 않고, 준비된 경우에만 모션 선택 우선권을 갖는다

- Status: CONFIRMED
- Priority: Structural
- 규칙: 새 cycle에서 현재 캐릭터의 준비된 attack-motion skill을 먼저 선택한다. 후보가 없거나 유효하지 않거나 cooldown이면 기존 combo-tier 일반 공격 풀로 fallback한다. 스킬은 기존 Cast/Hit/피해/보상 경로를 공유하며 별도 전투 저장 경로를 만들지 않는다. cooldown은 모션이 실제 active 상태에 들어간 뒤에만 소비하며 프로세스 실행 세션 한정이다.
- C: `Assets/Scripts/Character/PlayerCharacterAnimator.cs:633-683,699-722`; `Assets/Scripts/Skill/AutoAttackSkillRuntime.cs`.
- T: `Assets/Editor/Skill/Tests/AutoAttackSkillRuntimeTests.cs`; `Assets/Editor/Character/Tests/PlayerCharacterAnimatorTests.cs`.
- W: `ProjectDocs/WorkOrders/keybuddy-13c-auto-attack-skill-runtime-report.md`.
- 현재 영향 범위: Skill/CharacterSkill generated data, CharacterRoster current character, attack animation.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — playable skill relation/direct motion/cooldown contract.
- Skill 후보: 아니오.

#### RA02-A-004 — 타격의 공통 외부 경계는 HitPoint 한 번이며, Cast cue는 Hit보다 먼저 발생한다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 공격 인스턴스의 Strike는 `PlayerCharacterAnimator.HitPoint`를 정확히 한 번 발행한다. Cast와 Hit frame이 같아도 Cast cue가 먼저 처리되며, 스킬도 이 공통 Strike/HitPoint 경로를 사용한다.
- C: `Assets/Scripts/Character/PlayerCharacterAnimator.cs:72-84,751-755,767-775`; `Assets/Scripts/Character/AttackHitCue.cs:5-40`.
- T: `Assets/Editor/Character/Tests/PlayerCharacterAnimatorTests.cs`의 `Strike_EmitsHitPointExactlyOnce`; AutoAttackSkillRuntime tests.
- W: `ProjectDocs/DesignRules/attack-animation-rules.md`; `keybuddy-13c-auto-attack-skill-runtime-report.md`.
- 현재 영향 범위: target damage, hit/cast sound, FX, projectile, combo 및 defeat event downstream.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — hit frame/cast frame 및 one-HitPoint 회귀 검증.
- Skill 후보: 아니오.

#### RA02-A-005 — HitPoint를 받는 Target 중 Current 역할의 살아 있는 target만 피해·피격 연출을 적용한다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 정적 HitPoint 이벤트를 수신해도 `TargetEngagementRole.Current`가 아니거나 이미 defeated인 target은 피해·reaction·damage number·effect를 전혀 처리하지 않는다. Current target에 대해서는 damage → hit reaction → damage number → hit effect 순서를 유지하며, 이번 타격으로 defeat가 발생해도 해당 타격의 presentation을 중간에 덮어쓰지 않는다.
- C: `Assets/Scripts/Enemy/TargetCombatController.cs:491-530`.
- T: `Assets/Editor/Inventory/Tests/DefeatRewardTests.cs`; `Assets/Editor/Character/Tests/PlayerCharacterAnimatorTests.cs`의 hit event 경로.
- W: `ProjectDocs/WorkOrders/combat-fx-and-stage-layout-session-report.md`.
- 현재 영향 범위: two-slot monster queue, target lifecycle, visual/audio feedback, defeat 발행 전 피해 처리.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — standby/exiting target damage exclusion 및 hit order 회귀 검증.
- Skill 후보: 아니오.

#### RA02-A-006 — MonsterDefeated는 처치 순간 동기 이벤트이고, 리젠/퇴장 처리 및 AnyTargetDefeated와 역할이 다르다

- Status: CONFIRMED
- Priority: Structural
- 규칙: MonsterEncounterQueue는 target defeat 처리 중 `MonsterDefeated`를 동기 발행한다. 이는 reward transaction의 입력이며 `Target.AnyTargetDefeated`를 대체하지 않는다. spawn/role promotion/exit visual 및 later respawn 경로에서는 reward/exp/kill/stamina 이벤트를 중복 발행하지 않는다.
- C: `Assets/Scripts/Enemy/MonsterEncounterQueue.cs:80-125,812-852,920-960`.
- T: `Assets/Editor/Dungeon/Tests/DungeonSessionTrackerTests.cs`; `Assets/Editor/Inventory/Tests/CombatDefeatTransactionTests.cs`.
- W: `ProjectDocs/WorkOrders/combat-defeat-save-transaction-phase11f-d-report.md`.
- 현재 영향 범위: monster queue lifecycle, reward transaction, dungeon session ledger.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — defeat event single-emission/role lifecycle regression.
- Skill 후보: 아니오.

### 미확인 연결점

- DefeatRewardDistributor가 MonsterDefeated 이후 어떤 상태를 어느 순서로 저장·알림하는지.
- corruption gain, purification lifecycle 및 dungeon result ledger가 동일 defeat를 어떻게 분담하는지.

## RA-02-B — Defeat transaction과 보상

### 조사 완료 범위

- `DefeatRewardDistributor`, `InventoryManager`, `PlayerProgress`, `CharacterRoster`, `DungeonCorruptionSettlementService`
- Monster reward/character/dungeon/corruption 설정과 defeat transaction·reward 테스트
- `combat-defeat-save-transaction-phase11f-d-report.md`

### 발견 Rule

#### RA02-B-001 — `MonsterDefeated` 한 건의 경제·진행 변경은 DefeatRewardDistributor가 조정하는 하나의 저장 트랜잭션이다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 처치 보상, currency, kill count, XP, stamina, corruption 및 해당 quest 변경은 `MonsterDefeated`에서 `DefeatRewardDistributor`가 수집·적용한다. 변경이 하나라도 있으면 외부 저장은 정확히 한 번이며, 개별 하위 시스템은 이 경로에서 저장하지 않는다.
- C: `Assets/Scripts/Inventory/DefeatRewardDistributor.cs:204-292`; `Assets/Scripts/Inventory/InventoryManager.cs:810-822`; `Assets/Scripts/Common/PlayerProgress.cs:273-313`; `Assets/Scripts/Character/CharacterRoster.cs:842-867`.
- T: `Assets/Editor/Inventory/Tests/CombatDefeatTransactionTests.cs:41-97`의 `Defeat_ChangesAllValues_AndSavesExactlyOnce`.
- W: `ProjectDocs/WorkOrders/combat-defeat-save-transaction-phase11f-d-report.md`.
- 현재 영향 범위: monster defeat, inventory, progression, roster, dungeon corruption, quest, save notification.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — defeat 한 건의 Save 횟수와 하위 시스템 무저장 계약.
- Skill 후보: 아니오.

#### RA02-B-002 — 처치 보상은 정의된 currency와 최대 3개 item slot의 단일 누적 추첨으로 결정되고, item 결과는 최대 한 종류다

- Status: CONFIRMED
- Priority: Production
- 규칙: 유효하게 연결된 currency definition만 reward 대상이며, 고정/범위 금액은 item 추첨과 독립적이다. item은 앞의 3 slot만 순서대로 누적 확률 구간으로 사용하고, 0–9999 단일 난수 추첨에서 하나만 선택하거나 miss한다.
- C: `Assets/Scripts/Inventory/DefeatRewardDistributor.cs:294-389`; `Assets/Scripts/Dungeon/MonsterDefinition.cs`.
- T: `Assets/Editor/Inventory/Tests/DefeatRewardTests.cs:55-166,182-425`의 cumulative boundary, fourth-entry exclusion, one random call, at-most-one item, currency independence 시나리오.
- D: `Assets/Generated/TableData/`의 monster/item/currency generated assets.
- 현재 영향 범위: monster table data, currency/item catalog, reward toast 및 inventory delta.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — slot 수, 누적 chance 범위, currency reference 무결성.
- Skill 후보: 아니오.

#### RA02-B-003 — 처치 공로와 상태 변경의 귀속 캐릭터는 자동 교체 전에 캡처한 current character다

- Status: CONFIRMED
- Priority: Critical
- 규칙: Defeat transaction은 mutation 전에 current character ID를 고정한다. XP, stamina cost, corruption gain, quest 및 session defeat record는 이 ID에 귀속하며, stamina 소진에 따른 자동 교체는 저장 성공 후 notification 단계에서만 실행된다.
- C: `Assets/Scripts/Inventory/DefeatRewardDistributor.cs:214-258`; `Assets/Scripts/Character/CharacterRoster.cs:842-879`; `Assets/Scripts/Dungeon/DungeonSessionTracker.cs:259-271`.
- T: `Assets/Editor/Inventory/Tests/CombatDefeatTransactionTests.cs:41-97`; `Assets/Editor/Dungeon/Tests/DungeonSessionTrackerTests.cs:294-365,457-478`.
- W: `ProjectDocs/WorkOrders/combat-defeat-save-transaction-phase11f-d-report.md`.
- 현재 영향 범위: party auto-switch, EXP, stamina, corruption, quest, dungeon result attribution.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — stamina depletion과 character attribution 회귀 검증.
- Skill 후보: 아니오.

#### RA02-B-004 — 실패한 defeat 저장은 성공 알림을 내보내지 않고, 이미 적용한 모든 변경을 transaction 이전 상태로 되돌린다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 단일 외부 저장이 false를 반환하거나 예외를 던지면 inventory reward, progress, stamina, corruption, quest 및 Save metadata를 rollback한다. 이 경우 reward/progress/stamina notification, auto-switch, toast는 성공으로 관찰되지 않는다. 변경이 전혀 없으면 저장과 reward toast도 발생하지 않는다.
- C: `Assets/Scripts/Inventory/DefeatRewardDistributor.cs:250-292`; `Assets/Scripts/Dungeon/DungeonCorruptionSettlementService.cs:89-151`.
- T: `Assets/Editor/Inventory/Tests/CombatDefeatTransactionTests.cs:73-97`; `Assets/Editor/Inventory/Tests/DefeatRewardTests.cs:413-425,682-786`; `Assets/Editor/Dungeon/Tests/DungeonCorruptionSettlementServiceTests.cs:77-145`.
- W: `ProjectDocs/WorkOrders/combat-defeat-save-transaction-phase11f-d-report.md`.
- 현재 영향 범위: save failure handling, user-visible reward/progression state, all defeat mutation receipts.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — forced save failure rollback/no-notification regression.
- Skill 후보: 아니오.

### checkpoint 상태 — RA-02-B

| 항목 | 값 |
| --- | --- |
| Status | Complete |
| 조사 완료 범위 | Defeat event부터 reward/progress/stamina/corruption/quest mutation 및 external save/rollback까지 |
| 조사한 주요 근거 | CombatDefeatTransactionTests, DefeatRewardTests, DefeatRewardDistributor와 하위 receipt 구현, phase11f-d WorkOrder |
| 미확인 연결점 | purification의 시간 정산과 dungeon session result snapshot의 수명·저장 여부 |
| 다음 재개 위치 | RA-02-C Corruption / Purification / Dungeon Session Ledger |

## RA-02-C — Corruption / Purification / Dungeon Session Ledger

### 조사 완료 범위

- `DungeonCorruptionSettlementService`, `CorruptionStaminaCostPolicy`, `PurificationService`
- `DungeonSessionTracker`, `DungeonSessionLedger`, corruption/purification/session tests 및 현재 generated configs
- immediate corruption 및 session-ledger WorkOrder와 이전 town-settlement WorkOrder 비교

### 발견 Rule

#### RA02-C-001 — 유효한 처치는 귀속 캐릭터의 corruption을 dungeon 설정량만큼 즉시 누적하고, base floor와 configured maximum 사이로 제한한다

- Status: CONFIRMED
- Priority: Critical
- 규칙: defeat transaction의 captured character에 대해서만 `DungeonCorruptionSettlementService`가 corruption gain을 적용한다. gain은 dungeon의 defeat당 설정값으로 계산하고, 저장값은 character base corruption보다 낮아지지 않으며 corruption config maximum을 넘지 않는다. 이 mutation은 RA02-B의 단일 저장에 포함된다.
- C: `Assets/Scripts/Dungeon/DungeonCorruptionSettlementService.cs:89-151`; `Assets/Scripts/Inventory/DefeatRewardDistributor.cs:223-258`.
- T: `Assets/Editor/Dungeon/Tests/DungeonCorruptionSettlementServiceTests.cs:43-149`; `CombatDefeatTransactionTests.cs:41-97`.
- D: `Assets/Generated/TableData/CorruptionConfig/CorruptionConfig_default.asset` (max 300); dungeon generated assets.
- W: `ProjectDocs/WorkOrders/combat-defeat-save-transaction-phase11f-d-report.md`; `dungeon-corruption-immediate-persistence-phase11f-c-report.md`.
- 현재 영향 범위: defeat transaction, character save state, stamina multiplier, purification eligibility.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — config reference, gain/base/max clamp 및 one-save integration.
- Skill 후보: 아니오.

#### RA02-C-002 — corruption은 저장하지 않는 순수 정책으로 전투 stamina 비용 배수를 계산하며, current/base/config 상태가 입력이다

- Status: CONFIRMED
- Priority: Structural
- 규칙: base stamina cost가 양수일 때, 유효 config의 warning/danger threshold 이상이면 해당 configured multiplier를 적용한다. 현재 corruption이 비정상이거나 base보다 낮으면 base가 유효 하한이고, config가 없거나 invalid면 1배로 안전하게 fallback하며 곱셈 overflow는 최대 int로 포화된다.
- C: `Assets/Scripts/Corruption/CorruptionStaminaCostPolicy.cs:7-51`.
- T: `Assets/Editor/Corruption/Tests/CorruptionStaminaCostPolicyTests.cs:33-72`.
- D: `Assets/Generated/TableData/CorruptionConfig/CorruptionConfig_default.asset` (50%/80%, 2x/3x).
- 현재 영향 범위: character stamina spending, combat eligibility, corruption configuration.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — threshold order/config validity/multiplier bounds.
- Skill 후보: 아니오.

#### RA02-C-003 — purification은 slot·party·corruption 시간 정산을 하나의 저장 트랜잭션으로 취급하고, 실패 시 전부 복구한다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 정화 등록은 enabled·valid config, 완공된 required building, owned character, base 초과 corruption, recovery/정화 중복 부재 및 최소 파티 인원을 만족해야 한다. 등록·중단·파티 이동·Tick 정산은 UTC timestamp와 interval 기반으로 slot/party/corruption을 한 번 저장하며, 실패 또는 예외 시 slot, party, corruption 및 metadata를 원상복구한다. 중단은 정산 후 slot만 비우고 자동으로 party에 복귀시키지 않는다.
- C: `Assets/Scripts/Corruption/PurificationService.cs:35-44,176-299,300-465`.
- T: `Assets/Editor/Corruption/Tests/PurificationServiceTests.cs:53-207`.
- D: `Assets/Generated/TableData/PurificationConfig/PurificationConfig_church_prayer.asset` (church prayer, required building 2, 60초/1 corruption, base slot 1).
- 현재 영향 범위: character party availability, recovery exclusion, corruption state, save metadata, church building.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — purification config/building/slot reference와 failure rollback.
- Skill 후보: 아니오.

#### RA02-C-004 — dungeon session ledger는 저장 데이터가 아닌 실행 중 결과 원장이며, 실제 reward와 transaction-captured defeat만 aligned dungeon session에 기록한다

- Status: CONFIRMED
- Priority: Structural
- 규칙: tracker는 `FieldModeChanged(Dungeon, valid dungeon)` 뒤에만 session을 시작하고, `Town` 전환에서 active session을 완료 snapshot으로 만든다. 다른/invalid dungeon, 모순된 town payload 또는 unsupported mode에서는 active session을 complete하지 않고 abandon한다. reward는 `InventoryManager.RewardApplied`의 실제 양수 delta만, defeat는 coordinator가 전달한 captured character ID만 기록한다. 원장은 SaveSystem 호출이나 persistent data path가 없으며, completed snapshot은 FIFO이고 consume하면 재소비되지 않는다.
- C: `Assets/Scripts/Dungeon/DungeonSessionTracker.cs:76-301`; `Assets/Scripts/Dungeon/DungeonSessionLedger.cs:120-365`.
- T: `Assets/Editor/Dungeon/Tests/DungeonSessionTrackerTests.cs:147-281,294-522,534-830`; `Assets/Editor/Dungeon/Tests/DungeonSessionLedgerTests.cs:226-778`, 특히 `NoPersistentDataPath`와 `NoSaveSystemCalls`.
- W: `ProjectDocs/WorkOrders/dungeon-session-ledger-foundation-report.md`.
- 현재 영향 범위: FieldMode lifecycle, defeat transaction notification, dungeon result UI, runtime scene lifecycle.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — scene tracker singleton/reference, FieldMode payload 및 snapshot FIFO contract.
- Skill 후보: 아니오.

#### RA02-C-005 — 과거의 town-return corruption settlement 설명은 현재 즉시 defeat 저장 구현에 대해 LEGACY다

- Status: LEGACY
- Priority: Production
- 규칙: `dungeon-corruption-settlement-phase11c-report.md`는 corruption을 town return/session settlement에 결부한 역사적 설명이다. 현재 구현·현재 tests·phase11f-d WorkOrder는 corruption을 defeat transaction에 즉시 포함하므로, 이전 문서만으로 현행 동작을 확정할 수 없다.
- C: `Assets/Scripts/Dungeon/DungeonCorruptionSettlementService.cs:89-151`; `Assets/Scripts/Inventory/DefeatRewardDistributor.cs:223-258`.
- T: `Assets/Editor/Dungeon/Tests/DungeonCorruptionSettlementServiceTests.cs:43-149`; `CombatDefeatTransactionTests.cs:41-97`.
- W: 현재 `combat-defeat-save-transaction-phase11f-d-report.md` 및 `dungeon-corruption-immediate-persistence-phase11f-c-report.md` 대 이전 `dungeon-corruption-settlement-phase11c-report.md`.
- 현재 영향 범위: future documentation reading and corruption persistence interpretation.
- 사용자 판단 필요: 아니오.
- Validator 후보: 아니오.
- Skill 후보: 아니오.

### checkpoint 상태 — RA-02-C

| 항목 | 값 |
| --- | --- |
| Status | Complete |
| 조사 완료 범위 | defeat corruption, stamina multiplier, purification lifecycle, runtime dungeon session ledger |
| 조사한 주요 근거 | corruption/purification/session EditMode tests, current runtime code, current generated config, immediate persistence·session ledger WorkOrder, legacy settlement WorkOrder |
| 미확인 연결점 | RA-03 party/recruitment progression의 일반 stamina·party mutation 경로; RA-05 dungeon/world access가 FieldMode payload를 만드는 경로; RA-09 building/recovery가 purification과 공유하는 transaction 경로 |
| 다음 재개 위치 | 사용자 검토 후 RA-03 또는 연결 영역의 cross-area 재검토 |

## RA-02 완료 요약

| 분류 | 수 |
| --- | ---: |
| 전체 Rule | 15 |
| CONFIRMED | 14 |
| INFERRED | 0 |
| CONFLICT | 0 |
| LEGACY | 1 |
| Critical | 8 |
| CONFLICT + Critical | 0 |
| INFERRED + Critical | 0 |

### 조사 중 발견했지만 수정하지 않은 사항

- RA-01에서 보류된 direct save failure 처리 불일치는 본 영역에서 바꾸지 않았다. defeat/purification은 별도의 rollback transaction을 사용하지만, 일반 direct save 경로의 결론을 대체하지 않는다.
- 과거 town-return corruption settlement WorkOrder는 현행 immediate defeat persistence와 다르므로 LEGACY로만 기록했다. 어떤 문서도 삭제하거나 갱신하지 않았다.
