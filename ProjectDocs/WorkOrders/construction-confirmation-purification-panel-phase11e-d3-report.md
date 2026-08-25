# KeyBuddy 11E-D-3 완료 보고서

## 커밋

- `7d2ec66f Require construction completion confirmation`
- `68ff32d4 Fix purification panel scene wiring`
- 본 보고서는 별도 커밋으로 기록한다.

## 건축 상태 정책

- `BuildingConstructionPhase`에 `AwaitingConfirmation`을 추가했다.
- 예정 시각 전은 `InProgress`, 예정 시각 경과와 미확정 표식은 `AwaitingConfirmation`, `completionNotified == true`일 때만 `Completed`다.
- `completionNotified`는 이제 알림 발송 여부가 아니라 사용자의 영속적인 완료 확정 표식이다. SaveData 버전은 v6를 유지했다.
- 완료 확정은 `TownBuildingInteractionController`의 `btn_Open_Inn`/`btn_Open_Church` 클릭으로만 수행한다. 성공 시 한 번 저장하고 이벤트·토스트를 한 번 보내며, 실패·예외에서는 표식과 저장 메타데이터를 복구해 확인 대기를 유지한다.
- 확정 뒤에는 건축 버튼·타이머·완료 버튼을 모두 숨긴다.

## 해금과 모집

- `BuildingCompletionPolicy`가 완료 UTC와 확정 표식을 함께 검사하는 공통 읽기 전용 판정을 제공한다.
- 정화 메뉴 게이트, 정화 패널 직접 열기 방어, 명부·회복소의 정화 건물 판정, 남아 있는 교회 오프너, 모집 주기가 이 정책을 사용한다.
- 여관 모집 주기는 미확정 건물에서 계속 잠겨 있고, 확정 뒤 최초 초기화한 현재 UTC를 시작 시각으로 쓴다. 기존 주기·후보 데이터는 덮어쓰지 않는다.

## 기도 패널 연결

- `btn_Purification`의 기존 `ModalPanelOpener` 대상은 씬 인스턴스 `pn_Purification`이다(캐릭터 명부가 아님).
- `pn_Purification` 프리팹에 Character Catalog, Purification Config Catalog, `slot_Purification`, `list`, `church_prayer`를 명시 연결했다. 기존 폴백도 유지했다.

## 검증

- Unity C# 컴파일: 오류 0 (`2022.3.62f3`, batchmode 프로젝트 로드)
- 컴파일 로그 Missing Reference 항목: 0
- `git diff --check`: 통과
- 집중 EditMode 테스트: Unity 테스트 러너를 `BuildingConstructionServiceTests` 및 `RecruitmentCycleServiceTests` 필터로 두 번 요청했으나, 이 환경의 batch runner가 결과 XML을 만들지 않은 채 성공 종료했다. 따라서 테스트 통과로 보고하지 않는다.
- PlayMode·전체 EditMode·Sol은 실행하지 않았다.

## 범위와 안전성

- 변경: 건축 서비스·완료 정책·모집 게이트·정화 패널 프리팹 참조·기존 Town 씬의 완료 버튼 참조.
- CSV, Localization, Generated 에셋은 수정하지 않았다. SaveData 구조·버전·마이그레이션도 변경하지 않았다.
- 실제 `persistentDataPath`와 원격 푸시는 사용하지 않았다.
