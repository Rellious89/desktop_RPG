using System.Collections.Generic;
using System.Globalization;
using Inventory;
using TMPro;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 인벤토리 패널(pn_Inventory). 씬 시작 시에는 비활성이며 ControlDock의
    /// <see cref="ModalPanelOpener"/>가 켠다. 열고 닫기, 배경 입력 차단, Windows 클릭 관통 예외는
    /// <see cref="ModalPanel"/>이 캐릭터 교체 패널과 <b>같은 방식</b>으로 처리하고, 이 클래스는
    /// 재화와 아이템 목록을 그리는 일만 한다.
    ///
    /// <b>슬롯은 새로 만들지 않는다.</b> pn_Inventory에 미리 배치된 list_item 슬롯을 그대로 쓰고,
    /// 앞에서부터 보유 아이템을 채운 뒤 남는 칸은 빈 슬롯으로 비운다 - 런타임 복제본이 없으므로
    /// 목록을 몇 번 갱신해도 슬롯이 늘어나거나 중복되지 않는다. 슬롯 확장과 페이지는 이번 범위가
    /// 아니므로, 보유 종류가 배치된 슬롯 수보다 많으면 넘치는 만큼은 표시하지 않고 경고만 남긴다.
    ///
    /// <b>재화는 아이템 목록과 별개다.</b> 아이템 슬롯이 아니라 하단 재화 영역(lb_currency)에만
    /// 표시하고, 경험치/레벨/행동력과는 아무 관계가 없다. 자릿수 구분 쉼표는 실행 환경의 지역
    /// 설정과 무관하게 항상 같은 모양이 되도록 InvariantCulture로 포맷한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryPanel : ModalPanel
    {
        private const string ListRootName = "list";
        private const string CurrencyTextName = "lb_currency";

        [Header("References (비워두면 이름으로 자동 탐색)")]
        [Tooltip("슬롯이 배치된 영역(list). 이 아래의 InventorySlotView를 순서대로 사용한다.")]
        [SerializeField] private RectTransform listRoot;

        [Tooltip("보유 재화를 표시할 텍스트(lb_currency).")]
        [SerializeField] private TextMeshProUGUI currencyText;

        [Tooltip("재화 표시 형식. {0}에 세 자리마다 쉼표가 찍힌 숫자가 들어간다.")]
        [SerializeField] private string currencyFormat = "{0}";

        private InventorySlotView[] slots;
        private bool referencesResolved;
        private bool overflowWarned;

        private void Awake()
        {
            ResolveReferences();
        }

        protected override void OnModalOpened()
        {
            ResolveReferences();

            // 패널이 열려 있는 동안 개발용 진입점으로 값을 바꾸면 그 자리에서 반영된다.
            InventoryManager.InventoryChanged += RefreshContents;
        }

        protected override void OnModalClosed()
        {
            InventoryManager.InventoryChanged -= RefreshContents;
        }

        /// <summary>재화와 아이템 슬롯을 지금 저장 데이터 기준으로 다시 그린다. 패널을 열 때마다,
        /// 그리고 인벤토리가 바뀔 때마다 호출된다. 표시만 하고 데이터는 건드리지 않는다 - 패널을
        /// 닫아도 인벤토리 값이 달라지지 않는 이유가 이것이다.</summary>
        protected override void RefreshContents()
        {
            ResolveReferences();

            InventoryManager inventory = InventoryManager.Instance;
            if (inventory == null)
            {
                Debug.LogError("[InventoryPanel] 씬에 InventoryManager가 없어 인벤토리를 표시할 수 없습니다.", this);
                return;
            }

            RefreshCurrency(inventory);
            RefreshSlots(inventory);
        }

        private void RefreshCurrency(InventoryManager inventory)
        {
            if (currencyText == null) return;

            // "N0" + InvariantCulture: 1000 -> "1,000", 12345 -> "12,345". 실행 환경의 지역 설정이
            // 점(.)이나 공백을 쓰더라도 항상 쉼표로 고정된다.
            string formattedAmount = inventory.Currency.ToString("N0", CultureInfo.InvariantCulture);
            currencyText.text = string.Format(currencyFormat, formattedAmount);
        }

        private void RefreshSlots(InventoryManager inventory)
        {
            if (slots == null || slots.Length == 0) return;

            IReadOnlyList<InventoryManager.Entry> items = inventory.Items;

            for (int i = 0; i < slots.Length; i++)
            {
                InventorySlotView slot = slots[i];
                if (slot == null) continue;

                if (i < items.Count)
                {
                    InventoryManager.Entry entry = items[i];
                    slot.SetItem(entry.Definition.Icon, entry.Count);
                }
                else
                {
                    slot.SetEmpty();
                }
            }

            if (items.Count > slots.Length && !overflowWarned)
            {
                overflowWarned = true;
                Debug.LogWarning($"[InventoryPanel] 보유 아이템 종류({items.Count})가 배치된 슬롯 수" +
                                 $"({slots.Length})보다 많아 뒤쪽 아이템은 표시되지 않습니다 - 슬롯 확장은 " +
                                 "이번 범위가 아닙니다.", this);
            }
        }

        private void ResolveReferences()
        {
            if (referencesResolved) return;
            referencesResolved = true;

            if (listRoot == null) listRoot = FindDeepChild(transform, ListRootName) as RectTransform;
            if (currencyText == null) currencyText = FindChildComponent<TextMeshProUGUI>(CurrencyTextName);

            // 슬롯 순서는 계층 순서를 그대로 따른다 - Grid Layout Group이 화면에 배치하는 순서와 같다.
            slots = listRoot != null
                ? listRoot.GetComponentsInChildren<InventorySlotView>(true)
                : System.Array.Empty<InventorySlotView>();

            if (listRoot == null)
            {
                Debug.LogError($"[InventoryPanel] '{name}': 슬롯 영역('{ListRootName}')을 찾지 못했습니다.", this);
            }
            else if (slots.Length == 0)
            {
                Debug.LogError($"[InventoryPanel] '{name}': '{ListRootName}' 아래에 InventorySlotView가 하나도 " +
                               "없습니다 - list_item 프리팹에 InventorySlotView를 추가하세요.", this);
            }
            if (currencyText == null)
            {
                Debug.LogWarning($"[InventoryPanel] '{name}': 재화 텍스트('{CurrencyTextName}')를 찾지 못해 " +
                                 "보유 재화가 표시되지 않습니다.", this);
            }
        }
    }
}
