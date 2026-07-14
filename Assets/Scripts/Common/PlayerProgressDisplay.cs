using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// PlayerProgress의 레벨/경험치를 HUD ProgressPanel(레벨 텍스트, EXP 바, 퍼센트 텍스트)에 표시한다.
    /// PlayerProgress.OnExperienceChanged/OnLevelUp이 발생했을 때만 갱신한다 - 매 프레임 폴링하지 않는다.
    /// ExpBarFill은 Image.Type = Filled(Horizontal)로 이미 설정돼 있어서 fillAmount만 갱신하면 된다.
    /// </summary>
    public class PlayerProgressDisplay : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private Image expBarFill;
        [SerializeField] private TextMeshProUGUI percentageText;

        [SerializeField] private string levelFormat = "Lv. {0}";

        private void OnEnable()
        {
            PlayerProgress.OnExperienceChanged += Refresh;
            PlayerProgress.OnLevelUp += HandleLevelUp;
            Refresh();
        }

        private void OnDisable()
        {
            PlayerProgress.OnExperienceChanged -= Refresh;
            PlayerProgress.OnLevelUp -= HandleLevelUp;
        }

        private void HandleLevelUp(int newLevel)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (levelText != null)
            {
                levelText.text = string.Format(levelFormat, PlayerProgress.CurrentLevel);
            }

            float ratio = PlayerProgress.ExpToNextLevel <= 0
                ? 0f
                : (float)PlayerProgress.CurrentExp / PlayerProgress.ExpToNextLevel;
            ratio = Mathf.Clamp01(ratio);

            if (expBarFill != null)
            {
                expBarFill.fillAmount = ratio;
            }

            if (percentageText != null)
            {
                percentageText.text = $"{Mathf.RoundToInt(ratio * 100f)}%";
            }
        }
    }
}
