# VenomCultist Master Measurements

```text
Canvas: 512×512 RGBA
Facing: screen-right
Approved view angle: slight three-quarter side view
Character top Y: 310
Forward foot contact X/Y: 268 / 380
Calculated pivot X/Y normalized: 0.5234375 / 0.2578125
Visible bounding box: X 214..284 / Y 310..379
Occupied width/height: 71×70 px
Logical silhouette height: 70 px
Actual scale: 1.0
Palette: 10 opaque subject colors + transparency
Outline: near-black purple, approximately 1 logical pixel
Light direction: upper-left
Safe margin: at least 214 px horizontally and 132 px vertically
```

앞손의 단검과 독 방울까지 포함한 전체 실루엣을 70px 높이로 정규화했다. 전방 디딤발 접촉점
`(268, 380)`을 이후 모든 프레임의 Actor Origin으로 사용한다.

## Master files

- `Assets/Art/Enemy/VenomCultist/master/VenomCultist-master-v1.png`: 투명 RGBA 최종 마스터
- `Assets/Art/Enemy/VenomCultist/master/VenomCultist-master-v1-chromakey.png`: PerfectPixel 입력 확인용 자홍색 배경본
- `Assets/Art/Enemy/VenomCultist/master/VenomCultist-master-v1-transparent-source.png`: 정규화 전 투명 생성 원본
- `Assets/Art/Enemy/VenomCultist/master/VenomCultist-master-v1-chromakey-source.png`: 이미지 생성 원본

독의 녹색을 보존하기 위해 정규화된 크로마키본은 녹색이 아니라 `#ff00ff` 자홍색 배경을 사용한다.

## Generation prompt summary

Living human cultist in a deep dark-purple hooded robe, face almost completely hidden, oversized bone-white skull
pendant centered on the chest, exactly one short poison-coated dagger in the forward hand, compact 2.5-head SD body,
Low Companion v1 low-density pixel art, facing screen-right, no armor, second weapon, scenery or shadow.
