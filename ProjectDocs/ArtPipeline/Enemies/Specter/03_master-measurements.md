# Specter Master Measurements

```text
Canvas: 512×512 RGBA
Facing: screen-right
Approved view angle: slight three-quarter side view
Character top Y: 298
Visible bounding box: X 231..281 / Y 298..353
Occupied width/height: 51×56 px
Logical silhouette height: 56 px
Actor Origin / floor projection: X 256 / Y 360
Float gap: 6 px from lowest cloth pixel to Actor Origin
Calculated pivot normalized: X 0.5 / Y 0.296875
Palette: 7 opaque subject colors + transparency
Outline: dark blue-gray, approximately 1 logical pixel
Light direction: upper-left
Safe margin: at least 234 px horizontally and 152 px vertically
Actual scale: 0.8
```

부유형 Actor라 전방 디딤발 접촉점이 없다. 모든 애니메이션 프레임은 `(256, 360)`을 바닥 투영 기준점으로
사용하며, 기본 Idle의 밑단 최저점은 이 점보다 6px 위에 둔다. 부유 동작은 이 기준점을 바꾸지 않고
Sprite 내부의 밑단과 몸체 높이만 변화시킨다.

## Master files

- `Assets/Art/Enemy/Specter/master/Specter-master-v2.png`: 입과 늘어진 양손을 추가한 투명 RGBA 확정 마스터
- `Assets/Art/Enemy/Specter/master/Specter-master-v2-chromakey.png`: v2 PerfectPixel 입력 확인용 녹색 배경본
- `Assets/Art/Enemy/Specter/master/Specter-master-v1.png`: 팔과 입이 없는 초기 비교본
- `Assets/Art/Enemy/Specter/master/Specter-master-v1-chromakey.png`: v1 녹색 배경 비교본
- `Assets/Art/Enemy/Specter/master/Specter-master-v1-transparent-source.png`: 정규화 전 투명 생성 원본
- `Assets/Art/Enemy/Specter/master/Specter-master-v1-chromakey-source.png`: 이미지 생성 원본

## Generation prompt summary

Classic floating white burial-sheet Specter, two black eye holes with restrained cyan corruption light, a small open
mouth, two slightly raised cloth arms with drooping hands, no equipment, three broad torn lower tails, Low Companion v1 low-density pixel art, slight three-quarter view
facing screen-right, limited palette, hard pixel edges, flat chroma-key background, no shadow or translucent smoke.
