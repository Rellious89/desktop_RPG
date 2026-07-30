# 행동력 시스템 구현 완료 보고서

작업일: 2026-07-29
전제: 캐릭터 교체 UI는 구현·Play Mode 검증 완료 상태. 이번 작업에서 교체 UI의 레이아웃, 선택 처리
(선택 버튼 스프라이트 교체 방식), 프리팹 구조는 **변경하지 않았다**. 씬/프리팹 파일도 건드리지 않았다.

## 1. 행동력 기본 규칙

| 규칙 | 구현 위치 |
| --- | --- |
| 캐릭터별 독립 보유 | `SaveData.characters[].currentStamina` (캐릭터 id가 키) |
| 최대치는 정의 데이터 | `CharacterDefinition.MaxStamina` |
| 현재값은 저장 데이터 | `CharacterRoster.GetOrCreateState` |
| 저장 데이터 없으면 최대치로 초기화 | `CharacterRoster.SyncSaveStates` (`currentStamina < 0` → Max) |
| 몬스터 처치 1회당 1 소모 | `CharacterRoster.HandleAnyTargetDefeated` |

새 데이터 구조는 만들지 않았다. 기존 `CharacterDefinition` + `SaveData.characters`를 그대로 쓴다.

소모량은 `CharacterRoster`의 `Stamina Cost Per Defeat`(기본 1)로 노출했다. 단위는 **처치 확정 1회**이며
타격 수나 공격 횟수가 아니다.

## 2. 행동력 소모 시점

근거 이벤트는 기존 `Target.AnyTargetDefeated` **하나뿐**이다. 이 이벤트는 `Target.Defeat()` 안에서
내구도가 0이 되어 처치가 확정되는 순간에만 발생하며, `PlayerProgress` / `SessionKillCounter` /
`AudioManager`가 이미 같은 방식으로 구독하고 있다.

```csharp
private void OnEnable()  { Target.AnyTargetDefeated += HandleAnyTargetDefeated; }
private void OnDisable() { Target.AnyTargetDefeated -= HandleAnyTargetDefeated; }
```

다음 경로에는 어떤 구독도 추가하지 않았다 - 코드상 연결 자체가 없다.

- 키보드 입력(`GlobalKeyboardHook`)
- 공격 시작(`AttackStarted`) / 공격 준비(`ChargeStarted`)
- 타격 판정(`HitPoint`) / 데미지 적용(`Target.OnDamaged`)
- 몬스터 피격(`TargetCombatController`)
- 공격 애니메이션 종료(`AttackEnded`)

### 중복 호출 방어

`Target`은 `IsDefeated` 플래그로 처치당 한 번만 이벤트를 보내고, 같은 대상이 다시 죽으려면
Fade-out → 대기 → Fade-in을 거쳐야 하므로 최소 여러 프레임이 걸린다. 따라서 **"같은 프레임 + 같은
targetId"** 는 정상 흐름에서 나올 수 없는 중복 호출이다. 이 조합이 들어오면 경고를 남기고 두 번째
호출을 무시한다(`TryAcceptDefeatEvent`).

서로 다른 몬스터가 같은 프레임에 죽으면 targetId가 다르므로 각각 1씩 정상 소모된다.

## 3. 전투 가능 여부

`CharacterRoster.CurrentCharacterCanAct`(정적 프로퍼티)가 판정을 소유한다.

```csharp
public static bool CurrentCharacterCanAct   // 로스터가 없는 씬에서는 항상 true
```

`PlayerCharacterAnimator`는 기존 `Target.HasAttackableTarget` 게이트를 다음으로 확장했다.

```csharp
private static bool CanStartNewAttack => Target.HasAttackableTarget && CharacterRoster.CurrentCharacterCanAct;
```

이 값을 보는 지점은 **새 공격을 시작할지 판단하는 세 곳뿐**이다.

1. `Update()` – 키 입력을 공격 대기열에 올릴지
2. `Strike()` – 타격 직후 다음 Windup으로 이어갈지
3. `AdvanceAttack()`의 Recovery 종료 – 누적 입력 이월분으로 다음 충전을 시작할지

진행 중인 Windup/Charging/Recovery는 여기서 **끊지 않는다**. 행동력이 0이 되는 시점은 "몬스터를 방금
처치한 순간"이라, 재생 중이던 공격은 그대로 마무리되고 그 뒤 Idle로 돌아간 다음 새 전투가 시작되지
않는다. 몬스터가 리젠돼도 마찬가지다.

- 자동 교체 없음(코드에 경로 자체가 없다).
- 전투 시작 전에 이미 0이면 첫 입력부터 막히므로 Idle 상태로 대기한다.
- 별도의 "행동력 소진" 애니메이션 클립은 없다 - 지시서가 허용한 대로 Idle 상태로 둔다.

## 4. 캐릭터 상태 표시

교체 UI 코드는 변경하지 않았다. 기존 `CharacterSwapListItem`의 판정이 그대로 요구사항과 같다.

| 조건 | 표시 |
| --- | --- |
| 현재 전투 중 | `InUse` – 하늘빛 배경, 항목 선택 불가 |
| `current > 0` | `Ready` – 선택 가능 |
| `current == 0` | `Exhausted` – 회색 배경, 항목 선택 불가 |

행동력 소진 캐릭터를 눌렀을 때의 안내는 지시서가 제시한 세 방법 중 **교체 버튼 비활성화 + 상태
표시** 방식이 이미 적용되어 있다.

- 항목 Button의 `interactable`이 꺼져 Sprite Swap의 Disabled 스프라이트로 바뀐다.
- 배경색이 회색으로 바뀐다.
- 행동력 막대가 0%, `lb_percent`가 `0 / N`으로 표시된다(이번 작업으로 실제 0이 나오게 됐다).
- `btn_swap`은 교체 가능한 선택일 때만 켜진다.

문구까지 명시하고 싶으면 프리팹 `sp_stamina` 아래에 `lb_state`라는 이름의 TMP를 추가하고
`CharacterSwapListItem`의 State 3칸에 `01 UI`의 Key 5/6/7(전투 가능 / 사용 중 / 행동력 소진)을
지정하면 된다. 문자열은 지난 작업에서 CSV에 이미 넣어 두었다. **이번 작업에서는 UI를 바꾸지 말라는
지시에 따라 추가하지 않았다.**

## 5. 데이터 및 저장

- 저장 시점은 `CharacterRoster.SetStamina`에서 **값이 실제로 바뀐 경우에만** `SaveSystem.Save()`.
  매 프레임/입력마다 저장하지 않는다. 이미 0인 캐릭터에 0을 지정하면 파일을 쓰지 않는다.
- 저장 문서는 기존 공유 문서(`SaveSystem.Data`) 그대로다. `PlayerProgress`가 소유한
  `currentLevel`/`currentExp`/`totalKillCount`는 건드리지 않는다.
- 한 번의 처치에서 `PlayerProgress`와 `CharacterRoster`가 각각 저장을 호출하므로 파일 쓰기가 2회
  일어난다. 두 시스템 모두 같은 문서를 고친 뒤 저장하므로 어느 순서로 실행돼도 최종 파일은 정확하다.
- 앱 재실행 시에는 저장된 `currentStamina`를 그대로 읽고, 정의의 Max Stamina를 나중에 낮춘 경우에는
  그 값으로 잘라낸다(`SyncSaveStates`).

## 6. UI 연동

### 캐릭터 교체 리스트

기존 경로 그대로다. `SetStamina`가 `CharacterRoster.CharacterStateChanged(캐릭터)`를 보내고,
`CharacterSwapPanel`이 **그 캐릭터의 항목 하나만** 다시 그린다. 패널이 닫혀 있는 동안 값이 바뀐
경우에는 다음에 열 때 `OnEnable`이 리스트를 새로 만들면서 최신값이 반영된다.

### 현재 캐릭터 HUD

씬의 `HUDLayoutRoot`에는 아직 행동력 표시 오브젝트가 없다(`ComboGroup` / `ProgressGroup`(경험치) /
`KillCountGroup`뿐). 그래서 **HUD 오브젝트를 새로 만들지는 않고**, 붙이기만 하면 동작하는 컴포넌트를
추가했다.

`Assets/Scripts/Common/CharacterStaminaDisplay.cs`

- `CharacterRoster.CurrentCharacterChanged` / `CharacterStateChanged`만 구독한다(폴링 없음).
- `ProgressBarView`에 현재/최대 행동력을 직접 주입한다.
- `PlayerProgress`와 경험치 이벤트는 참조하지 않는다.
- 숫자 텍스트/이름 텍스트는 선택 사항이라 비워두면 그 표시만 건너뛴다.
- `Start()`에서 한 번 더 갱신해, 이 컴포넌트의 `OnEnable`이 로스터 `Awake`보다 먼저 돌아도 값이
  비어 있지 않게 한다.

HUD를 만들 때는 경험치 바(`expProgress`)를 복제해 `PlayerProgressDisplay`를 `ProgressBarView`로
교체하고, 부모에 `CharacterStaminaDisplay`를 붙이면 된다.

## 7. 테스트 편의 기능 (정식 UI 비노출)

`CharacterRoster` Inspector와 컨텍스트 메뉴에만 있다.

| 기능 | 사용법 |
| --- | --- |
| 초기 행동력 설정 | `Override Stamina On Start` 체크 + `Debug Start Stamina` 값. 시작 시 전 캐릭터를 그 값으로 덮어쓰고 저장한다. 켜져 있으면 경고 로그를 남긴다. |
| 행동력 전체 충전 | 컴포넌트 우측 톱니 메뉴 → `Debug - Refill All Stamina` |
| 현재 캐릭터 소진 | 톱니 메뉴 → `Debug - Drain Current Character Stamina` |
| 특정 캐릭터 지정 | `CharacterRoster.Instance.SetStamina(definition, value)` |

회복 규칙은 구현하지 않았다 - 위 기능은 전부 개발용 리셋이며 게임 규칙이 아니다.

## 8. 변경한 파일

| 파일 | 변경 |
| --- | --- |
| `Assets/Scripts/Character/CharacterRoster.cs` | 처치 이벤트 구독, 중복 방어, `CurrentCharacterCanAct`, 개발용 진입점 |
| `Assets/Scripts/Character/PlayerCharacterAnimator.cs` | 새 공격 시작 게이트를 `CanStartNewAttack`으로 확장(3개 지점) |
| `Assets/Scripts/Common/CharacterStaminaDisplay.cs` | 신규 - 현재 캐릭터 HUD용 표시 컴포넌트 |

`SaveData` / `SaveSystem` / `ProgressBarView` / `CharacterSwapPanel` / `CharacterSwapListItem` /
씬 / 프리팹은 변경하지 않았다.

## 9. 검증 상태

### 이 환경에서 확인한 것

- `Assets/Scripts` 전체를 Unity와 동일한 정의(`UNITY_STANDALONE_WIN` 포함)로 Roslyn 컴파일 → **오류 0건**.
- `Target.AnyTargetDefeated`가 `Defeat()` 안에서 단 한 번만 발생하는지 코드로 확인.
- 기존 게이트 동작 동일성 확인: 로스터가 없거나 행동력이 남아 있으면 `CanStartNewAttack`은
  `Target.HasAttackableTarget`과 완전히 같은 값이다 → 경험치/콤보/전투 회귀 경로가 없다.

### 확인하지 못한 것

- **Play Mode 실행 전체**(이 환경에서는 Unity 에디터가 프로젝트 락을 잡고 있어 실행할 수 없다).
  지시서의 검증 항목 1~14는 에디터에서 직접 확인해야 한다.

### 지시서 검증 항목별 확인 방법

1~4번(3 → 2 → 1 → 0): 테스트할 캐릭터 정의의 `Max Stamina`를 3으로 바꾸거나,
   `Override Stamina On Start` + `Debug Start Stamina = 3`으로 시작한다.
   (현재 6종 정의는 모두 `maxStamina: 5`다.)
5~6번: 0이 된 뒤 몬스터가 리젠돼도 타이핑으로 공격이 시작되지 않고, 교체 패널에서 회색 + `0 / 3`으로 보이는지.
10번: `Cha_ElfArcher`(누적 입력)와 근접 캐릭터를 각각 1마리씩 잡아 소모량이 같은지.
11~12번: 몬스터가 살아 있는 동안 계속 타이핑 → 행동력 변화 없음.
13번: 콘솔에 `처치 이벤트가 같은 프레임에 중복 발생` 경고가 뜨지 않아야 정상이다(떴다면 이중 호출이
   실제로 있었고, 그래도 1회만 소모된 것이다).

## 10. 이번 작업에서 구현하지 않은 것 / 알아둘 점

- **회복 규칙 없음**. 지시서 범위 밖이지만 결과를 분명히 해 둔다: 최대 5 × 6종 = 총 30회 처치 후에는
  모든 캐릭터가 소진되어 **더 이상 전투할 수 없는 상태로 남는다**. 되돌리는 방법은 위 개발용 충전
  기능뿐이다. 다음 작업에서 회복/보상 규칙이 필요하다.
- 보상 정산, 자동 교체, 행동력 소진 전용 애니메이션은 구현하지 않았다.
- 교체 UI의 레이아웃/선택 처리/스프라이트 교체 방식은 그대로 두었다.
- 행동력 상태 문구(`lb_state`)는 4장 참고 - UI 변경 금지 지시에 따라 보류했다.
