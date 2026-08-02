using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TableDataEditor
{
    /// <summary>
    /// CSV 세 장의 <b>컬럼 이름</b>. 3단계에서 만들 CSV는 이 이름을 그대로 써야 한다 -
    /// 헤더가 어긋나면 값을 추측해 읽지 않고 오류로 멈춘다.
    /// </summary>
    public static class TableDataColumns
    {
        // 공통
        public const string DisplayOrder = "display_order";
        public const string Enabled = "enabled";
        public const string Memo = "memo";
        public const string NameCategory = "name_category";
        public const string NameKey = "name_key";
        public const string WorldId = "world_id";

        // World.csv
        public static readonly string[] World =
        {
            WorldId, NameCategory, NameKey, DisplayOrder, Enabled, Memo,
        };

        // Monster.csv
        public const string MonsterId = "monster_id";
        public const string MotionProfileKey = "motion_profile_key";
        public const string PreviewSpriteKey = "preview_sprite_key";
        public const string MaxDurability = "max_durability";

        public static readonly string[] Monster =
        {
            MonsterId, NameCategory, NameKey, WorldId, MotionProfileKey, PreviewSpriteKey,
            MaxDurability, DisplayOrder, Enabled, Memo,
        };

        // Dungeon.csv
        public const string DungeonId = "dungeon_id";
        public const string RepresentativeSpriteKey = "representative_sprite_key";
        public const string MonsterIds = "monster_ids";
        public const string RewardItemIds = "reward_item_ids";

        public static readonly string[] Dungeon =
        {
            DungeonId, NameCategory, NameKey, WorldId, RepresentativeSpriteKey, MonsterIds,
            RewardItemIds, DisplayOrder, Enabled, Memo,
        };

        /// <summary>
        /// 작업자용 참조 컬럼의 머리글자. 헤더가 이 글자로 시작하면 <b>런타임 컬럼이 아니라 사람이
        /// 보라고 붙인 칸</b>이다(스프레드시트 함수로 채운 몬스터명/월드명 등). 임포터는 이 칸의 값을
        /// 읽지도, 검증하지도, 생성 에셋에 넣지도 않는다.
        /// </summary>
        public const string ReferenceOnlyPrefix = "$";

        /// <summary>컬럼이 아니라 파일 자체가 문제일 때 쓰는 컬럼 이름.</summary>
        public const string FilePseudoColumn = "(file)";

        /// <summary>헤더 줄 전체가 문제일 때 쓰는 컬럼 이름.</summary>
        public const string HeaderPseudoColumn = "(header)";
    }

    /// <summary>
    /// CSV 한 장을 읽어 <see cref="CsvTable"/>로 만든다. 파일 없음 / 인코딩 / 파싱 / 헤더 스키마까지가
    /// 여기 책임이고, 값의 의미는 보지 않는다.
    ///
    /// <b>여기서 실패하면 그 파일의 행 검증은 하지 않는다.</b> 헤더가 틀린 채로 행을 읽으면 엉뚱한
    /// 컬럼을 가리키는 오류가 수백 개 쏟아져 진짜 원인이 묻히기 때문이다. 대신 나머지 두 파일은
    /// 계속 읽는다 - Validate의 목적은 한 번에 모든 문제를 보여주는 것이다.
    ///
    /// <b><c>$</c>로 시작하는 헤더는 작업자용 참조 컬럼</b>이라 알려진 컬럼이 아니어도 통과시킨다
    /// (<see cref="IsReferenceOnlyColumn"/>). 다만 <b>헤더에서 지우지는 않는다</b> - 행의 칸 수는
    /// 참조 컬럼까지 포함한 전체 헤더 수와 맞아야 하고, 헤더에서 빼 버리면 값이 한 칸씩 밀린 행을
    /// 정상으로 읽게 된다. 값을 실제로 무시하는 것은 이후 단계가 컬럼을 <b>이름으로만</b> 읽기
    /// 때문이다 - 참조 컬럼 이름을 묻는 곳이 없으므로 스냅샷에도 생성 에셋에도 남지 않는다.
    /// </summary>
    public static class TableDataCsvReader
    {
        /// <summary>읽기에 실패하면 null을 돌려주고 원인을 <paramref name="log"/>에 남긴다.</summary>
        public static CsvTable Read(string assetPath, string fileName, string[] expectedColumns, TableDataDiagnosticLog log)
        {
            string fullPath = ToFullPath(assetPath);

            if (!File.Exists(fullPath))
            {
                log.Error(fileName, TableDataDiagnostic.FileLevelRow, TableDataColumns.FilePseudoColumn, assetPath,
                    "CSV 파일이 없습니다 - 이 경로에 UTF-8 CSV를 두세요.");
                return null;
            }

            if (!CsvParser.TryReadUtf8(fullPath, out string text, out string readError))
            {
                log.Error(fileName, TableDataDiagnostic.FileLevelRow, TableDataColumns.FilePseudoColumn, assetPath, readError);
                return null;
            }

            if (!CsvParser.TryParse(text, out List<CsvRecord> records, out string parseError, out int errorLine))
            {
                log.Error(fileName, errorLine, TableDataColumns.FilePseudoColumn, assetPath, "CSV 형식 오류: " + parseError);
                return null;
            }

            if (records.Count == 0)
            {
                log.Error(fileName, TableDataDiagnostic.FileLevelRow, TableDataColumns.FilePseudoColumn, assetPath,
                    "파일이 비어 있습니다 - 최소한 헤더 한 줄이 있어야 합니다.");
                return null;
            }

            CsvRecord headerRecord = records[0];
            string[] header = NormalizeHeader(headerRecord.Fields);
            records.RemoveAt(0);

            if (!ValidateHeader(fileName, header, expectedColumns, log)) return null;

            var table = new CsvTable(fileName, header, records);

            // 헤더는 맞는데 칸 수가 다른 줄은 값이 밀려 들어간 것이므로 행 단위로 보고한다.
            for (int i = table.Records.Count - 1; i >= 0; i--)
            {
                CsvRecord record = table.Records[i];
                if (record.Fields.Length == header.Length) continue;

                log.Error(fileName, record.Line, TableDataColumns.HeaderPseudoColumn,
                    record.Fields.Length.ToString(CultureInfo.InvariantCulture),
                    $"칸 수가 헤더와 다릅니다(헤더 {header.Length}칸, 이 행 {record.Fields.Length}칸) - 이 행은 건너뜁니다.");
                table.Records.RemoveAt(i);
            }

            return table;
        }

        /// <summary>헤더 칸의 앞뒤 공백만 다듬는다. 값과 달리 헤더 공백은 의미가 없고, 여기서 다듬지
        /// 않으면 편집기가 넣은 공백 하나로 파일 전체가 실패한다.</summary>
        private static string[] NormalizeHeader(string[] fields)
        {
            var header = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++) header[i] = (fields[i] ?? string.Empty).Trim();
            return header;
        }

        private static bool ValidateHeader(string fileName, string[] header, string[] expected, TableDataDiagnosticLog log)
        {
            bool ok = true;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);

            for (int i = 0; i < header.Length; i++)
            {
                string column = header[i];

                if (column.Length == 0)
                {
                    log.Error(fileName, TableDataDiagnostic.HeaderRow, TableDataColumns.HeaderPseudoColumn,
                        string.Join(",", header), $"{i + 1}번째 헤더 칸이 비어 있습니다.");
                    ok = false;
                    continue;
                }

                if (!seen.Add(column))
                {
                    log.Error(fileName, TableDataDiagnostic.HeaderRow, column, column,
                        "같은 컬럼 이름이 두 번 있습니다 - 어느 칸을 읽어야 할지 정할 수 없습니다.");
                    ok = false;
                    continue;
                }

                // 참조 컬럼은 이름이 정해져 있지 않으므로 알려진 컬럼 목록과 대조하지 않는다. 중복 검사는
                // 위에서 이미 지났다 - 같은 이름이 두 번 있으면 참조 컬럼이라도 어느 칸인지 정할 수 없다.
                if (IsReferenceOnlyColumn(column)) continue;

                if (HasReferencePrefix(column))
                {
                    log.Error(fileName, TableDataDiagnostic.HeaderRow, column, column,
                        $"'{TableDataColumns.ReferenceOnlyPrefix}' 뒤에 이름이 없습니다 - 참조 컬럼은 " +
                        $"'{TableDataColumns.ReferenceOnlyPrefix}monster_name'처럼 이름을 붙여야 합니다.");
                    ok = false;
                    continue;
                }

                if (!expectedSet.Contains(column))
                {
                    log.Error(fileName, TableDataDiagnostic.HeaderRow, column, column,
                        "알 수 없는 컬럼입니다 - 오타를 조용히 무시하지 않기 위해 오류로 처리합니다. " +
                        "허용 컬럼: " + string.Join(", ", expected) + ". " +
                        $"작업자용 참조 컬럼이라면 이름 앞에 '{TableDataColumns.ReferenceOnlyPrefix}'를 붙이세요.");
                    ok = false;
                }
            }

            foreach (string column in expected)
            {
                if (seen.Contains(column)) continue;

                log.Error(fileName, TableDataDiagnostic.HeaderRow, column, string.Join(",", header),
                    "필수 컬럼이 없습니다.");
                ok = false;
            }

            return ok;
        }

        /// <summary>
        /// 이 헤더가 <b>작업자용 참조 컬럼</b>인지. 조건은 두 가지다 - <c>$</c>로 시작할 것, 그리고
        /// <c>$</c> 뒤에 실제 이름이 있을 것. 이름 없는 <c>$</c> 하나짜리 헤더를 참조 컬럼으로 봐 주면
        /// 헤더 오타 하나가 검사 없이 통과하므로 <b>일부러 걸러 낸다</b>.
        ///
        /// 참조 컬럼은 CSV의 <b>어느 위치에 있어도</b> 된다. 위치가 아니라 이름으로 판정하고, 이후
        /// 단계도 컬럼을 이름으로만 읽기 때문이다.
        /// </summary>
        public static bool IsReferenceOnlyColumn(string column)
        {
            if (!HasReferencePrefix(column)) return false;

            return column.Substring(TableDataColumns.ReferenceOnlyPrefix.Length).Trim().Length > 0;
        }

        /// <summary>이름이 있든 없든 <c>$</c>로 시작하는지. "참조 컬럼처럼 쓰려다 만 헤더"를 알 수 없는
        /// 컬럼이 아니라 그에 맞는 오류로 안내하기 위해 나눠 둔다.</summary>
        private static bool HasReferencePrefix(string column)
        {
            return !string.IsNullOrEmpty(column)
                   && column.StartsWith(TableDataColumns.ReferenceOnlyPrefix, StringComparison.Ordinal);
        }

        /// <summary>"Assets/..." 프로젝트 상대 경로를 실제 파일 경로로 바꾼다.</summary>
        public static string ToFullPath(string assetPath)
        {
            // Application.dataPath는 ".../Assets"이므로 프로젝트 루트를 기준으로 합친다.
            string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot)
                ? assetPath
                : Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
