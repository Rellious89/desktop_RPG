# 단일 런타임 액터 4단계 (CharacterRoster 이행) 완료 보고서

작업일: 2026-07-31
범위: `CharacterRoster`를 "캐릭터별 씬 오브젝트 켜고 끄기"에서 "런타임 액터 1개에 프로필 적용"으로
이행. 2단계 산출물(`CharacterRuntimeActor` / `PlayerCharacterAnimator` / `AttackMovement` /
`FlashOnCue`)은 **한 줄도 건드리지 않았다** — 3단계 Codex 리뷰가 수정 불필요로 결론냈고, 이번
통합에서도 손댈 이유가 생기지 않았다.
전제: 씬, 프리팹, ScriptableObject·데이터 에셋, `ProjectSettings` YAML, 여섯 캐릭터 오브젝트,
`CharacterDefinition` **에셋 파일**은 변경하지 않았다. 저장 포맷과 `CharacterId` 규칙도 그대로다.
커밋하지 않았고 기존 dirty 변경은 보존했다.

> **씬 연결이 아직 남아 있다.** 사용자가 Unity에서 `CharacterRoster`의 **Runtime Actor** 칸에 씬의
> `CharacterRuntimeActor`를 연결해야 캐릭터가 화면에 나온다. 연결 전에는 명확한 오류를 남기고
> 아무도 투입하지 않는다(공격도 막힌다).

---

## 1. 변경 파일 (2개, 둘 다 `.cs`)

| 파일 | 변경 |
| --- | --- |
| `Assets/Scripts/Character/CharacterRoster.cs` | `Entry.characterObject` 제거, `runtimeActor` 직렬화 필드 추가, `BuildUsableEntries` 판정 축소·세분화, `ApplyActiveCharacter` 액터 기반 + `bool` 반환, `SetCurrentCharacter` 분리, `TrySwitchTo` 실제 결과 반환, `GetSwapBlockReason`/`Awake` 오류 경로, 낡은 문서 갱신 |
| `Assets/Scripts/Character/CharacterDefinition.cs` | 클래스 요약과 Motion Profile 툴팁에서 "씬 오브젝트와 프로필 대조" 설명 제거(문서/툴팁 문자열만, 필드·직렬화 무변경) |

소비자(`CharacterSwapPanel`, `CharacterRosterRecoveryAdapter`, `CharacterStaminaDisplay`,
`CurrentCharacterStaminaNotification`, `RecoveryService` 등)는 **한 곳도 수정하지 않았다** —
컴파일이 요구하지 않았다. 프로젝트 전체에서 `characterObject`를 읽던 곳은 `CharacterRoster` 내부
5곳뿐이었음을 grep으로 확인했다.

---

## 2. 직렬화 동작 (핵심)

`Entry`는 **중첩 클래스 이름과 바깥 리스트 필드 이름(`entries`)을 그대로 뒀고**,
`public CharacterDefinition definition`도 그대로다. 지운 것은 `characterObject` 하나뿐이다.

- Unity가 기존 씬의 `entries` 배열을 읽을 때 각 원소의 `definition` 참조는 **그대로 복원된다** —
  여섯 캐릭터의 정의 연결이 유지된다.
- 없어진 `characterObject` 서브필드는 역직렬화 때 조용히 버려지고, **사용자가 다음에 씬을 저장할 때**
  파일에서 사라진다. 이번 작업은 씬 파일을 쓰지 않았으므로 지금은 YAML에 그대로 남아 있다(무해).
- `runtimeActor`는 새 필드라 기존 씬에서 **null로 시작한다** — 그래서 자동 탐색/자동 생성을 넣지 않고
  오류 + 미투입으로 처리한다(3절).
- 저장 포맷(`SaveData.characters`), `CharacterId` 산출 규칙, 회복소 연동은 손대지 않았다.

---

## 3. 동작 규칙

### 3.1 목록 판정 (`BuildUsableEntries`)

남긴 것: 항목/정의 null, `CharacterId` 중복, **Motion Profile 미연결**, **`IsPlayable` 실패**.
뒤 둘은 서로 다른 메시지다(연결을 안 한 것과 Base Idle이 빈 것은 사용자가 할 일이 다르다).
제거한 것: 씬 오브젝트 연결 검사, 정의↔씬 프로필 불일치 검사(`WarnOnProfileMismatch` 삭제) —
연기하는 액터가 하나뿐이라 대조할 상대가 없다.

여기서 미리 걸러 두므로, 목록에 남은 캐릭터는 액터 적용이 실패하지 않는다.

### 3.2 교체 (`ApplyActiveCharacter` → `bool`)

| next | 동작 |
| --- | --- |
| `null` | `runtimeActor.Deactivate()` → `current = null` → `ResetCombo` → `CurrentCharacterChanged(null)`. **시작 시점(전원 회복 중) 포함 항상 신호를 보낸다** — 예전의 "값이 바뀔 때만" 게이트를 뺐다. 구독자 3곳 모두 null을 안전하게 처리함을 확인했다 |
| 정의 | `runtimeActor.TryApply(next)`가 **true일 때만** `current` 이동 + `ResetCombo` + 이벤트. 실패하면 `current`를 그대로 두고 정의 ID를 포함한 오류를 남긴 뒤 false — 액터가 직전 캐릭터로 롤백해 화면에 유지하므로 로스터와 화면이 어긋나지 않는다 |
| 정의 + 액터 미연결 | 정의 ID를 포함한 오류 후 false. `current` 무변경 |

### 3.3 `TrySwitchTo`

`GetSwapBlockReason`이 `None`이어도 적용이 실패하면 **`NotAvailable`로 바꿔서 false**를 돌려준다.
`CharacterSwapPanel.ApplyPendingSwap`이 실패 시 `reason`을 그대로 로그에 찍기 때문에, "false인데
사유는 None"이 UI에 보이는 상태를 만들지 않기 위해서다. 오버로드 2개와 `SwapBlockReason` 열거형은
그대로 유지했다.

`GetSwapBlockReason`은 로스터 소속 판정 **직후**에 `runtimeActor == null`을 `NotAvailable`로 본다 —
이 상태에서는 `current`가 항상 null이라 `AlreadyCurrent`와 겹치지 않는다.

### 3.4 `Awake` 오류 경로

- 사용 가능한 항목 0 → 오류 + **액터가 있으면 `Deactivate()`**. `CurrentCharacterCanAct`는 "목록이
  비면 true"(로스터를 쓰지 않는 씬 호환 규칙)라서, 액터를 켜둔 채 두면 아무도 투입되지 않았는데
  공격이 통한다 — 그 구멍을 막는다.
- `runtimeActor` 미연결 → 명확한 오류를 남기고 `current`를 null로 둔다. 사용 가능한 캐릭터가 있으므로
  `CurrentCharacterCanAct`는 false가 되어 공격이 시작되지 않는다.
- 저장 동기화(`SyncSaveStates`)와 디버그 행동력은 액터 유무와 무관하게 그대로 실행된다 — 액터가 없어도
  리스트/행동력 UI는 정상 동작한다.
- **캐릭터 GameObject를 순회하는 코드는 한 줄도 남지 않았다.**

### 3.5 보존한 것

`Entries`, `Current`, `CurrentCharacterCanAct`, `GetLevel/GetStamina/GetMaxStamina`,
`SpendStamina/SetStamina`, `ApplyRecoveryStamina`, `RaiseCharacterStateChanged`,
`DrainCurrentStamina`, 처치 1회당 행동력 소비, 회복 중 교체/변경 차단, `ResolveStartCharacter`의
결정적 선택 순서, 회복 합류 시 자동 교체 없음 — 전부 그대로다.

---

## 4. 검증

- `git status` / `git diff --stat`: 소스 2개만 수정(2단계 산출물 4개는 무변경). 씬·`Assets/Data`
  변경분은 **작업 시작 시점의 사용자 dirty 그대로**(322/2×6 라인, 이번 작업이 건드리지 않음).
- 직접 `csc` 컴파일(2단계와 동일한 소스·참조·define, `CharacterRuntimeActor.cs` 포함):
  - `Assembly-CSharp` 112 소스 → **오류 0**
  - `Assembly-CSharp-Editor` 6 소스 → **오류 0** (Motion Editor 연동 유지 확인)
  - 새 경고는 `runtimeActor`의 CS0649 하나뿐 — 프로젝트의 모든 `[SerializeField]` 필드와 같은 종류다.
- Unity Editor는 열지 않았고 씬/에셋 임포트도 돌리지 않았다.

> **런타임 실전 검증은 아직이다.** 실제 교체 연출과 시작 시 투입은 Windows 데스크톱 실행에서만
> 최종 확인할 수 있다.

---

## 5. 남은 Unity 에디터 작업

1. `CharacterRoster`의 **Runtime Actor** 칸에 씬의 `CharacterRuntimeActor` 오브젝트를 연결한다.
2. 액터로 쓸 오브젝트에 `CharacterRuntimeActor` 컴포넌트를 붙인다(필수 6종 컴포넌트는 기존 캐릭터
   오브젝트가 이미 전부 갖고 있다 — 2단계 보고서 3절).
3. Entries의 `Character Object` 칸이 사라진 것을 확인하고 씬을 저장한다(저장 시 YAML에서 제거됨).
4. 남은 캐릭터 오브젝트 정리와 CatMage `HitEffectSpawner` 값 처리(2단계 보고서 5절)는 여전히
   사용자 판단이 필요하다.
