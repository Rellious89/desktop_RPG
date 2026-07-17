using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// ControlDock의 배치 버튼(예전 btn_moveHandle). 예전에는 누르고 있는 동안 네이티브 창을
    /// 드래그하는 핸들이었지만(MoveHandleDrag), 전체 모니터 Overlay 구조에서는 "네이티브 창 이동"
    /// 개념 자체가 없어졌고 Stage/HUD/Dock을 각각 직접 드래그하는 Layout Mode로 대체됐다. 이 버튼은
    /// 이제 그 Layout Mode를 켜고 끄는 단순 토글 버튼이다 - 클릭할 때마다
    /// LayoutModeController.ToggleLayoutMode()를 호출할 뿐, 자기 자신은 드래그 로직을 갖지 않는다.
    ///
    /// 이 버튼은 Layout Mode 중에도 항상 클릭 가능해야 한다(꺼야 다시 일반 모드로 돌아올 수 있으므로) -
    /// ControlDockGroup의 UiGroupDraggable(드래그 캐처)이 이 버튼의 클릭을 가로채지 않도록, Hierarchy에서
    /// 이 버튼을 드래그 캐처보다 뒤(형제 순서상 더 아래 = 더 위에 렌더링)에 둬야 한다. 자세한 배치는
    /// 에디터 설정 안내 참고.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class LayoutModeToggleButton : MonoBehaviour
    {
        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            button.onClick.AddListener(HandleClick);
        }

        private void OnDisable()
        {
            button.onClick.RemoveListener(HandleClick);
        }

        private void HandleClick()
        {
            if (LayoutModeController.Instance == null)
            {
                Debug.LogWarning("[LayoutModeToggleButton] LayoutModeController.Instance가 없습니다.");
                return;
            }

            LayoutModeController.Instance.ToggleLayoutMode();
        }
    }
}
