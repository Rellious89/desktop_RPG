using UnityEngine;

namespace Party
{
    /// <summary>
    /// 파티 설정 하나의 정적 정의. 지금 담는 것은 식별자와 <b>기본 정원</b>과 활성 여부뿐이며
    /// <b>일부러 얇다</b> - 이 표의 존재 이유는 "파티에 몇 명까지 넣을 수 있는가"의 출발점을 한 곳에
    /// 두는 것이고, 지금 누가 파티에 있는지는 저장 문서의 몫이다.
    ///
    /// <b>여기에 편성도 해제도 없다.</b> 이 단계는 표를 읽어 오는 자리까지이며, 정원을 실제로
    /// 강제하는 것은 이후 파티 서비스가 이 값을 <b>물어봐서</b> 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "PartyConfigDefinition", menuName = "Party/Party Config Definition")]
    public class PartyConfigDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("이 파티 설정을 가리키는 키(PartyConfig.csv의 party_config_id). 적은 그대로 쓰이며 " +
                 "대소문자를 구분한다.")]
        [SerializeField] private string configId;

        [Header("Capacity")]
        [Tooltip("파티에 넣을 수 있는 기본 인원(PartyConfig.csv의 base_capacity). 1 이상만 표를 통과한다.")]
        [SerializeField] private int baseCapacity;

        [Header("Availability")]
        [Tooltip("PartyConfig.csv의 enabled 값을 그대로 옮겨 둔 것. 목록 판정은 PartyConfigCatalog가 이미 했다.")]
        [SerializeField] private bool enabled;

        /// <summary>이 설정을 가리키는 키. 비어 있으면 빈 문자열이며 <b>적힌 그대로</b> 돌려준다.</summary>
        public string ConfigId => string.IsNullOrWhiteSpace(configId) ? string.Empty : configId;

        /// <summary>
        /// 파티 기본 정원. <b>표에 적힌 값을 그대로</b> 돌려주며 여기서 보정하지 않는다 - 1 미만인
        /// 값은 임포터가 <b>오류로</b> 막으므로(자동으로 끌어올리지 않는다) 이 자리에서 다시 고치면
        /// 표와 런타임이 다른 말을 하게 된다.
        /// </summary>
        public int BaseCapacity => baseCapacity;

        /// <summary>표가 이 설정을 켜 두었는지(CSV의 <c>enabled</c> 값 그대로).</summary>
        public bool Enabled => enabled;

        /// <summary>목록에 올릴 수 있는 설정인지. 식별자가 있고 정원이 1 이상이어야 한다 - 정원이 0인
        /// 파티는 "설정이 있는데 아무도 못 넣는" 상태라 표의 뜻이 될 수 없다.</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(configId) && baseCapacity >= PartyConfigRules.MinimumBaseCapacity;
    }
}
