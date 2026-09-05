using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TableSyncEditor
{
    public enum TableSyncGitChangeKind { Modified, Added, Deleted }

    public sealed class TableSyncGitFileChange
    {
        public TableSyncGitFileChange(string relativePath, TableSyncGitChangeKind kind)
        {
            RelativePath = relativePath ?? string.Empty;
            Kind = kind;
        }

        public string RelativePath { get; }
        public TableSyncGitChangeKind Kind { get; }
    }

    public interface ITableSyncGitReader
    {
        bool TryGetRepositoryRoot(out string root, out string error);
        bool TryEnsureHead(out string error);
        bool TryGetChanges(string root, out List<TableSyncGitFileChange> changes, out string error);
        bool TryReadHeadFile(string root, string relativePath, out string text, out string error);
        bool TryReadWorkingFile(string root, string relativePath, out string text, out string error);
    }

    /// <summary>KeyBuddy의 실제 CSV만 대상으로 하는 작고 고정된 행 식별자 목록.</summary>
    public static class TableSyncKeyBuddyTableMap
    {
        private static readonly Dictionary<string, string[]> GameKeys = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "Building.csv", new[] { "building_id" } }, { "Character.csv", new[] { "character_id" } },
            { "CharacterAcquisition.csv", new[] { "acquisition_id" } }, { "CharacterSkill.csv", new[] { "character_id", "skill_id" } },
            { "CharacterStoryQuest.csv", new[] { "quest_id" } }, { "CharacterStoryQuestObjective.csv", new[] { "objective_id" } },
            { "CharacterUnlockCondition.csv", new[] { "condition_id", "entry_id" } }, { "CorruptionConfig.csv", new[] { "config_id" } },
            { "Currency.csv", new[] { "currency_id" } }, { "Dungeon.csv", new[] { "dungeon_id" } },
            { "Item.csv", new[] { "item_id" } }, { "Monster.csv", new[] { "monster_id" } },
            { "PartyConfig.csv", new[] { "party_config_id" } }, { "PurificationConfig.csv", new[] { "purification_type_id" } },
            { "RecruitmentAccess.csv", new[] { "recruitment_access_id" } }, { "RecruitmentPool.csv", new[] { "recruitment_type_id", "pool_entry_id" } },
            { "RecruitmentType.csv", new[] { "recruitment_type_id" } }, { "Shop.csv", new[] { "shop_id" } },
            { "ShopProduct.csv", new[] { "shop_id", "item_id" } }, { "Skill.csv", new[] { "skill_id" } },
            { "World.csv", new[] { "world_id" } },
        };

        public static bool TryGetPrimaryKeyColumns(string relativePath, out string[] columns)
        {
            string normalized = Normalize(relativePath);
            if (normalized.StartsWith("TableData/Localization/", StringComparison.Ordinal))
            {
                columns = new[] { "Key" };
                return true;
            }

            if (normalized.StartsWith("Assets/TableData/Game/", StringComparison.Ordinal) &&
                GameKeys.TryGetValue(Path.GetFileName(normalized), out columns)) return true;

            columns = null;
            return false;
        }

        public static bool IsSupportedTablePath(string relativePath)
        {
            string normalized = Normalize(relativePath);
            return normalized.StartsWith("Assets/TableData/Game/", StringComparison.Ordinal) ||
                   normalized.StartsWith("TableData/Localization/", StringComparison.Ordinal);
        }

        private static string Normalize(string path) => (path ?? string.Empty).Replace('\\', '/');
    }

    public sealed class TableSyncProjectTableChange
    {
        public string RelativePath;
        public TableSyncGitChangeKind FileChangeKind;
        public TableSyncDiffResult Diff;
        public TableSyncTable DisplayTable;
        public string FileDeletionMessage;
    }

    public sealed class TableSyncProjectScanResult
    {
        public readonly List<TableSyncProjectTableChange> Tables = new List<TableSyncProjectTableChange>();
        public readonly List<TableSyncDiagnostic> Diagnostics = new List<TableSyncDiagnostic>();
        public bool IsValid => Diagnostics.Count == 0;
        public int AddCount => Tables.Sum(table => table.Diff == null ? 0 : table.Diff.AddCount);
        public int UpdateCount => Tables.Sum(table => table.Diff == null ? 0 : table.Diff.UpdateCount);
        public int DeleteCount => Tables.Sum(table => table.Diff == null ? 0 : table.Diff.PossibleDeleteCount);
    }

    /// <summary>HEAD와 Working Tree만 읽어 기존 Diff Engine에 넘긴다. Git/CSV를 쓰는 경로는 없다.</summary>
    public static class TableSyncProjectChangeScanner
    {
        public static TableSyncProjectScanResult Scan(ITableSyncGitReader git)
        {
            var result = new TableSyncProjectScanResult();
            string root = null;
            string rootError = null;
            if (git == null || !git.TryGetRepositoryRoot(out root, out rootError))
            {
                result.Diagnostics.Add(new TableSyncDiagnostic("Git", 0, "(repository)", rootError ?? "Git 저장소를 찾을 수 없습니다."));
                return result;
            }

            if (!git.TryEnsureHead(out string headError))
            {
                result.Diagnostics.Add(new TableSyncDiagnostic("Git", 0, "HEAD", headError ?? "HEAD가 없습니다."));
                return result;
            }

            if (!git.TryGetChanges(root, out List<TableSyncGitFileChange> changes, out string changesError))
            {
                result.Diagnostics.Add(new TableSyncDiagnostic("Git", 0, "(status)", changesError ?? "Git 변경 목록을 읽지 못했습니다."));
                return result;
            }

            foreach (TableSyncGitFileChange change in changes.Where(change => TableSyncKeyBuddyTableMap.IsSupportedTablePath(change.RelativePath)))
                ScanTable(git, root, change, result);
            return result;
        }

        private static void ScanTable(ITableSyncGitReader git, string root, TableSyncGitFileChange change, TableSyncProjectScanResult result)
        {
            if (change.Kind == TableSyncGitChangeKind.Deleted)
            {
                if (!TableSyncKeyBuddyTableMap.TryGetPrimaryKeyColumns(change.RelativePath, out string[] deletedKeys))
                {
                    result.Diagnostics.Add(new TableSyncDiagnostic(change.RelativePath, 1, "(primary key)", "KeyBuddy Primary Key Mapping이 없습니다."));
                    return;
                }
                string deletedHeadText = null;
                string deletedHeadError = null;
                TableSyncTable deletedHead = null;
                TableSyncDiagnostic deletedReadError = null;
                if (!git.TryReadHeadFile(root, change.RelativePath, out deletedHeadText, out deletedHeadError) ||
                    !TableSyncCsvReader.TryReadText(change.RelativePath + " (HEAD)", deletedHeadText, out deletedHead, out deletedReadError))
                {
                    result.Diagnostics.Add(deletedReadError ?? new TableSyncDiagnostic(change.RelativePath, 0, "HEAD", deletedHeadError ?? "HEAD 파일을 읽지 못했습니다."));
                    return;
                }
                var empty = new TableSyncTable(change.RelativePath + " (Working Tree 없음)", (string[])deletedHead.Header.Clone(), new List<TableDataEditor.CsvRecord>());
                result.Tables.Add(new TableSyncProjectTableChange
                {
                    RelativePath = change.RelativePath,
                    FileChangeKind = change.Kind,
                    Diff = TableSyncDiffEngine.Compare(deletedHead, empty, deletedKeys),
                    DisplayTable = deletedHead,
                    FileDeletionMessage = "CSV file deleted — Google Sheet Row DELETE로 자동 해석하지 않았습니다.",
                });
                return;
            }

            if (!TableSyncKeyBuddyTableMap.TryGetPrimaryKeyColumns(change.RelativePath, out string[] keys))
            {
                result.Diagnostics.Add(new TableSyncDiagnostic(change.RelativePath, 1, "(primary key)", "KeyBuddy Primary Key Mapping이 없습니다."));
                return;
            }

            string workingText = null;
            string workingReadError = null;
            TableSyncTable working = null;
            TableSyncDiagnostic workingError = null;
            if (!git.TryReadWorkingFile(root, change.RelativePath, out workingText, out workingReadError) ||
                !TableSyncCsvReader.TryReadText(change.RelativePath + " (Working Tree)", workingText, out working, out workingError))
            {
                result.Diagnostics.Add(workingError ?? new TableSyncDiagnostic(change.RelativePath, 0, "Working Tree", workingReadError ?? "현재 CSV를 읽지 못했습니다."));
                return;
            }

            TableSyncTable head;
            if (change.Kind == TableSyncGitChangeKind.Added)
            {
                head = new TableSyncTable(change.RelativePath + " (HEAD 없음)", (string[])working.Header.Clone(), new List<TableDataEditor.CsvRecord>());
            }
            else
            {
                string headText = null;
                string headError = null;
                TableSyncDiagnostic headReadError = null;
                if (!git.TryReadHeadFile(root, change.RelativePath, out headText, out headError) ||
                    !TableSyncCsvReader.TryReadText(change.RelativePath + " (HEAD)", headText, out head, out headReadError))
                {
                    result.Diagnostics.Add(headReadError ?? new TableSyncDiagnostic(change.RelativePath, 0, "HEAD", headError ?? "HEAD 파일을 읽지 못했습니다."));
                    return;
                }
            }

            TableSyncDiffResult diff = TableSyncDiffEngine.Compare(head, working, keys);
            result.Tables.Add(new TableSyncProjectTableChange { RelativePath = change.RelativePath, FileChangeKind = change.Kind, Diff = diff, DisplayTable = working });
        }
    }

    /// <summary>외부 shell 없이 git 실행 파일에 인수를 직접 전달하는 Unity Editor용 read-only 어댑터.</summary>
    public sealed class TableSyncGitCli : ITableSyncGitReader
    {
        public bool TryGetRepositoryRoot(out string root, out string error)
        {
            if (!TryRun(ProjectRoot, "rev-parse --show-toplevel", out root, out error)) return false;

            // Process의 표준 출력에는 줄 끝이 포함된다. 이 값을 WorkingDirectory로 그대로 넘기면
            // Unity/Mono 환경에서 존재하지 않는 경로가 되어 다음 git 명령이 실패한다.
            root = (root ?? string.Empty).Trim();
            if (root.Length > 0) return true;

            error = "git이 저장소 루트를 반환하지 않았습니다.";
            return false;
        }
        public bool TryEnsureHead(out string error) => TryRun(ProjectRoot, "rev-parse --verify HEAD", out _, out error);

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        public bool TryGetChanges(string root, out List<TableSyncGitFileChange> changes, out string error)
        {
            changes = new List<TableSyncGitFileChange>();
            if (!TryRun(root, "diff --name-status -z HEAD -- Assets/TableData/Game TableData/Localization", out string tracked, out error)) return false;
            ParseNameStatus(tracked, changes);
            if (!TryRun(root, "ls-files --others --exclude-standard -z -- Assets/TableData/Game TableData/Localization", out string untracked, out error)) return false;
            foreach (string path in untracked.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
                changes.Add(new TableSyncGitFileChange(path, TableSyncGitChangeKind.Added));
            return true;
        }

        public bool TryReadHeadFile(string root, string relativePath, out string text, out string error)
        {
            return TryRun(root, "show HEAD:" + relativePath.Replace('\\', '/'), out text, out error);
        }

        public bool TryReadWorkingFile(string root, string relativePath, out string text, out string error)
        {
            string fullPath = Path.Combine(root, relativePath);
            if (!TableDataEditor.CsvParser.TryReadUtf8(fullPath, out text, out error)) return false;
            return true;
        }

        private static void ParseNameStatus(string output, List<TableSyncGitFileChange> changes)
        {
            string[] parts = output.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                string status = parts[i];
                string path = parts[i + 1];
                if (status.StartsWith("M", StringComparison.Ordinal)) changes.Add(new TableSyncGitFileChange(path, TableSyncGitChangeKind.Modified));
                else if (status.StartsWith("A", StringComparison.Ordinal)) changes.Add(new TableSyncGitFileChange(path, TableSyncGitChangeKind.Added));
                else if (status.StartsWith("D", StringComparison.Ordinal)) changes.Add(new TableSyncGitFileChange(path, TableSyncGitChangeKind.Deleted));
            }
        }

        private static bool TryRun(string workingDirectory, string arguments, out string output, out string error)
        {
            output = string.Empty;
            error = null;
            try
            {
                var start = new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false),
                    CreateNoWindow = true,
                };
                using (Process process = Process.Start(start))
                {
                    output = process.StandardOutput.ReadToEnd();
                    string standardError = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode == 0) return true;
                    error = string.IsNullOrEmpty(standardError) ? "git 명령이 실패했습니다." : standardError.Trim();
                    return false;
                }
            }
            catch (Exception exception)
            {
                error = "git을 실행하지 못했습니다: " + exception.Message;
                return false;
            }
        }
    }
}
