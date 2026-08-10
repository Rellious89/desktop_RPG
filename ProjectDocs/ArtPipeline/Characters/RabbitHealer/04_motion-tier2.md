# RabbitHealer — Attack B / Tier 2 Motion Brief

> 상태: `Motion Ready`
>
> 기준 Master: `Assets/Art/Character/RabbitHealer/master/RabbitHealer-master-v2.png`

## Purpose

기존 Attack A와 같은 원거리 기본 공격의 두 번째 시각 모션이다. 공격력 강화, 회복 스킬 또는 필살기가 아니다.
Attack A가 살짝 점프한 뒤 완드를 내리찍어 마법을 발사한다면, Attack B는 점프 없이 제자리에서 완드를 앞으로
내밀어 마법을 발사한다.

```text
Tier 1 Pool: Attack A
Tier 2 Pool: Attack A + Attack B
Tier 3 Pool (final target): Attack A + Attack B + Attack C
```

## Production Values

```text
Animation ID: attack_t2
Motion name: Attack B
Type: Alternate ranged basic attack
Frames: 3
Animation FPS: 18
Loop: No
Final canvas: 128×128 RGBA
Final path: Assets/Art/Character/RabbitHealer/attack_t2/RabbitHealer-attack_t2-NN.png
Pivot: (0.5, 0.1)
Cast frame candidate: 0
Hit frame candidate: 1
```

Cast/Hit 인덱스는 기존 Attack A와 같은 발사체 타이밍을 재사용하는 첫 연결값이다. 실제 프레임과 발사체를
Unity에서 겹쳐 본 뒤 미세 조정할 수 있다.

## Three-frame Poses

| Frame | Pose |
|---:|---|
| 00 | 양발을 접지한 채 완드를 몸 가까이에서 짧게 당기고 시선을 목표에 둔다. 점프하지 않는다. |
| 01 | 양발을 고정하고 완드를 화면 오른쪽 앞으로 내밀어 마법을 발사한다. 세 프레임 중 가장 명확한 포즈다. |
| 02 | 팔과 완드를 거두며 Base Idle 직전 자세로 돌아온다. 몸 전체가 튀거나 크게 젖혀지지 않는다. |

```text
00 short windup → 01 cast/strike → 02 recovery
```

## Motion Rules

- 세 프레임 모두 발, Pivot과 접지선을 고정한다.
- 점프, 공중 체공, 완드 내리찍기와 큰 전신 바운스를 사용하지 않는다.
- 작은 몸통 회전과 완드를 든 팔의 전진으로 동작 차이를 만든다.
- 귀는 머리 동작을 따라 최대한 작게 반응하며 길이와 간격이 바뀌지 않는다.
- 가방과 멜빵은 구조를 유지하고 1px 안팎의 작은 지연만 허용한다.
- 실제로 날아가는 마법은 독립 Projectile로 표현하며 캐릭터 프레임에 큰 발사체를 합치지 않는다.
- Attack A와 같은 기본 공격이므로 과장된 차징, 회복광, 보호막과 대형 마법진을 넣지 않는다.

## PerfectPixel Input

```text
Animation name: Attack B
Frames: 3
FPS: 18
Repeat: No
Facing direction dropdown: Not set
Motion description: cast one ranged magic shot while standing in place; extend the short wand forward without jumping, then return; keep both feet, ears, bag, outfit, and body scale consistent
```

## Acceptance Criteria

- 점프·내리찍기인 Attack A와 제자리 전방 시전인 Attack B가 작은 화면에서도 구분된다.
- 공격 강도가 아니라 포즈만 다른 기본 공격으로 읽힌다.
- Frame 01의 완드 방향과 마법 발사 시점이 명확하다.
- 발, Origin, 전체 크기와 화면 방향이 세 프레임에서 고정된다.
- 귀, 얼굴, 복식, 가방과 완드의 구조가 유지된다.
- Frame 02 뒤 Base Idle로 위치와 크기 점프 없이 돌아간다.

## Unity Pool Rule

Attack B 결과가 승인되면 별도의 `AttackMotionDefinition`으로 연결한다. Tier 2 풀에는 기존 Attack A와 새
Attack B를 모두 등록한다. Attack B만 넣으면 누적 풀 규칙을 위반한다.
