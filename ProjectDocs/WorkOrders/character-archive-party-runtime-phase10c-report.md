# KeyBuddy 10C

## 10C-A

- 명부는 `CharacterCatalog` 순서의 전체/보유 필터를 읽기 전용으로 표시한다.
- 카드와 상세 카드는 캐릭터·출신 월드 Localize 참조를 구독하고, 패널을 닫을 때 해제한다.
- `pn_CharacterArchive`, 카드 프리팹 및 씬은 컴포넌트와 Inspector 참조만 연결했다.
- 검증: `OwnedCharacterCollectionTests` 21/21 통과, Unity C# 컴파일 오류 0, `git diff --check` 통과.
