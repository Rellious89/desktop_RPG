using System;
using System.Collections.Generic;
using System.IO;
using TableDataEditor;

namespace TableSyncEditor
{
    /// <summary>기존 TableData CSV parser를 재사용해 비교 전용 테이블 스냅샷을 만든다.</summary>
    public static class TableSyncCsvReader
    {
        public static bool TryReadFile(string path, out TableSyncTable table, out TableSyncDiagnostic diagnostic)
        {
            table = null;
            diagnostic = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                diagnostic = new TableSyncDiagnostic("Input", 0, "(file)", "선택한 CSV 파일을 찾을 수 없습니다.");
                return false;
            }

            if (!CsvParser.TryReadUtf8(path, out string text, out string readError))
            {
                diagnostic = new TableSyncDiagnostic(Path.GetFileName(path), 0, "(file)", readError);
                return false;
            }

            return TryReadText(Path.GetFileName(path), text, out table, out diagnostic);
        }

        public static bool TryReadText(string name, string text, out TableSyncTable table, out TableSyncDiagnostic diagnostic)
        {
            table = null;
            diagnostic = null;
            if (!CsvParser.TryParse(text, out List<CsvRecord> records, out string parseError, out int errorLine))
            {
                diagnostic = new TableSyncDiagnostic(name, errorLine, "(csv)", "CSV 형식 오류: " + parseError);
                return false;
            }

            if (records.Count == 0)
            {
                diagnostic = new TableSyncDiagnostic(name, 1, "(header)", "Header가 없습니다.");
                return false;
            }

            string[] header = new string[records[0].Fields.Length];
            for (int i = 0; i < header.Length; i++) header[i] = (records[0].Fields[i] ?? string.Empty).Trim();
            records.RemoveAt(0);
            table = new TableSyncTable(name, header, records);
            return true;
        }
    }
}
