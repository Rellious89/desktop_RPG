using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// Item.csv 파이프라인에서 <b>이번 단계에 새로 생긴 계약</b>을 확인하는 시험 - 설명 컬럼의 스키마,
    /// 이름/설명이 함께 있어야 한다는 규칙, 두 참조의 카테고리·키 관계, 그리고 실제 Entry의 존재다.
    ///
    /// <b>파일을 쓰지도 에셋을 만들지도 않는다.</b> 행 검증은 메모리에서 만든 <see cref="CsvTable"/>로
    /// 돌리고, 실제 데이터 확인은 읽기 전용인 <see cref="TableDataValidator.Validate"/>와
    /// AssetDatabase 읽기만 쓴다(<see cref="CurrencyTableTests"/>와 같은 방식이다).
    /// </summary>
    public sealed class ItemTableTests
    {
        private const string File = TableDataPaths.ItemCsvFileName;

        private const string ItemSharedDataPath = "Assets/Localization/Tables/04_Item/04_Item Shared Data.asset";

        private static readonly MethodInfo ValidateItemsMethod =
            typeof(TableDataValidator).GetMethod("ValidateItems", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>Item.csv에 실제로 적혀 있는 다섯 행. 이름 키와 설명 키의 관계가 눈에 보이도록 짝으로 둔다.</summary>
        private static readonly (string ItemId, int NameKey, string IconKey)[] LiveRows =
        {
            ("50000", 1, "item_ico_1"),
            ("50001", 2, "item_ico_2"),
            ("50002", 3, "item_ico_3"),
            ("50003", 4, "item_ico_4"),
            ("50004", 5, "item_ico_5"),
        };

        /// <summary>
        /// 생성 에셋의 GUID. <b>여기 적힌 값이 바뀌면 그 에셋을 가리키던 씬/프리팹 참조가 끊긴 것</b>이라,
        /// 다시 만들어진 것을 "통과"로 넘기지 않기 위해 못 박아 둔다. 데이터를 고쳐도 GUID는 그대로이므로
        /// CSV를 편집하는 평소 작업으로는 이 시험이 깨지지 않는다 - 깨졌다면 에셋이 지워졌다 새로
        /// 만들어졌다는 뜻이다.
        /// </summary>
        private static readonly (string RelativePath, string Guid)[] GeneratedGuids =
        {
            ("Building/Building_1.asset", "1e0e90600454446e8980563a44db5bef"),
            ("Building/BuildingCatalog.asset", "d0ec875d301594c0cb66130069403d35"),
            ("Character/Character_Barbarian.asset", "248d14fc02c4142c49d3913832fb519e"),
            ("Character/Character_CatKnight.asset", "342e077c6a6894f169a84d989ea37a84"),
            ("Character/Character_CatMage.asset", "0af03590e26f24a0fa9976bcb738f27b"),
            ("Character/Character_ElfArcher.asset", "26424a01213dc4878ba2d84e1fd898f7"),
            ("Character/Character_ElfGuardian.asset", "40051d3b8e28b470aa140f0bad4cdb10"),
            ("Character/Character_RabbitHealer.asset", "157d45a3c95e243789cc99d02d3b662b"),
            ("Character/CharacterCatalog.asset", "5f24823f983054d358a82014246a9fa7"),
            ("CharacterSkill/CharacterSkillCatalog.asset", "3f9eaae2823dc4c8a87a194e8165f086"),
            ("Currency/Currency_jewel.asset", "25530e3cfd6944ca4873f62520cd7d22"),
            ("Currency/CurrencyCatalog.asset", "88a00488cdf3246d3bc4d40efd091841"),
            ("Dungeon/Dungeon_1.asset", "c41af48b8189b427897cc7dd37eeeb6b"),
            ("Dungeon/Dungeon_2.asset", "896cccf79b3de406383335a1c0879370"),
            ("Dungeon/Dungeon_3.asset", "ab7d6716f47f74bb1adb376d3c54a102"),
            ("Dungeon/Dungeon_4.asset", "63d4ce595405a4715a77ebc3849a871f"),
            ("Dungeon/Dungeon_5.asset", "9eb5880d32e9944c9900320a79d204c1"),
            ("Dungeon/Dungeon_6.asset", "4e13a928ed5554679878c787fa42f11b"),
            ("Dungeon/Dungeon_7.asset", "d724c65b2b9974fe9a22d1136b5a284a"),
            ("Dungeon/Dungeon_8.asset", "e0bb9dbdfe4ae4702a290c787e3b9742"),
            ("Dungeon/Dungeon_9.asset", "4b2420a8e45664fe3bef263783fab6f2"),
            ("Dungeon/Dungeon_test_dungeon_01.asset", "7432a20b52254412a86ec5659b75912e"),
            ("Dungeon/DungeonCatalog.asset", "5a3dc674fb40a445ba6d768b5775d116"),
            ("Item/Item_50000.asset", "0abf78287f21a4ecfb20566c2b8b02ac"),
            ("Item/Item_50001.asset", "b3c0618c290864a7ebcc1c4918dbd725"),
            ("Item/Item_50002.asset", "7f57e18d4d07d440590e07db76470d0d"),
            ("Item/Item_50003.asset", "77309d45b7b2343be8d600751716d1ae"),
            ("Item/Item_50004.asset", "c8030cc5dbcc24f638d72ed351c46977"),
            ("Item/ItemCatalog.asset", "749d57bd062ae47619e6dc1de90453fd"),
            ("Monster/Monster_1.asset", "6bc803a7b22e2422fb670082a75ff443"),
            ("Monster/Monster_2.asset", "750a5bed3166f44abb02d9f8c7920c9a"),
            ("Monster/Monster_3.asset", "5fc7227cf15f34aeab2816ab6f5ead2e"),
            ("Monster/Monster_4.asset", "7d7b2be59499e458ebc51a62bb25e1d5"),
            ("Monster/Monster_5.asset", "f1c479ab5d0554bd682c233ea9353d28"),
            ("Monster/Monster_6.asset", "8589e8a46793f40a18fc028b8c128a79"),
            ("Monster/Monster_7.asset", "4db51d9371d3144f9bf18a5fbe24eec5"),
            ("Monster/Monster_8.asset", "8daf2b2fd616f49e9ac1505f2a26a95e"),
            ("Monster/Monster_rock_golem.asset", "7c9d7650edc764de197473dc3392f3f8"),
            ("Monster/Monster_scarecrow.asset", "b128d432a8a324b2dacc7a3e703455bd"),
            ("Monster/MonsterCatalog.asset", "0846229ba87f7442a8cabec60e64dd41"),
            ("Skill/SkillCatalog.asset", "6f83e310e008c4b8dbc41d382a075cac"),
            ("World/World_1.asset", "79144df9714ff41edad2ce9c6244bd91"),
            ("World/World_2.asset", "629d166772e154d9eb3f9e0e564074ae"),
            ("World/World_3.asset", "b8ca4f12582f54475999d8df148f9c91"),
            ("World/World_animal_land.asset", "45889bb75f1774b7b972923fa3540827"),
            ("World/WorldCatalog.asset", "0283d76a32d564a4fbf6e9065a0d939d"),
        };

        /// <summary>실제 CSV를 읽는 검증은 프로젝트 전체 Sprite 인덱스를 만드는 무거운 동작이라
        /// 한 번만 돌리고 결과를 나눠 쓴다. 읽기 전용이므로 시험 사이에 상태가 새지 않는다.</summary>
        private static TableDataValidationResult liveResult;

        [SetUp]
        public void SetUpFixture()
        {
            Assert.IsNotNull(ValidateItemsMethod,
                "TableDataValidator.ValidateItems를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");
        }

        // ---- 스키마와 경로 ----

        [Test]
        public void Schema_IsExactlyTheAgreedColumns()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "item_id", "name_category", "name_key", "description_category", "description_key",
                    "icon_key", "display_order", "enabled", "memo",
                },
                TableDataColumns.Item,
                "Item.csv의 필수 컬럼과 순서가 약속과 달라졌습니다.");
        }

        [Test]
        public void Schema_KeepsTheAuthoringColumnsReferenceOnly()
        {
            foreach (string column in new[] { "$item_name", "$item_description", "$item_type" })
            {
                CollectionAssert.DoesNotContain(TableDataColumns.Item, column,
                    $"{column}은 작업자용 참조 컬럼이라 필수 컬럼이 되면 안 된다.");
                Assert.IsTrue(TableDataCsvReader.IsReferenceOnlyColumn(column),
                    $"{column}은 참조 컬럼 정책으로 통과해야 한다.");
            }
        }

        [Test]
        public void Schema_SharesTheDescriptionColumnNamesWithSkillCsv()
        {
            // 두 표가 같은 상수를 가리켜야 뜻이 갈라지지 않는다 - 이름을 두 번 적어 두면 한쪽만 고쳐진다.
            CollectionAssert.Contains(TableDataColumns.Item, TableDataColumns.DescriptionCategory);
            CollectionAssert.Contains(TableDataColumns.Item, TableDataColumns.DescriptionKey);
            CollectionAssert.Contains(TableDataColumns.Skill, TableDataColumns.DescriptionCategory);
            CollectionAssert.Contains(TableDataColumns.Skill, TableDataColumns.DescriptionKey);
        }

        [Test]
        public void IconRoot_PointsAtTheFolderTheIconsActuallyLiveIn()
        {
            Assert.AreEqual("Assets/Art/UI/Item", TableDataPaths.ItemIconRoot);
            Assert.IsTrue(AssetDatabase.IsValidFolder(TableDataPaths.ItemIconRoot),
                "아이템 아이콘 폴더가 없으면 icon_key는 무엇을 적어도 찾지 못한다.");
        }

        // ---- 행 검증: 이름과 설명은 한 덩어리 ----

        [Test]
        public void EnabledRow_WithBothReferences_IsAccepted()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log, Row("50000", "4", "1", "4", "10001"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, snapshot.Items.Count);
            Assert.IsTrue(snapshot.Items[0].Name.Resolved, "이름 참조가 실제 Entry로 해석되어야 한다.");
            Assert.IsTrue(snapshot.Items[0].Description.Resolved, "설명 참조가 실제 Entry로 해석되어야 한다.");
        }

        [Test]
        public void EnabledRow_WithoutName_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log, Row("50000", "", "", "4", "10001"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.NameCategory), Describe(log));
        }

        [Test]
        public void EnabledRow_WithoutDescription_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log, Row("50000", "4", "1", "", ""));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.DescriptionCategory), Describe(log));
        }

        [Test]
        public void HalfFilledDescription_IsAnErrorOnTheEmptyColumn()
        {
            Validate(out TableDataDiagnosticLog log, Row("50000", "4", "1", "4", ""));
            Assert.AreEqual(1, CountErrors(log, TableDataColumns.DescriptionKey), Describe(log));

            Validate(out TableDataDiagnosticLog other, Row("50000", "4", "1", "", "10001"));
            Assert.AreEqual(1, CountErrors(other, TableDataColumns.DescriptionCategory), Describe(other));
        }

        [Test]
        public void DisabledRow_WithoutDescription_IsAWarningNotAnError()
        {
            TableDataSnapshot snapshot = Validate(
                out TableDataDiagnosticLog log, Row("50000", "4", "1", "", "", enabled: "0"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, CountWarnings(log, TableDataColumns.DescriptionCategory), Describe(log));
            Assert.AreEqual(1, snapshot.Items.Count, "비활성 행도 에셋을 만들 수 있도록 스냅샷에는 남는다.");
        }

        // ---- 상점 판매 계약 ----

        [Test]
        public void SaleMetadata_AllowsSellableAndDisabledItems_AndPreservesDisabledPrice()
        {
            TableDataSnapshot snapshot = ValidateSale(out TableDataDiagnosticLog log,
                SaleRow("50000", "1", "jewel", "10", displayOrder: "10"),
                SaleRow("50004", "0", "jewel", "30", displayOrder: "20"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(2, snapshot.Items.Count);
            Assert.IsTrue(snapshot.ItemsById["50000"].Sellable);
            Assert.AreEqual("jewel", snapshot.ItemsById["50000"].SellCurrencyId);
            Assert.AreEqual(10, snapshot.ItemsById["50000"].SellPrice);

            ItemRow disabled = snapshot.ItemsById["50004"];
            Assert.IsFalse(disabled.Sellable, "판매 허용의 최종 스위치는 sellable이다.");
            Assert.AreEqual("jewel", disabled.SellCurrencyId);
            Assert.AreEqual(30, disabled.SellPrice,
                "sellable=0이어도 미리 설정된 가격은 스냅샷에서 지우지 않는다.");
        }

        [Test]
        public void LiveDisabledSaleItem_PreservesItsAuthoredPriceMetadata()
        {
            ItemRow item = Live().Snapshot.ItemsById["50004"];

            Assert.IsFalse(item.Sellable);
            Assert.AreEqual("jewel", item.SellCurrencyId);
            Assert.AreEqual(30, item.SellPrice);
        }

        [TestCase("1", "", "10")]
        [TestCase("1", "jewel", "0")]
        [TestCase("1", "jewel", "-1")]
        [TestCase("1", "jewel", "1.5")]
        [TestCase("1", "unknown", "10")]
        [TestCase("2", "jewel", "10")]
        public void SaleMetadata_InvalidContracts_AreRejected(string sellable, string currency, string price)
        {
            ValidateSale(out TableDataDiagnosticLog log, SaleRow("50000", sellable, currency, price));

            Assert.Greater(log.ErrorCount, 0, Describe(log));
        }

        // ---- 행 검증: 두 참조의 관계 ----

        [Test]
        public void DescriptionInADifferentCategory_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log, Row("50000", "4", "1", "5", "10001"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.DescriptionCategory), Describe(log));
        }

        [Test]
        public void DescriptionKey_ThatIsNotNameKeyPlusOffset_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log, Row("50000", "4", "1", "4", "10002"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.DescriptionKey), Describe(log));
        }

        [Test]
        public void DescriptionKeyRule_UsesTheOffsetConstant()
        {
            // 규칙이 코드에 한 번만 적혀 있는지 확인한다 - 시험이 10000을 따로 적어 두면 상수를 바꿔도
            // 시험만 통과하는 상태가 생긴다.
            Assert.AreEqual(10000, TableDataValidator.ItemDescriptionKeyOffset);
        }

        [Test]
        public void NameKey_ThatOverflowsWhenOffsetIsAdded_IsReportedAsOverflow()
        {
            string nameKey = int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Validate(out TableDataDiagnosticLog log, Row("50000", "4", nameKey, "4", "10001"));

            Assert.AreEqual(1, CountErrorsContaining(log, "정수 범위를 넘어"),
                "int 범위를 넘는 name_key는 감싸 돌아간 기대값이 아니라 넘침으로 알려야 한다: " + Describe(log));
        }

        // ---- 행 검증: 실재하는 Entry인지 ----

        [Test]
        public void DescriptionKey_ThatHasNoEntry_IsAnError()
        {
            // 카테고리도 같고 오프셋도 맞지만 04_Item에 그런 Entry가 없다 - 참조는 형식이 아니라
            // 실재로 판정한다.
            Validate(out TableDataDiagnosticLog log, Row("50000", "4", "89999", "4", "99999"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.DescriptionKey), Describe(log));
            Assert.AreEqual(1, CountErrors(log, TableDataColumns.NameKey),
                "이름 키도 실재하지 않으므로 함께 보고되어야 한다: " + Describe(log));
        }

        [Test]
        public void UnknownDescriptionCategory_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log, Row("50000", "999", "1", "999", "10001"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.NameCategory), Describe(log));
            Assert.AreEqual(1, CountErrors(log, TableDataColumns.DescriptionCategory), Describe(log));
        }

        // ---- 실제 프로젝트 데이터(읽기 전용) ----

        [Test]
        public void LiveCsv_HasNoItemDiagnosticsAtAll()
        {
            var lines = new List<string>();
            foreach (TableDataDiagnostic diagnostic in Live().Diagnostics)
            {
                if (string.Equals(diagnostic.File, File, StringComparison.Ordinal)) lines.Add(diagnostic.ToString());
            }

            Assert.AreEqual(0, lines.Count,
                "Item.csv는 이름/설명/아이콘이 모두 채워져 있어 경고도 남지 않아야 한다:\n" + string.Join("\n", lines));
        }

        [Test]
        public void LiveCsv_BindsEveryRowToTheNameKeyPlusOffsetEntry()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot, "여덟 표가 모두 읽혀야 스냅샷이 만들어진다: " + Live().Summary);

            Dictionary<string, string> idsByKey = ReadSharedEntries(ItemSharedDataPath);

            foreach ((string itemId, int nameKey, string _) in LiveRows)
            {
                Assert.IsTrue(snapshot.ItemsById.TryGetValue(itemId, out ItemRow row), $"{itemId} 행이 없다.");

                string descriptionKey = (nameKey + TableDataValidator.ItemDescriptionKeyOffset)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);

                Assert.IsTrue(row.Name.Resolved, $"{itemId}의 이름 참조");
                Assert.IsTrue(row.Description.Resolved, $"{itemId}의 설명 참조");
                Assert.AreEqual(row.Name.TableGuid, row.Description.TableGuid,
                    $"{itemId}의 이름과 설명이 서로 다른 Table을 가리킨다.");
                Assert.AreEqual(
                    idsByKey[descriptionKey],
                    row.Description.KeyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"{itemId}의 설명이 숫자 키 {descriptionKey}의 Entry를 가리켜야 한다.");
            }
        }

        // ---- 생성 에셋 ----

        [Test]
        public void GeneratedItems_CarryTheDescriptionReferenceAndIcon()
        {
            Dictionary<string, string> idsByKey = ReadSharedEntries(ItemSharedDataPath);

            foreach ((string itemId, int nameKey, string iconKey) in LiveRows)
            {
                var definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(TableDataPaths.ItemAssetPath(itemId));
                Assert.IsNotNull(definition, $"생성 에셋이 없습니다 - Rebuild를 먼저 실행하세요: {itemId}");

                Assert.IsTrue(definition.HasLocalizedName, $"{itemId}의 이름 참조가 비어 있다.");
                Assert.IsTrue(definition.HasLocalizedDescription, $"{itemId}의 설명 참조가 비어 있다.");

                var serialized = new SerializedObject(definition);
                SerializedProperty keyId = serialized
                    .FindProperty("localizedDescription")
                    .FindPropertyRelative("m_TableEntryReference")
                    .FindPropertyRelative("m_KeyId");

                string descriptionKey = (nameKey + TableDataValidator.ItemDescriptionKeyOffset)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);

                Assert.AreEqual(idsByKey[descriptionKey],
                    keyId.longValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"{itemId}의 설명이 숫자 키 {descriptionKey}를 가리켜야 한다.");

                Assert.IsNotNull(definition.Icon, $"{itemId}의 아이콘이 연결되지 않았다(icon_key '{iconKey}').");
                Assert.AreEqual(iconKey, definition.Icon.name, $"{itemId}가 다른 아이콘을 가리킨다.");
            }
        }

        [Test]
        public void GeneratedItemCatalog_KeepsTheCsvOrder()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(TableDataPaths.ItemCatalogAssetPath);
            Assert.IsNotNull(catalog, "ItemCatalog 생성 에셋이 없습니다.");

            var ids = new List<string>();
            foreach (ItemDefinition item in catalog.Items) ids.Add(item.ItemId);

            var expected = new List<string>();
            foreach ((string itemId, int _, string _) in LiveRows) expected.Add(itemId);

            CollectionAssert.AreEqual(expected, ids, "카탈로그 순서는 display_order 오름차순 그대로여야 한다.");
        }

        [Test]
        public void EveryGeneratedAsset_KeepsItsGuidAndMetaFile()
        {
            foreach ((string relativePath, string guid) in GeneratedGuids)
            {
                if (!relativePath.StartsWith("Item/", StringComparison.Ordinal)) continue;
                string assetPath = TableDataPaths.OutputRoot + "/" + relativePath;
                string metaPath = assetPath + ".meta";

                Assert.IsTrue(System.IO.File.Exists(metaPath),
                    $"'{metaPath}'가 없습니다 - 생성 에셋이 지워졌다면 그 에셋을 가리키던 참조도 끊깁니다.");
                Assert.AreEqual(guid, ReadMetaGuid(metaPath),
                    $"'{assetPath}'의 GUID가 달라졌습니다 - 에셋을 지웠다 다시 만들면 씬/프리팹 참조가 끊깁니다.");
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath),
                    $"'{assetPath}'를 읽지 못했습니다.");
            }
        }

        [Test]
        public void NoGeneratedAsset_ExistsOutsideTheListedSet()
        {
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string relativePath, string _) in GeneratedGuids)
            {
                if (!relativePath.StartsWith("Item/", StringComparison.Ordinal)) continue;
                expected.Add(TableDataPaths.OutputRoot + "/" + relativePath);
            }

            var found = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { TableDataPaths.ItemOutputFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!expected.Contains(path)) found.Add(path);
            }

            Assert.AreEqual(0, found.Count,
                "생성 폴더에 목록에 없는 에셋이 있습니다 - 새 에셋이 생겼다면 이 시험의 목록도 함께 고치세요:\n"
                + string.Join("\n", found));
        }

        // ---- 도우미 ----

        private static TableDataValidationResult Live()
        {
            return liveResult ?? (liveResult = TableDataValidator.Validate());
        }

        /// <summary>메모리 위의 표로 행 검증만 돌린다. 파일도 에셋도 건드리지 않는다.</summary>
        private static TableDataSnapshot Validate(out TableDataDiagnosticLog log, params string[][] rows)
        {
            var records = new List<CsvRecord>();
            for (int i = 0; i < rows.Length; i++) records.Add(new CsvRecord(i + 2, rows[i]));

            var table = new CsvTable(File, TableDataColumns.Item, records);
            var snapshot = new TableDataSnapshot();
            log = new TableDataDiagnosticLog();

            ValidateItemsMethod.Invoke(null, new object[] { table, snapshot, new TableDataAssetIndex(), log });
            return snapshot;
        }

        /// <summary>판매 세 칸이 포함된 Item.csv 헤더를 쓰는 순수 메모리 검증 경로다.</summary>
        private static TableDataSnapshot ValidateSale(out TableDataDiagnosticLog log, params string[][] rows)
        {
            var records = new List<CsvRecord>();
            for (int i = 0; i < rows.Length; i++) records.Add(new CsvRecord(i + 2, rows[i]));

            var table = new CsvTable(File, SaleColumns, records);
            var snapshot = new TableDataSnapshot();
            snapshot.CurrenciesById["jewel"] = new CurrencyRow { Id = "jewel", Enabled = true };
            log = new TableDataDiagnosticLog();

            ValidateItemsMethod.Invoke(null, new object[] { table, snapshot, new TableDataAssetIndex(), log });
            return snapshot;
        }

        private static readonly string[] SaleColumns =
        {
            TableDataColumns.ItemId, TableDataColumns.NameCategory, TableDataColumns.NameKey,
            TableDataColumns.DescriptionCategory, TableDataColumns.DescriptionKey, TableDataColumns.IconKey,
            TableDataColumns.Sellable, TableDataColumns.SellCurrencyId, TableDataColumns.SellPrice,
            TableDataColumns.DisplayOrder, TableDataColumns.Enabled, TableDataColumns.Memo,
        };

        private static string[] SaleRow(
            string id, string sellable, string currency, string price,
            string displayOrder = "10", string enabled = "1")
        {
            return new[]
            {
                id, "4", "1", "4", "10001", string.Empty,
                sellable, currency, price, displayOrder, enabled, string.Empty,
            };
        }

        /// <summary>컬럼 순서는 <see cref="TableDataColumns.Item"/>과 같다. 아이콘은 늘 비운다 -
        /// 아이콘 판정은 이 시험의 관심사가 아니고, 빈 값은 경고 한 건으로 끝난다.</summary>
        private static string[] Row(
            string id, string nameCategory, string nameKey, string descriptionCategory, string descriptionKey,
            string displayOrder = "10", string enabled = "1")
        {
            return new[]
            {
                id, nameCategory, nameKey, descriptionCategory, descriptionKey, string.Empty,
                displayOrder, enabled, string.Empty,
            };
        }

        /// <summary>Shared Data의 숫자 키 -> 내부 Entry Key ID.</summary>
        private static Dictionary<string, string> ReadSharedEntries(string sharedDataPath)
        {
            Assert.IsTrue(System.IO.File.Exists(sharedDataPath), $"'{sharedDataPath}'가 없습니다.");

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            string pendingId = null;

            foreach (string line in System.IO.File.ReadAllLines(sharedDataPath))
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("- m_Id:", StringComparison.Ordinal))
                {
                    pendingId = trimmed.Substring("- m_Id:".Length).Trim();
                    continue;
                }

                if (pendingId == null || !trimmed.StartsWith("m_Key:", StringComparison.Ordinal)) continue;

                map[trimmed.Substring("m_Key:".Length).Trim()] = pendingId;
                pendingId = null;
            }

            return map;
        }

        private static string ReadMetaGuid(string metaPath)
        {
            foreach (string line in System.IO.File.ReadAllLines(metaPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("guid:", StringComparison.Ordinal))
                {
                    return trimmed.Substring("guid:".Length).Trim();
                }
            }

            return null;
        }

        private static int CountErrors(TableDataDiagnosticLog log, string column)
        {
            return Count(log, TableDataSeverity.Error, column);
        }

        private static int CountWarnings(TableDataDiagnosticLog log, string column)
        {
            return Count(log, TableDataSeverity.Warning, column);
        }

        private static int Count(TableDataDiagnosticLog log, TableDataSeverity severity, string column)
        {
            int count = 0;
            foreach (TableDataDiagnostic diagnostic in log.Entries)
            {
                if (diagnostic.Severity == severity
                    && string.Equals(diagnostic.Column, column, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountErrorsContaining(TableDataDiagnosticLog log, string fragment)
        {
            int count = 0;
            foreach (TableDataDiagnostic diagnostic in log.Entries)
            {
                if (diagnostic.Severity == TableDataSeverity.Error
                    && diagnostic.Message.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>실패 메시지에 진단 전문을 붙인다 - "오류 1건을 기대했는데 2건"만으로는 원인을 알 수 없다.</summary>
        private static string Describe(TableDataDiagnosticLog log)
        {
            var lines = new List<string>();
            foreach (TableDataDiagnostic diagnostic in log.Entries) lines.Add(diagnostic.ToString());
            return lines.Count == 0 ? "(진단 없음)" : "\n" + string.Join("\n", lines);
        }
    }
}
