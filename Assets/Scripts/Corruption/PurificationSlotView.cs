using Character;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CharacterArchive;

namespace Corruption
{
    /// <summary>기도 슬롯 한 칸의 표시와 명부 카드 드롭만 담당한다. 데이터 변경은 부모 패널 서비스로 위임한다.</summary>
    [DisallowMultipleComponent]
    public sealed class PurificationSlotView : MonoBehaviour, IDropHandler
    {
        [SerializeField] private int slotIndex;
        private PurificationPanel panel;
        private GameObject enabledItem;
        private GameObject disabledItem;
        private Image portrait;
        private TMP_Text nameText;
        private TMP_Text percentText;
        private TMP_Text timerText;
        private Button stopButton;

        public int SlotIndex => slotIndex;

        private void Awake()
        {
            enabledItem = Find("item_Party_enable"); disabledItem = Find("item_Party_disable");
            portrait = FindComponent<Image>("portrait"); nameText = FindComponent<TMP_Text>("lb_CharacterName");
            percentText = FindComponent<TMP_Text>("lb_percent"); timerText = FindComponent<TMP_Text>("lb_RemainingTime");
            stopButton = FindComponent<Button>("btn_archive");
            if (stopButton != null) stopButton.onClick.AddListener(Stop);
        }

        private void OnDestroy() { if (stopButton != null) stopButton.onClick.RemoveListener(Stop); }
        internal void Bind(PurificationPanel value, int index) { panel = value; slotIndex = index; }

        internal void Refresh(CharacterDefinition definition, double corruption, System.TimeSpan remaining)
        {
            bool occupied = definition != null;
            SetActive(enabledItem, occupied); SetActive(disabledItem, !occupied);
            if (!occupied) return;
            if (portrait != null) portrait.sprite = definition.Portrait;
            if (nameText != null) nameText.text = CharacterNameBinding.GetCurrent(definition);
            if (percentText != null) percentText.text = string.Format("{0:0.#}%", Mathf.Clamp((float)(corruption / 300d * 100d), 0f, 100f));
            if (timerText != null) timerText.text = PurificationPanel.FormatRemaining(remaining);
        }

        public void OnDrop(PointerEventData eventData)
        {
            CharacterArchiveCardView card = eventData != null && eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<CharacterArchiveCardView>() : null;
            if (card == null || !card.IsDraggingToParty || card.Definition == null) return;
            panel?.Register(slotIndex, card.Definition);
        }

        private void Stop() { panel?.Stop(slotIndex); }
        private GameObject Find(string child) { Transform value = FindDeep(transform, child); return value != null ? value.gameObject : null; }
        private T FindComponent<T>(string child) where T : Component { Transform value = FindDeep(transform, child); return value != null ? value.GetComponent<T>() : null; }
        private static Transform FindDeep(Transform root, string name) { for (int i = 0; i < root.childCount; i++) { Transform child = root.GetChild(i); if (child.name == name) return child; Transform found = FindDeep(child, name); if (found != null) return found; } return null; }
        private static void SetActive(GameObject value, bool active) { if (value != null && value.activeSelf != active) value.SetActive(active); }
    }
}
