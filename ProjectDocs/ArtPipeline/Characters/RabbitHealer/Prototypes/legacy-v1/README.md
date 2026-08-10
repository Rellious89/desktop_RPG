# RabbitHealer Legacy V1 Migration Record

> 판정: `Existing resources retained`
>
> 작성일: 2026-08-10

## Purpose

제작 패키지 규칙 도입 전 생성된 Master, 캐릭터 프레임과 런타임 연결 상태를 보존하는 이관 기록이다. 새로운
이미지 생성 Attempt가 아니며 기존 파일을 이동하거나 덮어쓰지 않았다.

## Existing Resources

```text
Assets/Art/Character/RabbitHealer/master/ — v1, v2 candidates
Assets/Art/Character/RabbitHealer/idle/   — 4 frames
Assets/Art/Character/RabbitHealer/idle_a/ — 5 frames
Assets/Art/Character/RabbitHealer/idle_b/ — 6 frames
Assets/Art/Character/RabbitHealer/attack/ — 3 frames
```

런타임 납품 프레임은 128×128, PPU 50, Pivot `(0.5, 0.1)`로 관측되며 Motion Profile과
CharacterDefinition에 연결되어 있다.

## Master Comparison

- v1과 v2는 모두 1254×1254이고 불투명 점유 영역은 387×935로 같다.
- v1은 노란 단일 의상이다.
- v2는 기존 Character Sheet의 하늘색 반팔티와 노란 멜빵바지를 반영한다.
- v2는 2026-08-10 정식 Master로 승인되었다. v1은 비교용 구버전이다.

## Resume Rule

1. Attack B를 3프레임 / 18fps로 생성한다.
2. 점프 없이 제자리에서 완드를 앞으로 내미는지 검수한다.
3. 새 PerfectPixel Attempt는 별도 `Prototypes/{attempt}/README.md`에 기록한다.
4. 승인 후 Tier 2 풀에 Attack A와 Attack B를 함께 등록한다.
