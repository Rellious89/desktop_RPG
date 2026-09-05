using System;
using System.Collections.Generic;
using System.Linq;
using TableDataEditor;

namespace TableSyncEditor
{
    /// <summary>Phase 1 CSV 비교 결과. 이 계층은 파일을 쓰거나 Unity 에셋을 만지지 않는다.</summary>
    public enum TableSyncChangeKind { Add, Update, PossibleDelete, Unchanged }

    public sealed class TableSyncDiagnostic
    {
        public TableSyncDiagnostic(string source, int line, string column, string message)
        {
            Source = source ?? string.Empty;
            Line = line;
            Column = column ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public string Source { get; }
        public int Line { get; }
        public string Column { get; }
        public string Message { get; }

        public override string ToString()
        {
            return $"[{Source}] line {Line}, column '{Column}' - {Message}";
        }
    }

    public sealed class TableSyncTable
    {
        public TableSyncTable(string name, string[] header, IList<CsvRecord> records)
        {
            Name = name ?? string.Empty;
            Header = header ?? Array.Empty<string>();
            Records = records ?? Array.Empty<CsvRecord>();
        }

        public string Name { get; }
        public string[] Header { get; }
        public IList<CsvRecord> Records { get; }
    }

    public sealed class TableSyncCellChange
    {
        public TableSyncCellChange(string column, string masterValue, string modifiedValue)
        {
            Column = column;
            MasterValue = masterValue;
            ModifiedValue = modifiedValue;
        }

        public string Column { get; }
        public string MasterValue { get; }
        public string ModifiedValue { get; }
    }

    public sealed class TableSyncCellValue
    {
        public TableSyncCellValue(string column, string value)
        {
            Column = column ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string Column { get; }
        public string Value { get; }
    }

    /// <summary>
    /// CSV 행을 식별하는 Key 컬럼/값의 순서 있는 묶음. 문자열 하나로 이어 붙이지 않아 값 안의
    /// 구분자 때문에 서로 다른 복합 Key가 같은 것으로 보이는 일을 막는다.
    /// </summary>
    public sealed class TableSyncRowIdentity : IEquatable<TableSyncRowIdentity>
    {
        private readonly string[] columns;
        private readonly string[] values;

        public TableSyncRowIdentity(IList<string> columns, IList<string> values)
        {
            this.columns = columns == null ? Array.Empty<string>() : columns.ToArray();
            this.values = values == null ? Array.Empty<string>() : values.ToArray();
        }

        public IList<string> Columns => columns;
        public IList<string> Values => values;
        public string DisplayText => string.Join(" / ", values);

        public bool Equals(TableSyncRowIdentity other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || columns.Length != other.columns.Length || values.Length != other.values.Length) return false;
            for (int i = 0; i < columns.Length; i++)
            {
                if (!string.Equals(columns[i], other.columns[i], StringComparison.Ordinal) ||
                    !string.Equals(values[i], other.values[i], StringComparison.Ordinal)) return false;
            }

            return true;
        }

        public override bool Equals(object obj) => Equals(obj as TableSyncRowIdentity);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < columns.Length; i++)
                {
                    hash = hash * 31 + (columns[i] ?? string.Empty).GetHashCode();
                    hash = hash * 31 + (values[i] ?? string.Empty).GetHashCode();
                }

                return hash;
            }
        }
    }

    public sealed class TableSyncRowChange
    {
        public TableSyncRowChange(TableSyncChangeKind kind, string primaryKey, int masterLine, int modifiedLine,
            IList<TableSyncCellChange> cellChanges)
            : this(kind, new TableSyncRowIdentity(new[] { "(primary key)" }, new[] { primaryKey ?? string.Empty }), masterLine, modifiedLine, cellChanges, null)
        {
        }

        public TableSyncRowChange(TableSyncChangeKind kind, TableSyncRowIdentity identity, int masterLine, int modifiedLine,
            IList<TableSyncCellChange> cellChanges, IList<TableSyncCellValue> rowValues = null)
        {
            Kind = kind;
            Identity = identity;
            PrimaryKey = identity == null ? string.Empty : identity.DisplayText;
            MasterLine = masterLine;
            ModifiedLine = modifiedLine;
            CellChanges = cellChanges ?? Array.Empty<TableSyncCellChange>();
            RowValues = rowValues ?? Array.Empty<TableSyncCellValue>();
        }

        public TableSyncChangeKind Kind { get; }
        public string PrimaryKey { get; }
        public TableSyncRowIdentity Identity { get; }
        public int MasterLine { get; }
        public int ModifiedLine { get; }
        public IList<TableSyncCellChange> CellChanges { get; }
        public IList<TableSyncCellValue> RowValues { get; }
    }

    public sealed class TableSyncDiffResult
    {
        public readonly List<TableSyncDiagnostic> Diagnostics = new List<TableSyncDiagnostic>();
        public readonly List<TableSyncRowChange> Changes = new List<TableSyncRowChange>();

        public bool IsValid => Diagnostics.Count == 0;
        public int AddCount => Changes.Count(change => change.Kind == TableSyncChangeKind.Add);
        public int UpdateCount => Changes.Count(change => change.Kind == TableSyncChangeKind.Update);
        public int PossibleDeleteCount => Changes.Count(change => change.Kind == TableSyncChangeKind.PossibleDelete);
        public int UnchangedCount => Changes.Count(change => change.Kind == TableSyncChangeKind.Unchanged);
    }

    /// <summary>
    /// 헤더 이름과 Primary Key로 두 CSV 스냅샷을 비교한다. 스키마/키 오류가 하나라도 있으면 결과를
    /// 만들지 않아, 잘못된 입력이 정상 Diff처럼 보이지 않게 한다.
    /// </summary>
    public static class TableSyncDiffEngine
    {
        public static TableSyncDiffResult Compare(TableSyncTable master, TableSyncTable modified, string primaryKeyColumn)
        {
            return Compare(master, modified, new[] { primaryKeyColumn });
        }

        public static TableSyncDiffResult Compare(TableSyncTable master, TableSyncTable modified, IList<string> primaryKeyColumns)
        {
            var result = new TableSyncDiffResult();
            if (master == null || modified == null)
            {
                result.Diagnostics.Add(new TableSyncDiagnostic("Compare", 0, "(file)", "MASTER와 MODIFIED CSV를 모두 선택하세요."));
                return result;
            }

            string[] primaryKeys = (primaryKeyColumns ?? Array.Empty<string>())
                .Select(column => (column ?? string.Empty).Trim()).ToArray();
            if (primaryKeys.Length == 0 || primaryKeys.Any(column => column.Length == 0))
            {
                result.Diagnostics.Add(new TableSyncDiagnostic("Compare", 1, "(primary key)", "Primary Key 컬럼을 지정하세요."));
                return result;
            }

            Dictionary<string, int> masterColumns = ValidateHeader(master, result.Diagnostics, "MASTER");
            Dictionary<string, int> modifiedColumns = ValidateHeader(modified, result.Diagnostics, "MODIFIED");
            if (masterColumns == null || modifiedColumns == null) return result;

            foreach (string primaryKey in primaryKeys)
            {
                if (!masterColumns.ContainsKey(primaryKey))
                    result.Diagnostics.Add(new TableSyncDiagnostic("MASTER", 1, primaryKey, "Primary Key 컬럼이 없습니다."));
                if (!modifiedColumns.ContainsKey(primaryKey))
                    result.Diagnostics.Add(new TableSyncDiagnostic("MODIFIED", 1, primaryKey, "Primary Key 컬럼이 없습니다."));
            }

            AddSchemaDifferences(masterColumns, modifiedColumns, result.Diagnostics);
            if (result.Diagnostics.Count > 0) return result;

            Dictionary<TableSyncRowIdentity, CsvRecord> masterRows = IndexRows(master, masterColumns, primaryKeys, result.Diagnostics, "MASTER");
            Dictionary<TableSyncRowIdentity, CsvRecord> modifiedRows = IndexRows(modified, modifiedColumns, primaryKeys, result.Diagnostics, "MODIFIED");
            if (result.Diagnostics.Count > 0) return result;

            foreach (CsvRecord modifiedRow in modified.Records)
            {
                TableSyncRowIdentity identity = ReadIdentity(modifiedRow, modifiedColumns, primaryKeys);
                if (!masterRows.TryGetValue(identity, out CsvRecord masterRow))
                {
                    result.Changes.Add(new TableSyncRowChange(TableSyncChangeKind.Add, identity, 0, modifiedRow.Line, null,
                        ReadRowValues(modifiedRow, modified.Header)));
                    continue;
                }

                List<TableSyncCellChange> cells = FindCellChanges(masterRow, modifiedRow, master.Header, masterColumns, modifiedColumns, primaryKeys);
                result.Changes.Add(cells.Count == 0
                    ? new TableSyncRowChange(TableSyncChangeKind.Unchanged, identity, masterRow.Line, modifiedRow.Line, cells,
                        ReadRowValues(modifiedRow, modified.Header))
                    : new TableSyncRowChange(TableSyncChangeKind.Update, identity, masterRow.Line, modifiedRow.Line, cells,
                        ReadRowValues(modifiedRow, modified.Header)));
            }

            foreach (CsvRecord masterRow in master.Records)
            {
                TableSyncRowIdentity identity = ReadIdentity(masterRow, masterColumns, primaryKeys);
                if (!modifiedRows.ContainsKey(identity))
                    result.Changes.Add(new TableSyncRowChange(TableSyncChangeKind.PossibleDelete, identity, masterRow.Line, 0, null,
                        ReadRowValues(masterRow, master.Header)));
            }

            return result;
        }

        private static Dictionary<string, int> ValidateHeader(TableSyncTable table, List<TableSyncDiagnostic> diagnostics, string source)
        {
            if (table.Header.Length == 0)
            {
                diagnostics.Add(new TableSyncDiagnostic(source, 1, "(header)", "Header가 없습니다."));
                return null;
            }

            var columns = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < table.Header.Length; i++)
            {
                string header = table.Header[i] ?? string.Empty;
                if (header.Length == 0)
                    diagnostics.Add(new TableSyncDiagnostic(source, 1, "(header)", "빈 Header 컬럼이 있습니다."));
                else if (columns.ContainsKey(header))
                    diagnostics.Add(new TableSyncDiagnostic(source, 1, header, "Header 컬럼이 중복되었습니다."));
                else
                    columns.Add(header, i);
            }

            return columns;
        }

        private static void AddSchemaDifferences(Dictionary<string, int> master, Dictionary<string, int> modified,
            List<TableSyncDiagnostic> diagnostics)
        {
            foreach (string column in master.Keys.Where(column => !modified.ContainsKey(column)))
                diagnostics.Add(new TableSyncDiagnostic("Schema", 1, column, "MODIFIED에 없는 MASTER 컬럼입니다."));
            foreach (string column in modified.Keys.Where(column => !master.ContainsKey(column)))
                diagnostics.Add(new TableSyncDiagnostic("Schema", 1, column, "MASTER에 없는 MODIFIED 컬럼입니다."));
        }

        private static Dictionary<TableSyncRowIdentity, CsvRecord> IndexRows(TableSyncTable table,
            Dictionary<string, int> columns, string[] primaryKeys, List<TableSyncDiagnostic> diagnostics, string source)
        {
            var indexed = new Dictionary<TableSyncRowIdentity, CsvRecord>();
            foreach (CsvRecord record in table.Records)
            {
                if (record.Fields == null || record.Fields.Length != table.Header.Length)
                {
                    diagnostics.Add(new TableSyncDiagnostic(source, record.Line, "(row)",
                        $"필드 수가 Header와 다릅니다 (Header {table.Header.Length}개, row {record.Fields?.Length ?? 0}개)."));
                    continue;
                }

                TableSyncRowIdentity identity = ReadIdentity(record, columns, primaryKeys);
                int blankIndex = identity.Values.ToList().FindIndex(value => string.IsNullOrEmpty(value));
                if (blankIndex >= 0)
                {
                    diagnostics.Add(new TableSyncDiagnostic(source, record.Line, primaryKeys[blankIndex], "Primary Key 값이 비어 있습니다."));
                    continue;
                }

                if (indexed.ContainsKey(identity))
                    diagnostics.Add(new TableSyncDiagnostic(source, record.Line, string.Join(" + ", primaryKeys), $"Primary Key '{identity.DisplayText}'가 중복되었습니다."));
                else
                    indexed.Add(identity, record);
            }

            return indexed;
        }

        private static List<TableSyncCellChange> FindCellChanges(CsvRecord master, CsvRecord modified, string[] masterHeader,
            Dictionary<string, int> masterColumns, Dictionary<string, int> modifiedColumns, IList<string> primaryKeys)
        {
            var changes = new List<TableSyncCellChange>();
            foreach (string column in masterHeader)
            {
                if (primaryKeys.Contains(column)) continue;
                string masterValue = master.Fields[masterColumns[column]] ?? string.Empty;
                string modifiedValue = modified.Fields[modifiedColumns[column]] ?? string.Empty;
                if (!string.Equals(masterValue, modifiedValue, StringComparison.Ordinal))
                    changes.Add(new TableSyncCellChange(column, masterValue, modifiedValue));
            }

            return changes;
        }

        private static TableSyncRowIdentity ReadIdentity(CsvRecord record, Dictionary<string, int> columns, IList<string> primaryKeys)
        {
            var values = new string[primaryKeys.Count];
            for (int i = 0; i < primaryKeys.Count; i++) values[i] = record.Fields[columns[primaryKeys[i]]] ?? string.Empty;
            return new TableSyncRowIdentity(primaryKeys, values);
        }

        private static List<TableSyncCellValue> ReadRowValues(CsvRecord record, string[] header)
        {
            var values = new List<TableSyncCellValue>();
            for (int i = 0; i < header.Length; i++) values.Add(new TableSyncCellValue(header[i], record.Fields[i] ?? string.Empty));
            return values;
        }
    }
}
