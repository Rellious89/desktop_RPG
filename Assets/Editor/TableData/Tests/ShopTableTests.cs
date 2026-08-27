using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace TableDataEditor.Tests
{
    public sealed class ShopTableTests
    {
        private static readonly MethodInfo Shops = typeof(TableDataValidator).GetMethod("ValidateShops", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo Products = typeof(TableDataValidator).GetMethod("ValidateShopProducts", BindingFlags.NonPublic | BindingFlags.Static);

        [Test]
        public void LiveTables_ParseWithoutValidationErrors()
        {
            TableDataValidationResult result = TableDataValidator.Validate();
            Assert.IsFalse(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.AreEqual("general_shop", result.Snapshot.Shops[0].Id);
            Assert.AreEqual("50000", result.Snapshot.ShopProducts[0].ItemId);
        }

        [TestCase("", "50000", "jewel", "100")]
        [TestCase("missing", "50000", "jewel", "100")]
        [TestCase("general_shop", "missing", "jewel", "100")]
        [TestCase("general_shop", "50000", "missing", "100")]
        [TestCase("general_shop", "50000", "jewel", "0")]
        [TestCase("general_shop", "50000", "jewel", "-1")]
        [TestCase("general_shop", "50000", "jewel", "1.5")]
        public void Product_InvalidContracts_AreRejected(string shop, string item, string currency, string price)
        {
            TableDataSnapshot snapshot = Seed(); TableDataDiagnosticLog log = new TableDataDiagnosticLog();
            Products.Invoke(null, new object[] { ProductTable(new[] { shop, item, currency, price, "1", "1", "" }), snapshot, log });
            Assert.Greater(log.ErrorCount, 0);
        }

        [Test]
        public void Product_CompositeDuplicate_IsRejected()
        {
            TableDataSnapshot snapshot = Seed(); TableDataDiagnosticLog log = new TableDataDiagnosticLog();
            Products.Invoke(null, new object[] { ProductTable(Row(), Row()), snapshot, log });
            Assert.Greater(log.ErrorCount, 0);
        }

        [TestCase("", "1", "1")]
        [TestCase("general_shop", "2", "1")]
        [TestCase("general_shop", "1", "2")]
        public void Shop_InvalidContracts_AreRejected(string id, string building, string sales)
        {
            TableDataSnapshot snapshot = Seed(); TableDataDiagnosticLog log = new TableDataDiagnosticLog();
            Shops.Invoke(null, new object[] { ShopTable(new[] { id, "", "", building, sales, "1", "1", "" }), snapshot, log });
            Assert.Greater(log.ErrorCount, 0);
        }

        private static TableDataSnapshot Seed()
        {
            var s = new TableDataSnapshot();
            s.BuildingsById["1"] = new BuildingRow { Id = "1" };
            s.ItemsById["50000"] = new ItemRow { Id = "50000" };
            s.CurrenciesById["jewel"] = new CurrencyRow { Id = "jewel" };
            s.ShopsById["general_shop"] = new ShopRow { Id = "general_shop" };
            return s;
        }
        private static string[] Row() => new[] { "general_shop", "50000", "jewel", "100", "1", "1", "" };
        private static CsvTable ProductTable(params string[][] rows) => new CsvTable("ShopProduct.csv", TableDataColumns.ShopProduct, Records(rows));
        private static CsvTable ShopTable(params string[][] rows) => new CsvTable("Shop.csv", TableDataColumns.Shop, Records(rows));
        private static List<CsvRecord> Records(string[][] rows) { var list = new List<CsvRecord>(); for (int i = 0; i < rows.Length; i++) list.Add(new CsvRecord(i + 2, rows[i])); return list; }
    }
}
