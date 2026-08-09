# Save Version & Migration Foundation 완료 보고서

작성일: 2026-08-09  
대상 프로젝트: `desktop_RPG`  
현재 저장 스키마: v1

## 1. 완료 결과

기존 `Application.persistentDataPath/playerprogress.json` 경로를 유지하면서, 버전 없는 기존 문서를 v0으로 판정해 메모리에서 v1으로 순차 마이그레이션하는 기반을 구현했다. 마이그레이션된 문서는 로드 직후 덮어쓰지 않고 다음 명시적 `Save()`에서만 안전하게 기록한다.

미래 버전과 마이그레이션 실패는 원본을 그대로 두고 저장을 차단한다. 손상 파일은 격리에 성공해야만 새 진행 저장을 허용하며, 격리에 실패하면 원본을 보존하기 위해 저장을 차단한다. 저장 실패 시 디스크의 기존 정상본과 메모리의 revision/timestamp를 모두 이전 상태로 유지한다.

Steamworks SDK, SteamID, Steam Cloud API와 실제 Auto-Cloud 설정은 추가하지 않았다. 저장소와 경로 결정 책임만 분리해 후속 Steam 프로필 공급자를 연결할 수 있는 경계를 마련했다.

## 2. 저장 포맷 v0 → v1

v0은 최상위 `saveVersion` 필드가 없는 기존 JSON 문서다. 빈 객체 `{}`도 지원 가능한 v0 문서로 처리한다. 전체 `SaveData` 역직렬화 전에 `SaveVersionProbe`가 원문을 엄격한 JSON 문법으로 검사하고 버전을 판정하므로, 클래스 필드 초기값으로 버전을 추측하지 않는다.

v1은 기존 진행 필드에 아래 메타데이터를 추가한다.

```text
saveVersion: int
saveRevision: long
lastSavedAtUtc: string
```

- `saveVersion`: 현재 값 1
- `saveRevision`: 성공한 논리 저장마다 1 증가
- `lastSavedAtUtc`: UTC ISO-8601 왕복 형식(`o`, InvariantCulture)
- v0 → v1: 레벨, 경험치, 누적 킬, 재화, 아이템, 캐릭터 레벨·행동력, 회복 슬롯 값을 변경하지 않음
- v0에 없던 revision과 저장 시각: 각각 0과 빈 값으로 시작

마이그레이션 단계는 `FromVersion → FromVersion + 1`만 허용한다. 누락된 단계, 중복 단계, 한 버전을 건너뛰는 단계는 실패한다. 변환은 깊은 작업 사본에서 수행하고 모든 단계가 성공한 뒤에만 호출자 데이터에 반영하므로, 중간 실패가 런타임 데이터로 누출되지 않는다. 후속 v1 → v2는 단일 단계 구현과 기본 단계 표 등록으로 추가할 수 있다.

## 3. 로드 파이프라인과 상태

로드 순서는 다음으로 고정했다.

```text
저장소에서 원문 읽기
→ 버전 probe
→ 미래 버전 차단
→ SaveData 역직렬화
→ 순차 마이그레이션
→ SaveDataNormalizer 정규화
→ 런타임 Data 공개
```

`SaveSystem.LoadStatus`에서 아래 여섯 상태를 구분한다.

| 상태 | 의미 | LoadedFromFile | Save |
| --- | --- | --- | --- |
| `NewGame` | 파일 없음 | false | 허용 |
| `Loaded` | 현재 v1 정상 로드 | true | 허용 |
| `Migrated` | v0을 메모리에서 v1로 변환 | true | 허용, 즉시 자동 저장 없음 |
| `CorruptFallback` | 손상/읽기 실패 후 기본값 진행 | true | 격리 성공 시 허용, 실패 시 차단 |
| `FutureVersionBlocked` | 지원 버전보다 새로운 문서 | true | 차단 |
| `MigrationFailed` | 단계 누락 또는 변환 예외 | true | 차단 |

차단 상태에서도 기존 게임 호출부의 null 참조를 막기 위해 정규화된 메모리 기본 문서를 제공하지만, 상태와 저장 차단은 유지한다. 미래 버전에서는 역직렬화, 격리, 백업 갱신, 쓰기를 수행하지 않는다.

정규화는 `characters`, `items`, `recoverySlots`의 null 목록과 내부 null을 처리한다. 캐릭터·아이템의 null 항목은 제거하고, 인덱스가 슬롯 번호인 회복소는 null 항목을 빈 슬롯으로 교체한 뒤 최소 3개를 보장한다.

## 4. 프로필·저장소 경계

논리 프로필은 `local/primary`이고 실제 로컬 Primary 경로는 계속 아래와 같다.

```text
Application.persistentDataPath/playerprogress.json
```

`SaveSystem`은 `Application.persistentDataPath`나 파일 경로를 직접 조립하지 않는다. 기본 구현은 `LocalFileSaveStorage`를 지연 생성하고, 테스트는 `ISaveStorage`와 임시 루트 또는 메모리 저장소를 주입한다.

`SaveProfile`은 backend/slot/file name을, `SavePathProvider`는 주입된 루트에서 실제 경로를 결정한다. 후속 Steam 단계에서는 이 경계에 SteamID 기반 루트(예: `Profiles/{SteamID}/primary`) 또는 다른 저장소 구현을 연결할 수 있다. `UiSettingsSaveSystem`과 `WindowPlacementSaveSystem`은 수정하지 않았으며 기기별 설정은 진행 저장과 분리된 상태다.

현재 보조 파일 이름은 구현상 다음과 같다.

```text
playerprogress.json
playerprogress.json.bak
playerprogress.json.tmp
corrupted/playerprogress-{UTC timestamp}.json
```

보조 파일의 Steam Cloud 포함 여부와 Steam write batch는 후속 Steam 단계에서 결정한다.

## 5. 안전 저장·실패 복구 정책

정상 저장은 다음 순서로 수행한다.

```text
메타데이터 이전 값 캡처
→ revision + 1 및 UTC 저장 시각 반영
→ JSON 직렬화
→ 같은 디렉터리 tmp에 기록
→ 기존 primary를 backup으로 보내며 tmp를 primary로 교체
→ 성공 시 완료
```

- 기존 primary에 직접 `File.WriteAllText`하지 않는다.
- 첫 저장은 tmp를 primary로 이동한다.
- 기존 primary가 있으면 `File.Replace` 한 번으로 primary 교체와 최근 정상본 1개 백업을 수행한다.
- 백업을 포함한 교체에 실패하면 백업 없는 대체 경로를 시도하지 않고 실패한다.
- 쓰기 실패·예외·저장소 차단 시 revision, timestamp, version을 저장 시도 전 값으로 복원한다.
- 남은 tmp는 다음 `ReadPrimary()`에서 정리한다.
- 처음 마이그레이션된 v0의 명시적 저장에서는 기존 v0 primary가 backup으로 보존된다.
- 손상 primary는 격리 성공 후에만 새 primary 저장을 허용한다.
- 격리 실패 시 손상 원본을 덮어쓰지 않는다.
- 미래 버전과 마이그레이션 실패에서는 원본·백업·격리 파일을 변경하지 않는다.

## 6. 자동 검증 결과

실행 환경은 Unity 2022.3.62f3, macOS다. 사용 중인 실제 프로젝트와 사용자 저장 경로를 보호하기 위해 `Assets`, `Packages`, `ProjectSettings`, `Library`를 APFS 임시 클론으로 복사하고 클론의 `Library/ScriptAssemblies`만 재생성했다.

### 저장 기능 전용 EditMode

```text
CommonEditor.Tests
총 96 / 통과 96 / 실패 0 / 스킵 0
```

- `SaveMigrationTests`: 59/59
- `SaveStorageTests`: 19/19
- `SaveSystemIntegrationTests`: 18/18

Unity 실제 `JsonUtility`가 필요한 v0/v1 왕복, 상태 판정, v0 백업, 마이그레이션 실패와 재로드 항목까지 모두 통과했다.

### 전체 EditMode 회귀

```text
총 201 / 통과 201 / 실패 0 / 스킵 0
Unity 로그 error CS: 0
```

기존 인벤토리·보상 테스트를 포함한 전체 Editor 테스트가 통과했다. 테스트는 메모리 저장소 또는 시스템 임시 폴더만 사용하며 `Application.persistentDataPath/playerprogress.json`을 읽거나 쓰지 않는다.

## 7. 변경 파일

수정:

- `Assets/Scripts/Common/SaveData.cs`
- `Assets/Scripts/Common/SaveSystem.cs`

신규 런타임 코드:

- `Assets/Scripts/Common/SaveVersionProbe.cs`
- `Assets/Scripts/Common/SaveMigrationRunner.cs`
- `Assets/Scripts/Common/SaveDataNormalizer.cs`
- `Assets/Scripts/Common/SaveLoadResult.cs`
- `Assets/Scripts/Common/SaveStorage.cs`
- `Assets/Scripts/Common/SaveProfile.cs`
- 위 신규 스크립트의 `.meta`

신규 테스트:

- `Assets/Editor/Common/Tests/SaveMigrationTests.cs`
- `Assets/Editor/Common/Tests/SaveStorageTests.cs`
- `Assets/Editor/Common/Tests/SaveSystemIntegrationTests.cs`
- 위 테스트 및 신규 폴더의 `.meta`

완료 보고서:

- `ProjectDocs/WorkOrders/save-version-migration-foundation-report.md`

## 8. 범위·남은 위험

- 씬, 프리팹, 아트, CSV, Localization, Generated TableData 에셋을 수정하지 않았다.
- 사용자 작업 트리의 캐릭터 리소스, V2 테스트, Dungeon/Monster 데이터 변경을 수정하거나 되돌리지 않았다.
- `UiSettingsSaveSystem`, `WindowPlacementSaveSystem`을 수정하지 않았다.
- 커밋을 생성하지 않았다.
- Steam SDK, SteamID, Cloud API, Auto-Cloud 설정, 충돌 UI는 구현하지 않았다.
- 로컬 macOS Unity EditMode 검증은 완료했으며 Windows 배포 후보 빌드는 이번 단계 범위에 포함하지 않았다. Windows 후보 빌드 전에는 동일 테스트와 실제 Windows 파일 교체 동작을 최종 확인해야 한다.
- 저장 revision과 UTC 시각은 진단·향후 충돌 안내용이며 자동 Cloud 충돌 해결 기준으로 사용하지 않는다.

