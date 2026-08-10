# RabbitHealer — PerfectPixel Input & Legacy Production Record

> 상태: `Attack B input ready / Master v2 Approved`

## Character & Style

```text
Upload image: RabbitHealer-master-v2.png
Character name: RabbitHealer
Character description: A young white anthropomorphic rabbit healer wearing a light-blue short-sleeve shirt, yellow suspender pumpkin pants, a small brown crossbody bag, and one pink hair clip. Keep both long pink-inner ears, bare rabbit feet, and one short wooden wand identical in every frame.
Art style: Pixel Art
Target delivery frame: 128×128 RGBA
Facing direction dropdown: Not set
```

v2 Master는 2026-08-10 승인되었다. Attack B는 새 Attempt ID로 생성하고 기존 Attack A 런타임 결과를
덮어쓰지 않는다.

## Existing Output Inventory

| Motion | Frames | FPS | 경로 | 상태 |
|---|---:|---:|---|---|
| Base Idle | 4 | 6 | `Assets/Art/Character/RabbitHealer/idle/` | Connected |
| Idle A | 5 | 6 | `Assets/Art/Character/RabbitHealer/idle_a/` | Connected |
| Idle B | 6 | 6 | `Assets/Art/Character/RabbitHealer/idle_b/` | Connected |
| Tier 1 / attack | 3 | 18 | `Assets/Art/Character/RabbitHealer/attack/` | Connected |
| Tier 2 | — | — | — | Missing |

## Next Attempt Template — Attack B / Tier 2

```text
Animation name: Attack B
Frames: 3
FPS: 18
Repeat: No
Facing direction dropdown: Not set
Motion description: cast one ranged magic shot while standing in place; extend the short wand forward without jumping, then return; keep both feet, ears, bag, outfit, and body scale consistent
```

상세 키포즈와 고정 요소는 [`04_motion-tier2.md`](./04_motion-tier2.md)를 따른다. 마법 효과는 캐릭터 프레임에
과도하게 합치지 않고 별도 VFX 규칙을 우선한다.

## Attempt Record

```text
Date / Attempt ID:
Approved Master version:
Motion:
Frames / FPS / Repeat:
Facing dropdown:
Quality score:
Identity consistency: Pass / Fix / Reject
Ear consistency: Pass / Fix / Reject
Wand and bag consistency: Pass / Fix / Reject
Foot/origin stability: Pass / Fix / Reject
Motion readability: Pass / Fix / Reject
Usable frames:
Frames requiring fixes:
Decision:
One field to change next:
```
