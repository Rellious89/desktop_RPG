using System;
using Character;
using Common;
using Recovery;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace CharacterArchive
{
    /// <summary>명부 카드가 가진 기존 시각 요소에 캐릭터 조회 결과만 표시한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterArchiveCardView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
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
        public bool IsDraggingToParty { get; private set; }

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
            IsDraggingToParty = false;
            nameBinding.Unbind();
            if (worldName != null) worldName.StringChanged -= SetWorldName;
            worldName = null;
            definition = null;
            if (selectButton != null) selectButton.onClick.RemoveAllListeners();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // 명부 카드는 선택 버튼을 계속 사용한다. 실제 드롭은 PartySlotView가 이 플래그와
            // Definition을 함께 확인하므로, 다른 UI의 드래그가 잘못 합류되는 일은 없다.
            IsDraggingToParty = definition != null;
        }

        public void OnEndDrag(PointerEventData eventData) => IsDraggingToParty = false;
        public void OnDrag(PointerEventData eventData) { }

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
