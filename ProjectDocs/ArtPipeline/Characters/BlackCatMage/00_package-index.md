# BlackCatMage — Production Package Index

> Package Status: `Master Approved`
>
> Last Updated: 2026-08-10

## Identity

```text
Actor ID: BlackCatMage
Runtime ID: CatMage
Display Name: 검은 고양이 마법사 (작업 표시명)
Actor Type: Player
World ID: ANIMAL-LAND-01
Faction: 평화 진영
Aliases: CatMage
Species: 검은 고양이 수인
```

## Production Profile

```text
Profile name: Legacy V1 Runtime / recovered production Master v1
Final frame canvas: 128×128 RGBA
PPU: 50
Pivot: (0.5, 0.1)
Filter / Compression: Point / None
Facing: screen-right slight three-quarter
Approved Master: Assets/Art/Character/CatMage/master/CatMage-master-v1.png
```

## Current Status

| 항목 | 상태 | 기준 파일 |
|---|---|---|
| World placement | Existing | `ProjectDocs/WorldBuilding/ANIMAL-LAND-01-world-expansion-draft.md` |
| Character Brief | Existing production identity | [`01_character-brief.md`](./01_character-brief.md) |
| Master Design | Recovered production Master / v1 | `Assets/Art/Character/CatMage/master/CatMage-master-v1.png` |
| PerfectPixel Input | Master recovered | [`02_perfectpixel-input.md`](./02_perfectpixel-input.md) |
| Base Idle Motion | Legacy runtime documented | [`04_motion-idle.md`](./04_motion-idle.md) |
| Legacy Runtime Motions | idle 4f, idle_a 4f, idle_b 4f, cast 3f | `Assets/Art/Character/CatMage/` |
| Tier 2 | Missing | — |
| Unity Import | Connected / V1 | `Assets/Art/Character/CatMage/` |
| Motion Profile | Attack A connected / Tier 2 pool missing | `Assets/Data/MotionProfiles/Characters/CatMage/` |
| Character Definition | Connected | `Assets/Data/Characters/CatMage_CharacterDefinition.asset` |

## Approved Master

```text
Assets/Art/Character/CatMage/master/CatMage-master-v1.png
```

원본은 `/Users/rellious/Desktop/keybuddy/c_character/c_CatMage/CatMage_master.png`에서 복구했다. Low
PerfectPixel 출력과 현행 Idle의 알파 형태가 98.02% 일치해 현재 런타임의 생산 계보를 확인했다. 이전
Brief의 다른 후보 경로들은 여전히 복원 참고용이다.

## Available Motion Briefs

| Motion ID | Frames | FPS | Runtime | Brief |
|---|---:|---:|---|---|
| `idle` | 4 | 6 | Connected | Existing-resource brief |
| `idle_a` | 4 | 6 | Connected | Not yet authored |
| `idle_b` | 4 | 6 | Connected | Not yet authored |
| `tier1` / Attack A | 3 | 18 | Connected | 지팡이 전방 직선 발사 기록 완료 |
| `tier2` / Attack B | 3 | 18 | Missing | 지팡이 위쪽 대각선 시전 Brief ready |
| `tier3` / Attack C | — | — | Missing | 최종 출시 목표, 현재 범위 아님 |

## User-approved Decisions

- 기존 BlackCatMage/CatMage의 고양이 마법사 정체성과 현재 런타임 리소스는 유지한다.
- 재설계와 V2 교체가 가능하지만, 이번 패키지 이관에서 임의 교체하지 않는다.
- 데스크탑 `keybuddy` 작업 폴더의 `CatMage_master.png`를 기존 생산 Master로 복구한다.

## AI Proposals Not Yet Approved

- 작업 표시명 `검은 고양이 마법사`.
- 향후 V2 재설계 시 Low Companion 계열의 단순한 픽셀 밀도를 유지.
- Attack B를 다음 신규 제작 대상으로 사용.

## Known Conflicts and Gaps

- 패키지 ID `BlackCatMage`, 런타임 ID와 리소스 경로는 `CatMage`다.
- 기존 Brief가 가리키는 세부 후보 네 경로는 현재 작업 트리에 없지만 실제 생산 Master는 복구했다.
- Idle A/B는 독립 Motion Brief가 없다.
- Tier 2 리소스가 없다.

## Next Action

> `04_motion-tier2.md`의 입력으로 Attack B 3프레임을 생성하고 새 Attempt에 결과를 기록한다.
