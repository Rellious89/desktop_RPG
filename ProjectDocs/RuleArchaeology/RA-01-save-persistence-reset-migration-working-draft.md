# Rule Archaeology Working Draft — RA-01 Save / Persistence / Reset / Migration

> 이 문서는 정식 KeyBuddy Rule Registry나 DesignRule이 아니다. 현재 구현에 적용된 계약을 근거 기반으로 역산한 임시 조사 기록이며, 코드·데이터·Prefab의 기준을 변경하지 않는다.

## 조사 상태

| 항목 | 값 |
| --- | --- |
| Area ID | RA-01 |
| Base commit | `7f47233b952c37f2907aa365abe84de5b83ad809` |
| 시작 시 HEAD | `7f47233b952c37f2907aa365abe84de5b83ad809` (base와 동일) |
| 시작 시 작업 트리 | clean |
| Status | Complete |
| 완료 checkpoint | RA-01-A, RA-01-B, RA-01-C |
| 다음 재개 위치 | RA-02 (새 조사 영역) 또는 RA-01 재검토: `7f47233b` 이후 Save 관련 변경 발생 시 diff부터 확인 |

## 근거 표기

- `T`: 현재 EditMode 테스트
- `C`: 현재 런타임/Editor 구현
- `D`: 현재 데이터·설정
- `W`: 현재 WorkOrder
- `G`: Git history

## RA-01-A — Save 데이터 모델과 저장 위치

### 조사 완료 범위

- `SaveData`, `SaveSystem`, `SaveStorage`, `SaveProfile`
- `SaveDataNormalizer`, `SaveMigrationRunner`, `SaveVersionProbe`, `SaveLoadResult`
- Save migration/storage/integration/isolation 테스트의 공개 계약 목록
- Save migration 및 최근 reset/isolation WorkOrder

### 발견 Rule

#### RA01-A-001 — 프로세스 전체 진행 저장 문서는 하나다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 진행 저장은 `SaveSystem.Data`의 단일 공유 `SaveData` 인스턴스를 고쳐 쓴 뒤 `SaveSystem.Save()`로 기록한다. 개별 시스템이 별도 `SaveData`를 만들거나 일부 필드만 담은 문서를 저장하는 방식은 현재 계약에 맞지 않는다.
- C: `Assets/Scripts/Common/SaveSystem.cs:27-31,115-123`; `Assets/Scripts/Common/SaveData.cs:8-15`.
- T: `Assets/Editor/Common/Tests/SaveSystemIntegrationTests.cs`의 공개 API/직접 문서 주입 계약.
- W: `ProjectDocs/WorkOrders/save-version-migration-foundation-report.md` 3절.
- 현재 영향 범위: Character, Inventory, Building, Recruitment, Corruption, Quest 및 SaveSystem을 직접 쓰는 런타임 시스템.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — 정적/테스트 검증 후보.
- Skill 후보: 아니오.

#### RA01-A-002 — 진행 저장 형식은 v8이며, 버전 판정은 역직렬화 전에 원문에서 한다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 현재 쓰기 형식은 `CurrentSaveVersion = 8`이다. 파일의 실제 버전은 `saveVersion`의 역직렬화 기본값이 아니라 `SaveVersionProbe`가 원문 JSON에서 판정하며, version 없는 문서는 v0으로 취급한다.
- C: `Assets/Scripts/Common/SaveData.cs:25-49`; `Assets/Scripts/Common/SaveSystem.cs:176-242`; `Assets/Scripts/Common/SaveVersionProbe.cs`.
- T: `Assets/Editor/Common/Tests/SaveMigrationTests.cs`의 version 없는 문서·미래 버전·손상 JSON 시나리오.
- W: `ProjectDocs/WorkOrders/save-version-migration-foundation-report.md` 2~3절; 이후 v2, v3, v5, v6, v8 변경 커밋.
- G: `841114db`(versioned foundation)부터 `a5c4d761`(v8 story quest persistence)까지의 단계적 변경.
- 현재 영향 범위: 모든 기존 저장 파일의 로드 가능성 및 이후 저장 허용 여부.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — version step 연속성/현재 version 검증 후보.
- Skill 후보: 아니오.

#### RA01-A-003 — 저장 문서의 영속 범위는 진행 상태이며 휘발 전투 상태는 포함하지 않는다

- Status: CONFIRMED
- Priority: Structural
- 규칙: `SaveData`는 계정 진행, 캐릭터·파티, 재화·인벤토리, 회복·건설·모집·정화·서사 퀘스트 및 저장 메타데이터를 보유한다. 세션 킬카운트, 콤보, 내구도, 공격/애니메이션 상태 같은 휘발 상태는 저장 모델에 포함하지 않는다.
- C: `Assets/Scripts/Common/SaveData.cs:8-11,43-170`.
- T: `Assets/Editor/Common/Tests/SaveMigrationTests.cs`; 각 도메인의 SaveData 단위 테스트.
- W: `ProjectDocs/WorkOrders/save-version-migration-foundation-report.md` 2절.
- 현재 영향 범위: SaveData 필드 추가/사용 및 런타임 상태 소유 경계.
- 사용자 판단 필요: 아니오.
- Validator 후보: 아니오.
- Skill 후보: 아니오.

#### RA01-A-004 — 저장 경로는 profile/storage 계층이 소유하며 기본 진행 파일은 persistentDataPath의 playerprogress.json이다

- Status: CONFIRMED
- Priority: Structural
- 규칙: `SaveSystem`은 경로와 파일 작업을 직접 조립하지 않고 `ISaveStorage`에 위임한다. 제품 기본 `local/primary` 저장소의 주 파일은 `Application.persistentDataPath/playerprogress.json`이며, backup과 temporary 파일은 같은 profile 경로 규칙을 따른다.
- C: `Assets/Scripts/Common/SaveSystem.cs:17-19,61-69`; `Assets/Scripts/Common/SaveStorage.cs:138-201`.
- T: `Assets/Editor/Common/Tests/SaveStorageTests.cs`; `Assets/Editor/Common/Tests/SaveSystemStorageIsolationTests.cs`.
- W: `ProjectDocs/WorkOrders/save-version-migration-foundation-report.md` 4절.
- 현재 영향 범위: 진행 저장의 실제 위치, 테스트 저장소 주입, 향후 storage backend 경계.
- 사용자 판단 필요: 아니오.
- Validator 후보: 아니오.
- Skill 후보: 아니오.

#### RA01-A-005 — 정상 저장은 revision/UTC 메타데이터를 갱신한 뒤 원자적으로 교체하며, 실패 시 메타데이터를 되돌린다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 저장 직전 `saveVersion`, `saveRevision`, `lastSavedAtUtc`를 갱신한다. 저장소 쓰기가 실패하면 이 세 메타데이터는 이전 값으로 복원한다. 파일 쓰기는 같은 폴더의 temporary 파일과 primary/backup 교체를 사용하며, 기존 primary가 있으면 최근 정상본 하나를 backup으로 유지한다.
- C: `Assets/Scripts/Common/SaveData.cs:235-282`; `Assets/Scripts/Common/SaveSystem.cs:145-173`; `Assets/Scripts/Common/SaveStorage.cs:161-260`.
- T: `Assets/Editor/Common/Tests/SaveStorageTests.cs`; `Assets/Editor/Common/Tests/SaveSystemIntegrationTests.cs`의 write failure/revision 계약.
- W: `ProjectDocs/WorkOrders/save-version-migration-foundation-report.md` 5절.
- 현재 영향 범위: 모든 `SaveSystem.Save()` 호출의 성공/실패 의미, 저장 파일 복구 가능성.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — 저장 실패 메타데이터 rollback 회귀 검증 후보.
- Skill 후보: 아니오.

#### RA01-A-006 — 진행 저장과 기기별 UI/창 배치 설정 저장은 분리된다

- Status: CONFIRMED
- Priority: Structural
- 규칙: `UiSettingsSaveSystem` 및 `WindowPlacementSaveSystem`은 `SaveData` 진행 저장과 별도의 기기별 설정 경로로 취급된다.
- C: `Assets/Scripts/Common/UiSettingsSaveSystem.cs`; `Assets/Scripts/DesktopWindow/WindowPlacementSaveSystem.cs`; `Assets/Scripts/Common/SaveStorage.cs:186-190`.
- W: `ProjectDocs/WorkOrders/save-version-migration-foundation-report.md` 4절.
- 현재 영향 범위: RA-01 진행 저장 조사 범위와 UI/window 환경설정의 경계.
- 사용자 판단 필요: 아니오.
- Validator 후보: 아니오.
- Skill 후보: 아니오.

#### RA01-A-007 — 목록 중 일부는 순서 또는 인덱스 자체가 의미를 가진다

- Status: CONFIRMED
- Priority: Structural
- 규칙: `partyCharacterIds`는 고정 슬롯 순서를 보존하고, `recoverySlots`와 `purificationSlots`는 목록 인덱스가 슬롯 번호다. `items`는 획득/표시 순서를 보존한다. 따라서 일반 정규화·reset·저장 과정에서 이 목록을 무조건 압축·정렬·재배치하지 않는다.
- C: `Assets/Scripts/Common/SaveData.cs:92-117,163-170,213-232`; `Assets/Scripts/Common/SaveDataNormalizer.cs`.
- T: `Assets/Editor/Common/Tests/SaveMigrationTests.cs`; `Assets/Editor/Save/Tests/SaveResetServiceTests.cs`의 party/slot 보존 시나리오.
- W: `ProjectDocs/WorkOrders/save-data-v3-initial-party-migration-phase10a-2-report.md`.
- 현재 영향 범위: party, recovery, purification, inventory를 바꾸는 모든 저장 트랜잭션.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — slot-list 보존 회귀 검증 후보.
- Skill 후보: 아니오.

### 미확인 연결점

- 각 SaveData 필드의 최종 단일 writer와 실제 Save 호출 시점.
- v1~v8 각 migration step의 필드 변환 상세.
- Reset의 target 조합별 rollback 및 초기 character 복구 경계.
- legacy WorkOrder와 현재 v8 구현의 차이.

## RA-01-B — Migration / Reset / Storage Isolation

### 조사 완료 범위

- v0→v8 default migration step, migration working copy, normalization 및 load status
- `SaveResetService`, Reset Editor 테스트
- Local file storage의 temporary/backup/quarantine 및 test override
- Save migration/storage/integration/isolation 테스트 및 관련 WorkOrder/Git checkpoint

### 발견 Rule

#### RA01-B-001 — 마이그레이션은 v0부터 v8까지 단일 경로의 한 칸 단계로만 수행한다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 기본 migration table은 `v0→v1→v2→v3→v4→v5→v6→v7→v8`의 연속 단계다. 각 step은 정확히 한 버전만 올려야 하고, 중복된 시작 버전이나 누락된 중간 단계는 실패다.
- C: `Assets/Scripts/Common/SaveMigrationRunner.cs:6-23,103-147,164-214`.
- T: `Assets/Editor/Common/Tests/SaveMigrationTests.cs`의 default step gap, one-step, v0부터 current까지 연속성 시나리오.
- W: `ProjectDocs/WorkOrders/save-version-migration-foundation-report.md`; 이후 v2/v3/v5/v6/v7/v8 변경 WorkOrder.
- G: `841114db`, `081c42fc`, `81131e4d`, `725ecdb2`, `8527c796`, `d2c8a715`, `c7131bef`, `a5c4d761`.
- 현재 영향 범위: 과거 저장 파일의 로드/저장 가능 여부.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — step 연속성 및 `CurrentSaveVersion` 정합성.
- Skill 후보: 아니오.

#### RA01-B-002 — migration은 현재 카탈로그·에셋·밸런스 데이터를 읽지 않으며, 전부 성공하거나 호출부 문서를 전혀 바꾸지 않는다

- Status: CONFIRMED
- Priority: Critical
- 규칙: migration은 저장 문서의 작업용 깊은 사본에서만 수행하고, 모든 단계 성공 후에만 결과를 호출부 문서에 반영한다. 변환은 현재 Catalog/CSV/Asset을 읽지 않아 같은 과거 파일이 빌드마다 다른 결과가 되는 것을 막는다.
- C: `Assets/Scripts/Common/SaveMigrationRunner.cs:72-85,154-223,225-253`; v1→v2와 v2→v3 step 주석/구현.
- T: `Assets/Editor/Common/Tests/SaveMigrationTests.cs`의 failed migration no trace, deep copy, all-fields copy, v1/v2 historical result 시나리오.
- W: `ProjectDocs/WorkOrders/character-ownership-save-v2-report.md`; `save-data-v3-initial-party-migration-phase10a-2-report.md`.
- 현재 영향 범위: 모든 migration step 및 SaveData 필드 추가 시 copy coverage.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — SaveData/깊은 사본 필드 대조 회귀 검증.
- Skill 후보: 아니오.

#### RA01-B-003 — 미래 버전·migration 실패는 원본을 보존하고 진행 저장을 차단한다

- Status: CONFIRMED
- Priority: Critical
- 규칙: probe 결과가 현재보다 미래 버전이면 역직렬화하지 않는다. migration step이 없거나 예외가 나면 기본 메모리 문서를 제공하되 해당 기존 파일, backup, temporary, quarantine을 변경하지 않고 `Save()`를 false로 막는다.
- C: `Assets/Scripts/Common/SaveMigrationRunner.cs:429-486`; `Assets/Scripts/Common/SaveSystem.cs:176-242`.
- T: `Assets/Editor/Common/Tests/SaveMigrationTests.cs`의 future version 및 failed step 보존 시나리오; `Assets/Editor/Common/Tests/SaveSystemIntegrationTests.cs`.
- W: `ProjectDocs/WorkOrders/save-version-migration-foundation-report.md` 3·5절.
- 현재 영향 범위: downgrade 실행, 불완전 배포, migration 등록 결함.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — future-version no-deserialize/no-write 회귀 검증.
- Skill 후보: 아니오.

#### RA01-B-004 — malformed/unreadable primary는 격리 성공 후에만 새 진행으로 계속 저장할 수 있다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 파일이 존재하지만 읽을 수 없거나 JSON이 malformed이면 새 게임과 동일시하지 않는다. primary를 quarantine으로 이동해 보존하는 데 성공한 경우에만 기본 문서로 fallback하고 이후 저장을 허용한다. 격리에 실패하면 fallback 상태여도 저장은 차단된다.
- C: `Assets/Scripts/Common/SaveStorage.cs:206-215,262-299`; `Assets/Scripts/Common/SaveSystem.cs:245-263`.
- T: `Assets/Editor/Common/Tests/SaveStorageTests.cs`; `Assets/Editor/Common/Tests/SaveSystemIntegrationTests.cs`의 corrupt fallback/quarantine failure 시나리오.
- W: `ProjectDocs/WorkOrders/save-version-migration-foundation-report.md` 3·5절.
- 현재 영향 범위: 손상 파일 발견 시 사용자 진행 보존.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — corruption fallback/blocked-write 회귀 검증.
- Skill 후보: 아니오.

#### RA01-B-005 — 정규화는 모든 load 경로의 마지막 단계이며, migration과 의미 변경 책임을 분리한다

- Status: CONFIRMED
- Priority: Structural
- 규칙: 새 게임, 정상 로드, migration 완료 문서는 모두 `SaveDataNormalizer`를 거친다. 정규화는 null 목록/항목과 최소 슬롯 같은 구조를 보정하고, 필드 의미 변경은 migration step이 담당한다.
- C: `Assets/Scripts/Common/SaveDataNormalizer.cs:6-25`; `Assets/Scripts/Common/SaveMigrationRunner.cs:469-495`.
- T: `Assets/Editor/Common/Tests/SaveMigrationTests.cs`의 normalize idempotence, party/slot, corruption 값 보정 시나리오.
- W: `ProjectDocs/WorkOrders/save-version-migration-foundation-report.md` 3절; `purification-slot-save-v6-phase11e-b-report.md`.
- 현재 영향 범위: 모든 로드 결과의 null safety 및 slot/list 보존.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — normalizer 멱등성 및 허용/비허용 모양 회귀 검증.
- Skill 후보: 아니오.

#### RA01-B-006 — Reset은 선택 범위를 한 문서 변경으로 모아 정확히 한 번 저장하고, 실패·예외면 모두 rollback한다

- Status: CONFIRMED
- Priority: Critical
- 규칙: Reset은 유효한 선택 target만 적용하고, 변경 묶음마다 save delegate를 한 번 호출한다. 저장이 false이거나 예외가 발생하면 해당 reset이 바꾼 모든 SaveData 필드를 이전 상태로 되돌린다.
- C: `Assets/Editor/Save/SaveResetService.cs:303-337,610-641`.
- T: `Assets/Editor/Save/Tests/SaveResetServiceTests.cs`의 no-selection no-save, one-save, false/exception rollback, 복합 reset 시나리오.
- W: `ProjectDocs/WorkOrders/keybuddy-13e-save-reset-initial-character-recovery-report.md`.
- G: `4169204b`.
- 현재 영향 범위: Tools > Reset의 Item, Currency, Construction, Character, Quest, All 조합.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — reset save-once/rollback 회귀 검증.
- Skill 후보: 아니오.

#### RA01-B-007 — Character reset은 현재 저장 상태만 비우는 것이 아니라 Catalog/PartyConfig가 제공한 초기 상태로 복구한다

- Status: CONFIRMED
- Priority: Structural
- 규칙: Character reset은 catalog의 유효한 `InitiallyOwned` seed와 PartyConfig 고정 슬롯 수가 있어야 수행된다. 누락된 초기 캐릭터도 복구하고, 기본 보유 캐릭터는 삭제 대상에서 보호하며, party/recovery/purification/관련 quest·unlock 상태를 일관되게 갱신 또는 rollback한다.
- C: `Assets/Editor/Save/SaveResetService.cs:268-301,473-550`; `Assets/Editor/Save/SaveResetWindow.cs`.
- T: `Assets/Editor/Save/Tests/SaveResetServiceTests.cs`의 initial recovery, protected character, slot preservation, invalid seed rejection; `SaveResetWindowTests.cs`.
- W: `ProjectDocs/WorkOrders/keybuddy-13e-save-reset-initial-character-recovery-report.md`.
- G: `4169204b`.
- 현재 영향 범위: Editor reset, character ownership, party, recovery/purification slot, story quest 및 recruitment unlock.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — catalog seed/party slot contract 검증 후보.
- Skill 후보: 아니오.

#### RA01-B-008 — EditMode 테스트는 제품 persistentDataPath에 닿지 않도록 scoped storage override를 사용한다

- Status: CONFIRMED
- Priority: Critical
- 규칙: `PushStorageOverrideForTests`로 설치한 임시 storage는 중첩 LIFO 범위로 복귀해야 한다. 전역 fixture는 실제 primary와 backup이 시험 전후 동일함을 확인하며, test override를 예외로 빠져나와도 제품 저장소로 누출되지 않아야 한다.
- C: `Assets/Scripts/Common/SaveSystem.cs:287-348`; `Assets/Editor/Common/Tests/SaveSystemTestIsolationFixture.cs`.
- T: `Assets/Editor/Common/Tests/SaveSystemStorageIsolationTests.cs` 및 fixture.
- W: `ProjectDocs/WorkOrders/keybuddy-13f-test-save-isolation-report.md`; 13E report의 과거 누출 사례.
- G: `2668ff59`, `ba5034d4`.
- 현재 영향 범위: 모든 EditMode 테스트와 실제 사용자 저장 안전성.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — test fixture 적용 여부/actual file fingerprint 검증 후보.
- Skill 후보: 아니오.

#### RA01-B-009 — “Save System은 현재 MVP에 추가하지 않는다”는 README 범위 문구는 현재 구현 기준의 규칙이 아니다

- Status: LEGACY
- Priority: Production
- 규칙: `README.md`는 초기 MVP에서 Save System을 추가하지 않는다고 서술하지만, 현재 v8 SaveData와 migration/reset/storage isolation 구현·테스트·WorkOrder가 이를 명백히 대체한다. 이 문구를 현재 Save 정책의 근거로 사용하면 안 된다.
- C: `Assets/Scripts/Common/SaveData.cs:25`; `Assets/Scripts/Common/SaveSystem.cs`; Save 관련 Editor tests.
- T: Save migration/integration/reset/isolation 테스트.
- D: `README.md`의 “절대 추가하지 말 것” 목록.
- W: Save 관련 WorkOrder 전반.
- G: 초기 README 커밋 `c984300b` 이후 save foundation `841114db` 및 후속 v2~v8 변화.
- 현재 영향 범위: 문서 근거의 우선순위와 정식 Rule 승격 시 README 해석.
- 사용자 판단 필요: 예 — README를 현재 제품 방향 문서로 유지할지, 역사 문서로 표기할지의 문서 거버넌스 판단이 필요하다. 이번 조사에서는 변경하지 않는다.
- Validator 후보: 아니오.
- Skill 후보: 아니오.

### 미확인 연결점

- 개별 런타임 도메인이 SaveData를 바꾼 뒤 Save를 언제 호출하는지.
- 저장 실패 rollback이 각 도메인 transaction에서 어떤 데이터까지 포함하는지.
- `SaveSystem.Save()` 이외에 별도 저장을 사용하는 UI/window 설정 호출 지점.

## RA-01-C — Save와 다른 시스템의 연결점 및 저장 시점

### 조사 완료 범위

- `SaveSystem.Save()` 직접 호출 및 `SaveData` 주입/save delegate 연결점
- Character, Progress, Inventory, Building, Recruitment, Recovery, Purification, Party, Shop, Dungeon, Quest 흐름
- 관련 도메인 테스트, WorkOrder 및 Save failure 처리 방식
- 진행 저장과 Window Placement 설정 저장의 분리 확인

### 발견 Rule

#### RA01-C-001 — 여러 진행 필드를 함께 바꾸는 도메인 작업은 메모리 mutation → Save 1회 → 성공 후 알림 순서의 단일 트랜잭션으로 처리한다

- Status: CONFIRMED
- Priority: Critical
- 규칙: 보상/처치, 건설, 상점 거래, 모집, 파티 교체, 회복·정화, 퀘스트 보상처럼 여러 저장 필드가 함께 바뀌는 작업은 먼저 메모리에서 묶어 바꾸고 한 번 저장한다. 저장 성공 뒤에만 UI/이벤트/토스트/자동 교체를 알리고, 실패·예외면 영수증 또는 snapshot으로 변경값과 저장 메타데이터를 rollback한다.
- C: `Assets/Scripts/Inventory/DefeatRewardDistributor.cs:225-292`; `Assets/Scripts/Building/BuildingConstructionService.cs`; `Assets/Scripts/Shop/ShopTradeService.cs:240-264,310-330`; `Assets/Scripts/Party/PartyCompositionService.cs:246-268`; `Assets/Scripts/Recovery/PassiveStaminaRecoveryService.cs:105-124,187-197`; `Assets/Scripts/Corruption/PurificationService.cs`.
- T: `CombatDefeatTransactionTests.cs`; `BuildingConstructionServiceTests.cs`; `ShopTradeServiceTests.cs`; `PartyCompositionServiceTests.cs`; `PurificationServiceTests.cs`; `PassiveStaminaRecoveryServiceTests.cs`; recruitment tests의 SaveFailed 시나리오.
- W: `combat-defeat-save-transaction-phase11f-d-report.md`; `building-construction-start-phase9-2b-report.md`; `shop-trade-foundation-phase12b-report.md`; `party-composition-service-phase10b-report.md`; `passive-stamina-recovery-phase10d-report.md`.
- 현재 영향 범위: 다수 SaveData 필드를 한 사용자 행동/게임 이벤트에서 갱신하는 모든 도메인 작업.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — transaction이 Save를 한 번만 호출하고 실패 rollback 후 알림을 억제하는 회귀 검증.
- Skill 후보: 아니오.

#### RA01-C-002 — `WithoutSave` API는 독립 완료 API가 아니라 상위 트랜잭션 조립용이다

- Status: CONFIRMED
- Priority: Critical
- 규칙: `Apply...WithoutSave`, `TrySpend...WithoutSave` 같은 API는 메모리 변경/receipt만 제공한다. 호출자는 이어서 Save를 정확히 한 번 수행하고, 성공 뒤 알림을 내며, 실패 시 receipt로 되돌려야 한다. 이 경계는 중간 저장으로 인한 부분 영속을 피하기 위한 것이다.
- C: `Assets/Scripts/Inventory/InventoryManager.cs:839-870,1125-1135`; `Assets/Scripts/Inventory/DefeatRewardDistributor.cs:233-268`; `Assets/Scripts/Quest/CharacterStoryQuestService.cs:175-179`; `Assets/Scripts/Character/CharacterRoster.cs:906-910`.
- T: `CombatDefeatTransactionTests.cs`; `InventoryCostTests.cs`; `CharacterStoryQuestRewardTransactionTests.cs`; `BuildingConstructionServiceTests.cs`.
- W: `combat-defeat-save-transaction-phase11f-d-report.md`; `character-story-quest-reward-phase13d-report.md`; `building-construction-start-phase9-2b-report.md`.
- 현재 영향 범위: inventory cost/reward, defeat, story quest, recovery/party 관련 composition.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — `WithoutSave` 호출 후 단일 persist/rollback 경로 회귀 검증.
- Skill 후보: 아니오.

#### RA01-C-003 — 관찰 또는 선택적 처리 경로는 저장을 암묵적으로 시작하지 않고 준비된 문서만 사용한다

- Status: CONFIRMED
- Priority: Structural
- 규칙: Save를 시작할 권한이 없는 관찰자/세션 경로는 `SaveSystem.TryGetLoadedData`를 통해 이미 준비된 문서만 받는다. 파일 load나 새 문서 생성을 유발하지 않는 경계가 존재하며, `SaveSystem.Data`를 사용하는 명시적 초기화/변경 경로와 구분된다.
- C: `Assets/Scripts/Common/SaveSystem.cs:126-134`; `Assets/Scripts/Inventory/InventoryManager.cs:889-890`; `Assets/Scripts/Quest/CharacterStoryQuestService.cs:241-245`; `Assets/Scripts/Inventory/DefeatRewardDistributor.cs:210-216`.
- T: `Assets/Editor/Common/Tests/SaveSystemIntegrationTests.cs`의 직접 문서 주입/저장소 접근 방지 계약.
- W: `ProjectDocs/WorkOrders/combat-defeat-save-transaction-phase11f-d-report.md`.
- 현재 영향 범위: dungeon session, combat transaction, inventory mutation, quest event 처리.
- 사용자 판단 필요: 아니오.
- Validator 후보: 아니오.
- Skill 후보: 아니오.

#### RA01-C-004 — 새 게임 초기 캐릭터 지급은 `LoadStatus.NewGame`에서만 발생하며, 지급 자체는 즉시 저장하지 않는다

- Status: CONFIRMED
- Priority: Structural
- 규칙: 빈 character 목록이 아니라 `SaveSystem.LoadStatus == NewGame`만 초기 지급 조건이다. Loaded/Migrated/CorruptFallback/FutureVersionBlocked/MigrationFailed에는 지급하지 않으며, 초기 지급은 자체 Save를 호출하지 않는다.
- C: `Assets/Scripts/Character/CharacterRoster.cs:276-297`.
- T: `Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs`; `Assets/Editor/Common/Tests/SaveSystemIntegrationTests.cs`.
- W: `ProjectDocs/WorkOrders/character-ownership-save-v2-report.md`; `keybuddy-13e-save-reset-initial-character-recovery-report.md`.
- 현재 영향 범위: new-game ownership, damaged/future save 보호, first persistence timing.
- 사용자 판단 필요: 아니오.
- Validator 후보: 예 — load status별 initial grant 금지/허용 회귀 검증.
- Skill 후보: 아니오.

#### RA01-C-005 — 저장 실패 뒤 메모리 rollback은 단일 공통 동작이 아니라 호출 도메인의 책임으로 관찰된다

- Status: CONFLICT
- Priority: Critical
- 규칙: `SaveSystem.Save()`는 실패 시 저장 메타데이터만 복원하고 SaveData의 도메인 값은 복원하지 않는다. 복합 transaction 서비스는 자체 rollback을 구현하지만, 일부 직접 호출 경로는 반환값을 확인하지 않고 변경 뒤 이벤트까지 발행한다. 따라서 “모든 Save 실패가 메모리 변경을 rollback한다”는 전역 규칙은 현재 성립하지 않는다.
- C: `Assets/Scripts/Common/SaveSystem.cs:137-173`; rollback을 하는 `DefeatRewardDistributor.cs:257-292`, `PartyCompositionService.cs:249-264`, `ShopTradeService.cs:246-259`; 반환값을 사용하지 않는 `PlayerProgress.cs:256-262,471-475`, `CharacterRoster.cs:898-903`.
- T: 복합 transaction rollback은 `CombatDefeatTransactionTests.cs`, `InventoryCostTests.cs`, `BuildingConstructionServiceTests.cs`, `ShopTradeServiceTests.cs`, `PartyCompositionServiceTests.cs`에서 확인됨. 직접 `PlayerProgress`/`SetStamina` failure rollback을 현재 고정하는 별도 테스트는 이번 범위에서 확인하지 못함.
- W: `save-version-migration-foundation-report.md`는 SaveSystem 자체가 metadata만 복원함을 설명하고, 복합 WorkOrder들은 domain rollback을 별도로 기록한다.
- 현재 영향 범위: 저장 장치 오류 시 메모리 상태·UI 알림과 디스크 상태가 불일치할 수 있는 직접 저장 경로.
- 사용자 판단 필요: 예 — 전역 Save 실패 의미를 정식 Rule로 승격할 때, 이 차이를 허용된 예외로 둘지 별도 정책으로 다룰지 결정이 필요하다. 이번 조사에서는 변경하지 않는다.
- Validator 후보: 예 — direct-save failure semantics를 명시적으로 고정할지 판단 후 후보.
- Skill 후보: 아니오.

#### RA01-C-006 — 저장 서비스는 외부 의존성을 주입해 저장 단위 테스트와 실제 파일 경로를 분리한다

- Status: CONFIRMED
- Priority: Production
- 규칙: 도메인 서비스는 일반적으로 `Func<SaveData>`, `Func<bool>` save action, UTC provider 및 필요한 catalog를 주입받는다. 따라서 단위 테스트는 실제 persistentDataPath 없이 성공/false/throw를 재현한다.
- C: `BuildingConstructionService.cs`; `RecruitmentCycleService.cs`; `RecoveryStation.cs`; `PassiveStaminaRecoveryService.cs`; `PurificationService.cs`; `ShopTradeService.cs`; `PartyCompositionService.cs` 생성자.
- T: Building/Recruitment/Recovery/Corruption/Shop/Party EditMode service tests.
- W: `keybuddy-13f-test-save-isolation-report.md`; 도메인별 WorkOrder의 isolated test 서술.
- 현재 영향 범위: persistence transaction 테스트 가능성 및 production storage 의존성 격리.
- 사용자 판단 필요: 아니오.
- Validator 후보: 아니오.
- Skill 후보: 아니오.

#### RA01-C-007 — UI/window 배치 저장은 진행 Save transaction에 섞이지 않는다

- Status: CONFIRMED
- Priority: Convention
- 규칙: 창 위치 저장은 `WindowPlacementSaveSystem.Save`를 통해 별도 데이터로 처리되며, `SaveSystem.Save()` 진행 트랜잭션에 포함되지 않는다.
- C: `Assets/Scripts/DesktopWindow/TransparentWindowController.cs:1100-1105`; `Assets/Scripts/DesktopWindow/WindowPlacementSaveSystem.cs`.
- T: 이번 범위에서 전용 test 근거는 확인하지 못함.
- W: `save-version-migration-foundation-report.md` 4절.
- 현재 영향 범위: DesktopWindow/사용자 환경 설정과 게임 진행 상태의 저장 경계.
- 사용자 판단 필요: 아니오.
- Validator 후보: 아니오.
- Skill 후보: 아니오.

### 조사 중 발견했으나 수정하지 않은 항목

- `RA01-C-005`의 direct Save failure 처리 불일치. 코드·테스트·데이터는 변경하지 않았다.
- 초기 README의 Save System 제외 문구와 현재 v8 구현 간 문서 시점 불일치(`RA01-B-009`). 문서는 변경하지 않았다.

## RA-01 완료 요약 및 재개 정보

- 완료 범위: Save model/location, migration/reset/storage isolation, runtime integration/save timing.
- 주요 근거: current Save runtime 8개 파일, Save/Reset tests, 도메인 transaction tests, 10개 이상의 관련 WorkOrder, save-history milestone commits.
- 의도적으로 미조사: RA-02 전투 규칙의 세부 결과값, 각 도메인 고유 game rule의 전체 추출, UI/window 설정의 세부 schema.
- 재개 전 확인: `git rev-parse HEAD`를 base commit과 비교한다. 다르면 `git diff --name-status 7f47233b..HEAD -- Assets/Scripts/Common/Save* Assets/Editor/Save Assets/Editor/Common/Tests/Save* ProjectDocs/RuleArchaeology`로 RA-01 근거 변화부터 기록한다.
