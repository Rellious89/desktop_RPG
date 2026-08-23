using Character;
using Common;
using Recovery;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CharacterArchive
{
    /// <summary>기존 명부 파티 슬롯의 표시, 드롭, 탈퇴 버튼만 연결한다. 파티 저장 규칙은 Panel의 서비스 경로에만 있다.</summary>
    [DisallowMultipleComponent]
    public sealed class PartySlotView : MonoBehaviour, IDropHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private CharacterArchivePanel owner;
        private int slotIndex;
        private GameObject enabledRoot;
        private GameObject disabledRoot;
        private GameObject activeMarker;
        private GameObject recoveryMarker;
        private Button removeButton;
        private Image portrait;
        private TMP_Text nameText;
        private TMP_Text worldNameText;
        private readonly CharacterNameBinding nameBinding = new CharacterNameBinding();
        private LocalizedTextReference worldName;
        private CharacterDefinition character;
        private bool dragging;

        public int SlotIndex => slotIndex;
        public string CharacterId => character != null ? character.CharacterId : null;
        public bool IsEnabled { get; private set; }
        public bool IsDraggingPartyMember => dragging && character != null;

        public void Bind(CharacterArchivePanel panel, int index)
        {
            owner = panel;
            slotIndex = index;
            ResolveViews();
            if (removeButton != null) { removeButton.onClick.RemoveListener(Remove); removeButton.onClick.AddListener(Remove); }
        }

        public void Unbind()
        {
            if (removeButton != null) removeButton.onClick.RemoveListener(Remove);
            nameBinding.Unbind();
            UnbindWorldName();
            owner = null; character = null; dragging = false;
        }

        public void Refresh(SaveData data, CharacterDefinition current, int capacity)
        {
            IsEnabled = slotIndex < capacity;
            string id = IsEnabled && data != null && data.partyCharacterIds != null && slotIndex < data.partyCharacterIds.Count
                ? data.partyCharacterIds[slotIndex] : null;
            CharacterDefinition next = !string.IsNullOrEmpty(id) ? owner?.FindCharacter(id) : null;
            character = next;
            SetActive(enabledRoot, character != null);
            SetActive(disabledRoot, character == null);
            SetActive(activeMarker, Same(character, current));
            SetActive(recoveryMarker, character != null && RecoveryStation.IsCharacterIdInSavedSlot(data, character.CharacterId));
            if (portrait != null) { portrait.sprite = character != null ? character.Portrait : null; portrait.enabled = portrait.sprite != null; }
            nameBinding.Bind(character, value => { if (nameText != null) nameText.text = value ?? string.Empty; });
            BindWorldName(character);
            if (removeButton != null) removeButton.interactable = IsEnabled && character != null && data != null && data.partyCharacterIds != null && data.partyCharacterIds.Count > 1;
        }

        public void OnDrop(PointerEventData eventData)
        {
            CharacterArchiveCardView card = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponentInParent<CharacterArchiveCardView>() : null;
            if (card != null && card.IsDraggingToParty) { owner?.HandlePartyDrop(card.Definition, this, false); return; }
            PartySlotView source = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponentInParent<PartySlotView>() : null;
            if (source != null && source != this && source.IsDraggingPartyMember) owner?.HandlePartyDrop(source.character, this, true);
        }

        public void OnBeginDrag(PointerEventData eventData) => dragging = IsEnabled && character != null;
        public void OnDrag(PointerEventData eventData) { }
        public void OnEndDrag(PointerEventData eventData) => dragging = false;
        private void OnDisable()
        {
            dragging = false;
            nameBinding.Unbind();
            UnbindWorldName();
            SetWorldName(string.Empty);
        }
        private void Remove() => owner?.LeavePartyMember(this);

        private void ResolveViews()
        {
            enabledRoot = FindObject("item_Party_enable"); disabledRoot = FindObject("item_Party_disable");
            activeMarker = FindObject("active"); recoveryMarker = FindObject("recovery");
            GameObject remove = FindObject("btn_remove"); removeButton = remove != null ? remove.GetComponent<Button>() : null;
            GameObject portraitObject = FindObject("sp_portrait"); portrait = portraitObject != null ? portraitObject.GetComponent<Image>() : null;
            GameObject name = FindObject("lb_CharacterName"); nameText = name != null ? name.GetComponent<TMP_Text>() : null;
            GameObject worldName = FindObject("lb_WorldName"); worldNameText = worldName != null ? worldName.GetComponent<TMP_Text>() : null;
        }

        private void BindWorldName(CharacterDefinition definition)
        {
            LocalizedTextReference next = definition != null && definition.OriginWorld != null && definition.OriginWorld.HasLocalizedName
                ? definition.OriginWorld.LocalizedName : null;
            if (ReferenceEquals(worldName, next))
            {
                if (next == null) SetWorldName(string.Empty);
                return;
            }

            UnbindWorldName();
            if (next == null)
            {
                SetWorldName(string.Empty);
                return;
            }

            worldName = next;
            worldName.StringChanged += SetWorldName;
            SetWorldName(worldName.GetLocalizedString());
        }

        private void UnbindWorldName()
        {
            if (worldName != null) worldName.StringChanged -= SetWorldName;
            worldName = null;
        }

        private void SetWorldName(string value)
        {
            if (worldNameText != null) worldNameText.text = value ?? string.Empty;
        }
        private GameObject FindObject(string objectName)
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == objectName) return all[i].gameObject;
            return null;
        }
        private static bool Same(CharacterDefinition left, CharacterDefinition right) => left != null && right != null && left.CharacterId == right.CharacterId;
        private static void SetActive(GameObject target, bool value) { if (target != null && target.activeSelf != value) target.SetActive(value); }
    }
}
