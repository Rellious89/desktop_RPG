# Actor Production Tracker

로컬 파일을 읽어 제작 현황을 만드는 **읽기 전용 파생 뷰**입니다. Unity 데이터, CSV, 원본 패키지 문서를 수정하지 않으며 Google Sheets의 대체 현황판으로만 사용합니다.

## 갱신하고 열기

1. 프로젝트 루트에서 `node Tools/ActorProductionTracker/scan.mjs`를 실행합니다.
2. `Tools/ActorProductionTracker/dashboard.html`을 브라우저에서 엽니다. `file://`에서도 작동합니다.
3. 생성 결과는 `ProjectDocs/ActorProduction/`에 남습니다: JSON 인덱스, Markdown 요약, 대시보드 데이터.

설치나 네트워크 연결은 필요 없습니다.

## 읽는 대상과 판정

즉시 하위 폴더의 `Assets/Art/Character`, `Assets/Art/Enemy`, 제작 패키지, CharacterDefinition, MotionProfile, 그리고 이름에 character/monster가 있는 CSV를 합쳐 배우 목록을 만듭니다. PNG와 Unity `.meta`에서 크기·PPU·피벗도 관찰합니다. V1은 50 PPU / `(0.5, 0.1)`, V2는 50 PPU / `(0.5, 0.234)`가 모두 일치할 때만 표시합니다. 그 외는 `unknown`이며, 명시 V2 패키지이지만 아직 import가 없으면 `V2 candidate/pending import`입니다.

필수 항목은 `tracker.config.json`의 프로필 데이터입니다. 현재 자산 구조에 맞춘 잠정 규칙이며, Passive Enemy의 hit hold/recovery는 폴더명 대신 hit 프레임 수와 MotionProfile의 recovery 설정에서 판정합니다. Test Actor는 완성도에서 제외되고 Hold Actor는 누락을 보여도 우선순위 큐를 지배하지 않습니다.

## 별칭과 안전한 수정

`aliases`에는 패키지명과 런타임 ID의 명시적 연결만 둡니다. `overrides`에는 타입·월드·보류 상태처럼 파일에서 안전하게 알 수 없는 값만 넣습니다. 새 연결을 추정하지 마세요. 연결하지 못한 값은 경고와 `Unmapped`로 남겨 검토할 수 있게 합니다.

`plannedActors`는 문서에만 있는 후보·보류 배우의 카탈로그입니다. 이 항목은 제작 패키지나 런타임 데이터를 만들지 않고, Candidate는 준비도·현재 우선순위에서 제외됩니다.
