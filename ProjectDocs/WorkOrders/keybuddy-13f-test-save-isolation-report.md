# KeyBuddy 13F — Unity 테스트 실제 저장 경로 완전 격리 보고

작성일: 2026-09-02

## 원인과 결과

13E 검증에서 일부 EditMode 시험이 `SaveSystem.Data`를 처음 접근할 때 제품 기본 저장소를 지연 생성했다.
그 저장소는 같은 application identifier의 `Application.persistentDataPath/playerprogress.json`을 가리켜,
정적 상태를 직접 넣지 않은 실행 경로가 실제 사용자 저장을 읽거나 쓸 수 있었다.

이번 변경은 제품의 런타임 기본 경로를 바꾸지 않는다. `SaveSystem`에는 시험 전용의 범위형 저장소
override를 추가했고, 모든 EditMode 시험보다 먼저 실행되는 전역 fixture가 고유 임시 디렉터리의
`LocalFileSaveStorage`를 바깥 경계로 설치한다. 따라서 기존 시험이 가짜 저장소를 해제하거나 예외가
발생해도, 다시 제품 저장소가 아니라 이 임시 저장소로만 돌아간다.

## 격리 설계

- `SaveSystem.PushStorageOverrideForTests`는 `IDisposable` 범위 토큰을 반환한다. override는 스택으로
  관리되어 중첩 범위는 LIFO 순서로 바깥 임시 저장소에 결정적으로 복귀한다.
- 바깥 범위를 먼저 해제하는 잘못된 사용은 `InvalidOperationException`으로 즉시 거부한다. 이중 Dispose는
  안전하게 무시한다.
- `ConfigureForTests(null, null, null)`은 기존 시험 호환을 유지하면서, 현재 범위의 임시 기본 저장소를
  지우지 않는다.
- `SaveSystemTestIsolationFixture`는 집중 실행 전 실제 주 파일과 `.bak`의 존재 여부, SHA-256, UTC 수정
  시각을 읽기 전용으로 캡처하고 실행 뒤 동일함을 assertion으로 검증한다. 파일이 없었던 경우에는 새로
  생기지 않았는지도 검증한다. fixture 자신의 임시 폴더는 종료 시 정리한다.
- `CharacterRosterCatalogTests`의 새 게임 초기 지급 경로는 메모리 저장소를 명시적으로 넣어, 초기 지급이
  파일 저장을 직접 호출하지 않음을 함께 확인한다.

## 변경 파일

- `Assets/Scripts/Common/SaveSystem.cs`
- `Assets/Editor/Common/Tests/SaveSystemTestIsolationFixture.cs`
- `Assets/Editor/Common/Tests/SaveSystemStorageIsolationTests.cs`
- `Assets/Editor/Character/Tests/CharacterRosterCatalogTests.cs`
- `Assets/Editor/Common/Tests/SaveSystemIntegrationTests.cs`

마지막 파일의 실제 파일 기록 버전 기대값만 현재 계약인 v8로 고쳤다. `SaveData`의 버전, 저장 포맷,
마이그레이션 구현은 변경하지 않았다.

## 회귀 검증

원본 Unity Editor는 실행하지 않았다. `/private/tmp/keybuddy-13f-unity.Lr8rsI`에 `Assets`, `Packages`,
`ProjectSettings`만 복사하고 새 `Library`를 생성한 별도 프로젝트에서 Unity 2022.3.62f3 batchmode로 실행했다.

- 집중 EditMode 회귀: 222/222 통과
  - `SaveResetServiceTests`: 35/35
  - `SaveResetWindowTests`: 3/3
  - `SaveMigrationTests`: 95/95
  - `RecruitmentUnlockServiceTests`: 13/13
  - `CharacterRosterCatalogTests`: 51/51
  - `SaveSystemIntegrationTests`: 21/21
  - `SaveSystemStorageIsolationTests`: 4/4
- 새 격리 시험은 제품 기본 경로 계약, scoped Save/Load/NewGame, 중첩/역순 해제 거부와 복귀, 예외 뒤
  범위 누출 없음을 확인한다.
- Unity C# 컴파일 오류: 0건. 기존 CS0414 경고만 관찰됐다.
- `git diff --check`: 통과.

## 실제 저장 전후 검증

대상은 `Application.persistentDataPath`의 `playerprogress.json` 및 `playerprogress.json.bak`다. 두 파일 모두
실행 전후 존재했고, SHA-256은 동일했으며 UTC 수정 시각도 동일했다. 검증과 구현 중 실제 파일을 백업,
생성, 수정 또는 삭제하지 않았다.

## 다음 작업 권장

향후 새 EditMode 시험은 `SaveSystem.Data`에 앞서 scoped 저장소 fixture 또는 명시적 가짜 저장소를 사용하도록
공통 테스트 헬퍼로 표준화한다.
