using System;
using System.Collections.Generic;
using UnityEngine;

namespace Enemy
{
    /// <summary>몬스터 폴더 한 종에 대응하는 모션 제작 데이터.</summary>
    [CreateAssetMenu(fileName = "MonsterMotionProfile", menuName = "Enemy/Monster Motion Profile")]
    public class MonsterMotionProfile : ScriptableObject
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
        public class HitReactionSettings
        {
            [Min(0)] [SerializeField] private int holdFrame;
            [Min(0)] [SerializeField] private int recoveryFrame = 1;
            [Min(0f)] [SerializeField] private float recoveryDuration = 0.12f;
            [Min(0f)] [SerializeField] private float holdTimeout = 0.2f;
            [Min(0f)] [SerializeField] private float shakeStrength = 0.04f;
            [Min(0f)] [SerializeField] private float shakeFrequency = 35f;
            [Min(0f)] [SerializeField] private float shakeDecayDuration = 0.15f;

            public int HoldFrame => holdFrame;
            public int RecoveryFrame => recoveryFrame;
            public float RecoveryDuration => recoveryDuration;
            public float HoldTimeout => holdTimeout;
            public float ShakeStrength => shakeStrength;
            public float ShakeFrequency => shakeFrequency;
            public float ShakeDecayDuration => shakeDecayDuration;
        }

        [Serializable]
        public class PreviewSettings
        {
            [SerializeField] private Vector2 actorOffset;
            [SerializeField] private Vector2 receivePointOffset = new Vector2(0f, 0.35f);
            [Min(0.05f)] [SerializeField] private float actorScale = 1f;

            public Vector2 ActorOffset => actorOffset;
            public Vector2 ReceivePointOffset => receivePointOffset;
            public float ActorScale => Mathf.Max(0.05f, actorScale);
        }

        [Header("Identity")]
        [SerializeField] private string displayName = "New Monster";
        [SerializeField] private string resourceFolderPath;

        [Header("Motions")]
        [SerializeField] private FrameClip baseIdle = new FrameClip();
        [SerializeField] private List<FrameClip> idleEvents = new List<FrameClip>();
        [SerializeField] private FrameClip hit = new FrameClip();
        [SerializeField] private FrameClip defeat = new FrameClip();

        [Header("Hit Reaction")]
        [SerializeField] private HitReactionSettings hitReaction = new HitReactionSettings();

        [Header("Editor Preview")]
        [SerializeField] private PreviewSettings preview = new PreviewSettings();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string ResourceFolderPath => resourceFolderPath;
        public FrameClip BaseIdle => baseIdle;
        public IReadOnlyList<FrameClip> IdleEvents => idleEvents ?? (IReadOnlyList<FrameClip>)Array.Empty<FrameClip>();
        public FrameClip Hit => hit;
        public FrameClip Defeat => defeat;
        public HitReactionSettings HitReaction => hitReaction;
        public PreviewSettings Preview => preview;
    }
}
