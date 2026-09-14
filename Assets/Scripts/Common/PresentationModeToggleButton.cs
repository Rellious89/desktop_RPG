using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>AlwaysOn의 두 임시 버튼을 현재 표시 모드에 맞춰 교대로 보여준다.</summary>
    [DisallowMultipleComponent]
    public sealed class PresentationModeToggleButton : MonoBehaviour
    {
        [SerializeField] private PresentationModeController controller;
        [SerializeField] private Button gameModeButton;
        [SerializeField] private Button companionModeButton;

        private void OnEnable()
        {
            if (gameModeButton != null) gameModeButton.onClick.AddListener(HandleToggle);
            if (companionModeButton != null) companionModeButton.onClick.AddListener(HandleToggle);

            if (controller != null)
            {
                controller.ModeChanged -= HandleModeChanged;
                controller.ModeChanged += HandleModeChanged;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (gameModeButton != null) gameModeButton.onClick.RemoveListener(HandleToggle);
            if (companionModeButton != null) companionModeButton.onClick.RemoveListener(HandleToggle);
            if (controller != null) controller.ModeChanged -= HandleModeChanged;
        }

        private void HandleToggle()
        {
            controller?.ToggleMode();
        }

        private void HandleModeChanged(PresentationMode ignoredMode)
        {
            Refresh();
        }

        private void Refresh()
        {
            bool companion = controller != null && controller.IsCompanionMode;
            if (gameModeButton != null) gameModeButton.gameObject.SetActive(!companion);
            if (companionModeButton != null) companionModeButton.gameObject.SetActive(companion);
        }
    }
}
