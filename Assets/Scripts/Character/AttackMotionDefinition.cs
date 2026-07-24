using System;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 공격 모션 1개(프레임 배열 + 재생 타이밍)를 담는 독립 에셋. 여러 ComboTierAttackPool이 같은
    /// 에셋을 참조로 공유할 수 있다 - 데이터를 복제하지 않으므로 이 에셋을 수정하면 그것을 참조하는
    /// 모든 풀에 즉시 반영된다. 필드 구성은 기존 PlayerCharacterAnimator.AttackAnimation과 동일하다
    /// (0번 프레임이 Windup 시작, hitFrameIndex가 타격 프레임, 그 이후가 Recovery).
    /// </summary>
    [CreateAssetMenu(fileName = "AttackMotionDefinition", menuName = "Character/Attack Motion Definition")]
    public class AttackMotionDefinition : ScriptableObject, IAttackMotion
    {
        [Tooltip("Motion Editor에서만 참고하는 제작 메모. 런타임 공격 로직에는 사용하지 않는다.")]
        [SerializeField] private string editorDescription;

        [Tooltip("프레임 낱장 Sprite 배열(아틀라스 런타임 슬라이싱 아님). 프레임 수는 이 배열 길이 그대로다.")]
        [SerializeField] private Sprite[] frames;

        [Header("Playback")]
        [Tooltip("Windup/Recovery 프레임 재생 속도(초당 프레임 전환 횟수)")]
        [SerializeField] private float animationFps = 18f;

        [Tooltip("이 프레임(0부터)에 도달하면 타격 판정(HitPoint)이 발생한다. " +
                 "실제 프레임 수를 넘으면 마지막 프레임으로 자동 보정된다.")]
        [Min(0)]
        [SerializeField] private int hitFrameIndex = 1;

        [Header("Recovery (hitFrameIndex 다음 프레임부터 마지막 프레임까지)")]
        [Tooltip("마지막 프레임에 도달한 뒤 그 프레임을 유지하는 시간(초)")]
        [SerializeField] private float endFrameDuration = 0.12f;

        [Header("Queue")]
        [Tooltip("마지막 입력 이후 이 시간(초) 동안 새 입력이 없으면, 남아있는 예약(대기열)을 전부 취소하고 " +
                 "진행 중인 재생만 마친 뒤 복귀한다. 0.15~0.25 권장")]
        [SerializeField] private float queueExpireTimeout = 0.15f;

        [Header("Cast Presentation")]
        [Tooltip("이 프레임(0부터)에 도달하면 Cast Presentation(이펙트/사운드)이 한 번 실행된다. " +
                 "Hit Frame Index와 같아도 되고 달라도 된다.")]
        [Min(0)]
        [SerializeField] private int castFrameIndex;

        [Tooltip("시전 시 사용할 이펙트 프리팹(시전자 기준). 비어 있으면 이펙트 없음.")]
        [SerializeField] private GameObject castEffectPrefab;

        [Tooltip("시전자 Actor Origin 기준으로 더할 이펙트 위치(월드 유닛)")]
        [SerializeField] private Vector2 castEffectOffset;

        [Min(0.01f)]
        [SerializeField] private float castEffectScale = 1f;

        [Tooltip("Cast Frame에서 한 번 재생할 사운드. 비어 있으면 시전 사운드 없음.")]
        [SerializeField] private AudioClip castSound;

        [Header("Hit Presentation")]
        [Tooltip("이 공격의 Hit Frame에서 사용할 이펙트 프리팹. 런타임 연결 전에도 Motion Editor에서 배치 기준으로 사용한다.")]
        [SerializeField] private GameObject hitEffectPrefab;

        [Tooltip("선택한 몬스터의 Receive Point를 기준으로 더할 이펙트 위치(월드 유닛)")]
        [SerializeField] private Vector2 hitEffectOffset;

        [Min(0.01f)]
        [SerializeField] private float hitEffectScale = 1f;

        [Tooltip("이 공격의 Hit Frame에서 사용할 사운드. 비어 있으면 씬 기본 Hit SFX(AudioManager.hitClip)로 대체된다.")]
        [SerializeField] private AudioClip hitSound;

        public Sprite[] Frames => frames ?? Array.Empty<Sprite>();
        public float AnimationFps => animationFps;
        public int HitFrameIndex => hitFrameIndex;
        public float EndFrameDuration => endFrameDuration;
        public float QueueExpireTimeout => queueExpireTimeout;

        public int CastFrameIndex => castFrameIndex;
        public GameObject CastEffectPrefab => castEffectPrefab;
        public Vector2 CastEffectOffset => castEffectOffset;
        public float CastEffectScale => Mathf.Max(0.01f, castEffectScale);
        public AudioClip CastSound => castSound;

        public GameObject HitEffectPrefab => hitEffectPrefab;
        public Vector2 HitEffectOffset => hitEffectOffset;
        public float HitEffectScale => Mathf.Max(0.01f, hitEffectScale);
        public AudioClip HitSound => hitSound;
    }
}
