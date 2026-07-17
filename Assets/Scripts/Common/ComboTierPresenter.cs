using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// ComboManager.OnComboChanged/OnComboBroken을 구독해 콤보 숫자 텍스트와 티어 이름 텍스트의
    /// 색상/스케일 연출을 담당한다. ComboManager 자신은 콤보 계산과 자신만의 타임아웃용 tier(1~3)만
    /// 알 뿐 UI를 직접 건드리지 않는다 - 이 컴포넌트가 같은 콤보 수치를 받아 별도의 표현용 티어
    /// 목록(tiers)으로 다시 판정한다.
    ///
    /// 같은 티어 안에서의 증가는 짧은 "일반 펀치" 연출, 새 티어 진입은 더 크고 긴 "티어 전환" 연출을
    /// 재생한다. 두 연출 모두 텍스트별로 하나씩만 존재하는 스케일 코루틴(comboScaleRoutine/
    /// tierScaleRoutine)을 통해서만 해당 Transform의 localScale을 건드리므로 동시에 두 연출이 같은
    /// Transform을 겹쳐 제어하는 일이 없다. 새 연출이 들어오면 진행 중이던 연출을 멈추고 그 순간의
    /// 실제 화면 스케일을 시작값으로 삼아 새 목표까지 이어간다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ComboTierPresenter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TextMeshProUGUI comboText;
        [SerializeField] private TextMeshProUGUI tierText;

        [Header("Label")]
        [SerializeField] private string comboLabel = "COMBO";

        [Header("Tiers (minCombo 오름차순 - 자동 정렬됨)")]
        [SerializeField]
        private List<ComboTierData> tiers = new List<ComboTierData>
        {
            new ComboTierData { minCombo = 0, tierName = "Normal", textColor = Color.white, comboScale = 1f, transitionScale = 1.2f, transitionDuration = 0.25f },
            new ComboTierData { minCombo = 10, tierName = "Good", textColor = new Color(0.6f, 1f, 0.6f), comboScale = 1f, transitionScale = 1.3f, transitionDuration = 0.3f },
            new ComboTierData { minCombo = 25, tierName = "Great", textColor = new Color(1f, 0.85f, 0.2f), comboScale = 1.05f, transitionScale = 1.3f, transitionDuration = 0.3f },
            new ComboTierData { minCombo = 50, tierName = "Excellent", textColor = new Color(1f, 0.3f, 0.2f), comboScale = 1.1f, transitionScale = 1.35f, transitionDuration = 0.35f },
        };

        [Header("일반 증가 연출 (공통)")]
        [SerializeField] private float normalPunchScale = 1.12f;
        [SerializeField] private float normalPunchDuration = 0.12f;

        [Header("색상 전환")]
        [Tooltip("0이면 티어 진입 즉시 색이 바뀐다. 0보다 크면 그 시간(초) 동안 보간한다.")]
        [SerializeField] private float colorTransitionDuration = 0f;

        private Vector3 baseComboScale = Vector3.one;
        private Vector3 baseTierScale = Vector3.one;
        private float baseComboAlpha = 1f;
        private float baseTierAlpha = 1f;

        private Coroutine comboScaleRoutine;
        private Coroutine tierScaleRoutine;
        private Coroutine comboColorRoutine;
        private Coroutine tierColorRoutine;

        private ComboTierData fallbackTier;
        private int currentTierIndex = -1;
        // currentTierIndex == -1은 "아직 리졸브된 티어가 없다"는 뜻일 뿐, 그 자체로 "티어가
        // 바뀌었다"는 신호가 아니다 - 그 판단은 이 플래그로만 한다. false인 상태에서 들어오는 첫
        // UpdateForCombo는 항상 "기준선 확립"으로 취급해 강한 티어 전환 연출 없이 일반 펀치만
        // 재생한다(리셋 직후 0→1 콤보가 Normal로의 "전환"으로 오판되는 것을 막는다).
        private bool tierEstablished;

        private bool warnedEmptyTiers;
        private bool warnedDuplicateMinCombo;
        private bool warnedMissingReferences;

        private void Awake()
        {
            if (comboText != null)
            {
                baseComboScale = comboText.rectTransform.localScale;
                baseComboAlpha = comboText.color.a;
            }
            if (tierText != null)
            {
                baseTierScale = tierText.rectTransform.localScale;
                baseTierAlpha = tierText.color.a;
            }

            fallbackTier = new ComboTierData
            {
                minCombo = 0,
                tierName = "Normal",
                textColor = comboText != null ? comboText.color : Color.white,
                comboScale = 1f,
                transitionScale = 1f,
                transitionDuration = 0f,
            };

            SortTiers();
            WarnIfInvalid();
        }

        private void OnEnable()
        {
            ComboManager.OnComboChanged += HandleComboChanged;
            ComboManager.OnComboBroken += HandleComboBroken;

            SyncImmediate(ComboManager.CurrentCombo);
        }

        private void OnDisable()
        {
            ComboManager.OnComboChanged -= HandleComboChanged;
            ComboManager.OnComboBroken -= HandleComboBroken;

            StopTrackedRoutine(ref comboScaleRoutine);
            StopTrackedRoutine(ref tierScaleRoutine);
            StopTrackedRoutine(ref comboColorRoutine);
            StopTrackedRoutine(ref tierColorRoutine);
        }

        private void OnValidate()
        {
            SortTiers();
            WarnIfInvalid();
        }

        private void HandleComboChanged(int combo)
        {
            if (combo <= 0)
            {
                HandleComboEnded();
                return;
            }
            UpdateForCombo(combo);
        }

        private void HandleComboBroken(int finalCombo)
        {
            // OnComboChanged(0)에서 이미 처리되는 경우가 대부분이지만, "종료" 신호를 명시적으로도
            // 받아 안전하게 한 번 더 리셋한다(ApplyResetState는 여러 번 호출돼도 안전하다).
            HandleComboEnded();
        }

        private void HandleComboEnded()
        {
            ApplyResetState();
            SetVisible(false);
        }

        /// <summary>실제 게임플레이 콤보 증가에 반응한다 - 애니메이션(펀치 또는 전환)을 재생한다.</summary>
        private void UpdateForCombo(int combo)
        {
            int newTierIndex = ResolveTierIndex(combo);
            ComboTierData tier = GetTier(newTierIndex);

            // 리셋 이후 첫 판정(tierEstablished == false)은 "티어 전환"이 아니라 기준선 확립이다.
            // 예: 콤보 종료 후 0→1은 항상 Normal로 조용히 자리잡고 일반 펀치만 재생해야 한다 -
            // 9→10, 24→25처럼 실제로 currentTierIndex가 바뀌는 경우에만 강한 전환 연출을 튼다.
            bool isFirstSinceReset = !tierEstablished;
            bool tierChanged = tierEstablished && newTierIndex != currentTierIndex;
            currentTierIndex = newTierIndex;
            tierEstablished = true;

            if (comboText != null) comboText.text = $"{comboLabel} {combo}";
            if (tierText != null) tierText.text = tier.tierName;

            SetVisible(true);

            if (isFirstSinceReset)
            {
                ApplyTierColorInstant(tier);
                PlayNormalPunch(tier);
            }
            else if (tierChanged)
            {
                ApplyTierColor(tier);
                PlayTransition(tier);
            }
            else
            {
                PlayNormalPunch(tier);
            }
        }

        /// <summary>OnEnable 등 재활성화 시점에 애니메이션 없이 현재 콤보 상태로 즉시 맞춘다(예: HUD
        /// 토글로 껐다 켰을 때 불필요한 펀치 연출이 다시 재생되지 않게 한다).</summary>
        private void SyncImmediate(int combo)
        {
            StopTrackedRoutine(ref comboScaleRoutine);
            StopTrackedRoutine(ref tierScaleRoutine);
            StopTrackedRoutine(ref comboColorRoutine);
            StopTrackedRoutine(ref tierColorRoutine);

            if (combo <= 0)
            {
                ApplyResetState();
                SetVisible(false);
                return;
            }

            int tierIndex = ResolveTierIndex(combo);
            ComboTierData tier = GetTier(tierIndex);
            currentTierIndex = tierIndex;
            tierEstablished = true;

            if (comboText != null)
            {
                comboText.text = $"{comboLabel} {combo}";
                comboText.rectTransform.localScale = RestScale(baseComboScale, tier);
                SetColorPreserveAlpha(comboText, tier.textColor, baseComboAlpha);
            }
            if (tierText != null)
            {
                tierText.text = tier.tierName;
                tierText.rectTransform.localScale = RestScale(baseTierScale, tier);
                SetColorPreserveAlpha(tierText, tier.textColor, baseTierAlpha);
            }
            SetVisible(true);
        }

        /// <summary>콤보 종료/초기화 - 티어 인덱스, 텍스트, 색상, 스케일, 진행 중인 연출을 모두
        /// 기본값으로 되돌린다. 여러 번 호출돼도 안전하다.</summary>
        private void ApplyResetState()
        {
            StopTrackedRoutine(ref comboScaleRoutine);
            StopTrackedRoutine(ref tierScaleRoutine);
            StopTrackedRoutine(ref comboColorRoutine);
            StopTrackedRoutine(ref tierColorRoutine);

            currentTierIndex = -1;
            tierEstablished = false;
            ComboTierData normalTier = GetTier(ResolveTierIndex(0));

            if (comboText != null)
            {
                comboText.text = $"{comboLabel} 0";
                comboText.rectTransform.localScale = baseComboScale;
                SetColorPreserveAlpha(comboText, normalTier.textColor, baseComboAlpha);
            }
            if (tierText != null)
            {
                tierText.text = normalTier.tierName;
                tierText.rectTransform.localScale = baseTierScale;
                SetColorPreserveAlpha(tierText, normalTier.textColor, baseTierAlpha);
            }
        }

        private void SetVisible(bool visible)
        {
            if (comboText != null) comboText.enabled = visible;
            if (tierText != null) tierText.enabled = visible;
        }

        private Vector3 RestScale(Vector3 baseScale, ComboTierData tier)
        {
            return baseScale * tier.comboScale;
        }

        private void PlayNormalPunch(ComboTierData tier)
        {
            if (comboText != null)
            {
                Vector3 rest = RestScale(baseComboScale, tier);
                StopTrackedRoutine(ref comboScaleRoutine);
                comboScaleRoutine = StartCoroutine(PunchRoutine(comboText.rectTransform, rest * normalPunchScale, rest, normalPunchDuration, DoneComboScale));
            }
            if (tierText != null)
            {
                Vector3 rest = RestScale(baseTierScale, tier);
                StopTrackedRoutine(ref tierScaleRoutine);
                tierScaleRoutine = StartCoroutine(PunchRoutine(tierText.rectTransform, rest * normalPunchScale, rest, normalPunchDuration, DoneTierScale));
            }
        }

        private void PlayTransition(ComboTierData tier)
        {
            if (comboText != null)
            {
                Vector3 rest = RestScale(baseComboScale, tier);
                StopTrackedRoutine(ref comboScaleRoutine);
                comboScaleRoutine = StartCoroutine(PunchRoutine(comboText.rectTransform, rest * tier.transitionScale, rest, tier.transitionDuration, DoneComboScale));
            }
            if (tierText != null)
            {
                Vector3 rest = RestScale(baseTierScale, tier);
                StopTrackedRoutine(ref tierScaleRoutine);
                tierScaleRoutine = StartCoroutine(PunchRoutine(tierText.rectTransform, rest * tier.transitionScale, rest, tier.transitionDuration, DoneTierScale));
            }
        }

        private void ApplyTierColor(ComboTierData tier)
        {
            if (comboText != null)
            {
                StopTrackedRoutine(ref comboColorRoutine);
                Color target = tier.textColor;
                target.a = baseComboAlpha;
                if (colorTransitionDuration <= 0f)
                {
                    comboText.color = target;
                }
                else
                {
                    comboColorRoutine = StartCoroutine(ColorRoutine(comboText, target, colorTransitionDuration, DoneComboColor));
                }
            }
            if (tierText != null)
            {
                StopTrackedRoutine(ref tierColorRoutine);
                Color target = tier.textColor;
                target.a = baseTierAlpha;
                if (colorTransitionDuration <= 0f)
                {
                    tierText.color = target;
                }
                else
                {
                    tierColorRoutine = StartCoroutine(ColorRoutine(tierText, target, colorTransitionDuration, DoneTierColor));
                }
            }
        }

        /// <summary>리셋 이후 첫 판정에서 쓰는 즉시 색상 적용 - colorTransitionDuration을 무시하고
        /// 바로 맞춘다(SyncImmediate와 동일한 성격의 "조용한 동기화"이지 애니메이션 대상이 아니다).</summary>
        private void ApplyTierColorInstant(ComboTierData tier)
        {
            StopTrackedRoutine(ref comboColorRoutine);
            StopTrackedRoutine(ref tierColorRoutine);
            if (comboText != null) SetColorPreserveAlpha(comboText, tier.textColor, baseComboAlpha);
            if (tierText != null) SetColorPreserveAlpha(tierText, tier.textColor, baseTierAlpha);
        }

        private void DoneComboScale() => comboScaleRoutine = null;
        private void DoneTierScale() => tierScaleRoutine = null;
        private void DoneComboColor() => comboColorRoutine = null;
        private void DoneTierColor() => tierColorRoutine = null;

        private void SetColorPreserveAlpha(TextMeshProUGUI text, Color color, float alpha)
        {
            Color c = color;
            c.a = alpha;
            text.color = c;
        }

        /// <summary>기준값 → peak → rest 순으로 재생하는 단일 스케일 펀치. 일반 증가/티어 전환 모두
        /// 이 코루틴을 공유하며, 시작값은 항상 호출 시점의 실제 localScale이다(저장해둔 목표값이
        /// 아니다) - 그래야 진행 중인 연출을 중단하고 다시 시작해도 순간이동 없이 자연스럽게 이어진다.</summary>
        private IEnumerator PunchRoutine(RectTransform rect, Vector3 peak, Vector3 rest, float duration, Action onComplete)
        {
            if (duration <= 0f)
            {
                rect.localScale = rest;
                onComplete?.Invoke();
                yield break;
            }

            Vector3 start = rect.localScale;
            float upDuration = duration * 0.4f;
            float downDuration = duration - upDuration;

            float elapsed = 0f;
            while (elapsed < upDuration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / upDuration));
                rect.localScale = Vector3.LerpUnclamped(start, peak, u);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < downDuration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / downDuration));
                rect.localScale = Vector3.LerpUnclamped(peak, rest, u);
                yield return null;
            }

            rect.localScale = rest;
            onComplete?.Invoke();
        }

        private IEnumerator ColorRoutine(TextMeshProUGUI text, Color target, float duration, Action onComplete)
        {
            Color start = text.color;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                text.color = Color.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            text.color = target;
            onComplete?.Invoke();
        }

        private void StopTrackedRoutine(ref Coroutine routine)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
        }

        /// <summary>현재 콤보 이하에서 minCombo가 가장 큰 티어의 인덱스를 돌려준다. tiers가 정렬돼
        /// 있지 않아도 정확하게 동작한다(동률이면 나중에 검사된 쪽이 우선). 조건을 만족하는 티어가
        /// 하나도 없으면 -1.</summary>
        private int ResolveTierIndex(int combo)
        {
            int best = -1;
            for (int i = 0; i < tiers.Count; i++)
            {
                ComboTierData t = tiers[i];
                if (t == null) continue;
                if (combo < t.minCombo) continue;
                if (best == -1 || t.minCombo >= tiers[best].minCombo)
                {
                    best = i;
                }
            }
            return best;
        }

        private ComboTierData GetTier(int index)
        {
            if (tiers != null && index >= 0 && index < tiers.Count && tiers[index] != null)
            {
                return tiers[index];
            }
            return fallbackTier;
        }

        private void SortTiers()
        {
            if (tiers == null) return;
            tiers.Sort((a, b) => (a?.minCombo ?? int.MaxValue).CompareTo(b?.minCombo ?? int.MaxValue));
        }

        private void WarnIfInvalid()
        {
            if (tiers == null || tiers.Count == 0)
            {
                if (!warnedEmptyTiers)
                {
                    Debug.LogWarning("[ComboTierPresenter] tiers가 비어 있습니다. 기본 표현으로만 동작합니다.", this);
                    warnedEmptyTiers = true;
                }
            }
            else
            {
                warnedEmptyTiers = false;

                bool hasDuplicate = false;
                for (int i = 1; i < tiers.Count; i++)
                {
                    if (tiers[i] != null && tiers[i - 1] != null && tiers[i].minCombo == tiers[i - 1].minCombo)
                    {
                        hasDuplicate = true;
                        break;
                    }
                }

                if (hasDuplicate)
                {
                    if (!warnedDuplicateMinCombo)
                    {
                        Debug.LogWarning("[ComboTierPresenter] tiers에 동일한 minCombo 값이 중복됩니다. 정렬 후 더 나중 항목이 우선합니다.", this);
                        warnedDuplicateMinCombo = true;
                    }
                }
                else
                {
                    warnedDuplicateMinCombo = false;
                }
            }

            if (comboText == null && tierText == null)
            {
                if (!warnedMissingReferences)
                {
                    Debug.LogWarning("[ComboTierPresenter] comboText/tierText가 모두 비어 있습니다.", this);
                    warnedMissingReferences = true;
                }
            }
            else
            {
                warnedMissingReferences = false;
            }
        }
    }
}
