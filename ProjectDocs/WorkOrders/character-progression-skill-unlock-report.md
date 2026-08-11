# 캐릭터 성장과 스킬 해금 (4단계 보고서)

계정 하나가 공유하던 레벨/경험치를 **캐릭터별 성장**으로 옮기고, 그 위에 **레벨로 열리는 스킬** 판정을 얹은 작업의 기록이다. 네 개의 체크포인트(A~D)로 나누어 진행했다.

---

## 1. 최종 CharacterSaveState

저장 문서(`SaveData.characters`)의 항목 하나가 담는 것은 다음 넷이다.

| 필드 | 뜻 | 기본값 |
| --- | --- | --- |
| `characterId` | 이 항목의 **유일한 키**. `CharacterDefinition.CharacterId`와 같은 값이며 Ordinal 완전 일치로만 비교한다 | (없음) |
| `level` | 이 캐릭터의 레벨 | `1` |
| `currentExp` | **이번 레벨에서 모은** 경험치(누적 총량이 아니다). 4단계 A에서 새로 넣었다 | `0` |
| `currentStamina` | 현재 행동력. `-1`은 "아직 한 번도 초기화되지 않음"이며 `CharacterRoster`가 정의의 Max Stamina로 채운다 | `-1` |

**항목이 존재한다는 것 자체가 보유를 뜻한다.** 3단계에서 정한 이 규칙은 4단계에서도 그대로다 — 항목이 없으면 그 캐릭터를 가지고 있지 않은 것이고, 조회·성장·해금 중 어느 것도 항목을 만들지 않는다.

같은 id가 목록에 두 번 있으면 **먼저 나온 항목**이 근거다. 이 규칙은 `OwnedCharacterCollection` 한 곳에 있고, 성장과 해금 모두 그것을 빌려 쓴다.

---

## 2. 저장 형식 번호가 v2로 남은 이유

`currentExp`를 새로 넣었지만 **`SaveData.CurrentSaveVersion`은 2 그대로다.**

버전을 올리는 기준은 "필드가 늘었는가"가 아니라 **"예전 파일을 그대로 읽으면 뜻이 달라지는가"**다. `currentExp`는

- 예전 v2 파일에는 이 항목이 아예 없고, `JsonUtility`는 없는 항목을 **필드 기본값(0)**으로 읽는다.
- 0은 "이번 레벨에서 아직 아무것도 모으지 않았다"는 **정확히 맞는 뜻**이다. 잘못 해석될 여지가 없다.
- 따라서 변환 단계가 할 일이 없다.

버전을 올리면 아무 일도 하지 않는 v2→v3 단계를 만들어야 하고, 그 빈 단계는 이후 "이 단계는 왜 있는가"를 아무도 답할 수 없게 된다. 그래서 올리지 않았다.

다만 **깊은 사본은 반드시 함께 고쳐야 했다.** `SaveMigrationRunner`의 `CopyCharacters`가 `currentExp`를 옮기지 않으면 변환 작업본이 경험치를 잃고, 변환이 성공하는 순간 그 값이 사라진다. 이 위험을 잊지 않도록 "캐릭터 항목에 필드를 추가하면 깊은 사본도 함께 고쳐야 한다"는 시험을 따로 두었다.

---

## 3. 예전 전역 값은 보존하되 읽지도 쓰지도 않는다

`SaveData.currentLevel` / `SaveData.currentExp`는 계정 하나가 공유하던 예전 레벨/경험치다. 4단계 B 이후 **`PlayerProgress`는 이 두 필드를 읽지도 쓰지도 않는다.**

- **지우지 않은 이유**: 계정 단위 성장을 다시 쓰게 될지 아직 정해지지 않았고, 한 번 지운 값은 되돌릴 수 없다. 필드를 남겨 두면 파일에 적혀 있던 값이 그대로 보존된다.
- **읽지 않는 이유**: 캐릭터별 상태가 생긴 뒤로 그 값은 **누가 싸우든 같이 올라가는 주인 없는 숫자**가 됐다. 화면에 보이는 레벨이 그것이면 어떤 캐릭터를 키워도 같은 숫자가 오른다.

`SaveMigrationRunner`는 이 두 필드를 계속 복사한다 — 보존이 목적이므로 변환 경로에서 사라지면 안 된다.

시험으로 못 박은 것: 저장된 `currentLevel: 7` / `currentExp: 240`인 문서에서 처치와 앱 종료 저장을 거쳐도 **두 값이 한 글자도 바뀌지 않고**, 성장은 캐릭터 항목에만 쌓이며, `totalKillCount`만 계정 값으로 갱신된다.

---

## 4. 경험치와 레벨 규칙, 그리고 담을 수 있는 한계

계산의 주인은 `CharacterProgressionService` **하나뿐**이다. 저장소도 씬도 에셋도 모르고, 넘겨받은 `CharacterSaveState` 하나만 고친다.

### 규칙

- 레벨 하나에 필요한 경험치는 **고정**(`ExperiencePerLevel`, 기본 10)이며 레벨에 따라 달라지지 않는다. 성장 곡선은 아직 정하지 않았다.
- **남는 경험치는 이월된다.** 한 번에 여러 레벨이 오를 수 있다.
- `GetRequiredExperience(level)`은 **총량**이고, `ExperienceRemainingToNextLevel(state)`은 **지금 모아 둔 값을 뺀 나머지**다. 두 말이 같은 것을 가리키지 않도록 이름을 갈랐다.

### 진행도는 하나의 값으로 다룬다

레벨과 이번 레벨 진행도는 화면에 보이는 표기일 뿐이고, 계산은 **"레벨 1 / 경험치 0에서 여기까지 온 총 진행량"** 하나로 한다.

```
진행량 = (level - 1) * 필요량 + exp
```

그래서 **양수를 넣으면 진행량은 절대 뒤로 가지 않는다.** 예전처럼 레벨과 나머지를 따로 굴리면 상한 근처에서 레벨이 잘리며 진행이 되레 줄어드는 자리가 생겼다.

### int.MaxValue는 기획상의 최대 레벨이 아니다

**설계상의 최대 레벨은 없다.** `int.MaxValue`는 저장 칸이 담을 수 있는 한계일 뿐이며, 진짜 최대 레벨이 생긴다면 그것은 이 상수가 아니라 기획 데이터가 정할 값이다.

표현할 수 있는 마지막 자리는 `레벨 = int.MaxValue`, `경험치 = 필요량 - 1`이다. 그 자리를 넘겨 받은 경험치는 **받아들이지 않는다**:

| 상황 | 결과 |
| --- | --- |
| 마지막 자리에서 양수 1을 넣음 | 상태 그대로, `Changed=false`, `ExperienceAdded=0`, `LevelsGained=0` |
| `레벨 max-1 / exp 0`에서 아주 큰 양을 넣음(필요량 10) | 정확히 **19**만 받아들이고 `레벨 max / exp 9`, `LevelsGained=1` |
| 포화 뒤 반복 적립 | 계속 그대로 |
| 최대 레벨의 어긋난 `exp >= 필요량` | `exp = 필요량 - 1`로 정규화, `Changed=true`, `ExperienceAdded=0` |

`ExperienceAdded`는 **요청한 양이 아니라 실제로 받아들인 양**이다. 담을 수 없는 값을 받은 척하면 그 숫자가 거짓말이 된다.

모든 산술은 `long`으로 하고 반복문을 쓰지 않는다 — `int.MaxValue`에 가까운 값에서 레벨을 하나씩 올리는 반복은 사실상 끝나지 않는다.

**어긋난 값은 조회가 고치지 않는다.** 1보다 작은 레벨과 음수 경험치는 `Normalize`를 명시적으로 부를 때만 정리되고, 읽기만 하는 질의는 계산할 때만 하한으로 본다.

---

## 5. 캐릭터 교체 때의 표시와 이벤트

`PlayerProgress.CurrentLevel` / `CurrentExp` / `ExpToNextLevel`은 이제 **지금 전투 중인 캐릭터**의 값을 비춘다. `TotalKillCount`만 계정 전역 값으로 남았다.

### 교체는 획득이 아니다

교체 전용 신호 **`OnCurrentCharacterSynchronized`**를 새로 두었고, 교체 시점에

- `OnExpGained` / `OnLevelUp` / `OnExperienceChanged`는 **하나도 발생하지 않는다.**

그렇게 하지 않으면 Lv.3에서 Lv.12 캐릭터로 갈아탄 순간 레벨업 연출이 아홉 번 쏟아지고, 바가 뒤로 가는 교체는 "경험치를 잃은" 것처럼 보인다.

`PlayerProgressDisplay`는 이 신호를 **즉시 동기화 경로**(`SyncImmediately`)로 받아 진행 중이던 연출을 취소하고 새 캐릭터의 값으로 바로 맞춘다. 초기 로드 동기화와 같은 경로다.

### Awake 순서에 기대지 않는다

`CharacterRoster`는 자기 `Awake`에서 시작 캐릭터를 투입하며 `CurrentCharacterChanged`를 보내는데, 그 `Awake`가 `PlayerProgress.OnEnable`보다 먼저 돌면 **그 신호를 아무도 듣지 못한다.** 구독만으로는 놓친 이벤트를 되찾을 수 없다.

그래서 `OnEnable`에서 구독하고, **`Start`에서 현재 캐릭터를 다시 한 번 그대로 읽어 맞춘다.** `Start`는 씬의 모든 `Awake`가 끝난 뒤에 돌기 때문에 두 순서 중 어느 쪽이든 같은 결과가 된다. 두 순서 모두 시험으로 못 박았다.

### 로스터가 없거나 줄 대상이 없을 때

로스터가 없거나, 투입된 캐릭터가 없거나, 보유가 사라졌거나, 카탈로그가 없는 과도기 구성이면 캐릭터 경험치는 지급되지 않고 **저장 문서에 항목이 생기지도 않는다.** 표시는 안전한 기본값(레벨 1 / 경험치 0)이다.

그래도 **정상적인 처치라면 누적 킬카운트는 오른다** — 그것은 캐릭터가 아니라 계정이 한 일이기 때문이다.

---

## 6. 스킬 해금 질의와 "저장된 플래그가 없다"는 것

`CharacterSkillUnlockService`가 해금 판정의 유일한 주인이다. `CharacterSkillDefinition`과 `CharacterSkillCatalog`는 표의 값을 담는 **정적 데이터**로 남고 스스로 무엇도 열지 않는다.

### 공개 질의

| 질의 | 답 |
| --- | --- |
| `IsUnlocked(characterId, skillId)` | 지금 쓸 수 있는가 |
| `GetUnlockedSkills(characterId)` | 열려 있는 스킬들 |
| `GetLockedSkills(characterId)` | 아직 잠긴 스킬들 |
| `GetNewlyUnlockedSkills(characterId, previousLevel, newLevel)` | 그 구간에서 **새로** 열린 스킬들 |

결과는 **관계 카탈로그의 차례 그대로**이고 같은 스킬을 두 번 담지 않으며, 돌려주는 것은 언제나 **정식 카탈로그의 정의**다.

### 열리는 조건 (하나라도 어긋나면 안 열린다)

1. 캐릭터가 **활성 `CharacterCatalog`**에 있다.
2. 저장 목록의 **먼저 나온 항목**이 보유를 증명한다.
3. 관계가 목록에 있고 두 식별자가 모두 있다.
4. 그 스킬이 **정식 `SkillCatalog`**에 있다.
5. 관계의 `Character` / `Skill` 참조가 비어 있지 않고, 그 참조의 id가 관계에 적힌 id와 **일치**한다. 임포터가 같은 행에서 채우는 연결이므로 어긋났다면 데이터가 깨진 것이다.
6. 실효 레벨(하한 1)이 필요 레벨 **이상**이다.

새로 열림은 `이전 < 필요 <= 이후`다. 이미 열려 있던 것은 다시 나오지 않고, 거꾸로거나 같은 구간은 빈 목록이며, 1보다 작은 값은 계산할 때만 하한으로 본다. `int.MaxValue` 레벨도 안전하다(비교뿐, 산술이 없다).

### 해금 상태를 저장하지 않는 이유

**어디에도 적지 않는다.** 물을 때마다 표와 저장된 레벨로 다시 계산한다.

플래그를 저장하면 표의 필요 레벨을 고친 순간 저장 파일과 표가 어긋나고, **어느 쪽이 맞는지 정할 방법이 없어진다.** 계산이 근거이므로 표를 고치면 답도 함께 바뀐다. 해금 신호를 놓쳤다고 스킬이 잠기는 일도 없다.

어떤 조회도 저장 문서를 고치거나 항목을 만들지 않는다. 카탈로그가 `null`이거나 비어 있어도, id가 비어 있어도 조용히 "없음"으로 답한다.

---

## 7. 저장이 먼저, 알림은 그 뒤 — 그리고 처치 하나당 저장 한 번

### 저장 선행

성장 경로는 세 조각으로 갈라져 있다.

1. `ApplyExperienceToCurrentCharacter` — **값만 고친다**(저장도 알림도 하지 않는다)
2. `SaveProgress` — **한 번 저장한다**
3. `RaiseGrowthEvents` — **알린다**

구독자(토스트·스킬 목록·교체 패널)는 알림을 받은 순간 값을 다시 읽고, 그 자리에서 다른 저장을 부르는 것도 있다. 아직 저장하지 않은 상태에서 알리면 **"화면에는 올라간 레벨이 보이는데 파일에는 없는" 창**이 열리고, 그 사이에 앱이 꺼지면 사용자가 본 것과 다음에 불러오는 것이 달라진다. 저장을 마친 뒤에 알리면 알림이 언제나 **이미 남은 사실**을 가리킨다.

알림 차례는 다음과 같다.

```
OnExpGained → OnLevelUp(오른 수만큼) → OnExperienceChanged → OnSkillUnlocked → CharacterRoster.CharacterStateChanged
```

각 알림 시점의 저장 횟수가 이미 1인지까지 시험이 확인한다.

### 처치 하나당 저장 한 번

`HandleAnyTargetDefeated`의 순서:

1. **먼저** 이 이벤트가 처리할 값인지 가린다 — 빈 `targetId`, 같은 프레임의 같은 id. 걸러진 이벤트는 킬카운트도 경험치도 **둘 다** 건드리지 않는다. 한쪽만 걸러지면 두 값이 서로 어긋난다.
2. 누적 킬카운트는 **캐릭터와 무관하게** 오른다.
3. 경험치는 지금 투입된 보유 캐릭터에게만 간다.
4. **저장은 한 번.** 레벨이 몇 단계 오르든, 해금이 몇 개 열리든 파일 쓰기는 한 번이다.

중복 판정용 `DefeatEventFilter`는 **`PlayerProgress`만의 것**이다. 행동력 쪽(`CharacterRoster`)과 하나를 공유하면 먼저 처리한 쪽이 다른 쪽의 이벤트를 삼킨다.

**행동력이 0이 되는 마지막 처치도 경험치를 받는다.** 성장 경로는 행동력을 아예 보지 않으므로, 행동력을 깎는 `CharacterRoster`가 먼저 처리되든 나중에 처리되든 결과가 같다 — "마지막 한 방은 보상이 없다"는 구독 순서에 딸린 우연한 규칙을 만들지 않았다.

행동력 저장과 성장 저장은 **한 덩어리로 묶지 않는다.** 두 경로만 놓고 보면 서로 다른 주인이 각자 한 번씩 저장한다(관련 회귀 시험에서는 2회) — 둘을 한 거래로 합치면 한쪽의 실패가 다른 쪽을 되돌린다. 인벤토리 보상 등 다른 처치 저장 경로도 별도이며, 이번 단계는 처치 전체를 하나의 트랜잭션으로 합치지 않았다.

`OnApplicationQuit` 호환은 유지하되 예전 전역 레벨/경험치는 쓰지 않는다.

---

## 8. 파일과 커밋

### A — `568062d016c435033b72abbfc3656111b6d9fe51` (Add character experience progression foundation)

```text
Assets/Scripts/Character/CharacterProgressionService.cs (+ .meta)
Assets/Scripts/Common/SaveData.cs                       (CharacterSaveState.currentExp 추가)
Assets/Scripts/Common/SaveMigrationRunner.cs            (깊은 사본에 currentExp 추가)
Assets/Editor/Character/Tests/CharacterProgressionServiceTests.cs (+ .meta)
Assets/Editor/Common/Tests/SaveMigrationTests.cs
Assets/Editor/Common/Tests/SaveSystemIntegrationTests.cs
```

### B — `de006ac796432e559afdb4874477543204c90a39` (Route defeat experience to current character)

```text
Assets/Scripts/Common/PlayerProgress.cs                 (전역 레벨 → 현재 캐릭터 성장)
Assets/Scripts/Common/PlayerProgressDisplay.cs          (교체 즉시 동기화 경로)
Assets/Scripts/Character/CharacterRoster.cs             (TryGetCurrentState / GetExp)
Assets/Editor/Common/Tests/PlayerProgressCharacterTests.cs (+ .meta)
```

### C — `6a2ef4707805d8b27af420d2b8195caa356b9d87` (Add character skill level unlock evaluation)

```text
Assets/Scripts/Skill/CharacterSkillUnlockService.cs     (+ .meta)
Assets/Scripts/Common/PlayerProgress.cs                 (해금 신호 + 저장 선행 분리)
Assets/Scripts/Character/CharacterRoster.cs             (읽기 전용 Catalog)
Assets/Scripts/Skill/CharacterSkillCatalog.cs           (주석 정정, 동작 변경 없음)
Assets/Scripts/Skill/CharacterSkillDefinition.cs        (주석 정정, 동작 변경 없음)
Assets/Editor/Skill.meta, Assets/Editor/Skill/Tests.meta
Assets/Editor/Skill/Tests/CharacterSkillUnlockTests.cs  (+ .meta)
```

### D — 이 보고서를 담는 마지막 커밋

```text
Assets/Scenes/desktopScene_ReSize.unity
ProjectDocs/WorkOrders/character-progression-skill-unlock-report.md
Assets/Editor/Character/Tests/CharacterProgressionServiceTests.cs.meta   (공백 정리)
Assets/Editor/Common/Tests/PlayerProgressCharacterTests.cs.meta          (공백 정리)
Assets/Editor/Skill.meta                                                 (공백 정리)
Assets/Editor/Skill/Tests.meta                                           (공백 정리)
Assets/Editor/Skill/Tests/CharacterSkillUnlockTests.cs.meta              (공백 정리)
Assets/Scripts/Character/CharacterProgressionService.cs.meta             (공백 정리)
Assets/Scripts/Skill/CharacterSkillUnlockService.cs.meta                 (공백 정리)
```

뒤의 일곱 `.meta`는 4단계가 새로 만든 파일들로, **빈 YAML 값 뒤의 줄 끝 공백만 지웠다**(`userData:` / `assetBundleName:` / `assetBundleVariant:` 세 줄, 파일당 3곳, 모두 21곳). GUID도 임포터 설정도 그 밖의 어떤 줄도 건드리지 않은 **순수한 공백 정리**이며, `git diff -w`로 보면 차이가 남지 않는다.

체크포인트 D의 커밋 해시는 여기에 적지 않는다 — **이 문서가 그 커밋에 들어가므로 자기 해시를 안정적으로 담을 수 없다.** D는 "이 보고서를 포함하는 마지막 커밋"으로 식별한다.

---

## 9. 씬에서 바꾼 블록의 범위

대상 씬(`Assets/Scenes/desktopScene_ReSize.unity`)에서 손댄 것은 **`PlayerProgress` MonoBehaviour 블록 하나뿐**이다(`&628316359`). 실제 변경은 두 줄 삭제, 두 줄 추가다.

```diff
   m_EditorClassIdentifier:
-  currentLevel: 1
-  currentExp: 0
   totalKillCount: 0
   expToNextLevel: 10
   expPerTargetDefeat: 1
+  skillCatalog: {fileID: 11400000, guid: 6f83e310e008c4b8dbc41d382a075cac, type: 2}
+  characterSkillCatalog: {fileID: 11400000, guid: 3f9eaae2823dc4c8a87a194e8165f086, type: 2}
```

- **`currentLevel` / `currentExp` 줄을 지운 이유**: 4단계 B에서 그 private 직렬화 필드를 제거했다. 남겨 두면 어떤 필드도 가리키지 않는 죽은 줄이 된다.
- **보존한 것**: `totalKillCount` / `expToNextLevel` / `expPerTargetDefeat`의 값, 그리고 이 블록과 씬 전체의 모든 object/file ID.
- **건드리지 않은 것**: Canvas, RectTransform, 폰트, 레이아웃, 그 밖의 모든 컴포넌트. 다른 씬도 손대지 않았다.

두 GUID는 각 에셋의 `.meta`에서 그대로 가져왔다.

| 에셋 | GUID |
| --- | --- |
| `Assets/Generated/TableData/Skill/SkillCatalog.asset` | `6f83e310e008c4b8dbc41d382a075cac` |
| `Assets/Generated/TableData/CharacterSkill/CharacterSkillCatalog.asset` | `3f9eaae2823dc4c8a87a194e8165f086` |

캐릭터 카탈로그는 이 칸에 두지 않았다 — `PlayerProgress`는 `CharacterRoster.Catalog`(읽기 전용 이음매)를 따른다. 두 곳이 캐릭터 목록을 따로 들면 로스터가 인정한 캐릭터와 스킬이 인정한 캐릭터가 달라진다.

---

## 10. 프로덕션 Skill / CharacterSkill 표가 비어 있는 이유

지금 `Skill.csv`와 `CharacterSkill.csv`에는 **실제 행이 하나도 없고, 그것이 정상이다.** 어떤 스킬을 만들지 아직 정하지 않았기 때문이며, 오류도 경고도 아니다.

그래서 씬에 연결된 두 생성 카탈로그도 **비어 있다**(`Count == 0`). 그 상태에서

- 해금 질의는 전부 "없음"으로 답하고,
- `OnSkillUnlocked`는 **한 번도 발생하지 않으며**,
- 성장 자체는 평소대로 동작한다.

이번에 씬을 연결해 두는 것은 **표에 행이 생기는 날 씬을 다시 건드리지 않기 위해서**다. 배선이 미리 되어 있으면 CSV에 행을 넣고 Rebuild하는 것만으로 해금이 살아난다.

---

## 11. 시험

격리 클론에서 돌린 EditMode 전체 결과다.

```text
total 528 / passed 528 / failed 0 / skipped 0 / inconclusive 0
컴파일 오류 0
```

`528 = 프로덕션 시험 527 + 임시 씬 연기 시험 1`이다. 4단계 시작 시점의 기존 419개는 모두 유지됐고, 프로덕션 시험 108개가 추가되어 527개가 됐다. 씬 연기 시험은 **격리 클론에만 만들었고 원본 저장소에 넣지 않았다** — 대상 씬을 여는 시험을 상시 스위트에 두면 이후 모든 실행이 그 씬 파일에 손댈 기회를 갖게 된다.

fixture별:

| 개수 | Fixture |
| --- | --- |
| 39 | `CharacterProgressionServiceTests` |
| 41 | `CharacterRosterCatalogTests` |
| 21 | `OwnedCharacterCollectionTests` |
| 27 | `PlayerProgressCharacterTests` |
| 85 | `SaveMigrationTests` |
| 19 | `SaveStorageTests` |
| 21 | `SaveSystemIntegrationTests` |
| 16 | `CurrencyCatalogTests` |
| 25 | `DefeatRewardTests` |
| 11 | `InventoryCurrencyOverflowTests` |
| 1 | `PhaseFourSceneSmokeTests` (임시) |
| 25 | `CharacterSkillUnlockServiceTests` |
| 10 | `PlayerProgressSkillUnlockTests` |
| 6 | `CharacterLocalizationBindingTests` |
| 22 | `CharacterSkillTableTests` |
| 30 | `CharacterTableOutputTests` |
| 49 | `CharacterTableTests` |
| 28 | `CurrencyTableTests` |
| 25 | `MonsterRewardRulesTests` |
| 27 | `SkillTableTests` |

### 4단계가 더한 시험 (+108, 임시 씬 연기 제외)

- **A: 46** — 신규 `CharacterProgressionServiceTests` 39개가 적립과 이월, 여러 레벨, 어긋난 값의 정규화, 결과 모델, 그리고 담을 수 있는 마지막 자리(포화, 실제 수용량, 반복 적립, 정규화, 남은 여유)를 검증한다. 기존 `SaveMigrationTests` 6개와 `SaveSystemIntegrationTests` 1개도 `currentExp` 기본값·깊은 사본·v0→v1→v2·실제 메모리/임시 저장 왕복을 확인하도록 확장했다.
- **B: `PlayerProgressCharacterTests` 27** — 보유한 현재 캐릭터에게만 지급, 투입 없음/로스터 없음/보유 사라짐/저장 전용 id/과도기 구성, 중복·빈 이벤트, 마지막 행동력(구독 순서 양쪽), 저장 분리, 여러 레벨에서 저장 한 번, 예전 전역 값 보존, 캐릭터별 독립, 교체의 즉시 동기화, Awake 두 순서, `AddExp` 계약.
- **C: `CharacterSkillUnlockServiceTests` 25 + `PlayerProgressSkillUnlockTests` 10** — 아래/같음/위 경계, 구간 다중 해금, 재발급 없음, 보유·활성 캐릭터 가드, 스킬 없음/반쪽 관계/참조 비었음/참조 id 어긋남, 대소문자·공백 그대로, 중복 제거, 목록 차례, 프로덕션 빈 카탈로그, 조회 무변경, `int.MaxValue`, 그리고 해금 신호 동작·저장 한 번·**저장 선행 순서**.
- **D: 임시 씬 연기 1** — 아래 참조.

### 씬 연기 시험이 확인한 것

대상 씬을 **PlayMode 없이** 에디터에서 열어(`OpenSceneMode.Additive`; `PlayerProgress`는 `ExecuteInEditMode`가 아니므로 `Awake`가 돌지 않는다) 다음을 단언했다.

1. 씬에서 `PlayerProgress`를 찾는다.
2. `skillCatalog` / `characterSkillCatalog`가 **생성 폴더의 그 에셋 자체**다(`AreSame` + `AssetDatabase.GetAssetPath` 일치).
3. 두 카탈로그의 `Count`가 모두 **0**이다.
4. `totalKillCount` 0 / `expToNextLevel` 10 / `expPerTargetDefeat` 1이 보존됐다.
5. `currentLevel` / `currentExp` 필드가 타입에서 **사라졌다**.
6. 시험 전후 **씬 파일의 SHA-256이 같다** — 씬을 저장하거나 다시 쓰지 않았다.

### 되돌리기 증명

클론에서 씬의 연결을 커밋된(연결 전) 블록으로 되돌리자 씬 연기 시험이 **정확히 실패**했다(`Skill Catalog가 연결되지 않았습니다`). 되돌린 코드는 클론에만 있었고 확인 뒤 원본과 대조해 지웠다.

### 생성물이 그대로임을 보인 증거

| 확인 | 결과 |
| --- | --- |
| `git status -- Assets/Generated TableData` | 변경 없음 |
| `Assets/Generated/TableData` 전체 파일 해시 매니페스트 (원본) | `a962f394ea2f9d9127d015486207485009ea113a5f92ec3e4fc1974891886d28` |
| 같은 매니페스트 (클론, 전체 시험 실행 뒤) | `a962f394ea2f9d9127d015486207485009ea113a5f92ec3e4fc1974891886d28` |
| 클론 `Assets` vs 원본 `Assets` (임시 하네스 제거 후) | 드리프트 없음 |
| `git diff --check 1ad8fdc3` (4단계 시작 직전 커밋부터 지금 작업분까지 전 구간, 미커밋 변경 포함) | 경고 없음 |

CSV는 이번 4단계에서 한 줄도 바꾸지 않았다(3단계 보고서 12장의 CRLF 관례 참조). 위의 일곱 `.meta` 공백 정리를 마친 뒤 **4단계 시작 직전 커밋 `1ad8fdc3`부터 지금 작업분까지의 전 구간**을 `git diff --check 1ad8fdc3`로 확인했고, 미커밋 변경을 포함해 경고가 하나도 남지 않았다.

---

## 12. PlayMode를 돌리지 않은 것과 저장 경로 보호

**대상 씬의 PlayMode는 한 번도 실행하지 않았다.** 이 보고서의 어떤 문장도 화면에 무엇이 보이는지, 연출이 어떻게 재생되는지를 확인했다고 말하지 않는다.

이유는 하나다 — **PlayMode는 실제 저장 파일을 건드린다.** 씬을 재생하면 `SaveSystem`이 `Application.persistentDataPath` 아래의 진짜 저장 문서를 읽고, 성장·행동력·회복이 그 파일에 쓴다. 검증하려다 사용자의 실제 진행을 덮어쓰는 것은 어떤 확인보다도 비싸다.

그래서 모든 시험은 다음 규칙을 지킨다.

- `persistentDataPath`를 **어디에서도 쓰지 않는다.**
- 저장 문서는 리플렉션으로 직접 끼워 넣고, 저장은 **메모리 저장소**로 받아 쓰기 횟수만 센다.
- MonoBehaviour는 **비활성 호스트**에 붙인 뒤 `Awake` / `OnEnable` / `Start`를 시험이 직접 부른다 — 그래야 Unity가 수명 주기를 대신 돌리며 저장소에 손대지 않고, "Awake 순서가 달라도 같은 결과인가"를 시험이 정할 수 있다.
- 씬 연기 시험도 **에디터에서 열기만** 하고 저장하지 않으며, 그것을 파일 해시로 확인한다.

검증은 전부 macOS Unity 2022.3.62f3 배치모드 EditMode다. **실제 씬에서의 동작, 교체 연출의 눈에 보이는 결과, 윈도우 빌드에서만 드러날 수 있는 부분은 이 결과로 보증되지 않는다.**

---

## 13. 이번 범위에서 뺀 것

| 뺀 것 | 이유 |
| --- | --- |
| **성장 곡선** | 레벨마다 필요 경험치가 달라지는 규칙. `GetRequiredExperience(level)`이 레벨을 인자로 받아 두었으므로, 곡선이 생겨도 **부르는 쪽을 고칠 필요가 없다.** |
| **기획상의 최대 레벨** | 상한은 기획 데이터가 정할 값이지 계산이 정할 값이 아니다. 지금 있는 것은 저장 칸의 한계뿐이다. |
| **레벨업 보상** | 공격력 증가, 능력치 성장 등. 레벨이 오른다는 사실만 있고 그것이 무엇을 주는지는 아직 없다. |
| **스킬의 실제 효과** | `SkillDefinition.BehaviorKey`는 문자열일 뿐이고, 그것을 보고 무엇을 실행하는 코드는 없다. 이번 범위는 "쓸 수 있는가"까지다. |
| **스킬 UI** | 해금된 스킬 목록 패널, 해금 토스트. `OnSkillUnlocked`와 세 질의가 준비되어 있어 UI만 붙이면 된다. |
| **표의 실제 스킬 행** | 10장 참조. |
| **계정 단위 성장의 복구 또는 삭제** | 3장 참조 — 보존만 하고 결정을 미뤘다. |

---

## 14. 다음 단계(던전/스테이지)가 쓸 수 있는 공개 경계

이번에 만든 것 중 다른 시스템이 그대로 부를 수 있는 자리다.

### 성장

| 경계 | 쓰임새 |
| --- | --- |
| `PlayerProgress.AddExp(int)` | **처치 말고 다른 경로로 경험치를 주는 자리.** 스테이지 클리어 보상, 퀘스트 보상이 여기로 들어온다. 지금 투입된 보유 캐릭터에게 적용되고, 실제로 달라졌을 때만 저장·알림이 나가며, 저장이 알림보다 먼저 끝난다. |
| `CharacterProgressionService` | 저장소를 모르는 순수 계산기. 미리보기("이만큼 주면 몇 레벨이 되나")나 시뮬레이션이 저장 문서 없이 쓸 수 있다. |
| `PlayerProgress.OnLevelUp` / `OnExpGained` / `OnExperienceChanged` / `OnCurrentCharacterSynchronized` | 연출·토스트·HUD가 붙는 자리. 교체와 획득이 갈라져 있으므로 각자 필요한 것만 구독하면 된다. |

### 보유와 상태

| 경계 | 쓰임새 |
| --- | --- |
| `CharacterRoster.TryGetCurrentState(out 정식 정의, out 저장 항목)` | **현재 캐릭터의 저장 항목을 직접 고쳐야 하는** 시스템이 쓰는 단 하나의 이음매. 만들지 않으며, 투입 없음/미보유/과도기 구성이면 `false`다. |
| `CharacterRoster.GetLevel` / `GetExp` / `GetStamina` / `GetMaxStamina` | 정의를 받아 그 캐릭터의 값을 돌려주는 조회 가족. 쓸 수 없는 캐릭터는 0이고 항목이 생기지 않는다. |
| `CharacterRoster.Catalog` | 지금 쓰이는 활성 캐릭터 카탈로그(읽기 전용). 캐릭터 id를 근거로 계산하는 시스템은 **자기 Inspector 칸을 따로 두지 말고 이것을 따른다.** |
| `CharacterRoster.RaiseCharacterStateChanged(정의)` | 상태를 고친 뒤 알리는 정식 경로. 정식 정의를 넘기고 보유가 사라진 캐릭터를 걸러 준다. |

### 스킬

| 경계 | 쓰임새 |
| --- | --- |
| `CharacterSkillUnlockService.IsUnlocked` | 스테이지 전투가 "이 스킬을 쓸 수 있는가"를 물을 자리. |
| `GetUnlockedSkills` / `GetLockedSkills` | 스킬 목록 UI, 성장 미리보기. |
| `GetNewlyUnlockedSkills` | 구간 보상 계산. `PlayerProgress`가 아닌 다른 성장 경로(스테이지 보상 등)가 생겨도 같은 질의로 해금을 알릴 수 있다. |
| `PlayerProgress.OnSkillUnlocked` | 해금 토스트/연출이 붙는 자리. |

**던전/스테이지가 지켜야 할 것**은 이번에 세운 규칙과 같다 — 보상은 실제로 투입된 보유 캐릭터에게만, 저장은 한 덩어리로 한 번, 알림은 저장이 끝난 뒤에, 그리고 어떤 조회도 저장 항목을 만들지 않는다.
