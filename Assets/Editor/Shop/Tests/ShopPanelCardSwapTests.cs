using System.Reflection;
using NUnit.Framework;
using Shop.UI;
using UnityEngine;

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

        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, InstancePrivate).SetValue(target, value);

        private static object Invoke(object target, string name) =>
            target.GetType().GetMethod(name, InstancePrivate).Invoke(target, null);
    }
}
