using System;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 공격 모션 1개(프레임 배열 + 재생 타이밍)를 담는 독립 에셋. 여러 ComboTierAttackPool이 같은
    /// 에셋을 참조로 공유할 수 있다 - 데이터를 복제하지 않으므로 이 에셋을 수정하면 그것을 참조하는
    /// 모든 풀에 즉시 반영된다. 0번 프레임이 Windup 시작, hitFrameIndex가 타격 프레임, 그 이후가
    /// Recovery다. 공격 하나의 Cast/Hit 연출(이펙트·사운드·발사체)까지 전부 이 에셋이 단독으로
    /// 소유한다 - 씬 컴포넌트가 값을 대신 채워 넣는 fallback은 없다.
    /// </summary>
    [CreateAssetMenu(fileName = "AttackMotionDefinition", menuName = "Character/Attack Motion Definition")]
    public class AttackMotionDefinition : ScriptableObject, IAttackMotion
    {
        [Tooltip("Motion Editor에서만 참고하는 제작 메모. 런타임 공격 로직에는 사용하지 않는다.")]
        [SerializeField] private string editorDescription;

        [Tooltip("프레임 낱장 Sprite 배열(아틀라스 런타임 슬라이싱 아님). 프레임 수는 이 배열 길이 그대로다.")]
        [SerializeField] private Sprite[] frames;

        [Header("Frame-synced Overlay")]
        [Tooltip("공격 본체 frames와 같은 인덱스를 사용하는 오버레이 스프라이트. 비어 있거나 해당 요소가 null이면 그 프레임에는 오버레이가 없다.")]
        [SerializeField] private Sprite[] overlayFrames = Array.Empty<Sprite>();

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

        [Header("Queue (Direct Input 전용)")]
        [Tooltip("Direct Input 모드에서만 쓰는 값이다. 마지막 입력 이후 이 시간(초) 동안 새 입력이 없으면, " +
                 "남아있는 예약(대기열)을 전부 취소하고 진행 중인 재생만 마친 뒤 복귀한다. 0.15~0.25 권장. " +
                 "Use Accumulated Input이 켜져 있으면 이 값은 전혀 사용되지 않는다(대기열 자체가 없다).")]
        [SerializeField] private float queueExpireTimeout = 0.15f;

        [Header("Input Response")]
        [Tooltip("이 공격의 입력 방식.\n" +
                 "false(기본) = Direct Input: 키 입력 1회 = 대기열 +1 = 타격 1회(기존 동작).\n" +
                 "true = Accumulated Input: 첫 입력으로 공격 준비를 시작하고, 추가 입력을 모아 " +
                 "Required Inputs to Strike에 도달하는 순간 Cast/Hit이 발생한다(궁수/마법사용).")]
        [SerializeField] private bool useAccumulatedInput;

        [Tooltip("Accumulated Input 전용. 공격 시작(첫 입력 1회 포함)부터 타격까지 필요한 총 입력 수. " +
                 "1 이상이어야 한다.")]
        [Min(1)]
        [SerializeField] private int requiredInputsToStrike = 10;

        [Tooltip("Accumulated Input 전용. 충전 중 새 입력이 끊긴 뒤 현재 충전 자세를 그대로 유지하는 시간(초). " +
                 "이 시간 동안에는 충전량이 줄지 않는다.")]
        [Min(0f)]
        [SerializeField] private float noInputGraceTime = 0.5f;

        [Tooltip("Accumulated Input 전용. 유예 시간이 지난 뒤 충전량이 0으로 돌아가기까지 걸리는 시간(초) - " +
                 "가득 찬 충전이 0이 되는 데 걸리는 시간 기준이다. 감소 중 다시 입력하면 그 지점에서 이어서 " +
                 "충전된다. 0이면 유예 시간 직후 즉시 초기화된다.")]
        [Min(0f)]
        [SerializeField] private float chargeDecayDuration = 1f;

        [Tooltip("Accumulated Input 전용. 발사 이후(타격 프레임~Recovery) 들어온 입력을 다음 공격의 충전으로 " +
                 "넘길지 여부. 켜두면 빠르게 타이핑할 때 입력이 버려지지 않는다(권장: On).")]
        [SerializeField] private bool carryOverflowInputs = true;

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
        [Tooltip("이 공격의 Hit Frame에서 사용할 이펙트 프리팹. 비어 있으면 이 공격에는 타격 이펙트가 없다 " +
                 "(씬에서 대신 생성해주는 기본 이펙트는 없다). Motion Editor에서도 같은 값을 배치 기준으로 쓴다.")]
        [SerializeField] private GameObject hitEffectPrefab;

        [Tooltip("선택한 몬스터의 Receive Point를 기준으로 더할 이펙트 위치(월드 유닛)")]
        [SerializeField] private Vector2 hitEffectOffset;

        [Min(0.01f)]
        [SerializeField] private float hitEffectScale = 1f;

        [Tooltip("이 공격만 타격 이펙트의 랜덤 출력 범위를 직접 정한다. 끄면 맞는 몬스터의 " +
                 "HitEffectSpawner에 설정된 Spawn Jitter를 그대로 쓴다(몬스터 덩치에 맞춰 잡아둔 기본값). " +
                 "0도 의미 있는 값이라(= 항상 정확히 한 점에 맞음) 값만으로는 '지정 안 함'과 구분할 수 없어 " +
                 "이 토글로 구분한다.")]
        [SerializeField] private bool overrideHitEffectJitter;

        [Tooltip("Hit Effect가 튀는 범위(월드 유닛). X/Y 각각 ±값 사이에서 균등 랜덤으로 흩어진다 - " +
                 "0,0이면 랜덤 없이 Effect Offset 지점에 정확히 생성된다. Override 토글이 켜져 있을 때만 쓰인다.")]
        [SerializeField] private Vector2 hitEffectJitter;

        [Tooltip("이 공격의 Hit Frame에서 사용할 사운드. 비어 있으면 이 공격에는 타격음이 없다 " +
                 "(씬에서 대신 재생해주는 기본 Hit 클립은 없다).")]
        [SerializeField] private AudioClip hitSound;

        [Header("Projectile")]
        [Tooltip("Cast Frame에서 발사할 발사체 prefab(루트에 ProjectileMover 필요). 비어 있으면 발사체 없이 " +
                 "기존 근접 공격과 완전히 동일하게 동작한다. 비행 시간은 (Hit Frame - Cast Frame) / FPS로 " +
                 "자동 계산되므로 별도 속도 설정이 없고, Hit Frame이 Cast Frame보다 뒤여야 한다.")]
        [SerializeField] private GameObject projectilePrefab;

        [Tooltip("시전자 Actor Origin 기준 발사 위치(로컬 유닛) - 시전 손이나 지팡이 끝에 맞춘다. " +
                 "캐릭터가 flipX 상태면 X 오프셋이 좌우 반전된다.")]
        [SerializeField] private Vector2 projectileLaunchOffset;

        [Min(0.01f)]
        [SerializeField] private float projectileScale = 1f;

        public Sprite[] Frames => frames ?? Array.Empty<Sprite>();
        public Sprite[] OverlayFrames => overlayFrames ?? Array.Empty<Sprite>();
        public float AnimationFps => animationFps;
        public int HitFrameIndex => hitFrameIndex;
        public float EndFrameDuration => endFrameDuration;
        public float QueueExpireTimeout => queueExpireTimeout;

        public bool UseAccumulatedInput => useAccumulatedInput;
        public int RequiredInputsToStrike => Mathf.Max(1, requiredInputsToStrike);
        public float NoInputGraceTime => Mathf.Max(0f, noInputGraceTime);
        public float ChargeDecayDuration => Mathf.Max(0f, chargeDecayDuration);
        public bool CarryOverflowInputs => carryOverflowInputs;

        public int CastFrameIndex => castFrameIndex;
        public GameObject CastEffectPrefab => castEffectPrefab;
        public Vector2 CastEffectOffset => castEffectOffset;
        public float CastEffectScale => Mathf.Max(0.01f, castEffectScale);
        public AudioClip CastSound => castSound;

        public GameObject HitEffectPrefab => hitEffectPrefab;
        public Vector2 HitEffectOffset => hitEffectOffset;
        public float HitEffectScale => Mathf.Max(0.01f, hitEffectScale);
        public bool OverrideHitEffectJitter => overrideHitEffectJitter;
        /// <summary>음수 범위는 의미가 없으므로(Random.Range(-x, x)에서 부호가 무의미) 절댓값으로 보정해
        /// 돌려준다 - 인스펙터에 -0.2가 들어와도 ±0.2로 동작한다.</summary>
        public Vector2 HitEffectJitter => new Vector2(Mathf.Abs(hitEffectJitter.x), Mathf.Abs(hitEffectJitter.y));
        public AudioClip HitSound => hitSound;

        public GameObject ProjectilePrefab => projectilePrefab;
        public Vector2 ProjectileLaunchOffset => projectileLaunchOffset;
        public float ProjectileScale => Mathf.Max(0.01f, projectileScale);

#if UNITY_EDITOR
        /// <summary>런타임 프로퍼티는 항상 안전한 값으로 보정해서 돌려주지만(Mathf.Max), 잘못 들어온 값이
        /// 조용히 다른 값으로 동작하면 제작자가 알 수 없다. 에셋을 편집할 때 바로 알 수 있게 여기서
        /// 직렬화 값 자체를 유효 범위로 되돌리고 경고를 남긴다.</summary>
        private void OnValidate()
        {
            if (requiredInputsToStrike < 1)
            {
                Debug.LogWarning($"[AttackMotionDefinition] '{name}': Required Inputs to Strike는 1 이상이어야 합니다 " +
                                 $"(입력값 {requiredInputsToStrike}) - 1로 보정합니다.", this);
                requiredInputsToStrike = 1;
            }
            if (noInputGraceTime < 0f) noInputGraceTime = 0f;
            if (chargeDecayDuration < 0f) chargeDecayDuration = 0f;
        }
#endif
    }
}
