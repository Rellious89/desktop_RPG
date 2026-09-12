using System;
using System.Collections.Generic;

namespace Quest
{
    /// <summary>
    /// 튜토리얼 진행 중 허용되는 행동을 한곳에서 판정한다. 표의 tutorial_step이 꺼진 일반
    /// 서사 퀘스트에는 관여하지 않는다. 이후 일반 콘텐츠 해금표를 추가할 때 이 관문 앞이나 뒤에
    /// 별도 정책을 결합할 수 있도록 저장 상태를 직접 변경하지 않는 조회 전용 정책으로 둔다.
    /// </summary>
    public static class TutorialFlowPolicy
    {
        public const int FirstRecruitmentArrivalSeconds = 5;

        public static bool IsTutorialActive =>
            CharacterStoryQuestService.Instance != null &&
            CharacterStoryQuestService.Instance.TryGetActiveTutorialStep(out _, out _);

        public static bool Allows(CharacterStoryQuestConditionType condition, string targetId = null)
        {
            CharacterStoryQuestService service = CharacterStoryQuestService.Instance;
            if (service == null || !service.TryGetActiveTutorialStep(out _, out _)) return true;
            if (!service.TryGetActiveTutorialObjective(condition, out CharacterStoryQuestObjectiveDefinition objective))
                return false;
            return MatchesTarget(objective, targetId);
        }

        public static bool TryGetTarget(
            CharacterStoryQuestConditionType condition,
            out string targetId)
        {
            targetId = string.Empty;
            CharacterStoryQuestService service = CharacterStoryQuestService.Instance;
            if (service == null || !service.TryGetActiveTutorialObjective(condition, out var objective))
                return false;
            IReadOnlyList<string> targets = objective.TargetIds;
            if (targets == null || targets.Count == 0 || string.IsNullOrEmpty(targets[0])) return false;
            targetId = targets[0];
            return true;
        }

        public static bool IsForcedRecruitmentTarget(string characterId)
        {
            return !string.IsNullOrEmpty(characterId) &&
                   TryGetTarget(CharacterStoryQuestConditionType.CharacterOwned, out string targetId) &&
                   string.Equals(characterId, targetId, StringComparison.Ordinal);
        }

        public static int RecruitmentArrivalSeconds(int defaultSeconds)
        {
            if (!IsTutorialActive) return defaultSeconds;
            return IsCurrentStep(CharacterStoryQuestConditionType.BuildingCompleted) ||
                   IsCurrentStep(CharacterStoryQuestConditionType.CharacterOwned)
                ? FirstRecruitmentArrivalSeconds
                : defaultSeconds;
        }

        public static bool ShouldPausePassiveRecovery(string characterId)
        {
            if (string.IsNullOrEmpty(characterId) || !IsTutorialActive) return false;
            CharacterStoryQuestService service = CharacterStoryQuestService.Instance;

            // 최초 모집 캐릭터는 회복소에 직접 넣어 보는 단계 전까지 자연 회복시키지 않는다.
            if (service != null && service.TryGetTutorialTarget(
                    CharacterStoryQuestConditionType.CharacterOwned, true, out string recruitedId) &&
                string.Equals(characterId, recruitedId, StringComparison.Ordinal)) return true;

            // 아이템 사용 단계가 끝나기 전에 목표 캐릭터가 자연 회복으로 먼저 가득 차는 것을 막는다.
            if (!TryGetTarget(CharacterStoryQuestConditionType.ItemUseCount, out string itemUseTarget)) return false;
            return CharacterStoryQuestTarget.TrySplitItemUse(itemUseTarget, out _, out string itemCharacterId) &&
                   string.Equals(characterId, itemCharacterId, StringComparison.Ordinal);
        }

        public static bool IsCurrentStep(CharacterStoryQuestConditionType condition)
        {
            CharacterStoryQuestService service = CharacterStoryQuestService.Instance;
            return service != null && service.TryGetActiveTutorialObjective(condition, out _);
        }

        private static bool MatchesTarget(CharacterStoryQuestObjectiveDefinition objective, string targetId)
        {
            if (objective == null) return false;
            IReadOnlyList<string> targets = objective.TargetIds;
            if (targets == null || targets.Count == 0) return true;
            if (string.IsNullOrEmpty(targetId)) return false;
            for (int i = 0; i < targets.Count; i++)
                if (string.Equals(targets[i], targetId, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
