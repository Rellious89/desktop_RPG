# RabbitHealer — Tier 1 Attack Motion Brief

> 상태: `Existing runtime documented`

## Existing Runtime Motion

```text
Animation ID: attack / tier1
Type: Basic ranged support attack
Frames: 3
Animation FPS: 18
Loop: No
Canvas: 128×128 RGBA
Path: Assets/Art/Character/RabbitHealer/attack/
Cast frame: 0
Hit frame: 1
Pivot: (0.5, 0.1)
```

독립 발사체와 프레임 동기화 값은 `ProjectDocs/DesignRules/attack-animation-rules.md` 및
`Assets/Data/MotionProfiles/Characters/RabbitHealer/RabbitHealer_Attack.asset`을 기준으로 한다.

## Motion Intent

- Frame 0에서 살짝 뛰어오르며 짧은 완드를 들어 빠르게 시전 준비를 한다.
- Frame 1에서 완드를 아래로 내리찍는 가장 강한 포즈와 마법 발사가 일치한다.
- Frame 2에서 착지하며 안정 자세에 복귀한다.
- 공격 자체는 빠르지만 공격적 전사보다 회복가의 가벼운 마법 사용으로 읽힌다.

이 모션은 Attack A다. Tier 2에서 추가되는 Attack B는 점프 없이 제자리에서 완드를 앞으로 내미는 동작으로
구분한다.

## Locked Parts

- 발과 접지선, 전체 신체 크기
- 긴 귀의 길이와 안쪽 분홍색
- 하늘색 상의, 노란 멜빵과 갈색 가방
- 완드 하나의 길이와 손 연결
- 얼굴과 화면 방향

## Acceptance Criteria

- 18fps에서 Frame 1의 발사 타이밍이 즉시 읽힌다.
- 캐릭터 프레임과 독립 투사체가 중복으로 그려지지 않는다.
- 발과 Origin이 움직이지 않는다.
- 귀, 가방, 완드와 복식이 세 프레임에서 동일하다.
- 마지막 프레임 뒤 Base Idle로 크기 점프 없이 복귀한다.
