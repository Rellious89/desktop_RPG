using System;
using Character;
using UnityEngine;

namespace Recruitment
{
    /// <summary>
    /// 모집 후보 <b>한 칸</b>의 정적 정의 - "이 모집에서 이 캐릭터가 이만큼의 가중치로 나온다".
    /// <see cref="Skill.CharacterSkillDefinition"/>이 캐릭터와 스킬의 짝 하나를 담는 것과 같은
    /// 방식이며, 짝의 키는 <c>recruitment_type_id</c> + <c>pool_entry_id</c>다.
    ///
    /// <b>가중치를 여기서 보정하지 않는다.</b> 0이나 음수는 "뽑히지 않는 칸"이 아니라 <b>잘못 적힌
    /// 칸</b>이며, 조용히 1로 올려 통과시키면 표에 적힌 값과 실제 확률이 달라진다 - 그래서
    /// <see cref="Weight"/>는 적힌 값을 그대로 돌려주고 <see cref="IsValid"/>가 false가 된다
    /// (뽑기는 유효하지 않은 칸을 아예 후보에서 뺀다).
    /// </summary>
    [CreateAssetMenu(fileName = "RecruitmentPoolEntryDefinition", menuName = "Recruitment/Recruitment Pool Entry Definition")]
    public class RecruitmentPoolEntryDefinition : ScriptableObject
    {
        /// <summary>짝 키를 이을 때 쓰는 구분자. 밑줄 두 개인 이유는
        /// <see cref="Skill.CharacterSkillDefinition.PairIdSeparator"/>와 같다 - 서로 다른 짝이 같은
        /// 문자열이 되는 경우가 없어야 한다.</summary>
        public const string PairIdSeparator = "__";

        [Header("Identity")]
        [Tooltip("이 후보가 속한 모집의 키(RecruitmentType.csv의 recruitment_type_id).")]
        [SerializeField] private string recruitmentTypeId;

        [Tooltip("같은 모집 안에서 이 칸을 가리키는 번호(pool_entry_id).")]
        [SerializeField] private string poolEntryId;

        [Header("Candidate")]
        [Tooltip("후보 캐릭터의 Character Id. 저장 문서가 캐릭터를 가리키는 키이며 적힌 그대로 쓰인다.")]
        [SerializeField] private string characterId;

        [Tooltip("후보 캐릭터의 정의. character_id가 가리키는 Character.csv 행에서 만들어진다. " +
                 "판정의 기준은 언제나 Character Id이고 이 참조는 표시를 위한 것이다.")]
        [SerializeField] private CharacterDefinition character;

        [Tooltip("뽑기 가중치. <b>1 이상만 뜻이 있다</b> - 0이나 음수는 잘못 적힌 칸이며 후보에서 빠진다. " +
                 "확률은 같은 모집에 든 칸들의 가중치 합에 대한 비율이다.")]
        [SerializeField] private int weight;

        [Header("Availability")]
        [Tooltip("RecruitmentPool.csv의 enabled 값을 그대로 옮겨 둔 것.")]
        [SerializeField] private bool enabled;

        /// <summary>이 후보가 속한 모집의 키. 비어 있으면 빈 문자열이다.</summary>
        public string RecruitmentTypeId =>
            string.IsNullOrWhiteSpace(recruitmentTypeId) ? string.Empty : recruitmentTypeId;

        /// <summary>같은 모집 안에서 이 칸을 가리키는 번호. 비어 있으면 빈 문자열이다.</summary>
        public string PoolEntryId => string.IsNullOrWhiteSpace(poolEntryId) ? string.Empty : poolEntryId;

        /// <summary>후보 캐릭터의 Character Id. <b>적힌 그대로</b> 돌려준다.</summary>
        public string CharacterId => string.IsNullOrWhiteSpace(characterId) ? string.Empty : characterId;

        /// <summary>후보 캐릭터의 정의. 사라졌으면 null이며 그때도 <see cref="CharacterId"/>는 남는다.</summary>
        public CharacterDefinition Character => character;

        /// <summary>뽑기 가중치. <b>보정하지 않는다</b> - 적힌 값을 그대로 돌려준다.</summary>
        public int Weight => weight;

        /// <summary>표가 이 칸을 켜 두었는지(CSV의 <c>enabled</c> 값 그대로).</summary>
        public bool Enabled => enabled;

        /// <summary>두 id를 이은 짝 키. 중복 검사와 생성 에셋 이름에 쓴다.</summary>
        public string PairId => IsIdentified ? BuildPairId(RecruitmentTypeId, PoolEntryId) : string.Empty;

        /// <summary>짝을 특정할 수 있는지 - 두 id가 모두 있어야 한다.</summary>
        public bool IsIdentified =>
            !string.IsNullOrWhiteSpace(recruitmentTypeId) && !string.IsNullOrWhiteSpace(poolEntryId);

        /// <summary>뽑기에 쓸 수 있는 칸인지. 캐릭터를 가리키고 가중치가 1 이상이어야 한다.</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(characterId) && weight >= 1;

        /// <summary>두 id를 <see cref="PairIdSeparator"/>로 잇는다. <b>값을 다듬지 않는다</b>.</summary>
        public static string BuildPairId(string recruitmentTypeId, string poolEntryId)
        {
            return (recruitmentTypeId ?? string.Empty) + PairIdSeparator + (poolEntryId ?? string.Empty);
        }

        /// <summary>이 칸이 넘어온 모집에 속하는지. 비교는 <see cref="StringComparer.Ordinal"/> 완전
        /// 일치다 - 다듬지도 대소문자를 맞추지도 않는다.</summary>
        public bool BelongsTo(string typeId)
        {
            return !string.IsNullOrWhiteSpace(typeId)
                   && string.Equals(RecruitmentTypeId, typeId, StringComparison.Ordinal);
        }
    }
}
