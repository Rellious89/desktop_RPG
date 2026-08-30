using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.Localization.Plugins.CSV;
using UnityEditor.Localization.Plugins.CSV.Columns;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace CommonEditor.Localization
{
    /// <summary>
    /// 프로젝트 밖 TableData/Localization CSV와 String Table Collection을 비교하고 안전하게 Merge 한다.
    /// CSV 원본은 읽기 전용이며, 실제 반영은 Unity Localization의 Csv.ImportInto API만 사용한다.
    /// </summary>
    internal static class LocalizationBulkUpdateService
    {
        internal const string MenuPath = "Tools/Localize Update";
        private static readonly string[] RequiredLocaleCodes = { "en", "ko-KR" };

        internal sealed class ScanResult
        {
            internal readonly List<TableResult> Tables = new List<TableResult>();
            internal string Summary;
        }

        internal sealed class TableResult
        {
            internal string TableName;
            internal string CsvPath;
            internal StringTableCollection Collection;
            internal string FileHash;
            internal readonly List<CsvRow> Rows = new List<CsvRow>();
            internal readonly Dictionary<string, string> LocaleHeaders = new Dictionary<string, string>(StringComparer.Ordinal);
            internal readonly List<string> Errors = new List<string>();
            internal int NewKeyCount;
            internal int ChangedCount;
            internal int AssetOnlyCount;
            internal bool IsSelected;

            internal bool IsValid => Errors.Count == 0;
            internal bool HasChanges => NewKeyCount > 0 || ChangedCount > 0;
            internal string Status => IsValid
                ? (HasChanges ? "적용 가능" : "변경 없음")
                : string.Join("\n", Errors);
        }

        internal sealed class CsvRow
        {
            internal int LineNumber;
            internal string Key;
            internal long? ExplicitId;
            internal readonly Dictionary<string, string> LocaleValues = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        internal sealed class UpdateResult
        {
            internal bool Succeeded;
            internal int UpdatedTableCount;
            internal string Summary;
        }

        internal static string ProjectRootPath => Directory.GetParent(UnityEngine.Application.dataPath).FullName;
        internal static string DefaultCsvDirectory => Path.Combine(ProjectRootPath, "TableData", "Localization");

        internal static ScanResult Scan() => ScanDirectory(DefaultCsvDirectory, ResolveCollection);

        /// <summary>테스트에서 임시 CSV/Collection을 주입할 수 있도록 디렉터리와 resolver를 분리한다.</summary>
        internal static ScanResult ScanDirectory(string csvDirectory, Func<string, StringTableCollection> collectionResolver)
        {
            var result = new ScanResult();
            if (string.IsNullOrEmpty(csvDirectory) || !Directory.Exists(csvDirectory))
            {
                result.Summary = $"CSV 폴더를 찾을 수 없습니다: {csvDirectory}";
                return result;
            }

            foreach (string csvPath in Directory.EnumerateFiles(csvDirectory, "*.csv", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
            {
                string tableName = Path.GetFileNameWithoutExtension(csvPath);
                result.Tables.Add(ScanFile(csvPath, tableName, collectionResolver?.Invoke(tableName)));
            }

            int validCount = result.Tables.Count(table => table.IsValid);
            int changedCount = result.Tables.Count(table => table.IsValid && table.HasChanges);
            result.Summary = $"스캔 완료: {result.Tables.Count}개 테이블, 적용 가능 {changedCount}개, 오류 {result.Tables.Count - validCount}개";
            return result;
        }

        internal static UpdateResult UpdateSelected(IList<TableResult> scannedTables)
        {
            var selected = scannedTables?.Where(table => table != null && table.IsSelected).ToList() ?? new List<TableResult>();
            if (selected.Count == 0)
            {
                return new UpdateResult { Summary = "선택된 테이블이 없습니다." };
            }

            // 모든 선택 테이블을 먼저 다시 비교한다. 하나라도 stale/invalid이면 절대 일부 적용하지 않는다.
            foreach (TableResult previous in selected)
            {
                TableResult current = ScanFile(previous.CsvPath, previous.TableName, ResolveCollection(previous.TableName));
                if (!IsSameSnapshot(previous, current))
                {
                    return new UpdateResult
                    {
                        Summary = $"'{previous.TableName}'의 CSV 또는 비교 결과가 스캔 뒤 변경되었습니다. Scan을 다시 실행하세요.",
                    };
                }

                if (!current.IsValid)
                {
                    return new UpdateResult { Summary = $"'{previous.TableName}'에 오류가 있어 적용하지 않았습니다. Scan 결과를 확인하세요." };
                }
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Localize Update");
            try
            {
                foreach (TableResult table in selected)
                {
                    using (var reader = new StreamReader(table.CsvPath, Encoding.UTF8, true))
                    {
                        // false는 Inspector의 CSV(Merge)와 같은 "CSV에 없는 키 보존" 동작이다.
                        Csv.ImportInto(reader, table.Collection, CreateColumnMappings(table), createUndo: true, reporter: null, removeMissingEntries: false);
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Undo.CollapseUndoOperations(undoGroup);
                return new UpdateResult
                {
                    Succeeded = true,
                    UpdatedTableCount = selected.Count,
                    Summary = $"Localize Update 완료: {selected.Count}개 테이블을 Merge했습니다.",
                };
            }
            catch (Exception exception)
            {
                return new UpdateResult { Summary = $"Localize Update 실패: {exception.Message}" };
            }
        }

        private static StringTableCollection ResolveCollection(string tableName)
        {
            return LocalizationEditorSettings.GetStringTableCollection(tableName);
        }

        private static TableResult ScanFile(string csvPath, string tableName, StringTableCollection collection)
        {
            var result = new TableResult
            {
                CsvPath = csvPath,
                TableName = tableName,
                Collection = collection,
            };

            if (collection == null)
            {
                result.Errors.Add("동일한 이름의 StringTableCollection이 없습니다.");
                return result;
            }

            if (collection.SharedData == null)
            {
                result.Errors.Add("Collection Shared Data를 찾을 수 없습니다.");
                return result;
            }

            var tablesByLocale = new Dictionary<string, StringTable>(StringComparer.Ordinal);
            foreach (string localeCode in RequiredLocaleCodes)
            {
                var table = collection.GetTable(new LocaleIdentifier(localeCode)) as StringTable;
                if (table == null)
                {
                    result.Errors.Add($"Collection에 {localeCode} String Table이 없습니다.");
                }
                else
                {
                    tablesByLocale.Add(localeCode, table);
                }
            }

            if (!TryReadCsv(csvPath, result, out List<string[]> records))
            {
                return result;
            }

            ValidateAndReadRows(records, result);
            if (!result.IsValid)
            {
                return result;
            }

            CompareWithCollection(result, tablesByLocale);
            result.IsSelected = result.HasChanges;
            return result;
        }

        private static bool TryReadCsv(string csvPath, TableResult result, out List<string[]> records)
        {
            records = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(csvPath);
                using (SHA256 sha = SHA256.Create())
                {
                    result.FileHash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
                }

                string content = File.ReadAllText(csvPath, Encoding.UTF8);
                if (!Rfc4180CsvParser.TryParse(content, out records, out string parseError))
                {
                    result.Errors.Add($"잘못된 CSV: {parseError}");
                    return false;
                }

                if (records.Count == 0)
                {
                    result.Errors.Add("헤더가 없는 빈 CSV입니다.");
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                result.Errors.Add($"CSV를 읽을 수 없습니다: {exception.Message}");
                return false;
            }
        }

        private static void ValidateAndReadRows(IReadOnlyList<string[]> records, TableResult result)
        {
            string[] headers = records[0];
            var headerIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < headers.Length; index++)
            {
                string header = headers[index]?.Trim();
                if (string.IsNullOrEmpty(header) || !headerIndices.TryAdd(header, index))
                {
                    result.Errors.Add($"잘못되었거나 중복된 헤더: {headers[index]}");
                    return;
                }
            }

            if (!headerIndices.TryGetValue("Key", out int keyIndex))
            {
                result.Errors.Add("필수 Key 헤더가 없습니다.");
                return;
            }

            int idIndex = headerIndices.TryGetValue("Id", out int foundIdIndex) ? foundIdIndex : -1;
            foreach (var header in headerIndices)
            {
                if (header.Key == "Key" || header.Key == "Id")
                {
                    continue;
                }

                string localeCode = GetDefaultMappedLocaleCode(header.Key);
                if (localeCode == null)
                {
                    result.Errors.Add($"지원할 수 없는 locale 열: {header.Key}");
                    return;
                }

                if (result.LocaleHeaders.ContainsKey(localeCode))
                {
                    result.Errors.Add($"{localeCode} locale 열이 중복되었습니다.");
                    return;
                }

                result.LocaleHeaders.Add(localeCode, header.Key);
            }

            if (result.LocaleHeaders.Count == 0)
            {
                result.Errors.Add("지원되는 locale 열(en 또는 ko-KR)이 없습니다.");
                return;
            }

            var keys = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var ids = new Dictionary<long, List<int>>();
            for (int recordIndex = 1; recordIndex < records.Count; recordIndex++)
            {
                string[] record = records[recordIndex];
                int lineNumber = recordIndex + 1;
                if (record.All(string.IsNullOrEmpty))
                {
                    continue;
                }

                if (record.Length != headers.Length)
                {
                    result.Errors.Add($"{lineNumber}행: 열 수가 헤더와 다릅니다.");
                    continue;
                }

                string key = record[keyIndex];
                if (string.IsNullOrWhiteSpace(key))
                {
                    result.Errors.Add($"{lineNumber}행: Key가 비어 있습니다.");
                    continue;
                }

                var row = new CsvRow { LineNumber = lineNumber, Key = key };
                if (idIndex >= 0 && !string.IsNullOrWhiteSpace(record[idIndex]))
                {
                    if (!long.TryParse(record[idIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) || id <= 0)
                    {
                        result.Errors.Add($"{lineNumber}행: Id는 비어 있거나 양의 정수여야 합니다.");
                        continue;
                    }

                    row.ExplicitId = id;
                    AddLine(ids, id, lineNumber);
                }

                foreach (var localeHeader in result.LocaleHeaders)
                {
                    row.LocaleValues.Add(localeHeader.Key, record[headerIndices[localeHeader.Value]]);
                }

                AddLine(keys, key, lineNumber);
                result.Rows.Add(row);
            }

            AddDuplicateErrors(keys, "Key", result);
            AddDuplicateErrors(ids, "Id", result);
        }

        private static void CompareWithCollection(TableResult result, IReadOnlyDictionary<string, StringTable> tablesByLocale)
        {
            var sharedByKey = result.Collection.SharedData.Entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
            var sharedById = result.Collection.SharedData.Entries.ToDictionary(entry => entry.Id);
            var csvKeys = new HashSet<string>(result.Rows.Select(row => row.Key), StringComparer.Ordinal);

            foreach (CsvRow row in result.Rows)
            {
                if (!sharedByKey.TryGetValue(row.Key, out SharedTableData.SharedTableEntry sharedEntry))
                {
                    if (row.ExplicitId.HasValue && sharedById.TryGetValue(row.ExplicitId.Value, out SharedTableData.SharedTableEntry idOwner))
                    {
                        result.Errors.Add($"{row.LineNumber}행: Id {row.ExplicitId.Value}는 기존 Key '{idOwner.Key}'가 사용 중입니다.");
                        continue;
                    }

                    result.NewKeyCount++;
                    continue;
                }

                if (row.ExplicitId.HasValue && row.ExplicitId.Value != sharedEntry.Id)
                {
                    result.Errors.Add($"{row.LineNumber}행: 기존 Key '{row.Key}'의 Id가 에셋과 다릅니다.");
                    continue;
                }

                bool changed = false;
                foreach (var localeValue in row.LocaleValues)
                {
                    string currentValue = tablesByLocale[localeValue.Key].GetEntry(sharedEntry.Id)?.LocalizedValue ?? string.Empty;
                    if (!string.Equals(currentValue, localeValue.Value, StringComparison.Ordinal))
                    {
                        changed = true;
                        break;
                    }
                }

                if (changed)
                {
                    result.ChangedCount++;
                }
            }

            if (result.Errors.Count > 0)
            {
                return;
            }

            result.AssetOnlyCount = sharedByKey.Keys.Count(key => !csvKeys.Contains(key));
        }

        private static IList<CsvColumns> CreateColumnMappings(TableResult table)
        {
            // Unity 기본 mapping을 바탕으로, CSV가 실제로 제공한 locale 열만 남긴다.
            // 이는 제공하지 않은 locale 값을 빈 문자열로 덮어쓰지 않게 하면서 header 해석은 기본 mapping과 동일하게 유지한다.
            var mappings = ColumnMapping.CreateDefaultMapping(includeComments: false);
            var filtered = new List<CsvColumns>();
            foreach (CsvColumns mapping in mappings)
            {
                if (mapping is KeyIdColumns keyColumns)
                {
                    keyColumns.IncludeSharedComments = false;
                    filtered.Add(keyColumns);
                }
                else if (mapping is LocaleColumns localeColumns && table.LocaleHeaders.TryGetValue(localeColumns.LocaleIdentifier.Code, out string header))
                {
                    localeColumns.FieldName = header;
                    localeColumns.IncludeComments = false;
                    filtered.Add(localeColumns);
                }
            }

            return filtered;
        }

        private static bool IsSameSnapshot(TableResult previous, TableResult current)
        {
            return current.IsValid
                && string.Equals(previous.FileHash, current.FileHash, StringComparison.Ordinal)
                && previous.NewKeyCount == current.NewKeyCount
                && previous.ChangedCount == current.ChangedCount
                && previous.AssetOnlyCount == current.AssetOnlyCount;
        }

        private static string GetDefaultMappedLocaleCode(string header)
        {
            foreach (string localeCode in RequiredLocaleCodes)
            {
                // LocaleColumns.SetDefaultFieldNames()가 생성하는 기본 FieldName과 정확히 같아야 한다.
                if (string.Equals(header, new LocaleIdentifier(localeCode).ToString(), StringComparison.Ordinal))
                {
                    return localeCode;
                }
            }

            return null;
        }

        private static void AddLine<TKey>(IDictionary<TKey, List<int>> lines, TKey value, int lineNumber)
        {
            if (!lines.TryGetValue(value, out List<int> valueLines))
            {
                valueLines = new List<int>();
                lines.Add(value, valueLines);
            }

            valueLines.Add(lineNumber);
        }

        private static void AddDuplicateErrors<TKey>(IEnumerable<KeyValuePair<TKey, List<int>>> lines, string label, TableResult result)
        {
            foreach (var pair in lines.Where(pair => pair.Value.Count > 1))
            {
                result.Errors.Add($"중복 {label} '{pair.Key}': {string.Join(", ", pair.Value)}행");
            }
        }

        /// <summary>비교 전용의 작은 RFC 4180 parser. Import 자체는 Unity의 Csv.ImportInto가 담당한다.</summary>
        private static class Rfc4180CsvParser
        {
            internal static bool TryParse(string input, out List<string[]> records, out string error)
            {
                records = new List<string[]>();
                error = null;
                var record = new List<string>();
                var field = new StringBuilder();
                bool quoted = false;
                bool afterQuote = false;

                for (int index = 0; index < input.Length; index++)
                {
                    char character = input[index];
                    if (quoted)
                    {
                        if (character == '\"')
                        {
                            if (index + 1 < input.Length && input[index + 1] == '\"')
                            {
                                field.Append(character);
                                index++;
                            }
                            else
                            {
                                quoted = false;
                                afterQuote = true;
                            }
                        }
                        else
                        {
                            field.Append(character);
                        }

                        continue;
                    }

                    if (afterQuote && character != ',' && character != '\r' && character != '\n')
                    {
                        error = $"따옴표 뒤에 허용되지 않는 문자({index + 1}번째)가 있습니다.";
                        return false;
                    }

                    if (character == ',' )
                    {
                        record.Add(field.ToString());
                        field.Length = 0;
                        afterQuote = false;
                    }
                    else if (character == '\r' || character == '\n')
                    {
                        if (character == '\r' && index + 1 < input.Length && input[index + 1] == '\n')
                        {
                            index++;
                        }

                        record.Add(field.ToString());
                        records.Add(record.ToArray());
                        record.Clear();
                        field.Length = 0;
                        afterQuote = false;
                    }
                    else if (character == '\"')
                    {
                        if (field.Length != 0)
                        {
                            error = $"필드 중간의 따옴표({index + 1}번째)는 허용되지 않습니다.";
                            return false;
                        }

                        quoted = true;
                    }
                    else
                    {
                        field.Append(character);
                    }
                }

                if (quoted)
                {
                    error = "닫히지 않은 따옴표가 있습니다.";
                    return false;
                }

                if (field.Length > 0 || record.Count > 0 || afterQuote)
                {
                    record.Add(field.ToString());
                    records.Add(record.ToArray());
                }

                return true;
            }
        }
    }
}
