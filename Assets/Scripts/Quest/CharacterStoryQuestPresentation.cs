using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quest
{
    /// <summary>독립 패널의 카드와 상세 뷰가 같은 진행도 계산과 안전한 폴백 문구를 공유한다.</summary>
    public static class CharacterStoryQuestPresentation
    {
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
            if (objective == null) return string.Empty;
            int current = Progress(snapshot, objective);
            int required = objective.RequiredValue;
            string target = JoinTargets(objective.TargetIds);
            switch (objective.ConditionType)
            {
                case CharacterStoryQuestConditionType.CharacterLevelAtLeast:
                    return string.Format("Level {0} ({1}/{2})", required, current, required);
                case CharacterStoryQuestConditionType.MonsterDefeatCount:
                    return string.Format("{0} Defeat ({1}/{2})", target, current, required);
                case CharacterStoryQuestConditionType.DungeonEnterCount:
                    return string.Format("{0} Dungeon ({1}/{2})", target, current, required);
                case CharacterStoryQuestConditionType.StaminaSpent:
                    return string.Format("Stamina ({0}/{1})", current, required);
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
                default: return string.Empty;
            }
        }

        private static string JoinTargets(IReadOnlyList<string> targetIds)
        {
            if (targetIds == null || targetIds.Count == 0) return "Any";
            var values = new List<string>();
            for (int i = 0; i < targetIds.Count; i++)
                if (!string.IsNullOrWhiteSpace(targetIds[i])) values.Add(targetIds[i]);
            return values.Count > 0 ? string.Join(", ", values) : "Any";
        }
    }
}
