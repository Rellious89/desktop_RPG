using System;
using UnityEngine;

namespace Recruitment
{
    /// <summary>
    /// 모집 <b>창구</b> 하나의 정적 정의 - "어디서 어떤 모집이 열리는가". 여관(BUILDING/1)이
    /// <c>Inn_Normal</c> 모집을 연다는 사실이 이 에셋 하나에 담긴다.
    ///
    /// <b>창구는 건물을 참조하지 않는다.</b> 종류(<see cref="SourceType"/>)와 id(<see cref="SourceId"/>)
    /// 두 칸으로만 가리키므로, 나중에 건물이 아닌 것에 창구를 붙여도 이 클래스는 달라지지 않는다 -
    /// <c>Building</c> 어셈블리를 이쪽으로 끌어오지 않은 이유이기도 하다.
    ///
    /// <b>여기에 시간도 진행도 없다.</b> <see cref="ArrivalIntervalSeconds"/>는 표가 적어 둔 간격일
    /// 뿐이고, 다음 용병이 언제 오는지는 저장 문서의 몫이다 - 이 단계에는 그것을 세는 코드가 없다.
    /// </summary>
    [CreateAssetMenu(fileName = "RecruitmentAccessDefinition", menuName = "Recruitment/Recruitment Access Definition")]
    public class RecruitmentAccessDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("표가 이 창구를 가리키는 키(RecruitmentAccess.csv의 recruitment_access_id).")]
        [SerializeField] private string recruitmentAccessId;

        [Header("Recruitment")]
        [Tooltip("이 창구가 여는 모집의 키(recruitment_type_id). 저장/조회의 기준은 언제나 이 문자열이다.")]
        [SerializeField] private string recruitmentTypeId;

        [Tooltip("그 모집의 정의. recruitment_type_id가 가리키는 RecruitmentType.csv 행에서 만들어진다.")]
        [SerializeField] private RecruitmentTypeDefinition recruitmentType;

        [Header("Source")]
        [Tooltip("창구가 붙어 있는 대상의 종류 낱말(BUILDING 등). 런타임이 모르는 낱말이면 그 창구는 " +
                 "어떤 조회에도 걸리지 않는다 - 코드가 뜻을 추측하지 않는다.")]
        [SerializeField] private string sourceType;

        [Tooltip("창구가 붙어 있는 대상의 id. source_type이 BUILDING이면 Building.csv의 building_id다.")]
        [SerializeField] private string sourceId;

        [Header("Arrival")]
        [Tooltip("다음 후보가 도착하기까지의 간격(초). 0이면 기다림이 없다는 뜻이며 음수는 들어오지 " +
                 "않는다. <b>남은 시간은 여기가 아니다</b> - 그것은 저장 데이터의 몫이다.")]
        [Min(0)]
        [SerializeField] private int arrivalIntervalSeconds;

        [Tooltip("모집 한 번에 드는 비용의 수량. 0이면 이 창구는 무료다.")]
        [Min(0)]
        [SerializeField] private int consumeAmount;

        [Header("Ordering")]
        [Tooltip("같은 대상에 창구가 여럿일 때의 정렬 값. 작을수록 앞이며, 목록의 순서는 " +
                 "RecruitmentAccessCatalog의 작성 순서가 결정한다.")]
        [SerializeField] private int displayOrder;

        [Header("Availability")]
        [Tooltip("RecruitmentAccess.csv의 enabled 값을 그대로 옮겨 둔 것.")]
        [SerializeField] private bool enabled;

        /// <summary>이 창구를 가리키는 키. 비어 있으면 빈 문자열이다.</summary>
        public string RecruitmentAccessId =>
            string.IsNullOrWhiteSpace(recruitmentAccessId) ? string.Empty : recruitmentAccessId;

        /// <summary>이 창구가 여는 모집의 키. <b>적힌 그대로</b> 돌려준다.</summary>
        public string RecruitmentTypeId =>
            string.IsNullOrWhiteSpace(recruitmentTypeId) ? string.Empty : recruitmentTypeId;

        /// <summary>그 모집의 정의. 사라졌으면 null이며 그때도 <see cref="RecruitmentTypeId"/>는 남는다.</summary>
        public RecruitmentTypeDefinition RecruitmentType => recruitmentType;

        /// <summary>창구가 붙어 있는 대상의 종류 낱말. 비어 있으면 빈 문자열이다.</summary>
        public string SourceType => string.IsNullOrWhiteSpace(sourceType) ? string.Empty : sourceType;

        /// <summary>창구가 붙어 있는 대상의 id. 비어 있으면 빈 문자열이다.</summary>
        public string SourceId => string.IsNullOrWhiteSpace(sourceId) ? string.Empty : sourceId;

        /// <summary>다음 후보가 도착하기까지의 간격(초). 0 이상을 보장한다.</summary>
        public int ArrivalIntervalSeconds => Mathf.Max(0, arrivalIntervalSeconds);

        /// <summary>모집 한 번에 드는 수량. 0 이상을 보장한다.</summary>
        public int ConsumeAmount => Mathf.Max(0, consumeAmount);

        /// <summary>정렬용 순서 값. 작을수록 앞이다.</summary>
        public int DisplayOrder => displayOrder;

        /// <summary>표가 이 창구를 켜 두었는지(CSV의 <c>enabled</c> 값 그대로).</summary>
        public bool Enabled => enabled;

        /// <summary>목록에 올릴 수 있는 창구인지. 식별자와 모집 키가 모두 있어야 한다 - 어느 모집을
        /// 여는지 모르는 창구는 열 것이 없다.</summary>
        public bool IsValid =>
            !string.IsNullOrWhiteSpace(recruitmentAccessId) && !string.IsNullOrWhiteSpace(recruitmentTypeId);

        /// <summary>넘어온 대상이 이 창구가 붙어 있는 곳인지. 비교는 <b>두 칸 모두</b>
        /// <see cref="StringComparer.Ordinal"/> 완전 일치다 - 종류만 같고 id가 다른 창구를 같은 것으로
        /// 보면 다른 건물의 모집이 열린다.</summary>
        public bool MatchesSource(string type, string id)
        {
            return !string.IsNullOrWhiteSpace(type)
                   && !string.IsNullOrWhiteSpace(id)
                   && string.Equals(SourceType, type, StringComparison.Ordinal)
                   && string.Equals(SourceId, id, StringComparison.Ordinal);
        }
    }
}
