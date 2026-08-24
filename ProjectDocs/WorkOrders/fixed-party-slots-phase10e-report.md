# KeyBuddy 10E — 고정 파티 슬롯 보고서

- 10E-A: `725ecdb2b461d1720e35b57a50463124a2e444ff` — SaveData v4, v3→v4 승격, 빈 슬롯 정규화/Reset 보존
- 10E-B: `94ef79d190d6c1d6587193e2633bfe64cc9bad58` — 고정 슬롯 합류·탈퇴·교체·이동/교환 트랜잭션
- 10E-C: `9d93be74993b668d9a9c3e1d66a3438f8a948c9e` — 슬롯 순서 런타임 소비 계약 명시

`partyCharacterIds`는 v4부터 인덱스가 슬롯 위치인 목록이며 빈 칸은 `string.Empty`다. v3 압축 목록은 순서를 바꾸지 않고 앞 슬롯부터 그대로 승격한다. 정규화는 미보유/중복만 같은 인덱스의 빈 값으로 바꾸고 슬롯을 당기지 않는다.

파티원 수는 목록 길이가 아니라 점유 슬롯 수다. 탈퇴는 해당 슬롯만 비우며, 기존 파티원 이동은 빈 목표에는 이동하고 점유 목표에는 값을 교환한다. 저장 실패/예외는 기존 트랜잭션의 슬롯 목록·저장 메타데이터 롤백을 사용한다.

수정 범위는 SaveData/마이그레이션/정규화/Reset, PartyCompositionService·PartySlotUtility, CharacterArchive 드롭/버튼 판정, CharacterRoster 계약 주석 및 집중 테스트다. 씬·프리팹은 변경하지 않았고 SaveData는 v4다. 원격 푸시와 실제 `persistentDataPath` 접근은 하지 않았다.

## 최종 집중 검증

- 보정 커밋: `85cf73e6` — v4 고정 슬롯 계약에 맞게 마이그레이션·Reset 기대값을 보정했다.
- 격리 복제 프로젝트의 EditMode 집중 실행: **169/169 통과**. `SaveMigrationTests`, `SaveResetServiceTests`, `PartyCompositionServiceTests`, `PartySlotViewTests`, `DungeonAccessTests`, `PassiveStaminaRecoveryServiceTests`와 고정 슬롯 런타임 영향만 확인하는 CharacterRoster 메서드 4건을 실행했다.
- Unity C# 컴파일 오류: **0**. 실제 프로젝트는 이미 열린 Unity 인스턴스 잠금으로 별도 batch 컴파일을 열 수 없었으며, 같은 소스의 격리 복제본에서 컴파일했다.
- `git diff --check`: 통과. SaveData v4 유지, 씬·프리팹 변경 없음, 실제 `persistentDataPath` 접근 및 원격 푸시 없음.

수동 확인: 좌측 탈퇴 뒤 우측 위치 유지, 빈 중간 슬롯 정확한 합류, 점유 슬롯 교체, 명부 카드 이동/교환, 한 명 남은 파티의 탈퇴 차단, 재실행 후 슬롯 보존, 교체/던전 진입을 확인한다.
