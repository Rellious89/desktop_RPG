# 단일 런타임 액터 2단계 (프로필 교체 구현) 완료 보고서

작업일: 2026-07-31
범위: `CharacterRuntimeActor` 신규 / `PlayerCharacterAnimator`·`AttackMovement` 리팩터 /
`FlashOnCue` 잔상 최소 수정 / 씬 6액터 구성 감사.
전제: 씬(`desktopScene.unity`), 프리팹, ScriptableObject·데이터 에셋, `ProjectSettings` YAML,
여섯 캐릭터 오브젝트, `CharacterDefinition` 에셋을 **한 줄도 바꾸지 않았다**. `CharacterRoster.cs`도
이번 단계에서는 손대지 않았다. 커밋하지 않았고, 작업 시작 시점의 기존 dirty 변경(link.xml 삭제,
6개 Definition, 씬)은 그대로 보존했다.

> **씬 연결은 아직 없다.** 이번 산출물은 "붙이면 동작하는 컴포넌트"까지다.
> `CharacterRuntimeActor`는 아직 어떤 오브젝트에도 붙어 있지 않고 호출부도 없다 —
> 3단계에서 `CharacterRoster`가 유일한 호출부가 된다. 연결 전까지 기존 게임 동작은 완전히 그대로다.

---

## 1. 변경 파일

### 신규 (1개 + meta)

| 파일 | 붙는 위치 | 책임 |
| --- | --- | --- |
| `Assets/Scripts/Character/CharacterRuntimeActor.cs` | 런타임 캐릭터 액터 오브젝트 1개 | 정의/프로필/필수 컴포넌트 검증 → 비활성화 → 프로필 적용 → 배치 갱신 → 활성화의 순서 소유 |

`CharacterRuntimeActor.cs.meta`를 함께 추가했다(GUID `7d25a2cb…`). 프로젝트 전체 8714개 메타의
GUID가 모두 유일함을 확인했다.

### 수정 (3개)

| 파일 | 변경 |
| --- | --- |
| `Assets/Scripts/Character/PlayerCharacterAnimator.cs` | `EnsureInitialized` / `ApplyProfileData` / `ClearCombatState` / `ResetToBaseIdle` 분리, `public bool TryApplyMotionProfile` 추가, `OnEnable`·`OnDisable`·`FinishSession`이 같은 헬퍼를 재사용, 이동 호출 3곳을 `isActiveAndEnabled` 기준으로 교정 |
| `Assets/Scripts/Character/AttackMovement.cs` | `EnsureInitialized` / `ApplyProfileLayout` 분리, `public bool RefreshFromCurrentProfile` 추가, `layoutApplied` 도입(기준점 미확정 시 위치를 건드리지 않음) |
| `Assets/Scripts/Common/FlashOnCue.cs` | `OnDisable`에서 코루틴 중단 + 원래 색 복원(섬광 중 교체 시 흰색이 굳는 잔상 제거) |

`basicAttackPower`와 기존 public API(`MotionProfile`, `SetPresentationBasePosition`,
`PlayAttackMove`, `RequestChargeMove`, `EndChargeMove`, 5개 정적 이벤트)는 전부 그대로 유지했다.

---

## 2. 동작 규칙

### 2.1 적용 순서는 하나뿐이다

`CharacterRuntimeActor.TryApply(definition)`:

1. **검증 먼저** — `definition != null`, `CharacterMotionProfile.IsPlayable(definition.MotionProfile)`,
   필수 컴포넌트 6종. 하나라도 어긋나면 **지금 화면에 있는 캐릭터를 전혀 건드리지 않고** false.
2. `gameObject.SetActive(false)` — 정리 규칙을 액터에 다시 적지 않는다. 각 컴포넌트의 기존
   `OnDisable`이 전투/충전/발사체/오버레이/이동 오프셋/섬광 색/외곽선 Material을 스스로 되돌린다.
3. `animator.TryApplyMotionProfile(profile)` → `attackMovement.RefreshFromCurrentProfile()`.
   이 순서여야 이동 컨트롤러가 **새** 프로필의 Actor Offset/Scale을 읽는다.
4. `CurrentDefinition = definition` → `SetActive(true)`.

**캐릭터 오브젝트를 절대 Instantiate/Destroy하지 않는다.** 교체 때마다 새로 만들면 지금 풀링으로
없앤 연타 중 GC 압박이 교체 시점에 되살아난다.

### 2.2 실패 시

`CurrentDefinition`은 바뀌지 않는다. 1번에서 실패하면 오브젝트를 끄지도 않으므로 화면 상태가
그대로다. 3번에서 실패하면 직전 프로필로 되돌리고 **원래 활성 상태였으면 다시 켠다** — 실패한
교체 때문에 화면에서 캐릭터가 사라지는 경로를 만들지 않았다. 되돌릴 수 있는 직전 프로필이 없으면
잘못된 자세로 켜지 않고 꺼진 채로 두고 오류를 남긴다.

### 2.3 `PlayerCharacterAnimator.TryApplyMotionProfile`이 지우는 것

`ClearCombatState()` 하나가 목록의 전부이고, `OnDisable`·`OnEnable`도 같은 메서드를 쓴다.

- 공격 단계(`attackPhase`)/프레임/단계 타이머
- Direct 대기열(`pendingAttacks`), 누적 충전량(`chargeInputs`), 이월 입력(`carriedInputs`)
- Cast 1회 플래그(`castCueFired`), 마지막 입력 시각, 보고된 충전 비율
- 충전 신호 — `EndChargeSignal()`이 `chargeSignalActive`로 막으므로 **`ChargeEnded`는 정확히 한 번**
  나가고, 이미 닫힌 상태에서 다시 불러도 두 번 나가지 않는다
- 이번 사이클의 모션/프레임 배열/오버레이 배열 + 화면에 남은 오버레이 스프라이트
- 날아가던 발사체와 `launchId`(`ReleaseActiveProjectile`)
- 발사체 조준 대상 캐시(`cachedProjectileTarget` / `cachedProjectileTargetSpawner`)
- 프레임 순서 경고 캐시(`warnedProjectileMotions`)
- Idle Event 재생 상태(`playingVariant` / `variantTimer`)

티어 풀 3개는 `BuildResolvedPool`이 채우기 전에 항상 `Clear()`하므로 이전 캐릭터의 모션이
남지 않는다. 적용 후에는 언제나 **Base Idle 0프레임**에서 시작한다(`ResetToBaseIdle`).

Tier 1 풀이 비어 있으면 기존과 동일하게 **오류를 남기되 컴포넌트를 끄지 않는다** — 공격만 영영
시작되지 않고 Idle은 정상 재생된다.

### 2.4 `AttackMovement.RefreshFromCurrentProfile`

`(옛 오프셋 0으로 복귀) → 진행 중 구간 취소 → 기준점 재계산 → Actor Scale → 이동 수치 재읽기` 순서.

기준점은 기존 공식 그대로 `CombatStageLayout.CharacterSlotPosition + Preview.ActorOffset`(z는 현재
값 유지)이고, **Stage Layout이 비어 있을 때의 폴백(씬에 배치된 현재 Transform 위치/스케일 유지)도
그대로 살아 있다**.

> 순서가 중요한 이유: 폴백 경로는 `transform.localPosition`을 그대로 기준점으로 읽는다. 오프셋을
> 먼저 0으로 되돌리지 않으면 이전 캐릭터의 전진 오프셋이 새 기준점에 눌러앉는다.

### 2.5 비활성/Awake 이전 호출 안전성

`EnsureInitialized()`(양쪽 컴포넌트)는 프로필 데이터를 전혀 보지 않고 컴포넌트 캐시만 잡으며,
`initialized` 플래그로 **정확히 한 번만** 실행된다. 따라서

- `ProjectileSpawner`(없으면 `AddComponent`)와 `AttackFrameOverlay` 자식은 **몇 번을 다시
  적용해도 한 번만 만들어져 재사용**된다. 비활성 오브젝트에서 호출해도 `AddComponent`와
  자식 생성 모두 유효하다.
- Awake보다 먼저 호출돼도 되고, Awake가 나중에 돌면 같은 경로로 한 번 더 적용될 뿐이다.

`AttackMovement`에는 `layoutApplied`를 새로 뒀다. 기준점을 한 번도 잡지 않은 상태의 `basePosition`은
그냥 `(0,0,0)`이라, 그 값으로 "오프셋을 0으로 되돌리면" 캐릭터가 **원점으로 순간이동**한다 —
이 플래그가 그 경로를 막는다(`OnDisable`도 같은 보호를 받는다).

### 2.6 `isActiveAndEnabled` 교정

애니메이터가 `AttackMovement`를 부르는 3곳(`PlayAttackMove` / `RequestChargeMove` /
`EndChargeMove`)의 조건을 `enabled` → `isActiveAndEnabled`로 바꿨다. 오브젝트가 꺼지는 중에는
컴포넌트의 `enabled`가 아직 true인데 `Update`는 돌지 않으므로, 그 시점에 이동 구간을 새로 여는 것은
다음 활성화까지 남는 잔여 상태만 만든다. 활성 상태에서는 두 값이 같아 기존 동작에 차이가 없다.

### 2.7 `Deactivate()`

`SetActive(false)` + `CurrentDefinition = null`이 전부다. 이미 꺼져 있으면 다시 끄지 않으므로
`OnDisable`이 중복 실행되지 않는다(반복 호출 안전). 정리는 전부 2.1의 2번 경로가 한다.

---

## 3. 씬 6액터 구성 감사 (읽기 전용)

`desktopScene.unity`를 파싱해서 확인했다. **아무것도 바꾸지 않았다.**

| 오브젝트 | SpriteRenderer | PlayerCharacterAnimator | AttackMovement | FlashOnCue | HitEffectSpawner | ActorOutlineController |
| --- | --- | --- | --- | --- | --- | --- |
| Cha_CatKnight (활성) | O | O | O | O | O | O |
| Cha_Barbarian | O | O | O | O | O | O |
| Cha_CatMage | O | O | O | O | O | O |
| Cha_ElfArcher | O | O | O | O | O | O |
| Cha_ElfGuardian | O | O | O | O | O | O |
| Cha_RabbitHealer | O | O | O | O | O | O |

- 여섯 모두 컴포넌트 구성이 동일하다. `CharacterRuntimeActor`가 요구하는 필수 컴포넌트는 이 6종이다.
- **`ProjectileSpawner`와 `AttackFrameOverlay` 자식은 어느 오브젝트에도 없다** — 전원
  `PlayerCharacterAnimator`가 런타임에 자동 생성한다(자식 `m_Children: []`, 스포너 미부착 확인).
  이번 리팩터로 이 생성이 초기화 1회에 묶여 반복 적용에도 중복되지 않는다.
- 공통값: `basicAttackPower: 3`, `stageLayout` 동일 에셋(`a11674de…`), `attackFrameOverlayRenderer`/
  `attackOverlayMaterial` 비어 있음, `FlashOnCue` `flashDuration: 0.1` + 흰색, 위치 `(-0.7, 0, 0)`,
  스케일 1, `ActorOutlineController.capturedOriginalMaterial` 동일.
- **차이는 CatMage의 `HitEffectSpawner` 하나뿐이다.**

| 필드 | CatMage | 나머지 5명 |
| --- | --- | --- |
| `spawnJitterX` / `spawnJitterY` | 0.08 / 0.08 | 0 / 0 |
| `fallbackOffset` | (0, 0.3) | (0, 0) |
| `poolSize` | 8 | 4 |

  이 값들은 **옮기지도 정규화하지도 않았다.** 3단계에서 액터 오브젝트를 하나로 합칠 때, 이 씬 값이
  캐릭터별로 달라야 하는지(=프로필 소유로 올려야 하는지) 아니면 하나로 통일해도 되는지는 사용자
  판단이 필요하다 — 5절 참고.

---

## 4. 검증

Unity Editor가 프로젝트 락을 잡고 있어 배치모드 재컴파일 대신 **동일한 소스/참조/전처리기 심볼로
직접 컴파일**해 확인했다(씬을 열지도, 임포트를 돌리지도 않았다).

- `Assembly-CSharp`: `Assembly-CSharp.csproj`의 소스 111개 + 신규 1개 = 112개, 참조 248개,
  `UNITY_EDITOR` 포함 전체 define으로 `csc` 컴파일 → **오류 0**, DLL 생성 성공.
- `Assembly-CSharp-Editor`: 위 결과물을 참조로 소스 6개 컴파일 → **오류 0**. `MotionEditorWindow`가
  쓰는 `SetPresentationBasePosition` / `MotionProfile`이 그대로 살아 있음을 함께 확인했다.
- 새 경고 없음. 손댄 4개 파일에 남은 경고 2건은 전부 변경 전에도 있던 `[SerializeField]` CS0649다.
- `.meta` GUID 8714개 전수 중복 검사 통과.
- `git status`로 기존 dirty 변경 8건이 그대로 남아 있고 추가로 바뀐 것은 소스 3개 + 신규 2개뿐임을
  확인했다.

> **런타임 실전 검증은 아직이다.** 실제 교체 연출(교체 중 발사체 회수, 섬광 잔상, 배치 점프 여부)은
> Windows 데스크톱 실행에서만 최종 확인할 수 있고, 호출부가 생기는 3단계 이후에나 가능하다.

---

## 5. 남은 것 / 확인 필요

1. **3단계 연결이 남았다.** `CharacterRoster`가 "오브젝트 6개를 켜고 끄는" 지금 방식에서
   "액터 1개에 `TryApply`" 방식으로 바뀌어야 한다. 이번 단계에서는 지시대로 손대지 않았다.
2. **CatMage의 HitEffectSpawner 값(3절)** — 액터를 하나로 합치면 씬 값도 하나만 남는다. 캐릭터별로
   달라야 하는 값이면 프로필로 올려야 하고, 아니면 어느 쪽 값으로 통일할지 정해야 한다.
3. **`FlashOnCue.originalColor`는 Awake 시점의 색이다.** 지금은 여섯 오브젝트 모두 흰색(1,1,1,1)이라
   문제가 없지만, 앞으로 캐릭터별 기본 색조를 쓰게 되면 그 값도 프로필 소유로 올려야 한다.
4. **`Deactivate()`는 본체 스프라이트를 지우지 않는다.** 꺼진 상태라 보이지 않고, 다음 `TryApply`가
   **활성화 전에** 새 Base Idle 0프레임을 적용하므로 한 프레임도 새지 않는다 — 의도적으로 남겼다.
5. **`RestorePreviousState`는 사실상 방어 코드다.** 컴포넌트 검증을 통과한 뒤에는
   `RefreshFromCurrentProfile`이 실패할 경로가 없다. 지워도 동작은 같지만, "실패해도 화면이 비지
   않는다"는 규칙을 코드로 남겨 두는 편을 택했다.
