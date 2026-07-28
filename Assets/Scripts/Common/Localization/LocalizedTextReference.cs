using System;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace Common
{
    /// <summary>
    /// 카테고리 번호 + 숫자 키 저작 방식을 위한 프로젝트 전용 로컬라이징 참조.
    ///
    /// Unity Localization의 <see cref="LocalizedString"/>을 그대로 상속하므로
    /// 직렬화 레이아웃(Table GUID + Entry Key ID)과 런타임 동작이 완전히 동일하다.
    /// 카테고리 번호와 사용자 숫자 키는 중복 저장하지 않고,
    /// Editor 전용 Property Drawer가 Table GUID / Entry Key ID로부터 역으로 표시한다.
    ///
    /// 언어 선택, fallback, 문자열 포맷팅은 계속 Unity Localization이 담당한다.
    /// </summary>
    [Serializable]
    public class LocalizedTextReference : LocalizedString
    {
        public LocalizedTextReference()
        {
        }

        public LocalizedTextReference(TableReference tableReference, TableEntryReference entryReference)
            : base(tableReference, entryReference)
        {
        }

        /// <summary>
        /// Table과 Entry가 모두 지정되어 있는지 여부. <see cref="LocalizedString.IsEmpty"/>의 가독성용 반전값.
        /// 참조가 존재한다는 뜻이며, 해당 Entry에 번역 값이 채워져 있는지는 보장하지 않는다.
        /// </summary>
        public bool HasReference => !IsEmpty;
    }
}
