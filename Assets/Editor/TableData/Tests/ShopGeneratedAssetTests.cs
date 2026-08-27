using Building;
using Inventory;
using NUnit.Framework;
using Shop;
using UnityEditor;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// ShopTables 전용 Rebuild가 실제 Generated 출력에 옮긴 계약만 읽기 전용으로 확인한다.
    /// 이 시험은 에셋을 만들거나 다시 쓰지 않는다. 생성은 명시적인 Rebuild 메뉴/배치 경로의 몫이다.
    /// </summary>
    public sealed class ShopGeneratedAssetTests
    {
        [Test]
        public void GeneratedItems_PreserveAllSaleMetadata()
        {
            AssertItem("50000", true, "jewel", 10);
            AssertItem("50001", true, "jewel", 15);
            AssertItem("50002", true, "jewel", 20);
            AssertItem("50003", true, "jewel", 25);
            AssertItem("50004", false, "jewel", 30);
        }

        [Test]
        public void GeneratedShop_HasTheAuthoredBuildingAndSalesPolicy()
        {
            var shop = AssetDatabase.LoadAssetAtPath<ShopDefinition>(
                TableDataPaths.ShopAssetPath("general_shop"));

            Assert.IsNotNull(shop);
            Assert.AreEqual("general_shop", shop.ShopId);
            Assert.AreEqual(3, shop.RequiredBuildingId);
            Assert.IsTrue(shop.AcceptItemSales);
            Assert.AreEqual(10, shop.DisplayOrder);
            Assert.IsTrue(shop.Enabled);

            var catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(TableDataPaths.ShopCatalogAssetPath);
            Assert.IsNotNull(catalog);
            Assert.AreEqual(1, catalog.ActiveShops.Count);
            Assert.AreSame(shop, catalog.ActiveShops[0]);
        }

        [Test]
        public void GeneratedShopProduct_UsesTheCompositeKeyPathAndAuthoredValues()
        {
            var product = AssetDatabase.LoadAssetAtPath<ShopProductDefinition>(
                TableDataPaths.ShopProductAssetPath("general_shop", "50000"));

            Assert.IsNotNull(product);
            Assert.AreEqual("general_shop", product.ShopId);
            Assert.AreEqual("50000", product.ItemId);
            Assert.AreEqual("jewel", product.BuyCurrencyId);
            Assert.AreEqual(100, product.BuyPrice);
            Assert.AreEqual(10, product.DisplayOrder);
            Assert.IsTrue(product.Enabled);

            var catalog = AssetDatabase.LoadAssetAtPath<ShopProductCatalog>(
                TableDataPaths.ShopProductCatalogAssetPath);
            Assert.IsNotNull(catalog);
            Assert.AreEqual(1, catalog.GetActiveProducts("general_shop").Count);
            Assert.AreSame(product, catalog.Find("general_shop", "50000"));
        }

        [Test]
        public void GeneratedShopBuilding_PreservesTheOneSecondZeroCostContract()
        {
            var building = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(TableDataPaths.BuildingAssetPath("3"));

            Assert.IsNotNull(building);
            Assert.AreEqual("3", building.BuildingId);
            Assert.AreEqual(1, building.BuildTimeSeconds);
            Assert.AreEqual("jewel", building.CostCurrencyId);
            Assert.AreEqual(0, building.CostCurrencyAmount);
            Assert.AreEqual(0, building.CostItems.Count);
            Assert.IsTrue(building.Enabled);
        }

        [Test]
        public void ExistingItemAndBuildingAssets_KeepTheirGuids()
        {
            Assert.AreEqual("0abf78287f21a4ecfb20566c2b8b02ac",
                AssetDatabase.AssetPathToGUID(TableDataPaths.ItemAssetPath("50000")));
            Assert.AreEqual("1e0e90600454446e8980563a44db5bef",
                AssetDatabase.AssetPathToGUID(TableDataPaths.BuildingAssetPath("1")));
            Assert.AreEqual("d0ec875d301594c0cb66130069403d35",
                AssetDatabase.AssetPathToGUID(TableDataPaths.BuildingCatalogAssetPath));
        }

        private static void AssertItem(string itemId, bool sellable, string currencyId, int price)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(TableDataPaths.ItemAssetPath(itemId));
            Assert.IsNotNull(item, itemId + " generated asset is missing.");
            Assert.AreEqual(sellable, item.Sellable, itemId);
            Assert.AreEqual(currencyId, item.SellCurrencyId, itemId);
            Assert.AreEqual(price, item.SellPrice, itemId);
        }
    }
}
