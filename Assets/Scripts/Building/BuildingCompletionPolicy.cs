using System;
using System.Collections.Generic;
using Common;

namespace Building
{
    /// <summary>저장된 건설 기록의 사용자 확정 완료 여부를 읽기 전용으로 판정한다.</summary>
    public static class BuildingCompletionPolicy
    {
        public static bool IsConfirmedCompleted(SaveData data, string buildingId, DateTime nowUtc)
        {
            BuildingConstructionSaveState state = Find(data, buildingId);
            return state != null && state.completionNotified &&
                   SaveData.TryParseTimestamp(state.completeAtUtc, out DateTime completeAt) &&
                   completeAt <= ToUtc(nowUtc);
        }

        public static BuildingConstructionSaveState Find(SaveData data, string buildingId)
        {
            if (data == null || data.buildingConstructions == null || string.IsNullOrEmpty(buildingId)) return null;
            List<BuildingConstructionSaveState> states = data.buildingConstructions;
            for (int i = 0; i < states.Count; i++)
                if (states[i] != null && string.Equals(states[i].buildingId, buildingId, StringComparison.Ordinal)) return states[i];
            return null;
        }

        private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() :
            value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value;
    }
}
