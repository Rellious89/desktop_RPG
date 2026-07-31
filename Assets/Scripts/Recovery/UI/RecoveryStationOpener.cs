using Common;
using UnityEngine;
using UnityEngine.UI;

namespace Recovery
{
    /// <summary>
    /// ControlDock의 회복소 버튼(btn_RecoveryStation)에 붙는 <b>복합 열기</b> 버튼. 회복소는 캐릭터를
    /// 끌어다 놓아야 쓸 수 있으므로, 회복소를 열 때 캐릭터 목록이 함께 보여야 한다.
    ///
    /// <code>
    /// btn_RecoveryStation 클릭 -> pn_RecoveryStation 열기
    ///                          -> pn_CharacterSwap이 닫혀 있으면 함께 열기
    /// btn_change 클릭          -> pn_CharacterSwap만 열기 (기존 ModalPanelOpener 그대로, 이 클래스와 무관)
    /// </code>
    ///
    /// <b>이미 열린 교체 패널은 건드리지 않는다.</b> 열려 있으면 Open()조차 부르지 않는다 -
    /// ModalPanel.Open()은 이미 열린 패널을 맨 앞으로 올리고 내용을 새로 그리는데, 그러면 사용자가
    /// 옮겨 둔 위치는 그대로여도 선택 상태가 초기화되고 포커스가 교체 패널로 넘어가 버린다.
    /// 회복소 버튼을 눌렀는데 교체 패널이 앞으로 나오는 것은 의도가 아니다.
    ///
    /// 열기 순서도 그래서 <b>교체 패널 먼저, 회복소 나중</b>이다 - 마지막에 연 패널이 활성 패널이
    /// 되므로 회복소가 앞에 온다.
    ///
    /// 닫기는 각 패널이 스스로 한다. 이 버튼은 어떤 패널도 닫지 않는다.
    /// </summary>
    [RequireComponent(typeof(Button))]
    [DisallowMultipleComponent]
    public class RecoveryStationOpener : MonoBehaviour
    {
        [Tooltip("열 회복소 패널(pn_RecoveryStation). 비활성 오브젝트를 그대로 연결하면 된다.")]
        [SerializeField] private RecoveryStationPanel recoveryPanel;

        [Tooltip("함께 열 캐릭터 교체 패널(pn_CharacterSwap). 이미 열려 있으면 건드리지 않는다. " +
                 "비워두면 회복소만 연다.")]
        [SerializeField] private CharacterSwapPanel characterSwapPanel;

        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();

            if (recoveryPanel == null)
            {
                Debug.LogError($"[RecoveryStationOpener] '{name}': 회복소 패널이 연결되지 않았습니다 - " +
                               "Inspector에서 pn_RecoveryStation을 연결하세요.", this);
            }
            if (characterSwapPanel == null)
            {
                Debug.LogWarning($"[RecoveryStationOpener] '{name}': 캐릭터 교체 패널이 연결되지 않아 " +
                                 "회복소만 열립니다 - 캐릭터를 끌어올 목록이 없으므로 pn_CharacterSwap을 " +
                                 "연결하세요.", this);
            }
        }

        private void OnEnable()
        {
            // 먼저 지우고 다시 건다 - 오브젝트가 여러 번 켜졌다 꺼져도 리스너가 쌓이지 않는다.
            button.onClick.RemoveListener(OpenPanels);
            button.onClick.AddListener(OpenPanels);
        }

        private void OnDisable()
        {
            button.onClick.RemoveListener(OpenPanels);
        }

        public void OpenPanels()
        {
            // 교체 패널을 먼저 연다 - 회복소가 나중에 열려 활성 패널(맨 앞)이 되게 하기 위함이다.
            if (characterSwapPanel != null && !characterSwapPanel.gameObject.activeSelf)
            {
                characterSwapPanel.Open();
            }

            if (recoveryPanel != null) recoveryPanel.Open();
        }
    }
}
