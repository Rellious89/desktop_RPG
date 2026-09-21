using System;
using Common;
using Field;
using Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Building
{
    /// <summary>건물 건설 단계를 하단 메뉴의 기능 해금 상태로 보여 준다.</summary>
    [DisallowMultipleComponent]
    public sealed class MenuBuildingUnlockController : MonoBehaviour
    {
        [Serializable]
        private sealed class Entry
        {
            public UnityEngine.UI.Button menuButton;
            public UnityEngine.UI.Button companionButton;
            public TownBuildingInteractionController townBuilding;
            public GameObject lockIcon;
            public GameObject unlockIcon;
            public UnityEngine.UI.Image decoration;
            public UnityEngine.UI.Image companionDecoration;
            public TextMeshProUGUI timerText;

            [NonSerialized] public Color normalColor;
            [NonSerialized] public Color normalCompanionColor;
            [NonSerialized] public ModalPanelOpener menuOpener;
            [NonSerialized] public ModalPanelOpener companionOpener;
            [NonSerialized] public BuildingConstructionService fallbackService;
            [NonSerialized] public UnityAction menuClick;
            [NonSerialized] public UnityAction companionClick;
            [NonSerialized] public long displayedSeconds = -1;
        }

        [SerializeField] private InventoryManager inventoryManager;
        [SerializeField] private FieldModeManager fieldModeManager;
        [SerializeField] private BuildingPopupPanel unlockPopup;
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        [Tooltip("현재 메뉴 타이머 표기의 최대 초 수. 건물 자체의 완료 시각은 변경하지 않는다.")]
        [SerializeField] [Min(1)] private int maxDisplayedSeconds = 30;

        private static readonly Color32 LockedColor = new Color32(149, 149, 149, 100);

        private void Awake()
        {
            foreach (Entry entry in entries)
            {
                if (entry == null) continue;
                if (entry.decoration != null) entry.normalColor = entry.decoration.color;
                if (entry.companionDecoration != null)
                    entry.normalCompanionColor = entry.companionDecoration.color;
                if (entry.menuButton != null)
                {
                    entry.menuOpener = entry.menuButton.GetComponent<ModalPanelOpener>();
                    entry.menuClick = () => HandleClick(entry, entry.menuButton);
                }
                if (entry.companionButton != null)
                {
                    entry.companionOpener = entry.companionButton.GetComponent<ModalPanelOpener>();
                    entry.companionClick = () => HandleClick(entry, entry.companionButton);
                }
            }
        }

        private void OnEnable()
        {
            foreach (Entry entry in entries)
            {
                if (entry == null) continue;
                if (entry.menuButton != null) entry.menuButton.onClick.AddListener(entry.menuClick);
                if (entry.companionButton != null) entry.companionButton.onClick.AddListener(entry.companionClick);
            }
            Refresh();
        }

        private void OnDisable()
        {
            foreach (Entry entry in entries)
            {
                if (entry == null) continue;
                if (entry.menuButton != null) entry.menuButton.onClick.RemoveListener(entry.menuClick);
                if (entry.companionButton != null) entry.companionButton.onClick.RemoveListener(entry.companionClick);
            }
        }

        private void Update() => Refresh();

        private BuildingConstructionService Service(Entry entry)
        {
            BuildingConstructionService existing = entry.townBuilding != null
                ? entry.townBuilding.ConstructionService : null;
            if (existing != null) return existing;
            if (entry.fallbackService == null && inventoryManager != null)
            {
                entry.fallbackService = new BuildingConstructionService(
                    inventoryManager, () => SaveSystem.Data, SaveSystem.Save, () => DateTime.UtcNow);
            }
            return entry.fallbackService;
        }

        private void Refresh()
        {
            if (fieldModeManager != null && fieldModeManager.CurrentMode != FieldMode.Town &&
                unlockPopup != null && unlockPopup.gameObject.activeSelf)
                unlockPopup.Close();

            foreach (Entry entry in entries)
            {
                BuildingDefinition building = entry?.townBuilding != null ? entry.townBuilding.Building : null;
                BuildingConstructionService service = entry != null ? Service(entry) : null;
                if (building == null || service == null) continue;

                BuildingConstructionStatus status = service.GetStatus(building.BuildingId);
                bool completed = status.Phase == BuildingConstructionPhase.Completed;
                bool awaiting = status.Phase == BuildingConstructionPhase.AwaitingConfirmation;
                bool running = status.Phase == BuildingConstructionPhase.InProgress;

                // 기능 패널은 완료 확정 뒤에만 열 수 있다. Button 자체는 잠그지 않는다.
                if (entry.menuOpener != null && entry.menuOpener.enabled != completed)
                    entry.menuOpener.enabled = completed;
                if (entry.companionOpener != null && entry.companionOpener.enabled != completed)
                    entry.companionOpener.enabled = completed;

                SetActive(entry.lockIcon, !completed && !awaiting);
                SetActive(entry.unlockIcon, awaiting);
                if (entry.decoration != null)
                {
                    SetActive(entry.decoration.gameObject, true);
                    Color color = completed ? entry.normalColor : LockedColor;
                    if (entry.decoration.color != color) entry.decoration.color = color;
                }
                if (entry.companionDecoration != null)
                {
                    SetActive(entry.companionDecoration.gameObject, true);
                    Color color = completed ? entry.normalCompanionColor : LockedColor;
                    if (entry.companionDecoration.color != color)
                        entry.companionDecoration.color = color;
                }
                if (entry.timerText == null) continue;
                SetActive(entry.timerText.gameObject, running);
                if (!running)
                {
                    entry.displayedSeconds = -1;
                    continue;
                }
                long seconds = Math.Min(maxDisplayedSeconds,
                    BuildingInfoFormatter.ToDisplaySeconds(status.Remaining));
                if (seconds == entry.displayedSeconds) continue;
                entry.displayedSeconds = seconds;
                entry.timerText.text = seconds.ToString() + "s";
            }
        }

        private void HandleClick(Entry entry, UnityEngine.UI.Button source)
        {
            if (fieldModeManager != null && fieldModeManager.CurrentMode != FieldMode.Town) return;
            BuildingDefinition building = entry.townBuilding != null ? entry.townBuilding.Building : null;
            BuildingConstructionService service = Service(entry);
            if (building == null || service == null) return;

            BuildingConstructionStatus status = service.GetStatus(building.BuildingId);
            switch (status.Phase)
            {
                case BuildingConstructionPhase.NotStarted:
                    if (unlockPopup == null) return;
                    unlockPopup.SetFunctionIcon(entry.decoration != null ? entry.decoration.sprite : null);
                    unlockPopup.Bind(building, source.transform as RectTransform);
                    unlockPopup.SetConstructionService(service);
                    unlockPopup.Open();
                    break;
                case BuildingConstructionPhase.AwaitingConfirmation:
                    service.TryNotifyCompletion(building.BuildingId);
                    Refresh();
                    break;
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }
    }
}
