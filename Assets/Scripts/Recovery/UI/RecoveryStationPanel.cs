using Character;
using Common;
using UnityEngine;
using UnityEngine.UI;

namespace Recovery
{
    /// <summary>
    /// 회복소 패널(pn_RecoveryStation). 열고 닫기, 닫기 버튼, ESC 순서, 포커스, Windows 클릭 관통은
    /// 전부 기존 <see cref="ModalPanel"/> / <see cref="PopupPanelManager"/> 규칙을 그대로 쓴다 -
    /// 회복소 전용 팝업 시스템을 새로 만들지 않는다. 이 클래스는 슬롯 3개를 그리고 하단 버튼을
    /// 도메인 API(<see cref="RecoveryStation"/>)에 연결하는 일만 한다.
    ///
    /// <b>캐릭터 교체 패널과 완전히 독립이다.</b> 서로를 닫지 않고, 각자 PanelDragHandle로 따로
    /// 움직이며, 포커스도 PopupPanelManager가 클릭 순서대로 준다. 둘을 함께 <b>여는</b> 것만
    /// <see cref="RecoveryStationOpener"/>가 담당한다.
    ///
    /// <b>닫히면 대기(Pending)는 모두 사라진다.</b> 닫기 버튼, ESC, 코드에 의한 Close, 오브젝트 비활성
    /// 어느 경로든 결국 OnDisable을 지나므로 정리 지점이 하나다. 이미 시작된
    /// Recovering/RecoveryComplete는 영향을 받지 않는다 - 재화를 낸 회복은 패널을 닫는다고 사라지지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class RecoveryStationPanel : ModalPanel
    {
        [Header("Slots (슬롯마다 직접 연결한다 - 같은 이름의 자식이 반복되므로 자동 탐색하지 않는다)")]
        [Tooltip("list_RecoverySlot_1/2/3의 RecoveryStationSlotView. 순서는 상관없고 각 컴포넌트의 " +
                 "Slot Index가 실제 슬롯 번호를 정한다.")]
        [SerializeField] private RecoveryStationSlotView[] slots = new RecoveryStationSlotView[0];

        [Header("Bottom Buttons")]
        [Tooltip("회복 시작 버튼(bottom/btn_StartRecovery). 대기가 1명 이상일 때만 켜진다.")]
        [SerializeField] private Button startRecoveryButton;

        [Tooltip("대기 전체 취소 버튼(bottom/btn_cancel). 대기가 1명 이상일 때만 켜진다.")]
        [SerializeField] private Button cancelPendingButton;

        [Tooltip("완료된 캐릭터를 모두 합류시키는 버튼(bottom/btn_JoinParty). " +
                 "슬롯 안의 같은 이름 버튼이 아니라 하단의 것을 연결한다.")]
        [SerializeField] private Button joinAllButton;

        [Header("Refresh")]
        [Tooltip("남은 시간 표시를 다시 계산하는 주기(초, 실제 시간). 회복 중인 슬롯이 하나도 없으면 " +
                 "아무 것도 하지 않는다. 표시 초가 그대로면 문자열도 다시 만들지 않는다.")]
        [Min(0.05f)]
        [SerializeField] private float timeRefreshInterval = 0.25f;

        private float timeRefreshTimer;
        private bool referencesValidated;

        // 닫히는 도중에 들어오는 이벤트로 다시 그리지 않기 위한 재진입 방지 표시.
        private bool closing;

        protected override void OnModalOpened()
        {
            ValidateReferences();

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) slots[i].Bind(this);
            }

            BindButton(startRecoveryButton, HandleStartRecoveryClicked);
            BindButton(cancelPendingButton, HandleCancelPendingClicked);
            BindButton(joinAllButton, HandleJoinAllClicked);

            // 회복 진행/완료는 패널이 닫혀 있어도 계속 일어난다 - 열려 있는 동안만 구독해서 화면을 맞춘다.
            RecoveryService.SlotsChanged += HandleSlotsChanged;
            RecoveryService.RecoveryCompleted += HandleRecoveryCompleted;
            CharacterRoster.CharacterStateChanged += HandleCharacterStateChanged;

            timeRefreshTimer = 0f;
            closing = false;
        }

        protected override void OnModalClosed()
        {
            // 순서가 중요하다: 구독을 먼저 끊어야 아래 ClearPending이 만드는 SlotsChanged가
            // 이 패널로 되돌아오지 않는다(닫히는 중에 다시 그리는 재진입 경로를 없앤다).
            RecoveryService.SlotsChanged -= HandleSlotsChanged;
            RecoveryService.RecoveryCompleted -= HandleRecoveryCompleted;
            CharacterRoster.CharacterStateChanged -= HandleCharacterStateChanged;

            UnbindButton(startRecoveryButton, HandleStartRecoveryClicked);
            UnbindButton(cancelPendingButton, HandleCancelPendingClicked);
            UnbindButton(joinAllButton, HandleJoinAllClicked);

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) slots[i].Unbind();
            }

            closing = true;

            // 닫히면 아직 시작하지 않은 대기는 전부 버린다. 이미 진행 중인 슬롯은 건드리지 않는다.
            RecoveryStation station = RecoveryService.Station;
            if (station != null && station.PendingCount > 0)
            {
                station.ClearPending();
                NotifyCharacterListChanged();
            }
        }

        protected override void RefreshContents()
        {
            if (closing) return;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) slots[i].Refresh();
            }

            UpdateBottomButtons();
        }

        private void Update()
        {
            if (closing) return;

            RecoveryStation station = RecoveryService.Station;
            if (station == null) return;

            // 회복 중인 슬롯이 없으면 시간이 흐르는 표시가 없다 - 타이머 자체를 돌리지 않는다.
            if (!HasRecoveringSlot()) return;

            timeRefreshTimer += Time.unscaledDeltaTime;
            if (timeRefreshTimer < timeRefreshInterval) return;
            timeRefreshTimer = 0f;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) slots[i].RefreshRemainingTime();
            }

            // 남은 시간이 0을 지나 완료로 바뀌면 합류 버튼 상태도 함께 달라져야 한다.
            UpdateBottomButtons();
        }

        private bool HasRecoveringSlot()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && slots[i].DisplayedState == RecoveryCharacterState.Recovering) return true;
            }
            return false;
        }

        // ---- 하단 버튼 ----

        /// <summary>
        /// 하단 버튼은 가능한 행동이 있을 때만 표시한다.
        /// Pending이 있으면 회복 시작/취소를, 완료 슬롯이 있으면 전체 합류를 표시한다. 두 상태가
        /// 동시에 존재하면 세 버튼이 모두 보이며, 빈 슬롯이나 회복 중 슬롯만 있으면 전부 숨긴다.
        /// </summary>
        private void UpdateBottomButtons()
        {
            RecoveryStation station = RecoveryService.Station;
            int pendingCount = station != null ? station.PendingCount : 0;
            bool hasCompleted = station != null && HasCompletedSlot(station);
            bool hasPending = pendingCount >= 1;

            SetButtonVisible(startRecoveryButton, hasPending);
            SetButtonVisible(cancelPendingButton, hasPending);
            SetButtonVisible(joinAllButton, hasCompleted);
        }

        private static void SetButtonVisible(Button button, bool visible)
        {
            if (button == null) return;

            if (button.gameObject.activeSelf != visible) button.gameObject.SetActive(visible);
            button.interactable = visible;
        }

        private static bool HasCompletedSlot(RecoveryStation station)
        {
            for (int i = 0; i < station.SlotCount; i++)
            {
                if (station.GetSlotState(i) == RecoveryCharacterState.RecoveryComplete) return true;
            }
            return false;
        }

        private void HandleStartRecoveryClicked()
        {
            RecoveryStation station = RecoveryService.Station;
            if (station == null) return;

            RecoveryStartResult result = station.StartRecovery();
            switch (result.Code)
            {
                case RecoveryStartResultCode.Success:
                    // 슬롯이 전부 Recovering으로 바뀐다. 캐릭터 리스트에서도 그 캐릭터들이 회복 중으로
                    // 보여야 하므로 함께 갱신한다.
                    RefreshContents();
                    NotifyCharacterListChanged();
                    break;

                case RecoveryStartResultCode.InsufficientFunds:
                    // 재화도 캐릭터도 회복 슬롯도 바뀌지 않았다(1단계가 보장한다). 회복소만 평소의
                    // 닫기 경로로 닫으면 OnModalClosed에서 대기가 전부 정리된다 - 교체 패널은 그대로 둔다.
                    Debug.Log($"[RecoveryStationPanel] 재화가 부족해 회복을 시작하지 못했습니다 " +
                              $"(필요 {result.TotalCost}, 보유 {result.Balance}, 부족 {result.Shortfall}) - " +
                              "회복소를 닫고 대기를 정리합니다.", this);
                    Close();
                    break;

                default:
                    // 부분 성공은 없다 - 아무것도 바뀌지 않았으므로 화면만 최신으로 되돌린다.
                    // 스펙에 없는 경고 팝업은 만들지 않는다.
                    Debug.LogWarning($"[RecoveryStationPanel] 회복을 시작하지 못했습니다(사유: {result.Code}" +
                                     (result.BlockedCharacter != null
                                         ? $", 대상: {result.BlockedCharacter.CharacterId}, 상태: {result.BlockReason}"
                                         : string.Empty) + ").", this);
                    RefreshContents();
                    NotifyCharacterListChanged();
                    break;
            }
        }

        private void HandleCancelPendingClicked()
        {
            RecoveryStation station = RecoveryService.Station;
            if (station == null || station.PendingCount == 0) return;

            // 진행 중/완료 슬롯에는 영향이 없다(1단계 ClearPending의 계약).
            station.ClearPending();
            RefreshContents();
            NotifyCharacterListChanged();
        }

        private void HandleJoinAllClicked()
        {
            RecoveryStation station = RecoveryService.Station;
            if (station == null) return;

            // 완료된 캐릭터만 합류한다 - 진행 중인 슬롯은 그대로 남는다.
            if (station.JoinAllCompleted() == 0) return;

            RefreshContents();
            NotifyCharacterListChanged();
        }

        /// <summary>슬롯 안의 합류 버튼이 부른다 - 그 슬롯 하나만 합류시킨다.</summary>
        public void JoinSlot(int slotIndex)
        {
            RecoveryStation station = RecoveryService.Station;
            if (station == null) return;

            if (!station.TryJoin(slotIndex, out _)) return;

            RefreshContents();
            NotifyCharacterListChanged();
        }

        // ---- 드롭 ----

        /// <summary>
        /// 슬롯에 캐릭터를 놓았을 때 호출된다. 수락 여부는 전적으로
        /// <see cref="RecoveryStation.TryAddPendingToSlot"/>이 정한다 - 이미 차 있는 슬롯,
        /// 회복 중/완료 슬롯, 이미 대기 중인 캐릭터는 그쪽에서 거부되고 <b>아무 상태도 바뀌지 않는다</b>.
        /// 성공해도 재화 차감이나 타이머 시작은 없다(시작 버튼을 눌러야 한다).
        /// </summary>
        public void HandleSlotDrop(int slotIndex, CharacterDefinition character)
        {
            RecoveryStation station = RecoveryService.Station;
            if (station == null || character == null) return;

            if (!station.TryAddPendingToSlot(slotIndex, character, out RecoveryRegisterBlockReason reason))
            {
                // 실패는 조용히 무시한다 - 화면 상태가 그대로이므로 사용자에게는 "놓이지 않았다"로
                // 보인다. 스펙에 없는 경고 팝업을 만들지 않는다.
                Debug.Log($"[RecoveryStationPanel] 슬롯 {slotIndex}에 '{character.CharacterId}'를 등록하지 " +
                          $"못했습니다(사유: {reason}).", this);
                return;
            }

            RefreshContents();
            // 같은 캐릭터를 한 번 더 끌어오지 못하도록 리스트를 즉시 다시 그린다.
            NotifyCharacterListChanged();
        }

        // ---- 이벤트 ----

        private void HandleSlotsChanged()
        {
            RefreshContents();
        }

        private void HandleRecoveryCompleted(int slotIndex, CharacterDefinition character)
        {
            // 완료 전환은 SlotsChanged와 함께 오지만, 순서에 기대지 않고 여기서도 한 번 맞춘다.
            RefreshContents();
            NotifyCharacterListChanged();
        }

        private void HandleCharacterStateChanged(CharacterDefinition character)
        {
            if (closing) return;

            // 회복 중 캐릭터의 행동력이 한 단계 오르면 슬롯의 막대/수치가 달라진다.
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) slots[i].Refresh();
            }
        }

        /// <summary>열려 있는 캐릭터 교체 패널의 리스트를 다시 그리게 한다 - 회복 상태가 바뀌면
        /// 그쪽의 상태 문구와 드래그 가능 여부가 함께 달라져야 한다. 교체 패널이 닫혀 있으면
        /// 아무 일도 하지 않는다(패널을 억지로 열지 않는다).</summary>
        private void NotifyCharacterListChanged()
        {
            CharacterSwapPanel.RequestRefresh();
        }

        // ---- 참조 검증 ----

        private void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;

            // 먼저 지우고 다시 건다 - 패널을 여러 번 열고 닫아도 리스너가 쌓이지 않는다.
            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private static void UnbindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.RemoveListener(action);
        }

        /// <summary>빠진 참조를 자동으로 채우지 않고 무엇이 빠졌는지만 알린다(교체 패널과 같은 방침).
        /// 슬롯 번호 중복은 조용히 두면 두 슬롯이 같은 칸을 그리므로 반드시 드러낸다.</summary>
        private void ValidateReferences()
        {
            if (referencesValidated) return;
            referencesValidated = true;

            if (slots == null || slots.Length == 0)
            {
                Debug.LogError($"[RecoveryStationPanel] '{name}': 슬롯이 하나도 연결되지 않았습니다 - " +
                               "list_RecoverySlot_1/2/3의 RecoveryStationSlotView를 연결하세요.", this);
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                {
                    Debug.LogError($"[RecoveryStationPanel] '{name}': Slots[{i}]가 비어 있습니다.", this);
                    continue;
                }

                for (int j = i + 1; j < slots.Length; j++)
                {
                    if (slots[j] != null && slots[j].SlotIndex == slots[i].SlotIndex)
                    {
                        Debug.LogError($"[RecoveryStationPanel] '{name}': Slot Index {slots[i].SlotIndex}가 " +
                                       $"Slots[{i}]와 Slots[{j}]에 중복됩니다 - 두 칸이 같은 슬롯을 그립니다.", this);
                    }
                }
            }

            if (startRecoveryButton == null)
            {
                Debug.LogError($"[RecoveryStationPanel] '{name}': 회복 시작 버튼(btn_StartRecovery)이 연결되지 않았습니다.", this);
            }
            if (cancelPendingButton == null)
            {
                Debug.LogError($"[RecoveryStationPanel] '{name}': 취소 버튼(btn_cancel)이 연결되지 않았습니다.", this);
            }
            if (joinAllButton == null)
            {
                Debug.LogError($"[RecoveryStationPanel] '{name}': 전체 합류 버튼(btn_JoinParty)이 연결되지 않았습니다.", this);
            }

            if (RecoveryService.Station == null)
            {
                Debug.LogWarning($"[RecoveryStationPanel] '{name}': 회복소(RecoveryService)가 준비되지 않아 " +
                                 "슬롯이 모두 빈 칸으로 표시됩니다 - RecoveryService와 Recovery Balance Table " +
                                 "연결을 확인하세요.", this);
            }
        }
    }
}
