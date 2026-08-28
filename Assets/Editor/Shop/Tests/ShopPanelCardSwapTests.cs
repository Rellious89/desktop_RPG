using System.Reflection;
using NUnit.Framework;
using Shop.UI;
using UnityEngine;
using UnityEditor;

namespace ShopEditor.Tests
{
    /// <summary>카드 연출이 닫힘/중단 뒤에도 프리팹 기준 자세로 돌아가는 계약을 고정한다.</summary>
    public sealed class ShopPanelCardSwapTests
    {
        private static readonly BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject host;
        private GameObject buy;
        private GameObject sell;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("ShopPanelCardSwapTests");
            host.SetActive(false);
            buy = new GameObject("bg_Buy", typeof(RectTransform));
            sell = new GameObject("bg_Sell", typeof(RectTransform));
            buy.transform.SetParent(host.transform, false);
            sell.transform.SetParent(host.transform, false);
            buy.GetComponent<RectTransform>().anchoredPosition = new Vector2(12f, 8f);
            buy.GetComponent<RectTransform>().sizeDelta = new Vector2(170f, 48f);
            sell.GetComponent<RectTransform>().anchoredPosition = new Vector2(-6f, 4f);
            sell.GetComponent<RectTransform>().sizeDelta = new Vector2(170f, 48f);
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
        }

        [Test]
        public void InterruptedSwap_RestoresBuyCardBaselineAndDisablesSellInput()
        {
            ShopPanel panel = host.AddComponent<ShopPanel>();
            Set(panel, "buyRoot", buy);
            Set(panel, "sellRoot", sell);
            Invoke(panel, "CaptureCardBaselines");

            RectTransform buyRect = buy.GetComponent<RectTransform>();
            RectTransform sellRect = sell.GetComponent<RectTransform>();
            buyRect.anchoredPosition = new Vector2(999f, 999f);
            buyRect.sizeDelta = Vector2.one;
            buyRect.localScale = Vector3.one * 0.2f;
            buy.GetComponent<CanvasGroup>().alpha = 0.1f;
            sell.SetActive(true);
            sell.GetComponent<CanvasGroup>().blocksRaycasts = true;

            Invoke(panel, "StopSwapAndRestoreBuy");

            Assert.IsTrue(buy.activeSelf);
            Assert.IsFalse(sell.activeSelf);
            Assert.AreEqual(new Vector2(12f, 8f), buyRect.anchoredPosition);
            Assert.AreEqual(new Vector2(170f, 48f), buyRect.sizeDelta);
            Assert.AreEqual(Vector3.one, buyRect.localScale);
            Assert.AreEqual(1f, buy.GetComponent<CanvasGroup>().alpha);
            Assert.IsTrue(buy.GetComponent<CanvasGroup>().blocksRaycasts);
            Assert.IsFalse(sell.GetComponent<CanvasGroup>().blocksRaycasts);
        }

        [Test]
        public void InvalidDurationOrDistance_UsesImmediateSwapPath()
        {
            ShopPanel panel = host.AddComponent<ShopPanel>();
            Set(panel, "buyRoot", buy);
            Set(panel, "sellRoot", sell);
            Invoke(panel, "CaptureCardBaselines");

            Set(panel, "swapDuration", 0f);
            Set(panel, "swapHorizontalDistance", 150f);
            Assert.IsFalse((bool)Invoke(panel, "CanAnimateSwap"));

            Set(panel, "swapDuration", 0.22f);
            Set(panel, "swapHorizontalDistance", -1f);
            Assert.IsFalse((bool)Invoke(panel, "CanAnimateSwap"));
        }

        [Test]
        public void DisablePreparation_RestoresVisualsWithoutChangingHierarchyState()
        {
            ShopPanel panel = host.AddComponent<ShopPanel>();
            Set(panel, "buyRoot", buy);
            Set(panel, "sellRoot", sell);
            Invoke(panel, "CaptureCardBaselines");

            buy.SetActive(false);
            sell.SetActive(true);
            buy.transform.SetAsFirstSibling();
            sell.transform.SetAsLastSibling();
            int buySibling = buy.transform.GetSiblingIndex();
            int sellSibling = sell.transform.GetSiblingIndex();

            buy.GetComponent<RectTransform>().anchoredPosition = new Vector2(999f, 999f);
            sell.GetComponent<RectTransform>().anchoredPosition = new Vector2(-999f, -999f);

            Invoke(panel, "StopSwapBeforeDisable");

            Assert.IsFalse(buy.activeSelf, "부모 비활성화 중에는 구매 카드 활성 상태를 바꾸지 않아야 한다.");
            Assert.IsTrue(sell.activeSelf, "부모 비활성화 중에는 판매 카드 활성 상태를 바꾸지 않아야 한다.");
            Assert.AreEqual(buySibling, buy.transform.GetSiblingIndex());
            Assert.AreEqual(sellSibling, sell.transform.GetSiblingIndex());
            Assert.AreEqual(new Vector2(12f, 8f), buy.GetComponent<RectTransform>().anchoredPosition);
            Assert.AreEqual(new Vector2(-6f, 4f), sell.GetComponent<RectTransform>().anchoredPosition);
        }

        [Test]
        public void CardOrderSwap_LeavesDialogsAndOtherSiblingSlotsUntouched()
        {
            GameObject spacer = new GameObject("spacer", typeof(RectTransform));
            GameObject purchaseDialog = new GameObject("dialog_ItemBuy", typeof(RectTransform));
            GameObject sellDialog = new GameObject("dialog_ItemSell", typeof(RectTransform));
            spacer.transform.SetParent(host.transform, false);
            purchaseDialog.transform.SetParent(host.transform, false);
            sellDialog.transform.SetParent(host.transform, false);

            ShopPanel panel = host.AddComponent<ShopPanel>();
            Set(panel, "buyRoot", buy);
            Set(panel, "sellRoot", sell);
            Invoke(panel, "CaptureCardBaselines");
            int spacerIndex = spacer.transform.GetSiblingIndex();
            int purchaseIndex = purchaseDialog.transform.GetSiblingIndex();
            int sellIndex = sellDialog.transform.GetSiblingIndex();

            object buyBaseline = Get(panel, "buyBaseline");
            object sellBaseline = Get(panel, "sellBaseline");
            Invoke(panel, "SetCardOrder", buyBaseline, sellBaseline);

            Assert.AreEqual(spacerIndex, spacer.transform.GetSiblingIndex());
            Assert.AreEqual(purchaseIndex, purchaseDialog.transform.GetSiblingIndex());
            Assert.AreEqual(sellIndex, sellDialog.transform.GetSiblingIndex());
            Assert.AreEqual(0, buy.transform.GetSiblingIndex());
            Assert.AreEqual(1, sell.transform.GetSiblingIndex());
        }

        [Test]
        public void ResolveReferences_PrefersDirectDialogsOverDuplicateNestedDialogs()
        {
            GameObject directPurchase = new GameObject("dialog_ItemBuy", typeof(RectTransform));
            GameObject directSell = new GameObject("dialog_ItemSell", typeof(RectTransform));
            GameObject duplicateContainer = new GameObject("duplicate", typeof(RectTransform));
            GameObject duplicatePurchase = new GameObject("dialog_ItemBuy", typeof(RectTransform));
            GameObject duplicateSell = new GameObject("dialog_ItemSell", typeof(RectTransform));
            directPurchase.transform.SetParent(host.transform, false);
            directSell.transform.SetParent(host.transform, false);
            duplicateContainer.transform.SetParent(host.transform, false);
            duplicatePurchase.transform.SetParent(duplicateContainer.transform, false);
            duplicateSell.transform.SetParent(duplicateContainer.transform, false);

            ShopPanel panel = host.AddComponent<ShopPanel>();
            Invoke(panel, "ResolveReferences");

            Assert.AreSame(directPurchase.transform, Get(panel, "purchaseDialog"));
            Assert.AreSame(directSell.transform, Get(panel, "sellDialog"));
        }

        [Test]
        public void SwapLabelReferences_UseSellSwitchInBuyModeAndBuySwitchInSellMode()
        {
            ShopPanel panel = host.AddComponent<ShopPanel>();
            SerializedObject serialized = new SerializedObject(panel);
            Assert.AreEqual("74", serialized.FindProperty("switchToSellText.m_TableEntryReference.m_Key").stringValue);
            Assert.AreEqual("75", serialized.FindProperty("switchToBuyText.m_TableEntryReference.m_Key").stringValue);

            Set(panel, "isShowingBuy", true);
            Assert.AreSame(Get(panel, "switchToSellText"), Invoke(panel, "GetCurrentSwapTextReference"));
            Set(panel, "isShowingBuy", false);
            Assert.AreSame(Get(panel, "switchToBuyText"), Invoke(panel, "GetCurrentSwapTextReference"));
        }

        [Test]
        public void Dialogs_OpenAboveCardsAndRepeatedOpenClose_DoesNotCreateInputBlocker()
        {
            GameObject purchaseDialog = new GameObject("dialog_ItemBuy", typeof(RectTransform));
            GameObject sellDialog = new GameObject("dialog_ItemSell", typeof(RectTransform));
            GameObject other = new GameObject("other", typeof(RectTransform));
            purchaseDialog.transform.SetParent(host.transform, false);
            sellDialog.transform.SetParent(host.transform, false);
            other.transform.SetParent(host.transform, false);
            purchaseDialog.SetActive(false);
            sellDialog.SetActive(false);

            InvokeStatic("OpenDialog", purchaseDialog.transform);
            Assert.IsTrue(purchaseDialog.activeSelf);
            Assert.AreEqual(host.transform.childCount - 1, purchaseDialog.transform.GetSiblingIndex());
            purchaseDialog.SetActive(false);

            InvokeStatic("OpenDialog", sellDialog.transform);
            Assert.IsTrue(sellDialog.activeSelf);
            Assert.AreEqual(host.transform.childCount - 1, sellDialog.transform.GetSiblingIndex());
            sellDialog.SetActive(false);

            for (int i = 0; i < 3; i++)
            {
                InvokeStatic("OpenDialog", purchaseDialog.transform);
                Assert.AreEqual(host.transform.childCount - 1, purchaseDialog.transform.GetSiblingIndex());
                purchaseDialog.SetActive(false);
                Assert.IsNull(host.transform.Find("ShopDialogInputBlocker"));

                InvokeStatic("OpenDialog", sellDialog.transform);
                Assert.AreEqual(host.transform.childCount - 1, sellDialog.transform.GetSiblingIndex());
                sellDialog.SetActive(false);
                Assert.IsNull(host.transform.Find("ShopDialogInputBlocker"));
            }
        }

        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, InstancePrivate).SetValue(target, value);

        private static object Get(object target, string name) =>
            target.GetType().GetField(name, InstancePrivate).GetValue(target);

        private static object Invoke(object target, string name, params object[] arguments) =>
            target.GetType().GetMethod(name, InstancePrivate).Invoke(target, arguments);

        private static object InvokeStatic(string name, params object[] arguments) =>
            typeof(ShopPanel).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments);
    }
}
