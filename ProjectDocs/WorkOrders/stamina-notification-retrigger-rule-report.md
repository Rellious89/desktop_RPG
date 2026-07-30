# 행동력 소진 알림 재발동 규칙 수정 완료 보고서

이전 구현("캐릭터 활성화 기간당 1회")을 "행동력 0 상태 진입마다 1회"로 상태 머신을 변경했다.
아울러 사용자가 씬/프리팹에서 진행한 에디터 작업을 검토하는 과정에서 남아 있던 수동 테스트용 배선을
발견해 제거했다.

## 1. 상태 머신 변경

`CurrentCharacterStaminaNotification`의 잠금 필드를 캐릭터 활성화 기간 전체를 잠그던 것에서 "지금의
0 상태" 하나만 잠그는 값으로 바꿨다.

```csharp
private bool notifiedForCurrentActivation;   // 이전: 활성화 기간 전체를 잠금
private bool notifiedForCurrentDepletion;    // 이후: "지금의 0 상태"만 잠금
```

`Evaluate()`가 양방향으로 상태를 바꾼다.

```text
Stamina > 0   → 잠금 해제(다음 0 진입에서 다시 알릴 수 있음). 회복 시점 자체는 알리지 않는다.
Stamina <= 0 + 미알림 → 알림 요청 + 잠금
Stamina <= 0 + 이미 알림 → 무시
```

`HandleCharacterStateChanged`는 행동력 증가·감소 이벤트를 모두 `Evaluate()`로 넘긴다(이전에는 감소만
실질적으로 의미 있었다). `SyncToCurrentCharacter()`는 같은 캐릭터여도 매번 `Evaluate()`를 호출하도록
바꿨다 - 컴포넌트가 꺼져 있던 동안 같은 캐릭터의 행동력이 회복됐다면, 다시 켜졌을 때 그 회복을 반영해야
다음 소진에서 알림이 나간다.

## 2. SystemNotificationManager 보강 (§8)

`Show()`가 복제한 View가 비활성 상태로 남아 있을 수 있는 경우(원본 Slot Prefab이 비활성으로 저장된
경우 등)를 대비해, Bind와 등록 이후 명시적으로 활성화한 뒤 `PlayEnter()`를 호출하도록 순서를 보강했다.
활성/비활성 어느 프리팹을 연결해도 알림이 표시된다.

## 3. 발견하고 제거한 수동 테스트 배선 (§9, §10)

사용자가 프리팹을 만드는 과정에서 남긴 이전 수동 테스트 경로를 실제로 검토했다.

- `Assets/Art/UI/Prefab/Notification/item_notification.prefab`: `Play Enter On Enable`이 `On`으로
  남아 있었다. `SystemNotificationManager.Show()`가 이미 `PlayEnter()`를 명시적으로 호출하므로 `Off`로
  바꿨다 - 그렇지 않으면 `OnEnable` 자동 재생과 Manager의 명시적 호출이 겹쳐 불필요한 재시작이 생긴다.
- `Assets/Art/UI/Prefab/Notification/NotificationSlot.prefab`(nested prefab instance override): 다음
  두 수동 테스트 연결이 남아 있었다.
  - `btn_close.OnClick` → `UITweenTransition.PlayExit` (직접 연결 - `SystemNotificationItemView`를
    건너뛴다)
  - `UITweenTransition.OnExitCompleted` → `item_notification.SetActive(false)`
  둘 다 제거했다. 실제 경로는 `btn_close → SystemNotificationItemView.HandleCloseClicked → BeginExit
  → UITweenTransition.PlayExit → Manager 제거 요청 → Slot Destroy` 하나뿐이다 - 두 경로를 동시에 두면
  `PlayExit`이 두 번 걸리거나(한 번은 View가, 한 번은 버튼이 직접) `SetActive(false)`가 Manager의
  `Destroy`보다 먼저 오브젝트를 비활성화해 제거 콜백 타이밍이 뒤섞일 수 있었다.

## 4. 변경한 파일

| 파일 | 내용 |
| --- | --- |
| `Assets/Scripts/Common/Notification/CurrentCharacterStaminaNotification.cs` | 상태 머신을 "활성화 기간당 1회" → "0 진입마다 1회"로 변경 |
| `Assets/Scripts/Common/Notification/SystemNotificationManager.cs` | `Show()`에서 비활성 복제본을 활성화한 뒤 Enter 재생 |
| `Assets/Art/UI/Prefab/Notification/item_notification.prefab` | `Play Enter On Enable: On → Off` |
| `Assets/Art/UI/Prefab/Notification/NotificationSlot.prefab` | 수동 테스트용 `btn_close` 직결 연결과 `OnExitCompleted → SetActive(false)` 오버라이드 제거 |

이번 수정으로 씬/`SystemNotificationDefinition`/`SystemNotificationManager` 연결 등 사용자가 이미
마친 에디터 작업은 그대로 유지된다 - 추가로 수행할 에디터 작업은 없다.

## 5. 검증 상태

에디터가 프로젝트를 잠그고 있어 APFS 클론에서 실제 `desktopScene`(사용자가 연결을 마친 상태 그대로)을
batchmode Play Mode로 실행했다. **28개 검사 전부 PASS, 에러 로그 0건, `error CS` 0건.** Windows
(`StandaloneWindows64`) 대상 플레이어 스크립트 컴파일도 0 오류로 통과했다(14개 어셈블리).

### §11 권장 순서 기준 결과

| 항목 | 결과 |
| --- | --- |
| 리필 → 시작 시 알림 없음 | PASS |
| 행동력 0 진입 → 알림 1회 | PASS |
| 닫기 → 계속 0 상태에서 추가 알림 없음 | PASS |
| 리필(회복) → 알림 자동 생성 없음, 기존 카드 자동으로 닫히지 않음 | PASS |
| 다시 0 → 새 알림 발생 | PASS |
| 0 유지(반복 `SetStamina(0)`) → 추가 알림 없음 | PASS |
| §7: 알림을 닫지 않은 채 회복 후 재소진 → 새 카드 먼저 등장(일시 2개) → Replacement Delay 후 최종 1개 | PASS |
| 캐릭터 교체 후 동일 과정이 새 캐릭터에서 독립적으로 반복 | PASS |
| 다른 캐릭터의 행동력 변경(회복/소진 모두)은 무시 | PASS |
| 컴포넌트가 꺼져 있던 동안 회복 → 재활성화 후 재소진 시 알림 발생 | PASS |

검증에는 실제 저장 파일(`playerprogress.json`)이 쓰이므로 실행 전 백업하고 종료 후 원본으로 복원했다
(diff 일치 확인).

### 확인하지 못한 것

이전 보고서와 동일하게 Windows 네이티브 클릭 판정과 실제 Player 빌드 실행은 macOS에서 확인할 수 없다.
