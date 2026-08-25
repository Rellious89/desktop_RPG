# KeyBuddy 11E-D-2 완료 보고서

## 커밋

- `39f3eaee Connect church construction and purification unlock`
- 본 보고서는 별도 커밋으로 기록한다.

## 변경 파일

- `Assets/Scenes/desktopScene_ReSize.unity`
- `Assets/Scripts/Building/UI/BuildingCompletionButtonGate.cs`
- `Assets/Scripts/Corruption/PurificationPanel.cs`

## 교회 건축 연결

- 항상 활성인 `FieldSystem`에 두 번째 `TownBuildingInteractionController`를 추가했다.
- 교회 컨트롤러는 기존 여관 컨트롤러와 독립된 Building ID `2` (`Building_2.asset`)를 사용한다.
- 명시 연결: `ChurchSlot/UIAnchor`, `TownInteractionLayer/Interaction_Church`, `btn_Build_Church`, 교회 타이머와 Animator, `btn_Open_Church`, 기존 건축 팝업, `InventoryManager`, 기존 시작·완료 토스트(01_UI/42·43).
- 따라서 교회는 기존 `BuildingConstructionService` 기록과 completionNotified 정책으로 시작·복원·완료되며, 별도 SaveData 필드는 없다.
- 여관 컨트롤러의 기존 Inspector 참조는 변경하지 않았다.

## 정화 메뉴 해금

- `BuildingCompletionButtonGate`가 `btn_Purification`의 `interactable`만 Building ID 2의 완료 UTC 기준으로 제어한다. 저장·건축 알림·패널 열기는 소유하지 않는다.
- 기존 `ModalPanelOpener`의 `pn_Purification` 참조는 유지했다.
- `PurificationPanel`은 하단 버튼을 우회해 호출돼도 Building ID 2가 미완공이면 열리지 않도록 방어한다.
- `Interaction_Church`의 `PurificationChurchOpener`는 제거했다. `btn_Open_Church`는 완료 표시 전용이며 `interactable = false`다.

## 검증

- Unity C# 컴파일: 오류 0 (`2022.3.62f3`, batchmode 프로젝트 로드)
- 컴파일 로그의 Missing Reference 및 컨트롤러 필수 참조 오류: 0
- `git diff --check`: 통과
- SaveData: v6 유지. 구조·버전·마이그레이션 변경 없음.
- 자동 EditMode·PlayMode·Sol 및 기능 테스트: 요청에 따라 실행하지 않았다. 기능 테스트 통과로 표현하지 않는다.

## 범위와 안전성

- 씬 변경은 교회 건축 컨트롤러, 교회 버튼 명칭, 월드 완료 버튼 비활성화, 하단 정화 버튼 게이트로 한정했다. UI 시각·크기·배치는 변경하지 않았다.
- 실제 `persistentDataPath`를 사용하지 않았고 원격 푸시도 하지 않았다.

## 추후 수동 확인

1. 교회 미건축/건축 중/완료의 월드 버튼·타이머·완료 표시
2. Building ID 2 팝업 이름·시간·비용과 재실행 후 복원
3. 완공 전후 `btn_Purification` 비활성/활성 및 패널 열기
4. 여관 건축과 모집 흐름 회귀
