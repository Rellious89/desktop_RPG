using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TableSyncEditor
{
    /// <summary>기본은 HEAD → Working Tree 변경 스캔이며, 수동 CSV 비교는 보조 Audit으로만 둔다.</summary>
    internal sealed class TableSyncWindow : EditorWindow
    {
        private const string MenuPath = "Tools/Keybuddy/Table Sync Tool";
        private TableSyncProjectScanResult scanResult;
        private TableSyncVerificationResult verificationResult;
        private string status = "Scan Project Changes를 눌러 이번 작업에서 바뀐 Table CSV를 확인하세요.";
        private Vector2 scroll;
        private readonly Dictionary<TableSyncProjectTableChange, bool> expandedTables = new Dictionary<TableSyncProjectTableChange, bool>();
        private readonly Dictionary<TableSyncProjectTableChange, Vector2> tableScrollPositions = new Dictionary<TableSyncProjectTableChange, Vector2>();
        private bool showManualCompare;
        private string masterPath = string.Empty;
        private string modifiedPath = string.Empty;
        private string primaryKey = string.Empty;
        private TableSyncDiffResult manualResult;

        [MenuItem(MenuPath, priority = 120)]
        private static void Open()
        {
            TableSyncWindow window = GetWindow<TableSyncWindow>();
            window.titleContent = new GUIContent("Table Sync Tool");
            window.minSize = new Vector2(760, 400);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("KeyBuddy Table Sync Tool", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Git HEAD와 현재 Working Tree를 읽기 전용으로 비교합니다. CSV와 Google Sheet를 수정하지 않습니다.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan Project Changes", GUILayout.Width(180))) ScanProjectChanges();
                using (new EditorGUI.DisabledScope(scanResult == null || !scanResult.IsValid || scanResult.Tables.Count == 0))
                    if (GUILayout.Button("Create Sync Package", GUILayout.Width(160))) CreateSyncPackage();
                using (new EditorGUI.DisabledScope(!TableSyncSyncPackage.Exists))
                    if (GUILayout.Button("Open Sync Package Folder", GUILayout.Width(175))) TableSyncSyncPackage.OpenFolder();
                using (new EditorGUI.DisabledScope(!TableSyncSyncPackage.Exists))
                    if (GUILayout.Button("Verify Synced Tables", GUILayout.Width(155))) VerifySyncedTables();
                using (new EditorGUI.DisabledScope(!TableSyncSyncPackage.Exists))
                    if (GUILayout.Button("Delete Sync Package", GUILayout.Width(150))) DeleteSyncPackage();
            }
            EditorGUILayout.HelpBox(status, scanResult != null && !scanResult.IsValid ? MessageType.Error : MessageType.Info);
            DrawVerification();
            DrawProjectScan();
            EditorGUILayout.Space();
            showManualCompare = EditorGUILayout.Foldout(showManualCompare, "Advanced / Manual Compare", true);
            if (showManualCompare) DrawManualCompare();
        }

        private void ScanProjectChanges()
        {
            expandedTables.Clear(); tableScrollPositions.Clear();
            scanResult = TableSyncProjectChangeScanner.Scan(new TableSyncGitCli());
            status = scanResult.IsValid
                ? $"Changed Tables: {scanResult.Tables.Count}    ADD: {scanResult.AddCount}    UPDATE: {scanResult.UpdateCount}    DELETE: {scanResult.DeleteCount}"
                : "Git 기준 데이터를 읽지 못했습니다. 오류를 확인하세요.";
        }

        private void CreateSyncPackage()
        {
            verificationResult = null;
            status = TableSyncSyncPackage.Create(scanResult, out string error)
                ? "Sync Package를 생성했습니다. 원본 CSV는 수정하지 않았습니다."
                : "Sync Package 생성 실패: " + error;
        }

        private void VerifySyncedTables()
        {
            verificationResult = TableSyncSyncPackage.Verify();
            status = "Verify Synced Tables: " + verificationResult.Status;
        }

        private void DeleteSyncPackage()
        {
            string folder = TableSyncSyncPackage.Root;
            if (!EditorUtility.DisplayDialog(
                    "Delete Sync Package",
                    "Staging 폴더는 유지하고 내부의 CSV와 manifest만 삭제합니다.\n\n" + folder + "\n\nProject CSV와 Google Sheet는 수정하지 않습니다.",
                    "Clear Package Files", "Cancel")) return;
            verificationResult = null;
            status = TableSyncSyncPackage.Delete(out string error)
                ? "Sync Package 내부 파일을 삭제했습니다. Staging 폴더와 Project CSV는 유지했습니다."
                : "Sync Package 삭제 실패: " + error;
        }

        private void DrawVerification()
        {
            if (verificationResult == null) return;
            MessageType type = verificationResult.Status == "PASS" ? MessageType.Info : verificationResult.Status == "WARNING" ? MessageType.Warning : MessageType.Error;
            EditorGUILayout.HelpBox("Verification: " + verificationResult.Status, type);
            foreach (string error in verificationResult.Errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            foreach (string warning in verificationResult.Warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
        }

        private void DrawProjectScan()
        {
            if (scanResult == null) return;
            if (!scanResult.IsValid)
            {
                foreach (TableSyncDiagnostic diagnostic in scanResult.Diagnostics) EditorGUILayout.HelpBox(diagnostic.ToString(), MessageType.Error);
                return;
            }
            if (scanResult.Tables.Count == 0) { EditorGUILayout.LabelField("변경된 KeyBuddy Table CSV가 없습니다.", EditorStyles.wordWrappedMiniLabel); return; }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (TableSyncProjectTableChange table in scanResult.Tables) DrawProjectTable(table);
            EditorGUILayout.EndScrollView();
        }

        private void DrawProjectTable(TableSyncProjectTableChange table)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                expandedTables.TryGetValue(table, out bool expanded);
                string summary = table.Diff == null ? "CSV file deleted" : $"ADD {table.Diff.AddCount}   UPDATE {table.Diff.UpdateCount}   DELETE {table.Diff.PossibleDeleteCount}";
                expanded = EditorGUILayout.Foldout(expanded, $"{Path.GetFileName(table.RelativePath)}  —  {summary}", true);
                expandedTables[table] = expanded;
                if (!expanded) return;
                EditorGUILayout.LabelField(table.RelativePath, EditorStyles.miniLabel);
                if (table.FileChangeKind == TableSyncGitChangeKind.Deleted) EditorGUILayout.HelpBox(table.FileDeletionMessage, MessageType.Warning);
                if (table.Diff == null) return;
                if (!table.Diff.IsValid) { foreach (TableSyncDiagnostic diagnostic in table.Diff.Diagnostics) EditorGUILayout.HelpBox(diagnostic.ToString(), MessageType.Error); return; }
                DrawSpreadsheet(table);
            }
        }

        private void DrawSpreadsheet(TableSyncProjectTableChange table)
        {
            const float StatusWidth = 82f;
            const float CellWidth = 150f;
            TableSyncTable schema = table.DisplayTable;
            tableScrollPositions.TryGetValue(table, out Vector2 position);
            position = EditorGUILayout.BeginScrollView(position, GUILayout.Height(180));
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Status", GUILayout.Width(StatusWidth));
                foreach (string header in schema.Header) GUILayout.Label(header, GUILayout.Width(CellWidth));
            }

            foreach (TableSyncRowChange row in table.Diff.Changes)
            {
                if (row.Kind == TableSyncChangeKind.Unchanged) continue;
                var changedColumns = new HashSet<string>();
                foreach (TableSyncCellChange cell in row.CellChanges) changedColumns.Add(cell.Column);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(DisplayKind(row.Kind), GUILayout.Width(StatusWidth));
                    foreach (TableSyncCellValue value in row.RowValues)
                    {
                        Color previous = GUI.backgroundColor;
                        if (changedColumns.Contains(value.Column)) GUI.backgroundColor = new Color(1f, 0.78f, 0.30f);
                        string tooltip = row.Kind == TableSyncChangeKind.Update && changedColumns.Contains(value.Column)
                            ? FindPreviousValue(row, value.Column) + " → " + value.Value + "\nClick to copy new value"
                            : "Click to copy value";
                        if (GUILayout.Button(new GUIContent(value.Value, tooltip), EditorStyles.miniButton, GUILayout.Width(CellWidth)))
                            EditorGUIUtility.systemCopyBuffer = value.Value;
                        GUI.backgroundColor = previous;
                    }
                }
            }
            EditorGUILayout.EndScrollView();
            tableScrollPositions[table] = position;
            EditorGUILayout.LabelField("Cell을 클릭하면 해당 현재 값을 Clipboard에 복사합니다. 행/일괄 복사는 제공하지 않습니다.", EditorStyles.miniLabel);
        }

        private static string FindPreviousValue(TableSyncRowChange row, string column)
        {
            foreach (TableSyncCellChange change in row.CellChanges)
                if (change.Column == column) return change.MasterValue;
            return string.Empty;
        }

        private void DrawManualCompare()
        {
            DrawFileField("MASTER CSV", ref masterPath); DrawFileField("MODIFIED CSV", ref modifiedPath);
            primaryKey = EditorGUILayout.TextField("Primary Key Column", primaryKey);
            if (GUILayout.Button("Compare Selected CSV", GUILayout.Width(180))) CompareManual();
            if (manualResult == null) return;
            if (!manualResult.IsValid) { foreach (TableSyncDiagnostic diagnostic in manualResult.Diagnostics) EditorGUILayout.HelpBox(diagnostic.ToString(), MessageType.Error); return; }
            EditorGUILayout.LabelField($"ADD: {manualResult.AddCount}    UPDATE: {manualResult.UpdateCount}    POSSIBLE DELETE: {manualResult.PossibleDeleteCount}    UNCHANGED: {manualResult.UnchangedCount}");
        }

        private static void DrawFileField(string label, ref string path)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(120)); EditorGUILayout.SelectableLabel(path, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Select…", GUILayout.Width(70))) { string selected = EditorUtility.OpenFilePanel(label, string.IsNullOrEmpty(path) ? Application.dataPath : Path.GetDirectoryName(path), "csv"); if (!string.IsNullOrEmpty(selected)) path = selected; }
            }
        }

        private void CompareManual()
        {
            if (!TableSyncCsvReader.TryReadFile(masterPath, out TableSyncTable master, out TableSyncDiagnostic masterError)) manualResult = ResultWith(masterError);
            else if (!TableSyncCsvReader.TryReadFile(modifiedPath, out TableSyncTable modified, out TableSyncDiagnostic modifiedError)) manualResult = ResultWith(modifiedError);
            else manualResult = TableSyncDiffEngine.Compare(master, modified, primaryKey);
        }

        private static TableSyncDiffResult ResultWith(TableSyncDiagnostic diagnostic) { var result = new TableSyncDiffResult(); result.Diagnostics.Add(diagnostic); return result; }
        private static string DisplayKind(TableSyncChangeKind kind) => kind == TableSyncChangeKind.PossibleDelete ? "DELETE" : kind.ToString().ToUpperInvariant();
    }
}
