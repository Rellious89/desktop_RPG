using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// ControlDock의 패널 열기 버튼(캐릭터 교체는 btn_change, 인벤토리는 btn_inventory)에 붙여,
    /// 클릭하면 연결된 <see cref="ModalPanel"/>을 여는 최소 컴포넌트. 패널은 씬 시작 시 비활성
    /// 상태이므로 패널 자신이 "열어달라"는 신호를 받을 수 없다 - 비활성 오브젝트의 컴포넌트 참조는
    /// 그대로 유효하기 때문에 여기서 직접 Open()을 부른다.
    ///
    /// 닫기는 패널이 스스로 처리한다(btn_close). 패널이 열려 있는 동안에는 전체 화면 InputBlocker가
    /// 이 버튼을 덮으므로, 열린 상태에서 이 버튼이 다시 눌리거나 다른 패널이 함께 열리는 경로는 없다.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ModalPanelOpener : MonoBehaviour
    {
        [Tooltip("열 대상 패널(pn_CharacterSwap / pn_Inventory). 비활성 오브젝트를 그대로 연결하면 된다.")]
        [SerializeField] private ModalPanel panel;

        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();

            if (panel == null)
            {
                Debug.LogError($"[ModalPanelOpener] '{name}': 열 패널이 연결되지 않았습니다 - " +
                               "Inspector에서 대상 패널을 연결하세요.", this);
            }
        }

        private void OnEnable()
        {
            button.onClick.AddListener(OpenPanel);
        }

        private void OnDisable()
        {
            button.onClick.RemoveListener(OpenPanel);
        }

        public void OpenPanel()
        {
            if (panel == null) return;
            panel.Open();
        }
    }
}
