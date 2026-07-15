using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// ControlDock의 SoundToggle 버튼. 클릭하면 AudioManager.sfxEnabled를 반전시키고,
    /// onTargetGraphic/offTargetGraphic 중 상태에 맞는 쪽만 활성화해서 표시한다. 각 이미지의
    /// 스프라이트/색은 이 스크립트가 관여하지 않고 에디터에서 미리 설정해둔 값을 그대로 쓴다
    /// (버튼 기본 Image는 스프라이트를 비워서 안 보이게 처리한다 - 에디터에서 직접 관리).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class SoundToggleButton : MonoBehaviour
    {
        [Header("Target Graphic")]
        [Tooltip("On 상태일 때 보여줄 이미지(스프라이트/색은 이 오브젝트에 직접 설정).")]
        [SerializeField] private Image onTargetGraphic;
        [Tooltip("Off 상태일 때 보여줄 이미지(스프라이트/색은 이 오브젝트에 직접 설정).")]
        [SerializeField] private Image offTargetGraphic;

        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            button.onClick.AddListener(HandleClick);
            AudioManager.OnSfxEnabledChanged += Refresh;
            Refresh(AudioManager.Instance != null && AudioManager.Instance.SfxEnabled);
        }

        private void OnDisable()
        {
            button.onClick.RemoveListener(HandleClick);
            AudioManager.OnSfxEnabledChanged -= Refresh;
        }

        private void HandleClick()
        {
            if (AudioManager.Instance == null) return;
            AudioManager.Instance.ToggleSfxEnabled();
        }

        private void Refresh(bool enabled)
        {
            ToggleButtonVisual.Apply(enabled, onTargetGraphic, offTargetGraphic);
        }
    }
}
