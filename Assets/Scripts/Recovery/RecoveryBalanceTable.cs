using UnityEngine;

namespace Recovery
{
    /// <summary>
    /// 회복소 밸런스의 <b>단일 수정 지점</b>. 비용, 시간, 슬롯 수를 바꿀 일이 생기면 이 에셋 하나만
    /// 고친다 - 코드에 상수로 흩어 두지 않는다(CharacterDefinition / ItemDefinition과 같은 역할 분담이다).
    ///
    /// <b>진행 상태는 여기에 없다.</b> 어떤 캐릭터가 몇 번 슬롯에서 언제까지 회복 중인지는
    /// SaveData.recoverySlots가 소유하고, 이 에셋은 그 계산의 기준값만 제공한다.
    ///
    /// 값이 잘못되면(예: Seconds Per Stamina가 0) 조용히 기본값으로 대체하지 않고
    /// <see cref="RecoveryService"/>가 오류를 남기고 회복소를 멈춘다 - 0초 즉시 완료 같은
    /// 조용한 오작동을 만들지 않기 위함이다.
    /// </summary>
    [CreateAssetMenu(fileName = "RecoveryBalanceTable", menuName = "Recovery/Recovery Balance Table")]
    public class RecoveryBalanceTable : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("이 회복 규칙의 식별자. 지금은 규칙이 하나뿐이라 로그/보고용이다. 비워두면 에셋 이름을 쓴다.")]
        [SerializeField] private string recoveryId = "default";

        [Tooltip("회복 비용을 지불하는 재화의 식별자. 실제 잔액은 InventoryManager가 소유하고, " +
                 "이 값은 그 재화가 맞는지 대조하는 키다.")]
        [SerializeField] private string currencyId = "Jewel";

        [Header("Balance")]
        [Tooltip("부족한 행동력 1당 비용. 캐릭터 1명 비용 = (최대 행동력 - 현재 행동력) * 이 값.")]
        [Min(0)]
        [SerializeField] private int costPerMissingStamina = 100;

        [Tooltip("행동력 1을 회복하는 데 걸리는 초. 총 시간 = 부족한 행동력 * 이 값. " +
                 "0 이하로 두면 회복소 전체가 멈추고 오류를 남긴다.")]
        [Min(1)]
        [SerializeField] private int secondsPerStamina = 30;

        [Tooltip("동시에 회복할 수 있는 슬롯 수. 저장 데이터의 슬롯 목록은 이 값에 맞춰 늘어나며, " +
                 "값을 줄여도 이미 회복 중인 슬롯의 저장 값을 지우지는 않는다.")]
        [Min(1)]
        [SerializeField] private int maxSlots = 3;

        public string RecoveryId => string.IsNullOrWhiteSpace(recoveryId) ? name : recoveryId;

        /// <summary>인스펙터 값을 도메인이 쓰는 읽기 전용 스냅샷으로 바꾼다. 값 보정(클램프)은 하지
        /// 않는다 - 잘못된 값은 <see cref="RecoveryBalance.IsValid"/>가 false로 드러내야 한다.</summary>
        public RecoveryBalance ToBalance()
        {
            return new RecoveryBalance(RecoveryId, currencyId, costPerMissingStamina, secondsPerStamina, maxSlots);
        }
    }
}
