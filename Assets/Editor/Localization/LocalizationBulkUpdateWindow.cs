using UnityEditor;
using UnityEngine;

namespace CommonEditor.Localization
{
    /// <summary>Tools/Localize Update 창. 비교와 반영은 LocalizationBulkUpdateService가 담당한다.</summary>
    internal sealed class LocalizationBulkUpdateWindow : EditorWindow
    {
        private Vector2 scrollPosition;
        private LocalizationBulkUpdateService.ScanResult scanResult;
        private string summary = "Scan을 눌러 TableData/Localization CSV를 비교하세요.";

        [MenuItem(LocalizationBulkUpdateService.MenuPath)]
        private static void Open()
        {
            var window = GetWindow<LocalizationBulkUpdateWindow>();
            window.titleContent = new GUIContent("Localize Update");
            window.minSize = new Vector2(720, 260);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("KeyBuddy Localization CSV 일괄 갱신", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("CSV는 읽기 전용이며, 적용은 Unity Localization CSV Merge로만 수행됩니다.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan", GUILayout.Width(110)))
                {
                    Scan();
                }

                using (new EditorGUI.DisabledScope(scanResult == null))
                {
                    if (GUILayout.Button("변경 전체 선택", GUILayout.Width(110)))
                    {
                        foreach (var table in scanResult.Tables)
                        {
                            table.IsSelected = table.IsValid && table.HasChanges;
                        }
                    }

                    if (GUILayout.Button("선택 해제", GUILayout.Width(90)))
                    {
                        foreach (var table in scanResult.Tables)
                        {
                            table.IsSelected = false;
                        }
                    }

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Update Selected", GUILayout.Width(140)))
                    {
                        UpdateSelected();
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(summary, MessageType.Info);
            if (scanResult == null)
            {
                return;
            }

            DrawHeader();
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            foreach (var table in scanResult.Tables)
            {
                DrawRow(table);
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("선택", GUILayout.Width(48));
                GUILayout.Label("테이블명", GUILayout.MinWidth(160));
                GUILayout.Label("신규", GUILayout.Width(50));
                GUILayout.Label("변경", GUILayout.Width(50));
                GUILayout.Label("삭제 감지", GUILayout.Width(75));
                GUILayout.Label("상태", GUILayout.ExpandWidth(true));
            }
        }

        private static void DrawRow(LocalizationBulkUpdateService.TableResult table)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!table.IsValid))
                {
                    table.IsSelected = EditorGUILayout.Toggle(table.IsSelected, GUILayout.Width(48));
                }

                GUILayout.Label(table.TableName, GUILayout.MinWidth(160));
                GUILayout.Label(table.NewKeyCount.ToString(), GUILayout.Width(50));
                GUILayout.Label(table.ChangedCount.ToString(), GUILayout.Width(50));
                GUILayout.Label(table.DeletionDetectedCount.ToString(), GUILayout.Width(75));
                GUILayout.Label(table.Status, EditorStyles.wordWrappedMiniLabel, GUILayout.ExpandWidth(true));
            }
        }

        private void Scan()
        {
            scanResult = LocalizationBulkUpdateService.Scan();
            summary = scanResult.Summary;
            Debug.Log($"[Localize Update] {summary}");
        }

        private void UpdateSelected()
        {
            if (LocalizationBulkUpdateService.ShouldWarnForDeletion(scanResult.Tables)
                && !EditorUtility.DisplayDialog(
                    "삭제 감지 경고",
                    "이전 항목에서 삭제된 텍스트 키가 존재합니다. 테이블 확인을 요망합니다.",
                    "계속 업데이트",
                    "취소"))
            {
                summary = "삭제 감지 경고에서 업데이트를 취소했습니다.";
                return;
            }

            var update = LocalizationBulkUpdateService.UpdateSelected(scanResult.Tables);
            summary = update.Summary;
            if (update.Succeeded)
            {
                Debug.Log($"[Localize Update] {summary}");
                Scan();
            }
            else
            {
                Debug.LogWarning($"[Localize Update] {summary}");
            }
        }
    }
}
