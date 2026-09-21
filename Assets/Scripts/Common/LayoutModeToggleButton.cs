using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 예전 전역 Layout Mode 버튼의 씬 배선을 안전하게 유지하기 위한 레거시 마커다. 대상별 직접
    /// 롱프레스로 전환된 뒤에는 버튼 클릭에 기능을 연결하지 않는다. ControlDock 정리 시 함께 제거한다.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class LayoutModeToggleButton : MonoBehaviour
    {
        // 의도적으로 비어 있다. 씬/프리팹 참조를 깨지 않고 기능만 제거한다.
    }
}
