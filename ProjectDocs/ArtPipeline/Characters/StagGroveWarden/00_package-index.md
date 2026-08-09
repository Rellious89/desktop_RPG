# StagGroveWarden — Production Package Index

> Package Status: `Motion Ready`
>
> Last Updated: 2026-08-10

## Identity

```text
Actor ID: StagGroveWarden
Display Name: 수사슴 숲지기 (작업 표시명)
Actor Type: Player
World ID: ANIMAL-LAND-01
Faction: 평화 진영
Species: 수사슴 수인
Aliases: None
```

## Production Profile

```text
Profile name: KeyBuddy V2 Pilot
Final frame canvas: 128×128 RGBA
PPU: 50
Pivot candidate: (0.5, 0.234)
Ground-contact line: 30px from canvas bottom
Filter / Compression: Point / None
Pixel style reference: Test_IceMage V2
Palette target: approximately 32–48 opaque colors
Relative scale reference: IceMage body width + 14px antler height
```

이 프로필은 StagGroveWarden과 현재 V2 파일럿에 적용한다. V1 Actor 전체에 자동 적용하지 않는다.

## Current Status

| 항목 | 상태 | 기준 파일 |
|---|---|---|
| World concept | Approved | `ProjectDocs/WorldBuilding/ANIMAL-LAND-01-world-expansion-draft.md` |
| Character Brief | Approved | [`01_character-brief.md`](./01_character-brief.md) |
| Master Design | Approved / v1 | `Assets/Art/Character/StagGroveWarden/master/StagGroveWarden-master-v1.png` |
| Master Measurements | Approved | [`03_master-measurements.md`](./03_master-measurements.md) |
| PerfectPixel Input | Idle Attempt 01 Ready | [`02_perfectpixel-input.md`](./02_perfectpixel-input.md) |
| Base Idle Motion | Ready / 4f / 6fps | [`04_motion-idle.md`](./04_motion-idle.md) |
| PerfectPixel Output | Not generated | — |
| Unity Import | Not verified | — |
| Motion Profile | Not created | — |
| Character Table | Not registered | — |

## Approved Master

```text
Assets/Art/Character/StagGroveWarden/master/StagGroveWarden-master-v1.png
```

Prototype and generation history:

[`Prototypes/v2-master-01/README.md`](./Prototypes/v2-master-01/README.md)

## Available Motion Briefs

| Motion ID | Type | Frames | FPS | Repeat | Status |
|---|---|---:|---:|---|---|
| `idle` | Base Idle | 4 | 6 | Loop | PerfectPixel input ready |

Idle Variant, Tier 1, Tier 2와 기타 모션은 아직 만들지 않는다. Base Idle 출력과 V2 일관성을 먼저 검증한다.

## User-approved Decisions

- 애니멀랜드 첫 V2 파일럿으로 StagGroveWarden을 사용한다.
- Master Candidate 01의 체형, 뿔, 지팡이, 망토와 팔레트를 그대로 승인한다.
- 승인일은 2026-08-10이다.
- 승인 Master는 `StagGroveWarden-master-v1.png`다.
- V2 프레이밍 후보는 IceMage와 같은 128×128, PPU 50, Pivot `(0.5, 0.234)`를 사용한다.

## AI Proposals Not Yet Approved

- `수사슴 숲지기`는 작업 표시명이며 정식 개인 이름은 미정이다.
- 소속 숲, 국가, 왕실과 세력의 정식 명칭은 미정이다.
- 이동 방해·밀쳐내기 중심의 세부 스킬 구성은 시스템 구현 전 제안 상태다.
- 공격 모션의 프레임 수와 타격 방식은 Base Idle 검증 이후 결정한다.

## Known Conflicts and Gaps

- 기존 공통 문서는 V1 `512×512 / PPU 200` 규격을 기술하며 V2 공통 규칙으로 아직 갱신되지 않았다.
- Character Editor의 World Template도 V1 생산 기본값을 사용한다.
- 승인 Master는 Assets에 존재하지만 Unity가 생성하는 `.meta`와 실제 Import 결과는 아직 없다.
- PerfectPixel Idle Attempt 01 출력이 없으므로 뿔·지팡이 프레임 일관성은 아직 검증되지 않았다.
- CharacterDefinition, Motion Profile, Character.csv와 씬 연결은 이번 문서·리소스 생산 범위 밖이다.

## Next Action

> `02_perfectpixel-input.md`의 값으로 Base Idle Attempt 01을 생성하고, 결과와 UI 경고를 같은 문서의 회고표에
> 기록한다.

다음 작업 전에는 Master를 다시 생성하거나 공격 모션을 동시에 시작하지 않는다.
