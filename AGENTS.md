# KeyBuddy 작업 지침

## Rule Archaeology 진입 규칙

구현·수정 작업을 시작할 때, 먼저 이번 변경이 건드리는 subsystem을 판단한다.

1. `ProjectDocs/RuleArchaeology/MANIFEST.md`를 Rule Archaeology index로 읽어 관련 Area를 찾는다.
2. 관련 Area의 Working Draft가 있을 때만 그 문서를 읽는다. 모든 RA 문서를 기본으로 읽지 않는다.
3. 읽은 Rule의 `CONFIRMED` / `INFERRED` / `CONFLICT` / `LEGACY` 상태와 Priority를 존중한다.
4. Critical Rule을 변경하거나 위반할 가능성이 있으면 구현 전에 해당 근거와 필요한 최소 코드 touchpoint를 확인한다.
5. `Critical + CONFLICT` 또는 `Critical + INFERRED`가 작업 판단에 직접 영향을 주면 구현 전에 사용자에게 확인한다.

RA 문서는 현재 구현의 Rule Map이며 구현 코드를 대체하지 않는다. 실제 수정 전에는 관련 현재 코드·테스트·데이터/Prefab touchpoint를 필요한 범위에서 확인한다.

관련 RA가 없다고 새 RA를 자동으로 만들지 않는다. 기존 규칙이 중요한 구현 판단을 위험하게 만들 만큼 불명확할 때만 작업을 멈추고 RA 조사가 필요하다고 보고한다.

`ProjectDocs/RuleArchaeology/PROTOCOL.md`는 RA 조사를 새로 시작하거나 재개할 때만 읽는다. 일반 개발 작업에서 PROTOCOL 전체 또는 모든 RA 문서를 강제 로드하지 않는다.
