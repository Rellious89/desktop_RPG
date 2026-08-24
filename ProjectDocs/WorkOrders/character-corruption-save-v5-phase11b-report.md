# 11B 캐릭터 현재 오염도 저장 v5

## 결과

- 구현 커밋: 아래 후속 커밋
- SaveData: v5
- v4→v5: null 항목과 알 수 없는 ID를 보존하고, 각 실제 캐릭터의 `currentCorruption`을 0으로 초기화한다.
- 신규 지급: 새 게임 기본 보유 및 모집 획득에서 `CharacterDefinition.BaseCorruption`을 사용한다.
- 저장 정규화: 음수만 0으로 보정하며 Config 상한은 저장 계층에서 적용하지 않는다.

## 검증

- 집중 EditMode: 139/139 통과 (SaveMigration, SaveReset, OwnedCharacterCollection, Recruitment resolution).
- Unity C# 컴파일 오류 0, `git diff --check` 통과.
- CSV·Generated·씬·프리팹 변경 없음. 실제 persistentDataPath 및 원격 푸시는 사용하지 않았다.
