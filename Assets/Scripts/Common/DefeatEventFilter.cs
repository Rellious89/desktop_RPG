using UnityEngine;

namespace Common
{
    /// <summary>
    /// <see cref="Target.AnyTargetDefeated"/>를 구독하는 쪽이 "같은 처치를 두 번 처리하지 않도록"
    /// 쓰는 최소 방어 장치. 행동력 소비와 보상 지급처럼 서로 독립적으로 동작해야 하는 구독자는 각자
    /// 자기 필터를 하나씩 들고 쓴다(하나를 공유하면 먼저 처리한 쪽이 다른 쪽의 이벤트를 삼킨다).
    ///
    /// 판정 규칙은 <b>"같은 프레임 + 같은 targetId면 중복"</b>이다. 근거:
    ///   - Target은 IsDefeated 플래그로 처치당 정확히 한 번만 이벤트를 보낸다.
    ///   - 같은 대상이 다시 죽으려면 Fade-out -> 리젠 대기 -> Fade-in을 거쳐야 하므로 최소 여러
    ///     프레임이 걸린다.
    /// 따라서 같은 프레임에 같은 id가 두 번 들어오는 것은 정상 흐름에서 나올 수 없는 중복 호출이고,
    /// 서로 다른 몬스터가 같은 프레임에 죽는 경우(id가 다름)는 각각 정상 처리된다.
    /// </summary>
    public class DefeatEventFilter
    {
        private string lastTargetId;
        private int lastFrame = -1;

        /// <summary>이번 처치 이벤트를 처리해도 되는지 판정한다. 중복이면 false를 돌려주고, 왜
        /// 무시했는지 알 수 있도록 경고를 남긴다.</summary>
        public bool Accept(string targetId, Object context = null)
        {
            int frame = Time.frameCount;
            if (frame == lastFrame && targetId == lastTargetId)
            {
                Debug.LogWarning($"[DefeatEventFilter] '{targetId}'의 처치 이벤트가 같은 프레임에 중복 " +
                                 "발생해 한 번만 처리했습니다.", context);
                return false;
            }

            lastFrame = frame;
            lastTargetId = targetId;
            return true;
        }
    }
}
