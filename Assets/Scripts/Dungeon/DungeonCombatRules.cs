using UnityEngine;

namespace Dungeon
{
    /// <summary>모닥불 복귀와 던전 입장이 공유하는 전투 준비 기준의 단일 설정 원본.</summary>
    [DisallowMultipleComponent]
    public sealed class DungeonCombatRules : MonoBehaviour
    {
        public const float DefaultMinimumStaminaRatio = 0.3f;

        [Header("Combat Readiness")]
        [Tooltip("모닥불 전투 복귀와 던전 입장에 공통으로 필요한 최소 행동력 비율입니다. 0.3 = 30%")]
        [Range(0.01f, 1f)]
        [SerializeField] private float minimumStaminaRatio = DefaultMinimumStaminaRatio;

        public static DungeonCombatRules Instance { get; private set; }
        public static float MinimumStaminaRatio => Instance != null
            ? Mathf.Clamp(Instance.minimumStaminaRatio, 0.01f, 1f)
            : DefaultMinimumStaminaRatio;

        public static int RequiredStamina(int maximumStamina)
        {
            return CalculateRequiredStamina(maximumStamina, MinimumStaminaRatio);
        }

        public static int CalculateRequiredStamina(int maximumStamina, float ratio)
        {
            if (maximumStamina <= 0) return 0;
            return Mathf.Max(1, Mathf.CeilToInt(maximumStamina * Mathf.Clamp(ratio, 0.01f, 1f)));
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[DungeonCombatRules] 전투 규칙 관리 오브젝트가 중복되어 첫 인스턴스를 사용합니다.", this);
                enabled = false;
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnValidate()
        {
            minimumStaminaRatio = Mathf.Clamp(minimumStaminaRatio, 0.01f, 1f);
        }
    }
}
