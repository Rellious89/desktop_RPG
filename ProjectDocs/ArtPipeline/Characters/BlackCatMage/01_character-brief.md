# BlackCatMage — Character Brief

> 상태: `Low Companion v1` Master 재생성 대기. 고밀도 비교 실험은 종료했다.

## 정체성

| 항목 | 결정값 |
|---|---|
| ID | `BlackCatMage` |
| 역할 | Player companion |
| 한 문장 콘셉트 | 갈색 로브와 뾰족 모자를 쓴 검은 고양이 마법사. 지팡이 끝의 붉은 마법석이 유일한 강한 포인트다. |
| 성격 | 차분함, 귀여움, 약간의 덜렁거림 |
| 승인 시점 | 화면 오른쪽을 향하는 3/4 뷰 |
| 화면 진행 방향 | screen-right |
| 주 장비 | 목재 지팡이 1개, 끝에 작은 붉은 마법석 1개 |
| 의상 | 갈색 로브, 갈색 뾰족 마법사 모자, 단순 벨트 |

## 변경 불가 요소

- 검은 고양이의 귀, 꼬리, 따뜻한 밝은 눈
- 검은 털이 어두운 배경에 묻히지 않도록 하는 숯색/청회색 하이라이트
- 갈색 로브와 모자, 목재 지팡이, 단 하나의 붉은 마법석
- 화면 오른쪽을 향하는 Master 방향
- 지팡이 이외의 무기, 투사체, 상시 마법 이펙트는 Master에 포함하지 않음

## 시각 언어와 Master 기준

고양이 마법사의 최상위 생산 기준은 실제 빌드에서 승인된 바바리안 최종 스프라이트의 `Low Companion v1`이다.
목표는 약 2~2.5등신, 굵은 계단형 외곽선, 적은 색면, 후가공 기준 약 3×3 픽셀 덩어리다.
고밀도 마감과 Unity `VisualRoot Scale 0.35`는 비교 빌드에서 어색하지 않았지만 수작업 후보정 비용 때문에
현재 생산 규격에 채택하지 않는다.

기존 다직업 라인업의 고양이 마법사는 직업 식별 요소 참고로만 사용한다.

```text
ProjectDocs/ArtPipeline/Characters/CopperAxeBarbarian/Prototypes/class-lineup-03/low-b-cat-mage-source.png
ProjectDocs/ArtPipeline/Characters/CopperAxeBarbarian/Prototypes/class-lineup-03/low-c-cat-mage-source.png
```

반드시 보존할 캐릭터 정체성:

- 모자 아래에서도 즉시 읽히는 두 개의 따뜻한 밝은 눈과 양쪽 귀
- 화면 오른쪽을 향하는 친근한 3/4 전신 실루엣
- 단순한 갈색 로브·짧고 굵은 목재 지팡이·선명한 발과 꼬리
- 바바리안과 나란히 놓아도 같은 밀도로 읽히는 작은 데스크톱 컴패니언 인상

로브 색은 갈색, 마법석은 붉은색으로 유지한다. 모자나 지팡이가 얼굴과 몸보다 먼저 읽히는 실루엣,
바바리안보다 촘촘하고 부드러운 고밀도 마감은 Reject한다.

이전 후보는 비교 기록으로만 남긴다.

| 후보 | 파일 | 용도 |
|---|---|---|
| High/compact 비교 | `Assets/Art/Character/BlackCatMage/master/BlackCatMage-master-compact-v1.png` | 비교 종료. 화면 비주얼은 합격이나 고밀도 후보정 비용으로 생산 규격에서 제외 |
| B–C 후보 | `Assets/Art/Character/BlackCatMage/master/BlackCatMage-master-bc-v1.png` | 비교 기록. 현행 Low Companion v1과 다른 마감 밀도 |
| Lineup-aligned 후보 | `Assets/Art/Character/BlackCatMage/master/BlackCatMage-master-lineup-v1.png` | 비교 기록. 현행 Low Companion v1과 다른 마감 밀도 |
| Low Companion v1 | `Assets/Art/Character/BlackCatMage/master/BlackCatMage-master-low-v2.png` | 새 생산 후보. 생성 후 PerfectPixel Idle로 검증 |

## 아직 확정하지 않은 것

- 바바리안 대비 최종 Unity 표시 배율
- Character description에서 Fur highlight를 어느 정도까지 반복해야 정체성이 안정되는지
- `Facing direction` 드롭다운을 `Not set`으로 둘지 `Right`로 둘지. Master가 올바른 방향이므로 첫 Attempt는
  `Not set`으로 비교하고 결과가 반전될 때만 `Right`를 별도 Attempt로 시험한다.
