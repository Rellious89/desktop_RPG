# Character Story Quest UI — Phase 13C 완료 보고서

## 결과

- 작업 브랜치: `save-system` (시작 HEAD `d849055eb1195d0e243aa6cc2b0f3f7faae4add3`)
- SaveData: v8 유지. SaveData, 마이그레이션, 캐릭터/파티/전투 규칙은 변경하지 않았다.
- Generated 범위: `CharacterStoryQuest`와 `CharacterStoryQuestObjective`만 전용 Rebuild를 실행했다. 최신 CSV와 달랐던 `Quest_CatKnight_10002` 및 `Quest_CatKnight_10003`의 09_Quest title/description 키가 동기화되었다.
- CSV: `Assets/TableData/Game/CharacterStoryQuest.csv`는 값 변경 없이 LF와 최종 개행으로 정규화했다.

## 구현

- `CharacterStoryQuestUiController`를 추가해 명시적으로 연결된 Quest/Objective/Monster/Dungeon Catalog만 사용한다.
- Objective condition type별 09_Quest 제목·내용 템플릿, 대상 없음/단일/복수 대상 표시, 안전한 ID fallback, locale 변경 재표시를 구현했다. CharacterStoryQuest의 title/description은 이 UI에서 읽지 않는다.
- enabled Objective를 display_order로 모두 복제해 각 줄의 진행도를 표시하고, CurrentProgress는 목표별 clamp 비율의 동일 가중 평균으로 계산한다.
- TotalProgress는 선택 캐릭터의 enabled 서사 퀘스트를 기준으로 완료 수와 현재 퀘스트 진행도를 합산한다. 활성 퀘스트가 없거나 졸업한 상태도 안전하게 처리한다.
- 완료 버튼은 ReadyToComplete일 때만 활성화하며, 클릭은 TryConfirmComplete를 한 번만 호출하고 성공 시 즉시 다시 그린다.
- CharacterInfo/QuestInfo 전환과 Inspector 기본 페이지 복원을 연결했다.
- `pn_CharacterArchive`의 Current 아래에 `ObjectiveScroll/Viewport/Content` 단일 세로 ScrollRect를 만들고, QuestType과 QuestDesctiption을 Content의 자식으로 이동했다. Viewport는 RectMask2D, Content는 VerticalLayoutGroup + vertical Preferred Size ContentSizeFitter, ScrollRect는 vertical-only/clamped다. 동적 템플릿과 정적 장식 텍스트의 Raycast Target을 해제했다.

## 수정 파일

- `Assets/Art/UI/Prefab/panel/pn_CharacterArchive.prefab`
- `Assets/Scripts/CharacterArchive/CharacterArchivePanel.cs`
- `Assets/Scripts/CharacterArchive/CharacterStoryQuestUiController.cs`
- `Assets/Editor/CharacterArchive/CharacterStoryQuestUiPrefabSetup.cs`
- `Assets/Editor/CharacterArchive/Tests/CharacterStoryQuestUiControllerTests.cs`
- `Assets/Editor/CharacterArchive/Tests/CharacterStoryQuestArchivePrefabTests.cs`
- `Assets/Editor/TableData/CharacterStoryQuestRebuildCommand.cs`
- `Assets/TableData/Game/CharacterStoryQuest.csv`
- `Assets/Generated/TableData/CharacterStoryQuest/Quest_CatKnight_10002.asset`
- `Assets/Generated/TableData/CharacterStoryQuest/Quest_CatKnight_10003.asset`

## 검증

- Unity C# 컴파일 오류: 0.
- `CharacterArchiveEditorTests`: 8 passed, 0 failed. 대상 없음·단일·복수 ID fallback, 현재/전체 진행률, 기본 페이지 복원, 프리팹 ScrollRect 구성, desktopScene_ReSize 프리팹 연결을 포함한다.
- `QuestEditorTests`: 4 passed, 0 failed. 기존 CharacterStoryQuestService와 desktopScene_ReSize 서비스 연결 회귀를 포함한다.
- `git diff --check`: 통과.
- 전용 Rebuild: 오류 0, 다른 Generated 도메인 변경 0.

## 미실행 및 수동 확인

- 전체 EditMode/PlayMode/Sol 검증은 범위 밖이라 실행하지 않았다.
- 실제 persistentDataPath는 접근하지 않았다. 따라서 실제 저장을 통한 Ready → 완료 → 다음 퀘스트 전환, 게임 중 locale 전환, 실제 스크롤 감촉은 에디터 Play Mode에서 한 번 수동 확인이 필요하다.
- 구현 커밋: `250e10105bd270dd7164306c615a4126f37adaba` (`feat: connect character story quest archive UI`).
