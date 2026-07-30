# 캐릭터 교체 UI 기능 연결 완료 보고서

작업일: 2026-07-29
대상 씬: `Assets/Scenes/desktopScene.unity` (계층은 변경하지 않음)

이 문서는 지시서의 항목 순서를 그대로 따라간다. 각 항목에 **구현 위치**와 **검증 상태**를 함께 적고,
마지막에 Unity 에디터에서 사람이 해야 하는 연결 작업을 체크리스트로 정리한다.

## 0. 이번 작업의 전제

지시서대로 UI를 새로 만들거나 레이아웃을 바꾸지 않았다. 스크립트와 데이터 에셋만 추가/수정했고,
씬(`desktopScene.unity`)과 프리팹(`list_Character.prefab`)은 **한 줄도 건드리지 않았다** - 작업 중
Unity 에디터가 프로젝트를 열고 있어서(에디터 락 보유) YAML을 직접 고치면 에디터가 저장하는 순간
덮어써지기 때문이다. 그래서 씬/프리팹 연결은 8장 체크리스트로 넘긴다.

대신 런타임 코드가 "이름으로 자동 탐색"과 "없으면 만들기"를 지원하도록 만들어서, 손으로 연결해야
하는 항목 수를 최소로 줄였다.

## 1. 캐릭터 교체 버튼

| 요구 | 구현 |
| --- | --- |
| `btn_character` 항상 노출 | 씬 변경 없음(그대로) |
| 클릭 시 `pn_CharacterSwap` 활성화 | `CharacterSwapPanelOpener` → `CharacterSwapPanel.Open()` |
| 열릴 때 리스트 갱신 | `CharacterSwapPanel.OnEnable` → `RebuildList()` |
| 배경 전투 UI로 입력 전달 차단 | 전체 화면 `InputBlocker`(런타임 생성) + Windows 클릭 관통 예외 등록 |

`pn_CharacterSwap`은 씬 시작 시 비활성이라 자기 자신은 "열어달라"는 신호를 받을 수 없다. 그래서
여는 쪽(`btn_change`)에 `CharacterSwapPanelOpener`를 붙이고 비활성 패널 참조를 직접 들고 있게 했다.

**입력 차단의 범위**: 마우스 클릭만 막는다. 키보드 공격 입력(`GlobalKeyboardHook.AnyKeyDownThisFrame`)은
막지 않았다 - 이 앱은 다른 창에서 타이핑하는 동안 캐릭터가 공격하는 것이 기본 동작이라, 패널이 떠
있다는 이유로 전역 키 입력을 끊는 것은 지시서 범위를 넘는 동작 변경이라고 판단했다. 필요하면
`CharacterSwapPanel.OnEnable/OnDisable`에서 플래그 하나만 추가하면 된다.

## 2. 캐릭터 교체 패널

- 씬 시작 시 비활성: 씬의 `pn_CharacterSwap`이 이미 `m_IsActive: 0`이라 그대로 둔다.
- `btn_close` 클릭 시 닫기: `CharacterSwapPanel`이 이름으로 찾아 `Close()`에 연결한다.
- 패널 외부 클릭 닫기: 구현하지 않음(지시서에서 필수 제외).
- 다시 열 때 최신 상태 반영: `OnEnable`마다 `pendingCharacter`를 비우고 리스트를 다시 만든다.

## 3. 캐릭터 리스트 생성

`list_Character` 프리팹 인스턴스(씬의 `list` 아래 비활성 오브젝트)를 원본으로 복제한다.

표시 항목과 소스:

| 표시 | 소스 |
| --- | --- |
| 초상화 | `CharacterDefinition.Portrait` (비어 있으면 Motion Profile Base Idle 0번 프레임) |
| 이름 | `CharacterDefinition.DisplayName` (비어 있으면 Motion Profile Display Name) |
| 레벨 | `SaveData.characters[].level` |
| 현재/최대 행동력 | `SaveData.characters[].currentStamina` / `CharacterDefinition.MaxStamina` |
| 행동력 상태 | 전투 가능 / 사용 중 / 행동력 소진 (로컬라이징 Key 5·6·7) |

### 데이터 소스를 하나로 합친 방식

기존 캐릭터 관리 구조는 `RuntimeCharacterSwitcher`(ControlDock의 테스트 버튼)뿐이었다. 이 컴포넌트가
캐릭터 GameObject 배열과 시작 인덱스를 직접 들고 켜고 껐기 때문에, 교체 패널이 같은 일을 하면
활성화 주체가 둘이 된다. 그래서:

- **`CharacterRoster`**(신규)가 목록·현재 캐릭터·교체 처리를 단독으로 소유한다.
- `RuntimeCharacterSwitcher`는 배열/인덱스 필드를 버리고 `CharacterRoster.SwitchToNext()`만 호출한다.
- 캐릭터가 무엇인지는 **`CharacterDefinition` 에셋**, 지금 어떤 상태인지는 **저장 데이터**가 소유한다.
  씬 오브젝트는 "그 캐릭터를 화면에 그리는 수단"일 뿐 목록의 근거가 아니다.
- 모션 데이터는 지금까지대로 `CharacterMotionProfile`만 소유한다. `CharacterDefinition`은 그 프로필을
  참조만 하고, 정의와 씬 오브젝트의 프로필이 다르면 시작 시 오류를 남긴다.

## 4. ScrollRect

`list`에 스크롤 구조가 없으면 `CharacterSwapPanel.EnsureScrollStructure()`가 런타임에 만든다.

```text
list          (ScrollRect 추가, 기존 Vertical Layout Group은 파괴하지 않고 끔)
└ Viewport    (없으면 생성, RectMask2D)
  └ Content   (없으면 생성, Vertical Layout Group + Content Size Fitter[Vertical=PreferredSize])
    └ list_Character 복제본
```

- 이미 만들어 둔 구조가 있으면 그대로 재사용한다(여러 번 호출해도 안전).
- `list`에 직접 붙어 있던 Vertical Layout Group은 ScrollRect의 Viewport를 강제 배치해 스크롤을
  망가뜨리므로, 설정값(여백/간격/정렬)만 Content로 옮기고 원본은 **지우지 않고 끈다**.
- 항목 높이는 프리팹 값(96px)을 그대로 쓰고(Child Control Height off) 가로만 리스트 폭에 맞춘다.
- Movement Type은 Clamped라 캐릭터가 적을 때도 위쪽에 정상적으로 붙는다.

## 5. 캐릭터 선택과 교체

`currentCharacter`(= `CharacterRoster.Current`)와 `pendingCharacter`(= 패널 로컬 상태)를 분리했다.
리스트 클릭은 `pendingCharacter`만 바꾸고 전투 캐릭터는 건드리지 않는다.

교체 순서(`btn_swap` 클릭 → `CharacterRoster.TrySwitchTo`):

1. `GetSwapBlockReason`으로 검증 (`AlreadyCurrent` / `NoStamina` / `NotAvailable`)
2. 목록의 다른 캐릭터 GameObject를 **먼저 전부 끈다**
3. 선택한 캐릭터 GameObject를 켠다
4. `ComboManager.ResetCombo()`
5. `CurrentCharacterChanged` 발생 → 패널이 리스트를 갱신
6. 패널을 닫는다

행동력 0 처리는 **선택 자체를 막는 방식**을 택했다. 항목 Button의 `interactable`을 끄고(현재 사용
중인 캐릭터도 동일), 상태 문구와 배경색으로 이유를 함께 보여준다. `btn_swap`은 "지금 이 선택으로
교체가 실제로 일어날 수 있을 때"만 켜지므로, 눌렀는데 조용히 실패하는 경로가 없다. 그래도 버튼을
누르는 사이에 상태가 바뀐 경우에는 경고 로그를 남기고 선택을 해제한 뒤 패널을 열어 둔 채로
리스트를 갱신한다.

## 6. 선택 상태 피드백

`CharacterSwapListItem`이 배경 Image 색을 상태별로 바꾼다(Inspector에서 조정 가능).

| 상태 | 기본 색 |
| --- | --- |
| 전투 가능(미선택) | `normalColor` – 흰색 |
| 교체 대상으로 선택됨 | `selectedColor` – 노란빛 |
| 지금 전투 중 | `inUseColor` – 하늘빛 |
| 행동력 소진 | `exhaustedColor` – 회색 |

추가로 `btn_swap`의 활성/비활성 상태가 함께 바뀐다. 최종 연출(테두리/체크 아이콘)은 후속 작업이다.

## 7. 행동력 표시

경험치 바의 **시각 구조만** 재사용하기 위해 `ProgressBarView`(신규)를 만들었다.

- `PlayerProgress`, 경험치 획득/레벨업 이벤트를 **구독하지 않는다**(구독 코드 자체가 없다).
- 값은 호출부가 `SetValue(current, max)`로 주입한다.
- 행동력이 바뀌면 `CharacterRoster.CharacterStateChanged`가 그 캐릭터를 지목해서 보내고, 패널은
  해당 항목 하나만 다시 그린다.
- 같은 GameObject에 `PlayerProgressDisplay`가 함께 붙어 있으면 OnEnable에서 오류를 남긴다 - 둘이
  같은 Slider를 번갈아 덮어써서 행동력 막대가 경험치 값으로 덮이는 사고를 시작 시 잡기 위함이다.

**경험치 HUD(`expProgress`)는 손대지 않았다.** `PlayerProgressDisplay`는 레벨업 연출 큐를 갖고 있어
단순 대입과 동작이 다르고, 지금 그 애니메이션을 `ProgressBarView`로 옮기면 경험치 UI 회귀 위험만
커진다. 경험치 쪽까지 하나의 컴포넌트로 합치는 것은 별도 작업으로 남긴다.

## 8. 캐릭터 교체 시 주의사항

| 지시서가 지목한 문제 | 처리 |
| --- | --- |
| 이전 캐릭터의 입력 이벤트 잔존 | `PlayerCharacterAnimator.OnDisable`이 대기열·충전·발사체를 이미 비운다(기존 코드) |
| 두 캐릭터 동시 활성화 | `CharacterRoster.ApplyActiveCharacter`가 나머지를 먼저 전부 끈 뒤 하나만 켠다 |
| 공격 중 교체 시 애니메이션 고정 | **`PlayerCharacterAnimator.OnEnable` 신규 추가** – 아래 설명 |
| 콤보가 새 캐릭터에게 전달 | `ComboManager.ResetCombo()` 신규 추가, 교체 시 호출 |
| 행동력 표시가 이전 값으로 잔존 | 패널을 열 때마다 리스트 재구성 + 상태 변경 시 해당 항목 갱신 |
| 패널을 닫았는데 입력이 계속 차단 | `InputBlocker`는 `OnDisable`에서 반드시 꺼지고, 클릭 관통 예외도 함께 해제된다 |

### `PlayerCharacterAnimator.OnEnable`을 추가한 이유 (기존 잠재 버그)

`Awake`는 오브젝트당 한 번만 실행된다. 지금까지 캐릭터 교체는 GameObject를 껐다 켜는 방식인데,
`OnDisable`이 공격 단계(`attackPhase`)만 비우고 `activeAnimIndex` / `currentFrame` / `playingVariant`는
그대로 두고 있었다. 그래서 **두 번째로 활성화되는 캐릭터**는 꺼지기 직전의 프레임과 Idle Event 재생
상태를 그대로 들고 돌아왔다(기존 주석은 "Awake가 매번 초기화한다"고 적혀 있었지만 실제로는 첫
활성화에만 해당했다). `OnEnable`에서 Base Idle 0프레임으로 되돌리도록 고쳤다.

## 9. 저장 데이터 구조 변경

`SaveData`에 캐릭터별 상태를 추가하면서, 저장 문서를 여러 시스템이 함께 쓰게 되었다.

```csharp
SaveData
├ currentLevel / currentExp / totalKillCount   ← PlayerProgress 소유
└ characters : List<CharacterSaveState>        ← CharacterRoster 소유
    └ characterId / level / currentStamina
```

기존 `SaveSystem`은 저장할 때마다 호출부가 **새 SaveData를 만들어 넘기는** 구조였다. 그대로 두면
`PlayerProgress.SaveProgress()`가 캐릭터 상태를 통째로 지운다. 그래서 `SaveSystem`을 "프로세스가
공유하는 문서 하나(`SaveSystem.Data`)를 각자 고치고 `SaveSystem.Save()`로 기록"하는 구조로 바꿨다.

- `SaveSystem.Load()` → `SaveSystem.Data` + `SaveSystem.LoadedFromFile`로 대체(호출부는 `PlayerProgress` 하나뿐이었다).
- 기존 저장 파일에 `characters`가 없어도 그대로 읽힌다(없으면 빈 목록으로 보정 후 정의 기본값으로 채움).

**행동력 소비/회복 규칙은 만들지 않았다**(지시서 범위 밖). `CharacterRoster.SpendStamina` /
`SetStamina` 진입점만 열어 두었고, 지금은 아무도 호출하지 않으므로 모든 캐릭터의 행동력은 최대치를
유지한다. 아래 "행동력 0 상태 검증" 참고.

## 10. 추가/수정한 파일

### 신규

| 파일 | 역할 |
| --- | --- |
| `Assets/Scripts/Character/CharacterDefinition.cs` | 캐릭터 정의 에셋(저장 키/이름/초상화/최대 행동력) |
| `Assets/Scripts/Character/CharacterRoster.cs` | 목록·현재 캐릭터·교체 처리의 단일 관리자 |
| `Assets/Scripts/Common/ProgressBarView.cs` | 범용 진행도 표시(경험치 로직과 무관) |
| `Assets/Scripts/Common/CharacterSwap/CharacterSwapPanel.cs` | 패널 열기/닫기, 리스트 구성, 선택·교체 |
| `Assets/Scripts/Common/CharacterSwap/CharacterSwapListItem.cs` | 리스트 항목 한 개의 표시 |
| `Assets/Scripts/Common/CharacterSwap/CharacterSwapPanelOpener.cs` | `btn_change` → 패널 열기 |
| `Assets/Data/Characters/*_CharacterDefinition.asset` | 6종 캐릭터 정의(Motion Profile 연결 완료) |

### 수정

| 파일 | 변경 |
| --- | --- |
| `Assets/Scripts/Common/SaveData.cs` | `characters` 목록과 `CharacterSaveState` 추가 |
| `Assets/Scripts/Common/SaveSystem.cs` | 공유 저장 문서 구조로 전환 |
| `Assets/Scripts/Common/PlayerProgress.cs` | 공유 문서에 맞춰 로드/저장 경로만 변경(성장 로직 동일) |
| `Assets/Scripts/Common/ComboManager.cs` | `ResetCombo()` 추가 |
| `Assets/Scripts/Common/RuntimeCharacterSwitcher.cs` | 로스터 위임 전용으로 축소 |
| `Assets/Scripts/Character/PlayerCharacterAnimator.cs` | `OnEnable` 초기화 추가 |
| `Assets/Scripts/DesktopWindow/TransparentWindowController.cs` | `SetModalClickableRect()` 추가 |
| `TableData/Localization/01_UI.csv` | Key 5·6·7(전투 가능 / 사용 중 / 행동력 소진) 추가 |

## 11. Unity 에디터에서 해야 하는 연결 작업

> 스크립트가 컴파일된 뒤에 진행한다. 이름 자동 탐색이 되는 항목은 "비워 둬도 됨"으로 표시했다.

### 11-1. 로컬라이징 Import

1. `TableData/Localization/01_UI.csv`를 `01_UI` Collection에서 `Import > CSV(Merge)...`로 반영한다.
2. Key 5·6·7이 들어왔는지 확인한다.

### 11-2. `CharacterRoster` 배치

1. `StageVisualRoot`(또는 씬의 아무 관리 오브젝트)에 `CharacterRoster`를 추가한다.
2. Entries를 6개 만들고 각 행에 연결한다.

| Definition (`Assets/Data/Characters/`) | Character Object (`StageVisualRoot` 아래) |
| --- | --- |
| `CatKnight_CharacterDefinition` | `Cha_CatKnight` |
| `Barbarian_CharacterDefinition` | `Cha_Barbarian` |
| `CatMage_CharacterDefinition` | `Cha_CatMage` |
| `ElfGuardian_CharacterDefinition` | `Cha_ElfGuardian` |
| `RabbitHealer_CharacterDefinition` | `Cha_RabbitHealer` |
| `ElfArcher_CharacterDefinition` | `Cha_ElfArcher` |

3. Default Character에 `Barbarian_CharacterDefinition`을 넣는다(기존 `defaultCharacterIndex: 1`과 동일).

`btn_switching`의 `RuntimeCharacterSwitcher`에 남아 있던 Characters / Default Character Index 필드는
스크립트에서 사라졌으므로 Inspector에서도 보이지 않는다(씬에 남은 직렬화 값은 다음 저장 때 정리된다).

### 11-3. `list_Character` 프리팹

1. **`Progress` 오브젝트의 `PlayerProgressDisplay`를 제거하고 `ProgressBarView`를 추가한다.**
   (제거하지 않으면 리스트의 모든 행동력 막대가 경험치 값으로 덮인다. 남아 있으면 실행 시 오류 로그가 뜬다.)
2. 루트 `list_Character`에 `CharacterSwapListItem`을 추가한다.
   - References는 전부 비워 둬도 된다(`sp_portrait` / `lb_name` / `lb_level` / `lb_percent` 이름으로 찾는다).
   - Localization 3칸(State Ready / In Use / Exhausted)에 `01 UI`의 Key 5 / 6 / 7을 지정한다.
3. 행동력 상태 문구를 표시할 TMP 오브젝트 `lb_state`를 `sp_stamina` 아래에 추가한다.
   - 이름을 `lb_state`로 두면 자동 연결된다. 없으면 상태가 색으로만 구분되고 경고 로그가 뜬다.
4. 씬의 `list` 아래에 있는 `list_Character` 인스턴스는 **비활성 그대로** 둔다(복제 원본으로 쓴다).

### 11-4. 패널과 버튼

1. `pn_CharacterSwap`에 `CharacterSwapPanel`을 추가한다. References는 비워 둬도 된다
   (`btn_close` / `btn_swap` / `list` / list 아래의 `CharacterSwapListItem`을 이름으로 찾는다).
2. `btn_character > btn_change`에 `CharacterSwapPanelOpener`를 추가하고 Panel에 `pn_CharacterSwap`을 연결한다.
3. `pn_CharacterSwap`은 비활성 상태로 저장한다.

## 12. 검증 상태

### 이 환경에서 확인한 것

- `Assets/Scripts` 전체를 Unity와 같은 정의(`UNITY_STANDALONE_WIN` 포함)로 Roslyn 컴파일 → **오류 0건**.
- 씬/프리팹 YAML을 읽어 계층·컴포넌트·앵커를 확인하고, 그 구조를 전제로 자동 탐색 이름을 맞췄다.

### 이 환경에서 확인할 수 없는 것

- **Play Mode 동작 전체**(에디터가 락을 잡고 있어 실행하지 못했다).
- **클릭 관통 예외**(`SetModalClickableRect`)는 Win32 `WS_EX_TRANSPARENT` 경로라 **Windows 빌드에서만**
  실제로 검증된다. macOS 에디터에서는 이 코드가 아예 실행되지 않는다.
  이 등록이 없으면 Windows 빌드에서 패널이 보이기만 하고 버튼이 눌리지 않는다 - 기존 창은
  ControlDock 영역 밖이 전부 클릭 관통이기 때문이다. **Windows 빌드에서 반드시 확인해야 한다.**

### 행동력 0 상태 검증 방법

행동력 소비 규칙이 아직 없으므로 실제로 0이 되는 경로가 없다. 표시/차단 동작을 확인하려면 둘 중 하나:

- 임시 스크립트나 콘솔에서 `CharacterRoster.Instance.SetStamina(definition, 0)` 호출
- `%USERPROFILE%\AppData\LocalLow\<회사>\<제품>\playerprogress.json`의 해당 `currentStamina`를 0으로 수정 후 재실행

## 13. 이번 작업에서 구현하지 않은 것

지시서의 제외 항목(최종 연출, 상세 정보창, 장비/스탯, 행동력 회복, 자동 교체, 보상 정산, 건물/상점,
파티 구성, 다중 캐릭터 전투)에 더해, 판단에 따라 다음도 제외했다.

- **행동력 소비 규칙**: 언제 얼마나 줄지는 전투 설계 사항이라 API만 열어 두었다.
- **경험치 HUD의 `ProgressBarView` 전환**: 레벨업 연출 큐 회귀 위험 때문에 별도 작업으로 미뤘다.
- **현재 캐릭터 저장**: 기존 동작(재시작 시 항상 기본 캐릭터)을 유지했다. 지시서에 요구가 없었다.
- **캐릭터 이름 로컬라이징**: 표시 이름은 정의 에셋의 평문 문자열이다. 이름 확정 후 전환하는 편이 낫다.
- **패널 열림 중 키보드 공격 입력 차단**: 1장 참고.
