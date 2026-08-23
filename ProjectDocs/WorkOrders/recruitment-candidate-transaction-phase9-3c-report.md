# 9.3C READY 후보 저장 전환 완료 보고

## 핵심 규칙

- READY에서만 기존 `RecruitmentCandidateSelector`로 후보를 뽑고, 후보 ID와 다음 UTC 주기를 한 번의 저장으로 확정한다.
- 대기 후보·후보 없음·손상 상태는 재추첨이나 주기 변경 없이 보존한다. 저장 실패·예외는 후보, 시각, 저장 메타데이터를 롤백한다.
- `pendingCharacterId`는 저장/마이그레이션 깊은 복사에 포함하며 저장 버전은 2를 유지한다.

## 검증

- 집중 EditMode 후보 트랜잭션 8/8 통과, Unity C# 컴파일 오류 0, `git diff --check` 통과.

## 제외 범위

- UI·씬·프리팹, 후보 등록·반환, 캐릭터 지급, 비용, 주기 재시작, CSV·Localization·Generated, 실제 `persistentDataPath`, 원격 푸시는 구현하지 않았다.
