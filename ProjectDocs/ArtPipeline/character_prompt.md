# Character Production Prompt Template

이 문서는 이미지 생성 도구에 전달할 기본 템플릿이다. Unity 납품 규격의 최종 기준은
`ProjectDocs/DesignRules/character-sprite-and-animator-rules.md`다.

캐릭터 콘셉트와 애니메이션 설계부터 시작할 때는 먼저
`ProjectDocs/ArtPipeline/resource-production-workflow.md`의 Gate A~C를 통과한다. 이 템플릿은
승인된 Character Brief와 Master Design을 이미지 생성 요청으로 옮기는 용도다.

이 템플릿 전체를 PerfectPixel의 Character/Motion description에 붙여넣지 않는다. PerfectPixel용
축약 입력은 `ProjectDocs/ArtPipeline/perfectpixel-input-and-review.md`를 사용한다.

## [Character Name]
[Character Name]

## [Role]
Player / Enemy

## [Style]
[Art Direction]

## [Style Reference Hierarchy]

1. 승인된 실제 게임 스프라이트: `Assets/Art/ReferenceSheets/low-companion-v1-barbarian-reference.png`의 저밀도 픽셀 덩어리, 외곽선, 화면상 읽힘
2. Character Brief: 해당 Actor의 종족·직업·장비·색상과 화면 방향
3. `class-lineup-03` 및 LOW-B/LOW-C 비교: 직업·의상·실루엣 발상 참고

새 캐릭터가 바바리안 및 승인된 BlackCatMage와 다른 픽셀 크기·외곽선·화면상 읽힘으로 보이면 Master Design을 Reject한다.
목표는 약 3×3의 굵은 픽셀 덩어리와 작은 컴패니언 비율이며, 이는 얼굴·의상·재질의 표현 품질을 낮추라는
제약이 아니다. 같은 크기의 승인 캐릭터 수준으로 표정과 직업 요소가 읽혀야 한다.
일반 Actor는 머리끝부터 접지점까지 **70px 내외(65~75px)의 논리 실루엣 높이**를 사용한다. 바바리안은
근육질 대형 예외 79px, BlackCatMage는 모자 포함 예외 86px이다. 생성 프롬프트에는 반드시 해당 Actor의
목표 논리 높이와, 장비까지 포함한 전체 실루엣이 그 범위를 넘지 않는다는 제약을 넣는다. 단, 프롬프트의 수치는
지시일 뿐 검증값이 아니므로, 생성 후 실제 Master 실루엣을 측정해 목표값과 일치할 때만 PerfectPixel에 투입한다.

## [View]
[Approved Side / Three-Quarter View], facing screen [Right/Left]

## [Canvas]
512x512 px per frame

## [Outline]
[Consistent Pixel Outline]

## [Palette]
[Approved Palette / Limited Palette]

## [Background]
Transparent RGBA, no background, no external shadow

## [Animation]
[Animation Name], [Frame Count] Frames

## [Alignment]

- Separate PNG file for every frame
- Perfect frame alignment
- Identical 512x512 canvas for all frames
- Same actor origin and planted-foot contact point in every frame
- Same character design, proportions, equipment, palette, outline and lighting
- No cropped body parts, weapon or costume
- No anti-aliased halo or background-color residue

## [Locked Master Design]

- Character height and head-to-body ratio: [Approved measurements]
- Forward planted-foot contact point: [X, Y in top-left-origin pixel coordinates]
- Equipment dimensions: [Approved ratios or pixel measurements]
- Light direction: [Approved direction]
- Elements that must never change: [Locked features]

## [Motion Intent]

- Motion type: [Idle / IdleVariant / LoopableBasic / HitHoldRecovery]
- Key poses: [Ordered pose descriptions]
- HitPoint candidate: [Strike pose; omit for non-attacks]
- Moving parts: [Allowed parts]
- Locked parts: [Parts that must remain unchanged]
- Actor translation: None inside the sprite; use Attack Movement separately

## [Delivery]

```text
Assets/Art/{Character|Enemy}/{Name}/{animation}/{Name}-{animation}-00.png
Assets/Art/{Character|Enemy}/{Name}/{animation}/{Name}-{animation}-01.png
...
```

Do not deliver a combined sprite sheet as the final Unity asset. If the generator only returns a sheet,
split it into individual aligned 512x512 RGBA PNG files before import.

## Example: Cat Knight Idle

```text
Character: Cat Knight
Role: Player
Style: Cute SD Pixel Art
View: Approved view angle, facing screen right
Canvas: 512x512 px per frame
Background: Transparent RGBA
Animation: Idle, 4 frames
Alignment: Same planted-foot contact point and actor origin in every frame
Delivery: Four separate PNG files
```
