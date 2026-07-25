# Claude UX/검증 리서치 — Character Editor MVP (Wave 1)

> 작성자: Claude worker (Wave 1, 병렬 리서치 트랙)
> 대응 문서: [`00_orca-character-editor-brief.md`](./00_orca-character-editor-brief.md)
> 병렬 트랙: Codex worker가 스키마 v1 / 저장소 데이터 감사 / 기술 스택을 독립적으로 조사한다. 이 문서는 화면·플로우·검증·비교·Export 카피를 다루며, 두 문서는 Synthesis Gate에서 통합된다.
> 범위: 리서치/설계 문서. 구현 파일은 수정하지 않았다.

---

## 0. 근거 자료 요약

브리프는 "저장소를 소스 오브 트루스로 취급하고 문서-자산 충돌을 보고하라"고 명시한다. 아래는 이 조사에서 실제로 읽은 저장소 근거와, 그로부터 도출한 충돌·격차 목록이다. Codex 워커의 전체 감사와 중복될 수 있으나, 이 문서의 화면/필드/검증 설계가 왜 이런 모양인지 설명하는 데 필요한 최소 근거만 남긴다.

### 0.1 제품/세계관 근거
- 프로덕트명: **KeyBuddy** (`ProjectDocs/DesignRules/world-setting-rules.md`). 데스크탑을 공통 허브로 삼는 이세계 용병단 컨셉.
- 등록된 연결 세계 3종 (브리프의 "Current worlds"와 일치):
  - `ANIMAL-LAND-01` / 애니멀랜드 (Animal Land)
  - `HUMAN-FANTASY-01` / 판타지아 (Fantasia)
  - `UNDEAD-WORLD-01` / 망자들의 세계 (World of the Dead)
- **문서 격차**: 애니멀랜드·판타지아 절에는 "현재 소속 캐릭터/몬스터" 표가 있지만 `UNDEAD-WORLD-01` 절에는 그 표가 없다. Specter는 완전한 Character Brief/측정 문서를 갖춘 실제 몬스터인데도 세계 문서의 로스터 표에는 등재돼 있지 않다.

### 0.2 액터 근거와 이름 충돌
| 문서상 ID | 실제 Unity 폴더(`Assets/Art/Character|Enemy`) | 세계 | 역할 | 상태 |
|---|---|---|---|---|
| `LeafGlaiveElf` (나뭇잎 글레이브 엘프) | **`ElfGuardian`** | Fantasia | 글레이브 전사 | Master |
| `BlackCatMage` | **`CatMage`** | Animal Land | 마법사 | Active |
| `CopperAxeBarbarian` | **`Barbarian`** | Fantasia(?) | 쌍도끼 바바리안 | Active |
| `CatKnight` | `CatKnight` | Animal Land | 기사 | Existing |
| `VenomCultist` | `VenomCultist` | Fantasia | 독단검 이교도 (몬스터) | Concept(문서) / 자산 존재 |
| `Specter` | `Specter` | World of the Dead | 말단 유령 몬스터 | 자산 완성, 세계 로스터 미등재 |

**충돌**: 브리프가 예시로 든 "Elfguardian"은 ArtPipeline 문서의 작업명(`LeafGlaiveElf`)이 아니라 **실제 Unity 자산 폴더명**(`ElfGuardian`)과 일치한다. 즉 디자인 문서의 정답과 엔지니어링 자산의 정답이 서로 다르다. 같은 패턴이 `BlackCatMage`→`CatMage`, `CopperAxeBarbarian`→`Barbarian`에서도 반복된다. Character Editor의 `actorId`는 이 둘 중 하나를 정본으로 선택해야 하며(권장: **자산 폴더명 = 엔지니어링 자산과 실제로 연결되는 값**), 문서상 작업명은 별칭(alias)으로만 보존해야 한다. (§12-1)

**추가 자산-문서 격차**: `Assets/Art/Enemy`에는 `HyenaRaider`, `Werewolf`도 idle/hit/master 폴더를 완전히 갖추고 있지만 `ProjectDocs/ArtPipeline/Enemies`에는 이 둘의 Monster Brief가 전혀 없다. `Scarecrow`도 `character-sprite-and-animator-rules.md`의 기준 몬스터로 반복 인용되지만 별도 Brief 파일이 없다. Character Editor가 "기존 자산을 불러와 시트를 소급 작성"하는 경로를 지원해야 하는 근거다. (§12-2)

### 0.3 "scale 과부하" 문제가 실제로 존재한다는 근거
- `LeafGlaiveElf`(ElfGuardian) Brief: `실제 스케일: 1.3`, `신체 논리 실루엣 높이: 약 91px`, `CatKnight 대비 키 비율: 1.3`, `체형 등급 및 승인 등신 비율: 대형 일반형 / 약 2.5등신` — 스케일·키비율·논리 실루엣 높이 세 값이 서로 다른 소스처럼 보이지만 실제로는 `91 / 70 ≈ 1.3`으로 수치적으로 종속돼 있다.
- `VenomCultist`: `실제 스케일: 1.0`, 논리 높이 `70px`.
- `Specter`: `실제 스케일: 0.8`, 논리 높이 `56px` (부유형, 발 접지 없음, Actor Origin은 바닥 투영점).
- **`CopperAxeBarbarian` Brief에는 `게임 내 상대 크기: 아직 미확정`이라고 명시돼 있다** — 즉 현재 Active 상태인 실제 캐릭터조차 "실제 스케일/종족 스케일"에 해당하는 값이 비어 있다. Character Editor의 필수 필드 검증이 곧바로 걸릴 실제 사례다. (§9, §12-3)
- `BlackCatMage` Brief에는 `실제 스케일` 항목 자체가 표에 없다(동일한 격차).
- `character-sprite-and-animator-rules.md`: 논리 실루엣 높이 일반 기준 65~75px, 승인된 예외로 바바리안 **79px**(근육질 대형), BlackCatMage **86px**(모자 포함), 허수아비 **63px 내외**(90% 크기) — "승인된 예외"가 이미 반복적으로 쓰이는 실제 운영 패턴이다. Character Editor의 "승인된 예외" 메커니즘은 이 관행을 그대로 데이터화하면 된다.
- Pivot 계산 공식은 `resource-production-workflow.md`에 고정돼 있다: `pivotX = forwardFootCenterX / 512`, `pivotY = (512 - forwardFootContactPixelY) / 512`. Specter처럼 발 접지가 없는 부유형 액터는 "바닥 투영점"을 대신 쓴다 — Pivot 규칙 자체가 액터 유형에 따라 분기해야 함을 보여준다.
- `CopperAxeBarbarian/06_canvas-occupancy-experiment.md`: 무기를 포함한 바운딩 박스가 캔버스 폭 84%/높이 73%를 차지하면 PerfectPixel이 콘텐츠 기준으로 액터 전체를 축소해버리는 문제가 실측으로 기록돼 있다. 목표치는 폭 55~60% / 높이 48~52%. 이 수치를 "큰 무기가 512 캔버스를 초과할 가능성" 경고의 임계값 근거로 사용한다.
- 무기 대 신장 비율 실측치: Barbarian 도끼 손잡이 신장의 38%·도끼날 폭 18% (자루 2개), ElfGuardian 글레이브 전체 길이 신장의 **1.2배**(상한 1.25배), VenomCultist 단검 신장의 0.3배 + 펜던트 가슴폭의 0.4배. 무기 종류마다 이미 "신장 대비 비율"로 관리되고 있다 — Weapon 필드의 `weaponSizeRatio`를 이 관행 그대로 스키마화한다.

### 0.4 브리프에는 있지만 저장소에는 없는 개념
- `weapon family`(무기 계열) 캐탈로그: 저장소 어디에도 무기를 "계열"로 분류한 명시적 카탈로그가 없다. 실제로는 액터별 자유 서술(예: "한손 전투도끼", "글레이브", "지팡이", "단검")로만 존재한다. Character Editor가 이 개념을 **처음 도입**하는 것이므로, 초기 카탈로그는 기존 5개 실제 액터(Barbarian=OneHandedAxeDual, ElfGuardian=Glaive, CatMage=Staff, VenomCultist=Dagger, CatKnight=미상)에서 역산해 시딩해야 한다.
- `production-layer policy: Character/Outfit + Weapon + Effect`: 현재 파이프라인은 프레임당 **단일 평면 PNG**이며 레이어 분리 생산 체계가 없다(`character-sprite-and-animator-rules.md`의 "프레임마다 개별 PNG"는 애니메이션 프레임 단위 분리이지, 캐릭터/무기/이펙트 레이어 분리가 아니다). 이 필드는 향후 레이어 파이프라인을 위한 **계획 메타데이터**로 다루고, MVP에서 실제 레이어드 파일 존재를 강제하지 않는 것을 권장한다(§12-5).
- `large-motion canvas`: `character-sprite-and-animator-rules.md`의 "아직 규칙인지 아닌지 정하지 않은 것"에 "공격 모션의 최종 캔버스 확장(768×512 또는 1024 계열)"이 명시적으로 미확정 실험 항목으로 남아 있다. Editor는 이 값을 "실험적, 기존 리소스와 혼용 금지"로 취급해야 한다(§9 규칙 R10).

이 근거들을 바탕으로 아래 화면/필드/검증을 설계했다.

---

## 1. 사용자 & 사용 맥락

- 단일 로컬 사용자(1인 개발), 논-테크니컬 폼 기반 UI가 요구됨 (브리프 "Standalone application constraints" 5번).
- 사용 빈도: 새 캐릭터/몬스터를 세계관에 편입할 때마다(현재 페이스: 액터 1~2종/스프린트), 그리고 기존 액터의 수치를 교정할 때(예: Barbarian의 비어 있는 스케일 값을 채우는 정리 작업).
- 최종 산출물은 사람이 아니라 **다음 단계 파이프라인**(Codex가 이미지/컨셉 아트를 생성)이 읽는다 → Markdown export의 "계산값·해석·경고·승인된 예외"가 실제로 다음 작업자에게 전달되는 계약서 역할을 한다. 이는 단순 사람이 읽는 리포트가 아니라 **인수인계 문서**로 설계해야 함을 의미한다.
- JSON 직접 편집 금지 요구(브리프) → 모든 필드는 폼 컨트롤(텍스트, 셀렉트, 슬라이더, 토글)로 노출돼야 하며, JSON은 항상 폼의 파생 산출물이다.

---

## 2. 정보 구조 & 화면 맵

```text
Home (World & Actor Library)
├─ World Template List → World Template Editor (Create/Edit)
├─ Actor List (필터: World, Type, Status) → Actor Sheet Editor
│    ├─ Section: Identity
│    ├─ Section: Body & Proportions   ← 상속/오버라이드 배지가 가장 밀집되는 구간
│    ├─ Section: Look
│    ├─ Section: Weapon & Equipment
│    ├─ Section: Production & Canvas
│    ├─ Validation Sidebar (모든 섹션에서 상시 노출, sticky)
│    ├─ Comparison Tab → Comparison View
│    └─ Export Tab → Export Preview (JSON/Markdown)
└─ Import (JSON 파일 열기) → 스키마 버전 확인 → Actor Sheet Editor로 로드
```

화면 수는 6개로 최소화한다: Home, World Template Editor, Actor Sheet Editor(섹션 탭 내비게이션), Comparison View, Export Preview, Import 확인 다이얼로그. Validation은 별도 화면이 아니라 **상시 사이드바**로 둔다 — 브리프가 "Export 전에 모든 누락/충돌/경고/승인된 예외를 보여주라"고 요구하지만, 실제 반복 사용 관점에서는 입력 도중 즉시 피드백을 주는 편이 "Export 직전 긴 에러 목록과 마주하는" 경험보다 마찰이 적다. Export 시점에는 사이드바 내용을 그대로 모달로 다시 확인시켜 최종 게이트 역할을 하게 한다(§4).

---

## 3. 사용자 플로우

### 3.1 신규 액터 생성 (골든 패스)

```text
Home
 → "New Actor" 클릭
 → World 선택 (필수, 목록 비어있으면 "New World Template" 유도)
     └ World 선택 즉시: Body/Look/Production 섹션에 World 기본값이
       "상속됨" 배지와 함께 미리 채워짐
 → Identity 섹션 입력 (ID, 표시 이름, Character/Monster, 종족, 성별,
   연령대, 역할, 한 문장 콘셉트)
     └ actorId 입력 시 실시간으로 "Assets/Art/{Character|Enemy}/{ID}"
       폴더 존재 여부를 확인해 안내(§9 R14)
 → Body & Proportions 섹션
     └ Stature/Species Scale 분리 입력, 각 필드 옆 World 기본값 대비
       상속/오버라이드 배지
 → Look 섹션 (헤어/눈/피부/의상/팔레트/불변 요소/금지 요소)
 → Weapon & Equipment 섹션
     └ World+종족 기준으로 허용된 Weapon Family만 기본 노출,
       비허용 계열 선택 시 즉시 Blocking 표시 + "예외 요청" 버튼
 → Production & Canvas 섹션 (대부분 World 상속, 잠김 표시)
 → [사이드바] Validation 상태 확인 → 남은 Blocking 있으면 해당 섹션으로
   점프 링크 클릭 → 수정 또는 승인된 예외 등록
 → Comparison 탭 → 같은 World의 기존 액터 선택 → 수치 비교 확인
 → Export 탭 → 최종 확인 모달(Blocking 0건 필수) → Export 실행
 → `{actorId}.character.json` + `.character.md` 저장 확인 화면(경로 표시)
```

### 3.2 기존 JSON 가져오기/재편집

```text
Home → Import → 파일 선택
 → schemaVersion 확인
     └ 버전 낮음: "이전 스키마 버전입니다. 자동 마이그레이션을 진행합니다"
       배너 + 마이그레이션 로그 표시 (§12-10)
     └ 버전 미상/손상: Import 차단, 원인 표시
 → 폼에 값 로드, 연결된 World Template 버전도 함께 로드
     └ World Template이 그 사이 갱신됐다면: 오버라이드되지 않은 필드는
       새 기본값 반영, 오버라이드된 필드는 액터 값 유지 + "World 기본값이
       바뀌었습니다" 안내 배지(§7)
 → Actor Sheet Editor로 진입 (3.1과 동일한 검증/비교/Export 경로 재사용)
```

### 3.3 저장소의 "고아 액터"(문서 없이 자산만 있는 경우) 소급 작성

```text
Home → Actor List에 실제 Assets 폴더는 있지만 시트가 없는 액터를
"미작성" 배지로 표시(선택 사항, Wave2 스코프 후보로 표시만 해둔다)
 → New Actor 플로우로 진입하되 actorId 자동완성이 기존 폴더명을 제안
```

---

## 4. 화면별 상세 설계

### 4.1 World Template Editor (텍스트 와이어프레임)

```text
┌ World Template ─────────────────────────────────────────────┐
│ World ID *      [ HUMAN-FANTASY-01            ] (생성 후 잠김) │
│ 표시 이름 *      [ 판타지아 (Fantasia)          ]              │
│ 세계 유형 *      ( Fantasy ▾ )                                │
│ 상태            ( Concept | Active | Hold ▾ )                 │
│──────────────────────────────────────────────────────────────│
│ 승인 시점 *      ( 약한 3/4 측면 ▾ )                            │
│ 기본 화면 진행 방향 * ( screen-right ▾ )                        │
│ 픽셀 스타일 태그  [ Low Companion v1            ]              │
│ 논리 픽셀 블록 크기 ( 3×3 ▾ )                                   │
│──────────────────────────────────────────────────────────────│
│ 기본 종족/비율 템플릿 *  ( 2.5등신 SD ▾ )  [+ 템플릿 추가]        │
│ 기본 논리 높이(px) *     [ 70 ]  (권장 65~75, 예외는 액터에서)     │
│ 기본 체형 등급 *        ( Normal ▾ )                           │
│──────────────────────────────────────────────────────────────│
│ 외곽선 색/두께 *  [ 어두운 계열 ▾ ] [ 1 논리픽셀 ]                │
│ 광원 방향 *       ( 화면 왼쪽 위 ▾ )                             │
│──────────────────────────────────────────────────────────────│
│ 기본 캔버스 *         512×512 (고정, MVP 공통 규칙)              │
│ 대형 모션 캔버스 정책  ( World 기본과 동일 ▾ ) ⚠ 실험적 옵션 있음  │
│ PPU *                200 (고정, 프로젝트 전역 상수)              │
│ Unity 표시 배율 기본값 * 1.0 (고정, 변경 시 액터별 승인된 예외 필요) │
└────────────────────────────────────────────────────────────────┘
```

- `*`는 필수. 캔버스/PPU/기본 Unity 배율은 저장소 근거상 "프로젝트 전역 상수"이므로 World 단위에서도 사실상 편집 불가로 고정하고, MVP는 표시만 한다(향후 세계별 상이한 규격이 실제로 필요해지기 전까지는 편집을 열지 않는다 — §12-6).

### 4.2 Actor Sheet Editor — Body & Proportions 섹션 (상속 배지 포함 예시)

```text
┌ Body & Proportions ───────────────────────────────────────────────┐
│ Stature Class *         ( Large-Normal ▾ )         [상속: World]   │
│ Target Logical Height * [ 91 ] px                  [재정의됨]  ↺   │
│   World 기본값: 70px · 허용범위 65~75px · 91px는 범위를 벗어남      │
│   → ⚠ 경고: "승인된 예외" 등록 필요 (사유 입력) [예외 등록]         │
│──────────────────────────────────────────────────────────────────│
│ Build Class *           ( Normal ▾ )               [상속: World]   │
│ Proportion Template *   ( 2.5-head SD (VenomCultist 계열) ▾ )      │
│                                                     [재정의됨]  ↺   │
│ Species Scale *         [ 1.3 ]                    [재정의됨]  ↺   │
│   계산값: Target Height(91) ÷ World 기본 Height(70) = 1.30         │
│   Species Scale(1.3)과 일치 → 정합성 OK                            │
│──────────────────────────────────────────────────────────────────│
│ Head Size    ( Normal ▾ )   Hand Size ( Normal ▾ )                 │
│ Foot Size    ( Normal ▾ )   Torso Width ( Normal ▾ )                │
│──────────────────────────────────────────────────────────────────│
│ ☐ Floating Actor (발 접지 없음 — 체크 시 Pivot 규칙 자동 전환)      │
│ Physical Traits  [ 뾰족귀 ] [ + 추가 ]                              │
└─────────────────────────────────────────────────────────────────────┘
```

배지 규칙(§7)과 계산값 표시(§8)를 이 섹션에 가장 밀도 높게 배치한 이유: 저장소 근거(§0.3)에서 스케일 관련 필드가 실제로 가장 자주 어긋난 이력이 있기 때문이다.

### 4.3 Comparison View (텍스트 와이어프레임)

```text
┌ Comparison ─────────────────────────────────────────────────────────┐
│ Draft: ElfGuardian (LeafGlaiveElf)   vs   Reference: [ VenomCultist ▾]│
│                     같은 World(HUMAN-FANTASY-01)의 Master/Active만 노출│
│───────────────────────────────────────────────────────────────────────│
│ 지표                  Draft        Reference     Δ         플래그      │
│ Logical Height (px)   91           70            +21 (+30%) ⚠ 대형 예외│
│ Species Scale         1.3          1.0           +0.3       —         │
│ Proportion Template   2.5-head SD  2.5-head SD   동일       ✅ 일치    │
│ Build Class           Normal       Normal        동일       ✅ 일치    │
│ Head / Hand / Foot    N/N/N        N/N/N         동일       ✅ 일치    │
│ Torso Width           Normal       Normal        동일       ✅ 일치    │
│ Weapon Occupancy(H)   ~53%(추정)   ~35%(추정)     +18%p      ⚠ 확인 권장│
│ Base Canvas           512×512      512×512       동일       ✅ 일치    │
│ Large-Motion Canvas   512×512      512×512       동일       ✅ 일치    │
└───────────────────────────────────────────────────────────────────────┘
```

- Reference 액터 드롭다운은 같은 `worldId`이면서 `status ∈ {Master, Active}`인 액터만 노출한다(Concept 단계 액터는 수치가 아직 불안정하므로 비교 기준에서 제외 — 근거: 저장소의 실제 Concept 액터들은 스케일값 자체가 비어 있는 경우가 많다, §0.3).
- Weapon Occupancy는 계산 필드(§7)이며, 근사치임을 항상 "(추정)"으로 명시한다 — 실측 PerfectPixel 결과와 다를 수 있음을 숨기지 않는다(브리프: "Silhouette overlay는 향후 기능, MVP는 텍스트/수치 비교로 충분").
- World에 비교 가능한 Reference가 하나도 없으면(신규 World) 이 화면은 빈 상태 UI로 "비교할 기존 액터가 없습니다 — Export를 계속 진행할 수 있습니다"를 보여주고 Export를 막지 않는다(§12-12).

### 4.4 Validation Sidebar / Export 확인 모달

```text
┌ Validation ───────────────────────────┐
│ ● Blocking (1)                        │
│   - Unity 표시 배율이 1.0이 아닙니다 (0.35)   → [섹션 이동]│
│ ▲ Warning (2)                          │
│   - Target Height가 World 허용범위를 벗어남 (91px)   [예외 등록됨 ✓]│
│   - Weapon 점유율 추정치가 높음 (~53%)         [바로가기]  │
│ ✓ 승인된 예외 (1)                        │
│   - Target Height 65~75px 범위 초과: "ElfGuardian은 대형 종족" │
└────────────────────────────────────────┘
```

Export 버튼은 **Blocking 카운트 0**일 때만 활성화된다. 활성화돼 있어도 클릭 시 위와 동일한 목록을 모달로 다시 보여주고 명시적 "Export 진행" 확인을 요구한다(브리프: "Export 전에 모든 누락/충돌/경고/승인된 예외를 보여줄 것").

---

## 5. 폼 필드 명세 (Exhaustive)

표기: **필수(R)** / **선택(O)** / **계산됨(C, 읽기 전용)** / **상속가능(I)**. 근거 열은 이 필드가 저장소のどこ에서 나왔는지 표시한다.

### 5.1 World Template

| 필드(스키마 키) | UI 라벨 | 타입 | 구분 | 근거/비고 |
|---|---|---|---|---|
| `worldId` | World ID | string | R, 생성 후 불변 | `world-setting-rules.md` §8 |
| `displayName` | 표시 이름(EN/KO) | localized string | R | 모든 World가 "정식 이름(영문)" 쌍으로 기록됨 |
| `worldType` | 세계 유형 | enum(Fantasy/Animal/Modern/SciFi/Alien/Other) | R | `world-setting-rules.md` §5.2 |
| `status` | 상태 | enum(Concept/Active/Hold) | R | 로스터 표의 "상태" 컬럼과 동일 어휘 |
| `approvedViewAngle` | 승인 시점 | enum/text | R | 모든 Brief가 "약한 3/4 측면" 반복 |
| `approvedFacing` | 기본 화면 진행 방향 | enum(screen-right/screen-left) | R | 현재 전원 screen-right로 고정 운용 중 |
| `pixelStyleTag` | 픽셀 스타일 | string | R | `Low Companion v1` |
| `logicalPixelBlockSize` | 논리 픽셀 블록 크기 | string | R | "약 3×3" |
| `defaultProportionTemplateId` | 기본 비율 템플릿 | ref | R | 예: "2.5-head SD" |
| `defaultLogicalHeightPx` | 기본 논리 높이(px) | number | R | 65~75 권장, World 기본값 |
| `defaultBuildClass` | 기본 체형 등급 | enum | R | Slender/Normal/Muscular/Heavy |
| `outlineColor` | 외곽선 색 | string | R | 액터별 오버라이드 흔함 |
| `outlineWidthLogicalPx` | 외곽선 두께(논리 px) | number | R, 기본 1 | 전 액터 공통값 |
| `lightDirection` | 광원 방향 | enum | R, 기본 "화면 왼쪽 위" | 전 액터 동일값 관찰됨 |
| `baseCanvas` | 기본 캔버스 | enum(512×512 고정) | R, 잠김 | 전역 상수 |
| `largeMotionCanvasPolicy` | 대형 모션 캔버스 정책 | enum(SameAsBase / Experimental-768x512 / Experimental-1024x512 / Experimental-1024x1024) | R, 기본 SameAsBase | 미확정 실험 항목, 선택 시 경고 |
| `ppu` | PPU | number(고정 200) | R, 잠김 | 전역 상수 |
| `defaultUnityVisualScale` | Unity 표시 배율 기본값 | number(고정 1.0) | R, 잠김 | 정책상 보정 금지 대상 |
| `weaponFamiliesAllowed` | 허용 무기 계열 목록 | list\<ref\> | R | World+종족 단위 화이트리스트, §0.4 신규 개념 |
| `schemaVersion` / `templateVersion` | — | number | C | 시스템 관리 |

### 5.2 Actor Sheet — Identity

| 필드 | UI 라벨 | 타입 | 구분 | 근거 |
|---|---|---|---|---|
| `actorId` | Actor ID | string, slug | R, 불변 | 자산 폴더명과 일치 권장(§0.2) |
| `displayName` | 표시 이름(EN/KO) | localized string | R | "VenomCultist / 독단검 이교도" 패턴 |
| `actorType` | 유형 | enum(Character/Monster) | R | Brief "Role: Player/Enemy" |
| `worldId` | 출신 세계 | ref(World) | R | 선택 즉시 상속 적용 |
| `species` | 종족 | string | R | |
| `sex` | 성별 | enum(Male/Female/Other/NA) | R | 현재 구조화 필드로 존재하지 않음(§12-9) |
| `ageGroup` | 연령대 | enum(Child/Teen/Adult/Elder/NA) | R | 좌동, 신규 구조화 |
| `role` | 역할/직업 | string | R | "글레이브 전사" 등 |
| `oneLinerConcept` | 한 문장 콘셉트 | string | R | 전 Brief 필수 항목 |
| `status` | 상태 | enum(Concept/Master/Active/Hold) | R | 로스터 표 어휘 재사용 |
| `personalityKeywords` | 성격 키워드(≤3) | list\<string\> | O | |
| `originWorldSentence` | 출신 세계 한 문장 | string | R | |
| `arrivalSentence` | 데스크탑에 넘어온 계기 | string | R(Character만) | Monster는 "감염 전 모습/감염 표식"으로 대체 |
| `infectionMark` | 버그 감염 표식 | string | R(Monster만) | `world-setting-rules.md` §6.2 |

### 5.3 Actor Sheet — Body & Proportions (핵심: 브리프의 5개 분리 요구를 그대로 필드화)

| 필드 | UI 라벨 | 타입 | 구분 | 근거 |
|---|---|---|---|---|
| `statureClass` | Stature 등급 | enum(Small/Normal/Large/Giant) | R, I | 현재 "체형 등급" 텍스트에서 크기 성분만 분리 |
| `targetLogicalHeightPx` | 목표 논리 높이(px) | number | R, I | 실측 사례 70/91/56/79/86px |
| `buildClass` | Build 등급 | enum(Slender/Normal/Muscular/Heavy) | R, I | "체형 등급"에서 골격 성분만 분리 |
| `proportionTemplateId` | 비율 템플릿 | ref | R, I | "약 2.5등신" 등 |
| `speciesScale` | Species Scale | number | R, I | 현재 "실제 스케일"의 후신, 신장과 독립 검증(§9 R2) |
| `headSizeClass` | 머리 크기 등급 | enum(Small/Normal/Large) | R | |
| `handSizeClass` | 손 크기 등급 | enum(Small/Normal/Large) | R | |
| `footSizeClass` | 발 크기 등급 | enum(Small/Normal/Large) | R | |
| `torsoWidthClass` | 몸통 너비 등급 | enum(Slim/Normal/Wide) | R | |
| `physicalTraits` | 신체 특징 태그 | list\<string\> | O | 뾰족귀, 문신 등 |
| `isFloatingActor` | 부유형 여부 | boolean | R, 기본 false | Specter 사례 |
| `unityVisualScale` | Unity 표시 배율 | number | R, I, 기본 1.0 | 정책상 잠금, 예외 시 승인 필요 |

### 5.4 Actor Sheet — Look

| 필드 | UI 라벨 | 타입 | 구분 |
|---|---|---|---|
| `hairOrHeadDetail` | 헤어/머리 특징 | string | R(해당 시) |
| `eyeColor` | 눈 색 | string | O |
| `skinOrFurColor` | 피부/털 색 | string | R |
| `clothingDescription` | 의상 | string | R |
| `materialsKeywords` | 재질 키워드 | list\<string\> | O |
| `paletteMaxColors` | 최대 색 수 | number | R, World 기본 제안 |
| `paletteColors` | 팔레트 목록 | list\<string\> | O |
| `decorations` | 장식 | list\<string\> | O |
| `invariantElements` | 절대 불변 요소 | list\<string\> (≥1) | R |
| `forbiddenElements` | 금지 요소 | list\<string\> (≥1) | R |
| `outlineColorOverride` | 외곽선 색(오버라이드) | string | O, I |

### 5.5 Actor Sheet — Weapon & Equipment

| 필드 | UI 라벨 | 타입 | 구분 | 근거 |
|---|---|---|---|---|
| `weaponFamily` | 무기 계열 | enum(ref, World 화이트리스트) | R(Unarmed 예외) | §0.4 신규 개념, 초기 카탈로그: OneHandedAxeDual, Glaive, Staff, Dagger, Bow(Concept), Greatsword(Concept), Unarmed |
| `weaponSizeRatio` | 무기 크기 비율(신장 대비) | number | R(무기 있으면) | 실측 0.3(단검)~1.2(글레이브) |
| `weaponMainHand` | 주손 | enum(Right/Left) | R(무기 있으면) | |
| `weaponOffHand` | 보조손 | enum(None/Same/Different) | R | Barbarian은 Same(양손 동일 도끼) |
| `weaponDirection` | 방향/비대칭 | string | O | "곡선 날은 화면 오른쪽 위" 등 |
| `weaponStructureNotes` | 구조 메모 | string | O | "완전 직선 창대" 등 잠금 조건 |
| `secondaryEquipment` | 보조 장비 | list\<string\> | O | 펜던트, 어깨보호대 등 |
| `weaponFamilyException` | 무기 계열 예외 승인 | approvedException | O | §9 R7 |

### 5.6 Actor Sheet — Production & Canvas

| 필드 | UI 라벨 | 타입 | 구분 | 근거 |
|---|---|---|---|---|
| `baseCanvas` | 기본 캔버스 | enum(512×512) | R, I, 잠김 | 전역 상수 |
| `largeMotionCanvas` | 대형 모션 캔버스 | enum | R, I | World 정책 상속, 비-512 선택 시 경고 |
| `logicalPixelDensity` | 논리 픽셀 밀도 | string | R, I | "3×3" |
| `pivotRule` | Pivot 규칙 | enum(ForwardFootContact/FloatingProjection) | R, C(제안) | `isFloatingActor`로 자동 제안 |
| `pivotXNormalized` | Pivot X | number | C | `forwardFootCenterX / canvasWidth` |
| `pivotYNormalized` | Pivot Y | number | C | `(canvasHeight - forwardFootContactY) / canvasHeight` |
| `ppu` | PPU | number(200) | R, I, 잠김 | 전역 상수 |
| `productionLayerPolicy` | 생산 레이어 정책 | enum(FlatSingleLayer(기본) / PlannedCharacterOutfitWeaponEffect) | O, 계획 메타데이터 | §0.4, MVP는 강제하지 않음 |
| `resourceFolderPath` | 리소스 폴더 경로 | string | O, 자동 제안 | `Assets/Art/{Character\|Enemy}/{actorId}` |

### 5.7 시스템/메타 (사용자 미입력)

| 필드 | 구분 | 비고 |
|---|---|---|
| `schemaVersion` | C | Codex 스키마 v1과 동기화 |
| `worldTemplateRef` (id+version) | C | Export 시점 World 버전 스냅샷 |
| `createdAt` / `updatedAt` | C | |
| `approvedExceptions[]` | C(사용자가 사유만 입력) | §9 |
| `warnings[]` / `blockingErrors[]` | C | Export 시점 스냅샷 |
| `comparisonSnapshot` | C, O | Export 시 마지막 비교 결과 동봉(선택) |

---

## 6. 필수/계산 필드 요약

- **필수(R) 필드가 비어 있으면 Export 자체가 불가능**하다(브리프: "Do not require JSON editing" + "Show all missing fields before export"). 필수 필드는 위 표에서 "R"로 표시된 전부이며, `physicalTraits`/`decorations`/`paletteColors` 등 명시적으로 "O"인 항목만 비워둘 수 있다.
- **계산 필드(C)**는 사용자가 직접 입력할 수 없고 항상 다른 필드로부터 유도된다:
  1. `pivotXNormalized = forwardFootCenterX / canvasWidth`
  2. `pivotYNormalized = (canvasHeight - forwardFootContactPixelY) / canvasHeight`
     (부유형은 바닥 투영점 X/Y를 동일 공식에 대입)
  3. `impliedScaleFromHeight = targetLogicalHeightPx / worldDefaultLogicalHeightPx` — `speciesScale`과의 정합성 검사에 사용(§9 R2)
  4. `estimatedWeaponCanvasOccupancyHeight% ≈ min(1, (targetLogicalHeightPx × (1 + weaponSizeRatio × k)) / baseCanvasHeightPx)` — `k`는 무기 계열별 돌출 계수(글레이브·양손무기류는 1에 가깝게, 단검류는 0에 가깝게), 참고 실측: 바바리안 도끼 세트 실제 점유 73%/목표 48~52%, ElfGuardian 글레이브(1.2×신장) 미실측이므로 "(추정)" 표기 필수
  5. `effectiveDisplayHeight = targetLogicalHeightPx × speciesScale-정합 확인용(참고치, 실제 표시 크기 결정 요인 아님)` — 오해 방지를 위해 UI에 "월드 표시 크기를 직접 결정하지 않음, PPU/Unity 배율과 별개" 주석을 반드시 병기(브리프의 scale 과부하 문제 재발 방지가 이 문서의 핵심 목적이므로, 계산 필드 라벨 자체도 오해를 만들면 안 된다).
  6. Comparison View의 모든 `Δ` 열.

---

## 7. 상속(Inheritance) vs 오버라이드(Override) 표현 규칙

1. World을 선택하는 순간, `I`(상속가능) 표시된 모든 필드에 World 기본값이 채워지고 **"상속: World"** 배지(중립색, 예: 회색/파랑)가 붙는다. 값은 여전히 편집 가능하다 — 편집을 시작하는 순간 배지가 **"재정의됨"**(강조색, 예: 호박색)으로 바뀌고 옆에 되돌리기(↺) 아이콘이 생긴다.
2. "재정의됨" 필드에는 항상 World 기본값을 참고용으로 함께 표시한다(회색 취소선 또는 보조 텍스트) — 사용자가 "내가 왜 이 값을 바꿨는지" 다시 확인할 수 있어야 한다.
3. World Template이 이후 수정되면:
   - 여전히 "상속: World" 상태인 필드는 다음 로드 시 자동으로 새 기본값을 반영한다.
   - "재정의됨" 필드는 액터의 값을 그대로 유지하되, 만약 새 World 기본값이 액터의 재정의값과 우연히 같아졌다면 "이 오버라이드는 이제 World 기본값과 동일합니다 — 되돌리기를 고려하세요" 안내만 하고 자동으로 해제하지 않는다(암묵적 데이터 변경 방지).
4. `ppu`, `baseCanvas`, `defaultUnityVisualScale`처럼 "정책상 잠긴" 필드는 오버라이드 UI 자체를 기본적으로 숨기고, "예외 필요" 버튼을 눌러야 편집 잠금이 풀리며 동시에 §9의 Blocking 규칙이 즉시 발동한다(즉 이 필드들은 "재정의"가 아니라 "예외 승인"이라는 더 무거운 절차로만 바뀔 수 있다).
5. JSON/Markdown export에는 각 필드가 `inherited` / `overridden` / `exception` 중 어느 상태였는지 항상 함께 기록한다(§11 예시 참고) — Export만 보고도 "이 값이 왜 이런지" 재구성 가능해야 한다.

---

## 8. (참고) 화면 간 공통 상호작용 원칙

- 모든 폼 섹션은 저장 버튼 없이 즉시 로컬 상태에 반영되고, Validation Sidebar가 300ms 디바운스로 갱신된다(입력 중 화면 깜빡임 방지).
- 필수 필드 미입력 상태에서도 다른 섹션으로 자유롭게 이동 가능(브리프: "Guide the user through required fields" — 강제 순서가 아니라 사이드바 안내로 유도).
- 모든 전문 용어(Stature/Species Scale/Proportion Template/Pivot 등)는 필드 라벨 옆 `(?)` 아이콘에 저장소 근거 문장을 그대로 인용한 툴팁을 제공한다(브리프: "Explain specialized terms in the UI") — 새 용어를 UX 팀이 창작하지 않고 §0의 실제 문서 문장을 재사용해 팀 어휘와 어긋나지 않게 한다.

---

## 9. 검증 규칙 (Blocking vs Warning, 승인된 예외)

| ID | 규칙 | 심각도 | 예외 승인 가능 | 메시지(초안) |
|---|---|---|---|---|
| R1 | 필수 필드 미입력 | **Blocking** | 불가(값을 채워야 함) | "❌ 필수 항목이 비어 있습니다: {필드명}. Export하려면 값을 입력하세요." |
| R2 | Stature(목표 높이)와 Species Scale 불일치 — `impliedScaleFromHeight`와 `speciesScale` 차이 10%p 초과 | Warning | 가능(사유 필수) | "⚠ 목표 높이로 계산된 배율({impliedScale})과 Species Scale({speciesScale})이 어긋납니다. 단순히 '키만 큰' 개체인지, '종족 자체가 균일하게 큰' 개체인지 확인하세요." |
| R3 | Build=Normal 인데 Torso Width=Wide | Warning(사유 있으면 예외) | 가능 | "⚠ Build가 Normal인데 몸통 너비가 Wide입니다. 실루엣 불일치 가능성이 있습니다." |
| R4 | 같은 World+종족 내 Proportion Template 불일치 | Warning | 가능 | "⚠ 같은 세계·종족의 다른 액터({refActor})는 {refTemplate} 템플릿을 사용합니다. 의도된 차이인지 확인하세요." |
| R5 | 무기 포함 캔버스 점유 추정치가 임계값(폭 60%/높이 55%) 초과 | Warning | 가능 | "⚠ 무기를 포함한 예상 점유율이 512 캔버스의 안전 여백을 초과할 수 있습니다(추정 {value}%). PerfectPixel 단계에서 축소될 위험이 있습니다." |
| R6 | Unity 표시 배율 ≠ 1.0 | **Blocking**(기본) | 가능(고빈도 예외 아님, 사유 필수) | "❌ Unity 표시 배율이 1.0이 아닙니다({value}). 체격 보정에 Unity Scale을 쓰지 않는 것이 현재 정책입니다(BlackCatMage 0.35 실험 기각 전례 참고)." |
| R7 | 액터에 허용되지 않은 Weapon Family 선택 | **Blocking** | 가능(신규 세계관 확장 등 사유 필수) | "❌ {weaponFamily}는 이 액터/세계에 승인되지 않은 무기 계열입니다." |
| R8 | 동급 휴머노이드 간 머리/손/발 등급 과도한 차이 | Warning | 가능 | "⚠ 같은 세계의 유사 체형 액터 대비 머리/손/발 등급 차이가 큽니다({field})." |
| R9 | `speciesScale` 또는 `targetLogicalHeightPx` 미입력 | **Blocking** | 불가 | "❌ Species Scale/목표 높이가 비어 있습니다. (참고: CopperAxeBarbarian 현재 브리프도 이 값이 미확정 상태입니다 — 실 사례)." |
| R10 | `largeMotionCanvas` ≠ `baseCanvas`(512×512) | Warning | 가능 | "⚠ 실험적 설정입니다. 대형 모션 캔버스 확장은 아직 공통 규칙으로 확정되지 않았습니다. 기존 리소스와 혼용하지 마세요." |
| R11 | `ppu` ≠ 200 | **Blocking** | 불가 | "❌ PPU는 프로젝트 전역 고정값(200)입니다. 씬 내 상대 크기가 어긋납니다." |
| R12 | `isFloatingActor=true`인데 `pivotRule=ForwardFootContact` | **Blocking**(로직 오류, 자동수정 제안) | 불가(자동 수정 유도) | "❌ 부유형 액터는 Pivot 규칙이 '바닥 투영점'이어야 합니다. [자동 수정]" |
| R13 | `invariantElements` 또는 `forbiddenElements` 비어 있음 | Warning | 해당 없음(그냥 권장) | "⚠ 절대 불변 요소/금지 요소가 비어 있습니다. 다음 단계(컨셉 아트) 작업자가 판단 기준을 갖지 못합니다." |
| R14 | `actorId`가 예상 리소스 폴더명과 다름 | Warning | 해당 없음(정보성) | "⚠ 리소스 폴더명과 Actor ID가 다릅니다. 실제 폴더: {resourceFolderPath}. 자산 연결 시 혼동될 수 있습니다." |

**승인된 예외(Approved Exception) 공통 사양**: (a) 사유 텍스트 필수(최소 10자), (b) 승인자/일자는 선택 입력(1인 개발 맥락상 강제하지 않음), (c) Export된 JSON/Markdown에는 예외가 있어도 **숨기지 않고** "승인된 예외" 섹션에 원래 경고 문구와 사유를 함께 남긴다(§11), (d) 조건이 해소되지 않는 한 재-Export 시 예외 항목이 자동으로 사라지지 않는다(사용자가 명시적으로 해제해야 함) — 이는 브리프의 "Record approved exceptions in exported data"를 문자 그대로 지키기 위함이다.

---

## 10. 비교(Comparison) UX 설계 근거

브리프가 요구한 8개 비교 지표(stature/height, build/proportion, head/hand/foot/torso, species scale, weapon occupancy, canvas)를 §4.3의 단일 테이블에 모두 배치했다. 설계 결정:

- **드릴다운 없이 한 화면 테이블**로 구성한 이유: MVP는 "텍스트/숫자 비교로 충분"(브리프 명시)하므로, 여러 화면으로 쪼개면 오히려 스캔 비용이 늘어난다.
- **Δ 열은 항상 방향(+/-)과 %를 함께 표기**한다 — 절대값만 보여주면 "큰 게 문제인지 작은 게 문제인지" 판단 비용이 늘어난다.
- **플래그 열**은 검증 엔진(§9)과 동일한 임계값을 재사용한다 — 비교 화면이 별도의 판정 로직을 갖지 않고 항상 Validation Sidebar와 같은 결론을 내도록 하여, "비교에서는 괜찮다고 나왔는데 Export에서는 막힌다" 같은 불일치를 방지한다.
- Reference 액터가 없는 신규 World는 비교를 건너뛸 수 있게 하여(§4.3 마지막 항목) Export를 부당하게 막지 않는다.

---

## 11. 대표 Markdown Export 예시

브리프의 필수 산출물("Elfguardian sample")에 대응하는 예시다. `actorId`는 §0.2의 결론에 따라 실제 Unity 자산 폴더명 `ElfGuardian`을 정본으로 쓰고, ArtPipeline 문서상 작업명 `LeafGlaiveElf`는 별칭으로 보존했다. 수치는 모두 §0에서 인용한 실측/문서값이다.

```markdown
# ElfGuardian — Character Sheet

- Actor ID: `ElfGuardian`  (별칭/문서상 작업명: `LeafGlaiveElf` — ⚠ ID 불일치, §12-1 참고)
- 표시 이름: 나뭇잎 글레이브 엘프 (Leaf Glaive Elf)
- 유형: Character · 세계: Fantasia (`HUMAN-FANTASY-01`, World Template v1)
- 종족: 엘프 · 역할: 글레이브 전사 · 상태: Master
- 한 문장 콘셉트: 숲의 가벼운 천옷과 나뭇잎 장식을 두르고 한쪽 곡선 날의 직선형 글레이브를 사용하는 금발 엘프 전사.

## Body & Proportions

| 필드 | 값 | 출처 |
|---|---|---|
| Stature Class | Large-Normal | 재정의됨 (World 기본: Normal) |
| Target Logical Height | **91px** | 재정의됨 — ⚠ World 허용범위(65~75px) 초과, 승인된 예외 있음 |
| Build Class | Normal | 상속: World |
| Proportion Template | 2.5-head SD (VenomCultist 계열) | 재정의됨 |
| Species Scale | **1.3** | 재정의됨 — 계산 정합성: 91÷70=1.30 ✅ 일치 |
| Head / Hand / Foot | Normal / Normal / Normal | 상속: World |
| Torso Width | Normal | 상속: World |
| Floating Actor | false | 기본값 |
| Unity Visual Scale | 1.0 | 상속: World(잠김) |

## Look

- 헤어: 노란 금발, 긴 뾰족귀 노출
- 피부/의상: 밝은 숲색 천옷, 나뭇잎 장식
- 팔레트(14색 이내): 금발 노랑, 잎 녹색, 밝은 숲색 천, 갈색, 흰 창대, 은회색 날, 노란 금속 결합부
- 외곽선: 1 논리 픽셀, 짙은 갈색·녹색 (재정의됨 — World 기본 외곽선과 다름)
- 절대 불변 요소: 금발, 노출된 긴 뾰족귀, 가벼운 천옷, 나뭇잎 장식, 완전히 곧은 흰 창대, 한쪽 곡선 날, 뭉툭한 노란 후단 장식
- 금지 요소: 활과 화살통, 중갑과 판금 갑옷, 방패, 신장 1.25배를 넘는 창, 휘어진 창대, 뒤쪽 보조날, 양날 무기, 인간처럼 둥근 귀

## Weapon & Equipment

| 필드 | 값 |
|---|---|
| Weapon Family | Glaive |
| Weapon Size Ratio (신장 대비) | 1.2 (상한 1.25) |
| Main Hand | Right (양손 사용, 대칭 배치 아님) |
| Off Hand | Same(양손으로 한 자루 파지) |
| Direction/비대칭 | 곡선 날: 화면 오른쪽 위 · 뭉툭한 노란 장식: 화면 왼쪽 아래 |
| Secondary Equipment | 없음 |

## Production & Canvas

| 필드 | 값 | 출처 |
|---|---|---|
| Base Canvas | 512×512 | 상속: World(잠김) |
| Large-Motion Canvas | 512×512 | 상속: World |
| Logical Pixel Density | 3×3 (Low Companion v1) | 상속: World |
| Pivot Rule | Forward Foot Contact | 계산 제안(Floating=false) |
| Pivot X (계산) | 0.50 (참고: 마스터 캔버스 1254 기준 별도 측정 필요, 512 정규화 전) |
| Pivot Y (계산) | *(측정 대기 — Master Measurements 03 문서에 512 기준 좌표 미기재)* |
| PPU | 200 | 상속: World(잠김) |
| Production Layer Policy | FlatSingleLayer(현재 파이프라인 기준) | 계획 메타데이터, §0.4 참고 |
| Resource Folder Path | `Assets/Art/Character/ElfGuardian` | 자동 제안, actorId와 일치 |

## Validation Summary (Export 시점 스냅샷)

- Blocking: 0건
- Warning: 1건 — R2 관련 없음(정합 확인됨). **R5**: 무기 포함 캔버스 점유 추정치 확인 권장(추정 방식이며 실측 아님).
- 승인된 예외: 1건
  - 규칙: R2(Stature/Species Scale 관련 아님) → **정정: 실제로는 Target Height 65~75px 범위 초과에 대한 예외**
  - 사유: "ElfGuardian은 세계관상 VenomCultist 대비 체형 계열은 동일하되 신장/장비를 1.3배로 키운 대형 개체로 설계됨. Species Scale(1.3)과 계산 정합성 확인됨."
  - 승인자/일자: (미기재, 1인 개발 워크플로)

## Comparison Snapshot (vs VenomCultist, Fantasia)

| 지표 | ElfGuardian | VenomCultist | Δ |
|---|---|---|---|
| Logical Height | 91px | 70px | +21px (+30%) |
| Species Scale | 1.3 | 1.0 | +0.3 |
| Proportion Template | 2.5-head SD | 2.5-head SD | 동일 |
| Weapon Occupancy(H, 추정) | ~53% | ~35%(추정) | +18%p |

## Schema

- schemaVersion: 1 (Codex 트랙과 동기화 예정)
- worldTemplateRef: `HUMAN-FANTASY-01` v1
- exportedFiles: `ElfGuardian.character.json`, `ElfGuardian.character.md`
```

이 예시에서 일부러 **비워두거나 "측정 대기"로 남긴 항목**(Pivot Y 등)이 있다 — 저장소의 `LeafGlaiveElf/03_master-measurements.md`는 1254×1254 마스터 기준 좌표만 있고 512 캔버스 기준 좌표가 아직 없기 때문이다. Export는 이런 상태를 **거짓으로 채워 넣지 않고 "측정 대기"로 정직하게 남겨야 한다** — 이는 다음 단계 작업자(Codex, 컨셉 아트 생성)에게 잘못된 확정값을 넘기지 않기 위한 설계 원칙이다.

---

## 12. 제품 리스크 & 누락 요구사항

1. **Actor ID 정본 충돌**(§0.2): `LeafGlaiveElf`/`ElfGuardian`, `BlackCatMage`/`CatMage`, `CopperAxeBarbarian`/`Barbarian` — Character Editor가 어느 쪽을 정본으로 삼을지 Synthesis Gate에서 명시적으로 결정해야 한다. 이 문서는 "실제 Unity 자산 폴더명"을 정본으로 권장한다(엔지니어링 자산과의 연결이 끊기면 Editor의 존재 이유가 약해짐). 결정하지 않으면 Wave 2 구현이 임의로 하나를 고르게 되고 나중에 또 갈라질 위험이 있다.
2. **문서 없는 기존 자산**(HyenaRaider, Werewolf, Scarecrow): 완전한 스프라이트 세트가 있는데 Brief 문서가 없다. MVP가 "신규 액터 생성"만 지원하고 "기존 자산 소급 임포트"를 지원하지 않으면, 이 세 액터는 Character Editor로 관리되지 않는 사각지대로 영구히 남는다. 최소한 백로그에 명시적으로 남겨야 한다.
3. **필수 필드가 실제로 비어 있는 현재 Active 액터**: `CopperAxeBarbarian`은 브리프에 "게임 내 상대 크기: 아직 미확정"이라고 명시돼 있고, `BlackCatMage`는 `실제 스케일` 필드 자체가 브리프 표에 없다. 두 액터 모두 브리프의 필수 샘플(Elfguardian/VenomCultist)에는 포함되지 않지만, Editor의 필수 필드 검증 로직을 그대로 적용하면 이 두 액터는 즉시 Blocking 상태가 된다 — 이것이 버그가 아니라 **정확히 브리프가 해결하려는 문제**임을 코디네이터가 사용자에게 설명할 필요가 있다.
4. **World of the Dead 로스터 문서 공백**: `UNDEAD-WORLD-01` 절에 캐릭터/몬스터 로스터 표가 없다. Specter는 실제로 존재하는데 World 문서 갱신이 누락된 상태 — World Template을 이 문서에서 그대로 가져오면 Specter가 어느 World에도 "공식적으로" 속하지 않는 것처럼 보일 수 있다.
5. **`production-layer policy`의 실제 구현 근거 부재**(§0.4): 현재 파이프라인은 레이어 분리가 없는 단일 PNG 생산 체계다. 이 필드를 MVP에서 "강제되는 검증 대상"으로 만들면 모든 기존 액터가 즉시 위반 상태가 된다. 계획 메타데이터로만 다루는 것을 강력히 권장하며, Synthesis Gate에서 이 스코프를 명시적으로 좁혀야 한다.
6. **World/PPU/캔버스를 "전역 상수"로 볼지 "World별 설정"으로 볼지 미정**: 브리프는 이 값들을 World Template의 일부로 요구하지만, 저장소 근거상 이들은 세계관과 무관한 프로젝트 전역 상수처럼 운용되고 있다(PPU 200, 512 캔버스는 모든 World/모든 액터에서 동일). MVP UI에서 World마다 이 값을 다르게 설정할 수 있게 열어두면, 세계 간 상대 크기가 깨지는 바로 그 문제(브리프의 핵심 배경)를 재발시킬 위험이 있다 — §4.1에서 편집 잠금으로 완화했지만, Synthesis Gate에서 "이 값은 World가 아니라 프로젝트 전역 설정이어야 하는가"를 명확히 할 필요가 있다.
7. **무기 계열(Weapon Family) 카탈로그의 근거 취약성**(§0.4): 카탈로그 절반(Bow/Greatsword)이 아직 Master Design 승인 전인 Concept 액터(FoxArcher/DragonWarrior)에서 역산한 추정 항목이다. 실제 승인 시 계열 정의가 바뀌면 이미 저장된 액터 시트의 `weaponFamily` 값이 무효화될 수 있다 — 카탈로그 항목에 `status: Confirmed/Speculative` 플래그를 두는 것을 권장한다.
8. **이중언어 표시 이름 패턴**: 모든 세계/액터가 "영문 ID / 국문 표시명" 쌍으로 관리된다. `displayName`을 단일 문자열로 스키마화하면 Export 시 한쪽 언어가 소실된다 — 로컬라이즈드 문자열(최소 en/ko) 타입이 필요하다.
9. **성별/연령대 필드의 데이터 공백**: 브리프는 이를 필수 항목으로 요구하지만, 현재 어떤 Brief 문서에도 구조화된 필드로 존재하지 않는다(산문 속에 "남성"처럼 섞여 있을 뿐). 기존 액터를 처음 Editor로 들여올 때 거의 전부 미입력 상태로 시작하게 된다 — 필수(Blocking)로 둘지, "Unknown/Not Applicable" 기본값을 허용해 마찰을 줄일지 Synthesis Gate에서 결정 필요.
10. **스키마 버전 불일치 시 UX 미정의**: 브리프는 "schema version"을 저장 요구사항에는 넣었지만, Import 시 구버전 파일을 열었을 때의 사용자 경험(마이그레이션 배너, 실패 처리)은 명시하지 않았다. §3.2에서 초안을 제시했으니 Wave 2 스코프에 명시적으로 포함해야 한다.
11. **승인된 예외의 감사 추적**: "Export된 데이터에 승인된 예외를 기록하라"는 요구는 있지만 승인 주체/이력 보존 정책은 없다. 1인 개발 맥락에 맞춰 가벼운 사유 텍스트 방식(§9)을 제안했지만, 이후 여러 사람이 함께 쓰게 되면 재검토가 필요하다.
12. **비교 기준 액터가 없는 신규 World**: 브리프의 비교 기능은 "기존 동일 세계 액터"의 존재를 전제한다. 새 World Template을 막 만든 직후에는 비교 대상이 없다 — §4.3에서 스킵 가능하도록 설계했으나, Codex의 스키마에서도 이 케이스(Reference 없음)를 null-safe하게 다뤄야 한다.

---

## 13. Wave 2 UI 구현자를 위한 요약 인계

- 화면 6개(§2), 폼 섹션 5개(§5.2~5.6)를 그대로 컴포넌트 경계로 사용해도 무방하다.
- Validation Sidebar는 전역 상태(모든 섹션 공유)로 구현하고, §9 표의 14개 규칙을 순수 함수로 분리해 Comparison View와 Export 모달이 동일 함수를 재사용하게 한다(§10).
- 상속/오버라이드 배지(§7)는 필드 단위 공통 컴포넌트로 만들어 World Template 자체의 편집 화면(§4.1의 잠긴 필드 표현)에도 재사용 가능하다.
- §11 예시 Markdown의 구조(Body/Look/Weapon/Production/Validation Summary/Comparison Snapshot/Schema 7개 블록)를 Codex의 Schema v1 필드 순서와 맞춰야 한다 — Synthesis Gate에서 확인 필요.
- §12의 12개 리스크 중 1, 3, 5, 6번은 **Wave 2 착수 전에 코디네이터/사용자 결정이 필요한 항목**이다(구현 방향이 갈리는 지점). 나머지는 구현하면서 흡수 가능한 수준이다.
