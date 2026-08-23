# Character Origin World Foundation — 9.3D-A 완료 보고서

작성일: 2026-08-23
대상: `desktop_RPG` / `save-system`

## 결과

`Character.csv`의 `origin_world_id`를 `CharacterDefinition.originWorld`의 `WorldDefinition` 참조로 연결했다. Character-only Rebuild는 기존 World 생성 에셋을 정확히 하나인지 읽어 검증하지만 쓰거나 dirty로 만들지 않으며, 생성 CharacterDefinition 여섯 개만 CSV의 origin에 맞춰 갱신한다.

| Character | CSV `origin_world_id` | 생성 참조 |
| --- | ---: | --- |
| CatKnight | 1 | World_1 |
| CatMage | 1 | World_1 |
| RabbitHealer | 1 | World_1 |
| ElfArcher | 2 | World_2 |
| Barbarian | 2 | World_2 |
| ElfGuardian | 2 | World_2 |

## 범위

- 변경: Character CSV 스키마/행 검증, `CharacterDefinition` 직렬화 참조, Character-only Rebuild의 World 읽기 전용 해석, Character 관련 EditMode 시험, 생성 CharacterDefinition 6개.
- 포함: 이전 9.3C 보고서의 승인된 문구 교정(후보 저장 전환은 다음 UTC 주기부터 시작).
- 제외: 씬/프리팹, Recruitment UI, SaveData, 후보 획득·반환, Localization, World CSV, World Generated, 실제 `persistentDataPath`, 원격 push.

## Character-only Rebuild 검증

`TableDataMenu.RebuildCharacterTables`를 실행했다. 검증 결과는 오류 **0**, 경고 16이며, 생성 0개/갱신 9개(Character 6, Skill/CharacterSkill 카탈로그 포함)였다.

Rebuild 전후 `Assets/Generated/TableData/World`의 최상위 모든 파일을 SHA-256으로 비교해 **byte-identical**임을 확인했다. 별도 경계 시험도 Rebuild 중 World 파일 목록과 바이트가 바뀌지 않는지 확인한다.

## EditMode 검증

PlayMode 또는 Run All 없이 Character 범위의 EditMode 픽스처만 실행했다.

| 픽스처 | 결과 |
| --- | ---: |
| `TableDataEditor.Tests.CharacterTableTests` | 55 / 55 통과 |
| `TableDataEditor.Tests.CharacterOriginWorldRebuildTests` | 1 / 1 통과 |
| `TableDataEditor.Tests.CharacterTableOutputTests` | 31 / 31 통과 |

출력 시험은 Rebuild 전 최초 30 / 31이었다. 유일한 실패는 아직 갱신하지 않은 `CatKnight.originWorld == null`이었고, 위의 명시적 Character Rebuild 뒤 31 / 31로 통과했다. Unity C# 컴파일 오류는 **0**, `git diff --check`도 통과했다.

## 저장·원격

이 작업은 `persistentDataPath`를 읽거나 쓰지 않았고, push를 포함한 원격 작업을 수행하지 않았다.
