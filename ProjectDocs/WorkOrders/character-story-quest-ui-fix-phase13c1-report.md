# Character Story Quest UI Fix — Phase 13C-1 완료 보고서

## 결과

- 작업 브랜치/시작 기준: `save-system` / `59ffeaa4ff151206028d30aade7f0e016ca86b35`.
- 구현 커밋: `73d9a52d` (`fix: correct character story quest UI localization`).
- `CharacterStoryQuestUiController`는 `pn_CharacterArchive` 루트에서 제거하고 `pn_right/QuestInfo`에 정확히 한 개만 두었다. `CharacterArchivePanel`은 새 컴포넌트를 가리키는 최소 `storyQuestUi` 참조만 유지한다.
- SaveData는 v8(`CurrentSaveVersion = 8`)을 유지했다. SaveData/마이그레이션, 퀘스트 진행 서비스, 전투, CSV, Localization, Generated 에셋은 수정하지 않았다.

## 구현

- Current/Total 진행 바 아래의 각 `lb_percent`를 컨트롤러가 소유해 0~100 범위의 반올림 정수 `%`로 그린다.
- `lb_totalProgress`는 01_UI 키 87의 장기 수명 참조에서 받은 형식을 적용하며, 인자 순서를 현재 퀘스트 순번/완료 수/활성 퀘스트 수로 유지한다. 정적 `LocalizedTMPText`는 비활성화해 비동기 콜백이 동적 문구를 덮지 않게 했다.
- 09_Quest 키 1~4, 10001~10004, 100002, 100004를 컨트롤러 수명 동안 키별 참조/문자열 캐시로 유지한다. StringChanged 초기 로드와 locale 변경마다 Refresh하고, 로드 전에는 키 숫자 대신 안전한 비노출/문구 fallback을 사용한다.
- 목표 대상 몬스터/던전 이름도 기존 정의의 장기 참조를 구독해 table 비동기 로드 및 locale 변경 뒤 다시 표시한다. 단일/복수/무대상 조합과 동적 인자 계약은 유지했다.
- QuestInfo가 비활성 페이지일 때 OnDisable로 `btn_swap`/로컬라이즈 구독을 끊지 않도록 수명을 OpenFor/Close 및 OnDestroy에 맞췄다. 이로써 CharacterInfo → QuestInfo → CharacterInfo 반복 전환 뒤에도 토글이 유지된다.
- 동등 가중치 CurrentProgress, 완료 수 + 현재 진행률 TotalProgress, Ready 완료 버튼/즉시 다음 퀘스트, 기본 페이지 복원, 기존 ScrollRect는 변경하지 않았다.

## 변경 파일

- `Assets/Art/UI/Prefab/panel/pn_CharacterArchive.prefab`
- `Assets/Scripts/CharacterArchive/CharacterStoryQuestUiController.cs`
- `Assets/Editor/CharacterArchive/CharacterStoryQuestUiPrefabSetup.cs`
- `Assets/Editor/CharacterArchive/Tests/CharacterStoryQuestUiControllerTests.cs`
- `Assets/Editor/CharacterArchive/Tests/CharacterStoryQuestArchivePrefabTests.cs`

## 검증

- 프리팹 직렬화 정적 검증: QuestInfo에 컨트롤러 fileID 1개, 루트에는 없음, CharacterArchivePanel의 `storyQuestUi`가 동일 fileID를 참조, 두 percent 라벨과 동적 총진행 라벨이 명시 연결됨을 확인했다.
- Unity 2022.3.62f3 내장 C# 컴파일러로 runtime 및 editor 응답 파일을 직접 컴파일: 오류 0. 기존 프로젝트 경고 2개(`GlobalKeyboardHook`, `GlobalMouseWheelForwarder`의 미사용 필드)만 발생했다.
- `git diff --check`: 통과.
- 집중 EditMode는 활성 Unity 편집기가 동일 프로젝트를 열고 있어 별도 batch 인스턴스가 잠금으로 거부되어 실행하지 못했다. 이 작업은 실제 persistentDataPath에 접근하지 않았고, 원격 푸시도 하지 않았다.

## 사용자 수동 확인 항목

- Unity Test Runner에서 `CharacterArchiveEditorTests.CharacterStoryQuestUiControllerTests`, `CharacterArchiveEditorTests.CharacterStoryQuestArchivePrefabTests`, `QuestEditorTests.CharacterStoryQuestSceneWiringTests`를 실행한다.
- Play Mode에서 진행률 percent, 01_UI/87 총진행 형식, 09_Quest 단일/복수/무대상 번역, locale 전환 후 재표시를 확인한다.
- Ready 상태 완료 클릭 후 즉시 다음 퀘스트 전환 및 CharacterInfo ↔ QuestInfo 반복 토글을 확인한다.
