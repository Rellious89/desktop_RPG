using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// ControlDock의 HudToggle 버튼. GameHUD/ToastLayer GameObject를 SetActive로 껐다 켠다.
    /// ControlDock 자신과 캐릭터/허수아비는 이 스크립트가 건드리는 대상에 포함되지 않는다.
    /// 상태는 UiSettingsSaveSystem에 저장되어 다음 실행 때도 유지된다(시작 시 Awake에서 즉시 적용).
    ///
    /// shownTargetGraphic/hiddenTargetGraphic 중 상태에 맞는 쪽만 활성화해서 표시한다. 각 이미지의
    /// 스프라이트/색은 이 스크립트가 관여하지 않고 에디터에서 미리 설정해둔 값을 그대로 쓴다
    /// (버튼 기본 Image는 스프라이트를 비워서 안 보이게 처리한다 - 에디터에서 직접 관리).
    ///
    /// 주의: GameHUD 하위에 PlayerProgress나 SessionKillCounter처럼 "표시"가 아니라 "로직"을 담당하는
    /// 컴포넌트를 같이 두면, HUD를 숨기는 순간 그 컴포넌트의 OnDisable도 함께 호출되어 경험치/킬카운트
    /// 집계가 멈춘다. 그런 로직 컴포넌트는 GameHUD 밖(예: DesktopStage)에 둬야 한다. 저장된 상태가
    /// hudVisible=false이면 앱을 켜자마자 HUD가 숨겨진 채로 시작될 수 있으니 특히 주의.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class HudToggleButton : MonoBehaviour
    {
        [SerializeField] private GameObject gameHud;
        [SerializeField] private GameObject toastLayer;

        [Header("Target Graphic")]
        [Tooltip("HUD가 보일 때 표시할 이미지(스프라이트/색은 이 오브젝트에 직접 설정).")]
        [SerializeField] private Image shownTargetGraphic;
        [Tooltip("HUD가 숨겨졌을 때 표시할 이미지(스프라이트/색은 이 오브젝트에 직접 설정).")]
        [SerializeField] private Image hiddenTargetGraphic;

        private Button button;
        private bool isHudVisible = true;

        private void Awake()
        {
            button = GetComponent<Button>();

            // 저장된 설정이 있으면 기본값(true) 대신 그 값을 쓰고, 시작하자마자 바로 적용한다.
            UiSettingsData saved = UiSettingsSaveSystem.Load();
            isHudVisible = saved?.hudVisible ?? true;

            if (gameHud != null) gameHud.SetActive(isHudVisible);
            if (toastLayer != null) toastLayer.SetActive(isHudVisible);
        }

        private void OnEnable()
        {
            button.onClick.AddListener(Toggle);
            Refresh();
        }

        private void OnDisable()
        {
            button.onClick.RemoveListener(Toggle);
        }

        private void OnApplicationQuit()
        {
            UiSettingsSaveSystem.SaveHudVisible(isHudVisible);
        }

        private void Toggle()
        {
            isHudVisible = !isHudVisible;

            if (gameHud != null) gameHud.SetActive(isHudVisible);
            if (toastLayer != null) toastLayer.SetActive(isHudVisible);

            Refresh();
            UiSettingsSaveSystem.SaveHudVisible(isHudVisible); // 토글 즉시 저장 - 종료 시 저장은 비정상 종료 대비 안전망
        }

        private void Refresh()
        {
            ToggleButtonVisual.Apply(isHudVisible, shownTargetGraphic, hiddenTargetGraphic);
        }
    }
}
