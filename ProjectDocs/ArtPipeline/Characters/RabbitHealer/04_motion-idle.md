# RabbitHealer — Base Idle Motion Brief

> 상태: `Existing runtime documented`
>
> 재생성 기준 Master: `Assets/Art/Character/RabbitHealer/master/RabbitHealer-master-v2.png`

## Existing Runtime Motion

```text
Animation ID: idle
Type: Base Idle
Frames: 4
Animation FPS: 6
Loop: Yes
Canvas: 128×128 RGBA
Path: Assets/Art/Character/RabbitHealer/idle/
Pivot: (0.5, 0.1)
```

## Motion Intent

- 양발을 고정한 작고 편안한 호흡을 사용한다.
- 어린 힐러의 온화함과 주변 환자를 살피는 차분함이 읽혀야 한다.
- 귀는 몸의 호흡을 따라 최소한만 움직이며 별도의 큰 흔들림을 만들지 않는다.
- 완드와 가방은 중력과 몸통 호흡을 따라 1px 안팎으로만 반응한다.
- 공격, 주문 축적, 공포와 승리 포즈처럼 보이지 않는다.

## Locked Parts

- 양발과 접지선, 귀를 제외한 전체 신체 크기
- 긴 귀의 길이, 간격과 양쪽 분홍색 안쪽
- 얼굴, 눈, 코와 머리핀
- 멜빵 구조, 반팔 소매와 크로스백 끈
- 완드 길이, 쥔 손과 화면 방향

## Regeneration Input

```text
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Facing direction dropdown: Not set
Motion description: gentle breathing with a tiny torso rise; keep both feet, long ears, short wand, and crossbody bag steady
```

## Acceptance Criteria

- 6fps 반복에서 부드러운 호흡으로 읽힌다.
- 양발 하단과 Pivot 기준이 네 프레임에서 고정된다.
- 귀 길이와 안쪽 분홍 영역이 프레임마다 변하지 않는다.
- 가방 끈, 멜빵과 완드 구조가 유지된다.
- 화면 방향, 팔레트와 캐릭터 크기가 바뀌지 않는다.
