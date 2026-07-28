using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditorInternal;

namespace CommonEditor
{
    /// <summary>
    /// LocalizedTextReference(=LocalizedString)의 직렬화 필드를 읽고 쓰는 헬퍼.
    /// 런타임 참조는 항상 Table GUID + Entry Key ID로 기록한다.
    /// </summary>
    internal static class LocalizedTextReferenceProperty
    {
        private const string GuidTag = "GUID:";

        internal static SerializedProperty FindTableCollectionName(SerializedProperty property)
        {
            return property.FindPropertyRelative("m_TableReference").FindPropertyRelative("m_TableCollectionName");
        }

        internal static SerializedProperty FindKeyId(SerializedProperty property)
        {
            return property.FindPropertyRelative("m_TableEntryReference").FindPropertyRelative("m_KeyId");
        }

        internal static SerializedProperty FindKeyName(SerializedProperty property)
        {
            return property.FindPropertyRelative("m_TableEntryReference").FindPropertyRelative("m_Key");
        }

        /// <summary>직렬화된 Table 참조 문자열을 카테고리로 해석한다.</summary>
        internal static LocalizationCategoryCatalog.CategoryInfo ResolveCategory(string tableCollectionName)
        {
            if (string.IsNullOrEmpty(tableCollectionName))
            {
                return null;
            }

            if (TryParseGuid(tableCollectionName, out var guid))
            {
                return LocalizationCategoryCatalog.FindCategoryByGuid(guid);
            }

            return LocalizationCategoryCatalog.Categories
                .FirstOrDefault(c => string.Equals(c.CollectionName, tableCollectionName, StringComparison.Ordinal));
        }

        /// <summary>
        /// 카테고리로 해석되지 않는 참조를 사람이 읽을 수 있는 이름으로 바꾼다.
        /// 숫자 접두사 규칙을 따르지 않는 기존/외부 Collection을 구분해 표시하기 위한 용도.
        /// </summary>
        internal static string DescribeUnmanagedTable(string tableCollectionName)
        {
            if (string.IsNullOrEmpty(tableCollectionName))
            {
                return string.Empty;
            }

            if (!TryParseGuid(tableCollectionName, out var guid))
            {
                return tableCollectionName;
            }

            var collection = LocalizationEditorSettings.GetStringTableCollection(guid);
            return collection != null ? collection.TableCollectionName : tableCollectionName;
        }

        internal static bool TryParseGuid(string tableCollectionName, out Guid guid)
        {
            guid = Guid.Empty;
            return tableCollectionName != null
                && tableCollectionName.StartsWith(GuidTag, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(tableCollectionName.Substring(GuidTag.Length), out guid);
        }

        internal static string ToGuidString(Guid guid)
        {
            // Addressables가 사용하는 "N" 포맷(32자리)이어야 한다.
            return GuidTag + guid.ToString("N");
        }

        /// <summary>Table GUID + Entry Key ID를 직렬화 필드에 반영한다.</summary>
        internal static void SetReference(SerializedProperty property, LocalizationCategoryCatalog.EntryInfo entry)
        {
            FindTableCollectionName(property).stringValue = ToGuidString(entry.Category.TableCollectionNameGuid);
            FindKeyId(property).longValue = entry.KeyId;

            // Key ID를 사용하므로 이름 기반 참조는 비워 둔다.
            FindKeyName(property).stringValue = string.Empty;
        }

        /// <summary>Table만 지정하고 Entry는 비운다.</summary>
        internal static void SetCategoryOnly(SerializedProperty property, LocalizationCategoryCatalog.CategoryInfo category)
        {
            FindTableCollectionName(property).stringValue =
                category == null ? string.Empty : ToGuidString(category.TableCollectionNameGuid);
            FindKeyId(property).longValue = 0;
            FindKeyName(property).stringValue = string.Empty;
        }

        internal static void Clear(SerializedProperty property)
        {
            FindTableCollectionName(property).stringValue = string.Empty;
            FindKeyId(property).longValue = 0;
            FindKeyName(property).stringValue = string.Empty;
        }

        /// <summary>
        /// SerializedProperty를 직접 들고 있지 않아도 되도록, 대상 오브젝트 + 경로로 다시 열어 값을 쓴다.
        /// 검색창처럼 Drawer 바깥에서 값을 반영할 때 사용한다.
        /// </summary>
        internal static void ApplyToTargets(
            UnityEngine.Object[] targets,
            string propertyPath,
            LocalizationCategoryCatalog.EntryInfo entry)
        {
            if (targets == null || targets.Length == 0 || entry == null)
            {
                return;
            }

            var serializedObject = new SerializedObject(targets);
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null)
            {
                return;
            }

            SetReference(property, entry);
            serializedObject.ApplyModifiedProperties();

            // 참조가 확정됐으므로 미해결 Key 입력값을 모든 대상에서 지운다.
            // 이걸 빼먹으면 Inspector에는 잘못된 키가 계속 보이고 실제 참조와 어긋난다.
            LocalizedTextPendingKeys.Clear(targets, propertyPath);

            // 검색창에서 호출되므로 Inspector가 스스로 다시 그리지 않는다.
            InternalEditorUtility.RepaintAllViews();
        }
    }
}
