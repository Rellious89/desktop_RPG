using NUnit.Framework;

namespace TableSyncEditor.Tests
{
    public sealed class TableSyncDiffEngineTests
    {
        [Test]
        public void IdenticalTables_AreUnchanged()
        {
            TableSyncDiffResult result = Compare("skill_id,cooldown,enabled\nfire,10,1\n", "skill_id,cooldown,enabled\nfire,10,1\n");

            Assert.IsTrue(result.IsValid, Describe(result));
            Assert.AreEqual(1, result.UnchangedCount);
            Assert.AreEqual(0, result.AddCount + result.UpdateCount + result.PossibleDeleteCount);
        }

        [Test]
        public void NewRow_IsAdd()
        {
            TableSyncDiffResult result = Compare("skill_id,cooldown\nfire,10\n", "skill_id,cooldown\nfire,10\nice,8\n");

            Assert.AreEqual(1, result.AddCount, Describe(result));
            Assert.AreEqual("ice", result.Changes.Find(change => change.Kind == TableSyncChangeKind.Add).PrimaryKey);
            Assert.AreEqual("8", result.Changes.Find(change => change.Kind == TableSyncChangeKind.Add).RowValues[1].Value);
        }

        [Test]
        public void SingleCellChange_IsUpdateWithValues()
        {
            TableSyncDiffResult result = Compare("skill_id,cooldown\nfire,10\n", "skill_id,cooldown\nfire,8\n");

            Assert.AreEqual(1, result.UpdateCount, Describe(result));
            TableSyncCellChange change = result.Changes.Find(row => row.Kind == TableSyncChangeKind.Update).CellChanges[0];
            Assert.AreEqual("cooldown", change.Column);
            Assert.AreEqual("10", change.MasterValue);
            Assert.AreEqual("8", change.ModifiedValue);
            Assert.AreEqual("fire", result.Changes.Find(row => row.Kind == TableSyncChangeKind.Update).RowValues[0].Value);
        }

        [Test]
        public void MissingMasterRow_IsPossibleDelete()
        {
            TableSyncDiffResult result = Compare("skill_id,cooldown\nfire,10\nice,8\n", "skill_id,cooldown\nfire,10\n");

            Assert.AreEqual(1, result.PossibleDeleteCount, Describe(result));
            Assert.AreEqual("ice", result.Changes.Find(change => change.Kind == TableSyncChangeKind.PossibleDelete).PrimaryKey);
        }

        [Test]
        public void MultipleCellAndRowChanges_AreAllClassified()
        {
            TableSyncDiffResult result = Compare("id,a,b\none,1,2\ntwo,3,4\nthree,5,6\n", "id,b,a\none,8,7\ntwo,4,3\nfour,9,10\n", "id");

            Assert.IsTrue(result.IsValid, Describe(result));
            Assert.AreEqual(1, result.AddCount);
            Assert.AreEqual(1, result.UpdateCount);
            Assert.AreEqual(1, result.PossibleDeleteCount);
            Assert.AreEqual(1, result.UnchangedCount);
            Assert.AreEqual(2, result.Changes.Find(change => change.PrimaryKey == "one").CellChanges.Count);
        }

        [Test]
        public void DuplicatePrimaryKey_BlocksDiff()
        {
            TableSyncDiffResult result = Compare("id,value\none,a\none,b\n", "id,value\none,a\n", "id");

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("중복", Describe(result));
            Assert.AreEqual(0, result.Changes.Count);
        }

        [Test]
        public void MissingPrimaryKeyColumn_BlocksDiff()
        {
            TableSyncDiffResult result = Compare("id,value\none,a\n", "id,value\none,a\n", "missing");

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("Primary Key 컬럼이 없습니다", Describe(result));
        }

        [Test]
        public void EmptyPrimaryKey_BlocksDiff()
        {
            TableSyncDiffResult result = Compare("id,value\n,a\n", "id,value\n,a\n", "id");

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("비어 있습니다", Describe(result));
        }

        [Test]
        public void HeaderOrSchemaMismatch_BlocksDiff()
        {
            TableSyncDiffResult result = Compare("id,value\none,a\n", "id,other\none,a\n", "id");

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("없는", Describe(result));
        }

        [Test]
        public void MissingHeader_IsReportedBeforeComparison()
        {
            bool read = TableSyncCsvReader.TryReadText("MASTER.csv", string.Empty, out TableSyncTable _, out TableSyncDiagnostic diagnostic);

            Assert.IsFalse(read);
            StringAssert.Contains("Header가 없습니다", diagnostic.Message);
        }

        [Test]
        public void QuotedComma_UsesExistingCsvParserAndComparesCorrectly()
        {
            TableSyncDiffResult result = Compare("id,memo\none,\"hello, world\"\n", "memo,id\n\"goodbye, world\",one\n", "id");

            Assert.IsTrue(result.IsValid, Describe(result));
            Assert.AreEqual(1, result.UpdateCount);
            TableSyncCellChange change = result.Changes[0].CellChanges[0];
            Assert.AreEqual("hello, world", change.MasterValue);
            Assert.AreEqual("goodbye, world", change.ModifiedValue);
        }

        private static TableSyncDiffResult Compare(string masterText, string modifiedText, string key = "skill_id")
        {
            Assert.IsTrue(TableSyncCsvReader.TryReadText("MASTER.csv", masterText, out TableSyncTable master, out TableSyncDiagnostic masterError), masterError?.ToString());
            Assert.IsTrue(TableSyncCsvReader.TryReadText("MODIFIED.csv", modifiedText, out TableSyncTable modified, out TableSyncDiagnostic modifiedError), modifiedError?.ToString());
            return TableSyncDiffEngine.Compare(master, modified, key);
        }

        private static string Describe(TableSyncDiffResult result)
        {
            return string.Join("\n", result.Diagnostics);
        }
    }
}
