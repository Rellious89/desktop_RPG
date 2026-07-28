using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CommonEditor
{
    /// <summary>
    /// 아직 실제 Entry로 해석되지 않은 Key 입력값을 대상 오브젝트별로 기억한다.
    ///
    /// Drawer와 검색창이 같은 상태를 공유해야 하므로 Drawer 내부가 아니라 여기에 둔다.
    /// 검색창에서 Entry를 고르면 이 상태를 반드시 지워야 Inspector 표시와 실제 참조가 어긋나지 않는다.
    /// </summary>
    internal static class LocalizedTextPendingKeys
    {
        internal sealed class PendingKey
        {
            public int Value;
            public int CatalogVersion;
        }

        private static readonly Dictionary<string, PendingKey> pending = new Dictionary<string, PendingKey>();

        private static string MakeKey(int instanceId, string propertyPath) => $"{instanceId}/{propertyPath}";

        /// <summary>Drawer가 표시하는 대상(다중 선택 시 첫 번째)의 미해결 입력값.</summary>
        internal static PendingKey Get(SerializedProperty property)
        {
            var target = property.serializedObject.targetObject;
            if (target == null)
            {
                return null;
            }

            return pending.TryGetValue(MakeKey(target.GetInstanceID(), property.propertyPath), out var value)
                ? value
                : null;
        }

        /// <summary>다중 선택 편집을 고려해 모든 대상에 같은 미해결 입력값을 기록한다.</summary>
        internal static void Set(SerializedProperty property, int value)
        {
            int version = LocalizationCategoryCatalog.Version;

            foreach (var target in property.serializedObject.targetObjects)
            {
                if (target == null)
                {
                    continue;
                }

                pending[MakeKey(target.GetInstanceID(), property.propertyPath)] = new PendingKey
                {
                    Value = value,
                    CatalogVersion = version,
                };
            }
        }

        internal static void Clear(SerializedProperty property)
        {
            Clear(property.serializedObject.targetObjects, property.propertyPath);
        }

        /// <summary>
        /// SerializedProperty 없이 대상 목록만으로 지운다.
        /// 검색창처럼 Drawer 바깥에서 참조를 확정했을 때 사용한다.
        /// </summary>
        internal static void Clear(Object[] targets, string propertyPath)
        {
            if (targets == null)
            {
                return;
            }

            foreach (var target in targets)
            {
                if (target != null)
                {
                    pending.Remove(MakeKey(target.GetInstanceID(), propertyPath));
                }
            }
        }
    }
}
