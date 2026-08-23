using System;

namespace Recruitment
{
    /// <summary>창구 조회가 <b>왜</b> 그 답이 되었는지. "못 찾았다" 하나로 뭉뚱그리면 표가 잘못된
    /// 것인지 아직 지어지지 않은 건물인지 구분할 수 없다.</summary>
    public enum RecruitmentAccessOutcome
    {
        /// <summary>창구와 모집을 모두 찾았다.</summary>
        Resolved = 0,

        /// <summary>물어본 대상 자체가 비어 있다(종류나 id가 없다).</summary>
        InvalidSource = 1,

        /// <summary>그 대상에 붙어 있는 창구가 목록에 없다.</summary>
        NoAccess = 2,

        /// <summary>창구는 있으나 표가 꺼 두었다.</summary>
        AccessDisabled = 3,

        /// <summary>창구가 가리키는 모집을 목록에서 찾을 수 없다(끊어진 참조).</summary>
        UnknownRecruitmentType = 4,

        /// <summary>모집은 있으나 표가 꺼 두었다.</summary>
        RecruitmentTypeDisabled = 5,
    }

    /// <summary>
    /// 창구 조회 한 번의 결과. <b>값 타입</b>이며 아무것도 소유하지 않는다 - 같은 입력으로 몇 번을
    /// 물어도 같은 답이 나오고, 물어보는 것만으로는 프로젝트도 저장 파일도 한 글자 달라지지 않는다.
    /// </summary>
    public readonly struct RecruitmentAccessResolution
    {
        private RecruitmentAccessResolution(
            RecruitmentAccessOutcome outcome,
            RecruitmentAccessDefinition access,
            RecruitmentTypeDefinition recruitmentType)
        {
            Outcome = outcome;
            Access = access;
            RecruitmentType = recruitmentType;
        }

        public RecruitmentAccessOutcome Outcome { get; }

        /// <summary>찾은 창구. 창구를 찾기 전에 끝난 경우에는 null이다.</summary>
        public RecruitmentAccessDefinition Access { get; }

        /// <summary>찾은 모집. <see cref="Outcome"/>이 <see cref="RecruitmentAccessOutcome.Resolved"/>가
        /// 아니면 null일 수 있다.</summary>
        public RecruitmentTypeDefinition RecruitmentType { get; }

        /// <summary>모집을 실제로 열 수 있는 상태인지.</summary>
        public bool IsResolved => Outcome == RecruitmentAccessOutcome.Resolved;

        /// <summary>모집을 가리키는 키. 창구를 찾았으면 창구가 적어 둔 값이고, 아니면 빈 문자열이다 -
        /// <b>키가 판정의 기준</b>이므로 참조가 비어 있어도 이 값은 남는다.</summary>
        public string RecruitmentTypeId => Access != null ? Access.RecruitmentTypeId : string.Empty;

        internal static RecruitmentAccessResolution Failed(
            RecruitmentAccessOutcome outcome, RecruitmentAccessDefinition access = null,
            RecruitmentTypeDefinition recruitmentType = null)
        {
            return new RecruitmentAccessResolution(outcome, access, recruitmentType);
        }

        internal static RecruitmentAccessResolution Success(
            RecruitmentAccessDefinition access, RecruitmentTypeDefinition recruitmentType)
        {
            return new RecruitmentAccessResolution(RecruitmentAccessOutcome.Resolved, access, recruitmentType);
        }
    }

    /// <summary>
    /// "이 대상에서 어떤 모집이 열리는가"를 답하는 <b>유일한</b> 자리 - 여관(BUILDING/1)이면
    /// <c>Inn_Normal_Access</c>를 지나 <c>Inn_Normal</c>까지가 한 번의 조회다.
    ///
    /// <b>부작용이 하나도 없다.</b> 저장 문서를 읽지도 쓰지도 않고, 씬을 뒤지지도 UI를 건드리지도
    /// 않으며, 카탈로그를 고치지도 않는다 - 같은 입력에 언제나 같은 답을 주는 순수한 조회다.
    /// 그래서 UI 없이도, 저장 파일 없이도 시험할 수 있다.
    ///
    /// <b>판정의 기준은 언제나 id 문자열이다.</b> 창구가 들고 있는
    /// <see cref="RecruitmentAccessDefinition.RecruitmentType"/> 참조는 표시를 위한 것이며, 목록에서
    /// 같은 id를 찾지 못했을 때 <b>그 참조의 id가 정확히 일치하는 경우에만</b> 대신 쓴다
    /// (<see cref="Building.BuildingDefinition.ToCostRequest"/>가 정의와 id가 어긋나면 id를 남기는
    /// 것과 같은 규칙이다).
    /// </summary>
    public static class RecruitmentAccessResolver
    {
        /// <summary>건물에 붙은 창구를 찾는다. <paramref name="buildingId"/>는 Building.csv의
        /// <c>building_id</c>이며 <b>적힌 그대로</b> 비교한다.</summary>
        public static RecruitmentAccessResolution ResolveForBuilding(
            string buildingId, RecruitmentAccessCatalog accessCatalog, RecruitmentTypeCatalog typeCatalog)
        {
            return Resolve(RecruitmentSourceTypes.Building, buildingId, accessCatalog, typeCatalog);
        }

        /// <summary>
        /// 대상(종류 + id)에 붙은 창구와 그 창구가 여는 모집을 찾는다.
        ///
        /// 카탈로그가 null이어도 예외로 만들지 않는다 - 씬 구성이 덜 된 상태에서 터지는 것보다
        /// "창구가 없다"가 훨씬 다루기 쉬운 답이고, 어느 단계에서 끊겼는지는
        /// <see cref="RecruitmentAccessResolution.Outcome"/>이 그대로 말해 준다.
        /// </summary>
        public static RecruitmentAccessResolution Resolve(
            string sourceType, string sourceId,
            RecruitmentAccessCatalog accessCatalog, RecruitmentTypeCatalog typeCatalog)
        {
            if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceId))
            {
                return RecruitmentAccessResolution.Failed(RecruitmentAccessOutcome.InvalidSource);
            }

            RecruitmentAccessDefinition access =
                accessCatalog != null ? accessCatalog.FindBySource(sourceType, sourceId) : null;

            if (access == null) return RecruitmentAccessResolution.Failed(RecruitmentAccessOutcome.NoAccess);

            if (!access.Enabled)
            {
                return RecruitmentAccessResolution.Failed(RecruitmentAccessOutcome.AccessDisabled, access);
            }

            RecruitmentTypeDefinition type = ResolveType(access, typeCatalog);
            if (type == null)
            {
                return RecruitmentAccessResolution.Failed(RecruitmentAccessOutcome.UnknownRecruitmentType, access);
            }

            if (!type.Enabled)
            {
                return RecruitmentAccessResolution.Failed(
                    RecruitmentAccessOutcome.RecruitmentTypeDisabled, access, type);
            }

            return RecruitmentAccessResolution.Success(access, type);
        }

        /// <summary>모집 목록에서 id로 찾고, 없으면 창구가 들고 있는 참조가 <b>같은 id를 말할 때만</b>
        /// 그것을 쓴다. 어긋나는 참조는 쓰지 않는다 - 표가 가리키는 것과 다른 모집이 열리는 것보다
        /// 열리지 않는 편이 낫다.</summary>
        private static RecruitmentTypeDefinition ResolveType(
            RecruitmentAccessDefinition access, RecruitmentTypeCatalog typeCatalog)
        {
            string typeId = access.RecruitmentTypeId;
            if (string.IsNullOrEmpty(typeId)) return null;

            RecruitmentTypeDefinition fromCatalog = typeCatalog != null ? typeCatalog.Find(typeId) : null;
            if (fromCatalog != null) return fromCatalog;

            RecruitmentTypeDefinition referenced = access.RecruitmentType;
            if (referenced == null) return null;

            return string.Equals(referenced.RecruitmentTypeId, typeId, StringComparison.Ordinal) ? referenced : null;
        }
    }
}
