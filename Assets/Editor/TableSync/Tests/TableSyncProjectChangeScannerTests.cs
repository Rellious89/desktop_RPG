using System.Collections.Generic;
using NUnit.Framework;

namespace TableSyncEditor.Tests
{
    public sealed class TableSyncProjectChangeScannerTests
    {
        [Test]
        public void ScansSingleKeyAddUpdateAndDelete()
        {
            var git = Fake.Modified("Assets/TableData/Game/Skill.csv", "skill_id,value\na,1\nb,2\n", "skill_id,value\na,3\nc,4\n");
            TableSyncProjectScanResult result = TableSyncProjectChangeScanner.Scan(git);
            Assert.IsTrue(result.IsValid, Describe(result)); Assert.AreEqual(1, result.AddCount); Assert.AreEqual(1, result.UpdateCount); Assert.AreEqual(1, result.DeleteCount);
        }

        [Test]
        public void ScansCompositeKeyWithoutDelimiterCollision()
        {
            var git = Fake.Modified("Assets/TableData/Game/CharacterSkill.csv", "character_id,skill_id,value\na/b,c,1\na,b/c,2\n", "character_id,skill_id,value\na/b,c,3\na,b/c,2\n");
            TableSyncProjectScanResult result = TableSyncProjectChangeScanner.Scan(git);
            Assert.IsTrue(result.IsValid, Describe(result)); Assert.AreEqual(1, result.UpdateCount); Assert.AreEqual(1, result.Tables[0].Diff.UnchangedCount);
        }

        [Test]
        public void ScansCompositeKeyAddAndDelete()
        {
            var git = Fake.Modified("Assets/TableData/Game/ShopProduct.csv", "shop_id,item_id,buy_currency_id,buy_price,display_order,enabled,memo\nshop,a,jewel,1,1,1,\nshop,b,jewel,1,2,1,\n", "shop_id,item_id,buy_currency_id,buy_price,display_order,enabled,memo\nshop,a,jewel,1,1,1,\nshop,c,jewel,1,3,1,\n");
            TableSyncProjectScanResult result = TableSyncProjectChangeScanner.Scan(git);
            Assert.IsTrue(result.IsValid, Describe(result)); Assert.AreEqual(1, result.AddCount); Assert.AreEqual(1, result.DeleteCount);
        }

        [Test]
        public void NewCsvTreatsEveryRowAsAdd()
        {
            var git = Fake.Added("Assets/TableData/Game/Skill.csv", "skill_id,value\na,1\nb,2\n");
            TableSyncProjectScanResult result = TableSyncProjectChangeScanner.Scan(git);
            Assert.IsTrue(result.IsValid, Describe(result)); Assert.AreEqual(2, result.AddCount);
        }

        [Test]
        public void DeletedCsvKeepsDeleteIdentitiesForTheManifest()
        {
            var git = new Fake(); git.Changes.Add(new TableSyncGitFileChange("Assets/TableData/Game/Skill.csv", TableSyncGitChangeKind.Deleted));
            git.Head["Assets/TableData/Game/Skill.csv"] = "skill_id,value\na,1\nb,2\n";
            TableSyncProjectScanResult result = TableSyncProjectChangeScanner.Scan(git);
            Assert.IsTrue(result.IsValid, Describe(result)); Assert.AreEqual(2, result.DeleteCount); StringAssert.Contains("CSV file deleted", result.Tables[0].FileDeletionMessage);
        }

        [Test]
        public void ScansMultipleTablesAndLocalization()
        {
            var git = Fake.Modified("Assets/TableData/Game/Skill.csv", "skill_id,value\na,1\n", "skill_id,value\na,2\n");
            git.Changes.Add(new TableSyncGitFileChange("TableData/Localization/10_Skill.csv", TableSyncGitChangeKind.Modified));
            git.Head["TableData/Localization/10_Skill.csv"] = "Key,English(en)\n1,Old\n";
            git.Working["TableData/Localization/10_Skill.csv"] = "Key,English(en)\n1,New\n2,Added\n";
            TableSyncProjectScanResult result = TableSyncProjectChangeScanner.Scan(git);
            Assert.IsTrue(result.IsValid, Describe(result)); Assert.AreEqual(2, result.Tables.Count); Assert.AreEqual(1, result.AddCount); Assert.AreEqual(2, result.UpdateCount);
        }

        [Test]
        public void QuotedCommaAndNewlineUseExistingParser()
        {
            var git = Fake.Modified("Assets/TableData/Game/Skill.csv", "skill_id,memo\na,\"old, value\"\n", "skill_id,memo\na,\"new\nvalue\"\n");
            TableSyncProjectScanResult result = TableSyncProjectChangeScanner.Scan(git);
            Assert.IsTrue(result.IsValid, Describe(result)); Assert.AreEqual(1, result.UpdateCount);
        }

        [Test]
        public void GitErrorsBlockScanClearly()
        {
            var noRepository = new Fake { RootOk = false, Error = "not a git repository" };
            TableSyncProjectScanResult result = TableSyncProjectChangeScanner.Scan(noRepository);
            Assert.IsFalse(result.IsValid); StringAssert.Contains("not a git repository", Describe(result));
            var noHead = new Fake { HeadOk = false, Error = "HEAD is missing" };
            result = TableSyncProjectChangeScanner.Scan(noHead);
            Assert.IsFalse(result.IsValid); StringAssert.Contains("HEAD is missing", Describe(result));
        }

        [Test]
        public void NoChangesReturnsAnEmptyValidScan()
        {
            TableSyncProjectScanResult result = TableSyncProjectChangeScanner.Scan(new Fake());
            Assert.IsTrue(result.IsValid, Describe(result)); Assert.AreEqual(0, result.Tables.Count);
        }

        [Test]
        public void HeadReadFailureBlocksOnlyThatTable()
        {
            var git = new Fake(); git.Changes.Add(new TableSyncGitFileChange("Assets/TableData/Game/Skill.csv", TableSyncGitChangeKind.Modified)); git.Working["Assets/TableData/Game/Skill.csv"] = "skill_id,value\na,1\n";
            TableSyncProjectScanResult result = TableSyncProjectChangeScanner.Scan(git);
            Assert.IsFalse(result.IsValid); StringAssert.Contains("git failure", Describe(result));
        }

        private static string Describe(TableSyncProjectScanResult result) => string.Join("\n", result.Diagnostics);

        private sealed class Fake : ITableSyncGitReader
        {
            public readonly List<TableSyncGitFileChange> Changes = new List<TableSyncGitFileChange>();
            public readonly Dictionary<string, string> Head = new Dictionary<string, string>();
            public readonly Dictionary<string, string> Working = new Dictionary<string, string>();
            public bool RootOk = true; public bool HeadOk = true; public string Error = "git failure";
            public bool TryGetRepositoryRoot(out string root, out string error) { root = "/fake"; error = RootOk ? null : Error; return RootOk; }
            public bool TryEnsureHead(out string error) { error = HeadOk ? null : Error; return HeadOk; }
            public bool TryGetChanges(string root, out List<TableSyncGitFileChange> changes, out string error) { changes = Changes; error = null; return true; }
            public bool TryReadHeadFile(string root, string path, out string text, out string error) { bool ok = Head.TryGetValue(path, out text); error = ok ? null : Error; return ok; }
            public bool TryReadWorkingFile(string root, string path, out string text, out string error) { bool ok = Working.TryGetValue(path, out text); error = ok ? null : Error; return ok; }
            public static Fake Modified(string path, string head, string working) { var fake = new Fake(); fake.Changes.Add(new TableSyncGitFileChange(path, TableSyncGitChangeKind.Modified)); fake.Head[path] = head; fake.Working[path] = working; return fake; }
            public static Fake Added(string path, string working) { var fake = new Fake(); fake.Changes.Add(new TableSyncGitFileChange(path, TableSyncGitChangeKind.Added)); fake.Working[path] = working; return fake; }
        }
    }
}
