using Character;
using UnityEngine;

namespace Skill
{
    /// <summary>
    /// "이 캐릭터가 이 스킬을 가진다"는 <b>관계 한 줄</b>의 정적 정의. 캐릭터도 스킬도 상대를 목록으로
    /// 들고 있지 않고, 관계만 따로 모아 두는 이유는 하나다 - 한쪽 표에 목록 칸을 두면 같은 관계가
    /// 캐릭터 쪽과 스킬 쪽 두 곳에 적히고, 둘이 어긋났을 때 어느 쪽이 맞는지 정할 수 없게 된다.
    ///
    /// <b>여기에 해금 규칙은 없다.</b> <see cref="RequiredCharacterLevel"/>은 표에 적힌 숫자일 뿐이고,
    /// 그 값을 보고 스킬을 열어 주거나 막는 코드는 어디에도 없다 - 이번 단계의 범위는 관계 표가
    /// 존재하고 검증되며 에셋으로 만들어진다까지다.
    ///
    /// <b>식별자는 문자열 두 개이고, 참조는 그것을 푼 결과다.</b> <see cref="CharacterId"/> /
    /// <see cref="SkillId"/>가 이 관계의 정체이며(생성 에셋을 다시 찾을 때 쓰는 키가 이것이다),
    /// <see cref="Character"/> / <see cref="Skill"/>은 임포터가 <b>같은 행에서</b> 함께 채운 연결이라
    /// 둘이 어긋날 수 없다. 참조만 두면 연결이 끊어진 에셋의 정체를 알 수 없어 모든 관계가 "빈 id"로
    /// 뭉개진다.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterSkillDefinition", menuName = "Skill/Character Skill Definition")]
    public class CharacterSkillDefinition : ScriptableObject
    {
        /// <summary>
        /// 두 id를 하나의 키로 잇는 구분자. <b>밑줄 두 개</b>인 것은 ID 형식이 그것을 만들 수 없기
        /// 때문이다 - snake_case는 밑줄이 연달아 올 수 없고(<c>a__b</c>는 형식 오류다) 캐릭터의 legacy
        /// PascalCase id에는 밑줄 자체가 없다. 그래서 <c>a_b</c>+<c>c</c>와 <c>a</c>+<c>b_c</c>가 같은
        /// 키가 되는 경우가 생기지 않는다.
        /// </summary>
        public const string PairIdSeparator = "__";

        [Header("Identity")]
        [Tooltip("관계의 앞쪽 - 캐릭터 id. 적은 그대로 쓰이며 대소문자를 구분한다.")]
        [SerializeField] private string characterId;

        [Tooltip("관계의 뒤쪽 - 스킬 id. 적은 그대로 쓰이며 대소문자를 구분한다.")]
        [SerializeField] private string skillId;

        [Header("References")]
        [Tooltip("위 character_id가 가리키는 정의. 임포터가 같은 행에서 함께 채운다.")]
        [SerializeField] private CharacterDefinition character;

        [Tooltip("위 skill_id가 가리키는 정의. 임포터가 같은 행에서 함께 채운다.")]
        [SerializeField] private SkillDefinition skill;

        [Header("Condition")]
        [Tooltip("표에 적힌 필요 캐릭터 레벨. <b>지금은 이 값을 보고 무엇을 여닫는 코드가 없다</b> - " +
                 "해금 규칙은 이번 단계의 범위가 아니다.")]
        [Min(1)]
        [SerializeField] private int requiredCharacterLevel = 1;

        [Header("Ordering")]
        [Tooltip("관계를 정렬할 때 쓰는 순서 값. 작을수록 앞이다 - 목록의 순서는 " +
                 "CharacterSkillCatalog의 작성 순서가 결정한다.")]
        [SerializeField] private int displayOrder;

        /// <summary>관계의 앞쪽 캐릭터 id. 비어 있으면 빈 문자열이며 <b>적힌 그대로</b> 돌려준다.</summary>
        public string CharacterId => string.IsNullOrWhiteSpace(characterId) ? string.Empty : characterId;

        /// <summary>관계의 뒤쪽 스킬 id. 비어 있으면 빈 문자열이며 <b>적힌 그대로</b> 돌려준다.</summary>
        public string SkillId => string.IsNullOrWhiteSpace(skillId) ? string.Empty : skillId;

        /// <summary>두 id를 이은 관계 키. 목록에서 같은 관계가 두 번 들어오는지 볼 때 쓴다.
        /// 어느 한쪽이라도 비어 있으면 빈 문자열이다 - 반쪽짜리 관계에 키를 주지 않는다.</summary>
        public string PairId => IsValid ? BuildPairId(CharacterId, SkillId) : string.Empty;

        /// <summary>목록에 올릴 수 있는 관계인지 여부. 기준은 <b>두 식별자가 모두 있는가</b> 하나다 -
        /// 참조가 비어 있는 것은 표시/연결 문제이지 관계 자체가 없다는 뜻은 아니다.</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(characterId) && !string.IsNullOrWhiteSpace(skillId);

        /// <summary>character_id가 가리키는 정의. 연결되지 않았으면 null이다.</summary>
        public CharacterDefinition Character => character;

        /// <summary>skill_id가 가리키는 정의. 연결되지 않았으면 null이다.</summary>
        public SkillDefinition Skill => skill;

        /// <summary>표에 적힌 필요 캐릭터 레벨(1 이상). <b>해금 판정에 쓰이지 않는다</b>.</summary>
        public int RequiredCharacterLevel => Mathf.Max(1, requiredCharacterLevel);

        /// <summary>정렬용 순서 값. 작을수록 앞이며, 지정하지 않으면 0이다.</summary>
        public int DisplayOrder => displayOrder;

        /// <summary>두 id를 <see cref="PairIdSeparator"/>로 잇는다. <b>값을 다듬지 않는다</b> -
        /// 넘어온 문자열을 그대로 이어 붙이므로 대소문자도 공백도 그대로 남는다.</summary>
        public static string BuildPairId(string characterId, string skillId)
        {
            return (characterId ?? string.Empty) + PairIdSeparator + (skillId ?? string.Empty);
        }
    }
}
