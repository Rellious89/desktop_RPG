using System;
using System.Collections.Generic;
using System.Globalization;
using Building;
using Common;
using Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization.Tables;

namespace Shop.UI
{
    /// <summary>상점 패널의 구매 화면만 연결한다. 거래와 저장의 권위는 ShopTradeService에 남긴다.</summary>
    [DisallowMultipleComponent]
    public sealed class ShopPanel : ModalPanel
    {
        [Header("Catalogs")]
        [SerializeField] private string shopId = "general_shop";
        [SerializeField] private string requiredBuildingId = "3";
        [SerializeField] private ShopCatalog shopCatalog;
        [SerializeField] private ShopProductCatalog productCatalog;
        [SerializeField] private ItemCatalog itemCatalog;
        [SerializeField] private CurrencyCatalog currencyCatalog;
        [SerializeField] private GameObject itemRowPrefab;
        [SerializeField] private LocalizedTextReference purchaseFailedMessage =
            new LocalizedTextReference(new TableReference("01_UI"), new TableEntryReference("79"));

        private GameObject buyRoot;
        private GameObject sellRoot;
        private GameObject dialog;
        private RectTransform listRoot;
        private TextMeshProUGUI[] currencyTexts;
        private Button swapButton;
        private Button buyCloseButton;
        private Button sellCloseButton;
        private Button cancelButton;
        private Button confirmButton;
        private TextMeshProUGUI dialogCostText;
        private Image dialogCurrencyIcon;
        private readonly List<GameObject> rows = new List<GameObject>();
        private ShopTradeService tradeService;
        private ShopProductDefinition selectedProduct;
        private bool purchasing;
        private bool referencesResolved;
        private bool warnedMissingItem;

        protected override void OnEnable()
        {
            // ModalPanel의 OnEnable 전에 닫아야 잠긴 상태가 한 프레임이라도 표시·거래되지 않는다.
            if (!BuildingCompletionPolicy.IsConfirmedCompleted(SaveSystem.Data, requiredBuildingId, DateTime.UtcNow))
            {
                gameObject.SetActive(false);
                return;
            }
            base.OnEnable();
        }

        protected override void OnModalOpened()
        {
            ResolveReferences();
            SetMode(true);
            CloseDialog();
            InventoryManager.InventoryChanged += RefreshContents;
            BindButtons();
        }

        protected override void OnModalClosed()
        {
            InventoryManager.InventoryChanged -= RefreshContents;
            UnbindButtons();
            CloseDialog();
        }

        protected override void OnCloseRequested() => CloseDialog();

        protected override void RefreshContents()
        {
            if (!BuildingCompletionPolicy.IsConfirmedCompleted(SaveSystem.Data, requiredBuildingId, DateTime.UtcNow))
            {
                Close();
                return;
            }
            ResolveReferences();
            RefreshCurrency();
            RebuildProducts();
        }

        private void ResolveReferences()
        {
            if (referencesResolved) return;
            referencesResolved = true;
            buyRoot = FindDeepChild(transform, "bg_Buy")?.gameObject;
            sellRoot = FindDeepChild(transform, "bg_Sell")?.gameObject;
            dialog = FindDeepChild(transform, "dialog_ItemBuy")?.gameObject;
            listRoot = FindDeepChild(buyRoot != null ? buyRoot.transform : transform, "list") as RectTransform;
            currencyTexts = GetComponentsInChildren<TextMeshProUGUI>(true);
            swapButton = FindDeepChild(transform, "btn_swap")?.GetComponent<Button>();
            buyCloseButton = FindDeepChild(buyRoot != null ? buyRoot.transform : transform, "btn_close")?.GetComponent<Button>();
            sellCloseButton = FindDeepChild(sellRoot != null ? sellRoot.transform : transform, "btn_close")?.GetComponent<Button>();
            cancelButton = FindDeepChild(dialog != null ? dialog.transform : transform, "btn_cancle")?.GetComponent<Button>();
            confirmButton = FindDeepChild(dialog != null ? dialog.transform : transform, "btn_confirm")?.GetComponent<Button>();
            dialogCostText = FindDeepChild(dialog != null ? dialog.transform : transform, "lb_CostValue")?.GetComponent<TextMeshProUGUI>();
            dialogCurrencyIcon = FindFirstImage(dialog != null ? dialog.transform : null, "sp_Currency");
            if (dialog != null) dialog.SetActive(false);
        }

        private void BindButtons()
        {
            Bind(swapButton, ToggleMode);
            Bind(buyCloseButton, Close);
            Bind(sellCloseButton, Close);
            Bind(cancelButton, CloseDialog);
            Bind(confirmButton, ConfirmPurchase);
        }

        private void UnbindButtons()
        {
            Unbind(swapButton, ToggleMode); Unbind(buyCloseButton, Close); Unbind(sellCloseButton, Close);
            Unbind(cancelButton, CloseDialog); Unbind(confirmButton, ConfirmPurchase);
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return; button.onClick.RemoveListener(action); button.onClick.AddListener(action);
        }
        private static void Unbind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.RemoveListener(action);
        }

        private void ToggleMode() => SetMode(sellRoot == null || !sellRoot.activeSelf);

        /// <summary>후속 연출도 이 한 경로에 끼워 넣는다. 현재는 즉시 전환만 한다.</summary>
        private void SetMode(bool buy)
        {
            if (buyRoot != null) buyRoot.SetActive(buy);
            if (sellRoot != null) sellRoot.SetActive(!buy);
            CloseDialog();
        }

        private void RefreshCurrency()
        {
            int amount = SaveSystem.Data != null ? SaveSystem.Data.currency : 0;
            foreach (TextMeshProUGUI text in currencyTexts)
                if (text != null && text.name == "lb_currency") text.text = amount.ToString("N0", CultureInfo.InvariantCulture);
        }

        private void RebuildProducts()
        {
            for (int i = 0; i < rows.Count; i++) if (rows[i] != null) Destroy(rows[i]);
            rows.Clear();
            if (listRoot == null || itemRowPrefab == null || productCatalog == null || itemCatalog == null) return;
            IReadOnlyList<ShopProductDefinition> products = productCatalog.GetActiveProducts(shopId);
            for (int i = 0; i < products.Count; i++)
            {
                ShopProductDefinition product = products[i];
                ItemDefinition item = product != null ? itemCatalog.Find(product.ItemId) : null;
                if (item == null || !item.IsValid)
                {
                    if (!warnedMissingItem) { warnedMissingItem = true; Debug.LogWarning("[ShopPanel] 상품의 ItemDefinition을 찾지 못해 해당 행을 생략합니다.", this); }
                    continue;
                }
                GameObject row = Instantiate(itemRowPrefab, listRoot);
                row.name = itemRowPrefab.name + "_Runtime_" + item.ItemId;
                BindRow(row, product, item);
                rows.Add(row);
            }
        }

        private void BindRow(GameObject row, ShopProductDefinition product, ItemDefinition item)
        {
            TextMeshProUGUI name = FindDeepChild(row.transform, "lb_ItemName")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI cost = FindDeepChild(row.transform, "lb_CostValue")?.GetComponent<TextMeshProUGUI>();
            if (name != null) name.text = item.HasLocalizedName ? item.LocalizedName.GetLocalizedString() : item.DisplayName;
            if (cost != null) cost.text = product.BuyPrice.ToString("N0", CultureInfo.InvariantCulture);
            Image icon = FindFirstImage(row.transform, "sp_ItemIcon"); if (icon != null) icon.sprite = item.Icon;
            Image currencyIcon = FindFirstImage(row.transform, "sp_Currency"); CurrencyDefinition currency = currencyCatalog != null ? currencyCatalog.Find(product.BuyCurrencyId) : null;
            if (currencyIcon != null && currency != null) currencyIcon.sprite = currency.Icon;
            Button button = row.GetComponent<Button>(); if (button != null) button.onClick.AddListener(() => OpenPurchase(product));
        }

        private void OpenPurchase(ShopProductDefinition product)
        {
            if (product == null || dialog == null) return;
            selectedProduct = product;
            if (dialogCostText != null) dialogCostText.text = product.BuyPrice.ToString("N0", CultureInfo.InvariantCulture);
            CurrencyDefinition currency = currencyCatalog != null ? currencyCatalog.Find(product.BuyCurrencyId) : null;
            if (dialogCurrencyIcon != null && currency != null) dialogCurrencyIcon.sprite = currency.Icon;
            dialog.SetActive(true);
        }

        private void CloseDialog()
        {
            selectedProduct = null;
            if (dialog != null && dialog.activeSelf) dialog.SetActive(false);
        }

        private void ConfirmPurchase()
        {
            if (purchasing || selectedProduct == null) return;
            EnsureService(); if (tradeService == null) return;
            purchasing = true;
            try
            {
                ShopTradeResult result = tradeService.TryBuy(shopId, selectedProduct.ItemId, 1);
                if (result.Success) { CloseDialog(); RefreshContents(); return; }
                if (result.Code == ShopTradeResultCode.ShopLocked || result.Code == ShopTradeResultCode.UnknownShop) { Close(); return; }
                ToastManager.Instance?.Show(purchaseFailedMessage.GetLocalizedString());
            }
            finally { purchasing = false; }
        }

        private void EnsureService()
        {
            if (tradeService != null) return;
            InventoryManager inventory = InventoryManager.Instance;
            if (inventory != null) tradeService = new ShopTradeService(() => SaveSystem.Data, SaveSystem.Save,
                () => DateTime.UtcNow, shopCatalog, productCatalog, itemCatalog, inventory);
        }

        private static Image FindFirstImage(Transform root, string name)
        {
            Transform node = root != null ? FindDeepChild(root, name) : null;
            return node != null ? node.GetComponent<Image>() : null;
        }
    }
}
