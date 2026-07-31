# 회복소 MVP 3단계 (완료 알림·통합·회귀 검증) 구현 완료 보고서

작업일: 2026-07-31
범위: 회복 완료 알림 연결 / 알림 동적 포맷 인자 / per-cycle 알림 marker / 오프라인 완료 처리 /
통합 회귀. 1·2단계 위에 얹었다.
전제: 씬(`desktopScene.unity`), 프리팹, 스프라이트, UI 레이아웃, **기존 Localization 에셋은 한 줄도
변경하지 않았다** — `diff -r`로 `Assets/Scenes`, `Assets/Art`, `Assets/Localization`, `Assets/Data`가
무변경임을 확인했다(5절).

> **씬 연결은 아직 없다.** 이번 단계 산출물도 "에디터에서 연결하면 동작하는 컴포넌트"까지다.
> 화면에서 실제 알림을 보려면 **6절 체크리스트**(Definition 에셋 생성 + Table key 19 확인 +
> Notifier 배치)를 사용자가 Editor에서 수행해야 한다. 연결 전에도 기존 게임은 그대로 동작한다.

---

## 1. 변경 파일

### 신규 (2개)

| 파일 | 붙는 위치 | 책임 |
| --- | --- | --- |
| `Assets/Scripts/Recovery/RecoveryCompletionNotifier.cs` | 씬 상주 오브젝트 | 완료된 슬롯을 알림 요청으로 바꾸고 marker를 확정. 회복소 패널과 무관하게 동작 |
| `Assets/Scripts/Recovery/RecoveryCompletionNotice.cs` | — | "알림이 필요한 완료 슬롯" 값 + 정렬 규칙(`Compare`) |

각 `.cs`의 `.meta`를 함께 추가했다. 프로젝트 전체 8707개 메타의 GUID가 모두 유일하다.

### 수정 (5개)

| 파일 | 변경 |
| --- | --- |
| `Assets/Scripts/Common/SaveData.cs` | `RecoverySlotSaveState.completionNotified`(per-cycle marker) 추가, `Clear()`에서 초기화 |
| `Assets/Scripts/Recovery/RecoveryStation.cs` | 새 주기 시작 시 marker 초기화, `CollectPendingCompletionNotices` / `MarkCompletionNotified` 추가 |
| `Assets/Scripts/Common/Notification/SystemNotificationManager.cs` | `Show(definition, params object[] arguments)` 오버로드 추가 |
| `Assets/Scripts/Common/Notification/SystemNotificationItemView.cs` | 인자 사본 보관 + 포맷 적용, 포맷 실패 안전 처리, `CurrentMessageText`/`MessageArgumentCount` 노출 |
| `Assets/Scripts/Recovery/UI/RecoveryStationPanel.cs` | 버튼 명칭 정정(`btn_StartRecovery` / `btn_cancel`) — tooltip/오류 문구만, 로직 무변경 |

2단계 보고서(`recovery-station-phase2-report.md`)의 버튼 명칭도 실제 명칭으로 정정했다.

---

## 2. 상태 / 저장 / 알림 순서

### 2.1 알림 1회를 보장하는 근거는 "이벤트 횟수"가 아니라 "저장된 marker"다

```
RecoverySlotSaveState
  characterId / startStamina / startedAtUtc / completeAtUtc
  completionNotified   <- 이번 회복 주기의 완료 알림을 이미 요청했는가 (신규)
```

슬롯 하나가 곧 회복 주기 하나이므로 이 bool이 per-cycle marker다. 알림 대상 판단이 전부 이 값으로
수렴하기 때문에 다음이 **자동으로** 보장된다.

| 상황 | 왜 반복되지 않나 |
| --- | --- |
| 매 프레임 Tick/Flush | marker가 서 있으면 수집 대상에서 빠진다 |
| 앱 재시작(합류 전) | marker는 저장 파일에 남는다 |
| 이벤트 중복 구독 / `OnEnable` 반복 | 요청 대상은 marker가 정하므로 이벤트를 여러 번 받아도 한 번만 |
| 도메인 재초기화(새 `RecoveryStation`) | 런타임 상태가 아니라 저장 데이터를 읽으므로 동일 |
| 앱이 꺼진 사이 완료(오프라인) | 이벤트가 없어도 켤 때 스캔해서 알린다 |
| 새 회복 주기 | `StartRecovery`가 marker를 false로 초기화 |
| 합류 | `Clear()`가 marker를 false로 초기화 |

**예전 저장 파일 호환:** 필드가 없으면 JsonUtility가 `false`를 넣는다. 그것이 곧 "아직 알리지 않음"
이므로, 완료 상태로 저장돼 있던 레거시 슬롯은 다음 실행에서 **정확히 한 번** 알림을 받는다 —
기본값이 안전한 쪽이다.

### 2.2 marker는 알림이 수락된 뒤에만 남긴다

```
Flush()
  1. RecoveryService.Station == null          -> 아무 것도 하지 않고 재시도 (marker 미기록)
  2. 대기 목록 수집(정렬 포함)                  -> 0건이면 재시도 종료
  3. Manager / Definition 미준비               -> flushRequested = true 로 재시도 예약 (marker 미기록)
  4. 순서대로 Show(definition, 캐릭터이름)
       수락(view != null) -> acceptedSlots에 추가
       거절(view == null) -> 거기서 중단 (그 뒤 슬롯은 다음 기회에)
  5. acceptedSlots에 대해 MarkCompletionNotified() -> 저장 1회
  6. 남은 것이 있으면 flushRequested 유지
```

**초기화 순서에 알림을 잃지 않는다.** 회복소나 알림 매니저가 아직 없으면 marker를 남기지 않고
`retryIntervalSeconds`(기본 0.5초)마다 다시 시도한다.

**crash/reload 관점의 선택:** Show 수락 → marker 저장 사이에 앱이 죽으면 같은 알림이 한 번 더 뜬다.
반대 순서(먼저 저장)로 하면 그 주기의 알림을 **영구히 잃는다**. 알림 중복보다 유실이 나쁘다고 보고
"수락 후 기록"으로 정했다. 저장 실패 시에도 같은 이유로 오류만 남기고 진행한다.

### 2.3 결정 규칙 — 어떤 캐릭터 이름이 최종적으로 남는가

같은 `notificationId`는 최종 current 한 개만 유지된다(기존 정책 그대로). 따라서 **마지막으로 요청한
알림의 이름**이 화면에 남는다. 요청 순서는 다음 하나로 고정한다.

```
(완료 예정 시각 CompleteAtUtc, 슬롯 번호 SlotIndex) 오름차순
```

| 경우 | 최종 표시 |
| --- | --- |
| 완료 시각이 다름 | **가장 늦게(= 가장 최근에) 완료된** 캐릭터 |
| 완료 시각이 같음(동시 완료) | **슬롯 번호가 가장 큰** 캐릭터 |

이 기준은 도메인의 `RecoveryStation.RecoveryCompleted` 이벤트 순서(1단계에서 이미
`(완료 시각, 슬롯 번호)` 오름차순 보장)와 **동일**하다. 그래서 실시간 완료와 오프라인 완료가 서로
다른 결과를 내지 않는다. 규칙은 `RecoveryCompletionNotice.Compare` 한 곳에만 있다.

다른 타입 알림(`stamina_depleted` 등)은 이 정책의 영향을 받지 않는다(타입별로 독립).

### 2.4 동적 포맷 인자

```csharp
manager.Show(definition);                          // 기존 API - 동작 완전히 동일
manager.Show(definition, "바바리안");               // 신규 - 문구의 {0}에 채워진다
```

- **각 카드가 인자 사본을 소유한다**(`(object[])arguments.Clone()`). 호출부가 배열을 재사용하거나
  내용을 바꿔도 이미 떠 있는 카드는 영향받지 않는다.
- **`LocalizedString.Arguments`를 쓰지 않았다.** 그 프로퍼티는 Definition 에셋이 소유하는 **공유**
  객체라, 같은 Definition으로 띄운 다른 카드의 문구까지 바뀐다. 대신 기존처럼 `StringChanged`로 원문을
  받아 **뷰가 자기 사본으로 포맷**한다.
- **Locale 변경 시 인자가 유지된다.** 언어가 바뀌면 `StringChanged`가 다시 호출되고, 그때도 같은
  사본으로 다시 포맷한다.
- **인자가 없으면 `string.Format`을 부르지 않는다.** 문구에 `{0}`이 있어도 예외 없이 원문 그대로
  표시되므로 기존 알림 동작이 조금도 달라지지 않는다.
- **포맷 실패는 안전하다.** 자리표시자 번호가 인자보다 많거나 중괄호가 깨진 경우 예외를 밖으로 던지지
  않고 **원문을 그대로 표시**하며, 원인을 카드당 한 번 오류 로그로 남긴다.
- 같은 타입 교체 정책(current 1개 + 물러난 1개), retiring 처리, `StringChanged` 구독/해제 수명은
  인자가 있든 없든 동일하다.

---

## 3. 자동 검증

Unity Editor가 프로젝트 락을 잡고 있어 APFS 클론에서 batchmode로 실행했다. 하네스는 클론에만 두고
저장소에는 넣지 않았다.

### 3.1 Edit Mode (도메인)

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath <clone> \
  -executeMethod RecoveryVerify.RecoveryVerification.Run -logFile <clone>/P3-edit.log
```

| 항목 | 결과 |
| --- | --- |
| 종료 코드 | **0** |
| `error CS` | **0** |
| 결과 | **TOTAL: 270, PASSED: 270, FAILED: 0** (1·2단계 237 + 3단계 신규 33) |

신규 `T22 완료 알림 per-cycle marker`가 검증하는 것:

| 항목 | 확인 |
| --- | --- |
| 예전 저장 파일 호환 | 필드 없는 JSON → marker 기본값 false |
| 온라인 완료 1회 | 완료 전 0건 → 완료 후 1건 → 기록 후 0건 |
| 저장 횟수 | `MarkCompletionNotified` 1회 호출 = `Save` **1회**, 이미 기록된 marker 재기록 시 **0회** |
| 매 프레임 반복 금지 | 반복 `Tick` 후에도 대상 0건 |
| 재시작 반복 금지 | 같은 저장 데이터로 새 `RecoveryStation` → 대상 0건 |
| 레거시/marker 삭제 | marker를 지우면 정확히 1건 → 기록 후 0건 |
| 합류 | 슬롯 비움 + marker 초기화 |
| 새 주기 | `StartRecovery`가 marker 초기화 → 완료 시 다시 1건 |
| 동시 완료 결정성 | 3슬롯 → `Z`(이른 완료) → `X`(슬롯 0) → `Y`(슬롯 1) 순, **마지막이 큰 슬롯 번호** |
| 오프라인 완료 순서 | 완료 예정 시각 오름차순, 마지막이 가장 늦게 끝난 캐릭터 |
| 방어 | null 버퍼 / null·빈·범위 밖 목록 → 예외 없이 false |

### 3.2 Play Mode (실제 씬 + 실제 프리팹 + 알림 매니저)

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath <clone> \
  -executeMethod RecoveryVerify.RecoveryPlayModeVerification.Setup -logFile <clone>/P3-pm2.log
```

| 항목 | 결과 |
| --- | --- |
| 종료 코드 | **0** |
| 결과 | **TOTAL: 357, PASSED: 357, FAILED: 0** (1·2단계 270 + 3단계 신규 87) |
| 예기치 않은 예외/Assert | **0건** |

| 구역 | 검증 내용 |
| --- | --- |
| P25 | **동적 포맷 인자** — `Show(definition)` 호환(인자 0개, 자리표시자 원문 유지, 예외 없음), `Show(definition, arg)` 포맷 적용, Locale 재적용 시 인자 유지, 호출부 배열 변경 격리, 같은 배열 재사용 시 카드 간 오염 없음, 포맷 실패(자리표시자 초과 / 중괄호 깨짐) 시 원문 표시 + 이후 정상 복구 |
| P26 | **교체 정책** — 같은 타입 current 1개(마지막 이름이 남음), 이전 카드는 current 아님, **다른 타입(`stamina_depleted`) 무영향**(인자 없이 유지) |
| P27 | **완료 알림 연결** — 패널 없이 동작, 캐릭터 이름 인자 전달, marker 저장, 반복 Flush/Tick/이벤트 중복/`OnEnable` 반복에도 카드 1개, 합류 전 상태(완료 유지·슬롯 점유·교체 차단·행동력 최대 저장), 레거시 marker 삭제 시 정확히 1회, **Definition 미준비 시 marker 미기록 + 재시도 예약 → 준비 후 정상 요청**, 새 주기 marker 초기화 후 재알림, 동시 완료 시 큰 슬롯 번호가 최종 표시, 오프라인 완료 시 가장 늦은 완료 시각이 최종 표시, 전체 합류 후 빈 슬롯에서 새 알림 없음 |
| P28 | **알림 매니저 늦은 준비(`Instance == null`)** — 아래 3.3절 참고 |
| P01~P24 | 1·2단계 전체 회귀(전투/보상/행동력 소모/소진 알림 경로/교체/저장/다중 팝업/ESC/드래그·드롭/스크롤) |

**Localization 실제 문자열은 의존하지 않았다.** batchmode에서 테이블 로딩이 비동기라 결과가 흔들리므로,
포맷 규칙은 뷰의 본문 적용 경로에 원문을 직접 넣어 결정적으로 검증했다. 실제 번역 문구 확인은 4절
수동 항목이다.

### 3.3 알림 매니저 늦은 준비 시나리오 (P28)

요구된 초기화 순서 위험을 **별도 시나리오로 명시 검증**했다. P27의 "Definition 미준비"와는 다른
경로다 — 이쪽은 `SystemNotificationManager.Instance` **자체가 null**인 상태에서 첫 Flush가 일어난다.

재현 방법: 완료 슬롯 1개 + marker false + Definition 정상인 상태를 만든 뒤, Notifier의 직렬화 참조와
정적 `Instance`를 **둘 다** null로 만들고 Flush → 이후 유효한 매니저를 새로 만들어(그 `Awake`가
`Instance`를 세운다) 다시 Flush. `RecoveryStation.saveAction`을 세는 래퍼로 감싸 **Save 호출 횟수를
정확히 계측**했다.

| 단계 | 확인 | 결과 |
| --- | --- | --- |
| 매니저 없음 | 카드가 만들어지지 않음 | PASS |
| 매니저 없음 | **marker를 남기지 않음**(성급한 확정 없음) | PASS |
| 매니저 없음 | 재시도 상태(`flushRequested`) 유지 | PASS |
| 매니저 없음 | **Save 0회** | PASS |
| 매니저 없음 | 반복 시도에도 marker false / Save 0회 | PASS |
| 늦은 준비 | 새 매니저가 `Instance`가 됨 | PASS |
| 늦은 준비 | Flush 후 **카드 정확히 1개**, current 등록 | PASS |
| 늦은 준비 | 캐릭터 이름이 포맷되어 전달(`<이름>(이)가 회복을 완료하였습니다.`) | PASS |
| 늦은 준비 | **marker true**, **Save 정확히 1회** | PASS |
| 늦은 준비 | 재시도 상태 해제 | PASS |
| 반복 retry | 추가 카드 **0**(카드 1개 유지), Save **1회** 유지, current 인스턴스 동일 | PASS |
| 반복 retry | 완료 이벤트를 한 번 더 넣어도 추가 카드 없음 | PASS |

즉 **알림을 잃지도, 중복으로 띄우지도, marker/Save를 성급히 남기지도 않는다**는 것이 24개 assertion으로
확인됐다.

### 3.4 무결성

```bash
diff -r Assets/Scripts <clone>/Assets/Scripts        # 완전 동일(메타 포함)
diff -r Assets/Scenes  <clone>/Assets/Scenes         # 무변경
diff -r Assets/Art     <clone>/Assets/Art            # 무변경
diff -r Assets/Data    <clone>/Assets/Data           # 무변경 (기존 Notification Definition 포함)
diff -r Assets/Localization <clone>/Assets/Localization  # 무변경
grep -rh "^guid: " --include="*.meta" Assets/ | sort | uniq -d   # 중복 0 (8707/8707)
```

사용자의 실제 저장 파일은 검증 전 백업하고 **바이트 단위로 동일하게 복원**했다.

---

## 4. 자동 검증이 닿지 않는 항목 (수동 체크리스트)

| # | 확인할 것 | 왜 자동으로 못 하나 |
| --- | --- | --- |
| 1 | 실제 알림 카드에 **"<캐릭터>(이)가 회복을 완료하였습니다."** 가 정확히 표시되는지 | Localization 테이블 로딩이 batchmode에서 비동기 |
| 2 | 언어를 한국어↔영어로 바꿨을 때 알림 문구가 바뀌고 **캐릭터 이름은 유지**되는지 | 실제 Locale 전환 필요 |
| 3 | 30초 실시간 대기 후 완료 시 알림이 자동으로 뜨는지(패널을 닫아 둔 채로) | 하네스는 저장 시각을 조작해 완료를 만든다 |
| 4 | 앱을 껐다 켰을 때(진짜 프로세스 재시작) 오프라인 완료 알림이 **한 번만** 뜨는지 | 실제 프로세스 재시작 필요 |
| 5 | 알림 카드 등장/종료 연출, 적층 위치, 여러 알림이 겹칠 때의 모양 | 시각 판단 |
| 6 | `stamina_depleted` 알림이 지금까지대로 뜨는지(회귀) | 실제 전투로 행동력 0 만들기 |
| 7 | **Windows 빌드**: 알림 카드 클릭/닫기(`WindowInputRegion`), 패널이 클릭되는지 | macOS에서 Win32 검증 불가 |
| 8 | **Windows 투명창에서 패널 사이 공백을 지나는 드래그**(2단계 범위 밖 이슈) | Windows 실기 필요 |
| 9 | ESC·다중 팝업·공격 제외 키 조합 조작감 | 사람 조작 패턴 |

---

## 5. Unity Editor 연결 체크리스트

1·2단계 체크리스트를 먼저 끝낸 뒤 아래를 수행한다.

### 5-1. Localization (기존 에셋 확인 — 수정은 워크플로대로)

| # | 작업 |
| --- | --- |
| 1 | `01_UI` 테이블의 **key 19**가 존재하는지 확인한다. 현재 값: ko-KR `{0}(이)가 회복을 완료하였습니다.`, en `The recovery of {0} has been completed.` |
| 2 | ko-KR 문구가 **정확히** `{0}(이)가 회복을 완료하였습니다.` 인지 확인한다. 다르면 Google Spreadsheet에서 고치고 `TableData/Localization/01_UI.csv`를 내려받아 CSV(Merge) Import 한다 |
| 3 | `{0}`은 **반드시 유지**한다 — 캐릭터 이름이 채워지는 자리다(2단계에서 제거하기로 한 것은 상태 문구 key 10의 `{0}`이며, 이 key 19와는 다르다) |

> 이번 단계에서 Localization 에셋과 CSV는 **수정하지 않았다**. key 19는 이미 존재하므로 값 확인만
> 하면 된다.

### 5-2. 알림 Definition 에셋 생성

| # | 작업 |
| --- | --- |
| 4 | `Create > Notification > System Notification Definition` → `Assets/Data/Notifications/RecoveryCompleted.asset`으로 저장 |
| 5 | `Notification Id` = **`recovery_completed`** (정확히 이 문자열) |
| 6 | `Message` = Category `01_UI` / Key **19** |

> 기존 `Assets/Data/Notifications/SystemNotificationDefinition.asset`(`stamina_depleted`)은
> **건드리지 않는다**.

### 5-3. 알림 연결 컴포넌트 배치

| # | 작업 |
| --- | --- |
| 7 | 씬의 상주 오브젝트(예: `CurrentCharacterStaminaNotification`이 붙어 있는 오브젝트, 또는 회복소 관리자 오브젝트)에 **`Recovery Completion Notifier`** 컴포넌트 추가 |
| 8 | 필드 연결 |

```
Recovery Completion Notifier
  Notification Manager            = 씬의 SystemNotificationManager   (비워두면 Instance를 쓴다)
  Recovery Completed Notification = 4번에서 만든 RecoveryCompleted.asset
  Retry Interval Seconds          = 0.5
```

| # | 작업 |
| --- | --- |
| 9 | 이 오브젝트는 **회복소 패널과 무관하게 항상 켜져 있어야** 한다(패널이 닫혀 있어도 알림이 떠야 하므로 패널 아래에 두지 않는다) |
| 10 | 씬에 **하나만** 둔다. 여러 개 두어도 marker 덕분에 중복 알림은 생기지 않지만, 불필요한 재시도가 늘어난다 |

### 5-4. 2단계에서 확정된 버튼 명칭 (참고)

씬의 `pn_RecoveryStation` 인스턴스는 하단 버튼 이름이 다음과 같이 재정의되어 있다. 코드의 tooltip과
오류 문구도 이 명칭으로 맞췄다.

| 프리팹 에셋 이름 | 씬 인스턴스(실제 사용) 이름 |
| --- | --- |
| `btn_recovery` | **`btn_StartRecovery`** (라벨 `lb_StartRecovery`) |
| `btn_cancle` | **`btn_cancel`** (라벨 `lb_cancel`) |
| `btn_JoinParty` | `btn_JoinParty` (동일) |

---

## 6. 3단계 요구사항 대응표

| 요구 | 구현 / 검증 |
| --- | --- |
| 1) 완료 알림 연결 컴포넌트 | `RecoveryCompletionNotifier` — Definition 직렬화 참조, `RecoveryService.RecoveryCompleted` 구독, 캐릭터 이름 인자, 패널 무관 동작 (P27) |
| 2) 동적 포맷 인자 지원 | `Show(definition, params object[])` 추가, 기존 `Show(definition)` 동작 불변, 뷰별 인자 사본, Locale 재적용 유지, 포맷 실패 안전 (P25) |
| 3) Notification ID / key 19 | `recovery_completed`, 01_UI key 19. 에셋 생성·연결은 5절 체크리스트. `stamina_depleted` 회귀 (P26) |
| 4) 동일 타입 정책 / 동시 완료 결정성 | current 1개 유지, 다른 타입 무영향, `(완료 시각, 슬롯 번호)` 오름차순 → 마지막이 최종 표시 (2.3절, T22, P26, P27) |
| 5) per-cycle marker | `RecoverySlotSaveState.completionNotified`. Pending 미저장 계약 유지(대기는 여전히 저장되지 않는다). 레거시 기본값 안전, 새 주기 초기화 (T22, P27) |
| 6) 오프라인 완료 | `OnEnable` 스캔 + 완료 시각 오름차순 처리, 이미 marker가 선 슬롯은 제외 (T22, P27) |
| 7) 초기화 순서 안전 | 매니저/회복소/Definition 미준비 시 marker 미기록 + 재시도 예약. 중복 구독·`OnEnable` 반복에도 1회 (P27) + **매니저 `Instance == null` 시나리오 명시 검증** (P28, 3.3절) |
| 8) 통합 회귀 | 완료 시 행동력 최대 저장, 슬롯 점유 유지, 합류 전 교체 차단, 슬롯별/전체 합류 (P27 + 기존 P16/P17/P21) |
| 9) 버튼 명칭 정리 | `btn_StartRecovery` / `btn_cancel`로 tooltip·오류 문구 정정, 2단계 보고서 동기화. 로직·UI·에셋 무변경 |

---

## 7. 미해결 위험 / 후속

### 7-1. 알림 중복 vs 유실 (설계상의 선택)

Show 수락과 marker 저장 사이에 앱이 죽으면 같은 완료 알림이 한 번 더 뜬다. 반대 순서는 알림을
영구히 잃으므로 의도적으로 이 방향을 골랐다(2.2절). 실제로 이 창은 한 프레임 수준이라 재현 가능성은
매우 낮다.

### 7-2. marker는 슬롯 단위다

같은 슬롯에서 새 회복을 시작하면 marker가 초기화되므로 per-cycle 의미가 성립한다. 다만 "회복 주기
고유 id"를 따로 저장하지는 않았다 — 슬롯 하나에 동시에 두 주기가 존재할 수 없으므로 현재 규칙에서는
구분이 필요 없다. 슬롯 구조가 바뀌면(예: 한 슬롯에 큐) 이 전제를 다시 봐야 한다.

### 7-3. 여러 명 동시 완료 시 카드는 1개만 남는다

같은 타입 최종 current 1개 정책(기존 규칙)에 따른 결과다. "3명이 동시에 완료됐다"를 한 카드에
요약하려면 문구와 인자 설계가 추가로 필요하며 이번 범위가 아니다. 어떤 이름이 남는지는 2.3절 규칙으로
결정적이다.

### 7-4. Definition 에셋이 없으면 알림이 뜨지 않는다

5-2절 작업 전까지 `RecoveryCompletionNotifier`는 오류 로그를 한 번 남기고 대기한다. marker를 남기지
않으므로, 에셋을 연결하는 즉시 밀린 알림이 정상적으로 표시된다.
