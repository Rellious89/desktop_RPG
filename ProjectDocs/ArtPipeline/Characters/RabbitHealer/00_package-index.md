# RabbitHealer — Production Package Index

> Package Status: `Master Approved`
>
> Last Updated: 2026-08-10

## Identity

```text
Actor ID: RabbitHealer
Display Name: 토끼 힐러 (작업 표시명)
Actor Type: Player
World ID: ANIMAL-LAND-01
Faction: 평화 진영
Species: 흰 토끼 수인
Role: 부족 힐러
Aliases: None
```

## Production Profile

```text
Profile name: Legacy V1 Runtime / Master v2 Approved
Final frame canvas: 128×128 RGBA
PPU: 50
Pivot: (0.5, 0.1)
Filter / Compression: Point / None
Facing: screen-right slight three-quarter
Master source canvas: 1254×1254 RGBA
```

## Current Status

| 항목 | 상태 | 기준 파일 |
|---|---|---|
| World placement | Existing | `ProjectDocs/WorldBuilding/ANIMAL-LAND-01-world-expansion-draft.md` |
| Character Brief | Migrated from existing sheet | [`01_character-brief.md`](./01_character-brief.md) |
| Master Design | Approved / v2 | `Assets/Art/Character/RabbitHealer/master/RabbitHealer-master-v2.png` |
| Master Measurements | Approved | [`03_master-measurements.md`](./03_master-measurements.md) |
| PerfectPixel Record | Legacy production documented | [`02_perfectpixel-input.md`](./02_perfectpixel-input.md) |
| Base Idle | 4f / 6fps / connected | [`04_motion-idle.md`](./04_motion-idle.md) |
| Tier 1 | 3f / 18fps / connected | [`04_motion-tier1.md`](./04_motion-tier1.md) |
| Tier 2 / Attack B | Brief ready / no frames | [`04_motion-tier2.md`](./04_motion-tier2.md) |
| Unity Import | Connected / V1 delivery profile | `Assets/Art/Character/RabbitHealer/` |
| Motion Profile | Attack A connected / Tier 2 pool missing | `Assets/Data/MotionProfiles/Characters/RabbitHealer/` |
| Character Definition | Connected | `Assets/Data/Characters/RabbitHealer_CharacterDefinition.asset` |

## Approved Master

```text
Assets/Art/Character/RabbitHealer/master/RabbitHealer-master-v2.png
```

v2는 기존 Character Sheet의 하늘색 반팔티와 노란 멜빵 호박바지에 정확히 맞으며 2026-08-10 사용자
승인을 받았다. v1은 비교 기록으로 유지한다.

## Available Motion Briefs

| Motion ID | Frames | FPS | Runtime | Brief Status |
|---|---:|---:|---|---|
| `idle` | 4 | 6 | Connected | Existing motion documented |
| `idle_a` | 5 | 6 | Connected | Runtime only |
| `idle_b` | 6 | 6 | Connected | Runtime only |
| `tier1` / Attack A | 3 | 18 | Connected | 점프 후 완드를 내리찍는 원거리 기본 공격 |
| `tier2` / Attack B | 3 | 18 | Missing | 제자리에서 완드를 내밀어 발사하는 기본 공격 Brief ready |
| `tier3` / Attack C | — | — | Missing | 최종 출시 목표, 현재 범위 아님 |

## User-authored / Existing Locked Decisions

- 어린 여성 흰 토끼 수인이며 평화 진영의 부족 힐러다.
- 하늘색 반팔티, 노란색 멜빵 호박바지, 갈색 소형 크로스백과 분홍색 머리핀을 사용한다.
- 화면상 오른손에 짧고 가벼운 나무 완드 하나를 든다.
- 양쪽 긴 귀의 분홍색 안쪽이 보이며, 귀 높이는 신체 스케일 산정에서 제외한다.
- 기존 런타임 리소스와 연결은 유지한다.
- `RabbitHealer-master-v2.png`를 정식 Master로 사용한다. 승인일은 2026-08-10이다.
- 공격 Tier는 강도 단계가 아니라 기본 공격 모션 풀의 누적 개수다.
- Tier 1은 Attack A, Tier 2는 A+B, Tier 3는 A+B+C로 구성한다.
- Attack A는 살짝 점프한 뒤 완드를 내리찍어 마법을 발사한다.
- Attack B는 점프 없이 제자리에서 마법을 발사하며 3프레임 안에 끝낸다.

## AI Proposals Not Yet Approved

- 작업 표시명 표기를 `토끼 힐러`로 통일.

## Known Conflicts and Gaps

- 새 패키지 규칙 도입 전에 v1/v2가 모두 `Assets/.../master`에 저장되었다. 현재 정식본은 v2이며 v1은
  비교용 구버전이다.
- Character Sheet의 512×512 / PPU 200은 생산 소스 규격이며, 실제 게임 프레임은 128×128 / PPU 50이다.
- Idle A/B는 실제 프레임은 있으나 독립 Motion Brief가 없다.
- Tier 2 프레임과 런타임 연결이 없다.

## Next Action

> `04_motion-tier2.md`의 입력으로 Attack B 3프레임을 생성하고 새 Attempt에 결과를 기록한다.
