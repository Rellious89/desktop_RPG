using System;

namespace Recovery
{
    /// <summary>
    /// 회복소 밸런스 값 한 묶음의 <b>읽기 전용 스냅샷</b>. 실제 저작 원천은
    /// <see cref="RecoveryBalanceTable"/> 에셋 하나뿐이고, 도메인 로직(<see cref="RecoveryStation"/>)은
    /// 이 값 구조체만 본다 - 그래서 비용/시간/슬롯 수가 코드 여러 곳에 상수로 흩어지지 않는다.
    ///
    /// 에셋(ScriptableObject) 대신 값 구조체를 도메인에 넘기는 이유는 두 가지다.
    ///   - 도메인 로직이 UnityEngine 에셋 로딩에 묶이지 않아 검증 하네스에서 그대로 실행할 수 있다.
    ///   - 한 번 스냅샷을 뜨면 진행 중에 인스펙터에서 값을 바꿔도 그 회복 건의 계산 기준이 흔들리지 않는다.
    /// </summary>
    public readonly struct RecoveryBalance
    {
        /// <summary>회복 규칙 자체의 식별자. 지금은 "default" 하나뿐이며 로그/보고용이다.</summary>
        public readonly string RecoveryId;

        /// <summary>회복 비용을 지불하는 재화의 식별자("Jewel"). 실제 잔액은
        /// <see cref="IRecoveryWallet"/>이 소유하고, 이 값은 그 지갑이 같은 재화인지 대조하는 키다.</summary>
        public readonly string CurrencyId;

        /// <summary>부족 행동력 1당 비용.</summary>
        public readonly int CostPerMissingStamina;

        /// <summary>행동력 1 회복에 걸리는 초.</summary>
        public readonly int SecondsPerStamina;

        /// <summary>동시에 회복할 수 있는 슬롯 수.</summary>
        public readonly int MaxSlots;

        /// <summary>출전 파티원의 자연 회복 효율(회복소 대비 백분율).</summary>
        public readonly int PartyPassiveRecoveryEfficiencyPercent;

        /// <summary>보유하지만 출전하지 않은 캐릭터의 자연 회복 효율(회복소 대비 백분율).</summary>
        public readonly int NonPartyPassiveRecoveryEfficiencyPercent;

        public RecoveryBalance(string recoveryId, string currencyId, int costPerMissingStamina,
                               int secondsPerStamina, int maxSlots)
            : this(recoveryId, currencyId, costPerMissingStamina, secondsPerStamina, maxSlots, 30, 10)
        {
        }

        public RecoveryBalance(string recoveryId, string currencyId, int costPerMissingStamina,
                               int secondsPerStamina, int maxSlots,
                               int partyPassiveRecoveryEfficiencyPercent,
                               int nonPartyPassiveRecoveryEfficiencyPercent)
        {
            RecoveryId = recoveryId;
            CurrencyId = currencyId;
            CostPerMissingStamina = costPerMissingStamina;
            SecondsPerStamina = secondsPerStamina;
            MaxSlots = maxSlots;
            PartyPassiveRecoveryEfficiencyPercent = partyPassiveRecoveryEfficiencyPercent;
            NonPartyPassiveRecoveryEfficiencyPercent = nonPartyPassiveRecoveryEfficiencyPercent;
        }

        /// <summary>프로젝트 기본값(default / Jewel / 100 / 30 / 3). 에셋이 연결되지 않았을 때 조용히
        /// 대신 쓰는 폴백이 <b>아니다</b> - 에셋 필드의 초기값과 검증 하네스가 참조하는 기준값이다.</summary>
        public static RecoveryBalance Default => new RecoveryBalance("default", "Jewel", 100, 30, 3, 30, 10);

        /// <summary>계산과 등록을 진행해도 되는 값인지. 하나라도 어긋나면 회복소는 등록/진행/완료를
        /// 전부 멈춘다 - 잘못된 값으로 0초 즉시 완료나 0으로 나누기가 일어나지 않게 하기 위함이다.</summary>
        public bool IsValid => SecondsPerStamina > 0
                               && CostPerMissingStamina >= 0
                               && MaxSlots > 0
                               && PartyPassiveRecoveryEfficiencyPercent >= 0
                               && NonPartyPassiveRecoveryEfficiencyPercent >= 0
                               && !string.IsNullOrWhiteSpace(CurrencyId);

        /// <summary>어디가 잘못됐는지 사람이 읽을 수 있는 한 줄. <see cref="IsValid"/>가 true면 빈 문자열이다.</summary>
        public string DescribeInvalid()
        {
            if (IsValid) return string.Empty;

            if (SecondsPerStamina <= 0)
            {
                return $"Seconds Per Stamina가 {SecondsPerStamina}입니다 - 1 이상이어야 합니다.";
            }
            if (CostPerMissingStamina < 0)
            {
                return $"Cost Per Missing Stamina가 {CostPerMissingStamina}입니다 - 0 이상이어야 합니다.";
            }
            if (MaxSlots <= 0)
            {
                return $"Max Slots가 {MaxSlots}입니다 - 1 이상이어야 합니다.";
            }
            if (PartyPassiveRecoveryEfficiencyPercent < 0)
            {
                return $"Party Passive Recovery Efficiency Percent가 {PartyPassiveRecoveryEfficiencyPercent}입니다 - 0 이상이어야 합니다.";
            }
            if (NonPartyPassiveRecoveryEfficiencyPercent < 0)
            {
                return $"Non-Party Passive Recovery Efficiency Percent가 {NonPartyPassiveRecoveryEfficiencyPercent}입니다 - 0 이상이어야 합니다.";
            }
            return "Currency Id가 비어 있습니다.";
        }

        /// <summary>부족 행동력에 해당하는 비용. 음수 입력은 0으로 취급한다.</summary>
        public int GetCost(int missingStamina)
        {
            if (missingStamina <= 0) return 0;
            // int 곱셈 오버플로를 막는다 - 밸런스 값을 크게 잘못 넣어도 음수 비용이 나오지 않는다.
            long cost = (long)missingStamina * CostPerMissingStamina;
            return cost > int.MaxValue ? int.MaxValue : (int)cost;
        }

        /// <summary>부족 행동력을 모두 채우는 데 걸리는 시간. <see cref="IsValid"/>가 false면
        /// <see cref="TimeSpan.Zero"/>를 돌려주지만, 그 경우 호출부는 애초에 회복을 시작하지 않는다.</summary>
        public TimeSpan GetDuration(int missingStamina)
        {
            if (!IsValid || missingStamina <= 0) return TimeSpan.Zero;
            return TimeSpan.FromSeconds((double)missingStamina * SecondsPerStamina);
        }
    }
}
