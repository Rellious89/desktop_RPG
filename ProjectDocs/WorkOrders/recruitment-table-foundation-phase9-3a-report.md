# 9.3A 모집 테이블 기반 완료 보고

## 커밋

- 구현: `59a2fd55` — `Add recruitment table pipeline and candidate selection`
- 원격 푸시 없음
- 작업 트리 clean

## 구현 결과

- `CharacterAcquisition`, `RecruitmentType`, `RecruitmentPool`, `RecruitmentAccess`의 CSV 파싱·검증·생성 에셋·카탈로그를 기존 TableData 파이프라인에 추가했다.
- `BUILDING / 1`에서 `Inn_Normal_Access`와 `Inn_Normal`을 조회하는 순수 resolver를 추가했다.
- 비활성·잘못된 가중치·끊어진 참조·조건부 획득·중복 보유 캐릭터를 제외하는 주입형 가중치 후보 추첨을 추가했다. 후보가 없으면 `NoEligibleCandidate`를 반환한다.
- 후보 조회와 추첨은 SaveData, 저장소, UI를 변경하지 않는다.
- `btn_Open_Inn`이 기존 `InnSlot/UIAnchor`에서 건설 버튼·타이머와 동일한 화면 좌표를 사용하도록 보정했다.

## 검증

- Claude Opus 4.6 구현 커밋을 확인했다.
- 격리 클론 집중 EditMode: 265개 중 263개 통과.
- 남은 2건은 구현 회귀가 아니라 기준 데이터 의존 실패다.
  - `TownBuildingInteractionTests`: 대상 씬의 `btn_Build_Inn` 직렬화 비활성 상태.
  - `CharacterLocalizationBindingTests`: 사용자 커밋의 `06_Character.csv` 빈 행(7번)으로 인한 Localization 바인딩 실패.
- `Assembly-CSharp.dll` 및 Unity가 생성한 `Assembly-CSharp-Editor.dll` 확인.
- `git diff --check` 통과.

## 보존·제외 범위

- 사용자 커밋의 모집 CSV, Character/Localization 데이터와 씬을 수정하지 않았다.
- 기존 Generated 도메인은 모집 및 Character 변경에 필요한 범위 외 재생성하지 않았다.
- 모집 타이머, 캐릭터 실제 지급·등록, 비용 차감, UI·저장·Steam 연동은 구현하지 않았다.

## 다음 단계

여관 방문 타이머와 READY 상태 저장을 이 기반 위에 연결하고, 이후 후보 UI와 캐릭터 등록 API를 추가한다.
