# Shop Trade Foundation — Phase 12B-C

## 기준 및 범위

- 기준 커밋: `731ee204` (`Add atomic shop trade service`)
- 이번 단계는 12A 테이블 기반과 12B 원자적 거래 기반의 최종 검증만 수행했다.
- 상점 UI, 씬 연결, 토스트, 수량 선택 기능은 변경하지 않았다.

## 검증한 계약

- `general_shop`은 `requiredBuildingId: 3`을 사용하며, `BuildingCompletionPolicy.IsConfirmedCompleted`가 완료 확인 표식(`completionNotified`)까지 확인한 경우에만 거래가 허용된다.
- ShopProduct는 `(shop_id, item_id)` 복합 키로 조회된다. Generated 상품은 `general_shop + 50000`, 구매 재화 `jewel`, 가격 `100`이다.
- 구매는 ShopProduct의 구매 계약을 사용하고, 판매는 ShopProduct 목록이 아니라 Item의 `Sellable`, `SellCurrencyId`, `SellPrice`를 사용한다.
- Item 50004는 `Sellable=false`, `jewel`, `30`으로 생성되어 가격 데이터가 있어도 판매되지 않는다.
- 성공 거래는 메모리 적용 후 저장 1회와 `InventoryChanged` 알림 1회를 수행한다.
- 저장 실패 또는 저장 예외 시 아이템 목록·순서·수량·재화와 저장 메타데이터를 영수증 기반으로 복원한다.

## 집중 테스트

| 테스트 묶음 | 결과 |
|---|---:|
| `ShopTradeServiceTests` | 10/10 통과 |
| `ShopTableTests` | 12/12 통과 |
| `ShopGeneratedAssetTests` | 6/6 통과 |
| `InventoryTradeMutationTests` | 10/10 통과 |
| `BuildingCompletionConfirmTests` | 3/3 통과 |

Unity C# 컴파일 오류는 0건이었다. `git diff --check`도 통과했다.

## Generated 및 변경하지 않은 영역

- Generated Item 50000~50004의 판매 필드와 Shop/ShopProduct Generated 계약을 확인했다.
- `ItemCatalog`, `ShopCatalog`, `ShopProductCatalog`의 조회 계약을 확인했다.
- SaveData `CurrentSaveVersion`는 6으로 유지된다.
- CSV, Localization, 씬, 프리팹 및 거래 서비스 외 프로덕션 코드는 변경하지 않았다.
- 실제 `persistentDataPath`와 원격 저장소는 사용하지 않았다.

작업 트리는 보고서 커밋 후 clean 상태로 유지한다. 다음 단계는 상점 UI 연결이다.
