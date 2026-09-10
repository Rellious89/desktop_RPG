using Character;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// HUD 슬롯에서 여는 전용 확인창. 현재/선택 캐릭터의 이름을 Locale 변경에도 갱신하고,
    /// 실제 교체는 confirm 시점의 CharacterRoster.TrySwitchTo 재검증으로만 수행한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterSwapConfirmationDialog : ModalPanel
    {
        [SerializeField] private Button confirmButton;
        [SerializeField] private TextMeshProUGUI currentCharacterText;
        [SerializeField] private TextMeshProUGUI selectedCharacterText;

        private readonly CharacterNameBinding currentName = new CharacterNameBinding();
        private readonly CharacterNameBinding selectedName = new CharacterNameBinding();
        private CharacterDefinition selectedCharacter;

        protected override string CloseButtonName => "btn_cancle";

        public void OpenFor(CharacterDefinition selected)
        {
            selectedCharacter = selected;
            Open();
        }

        protected override void OnModalOpened()
        {
            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(Confirm);
                confirmButton.onClick.AddListener(Confirm);
            }
        }

        protected override void OnModalClosed()
        {
            if (confirmButton != null) confirmButton.onClick.RemoveListener(Confirm);
            currentName.Unbind();
            selectedName.Unbind();
            selectedCharacter = null;
        }

        protected override void RefreshContents()
        {
            CharacterRoster roster = CharacterRoster.Instance;
            CharacterDefinition current = roster != null ? roster.Current : null;
            currentName.Bind(current, ApplyCurrentName);
            selectedName.Bind(selectedCharacter, ApplySelectedName);

            if (confirmButton != null)
            {
                confirmButton.interactable = roster != null && selectedCharacter != null &&
                                             roster.GetSwapBlockReason(selectedCharacter) == CharacterRoster.SwapBlockReason.None;
            }
        }

        private void Confirm()
        {
            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null || selectedCharacter == null) { RefreshContents(); return; }

            // 열려 있는 동안 행동력/회복 상태가 달라질 수 있으므로 반드시 다시 권한을 확인한다.
            if (!roster.TrySwitchTo(selectedCharacter, out CharacterRoster.SwapBlockReason reason))
            {
                Debug.LogWarning($"[CharacterSwapConfirmationDialog] 캐릭터 교체가 취소됐습니다(사유: {reason}).", this);
                RefreshContents();
                return;
            }

            Close();
        }

        private void ApplyCurrentName(string value)
        {
            if (currentCharacterText != null) currentCharacterText.text = value ?? string.Empty;
        }

        private void ApplySelectedName(string value)
        {
            if (selectedCharacterText != null) selectedCharacterText.text = value ?? string.Empty;
        }
    }
}
