using Character;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Recovery;

namespace Common
{
    [DisallowMultipleComponent]
    public sealed class ItemUseTargetCharacterView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private ItemUseTargetDialog owner;
        private CharacterRoster roster;
        private CharacterDefinition character;
        private int effectValue;
        private Image portrait;
        private TextMeshProUGUI nameText;
        private TextMeshProUGUI levelText;
        private TextMeshProUGUI staminaText;
        private TextMeshProUGUI increaseText;
        private GameObject staminaFull;
        private ProgressBarView progress;
        private readonly CharacterNameBinding nameBinding = new CharacterNameBinding();
        private bool resolved;

        public CharacterDefinition Character => character;

        public void Bind(ItemUseTargetDialog dialog, CharacterRoster source, CharacterDefinition definition, int increase)
        {
            ResolveReferences();
            owner = dialog;
            roster = source;
            character = definition;
            effectValue = Mathf.Max(0, increase);

            Button button = GetComponent<Button>();
            if (button != null) button.interactable = true;
            CharacterRecoveryDragSource drag = GetComponent<CharacterRecoveryDragSource>();
            if (drag != null) drag.enabled = false;

            nameBinding.Bind(character, value =>
            {
                if (nameText != null) nameText.text = value;
            });
            if (portrait != null)
            {
                portrait.sprite = character != null ? character.Portrait : null;
                portrait.enabled = portrait.sprite != null;
            }

            Transform status = FindDeepChild(transform, "Status");
            if (status != null) status.gameObject.SetActive(false);
            Refresh();
        }

        public void Refresh()
        {
            ResolveReferences();
            if (roster == null || character == null) return;

            int current = roster.GetStamina(character);
            int max = roster.GetMaxStamina(character);
            if (levelText != null) levelText.text = $"Lv. {roster.GetLevel(character)}";
            if (staminaText != null) staminaText.text = $"{current} / {max}";
            if (progress != null) progress.SetValue(current, max);
            if (increaseText != null) increaseText.gameObject.SetActive(false);
            if (staminaFull != null) staminaFull.SetActive(max > 0 && current >= max);
        }

        public void Unbind()
        {
            nameBinding.Unbind();
            owner = null;
            roster = null;
            character = null;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (roster == null || character == null) return;
            int current = roster.GetStamina(character);
            int max = roster.GetMaxStamina(character);
            if (max <= 0 || current >= max)
            {
                if (increaseText != null) increaseText.gameObject.SetActive(false);
                return;
            }

            int preview = Mathf.Min(max, current + effectValue);
            int actualIncrease = preview - current;
            if (increaseText != null)
            {
                increaseText.text = $"+{actualIncrease}";
                increaseText.gameObject.SetActive(actualIncrease > 0);
            }
            if (progress != null) progress.SetPreviewValue(preview, max);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (increaseText != null) increaseText.gameObject.SetActive(false);
            if (roster != null && character != null && progress != null)
                progress.SetPreviewValue(roster.GetStamina(character), roster.GetMaxStamina(character));
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left) return;
            owner?.HandleTargetClicked(character);
        }

        private void OnDisable()
        {
            nameBinding.Unbind();
        }

        private void ResolveReferences()
        {
            if (resolved) return;
            resolved = true;
            portrait = FindComponent<Image>("sp_portrait");
            nameText = FindComponent<TextMeshProUGUI>("lb_name");
            levelText = FindComponent<TextMeshProUGUI>("lb_level");
            staminaText = FindComponent<TextMeshProUGUI>("lb_percent");
            increaseText = FindComponent<TextMeshProUGUI>("lb_increaseValue");
            Transform full = FindDeepChild(transform, "lb_staminaFull");
            staminaFull = full != null ? full.gameObject : null;
            progress = GetComponentInChildren<ProgressBarView>(true);
        }

        private T FindComponent<T>(string objectName) where T : Component
        {
            Transform found = FindDeepChild(transform, objectName);
            return found != null ? found.GetComponent<T>() : null;
        }

        private static Transform FindDeepChild(Transform root, string objectName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == objectName) return child;
                Transform found = FindDeepChild(child, objectName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
