using Character;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// ControlDock의 <b>테스트용</b> 행동력 전체 충전 버튼(btn_switching). 클릭하면 보유한 모든
    /// 캐릭터의 현재 행동력이 최대치로 돌아간다 - 지금 소환된 캐릭터, 꺼져 있는 캐릭터, 행동력이 0인
    /// 캐릭터를 모두 포함한다.
    ///
    /// <b>캐릭터를 교체하지 않는다.</b> 이 오브젝트는 원래 캐릭터 순환 교체 테스트 버튼이었지만,
    /// 정식 교체 UI(CharacterSwapPanel)가 그 역할을 가져간 뒤 행동력 반복 테스트용으로 바꿨다.
    /// 씬의 다른 참조가 깨지지 않도록 GameObject 이름(btn_switching)은 그대로 두고 역할만 바꾼 것이라,
    /// 이름과 하는 일이 다르다는 점에 주의한다.
    ///
    /// 행동력 값과 저장은 전부 <see cref="CharacterRoster"/>가 소유한다 - 이 컴포넌트는 버튼 클릭을
    /// 그쪽 메서드 하나로 넘기기만 하고 자체 데이터를 갖지 않는다. 시간 경과 회복이나 정식 회복소
    /// 기능으로 확장하지 않는다.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class StaminaRefillTestButton : MonoBehaviour
    {
        private Button button;

        private void Awake()
        {
            button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            button.onClick.AddListener(RefillAllStamina);
        }

        private void OnDisable()
        {
            button.onClick.RemoveListener(RefillAllStamina);
        }

        public void RefillAllStamina()
        {
            if (CharacterRoster.Instance == null)
            {
                Debug.LogError("[StaminaRefillTestButton] 씬에 CharacterRoster가 없어 행동력을 충전할 수 없습니다.", this);
                return;
            }

            CharacterRoster.Instance.RefillAllStaminaToMax();
        }
    }
}
