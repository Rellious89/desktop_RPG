# BlackCatMage — Master Measurements

> 상태: `Approved legacy Master recovered / v1`
>
> 복구일: 2026-08-10

## Approved Master

```text
Assets/Art/Character/CatMage/master/CatMage-master-v1.png
Original source: /Users/rellious/Desktop/keybuddy/c_character/c_CatMage/CatMage_master.png
SHA-1: 83363e587625f16f0c6fb02b9df6993c63f12183
```

사용자가 Master를 keybuddy 작업 폴더에 넣고 PerfectPixel 결과를 수정해 에디터에 적용했던 생산 과정을
확인했다. Low CatMage PerfectPixel Idle을 128×128로 최근접 축소했을 때 현행 Idle 00과 알파 형태가
98.02% 일치해 같은 생산 계보임을 추가 확인했다.

## Master Measurements

```text
Master canvas: 1254×1254 RGB/RGBA opaque
Background: green chroma-key, not transparent
Character bounds excluding green background: x 333–961 / y 308–1142
Occupied width: 629px
Occupied height: 835px
Facing: screen-right slight three-quarter
```

녹색 배경 판정은 `G > 180`, `G > R×1.5`, `G > B×1.5` 기준으로 측정했다. 이 파일은 생성 입력 Master이며
Unity 납품 프레임으로 직접 사용하지 않는다.

## Legacy Delivery Profile

```text
Runtime path: Assets/Art/Character/CatMage/
Canvas: 128×128 RGBA
PPU: 50
Pivot: (0.5, 0.1)
Filter: Point
Compression: None
```

| Motion | Frames | Runtime FPS | 상태 |
|---|---:|---:|---|
| idle | 4 | 6 | Connected |
| idle_a | 4 | 6 | Connected |
| idle_b | 4 | 6 | Connected |
| Attack A / cast | 3 | 18 | Connected |

## Identity Locks

- 양쪽 귀와 꼬리가 분명한 검은 고양이 수인
- 어두운 배경에서도 읽히는 숯색·청회색 털 하이라이트
- 갈색 로브와 뾰족 모자
- 짧고 굵은 목재 지팡이 1개와 작은 붉은 마법석 1개
- 따뜻하고 밝은 눈, 화면 오른쪽을 향하는 친근한 3/4 전신

기존 Brief에 기록된 compact/bc/lineup/low 후보 경로는 역사적 비교 기록이며 복구된 v1 Master를 대체하지
않는다.

