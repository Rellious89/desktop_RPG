using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace CommonEditor.Localization.Tests
{
    /// <summary>
    /// 실제 TableData/Localization 및 Assets/Localization/Tables를 절대 쓰지 않는다.
    /// 각 테스트는 별도 temporary collection/CSV를 만들고 TearDown에서 모두 제거한다.
    /// </summary>
    public sealed class LocalizationBulkUpdateServiceTests
    {
        private const string Header = "Key,Id,English(en),Korean (South Korea)(ko-KR)";
        private string assetFolder;
        private string csvFolder;
        private string collectionName;
        private string csvPath;
        private StringTableCollection collection;
        private SharedTableData.SharedTableEntry existing;
        private readonly List<string> creationAssetFolders = new List<string>();

        [SetUp]
        public void SetUp()
        {
            string unique = Guid.NewGuid().ToString("N");
            collectionName = "__LocalizationBulkUpdateTest_" + unique;
            assetFolder = "Assets/__LocalizationBulkUpdateTests/" + unique;
            csvFolder = Path.Combine(Path.GetTempPath(), "LocalizationBulkUpdateTests", unique);
            csvPath = Path.Combine(csvFolder, collectionName + ".csv");
            Directory.CreateDirectory(csvFolder);

            List<Locale> locales = LocalizationEditorSettings.GetLocales()
                .Where(locale => locale.Identifier.Code == "en" || locale.Identifier.Code == "ko-KR")
                .ToList();
            if (locales.Count != 2)
            {
                Assert.Ignore("이 프로젝트에는 격리 테스트에 필요한 en, ko-KR Locale이 없습니다.");
            }

            collection = LocalizationEditorSettings.CreateStringTableCollection(collectionName, assetFolder, locales);
            Assert.IsNotNull(collection, "격리된 StringTableCollection을 만들지 못했습니다.");

            existing = collection.SharedData.AddKey("existing");
            GetTable("en").AddEntry(existing.Id, "old english");
            GetTable("ko-KR").AddEntry(existing.Id, "기존 한국어");

            var assetOnly = collection.SharedData.AddKey("asset-only");
            GetTable("en").AddEntry(assetOnly.Id, "preserve english");
            GetTable("ko-KR").AddEntry(assetOnly.Id, "보존 한국어");
            EditorUtility.SetDirty(collection.SharedData);
            foreach (StringTable table in collection.StringTables)
            {
                EditorUtility.SetDirty(table);
            }
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string folder in creationAssetFolders)
            {
                if (AssetDatabase.IsValidFolder(folder))
                {
                    AssetDatabase.DeleteAsset(folder);
                }
            }

            if (!string.IsNullOrEmpty(assetFolder))
            {
                AssetDatabase.DeleteAsset(assetFolder);
            }

            if (!string.IsNullOrEmpty(csvFolder) && Directory.Exists(csvFolder))
            {
                Directory.Delete(csvFolder, true);
            }

            AssetDatabase.Refresh();
        }

        [Test]
        public void Scan_UnchangedCsv_ReportsNoNewOrChangedEntries()
        {
            WriteCsv($"existing,{existing.Id},old english,기존 한국어");

            var table = ScanSingle();

            Assert.IsTrue(table.IsValid, table.Status);
            Assert.AreEqual(0, table.NewKeyCount);
            Assert.AreEqual(0, table.ChangedCount);
            Assert.AreEqual(1, table.DeletionDetectedCount);
            CollectionAssert.AreEqual(new[] { "asset-only" }, table.DeletionDetectedKeys);
            Assert.That(table.Status, Does.Contain("삭제 감지 1"));
            Assert.IsFalse(table.IsSelected);
        }

        [Test]
        public void Scan_KoreanOnlyChange_CountsOneChangedKey()
        {
            WriteCsv($"existing,{existing.Id},old english,바뀐 한국어");

            var table = ScanSingle();

            Assert.AreEqual(1, table.ChangedCount);
        }

        [Test]
        public void Scan_BothLocalesOfOneKeyChanged_CountsOneChangedKey()
        {
            WriteCsv($"existing,{existing.Id},changed english,바뀐 한국어");

            var table = ScanSingle();

            Assert.AreEqual(1, table.ChangedCount);
        }

        [Test]
        public void Scan_TwoDifferentChangedKeys_CountsTwoChangedKeys()
        {
            var second = collection.SharedData.AddKey("second");
            GetTable("en").AddEntry(second.Id, "second old english");
            GetTable("ko-KR").AddEntry(second.Id, "두번째 기존 한국어");
            WriteCsv(
                $"existing,{existing.Id},changed english,기존 한국어",
                $"second,{second.Id},second old english,두번째 변경 한국어");

            var table = ScanSingle();

            Assert.AreEqual(2, table.ChangedCount);
        }

        [Test]
        public void Scan_MissingCollection_IsNewLocalizationAndCanCreate()
        {
            string newName = NewCreationName();
            WriteNewCollectionCsv(newName, "first,,First,첫번째", "second,,Second,두번째");

            var table = ScanByName(newName);

            Assert.IsTrue(table.IsNewLocalization);
            Assert.IsTrue(table.IsValid, table.Status);
            Assert.IsTrue(table.CanCreateCollection, table.Status);
            Assert.AreEqual(2, table.NewKeyCount);
            Assert.That(table.Status, Does.Contain("신규 로컬라이즈: Collection 없음"));
        }

        [Test]
        public void UpdateSelected_BlocksNewLocalizationUntilCollectionIsCreated()
        {
            string newName = NewCreationName();
            WriteNewCollectionCsv(newName, "first,,First,첫번째");
            var table = ScanByName(newName);
            table.IsSelected = true;

            var update = LocalizationBulkUpdateService.UpdateSelected(new[] { table });

            Assert.IsFalse(update.Succeeded);
            Assert.That(update.Summary, Does.Contain("테이블 생성"));
        }

        [Test]
        public void CreateCollection_UsesOfficialAssetsAndDoesNotImportCsv()
        {
            string newName = NewCreationName();
            string folder = LocalizationBulkUpdateService.CollectionsRootAssetPath + "/" + newName;
            creationAssetFolders.Add(folder);
            WriteNewCollectionCsv(newName, "first,,First,첫번째", "second,,Second,두번째");
            var missing = ScanByName(newName);

            var creation = LocalizationBulkUpdateService.CreateCollection(missing);

            Assert.IsTrue(creation.Succeeded, creation.Summary);
            var created = LocalizationEditorSettings.GetStringTableCollection(newName);
            Assert.IsNotNull(created);
            Assert.AreEqual(newName, created.TableCollectionName);
            Assert.IsNotNull(created.SharedData);
            Assert.IsNotNull(created.GetTable(new LocaleIdentifier("en")) as StringTable);
            Assert.IsNotNull(created.GetTable(new LocaleIdentifier("ko-KR")) as StringTable);
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<StringTableCollection>(folder + "/" + newName + ".asset") != null);
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<SharedTableData>(folder + "/" + newName + " Shared Data.asset") != null);
            Assert.AreEqual(0, created.SharedData.Entries.Count, "생성만으로 CSV Key를 Import하면 안 됩니다.");

            var afterCreation = ScanByName(newName);
            Assert.IsFalse(afterCreation.IsNewLocalization);
            Assert.AreEqual(2, afterCreation.NewKeyCount, "재스캔은 CSV의 모든 Key를 신규로 보여야 합니다.");
            Assert.AreEqual(0, afterCreation.ChangedCount);
            Assert.IsNotNull(collection.SharedData.GetEntry("existing"), "기존 임시 Collection은 생성으로 변경되면 안 됩니다.");
        }

        [Test]
        public void CreateCollection_BlocksConflictInvalidCsvAndInvalidNameWithoutChanges()
        {
            string conflictName = NewCreationName();
            string conflictFolder = LocalizationBulkUpdateService.CollectionsRootAssetPath + "/" + conflictName;
            Assert.IsFalse(string.IsNullOrEmpty(AssetDatabase.CreateFolder(
                LocalizationBulkUpdateService.CollectionsRootAssetPath,
                conflictName)));
            creationAssetFolders.Add(conflictFolder);
            WriteNewCollectionCsv(conflictName, "first,,First,첫번째");

            var conflict = ScanByName(conflictName);
            Assert.IsFalse(conflict.CanCreateCollection);
            Assert.That(conflict.CreationPlan.Status, Does.Contain("대상 폴더"));

            string invalidCsvName = NewCreationName();
            WriteCsvWithHeader(invalidCsvName, "Key,Key,English(en)", "first,first,First");
            var invalidCsv = ScanByName(invalidCsvName);
            Assert.IsTrue(invalidCsv.IsNewLocalization);
            Assert.IsFalse(invalidCsv.IsValid);
            Assert.IsFalse(invalidCsv.CanCreateCollection);

            string invalidName = "new[invalid]" + Guid.NewGuid().ToString("N");
            WriteNewCollectionCsv(invalidName, "first,,First,첫번째");
            var invalidTableName = ScanByName(invalidName);
            Assert.IsTrue(invalidTableName.IsValid);
            Assert.IsFalse(invalidTableName.CanCreateCollection);
            Assert.That(invalidTableName.CreationPlan.Status, Does.Contain("'['와 ']'"));

            WriteCsv($"existing,{existing.Id},old english,기존 한국어");
            Assert.IsFalse(LocalizationBulkUpdateService.GetCollectionCreationPlan(ScanSingle()).CanCreate,
                "기존 Collection은 생성 후보가 될 수 없습니다.");
            Assert.IsNotNull(collection.SharedData.GetEntry("existing"));
        }

        [Test]
        public void UpdateSelected_MergesNewAndChangedValues_PreservesIdsGuidsAndMissingKeys()
        {
            string collectionGuidBefore = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(collection));
            long existingIdBefore = existing.Id;
            WriteCsv(
                $"existing,{existing.Id},new english,기존 한국어",
                "new-key,,new value,새 값");

            var table = ScanSingle();
            Assert.IsTrue(table.IsValid, table.Status);
            Assert.AreEqual(1, table.NewKeyCount);
            Assert.AreEqual(1, table.ChangedCount);
            Assert.AreEqual(1, table.DeletionDetectedCount);
            Assert.IsTrue(table.IsSelected);

            var update = LocalizationBulkUpdateService.UpdateSelected(new[] { table });

            Assert.IsTrue(update.Succeeded, update.Summary);
            Assert.AreEqual(existingIdBefore, collection.SharedData.GetEntry("existing").Id);
            Assert.AreEqual(collectionGuidBefore, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(collection)));
            Assert.AreEqual("new english", GetTable("en").GetEntry(existingIdBefore).LocalizedValue);
            Assert.AreEqual("기존 한국어", GetTable("ko-KR").GetEntry(existingIdBefore).LocalizedValue);

            var newEntry = collection.SharedData.GetEntry("new-key");
            Assert.IsNotNull(newEntry, "빈 Id의 신규 Key는 Unity Import가 ID를 생성해야 합니다.");
            Assert.AreEqual("new value", GetTable("en").GetEntry(newEntry.Id).LocalizedValue);
            Assert.AreEqual("새 값", GetTable("ko-KR").GetEntry(newEntry.Id).LocalizedValue);

            var preserved = collection.SharedData.GetEntry("asset-only");
            Assert.IsNotNull(preserved, "CSV에 없는 Key는 Merge 뒤에도 보존되어야 합니다.");
            Assert.AreEqual("preserve english", GetTable("en").GetEntry(preserved.Id).LocalizedValue);
        }

        [Test]
        public void Scan_InvalidDuplicateHeaderAndUnsupportedLocaleAreBlocked_WhileMissingCollectionIsCreationCandidate()
        {
            WriteCsv("duplicate,,A,가", "duplicate,,B,나");
            string badHeaderPath = Path.Combine(csvFolder, "bad-header.csv");
            string unsupportedLocalePath = Path.Combine(csvFolder, "unsupported-locale.csv");
            string missingCollectionPath = Path.Combine(csvFolder, "missing-collection.csv");
            File.WriteAllText(badHeaderPath, "Key,Key,English(en)\nkey,key,hello");
            File.WriteAllText(unsupportedLocalePath, "Key,Id,French(fr)\nkey,,bonjour");
            File.WriteAllText(missingCollectionPath, Header + "\nkey,,hello,안녕");

            var scan = LocalizationBulkUpdateService.ScanDirectory(
                csvFolder,
                name => name == collectionName ? collection : null);

            Assert.IsFalse(scan.Tables.Single(table => table.TableName == collectionName).IsValid, "중복 Key는 적용을 막아야 합니다.");
            Assert.IsFalse(scan.Tables.Single(table => table.TableName == "bad-header").IsValid, "잘못된 헤더는 적용을 막아야 합니다.");
            Assert.IsFalse(scan.Tables.Single(table => table.TableName == "unsupported-locale").IsValid, "지원하지 않는 locale 열은 적용을 막아야 합니다.");
            var missingCollection = scan.Tables.Single(table => table.TableName == "missing-collection");
            Assert.IsTrue(missingCollection.IsValid, missingCollection.Status);
            Assert.IsTrue(missingCollection.IsNewLocalization, "Collection 부재는 신규 로컬라이즈 생성 후보여야 합니다.");
        }

        [Test]
        public void UpdateSelected_DoesNotChangeUncheckedTable()
        {
            WriteCsv($"existing,{existing.Id},would change,기존 한국어");
            var table = ScanSingle();
            table.IsSelected = false;

            var update = LocalizationBulkUpdateService.UpdateSelected(new[] { table });

            Assert.IsFalse(update.Succeeded);
            Assert.AreEqual("old english", GetTable("en").GetEntry(existing.Id).LocalizedValue);
        }

        [Test]
        public void ShouldWarnForDeletion_UsesOnlySelectedValidTables()
        {
            var selectedDeletion = new LocalizationBulkUpdateService.TableResult
            {
                IsSelected = true,
                DeletionDetectedCount = 1,
            };
            var uncheckedDeletion = new LocalizationBulkUpdateService.TableResult
            {
                IsSelected = false,
                DeletionDetectedCount = 3,
            };
            var invalidDeletion = new LocalizationBulkUpdateService.TableResult
            {
                IsSelected = true,
                DeletionDetectedCount = 2,
            };
            invalidDeletion.Errors.Add("validation error");

            Assert.IsTrue(LocalizationBulkUpdateService.ShouldWarnForDeletion(new[] { selectedDeletion, uncheckedDeletion }));
            Assert.IsFalse(LocalizationBulkUpdateService.ShouldWarnForDeletion(new[] { uncheckedDeletion, invalidDeletion }));
            Assert.IsFalse(LocalizationBulkUpdateService.ShouldWarnForDeletion(new[]
            {
                new LocalizationBulkUpdateService.TableResult { IsSelected = true, DeletionDetectedCount = 0 },
            }));
        }

        [Test]
        public void UpdateSelected_RequiresRescanWhenCsvChangedAfterScan()
        {
            WriteCsv($"existing,{existing.Id},first change,기존 한국어");
            var table = ScanSingle();
            WriteCsv($"existing,{existing.Id},second change,기존 한국어");

            var update = LocalizationBulkUpdateService.UpdateSelected(new[] { table });

            Assert.IsFalse(update.Succeeded);
            Assert.That(update.Summary, Does.Contain("다시 실행"));
            Assert.AreEqual("old english", GetTable("en").GetEntry(existing.Id).LocalizedValue);
        }

        private LocalizationBulkUpdateService.TableResult ScanSingle()
        {
            var scan = LocalizationBulkUpdateService.ScanDirectory(
                csvFolder,
                name => name == collectionName ? collection : null);
            return scan.Tables.Single(table => table.TableName == collectionName);
        }

        private LocalizationBulkUpdateService.TableResult ScanByName(string tableName)
        {
            var scan = LocalizationBulkUpdateService.ScanDirectory(
                csvFolder,
                name => name == collectionName ? collection : LocalizationEditorSettings.GetStringTableCollection(name));
            return scan.Tables.Single(table => table.TableName == tableName);
        }

        private StringTable GetTable(string localeCode)
        {
            return collection.GetTable(new LocaleIdentifier(localeCode)) as StringTable;
        }

        private void WriteCsv(params string[] rows)
        {
            File.WriteAllText(csvPath, Header + "\n" + string.Join("\n", rows));
        }

        private void WriteNewCollectionCsv(string tableName, params string[] rows)
        {
            File.WriteAllText(Path.Combine(csvFolder, tableName + ".csv"), Header + "\n" + string.Join("\n", rows));
        }

        private void WriteCsvWithHeader(string tableName, string header, params string[] rows)
        {
            File.WriteAllText(Path.Combine(csvFolder, tableName + ".csv"), header + "\n" + string.Join("\n", rows));
        }

        private static string NewCreationName()
        {
            return "__LocalizationBulkUpdateCreateTest_" + Guid.NewGuid().ToString("N");
        }
    }
}
