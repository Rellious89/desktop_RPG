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

            // ---- Damage Number: 위치뿐 아니라 연출값 전체를 몬스터별로 관리한다. Min Spawn Interval/
            // Pool Size는 개별 몬스터의 연출값이 아니라 성능 안전장치라 여기 옮기지 않고
            // DamageNumberSpawner에 그대로 남겨둔다. Anchor Transform도 마찬가지로 옮기지 않는다 -
            // 여기서는 Offset만 갖고, 실제 Anchor/최종 위치 계산은 런타임(TargetCombatController)이
            // 담당한다. 기본값은 DamageNumberSpawner의 기존 Inspector 기본값과 동일하게 맞췄다.
            [Tooltip("데미지 숫자가 뜨는 위치 - CombatStageLayout/Actor Offset이 반영된 몬스터의 최종 위치 " +
                     "기준 로컬 오프셋(월드 유닛). Motion Profile이 연결된 몬스터는 DamageNumberSpawner 자체 " +
                     "anchor 대신 이 값을 우선 쓴다(연결 안 됐으면 기존 DamageNumberSpawner 설정 그대로).")]
            [SerializeField] private Vector2 damageNumberOffset = new Vector2(0f, 1f);

            [Tooltip("Offset 기준 좌우 랜덤 폭(월드 유닛).")]
            [Min(0f)] [SerializeField] private float damageNumberRandomHorizontalJitter = 0.1f;

            [Tooltip("떠오르는 거리(월드 유닛).")]
            [Min(0f)] [SerializeField] private float damageNumberRiseDistance = 0.4f;

            [Tooltip("떠오르기 시작해서 완전히 사라지기까지 걸리는 시간(초).")]
            [Min(0.01f)] [SerializeField] private float damageNumberDuration = 0.6f;

            [SerializeField] private Color damageNumberTextColor = Color.red;

            [Min(0.01f)] [SerializeField] private float damageNumberFontSize = 15f;

            [SerializeField] private int damageNumberSortingOrder = 10;

            public int HoldFrame => holdFrame;
            public int RecoveryFrame => recoveryFrame;
            public float RecoveryDuration => recoveryDuration;
            public float HoldTimeout => holdTimeout;
            public float ShakeStrength => shakeStrength;
            public float ShakeFrequency => shakeFrequency;
            public float ShakeDecayDuration => shakeDecayDuration;
            public Vector2 DamageNumberOffset => damageNumberOffset;
            public float DamageNumberRandomHorizontalJitter => Mathf.Max(0f, damageNumberRandomHorizontalJitter);
            public float DamageNumberRiseDistance => Mathf.Max(0f, damageNumberRiseDistance);
            public float DamageNumberDuration => Mathf.Max(0.01f, damageNumberDuration);
            public Color DamageNumberTextColor => damageNumberTextColor;
            public float DamageNumberFontSize => Mathf.Max(0.01f, damageNumberFontSize);
            public int DamageNumberSortingOrder => damageNumberSortingOrder;
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
        [Tooltip("원본 스프라이트는 오른쪽을 보는 것을 기준으로 한다. 켜면 SpriteRenderer.flipX로 좌우 반전해서 표시한다(프레임 데이터 자체는 건드리지 않는다).")]
        [SerializeField] private bool spriteFlipX;

        [Header("Motions")]
        [SerializeField] private FrameClip baseIdle = new FrameClip();
        [SerializeField] private List<FrameClip> idleEvents = new List<FrameClip>();
        [Min(0.1f)]
        [SerializeField] private float idleEventCheckInterval = 4f;
        [Range(0f, 1f)]
        [SerializeField] private float idleEventChance = 0.5f;
        [SerializeField] private FrameClip hit = new FrameClip();
        [SerializeField] private FrameClip defeat = new FrameClip();

        [Header("Hit Reaction")]
        [SerializeField] private HitReactionSettings hitReaction = new HitReactionSettings();

        [Header("Editor Preview")]
        [SerializeField] private PreviewSettings preview = new PreviewSettings();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string ResourceFolderPath => resourceFolderPath;
        public bool SpriteFlipX => spriteFlipX;
        public FrameClip BaseIdle => baseIdle;
        public IReadOnlyList<FrameClip> IdleEvents => idleEvents ?? (IReadOnlyList<FrameClip>)Array.Empty<FrameClip>();
        public float IdleEventCheckInterval => Mathf.Max(0.1f, idleEventCheckInterval);
        public float IdleEventChance => Mathf.Clamp01(idleEventChance);
        public FrameClip Hit => hit;
        public FrameClip Defeat => defeat;
        public HitReactionSettings HitReaction => hitReaction;
        public PreviewSettings Preview => preview;
    }
}
