using System;

namespace Recruitment
{
    /// <summary>
    /// 뽑기가 쓰는 <b>난수 한 줄기</b>. 인터페이스로 끼워 넣는 이유는 하나다 - 시험이 경계값을
    /// 글자 그대로 확인할 수 있어야 하기 때문이다. <see cref="UnityEngine.Random"/>처럼 전역 상태를
    /// 쓰는 것을 직접 부르면 "이 뽑기가 무엇을 뽑을지"가 시험을 언제 어떤 순서로 돌리느냐에 따라
    /// 달라지고, 그러면 가중치 경계를 확인할 방법이 없다.
    /// </summary>
    public interface IRecruitmentRandom
    {
        /// <summary>0 이상 <paramref name="maxExclusive"/> 미만의 정수 하나. <paramref name="maxExclusive"/>는
        /// 언제나 1 이상으로만 넘어온다.</summary>
        int Next(int maxExclusive);
    }

    /// <summary>
    /// <see cref="System.Random"/> 하나를 감싼 기본 난수. <b>Unity의 전역 난수를 쓰지 않는다</b> -
    /// 전역 난수를 건드리면 같은 프레임의 다른 코드(연출/전투)가 보는 수열까지 달라지기 때문이다.
    ///
    /// 씨앗을 지정하면 <b>같은 씨앗은 같은 수열</b>을 준다 - 재현이 필요한 자리에서 그대로 쓸 수 있다.
    /// </summary>
    public sealed class SystemRecruitmentRandom : IRecruitmentRandom
    {
        private readonly Random random;

        /// <summary>시간 기반 씨앗으로 만든다.</summary>
        public SystemRecruitmentRandom()
        {
            random = new Random();
        }

        /// <summary>정해진 씨앗으로 만든다.</summary>
        public SystemRecruitmentRandom(int seed)
        {
            random = new Random(seed);
        }

        public int Next(int maxExclusive)
        {
            return maxExclusive <= 1 ? 0 : random.Next(maxExclusive);
        }
    }
}
