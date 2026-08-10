# Player Attack Tier Pool Audit

> 감사일: 2026-08-10
>
> 대상: 현재 Active Player 6종
>
> 범위: 프레임 → AttackMotionDefinition → ComboTierAttackPool → CharacterMotionProfile

## 판정 규칙

```text
Tier 1 Pool = Attack A
Tier 2 Pool = Attack A + Attack B
Tier 3 Pool = Attack A + Attack B + Attack C
```

- Tier는 공격력 단계가 아니라 선택 가능한 기본 공격 모션의 누적 개수다.
- 프레임 폴더가 있어도 AttackMotionDefinition이나 Profile이 참조하는 풀이 없으면 게임 연결 완료가 아니다.
- 별도 Definition이 같은 프레임 배열을 참조하면 동일한 Attack A로 인정한다.
- 현재 필수 범위는 Tier 2까지며 Tier 3는 최종 출시 목표다.
- 이 감사에서는 Unity 에셋 연결을 자동 변경하지 않았다.

## 요약

| Actor | Attack A 프레임 | Tier 1 풀 | Attack B 프레임 | Tier 2 누적 풀 | 현재 판정 |
|---|---:|---|---:|---|---|
| CatKnight | 3 | A / 정상 | 3 | A+B / 정상 | 연결 완료 |
| ElfArcher | 3 | A / 정상 | 5 | A복제+B / 정상 | 연결 완료 |
| ElfGuardian | 3 | A / 정상 | 3 | A복제+B / 정상 | 연결 완료, 미참조 빈 풀 1개 |
| Barbarian | 3 | A / 정상 | 없음 | 없음 | Attack B 제작 대기 |
| CatMage | 3 | A / 정상 | 없음 | 없음 | Attack B 제작 대기 |
| RabbitHealer | 3 | A / 정상 | 없음 | 없음 | Attack B 제작 대기 |

현재 Active Player 6종 중 3종이 Tier 2 누적 풀까지 연결되어 있다. 나머지 3종은 Attack B Brief만 준비됐으며
프레임·Definition·Tier 2 풀은 아직 없다.

## CatKnight

```text
Tier 1 Pool: Assets/Data/CatKnight/CatKnight_Tier1AttackPool.asset
  - CatKnight_BasicAttack_A / 3 frames
Tier 2 Pool: Assets/Data/CatKnight/CatKnight_Tier2AttackPool.asset
  - CatKnight_BasicAttack_A / 3 frames
  - CatKnight_BoostAttack_A / 3 frames
```

Tier 2 풀은 기존 A와 두 번째 모션 B를 함께 포함한다. `BoostAttack`이라는 과거 이름은 강도 단계처럼 보이지만
현재 규칙상 Attack B 역할로 해석한다. 리소스와 Profile 연결은 정상이다.

## ElfArcher

```text
Tier 1 Pool: ElfArcher_Tier1AttackPool.asset
  - ElfArcher_attack / 3 frames
Tier 2 Pool: ElfArcher_Tier2AttackPool.asset
  - ElfArcher_Attack_t2_1 / 3 frames (Attack A와 동일 프레임)
  - ElfArcher_Attack_t2_2 / 5 frames (Attack B)
```

Tier 2에서 Attack A를 같은 Definition으로 재사용하지 않고 프레임이 같은 복제 Definition을 사용한다. 런타임
동작에는 문제가 없으며 프레임 서명 기준으로 A+B 누적 규칙을 충족한다. 새 작업에서는 가능하면 동일 A
Definition을 직접 재사용해 중복 설정을 줄인다.

## ElfGuardian

```text
Tier 1 Pool: ElfGuardian_Tier1AttackPool.asset
  - ElfGuardian_Attack / 3 frames
Profile-linked Tier 2 Pool: ElfGuardian_Tier2AttackPool 1.asset
  - ElfGuardian_Attack_T2_1 / 3 frames (Attack A와 동일 프레임)
  - ElfGuardian_Attack_T2_2 / 3 frames (Attack B)
Unreferenced: ElfGuardian_Tier2AttackPool.asset / motions: []
```

MotionProfile은 이름 뒤에 `1`이 붙은 채워진 풀을 GUID로 정확히 참조한다. 따라서 실제 게임 연결은 정상이다.
이름이 거의 같은 빈 풀은 미참조 잔여 에셋이며 이번 감사에서는 삭제하지 않았다. 향후 정리 시 Unity 참조를
다시 확인한 뒤 빈 풀만 제거할 수 있다.

## Barbarian

```text
Attack A: Barbarian_attack / 3 frames / Tier 1 pool connected
Attack B: 왼손 도끼 가로 베기 Brief ready
Tier 2 Pool: missing
```

Attack B 프레임 승인 후 새 AttackMotionDefinition을 만들고 Tier 2 풀에 기존 `Barbarian_attack`과 Attack B를
함께 등록한다.

## CatMage

```text
Attack A: CatMage_Attack / 3 frames / Tier 1 pool connected
Attack B: 지팡이 위쪽 대각선 시전 Brief ready
Tier 2 Pool: missing
```

Attack B 프레임 승인 후 새 Definition에 발사체·Cast/Hit Effect를 독립 설정하고, Tier 2 풀에 A+B를 함께
등록한다.

## RabbitHealer

```text
Attack A: RabbitHealer_Attack / 3 frames / Tier 1 pool connected
Attack B: 점프 없는 제자리 전방 시전 Brief ready
Tier 2 Pool: missing
```

Attack B 프레임 승인 후 기존 Projectile 규칙을 재사용한다. Tier 2 풀은 기존 점프·내리찍기 A와 새 제자리
시전 B를 함께 포함해야 한다.

## 현황 툴 변경

Actor Production Tracker가 다음을 별도로 표시하도록 갱신했다.

- 필수 공격 프레임 존재 여부
- MotionProfile의 Tier 1/2/3 풀 참조
- 풀 내부의 재생 가능한 AttackMotionDefinition 수
- 하위 Tier 모션 포함 여부
- 상위 Tier에서 실제 새 모션이 하나 추가됐는지
- 미참조 AttackPool 에셋
- `공격 Tier 풀 공백` 필터

게임 연결 진행도는 Player에 대해 `MotionProfile + CharacterDefinition + 필수 공격 풀` 3항목으로 계산한다.
따라서 Barbarian, CatMage와 RabbitHealer는 현재 67%이며 Attack B와 Tier 2 풀을 연결하면 100%가 된다.

## 다음 연결 작업 체크리스트

Attack B 이미지 승인 후 캐릭터마다 다음 순서로 처리한다.

1. 납품 프레임을 `attack_t2` 또는 패키지에 기록한 canonical 폴더로 넣는다.
2. Attack B용 AttackMotionDefinition을 만든다.
3. FPS, Hit Frame, Cast Frame, 이동, 발사체와 이펙트를 설정한다.
4. Tier 2 Pool에 Attack A와 Attack B를 모두 등록한다.
5. CharacterMotionProfile의 `tier2Pool`이 그 풀을 참조하는지 확인한다.
6. 현황 툴을 다시 열어 Attack B 프레임과 A+B 누적 연결이 모두 표시되는지 확인한다.

