# 9.3B 여관 모집 주기 저장 기반 완료 보고

## 핵심 규칙

- `SaveData.recruitmentCycles`에 Access Id, 시작 UTC, READY UTC만 저장하며 저장 버전은 2를 유지한다.
- `BUILDING / 1`의 `RecruitmentAccess`를 조회하고, 최초 주기는 여관 `completeAtUtc`부터 `arrival_interval_seconds`만큼 계산한다.
- 상태 조회는 `Locked / NotInitialized / Waiting / Ready / Unreadable`만 파생하며 저장·자동 초기화·READY 이후 재시작을 하지 않는다.
- 손상 시각과 모르는 Access Id는 보존하고, 저장 실패 시 신규 주기와 저장 메타데이터를 복구한다.
- Tools Reset의 `Construction`과 `All`은 건축 기록과 모집 주기를 함께 지우며 실패 시 둘 다 복구한다.

## 검증

- 집중 EditMode: 모집 주기 13/13, Save Reset 12/12, 저장 마이그레이션 85/85 통과.
- Unity C# 컴파일 오류 0, `git diff --check` 통과.
- 실제 `persistentDataPath`와 원격 저장소는 사용하지 않았다.

## 제외 범위

- 씬·프리팹·UI·실시간 타이머 연결, 후보 추첨, 캐릭터 지급, 비용 차감, 주기 재시작은 구현하지 않았다.
- CSV·Localization·Generated 에셋은 변경하지 않았다.
- 원격 푸시는 하지 않았다.
