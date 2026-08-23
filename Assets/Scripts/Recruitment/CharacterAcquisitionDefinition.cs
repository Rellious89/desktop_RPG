using Character;
using UnityEngine;

namespace Recruitment
{
    /// <summary>
    /// 캐릭터 한 명을 <b>어떻게 얻을 수 있는가</b>만 담는 정적 정의. 캐릭터가 무엇인지는
    /// <see cref="CharacterDefinition"/>이 이미 말하고 있고, 이 에셋이 말하는 것은 "그 캐릭터를
    /// 손에 넣는 길이 무엇인가" 하나다(<see cref="Building.BuildingDefinition"/>이 건물의 비용만
    /// 담는 것과 같은 역할 분담이다).
    ///
    /// <b>여기에 진행 상태는 없다.</b> 지금 그 캐릭터를 가지고 있는지는 저장 문서의 몫이며
    /// (<see cref="Common.SaveData.characters"/>), 이 에셋은 몇 번을 읽어도 같은 답을 준다.
    ///
    /// <b>여기에 동작도 없다.</b> 실제로 캐릭터를 지급하는 코드는 이 에셋 바깥에 있다 - 이 단계에는
    /// 아예 존재하지 않는다. 이 에셋을 읽는 유일한 코드는
    /// <see cref="RecruitmentCandidateSelector"/>이며, 그것도 후보를 고를 뿐 아무것도 주지 않는다.
    ///
    /// <b>Character Id는 절대 다른 값으로 대체하지 않는다.</b> 저장 문서가 캐릭터를 가리키는 키가
    /// 언제나 이 문자열이기 때문이다 - 정의 참조가 비어 있어도 id는 남으며, 판정의 기준은 언제나
    /// id 쪽이다(<see cref="Building.BuildingDefinition.ItemCostEntry"/>와 같은 규칙).
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterAcquisitionDefinition", menuName = "Recruitment/Character Acquisition Definition")]
    public class CharacterAcquisitionDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("표가 이 행을 가리키는 키. CharacterAcquisition.csv의 acquisition_id가 그대로 들어온다.")]
        [SerializeField] private string acquisitionId;

        [Tooltip("이 행이 말하는 캐릭터의 Character Id. 저장 문서가 캐릭터를 가리키는 키이며, " +
                 "적힌 그대로 쓰인다 - 대소문자를 구분하고 앞뒤 공백도 떼지 않는다.")]
        [SerializeField] private string characterId;

        [Tooltip("그 캐릭터의 정의. character_id가 가리키는 Character.csv 행에서 만들어진다. " +
                 "판정의 기준은 언제나 Character Id이고 이 참조는 표시를 위한 것이다.")]
        [SerializeField] private CharacterDefinition character;

        [Header("Acquisition")]
        [Tooltip("획득 방식 낱말(RECRUIT_ONLY 등). 런타임이 모르는 낱말이면 그 캐릭터는 모집 후보에서 " +
                 "빠진다 - 코드가 뜻을 추측하지 않는다.")]
        [SerializeField] private string acquisitionType;

        [Tooltip("이미 가지고 있어도 다시 모집될 수 있는가. 꺼져 있으면 보유한 캐릭터는 후보에서 빠진다.")]
        [SerializeField] private bool allowDuplicateRecruitment;

        [Tooltip("획득에 걸린 조건의 키. <b>비어 있는 것이 정상</b>이며 '조건이 없다'는 뜻이다 - " +
                 "값이 있으면 그 조건을 판정하는 코드가 아직 없으므로 후보에서 빠진다.")]
        [SerializeField] private string conditionId;

        [Header("Availability")]
        [Tooltip("CharacterAcquisition.csv의 enabled 값을 그대로 옮겨 둔 것. 이 값 자체가 목록을 " +
                 "만들지는 않는다 - 목록은 CharacterAcquisitionCatalog가 이미 정했다.")]
        [SerializeField] private bool enabled;

        /// <summary>표가 이 행을 가리키는 키. 비어 있으면 빈 문자열이다.</summary>
        public string AcquisitionId => string.IsNullOrWhiteSpace(acquisitionId) ? string.Empty : acquisitionId;

        /// <summary>이 행이 말하는 캐릭터의 Character Id. <b>적힌 그대로</b> 돌려준다.</summary>
        public string CharacterId => string.IsNullOrWhiteSpace(characterId) ? string.Empty : characterId;

        /// <summary>그 캐릭터의 정의. 정의가 사라졌으면 null이며, 그때도 <see cref="CharacterId"/>는 남는다.</summary>
        public CharacterDefinition Character => character;

        /// <summary>획득 방식 낱말. 지정하지 않았으면 빈 문자열이며 <b>적힌 그대로</b> 돌려준다.</summary>
        public string AcquisitionType => string.IsNullOrWhiteSpace(acquisitionType) ? string.Empty : acquisitionType;

        /// <summary>이미 보유한 캐릭터도 다시 모집될 수 있는지.</summary>
        public bool AllowDuplicateRecruitment => allowDuplicateRecruitment;

        /// <summary>획득 조건 키. 비어 있으면 조건이 없다는 뜻이다.</summary>
        public string ConditionId => string.IsNullOrWhiteSpace(conditionId) ? string.Empty : conditionId;

        /// <summary>조건이 걸려 있는지. 조건을 <b>판정하지는 않는다</b> - 지금은 조건이 있다는 사실만 안다.</summary>
        public bool HasCondition => !string.IsNullOrWhiteSpace(conditionId);

        /// <summary>표가 이 행을 켜 두었는지(CSV의 <c>enabled</c> 값 그대로).</summary>
        public bool Enabled => enabled;

        /// <summary>목록에 올릴 수 있는 행인지. 캐릭터를 가리키지 못하는 행은 아무 뜻도 없다.</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(characterId);

        /// <summary>지금 런타임이 아는 방식으로 <b>모집으로 얻을 수 있는가</b>. 비교는 Ordinal 완전
        /// 일치이며 <b>대소문자를 구분한다</b> - 'recruit_only'는 RECRUIT_ONLY가 아니다.</summary>
        public bool IsRecruitable =>
            string.Equals(AcquisitionType, RecruitmentAcquisitionTypes.RecruitOnly, System.StringComparison.Ordinal);
    }
}
