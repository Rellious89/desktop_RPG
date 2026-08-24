using UnityEngine;

namespace Corruption
{
    [CreateAssetMenu(fileName = "PurificationConfigDefinition", menuName = "Corruption/Purification Config Definition")]
    public sealed class PurificationConfigDefinition : ScriptableObject
    {
        [SerializeField] private string purificationTypeId;
        [SerializeField] private string requiredBuildingId;
        [SerializeField] private int purificationIntervalSeconds;
        [SerializeField] private int purificationValuePerInterval;
        [SerializeField] private int baseSlotCount;
        [SerializeField] private bool enabled;

        public string PurificationTypeId => string.IsNullOrWhiteSpace(purificationTypeId) ? string.Empty : purificationTypeId;
        public string RequiredBuildingId => string.IsNullOrWhiteSpace(requiredBuildingId) ? string.Empty : requiredBuildingId;
        public int PurificationIntervalSeconds => purificationIntervalSeconds;
        public int PurificationValuePerInterval => purificationValuePerInterval;
        public int BaseSlotCount => baseSlotCount;
        public bool Enabled => enabled;
        public bool IsValid => !string.IsNullOrWhiteSpace(PurificationTypeId)
            && !string.IsNullOrWhiteSpace(RequiredBuildingId)
            && purificationIntervalSeconds >= 1 && purificationValuePerInterval >= 1 && baseSlotCount >= 1;
    }
}
