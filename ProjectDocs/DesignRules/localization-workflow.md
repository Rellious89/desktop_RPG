# 텍스트 로컬라이징 작업 가이드

이 문서는 KeyBuddy의 텍스트를 추가·수정하고 Unity에 반영하는 전체 작업 절차를 정의한다. 시간이 지난 뒤 다시 작업하거나 다른 환경에서 이어서 작업하더라도 이 문서 하나로 데이터의 원천, CSV 갱신 방법, Key/ID 규칙과 런타임 적용 방식을 확인할 수 있어야 한다.

## 1. 확정된 기본 구조

KeyBuddy는 **Unity Localization 패키지**(`com.unity.localization 1.5.12`)를 사용한다. 자체 JSON 로더, 자체 CSV 런타임 로더, 자체 언어 fallback 시스템은 만들지 않는다.

Google Sheets API와 OAuth는 사용하지 않는다. 번역 작업은 Google Spreadsheet에서 하고, 작업자가 필요한 시점에 현재 탭을 CSV로 내려받아 Unity에 수동으로 반영한다.

```text
Google Spreadsheet        사람이 문구를 작성·검토하는 원본
        │
        │ 현재 탭을 CSV로 다운로드
        ▼
TableData/Localization/   Git에 저장하는 교환 파일·스냅샷
        │
        │ Unity에서 CSV(Merge) 수동 Import
        ▼
String Table 에셋         프로젝트와 빌드가 실제로 사용하는 데이터
        │
        ▼
Unity Localization        언어 선택 / 영어 fallback / {0} 포맷팅
```

- 게임 실행 중에는 Google Sheet나 CSV를 읽지 않는다.
- CSV 파일이 바뀌었다고 자동 Import하지 않는다. 작업자가 내용을 확인하고 명시적으로 갱신한다.
- 다른 작업 환경은 Git에 저장된 String Table 에셋을 먼저 받은 뒤 같은 CSV를 Import한다.

## 2. 데이터 위치

### CSV 교환 파일

```text
TableData/Localization/
├─ 01_UI.csv
├─ 02_Battle.csv       # 실제 카테고리를 만들 때 추가
└─ ...
```

CSV는 `Assets` 밖에 있으므로 빌드 리소스가 아니다. 카테고리마다 CSV를 하나 사용하며 파일명은 String Table Collection 이름과 맞춘다.

### Unity String Table

```text
Assets/Localization/Tables/01_UI/
├─ 01_UI Shared Data.asset
├─ 01_UI.asset
├─ 01_UI_en.asset
└─ 01_UI_ko-KR.asset
```

Unity가 실제로 사용하는 데이터는 이 에셋들이다. CSV만 커밋하고 String Table 에셋을 빠뜨리면 다른 환경과 빌드에는 변경 내용이 반영되지 않는다.

## 3. 카테고리 규칙

String Table Collection 하나가 카테고리 하나다. 이름은 `<번호>_<이름>` 형식을 사용한다.

```text
01_UI
02_Battle
03_Dungeon
04_Item
05_Mercenary
06_Narrative
```

- 앞쪽 숫자가 사용자가 보는 카테고리 코드다. `01_UI`는 Inspector에서 `01 UI`로 표시된다.
- 숫자 접두사가 없는 Collection은 커스텀 Inspector에서 `(Unmanaged)`로 표시하며 자동으로 바꾸지 않는다.
- 아직 사용하지 않는 카테고리는 빈 에셋으로 미리 만들지 않는다.
- Google Spreadsheet도 카테고리마다 같은 이름의 탭을 사용한다.

## 4. 숫자 Key와 Unity 내부 ID

각 카테고리의 Entry Key 이름은 `1`부터 시작하는 양의 정수 문자열을 사용한다.

```text
1, 2, 3, ...
```

사람이 텍스트를 지정할 때는 `(카테고리, Key)` 조합을 사용한다.

```text
(01 UI, 1) = 01_UI Collection의 Key 1
```

숫자 Key와 Unity 내부 ID는 서로 다른 값이다.

| 구분 | 예시 | 역할 |
| --- | --- | --- |
| 숫자 Key | `"1"` | 사람이 Sheet와 Inspector에서 사용하는 이름 |
| Unity 내부 Key ID | `84858101760` | 씬·프리팹 직렬화와 런타임 참조에 사용하는 고유값 |

실제 컴포넌트에는 **Table GUID + Unity 내부 Key ID**가 저장된다. 카테고리 번호와 숫자 Key를 별도로 중복 저장하지 않으며, Editor Property Drawer가 실제 참조를 역산해 사람이 읽을 수 있게 보여 준다.

### ID를 비워 둔 CSV의 동작

Google Sheet에서 신규 행의 `Id`를 비워 두거나 `0`으로 두어도 된다. CSV Import 시 Unity는 다음 순서로 처리한다.

1. 같은 숫자 Key가 이미 있으면 기존 Entry와 기존 ID를 사용한다.
2. 같은 숫자 Key가 없으면 새 Entry를 만들고 새 ID를 생성한다.

따라서 현재의 단방향 작업 흐름에서는 Unity가 생성한 ID를 Google Sheet에 다시 올리지 않아도 된다. 같은 Key 이름으로 반복 Import하면 기존 ID가 유지된다.

단, 이 정책은 **기존 숫자 Key를 변경하지 않는 것**을 전제로 한다.

- 기존 Key를 재번호화하지 않는다.
- 삭제한 Key 번호를 다른 문구에 재사용하지 않는다.
- Entry를 지웠다가 같은 번호로 다시 만들지 않는다.
- 새로운 문구는 마지막 번호 뒤에 추가한다.

ID가 빈 상태에서 Sheet의 Key `2`를 `7`로 바꾸면 Unity는 이를 이름 변경으로 알 수 없고 새로운 Key `7`을 만든다. 기존 컴포넌트는 이전 ID를 계속 참조한다. Key 변경이 정말 필요하면 참조처를 먼저 감사하고 Unity에서 명시적으로 Rename하거나, ID를 포함한 CSV로 별도 마이그레이션한다.

## 5. Google Sheet와 CSV 형식

Spreadsheet 이름은 `KeyBuddy_Localization`을 사용하고 카테고리마다 탭을 나눈다.

Unity 기본 CSV 헤더를 그대로 유지한다.

```csv
Key,Id,English(en),Korean (South Korea)(ko-KR)
1,84858101760,Kill Count : {0},{0} 처치
2,,Combo,콤보
```

규칙:

- `Key`에는 해당 카테고리 안에서 중복되지 않는 양의 정수만 입력한다.
- `Id`는 기존 값이 있다면 변경하지 않는다. 신규 행은 공란 또는 `0`이어도 된다.
- Locale 헤더 이름은 Unity가 생성한 값을 그대로 사용한다.
- 번역이 없는 Locale 셀은 비워 둔다. 영어 문구를 복사해 채우지 않는다.
- 쉼표, 줄바꿈, 큰따옴표가 들어간 문구는 Google Sheets의 CSV 내보내기 결과를 그대로 사용한다.
- `{0}`, `{1}` 같은 인자의 수와 의미는 모든 Locale에서 일치해야 한다.

## 6. 최초 카테고리 연결 절차

새 카테고리를 처음 만들 때만 다음 과정을 수행한다.

1. `Window > Asset Management > Localization Tables`에서 `<번호>_<이름>` String Table Collection을 만든다.
2. 프로젝트에서 지원하는 Locale 테이블(en, ko-KR)을 함께 만든다.
3. Unity의 Collection 메뉴에서 CSV로 한 번 Export한다.
4. 생성된 CSV를 `TableData/Localization/<Collection>.csv`에 저장한다.
5. CSV를 Google Spreadsheet의 같은 이름 탭으로 가져온다.
6. 이후에는 Google Sheet를 문구 저작 원본으로 사용한다.

최초 Export는 정확한 헤더와 기존 Key ID를 확보하기 위한 작업이다. Google API 연결이나 자동 동기화 설정은 하지 않는다.

## 7. 평소 텍스트 갱신 절차

1. Google Spreadsheet에서 해당 카테고리 탭을 수정한다.
2. `파일 > 다운로드 > 쉼표로 구분된 값(.csv, 현재 시트)`으로 현재 탭을 받는다.
3. 내려받은 파일을 카테고리 이름으로 바꿔 기존 파일을 교체한다.

```text
다운로드 파일: KeyBuddy_Localization - 01_UI.csv
교체 대상:     TableData/Localization/01_UI.csv
```

4. Unity에서 대상 String Table Collection을 연다.
5. Collection 메뉴의 `Import > CSV(Merge)...`를 선택한다.
6. 해당 CSV를 지정한다.
7. Localization Inspector의 미리보기와 `Search Text...`에서 변경 내용이 보이는지 확인한다.
8. 동적 문구와 fallback은 Play Mode에서 한 번 확인한다.
9. CSV와 변경된 String Table 에셋을 함께 커밋한다.

### 왜 CSV(Merge)를 사용하는가

`CSV(Merge)`는 CSV에 있는 Entry만 추가·갱신하고, CSV에 없는 기존 Entry는 보존한다. 일반 `CSV` Import는 CSV에서 빠진 Entry를 삭제할 수 있으므로 테이블 전체 삭제를 의도한 특별한 상황이 아니면 사용하지 않는다.

Sheet에서 행을 지워도 Merge Import만으로 Unity Entry가 자동 삭제되지는 않는다. 삭제는 참조처를 확인한 뒤 Unity에서 별도 정리한다.

## 8. Git과 다른 작업 환경

텍스트를 갱신한 커밋에는 최소한 다음 파일이 함께 들어가야 한다.

```text
TableData/Localization/<Category>.csv
Assets/Localization/Tables/<Category>/<Category> Shared Data.asset
Assets/Localization/Tables/<Category>/<Category>_<Locale>.asset
관련 .meta 파일(새 파일인 경우)
```

다른 환경에서 작업할 때는:

1. 저장소의 기존 String Table과 `.meta`를 먼저 받는다.
2. 테이블을 삭제하거나 CSV만으로 새로 생성하지 않는다.
3. 최신 CSV를 `CSV(Merge)`로 Import한다.
4. 충돌이 생기면 Key ID와 GUID를 임의로 새로 만들지 않고 기존 저장소 값을 기준으로 확인한다.

Unity 내부 ID의 영속성은 Google Sheet가 아니라 Git에 저장된 `Shared Data.asset`이 보장한다.

## 9. English fallback 정책

영어는 프로젝트의 확정된 기본 언어다. 선택한 Locale의 번역 셀이 비어 있으면 영어를 출력한다.

다음 설정은 항상 유지한다.

1. `Assets/Localization Settings.asset` → **String Database > Use Fallback = ON**
2. Korean(ko-KR) Locale 에셋 → **Metadata > Fallback Locale = English (en)**

Locale을 새로 추가할 때도 해당 Locale의 Metadata에 `Fallback Locale = English (en)`을 지정한다.

검증 결과(2026-07-29): ko-KR 선택 상태에서 `01_UI / Key 1`의 한국어 값을 비우고 테이블을 다시 로드하면 `Kill Count : 3`이 영어로 출력된다.

| 상황 | 기대 동작 |
| --- | --- |
| Entry는 있고 현재 Locale 값만 비어 있음 | 영어 fallback 출력 |
| 영어 값까지 비어 있음 | Missing Translation 메시지 출력 |
| Table/Key 참조 자체가 없음 | Inspector 경고 + 런타임 Error 로그 |

설정 오류를 하드코딩 영어로 조용히 대체하지 않는다. 정상 화면처럼 보이게 숨기면 누락을 발견하기 어렵다.

## 10. 런타임 적용 방법

### 정적 TMP 텍스트

다른 스크립트가 내용을 덮어쓰지 않는 정적 TMP에는 `LocalizedTMPText`를 사용한다.

1. TMP 오브젝트에 `LocalizedTMPText`를 추가한다.
2. Target TMP를 연결한다.
3. Text 필드에서 Category와 Key를 지정한다.

같은 TMP에 Unity 기본 `Localize String Event`를 함께 붙이지 않는다.

### 동적 텍스트

숫자나 이름이 계속 바뀌는 문구는 전용 Presenter/Display가 `LocalizedTextReference`를 직접 소유한다. `SessionKillCounterDisplay.cs`가 기준 예시다.

```csharp
[SerializeField] private LocalizedTextReference textFormat;
private readonly object[] formatArguments = new object[1];

private void OnEnable()
{
    formatArguments[0] = currentValue;
    textFormat.Arguments = formatArguments;
    textFormat.StringChanged += ApplyLocalizedText;
}

private void Refresh()
{
    formatArguments[0] = currentValue;
    textFormat.Arguments = formatArguments;
    textFormat.RefreshString();
}

private void OnDisable()
{
    textFormat.StringChanged -= ApplyLocalizedText;
}
```

규칙:

- `Arguments`를 설정한 뒤 `StringChanged`를 구독한다. 구독 시 최초 로드가 발생할 수 있다.
- 값이 바뀌면 Arguments를 갱신하고 `RefreshString()`을 호출한다.
- `OnEnable` 구독과 `OnDisable` 해지를 짝지어 중복 구독을 막는다.
- 인자 배열은 재사용한다.
- 이미 다른 Presenter가 계속 쓰는 TMP에 `LocalizedTMPText`를 추가하지 않는다. 두 작성자가 서로의 문구를 덮어쓴다.
- 아직 기획이 확정되지 않은 임시 UI는 성급하게 전부 로컬라이징하지 않는다. 존치가 확정된 기능부터 전환한다.

## 11. 커스텀 Inspector 사용법

`LocalizedTextReference` 필드는 Category/Key와 각 Locale의 미리보기를 제공한다.

```text
Text                                  [Search Text...]
  Category   [01 UI             ▼]
  Key        [1                  ]
  English    Kill Count : {0}
  Korean     {0} 처치
```

- 유효한 Key를 입력하면 실제 Table GUID + Entry Key ID가 즉시 갱신된다.
- 유효하지 않은 Key를 입력하면 입력 숫자는 남지만 실제 Entry 참조는 비워진다.
- 유효하지 않은 상태에서는 Locale 미리보기가 공란이고 오류 HelpBox가 표시된다.
- 잘못된 Key 상태에서 검색 결과를 선택하면 Pending 입력을 제거하고 선택한 참조와 미리보기를 즉시 적용한다.
- `Search Text...`는 카테고리, 숫자 Key, 모든 Locale 문구를 부분 일치·대소문자 무시로 검색한다.
- 검색 대상은 현재 Unity에 Import된 String Table이다. Google Sheet나 CSV만 바꾼 상태에서는 검색 결과가 갱신되지 않는다.

관련 파일:

| 파일 | 역할 |
| --- | --- |
| `Assets/Scripts/Common/Localization/LocalizedTextReference.cs` | Unity `LocalizedString` 기반 런타임 참조 |
| `Assets/Scripts/Common/Localization/LocalizedTMPText.cs` | 정적 TMP용 컴포넌트 |
| `Assets/Editor/Localization/LocalizationCategoryCatalog.cs` | 카테고리와 번역 검색용 Editor 캐시 |
| `Assets/Editor/Localization/LocalizedTextReferenceDrawer.cs` | Category/Key 입력과 Locale 미리보기 |
| `Assets/Editor/Localization/LocalizedTextReferenceProperty.cs` | GUID/Key ID 읽기·쓰기 |
| `Assets/Editor/Localization/LocalizedTextPendingKeys.cs` | 유효하지 않은 임시 Key 입력 상태 공유 |
| `Assets/Editor/Localization/LocalizedTextSearchWindow.cs` | 텍스트 검색창 |

Editor 코드는 모두 `Assets/Editor/` 아래에 두어 플레이어 빌드에 포함되지 않게 한다.

## 12. 신규 카테고리 추가

1. 다음 예약 번호로 String Table Collection을 만든다.
2. `Assets/Localization/Tables/<Category>/`에 저장한다.
3. 모든 현재 Locale 테이블을 추가한다.
4. Unity에서 CSV를 최초 Export하여 `TableData/Localization/<Category>.csv`를 만든다.
5. Google Spreadsheet에 같은 이름의 탭을 만들고 CSV를 가져온다.
6. 이후 평소 갱신 절차대로 Sheet → CSV → `CSV(Merge)`를 사용한다.

## 13. 신규 Locale 추가

1. Locale 에셋을 만든다.
2. 모든 기존 Collection에 해당 Locale의 String Table을 추가한다.
3. 새 Locale Metadata에 `Fallback Locale = English (en)`을 지정한다.
4. Unity에서 CSV를 다시 Export해 새 Locale 헤더를 확인한다.
5. Google Spreadsheet의 모든 카테고리 탭에 같은 헤더의 열을 추가한다.
6. 이후 CSV를 내려받아 `CSV(Merge)`로 반영한다.
7. TMP Font Asset이 새 언어의 글리프를 지원하는지 별도로 확인한다.

문자열 테이블과 폰트 글리프는 별개다. 번역값이 정상이어도 TMP Font Asset에 문자가 없으면 사각형으로 표시된다.

## 14. 작업 완료 체크리스트

- [ ] Google Sheet의 Key 번호가 중복되거나 재사용되지 않았는가?
- [ ] 기존 Id 값을 임의로 수정하지 않았는가?
- [ ] 올바른 카테고리 CSV를 교체했는가?
- [ ] Unity에서 `CSV(Merge)`로 Import했는가?
- [ ] Inspector 미리보기와 검색 결과가 갱신됐는가?
- [ ] 동적 문구의 `{0}` 인자가 모든 Locale에서 일치하는가?
- [ ] 빈 번역 셀의 영어 fallback이 필요한 화면에서 동작하는가?
- [ ] CSV와 Shared Data/Locale Table 에셋을 함께 커밋했는가?
- [ ] 임시 UI를 불필요하게 로컬라이징 범위에 포함하지 않았는가?
