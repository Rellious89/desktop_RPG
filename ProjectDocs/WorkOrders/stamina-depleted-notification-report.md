# 행동력 소진 시스템 알림 구현 완료 보고서

무료 DOTween 기반 `UITweenTransition`(별도 작업) 위에, 현재 캐릭터의 행동력이 0이 되면 지속형 시스템
알림을 출력하는 기반을 구현했다. 알림 생성·적층·타입별 교체·닫기까지 코드로 완성했고, 씬과 프리팹
연결은 사용자가 에디터에서 수행한다(9절).

## 1. 구조 요약

```text
CharacterRoster                       행동력 규칙의 원천(변경하지 않음)
  ├─ CurrentCharacterChanged          캐릭터 교체 = 새 활성화 기간
  └─ CharacterStateChanged            값이 실제로 바뀔 때만 발생
        │
        ▼
CurrentCharacterStaminaNotification   "현재 캐릭터가 0이 됐다"만 판정 (얇은 연결)
        │  Show(definition)
        ▼
SystemNotificationManager             타입별 중복·교체·적층 정책 소유 (Notification)
        │  Instantiate(slotPrefab)
        ▼
SystemNotificationItemView            알림 1개의 표시·닫기·로컬라이징 (NotificationSlot)
        │
        ▼
UITweenTransition                     등장/종료 연출 (item_notification)
```

위치 제어는 두 축으로 나뉜다. `Notification`의 VerticalLayoutGroup이 Slot의 적층 위치를,
`item_notification`의 `UITweenTransition`이 카드의 좌우 연출을 담당한다. 그래서 레이아웃과 Tween이
서로 위치를 덮어쓰지 않는다.

## 2. 알림 타입 정의

`SystemNotificationDefinition`(ScriptableObject)이 알림 종류 하나를 정의한다.

- `Notification ID`: 타입 고유 식별자. 같은 id는 같은 타입으로 판단해 최종 1개만 유지한다. 비워두면
  에셋 파일 이름을 쓴다(id를 빼먹은 에셋들이 빈 문자열 타입 하나로 뭉쳐 서로를 교체하는 것을 막는다).
- `Message`: `LocalizedTextReference`. 문구는 코드에 넣지 않는다.

이번에는 `stamina_depleted` 하나만 사용한다. `raid_opened`, `mercenary_summon_ready`는 같은 구조로
에셋만 추가하면 된다. 동적 Arguments는 이번 범위가 아니며, View와 Definition의 역할만 분리해 뒀다.

## 3. 런타임 로컬라이징

View가 Definition의 `LocalizedTextReference`를 직접 구독한다.

```text
Bind      → StringChanged 구독(구독 자체가 최초 로드를 유발, Locale 변경 시 자동 재호출)
종료/제거 → StringChanged 구독 해제
```

`lb_message`의 기존 빈 `LocalizedTMPText`는 제거해야 한다. 남아 있으면 두 컴포넌트가 같은 TMP를
번갈아 덮어쓰므로, View가 바인딩 시점에 경고를 남긴다(조용히 두지 않는다).

## 4. 동일 타입 교체 규칙

```text
1. 이미 물러난 같은 타입 카드가 있으면 연출 없이 즉시 버린다(누적 방어)
2. 새 인스턴스를 먼저 생성 + Bind
3. currentByType[id] = 새 인스턴스   ← 이 순서가 14번 항목의 근거
4. 새 카드 Enter 재생
5. Same Type Replacement Delay 대기 (WaitForSecondsRealtime = TimeScale 무시)
6. 이전 카드 Exit 재생
7. Exit 완료 콜백에서 제거
```

타입마다 **현재 1개 + 물러난 1개**까지만 둔다. 그보다 오래된 카드는 즉시 버리므로, 요청이 프레임마다
반복돼도 같은 타입 카드가 2개를 넘지 않는다(Play Mode 실측 max=2).

제거 시에는 항상 "닫히는 인스턴스가 지금 등록된 최신 인스턴스와 같은지"를 확인한다. 그래서 이전 카드의
지연 종료 콜백이 새 카드의 Dictionary 등록을 지우지 않는다.

## 5. 적층 순서

`Notification`의 VerticalLayoutGroup은 현재 `Child Alignment: Lower Right`, `Reverse Arrangement: Off`다.
이 설정에서 첫 번째 sibling이 시각적으로 가장 위이므로, 새 알림을 `SetAsFirstSibling()`으로 넣는다.
Play Mode에서 실제 위치로 확인했다(신규 y=150 > 기존 y=50). 레이아웃 설정을 바꿔 순서가 뒤집히면
Manager의 `Newest As First Sibling`을 끈다.

## 6. 알림 1회 단위 규칙

`한 번의 캐릭터 소환/활성화 기간 = 알림 1회`다. "0으로 내려갈 때마다"가 아니다.

| 상황 | 결과 |
| --- | --- |
| 현재 캐릭터 행동력 0 도달 | 알림 1회 |
| 사용자가 알림 닫기 | 같은 캐릭터가 0인 동안 다시 뜨지 않음 |
| 0 상태 방치 / `SetStamina(0)` 반복 | 다시 뜨지 않음 |
| 같은 기간에 회복 후 다시 0 | 다시 뜨지 않음(이번 요구사항) |
| 캐릭터 교체 | 새 기간 - 다시 가능 |
| 같은 캐릭터 재소환 | 새 기간 - 다시 가능 |
| 컴포넌트만 꺼졌다 켜짐 | 같은 캐릭터라면 기간 유지(초기화하지 않음) |

`notifiedForCurrentActivation`은 요청 **전에** true로 바꿔 재진입과 같은 프레임 중복을 막는다.
`CharacterRoster`는 Awake에서 시작 캐릭터를 정하므로 시작 이벤트를 놓칠 수 있어, 모든 Awake가 끝난
Start에서 현재 상태를 한 번 직접 평가한다(시작 시 이미 0이면 이번 실행에서 한 번 표시된다).

## 7. Windows 클릭 영역

알림 카드(Slot) 영역에만 `WindowInputRegion`을 켠다. `Notification` 전체(전체 화면 stretch)에 켜면 빈
화면까지 마우스가 막힌다. 종료 연출이 시작되면 View가 `ReceiveMouseInput`을 먼저 끄고, 오브젝트가
비활성/삭제되면 `WindowInputRegion`이 자기 `OnDisable`에서 등록을 해제한다.

## 8. 추가한 파일

| 파일 | 내용 |
| --- | --- |
| `Assets/Scripts/Common/Notification/SystemNotificationDefinition.cs` | 알림 타입 정의 에셋(id + 로컬라이즈 메시지) |
| `Assets/Scripts/Common/Notification/SystemNotificationItemView.cs` | 알림 1개의 표시·닫기·로컬라이징·제거 요청 |
| `Assets/Scripts/Common/Notification/SystemNotificationManager.cs` | 생성·적층·타입별 교체·제거 정책 |
| `Assets/Scripts/Common/Notification/CurrentCharacterStaminaNotification.cs` | 행동력 소진 판정 → 알림 요청 |

기존 스크립트는 한 줄도 수정하지 않았다(`CharacterRoster` 포함).

## 9. 사용자가 수행할 에디터 작업

1. 씬의 `Canvas/Notification/NotificationSlot`을 프리팹으로 저장한다(예: `Assets/Art/UI/Prefab/notification_slot.prefab`).
2. Slot 루트에 `SystemNotificationItemView`를 추가한다. 추가 시 `Reset`이 자식에서 `UITweenTransition` /
   `TextMeshProUGUI` / `Button` / `WindowInputRegion`을 자동으로 찾아 넣으므로, 값이 맞는지만 확인한다.
3. Slot 루트(또는 카드 영역)에 `WindowInputRegion`을 추가하고 `Receive Mouse Input`을 켠다.
4. `lb_message`의 빈 `LocalizedTMPText`를 제거한다.
5. 프리팹 저장 후 **씬에 남아 있는 `NotificationSlot` 인스턴스는 삭제한다**(런타임 복제만 사용한다).
6. `01_UI` 카테고리에 행동력 소진 문구 키를 추가한다(CSV 갱신 → Unity에서 Merge Import, `localization-workflow.md`).
7. `Assets/Data/Notifications/`에 `SystemNotificationDefinition` 에셋을 만든다
   (`Create > Notification > System Notification Definition`). `Notification ID: stamina_depleted`,
   `Message`에 6번 키를 지정한다.
8. `Notification`에 `SystemNotificationManager`를 추가하고 `Slot Prefab`(1번), `Notification Root`(Notification의
   RectTransform), `Same Type Replacement Delay`(기본 0.25)를 연결한다.
9. `Notification`(또는 임의의 상시 오브젝트)에 `CurrentCharacterStaminaNotification`을 추가하고
   `Notification Manager`와 7번 Definition을 연결한다.

## 10. 검증 상태

Unity 에디터가 프로젝트를 잠그고 있어 APFS 클론에 하네스를 넣고 실제 `desktopScene`을 batchmode
Play Mode로 실행했다. 9절의 에디터 작업은 **클론에서만** 코드로 재현했고(저장소의 씬·프리팹은 수정하지
않았다), 실제 `item_notification` 프리팹과 실제 `01_UI` 테이블, 실제 `CharacterRoster`를 그대로 사용했다.

**결과: 57개 검사 전부 PASS, 에러 로그 0건, `error CS` 0건, 종료 코드 0.**

### 지시서 검증 항목별 결과

| 항목 | 결과 | 실측 |
| --- | --- | --- |
| 1 행동력 1→0에서 알림 1회 | PASS | cards=1, id=stamina_depleted |
| 2 오른쪽 Enter 연출 | PASS | 같은 프레임 시작값 (380,20)/alpha 0 → 완료 (-20,20)/alpha 1 |
| 3 현재 Locale 문구 | PASS | locale=en, text='Stamina' (플레이스홀더 아님) |
| 4 닫기 → Exit 후 제거 | PASS | 알파 감소·오른쪽 이동 확인 후 Slot 제거 |
| 5 닫아도 같은 캐릭터 0 동안 재발 없음 | PASS | 회복 후 재소진까지 cards=0 |
| 6 `SetStamina(0)` 반복 | PASS | cards=0 |
| 7 다른 캐릭터 상태 변경 | PASS | cards=0 |
| 8 캐릭터 교체 후 재알림 | PASS | 교체 직후(잔여 행동력) 없음 → 0 도달 시 생성 |
| 9 새 알림 먼저 등장 | PASS | 교체 순간 같은 타입 2개(일시), 새 카드가 Enter 시작 상태 |
| 10 Delay 후 기존 알림 Exit·제거 | PASS | old alive=False |
| 11 최종 1개 유지 | PASS | cards=1 |
| 12 다른 타입 동시 적층 | PASS | stamina=1 + other=1 동시 유지, `CloseByType`은 해당 타입만 제거 |
| 13 최신이 가장 위 | PASS | sibling index=0, 실제 y 150 > 50 |
| 14 이전 콜백이 최신 등록을 지우지 않음 | PASS | 교체 완료 후 current=최신 인스턴스 |
| 15 Windows 닫기 클릭 | 부분 확인 | 아래 참고 |
| 16 알림 외부 마우스 관통 | 부분 확인 | 아래 참고 |
| 17 기존 기능 무변화 | PASS | 기존 파일 무수정 + 교체/저장/공격 차단 게이트 실측 정상 |
| 18 컴파일 오류 없음 | PASS | 에디터 + `StandaloneWindows64` 플레이어 스크립트 컴파일 0 오류 |

추가로 §8 누적 방어(빠른 반복 요청 시 같은 타입 최대 2개 → 최종 1개), §12 시작 시점 동기화(시작 시
행동력 0이면 1회 표시), §15 종료 정리(Manager 파괴 시 예외 없음, Instance 해제)를 확인했다.

### 확인하지 못한 것

- **15·16번의 네이티브 클릭 판정.** `TransparentWindowController.RegisterInputRegion`은
  `#if UNITY_STANDALONE_WIN`이라 macOS에서는 등록 자체가 no-op다. 카드 영역만 `Receive Mouse Input`이
  켜지고 종료 시 꺼지는 것까지는 확인했으나, 실제 클릭 관통 여부는 Windows 빌드에서 확인해야 한다.
- **Windows Player 빌드 산출물.** 스크립트 컴파일은 Windows 정의로 통과했지만 실제 빌드/IL2CPP 실행은
  macOS에서 만들 수 없다.
- **한국어 Locale 표시.** batchmode 실행에서 선택된 Locale이 `en`이어서 영어 문구로 확인했다. 한국어
  표시는 에디터에서 Locale을 ko-KR로 두고 한 번 보는 것이 좋다.

## 11. 이번 작업에서 하지 않은 것

사운드, 마우스 드래그 종료, 출력 중 외곽선 반복 애니메이션, 레이드·용병 소환 알림, 동적 Arguments,
알림 자동 만료(지속형이므로 사용자가 닫을 때까지 유지).
