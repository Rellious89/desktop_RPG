# KeyBuddy 7.1단계 결과 팝업 경량 보정 보고

## 커밋

- 구현: `feacbb4d` (`Fix dungeon result timing and entry dismissal`)
- 보고: 이 문서를 포함하는 다음 커밋
- 원격 푸시: 수행하지 않음

## 변경 결과

- `DungeonResultPanel`은 `lb_Timer`에 연결된 `01_UI / 34` 참조를 사용한다. 24시간 미만에는 `HH:mm:ss`를 실제 경과 시간으로, 24시간 이상에는 `01_UI / 38`의 현지화 결과로 치환한다.
- Locale 변경 시 34번과 38번 참조의 변경 알림으로 같은 경과 시간을 다시 조합한다. 참조가 없거나 34번 문구에 토큰이 없으면 시간 값 자체를 표시한다.
- `TableData/Localization/01_UI.csv`의 34번은 `Time - HH:mm:ss` / `진행시간 - HH:mm:ss`로 동기화했다. 38번은 변경하거나 중복 생성하지 않았다.
- 승인된 `DungeonEntryService.DungeonEnterRequested`에서 열린 결과 패널을 정상 `Close()`로 닫고, 표시 중인 FIFO 선두 한 건만 기존 확인 경로로 Consume한다. 거부된 요청은 팝업과 FIFO를 변경하지 않는다.
- 입장 요청 이후와 활성 던전 세션 중에는 남은 완료 결과를 자동 표시하지 않는다. 실제 마을 복귀로 세션이 완료되면 기존 FIFO 순서로 다시 표시한다.
- 결과 UI 코드는 보상을 지급하거나 인벤토리 및 저장 시스템을 호출하지 않는다. `SaveData.CurrentSaveVersion`은 `2`를 유지한다.

## 변경 파일

- 런타임: `DungeonResultPanel.cs`, `DungeonResultCoordinator.cs`
- 테스트: `DungeonResultTimeFormatterTests.cs`, `DungeonResultUiTests.cs`
- Localization 원본: `TableData/Localization/01_UI.csv`
- 사용자 변경을 그대로 포함: `pn_DungeonResult.prefab`, `desktopScene_ReSize.unity`, `01_UI_en.asset`, `01_UI_ko-KR.asset`

## 검증

- 집중 EditMode: `25 / 25` 통과, failure/skip/inconclusive `0`
- 전체 EditMode: `751 / 751` 통과, failure/skip/inconclusive `0`
- Unity 2022.3.62f3 Windows Standalone 타깃 컴파일 오류 `0`
- 테스트는 격리 Git worktree에서 실행했으며 실제 `Application.persistentDataPath`를 사용하는 PlayMode는 실행하지 않았다.
- 집중 첫 실행에서 EditMode 테스트의 비활성 패널 수명주기 정리 누락 1건을 발견해 픽스처만 보정했고, 이후 집중 및 전체 테스트를 통과했다.

## 사용자 제작물 보존 및 남은 수동 확인

- 사용자에게서 시작된 두 `lb_Count` 참조 수정, 씬 직렬화 상태, Key 34 한국어·영어 값은 되돌리거나 재생성하지 않고 구현 커밋에 보존했다.
- RectTransform, 크기, 위치, 폰트, 색상, 머티리얼, 스크롤 및 레이아웃 값은 이번 구현에서 수정하지 않았다.
- Windows 빌드에서 Confirm, 상단 Close, ESC 및 던전 입장 버튼을 각각 눌렀을 때의 체감 동작은 수동 확인 대상으로 남긴다.

