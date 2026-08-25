# KeyBuddy 11E-D-1 완료 보고서

## 커밋

- `620fe562 Complete purification transfer integration`
- `d5d087a5 Complete purification progress UI`
- 본 보고서 커밋은 아래에 추가한다.

## 변경 파일

- `Assets/Scripts/CharacterArchive/CharacterArchivePanel.cs`
- `Assets/Scripts/Corruption/PurificationService.cs`
- `Assets/Scripts/Recovery/RecoveryService.cs`
- `Assets/Scripts/Recovery/RecoveryStation.cs`
- `Assets/Scripts/Corruption/PurificationPanel.cs`
- `Assets/Scripts/Corruption/PurificationSlotView.cs`

## 구현 내용

- 명부에서 기도 중인 보유 용병을 파티 고정 슬롯으로 드롭하면 `PurificationService.TryMoveToParty()`가 UTC 정산, 기도 해제, 파티 반영을 저장 한 번으로 처리한다. 자동 해제 토스트는 출력하지 않는다.
- 회복소 Pending 등록·취소는 기도 상태를 유지한다. 실제 `StartRecovery()`는 정화 서비스를 주입받아 해당 대상의 UTC 정산과 기도 해제를 재화 차감·회복 슬롯 시작과 같은 저장 트랜잭션으로 처리한다. 저장 실패 또는 예외에서는 회복 슬롯, 재화, 기도 슬롯, 오염도, 저장 메타데이터를 복구한다.
- 정화 패널은 시작(63), 직접 중단(64), 교체 시 기존 중단(64) 후 신규 시작(63), 마지막 파티원 차단(66) 토스트를 현재 언어의 캐릭터 이름으로 표시한다.
- `PurificationSlotView`는 기존 `cell_01` 10개를 사용해 현재 오염도 fill, 기본 오염도 고정 셀, 5~9% 느린 점멸과 9~10% 빠른 점멸을 Image alpha로 표시한다. 빈 슬롯·닫힘·캐릭터 변경 때 점멸을 초기화한다.
- 타이머는 `TryGetRemainingTime()`의 읽기 전용 계산으로 누적 `HH:MM:SS` 형식을 갱신한다. 패널은 정화 interval 경계에서만 최대 한 번 `Tick()`을 호출하며 프레임마다 저장하지 않는다.

## 검증

- Unity C# 컴파일: 오류 0 (`2022.3.62f3`, batchmode 프로젝트 로드)
- `git diff --check`: 통과
- 자동 EditMode·PlayMode·Sol 및 기능 테스트: 요청에 따라 실행하지 않음. 기능 통과로 표현하지 않는다.
- SaveData: v6 구조·버전·마이그레이션 변경 없음.

## 범위와 안전성

- 씬·프리팹 직렬화 변경 없음. 기존 `slot_Purification`의 `cell_01` 구조를 런타임에서 사용한다.
- Localization/CSV/Generated 파일은 수정하지 않았다.
- 실제 `persistentDataPath`는 사용하지 않았다.
- 원격 푸시는 수행하지 않았다.

## 후속 수동 확인

1. 빈/점유 기도 슬롯 등록 및 교체 토스트 순서
2. 마지막 파티원 등록 차단
3. 기도 용병의 파티 합류, 회복소 Pending 등록·취소, 실제 회복 시작
4. 직접 중단
5. 10셀·기본 셀·점멸, 24시간 초과 타이머, 재실행 후 오프라인 정산
