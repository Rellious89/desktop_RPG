# KeyBuddy 7단계 완료 보고 — 던전 귀환 결과 UI 연결

## 커밋

- 기준: `6caa84d3` (`Add dungeon result localization`)
- 구현·테스트: `f6db4bdf` (`Connect dungeon return result UI`)
- 보고서: 본 커밋
- 원격 푸시: 수행하지 않음

## 구현 결과

- 실제 `FieldMode.Dungeon` 확정 때 `Time.realtimeSinceStartupAsDouble`로 시작 시각을 잡고, 실제 `FieldMode.Town` 확정 때 차이를 계산한다. 배속과 UTC 시계 변경의 영향을 받지 않으며, 중복 시작은 타이머를 초기화하지 않는다.
- `DungeonSessionSnapshot.ElapsedSeconds`는 런타임 Tracker가 측정한 값만 담는다. 순수 원장은 Unity 시간 API를 사용하지 않는다.
- `DungeonResultCoordinator`가 완료 FIFO 선두를 `Peek`하고, 귀환 연출이 끝난 뒤 패널을 연다. 정상 확인 때 표시 중인 `SessionSequence`와 선두를 다시 비교해 정확히 한 건만 `Consume`한다. 다음 결과는 패널이 닫힌 다음 프레임에 표시한다.
- `btn_Confirm`, `btn_close`, ESC의 정상 `Close()`는 같은 확인 경로를 사용한다. 외부 `SetActive(false)`, 씬 종료 및 파괴는 Consume하지 않는다.
- 패널은 던전 이름, 경과 시간, 처치 수, 실제 획득 재화와 아이템을 표시한다. 아이템은 스냅샷의 최초 획득 순서를 유지하고 아이콘과 `long` 수량만 표시한다.
- 24시간 미만은 `HH:mm:ss`, 24시간 이상은 기존 `01_UI / 38`을 사용한다. Key 38을 중복 생성하지 않았고 값은 `ko-KR: 1일 이상`, `en: 1 day or more`다.
- 정산 UI는 보상을 다시 지급하거나 저장하지 않는다. `SaveData.CurrentSaveVersion`은 `2`다.

## 변경 파일

- 런타임: `DungeonSessionLedger.cs`, `DungeonSessionTracker.cs`, `DungeonResultCoordinator.cs`, `DungeonResultPanel.cs`, `DungeonResultRewardItemView.cs`, `FieldTransitionSequencer.cs`, `ModalPanel.cs`
- UI 연결: `pn_DungeonResult.prefab`, `item_DungeonResultReward.prefab`, `desktopScene_ReSize.unity`
- Localization: `TableData/Localization/01_UI.csv`, `01_UI_en.asset`
- 테스트: `DungeonSessionLedgerTests.cs`, `DungeonSessionTrackerTests.cs`, `DungeonResultTimeFormatterTests.cs`, `DungeonResultUiTests.cs` 및 신규 `.meta`

## 검증

- 집중 EditMode: `117 passed`, failure/skip/inconclusive `0`
- 전체 EditMode 최종 1회: `740 passed`, failure/skip/inconclusive `0`
- Unity 최종 재컴파일: 컴파일 오류 `0`
- 격리 Additive 씬·프리팹 스모크: 필수 참조, 두 `lb_Count`의 개별 연결, Coordinator/Tracker/Panel/Sequencer 연결 통과
- `git diff --check`: 통과
- `Assets/Generated`, `Assets/TableData`, `Assets/Data`, `SaveData.cs`, `SaveSystem.cs`: 변경 없음
- 실제 `persistentDataPath`를 사용하는 PlayMode는 실행하지 않았다. 신규 UI/Coordinator는 `SaveSystem` 및 저장 API를 호출하지 않는다.

## 사용자 제작물 보존

- 프리팹과 씬에는 필요한 스크립트 컴포넌트와 직렬화 참조만 추가했다. RectTransform, 크기, 위치, 폰트, 색상, 머티리얼, 스크롤 및 레이아웃 값은 변경하지 않았다.
- `Mulmaru SDF.asset`은 기준 커밋과 동일한 SHA-256 `6a51a51ad15210a9e8b86dfcbbbd4defe0570883f3dd662a2b4255008362314c`를 유지한다.
- 사용자 정적 라벨 Localization 및 씬 배치를 유지했다.

## 남은 수동 검증

- Windows 빌드에서 `WindowInputRegion`을 통한 실제 클릭 관통 차단
- 실제 귀환 연출 종료 직후 표시 타이밍과 가로 아이템 목록의 시각 확인
- Confirm, 상단 Close, ESC 각각의 체감 동작 확인

실제 사용자 저장 경로 보호를 위해 대상 씬 PlayMode 검증은 의도적으로 수행하지 않았다.
