# CopperAxeBarbarian — PerfectPixel Input & Attempt Index

> 상태: `Recovered Master v1 available`
>
> 런타임 ID: `Barbarian`

## Current Input

```text
Approved legacy Master: Assets/Art/Character/Barbarian/master/Barbarian-master-v1.png
Character name: Barbarian
Art style: Pixel Art
Target delivery frame: 128×128 RGBA
Facing: screen-right slight three-quarter
```

원본 파일은 데스크탑 `keybuddy/c_character/Barbarian/barbarian.png`에서 복구했으며 새 canonical Master와
체크섬이 동일하다.

## Locked Character Constraints

```text
A muscular copper-skinned fantasy barbarian holding one short one-handed axe in each hand. Keep the broad two-head SD body, copper skin, dark hair, simple leather-and-fur outfit, both complete axes, and the screen-right three-quarter view identical in every frame.
```

- 한손 도끼 2개의 날, 자루와 손 연결이 모두 보여야 한다.
- 어깨만 과장되어 머리와 하체 비율을 압도하지 않는다.
- 화면 방향, 피부색과 SD 체형을 프레임마다 바꾸지 않는다.
- 현대 무기, 총기, 대형 양손도끼와 추가 장비를 만들지 않는다.

## Attempt History

상세 수치와 입력 전문은 [`05_perfectpixel-attempt-log.md`](./05_perfectpixel-attempt-log.md)에 보존한다.

| Attempt | 입력 | 결과 | 판정 |
|---|---|---|---|
| 01 | 초기 Idle | 동작이 지나치게 정적 | Retry |
| 02 | 512 Idle / 4f | 4프레임 사용 가능, 어깨 동작 과다 | Fix candidate |
| 03 | 256 Idle | 크기는 적절하나 1×1 밀도와 프레임 스케일 드리프트 | Reject |
| 04 | Scale-lock feedback | 정체성과 양손 도끼 구조 붕괴 | Reject |
| 05 | C-hybrid 512 후보 | Master 미승인 | Pending |

## Next Runnable Input

다음 신규 모션은 [`04_motion-tier2.md`](./04_motion-tier2.md)의 Attack B다.

```text
Animation name: Attack B
Frames: 3
FPS: 18
Repeat: No
Facing direction dropdown: Not set
Motion description: swing the left-hand axe in one short horizontal cut across the body; keep the right-hand axe low and steady, both feet planted, and the body scale consistent
```

생성 후 아래 회고표를 채운다.

```text
Approved Master: Barbarian-master-v1.png
Animation name:
Frames:
FPS:
Repeat:
Motion description:
Facing direction dropdown:
Attempt ID:
```

한 번의 Attempt에서는 피드백 항목 하나만 바꾸고 결과를 `05_perfectpixel-attempt-log.md`에 추가한다.
