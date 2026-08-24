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

        /// <summary>활성/유효 여부와 관계없이 원본 설정을 찾는다. 런타임 서비스가 "설정 없음"과
        /// "설정 값 오류"를 구분해 호출자에게 전달할 때만 사용하며, 일반 조회는 <see cref="Find"/>를 쓴다.</summary>
        public PurificationConfigDefinition FindConfigured(string purificationTypeId)
        {
            if (string.IsNullOrWhiteSpace(purificationTypeId) || configs == null) return null;
            for (int i = 0; i < configs.Count; i++)
            {
                PurificationConfigDefinition config = configs[i];
                if (config != null && string.Equals(config.PurificationTypeId, purificationTypeId, StringComparison.Ordinal))
                    return config;
            }
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
