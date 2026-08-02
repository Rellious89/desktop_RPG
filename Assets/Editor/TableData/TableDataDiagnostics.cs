using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TableDataEditor
{
    /// <summary>진단의 심각도. Error가 하나라도 있으면 Rebuild는 <b>아무것도 쓰지 않고</b> 중단한다.</summary>
    public enum TableDataSeverity
    {
        Warning = 0,
        Error = 1,
    }

    /// <summary>
    /// 진단 한 건. <b>여섯 가지를 항상 채운다</b> - 심각도, CSV 파일 이름, 행 번호, 컬럼 이름, 문제가 된
    /// 값, 설명. 파일 자체나 헤더 문제처럼 "행"이 없는 경우에도 빈칸을 남기지 않고 합리적인 값을 넣는다
    /// (파일 문제는 <see cref="FileLevelRow"/>, 헤더 문제는 <see cref="HeaderRow"/>).
    ///
    /// CSV에서 사라진 생성 에셋(orphan)은 "행"이 존재하지 않으므로 <see cref="FileLevelRow"/>에
    /// 해당 CSV 파일 이름과 ID 컬럼 이름을 적고, 값에는 <b>에셋이 실제로 들고 있는 ID</b>를 넣는다.
    /// </summary>
    public sealed class TableDataDiagnostic
    {
        /// <summary>행이 없는 진단(파일 없음, 인코딩 오류, orphan 에셋 등)에 쓰는 행 번호.</summary>
        public const int FileLevelRow = 0;

        /// <summary>헤더 줄의 행 번호. CSV의 첫 줄이다.</summary>
        public const int HeaderRow = 1;

        public TableDataDiagnostic(
            TableDataSeverity severity,
            string file,
            int row,
            string column,
            string value,
            string message)
        {
            Severity = severity;
            File = file ?? string.Empty;
            Row = row;
            Column = column ?? string.Empty;
            Value = value ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public TableDataSeverity Severity { get; }

        /// <summary>CSV 파일 이름(경로가 아니라 "World.csv" 같은 이름).</summary>
        public string File { get; }

        /// <summary>레코드가 <b>실제로 시작한</b> 1-기반 행 번호. 따옴표 안의 줄바꿈이 있어도 화면에서
        /// 눈으로 찾을 수 있는 줄을 가리킨다. 행이 없는 진단은 <see cref="FileLevelRow"/>.</summary>
        public int Row { get; }

        public string Column { get; }

        /// <summary>문제가 된 값 원본. 공백을 다듬기 전 값을 넣어야 "왜 걸렸는지"가 보인다.</summary>
        public string Value { get; }

        public string Message { get; }

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append(Severity == TableDataSeverity.Error ? "[Error] " : "[Warning] ");
            builder.Append(File);
            builder.Append(Row > 0 ? " row " + Row.ToString(CultureInfo.InvariantCulture) : " (file)");
            builder.Append(", column '").Append(Column).Append('\'');
            builder.Append(", value '").Append(Value).Append('\'');
            builder.Append(" - ").Append(Message);
            return builder.ToString();
        }
    }

    /// <summary>
    /// 진단을 모으는 자리. <b>첫 오류에서 멈추지 않는다</b> - Validate는 세 파일을 끝까지 읽고 모든
    /// 문제를 한 번에 보여주는 것이 목적이라, 여기서는 수집만 하고 흐름 제어는 호출하는 쪽이 한다.
    /// </summary>
    public sealed class TableDataDiagnosticLog
    {
        private readonly List<TableDataDiagnostic> entries = new List<TableDataDiagnostic>();

        public IReadOnlyList<TableDataDiagnostic> Entries => entries;

        public int ErrorCount { get; private set; }

        public int WarningCount { get; private set; }

        public bool HasErrors => ErrorCount > 0;

        public void Error(string file, int row, string column, string value, string message)
        {
            Add(new TableDataDiagnostic(TableDataSeverity.Error, file, row, column, value, message));
        }

        public void Warning(string file, int row, string column, string value, string message)
        {
            Add(new TableDataDiagnostic(TableDataSeverity.Warning, file, row, column, value, message));
        }

        public void Add(TableDataDiagnostic diagnostic)
        {
            if (diagnostic == null) return;

            entries.Add(diagnostic);
            if (diagnostic.Severity == TableDataSeverity.Error) ErrorCount++;
            else WarningCount++;
        }
    }
}
