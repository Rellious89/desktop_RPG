using System;
using System.Collections.Generic;
using UnityEngine;
namespace Corruption
{
    [CreateAssetMenu(fileName = "CorruptionConfigCatalog", menuName = "Corruption/Config Catalog")]
    public sealed class CorruptionConfigCatalog : ScriptableObject
    {
        [SerializeField] private List<CorruptionConfigDefinition> configs = new List<CorruptionConfigDefinition>();
        private readonly List<CorruptionConfigDefinition> valid = new List<CorruptionConfigDefinition>(); private bool built;
        public IReadOnlyList<CorruptionConfigDefinition> Configs { get { EnsureBuilt(); return valid; } }
        public CorruptionConfigDefinition Find(string id) { if (string.IsNullOrWhiteSpace(id)) return null; EnsureBuilt(); foreach (var c in valid) if (string.Equals(c.ConfigId, id, StringComparison.Ordinal)) return c; return null; }
        public void MarkDirty() => built = false; private void OnEnable() => built = false;
        private void EnsureBuilt() { if (built) return; built = true; valid.Clear(); if (configs == null) return; var seen = new HashSet<string>(StringComparer.Ordinal); foreach (var c in configs) if (c != null && c.Enabled && c.IsValid && seen.Add(c.ConfigId)) valid.Add(c); }
#if UNITY_EDITOR
        private void OnValidate() => built = false;
#endif
    }
}
