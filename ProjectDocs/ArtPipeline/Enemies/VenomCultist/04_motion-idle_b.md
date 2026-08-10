# VenomCultist — Idle B Dagger Slash Motion Brief

> 상태: `Existing runtime documented`
>
> 재생성 기준 Master: `Assets/Art/Enemy/VenomCultist/master/VenomCultist-master-v1.png`

## Existing Runtime Motion

```text
Animation ID: idle_b
Type: Idle Variant / Dagger Slash
Frames: 6
Animation FPS: 6
Loop: Event clip
Path: Assets/Art/Enemy/VenomCultist/idle_b/
Runtime: MonsterMotionProfile idleEvents[1]
Gameplay hit: None
```

## Key Poses and Intent

1. 단검 팔과 상체를 작게 당긴다.
2. 허공을 향해 단검을 한 번 휘두른다.
3. 짧게 Follow-through한 뒤 Base Idle 위치로 복귀한다.

이 모션은 위협적인 대기 행동이며 공격 판정이 없다. 현재 Passive Enemy 구조에 기본 공격이 생긴 것으로
해석하지 않는다.

## Regeneration Input

```text
Animation name: IdleB
Frames: 6
FPS: 8
Repeat: Once
Facing direction dropdown: Not set
Motion description: slash one dagger through empty air; follow through briefly; return to idle
```

## Runtime Difference

PerfectPixel 입력은 8fps였지만 현재 MotionProfile은 6fps다. 기존 플레이 감각을 바꾸지 않기 위해 패키지의
현행값은 6fps로 기록하며, 8fps 복원은 별도 플레이 검수 대상으로 남긴다.

## Acceptance Criteria

- 단검은 항상 한 자루이며 다른 손에 새 무기가 생기지 않는다.
- 발과 Pivot은 고정하고 상체와 단검 팔만 회전한다.
- 후드와 해골 목걸이가 휘두르는 동안 유지된다.
- 실제 대상, 타격 이펙트와 이동을 추가하지 않는다.

