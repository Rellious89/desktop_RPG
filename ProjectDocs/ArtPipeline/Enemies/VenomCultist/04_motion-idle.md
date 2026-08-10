# VenomCultist — Base Idle Motion Brief

> 상태: `Existing runtime documented`
>
> 재생성 기준 Master: `Assets/Art/Enemy/VenomCultist/master/VenomCultist-master-v1.png`

## Existing Runtime Motion

```text
Animation ID: idle
Type: Base Idle
Frames: 4
Animation FPS: 6
Loop: Yes
Canvas: 128×128 RGBA
Path: Assets/Art/Enemy/VenomCultist/idle/
Pivot: (0.5, 0.1)
```

## Motion Intent

- 양발을 고정하고 몸통과 어깨만 작게 오르내리는 경계 호흡이다.
- 깊은 후드, 해골 목걸이와 로브 자락은 호흡에 최소한으로 반응한다.
- 앞손의 독 단검은 계속 유지하고 독성 녹색은 작은 면적으로 제한한다.
- 암살 돌진이나 주문 시전으로 보이는 큰 예비 동작은 넣지 않는다.

## Regeneration Input

```text
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Facing direction dropdown: Not set
Motion description: breathe calmly in place; let one poison drop fall; keep both feet and pendant steady
```

## Acceptance Criteria

- 6fps 반복에서 양발과 몸 전체 위치가 고정된다.
- 후드, 해골 목걸이, 단검 한 자루와 로브 실루엣이 유지된다.
- 단검이 손에서 사라지거나 두 자루로 늘어나지 않는다.
- 독 방울 생성 품질이 낮으면 본체 프레임을 우선 보존하고 독만 후가공한다.

