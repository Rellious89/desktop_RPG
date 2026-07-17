using System;
using DesktopWindow;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 유효한 키 입력이 들어올 때마다 콤보를 올리고, 현재 티어에 해당하는 타임아웃 동안 입력이
    /// 없으면 콤보를 0으로 되돌린다. 티어가 높을수록(Fever에 가까울수록) 타임아웃을 짧게 잡아서
    /// 콤보를 유지하려면 더 빠르게 계속 입력해야 하는 긴장감을 준다.
    /// GlobalKeyboardHook의 입력 신호를 보되, 실제 HitPoint/데미지와는 여전히 무관하다(콤보는
    /// "유효한 전투 키 입력" 자체를 카운트하는 정책을 유지한다) - 다만 공격 가능한 Target이 하나도
    /// 없는 동안(Target.HasAttackableTarget == false)에는 입력을 콤보로 인정하지 않고, 만료
    /// 타이머(sinceLastInput)도 그 값 그대로 멈춰둔다(0으로 초기화하지 않는다) - 적이 없어서
    /// 못 때리는 동안 콤보 제한 시간이 흘러 강제로 끊기는 것을 막기 위함이다. Target이 다시
    /// Alive가 되면 남은 시간부터 타이머가 이어서 진행된다.
    /// 씬에 하나만 두면 된다. 다른 스크립트는 정적 프로퍼티/이벤트로 콤보 상태를 읽는다
    /// (Target.AnyTargetDefeated, SessionKillCounter와 같은 패턴).
    /// </summary>
    [DisallowMultipleComponent]
    public class ComboManager : MonoBehaviour
    {
        [Header("Tier Thresholds (콤보가 이 값 이상이면 해당 티어)")]
        [Tooltip("1: Normal")]
        [SerializeField] private int tier1Threshold = 1;
        [Tooltip("2: Boost")]
        [SerializeField] private int tier2Threshold = 20;
        [Tooltip("3: Fever")]
        [SerializeField] private int tier3Threshold = 50;

        [Header("Timeout per Tier (초) - 마지막 입력 이후 이 시간 동안 추가 입력이 없으면 콤보가 끊긴다")]
        [Tooltip("콤보가 없거나 Tier 1일 때 적용되는 타임아웃")]
        [SerializeField] private float tier1Timeout = 1.0f;
        [SerializeField] private float tier2Timeout = 0.5f;
        [SerializeField] private float tier3Timeout = 0.3f;

        public static int CurrentCombo { get; private set; }
        public static int CurrentTier { get; private set; }

        /// <summary>콤보 수치가 바뀔 때마다(증가하거나 0으로 초기화될 때) 발생.</summary>
        public static event Action<int> OnComboChanged;

        /// <summary>콤보 티어가 실제로 바뀔 때만 발생(매 입력마다가 아니라 구간을 넘을 때만).</summary>
        public static event Action<int> OnComboTierChanged;

        /// <summary>타임아웃으로 콤보가 끊길 때 발생. 인자는 끊기기 직전의 콤보 수치.</summary>
        public static event Action<int> OnComboBroken;

        private float sinceLastInput;

        private void OnEnable()
        {
            CurrentCombo = 0;
            CurrentTier = 0;
        }

        private void Update()
        {
            bool canAttack = Target.HasAttackableTarget;

            if (canAttack && GlobalKeyboardHook.AnyKeyDownThisFrame)
            {
                RegisterInput();
            }

            if (CurrentCombo <= 0) return;

            // 공격 가능한 Target이 없는 동안에는 콤보 값과 sinceLastInput을 그대로 둔 채 만료 타이머만
            // 멈춘다 - BreakCombo를 호출하지 않는다. Fade-in이 끝나 다시 canAttack이 true가 되면 이
            // 남은 값부터 이어서 진행된다(초기화하지 않는다).
            if (!canAttack) return;

            sinceLastInput += Time.deltaTime;
            if (sinceLastInput >= CurrentTimeout())
            {
                BreakCombo();
            }
        }

        private float CurrentTimeout()
        {
            switch (CurrentTier)
            {
                case 3: return tier3Timeout;
                case 2: return tier2Timeout;
                default: return tier1Timeout;
            }
        }

        private void RegisterInput()
        {
            sinceLastInput = 0f;

            CurrentCombo++;
            OnComboChanged?.Invoke(CurrentCombo);

            UpdateTier();
        }

        private void BreakCombo()
        {
            int finalCombo = CurrentCombo;
            CurrentCombo = 0;
            sinceLastInput = 0f;

            OnComboChanged?.Invoke(CurrentCombo);
            UpdateTier();
            OnComboBroken?.Invoke(finalCombo);
        }

        private void UpdateTier()
        {
            int newTier = CalculateTier(CurrentCombo);
            if (newTier == CurrentTier) return;

            CurrentTier = newTier;
            OnComboTierChanged?.Invoke(CurrentTier);
        }

        private int CalculateTier(int combo)
        {
            if (combo >= tier3Threshold) return 3;
            if (combo >= tier2Threshold) return 2;
            if (combo >= tier1Threshold) return 1;
            return 0;
        }
    }
}
