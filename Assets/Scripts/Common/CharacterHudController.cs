using System.Collections.Generic;
using Character;
using Field;
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

        [Header("Field Mode Visibility")]
        [Tooltip("모드 전환의 단일 소유자. 비워 두면 시작 기본값인 마을로 간주한다. " +
                 "씬에서는 FieldModeManager를 직접 연결해야 전환을 즉시 따라간다.")]
        [SerializeField] private FieldModeManager fieldModeManager;

        [Tooltip("마을 모드에서 캐릭터 HUD를 표시할지.")]
        [SerializeField] private bool showInTown = true;

        [Tooltip("던전 모드에서 캐릭터 HUD를 표시할지.")]
        [SerializeField] private bool showInDungeon = true;

        private readonly List<CharacterHudSlotView> spawnedSlots = new List<CharacterHudSlotView>();
        private CharacterSwapConfirmationDialog swapConfirmationDialog;
        private bool subscribedToFieldMode;
        private bool visibleInCurrentMode = true;

        private void OnEnable()
        {
            CharacterRoster.CurrentCharacterChanged += HandleCurrentCharacterChanged;
            CharacterRoster.CharacterStateChanged += HandleCharacterStateChanged;
            CharacterRoster.RosterEntriesChanged += Rebuild;
            RecoveryService.SlotsChanged += RefreshAll;
            SubscribeToFieldMode();
            ApplyFieldMode(CurrentFieldMode());
        }

        private void OnDisable()
        {
            CharacterRoster.CurrentCharacterChanged -= HandleCurrentCharacterChanged;
            CharacterRoster.CharacterStateChanged -= HandleCharacterStateChanged;
            CharacterRoster.RosterEntriesChanged -= Rebuild;
            RecoveryService.SlotsChanged -= RefreshAll;
            UnsubscribeFromFieldMode();
        }

        // 플레이 중 Inspector 체크를 바꿔도 다음 필드 전환까지 이전 표시가 남지 않게 즉시 반영한다.
        // 루트는 끄지 않으므로 이 호출이 이벤트 구독 수명주기에 영향을 주지 않는다.
        private void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled) ApplyFieldMode(CurrentFieldMode());
        }

        /// <summary>로스터 표시 수는 빈 슬롯을 만들지 않고 최대 HUD 한도만 쓴다.</summary>
        public static int GetVisibleSlotCount(int rosterCount, int maximum)
        {
            return Mathf.Clamp(rosterCount, 0, Mathf.Max(0, maximum));
        }

        /// <summary>Inspector의 두 표시 체크를 현재 필드 모드에 적용한다.</summary>
        public static bool ShouldDisplayIn(FieldMode mode, bool displayInTown, bool displayInDungeon)
        {
            return mode == FieldMode.Town ? displayInTown : displayInDungeon;
        }

        private void Rebuild()
        {
            if (!visibleInCurrentMode)
            {
                SetAllSlotsInactive();
                return;
            }

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
            if (!visibleInCurrentMode) return;

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
            if (!visibleInCurrentMode) return;

            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null || selected == null || selected == roster.Current) return;

            // 교체의 실제 권한은 CharacterRoster가 단일하게 판정한다. HUD가 NoStamina를 별도
            // 예외 처리하는 이유는 확인창 대신 즉시 피드백을 주기 위해서이며, 판정 자체를 복제하지 않는다.
            CharacterRoster.SwapBlockReason reason = roster.GetSwapBlockReason(selected);
            if (reason == CharacterRoster.SwapBlockReason.NoStamina)
            {
                ResolveSwapConfirmationDialog()?.Close();
                ShowNoStaminaToast();
                return;
            }

            CharacterSwapConfirmationDialog dialog = ResolveSwapConfirmationDialog();
            if (dialog == null)
            {
                Debug.LogError("[CharacterHudController] dialog_CharacterSwap(CharacterSwapConfirmationDialog)을 찾지 못했습니다.", this);
                return;
            }

            // 확인창은 현재/선택 이름을 Locale 변경에도 갱신하고 confirm에서 TrySwitchTo를 재검증한다.
            // HUD는 결코 직접 Current를 바꾸지 않는다.
            dialog.OpenFor(selected, FindSlotRect(selected));
        }

        private CharacterSwapConfirmationDialog ResolveSwapConfirmationDialog()
        {
            if (swapConfirmationDialog == null)
            {
                swapConfirmationDialog = FindObjectOfType<CharacterSwapConfirmationDialog>(true);
            }

            return swapConfirmationDialog;
        }

        private RectTransform FindSlotRect(CharacterDefinition definition)
        {
            for (int i = 0; i < spawnedSlots.Count; i++)
            {
                CharacterHudSlotView slot = spawnedSlots[i];
                if (slot != null && slot.BoundCharacter == definition) return slot.transform as RectTransform;
            }

            return null;
        }

        internal static void ShowNoStaminaToast()
        {
            if (ToastManager.Instance == null) return;

            var text = new LocalizedTextReference
            {
                TableReference = "01_UI",
                TableEntryReference = "122",
            };
            string localized = text.GetLocalizedString();
            if (!string.IsNullOrEmpty(localized) && !localized.StartsWith("No translation found", System.StringComparison.Ordinal))
            {
                ToastManager.Instance.Show(localized);
            }
        }

        private void SetAllSlotsInactive()
        {
            for (int i = 0; i < spawnedSlots.Count; i++)
            {
                if (spawnedSlots[i] != null) spawnedSlots[i].gameObject.SetActive(false);
            }

            // 원본도 입력/레이아웃을 차지하지 않도록 슬롯과 같은 상태로 맞춘다.
            if (itemTemplate != null && itemTemplate.transform.parent == transform) itemTemplate.gameObject.SetActive(false);
        }

        private void SubscribeToFieldMode()
        {
            if (fieldModeManager == null) return;

            fieldModeManager.FieldModeChanged -= HandleFieldModeChanged;
            fieldModeManager.FieldModeChanged += HandleFieldModeChanged;
            subscribedToFieldMode = true;
        }

        private void UnsubscribeFromFieldMode()
        {
            if (!subscribedToFieldMode) return;

            subscribedToFieldMode = false;
            if (fieldModeManager != null) fieldModeManager.FieldModeChanged -= HandleFieldModeChanged;
        }

        private FieldMode CurrentFieldMode() =>
            fieldModeManager != null ? fieldModeManager.CurrentMode : FieldMode.Town;

        private void HandleFieldModeChanged(FieldMode mode, Dungeon.DungeonDefinition ignoredDungeon)
        {
            ApplyFieldMode(mode);
        }

        /// <summary>
        /// 컨트롤러 루트는 계속 켜 둔다. 루트를 끄면 FieldModeChanged 구독도 해제되어 다시 보일
        /// 필드 전환을 받을 수 없으므로, 실제 슬롯만 켜고 끈다.
        /// </summary>
        private void ApplyFieldMode(FieldMode mode)
        {
            bool shouldDisplay = ShouldDisplayIn(mode, showInTown, showInDungeon);
            if (visibleInCurrentMode == shouldDisplay)
            {
                if (shouldDisplay) Rebuild();
                return;
            }

            visibleInCurrentMode = shouldDisplay;
            if (!shouldDisplay)
            {
                ResolveSwapConfirmationDialog()?.Close();
                SetAllSlotsInactive();
                return;
            }

            Rebuild();
        }
    }
}
