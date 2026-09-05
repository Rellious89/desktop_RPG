using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TableSyncEditor
{
    [Serializable] public sealed class TableSyncManifest { public List<TableSyncManifestTable> Tables = new List<TableSyncManifestTable>(); }
    [Serializable] public sealed class TableSyncManifestTable { public string RelativePath; public bool FileDeleted; public List<TableSyncManifestChange> Changes = new List<TableSyncManifestChange>(); }
    [Serializable] public sealed class TableSyncManifestChange { public string Kind; public List<TableSyncManifestValue> Identity = new List<TableSyncManifestValue>(); public List<TableSyncManifestValue> Values = new List<TableSyncManifestValue>(); public List<TableSyncManifestValue> ChangedValues = new List<TableSyncManifestValue>(); }
    [Serializable] public sealed class TableSyncManifestValue { public string Column; public string Value; }
    public sealed class TableSyncVerificationResult { public readonly List<string> Errors = new List<string>(); public readonly List<string> Warnings = new List<string>(); public string Status => Errors.Count > 0 ? "ERROR" : Warnings.Count > 0 ? "WARNING" : "PASS"; }

    /// <summary>Working Tree 제안을 임시 패키지로 보관하고, 이후 재반영된 CSV만 검증한다.</summary>
    public static class TableSyncSyncPackage
    {
        public static string Root => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "TableSyncStaging");
        private static string ManifestPath => Path.Combine(Root, "sync-manifest.json");

        public static bool Create(TableSyncProjectScanResult scan, out string error)
        {
            error = null;
            if (scan == null || !scan.IsValid) { error = "유효한 Scan 결과가 없습니다."; return false; }
            List<string> collisions = FindFileNameCollisions(scan.Tables);
            if (collisions.Count > 0)
            {
                error = "동일 파일명 충돌로 Flat Staging을 만들 수 없습니다: " + string.Join(", ", collisions);
                return false;
            }
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
                Directory.CreateDirectory(Root);
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                var manifest = new TableSyncManifest();
                foreach (TableSyncProjectTableChange table in scan.Tables)
                {
                    if (table.Diff == null || !table.Diff.IsValid) continue;
                    manifest.Tables.Add(ToManifestTable(table));
                    if (table.FileChangeKind == TableSyncGitChangeKind.Deleted) continue;
                    string source = Path.Combine(projectRoot, table.RelativePath);
                    string target = Path.Combine(Root, Path.GetFileName(table.RelativePath));
                    File.Copy(source, target, true);
                }
                File.WriteAllText(ManifestPath, JsonUtility.ToJson(manifest, true));
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        public static bool Exists => File.Exists(ManifestPath);
        public static void OpenFolder() { if (Directory.Exists(Root)) EditorUtility.RevealInFinder(Root); }
        public static bool Delete(out string error)
        {
            error = null;
            try
            {
                if (!Directory.Exists(Root)) return true;
                foreach (string file in Directory.GetFiles(Root)) File.Delete(file);
                foreach (string directory in Directory.GetDirectories(Root)) Directory.Delete(directory, true);
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }
        public static TableSyncVerificationResult Verify()
        {
            var result = new TableSyncVerificationResult();
            if (!Exists) { result.Errors.Add("Sync Package manifest가 없습니다. 먼저 Create Sync Package를 실행하세요."); return result; }
            TableSyncManifest manifest;
            try { manifest = JsonUtility.FromJson<TableSyncManifest>(File.ReadAllText(ManifestPath)); }
            catch (Exception e) { result.Errors.Add("manifest를 읽지 못했습니다: " + e.Message); return result; }
            if (manifest == null || manifest.Tables == null) { result.Errors.Add("manifest 형식이 올바르지 않습니다."); return result; }
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            foreach (TableSyncManifestTable table in manifest.Tables) VerifyTable(projectRoot, table, result);
            return result;
        }

        private static TableSyncManifestTable ToManifestTable(TableSyncProjectTableChange table)
        {
            var item = new TableSyncManifestTable { RelativePath = table.RelativePath, FileDeleted = table.FileChangeKind == TableSyncGitChangeKind.Deleted };
            foreach (TableSyncRowChange row in table.Diff.Changes.Where(change => change.Kind != TableSyncChangeKind.Unchanged))
            {
                var change = new TableSyncManifestChange { Kind = row.Kind.ToString() };
                for (int i = 0; i < row.Identity.Columns.Count; i++) change.Identity.Add(new TableSyncManifestValue { Column = row.Identity.Columns[i], Value = row.Identity.Values[i] });
                foreach (TableSyncCellValue value in row.RowValues) change.Values.Add(new TableSyncManifestValue { Column = value.Column, Value = value.Value });
                foreach (TableSyncCellChange value in row.CellChanges) change.ChangedValues.Add(new TableSyncManifestValue { Column = value.Column, Value = value.ModifiedValue });
                item.Changes.Add(change);
            }
            return item;
        }

        private static void VerifyTable(string root, TableSyncManifestTable manifest, TableSyncVerificationResult result)
        {
            string currentPath = Path.Combine(root, manifest.RelativePath);
            if (manifest.FileDeleted) { if (File.Exists(currentPath)) result.Errors.Add(manifest.RelativePath + ": CSV file DELETE 미반영"); return; }
            string stagedPath = Path.Combine(Root, Path.GetFileName(manifest.RelativePath));
            if (!TableSyncCsvReader.TryReadFile(currentPath, out TableSyncTable current, out TableSyncDiagnostic currentError)) { result.Errors.Add(manifest.RelativePath + ": " + currentError.Message); return; }
            if (!TableSyncCsvReader.TryReadFile(stagedPath, out TableSyncTable staged, out TableSyncDiagnostic stagedError)) { result.Errors.Add(manifest.RelativePath + ": staged CSV " + stagedError.Message); return; }
            if (!TableSyncKeyBuddyTableMap.TryGetPrimaryKeyColumns(manifest.RelativePath, out string[] keys)) { result.Errors.Add(manifest.RelativePath + ": PK mapping 없음"); return; }
            foreach (TableSyncManifestChange expected in manifest.Changes) VerifyExpected(manifest.RelativePath, current, expected, result);
            TableSyncDiffResult differences = TableSyncDiffEngine.Compare(staged, current, keys);
            if (!differences.IsValid) { result.Errors.Add(manifest.RelativePath + ": " + differences.Diagnostics[0].Message); return; }
            foreach (TableSyncRowChange change in differences.Changes.Where(change => change.Kind != TableSyncChangeKind.Unchanged)) ReportAdditional(manifest.RelativePath, manifest.Changes, change, result);
        }

        private static void VerifyExpected(string table, TableSyncTable current, TableSyncManifestChange expected, TableSyncVerificationResult result)
        {
            TableDataEditor.CsvRecord row = Find(current, expected.Identity);
            if (expected.Kind == TableSyncChangeKind.PossibleDelete.ToString()) { if (row != null) result.Errors.Add(table + ": DELETE 미반영 " + Describe(expected)); return; }
            if (row == null) { result.Errors.Add(table + ": " + expected.Kind + " 누락 " + Describe(expected)); return; }
            IEnumerable<TableSyncManifestValue> values = expected.Kind == TableSyncChangeKind.Add.ToString() ? expected.Values : expected.ChangedValues;
            foreach (TableSyncManifestValue value in values)
            {
                int index = Array.IndexOf(current.Header, value.Column);
                if (index < 0 || !string.Equals(row.Fields[index] ?? string.Empty, value.Value ?? string.Empty, StringComparison.Ordinal)) result.Errors.Add(table + ": 값 불일치 " + Describe(expected) + " / " + value.Column);
            }
        }

        private static void ReportAdditional(string table, List<TableSyncManifestChange> expected, TableSyncRowChange actual, TableSyncVerificationResult result)
        {
            TableSyncManifestChange matched = expected.FirstOrDefault(change => SameIdentity(change, actual.Identity));
            if (matched == null) { result.Warnings.Add(table + ": 사용자 추가 변경 " + actual.PrimaryKey); return; }
            if (matched.Kind != TableSyncChangeKind.Update.ToString()) return;
            foreach (TableSyncCellChange cell in actual.CellChanges) if (!matched.ChangedValues.Any(value => value.Column == cell.Column)) result.Warnings.Add(table + ": 사용자 추가 Cell 변경 " + actual.PrimaryKey + " / " + cell.Column);
        }

        private static TableDataEditor.CsvRecord Find(TableSyncTable table, List<TableSyncManifestValue> identity)
        {
            foreach (TableDataEditor.CsvRecord row in table.Records) if (identity.All(value => { int i = Array.IndexOf(table.Header, value.Column); return i >= 0 && (row.Fields[i] ?? string.Empty) == (value.Value ?? string.Empty); })) return row;
            return null;
        }
        private static bool SameIdentity(TableSyncManifestChange expected, TableSyncRowIdentity actual)
        {
            if (expected.Identity.Count != actual.Columns.Count) return false;
            foreach (TableSyncManifestValue value in expected.Identity) { int i = actual.Columns.IndexOf(value.Column); if (i < 0 || actual.Values[i] != value.Value) return false; }
            return true;
        }
        private static List<string> FindFileNameCollisions(IEnumerable<TableSyncProjectTableChange> tables)
        {
            return tables
                .Where(table => table.Diff != null && table.Diff.IsValid && table.FileChangeKind != TableSyncGitChangeKind.Deleted)
                .GroupBy(table => Path.GetFileName(table.RelativePath), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        private static string Describe(TableSyncManifestChange change) => string.Join(" / ", change.Identity.Select(value => value.Value));
    }
}
