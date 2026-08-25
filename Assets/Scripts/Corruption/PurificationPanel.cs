using System;
using System.Collections.Generic;
using Building;
using Character;
using CharacterArchive;
using Common;
using UnityEngine;

namespace Corruption
{
    /// <summary>교회 기도 패널. 표시 갱신은 읽기 전용이며 정산/저장은 등록, 교체, 중단 때만 발생한다.</summary>
    [DisallowMultipleComponent]
    public sealed class PurificationPanel : ModalPanel
    {
        [SerializeField] private CharacterCatalog characterCatalog;
        [SerializeField] private PurificationConfigCatalog purificationCatalog;
        [SerializeField] private PurificationSlotView slotTemplate;
        [SerializeField] private RectTransform list;
        [SerializeField] private string purificationTypeId = "church_prayer";
        private readonly List<PurificationSlotView> slots = new List<PurificationSlotView>();
        private PurificationService service;
        private float refreshElapsed;

        protected override void OnModalOpened()
        {
            ResolveReferences();
            service = new PurificationService(() => SaveSystem.Data, SaveSystem.Save, () => DateTime.UtcNow,
                characterCatalog, purificationCatalog, IsBuildingComplete);
            EnsureSlots();
        }

        protected override void RefreshContents()
        {
            if (service == null) return;
            SaveData data = SaveSystem.Data;
            for (int i = 0; i < slots.Count; i++)
            {
                PurificationSlotSaveState slot = data != null && data.purificationSlots != null && i < data.purificationSlots.Count ? data.purificationSlots[i] : null;
                CharacterDefinition definition = slot != null && slot.HasCharacter && characterCatalog != null ? characterCatalog.Find(slot.characterId) : null;
                CharacterSaveState state = FindState(data, slot != null ? slot.characterId : null);
                service.TryGetRemainingTime(i, out TimeSpan remaining);
                slots[i].Refresh(definition, state != null ? state.currentCorruption : 0d,
                    definition != null ? definition.BaseCorruption : 0d, remaining);
            }
        }

        protected override void OnModalClosed()
        {
            refreshElapsed = 0f;
            for (int i = 0; i < slots.Count; i++) slots[i].ResetProgressVisuals();
        }

        private void Update()
        {
            if (service == null || !gameObject.activeInHierarchy) return;
            refreshElapsed += Time.unscaledDeltaTime;
            if (refreshElapsed < 0.2f) return;
            refreshElapsed = 0f;
            // 정산 경계에서만 최대 한 번 저장한다. 나머지 표시는 읽기 전용 계산이다.
            if (service.IsSettlementDue())
            {
                PurificationResult result = service.Tick();
                if (result.Success)
                {
                    CharacterArchivePanel.RequestRefresh(); CharacterSwapPanel.RequestRefresh();
                    Recovery.RecoveryService.NotifyRosterChangedAfterExternalSave();
                }
            }
            RefreshContents();
        }

        public void Register(int slotIndex, CharacterDefinition definition)
        {
            if (service == null || definition == null) return;
            PurificationResult result = service.TryRegister(purificationTypeId, definition.CharacterId, slotIndex);
            if (!result.Success)
            {
                if (result.Code == PurificationResultCode.MinimumPartySize) ShowToast("66", definition);
                return;
            }
            CharacterRoster.Instance?.RefreshPartyAfterExternalSave();
            CharacterArchivePanel.RequestRefresh(); CharacterSwapPanel.RequestRefresh(); Recovery.RecoveryService.NotifyRosterChangedAfterExternalSave();
            if (!string.IsNullOrEmpty(result.PreviousCharacterId)) ShowToast("64", characterCatalog != null ? characterCatalog.Find(result.PreviousCharacterId) : null);
            ShowToast("63", definition);
            RefreshContents();
        }

        public void Stop(int slotIndex)
        {
            if (service == null) return;
            PurificationResult result = service.TryStop(slotIndex);
            if (!result.Success) return;
            CharacterArchivePanel.RequestRefresh(); CharacterSwapPanel.RequestRefresh(); Recovery.RecoveryService.NotifyRosterChangedAfterExternalSave();
            ShowToast("64", characterCatalog != null ? characterCatalog.Find(result.CharacterId) : null);
            RefreshContents();
        }

        public static string FormatRemaining(TimeSpan value)
        {
            if (value < TimeSpan.Zero) value = TimeSpan.Zero;
            long hours = (long)value.TotalHours;
            return string.Format("{0:00}:{1:00}:{2:00}", hours, value.Minutes, value.Seconds);
        }

        private void ResolveReferences()
        {
            if (characterCatalog == null && CharacterRoster.Instance != null) characterCatalog = CharacterRoster.Instance.Catalog;
            if (purificationCatalog == null)
            {
                PurificationConfigCatalog[] all = Resources.FindObjectsOfTypeAll<PurificationConfigCatalog>();
                if (all != null && all.Length > 0) purificationCatalog = all[0];
            }
            if (list == null) { Transform found = FindDeepChild(transform, "list"); list = found as RectTransform; }
            if (slotTemplate == null) slotTemplate = GetComponentInChildren<PurificationSlotView>(true);
        }
        private void EnsureSlots()
        {
            if (slotTemplate == null || list == null || purificationCatalog == null) return;
            PurificationConfigDefinition config = purificationCatalog.Find(purificationTypeId);
            int count = config != null ? config.BaseSlotCount : 1;
            while (slots.Count < count)
            {
                PurificationSlotView slot = Instantiate(slotTemplate, list);
                slot.gameObject.SetActive(true); slots.Add(slot);
            }
            for (int i = 0; i < slots.Count; i++) slots[i].Bind(this, i);
            if (slotTemplate.transform.parent == list) slotTemplate.gameObject.SetActive(false);
        }
        private static bool IsBuildingComplete(string buildingId)
        {
            SaveData data = SaveSystem.Data; if (data == null || data.buildingConstructions == null) return false;
            for (int i = 0; i < data.buildingConstructions.Count; i++)
            {
                BuildingConstructionSaveState state = data.buildingConstructions[i];
                if (state != null && string.Equals(state.buildingId, buildingId, StringComparison.Ordinal) &&
                    SaveData.TryParseTimestamp(state.completeAtUtc, out DateTime completeAt) && completeAt <= DateTime.UtcNow) return true;
            }
            return false;
        }
        private static CharacterSaveState FindState(SaveData data, string id) { if (data == null || data.characters == null) return null; for (int i = 0; i < data.characters.Count; i++) if (data.characters[i] != null && data.characters[i].characterId == id) return data.characters[i]; return null; }
        private static void ShowToast(string key, CharacterDefinition definition)
        {
            if (ToastManager.Instance == null) return;
            var text = new LocalizedTextReference { TableReference = "01_UI", TableEntryReference = key };
            string localized = text.GetLocalizedString(definition != null ? CharacterNameBinding.GetCurrent(definition) : string.Empty);
            if (!string.IsNullOrEmpty(localized) && !localized.StartsWith("No translation found", StringComparison.Ordinal)) ToastManager.Instance.Show(localized);
        }
    }
}
