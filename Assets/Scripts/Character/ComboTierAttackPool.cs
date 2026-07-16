using System.Collections.Generic;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 콤보 티어 하나에 대응하는 공격 모션 목록만 담는 에셋. 어떤 티어에 쓰일지는 이 에셋 자체가
    /// 알지 못한다 - CatKnightIdleAnimator가 tier1Pool/tier2Pool/tier3Pool 필드로 어느 에셋을 어느
    /// 티어에 쓸지 정한다. 같은 AttackMotionDefinition을 여러 풀 에셋이 함께 참조해도 되고(예: 기본
    /// 모션을 Tier1/Tier2 풀 양쪽에 넣기), 상위 티어에 하위 티어 모션을 포함할지는 자동으로 처리하지
    /// 않는다 - 필요하면 Inspector에서 직접 그 항목을 추가해야 한다.
    /// </summary>
    [CreateAssetMenu(fileName = "ComboTierAttackPool", menuName = "Character/Combo Tier Attack Pool")]
    public class ComboTierAttackPool : ScriptableObject
    {
        [Tooltip("이 풀에 포함된 공격 모션들. 같은 AttackMotionDefinition을 여러 풀에서 참조할 수 있다.")]
        [SerializeField] private List<AttackMotionDefinition> motions = new List<AttackMotionDefinition>();

        public IReadOnlyList<AttackMotionDefinition> Motions => motions;
    }
}
