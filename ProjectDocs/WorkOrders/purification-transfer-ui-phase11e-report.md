# KeyBuddy 11E-C-1 / 11E-D — 정화 전환 규칙 보정 및 교회 UI 연결

- 서비스 커밋: `a7242c50` — `Support purification replacement and transfers`
- UI·씬 커밋: `b780d40c` — `Connect purification panel UI`

## 변경 범위

- `PurificationService.TryRegister()`는 점유 슬롯의 다른 보유 캐릭터 등록을 교체로 처리한다. 새 대상 검증 뒤 기존 대상 정산, 슬롯 교체, 파티 고정 슬롯 해제를 하나의 저장으로 묶고 실패·예외 때 슬롯/오염도/파티/저장 메타데이터를 되돌린다.
- `PurificationResult`에 이전 및 신규 캐릭터 ID를 추가했고, 기도 캐릭터를 정산·해제하여 지정 파티 슬롯에 넣는 `TryMoveToParty()`와 읽기 전용 남은 시간 조회를 추가했다.
- 회복소 Pending 등록은 기도 상태를 그대로 유지하도록 바꿨으며, 실제 시작 직전 재검증은 계속 기도 중 상태를 차단한다.
- `PurificationPanel`, `PurificationSlotView`, `PurificationChurchOpener`를 추가했다. 기존 `pn_Purification`/`slot_Purification` 프리팹에 연결하고 `Interaction_Church`의 잔존 여관 버튼 이름을 `btn_Open_Church`로 정리했다.
- 슬롯은 빈/점유 표시, 초상화·현지화 이름·오염 퍼센트·24시간 이상을 보존하는 `HH:MM:SS` 남은 시간, 명부 카드 드롭 등록·교체, 직접 중단을 지원한다. 표시 갱신은 저장을 호출하지 않는다.

## 검증

- `git diff --check` 통과.
- 서비스 집중 테스트는 점유 슬롯 교체와 파티 이동 단언을 추가했다.
- Unity 배치 컴파일 및 집중 EditMode 실행은 완료하지 못했다. 사용자 Unity Editor 프로세스가 이 프로젝트를 열고 있어 같은 프로젝트의 배치 실행이 잠금 오류로 거부됐으며, 해당 프로세스를 종료하거나 잠금을 해제하지 않았다.
- 현재 열려 있는 Unity의 AssetImportWorker 로그에는 이번 정화 C# 파일의 `error CS` 항목이 없었으나, 이는 배치 검증을 대체하지 않는다.

## 보존 및 제한

- SaveData는 v6 유지, 저장 구조·버전·마이그레이션·CSV·Generated는 변경하지 않았다.
- 사용자 Localization 변경 파일(`01_UI` 에셋 및 `TableData/Localization/01_UI.csv`)은 수정·되돌림·스테이징하지 않았다. 시작 시 이미 커밋된 상태였고 이후 변경하지 않았다.
- 씬 변경은 `desktopScene_ReSize.unity`의 Church 상호작용 버튼 명명 및 opener 부착뿐이며, 패널·슬롯 프리팹의 기존 시각·크기·배치는 유지했다.
- 실제 `persistentDataPath`와 원격 푸시는 사용하지 않았다.
