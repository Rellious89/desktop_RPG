# KeyBuddy 10F — 행동력 소진 파티 자동 교체 보고서

- 선행 메타 커밋: `ea69d734` — 정상 Unity 메타인 `Assets/Scripts/Party/PartySlotUtility.cs.meta`를 GUID 변경 없이 추가
- 구현 커밋: `b6263c08` — 처치 행동력 소진 시 고정 슬롯 순서 자동 교체

정상 처치 이벤트에서만, 현재 캐릭터의 행동력이 1 이상에서 정확히 0이 된 경우에 자동 교체를 한 번 시도한다. `SaveData.partyCharacterIds`의 v4 슬롯 인덱스에서 현재 캐릭터 다음 슬롯부터 순환하며 빈 슬롯, 자신, 미보유/사용 불가, 행동력 0, 회복소 등록 후보를 건너뛴다. 현재 캐릭터 슬롯을 찾지 못하면 슬롯 1부터 탐색한다.

후보 적용은 기존 `TrySwitchTo`/런타임 액터 경로를 재사용한다. 행동력 차감 저장은 기존대로 한 번이고, 자동 교체는 저장이나 파티 데이터 변경을 추가로 만들지 않는다. 후보가 없거나 적용에 실패하면 행동력 0의 현재 캐릭터와 고정 슬롯을 그대로 유지한다.

집중 EditMode 검증: `CharacterRosterCatalogTests` 48/48 통과. 자동 교체의 슬롯 순환, 빈 슬롯, 끝-처음 순환, 현재 슬롯 누락, 행동력 0·회복소 후보 제외, 후보 없음, 중복 처치, 저장 1회 및 파티 데이터 불변을 확인했다. 격리 복제 프로젝트 Unity C# 컴파일 오류는 0이며 `git diff --check`도 통과했다.

SaveData는 v4를 유지했다. 수정 파일은 `CharacterRoster.cs`, 관련 집중 테스트 및 이 보고서뿐이며 씬·프리팹·CSV·Localization·Generated 에셋은 변경하지 않았다. 실제 `persistentDataPath` 접근과 원격 푸시는 하지 않았다.
