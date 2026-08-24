using UnityEngine;
namespace Corruption
{
    [CreateAssetMenu(fileName = "CorruptionConfigDefinition", menuName = "Corruption/Config Definition")]
    public sealed class CorruptionConfigDefinition : ScriptableObject
    {
        [SerializeField] private string configId;
        [SerializeField] private int maxCorruption;
        [SerializeField] private int warningThresholdPercent;
        [SerializeField] private int dangerThresholdPercent;
        [SerializeField] private int warningStaminaCostMultiplier;
        [SerializeField] private int dangerStaminaCostMultiplier;
        [SerializeField] private bool enabled;
        public string ConfigId => string.IsNullOrWhiteSpace(configId) ? string.Empty : configId;
        public int MaxCorruption => maxCorruption;
        public int WarningThresholdPercent => warningThresholdPercent;
        public int DangerThresholdPercent => dangerThresholdPercent;
        public int WarningStaminaCostMultiplier => warningStaminaCostMultiplier;
        public int DangerStaminaCostMultiplier => dangerStaminaCostMultiplier;
        public bool Enabled => enabled;
        public bool IsValid => !string.IsNullOrWhiteSpace(ConfigId) && maxCorruption >= 1 && warningThresholdPercent >= 1 && warningThresholdPercent < dangerThresholdPercent && dangerThresholdPercent <= 100 && warningStaminaCostMultiplier >= 1 && dangerStaminaCostMultiplier >= warningStaminaCostMultiplier;
    }
}
