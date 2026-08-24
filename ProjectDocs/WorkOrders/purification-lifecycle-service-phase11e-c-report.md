# KeyBuddy 11E-C — 교회 기도·오염도 정화 서비스

- 구현 커밋: `d0bcf034` — `Add purification lifecycle service`
- `PurificationService`를 추가했다. 의존성은 SaveData/저장/UTC/CharacterCatalog/PurificationConfigCatalog/건물 완공 공급자로 주입하며, 씬 오브젝트나 `MonoBehaviour`를 검색하지 않는다.
- 등록은 활성·유효 정화 설정과 완공 건물을 확인하고, 보유·카탈로그 유효·회복소 비입소·중복 비배치를 검증한다. `BaseSlotCount`까지 고정 인덱스 슬롯을 확장하고 빈 슬롯은 압축하지 않는다.
- 파티원이 2명 이상인 기도 등록은 해당 고정 파티 슬롯만 빈 문자열로 바꾸고 정화 슬롯 등록과 저장을 한 번으로 묶는다. 1명 파티는 차단하며, 중단 뒤에는 파티로 자동 복귀하지 않는다.
- UTC 정산은 경과 tick과 잔여 tick에서 완성된 정수 주기만 적용한다. 소수 오염도와 Character BaseCorruption 하한을 보존하며, 하한 도달 뒤 초과 시간은 적립하지 않는다. 손상·미래 시각과 범위를 벗어난 진행 tick은 현재 UTC 기준으로 재설정해 무료/중복 정화를 막는다.
- 중단은 현재 UTC까지 먼저 정산한 뒤 그 슬롯만 비우며, 저장 실패·예외 때 정화 슬롯, 파티, 오염도, 저장 메타데이터를 모두 복구한다.
- 공용 저장 슬롯 판정을 추가해 PartyCompositionService의 합류/교체와 RecoveryStation의 대기 등록·실제 시작이 `InPurification`으로 막힌다. PassiveStaminaRecoveryService의 비파티 10% 비율 계산은 변경하지 않았다.
- 집중 EditMode: `CorruptionEditor.Tests.PurificationServiceTests` 9/9 통과.
- Unity C# 컴파일 오류 0, `git diff --check` 통과.
- SaveData는 v6을 그대로 유지했다. SaveData 구조·버전·마이그레이션, CSV·Generated, 씬·프리팹·UI는 변경하지 않았다.
- 작업 트리는 보고서 커밋 전 이 파일만 신규 상태였고, 구현 커밋 뒤 추가 코드 변경은 없다. 실제 `persistentDataPath` 및 원격 푸시는 사용하지 않았다.
