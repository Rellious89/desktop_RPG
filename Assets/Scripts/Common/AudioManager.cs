using System;
using Character;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 최소 SFX 시스템. 공격/처치/레벨업이 이미 쏘고 있는 기존 정적 이벤트(PlayerCharacterAnimator.HitPoint,
    /// Target.AnyTargetDefeated, PlayerProgress.OnLevelUp)를 직접 구독해서 재생 "요청"을 받는다 -
    /// 이벤트를 쏘는 쪽은 이 매니저의 존재를 몰라도 되고 수정할 필요도 없다(SessionKillCounter,
    /// RewardToast와 같은 구독 패턴). AudioSource도 이 컴포넌트 하나만 갖고 있고, 다른 스크립트는
    /// AudioSource를 직접 만들거나 제어하지 않는다.
    ///
    /// 이 매니저는 <b>재생·쿨다운</b>을 담당한다. 모션에 딸린 사운드(공격별 Hit
    /// Sound/Cast Sound)의 단일 원천은 AttackMotionDefinition이고 여기에는 그 값을 대신할 기본 클립이
    /// 없다 - clip이 null이면 "그 모션에는 소리가 없다"는 뜻이다. 반대로 Defeat/LevelUp은 특정 모션이
    /// 아니라 게임 상태 변화에 붙는 전역 사운드라 여기서 계속 소유한다.
    ///
    /// 마스터 볼륨은 AudioListener.volume에 적용해 이후 추가되는 AudioSource도 함께 제어한다.
    /// sfxVolume은 기존 효과음의 개별 튜닝값이다. Defeat/LevelUp 클립은 아직
    /// 연결하지 않아도 되며, 비어 있으면 조용히 무시한다(콘솔 경고도 남기지 않는다).
    /// 씬에 하나만 두면 된다. ControlDock의 SoundToggle처럼 UI에서 접근할 수 있도록 Instance를 둔다.
    ///
    /// DefaultExecutionOrder(-100): SoundToggleButton 등 다른 GameObject의 OnEnable이 이 컴포넌트의
    /// Awake보다 먼저 실행되면 Instance가 아직 null이라 초기 상태를 잘못(Off로) 표시할 수 있다.
    /// Unity는 서로 다른 GameObject 간 Awake/OnEnable 순서를 보장하지 않으므로, 이 매니저의 Awake가
    /// 항상 먼저 실행되도록 실행 순서를 명시적으로 앞당긴다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        /// <summary>마스터 볼륨의 음소거 상태가 바뀔 때 발생한다.</summary>
        public static event Action<bool> OnSfxEnabledChanged;
        public static event Action<float> OnMasterVolumeChanged;

        [Header("SFX Tuning")]
        [Tooltip("기존 효과음의 개별 볼륨. 마스터 볼륨과 별도로 Inspector에서 조정한다.")]
        [Range(0f, 1f)]
        [SerializeField] private float sfxVolume = 0.3f;

        [Header("Hit SFX (클립은 각 공격 모션이 소유 - 여기서는 연타 제한만 관리)")]
        [Tooltip("이 시간(초) 안에 들어오는 추가 Hit SFX 요청은 무시한다. 빠른 연타로 소리가 겹쳐 시끄러워지는 것을 막는다.")]
        [SerializeField] private float hitSfxCooldown = 0.08f;

        [Header("Defeat SFX (Target 처치 시 - 클립 연결 전이면 무시)")]
        [SerializeField] private AudioClip defeatClip;

        [Header("Level Up SFX (PlayerProgress 레벨업 시 - 클립 연결 전이면 무시)")]
        [SerializeField] private AudioClip levelUpClip;

        private AudioSource audioSource;
        private float lastHitSfxTime = -999f;
        private float masterVolume = 1f;
        private float lastNonZeroMasterVolume = 1f;

        public bool SfxEnabled => masterVolume > 0f;
        public float MasterVolume => masterVolume;

        private void Awake()
        {
            Instance = this;

            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f; // UI/게임플레이 피드백용 2D 사운드 - 리스너 거리와 무관하게 항상 들려야 한다

            // 이전 설정의 sfxEnabled는 UiSettingsSaveSystem.Load에서 마스터 값으로 이관된다.
            UiSettingsData saved = UiSettingsSaveSystem.Load();
            if (saved != null)
            {
                masterVolume = saved.masterVolume;
                lastNonZeroMasterVolume = saved.lastNonZeroMasterVolume;
            }
            AudioListener.volume = masterVolume;
        }

        private void OnApplicationQuit()
        {
            SaveMasterVolume();
        }

        private void OnEnable()
        {
            PlayerCharacterAnimator.HitPoint += HandleHitPoint;
            PlayerCharacterAnimator.CastSoundCue += HandleCastSoundCue;
            Target.AnyTargetDefeated += HandleAnyTargetDefeated;
            PlayerProgress.OnLevelUp += HandleLevelUp;
        }

        private void OnDisable()
        {
            PlayerCharacterAnimator.HitPoint -= HandleHitPoint;
            PlayerCharacterAnimator.CastSoundCue -= HandleCastSoundCue;
            Target.AnyTargetDefeated -= HandleAnyTargetDefeated;
            PlayerProgress.OnLevelUp -= HandleLevelUp;
        }

        private void HandleHitPoint(AttackHitCue cue)
        {
            RequestHitSfx(cue.Sound);
        }

        private void HandleCastSoundCue(AudioClip clip)
        {
            RequestCastSfx(clip);
        }

        private void HandleAnyTargetDefeated(string targetId)
        {
            RequestDefeatSfx();
        }

        private void HandleLevelUp(int newLevel)
        {
            RequestLevelUpSfx();
        }

        /// <summary>hitSfxCooldown 안에 들어오는 추가 요청은 무시한다 - 시각/데미지 처리는 그대로 매
        /// 타격마다 일어나고, 소리만 압축한다. 어떤 클립을 재생할지는 전적으로 공격 모션
        /// (AttackMotionDefinition.HitSound)이 정한다 - 여기에는 대신 재생할 기본 Hit 클립이 없고,
        /// clip이 null이면 "이 공격에는 타격음이 없다"는 뜻이라 조용히 아무것도 재생하지 않는다.
        ///
        /// null 검사를 쿨다운 검사보다 <b>먼저</b> 한다: 소리 없는 공격은 쿨다운을 소비하지도, 갱신하지도
        /// 않는 완전한 no-op이어야 한다. 그렇지 않으면 무음 공격 하나가 바로 뒤따르는 유음 공격의
        /// 타격음을 잡아먹는다(예: Tier 풀에 무음 모션과 유음 모션이 섞여 있는 연타).</summary>
        public void RequestHitSfx(AudioClip clip)
        {
            if (clip == null) return;
            if (Time.time - lastHitSfxTime < hitSfxCooldown) return;
            lastHitSfxTime = Time.time;
            PlayOneShot(clip);
        }

        /// <summary>공격 모션의 Cast Sound를 재생한다. 기본 Cast 사운드 개념은 없으므로 clip이 비어
        /// 있으면 PlayOneShot이 조용히 무시한다.</summary>
        public void RequestCastSfx(AudioClip clip)
        {
            PlayOneShot(clip);
        }

        public void RequestDefeatSfx()
        {
            PlayOneShot(defeatClip);
        }

        public void RequestLevelUpSfx()
        {
            PlayOneShot(levelUpClip);
        }

        private void PlayOneShot(AudioClip clip)
        {
            if (sfxVolume <= 0f || clip == null) return;
            audioSource.PlayOneShot(clip, sfxVolume);
        }

        /// <summary>기존 ControlDock 토글은 0과 마지막 양수 마스터 볼륨 사이를 전환한다.</summary>
        public void ToggleSfxEnabled()
        {
            SetSfxEnabled(!SfxEnabled);
        }

        public void SetSfxEnabled(bool enabled)
        {
            SetMasterVolume(enabled ? lastNonZeroMasterVolume : 0f);
            SaveMasterVolume();
        }

        public void SetMasterVolume(float volume)
        {
            float clamped = Mathf.Clamp01(volume);
            if (Mathf.Approximately(masterVolume, clamped)) return;

            bool wasAudible = SfxEnabled;
            masterVolume = clamped;
            if (clamped > 0f) lastNonZeroMasterVolume = clamped;
            AudioListener.volume = clamped;
            OnMasterVolumeChanged?.Invoke(clamped);
            if (wasAudible != SfxEnabled) OnSfxEnabledChanged?.Invoke(SfxEnabled);
        }

        public void SaveMasterVolume()
        {
            UiSettingsSaveSystem.SaveMasterVolume(masterVolume, lastNonZeroMasterVolume);
        }
    }
}
