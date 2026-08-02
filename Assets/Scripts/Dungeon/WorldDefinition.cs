using Common;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// 월드 하나의 정의 - "이 월드가 무엇인가"만 담는다(식별자, 표시 이름, 목록 순서).
    /// <see cref="DungeonDefinition"/>과 <see cref="MonsterDefinition"/>이 이 에셋을 참조해서
    /// 소속을 표현하며, <b>월드가 어떤 던전/몬스터를 가지는지는 여기에 없다</b> - 소속은 언제나
    /// 던전/몬스터 쪽이 들고 있고, 월드는 그것을 모른다(한 방향 참조).
    ///
    /// <b>식별자는 반드시 직접 적는다.</b> <see cref="DungeonDefinition"/>과 같은 이유로 파일 이름으로
    /// 대체하지 않는다 - 월드 id는 나중에 진행 저장과 데이터 임포트의 키가 되므로, 에셋 파일 이름을
    /// 바꾸는 것만으로 식별자가 함께 바뀌는 경로를 만들지 않는다. 비어 있는 월드는
    /// <see cref="IsValid"/>가 false이고 <see cref="WorldCatalog"/>가 목록에서 제외한다.
    ///
    /// <b>이름은 로컬라이징 참조만 들고 있다.</b> 한국어/영어 문자열을 이 에셋이나 코드에 적어두지
    /// 않으며, 표시하는 쪽이 <see cref="LocalizedTextReference"/>를 구독해서 그린다.
    /// </summary>
    [CreateAssetMenu(fileName = "WorldDefinition", menuName = "Dungeon/World Definition")]
    public class WorldDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("이 월드의 고유 식별자(world_01 등). <b>반드시 직접 적는다</b> - 비워두면 이 월드는 " +
                 "목록에 나오지 않는다(파일 이름으로 대체하지 않는다). 한 번 정하면 바꾸지 않는다.")]
        [SerializeField] private string worldId;

        [Header("Localization")]
        [Tooltip("화면에 표시할 월드 이름. 카테고리 번호 + 숫자 키로 지정한다.")]
        [SerializeField] private LocalizedTextReference localizedName = new LocalizedTextReference();

        [Header("Ordering")]
        [Tooltip("월드를 정렬할 때 쓰는 순서 값. 작을수록 앞이다 - 이 값 자체가 목록을 만들지는 않으며, " +
                 "목록의 순서는 WorldCatalog의 작성 순서가 결정한다.")]
        [SerializeField] private int displayOrder;

        /// <summary>이 월드의 고유 식별자. 비어 있으면 빈 문자열을 돌려준다 - <b>에셋 이름으로
        /// 대체하지 않는다</b>. 앞뒤 공백은 제거해서, 공백만 적힌 값이 유효한 id처럼 보이지 않게 한다.</summary>
        public string WorldId => string.IsNullOrWhiteSpace(worldId) ? string.Empty : worldId.Trim();

        /// <summary>목록에 올릴 수 있는 월드인지 여부. 지금 기준은 식별자 하나뿐이며, 문구가 비어 있는
        /// 것은 표시 문제이지 목록에서 빼야 할 이유가 아니다(그 경우는 그리는 쪽이 비워둔다).</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(worldId);

        /// <summary>월드 이름 참조. 표시하는 쪽이 구독해서 현재 Locale 문자열을 받는다.
        /// <b>절대 null을 돌려주지 않는다</b> - 참조가 비어 있을 수는 있어도 객체 자체는 항상 있다.</summary>
        public LocalizedTextReference LocalizedName => localizedName ?? (localizedName = new LocalizedTextReference());

        /// <summary>월드 이름의 Table/Key가 지정되어 있는지 여부(번역 값의 존재를 보장하지는 않는다).</summary>
        public bool HasLocalizedName => localizedName != null && localizedName.HasReference;

        /// <summary>정렬용 순서 값. 작을수록 앞이며, 지정하지 않으면 0이다.</summary>
        public int DisplayOrder => displayOrder;
    }
}
