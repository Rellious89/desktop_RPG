# KeyBuddy 12A — 상점 테이블 런타임 기반 완료 보고서

작성일: 2026-08-27
브랜치: `save-system`
기준 커밋: `73771cce Generate shop table assets`

## 12A 커밋 이력

- `e282885a Add shop table runtime foundation`
- `916bcd26 Validate shop table data`
- `bf5960a3 Test shop table validation`
- `f689b3b7 Add shop table rebuild scope`
- `73771cce Generate shop table assets`
- 본 보고서 및 최종 검증 커밋: 이 문서를 포함한 후속 커밋

## 최종 데이터 계약

- `Item.csv`는 기존 Item 식별·현지화·아이콘·정렬 필드에 `sellable`, `sell_currency_id`,
  `sell_price`를 더한다. `sellable`이 실제 플레이어 판매 허용 여부의 최종 스위치다.
- `sellable=0`은 판매 불가를 뜻하지만, 유효한 판매 재화와 양수 판매 가격은 보존한다. 현재
  `50004`는 `false / jewel / 30`으로 Generated Definition과 Validator Snapshot에 그대로 남는다.
- `Shop.csv`는 `shop_id`, 현지화 이름, `required_building_id`, `accept_item_sales`,
  `display_order`, `enabled`를 제공한다. 현재 `general_shop`은 Building ID 3을 요구한다.
- `ShopProduct.csv`는 상점이 플레이어에게 판매하는 상품만 가진다. 별도 product ID는 없으며,
  `shop_id + item_id`가 유일한 복합 키다. 현재 상품은 `general_shop + 50000`, `jewel`, 100이다.
- Building ID 3은 1초, `jewel`, 비용 0, 아이템 비용 없음으로 유효하며 기존 완료 확인 정책은 변경하지 않았다.

## 런타임 및 Rebuild 구조

- `ItemDefinition`은 `Sellable`, `SellCurrencyId`, `SellPrice`를 공개한다.
- `ShopDefinition`/`ShopCatalog`, `ShopProductDefinition`/`ShopProductCatalog`을 사용한다.
  Catalog는 enabled 항목만 `display_order`와 안정적인 키 순서로 제공한다.
- `TableDataRebuildScope.ShopTables`는 Item, Shop, ShopProduct만 갱신한다. Building은
  `BuildingTable` 전용 Rebuild로 별도 처리한다.
- 실제 생성은 `BuildingTable` 후 `ShopTables` 순서로 수행했다. 상점 Writer는 기존 에셋을 resolve하여
  갱신하고, 새 Shop·ShopProduct만 결정적 경로에 생성한다.

## Generated 결과와 보존 확인

- 갱신: Item 50000~50004 및 ItemCatalog 계약, BuildingCatalog.
- 생성: `Building_3`, `Shop_general_shop`, `ShopCatalog`,
  `ShopProduct_general_shop__50000`, `ShopProductCatalog`과 각각의 meta.
- 기존 GUID 유지 확인:
  - `Item_50000`: `0abf78287f21a4ecfb20566c2b8b02ac`
  - `Building_1`: `1e0e90600454446e8980563a44db5bef`
  - `BuildingCatalog`: `d0ec875d301594c0cb66130069403d35`
- Git 변경 범위를 확인하여 대상 Item·Building 및 신규 Shop/ShopProduct 출력 외의 Generated 도메인에는
  변경 또는 신규 파일이 없음을 확인했다.

## 집중 검증

| EditMode 묶음 | 결과 |
| --- | --- |
| `ShopTableTests` | 12 / 12 통과 |
| `ShopGeneratedAssetTests` | 6 / 6 통과 |
| `ItemTableTests` | 29 / 29 통과 |
| `BuildingTableTests` | 44 / 44 통과 |
| 합계 | 91 / 91 통과 |

- Item 판매 Validator는 판매 가능/불가+가격 보존, 빈 재화, 0·음수·비정수 가격, 알 수 없는 재화,
  잘못된 `sellable` 값을 메모리 CSV로 직접 검증한다.
- Unity C# 컴파일 오류: 0
- `git diff --check`: 통과
- SaveData: `CurrentSaveVersion = 6` 유지
- 전체 EditMode·PlayMode·Sol 검증은 이번 단계 범위에 따라 실행하지 않았다.

## 작업 경계

- 실제 `persistentDataPath`를 읽거나 쓰지 않았다.
- 원격 푸시는 수행하지 않았다.
- 구매·판매 트랜잭션, 재화·아이템 변경, 상점 UI·씬·프리팹, 재고는 구현하지 않았다.

다음 단계는 이 테이블 계약을 소비하는 원자적 구매·판매 서비스다.
