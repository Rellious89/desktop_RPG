# RabbitHealer — Character Sheet

> Legacy source sheet. 현재 제작 진입점은 [`00_package-index.md`](./00_package-index.md)이며, 정리된 설정은
> [`01_character-brief.md`](./01_character-brief.md)를 사용한다.

- Display name: 토끼힐러 / RabbitHealer
- Type: character · World: `ANIMAL-LAND-01` v1
- Aliases: None
- Species: Rabbit · Role: 부족 힐러 · Status: concept
- Concept: 부족생활을하는 토끼 종족. 그 중에서도 힐러역할이다. 어린 나이이며 여성 캐릭터이다. 흰색의 털을 가진 토끼이며, 노란색 멜빵 바지와 하늘색의 가벼운 반팔티를 안에 입고 있다. 갈색의 작은 가방을 크로스로 메고 있다.

## Body & Proportions

| Field | Value | Origin |
|---|---:|---|
| Stature | tiny | Override |
| Target logical height | 65px | Override |
| Build | slender | Override |
| Proportion | humanoid-sd-2.5-head | World |
| Species scale | 1 | World |
| Head / hand / foot / torso | m / s / m / narrow | mixed |

## Look

- Physical traits: 기다란 토끼 귀가 특징. 전체 크기는 귀 크기는 무시하고 머리부터 발끝까지의 크기를 기준으로 해야함. 토끼귀는 별개
- Hair / eyes / skin: 긴 토끼귀 / 분홍색 / 흰색
- Clothing: 갈색 크로스백 (작은); 맨발; 하늘색 반팔티; 노란색 멜빵바지 (호박바지)
- Materials: 천 질감
- Decorations: 분홍색 머리삔
- Invariants: 토끼 귀 안쪽은 분홍색으로 양쪽 귀 모두 안쪽이 보이는 각도여야 한다.
- Forbidden: 토끼 귀는 전체 스케일에 포함되지 않는다.

## Weapon & Equipment

- Weapon: short-wand, small, count 1
- Hands: anatomical-right / none
- Structure: 짧고 가벼운 한손용 나무 완드
- Allowed families: short-wand
- Secondary: None

## Production & Canvas

- Base canvas: 512×512
- Large-motion canvas: same as base
- Logical block: 3×3
- Pivot: forward-foot-contact
- PPU: 200
- Unity visual scale: 1
- Layers: character-or-outfit → weapon → effect

## Validation Summary

- None

## Approved Exceptions

- None

## Calculated Values and Interpretations

- Height relative to world baseline: -7.1%
- Weapon logical length estimate: 19.5
- Species scale is independent of stature and logical height.
- Unity visual scale is a runtime display transform and is normally 1.0.
- Canvas pixels, logical silhouette pixels, and logical pixel block size are distinct measurements.

## Schema

- schemaVersion: `1.0.0`
- worldTemplateRef: `ANIMAL-LAND-01` v1
