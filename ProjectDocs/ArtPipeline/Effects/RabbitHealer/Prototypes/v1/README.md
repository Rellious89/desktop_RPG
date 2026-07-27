# RabbitHealer Attack VFX Prototype v1

## 상태와 목적

- 상태: AI 정제 가능성 및 빠른 공격 템포 가독성 검증용 프로토타입
- 기준 공격: `RabbitHealer_Attack`, 18fps, Hit Frame 1
- 재생 의도: 연타 중 Frame 0 ↔ Frame 1 반복, 입력 종료 시 Frame 2 복귀 잔상
- 캐릭터 원본은 수정하지 않고 512×512 투명 오버레이 PNG로 분리한다.

## 산출물

- `Assets/Art/Effects/RabbitHealer/AttackPrototype/v1/RabbitHealer-attack-vfx-00.png`
- `Assets/Art/Effects/RabbitHealer/AttackPrototype/v1/RabbitHealer-attack-vfx-01.png`
- `Assets/Art/Effects/RabbitHealer/AttackPrototype/v1/RabbitHealer-attack-vfx-02.png`
- `RabbitHealer-attack-vfx-fast-loop.gif`: 18fps 기준 Frame 0 ↔ Frame 1 반복 미리보기
- `RabbitHealer-attack-vfx-full.gif`: Frame 0 → 1 → 2 전체 미리보기

## 생성 및 정렬 방식

1. 사용자 합성 시안과 효과 전용 레이어를 프레임별 참조 이미지로 사용했다.
2. 기본 이미지 생성 도구로 각 프레임을 별도 정제했다.
3. 결과는 단색 `#FF00FF` 크로마키 배경으로 생성한 뒤 알파 PNG로 변환했다.
4. 생성 모델이 변경한 캔버스 위치는 사용자의 원본 효과 알파 영역을 기준으로 복원했다.
5. 최근접 보간으로 512×512에 맞추고 알파를 이진화해 픽셀 가장자리를 유지했다.

## 최종 프롬프트 세트

### Frame 0

```text
Refine only the small yellow-white magic seed from the user's effect-only overlay. Preserve its center position and footprint relative to the 512x512 Rabbit Healer reference. Use a chunky warm-white core, pale-yellow outer pixels, and 4-6 short square sparks. Effect-only, restrained pixel art, hard square pixels, limited palette, no antialiasing, blur, bloom, character pixels, text, or watermark. Use a perfectly flat #FF00FF chroma-key background.
```

### Frame 1

```text
Refine only the launched yellow-white magic projectile and cyan staff swing marks from the user's effect-only overlay. Preserve their positions, diagonal launch direction, and footprint relative to the 512x512 Rabbit Healer reference. This is the strongest frame at 18fps and must read instantly. Use a compact warm-white projectile core, short chunky pale-yellow tail, and a restrained two- or three-stroke stepped cyan staff arc. Effect-only, hard square pixels, limited palette, no antialiasing, blur, bloom, oversized beam, character pixels, text, or watermark. Use a perfectly flat #FF00FF chroma-key background.
```

### Frame 2

```text
Refine only the cyan staff swing afterimage from the user's effect-only overlay. Preserve its position, curve direction, and footprint relative to the 512x512 Rabbit Healer reference. Use two or three short stepped cyan fragments, visibly weaker and smaller than Frame 1, with no yellow projectile. Effect-only, hard square pixels, limited two-cyan palette, no antialiasing, blur, glow, character pixels, text, or watermark. Use a perfectly flat #FF00FF chroma-key background.
```

## 현재 판단 보류 항목

- 실제 게임 표시 크기에서 Frame 0 ↔ Frame 1 반복이 충분히 인지되는지
- 노란 발사체를 프레임 오버레이가 아닌 독립 이동 오브젝트로 분리할지
- 정식 공통 이펙트 픽셀 블록 규격과 팔레트
