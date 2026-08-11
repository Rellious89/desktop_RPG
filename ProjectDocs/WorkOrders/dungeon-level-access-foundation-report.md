# 던전 레벨 접근 기반 (5단계 보고서)

던전에 **"몇 레벨이어야 들어갈 수 있는가"**를 표에 세우고, 그 판정을 하나의 권위 있는 통로에 몰아넣은 뒤, 목록 UI가 그 결과를 보여주게 한 작업의 기록이다. 세 개의 체크포인트(A~C)로 구현했고 D가 이 보고서를 담는다.

---

## 1. 목적과 최종 동작

던전마다 `required_character_level`을 표에 두고, **보유한 캐릭터 중 가장 높은 레벨**이 그 값 이상일 때만 입장 요청이 통과한다.

최종 동작을 한 줄로 요약하면 다음과 같다.

1. 표(`Dungeon.csv`) → 임포터 → `DungeonDefinition.RequiredCharacterLevel`로 값이 흐른다.
2. `DungeonAccessService`가 그 값과 `CharacterRoster.HighestOwnedCharacterLevel`을 비교해 **순수한 결과 구조체**를 돌려준다.
3. `DungeonPanel`은 그 결과로 목록 항목을 흐리게 하고 입장 버튼을 잠근다 — **표시일 뿐이다.**
4. `DungeonEntryService`가 **요청 직전에 현재 상태로 다시 판정**하고, 통과한 요청만 이벤트로 내보낸다 — 이것이 최종 거부자다.

**지금은 실제 밸런스 차등이 없다.** 아홉 던전이 모두 `1`이므로 정상적으로 사용할 수 있는 보유 캐릭터가 하나라도 있으면 전부 열려 있다. 이번 단계가 세운 것은 **판정 경로**이고, 어떤 던전이 몇 레벨인지는 기획 데이터가 나중에 정한다.

---

## 2. Dungeon.csv 스키마 추가

컬럼 하나를 **`reward_item_ids`와 `display_order` 사이**에 넣었다.

```csv
dungeon_id,name_category,name_key,$dungeon_name,world_id,$world_name,representative_sprite_key,monster_ids,$monster_name,reward_item_ids,required_character_level,display_order,enabled,memo
```

- **필수 정수이며 1 이상**이다. 비어 있으면 오류이고, 0이나 음수도 오류다. `TableDataFieldRules.TryReadIntAtLeast(..., minimum: 1, ...)`을 쓰므로 **잘못된 값을 자동으로 1로 올려 통과시키지 않는다** — 통과시키면 표에 적힌 것과 게임이 쓰는 것이 달라진다.
- **상한은 없다.** 최대 레벨이 아직 기획에 없으므로 임포터가 대신 정하지 않는다.
- 실제 값은 **id 1~9 아홉 행 전부 `1`**이다.

위치를 그 사이로 정한 이유는, 이 컬럼이 **던전의 조건**이지 표시 순서나 활성 여부 같은 운영 칸이 아니기 때문이다. `display_order` / `enabled` / `memo`는 모든 표가 공유하는 꼬리 관례라 그 앞에 두었다.

파일 관례는 기존 표와 같다 — UTF-8 / CRLF / **파일 끝 개행 없음**.

---

## 3. DungeonDefinition과 생성 파이프라인

### DungeonDefinition

```csharp
[Header("Condition")]
[Min(1)]
[SerializeField] private int requiredCharacterLevel = 1;

public int RequiredCharacterLevel => Mathf.Max(1, requiredCharacterLevel);
```

에셋은 **값을 담기만 한다.** 스스로 무엇도 여닫지 않으며 접근 판정을 알지 못한다. 읽기 접근자에서 한 번 더 `Max(1, ...)`을 거는 것은, 손으로 만든 에셋이나 옛 에셋이 0을 들고 있어도 판정 쪽이 "필요 레벨 0" 같은 뜻 없는 값을 보지 않게 하기 위해서다.

### 임포터와 검증기

| 위치 | 한 일 |
| --- | --- |
| `TableDataCsvReader.Dungeon` | 기대 컬럼 목록에 `RequiredCharacterLevel`을 `RewardItemIds`와 `DisplayOrder` 사이로 추가 |
| `TableDataRows.DungeonRow` | `public int RequiredCharacterLevel = 1;` |
| `TableDataValidator` | 던전 행마다 `TryReadIntAtLeast(..., 1, ...)`로 읽는다 |
| `TableDataRebuilder` | 생성 에셋의 `requiredCharacterLevel` 직렬화 필드에 기록 |
| `TableDataRebuilder.VerifyFields<DungeonDefinition>` | 기대 필드 목록에 `requiredCharacterLevel` 추가 — 필드 이름이 바뀌면 Rebuild가 조용히 넘어가지 않고 실패한다 |

생성물 쪽 변경은 **아홉 에셋에 한 줄씩**이다.

```diff
   rewardItems: []
+  requiredCharacterLevel: 1
   displayOrder: 10
```

---

## 4. 접근 판정의 근거 — "보유하고 쓸 수 있는 캐릭터 중 최고 레벨"

판정의 근거는 **현재 투입 캐릭터(Current)가 아니다.** `CharacterRoster.HighestOwnedCharacterLevel`이며, 계약은 새 인터페이스 `IOwnedCharacterLevelSource` 하나로만 노출한다.

```csharp
namespace Character
{
    public interface IOwnedCharacterLevelSource
    {
        int HighestOwnedCharacterLevel { get; }
    }
}
```

`CharacterRoster`가 이것을 구현한다. 순수 판정기인 `DungeonAccessService`는 `SaveData`나 `CharacterRoster`의 구체 타입을 참조하지 않고 이 좁은 인터페이스만 사용한다. 런타임 연결부인 `DungeonPanel`과 `DungeonEntryService`만 현재 `CharacterRoster.Instance`를 인터페이스 입력으로 넘긴다.

### 현재 캐릭터를 쓰지 않은 이유

던전 입장은 **계정이 가진 전력**에 대한 질문이지 "지금 화면에 누가 서 있는가"에 대한 질문이 아니다. Current를 근거로 하면 Lv.20 캐릭터를 가진 사용자가 Lv.1 캐릭터를 투입해 둔 동안 던전이 잠기고, 잠긴 던전을 열기 위해 캐릭터를 갈아끼우는 무의미한 조작이 생긴다.

### 세는 대상과 제외 대상

`usableEntries`(로스터가 이미 만들어 둔 **재생 가능한 보유 캐릭터** 목록)를 순회하고, 항목마다 `TryGetOwnedState`로 **지금도 보유 중인지 재확인**한다.

| 제외되는 것 | 이유 |
| --- | --- |
| 카탈로그에 없는 캐릭터 | 정식 정의가 없으면 게임이 인정한 캐릭터가 아니다 |
| 저장 항목이 없는(미보유) 캐릭터 | 항목의 존재가 보유의 근거라는 3단계 규칙 그대로 |
| 모션 프로필 검증에 실패해 **투입할 수 없는** 정의 | 화면에 세울 수 없는 캐릭터의 레벨로 던전이 열리면 안 된다 |
| `usableEntries`에는 남았지만 그 뒤 저장 문서에서 보유가 사라진 항목 | `usableEntries`는 Awake 때 한 번 만든 목록이라 낡을 수 있다 |

- 저장된 `level`이 **1 미만이면 런타임에서 1로 읽되 저장 값은 고치지 않는다** — 조회가 저장 문서를 고치지 않는다는 규칙을 그대로 지킨다.
- **쓸 수 있는 보유 캐릭터가 하나도 없으면 0**이다. 0은 "레벨 0인 캐릭터가 있다"가 아니라 "근거로 삼을 캐릭터가 없다"는 뜻이며, 판정은 이것을 `NoUsableOwnedCharacter`로 구분해 돌려준다.

---

## 5. 순수 판정기 — DungeonAccessService / DungeonAccessResult

`DungeonAccessService`는 `MonoBehaviour`가 아니며 씬에서 대상을 찾거나 카탈로그·저장소를 직접 조회하지 않는다. 생성자에서 `IOwnedCharacterLevelSource` 하나를 받고, 호출자가 넘긴 `DungeonDefinition`을 `Evaluate`하는 메서드 하나만 노출한다.

결과는 `readonly struct DungeonAccessResult`이며 네 값을 담는다.

| 필드 | 뜻 |
| --- | --- |
| `Allowed` | 지금 들어갈 수 있는가 |
| `DungeonRequiredLevel` | 그 던전이 요구하는 레벨 |
| `HighestOwnedLevel` | 판정에 쓰인 최고 보유 레벨 |
| `FailureReason` | 막혔다면 왜 막혔는가 |

거부 사유는 넷이다.

| 사유 | 조건 |
| --- | --- |
| `MissingOrInvalidDungeon` | 던전이 `null`이거나 식별자가 없다 |
| `MissingRosterOrProgression` | 레벨을 물어볼 대상이 없다(로스터 없음) |
| `NoUsableOwnedCharacter` | 최고 보유 레벨이 0이다 |
| `InsufficientLevel` | 최고 보유 레벨이 필요 레벨보다 낮다 |

`None`은 허용된 결과에만 붙는다. **이유 없는 거부를 만들지 않은 것**은 UI가 "왜 안 되는지"를 사용자에게 말할 수 있어야 하고, 로그에도 남아야 하기 때문이다.

**판정은 아무것도 고치지 않는다.** `SaveData`를 쓰지 않고, 저장 항목을 만들지 않으며, 해금 상태를 어디에도 적지 않는다. 물을 때마다 표와 저장된 레벨로 다시 계산한다 — 4단계 스킬 해금과 같은 이유다. 플래그를 저장하면 표의 필요 레벨을 고친 순간 어느 쪽이 맞는지 정할 방법이 없어진다.

---

## 6. 최종 거부자는 DungeonEntryService다

`DungeonEntryService.RequestEnterDungeon`은 요청을 받으면 **요청 기록과 이벤트 발행보다 먼저** 접근 판정을 다시 한다.

```
던전 null 검사 → IsValid 검사 → EvaluateAccess(현재 상태) → 거부면 여기서 끝
                                                        → 통과면 LastRequested* / AcceptedRequestCount 갱신
                                                        → DungeonEnterRequested 발행
```

- **거부된 요청은 `AcceptedRequestCount`를 올리지 않고 `LastRequestedDungeon`도 바꾸지 않는다.** 기록은 "받아들여진 요청"만의 것이다.
- **UI가 미리 계산한 값은 보지 않는다.** 그래서 버튼의 `interactable`을 외부에서 강제로 켜거나, 패널을 거치지 않고 직접 `RequestEnterDungeon`을 불러도 우회되지 않는다.
- 판정 대상이 없으면(로스터 없음) **거부**한다 — fail closed. 판정할 수 없을 때 열어 주는 쪽이 더 위험하다.
- 구독자는 여전히 **"언제나 유효하고 입장 가능한 던전"만** 받는다.

`FieldModeManager`는 그대로 **저수준 소비자**로 남는다. 레벨을 보지 않고 필드 전환 조건만 추가로 확인하며, 이번 변경은 그 사실을 문서 주석에 반영한 것뿐이다.

---

## 7. UI — DungeonListItemView / DungeonPanel

### 필요 레벨 표시

항목마다 `lb_RequiredLevel`에 **`"Lv. N"`**을 그린다. 이 문구는 번역 대상이 아니라 고정 형식이므로 `CultureInfo.InvariantCulture`로 만든다 — 사용자의 CurrentCulture가 무엇이든 자릿수 구분자나 다른 숫자 표기가 끼어들지 않는다.

### 잠긴 항목도 선택은 된다

레벨 미달 던전도 목록에서 **고를 수 있다.** 상세(몬스터/보상)를 봐야 "무엇을 위해 레벨을 올리는지"를 알 수 있기 때문이다. `Button.interactable`은 건드리지 않고, **입장 버튼만** 잠근다.

잠김 표시는 이름/레벨 텍스트의 **알파를 낮추는 것**이다.

- 배수는 `0.4`이며 **절대값이 아니라 프리팹에 설정된(authored) 알파에 대한 비율**이다. 원래 반투명하게 만들어 둔 텍스트도 비율만큼만 더 어두워진다.
- RGB는 건드리지 않는다.
- 되돌릴 기준색은 **바인딩 시점의 색**을 기억해 두고, 다시 기억하기 전에 먼저 되돌린다 — 그래서 잠금이 여러 번 반복돼도 색이 점점 흐려지지 않는다.
- 새 아트를 쓰지 않는다.

### 갱신 시점

| 시점 | 하는 일 |
| --- | --- |
| 목록을 만들 때 | `Bind` 직후, **`SetActive(true)` 이전에** `SetAccessResult`를 확정한다 — 켠 뒤에 낮추면 밝은 상태가 한 프레임 보인다 |
| `CharacterRoster.CharacterStateChanged` | 패널이 열려 있는 동안 구독해서, 레벨이 오르면 **모든 항목의 잠김 표시와 입장 버튼을 즉시 다시 판정**한다 |
| 선택이 바뀔 때 | 입장 버튼을 다시 판정한다 |

구독은 `OnModalOpened`에서 걸고 `OnModalClosed`에서 반드시 푼다(중복 구독 방지 플래그 포함).

### 입장 버튼

```
interactable = 요청을 아직 보내지 않았고 && 선택이 유효하며 && DungeonAccessService 판정을 통과함
```

패널이 로스터를 찾지 못하면 `MissingRosterOrProgression`으로 **전원 잠금**이다(fail closed). 그리고 이것은 표시일 뿐이며, 실제 거부는 6장대로 `DungeonEntryService`가 요청 시점에 다시 한다.

---

## 8. 프리팹 변경 범위

`Assets/Art/UI/Prefab/Dungeon/item_dungeonList.prefab` **한 개만** 바꿨다.

- 자식 `lb_RequiredLevel`(TextMeshProUGUI, 우측 정렬, 폭 46, 기본 텍스트 `Lv. 1`)을 추가하고 루트의 자식 목록에 넣었다.
- 기존 이름 텍스트의 `RectTransform`을 `AnchoredPosition (-25, 0)` / `SizeDelta (-50, 0)`으로 줄여 레벨 칸 자리를 만들었다.
- `DungeonListItemView`의 `requiredLevelText` 참조를 연결했다.
- **루트 크기는 164x40 그대로**이고, 폰트/머티리얼은 기존 항목이 쓰던 것을 그대로 쓴다 — **새 아트를 하나도 추가하지 않았다.**
- 프리팹의 GUID `3660717675ad041de8f30d0fd0390aeb`와 `.meta` 파일은 **바이트 단위로 그대로**다.

`pn_Dungeon.prefab`과 `Assets/Scenes/desktopScene_ReSize.unity`는 **손대지 않았다.**

---

## 9. 저장 형식은 건드리지 않았다

- `SaveData.CurrentSaveVersion`은 **2 그대로**다.
- 마이그레이션 단계를 **추가하지 않았다.**
- **해금/입장 가능 상태를 저장하지 않는다.** 판정은 언제나 표와 저장된 레벨로 다시 계산한다.
- `SaveData.cs`와 `SaveSystem.cs`는 **한 줄도 바뀌지 않았다.**

이번 단계가 저장에 새로 넣을 값이 없기 때문이다. "이 던전을 열었다"를 파일에 적는 순간, 표의 필요 레벨을 고쳤을 때 파일과 표가 어긋나고 어느 쪽이 맞는지 정할 방법이 없어진다.

---

## 10. 파일과 커밋

시작 기준은 `35ba4952`(Wire progression catalogs and document phase 4)이며, 그 시점에 `origin/nas`와 정렬되어 있었고 작업 트리는 깨끗했다.

### A — `b3f7b3965968932334a061162c0cfe9803d6de33` (Add dungeon required level table data)

```text
Assets/TableData/Game/Dungeon.csv                       (컬럼 추가, 9행 값 1)
Assets/Scripts/Dungeon/DungeonDefinition.cs             (requiredCharacterLevel + 접근자)
Assets/Editor/TableData/TableDataCsvReader.cs           (기대 컬럼)
Assets/Editor/TableData/TableDataRows.cs                (DungeonRow 필드)
Assets/Editor/TableData/TableDataValidator.cs           (1 이상 정수 읽기)
Assets/Editor/TableData/TableDataRebuilder.cs           (기록 + VerifyFields)
Assets/Editor/TableData/Tests/DungeonTableTests.cs (+ .meta)
Assets/Generated/TableData/Dungeon/Dungeon_1..9.asset   (각 1줄)
```

### B — `e687a795721ed79521e824582462fca75a1ba479` (Add authoritative dungeon level access gate)

```text
Assets/Scripts/Character/IOwnedCharacterLevelSource.cs  (+ .meta)
Assets/Scripts/Character/CharacterRoster.cs             (인터페이스 구현 + HighestOwnedCharacterLevel)
Assets/Scripts/Dungeon/DungeonAccessResult.cs           (+ .meta)
Assets/Scripts/Dungeon/DungeonAccessService.cs          (+ .meta)
Assets/Scripts/Dungeon/DungeonEntryService.cs           (요청 직전 재판정)
Assets/Scripts/Field/FieldModeManager.cs                (주석 정정, 동작 변경 없음)
Assets/Editor/Dungeon.meta, Assets/Editor/Dungeon/Tests.meta
Assets/Editor/Dungeon/Tests/DungeonAccessTests.cs (+ .meta)
```

### C — `cf7842839c3eda1925d371f3f81c7d1fad3bae20` (Add dungeon level lock UI)

```text
Assets/Scripts/Dungeon/UI/DungeonListItemView.cs        (Lv. N 표시 + 잠김 알파)
Assets/Scripts/Dungeon/UI/DungeonPanel.cs               (판정/구독/버튼/GetSpawnedItem)
Assets/Art/UI/Prefab/Dungeon/item_dungeonList.prefab    (lb_RequiredLevel 추가)
Assets/Editor/Dungeon/Tests/DungeonPanelAccessTests.cs (+ .meta)
```

### D — 이 보고서를 담는 마지막 커밋

```text
ProjectDocs/WorkOrders/dungeon-level-access-foundation-report.md
```

D의 커밋 해시는 여기에 적지 않는다 — **이 문서가 그 커밋에 들어가므로 자기 해시를 담을 수 없다.** D는 "이 보고서를 포함하는 마지막 커밋"으로 식별한다.

---

## 11. 시험

macOS Unity 2022.3.62f3 배치모드 EditMode 전체 실행이다.

| 시점 | 결과 | 이번 단계가 더한 시험 |
| --- | --- | --- |
| 기준선(`35ba4952`) | 527 / 527 | — |
| A 이후 | 544 / 544 | 표 시험 **17** |
| B 이후 | 566 / 566 | 접근/입장 시험 **22** |
| C 이후 및 최종 전체 검증 | **586 / 586** | 패널/프리팹 시험 **20** |

모든 실행에서 **failed 0 / skipped 0 / inconclusive 0, 컴파일 오류 0**이다.

- **A(17)** — 컬럼 위치와 순서, 필수/정수/1 이상 규칙, 0·음수·공란·비정수의 거부, 자동 보정하지 않음, 아홉 행이 모두 1인 것, 생성 에셋에 값이 실린 것, `VerifyFields` 확장.
- **B(22)** — 최고 보유 레벨 산출(미보유·비카탈로그·재생 불가 정의·낡은 항목 제외, 저장 1 미만은 1로 읽되 저장 무변경, 없으면 0), 네 거부 사유, 경계(같음/미만/초과), 판정이 저장 문서를 고치지 않음, 요청 직전 재판정, 거부 시 이벤트 미발행·카운트 미증가, 패널을 거치지 않은 직접 호출의 거부.
- **C(20)** — `Lv. N`이 CurrentCulture와 무관하게 불변인 것, 잠긴 항목이 여전히 선택 가능한 것, authored 알파 × 0.4가 반복 호출에도 누적되지 않고 원색으로 되돌아오는 것, 선택 시 입장 버튼 잠금, `CharacterStateChanged` 즉시 갱신, 로스터 없음 전원 잠금, 프리팹의 `requiredLevelText` 연결과 루트 크기.

### 독립 재실행

Luna가 별도 격리 클론(`/private/tmp/keybuddy-phase5c-luna.tLZpMo`)에서 **`DungeonPanelAccessTests` 20/20**과 **EditMode 전체 586/586**을 독립적으로 다시 돌려 같은 결과를 확인했다.

### 임시 씬/프리팹 연기 시험

클론에만 만든 EditMode 연기 시험 하나가 `Assets/Scenes/desktopScene_ReSize.unity`를 열고 `item_dungeonList` 프리팹을 로드해 **1/1 통과**했다. **이 시험은 저장소에 복사하지 않았다** — 대상 씬을 여는 시험을 상시 스위트에 두면 이후 모든 실행이 그 씬 파일에 손댈 기회를 갖게 된다.

### PlayMode를 돌리지 않았다

**PlayMode는 한 번도 실행하지 않았고, 어떤 시험도 `Application.persistentDataPath`를 쓰지 않았다.** 4단계와 같은 이유다 — 재생하면 `SaveSystem`이 실제 저장 문서를 읽고 쓴다.

따라서 **실행 중 화면에 실제로 어떻게 보이는지, 잠김 알파가 눈에 어떻게 읽히는지, 윈도우 빌드에서만 드러날 수 있는 부분은 이 결과로 보증되지 않는다.**

---

## 12. 범위 증거

| 확인 | 결과 |
| --- | --- |
| 시작 기준 `35ba4952`와 `origin/nas` | 정렬되어 있었고 작업 트리 깨끗 |
| 생성물 변경 | **Dungeon 에셋 9개만** 변경, 각 1줄 |
| 그 9개의 `.meta` / GUID | 변경 없음 |
| `Generated/` 의 World / Currency / Item / Monster / Character / Skill / CharacterSkill | **바이트 동일** |
| `Assets/Scenes/desktopScene_ReSize.unity` | 변경 없음 |
| `Assets/Art/UI/Prefab/Dungeon/pn_Dungeon.prefab` | 변경 없음 |
| `SaveData.cs` / `SaveSystem.cs` | 변경 없음 |
| Localization 에셋, 손으로 관리하는 Data | 변경 없음 |
| 프로덕션 프리팹 `.meta` | 바이트 동일 |
| `git -c core.whitespace=cr-at-eol diff --check` | 경고 없음 |
| 푸시 | 하지 않음 — `origin/nas`는 여전히 `35ba4952` |

`--check`에 `core.whitespace=cr-at-eol`을 준 이유는 표 파일의 관례 때문이다. `Assets/TableData/Game/*.csv`는 **CRLF이고 파일 끝 개행이 없다**(3단계에서 정한 관례). 옵션 없는 평범한 `git diff --check`는 이 두 가지를 각각 `trailing whitespace`와 `no newline at end of file`로 보고하지만, 둘 다 **의도된 파일 형식**이지 결함이 아니다. `cr-at-eol`을 주면 CR을 정상으로 보고 실제 공백 결함만 남는데, 그렇게 봤을 때 경고가 하나도 없다.

작업 트리에 대해서는 **A·B·C 세 구현 커밋 시점에 깨끗했다**는 것까지만 말할 수 있다. 이 보고서 자체는 아직 커밋되지 않았고, **D가 이 파일을 담는다.**

---

## 13. 검토 경로

Luna가 이번 변경에 대해 **Sol 에스컬레이션이 필요하지 않다고 판단**했다. 근거는 다음과 같다.

- 저장 형식 변경 없음, 저장 버전 변경 없음, 씬 변경 없음.
- 생성 파이프라인의 구조적 재작성 없음(컬럼 하나를 기존 규칙 그대로 얹었다).
- 자동 검증이 경계를 결정적으로 확인했다 — 전체 스위트 586/586과 범위 증거 대조.

**Sol 검토는 수행되지 않았다.** 이 보고서의 어떤 문장도 Sol이 이 변경을 봤다고 주장하지 않는다.

---

## 14. 이번 범위에서 뺀 것

| 뺀 것 | 상태 |
| --- | --- |
| **실제 던전 밸런스** | 아홉 던전 모두 `1`이다. 레벨별 차등은 기획 데이터가 정할 값이며 코드는 이미 어떤 값이든 받는다. |
| **던전 세션 원장** | "지금 어느 던전에 들어가 있는가", 진행/이탈/완료 상태를 담는 것이 없다. |
| **던전 보상** | `reward_item_ids`는 표에 있고 정의에 실리지만, 그것을 실제로 지급하는 코드는 없다. |
| **아이템 사용/마을 시스템** | 손대지 않았다. |
| **전투력 스케일링** | 레벨이 던전 난이도나 전투 수치에 영향을 주는 것은 없다. 레벨은 **입장 가부에만** 쓰인다. |

---

## 15. 다음 단계가 쓸 수 있는 경계

던전 세션과 보상 작업은 **해금 상태를 저장하지 않고도** 이번에 만든 것을 그대로 소비할 수 있다.

| 경계 | 쓰임새 |
| --- | --- |
| `DungeonEntryService.DungeonEnterRequested` | **이미 승인된 요청만** 도착한다. 세션 시작, 몬스터 큐 구성, 필드 전환이 여기에 붙으면 되고 레벨을 다시 검사할 필요가 없다. |
| `DungeonAccessService.Evaluate` | 입장 외의 자리에서 "이 던전이 열려 있는가"를 물을 때. 순수 계산기라 저장소 없이 미리보기/시뮬레이션에 쓸 수 있다. |
| `DungeonAccessResult` | `FailureReason` / `DungeonRequiredLevel` / `HighestOwnedLevel`을 그대로 안내 문구나 로그로 쓸 수 있다. |
| `IOwnedCharacterLevelSource` | 레벨 근거를 바꿔 끼울 수 있는 자리. 시험은 이미 이 이음매로 판정기를 격리한다. |
| `DungeonDefinition.RequiredCharacterLevel` | 표가 근거이므로, 밸런스를 넣을 때 **CSV에 값을 넣고 Rebuild하는 것만으로** 판정과 UI가 함께 바뀐다. |

**다음 단계가 지켜야 할 것**도 같다 — 잠금 상태를 파일에 적지 않는다(표가 근거다), 판정은 `DungeonEntryService`가 요청 시점에 하는 것 하나로 유지한다, 그리고 어떤 조회도 저장 항목을 만들지 않는다.
