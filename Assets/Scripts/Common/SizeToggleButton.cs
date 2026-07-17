using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// ControlDock의 tgl_size 버튼. 클릭할 때마다 sizePercentages를 순환하며 StageVisualRoot(캐릭터/적
    /// 등 게임 그래픽)의 화면상 크기를 바꾼다. 전체 모니터 Overlay 구조로 전환되기 전에는 네이티브
    /// 창 크기 자체를 바꿨지만, 지금은 StageVisualRootController.SetUserScale이 카메라 Viewport Rect만
    /// 조정한다 - 네이티브 창(모니터 Work Area 전체)은 이 배율과 무관하게 그대로 유지된다. 이 버튼은
    /// "다음 배율이 뭔지 정하고 요청만 보내는" 역할만 한다.
    ///
    /// sizePercentages는 Inspector에서 자유롭게 조정 가능하다 - 비율 구성이 나중에 바뀔 수 있다는
    /// 전제로 하드코딩하지 않았다. 저장은 배열의 인덱스가 아니라 실제 배율값(예: 1.5)으로 하므로,
    /// 나중에 배열 구성이 바뀌어도 저장된 값은 여전히 유효하다(가장 가까운 단계를 다시 찾아 이어간다).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class SizeToggleButton : MonoBehaviour
    {
        [Tooltip("순환할 배율 목록(1 = 100%). 순서대로 순환하며, 값 구성은 나중에 바뀔 수 있다.")]
        [SerializeField] private float[] sizePercentages = { 0.5f, 1f, 1.5f };

        [Header("Target Graphic (선택 - 배율 단계 수만큼, 순서는 sizePercentages와 대응)")]
        [Tooltip("비워둬도 된다. 채우면 현재 단계에 해당하는 것만 활성화된다.")]
        [SerializeField] private Image[] stepTargetGraphics;

        [Header("Label (선택)")]
        [Tooltip("현재 배율을 텍스트로 보여준다. 비워두면 표시하지 않는다.")]
        [SerializeField] private TextMeshProUGUI label;

        private Button button;
        private int currentIndex;

        private void Awake()
        {
            button = GetComponent<Button>();

            UiSettingsData saved = UiSettingsSaveSystem.Load();
            float initialScale = saved != null ? saved.sizeScale : 1f;
            currentIndex = FindClosestIndex(initialScale);

            ApplyCurrentScale(save: false);
        }

        private void OnEnable()
        {
            button.onClick.AddListener(HandleClick);
            Refresh();
        }

        private void OnDisable()
        {
            button.onClick.RemoveListener(HandleClick);
        }

        private void HandleClick()
        {
            if (sizePercentages == null || sizePercentages.Length == 0) return;

            currentIndex = (currentIndex + 1) % sizePercentages.Length;
            ApplyCurrentScale(save: true);
            Refresh();
        }

        private void ApplyCurrentScale(bool save)
        {
            if (sizePercentages == null || sizePercentages.Length == 0) return;

            float scale = sizePercentages[currentIndex];

            if (StageVisualRootController.Instance != null)
            {
                StageVisualRootController.Instance.SetUserScale(scale);
            }

            if (save)
            {
                UiSettingsSaveSystem.SaveSizeScale(scale); // 토글 즉시 저장 - 종료 시 저장은 별도로 두지 않는다(변경이 곧 확정이라 유예할 이유가 없음)
            }
        }

        private int FindClosestIndex(float scale)
        {
            if (sizePercentages == null || sizePercentages.Length == 0) return 0;

            int best = 0;
            float bestDiff = Mathf.Abs(sizePercentages[0] - scale);
            for (int i = 1; i < sizePercentages.Length; i++)
            {
                float diff = Mathf.Abs(sizePercentages[i] - scale);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = i;
                }
            }
            return best;
        }

        private void Refresh()
        {
            if (stepTargetGraphics != null)
            {
                for (int i = 0; i < stepTargetGraphics.Length; i++)
                {
                    if (stepTargetGraphics[i] != null)
                    {
                        stepTargetGraphics[i].gameObject.SetActive(i == currentIndex);
                    }
                }
            }

            if (label != null && sizePercentages != null && sizePercentages.Length > 0)
            {
                label.text = $"{Mathf.RoundToInt(sizePercentages[currentIndex] * 100f)}%";
            }
        }
    }
}
