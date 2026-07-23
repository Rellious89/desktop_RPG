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
            [Header("Target")]
            [Tooltip("평상시 표시할 적 스프라이트")]
            [SerializeField] private Sprite targetIdleSprite;
            [Tooltip("히트 프레임에서 표시할 적 피격 스프라이트. 비어 있으면 평상시 스프라이트를 유지한다.")]
            [SerializeField] private Sprite targetHitSprite;

            [Header("Placement (world units)")]
            [SerializeField] private Vector2 characterOffset = Vector2.zero;
            [SerializeField] private Vector2 targetOffset = new Vector2(1.15f, 0f);
            [Tooltip("적 Pivot 기준 실제 피격 지점. 이펙트와 사운드 프레임 트랙도 이 지점을 기준으로 확장한다.")]
            [SerializeField] private Vector2 receivePointOffset = new Vector2(0f, 0.35f);

            [Header("Preview Scale")]
            [Min(0.05f)]
            [SerializeField] private float characterScale = 1f;
            [Min(0.05f)]
            [SerializeField] private float targetScale = 1f;

            public Sprite TargetIdleSprite => targetIdleSprite;
            public Sprite TargetHitSprite => targetHitSprite;
            public Vector2 CharacterOffset => characterOffset;
            public Vector2 TargetOffset => targetOffset;
            public Vector2 ReceivePointOffset => receivePointOffset;
            public float CharacterScale => Mathf.Max(0.05f, characterScale);
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
