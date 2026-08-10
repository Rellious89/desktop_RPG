# BlackCatMage / CatMage Legacy V1 Migration Record

> 판정: `Reference only`
>
> 작성일: 2026-08-10

## Purpose

새 이미지를 생성한 Attempt가 아니라, 제작 패키지 도입 전에 흩어져 있던 자료와 현재 런타임을 연결해 두는
이관 기록이다.

## Runtime Evidence

```text
Assets/Art/Character/CatMage/idle/   — 4 frames
Assets/Art/Character/CatMage/idle_a/ — 4 frames
Assets/Art/Character/CatMage/idle_b/ — 4 frames
Assets/Art/Character/CatMage/cast/   — 3 frames
```

모든 납품 프레임은 128×128, PPU 50, Pivot `(0.5, 0.1)`로 관측되며 Motion Profile과 CharacterDefinition에
연결되어 있다.

## Recovered Source

기존 Brief의 세부 후보 경로들은 현재 작업 트리에 없지만, 실제 생산에 사용한 Master는
`/Users/rellious/Desktop/keybuddy/c_character/c_CatMage/CatMage_master.png`에서 확인했다. 이를
`Assets/Art/Character/CatMage/master/CatMage-master-v1.png`로 체크섬 변경 없이 복구했다.

## Resume Rule

1. 신규 Attack B Motion Brief를 작성한다.
2. Master v1을 기준으로 새 PerfectPixel Attempt를 별도 폴더에 기록한다.
3. V2 재설계가 승인되기 전까지 현재 v1 Master와 런타임 리소스를 유지한다.

Low PerfectPixel Idle 00을 최근접 128px로 축소하면 현행 Idle 00과 알파 형태 98.02%가 일치한다. 바이트
차이는 FireAlpaca 후보정과 최종 납품 과정을 거친 결과로 본다.
