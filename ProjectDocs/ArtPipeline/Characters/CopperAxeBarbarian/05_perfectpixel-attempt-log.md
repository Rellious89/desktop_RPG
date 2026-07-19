# CopperAxeBarbarian — PerfectPixel Attempt Log

출력 이미지를 받으면 시도별로 아래 블록을 복제해 결과를 기록한다. 첫 시도에서는 Motion description을
임의로 늘리지 않는다.

## Attempt 01 — Base Idle

```text
Date:
Base image version:

[Character & Style]
Character name: CopperAxeBarbarian
Character description: Muscular copper-skinned male barbarian with braided black hair, a short beard, a bare tattooed torso, a small iron guard on his right shoulder secured by a diagonal leather harness, leather bracers, dark fur trousers, and two matching one-handed axes. Preserve his front-biased three-quarter view, screen-right gaze, body proportions, tattoos, colors, gear, and both axes.
Art style: Pixel Art
Frame cell size:

[Animation]
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Motion description: subtle stationary breathing; keep feet and both axes steady
Facing direction: Not set

[PerfectPixel result]
Quality score:
UI warnings by frame:
Identity consistency: Pass / Fix / Reject
Breathing readability: Fix — 상체 움직임이 거의 없고 눈 깜빡임 위주
Both axes preserved: Pass / Fix / Reject
Foot/origin stability: Pass / Fix / Reject
Crop and safe margin: Pass / Fix / Reject
Usable frames:
Frames requiring fixes:
Rejected frames:

[Decision]
Keep current input / Retry: Retry
One field to change next: Motion description only
Reason: `subtle`이 지나치게 정적인 결과로 해석됨
```

## Attempt 02 — Base Idle

```text
Date:
Base image version:

[Character & Style]
Character name: CopperAxeBarbarian
Character description: Muscular copper-skinned male barbarian with braided black hair, a short beard, a bare tattooed torso, a small iron guard on his right shoulder secured by a diagonal leather harness, leather bracers, dark fur trousers, and two matching one-handed axes. Preserve his front-biased three-quarter view, screen-right gaze, body proportions, tattoos, colors, gear, and both axes.
Art style: Pixel Art
Frame cell size: Attempt 01과 동일

[Animation]
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Motion description: deep breathing; chest and shoulders visibly rise and fall; keep feet and both axes steady
Facing direction: Not set

[PerfectPixel result]
Quality score: 68
UI warnings by frame: 제공된 결과 화면에서 별도 경고 확인되지 않음
Identity consistency: Pass
Breathing readability: Fix — 호흡은 읽히지만 어깨 들썩임이 과함
Both axes preserved: Pass
Foot/origin stability: Pass — 모든 프레임 bbox bottom y=470, bbox top y=64
Crop and safe margin: Fix — 잘리지는 않았지만 공격 확장 여백이 부족함
Usable frames: 00, 01, 02, 03
Frames requiring fixes: 전체 세트의 어깨 움직임 완화 필요
Rejected frames: 없음

[Decision]
Keep current input / Retry: Regenerate with feedback
One field to change next: Feedback field only
Reason: 패딩은 보존되지 않았고 호흡은 개선됐으나 어깨 움직임이 과함
```

### Attempt 02 Base image

```text
ProjectDocs/ArtPipeline/Characters/CopperAxeBarbarian/References/CopperAxeBarbarian-master-input-v2-padded.png
Canvas: 512x512
Bounding box: 282x245
Occupancy: width 55.1%, height 47.9%
```

Attempt 02에서는 기준 이미지의 패딩과 Idle Motion description이 함께 변경된다. 따라서 호흡 표현과
점유율 보존을 한 번에 완전히 분리해 판정할 수는 없다. 결과가 개선되면 다음 생성에서 같은 패딩 이미지를
고정한 채 Motion description만 비교하여 두 변수의 영향을 재확인한다.

### Attempt 02 Output measurements

| Frame | Alpha bbox | Width occupancy | Height occupancy |
|---:|---|---:|---:|
| 00 | x54 y64, 422×406 | 82.4% | 79.3% |
| 01 | x54 y64, 418×406 | 81.6% | 79.3% |
| 02 | x54 y64, 422×406 | 82.4% | 79.3% |
| 03 | x54 y64, 422×406 | 82.4% | 79.3% |

- 입력 bbox: 282×245
- 출력 bbox 높이: 406px로 정규화됨 (`406 ÷ 245 ≈ 1.66`)
- 네 프레임의 높이와 상·하단 좌표가 동일하므로 Base Idle 내부의 프레임별 전체 축척은 안정적이다.
- 보존된 결과 파일:
  `ProjectDocs/ArtPipeline/Characters/CopperAxeBarbarian/Results/attempt-02-512-idle/`

## Attempt 03 — 256 Base Idle

```text
Date:
Base image version: CopperAxeBarbarian-master-input-v2-padded.png
Frame cell size: 256x256
Motion description:
Facing direction:

[PerfectPixel result]
Pixel density: Reject for final quality — 1x1 픽셀 단위로 내려가 CatKnight의 약 3x3 밀도보다 거침
Frame 00 alpha bbox: x24 y28, 214x208 (width 83.6%, height 81.3%)
Frame 01 alpha bbox: x36 y20, 202x216 (width 78.9%, height 84.4%)
Bottom alignment: Pass — 두 프레임 모두 bbox bottom y=236
Frame-to-frame scale: Reject — Frame 01 height가 Frame 00보다 약 3.85% 큼
Content-fit behavior: Confirmed — 수평 동작 범위가 좁은 Frame 01이 더 크게 정규화됨

[Unity size check]
Import: 별도 크기 보정 없이 PPU 200으로 비교
CatKnight reference height: about 213px
Barbarian frame 00 height: 208px
Barbarian frame 01 height: 216px
Perceived world size: Pass — CatKnight와 동일한 수준으로 확인

[Decision]
Keep / Retry: 크기 프로토타입으로 유지; 직접 최종 리소스로는 보류
Reason: 게임 표시 크기는 적합하지만 1x1 픽셀 밀도와 프레임별 축척 변화가 남음
```

보존된 결과 파일:

```text
ProjectDocs/ArtPipeline/Characters/CopperAxeBarbarian/Results/attempt-03-256-idle/
```

Unity 비교 화면: `unity-size-comparison.png`

FireAlpaca 오버레이에서 Frame 01의 머리·어깨·몸통 외곽이 Frame 00보다 바깥쪽에 나타난다. 도끼를 위로
들어 수평 bbox가 넓어진 Frame 00은 전체 Actor를 축소했고, 도끼가 내려가 수평 bbox가 좁아진 Frame 01은
발바닥 하단을 유지한 채 전체 Actor를 확대했다.

## Attempt 04 — Scale-lock Feedback Regeneration

```text
Source result: Attempt 02
Feedback: keep the body, head, and outline scale identical across all frames; do not resize the character to fit axe movement

[PerfectPixel result]
Identity consistency: Reject — Frame 00~02에서 양손 각각의 도끼 구성이 유지되지 않음
Both axes preserved: Reject — Frame 03에서만 한 손에 한 자루씩 명확히 보임
Foot/origin stability: Partial — bbox bottom은 전 프레임 y=470으로 동일
Frame 00 bbox: x58 y42, 348x428
Frame 01 bbox: x46 y42, 354x428
Frame 02 bbox: x18 y42, 386x428
Frame 03 bbox: x38 y86, 428x384
Frame-to-frame scale: Reject — Frame 03 높이가 앞 프레임보다 약 10.3% 작음
Content-fit behavior: Confirmed — 양손 도끼로 폭이 커진 Frame 03에서 Actor 전체가 축소됨

[Decision]
Keep / Retry: Reject
Reason: scale-lock 피드백이 후단 content-fit을 막지 못했고 무기 구성까지 훼손함
```

보존된 결과 파일:

```text
ProjectDocs/ArtPipeline/Characters/CopperAxeBarbarian/Results/attempt-04-feedback-scale-lock/
```

피드백 재생성은 Attempt 02의 특정 프레임을 고치지 않고 전체 프레임 세트를 새로 생성했다. `do not resize`
문장은 생성 모델의 포즈와 무기 배치에는 영향을 주었지만, PerfectPixel의 프레임별 content-fit 단계는 제어하지
못했다. 이후 절대 축척 고정을 Feedback만으로 해결하려는 재시도는 중단한다.

## Attempt 05 — C형 2등신 Base Idle 재시작

이 시도는 Attempt 04의 연속 보정이 아니라 캐릭터 디자인과 기준 이미지가 교체된 새 생산 기준의 첫 시도다.

```text
Date:
Base image version: CopperAxeBarbarian-master-input-v3-c-hybrid-512.png

[Character & Style]
Character name: CopperAxeBarbarian
Character description: Cute two-head-tall copper-skinned barbarian with braided black hair, a beard, a bare tattooed torso, a diagonal leather harness and a small iron guard on his right shoulder. He holds one matching one-handed axe in each hand; preserve his compact proportions and front-biased three-quarter view.
Art style: Pixel Art
Frame cell size: 512 x 512 px

[Animation]
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Motion description: steady chest-and-abdomen breathing with slight torso rise; keep shoulders relaxed, feet planted, and both axes steady
Facing direction: Not set

[PerfectPixel result]
Quality score:
UI warnings by frame:
Identity consistency: Pass / Fix / Reject
Compact two-head proportions: Pass / Fix / Reject
Breathing readability: Pass / Fix / Reject
Shoulder restraint: Pass / Fix / Reject
Both axes preserved: Pass / Fix / Reject
Frame-to-frame body scale: Pass / Fix / Reject
Foot/origin stability: Pass / Fix / Reject
Crop and safe margin: Pass / Fix / Reject
Usable frames:
Frames requiring fixes:
Rejected frames:

[Decision]
Keep current input / Retry:
One field to change next:
Reason:
```

첫 출력에서는 피드백을 함께 넣지 않는다. 결과가 너무 정적이거나 어깨가 과하게 들썩이는 경우에만
`04_motion-idle.md`에 정리된 해당 피드백 문장 하나를 선택해 전체 세트를 재생성한다.

## Attempt 01 — Chest Tap

```text
Date:
Base image version:

[Character & Style]
Character name: CopperAxeBarbarian
Character description: Muscular copper-skinned male barbarian with braided black hair, a short beard, a bare tattooed torso, a small iron guard on his right shoulder secured by a diagonal leather harness, leather bracers, dark fur trousers, and two matching one-handed axes. Preserve his front-biased three-quarter view, screen-right gaze, body proportions, tattoos, colors, gear, and both axes.
Art style: Pixel Art
Frame cell size:

[Animation]
Animation name: Chest Tap
Frames: 4
FPS: 6
Repeat: Play once / non-loop
Motion description: tap chest once with his right axe hand; keep both axes held; return to idle
Facing direction: Not set

[PerfectPixel result]
Quality score:
UI warnings by frame:
Identity consistency: Pass / Fix / Reject
Single chest tap readability: Pass / Fix / Reject
Correct anatomical right hand: Pass / Fix / Reject
Both axes preserved: Pass / Fix / Reject
Right axe avoids body/strap/face: Pass / Fix / Reject
Frame-to-frame character scale: Fix — 일부 프레임에서 크기 변화 관찰
Foot/origin stability: Pass / Fix / Reject
Return to base idle: Pass / Fix / Reject
Crop and safe margin: Pass / Fix / Reject
Usable frames:
Frames requiring fixes:
Rejected frames:

[Decision]
Keep current input / Retry:
One field to change next:
Reason: 모션 의도는 대체로 충족. 크기 변화는 입력문을 즉시 늘리지 않고 바운딩 박스 측정 후 판단
```

### Chest Tap 재시도 규칙

첫 결과에서 `right`가 화면 오른쪽 손으로 해석된 경우에만 Motion description을 아래 한 줄로 바꾼다.

```text
tap chest once with the axe hand below the shoulder guard; keep both axes held; return to idle
```

그 외 문제가 있다면 동일 시도에서 문장과 Frames/FPS/Facing direction을 동시에 바꾸지 않는다.
