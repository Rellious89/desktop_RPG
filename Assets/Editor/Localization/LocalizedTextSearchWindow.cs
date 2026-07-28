using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CommonEditor
{
    /// <summary>
    /// 카테고리 번호 / 숫자 키 / 각 Locale 번역 문구로 String Table Entry를 찾는 드롭다운 검색창.
    ///
    /// 검색 대상은 현재 프로젝트로 Pull된 String Table 에셋이며, Google Sheets를 직접 조회하지 않는다.
    /// 동일한 문구가 여러 키에 있으면 자동 선택하지 않고 모든 후보를 나열한다.
    /// </summary>
    internal sealed class LocalizedTextSearchWindow : EditorWindow
    {
        private const float RowHeight = 20f;
        private const float CategoryColumnWidth = 90f;
        private const float KeyColumnWidth = 44f;

        private Action<LocalizationCategoryCatalog.EntryInfo> onPicked;
        private string query = string.Empty;
        private Vector2 scroll;
        private int catalogVersion = -1;
        private string lastQuery;
        private readonly List<LocalizationCategoryCatalog.EntryInfo> results =
            new List<LocalizationCategoryCatalog.EntryInfo>();

        internal static void Open(Rect screenRect, Action<LocalizationCategoryCatalog.EntryInfo> onPicked)
        {
            var window = CreateInstance<LocalizedTextSearchWindow>();
            window.onPicked = onPicked;
            window.ShowAsDropDown(screenRect, new Vector2(Mathf.Max(560f, screenRect.width), 340f));
        }

        private void OnGUI()
        {
            DrawSearchBar();

            EnsureResults();

            DrawColumnHeader();

            if (results.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "일치하는 Entry가 없습니다. Google Sheet를 수정했다면 먼저 Unity에서 Pull해야 검색에 반영됩니다.",
                    MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var entry in results)
            {
                DrawRow(entry);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSearchBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUI.SetNextControlName("LocalizedTextSearchField");
            query = EditorGUILayout.TextField(query, EditorStyles.toolbarSearchField);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f)))
            {
                LocalizationCategoryCatalog.MarkDirty();
                catalogVersion = -1;
            }

            EditorGUILayout.EndHorizontal();

            if (focusedWindow == this && string.IsNullOrEmpty(GUI.GetNameOfFocusedControl()))
            {
                EditorGUI.FocusTextInControl("LocalizedTextSearchField");
            }
        }

        private void DrawColumnHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Category", EditorStyles.miniBoldLabel, GUILayout.Width(CategoryColumnWidth));
            GUILayout.Label("Key", EditorStyles.miniBoldLabel, GUILayout.Width(KeyColumnWidth));

            foreach (string label in LocalizationCategoryCatalog.LocaleLabels)
            {
                GUILayout.Label(label, EditorStyles.miniBoldLabel);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRow(LocalizationCategoryCatalog.EntryInfo entry)
        {
            var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(RowHeight));

            GUILayout.Label(entry.Category.DisplayName, GUILayout.Width(CategoryColumnWidth));
            GUILayout.Label(entry.KeyName, GUILayout.Width(KeyColumnWidth));

            for (int i = 0; i < entry.LocaleValues.Length; i++)
            {
                GUILayout.Label(entry.LocaleValues[i], EditorStyles.label);
            }

            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
            {
                onPicked?.Invoke(entry);
                Event.current.Use();
                Close();
            }
        }

        private void EnsureResults()
        {
            int version = LocalizationCategoryCatalog.Version;
            if (version == catalogVersion && string.Equals(query, lastQuery, StringComparison.Ordinal))
            {
                return;
            }

            catalogVersion = version;
            lastQuery = query;

            results.Clear();
            string normalized = string.IsNullOrWhiteSpace(query) ? null : query.Trim().ToLowerInvariant();

            foreach (var category in LocalizationCategoryCatalog.Categories)
            {
                foreach (var entry in category.Entries)
                {
                    if (normalized == null || entry.SearchBlob.Contains(normalized))
                    {
                        results.Add(entry);
                    }
                }
            }
        }
    }
}
