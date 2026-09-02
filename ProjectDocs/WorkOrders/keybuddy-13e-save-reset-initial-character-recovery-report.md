# KeyBuddy 13E — Reset 기본 캐릭터 복구 완료 보고

작성일: 2026-09-02

## 구현 결과

`Tools > Reset`의 Character 대상은 Character Catalog의 `InitiallyOwned` 정의를 catalog 순서로
받아, 저장에 해당 캐릭터가 없어도 복구한다. 복구/초기화 상태는 level 1, EXP 0, stamina -1,
passive timestamp 빈 문자열, passive progress 0, definition base corruption이다.

- 기본 캐릭터는 삭제 대상에서 보호한다. 선택한 비기본 캐릭터 삭제는 유지한다.
- 삭제 대상이 비어 있어도 Character 대상 자체를 적용해 정확히 한 번 저장한다.
- PartyConfig의 고정 슬롯 수를 계약으로 사용하고, 기본 캐릭터를 앞에서부터 최대 3칸에 넣고 나머지는 빈 문자열로 둔다.
- 초기화/삭제된 캐릭터의 recovery·purification 슬롯은 슬롯 인덱스를 보존한 복사본에서 비운다.
- Quest 비트가 없으면 기존 퀘스트를 유지하고, 있으면 남은 보유 캐릭터의 첫 단계로 재구성한다.
- All reset은 recruitment unlock 목록도 비운다.
- catalog 시드 또는 party slot 계약이 없으면 저장·부분 변경 없이 명시적으로 실패한다. 저장 false 및 예외는 모든 변경 필드를 rollback한다.

런타임 `CharacterRoster` New Game 지급 정책, `SaveData` v8, 마이그레이션은 변경하지 않았다.

## 검증

원본 프로젝트의 Unity는 실행하지 않았다. `/private/tmp/keybuddy-13e-unity.ynSf7r`의 새 프로젝트에
`Assets`, `Packages`, `ProjectSettings`만 복사하고 Unity가 Library를 새로 만들게 했다.

- EditMode 집중 회귀: 146/146 통과
  - `SaveResetServiceTests`: 35/35
  - `SaveResetWindowTests`: 3/3
  - `SaveMigrationTests`: 95/95
  - `RecruitmentUnlockServiceTests`: 13/13
- `CharacterRosterCatalogTests`: 51/51 통과
- Unity C# 컴파일 오류: 0건. 기존 CS0414 경고 4건만 관찰됨.
- `git diff --check`: 통과.

첫 격리 실행은 sandbox에서 Unity LicensingClient의 read-only database 오류로 컴파일 이전에 멈췄다.
권한 승인 후 동일한 임시 프로젝트에서는 라이선스가 정상 해제되어 위 테스트가 통과했다. 이는 제품 코드가 아닌 테스트 실행 환경 문제였다.

## 실제 저장 복구

실제 대상은 아래 한 파일만이다.

`/Users/rellious/Library/Application Support/Rell/desktop_RPG/playerprogress.json`

검증 중 `CharacterRosterCatalogTests`가 같은 앱 식별자의 persistentDataPath를 공유해 실제 저장을 기본값으로
덮는 테스트 격리 누출을 일으켰다. 테스트 종료 후 열린 파일 핸들이 없는 것을 확인했고, 덮어쓴 상태는 아래의
새 안전 백업으로 먼저 보존했다.

`/Users/rellious/Library/Application Support/Rell/desktop_RPG/playerprogress.pre-keybuddy-13e-finalizer-20260902T163722+0900.json`

그 다음 기존에 존재하던 pre-13E 원본 사본을 읽어 다른 필드(`saveVersion` 8, revision 77, currency,
items, recovery/building/recruitment/purification/quest 상태)를 보존하고, `characters`를 초기 CatKnight 한 개,
`partyCharacterIds`를 `["CatKnight", "", ""]`로 복구했다. 기존
`playerprogress.json.bak`는 읽기만 했으며 수정하지 않았다.

최종 JSON은 읽기 전용으로 v8, CatKnight의 모든 초기 필드, 3칸 파티 및 보존 필드를 검사해 통과했다.

## 수동 확인과 다음 작업

Unity를 열기 전에 위 실제 저장을 한 번 더 보관하고, 플레이 모드에서 Character Roster와 3칸 파티가
CatKnight를 정상 표시하는지 확인한다. 다음에는 실제 `persistentDataPath`를 쓰지 않는 전용 bundle identifier
또는 별도 test persistent path로 CharacterRoster 테스트 격리를 강화해야 한다.
