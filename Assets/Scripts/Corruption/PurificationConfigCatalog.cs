using System;
using System.Collections.Generic;
using UnityEngine;

namespace Corruption
{
    [CreateAssetMenu(fileName = "PurificationConfigCatalog", menuName = "Corruption/Purification Config Catalog")]
    public sealed class PurificationConfigCatalog : ScriptableObject
    {
        [SerializeField] private List<PurificationConfigDefinition> configs = new List<PurificationConfigDefinition>();
        private readonly List<PurificationConfigDefinition> valid = new List<PurificationConfigDefinition>();
        private bool built;

        public IReadOnlyList<PurificationConfigDefinition> Configs { get { EnsureBuilt(); return valid; } }
        public PurificationConfigDefinition Find(string purificationTypeId)
        {
            if (string.IsNullOrWhiteSpace(purificationTypeId)) return null;
            EnsureBuilt();
            foreach (PurificationConfigDefinition config in valid)
                if (string.Equals(config.PurificationTypeId, purificationTypeId, StringComparison.Ordinal)) return config;
            return null;
        }
        public void MarkDirty() => built = false;
        private void OnEnable() => built = false;
        private void EnsureBuilt()
        {
            if (built) return;
            built = true; valid.Clear();
            if (configs == null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PurificationConfigDefinition config in configs)
                if (config != null && config.Enabled && config.IsValid && seen.Add(config.PurificationTypeId)) valid.Add(config);
        }
#if UNITY_EDITOR
        private void OnValidate() => built = false;
#endif
    }
}
