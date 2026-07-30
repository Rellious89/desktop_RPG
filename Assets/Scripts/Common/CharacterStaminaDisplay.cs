using Character;
using TMPro;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 지금 전투 중인 캐릭터의 행동력을 HUD에 표시한다. 캐릭터 교체 리스트의 항목
    /// (<see cref="CharacterSwapListItem"/>)과 같은 값을 같은 근거(CharacterRoster)로 그리되,
    /// 이쪽은 "현재 캐릭터 하나"만 본다.
    ///
    /// <b>경험치와 완전히 분리된다.</b> PlayerProgress나 경험치 이벤트를 구독하지 않고,
    /// <see cref="ProgressBarView"/>에 캐릭터별 현재/최대 행동력을 직접 주입한다 - 경험치 바
    /// 프리팹에서 가져오는 것은 시각 구조뿐이다.
    ///
    /// 갱신 시점은 세 가지뿐이다: 캐릭터가 교체됐을 때, 그 캐릭터의 행동력이 바뀌었을 때,
    /// 이 컴포넌트가 켜졌을 때. 매 프레임 폴링하지 않는다.
    ///
    /// 씬에 HUD 오브젝트를 만들고 이 컴포넌트를 붙이면 바로 동작한다 - 참조가 비어 있으면
    /// 그 표시만 건너뛰므로, 막대만 두거나 숫자만 둘 수도 있다.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterStaminaDisplay : MonoBehaviour
    {
        [Tooltip("행동력 막대. 비워두면 이 GameObject와 자식에서 찾는다.")]
        [SerializeField] private ProgressBarView staminaBar;

        [Tooltip("현재/최대 행동력 숫자 텍스트(선택).")]
        [SerializeField] private TextMeshProUGUI staminaValueText;

        [Tooltip("현재 캐릭터 이름 텍스트(선택).")]
        [SerializeField] private TextMeshProUGUI characterNameText;

        [SerializeField] private string staminaFormat = "{0} / {1}";

        private void OnEnable()
        {
            if (staminaBar == null) staminaBar = GetComponentInChildren<ProgressBarView>(true);

            CharacterRoster.CurrentCharacterChanged += HandleCurrentCharacterChanged;
            CharacterRoster.CharacterStateChanged += HandleCharacterStateChanged;

            Refresh();
        }

        private void OnDisable()
        {
            CharacterRoster.CurrentCharacterChanged -= HandleCurrentCharacterChanged;
            CharacterRoster.CharacterStateChanged -= HandleCharacterStateChanged;
        }

        /// <summary>CharacterRoster.Awake가 이 컴포넌트의 OnEnable보다 늦게 돌면 그때 발생한
        /// CurrentCharacterChanged를 놓친다 - Start는 모든 Awake 이후에 실행되므로 여기서 한 번 더
        /// 맞춘다(PlayerProgressDisplay가 OnProgressInitialized를 기다리는 것과 같은 이유다).</summary>
        private void Start()
        {
            Refresh();
        }

        private void HandleCurrentCharacterChanged(CharacterDefinition character)
        {
            Refresh();
        }

        /// <summary>현재 캐릭터가 아닌 다른 캐릭터의 값이 바뀐 경우에는 다시 그리지 않는다.</summary>
        private void HandleCharacterStateChanged(CharacterDefinition character)
        {
            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null || roster.Current != character) return;

            Refresh();
        }

        private void Refresh()
        {
            CharacterRoster roster = CharacterRoster.Instance;
            CharacterDefinition character = roster != null ? roster.Current : null;
            if (character == null) return;

            int current = roster.GetStamina(character);
            int max = roster.GetMaxStamina(character);

            if (staminaBar != null) staminaBar.SetValue(current, max);
            if (staminaValueText != null) staminaValueText.text = string.Format(staminaFormat, current, max);
            if (characterNameText != null) characterNameText.text = character.DisplayName;
        }
    }
}
