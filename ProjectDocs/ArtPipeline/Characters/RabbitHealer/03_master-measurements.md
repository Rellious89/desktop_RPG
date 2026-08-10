# RabbitHealer — Master Measurements

> 상태: `Approved / Master v2`
>
> 측정일: 2026-08-10
>
> 승인일: 2026-08-10

## Approved Master

```text
Assets/Art/Character/RabbitHealer/master/RabbitHealer-master-v2.png
```

## Candidate Measurements

```text
Master canvas: 1254×1254 RGBA
Opaque bounds: x 453–839 / y 160–1094
Occupied width: 387px
Occupied height: 935px
Facing: screen-right
View: slight three-quarter
Partially transparent pixels: 0
External shadow: none
```

Pillow의 알파 경계는 오른쪽·아래쪽 exclusive 좌표 `(453, 160, 840, 1095)`로 확인했다. 위 표는 사람이 읽기
쉽도록 마지막 포함 픽셀 좌표로 기록한다.

## v1 / v2 Comparison

| Version | Canvas | Opaque bounds | Occupied size | 설정 일치 |
|---|---|---|---|---|
| v1 | 1254×1254 | `(453,160)–(839,1094)` | 387×935 | 노란 단일 의상이라 Character Sheet와 불일치 |
| v2 | 1254×1254 | `(453,160)–(839,1094)` | 387×935 | 하늘색 상의와 노란 멜빵바지로 일치 |

두 후보의 점유 영역은 같지만 v2가 기존 복식 설정을 정확히 반영한다. v2를 정식 Master로 승인했으며 v1은
삭제하지 않고 비교용 구버전으로 유지한다.

## Observed Delivery Profile

```text
Runtime canvas: 128×128 RGBA
PPU: 50
Pivot: (0.5, 0.1)
Filter: Point
Compression: None
Runtime path: Assets/Art/Character/RabbitHealer/
```

기존 Character Sheet의 512×512 / PPU 200은 역사적 생산 소스 규격이다. 실제 게임 연결 프레임의 납품값과
혼용하지 않는다.

## Master Locks

- 양쪽 안쪽 분홍색이 모두 보이는 긴 귀와 동일한 귀 간격
- 흰 털, 분홍색 눈·코·머리핀
- 하늘색 반팔티와 노란 멜빵 호박바지
- 갈색 크로스백의 방향과 몸 앞쪽 가방 위치
- 화면 오른쪽 손의 짧은 목재 완드 1개
- 맨발 토끼발과 화면 오른쪽 3/4 자세
- 귀를 제외한 신체 비율과 작은 체격
