# VenomCultist — Hit Hold/Recovery Motion Brief

> 상태: `Existing runtime documented`
>
> 재생성 기준 Master: `Assets/Art/Enemy/VenomCultist/master/VenomCultist-master-v1.png`

## Existing Runtime Motion

```text
Animation ID: hit
Type: HitHoldRecovery
Frames: 2
Animation FPS: 6
Sequential playback: No
Hold frame index: 0
Recovery frame index: 1
Recovery duration: 0.12s
Hold timeout: 0.2s
Path: Assets/Art/Enemy/VenomCultist/hit/
```

## Key Poses

1. Hold: 양발은 고정하고 상반신이 펀치 충격으로 뒤·옆으로 비틀리며, 단검 한 자루가 손에서 분리된다.
2. Recovery: 상반신이 중앙으로 돌아오기 시작하고 단검이 앞손 위치로 복귀할 준비를 한다.

현재 런타임은 두 프레임을 연속 재생하지 않고 Hold와 Recovery Sprite를 시간에 따라 고정 표시한다.

## Regeneration Input

```text
Animation name: Hit
Frames: 2
FPS: 8
Repeat: Once
Facing direction dropdown: Not set
Motion description: twist the upper body backward; release one dagger; begin recovering toward idle
```

## Acceptance Criteria

- Hold와 Recovery가 서로 다른 자세로 읽힌다.
- Hold에서 단검은 손과 공중에 중복되지 않고 정확히 한 자루만 존재한다.
- 양발, Actor Origin과 전체 위치가 고정된다.
- 깊은 후드와 해골 목걸이가 사라지거나 다른 장식으로 변하지 않는다.

