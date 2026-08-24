# 11B-1 캐릭터 오염도 소수 저장

- 구현 커밋: 아래 후속 커밋
- `CharacterSaveState.currentCorruption`을 `double`(기본 `0d`)로 변경했고 SaveData v5는 유지했다.
- 정규화는 음수·NaN·양/음 무한대를 `0d`로 바르며, 유한 소수 및 Config 최대치를 넘는 원시값은 보존한다.
- 새 게임과 모집 획득은 기존 `BaseCorruption`을 double 값으로 저장한다.
- 집중 EditMode: 140/140 통과. Unity C# 컴파일 오류 0, `git diff --check` 통과.
- CSV·Generated·씬·프리팹 및 실제 persistentDataPath는 변경·사용하지 않았고 원격 푸시도 하지 않았다.
