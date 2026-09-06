using System;
using Common;
using Quest;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CharacterArchive
{
    /// <summary>서사 퀘스트 전체 목록의 한 행. 선택 및 표시만 담당하고 진행 상태는 변경하지 않는다.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterStoryQuestListItemView : MonoBehaviour, IPointerClickHandler
    {
        private static readonly Color SelectedColor = new Color32(0x01, 0xDC, 0xFF, 0xFF);
        private static readonly Color CompletedColor = new Color32(0x95, 0x95, 0x95, 0xFF);

        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text questTypeText;
        [SerializeField] private LocalizedTMPText titleLocalizer;

        private CharacterStoryQuestDefinition definition;
        private LocalizedTextReference boundTitle;
        private Action<string> selectedCallback;

        public string QuestId => definition != null ? definition.QuestId : string.Empty;
        public RectTransform RectTransform => transform as RectTransform;
        public bool HasRequiredReferences => titleText != null && questTypeText != null && titleLocalizer != null;

        public void Bind(CharacterStoryQuestDefinition quest, string questType, bool selected, bool completed,
            Action<string> onSelected)
        {
            Unbind();
            EnsureRaycastTarget();
            // 제작용 샘플 문구의 정적 localizer가 실제 퀘스트 제목을 다시 덮어쓰지 않게 한다.
            if (titleLocalizer != null) titleLocalizer.enabled = false;
            definition = quest;
            selectedCallback = onSelected;

            ApplyTitle(definition != null ? definition.QuestId : string.Empty);
            if (definition != null && definition.LocalizedTitle != null && definition.LocalizedTitle.HasReference)
            {
                boundTitle = definition.LocalizedTitle;
                boundTitle.StringChanged += ApplyTitle;
            }

            if (questTypeText != null)
            {
                questTypeText.text = questType ?? string.Empty;
                questTypeText.gameObject.SetActive(selected);
            }

            Color color = selected ? SelectedColor : completed ? CompletedColor : Color.white;
            if (titleText != null) titleText.color = color;
            if (questTypeText != null) questTypeText.color = color;
        }

        public void Unbind()
        {
            if (boundTitle != null) boundTitle.StringChanged -= ApplyTitle;
            boundTitle = null;
            definition = null;
            selectedCallback = null;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (definition != null) selectedCallback?.Invoke(definition.QuestId);
        }

        private void OnDestroy() => Unbind();

        private void ApplyTitle(string value)
        {
            if (titleText == null) return;
            titleText.text = string.IsNullOrWhiteSpace(value) && definition != null
                ? definition.QuestId
                : value ?? string.Empty;
        }

        private void EnsureRaycastTarget()
        {
            Graphic graphic = GetComponent<Graphic>();
            if (graphic == null)
            {
                Image input = gameObject.AddComponent<Image>();
                input.color = Color.clear;
                graphic = input;
            }
            graphic.raycastTarget = true;
        }
    }
}
