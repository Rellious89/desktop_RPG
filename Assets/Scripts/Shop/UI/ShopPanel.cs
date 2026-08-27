using System;
using System.Collections.Generic;
using System.Globalization;
using Building;
using Common;
using Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Shop.UI
{
    /// <summary>상점의 구매·임시 판매 화면을 연결한다. 거래와 저장의 권위는 ShopTradeService에 남긴다.</summary>
    [DisallowMultipleComponent]
    public sealed class ShopPanel : ModalPanel, IInventoryItemRegistrationTarget
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
            new LocalizedTextReference("GUID:32fd067a20b754a50b20446b9c78d2ae", "79");

        private GameObject buyRoot;
        private GameObject sellRoot;
        private GameObject purchaseDialog;
        private GameObject sellDialog;
        private RectTransform buyListRoot;
        private RectTransform sellListRoot;
        private TextMeshProUGUI[] currencyTexts;
        private Button swapButton;
        private Button buyCloseButton;
        private Button sellCloseButton;
        private Button purchaseCancelButton;
        private Button purchaseConfirmButton;
        private Button sellButton;
        private Button sellCancelButton;
        private Button sellConfirmButton;
        private TextMeshProUGUI purchaseCostText;
        private Image purchaseCurrencyIcon;
        private TextMeshProUGUI sellItemsText;
        private TextMeshProUGUI sellCostText;
        private Image sellCurrencyIcon;
        private GameObject sellEmptyMessage;
        private readonly List<GameObject> buyRows = new List<GameObject>();
        private readonly List<GameObject> sellRows = new List<GameObject>();
        private ShopTradeService tradeService;
        private ShopSellSession sellSession;
        private InventoryManager sellSessionInventory;
        private ShopProductDefinition selectedProduct;
        private ShopSellLine[] sellConfirmationSnapshot;
        private bool purchasing;
        private bool selling;
        private bool referencesResolved;
        private bool warnedMissingItem;
        private GameObject sellDialogBlocker;

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
            CloseDialogs();
            InventoryManager.InventoryChanged += RefreshContents;
            BindButtons();
        }

        protected override void OnModalClosed()
        {
            InventoryManager.InventoryChanged -= RefreshContents;
            UnbindButtons();
            ResetSellSession();
            CloseDialogs();
        }

        protected override void OnCloseRequested()
        {
            ResetSellSession();
            CloseDialogs();
        }

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
            if (sellRoot != null && sellRoot.activeSelf) RefreshSellContents();
        }

        private void ResolveReferences()
        {
            if (referencesResolved) return;
            referencesResolved = true;
            buyRoot = FindDeepChild(transform, "bg_Buy")?.gameObject;
            sellRoot = FindDeepChild(transform, "bg_Sell")?.gameObject;
            purchaseDialog = FindDeepChild(transform, "dialog_ItemBuy")?.gameObject;
            sellDialog = FindDeepChild(transform, "dialog_ItemSell")?.gameObject;
            buyListRoot = FindDeepChild(buyRoot != null ? buyRoot.transform : transform, "list") as RectTransform;
            sellListRoot = FindDeepChild(sellRoot != null ? sellRoot.transform : transform, "list") as RectTransform;
            currencyTexts = GetComponentsInChildren<TextMeshProUGUI>(true);
            swapButton = FindDeepChild(transform, "btn_swap")?.GetComponent<Button>();
            buyCloseButton = FindDeepChild(buyRoot != null ? buyRoot.transform : transform, "btn_close")?.GetComponent<Button>();
            sellCloseButton = FindDeepChild(sellRoot != null ? sellRoot.transform : transform, "btn_close")?.GetComponent<Button>();
            purchaseCancelButton = FindDeepChild(purchaseDialog != null ? purchaseDialog.transform : transform, "btn_cancle")?.GetComponent<Button>();
            purchaseConfirmButton = FindDeepChild(purchaseDialog != null ? purchaseDialog.transform : transform, "btn_confirm")?.GetComponent<Button>();
            sellButton = FindDeepChild(sellRoot != null ? sellRoot.transform : transform, "btn_sell")?.GetComponent<Button>();
            sellCancelButton = FindDeepChild(sellDialog != null ? sellDialog.transform : transform, "btn_cancle")?.GetComponent<Button>();
            sellConfirmButton = FindDeepChild(sellDialog != null ? sellDialog.transform : transform, "btn_confirm")?.GetComponent<Button>();
            purchaseCostText = FindDeepChild(purchaseDialog != null ? purchaseDialog.transform : transform, "lb_CostValue")?.GetComponent<TextMeshProUGUI>();
            purchaseCurrencyIcon = FindCurrencyImage(purchaseDialog != null ? purchaseDialog.transform : null);
            sellItemsText = FindDeepChild(sellDialog != null ? sellDialog.transform : transform, "lb_Sell")?.GetComponent<TextMeshProUGUI>();
            sellCostText = FindDeepChild(sellDialog != null ? sellDialog.transform : transform, "lb_CostValue")?.GetComponent<TextMeshProUGUI>();
            sellCurrencyIcon = FindCurrencyImage(sellDialog != null ? sellDialog.transform : null);
            sellEmptyMessage = FindDeepChild(sellRoot != null ? sellRoot.transform : transform, "lb_emptyMsg")?.gameObject;
            if (purchaseDialog != null) purchaseDialog.SetActive(false);
            if (sellDialog != null) sellDialog.SetActive(false);
        }

        private void BindButtons()
        {
            Bind(swapButton, ToggleMode);
            Bind(buyCloseButton, Close);
            Bind(sellCloseButton, Close);
            Bind(purchaseCancelButton, ClosePurchaseDialog);
            Bind(purchaseConfirmButton, ConfirmPurchase);
            Bind(sellButton, OpenSellConfirmation);
            Bind(sellCancelButton, CloseSellDialog);
            Bind(sellConfirmButton, ConfirmSell);
        }

        private void UnbindButtons()
        {
            Unbind(swapButton, ToggleMode); Unbind(buyCloseButton, Close); Unbind(sellCloseButton, Close);
            Unbind(purchaseCancelButton, ClosePurchaseDialog); Unbind(purchaseConfirmButton, ConfirmPurchase);
            Unbind(sellButton, OpenSellConfirmation); Unbind(sellCancelButton, CloseSellDialog); Unbind(sellConfirmButton, ConfirmSell);
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
            CloseDialogs();
            if (buy)
            {
                InventoryItemRegistrationContext.ClearActiveTarget(this);
                ResetSellSession();
            }
            else
            {
                EnsureSellSession();
                InventoryItemRegistrationContext.SetActiveTarget(this);
                RefreshSellContents();
            }
        }

        private void RefreshCurrency()
        {
            int amount = SaveSystem.Data != null ? SaveSystem.Data.currency : 0;
            foreach (TextMeshProUGUI text in currencyTexts)
                if (text != null && text.name == "lb_currency") text.text = amount.ToString("N0", CultureInfo.InvariantCulture);
        }

        private void RebuildProducts()
        {
            for (int i = 0; i < buyRows.Count; i++) if (buyRows[i] != null) Destroy(buyRows[i]);
            buyRows.Clear();
            if (buyListRoot == null || itemRowPrefab == null || productCatalog == null || itemCatalog == null) return;
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
                GameObject row = Instantiate(itemRowPrefab, buyListRoot);
                row.name = itemRowPrefab.name + "_Runtime_" + item.ItemId;
                BindRow(row, product, item);
                buyRows.Add(row);
            }
        }

        private void BindRow(GameObject row, ShopProductDefinition product, ItemDefinition item)
        {
            TextMeshProUGUI name = FindDeepChild(row.transform, "lb_ItemName")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI cost = FindDeepChild(row.transform, "lb_CostValue")?.GetComponent<TextMeshProUGUI>();
            if (name != null) name.text = item.HasLocalizedName ? item.LocalizedName.GetLocalizedString() : item.DisplayName;
            if (cost != null) cost.text = product.BuyPrice.ToString("N0", CultureInfo.InvariantCulture);
            Image icon = FindFirstImage(row.transform, "sp_ShopItemIcon", "sp_ItemIcon"); if (icon != null) icon.sprite = item.Icon;
            Image currencyIcon = FindCurrencyImage(row.transform); CurrencyDefinition currency = currencyCatalog != null ? currencyCatalog.Find(product.BuyCurrencyId) : null;
            if (currencyIcon != null && currency != null) currencyIcon.sprite = currency.Icon;
            Button button = row.GetComponent<Button>(); if (button != null) button.onClick.AddListener(() => OpenPurchase(product));
        }

        private void OpenPurchase(ShopProductDefinition product)
        {
            if (product == null || purchaseDialog == null) return;
            selectedProduct = product;
            if (purchaseCostText != null) purchaseCostText.text = product.BuyPrice.ToString("N0", CultureInfo.InvariantCulture);
            CurrencyDefinition currency = currencyCatalog != null ? currencyCatalog.Find(product.BuyCurrencyId) : null;
            if (purchaseCurrencyIcon != null && currency != null) purchaseCurrencyIcon.sprite = currency.Icon;
            purchaseDialog.SetActive(true);
        }

        private void ClosePurchaseDialog()
        {
            selectedProduct = null;
            if (purchaseDialog != null && purchaseDialog.activeSelf) purchaseDialog.SetActive(false);
        }

        private void CloseDialogs()
        {
            ClosePurchaseDialog();
            CloseSellDialog();
        }

        private void ConfirmPurchase()
        {
            if (purchasing || selectedProduct == null) return;
            EnsureService(); if (tradeService == null) return;
            purchasing = true;
            try
            {
                ShopTradeResult result = tradeService.TryBuy(shopId, selectedProduct.ItemId, 1);
                if (result.Success) { ClosePurchaseDialog(); RefreshContents(); return; }
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

        private void EnsureSellSession()
        {
            InventoryManager inventory = InventoryManager.Instance;
            if (sellSession != null && sellSessionInventory == inventory) return;
            sellSession = new ShopSellSession(itemCatalog, inventory);
            sellSessionInventory = inventory;
        }

        private void RefreshSellContents()
        {
            EnsureSellSession();
            if (sellSession == null) return;
            bool sessionChanged = sellSession.Revalidate();
            // 확인창이 띄운 스냅샷과 현재 세션이 달라지면 그 스냅샷을 거래에 쓰지 않는다.
            if (sessionChanged && sellDialog != null && sellDialog.activeSelf) CloseSellDialog();

            for (int i = 0; i < sellRows.Count; i++) if (sellRows[i] != null) Destroy(sellRows[i]);
            sellRows.Clear();

            IReadOnlyList<ShopSellSession.Entry> entries = sellSession.Entries;
            if (sellListRoot != null && itemRowPrefab != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    ShopSellSession.Entry entry = entries[i];
                    GameObject row = Instantiate(itemRowPrefab, sellListRoot);
                    row.name = itemRowPrefab.name + "_Sell_" + entry.Item.ItemId;
                    BindSellRow(row, entry);
                    sellRows.Add(row);
                }
            }

            bool hasEntries = entries.Count > 0;
            if (sellEmptyMessage != null) sellEmptyMessage.SetActive(!hasEntries);
            if (sellButton != null) sellButton.interactable = hasEntries;
        }

        private void BindSellRow(GameObject row, ShopSellSession.Entry entry)
        {
            TextMeshProUGUI name = FindDeepChild(row.transform, "lb_ItemName")?.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI cost = FindDeepChild(row.transform, "lb_CostValue")?.GetComponent<TextMeshProUGUI>();
            if (name != null) name.text = entry.Item.HasLocalizedName ? entry.Item.LocalizedName.GetLocalizedString() : entry.Item.DisplayName;
            if (cost != null) cost.text = entry.UnitPrice.ToString("N0", CultureInfo.InvariantCulture);
            Image icon = FindFirstImage(row.transform, "sp_ShopItemIcon", "sp_ItemIcon"); if (icon != null) icon.sprite = entry.Item.Icon;
            Image currencyIcon = FindCurrencyImage(row.transform);
            CurrencyDefinition currency = currencyCatalog != null ? currencyCatalog.Find(entry.Item.SellCurrencyId) : null;
            if (currencyIcon != null && currency != null) currencyIcon.sprite = currency.Icon;

            Button button = row.GetComponent<Button>();
            if (button != null) button.interactable = false;
            ShopSellEntryView view = row.GetComponent<ShopSellEntryView>();
            if (view == null) view = row.AddComponent<ShopSellEntryView>();
            view.Bind(entry.Item.ItemId, sellListRoot, RemoveSellEntry);
        }

        private void RemoveSellEntry(string itemId)
        {
            if (sellSession != null && sellSession.Remove(itemId)) RefreshSellContents();
        }

        private void OpenSellConfirmation()
        {
            if (selling || sellDialog == null) return;
            EnsureSellSession();
            if (sellSession == null || !sellSession.TryCreateSnapshot(out ShopSellLine[] snapshot, out int totalPrice))
            {
                RefreshSellContents();
                return;
            }

            sellConfirmationSnapshot = snapshot;
            if (sellItemsText != null) sellItemsText.text = BuildSellSnapshotLabel();
            if (sellCostText != null) sellCostText.text = totalPrice.ToString("N0", CultureInfo.InvariantCulture);
            string currencyId = sellSession.Entries.Count > 0 ? sellSession.Entries[0].Item.SellCurrencyId : string.Empty;
            CurrencyDefinition currency = currencyCatalog != null ? currencyCatalog.Find(currencyId) : null;
            if (sellCurrencyIcon != null && currency != null) sellCurrencyIcon.sprite = currency.Icon;
            SetSellDialogBlocker(true);
            sellDialog.SetActive(true);
            sellDialog.transform.SetAsLastSibling();
        }

        private void CloseSellDialog()
        {
            sellConfirmationSnapshot = null;
            if (sellDialog != null && sellDialog.activeSelf) sellDialog.SetActive(false);
            SetSellDialogBlocker(false);
        }

        private string BuildSellSnapshotLabel()
        {
            if (sellSession == null || sellSession.Entries.Count == 0) return string.Empty;
            var names = new List<string>(sellSession.Entries.Count);
            for (int i = 0; i < sellSession.Entries.Count; i++)
            {
                ItemDefinition item = sellSession.Entries[i].Item;
                names.Add(item.HasLocalizedName ? item.LocalizedName.GetLocalizedString() : item.DisplayName);
            }
            return string.Join(", ", names);
        }

        private void ConfirmSell()
        {
            if (selling || sellConfirmationSnapshot == null || sellConfirmationSnapshot.Length == 0) return;
            EnsureService();
            if (tradeService == null) return;

            ShopSellLine[] snapshot = sellConfirmationSnapshot;
            selling = true;
            try
            {
                ShopSellBatchResult result = tradeService.TrySellBatch(shopId, snapshot);
                if (result.Success)
                {
                    CloseSellDialog();
                    sellSession?.Clear();
                    RefreshContents();
                    return;
                }

                CloseSellDialog();
                if (result.Code == ShopTradeResultCode.ShopLocked || result.Code == ShopTradeResultCode.UnknownShop)
                {
                    Close();
                    return;
                }
                sellSession?.Revalidate();
                RefreshSellContents();
                ToastManager.Instance?.Show(purchaseFailedMessage.GetLocalizedString());
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                CloseSellDialog();
                sellSession?.Revalidate();
                RefreshSellContents();
                ToastManager.Instance?.Show(purchaseFailedMessage.GetLocalizedString());
            }
            finally
            {
                selling = false;
            }
        }

        private void ResetSellSession()
        {
            InventoryItemRegistrationContext.ClearActiveTarget(this);
            sellSession?.Clear();
            for (int i = 0; i < sellRows.Count; i++) if (sellRows[i] != null) Destroy(sellRows[i]);
            sellRows.Clear();
            if (sellEmptyMessage != null) sellEmptyMessage.SetActive(true);
            if (sellButton != null) sellButton.interactable = false;
        }

        public bool CanRegisterInventoryItem(ItemDefinition item)
        {
            return isActiveAndEnabled && sellRoot != null && sellRoot.activeSelf &&
                (sellDialog == null || !sellDialog.activeSelf) && sellSession != null && sellSession.CanAdd(item);
        }

        public bool IsInventoryItemRegistrationDrop(Vector2 screenPosition, Camera eventCamera)
        {
            return isActiveAndEnabled && sellRoot != null && sellRoot.activeSelf && sellListRoot != null &&
                (sellDialog == null || !sellDialog.activeSelf) &&
                RectTransformUtility.RectangleContainsScreenPoint(sellListRoot, screenPosition, eventCamera);
        }

        public void RegisterInventoryItem(ItemDefinition item)
        {
            if (CanRegisterInventoryItem(item) && sellSession.TryAdd(item)) RefreshSellContents();
        }

        private void SetSellDialogBlocker(bool active)
        {
            if (sellDialog == null) return;
            if (active && sellDialogBlocker == null)
            {
                Transform parent = sellDialog.transform.parent;
                if (parent == null) return;
                sellDialogBlocker = new GameObject("SellDialogInputBlocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                sellDialogBlocker.layer = gameObject.layer;
                RectTransform rect = sellDialogBlocker.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                Stretch(rect);
                Image image = sellDialogBlocker.GetComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0.45f);
                image.raycastTarget = true;
            }
            if (sellDialogBlocker == null) return;
            if (active)
            {
                sellDialogBlocker.SetActive(true);
                sellDialogBlocker.transform.SetSiblingIndex(sellDialog.transform.GetSiblingIndex());
            }
            else sellDialogBlocker.SetActive(false);
        }

        private static Image FindCurrencyImage(Transform root)
        {
            return FindFirstImage(root, "sp_currencyIcon", "sp_Currency");
        }

        private static Image FindFirstImage(Transform root, params string[] names)
        {
            if (root == null || names == null) return null;
            for (int i = 0; i < names.Length; i++)
            {
                Transform node = FindDeepChild(root, names[i]);
                if (node != null && node.TryGetComponent(out Image image)) return image;
            }
            return null;
        }
    }
}
