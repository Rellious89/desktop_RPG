# KeyBuddy 11E-D-4 완료 보고서

## 커밋

- `e620c0b2 Fix purification eligibility and settlement`
- `8397ef5a Stabilize purification archive interaction`
- `18c66865 Fix purification portrait and reset labels`
- 본 보고서는 별도 커밋으로 기록한다.

## 변경 내용

- `PurificationService.TryRegister()`가 `currentCorruption <= BaseCorruption`을 권위 있게 `NoPurificationNeeded`로 차단한다. 슬롯·파티·기존 점유 용병·저장은 변경하지 않는다.
- `PurificationPanel`은 해당 결과에 01_UI/67 토스트를 연결했고, 정산 결과가 실제로 있을 때만 외부 UI 갱신을 요청한다.
- 하한 도달과 정상 정리된 슬롯은 `IsSettlementDue()`에서 false가 되어 반복 저장·시각 갱신을 막는다.
- `CharacterArchivePanel`은 드래그 프리뷰 중 Refresh를 보류하고 종료 뒤 한 번만 반영한다. 선택과 우측 상세 패널의 열림 상태를 분리해 사용자가 닫은 패널을 외부 Refresh가 다시 열지 않는다.
- `PurificationSlotView`는 실제 `sp_portrait` Image를 Inspector로 명시 참조하며 장식 `portrait` 프레임은 변경하지 않는다.
- Reset 창은 Building ID 1/2를 각각 `여관 (1)`, `교회 (2)`로 표시하고, 미확정 완료 상태를 `완료 확인 대기`로 표시한다.

## 검증

- Unity C# 컴파일: 오류 0 (`2022.3.62f3`, batchmode 프로젝트 로드)
- 컴파일 로그 Missing Reference 항목: 0
- `git diff --check`: 통과
- 집중 EditMode 테스트: `BuildingEditor.Tests.BuildingConstructionServiceTests` 필터로 실행을 요청했으나, 이 환경의 batch runner가 결과 XML을 만들지 않고 종료했다. 따라서 자동 테스트 통과로 보고하지 않는다.
- SaveData: v6 유지. 구조·버전·마이그레이션 변경 없음.

## 작업 트리와 안전성

- Unity가 생성·변경한 사용자 파일 `Assets/Fonts/Mulmaru/Mulmaru SDF.asset`은 수정·스테이징·되돌림하지 않았다.
- 원격 푸시와 실제 `persistentDataPath` 접근은 수행하지 않았다.
