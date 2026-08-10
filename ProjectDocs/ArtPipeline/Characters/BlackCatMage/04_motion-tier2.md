# BlackCatMage — Attack B / Tier 2 Motion Brief

> 상태: `Motion Ready`
>
> 기준 Master: `Assets/Art/Character/CatMage/master/CatMage-master-v1.png`

## Purpose

기존 Attack A와 같은 원거리 기본 공격의 두 번째 모션이다. Attack A가 지팡이를 앞으로 곧게 뻗는 직접
발사라면, Attack B는 지팡이를 몸 가까이에서 화면 위쪽 대각선으로 짧게 들어 올린 뒤 마법을 발사한다. 공격
수치가 강해지거나 별도 스킬이 되는 것은 아니다.

마법 캐릭터의 본체 모션 가짓수에는 한계가 있으므로 이후에는 같은 모션에 발사체·캐스팅 이펙트만 바꾸는
변형도 허용한다. 다만 Attack B 첫 제작본은 Attack A와 실루엣이 구분되어야 한다.

## Production Values

```text
Animation ID: cast_t2
Motion name: Attack B
Type: Alternate ranged basic attack
Frames: 3
Animation FPS: 18
Loop: No
Final canvas: 128×128 RGBA
Final path: Assets/Art/Character/CatMage/cast_t2/BlackCatMage-cast_t2-NN.png
Pivot: (0.5, 0.1)
Cast frame candidate: 0
Hit frame candidate: 1
```

## Three-frame Poses

| Frame | Pose |
|---:|---|
| 00 | 지팡이를 몸 가까이로 당기며 끝을 화면 위쪽 대각선으로 향하게 한다. 양발과 하체는 고정한다. |
| 01 | 지팡이 끝을 머리 옆 위쪽까지 짧게 들어 올려 마법을 발사한다. 팔을 목표 쪽으로 곧게 뻗지 않는다. |
| 02 | 지팡이를 세로 대기 위치로 내리며 로브와 꼬리가 작은 지연으로 복귀한다. |

```text
00 close gather → 01 raised diagonal cast → 02 settle
```

## Motion Rules

- Attack A의 전방 직선 찌르기 실루엣을 반복하지 않는다.
- 지팡이는 몸에서 멀리 뻗지 않고 머리 옆 위쪽 대각선에서 시전한다.
- 점프, 큰 회전, 긴 차징과 대형 마법진을 사용하지 않는다.
- 양발, Pivot, 신체 크기와 화면 방향을 고정한다.
- 모자, 귀, 꼬리, 로브와 붉은 마법석의 형태를 유지한다.
- 실제 마법 발사체는 독립 Projectile/VFX로 구성하며 본체 프레임에 긴 탄도를 그리지 않는다.
- 미래의 이펙트 교체형 변형은 같은 AttackMotion을 공유할 수 있지만, 풀에 새 모션으로 셀지는 별도로 결정한다.

## PerfectPixel Input

```text
Animation name: Attack B
Frames: 3
FPS: 18
Repeat: No
Facing direction dropdown: Not set
Motion description: lift the staff in one short diagonal casting gesture beside the head and fire without thrusting it forward; keep both feet, hat, ears, tail, robe, and body scale consistent
```

## Acceptance Criteria

- 전방 직선 발사 Attack A와 위쪽 대각선 시전 Attack B가 구분된다.
- 3프레임 안에서 준비, 발사와 복귀가 모두 읽힌다.
- Frame 01에서 붉은 마법석과 발사 지점이 분명하다.
- 지팡이가 추가되거나 길이·손 연결이 변하지 않는다.
- 발, Origin, 전체 크기와 화면 방향이 고정된다.
- Frame 02 뒤 Base Idle로 위치 점프 없이 돌아간다.

## Unity Pool Rule

결과 승인 후 Tier 2 풀에는 기존 Attack A와 새 Attack B를 모두 등록한다. 발사체와 Cast/Hit Effect는
AttackMotionDefinition별로 다르게 지정할 수 있다.

