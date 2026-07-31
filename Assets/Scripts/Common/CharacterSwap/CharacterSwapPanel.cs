using System.Collections.Generic;
using Character;
using Recovery;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 캐릭터 교체 패널(pn_CharacterSwap). 씬 시작 시에는 비활성 상태이며, ControlDock의
    /// <see cref="ModalPanelOpener"/>가 <see cref="ModalPanel.Open"/>으로 켠다 - 켜지는 순간
    /// (OnEnable) 리스트를 항상 새로 만들기 때문에, 닫았다 다시 열면 언제나 최신 상태가 표시된다.
    ///
    /// <b>선택과 교체는 분리한다.</b> 리스트 항목을 누르면 pendingCharacter만 바뀌고 전투 캐릭터는
    /// 그대로다. btn_swap을 눌러야 CharacterRoster.TrySwitchTo가 호출되어 실제 교체가 일어난다.
    /// 교체가 막히는 경우(현재 사용 중 / 행동력 0)에는 애초에 btn_swap이 비활성 상태이고 항목도
    /// 클릭되지 않으므로, "눌렀는데 아무 일도 일어나지 않는" 상태가 생기지 않는다.
    ///
    /// <b>리스트 구조는 프리팹이 소유한다.</b> ScrollRect / Viewport / Content는 프리팹에 미리
    /// 만들어 두고 에디터에서 연결한다 - 이 코드는 그 구조를 만들지도, 참조를 덮어쓰지도, 레이아웃
    /// 값을 보정하지도 않는다. 예전에는 런타임에 Viewport/Content/ScrollRect를 자동 생성했는데,
    /// 그러면 에디터에서 구조와 참조 관계를 확인하거나 조정할 수 없어서 전부 제거했다. 참조가
    /// 빠져 있으면 조용히 만들어 채우지 않고 경고를 남기고 리스트를 만들지 않는다.
    ///
    /// 열고 닫기, 닫기 버튼 연결, 배경 입력 차단, Windows 클릭 관통 예외는 <see cref="ModalPanel"/>이
    /// 공통으로 처리한다 - 이 클래스는 캐릭터 리스트를 만들고 선택/교체를 다루는 일만 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterSwapPanel : ModalPanel
    {
        [Header("References (모두 에디터에서 직접 연결한다 - 자동 탐색/자동 생성 없음)")]
        [Tooltip("선택한 캐릭터로 실제 교체하는 버튼(btn_swap).")]
        [SerializeField] private Button swapButton;

        [Tooltip("리스트의 ScrollRect(list에 붙어 있는 것). Viewport/Content 연결과 스크롤 설정은 " +
                 "이 컴포넌트에서 에디터로 관리하며, 코드가 값을 덮어쓰지 않는다.")]
        [SerializeField] private ScrollRect characterScrollRect;

        [Tooltip("리스트 항목을 넣을 Content. 비워두면 위 ScrollRect에 연결된 Content를 그대로 읽어 쓴다 " +
                 "(값을 다시 써넣지는 않는다).")]
        [SerializeField] private RectTransform content;

        [Tooltip("복제할 리스트 항목 원본. 프리팹의 Content 아래에 비활성으로 배치해 둔 list_Character를 연결한다.")]
        [SerializeField] private CharacterSwapListItem itemTemplate;

        private readonly List<CharacterSwapListItem> spawnedItems = new List<CharacterSwapListItem>();

        /// <summary>지금 열려 있는 패널. 회복소가 상태를 바꾼 뒤 이 목록을 다시 그리게 할 때 쓴다
        /// (열려 있지 않으면 null이라 아무 일도 일어나지 않는다 - 패널을 억지로 열지 않는다).</summary>
        private static CharacterSwapPanel openInstance;

        private CharacterDefinition pendingCharacter;
        private bool referencesValidated;

        /// <summary>열려 있는 캐릭터 교체 패널의 목록을 다시 그린다. 회복소에서 상태가 바뀌었을 때
        /// (등록/취소/시작/합류) 호출한다 - 상태 문구와 드래그 가능 여부가 함께 달라지기 때문이다.
        /// 패널이 닫혀 있으면 아무 일도 하지 않는다.</summary>
        public static void RequestRefresh()
        {
            if (openInstance == null) return;
            openInstance.RefreshAllItems();
            openInstance.UpdateSwapButton();
        }

        protected override void OnModalOpened()
        {
            ValidateReferences();

            if (swapButton != null)
            {
                swapButton.onClick.RemoveListener(ApplyPendingSwap);
                swapButton.onClick.AddListener(ApplyPendingSwap);
            }

            CharacterRoster.CurrentCharacterChanged += HandleCurrentCharacterChanged;
            CharacterRoster.CharacterStateChanged += HandleCharacterStateChanged;
            RecoveryService.SlotsChanged += HandleRecoverySlotsChanged;

            openInstance = this;
        }

        protected override void OnModalClosed()
        {
            CharacterRoster.CurrentCharacterChanged -= HandleCurrentCharacterChanged;
            CharacterRoster.CharacterStateChanged -= HandleCharacterStateChanged;
            RecoveryService.SlotsChanged -= HandleRecoverySlotsChanged;

            if (swapButton != null) swapButton.onClick.RemoveListener(ApplyPendingSwap);

            if (openInstance == this) openInstance = null;
            pendingCharacter = null;
        }

        /// <summary>패널이 열릴 때(그리고 이미 열린 상태에서 다시 Open이 불릴 때) 리스트를 최신 상태로
        /// 다시 만든다. 선택은 항상 초기화하므로 이전에 골라 둔 캐릭터가 남지 않는다.</summary>
        protected override void RefreshContents()
        {
            pendingCharacter = null;
            RebuildList();
        }

        // ---- 리스트 ----

        /// <summary>로스터의 캐릭터 목록으로 리스트를 처음부터 다시 만든다. 항목 수가 캐릭터 수와
        /// 항상 같도록 남는 복제본은 파괴한다 - 캐릭터가 줄어든 뒤 유령 항목이 남지 않게 한다.</summary>
        private void RebuildList()
        {
            RectTransform itemParent = ResolveItemParent();
            if (itemTemplate == null || itemParent == null) return;

            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null)
            {
                Debug.LogError("[CharacterSwapPanel] 씬에 CharacterRoster가 없어 캐릭터 리스트를 만들 수 없습니다.", this);
                UpdateSwapButton();
                return;
            }

            IReadOnlyList<CharacterRoster.Entry> rosterEntries = roster.Entries;

            for (int i = spawnedItems.Count - 1; i >= rosterEntries.Count; i--)
            {
                if (spawnedItems[i] != null) Destroy(spawnedItems[i].gameObject);
                spawnedItems.RemoveAt(i);
            }

            for (int i = 0; i < rosterEntries.Count; i++)
            {
                CharacterSwapListItem item = i < spawnedItems.Count ? spawnedItems[i] : CreateItem();
                if (i >= spawnedItems.Count) spawnedItems.Add(item);

                item.transform.SetSiblingIndex(i);
                item.gameObject.SetActive(true);
                item.Bind(rosterEntries[i].definition, HandleItemSelected);
            }

            RefreshAllItems();
            UpdateSwapButton();
        }

        private CharacterSwapListItem CreateItem()
        {
            CharacterSwapListItem item = Instantiate(itemTemplate, ResolveItemParent(), false);
            item.name = $"{itemTemplate.name}_item";
            return item;
        }

        private void RefreshAllItems()
        {
            for (int i = 0; i < spawnedItems.Count; i++)
            {
                RefreshItem(spawnedItems[i]);
            }
        }

        /// <summary>
        /// 항목 하나를 다시 그린다. <b>교체 가능 여부와 회복 드래그 가능 여부를 각각 따로 계산해서</b>
        /// 넘긴다 - 두 규칙의 소유자가 다르기 때문이다.
        ///
        /// <code>
        /// 교체 가능   : CharacterRoster.GetSwapBlockReason  (Active/행동력 0/회복 중이면 불가)
        /// 드래그 가능 : RecoveryStation.CanRegister          (Active/최대치/이미 등록됨/빈 슬롯 없음이면 불가)
        /// </code>
        ///
        /// 그래서 행동력 0인 캐릭터는 <b>교체 불가 + 드래그 가능</b>이고, 행동력이 가득 찬 캐릭터는
        /// <b>교체 가능 + 드래그 불가</b>가 된다. 이 둘을 하나의 interactable로 묶지 않는다.
        /// </summary>
        private void RefreshItem(CharacterSwapListItem item)
        {
            CharacterRoster roster = CharacterRoster.Instance;
            if (item == null || roster == null) return;

            CharacterDefinition character = item.BoundCharacter;
            if (character == null) return;

            RecoveryStation station = RecoveryService.Station;
            RecoveryCharacterState recoveryState = station != null
                ? station.GetState(character)
                : RecoveryCharacterState.Available;

            CharacterRoster.SwapBlockReason swapReason = roster.GetSwapBlockReason(character);
            bool canSwap = swapReason == CharacterRoster.SwapBlockReason.None;

            // 회복소가 없는 씬/구성에서는 드래그 기능 자체가 없다.
            bool canDrag = station != null && station.CanRegister(character);

            CharacterSwapListItem.DisplayState state = ResolveDisplayState(swapReason, recoveryState);
            // 상태 문구에는 상태만 담는다 - 남은 시간은 회복소 슬롯의 lb_time이 담당하며, 여기에
            // 섞으면 같은 값을 두 곳에서 서로 다른 주기로 갱신하게 된다.
            item.Refresh(
                roster.GetLevel(character),
                roster.GetStamina(character),
                roster.GetMaxStamina(character),
                state,
                character == pendingCharacter,
                canSwap,
                canDrag);
        }

        /// <summary>표시 상태는 회복 상태를 먼저 본다 - 회복 중/합류 대기는 교체 차단 사유
        /// (InRecovery)와 같은 사실을 가리키지만, 사용자에게는 "왜 못 고르는지"를 정확히 보여줘야 한다.
        /// 1단계에서 임시로 쓰던 Exhausted 매핑을 이 판정이 대체한다.</summary>
        private static CharacterSwapListItem.DisplayState ResolveDisplayState(
            CharacterRoster.SwapBlockReason swapReason, RecoveryCharacterState recoveryState)
        {
            if (recoveryState == RecoveryCharacterState.Recovering)
            {
                return CharacterSwapListItem.DisplayState.Recovering;
            }
            if (recoveryState == RecoveryCharacterState.RecoveryComplete)
            {
                return CharacterSwapListItem.DisplayState.RecoveryComplete;
            }

            switch (swapReason)
            {
                case CharacterRoster.SwapBlockReason.AlreadyCurrent:
                    return CharacterSwapListItem.DisplayState.InUse;
                case CharacterRoster.SwapBlockReason.NoStamina:
                    return CharacterSwapListItem.DisplayState.Exhausted;
                case CharacterRoster.SwapBlockReason.InRecovery:
                    // 로스터는 회복 중이라고 하는데 회복소가 그렇지 않다고 한 경우 - 회복소가 아직
                    // 준비되지 않았을 때뿐이다. 고를 수 없다는 사실만은 정확히 보여준다.
                    return CharacterSwapListItem.DisplayState.Recovering;
                default:
                    return CharacterSwapListItem.DisplayState.Ready;
            }
        }

        private void HandleRecoverySlotsChanged()
        {
            // 등록/취소/시작/합류로 상태가 바뀌면 문구와 드래그 가능 여부가 함께 달라진다.
            RefreshAllItems();
            UpdateSwapButton();
        }

        // ---- 선택 / 교체 ----

        private void HandleItemSelected(CharacterDefinition character)
        {
            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null) return;

            // 교체할 수 없는 캐릭터는 선택 자체를 남기지 않는다 - 선택 표시만 되고 교체 버튼은 계속
            // 꺼져 있는 애매한 상태를 만들지 않기 위함이다.
            if (roster.GetSwapBlockReason(character) != CharacterRoster.SwapBlockReason.None) return;

            pendingCharacter = character;
            RefreshAllItems();
            UpdateSwapButton();
        }

        private void ApplyPendingSwap()
        {
            CharacterRoster roster = CharacterRoster.Instance;
            if (roster == null || pendingCharacter == null) return;

            if (!roster.TrySwitchTo(pendingCharacter, out CharacterRoster.SwapBlockReason reason))
            {
                // 버튼을 누를 수 있었는데 막혔다면 그 사이 상태가 바뀐 것이다 - 리스트를 최신으로
                // 되돌려 사용자가 이유를 볼 수 있게 하고, 패널은 닫지 않는다.
                Debug.LogWarning($"[CharacterSwapPanel] 캐릭터 교체가 취소됐습니다(사유: {reason}).", this);
                pendingCharacter = null;
                RefreshAllItems();
                UpdateSwapButton();
                return;
            }

            Close();
        }

        /// <summary>교체 버튼은 "지금 이 선택으로 교체가 실제로 일어날 수 있을 때"만 켠다.</summary>
        private void UpdateSwapButton()
        {
            if (swapButton == null) return;

            CharacterRoster roster = CharacterRoster.Instance;
            swapButton.interactable = roster != null
                                      && pendingCharacter != null
                                      && roster.GetSwapBlockReason(pendingCharacter) == CharacterRoster.SwapBlockReason.None;
        }

        private void HandleCurrentCharacterChanged(CharacterDefinition character)
        {
            pendingCharacter = null;
            RefreshAllItems();
            UpdateSwapButton();
        }

        /// <summary>행동력이 바뀐 캐릭터의 항목만 다시 그린다(리스트 전체를 새로 만들지 않는다).
        /// 전체 충전처럼 여러 캐릭터가 한 번에 바뀌는 경우에도 캐릭터마다 이 신호가 오므로, 결과적으로
        /// 바뀐 항목이 모두 갱신된다. 교체 버튼은 어떤 캐릭터가 바뀌었든 다시 판정한다 - 선택해 둔
        /// 캐릭터의 교체 가능 여부가 다른 캐릭터의 변화와 무관하다고 단정하지 않기 위함이다.</summary>
        private void HandleCharacterStateChanged(CharacterDefinition character)
        {
            for (int i = 0; i < spawnedItems.Count; i++)
            {
                if (spawnedItems[i] != null && spawnedItems[i].BoundCharacter == character)
                {
                    RefreshItem(spawnedItems[i]);
                }
            }

            UpdateSwapButton();
        }

        // ---- 참조 검증 ----

        /// <summary>리스트 항목을 넣을 부모를 결정한다. 우선순위는 (1) ScrollRect에 연결된 Content,
        /// (2) Inspector에 직접 연결한 Content다 - 어느 쪽도 <b>쓰지 않고 읽기만</b> 하므로 에디터에서
        /// ScrollRect.Content를 바꾸면 다음 갱신부터 그 값이 그대로 반영된다.</summary>
        private RectTransform ResolveItemParent()
        {
            if (characterScrollRect != null && characterScrollRect.content != null) return characterScrollRect.content;
            return content;
        }

        /// <summary>빠진 참조를 자동으로 채우지 않고 무엇이 빠졌는지만 알린다 - 패널을 열 때 한 번만
        /// 검사한다. 예전처럼 런타임에 구조를 만들어 버리면 에디터에서 보이는 계층과 실제 동작이
        /// 달라지기 때문에, 여기서는 진단만 한다.</summary>
        private void ValidateReferences()
        {
            if (referencesValidated) return;
            referencesValidated = true;

            if (characterScrollRect == null)
            {
                Debug.LogError($"[CharacterSwapPanel] '{name}': ScrollRect가 연결되지 않았습니다 - " +
                               "list의 ScrollRect를 Inspector에 연결하세요.", this);
            }
            else if (characterScrollRect.viewport == null || characterScrollRect.content == null)
            {
                Debug.LogWarning("CharacterSwapPanel: ScrollRect Viewport 또는 Content가 설정되지 않았습니다.", this);
            }

            if (ResolveItemParent() == null)
            {
                Debug.LogError($"[CharacterSwapPanel] '{name}': 리스트 항목을 넣을 Content를 찾을 수 없습니다 - " +
                               "ScrollRect의 Content를 지정하거나 Content 필드를 직접 연결하세요.", this);
            }
            if (itemTemplate == null)
            {
                Debug.LogError($"[CharacterSwapPanel] '{name}': 리스트 항목 원본이 연결되지 않았습니다 - " +
                               "Content 아래의 list_Character(CharacterSwapListItem)를 연결하세요.", this);
            }
            if (swapButton == null)
            {
                Debug.LogError($"[CharacterSwapPanel] '{name}': 교체 버튼(btn_swap)이 연결되지 않았습니다.", this);
            }
        }
    }
}
