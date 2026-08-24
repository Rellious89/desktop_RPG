# KeyBuddy 11E-B — 교회 정화 슬롯 저장·마이그레이션 v6

- 구현 커밋: `d2c8a715` — `Add purification slot save migration v6`
- `SaveData.CurrentSaveVersion`을 6으로 올리고 고정 인덱스를 보존하는 `purificationSlots` 저장 구조를 추가했다.
- 정화 슬롯은 정화 방식 ID, 캐릭터 ID, 마지막 계산 UTC, 미완료 주기의 잔여 tick을 저장한다. 저장 계층은 최소 1개 슬롯과 null 없는 항목을 보장하며 확장 슬롯을 자르거나 압축하지 않는다.
- v5→v6 마이그레이션은 기존 캐릭터·파티·회복·오염도·아이템·재화·건축·모집 진행을 보존하고 빈 기본 정화 슬롯 1개를 생성한다.
- 마이그레이션 작업 사본은 정화 슬롯 목록과 각 슬롯을 깊은 복사한다. 음수 진행값은 0으로 보정하고 동일 캐릭터가 중복 배치된 경우 첫 슬롯만 유지한다.
- Reset의 Construction은 건축·모집과 함께 정화 슬롯을 기본 빈 상태로 되돌린다. Character 삭제는 해당 캐릭터가 들어 있는 정화 슬롯만 비우며, 저장 실패 또는 예외 시 관련 목록과 슬롯 값을 모두 원상복구한다.
- 집중 EditMode: `SaveMigrationTests`, `SaveDataNormalizerTests`, `SaveResetServiceTests` 합계 95/95 통과.
- Unity C# 컴파일 오류 0, `git diff --check` 통과.
- SaveData v6 이외에 씬·프리팹·CSV·Localization·Generated 에셋은 변경하지 않았다.
- 실제 `persistentDataPath`와 원격 푸시는 사용하지 않았다.

## 다음 단계 경계

이번 단계는 저장 기반만 제공한다. 교회 등록·중단 트랜잭션, 오프라인 정화 계산, 캐릭터 명부/교회 UI 연결은 후속 단계에서 구현한다.
