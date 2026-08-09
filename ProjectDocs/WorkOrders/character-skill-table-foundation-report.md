# Character / Skill / CharacterSkill Table Foundation 완료 보고서

작성일: 2026-08-09
대상 프로젝트: `desktop_RPG`
단계: KeyBuddy 2단계 (테이블 기반 캐릭터·스킬 정적 데이터)
현재 저장 스키마: v1 (이번 단계에서 변경 없음)

## 1. 최종 CSV 스키마

세 표 모두 `Assets/TableData/Game/` 아래에 두며 UTF-8 / CRLF / 파일 끝 개행 없음으로 기존 다섯 표와 같다. `$`로 시작하는 컬럼은 작업자용 참조 컬럼이며 임포터가 읽지도 검증하지도 생성 에셋에 넣지도 않는다.

### Character.csv

```csv
character_id,name_category,name_key,$character_name,motion_profile_key,portrait_key,base_max_health,max_stamina,display_order,enabled,memo
```

실제 행 6개. `display_order`는 현재 씬 로스터 순서를 그대로 옮긴 임시값이다.

| character_id | name_key | motion_profile_key | portrait_key | base_max_health | max_stamina | display_order | enabled |
| --- | --- | --- | --- | --- | --- | --- | --- |
| CatKnight | 1 | CatKnight_MotionProfile | sp_character_icon | (공란) | 30 | 10 | 1 |
| ElfArcher | 2 | ElfArcher_MotionProfile | (공란) | (공란) | 30 | 20 | 1 |
| Barbarian | 3 | Barbarian_MotionProfile | (공란) | (공란) | 30 | 30 | 1 |
| ElfGuardian | 4 | ElfGuardian_MotionProfile | (공란) | (공란) | 30 | 40 | 1 |
| RabbitHealer | 5 | RabbitHealer_MotionProfile | (공란) | (공란) | 30 | 50 | 1 |
| CatMage | 6 | CatMage_MotionProfile | (공란) | (공란) | 30 | 60 | 1 |

`name_category`는 전부 `6`(신규 `06_Character` 카테고리)이다. CatKnight만 기존 수동 에셋이 쓰던 초상화를 그대로 이어받았고, 나머지는 공란으로 두어 런타임의 Base Idle 첫 프레임 폴백을 쓴다.

### Skill.csv

```csv
skill_id,name_category,name_key,$skill_name,description_category,description_key,$skill_description,icon_key,skill_type,behavior_key,display_order,enabled,memo
```

실제 행 0개(헤더만).

### CharacterSkill.csv

```csv
character_id,$character_name,skill_id,$skill_name,required_character_level,display_order,enabled,memo
```

실제 행 0개(헤더만).

### 로컬라이징 교환 파일

`ProjectDocs/DesignRules/localization-workflow.md` §6 / §8 계약에 따라 최초 Export 스냅샷을 함께 저장했다.

```csv
TableData/Localization/06_Character.csv
Key,Id,English(en),Korean (South Korea)(ko-KR)
1,347070464,CatKnight,CatKnight
2,359653376,ElfArcher,ElfArcher
3,359653377,Barbarian,Barbarian
4,363847680,ElfGuardian,ElfGuardian
5,363847681,RabbitHealer,RabbitHealer
6,363847682,CatMage,CatMage
```

`Id`는 `Assets/Localization/Tables/06_Character/06_Character Shared Data.asset`이 실제로 들고 있는 Unity 내부 Key ID다. Table Collection GUID는 `0cb4ffaa38b6b4d8a9ad892beab1142d`이다.

**en과 ko-KR에 같은 영어 임시값을 넣은 것은 사용자 작업 지시에 따른 명시적 예외다.**

일반 워크플로(문서 §5)는 "번역이 없는 Locale 셀은 비워 둔다. 영어 문구를 복사해 채우지 않는다"이고, 공란으로 두면 §9의 영어 fallback이 동작한다. 그러나 이번 작업의 지시는 **"한국어 이름이 확정되지 않았으면 기존 표시명을 임시값으로 넣는다"** 였고, 이 지시가 일반 워크플로보다 우선한다. 검토 과정에서 나온 "ko-KR을 공란으로 두자"는 제안은 **적용하지 않았다.**

따라서 현재 상태는 워크플로 위반이 아니라 **지시된 예외**이며, 다음 두 가지를 함께 기록해 둔다.

- 승인된 한국어 이름이 나오면 이 CSV의 ko-KR 열 6칸만 교체해 `CSV(Merge)`로 반영한다. String Table 에셋의 Entry ID는 그대로 유지된다.
- ko-KR 값이 영어와 같으므로 **지금은 fallback 경로가 실행되지 않는다.** 공란 정책으로 되돌리려면 그때 §9 fallback 동작을 한 번 확인하면 된다.

시험(`CharacterLocalizationBindingTests`)이 en/ko-KR 여섯 값과 교환 CSV를 이 상태 그대로 고정하므로, 누군가 한쪽만 바꾸면 즉시 실패한다.

## 2. 모든 필드의 의미와 검증 규칙

공통 규칙: **어떤 칸도 값을 고쳐서 통과시키지 않는다.** 소문자화·공백 제거·하한 보정이 어디에도 없다. 비교는 전부 `StringComparer.Ordinal`이다.

### Character.csv

| 컬럼 | 의미 | 검증 |
| --- | --- | --- |
| `character_id` | 저장 데이터(`SaveData.characters`)와 표가 공유하는 키 | 필수. **표준 ID 형식 또는 legacy 6종**(3장). 중복은 오류이며 먼저 나온 행이 남는다. 앞뒤 공백은 형식 오류 |
| `name_category` / `name_key` | 표시 이름의 카테고리 번호 + 숫자 키 | `enabled=1`이면 둘 다 필수. 한쪽만 있으면 오류. 프로젝트에 실제로 있는 Entry여야 하며 없으면 오류 |
| `$character_name` | 작업자용 참조 | 읽지 않음 |
| `motion_profile_key` | `CharacterMotionProfile` 에셋 이름 | **활성/비활성과 무관하게 필수.** 이름 완전 일치로 찾고, 0개·2개 이상이면 오류. 찾은 뒤 `CharacterMotionProfile.IsPlayable`(Base Idle에 프레임 1장 이상)까지 확인해 실패하면 오류. `MonsterMotionProfile`은 타입이 달라 여기 쓸 수 없다 |
| `portrait_key` | 초상화 Sprite 이름 | 선택. 공란은 경고이며 런타임이 Base Idle 첫 프레임으로 대신한다. **이름을 적었는데 못 찾거나 여럿이면 오류**(사람이 적었다는 것은 그 그림을 쓰겠다는 뜻이므로 조용히 비우지 않는다) |
| `base_max_health` | 기본 최대 체력 | **선택. 공란이 정상이며 "아직 정하지 않음"을 뜻한다**(경고조차 남기지 않는다). 값이 있으면 정수 1 이상이어야 하고 0·음수·문자는 오류 |
| `max_stamina` | 최대 행동력 | 필수. 정수 1 이상. 0·공란은 오류 |
| `display_order` | 목록 정렬용 | 필수. 정수 0 이상. 음수는 오류. 같은 값이 겹치면 경고(오류 아님) |
| `enabled` | 카탈로그 포함 여부 | 정확히 `1` 또는 `0`. `true`/`TRUE`는 오류. `0`이어도 Definition 에셋은 만들며 카탈로그에서만 빠진다 |
| `memo` | 사람이 읽는 칸 | 검증하지 않음 |

### Skill.csv

| 컬럼 | 의미 | 검증 |
| --- | --- | --- |
| `skill_id` | 스킬 키 | 필수. **표준 ID 형식만.** 캐릭터 표의 legacy 예외는 이 표에 적용되지 않는다. 중복은 오류 |
| `name_category` / `name_key` | 스킬 이름 | `enabled=1`이면 필수. 실재 Entry여야 함 |
| `description_category` / `description_key` | 스킬 설명 | **완전한 선택 항목.** 둘 다 공란이면 오류도 경고도 없다. 한쪽만 채우면 오류. 둘 다 있으면 실재 검사 |
| `icon_key` | 아이콘 Sprite 이름 | 선택. 공란은 경고, 이름을 적었는데 못 찾거나 여럿이면 오류 |
| `skill_type` | 분류 키 | 선택. 값이 있으면 소문자 키 형식(`^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$`). 숫자만·대문자·공백 포함은 오류 |
| `behavior_key` | 나중에 동작을 고를 때 쓸 키 | `skill_type`과 같은 규칙. **지금 이 값을 해석하는 코드는 없다** |
| `display_order` / `enabled` / `memo` | Character.csv와 동일 | 동일 |

소문자 키 규칙은 ID 규칙보다 엄격하다. ID는 `101` 같은 숫자 문자열을 허용하지만 키는 허용하지 않는다 — 숫자만으로 된 분류/동작 키는 나중에 무엇으로 읽어야 할지 알 수 없기 때문이다.

### CharacterSkill.csv

| 컬럼 | 의미 | 검증 |
| --- | --- | --- |
| `character_id` | 관계의 앞쪽 | 필수. Character.csv와 같은 id 규칙. **Character.csv에 실재해야 한다** |
| `skill_id` | 관계의 뒤쪽 | 필수. Skill.csv와 같은 id 규칙. **Skill.csv에 실재해야 한다** |
| `required_character_level` | 표에 적힌 필요 레벨 | 필수. 정수 1 이상, **상한 없음**. 0·공란은 오류 |
| `display_order` / `enabled` / `memo` | 위와 동일 | 동일 |

관계 고유성은 `(character_id, skill_id)` **짝**이다. 한 캐릭터가 여러 스킬을, 한 스킬이 여러 캐릭터를 가질 수 있으므로 어느 한쪽만으로는 행을 특정할 수 없다. 짝이 중복되면 오류이며 먼저 나온 행이 남는다.

활성 규칙: `enabled=1`인 관계는 **활성 캐릭터와 활성 스킬만** 가리킬 수 있다. `enabled=0`인 관계는 비활성 대상을 가리켜도 되지만, **없는 id를 가리키는 것은 비활성이어도 오류**다 — 잘못된 참조를 통과시키면 다시 켜는 순간 조용히 깨진다.

### 검증·생성 순서

```text
World → Currency → Item → Monster → Dungeon → Character → Skill → CharacterSkill
```

가리켜지는 표가 언제나 먼저 온다. 캐릭터 쪽 세 표를 뒤에 붙인 이유는 이 셋이 앞의 다섯 표를 하나도 참조하지 않아 앞의 순서를 건드리지 않고 이어 붙일 수 있기 때문이다.

## 3. legacy 캐릭터 ID 예외

기존 6종의 id는 PascalCase다.

```text
Barbarian, CatKnight, CatMage, ElfArcher, ElfGuardian, RabbitHealer
```

이 값들은 `SaveData.characters[].characterId`와 씬 로스터가 이미 쓰고 있어 **한 글자도 바꿀 수 없다.** snake_case로 정리하는 순간 기존 저장 항목과의 연결이 끊긴다.

그래서 예외를 **전역 ID 규칙이 아니라 Character 전용 검사에만** 두었다.

- 전역 정규식 `^(?:[1-9][0-9]*|[a-z][a-z0-9]*(?:_[a-z0-9]+)*)$`는 **한 글자도 바꾸지 않았다.** 다섯 개 기존 표의 id 검사는 조금도 헐거워지지 않았다(테스트로 고정).
- `TableDataFieldRules.IsValidCharacterId`만 `표준 ID 형식 OR 위 6개와 Ordinal 완전 일치`를 허용한다.
- 위 6개에 없는 PascalCase는 Character.csv에서도 오류다. 프로젝트에 실제로 있는 테스트 캐릭터 `IceMage`, `Leopard`도 거부된다.
- 예외 목록은 늘리지 않는다. 새로 만드는 캐릭터는 표준 ID 형식을 쓴다(테스트가 목록 내용을 고정한다).
- 값 자동변환은 없다. `catknight`는 그 자체로는 올바른 표준 id지만 `CatKnight`와 **다른 값**이며, 대소문자를 맞춰 하나로 합치는 경로는 어디에도 없다.

## 4. Definition / Catalog 구조

### CharacterDefinition (확장)

기존 공개 API와 직렬화 필드를 그대로 두고 **뒤에만** 칸을 추가했다. 기존 수동 에셋은 그대로 읽히며 없는 칸은 Unity가 기본값으로 채운다.

| 구분 | 멤버 |
| --- | --- |
| 기존(변경 없음) | `CharacterId`, `DisplayName`, `MotionProfile`, `Portrait`, `MaxStamina` |
| 신규 | `LocalizedName`, `HasLocalizedName`, `HasBaseMaxHealth`, `BaseMaxHealth`, `DisplayOrder` |

- `LocalizedName`은 절대 null을 돌려주지 않으며 **`DisplayName`에 끼어들지 않는다.** 표시 이름 경로를 옮기는 것은 이번 범위가 아니다.
- 기본 체력은 `hasBaseMaxHealth`(bool) + `baseMaxHealth`(int) **두 칸**이다. 한 칸으로 합치면 빈 CSV 칸과 0을 구분할 수 없다. `BaseMaxHealth`는 미지정이면 0을 돌려주므로 반드시 `HasBaseMaxHealth`를 먼저 봐야 한다.
- 임포터는 `displayName` 칸을 **쓰지 않는다.** 사람이 손으로 채우던 칸이고 지금도 화면 이름의 근거이므로 덮어쓰면 표시 이름이 조용히 달라진다(`ItemDefinition.displayName`과 같은 규칙).

### 신규 에셋 5종

| 타입 | 위치 | 내용 |
| --- | --- | --- |
| `CharacterCatalog` | `Assets/Scripts/Character/` | 캐릭터 목록의 순서와 구성 |
| `SkillDefinition` | `Assets/Scripts/Skill/` | 스킬 한 종의 정적 정의(식별자, 이름/설명, 아이콘, 분류·동작 키, 순서) |
| `SkillCatalog` | `Assets/Scripts/Skill/` | 스킬 목록 |
| `CharacterSkillDefinition` | `Assets/Scripts/Skill/` | 관계 한 줄(id 2개 + 참조 2개 + 필요 레벨 + 순서) |
| `CharacterSkillCatalog` | `Assets/Scripts/Skill/` | 관계 목록 |

카탈로그 세 개는 모두 기존 `ItemCatalog` / `CurrencyCatalog`와 같은 패턴이다 — 목록 필드 하나, 검사 결과 캐시, `MarkDirty()`, `OnEnable`/`OnValidate` 무효화, 빈 칸·식별자 없음·중복 제외(먼저 작성된 항목이 남음). **읽는 쪽은 프로젝트를 뒤지지 않고 카탈로그 에셋 하나만 읽는다.**

정렬은 목록을 채우는 임포터의 몫이다.

| 카탈로그 | 정렬 |
| --- | --- |
| `CharacterCatalog` | `display_order` asc → `character_id` Ordinal asc |
| `SkillCatalog` | `display_order` asc → `skill_id` Ordinal asc |
| `CharacterSkillCatalog` | `display_order` asc → `character_id` → `skill_id` Ordinal asc |

관계는 **세 단계 비교**를 따로 구현했다. 짝 키를 이어 붙인 문자열(`a__b`)의 Ordinal 순서는 두 id를 차례로 비교한 순서와 다르기 때문이다 — 구분자 `_`(0x5F)가 숫자보다 뒤에 와서 캐릭터 `a`와 `a1`의 앞뒤가 뒤집힌다.

관계 에셋의 파일 이름 구분자는 밑줄 **두 개**다. snake_case는 밑줄이 연달아 올 수 없고 legacy PascalCase에는 밑줄이 없으므로, `a_b`+`c`와 `a`+`b_c`가 같은 키가 되는 경우가 생기지 않는다.

### 생성 결과

```text
Assets/Generated/TableData/Character/        Character_{6종}.asset + CharacterCatalog.asset
Assets/Generated/TableData/Skill/            SkillCatalog.asset (빈 목록)
Assets/Generated/TableData/CharacterSkill/   CharacterSkillCatalog.asset (빈 목록)
```

`CharacterCatalog`의 실제 순서는 `CatKnight → ElfArcher → Barbarian → ElfGuardian → RabbitHealer → CatMage`로 현재 로스터 순서와 같다.

### 범위를 좁힌 Rebuild가 실제로 하는 일

`TableDataRebuilder.Rebuild(TableDataRebuildScope)`의 범위 값은 **두 개뿐**이며(`All`, `CharacterSkillTables`), 그 밖의 값은 `TableDataRebuildScopes.EnsureSupported`가 **아무것도 하기 전에** `ArgumentOutOfRangeException`으로 막는다. C#의 enum은 아무 정수나 캐스팅해 넣을 수 있어서, 검사 없이 `if (scope == All)` 하나만 지나가면 의도한 적 없는 값이 "기존 도메인은 건드리지 않는다" 분기로 흘러 들어간다.

범위가 정하는 것과 정하지 않는 것을 나눠 두었다.

| | 범위와 무관하게 언제나 | 범위가 정함 |
| --- | --- | --- |
| CSV 파일/헤더/행/값 검증 | 여덟 표 전부 | — |
| 표 사이 참조 무결성 | 여덟 표 전부 | — |
| Localization Entry / Motion Profile / Sprite / 수동 ItemDefinition 조회 | 전부(입력 자산이다) | — |
| 직렬화 레이아웃 probe | 전부 | — |
| **생성 에셋 중복/경로 충돌/orphan 점검** | — | 범위 안 폴더만 |
| **생성/갱신/저장** | — | 범위 안 대상만 |

즉 **검사가 느슨해지는 곳은 없고**, 좁아지는 것은 "출력 쪽 생성 에셋을 어디까지 여는가"뿐이다.

범위 판정은 `TableDataValidator.GeneratedOutputFolders(scope)` **한 곳에서만** 이루어진다. 두 점검(충돌/orphan)은 그 결과를 펼친 집합을 도메인마다 `InScope(selected, folder)`로 **직접 물어보고** 열지 말지를 정한다 — 목록을 만드는 기준과 검사가 분기하는 기준이 따로 존재하지 않으므로, 둘이 어긋나 "목록에는 없는데 실제로는 열리는" 상태가 생길 수 없다. 시험이 이 소비 관계를 직접 확인한다(진단이 가리키는 생성 에셋 경로가 언제나 선택 목록 안에 있다).

`CharacterSkillTables` 범위에서 기존 다섯 도메인의 생성 폴더는 `LoadGeneratedById`도 `LoadAssetAtPath`도 호출되지 않는다. 이것은 말이 아니라 관측 가능한 사실이다 — 전체 범위에서 나오던 기존 도메인의 orphan 경고 4건이 좁은 범위에서는 **나오지 않으며**(경고 34건 → 30건), 시험이 그 차이를 고정한다.

저장도 마찬가지로 좁혔다. 좁은 범위 경로는 **전역 `AssetDatabase.SaveAssets()`를 호출하지 않는다.** 그것은 프로젝트에서 dirty 상태인 모든 에셋을 디스크에 쓰는 동작이라, 사람이 인스펙터에서 고쳐 두고 아직 저장하지 않은 무관한 에셋까지 이 Rebuild의 이름으로 함께 기록된다. 대신 이번에 만들거나 다시 쓴 대상(캐릭터 정의 + 스킬 정의 + 관계 정의 + 카탈로그 3개)만 모아 하나씩 `AssetDatabase.SaveAssetIfDirty`로 저장한다. 목록은 참조 동일성으로 걸러 **중복 저장도 누락도 없다.** 전체 범위(`All`)의 기존 `SaveAssets()` 동작은 **바꾸지 않았다.**

`TableDataValidator.Validate()`와 `TableDataRebuilder.Rebuild()`의 기존 무인자 형태는 그대로 남아 있고 동작도 예전과 같다(둘 다 `All`).

## 5. 수동 에셋과 생성 에셋의 공존

`Assets/Data/Characters/`의 수동 `CharacterDefinition` 8종은 **그대로 두었다.** 이동도 삭제도 하지 않았고 `CharacterRoster`와 씬 연결도 건드리지 않았다.

따라서 지금은 같은 `character_id`를 가진 정의가 두 벌 존재한다.

| | 수동 | 생성 |
| --- | --- | --- |
| 위치 | `Assets/Data/Characters/` | `Assets/Generated/TableData/Character/` |
| 개수 | 8 (6종 + 테스트용 IceMage, Leopard) | 6 |
| 쓰는 곳 | `CharacterRoster`(씬) — **현재 플레이 경로** | `CharacterCatalog` — 아직 아무도 읽지 않음 |

**이 공존은 충돌 오류가 아니다.** Item.csv는 같은 `item_id`를 가진 수동 `ItemDefinition`을 오류로 막지만 캐릭터는 **일부러 다르게** 했다 — 그 규칙을 그대로 적용하면 표를 만들자마자 6행 전부가 실패한다. 임포터에 캐릭터용 수동 에셋 충돌 검사를 넣지 않았고, 테스트가 "같은 id의 수동 정의가 있어도 오류 0"을 고정한다.

경계는 경로로 보장된다. 생성 폴더와 `Assets/Data`는 서로를 포함하지 않으며(테스트로 고정), 임포터의 조회·생성·정리 동작은 전부 자기 출력 폴더 안에서만 일어난다. `CharacterCatalog`에는 생성 에셋만 들어가며 수동 에셋은 들어오지 않는다(테스트로 고정).

정리 시점은 로스터를 생성 에셋으로 옮기는 후속 단계다. legacy id를 그대로 보존한 이유가 그때 저장 데이터를 건드리지 않고 갈아 끼우기 위함이다.

## 6. Skill / CharacterSkill을 비워 둔 이유

두 표는 헤더만 있고 프로덕션 행이 0개다. 이는 미완성이 아니라 **이번 단계에서 의도한 완료 상태**다.

1. **아직 확정된 스킬이 없다.** 이름·설명·아이콘·분류·동작이 정해지지 않은 상태에서 예시 행을 넣으면, 그 행이 나중에 진짜 데이터인지 자리 채우기인지 구분할 수 없게 된다.
2. **이번 단계의 목표는 데이터가 아니라 통로다.** 표가 존재하고, 검증되고, 에셋으로 만들어지고, 카탈로그가 결정적으로 정렬된다는 것까지가 범위다. 스킬 한 줄을 넣는 순간 "그 스킬이 무엇을 하는가"라는 질문이 따라오고, 그것은 3단계의 일이다.
3. **빈 표가 정상 상태임을 코드와 시험이 보장한다.** 헤더만 있는 CSV는 오류도 경고도 남기지 않으며(테스트로 고정), 빈 카탈로그도 정상으로 다룬다. 그래서 첫 스킬을 추가할 때 파이프라인 쪽을 다시 손볼 일이 없다.

`behavior_key`도 같은 이유로 문자열일 뿐이다. 이 값을 실제 동작으로 바꾸는 해석기는 어디에도 없고, 만들더라도 정의 에셋 바깥에 있어야 한다 — 정의가 스스로 동작을 들고 있으면 데이터와 규칙이 한 덩어리가 되어 표만 고쳐서는 확인할 수 없게 된다.

## 7. 변경 파일 전체

### 수정 (12)

```text
Assets/Scripts/Character/CharacterDefinition.cs
Assets/Editor/TableData/TableDataPaths.cs
Assets/Editor/TableData/TableDataCsvReader.cs          (TableDataColumns)
Assets/Editor/TableData/TableDataRows.cs
Assets/Editor/TableData/TableDataFieldRules.cs
Assets/Editor/TableData/TableDataAssetIndex.cs
Assets/Editor/TableData/TableDataValidator.cs
Assets/Editor/TableData/TableDataRebuilder.cs
Assets/Editor/TableData/TableDataMenu.cs
Assets/AddressableAssetsData/AssetGroups/Localization-Assets-Shared.asset
Assets/AddressableAssetsData/AssetGroups/Localization-String-Tables-English (en).asset
Assets/AddressableAssetsData/AssetGroups/Localization-String-Tables-Korean (South Korea) (ko-KR).asset
```

Addressables 변경은 **그룹 3개에 `06_Character` 항목이 각각 1건씩 추가된 것**이 전부다(diff로 확인).

| 그룹 | 추가된 항목 |
| --- | --- |
| `Localization-Assets-Shared` | `06_Character Shared Data` 1건 |
| `Localization-String-Tables-English (en)` | `06_Character_en` 1건 |
| `Localization-String-Tables-Korean (South Korea) (ko-KR)` | `06_Character_ko-KR` 1건 |

`CharacterLocalizationBindingTests`가 그룹 파일마다 `06_Character` 항목이 **정확히 하나**인지 확인한다.

### 신규 - 런타임 코드

```text
Assets/Scripts/Character/CharacterCatalog.cs (+ .meta)
Assets/Scripts/Skill.meta
Assets/Scripts/Skill/SkillDefinition.cs (+ .meta)
Assets/Scripts/Skill/SkillCatalog.cs (+ .meta)
Assets/Scripts/Skill/CharacterSkillDefinition.cs (+ .meta)
Assets/Scripts/Skill/CharacterSkillCatalog.cs (+ .meta)
```

### 신규 - 테스트

```text
Assets/Editor/TableData/Tests/CharacterTableTests.cs (+ .meta)
Assets/Editor/TableData/Tests/SkillTableTests.cs (+ .meta)
Assets/Editor/TableData/Tests/CharacterSkillTableTests.cs (+ .meta)
Assets/Editor/TableData/Tests/CharacterTableOutputTests.cs (+ .meta)
Assets/Editor/TableData/Tests/CharacterLocalizationBindingTests.cs (+ .meta)
```

### 신규 - 입력 데이터

```text
Assets/TableData/Game/Character.csv (+ .meta)
Assets/TableData/Game/Skill.csv (+ .meta)
Assets/TableData/Game/CharacterSkill.csv (+ .meta)
TableData/Localization/06_Character.csv
```

### 신규 - Localization

```text
Assets/Localization/Tables/06_Character.meta
Assets/Localization/Tables/06_Character/06_Character.asset (+ .meta)
Assets/Localization/Tables/06_Character/06_Character Shared Data.asset (+ .meta)
Assets/Localization/Tables/06_Character/06_Character_en.asset (+ .meta)
Assets/Localization/Tables/06_Character/06_Character_ko-KR.asset (+ .meta)
```

### 신규 - 생성 에셋

```text
Assets/Generated/TableData/Character.meta
Assets/Generated/TableData/Character/Character_{Barbarian,CatKnight,CatMage,ElfArcher,ElfGuardian,RabbitHealer}.asset (+ .meta)
Assets/Generated/TableData/Character/CharacterCatalog.asset (+ .meta)
Assets/Generated/TableData/Skill.meta
Assets/Generated/TableData/Skill/SkillCatalog.asset (+ .meta)
Assets/Generated/TableData/CharacterSkill.meta
Assets/Generated/TableData/CharacterSkill/CharacterSkillCatalog.asset (+ .meta)
```

모든 `.meta`와 Localization / Generated 에셋은 **격리 클론에서 Unity가 직접 생성**한 뒤 신규 범위만 원본에 반영했다. GUID를 손으로 만들거나 복제하지 않았다.

## 8. 테스트 결과

실행 환경: 사용자 Unity 에디터가 프로젝트 락을 잡고 있으므로 APFS 클론에서 실행했다.

```bash
Unity -batchmode -nographics -projectPath <clone> \
      -runTests -testPlatform EditMode -testResults results.xml
```

**compile error 0 / 전체 EditMode 325 중 325 통과 / failure 0 / skip 0 / inconclusive 0**

| 테스트 클래스 | 통과 | 비고 |
| --- | --- | --- |
| `CommonEditor.Tests.SaveMigrationTests` | 59 / 59 | 기존 (저장 96건) |
| `CommonEditor.Tests.SaveStorageTests` | 19 / 19 | 기존 (저장 96건) |
| `CommonEditor.Tests.SaveSystemIntegrationTests` | 18 / 18 | 기존 (저장 96건) |
| `InventoryEditor.Tests.CurrencyCatalogTests` | 16 / 16 | 기존 |
| `InventoryEditor.Tests.DefeatRewardTests` | 25 / 25 | 기존 |
| `InventoryEditor.Tests.InventoryCurrencyOverflowTests` | 11 / 11 | 기존 |
| `TableDataEditor.Tests.CurrencyTableTests` | 28 / 28 | 기존 |
| `TableDataEditor.Tests.MonsterRewardRulesTests` | 25 / 25 | 기존 |
| **`TableDataEditor.Tests.CharacterTableTests`** | **40 / 40** | 신규 |
| **`TableDataEditor.Tests.SkillTableTests`** | **27 / 27** | 신규 |
| **`TableDataEditor.Tests.CharacterSkillTableTests`** | **22 / 22** | 신규 |
| **`TableDataEditor.Tests.CharacterTableOutputTests`** | **29 / 29** | 신규 |
| **`TableDataEditor.Tests.CharacterLocalizationBindingTests`** | **6 / 6** | 신규 |
| 합계 | **325 / 325** | 신규 124, 저장 96, 기타 기존 105 |

신규 124건이 고정하는 것: CSV 스키마와 컬럼 순서, 고정 경로, legacy ID 예외와 **전역 정규식 무변경**, 값 자동변환 없음, 각 칸의 오류/경고 판정, 빈 표 유효성, 참조 무결성과 활성 규칙, 짝 중복, 카탈로그 결정적 정렬, 짝 키 모호성 없음, stale 처리 정책, 출력 경로 충돌, 수동/생성 공존이 오류가 아님, 실제 생성 에셋의 순서와 값.

여기에 이번 보정으로 다음이 더해졌다.

- **지원하지 않는 범위 거부** — `(TableDataRebuildScope)999`가 `EnsureSupported` / `IncludesLegacyDomains` / `Rebuild` / `GeneratedOutputFolders` / `Validate` 다섯 곳에서 모두 `ArgumentOutOfRangeException`으로 막히는지.
- **범위별 출력 도메인 선택** — 좁은 범위가 새 세 폴더만 고르고 기존 다섯 폴더를 하나도 포함하지 않는지, 전체 범위는 여덟 폴더를 모두 고르는지.
- **범위가 실제로 관측되는지** — 전체 범위에서는 기존 도메인의 orphan 경고가 나오고 좁은 범위에서는 <b>나오지 않는</b>다(그 폴더를 열지 않았다는 관측 가능한 증거).
- **검사가 선택 목록을 실제로 소비하는지** — `Validate(scope)`가 낸 <b>진단이 가리키는 생성 에셋 경로</b>가 그 범위의 `GeneratedOutputFolders(scope)` 안에만 있는지. 전체 범위에서 기존 도메인을 가리키는 진단이 실제로 존재함(= 시험이 공허하지 않음)도 함께 확인한다.
- **Addressables 항목의 정확한 값** — 그룹 3개 각각에서 06_Character 항목을 찾아 `m_Address`가 정확히 기대 주소(`Assets/.../06_Character Shared Data.asset` / `06_Character_en` / `06_Character_ko-KR`)이고 `m_GUID`가 해당 에셋 `.meta`의 GUID와 같은지, 세 GUID가 서로 다른지.
- **빈 ID 생성 에셋** — orphan 경고 1건이 실제로 나오고 진단 값이 `(빈 ID)`로 읽히는지, 생성 폴더 조회가 빈 ID 에셋을 버리지 않는지, 그러면서 `CharacterDefinition`의 파일 이름 폴백은 깨지지 않는지.
- **로컬라이즈 참조의 실제 값** — 생성 CharacterDefinition 6개가 06_Character의 Table GUID와 숫자 키 1..6의 Entry Key ID를 정확히 가리키는지, Shared Data / en / ko-KR / 교환 CSV의 여섯 값이 서로 일치하는지, Addressables 그룹 3개에 06_Character 항목이 각각 1건씩인지.

테스트는 프로젝트의 기존 에셋을 쓰지 않는다. 대부분 메모리 `ScriptableObject`/`Sprite`와 읽기 전용 조회만 쓰고, 생성 폴더 조회처럼 실제 `AssetDatabase` 경로가 필요한 두 건만 `Assets/__TableDataTestsTemp` 아래에 임시 에셋을 만들었다가 TearDown에서 폴더째 지운다(실패해도 지운다 - 시험 실행 후 클론에 잔여물이 없음을 확인했다). Rebuild를 호출하는 테스트는 범위 거부 시험 하나뿐이며, 그것은 검증도 폴더 생성도 하기 전에 예외로 끝난다.

### 하네스로 확인한 이음매 (클론 전용)

| 항목 | 결과 |
| --- | --- |
| targeted rebuild 1회차 | `wrote=True created=9 updated=0`, 오류 0 |
| **기존 generated 5개 도메인** | targeted rebuild 전후 **70개 파일 byte-identical** (SHA256) |
| targeted rebuild 2회차 | `created=0 updated=9`, **생성 에셋 9개 GUID 전부 유지** |
| 2회차 후 기존 generated | 다시 **70개 파일 byte-identical** |
| full rebuild | **클론에서만 실행.** `wrote=True created=0 updated=40`, 오류 0 |
| 클론 전체 `diff -r` | delta가 신규 범위 + Addressables 3개 파일뿐 |

**full rebuild는 원본 작업 트리에서 실행하지 않았다.** 원본의 신규 3개 생성 폴더는 클론의 targeted rebuild 산출물을 복사한 것이며, 원본에서는 Unity 자체를 실행하지 않았다(사용자 에디터가 락을 잡고 있다).

### 저장 격리 증명 (클론 전용)

targeted 경로가 전역 `SaveAssets()`를 쓰지 않는다는 것을, **사람이 고쳐 두고 아직 저장하지 않은 에셋**을 미리 만들어 두고 확인했다.

준비: `Currency_jewel.asset`(기존 도메인 생성 에셋)과 `CatMage_CharacterDefinition.asset`(임포터와 무관한 수동 에셋)의 `displayOrder`를 메모리에서 고치고 `SetDirty`만 건 채 저장하지 않는다. 그 상태에서 targeted rebuild를 실행한다.

| 확인 | 결과 |
| --- | --- |
| 기존 도메인 생성 에셋의 디스크 내용 | **그대로**(SHA256 동일) |
| 무관한 수동 에셋의 디스크 내용 | **그대로**(SHA256 동일) |
| 두 에셋의 dirty 상태 | **여전히 dirty** — 우리가 대신 저장하지 않았다 |
| 기존 generated 5개 도메인 전체 | **70개 파일 byte-identical** |
| 이번 범위의 대상 9개 | **전부 저장됨**(`SaveAssetIfDirty` 후 dirty 아님) |

예전 구현(`AssetDatabase.SaveAssets()`)이었다면 위 두 에셋이 이 Rebuild의 이름으로 함께 디스크에 기록됐을 것이다.

### 실패 시 격리와 남은 위험 (클론 전용)

쓰기 도중 실패했을 때 무엇이 남는지도 실제로 실패를 만들어 확인했다. production 코드에 시험용 hook을 넣지 않기 위해, private 진입점에 **파일 이름으로 쓸 수 없는 id**를 가진 스냅샷을 넘겨 `CreateAsset`이 실제로 던지게 했다.

| 확인 | 결과 |
| --- | --- |
| 주입한 실패 발생 | `UnityException` |
| 기존 도메인 생성 에셋 / 무관한 수동 에셋 | 디스크 **무변경**, **여전히 dirty**(저장 단계가 아예 실행되지 않는다) |
| 기존 generated 5개 도메인 | **70개 파일 byte-identical** |
| `AssetDatabase` 상태 | 정상 — 이어서 실행한 targeted rebuild가 그대로 성공(`StopAssetEditing`이 `finally`에서 돌았다) |
| **부분 생성물** | **남는다** — 실패 직전에 만들어진 `Character_harness_probe_ok.asset`이 그대로 존재 |

마지막 줄이 **이번에 해결하지 않은 잔여 위험**이다. `ResolveTargets`는 쓰기 전에 `CreateAsset`으로 대상 에셋을 먼저 확보하므로, 그 도중 예외가 나면 **이미 만들어진 에셋이 남는다.** 이는 이번 보정 이전부터 있던 비트랜잭션 구조이며, 되돌리기 프레임워크를 새로 만드는 것은 이번 범위가 아니라고 판단해 **고치지 않고 정확히 기록**한다.

- 실제로 이 경로에 닿으려면 Validate를 통과한 뒤 `CreateAsset`이 실패해야 한다. 정상 데이터에서는 경로/타입 충돌을 Validate가 먼저 잡으므로 일어나지 않는다.
- 남은 부분 생성물은 다음 Validate에서 **orphan 경고**로 보인다(자동 삭제하지 않는다).
- 위험의 범위는 **신규 세 폴더 안쪽뿐**이다. 기존 도메인과 무관 에셋은 위 표대로 저장조차 되지 않는다.

### stale generated 처리 정책

CSV에서 사라진 생성 에셋은 **경고를 남기고 카탈로그에서 제외할 뿐 자동 삭제하지 않는다.** 이는 기존 다섯 도메인과 동일한 확립된 정책이며, 신규 세 도메인도 같은 규칙을 따른다. 삭제는 되돌릴 수 없고 씬·프리팹이 이미 그 에셋을 참조하고 있으면 참조를 끊고 GUID를 영원히 없애기 때문이다. 판정은 각 도메인의 출력 폴더 **안에서만** 이루어진다.

사용자 명세의 "stale generated 정리"는 이 **참조 안전성을 고려한 논리적 정리**(경고 + 카탈로그 제외)로 이행한 것이며, 파일 삭제를 뜻하지 않는다.

이번 보정에서 한 가지를 고쳤다. 예전에는 생성 폴더 조회가 **ID가 빈 에셋을 조용히 버려서**, 코드에 있던 `(빈 ID)` 진단이 실제로는 절대 나오지 않았다. `SkillDefinition.SkillId`와 `CharacterSkillDefinition.PairId`는 값이 없으면 빈 문자열을 돌려주므로(파일 이름으로 대체하지 않는다), 그런 에셋이 생성 폴더에 남아 있어도 아무도 알려 주지 않는 상태였다. 이제 빈 키를 하나의 그룹으로 보존해 **orphan 경고가 실제로 나온다.** `CharacterDefinition`은 빈 값일 때 에셋 이름을 돌려주므로 이 변경의 영향을 받지 않으며, 파일 이름 폴백이 그대로임을 시험이 고정한다. 여전히 **자동 삭제는 하지 않는다.**

## 9. 기존 변경 보존 확인

시작 시점: 브랜치 `save-system`, HEAD `841114db`, `git status` 완전 clean. **커밋하지 않았다.**

아래 경로에는 변경이 하나도 없다.

```bash
$ git status --porcelain -- \
    Assets/Scripts/Common Assets/Data Assets/Scenes Assets/Prefabs \
    Assets/TableData/Game/{World,Currency,Item,Monster,Dungeon}.csv \
    Assets/Generated/TableData/{World,Currency,Item,Monster,Dungeon} \
    Assets/Scripts/Character/CharacterRoster.cs
(출력 없음)
```

| 보호 대상 | 상태 |
| --- | --- |
| `SaveData.cs`, `SaveSystem.cs`, 1단계 저장 파일 전부 | 무변경 → **`saveVersion` 무변경(v1 유지)** |
| 씬 · 프리팹 | 무변경 |
| 기존 World / Currency / Item / Monster / Dungeon CSV | 무변경 |
| 기존 Generated/TableData 5개 도메인 | 무변경 (git + 클론 SHA256 양쪽 확인) |
| `Assets/Data` 수동 CharacterDefinition 8종 | 무변경, 이동·삭제 없음 |
| MotionProfile / V2, IceMage / Leopard, Dungeon / Monster 사용자 데이터 | 무변경 |
| `CharacterRoster` 및 플레이 연결 | 무변경 |
| 전역 ID 정규식 | 무변경 (테스트로 고정) |

1단계 저장 버전·마이그레이션 기반(`save-version-migration-foundation-report.md`)의 동작은 이번 단계에서 한 줄도 바뀌지 않았고, 저장 관련 테스트 96건이 그대로 통과한다.

## 10. 의도적으로 범위에 넣지 않은 것

| 항목 | 이유 |
| --- | --- |
| 표시 이름을 `LocalizedName`으로 옮기기 | `DisplayName`과 두 경로를 동시에 살려 두면 어느 쪽이 진짜인지 알 수 없다. 연결은 한 번에 한다 |
| `CharacterRoster`를 `CharacterCatalog`로 전환 | 플레이 경로 변경은 별도 단계. 이번엔 정적 데이터만 만든다 |
| 신규 런타임 UI 연결 | 카탈로그를 읽는 화면을 만들지 않았다. 표가 존재하는 것까지가 범위 |
| 스킬 행동 resolver / 전투 / 해금 로직 | `behavior_key`와 `required_character_level`은 표에 적힌 값일 뿐이며 해석기가 없다 |
| 체력 시스템 | `base_max_health`는 전부 공란이고 체력 규칙 자체가 없다 |
| Skill 07 카테고리 생성 | 아직 쓰지 않는 카테고리는 빈 에셋으로 미리 만들지 않는다(워크플로 문서 §3) |
| 수동 CharacterDefinition을 Generated로 이동 | 5장 참조. 로스터 전환과 함께 정리한다 |
| stale 생성 에셋 자동 삭제 | 8장 참조. 되돌릴 수 없는 동작이라 기존 정책을 유지 |
| 임의 부분집합 rebuild 범위 | 범위 밖 표를 가리키던 참조가 null로 덮어써지므로 `All`과 `CharacterSkillTables` 둘만 노출 |
| 원본에서의 full rebuild / Unity 실행 | 지시대로 실행하지 않음. 사용자 에디터가 락을 잡고 있어 검증은 전부 격리 클론에서 했다 |
| 부분 생성물 되돌리기(트랜잭션) 프레임워크 | 8장의 잔여 위험 참조. 되돌리기 구조를 새로 만드는 것은 이번 범위가 아니라고 판단해 위험을 기록만 했다 |
| ko-KR 공란 전환 | 사용자 지시(한국어 미확정 시 기존 표시명 임시값)가 일반 워크플로보다 우선한다. 1장 참조 |
| Windows Player 빌드 / Play Mode 검증 | 이번 범위는 에디터 임포터와 ScriptableObject뿐이라 Play Mode에서 관측할 동작이 없다. Windows 빌드 컴파일은 정적 검토 수준 |

## 11. 다음 단계에서 SaveData v2가 필요한 이유

다음 단계로 확정된 것은 **"전체 CharacterCatalog와 실제 보유 캐릭터 컬렉션을 분리하고, v1 → v2 마이그레이션을 넣는 것"** 이다. 이 장은 그 작업이 왜 저장 스키마 변경을 요구하는지만 적는다.

### 지금(v1)의 상태

이번 단계는 **저장 스키마를 건드리지 않았다.** 표가 만든 것은 전부 "이 게임에 어떤 캐릭터가 있는가"이고, 저장 데이터가 들고 있는 것은 "그 캐릭터가 지금 어떤 상태인가"다.

```csharp
public class CharacterSaveState
{
    public string characterId;   // CharacterDefinition.CharacterId와 같은 값
    public int level = 1;
    public int currentStamina = -1;
}
```

문제는 **"보유하고 있는가"를 말하는 자리가 어디에도 없다는 것**이다.

- 씬의 `CharacterRoster`에 연결된 정의가 곧 보유 목록이고, 거기에는 기존 6종이 전부 들어 있다. 즉 v1은 **6종을 사실상 모두 로스터에 노출한다.**
- `SaveData.characters`는 "보유 목록"이 아니라 **상태 기록**이다. 로스터에 있는 캐릭터를 만나면 그때 항목이 만들어질 뿐이라, 항목의 유무로 소유를 판정할 수 없다.
- 그래서 지금은 "전체 캐릭터 목록"과 "보유 캐릭터 목록"이 **같은 것**이며, 둘을 나눌 경계 자체가 데이터에 없다.

이번에 만든 `CharacterCatalog`는 그 둘 중 **앞의 것**이다 — 표에 있는 활성 캐릭터 전체이며, 누가 그것을 가지고 있는지는 말하지 않는다(그래서 아직 로스터에 연결하지 않았다).

### v2가 필요한 이유

전체 목록과 보유 목록을 나누는 순간, **보유 상태는 정의 에셋이 아니라 저장 데이터가 소유해야 한다.** 카탈로그는 모든 플레이어에게 같지만 보유는 플레이어마다 다르기 때문이다. 그 자리가 v1에는 없으므로 v2가 필요하다.

그리고 마이그레이션이 반드시 따라온다. v1 저장 파일에는 "보유"라는 개념이 없고 실제로는 6종을 다 쓰고 있었으므로, **아무 조치 없이 v2의 빈 보유 목록으로 읽으면 기존 플레이어가 캐릭터를 전부 잃는다.** v1 → v2 단계는 기존 진행(레벨·행동력·재화·아이템·회복 슬롯)을 하나도 잃지 않고 **지금 쓰이던 캐릭터들을 소유 상태로 옮기는 것**이 목적이다.

### 1단계 기반이 그대로 쓰인다

`save-version-migration-foundation-report.md`에서 만든 구조를 그대로 쓴다.

- `SaveData.CurrentSaveVersion`을 2로 올린다.
- `FromVersion → FromVersion + 1`만 허용하는 단계 표에 v1 → v2 단계 하나를 등록한다.
- 변환은 깊은 작업 사본에서 하고 모든 단계가 성공한 뒤에만 반영된다.
- 마이그레이션된 문서는 즉시 자동 저장하지 않고 다음 명시적 `Save()`에서만 기록한다.
- 미래 버전 문서는 계속 저장이 차단된다.

**`character_id`를 legacy PascalCase 그대로 보존한 것이 이 마이그레이션을 단순하게 만든다.** 저장 항목의 키가 바뀌지 않으므로 v1 → v2 단계는 키 재매핑을 전혀 하지 않아도 되고, 보유 목록도 같은 문자열을 그대로 쓴다. 이번 단계에서 id를 snake_case로 "정리"했다면 저장 항목 키 변환까지 함께 해야 했고, 실패 시 기존 진행이 통째로 유실되는 종류의 마이그레이션이 되었을 것이다.

반대로 **로스터를 생성 `CharacterDefinition`으로 갈아 끼우는 것 자체는 v2를 요구하지 않는다.** 정의 에셋이 바뀌어도 `CharacterId`가 같으므로 저장 항목은 그대로 붙는다. 저장 스키마가 필요한 것은 **새로운 진행 상태(보유 여부)가 생기기 때문**이다.

### 다음 단계 설계에서 확정할 것

아래는 **이번 보고서에서 정하지 않는다.** 여기서 미리 이름을 붙이면 설계가 확정된 것처럼 보이기 때문이다.

- 보유 상태를 담는 구체적인 필드 이름과 모양(캐릭터 id 목록인지, `CharacterSaveState`에 소유 플래그를 더하는지)
- 캐릭터 획득 규칙과 그 시점
- 신규 캐릭터가 추가됐을 때의 소유 기본값(기본 미보유인지, 특정 조건에서 자동 지급인지)
- v1 문서에서 "지금 보유 중"으로 옮길 대상의 판정 기준(로스터 전체인지, 저장 항목이 있는 캐릭터인지)

이번 단계에서 확정된 것은 **표와 카탈로그가 전체 목록을 결정적으로 제공한다**는 것까지이며, 보유 경계는 다음 단계의 설계 대상이다.
