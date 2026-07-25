# LeafGlaiveElf Master Measurements

> 상태: Approved / Master v6
>
> v1~v4는 무기 길이, 창대 직선성, 해부 또는 체형 비율 문제로 모두 비교본으로만 보존한다.
> v5는 논리 높이를 원본 이미지의 실제 점유 높이로 잘못 적용해 과도하게 축소하고 색상을 강제 양자화한 오류본이다.
> `concept/LeafGlaiveElf-concept-v5-reset.png`의 해상도와 색을 그대로 보존하고 배경만 제거한 v6가 현재 확정 Master다.

```text
Master reference canvas: 1254×1254 RGBA
Facing: screen-right
Approved view angle: slight three-quarter side view
Character body logical height target for production output: approximately 91 px
Actual scale: 1.3
Approved body ratio: approximately 2.5 heads tall, VenomCultist family
Palette: preserve approved source colors; no automatic quantization
Outline: dark brown-green, approximately 1 logical pixel
Light direction: upper-left
```

논리 높이 약 91px은 PerfectPixel 이후 게임용 프레임을 정렬할 때 적용하는 제작 목표다. PerfectPixel에 올리는
Master 기준 이미지를 91px로 축소하거나 팔레트를 강제로 줄이지 않는다. 한쪽 곡선 날과 반대쪽 뭉툭한 장식,
VenomCultist 계열의 약 2.5등신 비율, 스케일 1.3과 신장 약 1.2배의 무기 길이를 원본 크기에서 보존한다.

## Master files

- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v6.png`: 승인 콘셉트의 1254×1254 해상도와 색을 보존하고 배경만 제거한 투명 RGBA 확정 마스터
- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v5.png`: 과도한 축소와 색상 양자화 문제로 Reject한 오류본
- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v5-chromakey.png`: v5 PerfectPixel 입력용 자홍색 배경본
- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v5-transparent-source.png`: 정규화 전 투명 소스
- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v5-chromakey-source.png`: 승인 콘셉트 원본 복사본
- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v4.png`: 무기가 과도하게 길고 해부·비율 문제가 있어 Reject한 비교본
- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v4-chromakey.png`: v4 PerfectPixel 입력용 자홍색 배경본
- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v3.png`: 직선 창대지만 뒷날이 작았던 비교본
- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v2.png`: 비대칭 양날이지만 창대 직선성이 부족했던 비교본
- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v1.png`: 곡선형 단일 주날을 사용한 이전 비교본
- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v1-chromakey.png`: v1 자홍색 배경 비교본
- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v1-transparent-source.png`: 정규화 전 투명 생성 원본
- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v1-chromakey-source.png`: 비율 보정된 이미지 생성 원본
- `Assets/Art/Character/LeafGlaiveElf/master/LeafGlaiveElf-master-v1-proportion-draft.png`: 머리가 작고 몸이 길어 Reject한 초기 비교본

## Generation prompt summary

Master v6 preserves the approved v5 concept at source resolution without palette quantization. It uses exact 2.5-head
proportions matching VenomCultist, normal equal-length arms and stable legs, one perfectly straight white-shaft glaive at
1.2× body height, one gently curved front blade, and one blunt yellow rear ornament.
