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
    /// keybuddy는 업무 중 상시 실행되는 데스크탑 컴패니언이라 사운드가 방해되면 안 된다 - sfxEnabled
    /// 기본값은 꺼짐(false)이고, sfxVolume이 0이어도 완전히 무음이다. Defeat/LevelUp 클립은 아직
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

        /// <summary>sfxEnabled가 바뀔 때마다 발생. SoundToggle 같은 UI가 아이콘/색상을 갱신하는 데 쓴다.</summary>
        public static event Action<bool> OnSfxEnabledChanged;

        [Header("On/Off")]
        [Tooltip("꺼져 있으면 어떤 SFX도 재생하지 않는다. 상시 실행 앱이라 기본값은 꺼짐을 권장한다.")]
        [SerializeField] private bool sfxEnabled = false;

        [Tooltip("0~1. 0이면 sfxEnabled와 무관하게 완전히 무음이다.")]
        [Range(0f, 1f)]
        [SerializeField] private float sfxVolume = 0.3f;

        [Header("Hit SFX (기본 공격 타격마다)")]
        [SerializeField] private AudioClip hitClip;

        [Tooltip("이 시간(초) 안에 들어오는 추가 Hit SFX 요청은 무시한다. 빠른 연타로 소리가 겹쳐 시끄러워지는 것을 막는다.")]
        [SerializeField] private float hitSfxCooldown = 0.08f;

        [Header("Defeat SFX (Target 처치 시 - 클립 연결 전이면 무시)")]
        [SerializeField] private AudioClip defeatClip;

        [Header("Level Up SFX (PlayerProgress 레벨업 시 - 클립 연결 전이면 무시)")]
        [SerializeField] private AudioClip levelUpClip;

        private AudioSource audioSource;
        private float lastHitSfxTime = -999f;

        public bool SfxEnabled => sfxEnabled;

        private void Awake()
        {
            Instance = this;

            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f; // UI/게임플레이 피드백용 2D 사운드 - 리스너 거리와 무관하게 항상 들려야 한다

            // 저장된 UI 설정이 있으면 Inspector 시작값 대신 그 값을 쓴다(HudToggleButton과 같은 패턴).
            UiSettingsData saved = UiSettingsSaveSystem.Load();
            if (saved != null)
            {
                sfxEnabled = saved.sfxEnabled;
            }
        }

        private void OnApplicationQuit()
        {
            UiSettingsSaveSystem.SaveSfxEnabled(sfxEnabled);
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

        /// <summary>hitSfxCooldown 안에 들어오는 추가 요청은 무시한다 - 시각/데미지 처리는 그대로 매 타격마다 일어나고, 소리만 압축한다.
        /// overrideClip이 있으면(공격별 Hit Sound) 그 클립만 재생하고 씬 기본 hitClip은 재생하지 않는다 -
        /// 어떤 클립을 재생할지는 여기서만 결정되므로 한 타격에 두 소리가 겹칠 일이 없다.</summary>
        public void RequestHitSfx(AudioClip overrideClip = null)
        {
            if (Time.time - lastHitSfxTime < hitSfxCooldown) return;
            lastHitSfxTime = Time.time;
            PlayOneShot(overrideClip != null ? overrideClip : hitClip);
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
            if (!sfxEnabled || sfxVolume <= 0f || clip == null) return;
            audioSource.PlayOneShot(clip, sfxVolume);
        }

        /// <summary>ControlDock의 SoundToggle이 호출하는 진입점.</summary>
        public void ToggleSfxEnabled()
        {
            SetSfxEnabled(!sfxEnabled);
        }

        public void SetSfxEnabled(bool enabled)
        {
            if (sfxEnabled == enabled) return;
            sfxEnabled = enabled;
            OnSfxEnabledChanged?.Invoke(sfxEnabled);
            UiSettingsSaveSystem.SaveSfxEnabled(sfxEnabled); // 토글 즉시 저장 - 종료 시 저장은 비정상 종료 대비 안전망
        }
    }
}
