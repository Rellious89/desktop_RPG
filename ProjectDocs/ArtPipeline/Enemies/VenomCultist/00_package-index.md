# VenomCultist — Production Package Index

> Package Status: `Runtime Documented`
>
> Last Updated: 2026-08-10

## Identity

```text
Actor ID: VenomCultist
Display Name: 독단검 이교도 (테이블 표기: 이교도)
Actor Type: Enemy / Passive combat target
World ID: HUMAN-FANTASY-01
Runtime World: 2 / 판타지아
Species: 인간
Role: 독 단검을 든 하급 이교도
Aliases: None
```

## Production Profile

```text
Profile name: Legacy V1 Runtime / Low Companion v1
Final frame canvas: 128×128 RGBA
PPU: 50
Pivot: (0.5, 0.1)
Filter / Compression: Point / None
Facing: source screen-right, runtime Flip X enabled
Master source canvas: 512×512 RGBA
Actor scale: 1.0
```

## Current Status

| 항목 | 상태 | 기준 파일 |
|---|---|---|
| Monster Brief | Existing | [`01_monster-brief.md`](./01_monster-brief.md) |
| Master Design | Existing production master / v1 | `Assets/Art/Enemy/VenomCultist/master/VenomCultist-master-v1.png` |
| Master Measurements | Documented | [`03_master-measurements.md`](./03_master-measurements.md) |
| PerfectPixel Record | Existing production documented | [`02_perfectpixel-input.md`](./02_perfectpixel-input.md) |
| Base Idle | 4f / 6fps / connected | [`04_motion-idle.md`](./04_motion-idle.md) |
| Idle A / Prayer | 6f / 6fps / connected | [`04_motion-idle_a.md`](./04_motion-idle_a.md) |
| Idle B / Dagger Slash | 6f / 6fps / connected | [`04_motion-idle_b.md`](./04_motion-idle_b.md) |
| Hit Hold/Recovery | 2f / 6fps / connected | [`04_motion-hit.md`](./04_motion-hit.md) |
| Defeat | Fade-only / intentional | `Assets/Data/MotionProfiles/Monsters/VenomCultist/VenomCultist_MotionProfile.asset` |
| Portrait | Connected | `Assets/Art/Enemy/VenomCultist/Portrait/Portrait_VenomCultist.png` |
| Monster Table | ID 3 / enabled | `Assets/TableData/Game/Monster.csv` |
| Motion Profile | Connected / playable | `Assets/Data/MotionProfiles/Monsters/VenomCultist/VenomCultist_MotionProfile.asset` |

## Runtime Motion Inventory

| Motion ID | Frames | FPS | Runtime behavior |
|---|---:|---:|---|
| `idle` | 4 | 6 | Base Idle loop |
| `idle_a` | 6 | 6 | Random prayer event |
| `idle_b` | 6 | 6 | Random dagger-slash event |
| `hit[0]` | 1 pose | 6 | Hit Hold |
| `hit[1]` | 1 pose | 6 | Hit Recovery |
| `defeat` | 0 | 6 | 피격 자세 유지 후 Fade-out |

## Locked Production Decisions

- 깊은 보라색 후드, 얼굴 그림자, 가슴의 큰 해골 목걸이와 한 자루의 독 단검을 유지한다.
- 기도 중 단검이 사라졌다가 Idle 복귀 시 다시 나타나는 게임적 생략을 허용한다.
- Hit Hold에서 단검은 손에서 분리되며, Recovery에서 앞손 위치로 돌아갈 준비를 한다.
- 기본 런타임은 공격 모션을 사용하지 않는 Passive Enemy 구조다. `idle_b`의 단검 휘두르기는 공격 판정이
  없는 대기 이벤트다.
- Defeat 전용 프레임은 현재 필수가 아니며 Fade-only가 정상 동작이다.
- 기존 128×128 / PPU 50 / Pivot `(0.5, 0.1)` 런타임 리소스를 유지한다.

## Known History and Gaps

- 테이블 표시 이름은 `이교도`, 제작 표시 이름은 `독단검 이교도`다. 리소스 ID는 `VenomCultist`로 고정한다.
- PerfectPixel 입력의 Idle B는 8fps였으나 실제 MotionProfile은 6fps로 연결돼 있다. 현행 런타임 기준은
  6fps이며, 속도 변경은 별도 플레이 검수 후 결정한다.
- 새 패키지 규칙 기준 문서 이행은 완료됐지만, 기존 V1 리소스를 V2로 재생산한다는 승인은 아직 없다.

## Next Action

> 현재 런타임 유지. V2 재생산, Idle B 속도 변경 또는 Defeat 모션 추가 요청이 있을 때 이 패키지를 기준으로 작업한다.

