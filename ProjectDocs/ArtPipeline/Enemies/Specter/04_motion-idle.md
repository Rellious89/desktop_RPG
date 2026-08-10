# Specter — Base Idle Motion Brief

> 상태: `Existing runtime documented`
>
> 재생성 기준 Master: `Assets/Art/Enemy/Specter/master/Specter-master-v2.png`

## Existing Runtime Motion

```text
Animation ID: idle
Type: Base Idle
Frames: 4
Animation FPS: 6
Loop: Yes
Canvas: 128×128 RGBA
Path: Assets/Art/Enemy/Specter/idle/
Pivot: (0.5, 0.1)
```

## Motion Intent

- 바닥 투영점을 고정한 채 천 몸체만 작게 위아래로 부유한다.
- 양팔과 늘어진 손, 세 갈래 밑단이 몸체의 부유를 따라 약하게 흔들린다.
- 눈구멍 2개와 작은 열린 입은 네 프레임 모두 같은 얼굴로 유지한다.
- 큰 이동, 공격 준비, 연기 확산으로 보이지 않아야 한다.

## Regeneration Input

```text
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Facing direction dropdown: Not set
Motion description: gently bob in place; sway the cloth arms and three lower tails; loop smoothly
```

## Acceptance Criteria

- 6fps 반복에서 시작과 끝이 튀지 않는다.
- Actor Origin과 화면 방향이 고정된다.
- 흰 천, 양손, 눈 2개, 입과 세 갈래 밑단이 변형되지 않는다.
- 외부 그림자, 무기와 반투명 연기 덩어리를 추가하지 않는다.

