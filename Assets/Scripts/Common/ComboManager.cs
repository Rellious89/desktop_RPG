using System;
using Character;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// <b>실제 타격 1회 = 콤보 1</b>. 예전에는 키 입력 자체를 콤보로 셌지만, 누적 입력 공격이 생기면서
    /// "아직 쏘지도 않은 충전 입력"까지 콤보로 잡히는 문제가 있었다. 이제 콤보를 올리는 유일한 근거는
    /// PlayerCharacterAnimator.HitPoint(공격 모션이 Hit Frame에 도달한 순간)다 - 키 입력, 충전 진행,
    /// 몬스터 처치는 콤보를 올리지 않는다. 콤보 수치의 단위는 입력 수가 아니라 타격 수다.
    ///
    /// 만료 타이머는 마지막 <b>타격</b>부터 잰다. 다음 두 경우에는 값을 그대로 둔 채 진행만 멈춘다
    /// (0으로 초기화하지 않으므로, 재개되면 남은 시간부터 이어진다).
    ///   - 공격 가능한 Target이 없을 때(Target.HasAttackableTarget == false): 적이 없어서 못 때리는
    ///     동안 제한 시간이 흘러 콤보가 강제로 끊기는 것을 막는다.
    ///   - 누적 입력 공격을 충전하는 동안(ChargeStarted ~ ChargeEnded): 충전 시간이 티어 타임아웃보다
    ///     길 수 있어서, 정상적으로 활을 당기고 있는데 콤보가 끊기면 안 된다. 발사(Hit)로 끝나면 타이머가
    ///     리셋되고, 충전이 취소되면 그 자리에서 다시 흐르기 시작한다.
    ///
    /// 티어가 높을수록(Fever에 가까울수록) 타임아웃을 짧게 잡아 콤보 유지에 긴장감을 준다. 티어 임계값
    /// 자체는 기준 단위가 타격 수로 바뀐 뒤의 체감을 보고 조정한다(이번에는 그대로 둔다).
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

        [Header("Timeout per Tier (초) - 마지막 타격 이후 이 시간 동안 다음 타격이 없으면 콤보가 끊긴다")]
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

        /// <summary><see cref="ResetCombo"/>가 타임아웃 처리와 같은 경로를 타도록 잡아두는 씬 인스턴스.</summary>
        private static ComboManager instance;

        private float sinceLastHit;

        /// <summary>누적 입력 공격을 충전하는 동안 true - 이 동안에는 만료 타이머가 흐르지 않는다.
        /// PlayerCharacterAnimator가 시작/종료를 정확히 한 번씩 보내므로(비활성화 경로 포함) 여기서
        /// 별도의 타임아웃 안전장치를 두지 않는다.</summary>
        private bool chargeInProgress;

        private void Awake()
        {
            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>캐릭터 교체처럼 "지금까지의 타격 흐름이 끊겼다"고 봐야 하는 순간에 콤보를 즉시
        /// 0으로 되돌린다. 타임아웃으로 끊긴 경우와 구분할 이유가 없으므로 같은 경로(BreakCombo)를
        /// 그대로 타서 OnComboChanged/OnComboTierChanged/OnComboBroken이 평소와 똑같이 나간다.</summary>
        public static void ResetCombo()
        {
            if (instance != null)
            {
                if (CurrentCombo > 0) instance.BreakCombo();
                return;
            }

            // 씬에 ComboManager가 없는 구성(테스트 씬 등)에서도 정적 값이 남아 있지 않게 정리한다.
            CurrentCombo = 0;
            CurrentTier = 0;
        }

        private void OnEnable()
        {
            CurrentCombo = 0;
            CurrentTier = 0;
            sinceLastHit = 0f;
            chargeInProgress = false;

            PlayerCharacterAnimator.HitPoint += HandleHitPoint;
            PlayerCharacterAnimator.ChargeStarted += HandleChargeStarted;
            PlayerCharacterAnimator.ChargeEnded += HandleChargeEnded;
        }

        private void OnDisable()
        {
            PlayerCharacterAnimator.HitPoint -= HandleHitPoint;
            PlayerCharacterAnimator.ChargeStarted -= HandleChargeStarted;
            PlayerCharacterAnimator.ChargeEnded -= HandleChargeEnded;
        }

        private void Update()
        {
            if (CurrentCombo <= 0) return;

            // 콤보 값과 sinceLastHit을 그대로 둔 채 만료 타이머만 멈춘다 - BreakCombo를 호출하지 않는다.
            // 조건이 풀리면 남은 값부터 이어서 진행된다(초기화하지 않는다).
            if (!Target.HasAttackableTarget) return;
            if (chargeInProgress) return;

            sinceLastHit += Time.deltaTime;
            if (sinceLastHit >= CurrentTimeout())
            {
                BreakCombo();
            }
        }

        /// <summary>공격 모션이 Hit Frame에 도달한 순간 - Direct든 누적 입력이든 여기서만 콤보가 오른다.
        /// 인자(데미지/연출 값)는 콤보와 무관하므로 보지 않는다.</summary>
        private void HandleHitPoint(AttackHitCue cue)
        {
            RegisterHit();
        }

        private void HandleChargeStarted()
        {
            chargeInProgress = true;
        }

        private void HandleChargeEnded()
        {
            chargeInProgress = false;
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

        private void RegisterHit()
        {
            sinceLastHit = 0f;

            CurrentCombo++;
            OnComboChanged?.Invoke(CurrentCombo);

            UpdateTier();
        }

        private void BreakCombo()
        {
            int finalCombo = CurrentCombo;
            CurrentCombo = 0;
            sinceLastHit = 0f;

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
