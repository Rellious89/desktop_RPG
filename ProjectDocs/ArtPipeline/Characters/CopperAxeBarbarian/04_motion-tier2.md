# CopperAxeBarbarian — Attack B / Tier 2 Motion Brief

> 상태: `Motion Ready`
>
> 기준 Master: `Assets/Art/Character/Barbarian/master/Barbarian-master-v1.png`

## Purpose

기존 Attack A와 같은 기본 공격의 두 번째 모션이다. Attack A가 해부학적 오른손 도끼의 수직 내려베기라면,
Attack B는 해부학적 왼손 도끼를 몸 바깥에서 안쪽으로 가로로 휘두르는 짧은 횡베기다. 강공격이나 스킬이
아니다.

## Production Values

```text
Animation ID: attack_t2
Motion name: Attack B
Type: Alternate melee basic attack
Frames: 3
Animation FPS: 18
Loop: No
Final canvas: 128×128 RGBA
Final path: Assets/Art/Character/Barbarian/attack_t2/Barbarian-attack_t2-NN.png
Pivot: (0.5, 0.1)
Hit frame candidate: 1
```

## Three-frame Poses

| Frame | Pose |
|---:|---|
| 00 | 해부학적 왼손 도끼를 몸 바깥쪽·뒤쪽으로 짧게 당긴다. 오른손 도끼는 몸 앞의 낮은 방어 위치를 유지한다. |
| 01 | 왼손 도끼를 허리~가슴 높이에서 화면 오른쪽 안쪽으로 가로 베어 가장 넓은 실루엣과 타격점을 만든다. |
| 02 | 왼팔이 몸 앞을 지난 짧은 후속 자세. 양발과 몸 중심을 유지하며 Idle로 돌아온다. |

```text
00 left-axe load → 01 horizontal strike → 02 compact follow-through
```

## Motion Rules

- 공격 주체는 해부학적 왼손 도끼다.
- 오른손 도끼는 수직 공격을 반복하지 않고 낮은 보조 위치를 유지한다.
- 도끼 두 개를 동시에 휘두르는 회전 공격으로 확대하지 않는다.
- 점프, 360도 회전과 큰 돌진을 사용하지 않는다.
- 양발, Pivot과 접지선은 고정하고 허리·어깨 회전으로 횡베기를 표현한다.
- 도끼 날, 자루와 손의 연결을 세 프레임 모두 유지한다.
- Attack Movement가 필요하면 기존 Attack A보다 크지 않은 별도 런타임 값으로 조절한다.

## PerfectPixel Input

```text
Animation name: Attack B
Frames: 3
FPS: 18
Repeat: No
Facing direction dropdown: Not set
Motion description: swing the left-hand axe in one short horizontal cut across the body; keep the right-hand axe low and steady, both feet planted, and the body scale consistent
```

## Acceptance Criteria

- 오른손 수직 내려베기 Attack A와 왼손 가로 베기 Attack B가 즉시 구분된다.
- 공격 B가 강공격이나 회전 스킬처럼 과장되지 않는다.
- Frame 01에서 왼손 도끼 날과 타격 방향이 선명하다.
- 두 도끼의 개수, 형태와 손 연결이 바뀌지 않는다.
- 발, Origin, 전체 크기와 화면 방향이 고정된다.
- Frame 02 뒤 Base Idle로 위치 점프 없이 돌아간다.

## Unity Pool Rule

결과 승인 후 Tier 2 풀에는 기존 Attack A와 새 Attack B를 모두 등록한다.

