using UnityEngine;

namespace Recruitment
{
    /// <summary>
    /// 모집 종류 하나의 정적 정의. 지금 담는 것은 식별자와 활성 여부뿐이다 - <b>일부러 얇다</b>.
    /// 이 표의 존재 이유는 "이 게임에 어떤 모집이 있는가"라는 목록을 한 곳에 두는 것이고,
    /// 그 모집이 누구를 뽑는지는 <see cref="RecruitmentPoolCatalog"/>, 어디서 열리는지는
    /// <see cref="RecruitmentAccessCatalog"/>가 각자 말한다.
    ///
    /// <b>여기에 확률도 후보도 없다.</b> 가중치는 풀 표의 몫이고, 이 에셋에 후보를 함께 담으면
    /// "누가 후보인가"를 정하는 곳이 둘이 되어 어긋날 자리가 생긴다.
    /// </summary>
    [CreateAssetMenu(fileName = "RecruitmentTypeDefinition", menuName = "Recruitment/Recruitment Type Definition")]
    public class RecruitmentTypeDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("표와 다른 모집 표들이 이 모집을 가리키는 키(RecruitmentType.csv의 " +
                 "recruitment_type_id). 적은 그대로 쓰이며 대소문자를 구분한다.")]
        [SerializeField] private string recruitmentTypeId;

        [Header("Availability")]
        [Tooltip("RecruitmentType.csv의 enabled 값을 그대로 옮겨 둔 것. 목록 판정은 " +
                 "RecruitmentTypeCatalog가 이미 했다.")]
        [SerializeField] private bool enabled;

        /// <summary>이 모집을 가리키는 키. 비어 있으면 빈 문자열이며 <b>적힌 그대로</b> 돌려준다.</summary>
        public string RecruitmentTypeId =>
            string.IsNullOrWhiteSpace(recruitmentTypeId) ? string.Empty : recruitmentTypeId;

        /// <summary>표가 이 모집을 켜 두었는지(CSV의 <c>enabled</c> 값 그대로).</summary>
        public bool Enabled => enabled;

        /// <summary>목록에 올릴 수 있는 모집인지. 지금 기준은 식별자 하나뿐이다.</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(recruitmentTypeId);
    }
}
