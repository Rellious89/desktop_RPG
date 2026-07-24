using System;
using System.Collections.Generic;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 캐릭터 한 종의 애니메이션과 공격 이동 제작값을 함께 보관하는 에셋.
    /// Motion Editor가 이 에셋을 편집하고, 런타임 컴포넌트는 연결된 프로필이 있을 때만
    /// 프로필 값을 사용한다. 프로필이 비어 있으면 기존 Inspector 직렬화 값을 그대로 사용한다.
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
            [Tooltip("켜면 AttackMovement 컴포넌트의 Inspector 값 대신 이 프로필 값을 사용한다.")]
            [SerializeField] private bool overrideComponentValues = true;
            [Tooltip("Positive: move forward. Negative: move backward. Zero: no movement.")]
            [SerializeField] private float moveDistance = 0.2f;
            [Min(0.001f)]
            [SerializeField] private float moveOutDuration = 0.14f;
            [Min(0.001f)]
            [SerializeField] private float moveBackDuration = 0.05f;

            public bool OverrideComponentValues => overrideComponentValues;
            public float MoveDistance => moveDistance;
            public float MoveOutDuration => Mathf.Max(0.001f, moveOutDuration);
            public float MoveBackDuration => Mathf.Max(0.001f, moveBackDuration);
        }

        [Serializable]
        public class PreviewSettings
        {
            // targetIdleSprite/targetHitSprite: 몬스터 프로필이 도입되기 전 남은 미사용 레거시 필드.
            // 지금은 대상(몬스터) 표시를 MonsterMotionProfile이 전담하므로 어디서도 읽지 않는다.
            // 기존에 값이 들어간 에셋이 있을 수 있어 필드/데이터는 삭제하지 않는다.
            [SerializeField] private Sprite targetIdleSprite;
            [SerializeField] private Sprite targetHitSprite;

            [Header("Actor Presentation")]
            [Tooltip("Combat Stage Layout의 Character Slot Position에 더할 이 캐릭터만의 보정 위치(로컬 유닛). " +
                     "Motion Editor Preview와 런타임(AttackMovement) 양쪽에서 동일하게 쓰인다.")]
            [SerializeField] private Vector2 characterOffset = Vector2.zero;
            [Tooltip("적 Pivot 기준 실제 피격 지점. 이펙트와 사운드 프레임 트랙도 이 지점을 기준으로 확장한다.")]
            [SerializeField] private Vector2 receivePointOffset = new Vector2(0f, 0.35f);
            [Tooltip("이 캐릭터의 표시 배율. Preview와 런타임(AttackMovement) 양쪽에서 동일하게 적용된다.")]
            [Min(0.05f)]
            [SerializeField] private float characterScale = 1f;

            // targetOffset/targetScale: 예전에는 "캐릭터-몬스터 사이 기본 거리"/"몬스터를 이 배율만큼
            // 추가로 더 키워서 보여주기"를 캐릭터 프로필이 들고 있었다. 전자는 공용 CombatStageLayout.
            // MonsterSlotPosition으로, 후자는 몬스터 자신의 Actor Scale로 이전됐다 - 각 액터는 이제 자기
            // 표시 보정만 관리한다. 기존 데이터 보존을 위해 필드는 남기되 더 이상 계산에 쓰지 않는다.
            [SerializeField] private Vector2 targetOffset = new Vector2(1.15f, 0f);
            [Min(0.05f)]
            [SerializeField] private float targetScale = 1f;

            public Sprite TargetIdleSprite => targetIdleSprite;
            public Sprite TargetHitSprite => targetHitSprite;
            public Vector2 ActorOffset => characterOffset;
            public Vector2 ReceivePointOffset => receivePointOffset;
            public float ActorScale => Mathf.Max(0.05f, characterScale);
            [Obsolete("CombatStageLayout.MonsterSlotPosition으로 대체됨 - 더 이상 배치 계산에 쓰이지 않는다.")]
            public Vector2 TargetOffset => targetOffset;
            [Obsolete("몬스터 자신의 MonsterMotionProfile.Preview.ActorScale로 대체됨 - 더 이상 배치 계산에 쓰이지 않는다.")]
            public float TargetScale => Mathf.Max(0.05f, targetScale);
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
    }
}
