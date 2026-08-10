# Specter — Production Package Index

> Package Status: `Runtime Documented`
>
> Last Updated: 2026-08-10

## Identity

```text
Actor ID: Specter
Display Name: 스펙터
Actor Type: Enemy / Passive combat target
World ID: UNDEAD-WORLD-01
Runtime World: 3 / 망자의 도시
Species: 하급 망령
Role: 작은 부유형 근접 방해 몬스터
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
Actor scale: 0.8
```

## Current Status

| 항목 | 상태 | 기준 파일 |
|---|---|---|
| Monster Brief | Existing | [`01_monster-brief.md`](./01_monster-brief.md) |
| Master Design | Existing production master / v2 | `Assets/Art/Enemy/Specter/master/Specter-master-v2.png` |
| Master Measurements | Documented | [`03_master-measurements.md`](./03_master-measurements.md) |
| PerfectPixel Record | Existing production documented | [`02_perfectpixel-input.md`](./02_perfectpixel-input.md) |
| Base Idle | 4f / 6fps / connected | [`04_motion-idle.md`](./04_motion-idle.md) |
| Idle A | 4f / 6fps / connected | [`04_motion-idle_a.md`](./04_motion-idle_a.md) |
| Hit Hold/Recovery | 2f / 6fps / connected | [`04_motion-hit.md`](./04_motion-hit.md) |
| Defeat | Fade-only / intentional | `Assets/Data/MotionProfiles/Monsters/Specter/Specter_MotionProfile.asset` |
| Portrait | Connected | `Assets/Art/Enemy/Specter/Portrait/Portrait_Specter.png` |
| Monster Table | ID 2 / enabled | `Assets/TableData/Game/Monster.csv` |
| Motion Profile | Connected / playable | `Assets/Data/MotionProfiles/Monsters/Specter/Specter_MotionProfile.asset` |

## Runtime Motion Inventory

| Motion ID | Frames | FPS | Runtime behavior |
|---|---:|---:|---|
| `idle` | 4 | 6 | Base Idle loop |
| `idle_a` | 4 | 6 | Random Idle Event |
| `hit[0]` | 1 pose | 6 | Hit Hold |
| `hit[1]` | 1 pose | 6 | Hit Recovery |
| `defeat` | 0 | 6 | 피격 자세 유지 후 Fade-out |

## Locked Production Decisions

- 흰 장례 천, 눈구멍 2개, 작은 입, 늘어진 양손과 세 갈래 밑단을 유지한다.
- Actor Origin은 바닥 투영점이며 부유 간격은 스프라이트 내부에서만 변화한다.
- 기본 런타임은 공격 모션을 사용하지 않는 Passive Enemy 구조다.
- Defeat 전용 프레임은 현재 필수가 아니며 Fade-only가 정상 동작이다.
- 기존 128×128 / PPU 50 / Pivot `(0.5, 0.1)` 런타임 리소스를 유지한다.

## Known History and Gaps

- `02_perfectpixel-input.md`에는 Idle A를 알파 연출로 만들 계획이 기록돼 있지만, 실제 런타임은
  `idle_a` 폴더의 별도 4프레임을 참조한다. 현행 기준은 실제 MotionProfile 연결이다.
- v1 Master와 source/chromakey 파생본은 비교 기록이며 현재 기준 Master는 v2 투명본이다.
- 새 패키지 규칙 기준 문서 이행은 완료됐지만, 기존 V1 리소스를 V2로 재생산한다는 승인은 아직 없다.

## Next Action

> 현재 런타임 유지. V2 재생산 또는 Defeat 모션 추가 요청이 있을 때 이 패키지를 기준으로 새 작업을 연다.

