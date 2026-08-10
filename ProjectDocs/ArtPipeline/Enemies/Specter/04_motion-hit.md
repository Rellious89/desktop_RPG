# Specter — Hit Hold/Recovery Motion Brief

> 상태: `Existing runtime documented`
>
> 재생성 기준 Master: `Assets/Art/Enemy/Specter/master/Specter-master-v2.png`

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
Path: Assets/Art/Enemy/Specter/hit/
```

## Key Poses

1. Hold: 천과 양팔이 바깥으로 순간적으로 퍼지고 얼굴이 중앙으로 눌린 피격 자세.
2. Recovery: 퍼진 천이 안쪽으로 수축하며 Base Idle로 돌아갈 수 있는 중간 자세.

현재 런타임은 두 프레임을 6fps로 연속 재생하지 않는다. Hit 시 Hold를 고정하고 지정된 시간 뒤 Recovery
Sprite로 전환한다.

## Regeneration Input

```text
Animation name: Hit
Frames: 2
FPS: 8
Repeat: Once
Facing direction dropdown: Not set
Motion description: burst the sheet and arms outward; squeeze the face in pain; contract toward idle
```

## Acceptance Criteria

- Hold와 Recovery의 실루엣이 명확히 다르다.
- 천이 퍼지는 변화가 털, 가시, 광선이나 연기 폭발로 변하지 않는다.
- Actor Origin과 부유 기준은 두 자세에서 동일하다.
- 눈 2개, 작은 입, 양손과 세 갈래 밑단이 유지된다.

