# CopperAxeBarbarian — Production Package Index

> Package Status: `Master Approved`
>
> Last Updated: 2026-08-10

## Identity

```text
Actor ID: CopperAxeBarbarian
Runtime ID: Barbarian
Display Name: 구리도끼 야만전사 (작업 표시명)
Actor Type: Player
World ID: FANTASIA
Aliases: Barbarian
```

## Production Profile

```text
Profile name: Legacy V1 Runtime / recovered production Master v1
Final frame canvas: 128×128 RGBA
PPU: 50
Pivot: (0.5, 0.1)
Filter / Compression: Point / None
Facing: screen-right slight three-quarter
Approved Master: Assets/Art/Character/Barbarian/master/Barbarian-master-v1.png
```

현재 게임 리소스의 납품 규격을 기록한 값이다. `References`와 `Prototypes`의 고해상도 이미지는 정식 Master가
아니며, V2 생산 프로필로 자동 승격하지 않는다.

## Current Status

| 항목 | 상태 | 기준 파일 |
|---|---|---|
| Character Brief | Existing production identity | [`01_character-brief.md`](./01_character-brief.md) |
| Master Design | Recovered production Master / v1 | `Assets/Art/Character/Barbarian/master/Barbarian-master-v1.png` |
| Master Measurements | Recovered / measured | [`03_master-measurements.md`](./03_master-measurements.md) |
| PerfectPixel History | Attempts 01–05 recorded | [`02_perfectpixel-input.md`](./02_perfectpixel-input.md) |
| Motion Briefs | Base Idle, Idle A | `04_motion-idle.md`, `04_motion-idle_a.md` |
| Legacy Runtime Motions | idle 4f, idle_a 4f, idle_b 4f, attack 3f | `Assets/Art/Character/Barbarian/` |
| Tier 2 | Missing | — |
| Unity Import | Connected / V1 | `Assets/Art/Character/Barbarian/` |
| Motion Profile | Attack A connected / Tier 2 pool missing | `Assets/Data/MotionProfiles/Characters/Barbarian/` |
| Character Definition | Connected | `Assets/Data/Characters/Definitions/` |

## Approved Master

```text
Assets/Art/Character/Barbarian/master/Barbarian-master-v1.png
```

원본은 `/Users/rellious/Desktop/keybuddy/c_character/Barbarian/barbarian.png`에서 복구했다. 이 폴더에 Master를
넣고 PerfectPixel 결과를 수정해 현재 게임 리소스에 적용했다는 사용자 확인과, SD 출력·현행 Idle의 알파
형태 98.11% 일치를 생산 계보 근거로 사용한다.

## Available Motion Briefs

| Motion ID | Frames | FPS | Runtime | Brief |
|---|---:|---:|---|---|
| `idle` | 4 | 6 | Connected | Existing |
| `idle_a` | 4 | 6 | Connected | Existing |
| `idle_b` | 4 | 6 | Connected | Not yet authored |
| `tier1` / Attack A | 3 | 18 | Connected | 오른손 도끼 수직 내려베기 기록 완료 |
| `tier2` / Attack B | 3 | 18 | Missing | 왼손 도끼 가로 베기 Brief ready |
| `tier3` / Attack C | — | — | Missing | 최종 출시 목표, 현재 범위 아님 |

## User-approved Decisions

- 이 문서 이관 과정에서는 기존 런타임 리소스를 유지한다.
- 기존 후보, 실패 결과와 실험 기록을 삭제하거나 정식 Master로 오인하지 않는다.
- 데스크탑 `keybuddy` 작업 폴더에서 확인한 `barbarian.png`를 기존 생산 Master로 복구한다.

## AI Proposals Not Yet Approved

- 작업 표시명 `구리도끼 야만전사`.
- 향후 V2 재설계 시 `References/CopperAxeBarbarian-master-input-v3-c-hybrid-512.png`를 새 후보로 재검토.
- Attack B를 다음 신규 제작 대상으로 사용.

## Known Conflicts and Gaps

- 패키지 ID `CopperAxeBarbarian`과 런타임 ID `Barbarian`이 다르므로 출력 경로에는 `Barbarian`을 사용한다.
- Attempt 03은 게임상 크기는 적절했지만 1×1 픽셀 밀도와 스케일 드리프트로 Reject되었다.
- Attempt 04는 캐릭터 정체성과 양손 도끼 형태가 무너져 Reject되었다.
- Idle B는 실제 리소스만 있고 독립 Motion Brief가 없다.
- Tier 2 리소스가 없다.

## Next Action

> `04_motion-tier2.md`의 입력으로 Attack B 3프레임을 생성하고 새 Attempt에 결과를 기록한다.
