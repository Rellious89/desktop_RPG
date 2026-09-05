# KeyBuddy Rule Archaeology Protocol

> 이 문서는 게임 규칙이 아니라, 현재 프로젝트에 실제 적용된 규칙을 근거 기반으로 역산·기록하기 위한 조사 절차다. 정식 DesignRule이나 Rule Registry를 변경하거나 대체하지 않는다.

## 1. 목적과 범위

- Rule Archaeology는 현재 구현된 계약을 발견하고 근거를 연결한다.
- 조사 결과는 `Rule Archaeology Working Draft`이며, 사용자 검토와 별도 승격 결정 전까지 정식 Rule이 아니다.
- 과거 문서·커밋은 현재 구현과 테스트보다 낮은 우선순위의 근거다.

## 2. Evidence Priority

가능한 한 하나의 규칙을 둘 이상의 근거로 교차 검증한다.

1. 현재 테스트
2. 현재 실제 구현
3. 현재 데이터/설정/Prefab/Scene
4. 관련 WorkOrder
5. DesignRules 및 기타 문서
6. Git history

오래된 문서 하나만으로 현재 규칙을 확정하지 않는다. 현재 구현/테스트와 과거 문서가 다르면 차이를 기록하고 `CONFLICT` 또는 `LEGACY` 여부를 판정한다.

## 3. Rule Status

| Status | 판정 기준 |
| --- | --- |
| CONFIRMED | 현재 테스트 또는 구현으로 직접 확인되고, 다른 현재 근거와 모순되지 않는다. |
| INFERRED | 여러 현재 근거가 같은 결론을 강하게 시사하지만, 직접 고정한 구현/테스트 근거가 부족하다. |
| CONFLICT | 현재 근거나 현재 동작 경로 사이에 양립하지 않는 계약·처리가 확인된다. |
| LEGACY | 과거 문서·코드·작업 기준으로는 존재했지만 현재 구현/테스트가 이를 대체했거나 현재 근거로 사용할 수 없다. |

## 4. Rule Priority

| Priority | 판정 기준 |
| --- | --- |
| Critical | 저장 손실·중복 보상·진행 불일치·사용자 데이터 보호·핵심 게임 루프 손상 가능성이 있는 계약. |
| Structural | 시스템 경계, 데이터 소유권, lifecycle, 변환 또는 주요 상태 전이를 결정하는 계약. |
| Production | 제작·테스트·운영·배포 안전성 또는 리소스 투입 품질을 결정하는 계약. |
| Convention | 이름, 표현, 정렬, 반복 가능한 작업 방식 등 일관성을 위한 계약. |

## 5. Rule Record Format

각 Rule은 최소한 아래 항목을 포함한다.

```text
Rule ID / 제목
Status / Priority
규칙
현재 코드 근거
테스트 근거
문서·WorkOrder 근거
Git 근거 (필요할 때)
현재 영향 범위
사용자 판단 필요 여부
Validator 후보 여부
Skill 후보 여부
```

근거가 없는 내용을 Rule로 만들지 않는다. 발견한 문제는 별도 항목으로 기록하되, 조사 중 개선안을 설계하거나 구현하지 않는다.

## 6. 작업 금지사항

- 게임 코드, 테스트, 데이터, Prefab, Scene, migration을 수정하지 않는다.
- 리팩터링, 오류 수정, 성능 개선, 새 규칙 제안, 정식 Rule 승격을 하지 않는다.
- Validator나 Skill을 구현하지 않는다.
- 전체 파일이나 전체 Git history를 무차별적으로 읽지 않는다.
- 사용자 검토 전 기존 DesignRules/README/WorkOrder를 덮어쓰거나 삭제하지 않는다.

Working Draft, Manifest, Protocol의 작성·갱신만 허용된다.

## 7. Checkpoint 및 중단/재개

- 각 영역은 독립 checkpoint로 나눈다.
- checkpoint 완료 시 해당 Working Draft에 `Status`, `Base commit`, 완료 범위, 주요 근거, 미확인 연결점, 다음 재개 위치를 즉시 기록한다.
- 시작 시 `HEAD`와 영역 base commit을 비교한다. 다르면 관련 경로의 diff와 새 근거를 먼저 기록한다.
- 재개 시 완료 checkpoint를 다시 조사하지 않는다. Manifest와 해당 Working Draft의 다음 재개 위치부터 시작한다.
- Git commit은 사용자가 요청하지 않는 한 만들지 않는다. Working Draft가 untracked인 상태도 조사 상태로 유효하다.

## 8. 완료 보고 형식

영역 완료 후에는 전체 Rule을 대화에 재나열하지 않고 먼저 다음을 보고한다.

1. 발견 Rule 총수
2. CONFIRMED / INFERRED / CONFLICT / LEGACY 수
3. Critical Rule 수
4. `CONFLICT + Critical` 목록
5. `INFERRED + Critical` 목록
6. 사용자 판단 필요 항목
7. Validator 후보
8. Skill 후보
9. 조사 중 발견했으나 수정하지 않은 문제
10. 조사 기록 저장 위치 및 다음 재개 정보

정식 Rule Registry 승격, 기존 DesignRules 변경, Validator/Skill 구현은 사용자 검토 이후에만 진행한다.
