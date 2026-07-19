# CopperAxeBarbarian — Base Idle Motion Brief

> 상태: PerfectPixel 입력용 1차안
> CatKnight 기본 Idle과 동일한 프레임 수·재생 속도를 사용한다.

## 기본 설정

- Animation ID: `idle`
- Type: `Idle`
- 게임플레이 목적: 다른 동작이 없을 때 캐릭터가 살아 있음을 보여주는 기본 대기 루프
- 프레임 수: 4
- `animationFps`: 6
- 총 루프 시간: 약 0.667초
- Loop: 계속 반복
- 최종 경로: `Assets/Art/Character/CopperAxeBarbarian/idle/CopperAxeBarbarian-idle-NN.png`
- Actor Origin: 전방 디딤발 지면 접촉점. Master Design 승인 후 정확한 픽셀 좌표 기입

## 모션 원칙

- 동작은 제자리 호흡뿐이며 공격 준비 동작처럼 보이면 안 된다.
- 골반, 양발과 무게중심은 고정한다.
- 흉곽과 복부의 호흡 변화는 육안으로 읽히되 어깨는 긴장을 풀고 최소한만 따라간다.
- 머리끝의 수직 이동은 출력 신체 높이의 약 1% 이내를 목표로 한다. 별도의 끄덕임은 넣지 않는다.
- 복부와 가슴의 명암 덩어리는 호흡에 맞춰 아주 미세하게 변형할 수 있지만 근육 크기가 바뀌면 안 된다.
- 두 손은 도끼 손잡이를 계속 잡는다. 손가락 수, 그립 위치와 도끼 치수는 바뀌지 않는다.
- 두 도끼는 손목을 따라 최소한만 움직이며 독립적으로 흔들리지 않는다.
- 어깨보호대와 가슴 사선 가죽끈은 몸통에 고정된 하나의 장비처럼 함께 움직인다.
- 검은 브레이드는 상체 이동을 따라가되 독립적인 휘날림은 넣지 않는다.
- 문신의 선과 덩어리 형태를 프레임마다 다시 그리지 않는다.

## 프레임 설계

| 프레임 | 호흡 단계 | 자세 변화 |
|---:|---|---|
| 00 | 중립/날숨 완료 | 승인된 Master Design의 기준 자세. 발·골반·도끼 위치 기준 프레임 |
| 01 | 들숨 | 흉곽과 복부가 조금 팽창하고 몸통이 미세하게 상승. 어깨는 긴장을 푼 채 최소한만 따라감 |
| 02 | 들숨 정점 | 가슴과 복부의 호흡이 가장 잘 읽히는 상태. 어깨가 튀어 오르지 않으며 발과 골반은 00과 동일 |
| 03 | 날숨/복귀 | 01과 00 사이의 복귀 자세. 다음 00으로 자연스럽게 연결 |

`00 → 01 → 02 → 03 → 00`이 끊김 없이 이어져야 한다. 01과 03은 비슷한 높이를 사용할 수 있지만,
03은 가슴과 어깨가 내려가는 방향이 읽히도록 명암이나 브레이드 끝 위치를 1픽셀 이내에서 다르게 한다.

## 움직여도 되는 부위

- 흉곽, 어깨와 머리의 미세한 수직 이동
- 호흡에 따른 가슴/복부 명암 덩어리의 최소 변화
- 몸통과 함께 움직이는 어깨보호대, 가죽끈, 브레이드와 손목

## 고정해야 하는 부위

- 전방 디딤발 접촉점과 양발 전체
- 골반 위치와 다리 길이
- 두 도끼의 크기, 날 모양, 손잡이 길이와 손의 그립 위치
- 얼굴, 수염, 문신, 장비 구조와 팔레트
- 캔버스와 Actor Origin

## PerfectPixel UI 입력

```text
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Motion description: deep breathing through chest and abdomen; minimal shoulder movement; keep feet and both axes steady
Facing direction: Not set
```

이번 C형 2등신 재시험에서는 아래 문장으로 교체한다.

```text
Motion description: steady chest-and-abdomen breathing with slight torso rise; keep shoulders relaxed, feet planted, and both axes steady
```

위 한 줄보다 자세한 프레임 설계와 고정 부위는 PerfectPixel 입력문이 아니라 출력 검수 기준으로 사용한다.

### 변경 이력

- Attempt 01: `subtle stationary breathing; keep feet and both axes steady`
  - 결과: 상체 호흡이 거의 보이지 않고 눈 깜빡임 위주로 생성됨.
- Attempt 02: `deep breathing; chest and shoulders visibly rise and fall; keep feet and both axes steady`
  - 변경 이유: `subtle`을 제거하고 실제로 움직여야 하는 가슴과 어깨를 명시함.
  - 다른 값(Frames/FPS/Repeat/Facing)은 Attempt 01과 동일하게 유지함.
- Attempt 02 결과: 호흡은 읽히지만 어깨가 과도하게 들썩임. `shoulders visibly rise and fall`이 너무
  직접적으로 반영된 것으로 판단.
- 다음 신규 생성은 디자인 교체로 인해 기존 Attempt의 연속 피드백이 아니라 새 기준 이미지의 첫 Attempt로 기록한다.
- C형 2등신 신규 입력: `steady chest-and-abdomen breathing with slight torso rise; keep shoulders relaxed, feet planted, and both axes steady`
- Attempt 02 결과에서 피드백 재생성 시 추가 문장:

```text
reduce shoulder lift; use gentle chest and abdomen expansion; keep head and both axes steady
```

피드백 재생성은 특정 프레임 수정이 아니라 전체 4프레임을 다시 생성하는 새 Attempt로 기록한다.

새 결과가 다시 눈 깜빡임 위주로 너무 정적일 때만 다음 피드백을 사용한다.

```text
increase chest and abdomen movement slightly; keep shoulders relaxed and both axes steady
```

어깨 들썩임이 다시 과할 때는 아래 피드백을 사용한다.

```text
reduce shoulder lift; shift breathing motion to chest and abdomen; keep head and both axes steady
```

## 합격 기준

- 00과 다음 루프의 00 사이에 위치 점프가 없다.
- 발·골반이 완전히 고정되고 상체만 미세하게 들썩인다.
- 두 도끼와 문신이 프레임마다 동일한 디자인을 유지한다.
- 6fps에서 빠른 바운스가 아니라 무거운 호흡으로 읽힌다.
- 루프를 10회 반복해도 가슴 가죽끈과 브레이드가 떨려 보이지 않는다.
