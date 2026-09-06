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
        [SerializeField] private GameObject completeLabel;

        private CharacterStoryQuestDefinition definition;
        private Action<string> selectedCallback;

        public string QuestId => definition != null ? definition.QuestId : string.Empty;
        public RectTransform RectTransform => transform as RectTransform;
        public bool HasRequiredReferences => titleText != null && questTypeText != null &&
                                             titleLocalizer != null && completeLabel != null;

        public void Bind(CharacterStoryQuestDefinition quest, string stageTitle, string questType, bool selected,
            bool completed, Action<string> onSelected)
        {
            Unbind();
            EnsureRaycastTarget();
            // {0} 인자가 필요한 제목은 컨트롤러가 현재 Locale과 정렬된 단계 번호로 조립한다.
            if (titleLocalizer != null) titleLocalizer.enabled = false;
            definition = quest;
            selectedCallback = onSelected;

            if (titleText != null) titleText.text = stageTitle ?? string.Empty;
            if (completeLabel != null) completeLabel.SetActive(completed);

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
            definition = null;
            selectedCallback = null;
            if (completeLabel != null) completeLabel.SetActive(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (definition != null) selectedCallback?.Invoke(definition.QuestId);
        }

        private void OnDestroy() => Unbind();

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
