using System;
using Common;
using UnityEditor;
using UnityEngine;

namespace CommonEditor
{
    /// <summary>
    /// LocalizedTextReference 전용 Inspector.
    ///
    /// Category(숫자 코드) + Key(1부터 시작하는 숫자)로 저작하고,
    /// 실제 직렬화는 Unity Localization의 Table GUID + Entry Key ID를 그대로 사용한다.
    /// Locale별 번역 미리보기와 문구 검색을 함께 제공한다.
    ///
    /// 표시 상태와 실제 참조는 항상 일치해야 한다.
    /// 해석되지 않는 Key를 입력하면 입력값만 남기고 Entry 참조는 지워서,
    /// 이전 키의 문구가 미리보기나 런타임에 계속 남는 일이 없게 한다.
    /// </summary>
    [CustomPropertyDrawer(typeof(LocalizedTextReference))]
    internal sealed class LocalizedTextReferenceDrawer : PropertyDrawer
    {
        private const float PreviewLabelWidth = 72f;
        private const string NoneOption = "(None)";

        // 매 GUI 호출마다 카테고리 목록을 다시 만들지 않도록 캐시한다.
        private static string[] cachedCategoryOptions;
        private static LocalizationCategoryCatalog.CategoryInfo[] cachedCategoryMap;
        private static int cachedCategoryVersion = -1;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float step = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            int rows = 3 + LocalizationCategoryCatalog.Locales.Count; // 헤더 + Category + Key + Locale 미리보기

            float height = step * rows;

            var state = Resolve(property);
            if (state.Message != null)
            {
                height += EditorGUIUtility.singleLineHeight * 1.8f + EditorGUIUtility.standardVerticalSpacing;
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            ResolvePendingIfTablesChanged(property);

            var state = Resolve(property);
            float line = EditorGUIUtility.singleLineHeight;
            float step = line + EditorGUIUtility.standardVerticalSpacing;
            var rect = new Rect(position.x, position.y, position.width, line);

            DrawHeader(rect, property, label);
            rect.y += step;

            EditorGUI.indentLevel++;

            DrawCategory(rect, property, state);
            rect.y += step;

            DrawKey(rect, property, state);
            rect.y += step;

            var previewLabels = LocalizationCategoryCatalog.LocaleLabels;
            for (int i = 0; i < previewLabels.Count; i++)
            {
                string value = state.Entry != null && i < state.Entry.LocaleValues.Length
                    ? state.Entry.LocaleValues[i]
                    : string.Empty;

                DrawReadOnlyRow(rect, previewLabels[i], value);
                rect.y += step;
            }

            if (state.Message != null)
            {
                var messageRect = EditorGUI.IndentedRect(new Rect(rect.x, rect.y, rect.width, line * 1.8f));
                EditorGUI.HelpBox(messageRect, state.Message, state.MessageType);
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawHeader(Rect rect, SerializedProperty property, GUIContent label)
        {
            const float searchWidth = 100f;
            var labelRect = new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - searchWidth - 4f), rect.height);
            var searchRect = new Rect(rect.xMax - searchWidth, rect.y, searchWidth, rect.height);

            EditorGUI.LabelField(labelRect, label, EditorStyles.boldLabel);

            if (GUI.Button(searchRect, "Search Text...", EditorStyles.miniButton))
            {
                var targets = property.serializedObject.targetObjects;
                string path = property.propertyPath;
                LocalizedTextSearchWindow.Open(
                    GUIUtility.GUIToScreenRect(searchRect),
                    entry => LocalizedTextReferenceProperty.ApplyToTargets(targets, path, entry));
            }
        }

        private static void DrawCategory(Rect rect, SerializedProperty property, ResolvedState state)
        {
            RefreshCategoryOptions();

            string[] options = cachedCategoryOptions;
            var map = cachedCategoryMap;
            int selected = 0;

            if (state.Category != null)
            {
                selected = Array.IndexOf(map, state.Category);
                if (selected < 0)
                {
                    selected = 0;
                }
            }
            else if (!string.IsNullOrEmpty(state.RawTableReference))
            {
                // 규칙을 벗어난 참조를 조용히 덮어쓰지 않도록 마지막에 별도 항목으로 보여 준다.
                var extended = new string[options.Length + 1];
                options.CopyTo(extended, 0);
                extended[options.Length] = $"(Unmanaged) {state.UnmanagedTableName}";
                options = extended;
                selected = options.Length - 1;
            }

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = LocalizedTextReferenceProperty
                .FindTableCollectionName(property).hasMultipleDifferentValues;
            int next = EditorGUI.Popup(rect, "Category", selected, options);
            EditorGUI.showMixedValue = false;

            if (!EditorGUI.EndChangeCheck() || next == selected)
            {
                return;
            }

            if (next >= map.Length)
            {
                // "(Unmanaged)" 항목 재선택은 아무것도 바꾸지 않는다.
                return;
            }

            ApplyCategorySelection(property, map[next], state.DisplayKeyNumber);
        }

        private static void DrawKey(Rect rect, SerializedProperty property, ResolvedState state)
        {
            var keyIdProperty = LocalizedTextReferenceProperty.FindKeyId(property);

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = keyIdProperty.hasMultipleDifferentValues;
            int next = EditorGUI.IntField(rect, "Key", state.DisplayKeyNumber);
            EditorGUI.showMixedValue = false;

            if (!EditorGUI.EndChangeCheck() || next == state.DisplayKeyNumber)
            {
                return;
            }

            ApplyKeyInput(property, state.Category, next);
        }

        /// <summary>
        /// Key 입력을 반영한다. GUI 밖에서도 같은 규칙이 적용되도록 분리해 둔다.
        ///
        /// 해석되지 않는 Key는 입력값만 남기고 Entry 참조를 지운다.
        /// Category는 유지하되(선택이 없으면 참조 전체를 비운다) Key ID / Key Name만 비우므로,
        /// 미리보기가 공란이 되고 이전 키의 문구가 런타임에 남지 않는다.
        /// </summary>
        internal static void ApplyKeyInput(
            SerializedProperty property,
            LocalizationCategoryCatalog.CategoryInfo category,
            int keyNumber)
        {
            if (category != null && keyNumber > 0 && category.EntriesByNumber.TryGetValue(keyNumber, out var entry))
            {
                LocalizedTextReferenceProperty.SetReference(property, entry);
                LocalizedTextPendingKeys.Clear(property);
                return;
            }

            if (category != null)
            {
                LocalizedTextReferenceProperty.SetCategoryOnly(property, category);
            }
            else
            {
                LocalizedTextReferenceProperty.Clear(property);
            }

            LocalizedTextPendingKeys.Set(property, keyNumber);
        }

        /// <summary>
        /// Category 선택을 반영한다. 사용자가 보던 숫자 키는 새 카테고리에서 다시 찾아본다.
        /// 새 카테고리에 그 키가 없으면 Entry 참조 없이 카테고리만 남고 입력값은 미해결로 유지된다.
        /// </summary>
        internal static void ApplyCategorySelection(
            SerializedProperty property,
            LocalizationCategoryCatalog.CategoryInfo picked,
            int desiredKey)
        {
            if (picked == null)
            {
                // "(None)" 은 필드 전체 초기화다. 미해결 입력값도 함께 지운다.
                LocalizedTextReferenceProperty.Clear(property);
                LocalizedTextPendingKeys.Clear(property);
                return;
            }

            ApplyKeyInput(property, picked, desiredKey);
        }

        private static void DrawReadOnlyRow(Rect rect, string label, string value)
        {
            var indented = EditorGUI.IndentedRect(rect);
            float labelWidth = Mathf.Min(PreviewLabelWidth, indented.width * 0.4f);
            var labelRect = new Rect(indented.x, indented.y, labelWidth, indented.height);
            var valueRect = new Rect(indented.x + labelWidth, indented.y, indented.width - labelWidth, indented.height);

            EditorGUI.LabelField(labelRect, label);
            EditorGUI.SelectableLabel(valueRect, value, EditorStyles.textField);
        }

        private static void RefreshCategoryOptions()
        {
            int version = LocalizationCategoryCatalog.Version;
            if (cachedCategoryVersion == version && cachedCategoryOptions != null)
            {
                return;
            }

            var categories = LocalizationCategoryCatalog.Categories;
            cachedCategoryOptions = new string[categories.Count + 1];
            cachedCategoryMap = new LocalizationCategoryCatalog.CategoryInfo[categories.Count + 1];
            cachedCategoryOptions[0] = NoneOption;
            cachedCategoryMap[0] = null;

            for (int i = 0; i < categories.Count; i++)
            {
                cachedCategoryOptions[i + 1] = categories[i].DisplayName;
                cachedCategoryMap[i + 1] = categories[i];
            }

            cachedCategoryVersion = version;
        }

        internal sealed class ResolvedState
        {
            public LocalizationCategoryCatalog.CategoryInfo Category;
            public LocalizationCategoryCatalog.EntryInfo Entry;
            public string RawTableReference;
            public string UnmanagedTableName;
            public int DisplayKeyNumber;
            public string Message;
            public MessageType MessageType = MessageType.None;
        }

        internal static ResolvedState Resolve(SerializedProperty property)
        {
            var state = new ResolvedState
            {
                RawTableReference = LocalizedTextReferenceProperty.FindTableCollectionName(property).stringValue,
            };

            long keyId = LocalizedTextReferenceProperty.FindKeyId(property).longValue;
            string keyName = LocalizedTextReferenceProperty.FindKeyName(property).stringValue;

            state.Category = LocalizedTextReferenceProperty.ResolveCategory(state.RawTableReference);

            if (state.Category == null && !string.IsNullOrEmpty(state.RawTableReference))
            {
                state.UnmanagedTableName = LocalizedTextReferenceProperty.DescribeUnmanagedTable(state.RawTableReference);
                state.Message =
                    $"'{state.UnmanagedTableName}' 는 숫자 카테고리 규칙(<번호>_<이름>, 예: 01_UI)을 따르지 않는 Collection입니다. " +
                    "참조는 그대로 유지됩니다. 규칙에 맞추려면 Category를 직접 선택하세요.";
                state.MessageType = MessageType.Warning;
            }

            if (state.Category != null)
            {
                if (keyId != 0)
                {
                    state.Category.EntriesById.TryGetValue(keyId, out state.Entry);
                }
                else if (!string.IsNullOrEmpty(keyName))
                {
                    state.Entry = state.Category.Entries.Find(e => e.KeyName == keyName);
                }
            }

            if (state.Entry != null)
            {
                state.DisplayKeyNumber = state.Entry.HasNumericKey ? state.Entry.KeyNumber : 0;
            }

            var pending = LocalizedTextPendingKeys.Get(property);
            if (pending != null)
            {
                state.DisplayKeyNumber = pending.Value;

                // 미해결 입력 중에는 이전 Entry의 문구가 미리보기에 남지 않아야 한다.
                state.Entry = null;

                if (state.Category == null)
                {
                    state.Message = "Category를 먼저 선택하세요. 참조는 비어 있습니다.";
                }
                else if (pending.Value <= 0)
                {
                    state.Message = "Key는 1 이상의 정수여야 합니다. Entry 참조는 비어 있습니다.";
                }
                else
                {
                    state.Message =
                        $"'{state.Category.DisplayName}' 카테고리에 Key {pending.Value} 가 없습니다. " +
                        "Localization Tables 창에서 Entry를 추가하거나 Google Sheet에서 Pull한 뒤 다시 시도하세요. " +
                        "Entry 참조는 비어 있습니다.";
                }

                state.MessageType = MessageType.Error;
                return state;
            }

            if (state.Entry != null)
            {
                if (!state.Entry.HasNumericKey)
                {
                    state.Message =
                        $"Entry Key '{state.Entry.KeyName}' 가 숫자 키 규칙을 따르지 않습니다. " +
                        "Localization Tables 창에서 1 이상의 정수로 이름을 바꾸세요.";
                    state.MessageType = MessageType.Warning;
                }
            }
            else if (state.Category != null && (keyId != 0 || !string.IsNullOrEmpty(keyName)))
            {
                state.Message =
                    $"'{state.Category.DisplayName}' 카테고리에서 참조된 Entry(Key ID {keyId})를 찾을 수 없습니다. " +
                    "Entry가 삭제되었거나 다른 Collection의 키일 수 있습니다.";
                state.MessageType = MessageType.Error;
            }
            else if (state.Category != null)
            {
                state.Message = "Key가 지정되지 않았습니다.";
                state.MessageType = MessageType.Warning;
            }

            return state;
        }

        /// <summary>
        /// 테이블이 바뀌면(키 추가, Pull, Undo 등) 미해결 입력을 다시 판정한다.
        /// 값을 쓰므로 GetPropertyHeight가 아니라 OnGUI에서만 호출한다.
        /// </summary>
        private static void ResolvePendingIfTablesChanged(SerializedProperty property)
        {
            var pending = LocalizedTextPendingKeys.Get(property);
            if (pending == null || pending.CatalogVersion == LocalizationCategoryCatalog.Version)
            {
                return;
            }

            pending.CatalogVersion = LocalizationCategoryCatalog.Version;

            if (pending.Value <= 0)
            {
                return;
            }

            var category = LocalizedTextReferenceProperty.ResolveCategory(
                LocalizedTextReferenceProperty.FindTableCollectionName(property).stringValue);

            if (category != null && category.EntriesByNumber.TryGetValue(pending.Value, out var entry))
            {
                LocalizedTextReferenceProperty.SetReference(property, entry);
                property.serializedObject.ApplyModifiedProperties();
                LocalizedTextPendingKeys.Clear(property);
            }
        }
    }
}
