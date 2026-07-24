using UnityEngine;

namespace Common
{
    /// <summary>
    /// DamageNumberSpawner.Spawn()에 몬스터별 연출값을 실어 보낼 때 쓰는 값 묶음. Min Spawn Interval/
    /// Pool Size/Anchor Transform은 개별 몬스터의 연출값이 아니라 스포너 쪽 성능·생성 안전장치라 여기
    /// 담지 않는다 - DamageNumberSpawner 자신의 Inspector 값을 그대로 쓴다.
    /// </summary>
    public readonly struct DamageNumberPresentation
    {
        public readonly float RandomHorizontalJitter;
        public readonly float RiseDistance;
        public readonly float Duration;
        public readonly Color TextColor;
        public readonly float FontSize;
        public readonly int SortingOrder;

        public DamageNumberPresentation(float randomHorizontalJitter, float riseDistance, float duration, Color textColor, float fontSize, int sortingOrder)
        {
            RandomHorizontalJitter = randomHorizontalJitter;
            RiseDistance = riseDistance;
            Duration = duration;
            TextColor = textColor;
            FontSize = fontSize;
            SortingOrder = sortingOrder;
        }
    }
}
