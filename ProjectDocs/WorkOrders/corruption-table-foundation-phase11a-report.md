# 11A 오염도 테이블 런타임 기반

- 구현 기준: `2d26169e`
- 구현 커밋: 아래 후속 커밋
- SaveData: v4 유지(변경·마이그레이션 없음)

## 반영 내용

- Character.csv의 `base_corruption`을 `CharacterDefinition.BaseCorruption`으로 연결했고 production 캐릭터 값은 모두 0이다.
- Dungeon.csv의 `corruption_interval_seconds` 및 `corruption_gain_per_interval`을 `DungeonDefinition`에 연결했다.
- `CorruptionConfigDefinition`/`CorruptionConfigCatalog`, CSV 검증, 전체 및 CorruptionConfig 전용 Rebuild, Generated Definition/Catalog를 추가했다.
- `default`는 300 / 50 / 80 / 2 / 3으로 생성되며 활성 행만 Catalog에 들어간다.

## 검증

- 집중 EditMode: 129/129 통과 (Character, Dungeon, PartyConfig, CorruptionConfig 및 출력 범위).
- Unity C# 컴파일: 오류 0.
- 전체 Rebuild 및 CorruptionConfig 전용 Rebuild 성공.
- 전용 Rebuild 전후 CorruptionConfig 외 Generated 파일 SHA-256 동일.
- `git diff --check` 통과.

## 범위

- 씬·프리팹·Localization·SaveData는 변경하지 않았다.
- 새 Generated 폴더와 Character/Dungeon Generated 에셋을 현재 CSV 기준으로 재생성했으며, 기존 meta GUID는 유지했다.
- 실제 persistentDataPath와 원격 푸시는 사용하지 않았다.
