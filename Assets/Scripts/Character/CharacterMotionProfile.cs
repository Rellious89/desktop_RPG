using System;
using System.Collections.Generic;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 캐릭터 한 종의 애니메이션과 공격 이동 제작값을 함께 보관하는 에셋 - 캐릭터 모션 제작
    /// 데이터의 <b>유일한 원천</b>이다. Motion Editor가 이 에셋을 편집하고, 런타임 컴포넌트
    /// (PlayerCharacterAnimator/AttackMovement)는 이 값만 읽는다. 씬 컴포넌트에 같은 값을 다시
    /// 직렬화해두는 fallback 경로는 없다 - 프로필이 비어 있으면 해당 캐릭터는 조용히 임시
    /// 데이터로 동작하는 대신 명시적인 오류를 남기고 비활성화된다.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterMotionProfile", menuName = "Character/Character Motion Profile")]
    public class CharacterMotionProfile : ScriptableObject
    {
        [Serializable]
        public class FrameClip
        {
            [SerializeField] private string displayName = "Motion";
            [Tooltip("Motion Editor에서만 참고하는 제작 메모. 런타임 애니메이션에는 사용하지 않는다.")]
            [SerializeField] private string editorDescription;
            [SerializeField] private Sprite[] frames = Array.Empty<Sprite>();
            [Min(0.01f)]
            [SerializeField] private float animationFps = 6f;

            public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Motion" : displayName;
            public Sprite[] Frames => frames ?? Array.Empty<Sprite>();
            public float AnimationFps => Mathf.Max(0.01f, animationFps);
        }

        [Serializable]
        public class AttackMovementSettings
        {
            [Tooltip("Positive: move forward. Negative: move backward. Zero: no movement.")]
            [SerializeField] private float moveDistance = 0.2f;
            [Min(0.001f)]
            [SerializeField] private float moveOutDuration = 0.14f;
            [Min(0.001f)]
            [SerializeField] private float moveBackDuration = 0.05f;

            public float MoveDistance => moveDistance;
            public float MoveOutDuration => Mathf.Max(0.001f, moveOutDuration);
            public float MoveBackDuration => Mathf.Max(0.001f, moveBackDuration);
        }

        /// <summary>이 캐릭터 자신의 표시 보정만 담는다. 상대(몬스터) 표시는 전적으로
        /// MonsterMotionProfile이, 두 액터의 기본 서 있는 위치는 공용 CombatStageLayout이 담당한다.</summary>
        [Serializable]
        public class PreviewSettings
        {
            [Header("Actor Presentation")]
            [Tooltip("Combat Stage Layout의 Character Slot Position에 더할 이 캐릭터만의 보정 위치(로컬 유닛). " +
                     "Motion Editor Preview와 런타임(AttackMovement) 양쪽에서 동일하게 쓰인다.")]
            [SerializeField] private Vector2 characterOffset = Vector2.zero;
            [Tooltip("적 Pivot 기준 실제 피격 지점. 이펙트와 사운드 프레임 트랙도 이 지점을 기준으로 확장한다.")]
            [SerializeField] private Vector2 receivePointOffset = new Vector2(0f, 0.35f);
            [Tooltip("이 캐릭터의 표시 배율. Preview와 런타임(AttackMovement) 양쪽에서 동일하게 적용된다.")]
            [Min(0.05f)]
            [SerializeField] private float characterScale = 1f;

            public Vector2 ActorOffset => characterOffset;
            public Vector2 ReceivePointOffset => receivePointOffset;
            public float ActorScale => Mathf.Max(0.05f, characterScale);
        }

        [Header("Identity")]
        [SerializeField] private string displayName = "New Character";
        [Tooltip("이 프로필의 원본 스프라이트 폴더. Motion Editor가 Art 폴더와 프로필을 연결할 때 사용한다.")]
        [SerializeField] private string resourceFolderPath;

        [Header("Idle")]
        [SerializeField] private FrameClip baseIdle = new FrameClip();
        [SerializeField] private List<FrameClip> idleEvents = new List<FrameClip>();
        [Min(0.1f)]
        [SerializeField] private float idleEventCheckInterval = 4f;
        [Range(0f, 1f)]
        [SerializeField] private float idleEventChance = 0.5f;

        [Header("Attack Pools")]
        [SerializeField] private ComboTierAttackPool tier1Pool;
        [SerializeField] private ComboTierAttackPool tier2Pool;
        [SerializeField] private ComboTierAttackPool tier3Pool;

        [Header("Attack Movement")]
        [SerializeField] private AttackMovementSettings attackMovement = new AttackMovementSettings();

        [Header("Editor Preview")]
        [SerializeField] private PreviewSettings preview = new PreviewSettings();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string ResourceFolderPath => resourceFolderPath;
        public FrameClip BaseIdle => baseIdle;
        public IReadOnlyList<FrameClip> IdleEvents => idleEvents ?? (IReadOnlyList<FrameClip>)Array.Empty<FrameClip>();
        public float IdleEventCheckInterval => Mathf.Max(0.1f, idleEventCheckInterval);
        public float IdleEventChance => Mathf.Clamp01(idleEventChance);
        public ComboTierAttackPool Tier1Pool => tier1Pool;
        public ComboTierAttackPool Tier2Pool => tier2Pool;
        public ComboTierAttackPool Tier3Pool => tier3Pool;
        public AttackMovementSettings AttackMovement => attackMovement;
        public PreviewSettings Preview => preview;

        /// <summary>
        /// 이 프로필로 캐릭터를 실제로 구동할 수 있는지 판정하는 <b>단일 규칙</b>. 프로필이 연결돼
        /// 있고 Base Idle에 재생 가능한 프레임이 하나 이상 있어야 한다(공격 풀은 없어도 Idle은
        /// 돌아가므로 여기서는 보지 않는다 - PlayerCharacterAnimator가 따로 오류를 남긴다).
        ///
        /// PlayerCharacterAnimator와 AttackMovement가 <b>반드시 이 메서드를 함께</b> 써야 한다.
        /// 각자 다른 기준(예: 한쪽은 null만, 다른 쪽은 Base Idle까지)으로 판정하면 "프로필은 있지만
        /// Base Idle이 비어 있는" 캐릭터가 애니메이션 없이 이동만 하는 상태로 남는다.
        /// </summary>
        public static bool IsPlayable(CharacterMotionProfile profile)
        {
            return profile != null
                   && profile.BaseIdle != null
                   && profile.BaseIdle.Frames.Length > 0;
        }
    }
}
