using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// PlayerProgress가 비추는 <b>지금 전투 중인 캐릭터</b>의 레벨/경험치를 HUD ProgressPanel(레벨
    /// 텍스트, EXP 바, 퍼센트 텍스트)에 표시한다. PlayerProgress.OnExperienceChanged가 발생했을 때만
    /// 갱신한다 - 매 프레임 폴링하지 않는다.
    ///
    /// <b>초기 표시·캐릭터 교체와 획득 연출은 분리한다.</b> 저장 데이터 로드(PlayerProgress.Awake)는 이 컴포넌트의
    /// OnEnable보다 늦게 끝날 수 있으므로, PlayerProgress.IsInitialized가 true가 되기 전에는 아무 것도
    /// 그리지 않고 OnProgressInitialized를 기다렸다가 저장된 레벨/비율을 연출 없이 즉시 반영한다
    /// (SyncImmediately). 예전에는 로드 전 정적 기본값(Level 0)으로 표시가 굳어서, 첫 처치 때
    /// 0 -> 저장 레벨까지 레벨업 연출이 쏟아졌다. <b>캐릭터 교체도 같은 즉시 경로를 쓴다</b> -
    /// PlayerProgress.OnCurrentCharacterSynchronized를 받아 진행 중이던 연출을 취소하고 새 캐릭터의
    /// 값으로 바로 맞춘다.
    /// 경험치 바는 Unity Slider를 표시 전용으로 사용한다. Slider는 Fill Image가 Sliced 타입이면
    /// fillAmount로 텍스처를 잘라내지 않고 Fill Rect의 폭을 바꾸므로, 나인슬라이스 양끝 모양을 유지한
    /// 채 진행률을 표현할 수 있다. Inspector의 Slider.Value로 Edit Mode에서도 즉시 미리볼 수 있다.
    /// 경험치가 오르면 Gain Preview Fill은 목표 비율로 즉시 확장되고, Main Fill(Slider)은 잠깐 기다린
    /// 뒤 부드럽게 따라간다. Preview는 Main보다 먼저 그려져 Main이 따라오는 만큼 덮이도록 구성한다.
    /// </summary>
    public class PlayerProgressDisplay : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TextMeshProUGUI levelText;

        [Tooltip("표시 전용 경험치 Slider. Min Value 0, Max Value 1, Whole Numbers Off로 설정하고 Fill Rect에 " +
                 "Sliced 타입의 Fill Image를 연결한다. 비워두면 같은 GameObject에서 자동으로 찾는다.")]
        [SerializeField] private Slider expBarSlider;

        [Tooltip("획득할 경험치 위치를 즉시 보여주는 연출용 Fill의 RectTransform. Main Fill과 같은 Fill Area " +
                 "아래에 두고, Main Fill보다 먼저 그려지도록 Hierarchy에서 앞쪽 sibling으로 배치한다. " +
                 "Image Type은 Sliced를 사용한다. 비워두면 Main Fill만 부드럽게 이동한다.")]
        [SerializeField] private RectTransform expBarGainPreviewFill;

        [SerializeField] private TextMeshProUGUI percentageText;

        [SerializeField] private string levelFormat = "Lv. {0}";

        [Header("EXP Gain Animation")]
        [Tooltip("Preview Fill이 먼저 늘어난 뒤 Main Fill이 움직이기 시작하기까지의 대기 시간.")]
        [Min(0f)]
        [SerializeField] private float mainFillDelay = 0.1f;

        [Tooltip("Main Fill이 현재 위치에서 목표 위치까지 따라가는 시간.")]
        [Min(0f)]
        [SerializeField] private float mainFillDuration = 0.35f;

        [Tooltip("레벨업 때 Main Fill이 100%에 도달한 모습을 유지하는 시간. 이후 0%로 리셋한다.")]
        [Min(0f)]
        [SerializeField] private float levelUpFullHold = 0.1f;

        private Coroutine fillAnimationRoutine;
        private bool presentationInitialized;
        private int presentedLevel;
        private bool levelUpTransitionActive;
        private int queuedLevelUps;
        private float latestTargetRatio;

        private void OnEnable()
        {
            ResolveSlider();
            PlayerProgress.OnExperienceChanged += Refresh;
            PlayerProgress.OnProgressInitialized += SyncImmediately;

            // 캐릭터가 바뀌면 보고 있던 값이 통째로 다른 캐릭터의 것이 된다 - 획득 경로(Refresh)가
            // 아니라 즉시 동기화 경로로 받아야 레벨 차이만큼 레벨업 연출이 쏟아지지 않는다.
            PlayerProgress.OnCurrentCharacterSynchronized += SyncImmediately;

            // PlayerProgress.Awake보다 이 OnEnable이 먼저 돌 수 있다 - 그때는 아직 저장 데이터가
            // 로드되지 않았으므로(Level 0) 아무 것도 그리지 않고 초기화 신호를 기다린다. 이미 로드가
            // 끝난 뒤라면(순서가 반대였거나 재활성화) 곧바로 현재 값으로 맞춘다.
            if (PlayerProgress.IsInitialized) SyncImmediately();
        }

        private void OnDisable()
        {
            PlayerProgress.OnExperienceChanged -= Refresh;
            PlayerProgress.OnProgressInitialized -= SyncImmediately;
            PlayerProgress.OnCurrentCharacterSynchronized -= SyncImmediately;

            if (fillAnimationRoutine != null)
            {
                StopCoroutine(fillAnimationRoutine);
                fillAnimationRoutine = null;
            }
            presentationInitialized = false;
            levelUpTransitionActive = false;
            queuedLevelUps = 0;
        }

        /// <summary>지금 값을 <b>연출 없이</b> 그대로 표시에 반영한다. 진행 중이던 연출은 취소한다.
        ///
        /// 이 경로를 타는 것은 두 가지다 - <b>초기 동기화</b>(저장 로드 직후)와 <b>캐릭터 교체</b>.
        /// 둘 다 경험치를 "획득한" 것이 아니므로 AnimateLevelUpQueue/AnimateNormalGain을 절대 타지
        /// 않는다. 저장 레벨이 29면 첫 프레임부터 Lv. 29와 그 비율이 보이고 0에서 29까지 올라가는
        /// 연출은 없으며, Lv.3에서 Lv.12 캐릭터로 갈아타도 레벨업 연출이 아홉 번 쏟아지지 않는다.
        /// 실제 획득부터는 Refresh가 평소대로 획득 연출을 재생한다.</summary>
        private void SyncImmediately()
        {
            if (fillAnimationRoutine != null)
            {
                StopCoroutine(fillAnimationRoutine);
                fillAnimationRoutine = null;
            }
            levelUpTransitionActive = false;
            queuedLevelUps = 0;
            presentationInitialized = false; // 아래 Refresh가 "즉시 동기화" 경로를 타게 한다.
            Refresh();
        }

        private void Refresh()
        {
            // 저장 데이터 로드 전에는 표시를 시작하지 않는다 - 여기서 그리면 정적 기본값(Level 0)이
            // 그대로 굳어서, 첫 처치 때 0 -> 29 레벨업 연출이 쏟아진다.
            if (!PlayerProgress.IsInitialized) return;

            if (levelText != null)
            {
                levelText.text = string.Format(levelFormat, PlayerProgress.CurrentLevel);
            }

            float ratio = PlayerProgress.ExpToNextLevel <= 0
                ? 0f
                : (float)PlayerProgress.CurrentExp / PlayerProgress.ExpToNextLevel;
            ratio = Mathf.Clamp01(ratio);
            latestTargetRatio = ratio;

            if (!presentationInitialized)
            {
                // 초기 동기화: presentedLevel과 두 Fill을 현재 저장값으로 즉시 맞춘다(연출 없음).
                SetMainFill(ratio);
                SetGainPreviewFill(ratio);
                presentedLevel = PlayerProgress.CurrentLevel;
                presentationInitialized = true;
            }
            else
            {
                int levelsGained = Mathf.Max(0, PlayerProgress.CurrentLevel - presentedLevel);
                presentedLevel = PlayerProgress.CurrentLevel;

                if (levelUpTransitionActive)
                {
                    // 빠른 연타로 레벨업 연출 도중 다음 경험치가 들어와도 100% -> 0% 전환은
                    // 취소하지 않는다. 최신 목표치만 갱신해 전환 직후 계속 따라가게 한다.
                    queuedLevelUps += levelsGained;
                    SetGainPreviewFill(queuedLevelUps > 0 ? 1f : latestTargetRatio);
                }
                else
                {
                    if (fillAnimationRoutine != null)
                    {
                        StopCoroutine(fillAnimationRoutine);
                    }

                    if (levelsGained > 0)
                    {
                        queuedLevelUps = levelsGained;
                        fillAnimationRoutine = StartCoroutine(AnimateLevelUpQueue());
                    }
                    else
                    {
                        fillAnimationRoutine = StartCoroutine(AnimateNormalGain(latestTargetRatio));
                    }
                }
            }

            if (percentageText != null)
            {
                percentageText.text = $"{Mathf.RoundToInt(ratio * 100f)}%";
            }
        }

        private IEnumerator AnimateNormalGain(float targetRatio)
        {
            SetGainPreviewFill(targetRatio);
            yield return WaitUnscaled(mainFillDelay);
            yield return AnimateMainFillTo(targetRatio);

            SetMainFill(targetRatio);
            SetGainPreviewFill(targetRatio);
            fillAnimationRoutine = null;
        }

        private IEnumerator AnimateLevelUpQueue()
        {
            levelUpTransitionActive = true;

            while (true)
            {
                while (queuedLevelUps > 0)
                {
                    // PlayerProgress는 레벨업 후 남은 경험치만 공개하므로 시각적으로는 이전 바를
                    // 먼저 100%까지 채운 뒤 0%로 리셋한다.
                    SetGainPreviewFill(1f);
                    yield return WaitUnscaled(mainFillDelay);
                    yield return AnimateMainFillTo(1f);
                    yield return WaitUnscaled(levelUpFullHold);

                    SetMainFill(0f);
                    queuedLevelUps--;
                }

                SetGainPreviewFill(latestTargetRatio);
                yield return WaitUnscaled(mainFillDelay);

                if (queuedLevelUps > 0) continue;

                float targetSnapshot = latestTargetRatio;
                yield return AnimateMainFillTo(targetSnapshot, true);

                if (queuedLevelUps > 0 || !Mathf.Approximately(targetSnapshot, latestTargetRatio))
                {
                    continue;
                }

                SetMainFill(latestTargetRatio);
                SetGainPreviewFill(latestTargetRatio);
                break;
            }

            levelUpTransitionActive = false;
            fillAnimationRoutine = null;
        }

        private IEnumerator AnimateMainFillTo(float targetRatio, bool abortForQueuedLevelUp = false)
        {
            if (expBarSlider == null) yield break;

            float startRatio = expBarSlider.normalizedValue;
            float duration = Mathf.Max(0f, mainFillDuration);
            if (duration <= 0f)
            {
                SetMainFill(targetRatio);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (abortForQueuedLevelUp && queuedLevelUps > 0) yield break;

                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                SetMainFill(Mathf.LerpUnclamped(startRatio, targetRatio, t));
                yield return null;
            }
            SetMainFill(targetRatio);
        }

        private static IEnumerator WaitUnscaled(float duration)
        {
            float wait = Mathf.Max(0f, duration);
            float elapsed = 0f;
            while (elapsed < wait)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void SetMainFill(float ratio)
        {
            if (expBarSlider == null) return;
            expBarSlider.SetValueWithoutNotify(Mathf.Clamp01(ratio));
        }

        private void SetGainPreviewFill(float ratio)
        {
            if (expBarGainPreviewFill == null) return;

            Vector2 anchorMax = expBarGainPreviewFill.anchorMax;
            anchorMax.x = Mathf.Clamp01(ratio);
            expBarGainPreviewFill.anchorMax = anchorMax;
        }

        /// <summary>PlayerProgressDisplay와 Slider를 같은 expProgress 오브젝트에 두는 권장 구성이면
        /// 별도 Inspector 연결 없이도 자동으로 찾는다.</summary>
        private void ResolveSlider()
        {
            if (expBarSlider != null) return;
            expBarSlider = GetComponent<Slider>();
            if (expBarSlider != null) return;

            Debug.LogWarning("[PlayerProgressDisplay] Exp Bar Slider가 연결되지 않았고 같은 GameObject에도 " +
                             "Slider가 없습니다. 경험치 수치는 갱신되지만 Fill 시각은 변경되지 않습니다.", this);
        }
    }
}
