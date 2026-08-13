# 던전 세션 원장 기반(KeyBuddy Phase 6) 완료 보고서

작업일: 2026-08-13

시작 커밋: `fa86817d`, 구현 완료 HEAD: `67ea27ef`. 보고서 커밋(D)의 실제 해시는 최종 응답에 기재한다.

## 1. 커밋 이력

| 단계 | 커밋 | 설명 |
| --- | --- | --- |
| A | `de0de088` | 실제 인벤토리 보상 적용 결과 |
| B | `7d7e12cd` | 순수 던전 세션 원장 |
| C | `67ea27ef` | 런타임 트래커·씬 연결 |
| D | 본 보고서 커밋/최종 응답에 실제 해시 기재 | 이 보고서 |

## 2. 변경 파일 목록

A–C 구현 범위: `git diff --stat fa86817d..HEAD` 기준 15개 파일, +3,576 / −79줄.

### D — 보고서

| 파일 | 변경 |
| --- | --- |
| `ProjectDocs/WorkOrders/dungeon-session-ledger-foundation-report.md` | 신규 |

### A — 실제 보상 결과 (`de0de088`)

| 파일 | 변경 |
| --- | --- |
| `Assets/Scripts/Inventory/InventoryManager.cs` | +175 −4 |
| `Assets/Scripts/Inventory/DefeatRewardDistributor.cs` | 리네임 + +44 −35 |
| `Assets/Scripts/Inventory/DefeatRewardDistributor.cs.meta` | 리네임(바이트 동일) |
| `Assets/Editor/Inventory/Tests/DefeatRewardTests.cs` | +84 −79 |
| `Assets/Editor/Inventory/Tests/InventoryRewardApplyTests.cs` | 신규 730줄 |
| `Assets/Editor/Inventory/Tests/InventoryRewardApplyTests.cs.meta` | 신규 |

### B — 순수 세션 원장 (`7d7e12cd`)

| 파일 | 변경 |
| --- | --- |
| `Assets/Scripts/Dungeon/DungeonSessionLedger.cs` | 신규 292줄 |
| `Assets/Scripts/Dungeon/DungeonSessionLedger.cs.meta` | 신규 |
| `Assets/Editor/Dungeon/Tests/DungeonSessionLedgerTests.cs` | 신규 834줄 |
| `Assets/Editor/Dungeon/Tests/DungeonSessionLedgerTests.cs.meta` | 신규 |

### C — 런타임 트래커 (`67ea27ef`)

| 파일 | 변경 |
| --- | --- |
| `Assets/Scripts/Dungeon/DungeonSessionTracker.cs` | 신규 287줄 |
| `Assets/Scripts/Dungeon/DungeonSessionTracker.cs.meta` | 신규 |
| `Assets/Editor/Dungeon/Tests/DungeonSessionTrackerTests.cs` | 신규 1,138줄 |
| `Assets/Editor/Dungeon/Tests/DungeonSessionTrackerTests.cs.meta` | 신규 |
| `Assets/Scenes/desktopScene_ReSize.unity` | +16줄(트래커 배선) |

## 3. 권한적 세션 시작과 종료

세션의 시작과 종료를 결정하는 유일한 권한은 `FieldModeManager.FieldModeChanged(FieldMode, DungeonDefinition)` 이벤트다.

- **시작**: `FieldModeChanged(Dungeon, dungeon)`이 발행될 때만 `DungeonSessionTracker.HandleFieldModeChanged`가 세션을 시작한다. UI 버튼 누름이나 `TryEnterDungeon` 요청 자체는 아무 효과가 없다 — `FieldModeManager`가 프레임 잠금·유효성·중복 진입 검사를 모두 통과한 뒤 내부 상태를 확정하고 이벤트를 한 번만 발행해야 비로소 트래커에 도달한다.
- **종료(완료)**: `FieldModeChanged(Town, null)`이 발행되면 활성 세션을 완료하고 스냅샷을 대기열에 넣는다. 마을 복귀 요청이나 버튼만으로는 완료되지 않는다.
- **다른 던전 전환**: 활성 세션 중 다른 `DungeonId`의 던전 이벤트가 오면 이전 세션을 **스냅샷 없이 버리고(abandon)** 새 세션을 시작한다.
- **fail-closed**: `Dungeon` 모드인데 던전이 null/유효하지 않음, `Town`인데 `dungeon != null`, 지원하지 않는 모드값 — 모두 활성 세션을 **완료 없이 abandon**한다. `HandleFieldModeChanged`와 `ResyncWithActualState` 양쪽 모두 동일 규칙이다.

## 4. 요청 보상 vs 실제 보상

### 흐름

```
MonsterDefeated → DefeatRewardDistributor
    ↓ 몬스터 테이블에서 요청 재화·아이템 결정
    ↓
InventoryManager.ApplyRewards(요청 재화, 요청 아이템 목록)
    ├─ 포화 산술로 실제 재화 delta 계산 (int.MaxValue 상한)
    ├─ 칸별 실제 아이템 수량 계산 (int.MaxValue 상한)
    ├─ 실제 변경이 있으면:
    │     1. 불변 InventoryRewardApplyResult 생성
    │     2. SaveSystem.Save() 1회
    │     3. InventoryChanged 1회
    │     4. RewardApplied 이벤트 1회 (결과가 비어 있지 않을 때만)
    └─ 실제 변경이 없으면: 저장·InventoryChanged·RewardApplied 모두 발생하지 않음
    ↓
InventoryRewardApplyResult (실제 delta만 담김)
    ├→ DefeatRewardDistributor: 비어 있지 않으면 토스트 표시
    └→ DungeonSessionTracker.HandleRewardApplied (RewardApplied 이벤트 경유)
        └→ DungeonSessionLedger.RecordReward (실제 delta를 long 포화 합산)
```

### 핵심 규칙

- `InventoryRewardApplyResult`는 `internal` 생성자를 가진 불변 객체다. `ActualCurrencyDelta`와 `ItemDeltas`(각각 `InventoryRewardItemDelta`)는 요청량이 아니라 **실제 적용된 양**이다.
- 요청 100인데 상한에 걸려 3만 올랐으면 `ActualCurrencyDelta == 3`이다.
- `IsEmpty`면 `RewardApplied` 이벤트가 발행되지 않으므로 원장에도 기록되지 않는다.
- 마을 복귀(`Town`) 이벤트 자체에는 보상이 없다. 복귀 시점에 보상을 발행하는 경로는 없다.

## 5. 스냅샷 구조

`DungeonSessionSnapshot` (`Assets/Scripts/Dungeon/DungeonSessionLedger.cs`):

| 필드 | 타입 | 설명 |
| --- | --- | --- |
| `DungeonId` | `string` | 던전 정의의 `dungeonId`, Ordinal 비교 |
| `SessionSequence` | `long` | 원장이 부여하는 단조증가 시퀀스 번호 |
| `EarnedCurrency` | `long` | 포화 합산된 실제 획득 재화 |
| `DefeatedMonsterCount` | `long` | 포화 카운트된 처치 수 |
| `EarnedItems` | `ReadOnlyCollection<DungeonSessionItemReward>` | 최초 획득 순서 보존, Ordinal ID, 포화 합산 |
| `IsEmpty` | `bool` | 재화 0 + 처치 0 + 아이템 0이면 true |

`DungeonSessionItemReward`:

| 필드 | 타입 |
| --- | --- |
| `ItemDefinition` | `ItemDefinition` |
| `ItemId` | `string` |
| `Count` | `long` |

모든 수치는 `long` 포화 산술이다. 같은 `ItemId`가 여러 번 기록되면 하나의 항목으로 합산되며, 최초 획득 순서가 보존된다.

## 6. FIFO 대기열과 Peek/Consume 정책

`DungeonSessionLedger`는 완료된 스냅샷을 FIFO 대기열(`Queue<DungeonSessionSnapshot>`)에 넣는다.

- `TryPeekNextCompletedSession(out snapshot)` — 가장 오래된 스냅샷을 반환하되 대기열에서 꺼내지 않는다. 여러 번 호출해도 같은 인스턴스를 반환한다.
- `TryConsumeNextCompletedSession(out snapshot)` — 가장 오래된 스냅샷을 **꺼내서** 반환한다. 한 번 소비하면 다시 얻을 수 없다.

`DungeonSessionTracker`는 이 두 메서드를 그대로 프록시한다. 소비 정책은 호출부(Phase 7)가 결정한다.

### Phase 7 API 포인트

- `DungeonSessionTracker.TryPeekNextCompletedSession` / `TryConsumeNextCompletedSession` — 결과 화면·보상 요약 표시에 사용.
- `DungeonSessionTracker.SessionCompleted` 이벤트 — 세션 완료 즉시 `DungeonSessionSnapshot`을 전달. 결과 팝업 트리거 등에 사용.
- `DungeonSessionTracker.HasActiveSession`, `PendingCompletedSessionCount` — 상태 조회.

## 7. 구독과 생명주기

`DungeonSessionTracker`는 `[DisallowMultipleComponent]` MonoBehaviour다.

- **구독 참조**: `subscribedFmm`, `subscribedQueue`, `subscribedIm` — 실제 구독 중인 인스턴스를 추적한다. `Subscribe()`는 항상 `Unsubscribe()`를 먼저 호출하므로 참조가 교체되어도 이전 인스턴스의 이벤트를 정확히 해제한다.
- **OnEnable**: `ResolveReferences()` → `Subscribe()` → `ResyncWithActualState()`.
- **Start**: 동일 체인을 항상 재시도한다(조기 반환 없음). OnEnable 시점에 일부 참조가 누락되었을 때 보완한다.
- **OnDisable**: `Unsubscribe()`.
- **ResyncWithActualState**: 비활성 동안 놓친 전환을 보상한다. 같은 던전이면 세션 유지, 다른 던전이면 abandon + 새 시작, 마을이면 abandon. 유효하지 않은 쌍이나 지원하지 않는 모드는 fail-closed abandon.

## 8. 저장과 영속성

- `SaveData.CurrentSaveVersion`은 **v2 그대로**다. 세션 원장은 필드를 추가하지 않았다.
- 세션 데이터는 **메모리 전용**이다. `DungeonSessionLedger`와 `DungeonSessionTracker`는 `SaveSystem`, `SaveData`, `persistentDataPath`를 참조하지 않는다. 마이그레이션이나 영속 세션 저장은 없다.
- 저장 호출 빈도: `InventoryManager.ApplyRewards`는 실제 변경이 있을 때만 저장 1회 + `InventoryChanged` 1회. 트래커의 `Peek`/`Consume`은 저장을 발생시키지 않는다.

## 9. 테스트

| 단계 | 기준선 | 결과 | 실패·스킵·미확정 | 컴파일 오류 |
| --- | --- | --- | --- | --- |
| 기준선(fa86817d) | 586 | — | — | — |
| A 전체 | 623 | 623/623 | 0 | 0 |
| B 전체 | 670 | 670/670 | 0 | 0 |
| C 전체(최종) | 719 | 719/719 | 0 | 0 |
| C 집중(DungeonSessionTrackerTests) | 49 | 49/49 | 0 | 0 |

씬 애디티브 EditMode 스모크 테스트(`Scene_ExactlyOneTracker_RefsAreExactSceneComponents`):
- 열린 씬에서 `DungeonSessionTracker`가 정확히 1개임을 확인한다.
- `fieldModeManager`는 트래커와 같은 GameObject의 `FieldModeManager` 인스턴스이고, `encounterQueue`와 `inventoryManager`는 같은 씬의 컴포넌트임을 `scene` 비교로 검증한다.

## 10. UI 리스케일·씬·프리팹 무결성

- 프리팹 3개: Phase 6에서 바이트 변경 없음(`git diff fa86817d..HEAD -- '*.prefab'` 빈 출력).
- `desktopScene.unity`: 변경 없음.
- `desktopScene_ReSize.unity`: +16줄(트래커 컴포넌트 배선만). 기존 컴포넌트 블록은 건드리지 않았다.
- `DefeatRewardDistributor` 메타 GUID `ec5ac0a2b315419e88cda22433753fe4`:
  - `desktopScene_ReSize.unity:5455` — 확인.
  - `desktopScene.unity:5404` — 확인.
- target-scene PlayMode는 의도적으로 실행하지 않았다(`persistentDataPath` 안전성 — 자동화 환경에서 실제 저장 파일에 영향을 줄 수 있다).

## 11. 비활성 중 마을 복귀

트래커가 비활성(`OnDisable`) 상태에서 마을 복귀가 일어나면, 재활성화 시 `ResyncWithActualState`가 마을 상태를 감지하고 활성 세션을 **위조 완료 없이 abandon**한다. 이것은 의도된 동작이다.

선택적으로 라이브 PlayMode에서 수동 검증할 수 있으나, Phase 6 범위에서는 EditMode 테스트(`Resync_Town_AbandonsActive`)로 커버한다.

## 12. Sol High 최종 게이트

gpt-5.6-sol high 승인, 블로커 없음.

잔여 사항:
- 비활성 중 마을 복귀 시 위조 결과 없이 의도적으로 abandon (위 11번).
- 선택적 라이브 PlayMode 수동 검증.

## 13. 범위 외(Non-scope)

- CSV 파일, Generated 폴더: 변경 없음.
- SaveData 스키마 변경, 마이그레이션: 없음.
- 영속 세션 저장: 없음 (메모리 전용).
- PlayMode 자동 테스트: 실행하지 않음.
- 기존 보고서 파일: 수정하지 않음.

## 14. Phase 7 API·흐름 안내

Phase 7에서 사용할 진입점:

| API | 용도 |
| --- | --- |
| `DungeonSessionTracker.SessionCompleted` 이벤트 | 세션 완료 직후 결과 팝업 트리거 |
| `TryPeekNextCompletedSession` | 결과 화면에 스냅샷 표시 (소비하지 않음) |
| `TryConsumeNextCompletedSession` | 사용자 확인 후 스냅샷 소비 |
| `HasActiveSession` | 던전 진행 중 UI 표시 판단 |
| `PendingCompletedSessionCount` | 미소비 결과 개수 |
| `DungeonSessionSnapshot.EarnedCurrency/DefeatedMonsterCount/EarnedItems` | 결과 요약 렌더링 |

Phase 7 흐름 예시:
1. `SessionCompleted` 이벤트 → 결과 팝업 열기.
2. 팝업에서 `TryPeekNextCompletedSession`으로 스냅샷 읽기.
3. 사용자 확인 → `TryConsumeNextCompletedSession`으로 소비.
4. 스냅샷은 읽기 전용 요약이다. 보상은 이미 `ApplyRewards` 시점에 인벤토리에 적용·저장 완료되었으므로, **스냅샷을 근거로 보상을 재지급하거나 인벤토리를 다시 저장하지 않는다.** Consume은 대기열에서 제거할 뿐이다.
