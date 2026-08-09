# StagGroveWarden — PerfectPixel Input Sheet

> 상태: Idle Attempt 01 입력 준비 완료
>
> 기준 Master: `Assets/Art/Character/StagGroveWarden/master/StagGroveWarden-master-v1.png`

## Character & Style

```text
Upload image: StagGroveWarden-master-v1.png
Character name: StagGroveWarden
Character description: A calm male anthropomorphic stag forest warden with simple antlers, reddish-brown fur, a short olive cape, and one straight wooden staff holding a small cyan stone. Keep his split hooves, cream muzzle, green tunic, leather vest, and all equipment identical in every frame.
Art style: Pixel Art
Frame cell size: 128 x 128 px
Facing direction dropdown: Not set
```

`Character description`에는 동작, Pivot, Unity 설정과 긴 금지 목록을 추가하지 않는다.

## 1. Base Idle

```text
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Facing direction dropdown: Not set
Motion description: gentle chest breathing with slight torso rise; keep hooves, antlers, and staff steady
```

### 동작 설명

- 제자리에서 느리고 작은 흉곽·복부 호흡을 한다.
- 발, 골반과 접지선은 고정한다.
- 뿔과 지팡이는 몸통을 따라 최소한만 움직이며 형태가 변하지 않는다.
- 03에서 다음 00으로 자연스럽게 돌아간다.

### 검수 기준

- 뿔 가지 수와 각도가 전 프레임에서 동일함
- 지팡이 길이, 갈라진 끝과 청록색 마법석이 동일함
- 양발의 접지선과 캐릭터 전체 크기가 동일함
- 짙은 녹색 튜닉, 갈색 조끼와 한쪽 망토 구조가 유지됨
- 몸통 호흡만 읽히고 공격·시전 포즈가 생기지 않음
- 128×128 캔버스에서 상하좌우가 잘리지 않음

## Attempt 01 회고표

PerfectPixel 생성 후 아래 표를 채운다.

```text
Date / Attempt ID:
Actor: StagGroveWarden
Base image version: Master v1

[Character & Style]
Character name: StagGroveWarden
Character description: A calm male anthropomorphic stag forest warden with simple antlers, reddish-brown fur, a short olive cape, and one straight wooden staff holding a small cyan stone. Keep his split hooves, cream muzzle, green tunic, leather vest, and all equipment identical in every frame.
Art style: Pixel Art
Frame cell size: 128 x 128 px

[Animation]
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Motion description: gentle chest breathing with slight torso rise; keep hooves, antlers, and staff steady
Facing direction dropdown selection: Not set

[PerfectPixel result]
Quality score:
UI warnings by frame:
Identity consistency: Pass / Fix / Reject
Motion readability: Pass / Fix / Reject
Antler consistency: Pass / Fix / Reject
Staff consistency: Pass / Fix / Reject
Foot/origin stability: Pass / Fix / Reject
Crop and safe margin: Pass / Fix / Reject
Usable frames:
Frames requiring fixes:
Rejected frames:

[Decision]
Keep current input / Retry
One field to change next:
Reason:
```

## Feedback 후보

호흡이 너무 약할 때:

```text
increase chest breathing slightly; keep hooves, antlers, staff, and head shape steady
```

머리와 뿔이 과하게 움직일 때:

```text
reduce head and shoulder movement; keep breathing in the chest and abdomen
```

뿔이나 지팡이 형태가 바뀌면 Feedback으로 구제하기 전에 해당 Attempt를 Reject하고 새 시도로 기록한다.
