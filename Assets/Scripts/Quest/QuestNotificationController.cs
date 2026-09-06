using System.Collections.Generic;
using Character;
using CharacterArchive;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Quest
{
    /// <summary>HUD 완료 가능 퀘스트 알림만 소유한다. 자기 자신은 계속 살아 두고, ready가 없을 때는
    /// 클릭 가능한 메시지 영역만 숨겨 저장 확정 이벤트를 계속 받을 수 있게 한다.</summary>
    [DisallowMultipleComponent]
    public sealed class QuestNotificationController : MonoBehaviour
    {
        [SerializeField] private CharacterCatalog characterCatalog;
        [SerializeField] private CharacterStoryQuestCatalog questCatalog;
        [SerializeField] private CharacterArchivePanel characterArchivePanel;
        [SerializeField] private GameObject messageBox;
        [SerializeField] private TMP_Text countText;
        [SerializeField] private Button messageButton;

        private bool subscribed;
        private List<string> readyCharacterIds = new List<string>();

        public bool HasRequiredReferences => characterCatalog != null && questCatalog != null &&
                                             messageBox != null && countText != null && messageButton != null;
        public bool HasArchiveTarget => characterArchivePanel != null;
        public int ReadyCount => readyCharacterIds.Count;
        public string CurrentTargetId => readyCharacterIds.Count > 0 ? readyCharacterIds[0] : string.Empty;

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void Start() => Refresh();

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        public void Refresh()
        {
            SaveData data;
            readyCharacterIds = SaveSystem.TryGetLoadedData(out data)
                ? CharacterStoryQuestReadyQuery.GetReadyCharacterIds(data, characterCatalog, questCatalog)
                : new List<string>();

            bool visible = readyCharacterIds.Count > 0;
            if (countText != null) countText.text = readyCharacterIds.Count.ToString();
            if (messageBox != null && messageBox.activeSelf != visible) messageBox.SetActive(visible);
        }

        private void Subscribe()
        {
            if (subscribed) return;
            CharacterStoryQuestService.QuestStateChanged += HandleQuestStateChanged;
            if (messageButton != null)
            {
                messageButton.onClick.RemoveListener(OpenCurrentReadyQuest);
                messageButton.onClick.AddListener(OpenCurrentReadyQuest);
            }
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            CharacterStoryQuestService.QuestStateChanged -= HandleQuestStateChanged;
            if (messageButton != null) messageButton.onClick.RemoveListener(OpenCurrentReadyQuest);
            subscribed = false;
        }

        private void HandleQuestStateChanged(string _) => Refresh();

        /// <summary>클릭 순간 다시 조회해 완료 처리된 stale 대상을 열지 않는다. 같은 상태에서는 항상
        /// 첫 Ordinal CharacterId를 선택하므로 반복 클릭이 다른 용병으로 순환하지 않는다.</summary>
        public void OpenCurrentReadyQuest()
        {
            Refresh();
            if (readyCharacterIds.Count == 0 || characterArchivePanel == null) return;
            characterArchivePanel.OpenForStoryQuest(readyCharacterIds[0]);
        }
    }
}
