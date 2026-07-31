# 회복소 MVP 1단계 (도메인·저장·재화·시간) 구현 완료 보고서

작업일: 2026-07-31 (초판) / 2026-07-31 (검토 보정 반영)
범위: 도메인 규칙 / 저장 구조 / 재화 트랜잭션 / 시간 계산까지. **UI는 포함하지 않는다.**

> **검토 보정 이력:** 초판 이후 코드 리뷰에서 나온 finding 5건을 모두 반영했다. 무엇을 왜 고쳤는지는
> **9절**에 따로 정리했고, 본문(1~8절)은 보정 후 최종 동작으로 갱신돼 있다.

전제: 씬(`desktopScene.unity`), 프리팹, 스프라이트, UI 레이아웃은 **한 줄도 변경하지 않았다**
(`git status`로 확인 - 변경된 파일은 전부 `.cs`다).

---

## 1. 구현 요약과 상태 소유권

### 1.1 계층 구조

```
RecoveryBalanceTable (ScriptableObject)   ← 밸런스 단일 수정 지점
        │ ToBalance()
        ▼
RecoveryBalance (readonly struct)         ← 도메인이 보는 값 스냅샷
        │
RecoveryStation (순수 C#)                  ← 규칙 전체. 씬/MonoBehaviour 의존 없음
        │  ├ IRecoveryRoster  → CharacterRosterRecoveryAdapter → CharacterRoster
        │  ├ IRecoveryWallet  → InventoryRecoveryWallet        → InventoryManager
        │  ├ Func<SaveData>   → SaveSystem.Data
        │  ├ Func<bool>       → SaveSystem.Save
        │  └ Func<DateTime>   → DateTime.UtcNow
        ▼
RecoveryService (MonoBehaviour)           ← 씬에 붙는 얇은 껍데기. Tick 호출 + 정적 이벤트 전달
```

`RecoveryStation`이 순수 C#인 덕분에 씬 없이 237개의 규칙 검증을 그대로 돌릴 수 있다
(검증 seam은 clock / save / currency / roster 4개뿐이며 그 이상 추상화하지 않았다).

### 1.2 상태 소유권 — 상태 값은 **어디에도 저장하지 않는다**

`RecoveryCharacterState`는 저장 필드가 아니라 **매번 파생되는 값**이다. 상태 문자열을 따로
저장하면 실제 데이터와 어긋난 상태가 파일에 남기 때문이다.

| 상태 | 판정 근거(= 소유자) | 저장 여부 |
| --- | --- | --- |
| `Recovering` / `RecoveryComplete` | `SaveData.recoverySlots[i]` (회복소 소유) — 회복소 인스턴스가 없을 때도 `RecoveryStation.IndexOfSavedSlot`으로 같은 답을 낸다 | 저장 |
| `Active` | `CharacterRoster.Current` (로스터 소유) | 저장 안 함(런타임) |
| `PendingRecovery` | `RecoveryStation.pendingBySlot[]` (런타임 배열) | **저장 안 함** |
| `Exhausted` / `Available` | `SaveData.characters[].currentStamina` (로스터 소유) | 저장 |

판정 우선순위도 위 표의 순서 그대로이며, 이것이 기존 캐릭터 모델과 충돌하지 않는 근거다.
행동력 값의 소유자는 **여전히 `CharacterRoster` 하나**이고, 회복소는 계산 결과를
`ApplyRecoveryStamina`로 돌려줄 뿐 자기 사본을 들고 있지 않다.

**Active 우선 규칙(확정):** 전투 중인 캐릭터는 행동력이 0이어도 `Active`이며 `Exhausted`가 아니다.
따라서 회복 등록이 불가하다. (Coordinator 결정 Q1=a. 관련 위험은 8-1절 참고.)

**회복 중인 캐릭터는 절대 Active가 되지 않는다.** 앱을 켤 때 시작 캐릭터를 고르는
`CharacterRoster.ResolveStartCharacter`가 회복 슬롯에 있는 캐릭터를 후보에서 제외한다. 이 판정은
`RecoveryService`가 아직 만들어지기 전(로스터의 `Awake`)에 일어나므로, 저장된 `recoverySlots`를 직접
보는 정적 판정(`RecoveryStation.IndexOfSavedSlot`)이 근거다 - 회복소가 있을 때와 없을 때가 같은 답을
내도록 근거를 하나로 모아 두었다(9절 Finding 2).

### 1.3 판정 API는 두 벌로 분리했다

| 판정 | API | 소유 클래스 |
| --- | --- | --- |
| 캐릭터 **교체** 가능 | `CharacterRoster.GetSwapBlockReason` → `SwapBlockReason` | CharacterRoster |
| 회복 **등록/드래그** 가능 | `RecoveryStation.GetRegisterBlockReason` → `RecoveryRegisterBlockReason` | RecoveryStation |

규칙이 다르기 때문에(행동력 0은 교체 불가지만 회복 등록은 가능) 하나로 합치지 않았다.

---

## 2. 변경 파일 전체

### 신규 (스크립트 13개, 전부 `Assets/Scripts/Recovery/`)

| 파일 | 역할 |
| --- | --- |
| `RecoveryBalanceTable.cs` | 밸런스 ScriptableObject. **비용/시간/슬롯 수의 단일 수정 지점** |
| `RecoveryBalance.cs` | 밸런스 값 스냅샷 구조체 + `IsValid` / `GetCost` / `GetDuration` |
| `RecoveryCharacterState.cs` | 상태 enum (파생 값, 저장하지 않음) |
| `RecoveryRegisterBlockReason.cs` | 회복 등록 차단 사유 enum |
| `RecoveryStartResult.cs` | 시작 결과 코드 enum + 결과 구조체 |
| `RecoveryCostQuote.cs` | 비용/시간 견적 구조체 |
| `RecoverySlotView.cs` | UI용 읽기 전용 슬롯 정보 |
| `IRecoveryRoster.cs` | 캐릭터 쪽 seam |
| `IRecoveryWallet.cs` | 재화 쪽 seam |
| `CharacterRosterRecoveryAdapter.cs` | `IRecoveryRoster` → `CharacterRoster` 어댑터 |
| `InventoryRecoveryWallet.cs` | `IRecoveryWallet` → `InventoryManager` 어댑터 |
| `RecoveryStation.cs` | **규칙 전체**(순수 C#) |
| `RecoveryService.cs` | 씬 컴포넌트(밸런스 연결 + Tick + 정적 이벤트) |

위 13개 `.cs` 각각의 `.meta`와 폴더 메타 `Assets/Scripts/Recovery.meta`도 함께 추가했다(총 14개).
Unity가 생성하는 정상 형식이며, 프로젝트 전체 8698개 메타의 GUID가 모두 유일함을 확인했다.

### 수정 (6개)

| 파일 | 변경 내용 |
| --- | --- |
| `Assets/Scripts/Common/SaveData.cs` | `recoverySlots` 필드 + `RecoverySlotSaveState` 클래스 + `EnsureRecoverySlots()` 추가 |
| `Assets/Scripts/Common/SaveSystem.cs` | 불러오기 시 `SaveData.EnsureRecoverySlots(data)` 1줄 호출 |
| `Assets/Scripts/Character/CharacterRoster.cs` | `SwapBlockReason.InRecovery` 추가 / `SetStamina` 회복 중 거부 / `ApplyRecoveryStamina`·`RaiseCharacterStateChanged` 추가 / **`RefillAllStaminaToMax` 제거** |
| `Assets/Scripts/Inventory/InventoryManager.cs` | `TrySpendCurrency` / `TrySpendCurrencyWithoutSave` / `RefundCurrencyWithoutSave` / `NotifyChangedAfterExternalSave` 추가 |
| `Assets/Scripts/Common/StaminaRefillTestButton.cs` | 전체 리필 동작 제거(클릭해도 아무 일 없음). 씬의 Missing Script를 만들지 않기 위해 파일은 유지 |
| `Assets/Scripts/Common/CharacterSwap/CharacterSwapPanel.cs` | `InRecovery` 사유를 "선택 불가" 표시로 매핑(프리팹/스프라이트 변경 없음) |

**씬/프리팹/에셋 변경: 0건.** `diff -r`로 클론과 저장소의 `Assets/Scenes`, `Assets/Data`가
완전히 동일함을 확인했다(4.2절).

---

## 3. 요구사항별 구현 위치

### 3-1) 회복 밸런스 단일 수정 지점

`RecoveryBalanceTable` 에셋 하나. 필드와 기본값은 요구대로다.

| 필드 | Inspector 이름 | 기본값 |
| --- | --- | --- |
| `recoveryId` | Recovery Id | `default` |
| `currencyId` | Currency Id | `Jewel` |
| `costPerMissingStamina` | Cost Per Missing Stamina | `100` |
| `secondsPerStamina` | Seconds Per Stamina | `30` |
| `maxSlots` | Max Slots | `3` |

코드 어디에도 100 / 30 / 3이 상수로 흩어져 있지 않다. 유일한 예외는 저장 계층의
`SaveData.DefaultRecoverySlotCount = 3`인데, 이것은 **"예전 저장 파일을 열 때 최소 몇 칸을
만들어 둘 것인가"**라는 저장 계층의 관심사이며, 실제 사용 슬롯 수는 언제나 테이블의 Max Slots가 정한다
(Max Slots를 5로 올리면 슬롯 목록도 5칸으로 늘어난다 — T02에서 검증).

### 3-2) 상태와 소유권

1.2절 표 참고. 요구된 규칙별 구현 위치:

| 규칙 | 구현 |
| --- | --- |
| Pending은 저장 금지 / 재화 차감 금지 / 실제 Recovering 취급 금지 | `pendingBySlot[]`(런타임 배열), `IsInRecoverySlot()`이 Pending을 제외 |
| Available: 최대 미만이면 등록 가능, 최대면 불가 | `GetRegisterBlockReasonIgnoringCapacity` → `StaminaFull` |
| Active: 등록 불가 | 같은 메서드 → `Active` |
| Exhausted(0): 교체 불가, 등록 가능 | 교체는 `SwapBlockReason.NoStamina`, 등록은 통과 |
| Recovering/RecoveryComplete: 교체 불가 | `CharacterRoster.GetSwapBlockReason` → `InRecovery` |
| Recovering/RecoveryComplete: 시작 시 Active 금지 | `CharacterRoster.ResolveStartCharacter`가 후보에서 제외. 전원 회복 중이면 `current = null` |
| Recovering/RecoveryComplete: 외부 행동력 변경 금지 | `CharacterRoster.SetStamina` 거부 + `ApplyDebugStartStamina` 제외 |
| 교체 판정과 등록 판정의 분리 | 1.3절 |

위 4개 차단은 **모두 같은 근거**(저장된 `recoverySlots`)를 본다. `RecoveryService.IsCharacterInRecovery`가
회복소가 있으면 `RecoveryStation.IsInRecoverySlot`을, 없으면 정적 `IsCharacterIdInSavedSlot`을 쓰는데
둘 다 결국 같은 정적 스캔 함수로 수렴하므로 **경로에 따라 답이 갈릴 수 없다**.

### 3-3) 회복 슬롯 / 저장

```csharp
[Serializable] public class RecoverySlotSaveState {
    public string characterId;    // 비어 있으면 빈 슬롯
    public int    startStamina;   // 시작 순간의 현재 행동력
    public string startedAtUtc;   // ISO-8601 "o" + InvariantCulture
    public string completeAtUtc;  // 같은 서식
}
```

- **Slot Index는 리스트 인덱스**다. 목록을 줄이면 번호가 밀려 다른 슬롯의 진행이 뒤바뀌므로,
  빈 슬롯도 항목을 유지한다.
- **진행률과 완료 여부는 저장하지 않는다.** 둘 다 `startedAtUtc` / `completeAtUtc`와 현재 시각으로
  파생되며, 그래야 앱을 꺼 둔 동안 흐른 시간이 그대로 반영된다.
- 예전 저장 파일 대응: `SaveSystem.EnsureLoaded`가 `SaveData.EnsureRecoverySlots(data)`를 부른다.
  항목 자체가 없어도 / `null`이어도 / 항목 중 일부가 `null`이어도 **예외 없이** 빈 슬롯 3개가 된다.
  이미 3개보다 많으면 **잘라내지 않는다**(Max Slots를 나중에 줄여도 회복 중 저장 값을 지우지 않는다).
- UTC 직렬화: `ToString("o", CultureInfo.InvariantCulture)` / `TryParseExact(..., RoundtripKind)`.
  `ar-SA`(히즈라력)에서 쓰고 `th-TH`(불기)에서 읽어도 같은 시각으로 왕복한다(T11).
- 중복 등록 방지: `TryAddPending`이 `AlreadyPending` / `AlreadyInRecovery`로 막고,
  `StartRecovery`가 시작 직전에 다시 검증한다.
- 슬롯 범위 방어: 등록 정원은 `IsSlotIndexValid`(밸런스의 Max Slots), 조회/진행/합류는
  `IsAddressableSlotIndex`(저장된 슬롯 전체)로 나눠서 본다. 범위 밖 등록은 `SlotUnavailable`이다.
  이렇게 나눈 이유는 Max Slots를 나중에 **줄였을 때** 정원 밖으로 밀려난 슬롯의 캐릭터가
  (a) 회복 중으로 계속 잠기되 (b) 진행·완료·합류는 정상적으로 되어 영구히 갇히지 않게 하기 위함이다(T19).
- 이벤트: 3-6절.

### 3-4) 계산 / 진행

| 규칙 | 구현 |
| --- | --- |
| 부족 행동력 = Max − Current | `GetMissingStamina` |
| 캐릭터 비용 = 부족 × CostPerMissingStamina | `RecoveryBalance.GetCost` (long 승격 후 포화) |
| 여러 명 비용 합산 | `long`으로 누적 후 `RecoveryStation.SaturateToInt`로 포화. 잔액 비교는 **포화 전 long 원본**으로 한다 |
| 총 시간 = 부족 × SecondsPerStamina | `RecoveryBalance.GetDuration` |
| 회복량 = floor((now − startedAt) / SecondsPerStamina) | `ComputeRecoveredSteps` |
| 현재 행동력 = min(Max, StartStamina + 회복량) | `ComputeCurrentStamina`. 단 **현재 값이 하한**이라 회복이 행동력을 깎지 않는다 |
| 매 프레임 저장 금지 | `Tick`은 **행동력이 실제로 오른 경우에만** 저장 1회. 완료 전환만 일어난 경우에는 저장하지 않는다(완료는 이미 저장된 `completeAtUtc`에서 파생되는 값이라 새로 기록할 것이 없다) |
| 완료 시 Recovering → RecoveryComplete, 자동 합류 금지 | `IsComplete` 판정만 바뀌고 슬롯은 그대로 남는다 |
| Time.time / 코루틴 누적 의존 금지 | 코드에 `Time.time`도 코루틴도 없다. `Time.unscaledDeltaTime`은 **확인 주기**로만 쓰이며 회복 속도와 무관하다 |
| 오프라인 경과 반영 | 저장된 UTC와 현재 UTC의 차이만 본다. `RecoveryService`는 `OnEnable`에서 첫 `Tick`을 즉시 돌린다 |

**데이터 오류 정책(무한 루프/예외 없음):**
`SecondsPerStamina <= 0`, `CostPerMissingStamina < 0`, `MaxSlots <= 0`, 빈 `CurrencyId` 중 하나라도
해당하면 `RecoveryBalance.IsValid == false`가 되고, 회복소는 **조용히 기본값으로 대체하지 않고 전부
멈춘다**: 등록 실패(`InvalidBalance`), 시작 실패(`InvalidBalance`), `Tick`은 즉시 반환.
0으로 나누는 지점 자체가 실행되지 않으며, 이미 회복 중이던 슬롯의 저장 값은 지우지 않는다(T10).
`RecoveryService`는 이 경우 오류 로그를 남기고 스스로 `enabled = false`가 된다.

**추가로 잡은 안전 정책 4가지:**
- **시계 역행**: 경과가 음수면 회복량 0. 그리고 계산 결과가 지금 값보다 낮아도 **내리지 않는다**
  (`ComputeCurrentStamina`가 현재 행동력을 하한으로 쓴다). 회복이 행동력을 빼앗는 일은 없다(T18).
  이 하한 규칙 때문에 **바깥에서 행동력을 올려 두면 그 값이 그대로 눌러앉는다** - 그래서 회복 중
  캐릭터에 대한 외부 변경 경로(`SetStamina`, 디버그 override)를 전부 막아야 한다(9절 Finding 3).
- **시각 문자열 손상**: 파싱 실패 시 그 슬롯만 진행을 멈추고 경고를 한 번 남긴다. 저장 값은 지우지 않는다.
- **비용 합산 오버플로**: 개인 비용은 `int.MaxValue`로 포화하지만, 그것을 `int`로 더하면 2~3명만
  모여도 음수로 넘친다(= "재화가 충분하다"는 잘못된 판정으로 회복이 공짜로 시작될 수 있다).
  합산은 `long`으로 쌓고, **잔액 비교는 포화 전 long 원본**으로 하며, 밖으로 나가는 값만
  `SaturateToInt`로 줄인다. 어떤 밸런스에서도 음수 비용이나 우회 시작이 나오지 않는다(T20/T21).
- **Tick 재진입**: 완료 이벤트를 받은 쪽이 그 자리에서 다시 `Tick`이나 합류를 부르면 순회 중인
  버퍼가 비워진다. `ticking` 가드 + Tick/합류 버퍼 분리로 막는다.

**포화 정책(문서화):** `RecoveryStation.SaturateToInt(long)`은 음수를 `0`으로, `int` 범위 초과를
`int.MaxValue`로 만든다(감기 없음). 이 값은 **표시/보고 전용**이며, 지불 가능 여부 판정에는 절대
쓰이지 않는다.

**동일 완료 시각의 결정적 순서:** 완료 이벤트는 `(완료 시각, 슬롯 번호)` 오름차순으로 발생한다
(`CompletedSlot.Compare`). 같은 시각에 3개가 끝나도 항상 슬롯 0 → 1 → 2 순서다(T13에서 3회 반복 확인).
이후 알림 단계가 순서를 임의로 정하지 않고 이 순서를 그대로 쓰면 된다.

### 3-5) Pending 및 일괄 시작 API

```csharp
bool TryAddPending(CharacterDefinition, out RecoveryRegisterBlockReason);              // 첫 빈 슬롯
bool TryAddPendingToSlot(int slot, CharacterDefinition, out RecoveryRegisterBlockReason); // 슬롯 지정
bool RemovePending(CharacterDefinition);
bool RemovePendingAtSlot(int slot);
int  ClearPending();                       // Recovering/RecoveryComplete 에는 영향 없음
RecoveryStartResult StartRecovery();
```

`StartRecovery`의 순서는 요구된 a~i 그대로 코드에 나타난다.

| 단계 | 코드 |
| --- | --- |
| a. Pending 목록 확인 | `startSlotBuffer` 채우기(슬롯 번호 오름차순) → 비면 `NoPending` |
| b. 각 캐릭터 상태 재검증 | `ValidateForStart` — 실패 시 즉시 `InvalidCharacterState` 반환 |
| c. 현재/최대 행동력 재확인 | `GetMissingStamina`가 로스터에서 다시 읽는다(화면 견적을 믿지 않는다) |
| d. 각 비용 재계산 | `balance.GetCost(missing)` |
| e. 총합 | `long totalCostRaw` (넘치지 않는 원본) |
| f. 보유 Jewel 확인 | `totalCostRaw > walletBalance` — **포화 전 long으로 비교**한다 |
| g. 충분할 때만 총액 **한 번** 차감 | `wallet.TrySpendWithoutSave(totalCost)` — 부족하면 아무것도 바꾸지 않고 false |
| h. 전원 **같은** UTC 시작 시각, 독립 완료 시각 | `startedAtText` 하나를 모든 슬롯에 쓰고 `completeAtUtc`만 각자 계산 |
| i. `SaveSystem.Save()` **정확히 1회** | `saveAction()` 한 번 |

**부분 성공 금지의 근거:**
- b~e 단계는 **읽기만** 한다. 하나라도 invalid면 재화·캐릭터·슬롯 어디에도 손대지 않은 채 반환한다.
- f/g에서 재화가 부족하면 g가 실행되지 않거나 false를 반환하고, 그 시점까지 슬롯 기록은 없다.
- i에서 저장이 실패하면 **슬롯을 `Clear()`하고 `RefundWithoutSave`로 재화를 되돌린 뒤** `SaveFailed`를
  반환한다 → 메모리와 파일이 모두 시작 전 상태다(T15).

**실패 코드 구분:** `InsufficientFunds`와 `InvalidCharacterState`는 별개 코드다. 전자는
`TotalCost`/`Balance`/`Shortfall`을 채워 주고, 후자는 `BlockedCharacter`/`BlockReason`을 채워 준다.
UI는 `InsufficientFunds`를 받으면 패널을 닫고 Pending을 지우면 된다(**이 단계에서는 구현하지 않았다**).

**기존 재화 API 조사 결과 — 진짜 원자성 확보:**
기존 `InventoryManager.AddCurrency(-금액)`은 `Mathf.Max(0, value)`로 결과를 **0으로 자른다**.
즉 300을 가진 상태에서 500을 쓰면 실패가 아니라 "잔액 0"이 되는 **부분 지불**이 일어난다.
회복 비용 지불에 그대로 쓰면 재화만 사라지고 회복은 시작되지 않는 경로가 생긴다. 그래서:

```csharp
bool TrySpendCurrency(int amount);            // 부족하면 아무것도 바꾸지 않고 false (저장 O)
bool TrySpendCurrencyWithoutSave(int amount);  // 같은 판정, 메모리만 (저장 X, 알림 X)
void RefundCurrencyWithoutSave(int amount);    // 트랜잭션 취소 전용
void NotifyChangedAfterExternalSave();         // 외부가 저장을 마친 뒤 UI 갱신만
```
저장을 분리한 이유는 재화 차감과 슬롯 기록이 한 트랜잭션이라 그 사이에 `Save()`가 두 번 일어나면
안 되기 때문이다.

### 3-6) 합류 API

```csharp
bool TryJoin(int slotIndex, out CharacterDefinition joined);  // 그 슬롯의 RecoveryComplete 한 명만
int  JoinAllCompleted();                                       // RecoveryComplete만, Recovering은 유지
```

- 완료 전 합류는 거부한다(`GetSlotState(slot) != RecoveryComplete`면 false).
- 합류 시 최종 행동력을 반영하고 슬롯을 비운다 → 상태가 `Available`로 파생된다.
- **저장은 "사용자가 누른 동작 1회당 1회"**다. `JoinAllCompleted`가 3명을 합류시켜도 `Save()`는 한 번이다
  (`ApplyJoin`이 슬롯들을 한 덩어리로 처리하고 마지막에 한 번 저장).

### 3-7) 기존 리필 경로

**조사 결과:**
- `btn_RecoveryStation` (씬 `desktopScene.unity:4512`): `Button.m_OnClick.m_PersistentCalls.m_Calls`가
  **비어 있다**. 전체 리필로 이어지는 연결은 처음부터 없었고, `pn_RecoveryStation` 패널을 여는
  `ModalPanelOpener` 경로만 있다.
- 전체 리필의 유일한 실경로는 `btn_switching` → `StaminaRefillTestButton` →
  `CharacterRoster.RefillAllStaminaToMax()`였다.
- `RuntimeCharacterSwitcher`라는 스크립트는 이 프로젝트에 **존재하지 않는다**(과거 순환 교체
  테스트 버튼이 `StaminaRefillTestButton`으로 대체되면서 사라진 것으로 보인다).

**처리(전체 리필 기능 제거):**
1. `CharacterRoster.RefillAllStaminaToMax()`와 `Debug - Refill All Stamina` 컨텍스트 메뉴를 **삭제**했다.
   이제 코드 어디에도 "모든 캐릭터를 최대치로" 만드는 공개 API가 없다.
2. `StaminaRefillTestButton`은 클릭 리스너 등록 자체를 없앴다 — **눌러도 아무 일도 일어나지 않는다.**
   파일을 지우지 않은 이유는 이 컴포넌트가 씬의 `btn_switching`에 붙어 있어, 스크립트를 지우면
   그 자리에 Missing Script가 남기 때문이다. `Awake`에서 "기능이 제거됐으니 에디터에서 정리하라"는
   경고를 한 번 남긴다. 에디터 정리 항목은 7절 체크리스트에 있다.
3. 회복 중 캐릭터의 행동력을 바깥에서 바꾸는 남은 경로도 막았다: `CharacterRoster.SetStamina`는
   회복 슬롯에 있는 캐릭터에 대해 경고를 남기고 **거부**한다. 회복소 자신은 전용 경로
   `ApplyRecoveryStamina`(메모리만, 저장·이벤트 없음)를 쓴다.
4. `SetStamina`의 정당한 내부 호출처는 그대로 동작한다: `SpendStamina`(처치 시 소비),
   `DrainCurrentStamina`(디버그) — 둘 다 대상이 전투 중이거나 대기 중인 캐릭터라
   회복 슬롯에 들어 있을 수 없다. Play Mode에서 실제로 확인했다(P03).
5. 개발용 `Override Stamina On Start`(`ApplyDebugStartStamina`)도 회복 슬롯에 있는 캐릭터를
   **건너뛴다**. 이 플래그는 회복소보다 먼저(로스터 `Awake`) 실행되므로 예외 없이 저장 슬롯 판정을
   써야 한다 - 자세한 이유는 9절 Finding 3.
6. 남은 외부 변경 경로는 없다: 회복 중 캐릭터의 행동력을 바꿀 수 있는 공개 API는 회복소 전용
   `ApplyRecoveryStamina` 하나뿐이다.

**씬 YAML은 수정하지 않았다.**

---

## 4. 검증 — 실행 명령과 결과

Unity Editor가 프로젝트 락을 잡고 있으므로 APFS 클론에서 batchmode로 실행했다.
검증용 하네스는 클론에만 두었고 저장소에는 넣지 않았다.

### 4.1 클론 생성

```bash
cp -Rc Assets Packages ProjectSettings Library <scratchpad>/verify/     # 7.4s
rm -rf <scratchpad>/verify/Library/ScriptAssemblies
```

### 4.2 컴파일 + 도메인 규칙 검증 (Edit Mode)

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath <scratchpad>/verify \
  -executeMethod RecoveryVerify.RecoveryVerification.Run \
  -logFile <scratchpad>/verify/unity.log
```

| 항목 | 결과 |
| --- | --- |
| 종료 코드 | **0** |
| `grep -c "error CS" unity.log` | **0** |
| `warning CS` (Recovery/CharacterRoster/Inventory/StaminaRefill 관련) | **0** |
| 검증 결과 | **TOTAL: 237, PASSED: 237, FAILED: 0** |

검증 코드가 저장소의 코드와 동일함을 확인:
```bash
diff -r Assets/Scripts <clone>/Assets/Scripts   # 차이는 Unity가 생성한 .meta 파일뿐
diff -r Assets/Data    <clone>/Assets/Data      # exit 0 (동일)
diff -r Assets/Scenes  <clone>/Assets/Scenes    # exit 0 (동일 = 씬 미변경 증명)
```

**요구된 검증 항목과 대응하는 테스트:**

| 요구 검증 항목 | 테스트 | 결과 |
| --- | --- | --- |
| 기존 저장 데이터 역직렬화 → 빈 3 슬롯 | T01, T02 | PASS (기존 필드 유실 없음까지 확인) |
| 1/2/3인 비용 합산 및 단일 차감 / Save 1회 | T03 | PASS (300 / 700 / 800, Save 각 1회) |
| 3명 중 1명 invalid → 모두 실패 / 차감 0 / 영속 변화 0 | T04 | PASS (Save 0회, 슬롯 점유 0) |
| Jewel 부족 → 모두 실패 / 차감 0 / 기존 Recovering 유지 | T05 | PASS (기존 슬롯의 시작 시각까지 동일) |
| 동일 캐릭터 중복 등록 거부 | T06 | PASS (Pending 중 / 회복 중 둘 다) |
| 30초 단위 단계 증가 / 완료 전환 / 오프라인 경과 | T07 | PASS (29·30·59·60·90초 경계, 3시간 오프라인) |
| 슬롯별/전체 합류, 전체 합류 시 Recovering 유지 | T08 | PASS |
| Pending 미저장 | T09 | PASS (JSON 직렬화 결과까지 확인) |
| seconds ≤ 0 등 데이터 오류에서 안전 | T10 | PASS (무한 루프·예외 없음, 저장 값 보존) |
| UTC 문화권 독립 직렬화 | T11 | PASS (ar-SA ↔ th-TH 왕복) |
| Active 우선 판정 | T12 | PASS |
| 동일 완료 시각의 결정적 순서 | T13 | PASS (3회 반복 동일) |
| 최대치 / 빈 슬롯 없음 / 범위 밖 슬롯 | T14 | PASS |
| 저장 실패 시 전체 롤백 | T15 | PASS |
| 대기 취소가 회복 중에 영향 없음 | T16 | PASS |
| 재시작 후 Recovering/RecoveryComplete 유지 | T17 | PASS (실제 JSON 왕복) |
| 시계 역행 / 손상 데이터 | T18 | PASS |
| 저장 슬롯 기반 정적 판정(회복소 없이도 동일한 답) | T19 | PASS (null 안전, 저장 데이터 불변, 정원 밖 슬롯 포함) |
| 3명 비용 포화 합산이 음수가 되지 않음 / 우회 시작 불가 | T20 | PASS (`int.MaxValue` 비용 3명 → 재화 부족 전체 실패, 차감 0) |
| 포화 정책 경계값 | T21 | PASS |

### 4.3 Play Mode 통합 검증 (실제 씬 + 실제 저장 파일)

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath <scratchpad>/verify \
  -executeMethod RecoveryVerify.RecoveryPlayModeVerification.Setup \
  -logFile <scratchpad>/verify/playmode.log
```

`desktopScene.unity`를 실제로 열고 Play Mode에 진입해, 실제 `CharacterRoster` / `InventoryManager` /
`SaveSystem`(실제 저장 파일)로 검증했다.

| 항목 | 결과 |
| --- | --- |
| 종료 코드 | **0** |
| 검증 결과 | **TOTAL: 90, PASSED: 90, FAILED: 0** |
| 예기치 않은 예외/Assert | **0건** |

| 구역 | 확인 내용 |
| --- | --- |
| P01 | 씬 초기화 회귀 없음 (Roster/Inventory 생성, 전투 캐릭터 `CatKnight` 결정, 로스터 6명) |
| P02 | **실제 사용자 저장 파일**(회복소 필드 없음)이 빈 3 슬롯으로 열림. `characters=6, items=3, currency=10400` 그대로 유지 |
| P03 | 회복소가 **없는** 씬에서도 기존 흐름 정상: `SetStamina` / `SpendStamina` / `CurrentCharacterCanAct` / `GetSwapBlockReason` / `TrySwitchTo` / `AddCurrency`. `IsCharacterInRecovery`가 NullReference 없이 false |
| P04 | 재화 차감 원자성: 잔액 초과 시 실패 + 잔액 불변, 성공 시 정확히 차감, 환불 동작 |
| P05 | 실제 컴포넌트로 회복소 end-to-end: 기본값(3/100/30/Jewel) 확인 → 등록 → Pending 미저장 → 시작 → 총액 차감 → **실제 저장 파일에 `recoverySlots` 기록됨** |
| P06 | 회복 중 캐릭터: 교체 차단(`InRecovery`), `TrySwitchTo` 거부, `SetStamina` 거부, 완료 전 합류 거부, 재등록 거부 |
| P07 | 정리 후 상태가 `Available`로 복귀, 교체 가능 |
| P08 | **재시작 시 회복 중이던 default가 Active가 되지 않는다.** 저장 슬롯에 `CatKnight`(default)를 넣고 로스터의 `Awake`를 다시 태우면 `ElfArcher`(Entries 순서상 첫 비회복 캐릭터)가 결정적으로 선택된다. 회복이 끝나 행동력이 최대가 돼도 Active로 승격되지 않고 교체 차단이 유지된다 |
| P09 | **보유 캐릭터 3명이 전부 회복 슬롯이면 아무도 켜지지 않는다.** `Current == null`, `CurrentCharacterCanAct == false`, 캐릭터 오브젝트 3개 모두 비활성, `DrainCurrentStamina`/`SpendStamina(null)`도 예외 없음 |
| P10 | **회복소가 없거나 밸런스 오류로 비활성화돼도** 교체 차단(`InRecovery`)과 `SetStamina` 거부가 그대로 동작한다(저장 슬롯 폴백) |
| P11 | **`Override Stamina On Start`가 회복 중 캐릭터를 건드리지 않는다.** 일반 캐릭터만 덮어쓰고, 회복 중 캐릭터의 행동력은 그대로다 |
| P12 | 슬롯을 모두 비우면 로스터가 정상 상태로 복귀한다 |

P08~P12는 `CharacterRoster.Awake`의 **실제 경로**를 다시 태워서 확인했다(비활성 GameObject에
`AddComponent` → Entries/Default 주입 → 활성화 순서로 `Awake`를 원하는 저장 상태 위에서 재실행).
저장 데이터를 그대로 둔 채 로스터만 다시 만드는 것이라 "앱 재시작"과 같은 조건이다.

검증 실행 전 사용자의 실제 저장 파일
(`~/Library/Application Support/Rell/desktop_RPG/playerprogress.json`)을 백업했고,
검증 후 **바이트 단위로 동일하게 복원**했다(`diff` 결과 무차이).

### 4.4 메타 파일 검증

```bash
# 모든 .cs 에 .meta 가 있고, 짝 없는 .meta 가 없는지
for f in Assets/Scripts/Recovery/*.cs;   do [ -f "$f.meta" ] || echo "MISSING: $f.meta"; done
for m in Assets/Scripts/Recovery/*.meta; do [ -f "${m%.meta}" ] || echo "ORPHAN: $m"; done
# 프로젝트 전체 GUID 중복
grep -rh "^guid: " --include="*.meta" Assets/ | sort | uniq -d
```

| 항목 | 결과 |
| --- | --- |
| 폴더 메타 `Assets/Scripts/Recovery.meta` | 존재 |
| 스크립트 메타 | 13/13 존재, 짝 없는 메타 0개 |
| 프로젝트 전체 메타 수 / 고유 GUID 수 | **8698 / 8698** (중복 0) |

### 4.5 검증 불가 항목 (명시)

- **Windows Player 빌드 / 실제 Win32 키보드 훅**은 macOS 개발 머신에서 확인할 수 없다.
  다만 이번 변경은 `Assets/Scripts/DesktopWindow/*`를 전혀 건드리지 않았고, 회복소는 P/Invoke를
  쓰지 않는 순수 관리 코드다.
- **회복 완료까지의 실시간 대기(30초/캐릭터)**는 Play Mode 하네스에서 실제로 기다리지 않았다.
  시간 경과 로직 자체는 clock seam으로 Edit Mode에서 전부 검증했다(T07/T13/T17/T18).
- **UI 표시/드래그/패널 동작**은 이번 범위가 아니라 검증하지 않았다.

---

## 5. `SaveSystem.Save` 호출 횟수 검증 근거

검증 하네스는 실제 `SaveSystem.Save`를 카운터가 붙은 seam(`() => { SaveCount++; return ok; }`)으로
바꿔 넣고 호출 횟수를 직접 셌다. 아래는 그 계측 결과다.

| 동작 | 기대 | 실측 | 테스트 |
| --- | --- | --- | --- |
| Pending 등록 1~2명 | 0 | **0** | T09 |
| Pending 전체 취소 | 0 | **0** | T09 |
| 시작 (1명, 총액 300) | 1 | **1** | T03 |
| 시작 (2명, 총액 700) | 1 | **1** | T03 |
| 시작 (3명, 총액 800) | 1 | **1** | T03 |
| 시작 실패 (invalid 상태) | 0 | **0** | T04 |
| 시작 실패 (재화 부족) | 0 | **0** | T05 |
| 시작 중 저장 실패 | 1회 시도 후 전체 롤백 | **1** (롤백 확인) | T15 |
| 29초 경과, `Tick` 3회 | 0 | **0** | T07 |
| 30초 경계 `Tick` (한 단계 상승) | 1 | **1** | T07 |
| 같은 시각에 `Tick` 반복 | 추가 0 | **추가 0** | T07 |
| 완료 전환만 일어난 `Tick` | 0 | **0** | T07 |
| 완료 후 계속 `Tick` | 0 | **0** | T07 |
| 슬롯별 합류 1회 | 1 | **1** | T08 |
| 전체 합류 1회 | 1 | **1** | T08 |
| 비용 합산 오버플로로 재화 부족 | 0 | **0** | T20 |
| 디버그 override가 회복 중 캐릭터만 있어 바꿀 값이 없을 때 | 0 | **0** | `applied > 0`일 때만 저장 |

핵심 근거 2가지:
- `Tick`은 `staminaChangedBuffer.Count > 0`일 때만 `saveAction()`을 부른다. 슬롯 3개가 같은
  프레임에 동시에 한 단계씩 올라도 **파일 쓰기는 1회**다(값 변경은 전부
  `ApplyRecoveryStamina`로 메모리에서 끝내고 마지막에 한 번만 저장).
- `ApplyJoin`은 대상 슬롯 전체를 처리한 뒤 마지막에 한 번만 저장한다. "버튼 1회 = 저장 1회"가
  전체 합류에도 성립한다.

---

## 6. 2단계 UI가 연결해야 할 정확한 API

진입점은 `Recovery.RecoveryService.Station`(없으면 `null` → 회복소 패널을 열지 않는다).

### 6.1 조회 (그리기)

```csharp
RecoveryStation s = RecoveryService.Station;

int  s.SlotCount                                   // 슬롯 칸 수 (밸런스가 정한다)
int  s.FreeSlotCount                               // 지금 올릴 수 있는 빈 칸 수
int  s.PendingCount
bool s.IsOperational                               // false면 패널을 열지 않는다
RecoveryBalance s.Balance                          // 비용/시간 표시용

RecoverySlotView s.GetSlot(int slotIndex)          // 슬롯 한 칸 전체 정보
    → .SlotIndex .Character .State .CurrentStamina .MaxStamina
      .StartedAtUtc .CompleteAtUtc .Remaining .Cost
      .IsEmpty .IsPending .CanJoin

RecoveryCharacterState s.GetState(CharacterDefinition)   // 캐릭터 리스트 항목 표시용
RecoveryCharacterState s.GetSlotState(int slotIndex)
bool s.IsInRecoverySlot(CharacterDefinition)
```

### 6.2 판정 (드래그/버튼 활성)

```csharp
RecoveryRegisterBlockReason s.GetRegisterBlockReason(CharacterDefinition)  // 드래그 가능 판정
bool s.CanRegister(CharacterDefinition)
int  s.GetMissingStamina(CharacterDefinition)
bool s.TryGetQuote(CharacterDefinition, out RecoveryCostQuote)             // 1명 견적
RecoveryCostQuote s.GetPendingQuote()                                      // 합계 견적 (Cost / LongestDuration / CharacterCount)

// 캐릭터 교체 판정은 회복소가 아니라 기존 API를 쓴다
CharacterRoster.Instance.GetSwapBlockReason(def) == CharacterRoster.SwapBlockReason.InRecovery
```

### 6.3 조작

```csharp
bool s.TryAddPending(def, out RecoveryRegisterBlockReason)                 // 첫 빈 칸
bool s.TryAddPendingToSlot(slotIndex, def, out RecoveryRegisterBlockReason) // 특정 칸에 드롭
bool s.RemovePending(def)  /  bool s.RemovePendingAtSlot(slotIndex)
int  s.ClearPending()                                                      // 패널 닫을 때

RecoveryStartResult s.StartRecovery();
//   .IsSuccess / .Code / .TotalCost / .Balance / .Shortfall
//   .BlockedCharacter / .BlockReason
//   Code == InsufficientFunds  → 패널을 닫고 ClearPending() 호출 (2단계 작업)
//   Code == InvalidCharacterState → BlockedCharacter/BlockReason으로 사유 표시

bool s.TryJoin(slotIndex, out CharacterDefinition joined)
int  s.JoinAllCompleted()
```

### 6.4 이벤트 (정적 — 패널이 열려 있지 않아도 구독 가능)

```csharp
RecoveryService.SlotsChanged        // Action           : 패널 전체 다시 그리기
RecoveryService.StaminaStepChanged  // Action<Def,int,int> : (캐릭터, 현재, 최대)
RecoveryService.RecoveryCompleted   // Action<int,Def>  : (슬롯 번호, 캐릭터) — 3단계 알림이 쓸 신호
                                    //   같은 시점 다중 완료는 (완료 시각, 슬롯 번호) 오름차순 보장
CharacterRoster.CharacterStateChanged  // 기존 이벤트. 회복 중 행동력 단계 상승도 여기로 전달된다
                                       // → 기존 캐릭터 리스트/행동력 표시는 추가 연결 없이 그대로 갱신된다
```

---

## 7. Inspector / Editor 연결 체크리스트 (사용자 작업)

이번 단계에서는 씬/프리팹/에셋을 건드리지 않았으므로, 아래는 **Unity Editor에서 직접 해야 하는
작업 목록**이다. 이 작업들을 하기 전까지 회복소는 씬에서 동작하지 않는다(코드만 준비된 상태).

| # | 작업 | 위치 | 비고 |
| --- | --- | --- | --- |
| 1 | `Recovery Balance Table` 에셋 생성 | Project 창 우클릭 → `Create > Recovery > Recovery Balance Table` → `Assets/Data/Recovery/RecoveryBalanceTable.asset` 로 저장 | 필드 기본값이 이미 `default / Jewel / 100 / 30 / 3`이라 **값을 고칠 필요 없다** |
| 2 | `RecoveryService` 컴포넌트 배치 | 씬의 관리자 오브젝트(예: `CharacterRoster`/`InventoryManager`가 붙어 있는 오브젝트)에 `Add Component > Recovery Service` | 씬에 **하나만** 둔다 |
| 3 | `RecoveryService`의 `Balance Table` 필드에 1번 에셋 연결 | Inspector | 비어 있으면 오류 로그 + 자동 비활성화된다 |
| 4 | (선택) `Tick Interval Seconds` 확인 | Inspector, 기본 `0.25` | 화면 갱신 주기일 뿐 회복 속도와 무관 |
| 5 | **`btn_switching`의 `Stamina Refill Test Button` 컴포넌트 제거** | 씬 → ControlDock → `btn_switching` | 현재는 눌러도 아무 동작 없음 + 시작 시 경고 로그. 제거하면 로그도 사라진다 |
| 6 | **`btn_switching` 오브젝트 자체 제거 여부 결정** | 같은 위치 | 테스트용 버튼이라 UI에서 빼는 것이 자연스럽다. 제거하면 5번도 자동 해결 |
| 7 | 5·6번 정리 후 `Assets/Scripts/Common/StaminaRefillTestButton.cs` 파일 삭제 | Project 창 | **씬에서 컴포넌트를 먼저 뗀 뒤에** 지운다(먼저 지우면 Missing Script가 남는다) |
| 8 | `btn_RecoveryStation`의 `onClick` 확인 | 씬 `btn_RecoveryStation` | 조사 결과 이미 비어 있다(전체 리필 연결 없음). 2단계에서 회복소 패널 열기를 연결하면 된다 |
| 9 | `CharacterRoster`의 `Override Stamina On Start` 가 꺼져 있는지 확인 | Inspector, Debug 섹션 | 켜져 있으면 매 실행 행동력을 덮어쓴다. **회복 중 캐릭터는 이제 제외되므로 회복 데이터를 망가뜨리지는 않지만**, 나머지 캐릭터 값이 매 실행 덮어써지므로 실제 검증 전에는 꺼야 한다 |

**주의:** 1~3번을 하기 전에는 `RecoveryService.Station`이 `null`이므로, 기존 게임은 회복소가 없던
때와 완전히 동일하게 동작한다(P03에서 확인). 즉 이 체크리스트를 나중에 해도 기존 기능은 깨지지 않는다.

---

## 8. 미해결 위험 / 후속 논의 항목

### 8-1) Active 우선 규칙의 소프트락 가능성 (기록된 위험)

전투 중인 캐릭터는 행동력이 0이어도 `Active`라 회복 등록이 불가하다(Coordinator 결정 Q1=a,
사용자 명세의 "Active 등록 불가" + MVP 제외 항목 "현재 Active 캐릭터의 즉시 회복소 등록"에 근거).

그 결과 다음 상황에서 **회복을 시작할 방법이 없다**:
- 보유 캐릭터가 1명뿐이고 그 캐릭터의 행동력이 0인 경우
- 또는 나머지 캐릭터가 전부 행동력 0(교체 대상이 될 수 없음)이면서 동시에 회복 중인 경우

이번 범위에서 임의로 확장하지 않았다. 완화 방향(추후 결정 필요):
- (a) 회복소 UI에 "먼저 다른 캐릭터로 교체하세요" 안내를 명시적으로 띄운다(2단계 UI 범위).
- (b) `Exhausted`이면서 `Active`인 경우에 한해 등록을 허용하도록 규칙을 완화한다.
- (c) 행동력 0인 캐릭터도 교체 대상으로 허용한다(현재는 `NoStamina`로 차단).

### 8-2) 재화 종류가 하나뿐이다

현재 게임의 재화는 `SaveData.currency` 전역 값 하나뿐이라, 밸런스 테이블의 `Currency Id`("Jewel")는
**대조용 키**로만 쓰인다(`InventoryRecoveryWallet`이 그 id를 그대로 들고 있고, `StartRecovery`가
시작 직전에 문자열을 비교해 다르면 `InvalidBalance`로 막는다). 재화가 여러 종류가 되면
`InventoryRecoveryWallet`이 종류별로 갈라져야 한다. 지금 미리 만들지 않았다.

### 8-3) 회복 중 `Max Stamina`를 낮추면

저장된 `startStamina`가 새 최대치보다 클 수 있다. `ComputeCurrentStamina`가 최대치로 자르므로
값이 튀지는 않고, `IsComplete`가 "최대치 도달"도 완료 조건으로 보기 때문에 슬롯이 영원히 끝나지 않는
상태로 남지도 않는다. 다만 사용자가 낸 비용보다 적게 회복되는 것은 사실이다(밸런스 변경 시의 일반적 문제).

### 8-4) `CharacterSwapPanel`의 `InRecovery` 표시

프리팹/스프라이트를 건드릴 수 없어 "행동력 소진"과 같은 시각 표시(`DisplayState.Exhausted`)를
공유한다. 고를 수 없는데 `Ready`로 보이는 상태를 만들지 않는 것이 우선이라 이렇게 두었지만,
2단계에서 회복 중 전용 표시(아이콘/남은 시간)를 추가하는 것이 바람직하다.

### 8-5) 밸런스 테이블 에셋이 아직 없다

7절 1번 작업 전까지 `RecoveryService`는 오류 로그를 남기고 비활성화된다. 이는 의도된 동작이다
(조용히 기본값으로 대체하지 않는다 — 프로젝트의 "missing data = 오류 + 비활성화" 규칙과 같다).

### 8-6) 완료 알림은 아직 없다 — 그리고 **1회성 보장은 3단계가 반드시 해결해야 한다**

`RecoveryService.RecoveryCompleted`는 발생하지만 아무도 구독하지 않는다. 3단계 알림
(`SystemNotificationManager`)이 이 이벤트를 구독하면 되며, 다중 완료의 순서는 이미 결정적으로
보장돼 있다(3-4절).

> **✅ 3단계에서 해결됨 (2026-07-31).** `RecoverySlotSaveState.completionNotified`(저장되는 per-cycle
> marker)가 도입되어 회복 주기당 알림 1회가 보장된다. 코드의 `TODO(3단계 알림)` 주석도 제거됐고,
> `completionReported`는 "같은 실행 안에서 도메인 이벤트를 슬롯당 한 번만 내보내는 guard"로 역할이
> 좁혀졌다. 자세한 내용은 `recovery-station-phase3-report.md` 2.1~2.2절 참고.
> 아래는 **1단계 완료 시점의 사실**로 보존한다.
>
> **⚠ (1단계 시점) 3단계가 반드시 처리해야 할 미해결 항목 (Finding 5):**
> 완료 이벤트를 슬롯당 한 번만 보내는 표시(`RecoveryStation.completionReported`)는 **런타임 전용**이라
> 저장되지 않는다. 그래서 **합류하지 않은 채 앱을 껐다 켜면 같은 회복 주기에 대해 완료 알림이 다시
> 발생한다.** 사용자 요구는 "회복 주기당 알림 1회"이므로, 이대로 3단계 알림을 붙이면 요구가 깨진다.
>
> 이번 단계에서 저장 스키마를 미리 만들지 않은 이유는 **알림 쪽이 무엇을 키로 삼을지가 아직 정해지지
> 않았기 때문**이다(슬롯 번호? 회복 주기 id? 캐릭터 id + 완료 시각?). 지금 추측으로 필드를 넣으면
> 3단계에서 다시 바꿔야 한다.
>
> **3단계 알림 설계가 이 문제를 소유한다.** 영속 notified marker(또는 동등한 수단)를 정의하고
> `SaveData.RecoverySlotSaveState`에 필드를 추가하거나 별도 저장 항목을 두어야 한다. 코드에도
> 같은 내용의 `TODO(3단계 알림)` 주석을 `RecoveryStation.completionReported` 선언부에 남겨 두었다.

### 8-7) Max Slots를 줄이면 정원 밖 슬롯이 생긴다

`EnsureRecoverySlots`는 목록을 잘라내지 않으므로, Max Slots를 5 → 3으로 줄이면 슬롯 3·4에 남아 있던
캐릭터가 등록 정원 밖에 놓인다. 이 캐릭터들은 (a) 회복 중으로 계속 잠기고 (b) 진행·완료·전체 합류로
정상 회수된다(T19에서 검증). 다만 **새로 등록할 수 있는 칸은 줄어든 정원까지**이므로, 정원 밖 슬롯이
비워지기 전까지는 실제 동시 회복 인원이 Max Slots보다 많을 수 있다. 운영상 Max Slots는 회복이 모두
끝난 뒤에 바꾸는 것이 안전하다.

---

## 9. 검토 보정 기록 (초판 이후)

코드 리뷰에서 나온 finding 5건을 모두 반영했다. 아래는 "무엇이 문제였고, 왜 그게 문제이며,
어떻게 고쳤는가"의 기록이다.

### Finding 1 — Unity `.meta` 누락

**문제:** 신규 `Assets/Scripts/Recovery/` 폴더와 그 안의 `.cs` 13개에 `.meta`가 없었다. 검증 클론에서는
Unity가 자동 생성했지만 저장소에는 없어서, 사용자의 Editor가 새로 임포트할 때마다 **다른 GUID**가
생길 수 있었다. 씬/프리팹이 스크립트를 GUID로 참조하므로 버전관리 관점에서 불안정하다.

**수정:** 검증 클론에서 Unity가 실제로 생성한 메타를 그대로 저장소에 복사했다(폴더 메타 1 + 스크립트
메타 13 = 14개). 형식은 Unity 표준 `MonoImporter`/`DefaultImporter`다. 프로젝트 전체 8698개 메타의
GUID가 모두 유일함을 확인했고, 짝 없는 메타도 없다(4.4절).

**보고서 정정:** 신규 파일 표의 머리말이 "11개"였으나 실제로는 13개를 나열하고 있었다 → "스크립트
13개"로 고쳤다.

### Finding 2 — 재시작 시 Recovering 캐릭터가 Active가 되는 치명적 회귀 ⚠

**문제 (가장 심각):** `CharacterRoster.Awake`는 `RecoveryService`보다 **먼저** 실행되면서
`ResolveStartCharacter`로 default(또는 첫 번째) 캐릭터를 Active로 켰다. 그런데 그 캐릭터가 지난 실행에서
`recoverySlots`에 저장돼 있어도 **`ResolveStartCharacter`는 그것을 보지 않았다.** 결과적으로:

1. 회복 중인 캐릭터가 시작하자마자 전투에 투입된다.
2. `RecoveryService`가 뒤늦게 Tick을 돌려 행동력이 1 이상이 된다.
3. `CurrentCharacterCanAct`가 true가 되어 **회복 중인 캐릭터로 공격할 수 있다** — 사실상 자동 합류다.

`GetSwapBlockReason`/`SetStamina`도 `RecoveryService.Station`이 null이면 차단이 통째로 풀렸다.
회복소가 아직 만들어지기 전이거나 밸런스 에셋이 비어 서비스가 비활성화된 경우가 모두 여기 해당한다.

**수정 — 판정 근거를 하나로 모았다:**

```csharp
// RecoveryStation (정적, 저장 데이터만 본다. 저장 데이터를 고치지 않고 null 에도 안전)
public static int  IndexOfSavedSlot(SaveData data, string characterId);
public static bool IsCharacterIdInSavedSlot(SaveData data, string characterId);

// RecoveryService — 회복소가 있으면 station, 없으면 위 정적 판정. 둘 다 같은 스캔으로 수렴한다.
public static bool IsCharacterInRecovery(CharacterDefinition definition);
```

`RecoveryStation.IsInRecoverySlot`도 같은 스캔을 쓰므로 **경로에 따라 답이 갈릴 수 없다.**
그 위에서 네 곳을 고쳤다.

| 위치 | 변경 |
| --- | --- |
| `ResolveStartCharacter` | 회복 슬롯에 있는 캐릭터를 후보에서 제외. default가 회복 중이면 Entries 순서상 **첫 비회복 캐릭터**를 결정적으로 선택. 전원 회복 중이면 `null` 반환 + 경고 |
| `ApplyActiveCharacter` | `null`을 정상 입력으로 받도록 수정. 모든 캐릭터 오브젝트를 끄고 `current = null`로 두며 이벤트도 정상 발생(구독자 3곳 모두 null 안전을 확인) |
| `CurrentCharacterCanAct` | 판정을 셋으로 분리. 로스터 없음/Entries 비어 있음 → `true`(기존 씬 호환 유지), **캐릭터는 있는데 `current == null` → `false`**(예전에는 이것도 true라 아무도 없는 채 공격 입력이 통했다), 그 외 → 행동력 > 0 |
| `GetSwapBlockReason` / `SetStamina` | `RecoveryService.IsCharacterInRecovery`를 그대로 쓰되, 그 메서드가 이제 저장 폴백을 갖는다 |

**유지한 것:** 현재 Active 캐릭터의 회복 등록 금지 규칙(Q1=a)은 그대로다. 자동 합류도 없다 —
회복이 끝난 캐릭터는 여전히 사용자가 합류를 눌러야 Available이 된다.

**검증:** P08(default가 회복 중 → `ElfArcher`가 결정적으로 선택, 회복 완료 후에도 Active 승격 없음),
P09(3명 전원 회복 → `Current == null`, 공격 불가, 오브젝트 전부 비활성, 예외 0),
P10(서비스 없음/invalid balance에서도 교체·`SetStamina` 차단), T19(정적 판정 일관성).

### Finding 3 — `Override Stamina On Start`가 회복 중 값을 덮어씀

**문제:** `ApplyDebugStartStamina`는 `RecoveryService`보다 먼저 실행되면서 **모든** 캐릭터의 행동력을
덮어썼다. 초판 보고서는 "다음 Tick에서 원래 값으로 돌아온다"고 썼지만 **사실이 아니었다** —
`ComputeCurrentStamina`가 **현재 값을 하한**으로 삼기 때문에(회복이 행동력을 깎지 않는다는 규칙,
T18에서 추가된 정책) 덮어쓴 값이 그대로 눌러앉는다. 특히 최대치로 덮으면 `IsComplete`의
"최대치 도달" 조건이 즉시 참이 되어 **회복이 공짜로 끝난다.**

**수정:** `ApplyDebugStartStamina`가 저장 슬롯 판정으로 회복 중/완료 캐릭터를 **건너뛴다.**
실제로 바꾼 캐릭터가 하나도 없으면 저장도 하지 않는다. 경고 문구도 실제 동작에 맞게 바꿨다
("캐릭터 N명의 행동력을 덮어썼습니다(회복소에 있는 M명은 제외)").

**유지한 것:** 외부 전체 리필 제거(초판 3-7절)와 기존 전투의 정상 `SetStamina`/`SpendStamina` 경로는
그대로다.

**검증:** P11.

### Finding 4 — 다중 비용 `int` 합산 오버플로

**문제:** `RecoveryBalance.GetCost`는 개인 비용을 `int.MaxValue`로 포화시키지만,
`StartRecovery`와 `GetPendingQuote`가 그 값을 `int totalCost +=`로 더했다. 2~3명만 모여도 **음수로
넘친다.** 음수 총액은 `totalCost > walletBalance` 비교를 통과하므로, 극단적인 밸런스 값에서
**재화를 거의 내지 않고 회복이 시작될 수 있었다.**

**수정:**
- 합산을 `long`으로 바꿨다(`long totalCostRaw`, `long totalCost`/`totalMissing`).
- **잔액 비교는 포화 전 long 원본**(`totalCostRaw > walletBalance`)으로 한다 — 이것이 우회 시작을
  막는 핵심이다.
- 밖으로 나가는 값만 `RecoveryStation.SaturateToInt(long)`로 줄인다. UI 반환 구조의 `int Cost`는
  요구대로 그대로 뒀다.

**포화 정책(문서화):** 음수 → `0`, `int` 범위 초과 → `int.MaxValue`(감기 없음). 포화된 값은
표시/보고 전용이며 지불 가능 여부 판정에는 쓰이지 않는다.

**검증:** T20(개인 비용이 `int.MaxValue`로 포화하는 밸런스 + 3명 → 견적/총액 모두 음수 아님,
잔액이 `int.MaxValue`여도 재화 부족으로 전체 실패, 차감 0, Save 0회, 슬롯 점유 0),
T21(경계값 6종).

### Finding 5 — 3단계 알림의 1회성 (이번 단계에서는 구현하지 않음)

**문제:** `completionReported`가 런타임 전용이라, 합류하지 않은 채 앱을 다시 켜면 같은 회복 주기에
대해 완료 알림이 다시 발생한다. 사용자 요구는 회복 주기당 알림 1회다.

**이번 단계 처리:** 요청대로 알림은 구현하지 않았고, 저장 스키마도 추측으로 추가하지 않았다.
대신 **명시적 TODO와 위험을 남겼다:**
- 코드: `RecoveryStation.completionReported` 선언부에 `TODO(3단계 알림)` 주석(무엇이 문제이고 왜
  지금 스키마를 만들지 않는지, 누가 소유해야 하는지).
- 보고서: **8-6절**에 경고 블록.

3단계 알림 설계가 영속 notified marker(또는 동등한 수단)를 정의하고 소유해야 한다.

### 그 외 — Finding 2 수정 과정에서 함께 정리한 것

`IsInRecoverySlot`이 저장 슬롯 **전체**를 보게 되면서, Max Slots를 줄였을 때 정원 밖으로 밀려난
슬롯의 캐릭터가 "회복 중이 아님"으로 잘못 판정돼 이중 등록되던 구멍이 막혔다. 다만 그대로 두면
그 캐릭터가 합류 불가로 영구히 갇히므로, 슬롯 번호 판정을 두 벌로 나눴다.

- `IsSlotIndexValid` — 새 등록 정원(밸런스 Max Slots)
- `IsAddressableSlotIndex` — 조회·진행·합류(저장된 슬롯 전체)

`Tick`과 `JoinAllCompleted`도 저장된 슬롯 전체를 훑는다. `completionReported`는 고정 길이 배열에서
`HashSet<int>`로 바꿨다. 운영상의 주의는 8-7절에 적었다.
