using System;
using Character;
using Common;
using Recovery;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CharacterArchive
{
    /// <summary>명부 카드가 가진 기존 시각 요소에 캐릭터 조회 결과만 표시한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterArchiveCardView : MonoBehaviour
    {
        [SerializeField] private Image portrait;
        [SerializeField] private TMP_Text characterNameText;
        [SerializeField] private TMP_Text worldNameText;
        [SerializeField] private Button selectButton;
        [SerializeField] private GameObject partyMarker;
        [SerializeField] private GameObject recoveryMarker;
        [SerializeField] private GameObject activeMarker;
        [SerializeField] private GameObject selectMarker;

        private readonly CharacterNameBinding nameBinding = new CharacterNameBinding();
        private CharacterDefinition definition;
        private LocalizedTextReference worldName;

        public CharacterDefinition Definition => definition;

        public void Bind(CharacterDefinition value, SaveData data, CharacterDefinition current, bool selected, Action<CharacterArchiveCardView> onSelected)
        {
            Unbind();
            definition = value;
            if (definition == null)
            {
                ApplyEmpty();
                return;
            }

            if (portrait != null) portrait.sprite = definition.Portrait;
            nameBinding.Bind(definition, SetCharacterName);

            if (definition.OriginWorld != null && definition.OriginWorld.HasLocalizedName)
            {
                worldName = definition.OriginWorld.LocalizedName;
                worldName.StringChanged += SetWorldName;
            }
            else SetWorldName(string.Empty);

            SetActive(partyMarker, Contains(data != null ? data.partyCharacterIds : null, definition.CharacterId));
            SetActive(recoveryMarker, RecoveryStation.IsCharacterIdInSavedSlot(data, definition.CharacterId));
            SetActive(activeMarker, current != null && string.Equals(current.CharacterId, definition.CharacterId, StringComparison.Ordinal));
            SetSelected(selected);

            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                selectButton.onClick.AddListener(() => onSelected?.Invoke(this));
            }
        }

        public void SetSelected(bool value) => SetActive(selectMarker, value);

        private void OnDisable() => Unbind();

        private void OnDestroy() => Unbind();

        private void Unbind()
        {
            nameBinding.Unbind();
            if (worldName != null) worldName.StringChanged -= SetWorldName;
            worldName = null;
            definition = null;
            if (selectButton != null) selectButton.onClick.RemoveAllListeners();
        }

        private void ApplyEmpty()
        {
            if (portrait != null) portrait.sprite = null;
            SetCharacterName(string.Empty);
            SetWorldName(string.Empty);
            SetActive(partyMarker, false);
            SetActive(recoveryMarker, false);
            SetActive(activeMarker, false);
            SetSelected(false);
        }

        private void SetCharacterName(string value) { if (characterNameText != null) characterNameText.text = value ?? string.Empty; }
        private void SetWorldName(string value) { if (worldNameText != null) worldNameText.text = value ?? string.Empty; }
        private static void SetActive(GameObject target, bool value) { if (target != null && target.activeSelf != value) target.SetActive(value); }
        private static bool Contains(System.Collections.Generic.List<string> ids, string id)
        {
            if (ids == null || string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < ids.Count; i++) if (string.Equals(ids[i], id, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
