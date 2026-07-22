# BlackCatMage — PerfectPixel Input Sheet

> 이 문서는 PerfectPixel의 실제 입력 필드만 기록한다. 내부 디자인 규칙과 Unity 정렬 규칙은
> `01_character-brief.md` 및 공통 규칙 문서를 따른다.

## 실제 UI 입력 구조

### 자유 입력

| UI 필드 | 값/원칙 |
|---|---|
| Character name | `BlackCatMage` |
| Character description | 고정 외형만 한두 문장으로 작성. 방향·프레임별 동작·Pivot·캔버스 지시는 작성하지 않음. |
| Motion description | 이번 모션의 핵심 동작만 한 줄로 작성. |
| Regenerate with feedback | 결과를 본 뒤 바꿀 점 1~2개만 한 줄로 작성. |

### 드롭다운/수치 선택

| UI 필드 | 1차 Attempt 값 |
|---|---|
| Art style | `Pixel Art` |
| Frame cell size | `512 x 512 px` |
| Frames | 모션별 값 |
| FPS | 모션별 값 |
| Repeat | 모션별 값 |
| Facing direction | `Not set` baseline. Master 방향을 우선 보존하는지 확인한다. |

`Facing direction`은 문장 프롬프트가 아니다. 결과가 Master와 반대일 때에만 `Right`를 바꾼 별도 Attempt를
만들고, Character description이나 Motion description에 방향 보정 문장을 추가하지 않는다.

## Low Companion v1 Master 기준 Base Idle

### 기준 이미지

```text
Assets/Art/Character/BlackCatMage/master/BlackCatMage-master-low-v2.png
```

### Character description

```text
Cute anthropomorphic black cat wizard with charcoal-blue fur highlights, a warm brown robe and pointed hat, and one wooden staff with a small ruby-red gem. Keep the ears, tail, bright eyes, robe, hat, and single staff consistent.
```

### Animation 설정

```text
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Facing direction dropdown: Not set
```

### Motion description

```text
gentle breathing idle; slight robe, hat, tail, and staff movement; keep both feet planted
```

### Feedback 사용 예시

다음은 한 Attempt에서 하나만 사용한다.

```text
reduce the hat movement; keep the ears, staff, and body pose unchanged
```

```text
make the breathing more visible through the robe; keep the feet and staff steady
```

```text
restore the single staff and visible tail; keep the same idle motion
```

## Attempt 기록

| Attempt | Master | Facing dropdown | Frames/FPS | 결과 밀도 | 화면 크기 | 방향 | 판정 |
|---|---|---|---|---|---|---|---|
| 01 | Low Companion v1 | Not set | 4 / 6 | 미기록 | 미기록 | 미기록 | 대기 |
| 02 | Low Companion v1 | Right (필요 시) | 4 / 6 | 미기록 | 미기록 | 미기록 | 대기 |

Attempt 02는 Attempt 01이 Master 방향을 반전했을 때만 실행한다. Frame cell size, Master, Frames/FPS와
Facing dropdown을 한 번에 여러 개 바꾸지 않는다.
