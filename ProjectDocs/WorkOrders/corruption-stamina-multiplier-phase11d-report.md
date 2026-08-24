# KeyBuddy 11D — 오염도 구간별 행동력 소모 배율

## 커밋

- 구현: `cd4971d5` — `Apply corruption stamina cost multipliers`

## 구현 결과

- `CorruptionStaminaCostPolicy`가 현재 오염도, Character BaseCorruption, default CorruptionConfig와 기본 행동력 비용을 받아 최종 비용을 순수하게 계산한다.
- 판정값은 유한한 0 이상 값으로 정리하고 BaseCorruption 이상, Config MaxCorruption 이하로 제한한다.
- `WarningThresholdPercent`와 `DangerThresholdPercent`는 각각 포함 경계이며, Config의 Warning/Danger 행동력 배율을 사용한다. 기본 구간은 1배다.
- 비용 곱셈은 `int.MaxValue`에서 포화해 정수 오버플로를 만들지 않는다.
- CharacterRoster는 승인된 처치에서 현재 캐릭터를 먼저 확정하고 정책이 계산한 비용을 기존 `SpendStamina`에 한 번만 전달한다. 따라서 기존 저장, CharacterStateChanged, 행동력 0 자동 교체, 중복 처치 필터는 유지된다.
- Catalog 또는 default Config가 없거나 무효이면 기존 기본 비용(1배)을 사용한다. 실제 플레이 중 구성 누락 경고는 인스턴스당 한 번만 기록한다.
- `desktopScene_ReSize.unity`의 기존 CharacterRoster에 Generated CorruptionConfigCatalog 참조만 연결했다.

## 수정 파일

- `Assets/Scripts/Corruption/CorruptionStaminaCostPolicy.cs`
- `Assets/Scripts/Character/CharacterRoster.cs`
- `Assets/Scenes/desktopScene_ReSize.unity`
- `Assets/Editor/Corruption/Tests/CorruptionStaminaCostPolicyTests.cs`
- `Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs`

## 집중 검증

- EditMode: `CorruptionStaminaCostPolicyTests`, `CharacterRosterCatalogTests`, `DungeonCorruptionSettlementServiceTests`
- 결과: **75/75 통과**, 실패 0
- 검증 항목: 50%·80% 포함 경계, Config 배율, BaseCorruption 하한, 잘못된 수치, Config 폴백, 비용 포화, 처치 한 번 저장/상태 이벤트, 3배 소진 뒤 자동 교체, 중복 처치 차단, 대상 씬 Catalog 연결
- Unity C# 컴파일 오류: **0** (집중 EditMode 실행 중 확인)
- `git diff --check`: 통과
- SaveData: **v5 유지**
- CSV, Generated 에셋, 프리팹, Localization: 변경 없음
- 씬 변경: `desktopScene_ReSize.unity`의 CharacterRoster Catalog 참조 한 개만 추가
- 실제 persistentDataPath: 사용하지 않음
- 원격 푸시: 하지 않음
