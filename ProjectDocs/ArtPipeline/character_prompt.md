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
