using System;

namespace Recovery
{
    /// <summary>
    /// 회복 한 건(캐릭터 1명 또는 Pending 전체)의 <b>비용/시간 견적</b>. 계산 결과를 담기만 하는 값이며
    /// 재화를 차감하거나 상태를 바꾸지 않는다 - UI가 시작 버튼을 누르기 전에 보여줄 값이다.
    ///
    /// 실제 차감은 <see cref="RecoveryStation.StartRecovery"/>가 견적을 <b>다시 계산해서</b> 한다.
    /// 이 구조체를 만들어 둔 뒤 캐릭터의 행동력이 바뀌었을 수 있기 때문에, 화면에 보이던 값을 그대로
    /// 믿고 차감하지 않는다.
    /// </summary>
    public readonly struct RecoveryCostQuote
    {
        /// <summary>부족한 행동력 총합(최대 - 현재).</summary>
        public readonly int MissingStamina;

        /// <summary>필요한 재화 총합.</summary>
        public readonly int Cost;

        /// <summary>가장 오래 걸리는 캐릭터의 회복 시간. 슬롯마다 완료 시각이 독립이므로 합계가 아니라
        /// 최댓값이다 - "이만큼 기다리면 전부 끝난다"가 사용자가 보고 싶은 값이다.</summary>
        public readonly TimeSpan LongestDuration;

        /// <summary>견적에 포함된 캐릭터 수.</summary>
        public readonly int CharacterCount;

        public RecoveryCostQuote(int missingStamina, int cost, TimeSpan longestDuration, int characterCount)
        {
            MissingStamina = missingStamina;
            Cost = cost;
            LongestDuration = longestDuration;
            CharacterCount = characterCount;
        }

        public static RecoveryCostQuote Empty => new RecoveryCostQuote(0, 0, TimeSpan.Zero, 0);
    }
}
