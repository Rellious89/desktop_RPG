using TMPro;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// ComboManager의 콤보 수치를 "COMBO N" 형태로 표시한다. 콤보가 1 이상일 때만 보이고
    /// 0이 되면 숨긴다(text.enabled만 끄고 GameObject 자체는 비활성화하지 않는다 - 그래야
    /// 이 스크립트의 이벤트 구독이 끊기지 않는다). 콤보 티어에 따라 색상만 가볍게 바꾼다.
    /// 위치/글자 크기는 lb_Combo의 RectTransform과 TMP 컴포넌트 자체 설정을 그대로 따른다 -
    /// 표기 방식이 나중에 바뀌어도(데미지 숫자처럼) 이 스크립트는 text/색상만 갱신하므로 영향이 적다.
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class ComboDisplay : MonoBehaviour
    {
        [SerializeField] private string label = "COMBO";

        [Header("Tier Colors (인덱스 = 티어, 0=None)")]
        [SerializeField] private Color[] tierColors =
        {
            Color.white, // 0: None
            Color.white, // 1: Normal
            new Color(1f, 0.85f, 0.2f), // 2: Boost
            new Color(1f, 0.3f, 0.2f), // 3: Fever
        };

        private TextMeshProUGUI text;

        private void Awake()
        {
            text = GetComponent<TextMeshProUGUI>();
        }

        private void OnEnable()
        {
            ComboManager.OnComboChanged += HandleComboChanged;
            ComboManager.OnComboTierChanged += HandleTierChanged;

            HandleTierChanged(ComboManager.CurrentTier);
            Refresh();
        }

        private void OnDisable()
        {
            ComboManager.OnComboChanged -= HandleComboChanged;
            ComboManager.OnComboTierChanged -= HandleTierChanged;
        }

        private void HandleComboChanged(int combo)
        {
            Refresh();
        }

        private void HandleTierChanged(int tier)
        {
            if (tierColors.Length == 0) return;
            text.color = tierColors[Mathf.Clamp(tier, 0, tierColors.Length - 1)];
        }

        private void Refresh()
        {
            int combo = ComboManager.CurrentCombo;
            bool visible = combo >= 1;

            text.enabled = visible;
            if (visible)
            {
                text.text = $"{label} {combo}";
            }
        }
    }
}
