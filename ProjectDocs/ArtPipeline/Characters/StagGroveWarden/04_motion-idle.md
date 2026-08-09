# StagGroveWarden — Base Idle Motion Brief

> 상태: PerfectPixel 입력 준비 완료
>
> 기준 이미지: `Assets/Art/Character/StagGroveWarden/master/StagGroveWarden-master-v1.png`

## 기본 설정

```text
Animation ID: idle
Type: Idle
Frames: 4
Animation FPS: 6
Loop: Yes
Final canvas: 128×128 RGBA
Final path: Assets/Art/Character/StagGroveWarden/idle/StagGroveWarden-idle-NN.png
Pivot candidate: (0.5, 0.234)
Ground-contact line: 30px from canvas bottom
```

게임플레이 목적은 다른 행동이 없을 때 캐릭터가 살아 있고 주변을 경계한다는 인상을 주는 것이다. 공격 준비,
마법 시전 또는 위협 포즈처럼 보이면 안 된다.

## 모션 원칙

- 흉곽과 복부의 느리고 작은 호흡만 중심 동작으로 사용한다.
- 머리는 몸통 호흡을 따라 최대 1px 범위에서만 움직인다.
- 뿔의 가지 수, 길이와 각도는 네 프레임 모두 동일해야 한다.
- 귀는 별도의 큰 움직임을 만들지 않고 머리와 함께 움직인다.
- 양발, 골반과 접지선은 완전히 고정한다.
- 지팡이 끝과 아래쪽이 프레임마다 흔들리거나 길어지지 않는다.
- 지팡이를 쥔 손의 그립 위치를 고정한다.
- 망토는 상체 호흡을 따라 최대 1px 정도만 움직이고 독립적으로 휘날리지 않는다.
- 짧은 꼬리는 몸 뒤의 동일한 위치를 유지한다.
- 청록색 마법석은 상시 발광하거나 마법 이펙트를 만들지 않는다.

## 프레임 설계

| 프레임 | 호흡 단계 | 자세 변화 |
|---:|---|---|
| 00 | 중립 / 날숨 완료 | 승인 Master의 자세와 장비 위치를 따르는 기준 프레임 |
| 01 | 들숨 | 흉곽이 미세하게 확장하고 몸통이 최대 1px 상승. 머리와 망토가 최소한만 따라감 |
| 02 | 들숨 정점 | 호흡이 가장 읽히는 프레임. 발·골반·지팡이 아래쪽은 00과 동일 |
| 03 | 날숨 / 복귀 | 01과 00 사이의 복귀 상태. 다음 00으로 위치 점프 없이 연결 |

```text
00 → 01 → 02 → 03 → 00
```

## 움직여도 되는 부위

- 흉곽과 복부의 작은 색면·형태 변화
- 몸통 호흡을 따라가는 머리의 1px 이내 수직 이동
- 어깨와 지팡이를 든 손의 최소한의 동반 이동
- 망토 끝의 1px 이내 지연

## 고정해야 하는 부위

- 양발 전체, 골반, 다리 길이와 Actor Origin
- 뿔의 가지 수, 전체 길이와 좌우 각도
- 귀의 길이와 머리 부착 위치
- 얼굴, 주둥이, 눈과 코
- 지팡이 길이, 갈라진 끝, 청록색 마법석의 크기와 위치
- 손의 그립 위치
- 망토 길이, 고정핀과 조끼 구조
- 꼬리 길이와 방향
- 팔레트, 외곽선, 캔버스와 화면 방향

## PerfectPixel UI 입력

```text
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Facing direction dropdown: Not set
Motion description: gentle chest breathing with slight torso rise; keep hooves, antlers, and staff steady
```

설명이 너무 정적으로 해석되어 눈만 깜빡일 때 사용할 첫 피드백:

```text
increase chest breathing slightly; keep hooves, antlers, staff, and head shape steady
```

어깨·머리·뿔이 과하게 들썩일 때 사용할 피드백:

```text
reduce head and shoulder movement; keep breathing in the chest and abdomen
```

한 번에 두 피드백을 함께 사용하지 않는다.

## 합격 기준

- 네 프레임의 뿔 실루엣이 동일하다.
- 발바닥 하단과 Pivot 기준이 모든 프레임에서 고정된다.
- 지팡이의 길이, 갈라진 끝과 청록색 마법석이 변하지 않는다.
- 망토와 꼬리가 떨리거나 독립적으로 휘날리지 않는다.
- 화면 방향과 3/4 시점이 Master와 같다.
- 6fps로 반복했을 때 빠른 바운스가 아닌 침착한 호흡으로 읽힌다.
- 공격 준비, 마법 시전과 승리 포즈로 보이지 않는다.
- 128×128 안에서 뿔과 지팡이에 안전 여백이 남는다.
