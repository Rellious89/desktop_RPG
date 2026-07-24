using UnityEngine;

namespace Common
{
    /// <summary>
    /// 캐릭터/몬스터 공용 무대 배치값. "캐릭터는 기본으로 어디 서 있는가"/"몬스터는 기본으로 어디 서
    /// 있는가"만 담당하고, 각 액터 고유의 보정(Actor Offset/Scale, 몬스터의 Sprite Flip X 등)은
    /// CharacterMotionProfile/MonsterMotionProfile이 각자 갖는다.
    ///
    /// Motion Editor Preview와 런타임(AttackMovement/TargetCombatController)이 이 값 + 각 프로필의
    /// Actor Offset을 더해 동일한 공식으로 위치를 계산한다 - 씬 Transform을 손으로 맞출 필요가 없다.
    /// 프로젝트에는 하나만 존재하며 Assets/Data/MotionProfiles/CombatStageLayout.asset로 공유된다.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatStageLayout", menuName = "Common/Combat Stage Layout")]
    public class CombatStageLayout : ScriptableObject
    {
        [Tooltip("캐릭터가 기본으로 서는 위치(로컬 유닛, StageVisualRoot 기준). 기본적으로 좌측.")]
        [SerializeField] private Vector2 characterSlotPosition = new Vector2(-0.7f, 0f);

        [Tooltip("몬스터가 기본으로 서는 위치(로컬 유닛, StageVisualRoot 기준). 기본적으로 우측.")]
        [SerializeField] private Vector2 monsterSlotPosition = new Vector2(0.4f, 0f);

        public Vector2 CharacterSlotPosition => characterSlotPosition;
        public Vector2 MonsterSlotPosition => monsterSlotPosition;
    }
}
