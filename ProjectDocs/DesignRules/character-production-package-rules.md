# 캐릭터·몬스터 제작 패키지 규칙

> 문서 상태: Production Package Rule v1.0 / 2026-08-10
>
> 적용 범위: 신규 캐릭터·몬스터 설정, Master 생성, 애니메이션 추가, 기존 리소스 재설계와 V1→V2 전환

이 문서는 캐릭터와 몬스터의 설정, 이미지 생성 입력, 승인 Master, 애니메이션 규칙과 제작 이력을 하나의
Actor 폴더에서 계속 찾을 수 있게 만드는 영구 관리 규칙이다.

채팅, 이미지 생성 세션 또는 기억에만 남은 설정은 프로젝트 설정으로 인정하지 않는다. 새로운 Actor를 만들거나
기존 Actor의 디자인·애니메이션을 변경할 때는 반드시 이 규칙의 **제작 패키지**를 생성하거나 갱신한다.

## 1. 핵심 원칙

1. **Actor 하나당 제작 패키지 폴더 하나를 사용한다.**
2. 이미지 생성 전에 Brief를 작성하고, 이미지 승인 후 Master 측정값을 기록한다.
3. 애니메이션마다 별도의 Motion Brief를 작성한다.
4. PerfectPixel과 생성형 이미지 도구의 실제 입력값과 결과 판정을 기록한다.
5. 사용자 승인, Reject와 보류 결정은 해당 턴 안에 패키지 문서에 반영한다.
6. 정식 설정과 AI 제안값을 같은 상태로 취급하지 않는다.
7. 기존 파일을 무기명으로 덮어쓰지 않고 버전과 Attempt를 남긴다.
8. 세계관, 캐릭터 설정, 이미지, 애니메이션과 Unity 납품값이 서로 다른 폴더에 흩어져도 패키지 인덱스에서
   모두 찾을 수 있어야 한다.

## 2. 패키지 경로

### 플레이어 캐릭터

```text
ProjectDocs/ArtPipeline/Characters/{ActorId}/
```

### 몬스터·적대 Actor

```text
ProjectDocs/ArtPipeline/Enemies/{ActorId}/
```

`ActorId`는 영문 ASCII 식별자를 사용하며 테이블·리소스 ID와 가능한 한 일치시킨다. 런타임 폴더명이나 과거
이름이 다르면 폴더를 중복 생성하지 않고 `aliases`와 기존 경로를 패키지 인덱스에 기록한다.

예시:

```text
ProjectDocs/ArtPipeline/Characters/StagGroveWarden/
ProjectDocs/ArtPipeline/Enemies/VenomCultist/
```

## 3. 필수 패키지 구성

```text
{ActorId}/
├── 00_package-index.md
├── 01_character-brief.md        # Player
│   또는 01_monster-brief.md     # Enemy
├── 02_perfectpixel-input.md
├── 03_master-measurements.md
├── 04_motion-{motion-id}.md
├── 04_motion-{other-motion-id}.md
└── Prototypes/
    └── {attempt-or-version}/
        ├── README.md
        └── 후보 이미지와 비교 자료
```

### 항상 필요한 파일

| 파일 | 역할 |
|---|---|
| `00_package-index.md` | 현재 상태, 정식 경로, 승인값, 보류 사항과 다음 작업을 한 화면에서 확인하는 진입점 |
| `01_character-brief.md` 또는 `01_monster-brief.md` | 세계관, 정체성, 역할, 체형, 실루엣, 장비, 팔레트, 고정·금지 요소 |
| `02_perfectpixel-input.md` | 실제 UI에 복사할 Character/Animation 입력과 Attempt 회고표 |
| `03_master-measurements.md` | 승인 Master 파일, 캔버스, 점유 영역, 접지점, Pivot, 팔레트와 비교값 |
| `04_motion-{id}.md` | 해당 모션의 목적, 프레임·FPS, 키포즈, 고정 요소, 입력값과 합격 기준 |

### 조건부 파일

- PerfectPixel을 사용하지 않는 Actor도 `02_perfectpixel-input.md`를 삭제하지 않는다. 대신 `Not used`와 대체
  제작 경로를 기록한다.
- 아직 Master가 없으면 `03_master-measurements.md`에 `Pending` 상태와 목표값만 기록할 수 있다.
- 아직 애니메이션이 없더라도 최소한 기본 Idle의 Motion Brief는 Master 승인 직후 생성한다.
- 공격, 피격, 이벤트 Idle과 특수 모션은 실제 제작 대상이 될 때만 파일을 추가한다.

## 4. `00_package-index.md` 필수 항목

패키지에 들어왔을 때 다른 문서를 모두 열지 않고도 현재 상태를 알 수 있어야 한다.

```text
Actor ID:
표시명:
Actor Type: Player | Enemy
World ID:
Aliases:
Production Profile:
Package Status:
Approved Master:
Current PerfectPixel Attempt:
Available Motion Briefs:
Unity Resource Status:
User-approved decisions:
AI proposals not yet approved:
Known conflicts / gaps:
Next action:
```

`Next action`은 하나만 적는다. 여러 후속 작업을 한꺼번에 시작하지 않는다.

## 5. 패키지 상태

다음 상태명을 사용한다.

| 상태 | 의미 |
|---|---|
| `Concept` | 후보 아이디어만 존재 |
| `Brief Draft` | 필수 설정 작성 중 |
| `Brief Approved` | 설정과 디자인 제약 승인 완료 |
| `Master Candidate` | Master 후보 생성 및 검토 중 |
| `Master Approved` | 정식 Master와 측정값 잠금 완료 |
| `Motion Ready` | 하나 이상의 Motion Brief와 생성 입력 준비 완료 |
| `Output Review` | PerfectPixel 또는 외부 생성 결과 검수 중 |
| `Unity Ready` | 납품 프레임·임포트·런타임 검증 완료 |
| `Hold` | 보류 이유와 재개 조건이 기록된 상태 |
| `Deprecated` | 대체 Actor·리소스와 폐기 근거가 기록된 상태 |

상태를 건너뛸 수는 있지만, 앞 단계의 필수 문서와 승인값을 생략할 수는 없다.

## 6. 사용자 확정값과 AI 제안값

### 사용자 승인이 필요한 항목

- Actor의 핵심 콘셉트와 세계관 소속
- 종족, 직업과 전투 역할을 크게 바꾸는 선택
- 얼굴, 체형, 무기와 대표 복식의 정체성
- 최종 Master 이미지
- 기존 승인 Master를 무효화하는 재설계
- 캐릭터를 폐기하거나 다른 Actor로 통합하는 결정

### AI가 자동으로 채울 수 있는 항목

- 빈 설정을 핵심 콘셉트와 세계관 규칙에 맞게 확장
- Brief의 문서 구조와 생성 프롬프트
- 장비 비율, 실루엣 검수 기준과 금지 요소 초안
- Motion Brief, PerfectPixel 입력과 회고표
- 파일명, 폴더 구조와 측정값 기록
- 기존 리소스·문서의 충돌 조사

AI 제안값은 사용자가 승인하기 전까지 `Draft`, `Candidate`, `Proposed` 중 하나로 표시한다. 사용자 발언을
AI 제안처럼 낮추거나, AI가 만든 세부 설정을 사용자 확정값처럼 올리지 않는다.

## 7. 승인 게이트

### Gate A — Brief

- 이미지 생성 전에 세계, 종족, 역할, 실루엣, 장비, 팔레트와 금지 요소를 채운다.
- 기존 Actor와의 역할·외형 중복을 확인한다.
- 사용자가 직접 정하지 않은 값은 AI 제안으로 분리한다.

### Gate B — Master

- 후보 중 하나를 사용자가 승인해야 정식 Master로 승격한다.
- 승인 파일을 버전명으로 복사하고 `03_master-measurements.md`를 작성한다.
- 승인 이후 애니메이션마다 얼굴, 체형과 장비를 다시 해석하지 않는다.

### Gate C — Motion

- 모션마다 목적, 프레임 수, FPS, 키포즈, 움직일 부위와 고정할 부위를 기록한다.
- 실제 도구에 넣을 짧은 입력문과 사람이 판정할 상세 기준을 분리한다.

### Gate D — Output

- 점수만으로 합격시키지 않고 정체성, 크기, 접지, 장비와 프레임 일관성을 검수한다.
- 수정 가능한 프레임과 Reject할 Attempt를 구분한다.
- 재생성은 기존 결과 덮어쓰기가 아니라 새 Attempt로 기록한다.

### Gate E — Unity

- 최종 캔버스, PPU, Pivot, Filter, Compression과 프레임 순서를 확인한다.
- Motion Profile, CharacterDefinition 또는 Monster 테이블 연결 상태를 기록한다.
- 실제 런타임 검증이 끝나기 전에는 `Unity Ready`로 표시하지 않는다.

## 8. 이미지와 리소스 경로

### 승인 Master

```text
Assets/Art/Character/{ActorId}/master/{ActorId}-master-vNN.png
Assets/Art/Enemy/{ActorId}/master/{ActorId}-master-vNN.png
```

### 최종 애니메이션 프레임

```text
Assets/Art/Character/{ActorId}/{motion}/{ActorId}-{motion}-NN.png
Assets/Art/Enemy/{ActorId}/{motion}/{ActorId}-{motion}-NN.png
```

### 후보와 실패 기록

```text
ProjectDocs/ArtPipeline/{Characters|Enemies}/{ActorId}/Prototypes/{version-or-attempt}/
```

- 승인되지 않은 후보를 `Assets/.../master`에 넣지 않는다.
- 크로마키 원본, 배경 제거 중간본과 비교 이미지는 `Prototypes`에 둔다.
- 승인 Master는 `v1`, `v2`처럼 버전명으로 보존하고 무기명 `final.png`를 사용하지 않는다.
- Reject 파일은 삭제가 필요한 특별한 이유가 없으면 제작 근거로 보존하되 `README.md`에 사용 금지를 표시한다.

## 9. Production Profile 기록

V1, V2 또는 실험 프로필을 파일 위치만 보고 추측하지 않는다. `00_package-index.md`, Brief와 Master 측정 문서에
사용 프로필과 아래 값을 기록한다.

```text
Profile name:
Frame canvas:
PPU:
Pivot rule or candidate:
Filter / Compression:
Pixel style reference:
Palette target:
Relative scale reference:
```

기존 공통 규칙과 다른 실험값은 다른 Actor 전체에 자동 전파하지 않는다. 검증된 프로필을 공통 규격으로
승격할 때는 `character-sprite-and-animator-rules.md`와 관련 Preset·에디터 기본값을 함께 갱신한다.

## 10. 생성 도구 기록

ImageGen, PerfectPixel 또는 다른 생성 도구를 사용했다면 다음을 `Prototypes/.../README.md` 또는
`02_perfectpixel-input.md`에 기록한다.

```text
생성 도구 / 모드:
생성일:
입력 이미지와 역할:
최종 프롬프트 또는 UI 입력값:
원본 출력 파일:
후처리 과정:
측정값:
Pass / Fix / Reject 판정:
승인 또는 Reject 이유:
```

프롬프트 전문을 다시 찾기 위해 과거 채팅을 열어야 하는 상태를 만들지 않는다.

## 11. 설정 변경과 애니메이션 추가 규칙

### 설정을 변경할 때

1. `00_package-index.md`에 변경 이유와 영향 범위를 기록한다.
2. Brief의 변경값을 갱신한다.
3. 기존 Master·Motion과 충돌하는지 확인한다.
4. Master 재생성이 필요하면 기존 승인본을 유지하고 새 버전 후보를 만든다.
5. 새 Master가 승인될 때 영향받는 Motion Brief와 PerfectPixel 입력을 함께 갱신한다.

### 애니메이션을 추가할 때

1. 기존 Master와 `01_*_brief.md`를 먼저 읽는다.
2. `04_motion-{id}.md`를 생성한다.
3. 움직일 부위와 고정할 부위를 명시한다.
4. `02_perfectpixel-input.md`에 해당 Animation 입력과 Attempt 회고표를 추가한다.
5. 출력 검수 후 `00_package-index.md`의 Available Motions와 다음 작업을 갱신한다.

이 절차를 따르면 새 세션에서도 캐릭터 설명을 다시 요청하거나 이전 이미지를 추측할 필요가 없다.

## 12. 기존 Actor 소급 적용

기존 Actor 패키지를 한 번에 모두 다시 작성할 필요는 없다. 해당 Actor를 다음에 수정하거나 애니메이션을 추가할
때 아래 최소 순서로 소급한다.

1. 현재 문서와 실제 Assets를 조사한다.
2. `00_package-index.md`를 만든다.
3. 존재하는 Brief를 정리하거나 누락된 Brief를 작성한다.
4. 현재 사용 중인 Master·프레임·Motion Profile을 evidence로 기록한다.
5. 모르는 값은 추측하지 않고 `Unknown` 또는 `Needs verification`으로 남긴다.
6. 이후 변경부터 정상 승인 게이트를 적용한다.

## 13. 완료 체크리스트

새 캐릭터·몬스터 설정 또는 리소스 작업을 종료하기 전에 확인한다.

- [ ] Actor 전용 제작 패키지 폴더가 존재한다.
- [ ] `00_package-index.md`에 현재 상태와 다음 작업이 기록돼 있다.
- [ ] Brief에 세계관, 정체성, 실루엣, 장비, 팔레트와 금지 요소가 있다.
- [ ] 승인 Master 경로와 버전이 하나로 식별된다.
- [ ] Master 측정값과 Production Profile이 기록돼 있다.
- [ ] 각 제작 모션에 Motion Brief가 있다.
- [ ] 실제 생성 입력과 Attempt 결과를 다시 찾을 수 있다.
- [ ] 사용자 승인과 AI 제안이 구분돼 있다.
- [ ] Reject 결과가 승인본처럼 참조되지 않는다.
- [ ] Assets 경로, 문서 경로와 런타임 ID의 불일치가 aliases 또는 gap으로 기록돼 있다.
- [ ] 다음 작업이 하나로 정리돼 있다.

## 14. 기준 예시

첫 적용 예시는 다음 패키지다.

```text
ProjectDocs/ArtPipeline/Characters/StagGroveWarden/
```

이후 새로운 Actor는 이 구조를 복사하되 내용과 Production Profile은 해당 Actor 기준으로 다시 작성한다.
