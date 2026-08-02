using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TableDataEditor
{
    /// <summary>레코드 하나. <see cref="Line"/>은 이 레코드가 <b>실제로 시작한</b> 1-기반 행 번호다.</summary>
    public sealed class CsvRecord
    {
        public CsvRecord(int line, string[] fields)
        {
            Line = line;
            Fields = fields;
        }

        public int Line { get; }

        public string[] Fields { get; }
    }

    /// <summary>
    /// 파싱과 헤더 검증을 마친 CSV 한 장. 이후 검증기는 컬럼을 <b>이름으로만</b> 읽는다 - 인덱스를
    /// 직접 쓰면 컬럼 순서를 바꿨을 때 조용히 다른 값을 읽게 되기 때문이다.
    /// </summary>
    public sealed class CsvTable
    {
        private readonly Dictionary<string, int> columnIndex;

        public CsvTable(string fileName, string[] header, List<CsvRecord> records)
        {
            FileName = fileName;
            Header = header;
            Records = records;

            columnIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < header.Length; i++)
            {
                if (!columnIndex.ContainsKey(header[i])) columnIndex[header[i]] = i;
            }
        }

        public string FileName { get; }

        public string[] Header { get; }

        public List<CsvRecord> Records { get; }

        /// <summary>컬럼 값을 원본 그대로 돌려준다. 컬럼이 없거나 칸이 모자라면 빈 문자열이다 -
        /// <b>여기서 Trim하지 않는다</b>. 공백을 다듬을지는 검증 규칙마다 다르고, 다듬은 값을 원본처럼
        /// 다루면 "공백이 들어 있다"는 경고를 낼 수 없다.</summary>
        public string Get(CsvRecord record, string column)
        {
            if (record == null) return string.Empty;
            if (!columnIndex.TryGetValue(column, out int index)) return string.Empty;
            if (index >= record.Fields.Length) return string.Empty;
            return record.Fields[index] ?? string.Empty;
        }
    }

    /// <summary>
    /// RFC4180 수준의 CSV 파서. <b><c>Split(',')</c>을 쓰지 않는다</b> - 따옴표로 감싼 필드 안의
    /// 쉼표/줄바꿈/이스케이프된 따옴표를 잘못 자르면 데이터가 조용히 어긋나기 때문이다.
    ///
    /// 지원하는 것: 따옴표 필드, <c>""</c> 이스케이프, 따옴표 안의 쉼표와 줄바꿈, CRLF/LF/CR 혼용,
    /// 파일 끝의 개행 유무. <b>빈 줄은 레코드로 만들지 않는다</b>(표 사이의 여백은 흔하고 의미가 없다).
    ///
    /// 잘못된 모양은 조용히 넘기지 않고 오류로 보고한다 - 따옴표를 닫지 않은 채 파일이 끝나는 경우,
    /// 따옴표를 닫은 뒤에 값이 더 붙는 경우(<c>"ab"c</c>), 따옴표로 시작하지 않은 필드 안에 따옴표가
    /// 나오는 경우(<c>ab"c</c>)다. 셋 다 사람이 손으로 편집하다 만드는 실수이고, 관대하게 해석하면
    /// 의도와 다른 값이 그대로 에셋에 들어간다.
    /// </summary>
    public static class CsvParser
    {
        /// <summary>
        /// 텍스트를 레코드 목록으로 파싱한다. 실패하면 false를 돌려주고 <paramref name="error"/>와
        /// <paramref name="errorLine"/>에 사람이 읽을 수 있는 원인과 행 번호를 채운다.
        /// </summary>
        public static bool TryParse(string text, out List<CsvRecord> records, out string error, out int errorLine)
        {
            records = new List<CsvRecord>();
            error = null;
            errorLine = 0;

            if (string.IsNullOrEmpty(text)) return true;

            var fields = new List<string>();
            var field = new StringBuilder();

            int line = 1;
            int recordLine = 1;
            bool inQuotes = false;
            bool quotedField = false;

            // 따옴표로 필드를 닫은 직후인지. 닫은 뒤에는 구분자(,)나 줄바꿈만 올 수 있고, 값이 더 붙으면
            // (예: "ab"c) 조용히 이어 붙이지 않고 오류로 잡는다.
            bool quoteClosed = false;
            bool fieldStarted = false;
            bool recordStarted = false;

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            // "" -> 값에 따옴표 한 개.
                            field.Append('"');
                            i += 2;
                            continue;
                        }

                        inQuotes = false;
                        quoteClosed = true;
                        i++;
                        continue;
                    }

                    if (c == '\r')
                    {
                        // 따옴표 안의 줄바꿈은 값의 일부다. 원본 개행 형태와 무관하게 \n으로 모은다.
                        field.Append('\n');
                        line++;
                        i += (i + 1 < text.Length && text[i + 1] == '\n') ? 2 : 1;
                        continue;
                    }

                    if (c == '\n')
                    {
                        field.Append('\n');
                        line++;
                        i++;
                        continue;
                    }

                    field.Append(c);
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    if (fieldStarted)
                    {
                        error = quotedField
                            ? "따옴표로 닫은 필드 뒤에 값이 더 있습니다(예: \"ab\"c). 값 안의 따옴표는 \"\"로 적으세요."
                            : "따옴표로 시작하지 않은 필드 안에 따옴표가 있습니다(예: ab\"c). 필드 전체를 따옴표로 감싸고 값 안의 따옴표는 \"\"로 적으세요.";
                        errorLine = line;
                        return false;
                    }

                    inQuotes = true;
                    quotedField = true;
                    fieldStarted = true;
                    recordStarted = true;
                    i++;
                    continue;
                }

                if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Length = 0;
                    fieldStarted = false;
                    quotedField = false;
                    quoteClosed = false;
                    recordStarted = true;
                    i++;
                    continue;
                }

                if (c == '\r' || c == '\n')
                {
                    int advance = 1;
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') advance = 2;

                    fields.Add(field.ToString());
                    field.Length = 0;

                    CommitRecord(records, fields, recordLine, recordStarted);

                    fields.Clear();
                    fieldStarted = false;
                    quotedField = false;
                    quoteClosed = false;
                    recordStarted = false;
                    line++;
                    recordLine = line;
                    i += advance;
                    continue;
                }

                if (quoteClosed)
                {
                    error = "따옴표로 닫은 필드 뒤에 값이 더 있습니다(예: \"ab\"c). 값 안의 따옴표는 \"\"로 적으세요.";
                    errorLine = line;
                    return false;
                }

                field.Append(c);
                fieldStarted = true;
                recordStarted = true;
                i++;
            }

            if (inQuotes)
            {
                error = "따옴표를 닫지 않은 채 파일이 끝났습니다.";
                errorLine = recordLine;
                return false;
            }

            // 파일이 개행으로 끝나면 마지막 레코드는 위에서 이미 확정됐다. 그 경우 fields는 비어 있고
            // recordStarted도 false라 아래에서 아무것도 추가하지 않는다.
            if (fieldStarted || fields.Count > 0)
            {
                fields.Add(field.ToString());
                CommitRecord(records, fields, recordLine, recordStarted);
            }

            return true;
        }

        /// <summary>완전히 빈 줄(칸 하나, 값 없음)은 레코드로 만들지 않는다.</summary>
        private static void CommitRecord(List<CsvRecord> records, List<string> fields, int line, bool recordStarted)
        {
            if (!recordStarted && fields.Count <= 1 && (fields.Count == 0 || fields[0].Length == 0)) return;

            records.Add(new CsvRecord(line, fields.ToArray()));
        }

        /// <summary>
        /// UTF-8로만 읽는다. BOM은 허용하고(있으면 떼어낸다), <b>UTF-8이 아닌 바이트열은 오류</b>다 -
        /// 조용히 대체 문자로 바꿔 읽으면 깨진 ID가 그대로 에셋 이름이 된다.
        /// </summary>
        public static bool TryReadUtf8(string fullPath, out string text, out string error)
        {
            text = null;
            error = null;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(fullPath);
            }
            catch (Exception e)
            {
                error = "파일을 읽지 못했습니다: " + e.Message;
                return false;
            }

            int offset = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) offset = 3;

            try
            {
                var strict = new UTF8Encoding(false, true);
                text = strict.GetString(bytes, offset, bytes.Length - offset);
                return true;
            }
            catch (DecoderFallbackException)
            {
                error = "UTF-8로 읽을 수 없는 바이트가 있습니다 - 파일을 UTF-8(BOM 있어도 됨)로 다시 저장하세요.";
                return false;
            }
        }
    }
}
