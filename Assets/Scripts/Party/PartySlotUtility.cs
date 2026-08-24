using System;
using System.Collections.Generic;

namespace Party
{
    /// <summary>SaveData v4 고정 파티 슬롯의 공통 조회/확장 규칙.</summary>
    public static class PartySlotUtility
    {
        public static int OccupiedCount(IList<string> slots)
        {
            int count = 0;
            if (slots == null) return count;
            for (int i = 0; i < slots.Count; i++) if (!string.IsNullOrEmpty(slots[i])) count++;
            return count;
        }
        public static int IndexOf(IList<string> slots, string id)
        {
            if (slots == null || string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < slots.Count; i++) if (string.Equals(slots[i], id, StringComparison.Ordinal)) return i;
            return -1;
        }
        public static string At(IList<string> slots, int index) => slots != null && index >= 0 && index < slots.Count ? slots[index] ?? string.Empty : string.Empty;
        public static void EnsureIndex(List<string> slots, int index)
        {
            while (slots.Count <= index) slots.Add(string.Empty);
        }
        public static int FirstEmpty(IList<string> slots, int capacity)
        {
            for (int i = 0; i < capacity; i++) if (string.IsNullOrEmpty(At(slots, i))) return i;
            return -1;
        }
    }
}
