using System;
using System.Collections.Generic;
using Common;
using Building;
using Character;
using Dungeon;
using Inventory;
using UnityEngine.Localization.Tables;
using UnityEngine;

namespace Quest
{
    /// <summary>독립 패널의 카드와 상세 뷰가 같은 진행도 계산과 안전한 폴백 문구를 공유한다.</summary>
    public static class CharacterStoryQuestPresentation
    {
        public const string QuestTableGuid = "11805744adb144cd3bb37f325635e0d9";
        public static readonly int[] ObjectiveLocalizationKeys =
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
            10001, 10002, 10003, 10004, 10005, 10006, 10007, 10008,
            10009, 10010, 10011, 10012, 10013, 10014, 10015, 10016,
            100002, 100004,
        };
        public static int Progress(CharacterStoryQuestSnapshot snapshot, CharacterStoryQuestObjectiveDefinition objective)
        {
            if (objective == null) return 0;
            int value = 0;
            if (snapshot?.ObjectiveProgress != null)
                snapshot.ObjectiveProgress.TryGetValue(objective.ObjectiveId, out value);
            return Mathf.Clamp(value, 0, objective.RequiredValue);
        }

        public static float OverallProgress(
            IReadOnlyList<CharacterStoryQuestObjectiveDefinition> objectives,
            CharacterStoryQuestSnapshot snapshot)
        {
            if (objectives == null || objectives.Count == 0) return 0f;
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < objectives.Count; i++)
            {
                CharacterStoryQuestObjectiveDefinition objective = objectives[i];
                if (objective == null) continue;
                sum += (float)Progress(snapshot, objective) / objective.RequiredValue;
                count++;
            }
            return count > 0 ? Mathf.Clamp01(sum / count) : 0f;
        }

        public static string ObjectiveText(
            CharacterStoryQuestObjectiveDefinition objective,
            CharacterStoryQuestSnapshot snapshot)
        {
            return ObjectiveText(objective, snapshot, null, null, null);
        }

        /// <summary>명부와 독립 패널이 공유하는 목표 내용 계약이다. 제목은 이 메서드의 책임이 아니며,
        /// caller가 퀘스트 테이블의 실제 LocalizedTextReference 값을 공급한다.</summary>
        public static string ObjectiveText(
            CharacterStoryQuestObjectiveDefinition objective,
            CharacterStoryQuestSnapshot snapshot,
            MonsterCatalog monsterCatalog,
            DungeonCatalog dungeonCatalog,
            Func<int, string> questText,
            BuildingCatalog buildingCatalog = null,
            CharacterCatalog characterCatalog = null,
            ItemCatalog itemCatalog = null)
        {
            if (objective == null) return string.Empty;
            int current = Progress(snapshot, objective);
            int required = objective.RequiredValue;
            switch (objective.ConditionType)
            {
                case CharacterStoryQuestConditionType.CharacterLevelAtLeast:
                    return Format(questText, 10001, "레벨 {0} ({1}/{2})", required, current, required);
                case CharacterStoryQuestConditionType.MonsterDefeatCount:
                    return Format(questText, 10002, "{0} {1}마리 ({2}/{3})",
                        TargetName(objective, monsterCatalog, true, questText), required, current, required);
                case CharacterStoryQuestConditionType.DungeonEnterCount:
                    return Format(questText, 10004, "{0} {1}회 ({2}/{3})",
                        TargetName(objective, dungeonCatalog, false, questText), required, current, required);
                case CharacterStoryQuestConditionType.StaminaSpent:
                    return Format(questText, 10003, "행동력 {0} ({1}/{2})", required, current, required);
                case CharacterStoryQuestConditionType.TownReturnCount:
                    return Format(questText, 10005, "마을 복귀 ({0}/{1})", current, required);
                case CharacterStoryQuestConditionType.BuildingCompleted:
                    return Format(questText, 10006, "기능 {0} 해금 확인 ({1}/{2})",
                        BuildingTargetName(objective, buildingCatalog), current, required);
                case CharacterStoryQuestConditionType.CharacterOwned:
                    return Format(questText, 10007, "캐릭터 {0} 영입 ({1}/{2})",
                        CharacterTargetName(objective, characterCatalog), current, required);
                case CharacterStoryQuestConditionType.CharacterArchiveOpenCount:
                    return Format(questText, 10008, "용병 명부 확인 ({0}/{1})", current, required);
                case CharacterStoryQuestConditionType.PartyContainsCharacter:
                    return Format(questText, 10009, "캐릭터 {0} 파티 등록 ({1}/{2})",
                        CharacterTargetName(objective, characterCatalog), current, required);
                case CharacterStoryQuestConditionType.RecoveryStarted:
                    return Format(questText, 10010, "캐릭터 {0} 회복 시작 ({1}/{2})",
                        CharacterTargetName(objective, characterCatalog), current, required);
                case CharacterStoryQuestConditionType.CharacterRecoveryComplete:
                    return Format(questText, 10011, "캐릭터 {0} 회복 완료 ({1}/{2})",
                        CharacterTargetName(objective, characterCatalog), current, required);
                case CharacterStoryQuestConditionType.RecoveryJoined:
                    return Format(questText, 10012, "캐릭터 {0} 합류 ({1}/{2})",
                        CharacterTargetName(objective, characterCatalog), current, required);
                case CharacterStoryQuestConditionType.ItemPurchaseCount:
                    return Format(questText, 10013, "아이템 {0} 구매 ({1}/{2})",
                        ItemTargetName(objective, itemCatalog), current, required);
                case CharacterStoryQuestConditionType.ItemUseCount:
                    return Format(questText, 10014, "아이템 {0} 사용 ({1}/{2})",
                        ItemTargetName(objective, itemCatalog), current, required);
                case CharacterStoryQuestConditionType.CharacterStaminaFull:
                    return Format(questText, 10015, "캐릭터 {0} 행동력 최대 ({1}/{2})",
                        CharacterTargetName(objective, characterCatalog), current, required);
                case CharacterStoryQuestConditionType.ManualCharacterSwitchCount:
                    return Format(questText, 10016, "캐릭터 {0} 교체 ({1}/{2})",
                        CharacterTargetName(objective, characterCatalog), current, required);
                default:
                    return string.Format("{0}/{1}", current, required);
            }
        }

        public static string ConditionTitle(CharacterStoryQuestConditionType type)
        {
            switch (type)
            {
                case CharacterStoryQuestConditionType.CharacterLevelAtLeast: return "Level";
                case CharacterStoryQuestConditionType.MonsterDefeatCount: return "Defeat";
                case CharacterStoryQuestConditionType.DungeonEnterCount: return "Dungeon";
                case CharacterStoryQuestConditionType.StaminaSpent: return "Stamina";
                case CharacterStoryQuestConditionType.TownReturnCount: return "Return";
                case CharacterStoryQuestConditionType.BuildingCompleted: return "Building";
                case CharacterStoryQuestConditionType.CharacterOwned: return "Recruit";
                case CharacterStoryQuestConditionType.CharacterArchiveOpenCount: return "Archive";
                case CharacterStoryQuestConditionType.PartyContainsCharacter: return "Party";
                case CharacterStoryQuestConditionType.RecoveryStarted: return "Recovery";
                case CharacterStoryQuestConditionType.CharacterRecoveryComplete: return "Recovery Complete";
                case CharacterStoryQuestConditionType.RecoveryJoined: return "Join";
                case CharacterStoryQuestConditionType.ItemPurchaseCount: return "Purchase";
                case CharacterStoryQuestConditionType.ItemUseCount: return "Use Item";
                case CharacterStoryQuestConditionType.CharacterStaminaFull: return "Stamina Full";
                case CharacterStoryQuestConditionType.ManualCharacterSwitchCount: return "Switch";
                default: return string.Empty;
            }
        }

        public static string ConditionTitle(CharacterStoryQuestConditionType type, Func<int, string> questText)
        {
            switch (type)
            {
                case CharacterStoryQuestConditionType.CharacterLevelAtLeast: return TextOrFallback(questText, 1, "레벨");
                case CharacterStoryQuestConditionType.MonsterDefeatCount: return TextOrFallback(questText, 2, "몬스터 처치");
                case CharacterStoryQuestConditionType.DungeonEnterCount: return TextOrFallback(questText, 4, "던전 입장");
                case CharacterStoryQuestConditionType.StaminaSpent: return TextOrFallback(questText, 3, "행동력 소모");
                case CharacterStoryQuestConditionType.TownReturnCount: return TextOrFallback(questText, 5, "마을 복귀");
                case CharacterStoryQuestConditionType.BuildingCompleted: return TextOrFallback(questText, 6, "건물 완공");
                case CharacterStoryQuestConditionType.CharacterOwned: return TextOrFallback(questText, 7, "용병 영입");
                case CharacterStoryQuestConditionType.CharacterArchiveOpenCount: return TextOrFallback(questText, 8, "용병 명부");
                case CharacterStoryQuestConditionType.PartyContainsCharacter: return TextOrFallback(questText, 9, "파티 등록");
                case CharacterStoryQuestConditionType.RecoveryStarted: return TextOrFallback(questText, 10, "회복 시작");
                case CharacterStoryQuestConditionType.CharacterRecoveryComplete: return TextOrFallback(questText, 11, "회복 완료");
                case CharacterStoryQuestConditionType.RecoveryJoined: return TextOrFallback(questText, 12, "합류");
                case CharacterStoryQuestConditionType.ItemPurchaseCount: return TextOrFallback(questText, 13, "아이템 구매");
                case CharacterStoryQuestConditionType.ItemUseCount: return TextOrFallback(questText, 14, "아이템 사용");
                case CharacterStoryQuestConditionType.CharacterStaminaFull: return TextOrFallback(questText, 15, "행동력 회복");
                case CharacterStoryQuestConditionType.ManualCharacterSwitchCount: return TextOrFallback(questText, 16, "캐릭터 교체");
                default: return string.Empty;
            }
        }

        public static LocalizedTextReference CreateQuestTextReference(int key)
        {
            return new LocalizedTextReference((TableReference)new Guid(QuestTableGuid), key.ToString());
        }

        public static IEnumerable<LocalizedTextReference> ObjectiveTextReferences(
            IReadOnlyList<CharacterStoryQuestObjectiveDefinition> objectives,
            MonsterCatalog monsterCatalog,
            DungeonCatalog dungeonCatalog,
            BuildingCatalog buildingCatalog = null,
            CharacterCatalog characterCatalog = null,
            ItemCatalog itemCatalog = null)
        {
            foreach (int key in ObjectiveLocalizationKeys) yield return CreateQuestTextReference(key);
            if (objectives == null) yield break;
            for (int i = 0; i < objectives.Count; i++)
            {
                CharacterStoryQuestObjectiveDefinition objective = objectives[i];
                if (objective?.TargetIds == null) continue;
                bool monster = objective.ConditionType == CharacterStoryQuestConditionType.MonsterDefeatCount;
                bool dungeon = objective.ConditionType == CharacterStoryQuestConditionType.DungeonEnterCount;
                bool building = objective.ConditionType == CharacterStoryQuestConditionType.BuildingCompleted;
                bool character = IsCharacterTarget(objective.ConditionType);
                bool item = IsItemTarget(objective.ConditionType);
                if (!monster && !dungeon && !building && !character && !item) continue;
                for (int target = 0; target < objective.TargetIds.Count; target++)
                {
                    string id = objective.TargetIds[target];
                    LocalizedTextReference reference = monster
                        ? monsterCatalog?.Find(id)?.LocalizedName
                        : dungeon ? DungeonNameReference(dungeonCatalog, id)
                        : building ? buildingCatalog?.Find(id)?.LocalizedFunctionName
                        : character ? characterCatalog?.Find(id)?.LocalizedName
                        : itemCatalog?.Find(ItemTargetId(id))?.LocalizedName;
                    if (reference != null && reference.HasReference) yield return reference;
                }
            }
        }

        private static string TargetName(CharacterStoryQuestObjectiveDefinition objective, MonsterCatalog catalog,
            bool monster, Func<int, string> questText)
        {
            IReadOnlyList<string> targetIds = objective != null ? objective.TargetIds : null;
            if (targetIds == null || targetIds.Count == 0)
                return TextOrFallback(questText, monster ? 100002 : 100004, monster ? "아무 몬스터" : "아무 던전");
            var values = new List<string>();
            for (int i = 0; i < targetIds.Count; i++)
            {
                string id = targetIds[i];
                if (string.IsNullOrWhiteSpace(id)) continue;
                values.Add(LocalizedOrId(catalog != null ? catalog.Find(id)?.LocalizedName : null, id));
            }
            return values.Count > 0 ? string.Join(", ", values) : TextOrFallback(questText, monster ? 100002 : 100004, monster ? "아무 몬스터" : "아무 던전");
        }

        private static string FirstTargetOrDash(CharacterStoryQuestObjectiveDefinition objective)
        {
            return objective?.TargetIds != null && objective.TargetIds.Count > 0 &&
                   !string.IsNullOrWhiteSpace(objective.TargetIds[0])
                ? objective.TargetIds[0]
                : "-";
        }

        private static bool IsItemTarget(CharacterStoryQuestConditionType type)
        {
            return type == CharacterStoryQuestConditionType.ItemPurchaseCount ||
                   type == CharacterStoryQuestConditionType.ItemUseCount;
        }

        private static string ItemTargetId(string targetId)
        {
            return CharacterStoryQuestTarget.TrySplitItemUse(targetId, out string itemId, out _)
                ? itemId : targetId;
        }

        private static string ItemTargetName(CharacterStoryQuestObjectiveDefinition objective, ItemCatalog catalog)
        {
            string id = ItemTargetId(FirstTargetOrDash(objective));
            return LocalizedOrId(catalog?.Find(id)?.LocalizedName, id);
        }

        public static bool IsCharacterTarget(CharacterStoryQuestConditionType type)
        {
            switch (type)
            {
                case CharacterStoryQuestConditionType.CharacterOwned:
                case CharacterStoryQuestConditionType.PartyContainsCharacter:
                case CharacterStoryQuestConditionType.RecoveryStarted:
                case CharacterStoryQuestConditionType.CharacterRecoveryComplete:
                case CharacterStoryQuestConditionType.RecoveryJoined:
                case CharacterStoryQuestConditionType.CharacterStaminaFull:
                case CharacterStoryQuestConditionType.ManualCharacterSwitchCount:
                    return true;
                default:
                    return false;
            }
        }

        private static string CharacterTargetName(CharacterStoryQuestObjectiveDefinition objective, CharacterCatalog catalog)
        {
            string id = FirstTargetOrDash(objective);
            return LocalizedOrId(catalog?.Find(id)?.LocalizedName, id);
        }

        private static string BuildingTargetName(CharacterStoryQuestObjectiveDefinition objective, BuildingCatalog catalog)
        {
            string id = FirstTargetOrDash(objective);
            BuildingDefinition building = catalog?.Find(id);
            return building != null && building.HasLocalizedFunctionName
                ? LocalizedOrId(building.LocalizedFunctionName, id)
                : id;
        }

        private static string TargetName(CharacterStoryQuestObjectiveDefinition objective, DungeonCatalog catalog,
            bool monster, Func<int, string> questText)
        {
            IReadOnlyList<string> targetIds = objective != null ? objective.TargetIds : null;
            if (targetIds == null || targetIds.Count == 0)
                return TextOrFallback(questText, monster ? 100002 : 100004, monster ? "아무 몬스터" : "아무 던전");
            var values = new List<string>();
            for (int i = 0; i < targetIds.Count; i++)
            {
                string id = targetIds[i];
                if (string.IsNullOrWhiteSpace(id)) continue;
                values.Add(LocalizedOrId(DungeonNameReference(catalog, id), id));
            }
            return values.Count > 0 ? string.Join(", ", values) : TextOrFallback(questText, 100004, "아무 던전");
        }

        private static LocalizedTextReference DungeonNameReference(DungeonCatalog catalog, string id)
        {
            if (catalog?.Dungeons == null) return null;
            foreach (DungeonDefinition dungeon in catalog.Dungeons)
                if (dungeon != null && string.Equals(dungeon.DungeonId, id, StringComparison.Ordinal)) return dungeon.DungeonName;
            return null;
        }

        private static string LocalizedOrId(LocalizedTextReference reference, string id)
        {
            if (reference != null && reference.HasReference)
            {
                string value = reference.GetLocalizedString();
                if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("No translation found", StringComparison.Ordinal)) return value;
            }
            return string.IsNullOrWhiteSpace(id) ? "-" : id;
        }

        private static string TextOrFallback(Func<int, string> questText, int key, string fallback)
        {
            string value = questText != null ? questText(key) : null;
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string Format(Func<int, string> questText, int key, string fallback, params object[] arguments)
        {
            string format = TextOrFallback(questText, key, fallback);
            try { return string.Format(format, arguments); }
            catch (FormatException) { return string.Format(fallback, arguments); }
        }
    }
}
