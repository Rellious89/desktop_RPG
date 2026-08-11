# 캐릭터 보유(SaveData v2) 완료 보고서

작성일: 2026-08-11
대상 프로젝트: `desktop_RPG`
단계: KeyBuddy 3단계 (전체 캐릭터 목록과 실제 보유 캐릭터의 분리)
저장 스키마: v1 → **v2**

## 1. v2가 뜻하는 것

`SaveData.characters`의 **항목 하나가 곧 두 가지를 동시에** 말한다.

1. 플레이어가 **그 `characterId`를 가지고 있다**(보유).
2. 그 캐릭터의 진행 상태(`level`, `currentStamina`)가 이 값이다.

둘은 나뉘지 않는다. 보유를 적는 별도의 목록이나 플래그는 없으며 **항목의 존재 자체가 보유**다. id 비교는 언제나 `StringComparer.Ordinal` 완전 일치이므로 `Barbarian`과 `barbarian`은 서로 다른 캐릭터다.

v1까지는 이 뜻이 없었다. **쓸 수 있는 캐릭터를 정한 것은 씬에 직렬화된 로스터 목록**이었고, `SaveData.characters`는 그 목록을 따라 만들어진 **상태 기록**이었다 — 기준 커밋(`6d229979`)의 `CharacterRoster.Awake`는 `SyncSaveStates()`로 **쓸 수 있는 모든 항목에 대해 상태를 만들어 두었다**(`GetOrCreateState`). 즉 항목은 그 캐릭터를 실제로 써 봐야 생기는 것이 아니라 시작할 때 한꺼번에 만들어졌다.

그래서 저장 목록의 유무는 보유를 뜻하지 않았다. 로스터에서 빼면 항목이 남아 있어도 쓸 수 없었고, 로스터에 넣으면 항목이 없어도 다음 실행에 생겼다. v2는 그 권한을 저장 문서로 옮기므로, v1 문서를 그대로 v2 규칙으로 읽으면 **그 문서가 저장될 당시 아직 항목이 만들어지지 않았던 캐릭터**(그 캐릭터가 로스터에 들어오기 전에 저장된 문서 등)가 미보유가 된다.

카탈로그와 저장 목록이 어긋날 때의 규칙은 셋이다.

| 상황 | 결과 |
| --- | --- |
| 카탈로그에 있고 저장에 없다 | **미보유.** 목록에 나오지 않으며, 그 사실을 적기 위해 항목을 만들지 않는다 |
| 저장에 있고 카탈로그에 없다 | **값은 보존, 런타임에서는 감춤.** 지우지 않지만 보유 목록·조회·변경 어디에도 나타나지 않는다 |
| 카탈로그에서 빠진 비활성 캐릭터 | 생성 카탈로그가 활성 행만 담으므로 따로 거를 것이 없다 |

## 2. v1 → v2 정확한 규칙

`V1ToV2Step`이 하는 일은 하나다 — **v1 시절 모두가 쓸 수 있던 여섯 캐릭터를 보유로 확정한다.**

역사적 여섯 id(철자·대소문자가 저장 키 그 자체이며, 덧붙이는 차례도 이 순서다):

```text
Barbarian, CatKnight, CatMage, ElfArcher, ElfGuardian, RabbitHealer
```

- 이미 있는 id는 **건드리지 않는다.** 없는 것만 목록 **끝에** 덧붙이며 값은 `level = 1`, `currentStamina = -1`("아직 초기화되지 않음")이다.
- 기존 항목의 순서·id·레벨·행동력, **null 항목, 중복 항목, 카탈로그에 없는 id를 하나도 손대지 않는다.** 대소문자를 맞추거나 중복을 합치지도 않는다 — 합쳐 놓으면 어느 쪽 진행이 사라졌는지 아무도 모른다.
- 목록이 `null`인 문서에서는 **실제로 덧붙일 것이 생겼을 때만** 목록을 만든다.
- 여러 번 돌려도 결과가 같다(멱등).

**이 여섯 id는 `V1ToV2Step` 안에 private 상수로 박혀 있고 바깥에 노출하지 않는다.** 이 단계는 Unity 에셋도 `CharacterCatalog`도 읽지 않는다 — 변환은 "그때 무엇이 있었는가"라는 **역사적 사실**을 옮기는 일인데, 지금의 표를 읽으면 나중에 캐릭터를 추가하거나 지우는 순간 **예전 파일의 변환 결과가 함께 바뀐다.** 같은 저장 파일이 빌드마다 다르게 변환되면 안 된다.

## 3. v0 → v1 → v2 동작

한 칸씩만 올린다. `SaveMigrationRunner`의 기본 표에 두 단계가 등록되어 있고, 중간을 건너뛰는 경로는 없다.

| 시작 | 거치는 단계 | 결과 |
| --- | --- | --- |
| v0(버전 필드 없음) | `UnversionedToV1Step` → `V1ToV2Step` | 메타데이터가 "모름"(revision 0 / 빈 시각)으로 채워지고, 여섯이 보유로 확정된다 |
| v1 | `V1ToV2Step` | 메타데이터는 **그대로**, 없는 여섯만 덧붙는다 |
| v2 | 없음 | `AlreadyCurrent` — 여섯을 다시 채우지 않는다 |
| v2보다 새 문서 | 없음 | `FutureVersionBlocked` — 역직렬화도 저장도 하지 않는다 |

- **변환은 전부 되거나 전혀 안 된다.** 단계는 깊은 작업 사본 위에서만 돌고, 모든 칸이 성공했을 때만 호출부의 문서에 옮겨진다. 뒤 칸이 없거나 예외가 나면 호출부 문서는 **필드 하나까지 그대로**이며 덧붙은 여섯도 새어 나가지 않는다.
- 올린 문서는 즉시 저장하지 않는다. 다음 명시적 `Save()`가 결과를 파일에 남기고, 그때 원자적 교체가 **직전 원본을 백업 자리에 그대로** 남긴다.

## 4. 기존 플레이어 보존

v1 문서를 v2 규칙으로 그냥 읽으면 **저장 당시 아직 항목이 없던 캐릭터가 미보유가 되어** 가진 것이 줄어든다(1장 참조). `V1ToV2Step`이 그 일을 막는다.

- 레벨·행동력·재화·아이템·회복 슬롯 등 **다른 필드는 하나도 바뀌지 않는다.**
- 이미 진행 중이던 캐릭터의 값은 초기값으로 덮이지 않는다.
- `character_id`를 legacy PascalCase 그대로 보존했기 때문에 키 재매핑이 전혀 필요 없었다 — 이번 마이그레이션이 단순할 수 있었던 가장 큰 이유다.

## 5. 새 게임 초기 지급

- 카탈로그에서 `initially_owned = 1`인 정의만, 아직 없는 것만, `level = 1` / `currentStamina = -1`로 더한다. 각 id는 **정확히 한 번**.
- **`SaveSystem.LoadStatus == SaveLoadStatus.NewGame`일 때만** 실행한다. `Loaded` / `Migrated` / `CorruptFallback` / `FutureVersionBlocked` / `MigrationFailed`에서는 **한 항목도 만들지 않는다.**
- 판단의 근거는 불러오기 상태 **하나뿐**이며 "목록이 비어 있다"가 아니다. v2에서 **빈 보유 목록은 정상적인 상태**라서, 비었다고 채우면 플레이어가 스스로 비운 목록이 다시 채워지고 읽지 못한 문서 위에 지급이 얹힌다.
- 멱등이므로 저장 전에 종료해도 다음 실행이 같은 결과를 만든다.

## 6. 카탈로그와 보유 컬렉션의 역할

| | `CharacterCatalog` | `OwnedCharacterCollection` |
| --- | --- | --- |
| 답하는 질문 | 이 게임에 어떤 캐릭터가 있는가 | 이 플레이어가 무엇을 가지고 있는가 |
| 근거 | 표(Character.csv) → 생성 에셋 | 주입받은 `SaveData` 문서 |
| 모든 플레이어에게 | 같다 | 다르다 |

`OwnedCharacterCollection`은 **저장소를 모른다.** 파일 경로도 `persistentDataPath`도 `SaveSystem`도 보지 않고 생성될 때 받은 문서 하나만 다룬다 — 그래서 시험이 디스크 없이 전부 돈다. 제공하는 것은 전체 목록, 보유 목록(카탈로그 순서), Ordinal `IsOwned`, `TryGetState`, `InitializeNewGame`뿐이다.

- **읽는 동작은 문서를 절대 바꾸지 않는다.** 목록을 훑든 상태를 얻든 저장 목록의 개수도 내용도 달라지지 않는다.
- 항목을 **더하는** 경로는 `InitializeNewGame` 하나뿐이고, **지우는 경로는 아예 없다.**
- 보유 목록은 카탈로그를 훑어 만들므로 저장 목록에 같은 id가 두 번 있어도 **한 번만** 나오고, 차례는 표의 `display_order`가 정한다.
- `OwnedCharacters`는 **호출할 때마다 새 목록**을 돌려준다. 재사용 버퍼를 돌려주면 다음 호출이 이미 건네준 목록을 비우고 다시 채워, 목록을 받아 두고 나중에 훑는 코드가 등 뒤에서 다른 답을 보게 된다.

## 7. CharacterRoster 전환과 "읽는다고 갖게 되지 않는다"

- 직렬화된 `CharacterCatalog` 참조가 생겼고, 목록은 **카탈로그 순서 ∩ 저장 보유 → 기존 재생 가능 검사** 순으로 만들어진다.
- **`GetOrCreateState` / `SyncSaveStates`를 없앴다.** 예전에는 "로스터에 있으면 저장 항목을 만든다"였고 그것이 곧 캐릭터 지급이었다. 지금 남은 정규화는 **이미 보유한** 상태의 행동력만 손본다(`-1` → 정의의 `MaxStamina`, 그 밖에는 `0 ~ Max`로 클램프).
- 모든 공개 상태 경로는 **먼저 `usableEntries`에서 정확 ID로 정식 정의를 해석한 뒤** 그 상태를 찾는다. 그래서 카탈로그에 없는 저장 전용 id를 가리키는 정의를 만들어 넘겨도 값이 읽히거나 바뀌지 않는다(보존하되 감춘다).
- `GetLevel` / `GetStamina` / `GetMaxStamina`는 null·미보유·저장 전용 id에 대해 **0**이고 항목을 만들지 않는다.
- `SetStamina` / `SpendStamina` / `ApplyRecoveryStamina` / 디버그 오버라이드는 그런 대상에 **아무 일도 하지 않는다** — 저장하지도, 이벤트를 보내지도 않는다.
- **모든 비교가 `CharacterId`의 Ordinal 완전 일치**이며 에셋 참조가 아니다. 같은 id의 수동 에셋을 넘겨도 카탈로그의 생성 정의로 이어지고, 상한도 이벤트 인자도 **정식 정의**의 것이다(수동 에셋이 다른 `maxStamina`를 들고 있어도 표의 값으로 클램프된다).
- `RaiseCharacterStateChanged`는 미보유·미지의 대상을 **무시하고**, 알릴 때는 정식 정의를 넘긴다.
- `CurrentCharacterCanAct`: **로스터가 아예 없는 씬에서만** true. 카탈로그 모드에서 보유가 0이면 **false**다(보유 0은 "로스터가 없다"가 아니다). 카탈로그가 연결되지 않은 과도기 구성에서만 예전 판정을 유지한다.
- 직렬화된 `entries`는 **카탈로그가 없을 때만 쓰는 과도기 폴백**으로 남아 있다. 카탈로그가 있으면 한 항목도 읽히지 않으며, 그 경로는 저장 데이터에 항목을 만들지 않는다.
- `Entries` / `Entry` / `Current` / `TrySwitchTo` / 행동력 API / 회복 API와 이벤트의 **공개 형태는 그대로**다.

## 8. 회복소와 교체

- 회복 어댑터의 `Contains` / `FindById`가 **정확 ID**로 판정한다(참조 비교였다면 같은 캐릭터를 "회복소가 모르는 캐릭터"로 판정했다).
- 어댑터의 목록이 곧 로스터의 보유 목록이므로 **보유한 캐릭터만 회복소에 나온다.**
- 모르는 슬롯 id나 보유하지 않은 id는 `FindById`가 `null`을 돌려주고, `RecoveryStation`이 기존 규칙대로 **슬롯당 한 번 경고를 남기고 진행만 멈춘다(저장 값은 유지)**. 상태를 만들어 채워 주지 않는다.
- 교체: 미보유 대상은 `TrySwitchTo`가 `NotAvailable`로 거부한다. 회복 중 판정, 행동력 판정, 이미 투입됨 판정의 순서와 뜻은 그대로다.
- 시작 캐릭터 선택: 보유한 기본 캐릭터가 회복 중이 아니면 그것, 아니면 카탈로그 차례로 처음 만나는 비회복 보유 캐릭터, 전원이 회복 중이면 **아무도 투입하지 않는다**(null).

## 9. 씬 변경 범위 (체크포인트 D)

대상: `Assets/Scenes/desktopScene_ReSize.unity`의 **`CharacterRoster` 컴포넌트 하나**.

```diff
-  entries:
-  - definition: {8개 수동 CharacterDefinition}
+  catalog: {fileID: 11400000, guid: 5f24823f983054d358a82014246a9fa7, type: 2}
+  entries: []
   runtimeActor: {fileID: 1959120555}
-  defaultCharacter: {수동 CatKnight}
+  defaultCharacter: {fileID: 11400000, guid: 342e077c6a6894f169a84d989ea37a84, type: 2}
```

- 씬 전체 diff는 **이 한 덩어리(+3 / −10줄)뿐**이다. `runtimeActor`, `staminaCostPerDefeat`(1), `overrideStaminaOnStart`(false), `debugStartStamina`(30)를 포함해 다른 모든 YAML 바이트는 그대로다.
- 비워 낸 여덟 항목은 수동 정의 6종 + 테스트용 `Test_IceMage` / `Test_Leopard`였다.
- **`defaultCharacter`는 CatKnight로 유지했다.** 다만 가리키는 대상을 수동 에셋에서 **생성 에셋**(`Character_CatKnight.asset`)으로 바꿔, 이 컴포넌트가 더 이상 수동 정의에 의존하지 않는다. 시작 캐릭터 id와 사용자 가시 동작은 그대로다.
  - 최초 지시서는 `248d14fc…`(생성 **Barbarian**)를 지정했으나, 그 값은 기존 수동 기본값의 캐릭터를 잘못 추정한 것이어서 조정 후 생성 CatKnight(`342e077c…`)로 확정했다. 시작 캐릭터를 바꾸는 것은 이번 범위가 아니다.
  - 참고로 체크포인트 C의 정확 ID 해석 덕분에 수동 기본값을 그대로 두어도 생성 정의로 이어지지만, 컴포넌트가 수동 에셋을 참조하지 않는 편이 정리 상태로 낫다.
- 참조 GUID는 모두 실재를 확인했고, 카탈로그가 **정확히 여섯 생성 정의**를 로스터 순서로 담고 있음도 확인했다.

## 10. 검증 결과

전부 격리 클론에서 실행했다(사용자 Unity 에디터가 원본 프로젝트 락을 잡고 있다).

| 항목 | 결과 |
| --- | --- |
| Unity 컴파일 오류 | **0** |
| 전체 EditMode | **419 / 419 통과** — failure 0, skip 0, inconclusive 0 |
| 씬 로드/임포트 스모크 | **통과** (아래 참조) |
| 생성 에셋 96개(5개 기존 도메인 + Character/Skill/CharacterSkill, `.asset`+`.meta`) | 체크포인트 D 전후 **SHA256 byte-identical** |
| 보호/사용자 파일 | 무변경 |
| `git diff --check` | 클린 |

주요 픽스처:

| 픽스처 | 통과 |
| --- | --- |
| `CharacterEditor.Tests.CharacterRosterCatalogTests` | 41 / 41 |
| `CharacterEditor.Tests.OwnedCharacterCollectionTests` | 21 / 21 |
| `CommonEditor.Tests.SaveMigrationTests` | 79 / 79 |
| `CommonEditor.Tests.SaveStorageTests` | 19 / 19 |
| `CommonEditor.Tests.SaveSystemIntegrationTests` | 20 / 20 |
| `TableDataEditor.Tests.CharacterTableTests` | 49 / 49 |
| `TableDataEditor.Tests.CharacterTableOutputTests` | 30 / 30 |
| 그 밖의 기존 픽스처 | 160 / 160 |

씬 스모크는 EditMode에서 씬을 열어(= `Awake`가 돌지 않는다) 직렬화 값을 직접 확인했다: 카탈로그 참조 해석, `entries` 비어 있음, `defaultCharacter`가 `Assets/Generated/TableData/Character/Character_CatKnight.asset`이고 id가 `CatKnight`, `runtimeActor` 연결 유지, 행동력/디버그 설정 3개 값 유지, 카탈로그가 여섯을 로스터 순서로 담고 전부 생성 에셋이며 전부 `InitiallyOwned = true`. 스모크는 씬을 저장하지 않으며, 실행 후 클론과 원본의 `Assets` 차이는 하네스 파일뿐이었다(= Unity가 씬을 다시 쓰지 않았다 → 손으로 고친 YAML이 그대로 유효하다).

시험은 어디에서도 `persistentDataPath`를 쓰지 않는다. 저장이 필요한 시험은 메모리 전용 `ISaveStorage`를 `ConfigureForTests`로 끼워 쓰기 횟수만 센다.

## 11. 변경 파일 (체크포인트별)

### A — `081c42fcd83c0a1ac8af46d1bc0380c85a3083ce` · Add character ownership save migration v2

```text
Assets/Scripts/Common/SaveData.cs
Assets/Scripts/Common/SaveMigrationRunner.cs
Assets/Editor/Common/Tests/SaveMigrationTests.cs
Assets/Editor/Common/Tests/SaveSystemIntegrationTests.cs
```

### B — `541cd5d3e7937eeba4d0aaab1db5f3dcd16b8d03` · Add initial character ownership table data

```text
Assets/TableData/Game/Character.csv
Assets/Scripts/Character/CharacterDefinition.cs
Assets/Editor/TableData/TableDataCsvReader.cs
Assets/Editor/TableData/TableDataFieldRules.cs
Assets/Editor/TableData/TableDataRows.cs
Assets/Editor/TableData/TableDataValidator.cs
Assets/Editor/TableData/TableDataRebuilder.cs
Assets/Editor/TableData/Tests/CharacterTableTests.cs
Assets/Editor/TableData/Tests/CharacterTableOutputTests.cs
Assets/Generated/TableData/Character/Character_{Barbarian,CatKnight,CatMage,ElfArcher,ElfGuardian,RabbitHealer}.asset
```

### C — `714685330d55bfab82849cf9a04c02d67bc9238c` · Use saved ownership in character roster

```text
Assets/Scripts/Character/OwnedCharacterCollection.cs (+ .meta)
Assets/Scripts/Character/CharacterRoster.cs
Assets/Scripts/Recovery/CharacterRosterRecoveryAdapter.cs
Assets/Editor/Character.meta
Assets/Editor/Character/Tests.meta
Assets/Editor/Character/Tests/OwnedCharacterCollectionTests.cs (+ .meta)
Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs (+ .meta)
Assets/Scripts/Common/SaveMigrationRunner.cs            (v1 설명 정정, 동작 변경 없음)
Assets/Editor/Common/Tests/SaveMigrationTests.cs        (설명 정정)
Assets/Editor/Common/Tests/SaveSystemIntegrationTests.cs (설명 정정)
```

마지막 세 파일은 A에서도 손댄 파일이지만, 이번에는 **주석/설명만** 고쳤고 마이그레이션 동작과 단언은 바뀌지 않았다(14-1장 참조).

### D — 이 보고서를 담는 마지막 커밋

```text
Assets/Scenes/desktopScene_ReSize.unity
ProjectDocs/WorkOrders/character-ownership-save-v2-report.md
```

체크포인트 D의 커밋 해시는 여기에 적지 않는다 — **이 문서가 그 커밋에 들어가므로 자기 해시를 안정적으로 담을 수 없다.** D는 "이 보고서를 포함하는 마지막 커밋"으로 식별한다.

## 12. Character.csv의 CRLF 관례

프로젝트의 표 CSV 7종은 모두 **UTF-8 / CRLF / 파일 끝 개행 없음**이다. 그래서 `git diff --check`는 이 파일들의 수정된 줄마다 `trailing whitespace`를 보고한다(줄 끝 CR을 공백으로 본다). 이는 파일을 처음 추가한 커밋에서도 같은 수로 나타나는 **기존 관례이며 결함이 아니다.** 줄바꿈을 LF로 바꾸면 모든 줄의 바이트가 달라지고 나머지 여섯 CSV와의 관례가 깨지므로 **바꾸지 않았다.** 이번 체크포인트 D에는 CSV 변경이 없어 `git diff --check`가 완전히 조용하다.

## 13. 수동 CharacterDefinition 정리 후보

`Assets/Data/Characters/`의 수동 정의 8종은 **이번 범위에서 건드리지 않았다.** 이제 씬이 그것들을 참조하지 않으므로, 다음 정리 대상이 된다.

| 에셋 | 상태 |
| --- | --- |
| `CatKnight` / `ElfArcher` / `Barbarian` / `ElfGuardian` / `RabbitHealer` / `CatMage` | 생성 에셋과 같은 id로 중복. 씬 참조가 사라졌으므로 **삭제 후보** |
| `Test_IceMage` / `Test_Leopard` | 표에 없는 테스트 자산. 로스터에서 빠졌으므로 **삭제 또는 테스트 전용 폴더로 이동 후보** |

지우기 전에 확인할 것: 다른 씬·프리팹·에디터 도구(Motion Editor 등)가 그 GUID를 참조하는지. 삭제는 되돌릴 수 없고 GUID가 사라지므로 이번 단계에서 하지 않았다. 시험 하나가 수동 `CatKnight`를 읽어 "같은 id로 공존해도 충돌이 아니다 / `InitiallyOwned`가 false로 읽힌다"를 고정하고 있으므로, 지울 때 그 시험도 함께 손봐야 한다.

## 14. 앞으로의 캐릭터 획득 경계

획득은 **저장 목록에 항목 하나를 더하는 일**이다. 그 자리는 이미 v2에 있다.

- 획득 규칙(상점·드롭·퀘스트 보상 등)이 생겨도 **스키마는 그대로**다. 새 필드가 필요 없으므로 **획득을 넣는다는 이유만으로 SaveData v3이 필요하지는 않다.**
- 새 캐릭터를 표에 추가하는 것도 v3을 요구하지 않는다. `initially_owned = 0`으로 두면 새 게임에서도 주지 않고, 기존 플레이어에게도 저절로 생기지 않는다(마이그레이션은 역사적 여섯만 다룬다).
- 지급 경로는 하나로 모아야 한다 — 지금 `InitializeNewGame`이 그 유일한 자리이며, 획득 기능은 같은 성질(Ordinal id, 중복 금지, 기존 값 보존)을 지키는 API를 그 옆에 두는 것이 맞다.

**획득/회수 API와 즉시 반영은 이번 범위가 아니다.** 지금 보유가 바뀌는 시점은 새 게임 초기 지급과 v1 → v2 변환뿐이고, 둘 다 로스터가 만들어지기 전에 끝난다. 그래서 런타임 도중 보유가 바뀌는 경로는 아직 없다.

앞으로 런타임에서 보유를 바꾸는 기능을 넣을 때는 **함께 갱신해야 하는 것들이 있다.** 지금 구조는 그 갱신을 자동으로 해 주지 않는다.

- `CharacterRoster.usableEntries`와 `current` — 목록은 `Awake` 때 한 번 만들어지고, `current`는 그 목록에서 고른 캐릭터다. 보유가 늘거나 줄면 둘 다 다시 맞춰야 한다.
- 회복 어댑터(`CharacterRosterRecoveryAdapter`) — 생성될 때 로스터의 목록을 복사해 들고 있으므로 새 목록으로 다시 만들어야 한다.
- UI가 들고 있는 스냅샷 — `OwnedCharacters`는 호출 시점의 독립적인 목록이라, 받아 둔 쪽이 다시 물어보지 않으면 예전 목록을 계속 그린다.

지금은 그 셋이 어긋나도 **안전한 쪽으로** 동작한다. 세 경로(`GetMaxStamina` / `GetSwapBlockReason` / `RaiseCharacterStateChanged`)와 모든 조회·변경이 목록이 아니라 **지금 저장 문서에 그 상태가 있는지**를 다시 확인하므로, 보유가 사라진 캐릭터는 목록에 남아 있어도 상한 0 / 교체 불가 / 이벤트 없음이 된다. 다만 이것은 잘못된 화면을 막는 안전장치이지 갱신이 아니다 — 실제 기능을 넣을 때는 위 셋을 명시적으로 다시 맞추는 경로를 함께 만들어야 한다.

다만 **다음 경험치/레벨 단계는 스키마 판단이 필요할 수 있다.** 캐릭터별 경험치, 성장 곡선, 레벨업 시점의 파생 값(최대 체력 등)을 저장해야 한다면 `CharacterSaveState`에 칸이 늘어난다. 그때는 v2 → v3 단계 하나를 1단계 기반(한 칸씩, 깊은 사본, 실패 시 원본 보존) 그대로 추가하면 된다. 이 보고서는 그 결정을 미리 내리지 않는다.

## 14-1. 최종 게이트에서 고친 것

마지막 검토에서 두 가지가 걸렸고 체크포인트 C에 반영했다.

1. **목록만 믿던 세 경로.** `GetMaxStamina` / `GetSwapBlockReason` / `RaiseCharacterStateChanged`가 `Awake` 때 만든 `usableEntries`만 보고 답하고 있었다. 로스터가 만들어진 뒤 저장 문서에서 보유가 사라지면 그 셋은 **이미 잃은 캐릭터를 아직 가진 것처럼** 다뤘다(상한이 나오고, 투입 중이거나 행동력이 남아 있으면 교체까지 통과했다). 이제 셋 다 `TryGetOwnedState`로 **지금 저장 문서에 그 id의 상태가 있는지**를 확인하며, 없으면 각각 0 / `NotAvailable` / 아무것도 알리지 않음이다. 카탈로그가 없는 폴백 구성의 동작은 그대로다.
2. **v1 설명이 사실과 달랐다.** "항목은 실제로 써 본 뒤에야 생겼다"고 적었으나, 기준 커밋의 `Awake`는 쓸 수 있는 모든 항목의 상태를 시작할 때 만들었다. 1장과 `V1ToV2Step` 주석, 관련 시험 설명을 실제 동작에 맞게 고쳤다. **마이그레이션 동작 자체는 바뀌지 않았다.**
3. **폴백 구성이 1번 수정에 휘말려 무너졌다.** 1번에서 새로 만든 `ResolveOwnedUsable`이 저장 문서를 다시 확인하려고 `TryGetOwnedState`를 거쳤는데, 그 함수는 카탈로그가 없으면(`owned == null`) 언제나 실패한다. 그래서 **카탈로그를 연결하지 않은 과도기 씬에서 모든 캐릭터가 "쓸 수 없음"이 되어** `GetSwapBlockReason`이 직렬화된 목록 기준의 이유(예: `AlreadyCurrent`)를 내지 못하고 `RaiseCharacterStateChanged`도 아무것도 알리지 않았다. `ResolveOwnedUsable`이 `owned == null`일 때 예전처럼 `ResolveUsable`로 답하도록 고쳤다 — **저장 문서를 다시 확인하는 것은 카탈로그 모드에서만** 하는 일이다(그 구성에만 보유라는 개념이 있다). `GetMaxStamina`는 처음부터 자기 `owned == null` 분기를 따로 갖고 있어 영향을 받지 않았고 그대로 두었다.

3번은 폴백 구성 시험 3건으로 고정했다. 수정을 되돌리면 그중 `GetSwapBlockReason`과 `RaiseCharacterStateChanged` 시험이 실제로 실패하고, `GetMaxStamina` 시험은 통과한다(그 경로는 애초에 무너지지 않았다) — 시험이 정확히 무너진 두 경로만 겨냥하고 있다는 뜻이다.

## 15. 의도적으로 범위에 넣지 않은 것

| 항목 | 이유 |
| --- | --- |
| 캐릭터 획득 UI·규칙 | 이번 단계는 보유를 **저장하고 읽는** 경계까지다 |
| 경험치/레벨 성장 | 다음 단계. 스키마 영향은 14장 참조 |
| 수동 CharacterDefinition 삭제 | 13장 참조 — 참조 감사 후에 한다 |
| `initially_owned = 0` 캐릭터 | 지금 여섯 행이 모두 1이다. 잠금 캐릭터는 획득 규칙과 함께 정한다 |
| 표시 이름을 `LocalizedName`으로 전환 | 2단계에서 미룬 그대로. 두 경로를 동시에 살려 두지 않는다 |
| Skill / CharacterSkill 데이터 | 표만 존재하고 행이 없다. 이번 단계와 무관 |
| 씬의 다른 컴포넌트 | 9장의 한 컴포넌트 외에는 손대지 않았다 |
| PlayMode 스모크 | 아래 참조 |

**PlayMode 스모크는 하지 않았다.** 이 씬을 PlayMode로 띄우면 `CharacterRoster.Awake`가 `SaveSystem.Data`를 건드리고, 그 경로는 실제 `persistentDataPath`의 저장 파일을 읽고 쓴다 — 클론은 프로젝트 경로가 달라도 `persistentDataPath`를 실제 앱과 공유하므로 사용자의 진행이 덮어써질 수 있다. 이번 작업의 고정 조건이 `persistentDataPath` 미사용이므로 안전한 기존 경로가 없다고 판단해 실행하지 않았고, 대신 EditMode 씬 로드 스모크로 직렬화가 의도대로 읽히는지를 확인했다. 실제 실행 확인은 사용자 에디터에서 한 번 열어 보는 것으로 대신하는 편이 안전하다.

Windows Player 빌드/실행은 macOS 개발 환경에서 확인할 수 없다. 이번 범위에 플랫폼 의존 코드는 없지만, Windows 빌드 컴파일은 정적 검토 수준이다.
