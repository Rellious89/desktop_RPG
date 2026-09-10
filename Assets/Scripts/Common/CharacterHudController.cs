using System.Collections.Generic;
using Character;
using Recovery;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// HUDLayoutRoot 아래 CharacterHUD의 파티 슬롯을 CharacterRoster의 표시 전용 투영으로 유지한다.
    /// 파티와 Current의 소유권은 로스터에 있으며, HUD는 교체를 직접 수행하지 않고 기존
    /// CharacterSwapConfirmationDialog로 선택을 넘긴다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterHudController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("비활성 원본 item_CharacterHUD. 이 프리팹을 복제해 실제 슬롯을 만든다.")]
        [SerializeField] private CharacterHudSlotView itemTemplate;

        [Min(1)]
        [SerializeField] private int maxVisibleSlots = 3;

        private readonly List<CharacterHudSlotView> spawnedSlots = new List<CharacterHudSlotView>();
        private CharacterSwapConfirmationDialog swapConfirmationDialog;

        private void OnEnable()
        {
            CharacterRoster.CurrentCharacterChanged += HandleCurrentCharacterChanged;
            CharacterRoster.CharacterStateChanged += HandleCharacterStateChanged;
            CharacterRoster.RosterEntriesChanged += Rebuild;
            RecoveryService.SlotsChanged += RefreshAll;
            Rebuild();
        }

        private void OnDisable()
        {
            CharacterRoster.CurrentCharacterChanged -= HandleCurrentCharacterChanged;
            CharacterRoster.CharacterStateChanged -= HandleCharacterStateChanged;
            CharacterRoster.RosterEntriesChanged -= Rebuild;
            RecoveryService.SlotsChanged -= RefreshAll;
        }

        /// <summary>로스터 표시 수는 빈 슬롯을 만들지 않고 최대 HUD 한도만 쓴다.</summary>
        public static int GetVisibleSlotCount(int rosterCount, int maximum)
        {
            return Mathf.Clamp(rosterCount, 0, Mathf.Max(0, maximum));
        }

        private void Rebuild()
        {
            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null || itemTemplate == null) { SetAllSlotsInactive(); return; }

            int count = GetVisibleSlotCount(roster.Entries.Count, maxVisibleSlots);
            EnsureSlotCount(count);
            for (int i = 0; i < count; i++)
            {
                CharacterHudSlotView slot = spawnedSlots[i];
                slot.gameObject.SetActive(true);
                slot.transform.SetSiblingIndex(i);
                slot.Bind(roster.Entries[i].definition, HandleSlotClicked);
                RefreshSlot(slot);
            }

            for (int i = count; i < spawnedSlots.Count; i++) spawnedSlots[i].gameObject.SetActive(false);
            // 원본은 레이아웃/입력을 차지하지 않도록 계속 비활성으로 보존한다.
            if (itemTemplate.transform.parent == transform) itemTemplate.gameObject.SetActive(false);
        }

        private void EnsureSlotCount(int count)
        {
            while (spawnedSlots.Count < count)
            {
                CharacterHudSlotView slot = Instantiate(itemTemplate, transform, false);
                slot.name = $"{itemTemplate.name}_{spawnedSlots.Count + 1}";
                spawnedSlots.Add(slot);
            }
        }

        private void RefreshAll()
        {
            for (int i = 0; i < spawnedSlots.Count; i++)
            {
                if (spawnedSlots[i] != null && spawnedSlots[i].gameObject.activeSelf) RefreshSlot(spawnedSlots[i]);
            }
        }

        private void RefreshSlot(CharacterHudSlotView slot)
        {
            CharacterRoster roster = CharacterRoster.Instance;
            if (slot == null || roster == null || slot.BoundCharacter == null) return;

            CharacterDefinition definition = slot.BoundCharacter;
            slot.Refresh(
                roster.GetStamina(definition),
                roster.GetMaxStamina(definition),
                roster.GetCorruption(definition),
                roster.GetCorruptionDisplayMaximum(),
                roster.Current == definition);
        }

        // CharacterRoster.Awake의 CurrentCharacterChanged가 HUD OnEnable보다 뒤에 올 수 있다.
        // 그 경우 첫 RefreshAll은 슬롯이 아직 없으므로, 여기서는 항상 목록부터 다시 만든다.
        private void HandleCurrentCharacterChanged(CharacterDefinition ignoredCharacter) => Rebuild();

        private void HandleCharacterStateChanged(CharacterDefinition character)
        {
            for (int i = 0; i < spawnedSlots.Count; i++)
            {
                CharacterHudSlotView slot = spawnedSlots[i];
                if (slot != null && slot.gameObject.activeSelf && slot.BoundCharacter == character)
                {
                    RefreshSlot(slot);
                    return;
                }
            }
        }

        private void HandleSlotClicked(CharacterDefinition selected)
        {
            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null || selected == null || selected == roster.Current) return;

            if (swapConfirmationDialog == null)
            {
                swapConfirmationDialog = FindObjectOfType<CharacterSwapConfirmationDialog>(true);
            }
            if (swapConfirmationDialog == null)
            {
                Debug.LogError("[CharacterHudController] dialog_CharacterSwap(CharacterSwapConfirmationDialog)을 찾지 못했습니다.", this);
                return;
            }

            // 확인창은 현재/선택 이름을 Locale 변경에도 갱신하고 confirm에서 TrySwitchTo를 재검증한다.
            // HUD는 결코 직접 Current를 바꾸지 않는다.
            swapConfirmationDialog.OpenFor(selected);
        }

        private void SetAllSlotsInactive()
        {
            for (int i = 0; i < spawnedSlots.Count; i++)
            {
                if (spawnedSlots[i] != null) spawnedSlots[i].gameObject.SetActive(false);
            }
        }
    }
}
