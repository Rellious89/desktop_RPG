# 공격 애니메이션 규칙

공격은 두 가지 타입으로 분류한다: 연속 입력에 반응하는 **연속 공격(LoopableBasic)**과, 한 번 시작하면 끝까지 완주하는 **1회성 공격/스킬(CommittedSkill)**.

이 문서는 규칙이며, 개발 단계에 따라 언제든 바뀔 수 있다.

## 기본 공격 (`LoopableBasic`)

기본 공격은 키보드 연타를 시각적으로 표현하는 핵심 모션이다.

- 입력 중 반복 가능한 공격 루프를 사용한다.
- 준비 프레임과 히트 프레임을 반복한다.
- 실제 타격은 애니메이션의 `HitPoint`가 지정된 프레임에서만 발생한다.
- 키 입력마다 애니메이션을 처음부터 재시작하지 않는다.
- 입력이 유지되는 동안 공격 루프를 유지한다.
- 마지막 입력 이후 유예 시간이 지나면 복귀 프레임을 재생한 뒤 Idle로 돌아간다.
- 콤보 단계에 따라 기본 공격의 변형 애니메이션, 속도, 전진 거리, 이펙트, 피격 강도를 변경할 수 있다.
- 콤보 변형도 기본 공격과 같은 연타 가능 구조를 유지한다.

```text
Idle
→ Basic Attack Start
→ Basic Attack Loop (준비 ↔ Hit 반복)
→ Basic Attack End
→ Idle
```

### 현재 구현 상태

`Assets/Scripts/Character/PlayerCharacterAnimator.cs`가 이 타입을 구현한다.

- `AttackAnimation` 데이터: `animationFps`(Windup/Recovery 프레임 전환 속도, 이전 이름 `stepFramesPerSecond`), `endFrameDuration`(복귀 프레임 노출 시간), `queueExpireTimeout`(마지막 입력 이후 예약을 취소하는 유예 시간)
- Idle 계열(`FrameAnimation`)의 프레임 재생 속도 필드도 동일하게 `animationFps`로 통일했다 — Idle과 Attack 모두 "이 애니메이션의 프레임을 초당 몇 번 전환할지"라는 같은 의미로 같은 이름을 쓴다.
- 상태: `AttackPhase.None → Windup → Strike → Recovery → None`
- 키 입력은 애니메이션을 직접 트리거하지 않고 `pendingAttacks` 대기열에 쌓이며, Strike가 끝날 때마다 하나씩 소비된다. 입력이 끊기면(`queueExpireTimeout` 경과) 남은 예약은 버리고 진행 중인 사이클만 마친 뒤 복귀한다.
- 이벤트: `AttackStarted`(세션 시작 1회), `HitPoint`(타격마다, 위 규칙의 `HitPoint`와 동일), `AttackEnded`(Idle 복귀 시 1회)
- 콤보 티어별 공격 모션은 `ComboTierAttackPool` 에셋을 통해 선택한다. Tier 3 풀은 비어 있을 경우 Tier 2 → Tier 1 순으로 폴백하고, Tier 2는 Tier 1로 폴백한다.
- `PlayerCharacterAnimator`의 레거시 `attack` 필드는 Tier 1 풀을 아직 연결하지 않은 기존 씬의 하위 호환용이다. 새 리소스는 `tier1Pool`/`tier2Pool` 에셋에 등록한다.

#### 공격 모션 Tier의 의미와 누적 풀 규칙

Tier는 공격의 강도나 스킬 등급이 아니라 **현재 기본 공격에서 선택할 수 있는 서로 다른 공격 모션의 누적
개수**를 뜻한다. 각 모션은 같은 기본 공격의 시각적 변형이며, Tier가 올라갈 때 새 모션 한 종류를 추가한다.

```text
Tier 1 Pool = Attack A
Tier 2 Pool = Attack A + Attack B
Tier 3 Pool = Attack A + Attack B + Attack C
```

- Attack A/B/C는 모두 기본 공격이며 하나가 다른 하나보다 강하다는 뜻이 아니다.
- 각 모션은 캐릭터의 전투 방식 안에서 준비·타격·복귀 자세만 다르게 만든다.
- 최종 출시 품질 목표는 플레이어 캐릭터당 기본 공격 모션 3종, 즉 Tier 3 풀까지다.
- 현재 제작 단계에서는 일부 캐릭터만 Tier 2까지 시험하며 Tier 3를 필수 공백으로 취급하지 않는다.
- `ComboTierAttackPool`은 하위 모션을 자동으로 합치지 않는다. 따라서 Tier 2 에셋에 A와 B를 모두, Tier 3
  에셋에 A/B/C를 모두 직접 등록한다.
- 풀 안에서는 현재 구현대로 모션을 균등 랜덤 선택하며 직전 모션과 같은 결과도 허용한다.
- 상위 풀이 비어 있을 때만 기존 폴백 규칙(Tier 3 → Tier 2 → Tier 1)을 사용한다.
- 공격 B/C를 강공격, 필살기 또는 `CommittedSkill` 전제로 설계하지 않는다.

#### `queueExpireTimeout` 동작 (시간값 점검 결과 반영)

- 입력 큐(`pendingAttacks`)는 단순 카운터다. **입력 하나하나에 개별 만료시간을 두지 않는다.**
- 새 키 입력이 들어올 때마다 `lastInputTime`(마지막 입력 시각) 하나만 갱신된다.
- 타격(Strike) 시점마다 "마지막 입력 이후 `queueExpireTimeout`이 지났는가"를 판정해서, 지났으면 남아있는 `pendingAttacks`를 전부 폐기하고 Recovery로 넘어간다. 지나지 않았고 대기 중인 타격이 있으면 곧바로 다음 Windup을 시작한다.
- 즉 "큐 항목별 만료"가 아니라 "마지막 입력 시각 기준의 전체 폐기 판정"이다. 이 동작은 그대로 유지하며, 문서화만 명확히 한다.

#### 공격 세션 간 최소 간격 (`postAttackDelay`)

- 별도의 최소 간격 값(`postAttackDelay`)은 **추가하지 않는다.**
- 공격 세션이 끝나(`FinishSession`) `Idle`로 돌아온 직후 새 입력이 들어오면, 대기시간 없이 즉시 다음 공격 세션을 시작할 수 있다. 이것이 현재 규칙이다.

#### 연속 공격 시 Recovery 생략

- 콤보(연타)가 이어지는 동안에는 Recovery 단계 자체에 진입하지 않고 곧바로 다음 Windup으로 넘어간다 — 이때 `endFrameDuration`도 함께 건너뛴다(Recovery에 진입해야만 참조되는 값이라 자연히 생략됨).
- 이 "Recovery 생략" 동작은 **현재 CatKnight 기본 공격의 연계 방식**으로 기록한다. 모든 캐릭터/공격에 적용되는 공통 규칙으로 아직 확정하지 않는다 — 캐릭터/스킬에 따라 콤보 중에도 Recovery를 강제로 보여줘야 하는 경우가 생길 수 있기 때문이다.

## 1회성 공격 / 스킬 (`CommittedSkill`)

강공격, 콤보 피니셔, 스킬처럼 긴 준비 동작이나 명확한 마무리 동작이 필요한 공격은 1회성으로 처리한다.

- 준비 → 히트 → 복귀까지 한 번 완주한다.
- 진행 중 새 키 입력이 들어와도 애니메이션을 재시작하지 않는다.
- 새 입력은 콤보, 다음 기본 공격 유지 시간, 또는 입력 버퍼에 누적한다.
- 1회성 공격 종료 후 입력이 계속되고 있다면 Idle이 아니라 기본 공격 루프로 복귀한다.
- 일반 키 입력 하나에 직접 연결하지 않는다.
- 콤보 조건, 누적 입력량, 쿨다운, 랜덤 연출 등으로 예약 발동한다.

```text
Basic Attack Hit
→ Skill Start (긴 준비)
→ Skill Hit
→ Skill End
→ 입력 유지 시 Basic Attack Loop
→ 입력 없음 시 Idle
```

### 현재 구현 상태

미구현. `CommittedSkill` 타입 자체가 아직 코드에 없다. 스킬/마법 공격을 추가할 때 신설한다.

## 공통 원칙

- 모든 공격은 프레임 번호가 아닌 `HitPoint`를 실제 판정 기준으로 사용한다.
- 기본 공격은 타이핑 리듬과 즉시성을 담당한다.
- 1회성 공격은 누적된 타이핑에 대한 보상과 강조 연출을 담당한다.
- 빠른 입력 중에도 입력은 버리지 않으며, 공격 애니메이션만 적절한 리듬으로 표현한다.

## Attack Movement (캐릭터 전체 이동 연출)

Sprite Pivot은 Actor Origin이며 애니메이션 프레임 내부에서는 바뀌지 않는다([character-sprite-and-animator-rules.md](./character-sprite-and-animator-rules.md) 참고). 그래서 캐릭터 전체가 월드상에서 앞으로 튀어나갔다 돌아오는 것 같은 이동 연출은 스프라이트 프레임이 아니라 별도의 **Attack Movement** 설정으로 처리한다.

- Attack Movement는 Transform 위치만 움직이며, `SpriteFlipbook`의 애니메이션 재생과는 독립적이다.
- 공격별 선택 사항이다 — 이동이 필요 없는 공격은 이동 거리를 **0**으로 설정한다.
- 현재 구현: `Assets/Scripts/Character/AttackMovement.cs` (클래스명 `KeyPunchReaction` → `AttackMovement`, 필드명도 함께 리네임했다). `moveDistance`(이동 거리), `moveOutDuration`(전진 시간), `moveBackDuration`(복귀 시간)로 구성된다.

## 타격 이펙트 (Hit Effect)

`PlayerCharacterAnimator.HitPoint → Target.ApplyDamage → 피격 반응(자세/플래시/흔들림) → 데미지 숫자 → 타격 이펙트` 순서를
`Assets/Scripts/Enemy/TargetCombatController.cs`의 `OnHitPoint`가 유지한다. 이펙트 생성 자체는 재사용 컴포넌트로 분리했다.

- `Target.ApplyDamage`는 이번 타격이 처치를 유발하면 `OnDefeated`를 동기 호출한다. 이후
  `defeatFadeDuration → respawnDelay → OnRespawnStarted → respawnFadeDuration → OnRespawned` 순서로 진행한다.
  Fade-in 완료 전까지 `Target.IsDefeated`가 true이므로 새 공격과 콤보 입력은 차단되고, 콤보 만료 타이머는 유예된다.
- `OnHitPoint`는 `HandleDefeated`가 동기적으로 설정하는 `defeatedByCurrentHit`로 처치 타격을 구분한다.
  처치를 유발한 마지막 타격의 피격 자세·플래시·데미지 숫자·히트 이펙트는 정상 출력하되, 이후 예약 공격은 폐기한다.

- `Assets/Scripts/Common/HitEffectSpawner.cs`: 피격 대상에 붙는 재사용 컴포넌트(Target, DamageNumberSpawner와 같은 패턴). `defaultEffectPrefab`/`impactPoint`/`fallbackOffset`/`defaultDuration`을 Inspector에서 받는다. `Spawn(prefabOverride, durationOverride)`에 다른 prefab을 넘기면 강공격/콤보 티어/치명타 전용 이펙트도 같은 구조로 재생할 수 있다(아직 기본 이펙트 1종만 연결됨).
- 생성된 이펙트는 `StageVisualRoot` 하위의 공통 `CombatFxRoot`에 풀링한다. 생성 순간 `ImpactPoint`의 월드 위치를 `CombatFxRoot` 로컬 좌표로 변환해 스냅샷으로 사용하므로 Stage 위치/배율은 정확히 한 번 상속하지만, 이후 Target의 흔들림이나 이동은 따라가지 않는다.
- `impactPoint`를 비워두면 `fallbackOffset`을 이 오브젝트 기준으로 더한 위치를 안전하게 대신 쓴다. prefab이 비어 있거나 duration이 비정상값(0 이하/NaN/Infinity)이어도 예외 없이 무시하거나 기본값(0.15초)으로 보정한다.
- 처치 판정은 `OnHitPoint` 진입 시점(`target.IsDefeated`)을 기준으로 한다 - 이미 처치된 상태로 들어온 타격은 맨 앞에서 걸러지고, 살아있던 대상을 처치하는 마지막 타격은 `ApplyDamage` 이후 `IsDefeated`가 true가 되어도 데미지 숫자/이펙트까지 끝까지 표시한다.
- `Assets/Scripts/Common/HitEffectPop.cs`: 기본 이펙트 prefab의 짧은 확대·Fade 재생을 담당하며, 완료 콜백으로 `HitEffectSpawner` 풀에 반환된다. `HitEffectPop`이 없는 prefab은 스포너의 대기 코루틴이 대신 회수한다.
- 기본 이펙트 에셋: `Assets/Art/Effects/HitBasic/HitBasic-spark-00.png`(128×128, PPU 200, pivot 중앙) + `Assets/Prefabs/Effects/HitEffect_Basic.prefab`. 정식 아트 리소스가 없어 만든 더미용 스타버스트 스프라이트다 - 실제 이펙트 아트가 준비되면 이 prefab의 `SpriteRenderer`만 교체하면 된다.
- Scarecrow의 `ImpactPoint` 자식 Transform(`Assets/Scenes/desktopScene.unity`)이 `HitEffectSpawner.impactPoint`에 연결되어 있다 - Scarecrow 기준 대략 몸통 높이(로컬 y 0.5)에 둔 임시 지점이며, 정확한 피격 위치는 아트가 확정되면 다시 조정될 수 있다.

## 발사체 (Projectile)

원거리 공격의 발사체는 캐릭터 오버레이(`overlayFrames`)에 그려 넣지 않고, 시전 위치에서 몬스터 피격 위치까지 실제로 이동하는 독립 오브젝트로 처리한다. 최초 적용은 RabbitHealer 기본 공격 프로토타입이다.

### 데이터

공격 모션 하나가 소유하는 값은 세 개뿐이다(`AttackMotionDefinition` / `IAttackMotion` / 레거시 `PlayerCharacterAnimator.AttackAnimation` 모두 동일).

- `projectilePrefab` (기본값 없음/비어 있음): **비어 있으면 발사체 관련 처리를 전부 건너뛰어 기존 근접 공격과 완전히 동일하게 동작한다.**
- `projectileLaunchOffset` (기본 `(0,0)`): 시전자 Actor Origin 기준 로컬 오프셋. 캐릭터 `SpriteRenderer.flipX`면 X만 좌우 반전한다.
- `projectileScale` (기본 `1`): 발사체 prefab 원본 로컬 스케일에 곱하는 배율.

발사체 내부의 스프라이트/재생 데이터는 공격 모션이 아니라 **발사체 prefab이 소유한다** - 공격 모션에 중복 저장하지 않는다. 공격별 피격 이펙트 설정(`hitEffectPrefab/Offset/Scale`)도 기존 `AttackHitCue` 경로 그대로이며 발사체 prefab에 복제하지 않는다.

### 타이밍

발사체 때문에 공격 애니메이션이나 다음 공격이 기다리는 일은 없다. 기존 `HitPoint` 흐름을 그대로 두고 그 위에 얹는다.

1. `Cast Frame` - `TryFireCastCue()`가 Cast Effect/Sound를 실행한 뒤 발사체를 생성한다(공격 인스턴스당 한 번).
2. `Cast Frame → Hit Frame` - 직선 이동. 비행 시간은 `(HitFrameIndex - CastFrameIndex) / AnimationFps`로 계산하며, 별도의 고정 속도 값은 두지 않는다.
3. `Hit Frame` - `Strike()`가 `HitPoint`를 쏘기 **직전에** 발사체를 목표 위치로 스냅하고 완료시킨다. 프레임 드롭이나 Update 오차가 있어도 시각과 피격 타이밍이 어긋나지 않는다.
4. 이후 피해/피격 반응/데미지 숫자/피격 이펙트/사운드는 기존 순서 그대로다.

발사체는 어떤 판정도 발생시키지 않는다 - 피해는 오직 기존 `HitPoint` 한 번뿐이라 한 공격에서 피해가 두 번 들어갈 여지가 없다.

발사체를 쓰는 공격은 `CastFrameIndex < HitFrameIndex`여야 한다. 같거나 Cast가 더 늦으면 날아갈 구간 자체가 없으므로 모션당 한 번 경고를 남기고 발사체 없이 진행한다(공격 자체는 정상 동작).

RabbitHealer 기본 공격 기준: FPS 18, Cast 0, Hit 1 → 비행 시간 약 0.056초.

### 구성 요소

- `Assets/Scripts/Common/ProjectileMover.cs`: 발사체 루트. 시작점/도착점/진행도/`+X` 기준 방향 회전/수명/풀 반환을 담당한다. 시각 표현은 전혀 건드리지 않는다.
- `Assets/Scripts/Common/IProjectileVisual.cs`: 표현 컴포넌트 계약(`BeginFlight` → `SetFlightProgress(0~1)` → `ResetVisual`). Mover가 루트/자식을 통틀어 전부 찾아 같은 진행도를 넘기므로, 이동 시스템이 특정 `SpriteRenderer` 한 장에 묶이지 않는다.
- `Assets/Scripts/Common/ProjectileSpriteAnimation.cs`: 기본 표현 구현. Sprite 배열을 비행 시간에 균등 분배해 재생한다 - 한 장이면 단일 이미지 발사체, 여러 장이면 애니메이션 발사체이며 Mover는 둘을 구분하지 않는다. Fade In/Out은 기본 0(꺼짐)이고, 비행 시간이 `minFadeFlightDuration`(기본 0.12초)보다 짧으면 켜져 있어도 적용하지 않는다.
- `Assets/Scripts/Common/ProjectileSpawner.cs`: prefab별 오브젝트 풀. 시전자에 붙으며, `PlayerCharacterAnimator.Awake`가 없으면 자동으로 붙인다. 피격 이펙트 풀(`HitEffectSpawner`)과 책임을 섞지 않는다 - 수명 규칙과 복원 대상이 다르기 때문이다.

### 방향 규칙

- 발사체 원본 이미지의 **오른쪽(+X)이 머리(진행 방향), 왼쪽(-X)이 꼬리**다.
- 런타임에 `시전 위치 → 도착 위치` 벡터로 Z축 회전을 계산한다.
- 캐릭터의 `SpriteRenderer.flipX`는 발사체에 상속하지 않는다 - 좌우 반전은 발사 위치 X 오프셋에만 적용된다.

### 좌표 규칙

- 발사체는 캐릭터의 자식이 아니라 `StageVisualRootController.CombatFxRoot` 아래에서 이동한다 - 캐릭터의 flipX나 Attack Movement를 따라가지 않으면서, Stage 위치/배율은 Transform 계층을 통해 정확히 한 번만 상속받는다(`HitEffectSpawner`와 같은 규칙 - 별도 배율 보정 코드를 두지 않는다).
- 시작점: 캐릭터 `transform.TransformPoint(launchOffset)` - Actor Scale과 Stage 배율을 그대로 따라가므로 어떤 배율에서도 시전 손 위치에 붙어 있는다.
- 도착점: 몬스터 `HitEffectSpawner.GetImpactWorldPosition(hitEffectOffset)` - 피격 이펙트와 같은 기준점이다. private 필드에 직접 의존하지 않는 읽기 전용 API이며 `Spawn()` 동작은 바꾸지 않았다. 피격 이펙트의 랜덤 지터는 이펙트 내부 표현이라 발사체 목표점에는 반영하지 않는다.
- 조준 대상은 `Target.TryGetAttackableTarget()`(읽기 전용 정적 조회)으로 찾는다. 목표 위치는 발사 순간에 스냅샷으로 굳으므로, 비행 중 대상이 처치되거나 리젠돼도 이전 발사체가 새 몬스터를 따라가지 않는다.

### 풀링

- prefab별 `Queue`로 재사용한다(기본 prewarm 4개). 여러 발사체가 동시에 날아가도 정상 동작하며, 풀이 비면 그때만 예외적으로 추가 생성한다.
- 반환 시 위치/회전/로컬 스케일은 스포너가, Sprite/알파/재생 진행도는 `IProjectileVisual.ResetVisual`이 프리팹 원본 값으로 되돌린다.
- 각 발사에 일련번호(`LaunchId`)를 발급한다 - 호출자가 완료시킬 때 대조해서, 이미 회수돼 다른 공격에 재사용된 인스턴스를 실수로 건드리지 않는다.
- Strike가 끝내 오지 않으면(캐릭터 비활성화/교체) 도착 후 `arrivalHoldSeconds`(기본 0.35초) 뒤 스스로 회수된다. 캐릭터가 비활성화될 때도 `PlayerCharacterAnimator.OnDisable`이 즉시 회수한다.

### RabbitHealer 프로토타입 연결값

`Assets/Data/MotionProfiles/Characters/RabbitHealer/RabbitHealer_Attack.asset`

| 항목 | 값 |
| --- | --- |
| `projectilePrefab` | `Assets/Prefabs/Effects/Projectile_RabbitHealer_Basic.prefab` |
| `projectileLaunchOffset` | `(0.5, 0.22)` - 지팡이 끝 추정 위치, Play 모드에서 미세 조정 필요 |
| `projectileScale` | `0.35` |

발사체 prefab의 스프라이트는 아직 **임시**로 기존 더미 이펙트(`HitBasic-spark-00.png`)를 쓴다 - 정식 발사체 아트가 준비되면 `Visual` 자식의 `SpriteRenderer.sprite`와 `ProjectileSpriteAnimation.frames`만 교체한다. 오버레이(`overlayFrames`)에 그려져 있는 기존 노란 발사체는 아직 남아 있다 - 무기 잔상과 캐스팅 섬광만 남기고 지우는 것은 사용자 아트 작업이다.
