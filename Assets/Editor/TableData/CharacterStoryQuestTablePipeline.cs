using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Character;
using CommonEditor;
using Dungeon;
using Quest;
using UnityEditor;
using UnityEngine;

namespace TableDataEditor
{
    /// <summary>서사 퀘스트 두 표만 읽고 생성하는 독립 범위. 기존 TableData Rebuild의 입력/출력에는
    /// 손대지 않아, 이 메뉴로 다른 Generated 도메인이 dirty 되거나 GUID가 바뀌지 않는다.</summary>
    public static class CharacterStoryQuestTablePipeline
    {
        public const string QuestCsvPath = "Assets/TableData/Game/CharacterStoryQuest.csv";
        public const string ObjectiveCsvPath = "Assets/TableData/Game/CharacterStoryQuestObjective.csv";
        public const string OutputFolder = "Assets/Generated/TableData/CharacterStoryQuest";
        public const string ObjectiveOutputFolder = "Assets/Generated/TableData/CharacterStoryQuestObjective";
        private const string QuestCatalogPath = OutputFolder + "/CharacterStoryQuestCatalog.asset";
        private const string ObjectiveCatalogPath = ObjectiveOutputFolder + "/CharacterStoryQuestObjectiveCatalog.asset";

        private static readonly string[] QuestColumns = { "quest_id", "character_id", "previous_quest_id", "title_category", "title_key", "description_category", "description_key", "display_order", "is_final", "enabled" };
        private static readonly string[] ObjectiveColumns = { "objective_id", "quest_id", "condition_type", "target_ids", "required_value", "display_order", "enabled" };

        [MenuItem("Tools/Keybuddy/Table Data/Rebuild (Character Story Quest only)", priority = 110)]
        public static void RebuildFromMenu()
        {
            TableDataDiagnosticLog log = Rebuild();
            foreach (var item in log.Entries) Debug.Log(item.ToString());
            EditorUtility.DisplayDialog("Character Story Quest", log.HasErrors ? $"오류 {log.ErrorCount}건으로 중단했습니다." : "Quest 두 도메인만 다시 만들었습니다.", "확인");
        }

        public static TableDataDiagnosticLog Rebuild()
        {
            var log = new TableDataDiagnosticLog();
            List<QuestRow> quests = ReadQuests(log);
            List<ObjectiveRow> objectives = ReadObjectives(log);
            Validate(quests, objectives, log);
            if (log.HasErrors) return log;

            EnsureFolder(OutputFolder); EnsureFolder(ObjectiveOutputFolder);
            var questAssets = new List<CharacterStoryQuestDefinition>();
            var objectiveAssets = new List<CharacterStoryQuestObjectiveDefinition>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (QuestRow row in quests) questAssets.Add(WriteQuest(row));
                foreach (ObjectiveRow row in objectives) objectiveAssets.Add(WriteObjective(row));
                WriteCatalog(ResolveOrCreate<CharacterStoryQuestCatalog>(QuestCatalogPath), "quests", SortQuests(questAssets));
                WriteCatalog(ResolveOrCreate<CharacterStoryQuestObjectiveCatalog>(ObjectiveCatalogPath), "objectives", SortObjectives(objectiveAssets));
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            return log;
        }

        public static TableDataDiagnosticLog ValidateOnly()
        {
            var log = new TableDataDiagnosticLog(); Validate(ReadQuests(log), ReadObjectives(log), log); return log;
        }

        private static void Validate(List<QuestRow> quests, List<ObjectiveRow> objectives, TableDataDiagnosticLog log)
        {
            var characters = ReadIdSet(TableDataPaths.CharacterCsvPath, "character_id");
            var monsters = ReadIdSet(TableDataPaths.MonsterCsvPath, "monster_id");
            var dungeons = ReadIdSet(TableDataPaths.DungeonCsvPath, "dungeon_id");
            var byId = new Dictionary<string, QuestRow>(StringComparer.Ordinal);
            var roots = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (QuestRow row in quests)
            {
                if (byId.ContainsKey(row.Id)) { Error(log, QuestCsvPath, row.Line, "quest_id", row.Id, "quest_id는 고유해야 합니다."); continue; }
                byId.Add(row.Id, row);
                if (!characters.Contains(row.CharacterId)) Error(log, QuestCsvPath, row.Line, "character_id", row.CharacterId, "Character.csv에 없는 character_id입니다.");
                if (string.IsNullOrEmpty(row.PreviousId)) roots[row.CharacterId] = roots.TryGetValue(row.CharacterId, out int count) ? count + 1 : 1;
                if (row.Enabled != true && row.IsFinal) Error(log, QuestCsvPath, row.Line, "is_final", "1", "비활성 퀘스트를 final로 둘 수 없습니다.");
            }
            foreach (QuestRow row in quests)
            {
                if (string.IsNullOrEmpty(row.PreviousId)) continue;
                if (!byId.TryGetValue(row.PreviousId, out QuestRow previous)) Error(log, QuestCsvPath, row.Line, "previous_quest_id", row.PreviousId, "선행 퀘스트가 없습니다.");
                else if (previous.CharacterId != row.CharacterId) Error(log, QuestCsvPath, row.Line, "previous_quest_id", row.PreviousId, "선행 퀘스트는 같은 캐릭터여야 합니다.");
                else if (previous.Id == row.Id) Error(log, QuestCsvPath, row.Line, "previous_quest_id", row.PreviousId, "자기 자신을 선행으로 둘 수 없습니다.");
            }
            foreach (var root in roots) if (root.Value != 1) Error(log, QuestCsvPath, 0, "previous_quest_id", root.Key, "캐릭터별 루트 퀘스트는 정확히 하나여야 합니다.");
            foreach (QuestRow row in quests) if (HasCycle(row, byId)) Error(log, QuestCsvPath, row.Line, "previous_quest_id", row.PreviousId, "선행 체인에 순환이 있습니다.");
            foreach (QuestRow row in quests) if (!row.IsFinal && !HasChild(row.Id, quests)) Error(log, QuestCsvPath, row.Line, "is_final", "0", "후속 퀘스트가 없는 마지막 퀘스트는 is_final=1이어야 합니다.");
            foreach (QuestRow row in quests) if (row.IsFinal && HasChild(row.Id, quests)) Error(log, QuestCsvPath, row.Line, "is_final", "1", "is_final 퀘스트에는 후속 퀘스트가 있을 수 없습니다.");

            var objectiveIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ObjectiveRow row in objectives)
            {
                if (!objectiveIds.Add(row.Id)) Error(log, ObjectiveCsvPath, row.Line, "objective_id", row.Id, "objective_id는 전역 고유여야 합니다.");
                if (!byId.TryGetValue(row.QuestId, out QuestRow quest)) { Error(log, ObjectiveCsvPath, row.Line, "quest_id", row.QuestId, "Quest.csv에 없는 quest_id입니다."); continue; }
                if (row.RequiredValue <= 0) Error(log, ObjectiveCsvPath, row.Line, "required_value", row.RequiredValue.ToString(CultureInfo.InvariantCulture), "required_value는 양수여야 합니다.");
                if (row.Condition == null) { Error(log, ObjectiveCsvPath, row.Line, "condition_type", row.ConditionText, "허용되지 않는 condition_type입니다."); continue; }
                if ((row.Condition == CharacterStoryQuestConditionType.CharacterLevelAtLeast || row.Condition == CharacterStoryQuestConditionType.StaminaSpent) && row.TargetIds.Count != 0)
                    Error(log, ObjectiveCsvPath, row.Line, "target_ids", string.Join("|", row.TargetIds), "이 조건은 target_ids를 사용하지 않습니다.");
                foreach (string target in row.TargetIds)
                {
                    if (row.Condition == CharacterStoryQuestConditionType.MonsterDefeatCount && !monsters.Contains(target)) Error(log, ObjectiveCsvPath, row.Line, "target_ids", target, "Monster.csv에 없는 대상입니다.");
                    if (row.Condition == CharacterStoryQuestConditionType.DungeonEnterCount && !dungeons.Contains(target)) Error(log, ObjectiveCsvPath, row.Line, "target_ids", target, "Dungeon.csv에 없는 대상입니다.");
                }
            }
        }

        private static List<QuestRow> ReadQuests(TableDataDiagnosticLog log)
        {
            CsvTable table = Read(QuestCsvPath, QuestColumns, log); var result = new List<QuestRow>(); if (table == null) return result;
            foreach (var record in table.Records)
            {
                var row = new QuestRow { Line = record.Line, Id = table.Get(record, "quest_id"), CharacterId = table.Get(record, "character_id"), PreviousId = table.Get(record, "previous_quest_id"), DisplayOrder = ParseInt(table.Get(record, "display_order"), QuestCsvPath, record.Line, "display_order", log), IsFinal = ParseBool(table.Get(record, "is_final"), QuestCsvPath, record.Line, "is_final", log), Enabled = ParseBool(table.Get(record, "enabled"), QuestCsvPath, record.Line, "enabled", log) };
                TableDataFieldRules.TryResolveLocalizedEntry("CharacterStoryQuest.csv", record.Line, "title_category", table.Get(record, "title_category"), "title_key", table.Get(record, "title_key"), log, out row.Title);
                TableDataFieldRules.TryResolveLocalizedEntry("CharacterStoryQuest.csv", record.Line, "description_category", table.Get(record, "description_category"), "description_key", table.Get(record, "description_key"), log, out row.Description);
                if (string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.CharacterId)) Error(log, QuestCsvPath, record.Line, "quest_id", row.Id, "quest_id와 character_id는 비어 있을 수 없습니다."); result.Add(row);
            } return result;
        }
        private static List<ObjectiveRow> ReadObjectives(TableDataDiagnosticLog log)
        {
            CsvTable table = Read(ObjectiveCsvPath, ObjectiveColumns, log); var result = new List<ObjectiveRow>(); if (table == null) return result;
            foreach (var record in table.Records)
            {
                string targets = table.Get(record, "target_ids"); var ids = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string raw in targets.Split('|')) { string value = raw.Trim(); if (value.Length == 0) continue; if (!seen.Add(value)) Error(log, ObjectiveCsvPath, record.Line, "target_ids", value, "target_ids에 중복 ID가 있습니다."); else ids.Add(value); }
                var row = new ObjectiveRow { Line = record.Line, Id = table.Get(record, "objective_id"), QuestId = table.Get(record, "quest_id"), ConditionText = table.Get(record, "condition_type"), TargetIds = ids, RequiredValue = ParseInt(table.Get(record, "required_value"), ObjectiveCsvPath, record.Line, "required_value", log), DisplayOrder = ParseInt(table.Get(record, "display_order"), ObjectiveCsvPath, record.Line, "display_order", log), Enabled = ParseBool(table.Get(record, "enabled"), ObjectiveCsvPath, record.Line, "enabled", log) };
                row.Condition = ParseCondition(row.ConditionText); if (string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.QuestId)) Error(log, ObjectiveCsvPath, record.Line, "objective_id", row.Id, "objective_id와 quest_id는 비어 있을 수 없습니다."); result.Add(row);
            } return result;
        }
        private static CsvTable Read(string path, string[] required, TableDataDiagnosticLog log)
        {
            if (!File.Exists(path)) { Error(log, path, 0, "(file)", path, "CSV 파일이 없습니다."); return null; }
            if (!CsvParser.TryParse(File.ReadAllText(path), out List<CsvRecord> records, out string error, out int line)) { Error(log, path, line, "(csv)", "", error); return null; }
            if (records.Count == 0) { Error(log, path, 0, "(header)", "", "헤더가 없습니다."); return null; }
            CsvRecord header = records[0]; var names = new HashSet<string>(header.Fields, StringComparer.Ordinal); foreach (string column in required) if (!names.Contains(column)) Error(log, path, 1, column, "", "필수 헤더가 없습니다.");
            return new CsvTable(Path.GetFileName(path), header.Fields, records.GetRange(1, records.Count - 1));
        }
        private static CharacterStoryQuestDefinition WriteQuest(QuestRow row)
        { var asset = ResolveOrCreate<CharacterStoryQuestDefinition>(OutputFolder + "/Quest_" + row.Id + ".asset"); var o = new SerializedObject(asset); o.FindProperty("questId").stringValue = row.Id; o.FindProperty("characterId").stringValue = row.CharacterId; o.FindProperty("previousQuestId").stringValue = row.PreviousId; WriteLocalized(o.FindProperty("localizedTitle"), row.Title); WriteLocalized(o.FindProperty("localizedDescription"), row.Description); o.FindProperty("displayOrder").intValue = row.DisplayOrder; o.FindProperty("isFinal").boolValue = row.IsFinal; o.FindProperty("enabled").boolValue = row.Enabled; o.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset); return asset; }
        private static CharacterStoryQuestObjectiveDefinition WriteObjective(ObjectiveRow row)
        { var asset = ResolveOrCreate<CharacterStoryQuestObjectiveDefinition>(ObjectiveOutputFolder + "/Objective_" + row.Id + ".asset"); var o = new SerializedObject(asset); o.FindProperty("objectiveId").stringValue = row.Id; o.FindProperty("questId").stringValue = row.QuestId; o.FindProperty("conditionType").enumValueIndex = (int)row.Condition.Value; var targets = o.FindProperty("targetIds"); targets.arraySize = row.TargetIds.Count; for (int i = 0; i < row.TargetIds.Count; i++) targets.GetArrayElementAtIndex(i).stringValue = row.TargetIds[i]; o.FindProperty("requiredValue").intValue = row.RequiredValue; o.FindProperty("displayOrder").intValue = row.DisplayOrder; o.FindProperty("enabled").boolValue = row.Enabled; o.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset); return asset; }
        private static void WriteLocalized(SerializedProperty property, LocalizedEntryRef entry)
        { if (!entry.Resolved) { LocalizedTextReferenceProperty.Clear(property); return; } LocalizedTextReferenceProperty.FindTableCollectionName(property).stringValue = LocalizedTextReferenceProperty.ToGuidString(entry.TableGuid); LocalizedTextReferenceProperty.FindKeyId(property).longValue = entry.KeyId; LocalizedTextReferenceProperty.FindKeyName(property).stringValue = string.Empty; }
        private static T ResolveOrCreate<T>(string path) where T : ScriptableObject { T asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset != null) return asset; asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset; }
        private static void WriteCatalog(ScriptableObject catalog, string field, IList<ScriptableObject> entries) { var o = new SerializedObject(catalog); var list = o.FindProperty(field); list.arraySize = entries.Count; for (int i = 0; i < entries.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = entries[i]; o.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(catalog); }
        private static IList<ScriptableObject> SortQuests(List<CharacterStoryQuestDefinition> items) { items.Sort((a,b) => a.DisplayOrder != b.DisplayOrder ? a.DisplayOrder.CompareTo(b.DisplayOrder) : string.CompareOrdinal(a.QuestId,b.QuestId)); return items.ConvertAll(x => (ScriptableObject)x); }
        private static IList<ScriptableObject> SortObjectives(List<CharacterStoryQuestObjectiveDefinition> items) { items.Sort((a,b) => a.DisplayOrder != b.DisplayOrder ? a.DisplayOrder.CompareTo(b.DisplayOrder) : string.CompareOrdinal(a.ObjectiveId,b.ObjectiveId)); return items.ConvertAll(x => (ScriptableObject)x); }
        private static void EnsureFolder(string folder) { string parent = Path.GetDirectoryName(folder).Replace('\\','/'); string name = Path.GetFileName(folder); if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(parent, name); }
        private static HashSet<string> ReadIdSet(string path, string column) { var values = new HashSet<string>(StringComparer.Ordinal); if (!File.Exists(path) || !CsvParser.TryParse(File.ReadAllText(path), out var rows, out _, out _) || rows.Count == 0) return values; var table = new CsvTable(Path.GetFileName(path), rows[0].Fields, rows.GetRange(1, rows.Count - 1)); foreach (var row in table.Records) values.Add(table.Get(row, column)); return values; }
        private static bool HasCycle(QuestRow row, Dictionary<string, QuestRow> byId) { var seen = new HashSet<string>(StringComparer.Ordinal); QuestRow cursor = row; while (!string.IsNullOrEmpty(cursor.PreviousId) && byId.TryGetValue(cursor.PreviousId, out cursor)) if (!seen.Add(cursor.Id)) return true; return false; }
        private static bool HasChild(string id, List<QuestRow> quests) { foreach (var quest in quests) if (quest.PreviousId == id) return true; return false; }
        private static CharacterStoryQuestConditionType? ParseCondition(string text) { switch (text) { case "CHARACTER_LEVEL_AT_LEAST": return CharacterStoryQuestConditionType.CharacterLevelAtLeast; case "MONSTER_DEFEAT_COUNT": return CharacterStoryQuestConditionType.MonsterDefeatCount; case "DUNGEON_ENTER_COUNT": return CharacterStoryQuestConditionType.DungeonEnterCount; case "STAMINA_SPENT": return CharacterStoryQuestConditionType.StaminaSpent; default: return null; } }
        private static int ParseInt(string text,string file,int line,string column,TableDataDiagnosticLog log) { if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) return value; Error(log,file,line,column,text,"정수여야 합니다."); return 0; }
        private static long ParseLong(string text,string file,int line,string column,TableDataDiagnosticLog log) { if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)) return value; Error(log,file,line,column,text,"정수여야 합니다."); return 0; }
        private static bool ParseBool(string text,string file,int line,string column,TableDataDiagnosticLog log) { if (text == "0") return false; if (text == "1") return true; Error(log,file,line,column,text,"0 또는 1이어야 합니다."); return false; }
        private static void Error(TableDataDiagnosticLog log,string path,int line,string column,string value,string message) => log.Error(Path.GetFileName(path),line,column,value,message);
        private sealed class QuestRow { public int Line,DisplayOrder; public string Id,CharacterId,PreviousId; public LocalizedEntryRef Title,Description; public bool IsFinal,Enabled; }
        private sealed class ObjectiveRow { public int Line,RequiredValue,DisplayOrder; public string Id,QuestId,ConditionText; public CharacterStoryQuestConditionType? Condition; public List<string> TargetIds; public bool Enabled; }
    }
}
