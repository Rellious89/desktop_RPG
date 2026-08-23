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
        private bool allowPartyDrag;

        public CharacterDefinition Definition => definition;
        public bool IsDraggingToParty { get; private set; }

        public void Bind(CharacterDefinition value, SaveData data, CharacterDefinition current, bool selected, Action<CharacterArchiveCardView> onSelected, bool allowPartyDrag)
        {
            Unbind();
            definition = value;
            this.allowPartyDrag = allowPartyDrag && definition != null;
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

        private void OnDisable()
        {
            CharacterArchiveDragPreview.End();
            Unbind();
        }

        private void OnDestroy()
        {
            CharacterArchiveDragPreview.End();
            Unbind();
        }

        private void Unbind()
        {
            IsDraggingToParty = false;
            nameBinding.Unbind();
            if (worldName != null) worldName.StringChanged -= SetWorldName;
            worldName = null;
            definition = null;
            allowPartyDrag = false;
            if (selectButton != null) selectButton.onClick.RemoveAllListeners();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // 상세 카드와 미보유 카드는 조회/선택만 할 수 있다. 드롭은 PartySlotView가 이 플래그와
            // Definition을 함께 확인하므로, 허용되지 않은 카드가 합류 경로에 들어가지 않는다.
            IsDraggingToParty = allowPartyDrag && definition != null;
            if (IsDraggingToParty) CharacterArchiveDragPreview.Begin(gameObject, eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            IsDraggingToParty = false;
            CharacterArchiveDragPreview.End();
        }
        public void OnDrag(PointerEventData eventData)
        {
            if (IsDraggingToParty) CharacterArchiveDragPreview.UpdatePosition(eventData.position);
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
