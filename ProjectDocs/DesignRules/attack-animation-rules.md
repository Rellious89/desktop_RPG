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

`Assets/Scripts/Character/CatKnightIdleAnimator.cs`가 이 타입 하나만 구현한 상태다.

- `AttackAnimation` 데이터: `animationFps`(Windup/Recovery 프레임 전환 속도, 이전 이름 `stepFramesPerSecond`), `endFrameDuration`(복귀 프레임 노출 시간), `queueExpireTimeout`(마지막 입력 이후 예약을 취소하는 유예 시간)
- Idle 계열(`FrameAnimation`)의 프레임 재생 속도 필드도 동일하게 `animationFps`로 통일했다 — Idle과 Attack 모두 "이 애니메이션의 프레임을 초당 몇 번 전환할지"라는 같은 의미로 같은 이름을 쓴다.
- 상태: `AttackPhase.None → Ready → Strike → End → None`
- 키 입력은 애니메이션을 직접 트리거하지 않고 `pendingAttacks` 대기열에 쌓이며, Strike가 끝날 때마다 하나씩 소비된다. 입력이 끊기면(`queueExpireTimeout` 경과) 남은 예약은 버리고 진행 중인 사이클만 마친 뒤 복귀한다.
- 이벤트: `AttackStarted`(세션 시작 1회), `HitPoint`(타격마다, 위 규칙의 `HitPoint`와 동일), `AttackEnded`(Idle 복귀 시 1회)
- 콤보 단계별 변형(속도/전진 거리/이펙트/피격 강도 변화)은 아직 미구현 — 현재는 콤보 개념 없이 단일 기본 공격만 존재한다.

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
