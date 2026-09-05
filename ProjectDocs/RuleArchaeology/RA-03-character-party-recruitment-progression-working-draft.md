# Rule Archaeology Working Draft — RA-03 Character / Party / Recruitment / Progression

> 정식 KeyBuddy Rule Registry나 DesignRule이 아닌 조사 기록이다. 현재 코드·테스트·데이터를 변경하지 않는다.

## 조사 상태

| 항목 | 값 |
| --- | --- |
| Area ID | RA-03 |
| Base commit | `7f47233b952c37f2907aa365abe84de5b83ad809` |
| 시작 시 HEAD | `7f47233b952c37f2907aa365abe84de5b83ad809` (base와 동일) |
| Status | Complete |
| 완료 checkpoint | RA-03-A, RA-03-B, RA-03-C |
| 다음 재개 위치 | 사용자 검토 후 cross-area 재검토 또는 사용자가 지정한 다음 RA |

## 근거 표기

- `T`: 현재 EditMode 테스트
- `C`: 현재 런타임/Editor 구현
- `D`: 현재 데이터·설정
- `W`: 현재 WorkOrder
- `G`: Git history

## RA-03-A — Character identity / lifecycle / Party

### 조사 완료 범위

- `SaveData.characters`, `CharacterSaveState`, `CharacterDefinition`, `CharacterCatalog`, `OwnedCharacterCollection`, `CharacterRoster`
- `PartyCompositionService`, `PartySlotUtility`, Character Archive UI 및 current party 관련 테스트
- character ownership, fixed party slot, party composition WorkOrder와 generated character/party data

### 발견 Rule

#### RA03-A-001 — `SaveData.characters`의 유효한 `characterId` 항목 존재가 보유와 캐릭터별 영구 상태를 동시에 뜻한다

- Status: CONFIRMED
- Priority: Critical
- 규칙: `CharacterSaveState` 하나는 해당 `characterId`의 보유 사실과 level/currentExp/currentStamina/currentCorruption 등의 영구 상태를 함께 나타낸다. 조회·로스터 구성은 미보유 항목을 만들지 않으며, 현행 코드에는 보유 캐릭터를 제거하는 정식 획득 반대 경로가 없다. 카탈로그에 없는 저장 ID는 삭제하지 않고 보존하지만 현재 빌드의 usable character로는 노출하지 않는다.
- C: `Assets/Scripts/Common/SaveData.cs:60-98,465-496`; `Assets/Scripts/Character/OwnedCharacterCollection.cs:11-220`; `Assets/Scripts/Character/CharacterRoster.cs:430-597`.
- T: `Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs:374-455,603-681,976-1100`; `Assets/Editor/Common/Tests/PlayerProgressCharacterTests.cs:140-177`.
- W: `ProjectDocs/WorkOrders/character-ownership-save-v2-report.md`.
- 현재 영향 범위: save/load, character roster, recruitment, party eligibility, progression, recovery/purification/quest/skill ID relations.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — character state ID 중복·빈 값·catalog relation 및 party/recovery/purification cross-reference 검증.
- Skill 후보: 아니오.

#### RA03-A-002 — 새 게임의 initial ownership은 LoadStatus가 `NewGame`일 때만 seed되며, 진행 중인 문서에 소급 적용되지 않는다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 활성 catalog의 `InitiallyOwned` definition은 새 게임에서만 level 1, currentExp 0, stamina 미초기화(-1), base corruption 상태로 추가된다. Loaded/Migrated/복구·차단 상태와 빈 보유 목록은 initial grant 근거가 아니며, 로드 중 ownership을 되살리지 않는다.
- C: `Assets/Scripts/Character/OwnedCharacterCollection.cs:154-205`; `Assets/Scripts/Character/CharacterRoster.cs:243-290,537-562`; `Assets/Scripts/Character/CharacterDefinition.cs:145-155`.
- T: `Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs:603-654`; `CharacterRosterCatalogTests.cs:437-455`.
- D: `Assets/Generated/TableData/Character/` 및 Character Catalog의 `initiallyOwned` 설정.
- W: `ProjectDocs/WorkOrders/character-ownership-save-v2-report.md`; `ProjectDocs/WorkOrders/keybuddy-13e-save-reset-initial-character-recovery-report.md`.
- 현재 영향 범위: new game, reset/migration 이후 roster, initial stamina/corruption.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — NewGame 이외 load status에서 initial ownership 비생성 검증.
- Skill 후보: 아니오.

#### RA03-A-003 — active character definition과 saved ownership은 분리되며, 비활성/미해석 ID의 저장 상태는 삭제하지 않는다

- Status: CONFIRMED
- Priority: Structural
- 규칙: Character Catalog는 generated table의 enabled character definition만으로 현재 빌드의 active character universe를 구성하고, 빈/중복 ID는 catalog에서 제외한다. 반면 save에만 남은 ID는 normalizer/roster가 임의로 제거하거나 다시 active로 만들지 않는다. 따라서 해당 ID의 persistent state는 보존되지만 catalog definition이 없는 동안 roster/public state path에서 사용할 수 없다.
- C: `Assets/Scripts/Character/CharacterCatalog.cs:10-140`; `Assets/Scripts/Character/OwnedCharacterCollection.cs:11-37`; `Assets/Scripts/Common/SaveDataNormalizer.cs:23-48`; `Assets/Scripts/Character/CharacterRoster.cs:564-597`.
- T: `Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs:116-143,655-705,976-1100`.
- D: `Assets/Generated/TableData/Character/` 및 Character Catalog generated asset.
- W: `ProjectDocs/WorkOrders/character-ownership-save-v2-report.md`.
- 현재 영향 범위: table-data enable/disable, forward/backward compatibility, roster visibility, save preservation.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — catalog enabled/unique ID와 save-only ID preservation/visibility boundary.
- Skill 후보: 아니오.

#### RA03-A-004 — Party는 보유·성장과 분리된 persistent fixed-slot 출전 편성이며, 슬롯 순서는 게임 규칙이다

- Status: CONFIRMED
- Priority: Critical
- 규칙: `partyCharacterIds`는 빈 칸을 `string.Empty`로 보존하는 고정 슬롯 목록이고, 파티의 capacity나 current character는 저장하지 않는다. party 순서는 usable roster의 열거 순서, 외부 party 변경 후 fallback selection, exhaustion auto-switch의 순환 순서를 결정하므로 단순 UI 정렬이 아니다. move는 target slot과 swap하고 내부 빈 칸을 압축하지 않는다.
- C: `Assets/Scripts/Common/SaveData.cs:92-98`; `Assets/Scripts/Party/PartySlotUtility.cs`; `Assets/Scripts/Party/PartyCompositionService.cs:124-204`; `Assets/Scripts/Character/CharacterRoster.cs:430-480,368-402,673-682`.
- T: `Assets/Editor/Party/Tests/PartyCompositionServiceTests.cs:77-192`; `Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs:71-185`.
- W: `ProjectDocs/WorkOrders/fixed-party-slots-phase10e-report.md`; `ProjectDocs/WorkOrders/party-composition-service-phase10b-report.md`.
- 현재 영향 범위: party archive, active combat roster, auto-switch, passive recovery, purification/recovery eligibility.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — capacity 범위, empty-slot preservation, duplicate/ownership/recovery/purification exclusion, auto-switch order.
- Skill 후보: 아니오.

#### RA03-A-005 — PartyCompositionService는 최소 1명·configured capacity·보유 및 격리 상태를 검증하고, 변경은 단일 저장 rollback transaction이다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 빈 party에는 보유 캐릭터 한 명을 join할 수 있으나 마지막 한 명은 leave할 수 없다. join/replace/move는 configured capacity와 unique owned ID를 요구하며, recovery 또는 purification slot에 있는 캐릭터는 party mutation에서 제외한다. party 목록 변경은 성공한 외부 save 한 번으로 확정되고 false/예외면 party와 save metadata를 원상복구한다.
- C: `Assets/Scripts/Party/PartyCompositionService.cs:62-299`.
- T: `Assets/Editor/Party/Tests/PartyCompositionServiceTests.cs:77-249`; `Assets/Editor/Corruption/Tests/PurificationServiceTests.cs:206-212`.
- D: `Assets/Generated/TableData/PartyConfig/`의 default party config.
- W: `ProjectDocs/WorkOrders/party-composition-service-phase10b-report.md`; `ProjectDocs/WorkOrders/party-config-table-foundation-phase10a-1-report.md`.
- 현재 영향 범위: Character Archive drag/drop, recovery/purification transfer, persistent party save data.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — party data validity, atomic save failure rollback, minimum/capacity guards.
- Skill 후보: 아니오.

### checkpoint 상태 — RA-03-A

| 항목 | 값 |
| --- | --- |
| Status | Complete |
| 조사 완료 범위 | ownership/persistent state, catalog resolution, initial grant, fixed party slots and party transaction |
| 조사한 주요 근거 | CharacterRosterCatalogTests, PartyCompositionServiceTests, current SaveData/roster/party code, generated config, ownership·party WorkOrder |
| 미확인 연결점 | current character 전환의 persistence/mode boundary 및 recruitment/progression 사용처 |
| 다음 재개 위치 | RA-03-B Character switching / Recruitment |

## RA-03-B — Character switching / Recruitment / Unlock

### 조사 완료 범위

- `CharacterRoster`, `CharacterRuntimeActor`, `CharacterSwapPanel`, Character Archive refresh path
- recruitment cycle/draw/selector/unlock/resolution services, generated acquisition data 및 recruitment tests

### 발견 Rule

#### RA03-B-001 — Current는 저장되는 대표자나 Party slot이 아니라, runtime actor가 지금 연기하는 한 명의 전투 캐릭터다

- Status: CONFIRMED
- Priority: Structural
- 규칙: `CharacterRoster.Current`만 현행 current/active combat character를 의미하며 별도 persistent current/leader/representative 필드는 없다. 수동 교체는 `TrySwitchTo`가 canonical usable definition을 단일 `CharacterRuntimeActor`에 적용한 뒤에만 Current를 옮기고 combo를 reset하며 `CurrentCharacterChanged`를 발행한다. 이 경로는 `partyCharacterIds`를 바꾸거나 Save를 호출하지 않는다.
- C: `Assets/Scripts/Character/CharacterRoster.cs:10-37,780-834,1010-1052`; `Assets/Scripts/Character/CharacterRuntimeActor.cs:25-112`; `Assets/Scripts/Common/CharacterSwap/CharacterSwapPanel.cs:15-20,228-282`; `Assets/Scripts/Common/SaveData.cs:92-98`.
- T: `Assets/Editor/Common/Tests/PlayerProgressCharacterTests.cs:401-540`; `Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs:458-528`.
- W: `ProjectDocs/WorkOrders/character-swap-panel-report.md`; `ProjectDocs/WorkOrders/character-archive-party-runtime-phase10ce-report.md`.
- 현재 영향 범위: attack input/animation, progression display and attribution, character swap UI, runtime presentation.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — Current/RuntimeActor atomicity, no Save/no party mutation on manual switch, canonical ID resolution.
- Skill 후보: 아니오.

#### RA03-B-002 — 수동 교체 대상은 현재 출전 파티의 usable·보유 캐릭터여야 하며, 회복 중·stamina 0·현재 자신은 교체할 수 없다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 교체 권한은 catalog definition만이 아니라 party 순서 ∩ 보유 state ∩ playable motion profile로 만든 usable roster에 한정된다. runtime actor가 없거나, 이미 current이거나, recovery slot에 있거나, stamina가 0이면 거부된다. 모든 출전 파티원이 recovery 중이면 Current는 null이고 공격도 불가하다.
- C: `Assets/Scripts/Character/CharacterRoster.cs:210-290,430-597,646-682,788-830`.
- T: `Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs:97-115,491-588,789-945`.
- W: `ProjectDocs/WorkOrders/character-swap-panel-report.md`; `ProjectDocs/WorkOrders/keybuddy-13e-save-reset-initial-character-recovery-report.md`.
- 현재 영향 범위: swap panel, recovery, combat input gate, runtime actor scene wiring.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — current eligibility and no-active-character combat gate.
- Skill 후보: 아니오.

#### RA03-B-003 — 현재 전환은 Party 편성을 바꾸지 않는 기존의 runtime-only 경계이며, 현행 authorization에는 FieldMode/전투 중 차단이 없다

- Status: CONFIRMED
- Priority: Structural
- 규칙: `TrySwitchTo`와 swap panel의 실제 authorization path는 FieldMode/Town/Dungeon을 조회하지 않는다. 따라서 UI가 접근 가능한 상태에서는 전투 중에도 existing current character를 바꾸는 별도 runtime-only 경로가 존재한다. 공격 중 교체 시 runtime actor가 이전 animation/movement를 정리하도록 설계되어 있고, current 전환은 party 저장 상태를 변경하지 않는다. Character Archive의 party 편성 변경 또한 service 자체에는 FieldMode guard가 없다.
- C: `Assets/Scripts/Character/CharacterRoster.cs:10-25,817-834`; `Assets/Scripts/Common/CharacterSwap/CharacterSwapPanel.cs:228-282`; `Assets/Scripts/Party/PartyCompositionService.cs:62-299`; `Assets/Scripts/CharacterArchive/CharacterArchivePanel.cs:282-361`.
- T: `Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs:529-588`; `Assets/Editor/Common/Tests/PlayerProgressCharacterTests.cs:401-420`.
- W: `ProjectDocs/WorkOrders/character-swap-panel-report.md`.
- 현재 영향 범위: combat runtime, player input, current-character progression attribution, party UI availability outside this area.
- 사용자 판단 필요: 아니오 — scene/menu visibility is a separate RA-05/RA-07 connection, but no contrary authorization rule was found here.
- Validator 후보: 예 — combat-time switch cleanup and FieldMode-independent authorization contract.
- Skill 후보: 아니오.

#### RA03-B-004 — stamina exhaustion 자동 교체는 persistent Party 슬롯 순환을 사용하되, defeat transaction의 성공 저장 뒤에만 발생한다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 현재 캐릭터의 defeat stamina가 양수에서 정확히 0이 되면, 해당 slot 다음부터 wrap-around하는 party slot 순서로 action 가능한 후보를 한 번 탐색한다. 이 auto-switch는 defeat receipt의 external save와 notification이 성공한 뒤에만 실행되며 party 목록 자체는 바꾸지 않는다. 대체할 후보가 없으면 exhausted Current가 유지된다.
- C: `Assets/Scripts/Character/CharacterRoster.cs:344-402,842-879`; `Assets/Scripts/Inventory/DefeatRewardDistributor.cs:223-292`.
- T: `Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs:176-343`; `Assets/Editor/Inventory/Tests/CombatDefeatTransactionTests.cs:41-97`.
- W: `ProjectDocs/WorkOrders/party-stamina-auto-switch-phase10f-report.md`; `ProjectDocs/WorkOrders/combat-defeat-save-transaction-phase11f-d-report.md`.
- 현재 영향 범위: defeat reward transaction, party ordering, current character and EXP/corruption attribution.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — slot wrap, single auto-switch, rollback/no auto-switch-on-failed-save.
- Skill 후보: 아니오.

#### RA03-B-005 — recruitment unlock은 영구 후보 자격이고, ownership/실제 영입과 분리되어 있다

- Status: CONFIRMED
- Priority: Structural
- 규칙: condition이 있는 enabled acquisition은 owned character count 또는 max owned character level 조건을 같은 group 안에서는 AND, group 간에는 OR로 평가한다. 최초 달성한 character ID는 `unlockedRecruitmentCharacterIds`에 저장되어 이후 조건이 후퇴해도 남는다. unlock은 ownership을 만들지 않으며, selector는 조건부 candidate가 이 영구 unlock 기록을 가질 때만 후보에 포함한다.
- C: `Assets/Scripts/Recruitment/RecruitmentUnlockService.cs:23-96`; `Assets/Scripts/Recruitment/RecruitmentCandidateSelector.cs:85-153`; `Assets/Scripts/Common/SaveData.cs:154-160`.
- T: `Assets/Editor/Recruitment/Tests/RecruitmentUnlockServiceTests.cs:74-153`; `Assets/Editor/Recruitment/Tests/RecruitmentCandidateDrawServiceTests.cs:57-230`.
- D: `Assets/TableData/Game/CharacterAcquisition.csv` 및 generated acquisition/unlock condition assets.
- W: `ProjectDocs/WorkOrders/keybuddy-character-unlock-v7-report.md`; `ProjectDocs/WorkOrders/recruitment-table-foundation-phase9-3a-report.md`.
- 현재 영향 범위: recruitment candidate eligibility, persistent unlock list, owned-character progression condition.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — acquisition/condition references, group semantics, unlock persistence/id uniqueness.
- Skill 후보: 아니오.

#### RA03-B-006 — 영입은 pending candidate를 새 character state로 바꾸는 한 번의 저장 transaction이며, 이미 보유한 후보를 다시 지급하지 않는다

- Status: CONFIRMED
- Priority: Critical
- 규칙: valid pending candidate는 catalog definition을 다시 해석한 뒤에만 영입한다. 성공 시 character state(level 1, exp 0, max stamina, base corruption)를 추가하고 pending ID를 비우며 character story quest activation도 같은 저장에 포함한다. save 실패/예외면 character list, pending ID, quest 및 metadata를 rollback한다. 이미 보유한 ID는 state를 추가하거나 pending을 소비하지 않는다.
- C: `Assets/Scripts/Recruitment/RecruitmentCandidateResolutionService.cs:69-274`; `Assets/Scripts/Recruitment/UI/RecruitmentUiController.cs:260-303`.
- T: `Assets/Editor/Recruitment/Tests/RecruitmentCandidateResolutionServiceTests.cs:63-203`; `Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs:131-143`.
- W: `ProjectDocs/WorkOrders/recruitment-candidate-resolution-phase9-3e-report.md`; `ProjectDocs/WorkOrders/recruitment-candidate-transaction-phase9-3c-report.md`.
- 현재 영향 범위: ownership, recruitment cycle pending state, initial progression, story quest initialization, roster refresh.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — pending/catalog/ownership reference and acquire rollback transaction.
- Skill 후보: 아니오.

#### RA03-B-007 — duplicate recruitment 허용 플래그의 selector 계약과 resolution 계약은 서로 맞지 않는다

- Status: CONFLICT
- Priority: Production
- 규칙: `AllowDuplicateRecruitment=true`인 acquisition은 selector에서 이미 보유한 ID도 eligible candidate로 허용한다. 그러나 resolution은 이미 보유한 pending candidate를 항상 `AlreadyOwned`로 거부하고 pending을 보존하므로, 해당 flag가 활성화되면 후보 선택과 실제 영입 결과가 양립하지 않는다. 현재 generated acquisition data는 모든 flag를 false로 두어 이 경로는 현행 데이터에서 도달하지 않는다.
- C: `Assets/Scripts/Recruitment/CharacterAcquisitionDefinition.cs:70-93`; `Assets/Scripts/Recruitment/RecruitmentCandidateSelector.cs:124-153`; `Assets/Scripts/Recruitment/RecruitmentCandidateResolutionService.cs:94-147`.
- T: `Assets/Editor/Recruitment/Tests/RecruitmentCandidateResolutionServiceTests.cs:85-99`; `Assets/Editor/TableData/Tests/RecruitmentTableTests.cs:217`.
- D: `Assets/Generated/TableData/CharacterAcquisition/*.asset`의 `allowDuplicateRecruitment: 0`.
- 현재 영향 범위: future acquisition data enabling duplicate recruitment; pending recruitment recovery.
- 사용자 판단 필요: 아니오 — 현재 데이터에서 비활성이고 Critical로 분류되지 않았으며, 수정·설계는 하지 않는다.
- Validator 후보: 예 — duplicate flag와 resolution capability compatibility.
- Skill 후보: 아니오.

### checkpoint 상태 — RA-03-B

| 항목 | 값 |
| --- | --- |
| Status | Complete |
| 조사 완료 범위 | current/runtime actor switching, party refresh/auto-switch, recruitment cycle candidate/unlock/acquire paths |
| 조사한 주요 근거 | CharacterRosterCatalogTests, PlayerProgressCharacterTests, recruitment tests, current switching/recruitment code, generated acquisition data, WorkOrders |
| 미확인 연결점 | character-level progression persistence/legacy account fields and cross-system ID linkage |
| 다음 재개 위치 | RA-03-C Character progression / data linkage |

## RA-03-C — Character Progression / data linkage

### 조사 완료 범위

- `CharacterProgressionService`, `PlayerProgress`, `CharacterSkillUnlockService`, SaveData normalizer/migration copy path
- progression tests, character catalog tests, current cross-system character ID usages and progression WorkOrder

### 발견 Rule

#### RA03-C-001 — 경험치와 레벨은 현재 전투 중인 보유 캐릭터의 `CharacterSaveState`에만 쌓이고, total kill count만 계정 전역이다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 정상 defeat는 total kill count를 전역으로 올리고, exp는 defeat 시점의 current usable owned character에만 적용한다. 아무도 투입되지 않았거나 현재 state가 없어도 kill count는 증가하지만 캐릭터 state를 새로 만들지 않는다. current 전환 뒤의 이후 defeat는 새 Current에 귀속된다.
- C: `Assets/Scripts/Common/PlayerProgress.cs:220-355,455-462`; `Assets/Scripts/Character/CharacterRoster.cs:754-761`.
- T: `Assets/Editor/Common/Tests/PlayerProgressCharacterTests.cs:88-285,344-420,563-691`; `Assets/Editor/Inventory/Tests/CombatDefeatTransactionTests.cs:41-97`.
- W: `ProjectDocs/WorkOrders/character-progression-skill-unlock-report.md`; `ProjectDocs/WorkOrders/combat-defeat-save-transaction-phase11f-d-report.md`.
- 현재 영향 범위: defeat transaction, character swap, skill unlock, level UI, save/load.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — character-local EXP, global kill, unowned/current-null no-grant, capture-before-auto-switch integration.
- Skill 후보: 아니오.

#### RA03-C-002 — character progression은 고정 EXP-per-level, remainder carry, multi-level gain 및 저장 표현 한계 포화를 사용한다

- Status: CONFIRMED
- Priority: Structural
- 규칙: `CharacterProgressionService`는 level 1/exp 0부터의 총 진행량으로 계산하고 양수 EXP만 받아들인다. 필요한 EXP는 현재 모든 level에 고정이며 remainder는 이월되고 한 번에 여러 level이 가능하다. 기획상 max level은 없지만 `int.MaxValue`와 마지막 level의 `required-1` exp가 저장 표현 한계이며 넘는 요청은 실제 수용량만 반영한다. 0 이하 요청은 정규화도 mutation도 하지 않는다.
- C: `Assets/Scripts/Character/CharacterProgressionService.cs:75-292`.
- T: `Assets/Editor/Character/Tests/CharacterProgressionServiceTests.cs`; `Assets/Editor/Common/Tests/PlayerProgressCharacterTests.cs:302-344,563-638`.
- W: `ProjectDocs/WorkOrders/character-progression-skill-unlock-report.md`.
- 현재 영향 범위: per-character save state, EXP/level UI, character skill unlock threshold.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — positive-only grant, overflow saturation, currentExp range and level/skill threshold consistency.
- Skill 후보: 아니오.

#### RA03-C-003 — Character ID는 catalog-validated Ordinal key이며 persistent state와 주요 character-specific systems의 연결 키다

- Status: CONFIRMED
- Priority: Critical
- 규칙: active `CharacterCatalog`는 빈/중복 ID를 제외하고 Ordinal exact match로 canonical definition을 찾는다. 동일 ID의 수동 definition은 roster에서 canonical generated definition으로 해석될 수 있다. 동일 문자열은 SaveData character state, party slots, recruitment acquisition/pending/unlock, character skill relation/cooldown, recovery/purification, corruption, story quest의 연결 키로 사용된다. `CharacterDefinition`은 ID 변경이 기존 save 연결을 끊는다고 명시한다.
- C: `Assets/Scripts/Character/CharacterCatalog.cs:35-140`; `Assets/Scripts/Character/CharacterDefinition.cs:30-38`; `Assets/Scripts/Character/CharacterRoster.cs:564-631`; `Assets/Scripts/Skill/CharacterSkillUnlockService.cs:206-228`; `Assets/Scripts/Recruitment/RecruitmentCandidateResolutionService.cs:94-147`.
- T: `Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs:458-490,655-755,1068-1100`; `Assets/Editor/Skill/Tests/CharacterSkillUnlockTests.cs`.
- D: generated Character Catalog/Character/CharacterSkill/CharacterAcquisition assets.
- W: `ProjectDocs/WorkOrders/character-skill-table-foundation-report.md`; `ProjectDocs/WorkOrders/character-origin-world-foundation-phase9-3d-a-report.md`.
- 현재 영향 범위: all per-character data linkage; future character-specific data must use the existing exact ID relation rather than asset reference identity.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — cross-catalog Character ID uniqueness/reference integrity and unresolved-ID preservation.
- Skill 후보: 아니오.

#### RA03-C-004 — account-global `SaveData.currentLevel/currentExp`는 현행 character progression의 입력·출력이 아닌 보존 대상 legacy fields다

- Status: LEGACY
- Priority: Production
- 규칙: 현재 `PlayerProgress`는 legacy account-global level/exp를 읽거나 갱신하지 않고, character state만 성장시킨다. 현행 SaveData와 migration copy는 두 필드를 보존한다. 과거 WorkOrder 안의 “필드가 타입에서 사라졌다”라는 서술은 현재 SaveData와 맞지 않으므로 역사적 설명으로만 취급한다.
- C: `Assets/Scripts/Common/SaveData.cs:56-57`; `Assets/Scripts/Common/PlayerProgress.cs:10-39,220-355`; `Assets/Scripts/Common/SaveMigrationRunner.cs:238-239`.
- T: `Assets/Editor/Common/Tests/PlayerProgressCharacterTests.cs:344-382`.
- W: `ProjectDocs/WorkOrders/character-progression-skill-unlock-report.md:42-49,266,346`.
- 현재 영향 범위: save migration, progression data interpretation, future documentation reading.
- 사용자 판단 필요: 아니오.
- Validator 후보: 아니오.
- Skill 후보: 아니오.

### checkpoint 상태 — RA-03-C

| 항목 | 값 |
| --- | --- |
| Status | Complete |
| 조사 완료 범위 | per-character progression, global kill boundary, progression clamp, canonical ID linkage, legacy global progress fields |
| 조사한 주요 근거 | progression/roster/skill tests, current SaveData/progression/catalog code, generated data, current and legacy portions of progression WorkOrder |
| 미확인 연결점 | RA-05/RA-07이 control하는 Character Archive/swap UI의 visibility; RA-09 recovery/purification shared party transactions; no RA-03-only unresolved Critical link |
| 다음 재개 위치 | 사용자 검토 후 cross-area 재검토 또는 사용자가 지정한 다음 RA |

## 명시 확인 항목

| 항목 | 현행 결론 |
| --- | --- |
| A. Party 순서 | 예. fixed-slot 순서는 usable roster, fallback, auto-switch에 의미가 있다. |
| B. current/대표/활성 | `Current`만 runtime active combat character다. persistent representative/leader는 없고 Party는 별도의 persistent composition이다. |
| C. Party를 바꾸지 않는 일시 전환 | 예. `TrySwitchTo` → 단일 RuntimeActor 적용 → `Current` 전환은 party/save를 바꾸지 않는 기존 경계다. |
| D. 교체와 Save/Party | manual current switch는 save와 party mutation을 하지 않는다. party composition은 별도 single-save transaction이다. |
| E. 전투 중 교체 | authorization path에 FieldMode/전투 차단이 없으며 runtime actor는 공격 중 정리를 수행한다. UI 접근성은 별도 영역이다. |
| F. Party 0/1 | empty party는 join 가능; leave는 1명에서 차단된다. usable/current가 없으면 combat input은 차단된다. |
| G. Character ID 안정성 | 예. Ordinal exact persistent key이며 catalog validation과 다수 시스템 relation의 공통 키다. ID 변경은 save 연결을 끊는다. |
| H. Unlock/Recruitment/Progression 결합 | unlock condition은 owned count/max owned level을 읽고 persistent unlock을 만든다. selector가 이를 후보 자격으로 읽고 resolution이 실제 ownership/initial progression을 만든다. |

## RA-03 완료 요약

| 분류 | 수 |
| --- | ---: |
| 전체 Rule | 16 |
| CONFIRMED | 14 |
| INFERRED | 0 |
| CONFLICT | 1 |
| LEGACY | 1 |
| Critical | 9 |
| CONFLICT + Critical | 0 |
| INFERRED + Critical | 0 |

### 조사 중 발견했지만 수정하지 않은 사항

- `AllowDuplicateRecruitment=true`일 때 selector는 owned candidate를 허용하지만 resolution은 `AlreadyOwned`로 거부하는 비활성 CONFLICT를 기록했다. 현재 generated data는 모두 false이며 수정하지 않았다.
- account-global `currentLevel/currentExp`를 제거했다고 말하는 과거 WorkOrder 부분은 현재 SaveData 구현과 달라 LEGACY로만 기록했다.
- PartyCompositionService와 current switch authorization에는 FieldMode guard가 없음을 기록했으며, scene/menu visibility의 실제 제어는 RA-05/RA-07 범위로 남겼다.
