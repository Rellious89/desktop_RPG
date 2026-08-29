# KeyBuddy 캐릭터 등장 조건 영구 해금 기반 작업 보고

- 기준 브랜치/커밋: `save-system` / `c3ce1524`
- 구현 커밋: `c7131bef` (`Implement permanent recruitment unlock conditions`)
- SaveData: v7. `unlockedRecruitmentCharacterIds`에 캐릭터 ID별 최초 해금을 저장하며, v6→v7은 기존 진행을 보존하고 빈 목록을 만든다.
- 테이블: `CharacterUnlockCondition.csv`의 두 지원 타입, AND(동일 group) / OR(서로 다른 group), entry 중복·수치·target_id·참조 검증과 전용 Generated 폴더를 추가했다.
- 모집: 조건 최초 만족을 한 번 저장해 확정한 후 후보에 넣고, 이후 조건이 후퇴해도 기록을 기준으로 유지한다. 실패 또는 예외는 새 해금 목록과 저장 메타데이터를 되돌린다.
- Reset: 선택 캐릭터 삭제는 해당 ID의 해금만 제거하고, All Reset은 해금 목록도 비운다. 기본 보유 캐릭터 보호와 기존 단일 저장 롤백은 유지한다.
- Generated 범위: `Assets/Generated/TableData/CharacterUnlockCondition`만 새로 추가했다. 기존 Generated GUID/파일은 변경하지 않았다.
- 집중 테스트 수: 새 자동 테스트는 0개(기존 저장 필드 계약 테스트는 v7 필드를 반영).
- Unity 컴파일: 0개라고 단정할 수 없다. batchmode는 Unity 라이선스 데이터베이스가 read-only 상태여서 시작 단계에서 중단됐고 XML 결과도 생성되지 않았다.
- 정적 확인: `git diff --check` 통과.
- 작업 트리: 보고서 커밋 직전 변경 사항만 존재.
- 원격 미푸시, 실제 persistentDataPath 미사용.

## 후속 UI 단계

1. 용병 명부의 미해금 캐릭터 카드/상세에 등장 조건 표시
2. 조건 최초 달성 시 해금 알림 UI 또는 토스트 제공
