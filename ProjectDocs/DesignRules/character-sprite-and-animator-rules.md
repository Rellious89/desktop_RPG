# 캐릭터/몬스터 스프라이트 & Animator 규칙 (초안)

CatKnight(플레이어)와 Scarecrow(몬스터) 둘을 만들면서 실제로 Inspector에 들어간 값들을 놓고,
**모든 캐릭터가 똑같이 따라야 하는 것(규칙)**과 **캐릭터마다 달라도 되는 것(튜닝)**을 나눠봤다.

지금은 규칙을 모으는 단계다. 정확한 최종값을 정하는 문서가 아니라, "대략 이런 축으로 규칙과
튜닝을 나눈다"는 정도의 초안이다. 다음 캐릭터/애니메이션을 만들 때 실제로 부딪혀보면서
규칙이 더 정교해지거나, 지금 규칙이라고 적어둔 것도 튜닝 쪽으로 옮겨질 수 있다.

## 공통 규칙 (Common Rules)

캐릭터마다 달라지면 시스템이 깨지거나, 일관성이 무너지는 값들. 새 캐릭터를 만들 때 그대로 따른다.

| 항목 | 규칙 | 근거 |
|---|---|---|
| 캔버스 크기 | 512 x 512 px / 프레임 | 지금까지 CatKnight, Scarecrow 모두 동일 |
| Pixels Per Unit | 200 | 캐릭터마다 다르면 씬 안에서 서로 상대적 크기가 어긋난다 |
| 텍스처 임포트 | Sprite(Single), Point Filter(No Compression), Alpha Is Transparency 켬 | 픽셀아트가 흐려지지 않게, 배경 투명 유지 |
| 프레임 저장 방식 | **아틀라스(가로 1행 스트립)가 아니라 프레임마다 개별 PNG 파일** | 아래 "아틀라스 구조 정정" 참고 |
| 프레임 참조 방식 | `Sprite[] frames` 배열로 Inspector에서 직접 받는다. 런타임에 `Rect`를 계산해서 자르지 않는다 | 개별 스프라이트를 그대로 쓰면 프레임 수는 `frames.Length`가 곧 정답이라 별도 계산/입력이 필요 없다 |
| Pivot / PPU | 각 프레임 PNG의 임포트 설정(`spritePivot`, `spritePixelsToUnits`)에 직접 박아둔다. Animator 컴포넌트에는 pivot/PPU 필드가 없다 | 스프라이트 자체가 이미 올바른 좌표계를 갖고 있으면 코드가 더 단순해진다 |
| Pivot 기준 발 | **앞쪽 디딤발(전방 디딤발)** | 좌우 두 발 중 화면 앞쪽(진행 방향 쪽)에 놓인 디딤발의 지면 접촉점을 기준으로 통일한다 |
| Pivot 계산 방식 | x = 전방 디딤발의 수평 중심(캔릭터마다 0.5에서 벗어날 수 있음) / y = (캔버스 높이 − 전방 디딤발 지면 접촉점 픽셀y) ÷ 캔버스 높이 | 모든 프레임에서 전방 디딤발 위치가 같은 픽셀 좌표에 있어야 성립한다 — 수작업으로 프레임 간 발 위치를 맞춰야 함 |
| 캐릭터별 Pivot Y 차이 | **허용됨** — Pivot X는 전방 디딤발 수평 중심에 맞추지만, Pivot Y 정규화 값은 원본 이미지 내 발 위치에 따라 캐릭터마다 달라질 수 있다 | 체격/자세가 다른 캐릭터를 억지로 같은 Pivot Y로 맞추면 원화 비율이 왜곡된다 |
| 512×512 내 발 위치 | 모든 캐릭터/몬스터의 **전방 디딤발을 캔버스 중앙 하단의 동일 지점**에 배치한다 | 씬에 배치했을 때 캐릭터마다 접지선(바닥)이 어긋나지 않게 하기 위함 |
| 체격 차이에 따른 크기 표현 | 모든 캐릭터는 512×512 캔버스를 공통으로 쓰고, **CatKnight의 머리끝~발바닥 픽셀 높이를 기준값**으로 삼는다. 캐릭터별 체격 차이는 사전에 정의한 비율 범위 안에서 원본 이미지(캐릭터가 캔버스 내에서 차지하는 픽셀 크기) 크기로 표현한다. Transform Scale은 기본적으로 **1**을 유지한다 | 씬에서 Transform Scale로 캐릭터 크기를 조정하면 캐릭터마다 실제 렌더 배율이 달라져 Pivot/충돌/이펙트 정렬이 어긋난다 — 체격 차이는 원화 단계에서 미리 반영한다 |
| Sprite Pivot의 의미 | Sprite Pivot은 **Actor Origin**(캐릭터를 대표하는 월드 좌표 기준점)을 나타낸다. **같은 애니메이션의 모든 프레임에서 Pivot을 고정**한다 | Pivot이 애니메이션 도중 흔들리면 캐릭터가 서 있는 자리 자체가 프레임마다 미세하게 밀리는 것처럼 보인다 |
| 자세 변화 / 공격 동작 표현 위치 | 몸의 자세 변화와 공격 동작은 **스프라이트 프레임 내부(원화)에서 표현**한다. 프레임별 Pivot을 바꿔서 전진/자세 변화를 흉내내지 않는다 | Pivot은 Actor Origin이라는 한 가지 역할만 맡아야 코드/충돌/이펙트 정렬이 예측 가능해진다. 전진 연출은 별도의 Attack Movement로 처리한다 — [attack-animation-rules.md](./attack-animation-rules.md) 참고 |
| 공격(Attack) 애니메이션 프레임 수 | **임의의 프레임 수를 가질 수 있다** (고정값 아님) | 프레임 수를 별도로 입력받지 않고 `frames.Length`를 그대로 쓴다. 값을 따로 입력받으면 실제 에셋과 어긋날 수 있어서 에셋 자체를 정답으로 둔다 |
| 피격(Hit) 애니메이션 프레임 수 | **임의의 프레임 수를 가질 수 있다** (고정값 아님) | 위와 동일한 이유로 `frames.Length`를 그대로 쓴다 |
| 파일/폴더 네이밍 | `Assets/Art/{Character|Enemy}/{이름}/{애니메이션}/{이름}-{애니메이션}-NN.png` | 애니메이션마다 하위 폴더, 프레임 번호는 2자리(00, 01, ...) |

> 처음엔 "공격/피격 = 3프레임 고정"을 규칙으로 적었었는데, 이건 실수였다. 지금 단계에서 딱 맞아떨어졌을 뿐이지
> 캐릭터/애니메이션마다 프레임 수가 달라질 수 있는 값이라 코드에 상수로 박아두면 안 되는 부분이었다.
> 프레임 수 자체를 Inspector 값으로 따로 입력받는 것도 피했다 — 실제 프레임 배열과 별도로 입력한 값이
> 동시에 존재하면 둘이 어긋날 수 있기 때문이다(아래 "튜닝 영역"의 `hitFrameIndex`/`holdFrame`/`recoveryFrame`처럼
> 프레임 "개수"가 아니라 "어느 인덱스를 쓸지"만 캐릭터별로 지정한다).

### 아틀라스 구조 정정: 가로 1행 스트립 → 개별 프레임 파일

한 번 더 정정한다. 처음엔 여러 프레임을 가로로 이어붙인 아틀라스 PNG 하나를 만들고, 런타임에
`Rect(i * frameWidth, 0, frameWidth, frameHeight)`로 잘라 쓰는 방식이었다. 초기 개발엔 편했지만 문제가 있었다:

- 에디터에서 이미지가 옆으로 길게 펼쳐져 있어서 눈으로 확인/작업하기 불편했다(원화 작업은 보통 프레임 1장 기준)
- 프레임 수가 늘어날수록 아틀라스 가로 폭이 늘어나서 Max Texture Size 제한에 쉽게 걸린다 (512px 기준 8프레임 = 정확히 4096px, 9프레임부터 초과)
- 애니메이션이 길어지거나 여러 모션을 한 아틀라스에 합치기 어려움
- 이미지 여백/패딩을 프레임별로 다르게 주기 어려움

그래서 **프레임마다 개별 PNG 파일로 저장하고, `Sprite[] frames` 배열로 Inspector에서 직접 참조**하는 방식으로 바꿨다.
원본 이미지가 1행이든 여러 행이든 런타임은 신경 쓸 필요가 없어졌고, 프레임 수 제한도 사실상 사라졌다(프레임 하나하나가
독립된 512×512 텍스처라 8프레임 제한 같은 게 없다). 에디터에서도 각 프레임을 낱장 이미지로 바로 볼 수 있다.

## 튜닝 영역 (개별 조정 가능)

캐릭터/몬스터마다 자유롭게 달라도 되는 값들. Inspector에서 캐릭터별로 따로 잡는다.

- 애니메이션별 `animationFps` (idle, idle 변형, attack 등) — 예: CatKnight idle 6fps, idle_b 4fps / Scarecrow idle 3fps. Idle 계열과 Attack 계열 모두 같은 이름(`animationFps`)을 쓴다(이전 이름: Idle `framesPerSecond`, Attack `stepFramesPerSecond`)
- 각 애니메이션의 프레임 수 — idle 변형은 3~6프레임처럼 캐릭터마다 제각각이고, attack/hit도 이제 고정이 아니다(위 공통 규칙 참고)
- 공격 애니메이션의 `hitFrameIndex` — 몇 번째 프레임에서 타격 판정(HitPoint)이 발생할지
- Hit 애니메이션의 `holdFrame`/`recoveryFrame` 인덱스 — 어느 프레임을 "유지 자세"/"복귀 자세"로 쓸지
- 복귀 유지 시간(`endFrameDuration`), 입력 유예 시간(`queueExpireTimeout`) — 세부 동작은 [attack-animation-rules.md](./attack-animation-rules.md) 참고
- 피격 유지/복귀 시간(`holdTimeout`, `recoveryDuration`), 흔들림 연출(`shakeStrength`/`shakeFrequency`/`shakeDecayDuration`)
- 전투 수치: `basicAttackPower`, `maxDurability`, `respawnDelay`
- 콤보 임계값/타임아웃, 색상(플래시 색 등)

## Scarecrow Hit 애니메이션 프레임 구조

- 기존 3프레임(`hit-00`/`01`/`02`) 중 `hit-00`은 Idle 프레임과 사실상 동일한 중복 이미지였고, 코드 흐름상으로도 `holdFrame`(과거 1번)으로 바로 점프해서 0번이 실제로 재생된 적이 없었다. 그래서 `hit-00`을 제거하고, 기존 `hit-01`(Hold 자세) → 새 `frames[0]`, 기존 `hit-02`(Recovery 자세) → 새 `frames[1]`로 재배치했다. `holdFrame`/`recoveryFrame` 설정값도 각각 0 / 1로 맞췄다.
- 지금 Hit 애니메이션은 **2포즈(Hold/Recovery) 방식**이다 — 프레임을 순차 재생(flipbook)하는 구조가 아니라, 피격 중에는 `holdFrame`을 고정 표시하고 피격이 끝나면 `recoveryFrame`을 고정 표시하는 상태 전환이다. `holdTimeout`/`recoveryDuration`로 각 포즈를 얼마나 유지할지만 제어하고, 프레임 사이를 보간하거나 여러 장을 순서대로 넘기지 않는다.
- 이번 변경은 프레임 배열 구성과 인덱스 값만 조정한 것이고, `holdTimeout`/`recoveryDuration` 값과 Hold→Recovery 전환 로직 자체는 그대로 유지했다 — 체감되는 타이밍은 이전과 동일하다.
- **향후 과제**: 만약 피격 시 여러 프레임에 걸쳐 흔들리거나 움직이는 연출이 필요한 Hit 에셋을 추가하게 되면, 지금의 "인덱스 2개 고정 표시" 구조로는 표현할 수 없다. 그때는 CatKnight 공격의 Windup/Recovery처럼 `animationFps` 기반으로 프레임 구간을 순차 재생하는 구조로 확장할지 검토해야 한다.

## 향후 리팩터링 후보

CatKnightIdleAnimator와 ScarecrowAnimator에는 다음 최소 프레임 재생 로직이 중복되어 있다.

- Sprite 배열
- animationFps
- 시간 누적
- 프레임 인덱스 전환
- SpriteRenderer 갱신
- 배열 및 인덱스 방어

현재는 Actor 사례가 2개뿐이므로 공통화하지 않는다.

세 번째 Actor 구현 시 동일 로직이 다시 복제되면
SpriteSequencePlayer 또는 유사한 재사용 컴포넌트 추출을 검토한다.

Attack/Hit/Defeated/Respawn 상태머신은 서로 다른 도메인 책임이므로
공통 베이스 상태머신으로 통합하지 않는다.

## 아직 규칙인지 아닌지 정하지 않은 것

- **좌우 반전(FlipX) 기준** — 원화가 기본적으로 오른쪽을 보게 그린다는 규칙을 정할지, 캐릭터마다 케이스 바이 케이스로 둘지 아직 안 정함
- **여러 캐릭터가 코드(Animator 클래스)를 공유할지** — 지금은 CatKnightIdleAnimator/ScarecrowAnimator가 구조는 비슷해도 완전히 별개 클래스다. 구체적인 판단 기준은 위 "향후 리팩터링 후보" 참고

## 참고: 현재 실제 값

| 항목 | CatKnight | Scarecrow |
|---|---|---|
| pivot (프레임 PNG 임포트 설정에 baked-in) | (0.5, 0.078125) | (0.5, 0.0703125) |
| spritePixelsToUnits (프레임 PNG 임포트 설정) | 200 | 200 |
| idle fps | 6 | 3 |
| idle 변형 | a(4f,6fps) / b(3f,4fps) / c(6f,6fps) | 없음 |
| 공격 프레임 수 / hitFrameIndex | 3프레임 / 1 | - |
| 피격 프레임 수 | - | 3프레임 |
| 공격/피격 관련 수치 | stepFPS 18, endFrameDuration 0.12, queueExpireTimeout 0.15, basicAttackPower 3 | holdTimeout 0.2, recoveryDuration 0.12, shakeStrength 0.04 |
