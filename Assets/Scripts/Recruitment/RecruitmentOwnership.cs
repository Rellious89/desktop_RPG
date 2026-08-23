using System;
using System.Collections.Generic;

namespace Recruitment
{
    /// <summary>
    /// "이 캐릭터를 이미 가지고 있는가"를 답하는 <b>얇은 질문 하나</b>.
    ///
    /// 뽑기가 <see cref="Common.SaveData"/>도 <see cref="Common.SaveSystem"/>도
    /// <see cref="Character.OwnedCharacterCollection"/>도 <b>알지 못하게</b> 하려고 둔 경계다 -
    /// 보유 여부는 저장 문서가 소유하는 사실이고, 뽑기는 그 사실을 <b>넘겨받아 읽을</b> 뿐 저장
    /// 문서에 손을 대지 않는다. 그래서 뽑기는 디스크도 씬도 없이 그대로 시험할 수 있다.
    /// </summary>
    public interface IRecruitmentOwnership
    {
        /// <summary>이미 보유한 캐릭터인지. 비교는 <b>넘어온 문자열 그대로</b>여야 한다 - 다듬거나
        /// 대소문자를 맞추면 표에 적힌 id와 저장 문서의 키가 어긋난 것을 못 보고 지나친다.</summary>
        bool IsOwned(string characterId);
    }

    /// <summary>
    /// <see cref="IRecruitmentOwnership"/>를 만드는 자리. 부르는 쪽은 자기가 이미 들고 있는 보유
    /// 목록(저장 문서에서 읽었든 시험이 지어냈든)을 그대로 건네면 된다.
    /// </summary>
    public static class RecruitmentOwnership
    {
        /// <summary>아무것도 가지지 않은 상태. 새 저장 문서를 흉내 낼 때 쓴다.</summary>
        public static IRecruitmentOwnership None { get; } = new IdSet(null);

        /// <summary>보유한 character_id들로 만든다. <b>넘어온 목록을 복사해</b> 들고 있으므로,
        /// 나중에 원본이 바뀌어도 이미 만든 판정은 달라지지 않는다. 비교는
        /// <see cref="StringComparer.Ordinal"/>이다.</summary>
        public static IRecruitmentOwnership Of(IEnumerable<string> ownedCharacterIds)
        {
            return new IdSet(ownedCharacterIds);
        }

        /// <summary>보유한 character_id들로 만든다(인자 나열 형태).</summary>
        public static IRecruitmentOwnership Of(params string[] ownedCharacterIds)
        {
            return new IdSet(ownedCharacterIds);
        }

        private sealed class IdSet : IRecruitmentOwnership
        {
            private readonly HashSet<string> owned;

            internal IdSet(IEnumerable<string> ids)
            {
                owned = new HashSet<string>(StringComparer.Ordinal);
                if (ids == null) return;

                foreach (string id in ids)
                {
                    if (!string.IsNullOrEmpty(id)) owned.Add(id);
                }
            }

            public bool IsOwned(string characterId)
            {
                return !string.IsNullOrEmpty(characterId) && owned.Contains(characterId);
            }
        }
    }
}
