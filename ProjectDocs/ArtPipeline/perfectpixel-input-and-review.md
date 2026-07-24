# PerfectPixel 입력 및 출력 점검 규칙

> 기준 UI: 캐릭터 이미지 업로드 방식의 PerfectPixel Studio
> 목적: 내부 제작 명세를 PerfectPixel의 실제 입력 필드에 맞는 짧은 값으로 변환하고, 출력 결과를 기록해 다음 생성의 정확도를 높인다.

## 1. 내부 명세와 PerfectPixel 입력을 분리한다

`Character Brief`와 `Motion Brief`는 사람이 디자인 의도를 보존하고 출력물을 판정하기 위한 내부 문서다.
이 문서의 모든 내용을 PerfectPixel 설명란에 붙여넣지 않는다.

PerfectPixel에서 제작자가 직접 작성하는 **자유 텍스트**는 아래 세 필드뿐이다.

1. `Character description`: 움직임을 제외한 캐릭터 고정 외형
2. `Motion description`: 이번 애니메이션에서 일어나는 핵심 동작 한 줄
3. `Regenerate with feedback`: 이미 생성된 결과에서 바꿀 점 1~2개

`Character name`은 작업 ID를 입력하는 필드이지만, 창작 지시문으로 취급하지 않는다.
방향, Frames, FPS, Repeat, Art style, Frame cell size는 문장으로 전달하는 프롬프트가 아니라 PerfectPixel UI의
드롭다운 또는 수치 선택값이다.

프레임별 포즈, Pivot, 파일 경로, Unity 설정, 세부 금지 조건은 PerfectPixel 입력문이 아니라 출력 검수
기준으로 사용한다.

## 2. Character & Style 입력

| UI 필드 | 입력 원칙 |
|---|---|
| Upload image | 승인된 캐릭터 기준 이미지 1장. 논리 해상도로 축소하지 않은 선명한 컨셉 원본을 사용하고 최종 디자인과 장비가 모두 보여야 함 |
| Character name | 영문 작업 ID 하나. 공백과 수식 문장 없이 일관되게 사용 |
| Character description | 1~2개의 짧은 영문 문장. 외형·장비·고정 정체성만 기록 |
| Art style | `Pixel Art` |
| Frame cell size | 생성 시 실제 선택값을 회고표에 기록. Unity 최종 납품 512×512 규격과 구분 |

### Character description에 넣을 것

- 종족/성별과 대표 체격
- 머리, 피부, 얼굴의 고정 특징
- 핵심 복장과 장비
- 무기 종류와 개수
- 모든 프레임에서 유지되어야 할 정체성

### Character description에서 뺄 것

- `frame 00`, `frame 01` 같은 프레임별 지시
- Idle, 공격, 피격 같은 동작 설명
- Pivot, PPU, Unity 경로와 파일명
- 캔버스 정렬과 후가공 절차
- `screen-right`, `screen-left`, `facing right` 같은 방향 지시문
- 긴 금지 목록과 품질 판정 문장

설명은 쉼표로 키워드를 끝없이 나열하지 않는다. 입력 이미지에서 이미 명확히 보이는 세부 장식은 반복하지
않고, 생성 중 사라지기 쉬운 정체성만 남긴다.

## 3. Animation 입력

| UI 필드 | 입력 원칙 |
|---|---|
| Animation name | 짧은 영문 이름. 프로젝트 Animation ID와 대응 관계를 기록 |
| Frames | Motion Brief에서 정한 프레임 수 |
| FPS | Motion Brief의 목표 `animationFps` |
| Repeat | 기본 Idle은 `Loop`, 이벤트 Idle은 1회 재생 옵션 |
| Motion description | 짧은 영문 한 줄. 주 동작 → 보존할 핵심 → 복귀 순서 |
| Facing direction | **드롭다운 선택값**. 기준 이미지가 이미 올바른 방향이면 `Not set`을 기준 Attempt로 먼저 시험한다. 결과가 반전될 때만 해당 화면 방향 옵션을 별도 Attempt로 시험하고, 출력 결과를 기록한다. |

### Motion description 작성 공식

```text
[핵심 동작]; [반드시 유지할 대상]; [필요하면 종료 상태]
```

권장 길이는 약 6~18단어다. 한 문장 안에 프레임별 지시, 카메라, Pivot, 팔레트와 모든 금지 조건을
넣지 않는다.

좋은 예:

```text
deep breathing through chest and abdomen; minimal shoulder movement; keep feet and both axes steady
```

```text
tap chest once with his right axe hand; keep both axes held; return to idle
```

피해야 할 예:

```text
Frame 00 exactly matches... In frame 01... In frame 02... Preserve the 512x512 canvas...
```

PerfectPixel이 프레임별 연출을 정확하게 따르지 못하면 설명을 길게 늘리기보다 다음 순서로 조정한다.

1. 동작을 더 짧고 일반적인 동사로 교체
2. 한 번에 한 제약만 추가
3. 프레임 수 또는 FPS 조정
4. 그래도 실패하면 생성 결과에서 쓸 프레임을 선별하고 후가공

## 4. Regenerate with feedback

결과 화면의 Feedback은 특정 프레임을 직접 수정하는 필드로 보지 않는다. 기존 결과와 추가 피드백을
참고해 선택된 애니메이션 전체를 다시 생성하는 기능으로 취급한다.

- 원래 Motion description을 장황하게 다시 쓰지 않는다.
- 현재 결과에서 바꿀 점만 짧게 적는다.
- 한 번에 1~2개의 관련된 수정만 요청한다.
- 재생성 결과는 기존 Attempt의 수정본이 아니라 새 Attempt로 저장한다.
- 제외할 프레임을 선택했다면 선택 상태도 기록한다.
- Quality가 올라도 정체성, 축척과 접지점 검수를 다시 수행한다.
- `do not resize`, `keep identical scale` 같은 문장은 생성 내용에는 영향을 줄 수 있지만 후단 content-fit을
  제어한다고 가정하지 않는다.
- 축척 고정 피드백 때문에 무기 생략, 한 손 중첩 또는 포즈 단순화가 발생하면 결과 전체를 Reject한다.
- Feedback은 방향/축척/프레임 정렬을 강제하는 시스템 명령으로 취급하지 않는다. 생성 내용의 수정 제안일 뿐이며,
  content-fit 또는 방향 보정을 보장하지 않는다.

### Feedback 작성 공식

```text
[현재 결과에서 바꿀 한 가지]; keep [반드시 남길 정체성 또는 동작]
```

예:

```text
reduce the shoulder lift; keep the head, axes, and planted feet unchanged
```

```text
make the staff thrust clearer; keep the robe, hat, and body pose consistent
```

예:

```text
reduce shoulder lift; use gentle chest and abdomen expansion; keep head and both axes steady
```

## 5. PerfectPixel 출력 회고표

각 생성 시도마다 실제 입력값과 결과를 함께 남긴다. 같은 시도에서 여러 값을 동시에 바꾸지 않는다.

```text
Date / Attempt ID:
Actor:
Base image version:

[Character & Style]
Character name:
Character description:
Art style:
Frame cell size:

[Animation]
Animation name:
Frames:
FPS:
Repeat:
Motion description:
Facing direction dropdown selection:

[PerfectPixel result]
Quality score:
UI warnings by frame:
Identity consistency: Pass / Fix / Reject
Motion readability: Pass / Fix / Reject
Weapon count and shape: Pass / Fix / Reject
Foot/origin stability: Pass / Fix / Reject
Crop and safe margin: Pass / Fix / Reject
Usable frames:
Frames requiring fixes:
Rejected frames:

[Decision]
Keep current input / Retry
One field to change next:
Reason:
```

UI가 표시하는 Quality 점수와 `다른 프레임보다 콘텐츠가 적음`, `가장자리에 닿아 잘림` 같은 경고를
반드시 기록한다. 점수만으로 합격시키지 않고 실제 캐릭터 정체성, 동작 가독성, 무기와 접지점을 함께 본다.

## 6. 출력 검증 후 문서에 반영할 것

- 자주 사라지는 외형 요소는 `Character description`에 추가한다.
- 잘못 해석되는 동작만 `Motion description`의 동사를 교체한다.
- 설명이 없어도 안정적으로 유지되는 요소는 입력문에서 제거한다.
- UI의 Facing direction이 3/4 시점을 무너뜨리면 해당 Actor는 `Not set`을 고정한다.
- 방향 일관성은 먼저 Master Design에서 해결한다. `Facing direction` 드롭다운과 Feedback은 보조 실험값이며,
  기준 이미지와 반대 방향으로 나온 결과를 프롬프트만으로 구제하려 하지 않는다.
- Frame cell size별 결과 차이와 512×512 최종 변환 방식을 실제 출력으로 확정한다.
- 재생감 문제는 PerfectPixel 생성 결과를 Unity에서 확인한 뒤 FPS 또는 프레임 수 규칙에 반영한다.

## 7. 확인된 PerfectPixel 동작 — 바바리안 실험

- 입력 이미지의 투명 패딩은 출력 Actor 크기 제어 수단으로 신뢰할 수 없다.
- PerfectPixel은 각 프레임의 전체 콘텐츠 영역을 Frame cell size에 맞춰 개별 정규화하는 경향이 있다.
- 무기나 팔다리가 넓게 뻗은 프레임은 Actor 전체가 작아지고, 콘텐츠 폭이 좁은 프레임은 커질 수 있다.
- 발바닥 하단을 비슷하게 유지하면서 머리와 몸통 높이가 변할 수 있으므로 Pivot 고정만으로 해결되지 않는다.
- 256 Frame cell size에서도 content-fit 축척 변화가 발생했다.
- 256 출력은 1×1 픽셀 수준으로 표현되어 CatKnight의 약 3×3 픽셀 밀도와 맞지 않았다.
- 256 출력은 PPU 200에서 CatKnight와 거의 동일한 게임 표시 크기를 보였으므로 크기 프로토타입으로는 유효하다.
- 현재 우선 생산값은 512 Frame cell size이며, 프레임 간 신체 축척은 후가공 검수 대상으로 둔다.
- 축척 판정에는 무기를 포함한 전체 알파 bbox가 아니라 머리끝~발바닥과 몸통 랜드마크를 사용한다.
- PerfectPixel은 Master의 **논리 실루엣 크기를 기준으로** 출력한다. 따라서 Master 투입 전 목표 높이를 실제로
  측정한다. 가장 선명한 원본을 입력하되, 생성 프롬프트의 높이 지시만으로 크기가 맞았다고 판단하지 않는다.
- 캐릭터 간 상대 체격은 입력 패딩으로 통제하지 않는다. 출력 후 캐릭터별 표시 배율을 별도 기록하고 Unity 화면에서 확정한다.
- 최종 생산은 지배적인 3×3 픽셀 덩어리를 기준으로 논리 실루엣 높이를 측정한다. 일반 Actor는 70px 내외
  (65~75px), 바바리안은 승인된 대형 예외 79px, BlackCatMage는 모자 포함 예외 86px이다. 이 높이는
  크기 규격일 뿐 내부 색면·표정·의상 디테일의 상한이 아니다. BlackCatMage보다 표현 품질이 낮아도 Reject한다.
- Scale-lock 피드백 실험에서 양손 도끼가 모두 등장한 프레임만 약 10.3% 축소됐다. 프롬프트와 Feedback만으로
  절대 축척을 보장하는 접근은 중단하고 외부 후가공 단계에서 통제한다.
- 현재 생산 규격은 실제 빌드에서 승인된 바바리안의 `Low Companion v1`이다. 새 Actor의 PerfectPixel 출력은
  바바리안과 비교해 과도하게 고밀도·미세 묘사로 보이지 않아야 하며, 최종 후가공에서 약 3×3의 굵은 픽셀
  덩어리를 목표로 한다.
- 고해상도 Master는 화면상 매력과 PerfectPixel 해석에서 장점이 있었으나, 프레임별 수작업 비용이 높아
  현재 생산 공정에서는 사용하지 않는다. `class-lineup-03`은 실루엣과 직업 발상 참고로만 보관한다.
