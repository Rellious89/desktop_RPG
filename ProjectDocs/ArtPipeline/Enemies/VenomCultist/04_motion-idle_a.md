# VenomCultist — Idle A Prayer Motion Brief

> 상태: `Existing runtime documented`
>
> 재생성 기준 Master: `Assets/Art/Enemy/VenomCultist/master/VenomCultist-master-v1.png`

## Existing Runtime Motion

```text
Animation ID: idle_a
Type: Idle Variant / Prayer
Frames: 6
Animation FPS: 6
Loop: Event clip
Path: Assets/Art/Enemy/VenomCultist/idle_a/
Runtime: MonsterMotionProfile idleEvents[0]
```

## Key Poses and Intent

1. 머리와 후드를 숙이며 단검을 숨긴다.
2. 빈 두 손을 가슴 앞에 모아 짧게 기도한다.
3. 상체를 세우고 Base Idle로 복귀한다.

기도 중 단검이 별도 수납 동작 없이 사라지고 복귀 때 다시 나타나는 것은 승인된 게임적 생략이다. 단검을
로브나 허리춤에 새로 그리지 않는다.

## Regeneration Input

```text
Animation name: IdleA
Frames: 6
FPS: 6
Repeat: Once
Facing direction dropdown: Not set
Motion description: bow the hood and clasp both empty hands in prayer; return to idle
```

## Acceptance Criteria

- 양발, Pivot과 몸 전체 위치가 고정된다.
- 모은 손이 해골 목걸이를 일부 가릴 수 있지만 목걸이 형태가 바뀌지 않는다.
- 기도 중 손이나 공중에 단검이 남지 않는다.
- 공격 주문, 발광 폭발과 새로운 장비를 추가하지 않는다.

