# KeyBuddy 1차 리소스 생산 워크플로

> 적용 범위: 캐릭터 콘셉트 설정 → 기준 픽셀 디자인 생성 → 애니메이션 콘셉트 설정
>
> 이 문서의 결과물은 PerfectPixel 입력 자료다. PerfectPixel 출력 프레임의 선별·후보정과 Unity 연결은 다음 공정이다.

최종 Unity 납품 규격은
`ProjectDocs/DesignRules/character-sprite-and-animator-rules.md`, 공격 동작 규칙은
`ProjectDocs/DesignRules/attack-animation-rules.md`를 우선한다. 이 문서와 충돌하면 두 규칙 문서가 정답이다.

## 0. 이번 채널에서 확정할 것

각 Actor마다 아래 세 가지 패키지만 만든다.

1. **Character Brief**: 역할, 성격, 실루엣, 체격, 장비, 팔레트와 금지 요소
2. **Master Design**: 한 방향의 기준 캐릭터 이미지와 고정 디자인 명세
3. **Motion Brief**: 애니메이션별 목적, 키포즈, 루프 구조, 타격 포즈와 금지 변형

이번 단계에서 여러 장의 최종 Unity 프레임을 완성하려고 하지 않는다. 생성된 이미지는 디자인 기준 또는
PerfectPixel 참조 이미지이며, 최종 프레임은 PerfectPixel 이후 개별 512×512 RGBA PNG로 정리한다.

## 1. 변경 불가 규격

| 항목 | 고정값 |
|---|---|
| 프레임 캔버스 | 512×512 px |
| Unity PPU | 200 |
| 배경 | 완전 투명 RGBA |
| 외부 그림자 | 캐릭터 PNG에 포함하지 않음 |
| Actor Origin | 전방 디딤발의 지면 접촉점 |
| 프레임 정렬 | 같은 Actor의 모든 프레임에서 위 접촉점을 동일 픽셀 좌표에 고정 |
| 크기 표현 | 승인된 저밀도 원화의 픽셀 크기로 표현, Unity Transform/VisualRoot Scale은 기본 1 |
| 최종 저장 | 프레임별 개별 PNG, 2자리 번호 |
| 시점 | 엄격한 사이드뷰를 공통 강제하지 않음. Actor별 Master Design에서 승인한 시점을 전 프레임에 고정 |
| 화면 진행 방향 | 현재 1차 생산 Master는 **화면 오른쪽(screen-right)** 방향으로 통일한다. 다른 화면 방향/FlipX 정책은 아직 확정하지 않음 |
| 좌우 용어 | 수식어 없는 좌우는 캐릭터의 해부학적 기준. 이미지/게임 화면 기준은 `화면 오른쪽/왼쪽`으로 명시 |
| 공격 판정 | 프레임 번호 자체가 아니라 `HitPoint`; 리소스에는 후보 `hitFrameIndex`를 기록 |
| 전체 전진 | 프레임/Pivot 이동이 아니라 별도 Attack Movement로 처리 |

팔레트 색 수, 논리 픽셀 배율, 외곽선 색/두께, 광원 방향은 첫 Master Design 승인 때 Actor별로
수치화하여 고정한다. 승인 뒤 애니메이션마다 다시 해석하지 않는다.

## 2. Gate A — Character Brief

이미지를 만들기 전에 아래 양식을 먼저 채운다. 비어 있는 항목이 있으면 생성 프롬프트를 작성하지 않는다.

```text
ID / 표시 이름:
Role: Player | Enemy
게임 안의 기능:
성격 키워드 3개:
한 문장 콘셉트:
승인할 시점(사이드/3/4 등):
기본 화면 진행 방향:
시각 언어 후보: `Low Companion v1` (변경은 별도 프로토타입 승인 후에만 가능)
CatKnight 대비 키 비율:
머리:몸 비율:
실루엣 핵심 3개:
주 장비와 정확한 크기 비율:
비대칭 요소:
기본 팔레트와 최대 색 수:
외곽선 규칙:
광원 방향:
전방 디딤발:
절대 바뀌면 안 되는 요소:
금지 요소:
```

### 승인 조건

- 축소된 실루엣만으로 역할을 구분할 수 있다.
- 무기와 장비의 길이·두께를 신장 대비 비율로 설명할 수 있다.
- CatKnight 대비 체격 비율이 정해져 있다.
- 전방 디딤발과 지면 접촉점을 지목할 수 있다.
- 비대칭 요소가 있다면 한 방향 제작 시 어느 쪽에 보이는지 정해져 있다.

## 3. Gate B — Master Design

Character Brief를 이용해 후보를 만들고, 한 장을 골든 샘플로 확정한다. 후보 수를 늘리는 것보다
선택된 한 장의 비율과 장비를 명문화하는 것을 우선한다.

### 생성 요청의 필수 제약

```text
512×512 canvas, transparent RGBA background.
Full-body view using the Actor's approved Master Design camera angle, facing the project's current screen direction.
One character only; no ground, cast shadow, scenery, text, UI, border or effect.
Keep the full body, costume and equipment inside the canvas with safe margin.
Hard pixel edges only; no anti-aliasing, blur, semi-transparent halo or JPEG noise.
The forward planted foot must have one clear ground-contact point.
```

`character_prompt.md`의 Style, Outline, Palette 항목에는 `cute pixel art` 같은 추상 표현만 쓰지 않고
Character Brief에서 승인한 색 수, 외곽선 두께, 광원과 비율을 넣는다.

### Master Design과 함께 기록할 좌표/수치

```text
Canvas: 512×512
Facing:
Approved view angle:
Character top Y:
Forward foot contact X/Y:
Calculated pivot X/Y normalized:
Occupied width/height:
Weapon width/length:
Palette colors:
Outline width/color:
Light direction:
Safe margin:
```

Pivot 계산은 규칙 문서대로 한다.

```text
pivotX = forwardFootCenterX / 512
pivotY = (512 - forwardFootContactPixelY) / 512
```

이미지 좌표 기록 방식은 좌상단 원점 기준으로 통일한다. Master Design 단계에서 계산한 Pivot은 후속
모든 프레임에 동일하게 적용한다.

### 승인 조건

- 512×512 안에서 신체와 장비가 잘리지 않고 안전 여백이 있다.
- 전방 디딤발 접촉점이 명확하다.
- 반투명 번짐과 외부 그림자가 없다.
- 얼굴, 장비, 팔레트, 외곽선, 광원과 체격을 수치 또는 명시적 문장으로 고정했다.
- 100% 게임 표시 크기에서 실루엣과 주요 장비를 읽을 수 있다.

## 4. Gate C — Motion Brief

Master Design을 바꾸지 않고 애니메이션의 의도와 키포즈만 설계한다. 키포즈 참고 이미지를 생성할 수
있지만, 매번 새 캐릭터를 생성하지 않고 Master Design 편집으로 요청한다.

애니메이션마다 아래 양식을 작성한다.

```text
Animation ID:
Type: Idle | IdleVariant | LoopableBasic | HitHoldRecovery
게임플레이 목적:
예상 프레임 범위:
예상 animationFps:
Loop 여부:
Actor Origin / 전방 디딤발 좌표:
키포즈 순서:
HitPoint 후보 포즈와 예상 인덱스:
Attack Movement 필요 여부와 방향(거리 수치는 Unity에서 튜닝):
움직여도 되는 부위:
고정해야 하는 부위:
실루엣 변화:
금지 변형:
PerfectPixel Animation name:
PerfectPixel Frames / FPS / Repeat:
PerfectPixel Motion description (6~18단어 한 줄):
PerfectPixel Facing direction 드롭다운 선택값:
재생성 Feedback 후보(출력 확인 뒤에만 작성):
```

### Player 기본 공격 (`LoopableBasic`)

- 구조는 `Windup → Strike → Recovery`로 설계한다.
- 연타 중에는 현재 CatKnight처럼 Recovery를 생략할 수 있지만, 이는 캐릭터별 Motion Brief에 명시한다.
- Strike 포즈는 실제 접촉이 한눈에 읽혀야 하며 `HitPoint` 후보가 된다.
- 캐릭터 전체가 앞으로 이동하는 연출은 스프라이트 안에서 Actor Origin을 옮기지 않는다.
- 프레임 수는 고정하지 않는다. PerfectPixel 결과를 본 뒤 최종 배열 길이로 확정한다.

### Enemy 피격 (`HitHoldRecovery`)

- 현재 런타임은 순차 피격 애니메이션이 아니라 Hold/Recovery 두 포즈를 고정 표시한다.
- 따라서 1차 몬스터는 Hold 포즈와 Recovery 포즈를 각각 명확히 설계한다.
- 여러 프레임의 연속 피격 연출을 전제로 만들지 않는다.

### 승인 조건

- 각 키포즈의 목적을 한 문장으로 설명할 수 있다.
- Master Design의 얼굴, 체격, 장비 치수와 팔레트를 바꾸지 않는다.
- 전방 디딤발 접촉 좌표와 Actor Origin이 유지된다.
- 타격 포즈와 복귀 포즈가 구분된다.
- 캐릭터 전체 이동과 프레임 내부 자세 변화가 분리되어 있다.

## 5. PerfectPixel 인계 패키지

Actor별로 다음을 함께 넘긴다.

```text
01_character-brief.md
02_master-design.png
03_master-measurements.md
04_motion-{animation}.md
05_keypose-{animation}-{name}.png   (선택)
```

위 파일 전체를 PerfectPixel 설명란에 붙여넣지 않는다. 실제 UI 입력은
`ProjectDocs/ArtPipeline/perfectpixel-input-and-review.md` 규칙에 따라 아래처럼 축약한다.

```text
자유 입력: 기준 이미지 + 이름 + 짧은 Character description
드롭다운/수치 선택: Art style + Frame cell size + Frames + FPS + Repeat + Facing direction
재생성 시 자유 입력: 짧은 Feedback 한 줄
```

`Facing direction`은 Animation description에 쓰는 문장이 아니라 UI 드롭다운이다. 기준 이미지가 올바른
방향이면 `Not set`으로 첫 Attempt를 만들고, 방향이 반전되는 경우에만 드롭다운 옵션을 바꾼 별도 Attempt를
비교한다. 방향을 긴 프롬프트로 보정하려 하지 않는다.

Character Brief와 Motion Brief의 상세 내용은 PerfectPixel 출력물을 판정하는 내부 기준이다.
PerfectPixel에서는 애니메이션 단위로 여러 프레임을 생성한다. 1프레임짜리 독립 생성을 기본 공정으로
사용하지 않는다. 출력은 초안이며, 이후 공정에서 다음과 같이 처리한다.

```text
짧은 UI 입력 기록 → 애니메이션 생성 → UI 점수/경고 기록 → 프레임 분리
→ 사용/수정/폐기 판정 → 접지점·비율·팔레트 보정
→ 부족한 중간 프레임 수작업 → 개별 PNG 납품 → Unity 연결 및 타이밍 검증
```

## 6. PerfectPixel 이후 절대 보존할 기준

- Master Design의 얼굴, 체격, 장비 형태와 비율
- Master Design에서 승인된 시점과 화면 진행 방향
- 동일 Actor의 팔레트, 외곽선과 광원
- 512×512 캔버스와 전방 디딤발 접촉 픽셀 좌표
- 모든 프레임에 동일한 Actor Origin/Pivot
- 최종 파일 경로:
  `Assets/Art/{Character|Enemy}/{Name}/{animation}/{Name}-{animation}-NN.png`

다음은 수정 후 사용 가능하다: 위치 1~2px 오차, 국소적인 외곽선/색상 오차, 장비 각도 오차.
다음은 원칙적으로 폐기한다: 머리/몸 비율 변화, 다른 장비로 변형, 방향 반전, 팔다리 오류,
캐릭터 정체성이 달라진 프레임.

## 6-1. 현재 시각 언어 상태 — `Low Companion v1` (확정)

- 최상위 기준은 실제 빌드에서 승인된 바바리안 최종 스프라이트다.
  기준 파일: `Assets/Art/ReferenceSheets/low-companion-v1-barbarian-reference.png`.
  새 Actor는 이 파일과 같은 게임 안에 나란히 놓였을 때 다른 해상도·다른 게임처럼 보이면 Reject한다.
- 방향은 화면 오른쪽의 친근한 3/4 전신. 체형은 약 2~2.5등신의 작은 데스크톱 컴패니언 비율이며,
  직업을 읽게 하는 장비는 한두 개의 큰 덩어리로 단순화한다.
- 최종 게임 표시에서 보이는 픽셀 덩어리는 바바리안과 같은 **굵은 저밀도(후가공 기준 약 3×3)**를 목표로 한다.
  이는 소스 논리 해상도 강제가 아니라, PerfectPixel 출력과 FireAlpaca 후보정 뒤의 납품 품질 기준이다.
- 외곽선은 진한 거의 검정색의 굵은 계단형 외곽선, 색은 적은 수의 명확한 색면과 소수의 하이라이트로 끝낸다.
  부드러운 그라데이션, 미세한 질감, 2×2 이하의 과도하게 촘촘한 디테일은 생산용 Master에서 사용하지 않는다.
- `class-lineup-03`과 LOW-B/LOW-C 시트는 직업·의상·실루엣 발상 참고용이다. 더 이상 캐릭터군의
  해상도·얼굴 묘사·마감 밀도를 결정하는 상위 기준이 아니다.
- 고밀도 Master 및 Unity `VisualRoot Scale 0.35` 적용은 비교 실험으로는 합격이었지만, 프레임 수작업
  보정 비용이 높으므로 현재 1차 생산 규격에는 채택하지 않는다. 다시 도입하려면 여러 Actor의 Idle/Attack
  후가공 비용까지 포함한 별도 프로토타입 승인이 필요하다.

## 7. 1차 생산 순서

한 번에 여러 Actor를 만들지 않고 아래 순서로 버티컬 슬라이스를 완주한다.

1. Player 1종 Character Brief 승인
2. Player Master Design 및 측정값 승인
3. Player Idle, Idle Variant, Tier 1, Tier 2 Motion Brief 승인
4. Enemy 1종 Character Brief와 Master Design 승인
5. Enemy Idle, Hit Hold/Recovery Motion Brief 승인
6. PerfectPixel 생성·후보정·Unity 연결
7. 50/100/150 Stage 배율, 접지, HitPoint, ImpactPoint 검증 후 다음 Actor 착수

현재 규칙상 `CommittedSkill`은 미구현이므로, 1차 생산의 Tier 2를 독립적인 스킬 상태머신 전제로
설계하지 않는다. 현행 콤보 풀에서 재생 가능한 공격 리소스로 설계한다.
