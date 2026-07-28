# 로컬라이징 작업 규칙

KeyBuddy의 다국어 텍스트는 **Unity Localization 패키지**(`com.unity.localization 1.5.12`)를 런타임 원천으로 사용한다. 자체 JSON/CSV 로더나 자체 fallback 시스템은 만들지 않는다.

번역 저작(문구를 쓰고 고치는 작업)의 원본은 **Google Spreadsheet**이며, Unity에서 Pull한 String Table 에셋만 프로젝트와 빌드에서 사용한다. 런타임에는 Google Sheet에 접속하지 않는다.

```
Google Spreadsheet (저작 원본)
        │  Pull (에디터에서 수동)
        ▼
String Table 에셋 (프로젝트/빌드가 실제로 쓰는 데이터)
        │
        ▼
Unity Localization 런타임 (언어 선택 / fallback / {0} 포맷팅)
```

## 구성 요소

| 파일 | 역할 |
| --- | --- |
| `Assets/Scripts/Common/Localization/LocalizedTextReference.cs` | 런타임 참조 타입. `LocalizedString`을 그대로 상속하므로 직렬화·런타임 동작이 동일하다 |
| `Assets/Scripts/Common/Localization/LocalizedTMPText.cs` | Arguments가 필요 없는 정적 TMP 텍스트용 최소 컴포넌트 |
| `Assets/Editor/Localization/LocalizationCategoryCatalog.cs` | 숫자 접두사 Collection을 카테고리로 해석해 캐시하는 Editor 인덱스 |
| `Assets/Editor/Localization/LocalizedTextReferenceProperty.cs` | 직렬화 필드(Table GUID / Entry Key ID) 읽기·쓰기 헬퍼 |
| `Assets/Editor/Localization/LocalizedTextReferenceDrawer.cs` | Category/Key 저작 + 번역 미리보기 Inspector |
| `Assets/Editor/Localization/LocalizedTextSearchWindow.cs` | 번호·문구 검색 드롭다운 |

Editor 코드는 전부 `Assets/Editor/` 아래에 두어 런타임 빌드에 `UnityEditor.Localization`이 포함되지 않게 한다.

## 카테고리 명명 규칙

String Table Collection 하나가 카테고리 하나다. 이름은 `<번호>_<이름>` 형식을 쓴다.

```
01_UI
02_Battle
03_Dungeon
04_Item
05_Mercenary
06_Narrative
```

- 카테고리 코드는 Collection 이름 **앞쪽 숫자**에서 판별한다. `01_UI` → 코드 1, Inspector 표시는 `01 UI`.
- 숫자 접두사가 없는 Collection(`UI.HUD` 같은 기존/외부 테이블)은 카테고리 목록에 나타나지 않는다. 그런 Collection을 참조하는 필드는 Inspector에 `(Unmanaged)`로 표시되고 경고가 뜨며, **참조가 조용히 바뀌지는 않는다.**
- `02_Battle` 이후는 **예약 코드**다. 실제로 필요할 때 만들고, 빈 테이블 에셋을 미리 만들어 두지 않는다.

에셋 경로는 카테고리마다 폴더를 나눈다.

```
Assets/Localization/Tables/01_UI/
├─ 01_UI Shared Data.asset
├─ 01_UI.asset            (Collection)
├─ 01_UI_en.asset
└─ 01_UI_ko-KR.asset
```

## 숫자 키 규칙

각 Collection의 Entry Key 이름은 **1부터 시작하는 양의 정수 문자열**을 쓴다.

```
1, 2, 3, ...
```

사용자가 인식하는 식별자는 `(카테고리 코드, 키 번호)` 조합이다. 예: `(1, 3)` = `01_UI` Collection의 Key `3`.

여기서 헷갈리기 쉬운 두 가지를 구분해야 한다.

| | 값 | 누가 쓰나 |
| --- | --- | --- |
| 사용자 숫자 키 | Entry의 **이름** `"1"` | 사람이 Inspector/Sheet에서 본다 |
| Unity 내부 Key ID | Shared Table Data가 관리하는 long (예: `84858101760`) | 씬/프리팹 직렬화와 런타임 조회가 쓴다 |

**직렬화되는 것은 Table GUID + Entry Key ID뿐이다.** 카테고리 번호와 숫자 키는 어디에도 중복 저장하지 않고, Property Drawer가 GUID/Key ID로부터 역으로 계산해 보여 준다. 덕분에 Collection 이름을 바꿔도(예: `01_UI` → `01_Interface`) 기존 참조가 끊기지 않는다.

**한 번 만든 Entry의 내부 Key ID는 바꾸지 않는다.** Entry를 지웠다가 같은 이름으로 다시 만들면 Key ID가 달라져 기존 참조가 전부 끊긴다. 키를 정리할 때는 삭제 대신 이름 변경(Rename)을 쓴다.

## Google Sheet 구조

```
Spreadsheet: KeyBuddy_Localization

Tabs: 01_UI / 02_Battle / 03_Dungeon / 04_Item / 05_Mercenary / 06_Narrative
```

각 탭의 기본 컬럼:

```
Key | en               | ko
1   | Kill Count : {0} | {0} 처치
2   | OK               |
3   | Cancel           | 취소
```

- `Key` 열에는 숫자만 넣는다.
- 번역이 아직 없는 칸은 비워 둔다. 임시 영어 문구를 ko 열에 복사해 넣지 않는다 — fallback이 처리할 일을 사람이 대신하면 나중에 미번역 칸을 찾을 수 없다.

### Google Sheets 연결 절차 (아직 연결되지 않음)

현재 프로젝트에는 Spreadsheet ID / Sheet ID / 인증 정보가 없다. 연결은 다음 순서로 한다.

1. Google Cloud에서 서비스 계정 또는 OAuth 클라이언트를 만들고 자격증명 JSON을 받는다.
2. Unity에서 `Assets > Create > Localization > Google Sheets Service` 로 Service Asset을 만들고, 자격증명을 지정한다.
3. `Window > Asset Management > Localization Tables` 에서 `01_UI` Collection을 선택한다.
4. Inspector 하단 `Extensions` 에 **Google Sheets Extension**을 추가하고 다음을 채운다.
   - `Sheets Service Provider` : 2에서 만든 Service Asset
   - `Spreadsheet Id` : `KeyBuddy_Localization` 문서 ID
   - `Sheet Id` : 해당 탭(`01_UI`)의 gid
   - `Columns` : `Key Column`(Key) + Locale별 `Locale Column`(en, ko)
   - `Remove Missing Pulled Keys` : **초기에는 끈다.** 켜면 Sheet에 없는 키가 Unity에서 삭제되어 Key ID가 사라진다.
5. 카테고리를 추가할 때마다 4를 반복한다. Spreadsheet는 하나를 공유하고 Sheet ID만 탭마다 다르다.

자격증명은 저장소에 커밋하지 않는다.

### Pull / Push 정책

- **Pull**이 기본이다. Sheet에서 키·번역을 추가하고 Unity에서 Pull한다.
- **Push**는 Sheet를 처음 만들 때나, 명시적으로 Unity → Sheet 역동기화가 필요할 때만 쓴다. 습관적으로 Push하면 Sheet 쪽 편집이 덮어써진다.
- 빌드 시 자동 Pull은 하지 않는다. Pull은 사람이 판단해서 실행하고, 결과 에셋을 커밋한다.
- **Google Sheet를 고쳤어도 Unity에서 Pull하기 전에는 Inspector 검색에 나오지 않는다.** 검색 대상은 프로젝트에 있는 String Table 에셋이지 Sheet가 아니다.

## English fallback 정책

**영어는 이 프로젝트의 확정된 기본 언어다.** 번역 값이 비어 있으면 영어로 대체된다. 이를 위해 다음 두 설정이 켜져 있으며, **끄지 않는다.**

1. `Assets/Localization Settings.asset` → **String Database → Use Fallback = ON**
2. Korean(ko-KR) Locale 에셋 → **Metadata → Fallback Locale = English (en)**

2가 함께 필요한 이유: FallbackLocale 메타데이터가 없으면 Unity는 CultureInfo 부모(`ko-KR` → `ko` → invariant)로 fallback을 찾는데, 프로젝트에 `ko` Locale이 없으므로 영어까지 도달하지 못한다. 1만 켜서는 동작하지 않는다.

Locale을 새로 추가할 때도 **반드시 해당 Locale의 Metadata에 Fallback Locale = English (en) 을 지정한다.** 지정하지 않으면 그 언어만 조용히 미번역 메시지가 나온다.

> 검증 결과(2026-07-29, Play Mode 실측): ko-KR 선택 상태에서 `01_UI / Key 1` 의 한국어 값을 비우면 `Kill Count : 3` (영어)이 출력된다. 설정 적용 전에는 같은 조건에서 `No translation found for '1' in 01_UI` 가 출력됐다.

한 가지 동작 특성: fallback 판정은 **테이블 Entry를 로드하는 시점**에 일어난다. 이미 로드된 Entry의 값을 실행 중에 코드로 비우면 fallback이 다시 계산되지 않고 미번역 메시지가 나온다. Sheet에서 빈 칸을 Pull해 온 실제 상황(에셋에 빈 값이 저장된 상태로 로드)에서는 정상적으로 영어가 나오므로 실사용에는 영향이 없다.

`Missing Translation State` 는 `Show Missing Translation Message` 로 둔다. fallback까지 실패한 경우(영어 값 자체가 없는 키)에만 화면에 `No translation found for '<key>' in <table>` 이 보이며, 이는 키 누락을 조용히 넘기지 않기 위한 의도된 동작이다.

번역값 누락과 참조 누락은 구분한다.

| 상황 | 기대 동작 |
| --- | --- |
| Entry는 있고 ko 값만 비어 있음 | Unity Localization fallback → 영어 출력 |
| Table/Key 참조 자체가 비었거나 잘못됨 | Inspector 경고 + 런타임 Error 로그 + 개발용 Missing 표시 |

**잘못된 키를 하드코딩 영어 문자열로 조용히 대체하지 않는다.** 그렇게 하면 설정 누락이 정상 화면처럼 보여 끝까지 발견되지 않는다.

## 적용 방법

### 정적 텍스트 (Arguments 없음)

`LocalizedTMPText` 컴포넌트를 TMP 오브젝트에 붙이고 Category/Key만 지정한다.

- 같은 오브젝트에 Unity 기본 `Localize String Event`를 **함께 붙이지 않는다.** 두 컴포넌트가 같은 TMP 텍스트를 번갈아 덮어써서 어느 쪽이 이겼는지에 따라 결과가 달라진다. 붙어 있으면 `LocalizedTMPText`가 Editor 경고를 띄운다.

### 동적 텍스트 (`{0}` Arguments 필요)

전용 컴포넌트에서 `LocalizedTextReference`를 직접 다룬다. `Assets/Scripts/Common/SessionKillCounterDisplay.cs` 가 기준 예시다.

```csharp
[SerializeField] private LocalizedTextReference killCountFormat;
private readonly object[] formatArguments = new object[1];

private void OnEnable()
{
    if (killCountFormat != null && killCountFormat.HasReference)
    {
        // 구독 즉시 최초 로드가 일어나므로 Arguments를 먼저 채운다.
        formatArguments[0] = SessionKillCounter.SessionKillCount;
        killCountFormat.Arguments = formatArguments;
        killCountFormat.StringChanged += ApplyLocalizedText;
    }
}

private void OnDisable()
{
    killCountFormat.StringChanged -= ApplyLocalizedText;   // 중복 구독 방지
}

private void Refresh()
{
    formatArguments[0] = SessionKillCounter.SessionKillCount;
    killCountFormat.Arguments = formatArguments;
    killCountFormat.RefreshString();
}
```

규칙:

- `StringChanged` 구독은 `OnEnable`, 해지는 `OnDisable`에서 짝을 맞춘다. Locale 변경 시 자동 갱신은 이 구독이 담당한다.
- 구독보다 `Arguments` 설정이 먼저다. 구독하는 순간 최초 로드가 일어나므로, Arguments가 비어 있으면 `{0}`이 포맷 오류를 낸다.
- 값이 바뀌면 `Arguments`를 갱신하고 `RefreshString()`을 호출한다.
- 배열은 재사용한다(`new object[1]`을 매 프레임 만들지 않는다).

## Inspector 사용법

`LocalizedTextReference` 필드는 다음처럼 보인다.

```
Kill Count Format                    [Search Text...]
  Category   [01 UI            ▼]
  Key        [1                 ]
  English    Kill Count : {0}
  Korean     {0} 처치
```

1. **Category** 드롭다운에서 카테고리를 고른다. 숫자 접두사가 있는 Collection만 코드 오름차순으로 나온다.
2. **Key**에 숫자를 입력한다. 해당 Entry가 있으면 참조가 즉시 Table GUID + Entry Key ID로 갱신된다.
3. **English / Korean** 은 읽기 전용 미리보기다. Locale이 늘어나면 행이 자동으로 늘어난다.
4. 존재하지 않는 Key, 0/음수 Key, Category 미선택은 **참조를 바꾸지 않고** 빨간 경고만 띄운다. 입력값은 남아 있으므로 그대로 고치면 된다.

### 검색

`Search Text...` 버튼을 누르면 검색창이 뜬다.

- 검색 대상: 카테고리 번호, 숫자 키, 그리고 **모든 Locale의 번역 문구**
- 부분 일치, 대소문자 무시. `Kill Count`, `kill count`, `처치`, `1` 모두 같은 Entry를 찾는다.
- 결과는 `Category | Key | English | Korean` 로 나온다. 같은 문구가 여러 키에 있으면 자동 선택하지 않고 후보를 전부 보여 준다.
- 행을 클릭하면 그 Entry의 Table GUID / Entry Key ID가 필드에 반영된다.
- `Refresh` 버튼은 캐시를 강제로 다시 만든다. 보통은 테이블을 고치거나 Pull하면 자동으로 갱신된다.

## 신규 카테고리 추가 절차

1. Google Sheet에 새 탭을 만든다. 이름은 `02_Battle` 형식.
2. `Window > Asset Management > Localization Tables > New Table Collection` 으로 같은 이름의 String Table Collection을 만든다. 저장 위치는 `Assets/Localization/Tables/02_Battle/`.
3. 만들 Locale은 프로젝트에 있는 전부(en, ko-KR)를 선택한다.
4. Collection에 Google Sheets Extension을 추가하고 해당 탭의 Sheet ID를 지정한다(위 "연결 절차" 4번).
5. Pull한다. Inspector Category 드롭다운에 `02 Battle`이 자동으로 나타난다.

## 신규 Locale 추가 절차

1. `Window > Asset Management > Localization Tables > Locale Generator` 로 Locale 에셋을 만든다.
2. 기존 Collection들에 해당 Locale의 String Table을 추가한다.
3. Google Sheet의 각 탭에 열을 추가하고, Google Sheets Extension의 `Columns`에 새 Locale Column을 등록한다.
4. **새 Locale의 Metadata에 Fallback Locale = English (en) 을 반드시 지정한다.** 빠뜨리면 그 언어만 미번역 시 영어로 대체되지 않는다.
5. Pull한다. Inspector 미리보기와 검색 열이 자동으로 늘어난다.

**TMP 폰트 글리프 지원은 문자열 로컬라이징과 별개 문제다.** 테이블에 일본어 문구를 넣어도 해당 TMP Font Asset에 글리프가 없으면 □로 나온다. 새 언어를 추가할 때는 Font Asset의 문자 세트를 따로 확보해야 한다(`StringTableCollection.GenerateCharacterSet()`으로 필요한 문자 목록을 뽑을 수 있다).
