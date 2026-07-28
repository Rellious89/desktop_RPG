using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace CommonEditor
{
    /// <summary>
    /// 숫자 접두사를 가진 String Table Collection을 "카테고리"로 해석해 캐시하는 Editor 전용 인덱스.
    ///
    /// 명명 규칙: &lt;숫자&gt;_&lt;이름&gt; (예: 01_UI, 02_Battle)
    /// 카테고리 코드는 Collection 이름 앞쪽 숫자에서 판별한다.
    /// Entry Key 이름은 1부터 시작하는 양의 정수 문자열을 사용한다.
    ///
    /// 캐시는 지연 생성되며 Localization 에셋 변경 / Undo / Domain Reload 시 무효화된다.
    /// </summary>
    [InitializeOnLoad]
    internal static class LocalizationCategoryCatalog
    {
        internal sealed class EntryInfo
        {
            public CategoryInfo Category;
            public long KeyId;
            public string KeyName;

            /// <summary>사용자 숫자 키. 숫자 키 규칙을 따르지 않는 Entry는 -1.</summary>
            public int KeyNumber = -1;

            /// <summary><see cref="Locales"/>와 같은 순서의 Locale별 번역 값. 값이 없으면 빈 문자열.</summary>
            public string[] LocaleValues;

            /// <summary>검색용 소문자 캐시.</summary>
            public string SearchBlob;

            public bool HasNumericKey => KeyNumber > 0;
        }

        internal sealed class CategoryInfo
        {
            public int Code;
            public string CollectionName;

            /// <summary>Inspector 표시용 이름. 예: "01 UI"</summary>
            public string DisplayName;

            public StringTableCollection Collection;
            public Guid TableCollectionNameGuid;
            public List<EntryInfo> Entries = new List<EntryInfo>();
            public Dictionary<int, EntryInfo> EntriesByNumber = new Dictionary<int, EntryInfo>();
            public Dictionary<long, EntryInfo> EntriesById = new Dictionary<long, EntryInfo>();
        }

        private static bool dirty = true;
        private static int version;

        private static readonly List<CategoryInfo> categories = new List<CategoryInfo>();
        private static readonly List<Locale> locales = new List<Locale>();
        private static readonly List<string> localeLabels = new List<string>();
        private static readonly Dictionary<Guid, CategoryInfo> categoriesByGuid = new Dictionary<Guid, CategoryInfo>();

        static LocalizationCategoryCatalog()
        {
            var events = LocalizationEditorSettings.EditorEvents;
            events.CollectionAdded += _ => MarkDirty();
            events.CollectionRemoved += _ => MarkDirty();
            events.CollectionModified += (_, __) => MarkDirty();
            events.TableEntryAdded += (_, __) => MarkDirty();
            events.TableEntryRemoved += (_, __) => MarkDirty();
            events.TableEntryModified += _ => MarkDirty();
            events.TableAddedToCollection += (_, __) => MarkDirty();
            events.TableRemovedFromCollection += (_, __) => MarkDirty();
            events.LocaleAdded += _ => MarkDirty();
            events.LocaleRemoved += _ => MarkDirty();
            Undo.undoRedoPerformed += MarkDirty;
        }

        /// <summary>캐시가 다시 만들어질 때마다 증가한다. Drawer의 로컬 캐시 무효화에 사용한다.</summary>
        internal static int Version
        {
            get
            {
                EnsureBuilt();
                return version;
            }
        }

        /// <summary>숫자 접두사를 가진 카테고리 목록. 코드 오름차순.</summary>
        internal static IReadOnlyList<CategoryInfo> Categories
        {
            get
            {
                EnsureBuilt();
                return categories;
            }
        }

        /// <summary>프로젝트 Locale 목록. Locale이 늘어나면 미리보기/검색 열이 자동으로 늘어난다.</summary>
        internal static IReadOnlyList<Locale> Locales
        {
            get
            {
                EnsureBuilt();
                return locales;
            }
        }

        /// <summary><see cref="Locales"/>와 같은 순서의 표시용 이름. 예: "English", "Korean"</summary>
        internal static IReadOnlyList<string> LocaleLabels
        {
            get
            {
                EnsureBuilt();
                return localeLabels;
            }
        }

        internal static void MarkDirty()
        {
            dirty = true;
        }

        internal static CategoryInfo FindCategoryByGuid(Guid guid)
        {
            EnsureBuilt();
            return categoriesByGuid.TryGetValue(guid, out var category) ? category : null;
        }

        internal static CategoryInfo FindCategoryByCode(int code)
        {
            EnsureBuilt();
            return categories.FirstOrDefault(c => c.Code == code);
        }

        /// <summary>
        /// Collection 이름에서 카테고리 코드와 표시 이름을 뽑아낸다.
        /// 유효한 양의 정수 접두사가 없으면 false.
        /// </summary>
        internal static bool TryParseCategoryName(string collectionName, out int code, out string displayName)
        {
            code = 0;
            displayName = null;

            if (string.IsNullOrEmpty(collectionName))
            {
                return false;
            }

            int digits = 0;
            while (digits < collectionName.Length && char.IsDigit(collectionName[digits]))
            {
                digits++;
            }

            if (digits == 0)
            {
                return false;
            }

            string prefix = collectionName.Substring(0, digits);
            if (!int.TryParse(prefix, NumberStyles.None, CultureInfo.InvariantCulture, out code) || code <= 0)
            {
                return false;
            }

            string rest = collectionName.Substring(digits).TrimStart('_', '-', ' ');
            displayName = rest.Length == 0 ? prefix : $"{prefix} {rest.Replace('_', ' ')}";
            return true;
        }

        /// <summary>Entry Key 이름을 사용자 숫자 키로 해석한다. 1 이상만 유효.</summary>
        internal static bool TryParseKeyNumber(string keyName, out int keyNumber)
        {
            keyNumber = -1;
            return !string.IsNullOrEmpty(keyName)
                && int.TryParse(keyName, NumberStyles.None, CultureInfo.InvariantCulture, out keyNumber)
                && keyNumber > 0;
        }

        private static void EnsureBuilt()
        {
            if (!dirty)
            {
                return;
            }

            dirty = false;
            Rebuild();
            version++;
        }

        private static void Rebuild()
        {
            categories.Clear();
            categoriesByGuid.Clear();
            locales.Clear();
            localeLabels.Clear();

            locales.AddRange(LocalizationEditorSettings.GetLocales()
                .Where(l => l != null)
                .OrderBy(l => l.SortOrder)
                .ThenBy(l => l.Identifier.Code, StringComparer.Ordinal));

            foreach (var locale in locales)
            {
                localeLabels.Add(BuildLocaleLabel(locale));
            }

            foreach (var collection in LocalizationEditorSettings.GetStringTableCollections())
            {
                if (collection == null || collection.SharedData == null)
                {
                    continue;
                }

                if (!TryParseCategoryName(collection.TableCollectionName, out int code, out string displayName))
                {
                    // 숫자 접두사가 없는 Collection은 카테고리로 취급하지 않는다.
                    // (조용히 잘못 연결하지 않기 위해 Drawer에서 별도로 경고한다.)
                    continue;
                }

                var category = new CategoryInfo
                {
                    Code = code,
                    CollectionName = collection.TableCollectionName,
                    DisplayName = displayName,
                    Collection = collection,
                    TableCollectionNameGuid = collection.SharedData.TableCollectionNameGuid,
                };

                var tables = new StringTable[locales.Count];
                for (int i = 0; i < locales.Count; i++)
                {
                    tables[i] = collection.GetTable(locales[i].Identifier) as StringTable;
                }

                foreach (var sharedEntry in collection.SharedData.Entries)
                {
                    var entry = new EntryInfo
                    {
                        Category = category,
                        KeyId = sharedEntry.Id,
                        KeyName = sharedEntry.Key,
                        LocaleValues = new string[locales.Count],
                    };

                    if (TryParseKeyNumber(sharedEntry.Key, out int keyNumber))
                    {
                        entry.KeyNumber = keyNumber;
                    }

                    for (int i = 0; i < tables.Length; i++)
                    {
                        entry.LocaleValues[i] = tables[i]?.GetEntry(sharedEntry.Id)?.Value ?? string.Empty;
                    }

                    entry.SearchBlob = BuildSearchBlob(category, entry);

                    category.Entries.Add(entry);
                    category.EntriesById[entry.KeyId] = entry;
                    if (entry.HasNumericKey && !category.EntriesByNumber.ContainsKey(entry.KeyNumber))
                    {
                        category.EntriesByNumber[entry.KeyNumber] = entry;
                    }
                }

                category.Entries.Sort(CompareEntries);
                categories.Add(category);

                if (category.TableCollectionNameGuid != Guid.Empty)
                {
                    categoriesByGuid[category.TableCollectionNameGuid] = category;
                }
            }

            categories.Sort((a, b) => a.Code != b.Code
                ? a.Code.CompareTo(b.Code)
                : string.Compare(a.CollectionName, b.CollectionName, StringComparison.Ordinal));
        }

        private static int CompareEntries(EntryInfo a, EntryInfo b)
        {
            // 숫자 키를 앞쪽에 오름차순으로, 규칙을 벗어난 키는 뒤에 이름순으로 둔다.
            if (a.HasNumericKey && b.HasNumericKey)
            {
                return a.KeyNumber.CompareTo(b.KeyNumber);
            }

            if (a.HasNumericKey != b.HasNumericKey)
            {
                return a.HasNumericKey ? -1 : 1;
            }

            return string.Compare(a.KeyName, b.KeyName, StringComparison.Ordinal);
        }

        private static string BuildSearchBlob(CategoryInfo category, EntryInfo entry)
        {
            var builder = new StringBuilder();
            builder.Append(category.Code.ToString(CultureInfo.InvariantCulture)).Append(' ');
            builder.Append(category.DisplayName).Append(' ');
            builder.Append(entry.KeyName).Append(' ');

            foreach (string value in entry.LocaleValues)
            {
                builder.Append(value).Append(' ');
            }

            return builder.ToString().ToLowerInvariant();
        }

        private static string BuildLocaleLabel(Locale locale)
        {
            var culture = locale.Identifier.CultureInfo;
            if (culture != null)
            {
                try
                {
                    // "Korean (South Korea)"가 아니라 "Korean"처럼 언어 이름만 보여준다.
                    var language = CultureInfo.GetCultureInfo(culture.TwoLetterISOLanguageName);
                    if (!string.IsNullOrEmpty(language.EnglishName))
                    {
                        return language.EnglishName;
                    }
                }
                catch (CultureNotFoundException)
                {
                    // 아래 fallback 사용
                }

                return culture.EnglishName;
            }

            return string.IsNullOrEmpty(locale.LocaleName) ? locale.Identifier.Code : locale.LocaleName;
        }

        /// <summary>
        /// Localization 에셋이 프로젝트에서 바뀌면(Pull, 수동 편집, 파일 이동 등) 캐시를 무효화한다.
        /// </summary>
        private sealed class AssetWatcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] importedAssets,
                string[] deletedAssets,
                string[] movedAssets,
                string[] movedFromAssetPaths)
            {
                if (ContainsLocalizationAsset(importedAssets)
                    || ContainsLocalizationAsset(movedAssets)
                    || HasAssetExtension(deletedAssets)
                    || HasAssetExtension(movedFromAssetPaths))
                {
                    MarkDirty();
                }
            }

            private static bool ContainsLocalizationAsset(string[] paths)
            {
                foreach (string path in paths)
                {
                    if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                    if (type == null)
                    {
                        continue;
                    }

                    if (typeof(SharedTableData).IsAssignableFrom(type)
                        || typeof(LocalizationTable).IsAssignableFrom(type)
                        || typeof(LocalizationTableCollection).IsAssignableFrom(type)
                        || typeof(Locale).IsAssignableFrom(type))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool HasAssetExtension(string[] paths)
            {
                // 삭제/이동 전 경로는 타입을 조회할 수 없으므로 확장자만 보고 보수적으로 무효화한다.
                foreach (string path in paths)
                {
                    if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
