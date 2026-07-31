using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// <b>기능이 제거된 옛 테스트 버튼(btn_switching)의 껍데기.</b> 예전에는 클릭하면 보유한 모든
    /// 캐릭터의 행동력이 최대치로 돌아갔지만, 정식 회복 규칙(회복소)이 들어오면서 그 경로를 없앴다 -
    /// 재화를 내고 시간을 기다리는 회복을 우회해 행동력을 채우는 버튼이 남아 있으면, 회복 중인
    /// 캐릭터의 행동력이 회복소 계산과 어긋난다.
    ///
    /// <b>파일을 지우지 않고 남겨 둔 이유:</b> 이 컴포넌트는 씬(desktopScene)의 btn_switching에 붙어
    /// 있다. 스크립트 파일을 지우면 그 자리에 "Missing Script"가 남아 씬이 더러워지므로, 코드 쪽
    /// 동작만 먼저 없애고 <b>컴포넌트와 오브젝트 제거는 에디터에서</b> 하도록 남겼다. 그 정리가 끝나면
    /// 이 파일도 함께 지운다.
    ///
    /// 지금은 클릭해도 아무 일도 일어나지 않는다(Button의 onClick에 아무것도 등록하지 않는다).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class StaminaRefillTestButton : MonoBehaviour
    {
        private void Awake()
        {
            // 씬에 아직 남아 있다는 사실을 시작할 때 한 번만 알린다 - 조용히 죽어 있으면 "왜 눌러도
            // 안 되지?"를 코드에서 찾아야 한다.
            Debug.LogWarning($"[StaminaRefillTestButton] '{name}'의 행동력 전체 충전 기능은 제거됐습니다 - " +
                             "회복은 회복소(RecoveryService)에서만 일어납니다. 이 컴포넌트와 버튼은 " +
                             "에디터에서 정리하세요.", this);
        }
    }
}
