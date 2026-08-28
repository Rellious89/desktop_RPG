using System.Reflection;
using Common;
using Inventory;
using NUnit.Framework;
using Shop.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEditor;

namespace ShopEditor.Tests
{
    /// <summary>판매 등록 행의 제거 드래그는 원본을 건드리지 않고 프리뷰만 정리한다.</summary>
    public sealed class ShopSellEntryViewTests
    {
        private GameObject canvasObject;
        private GameObject listObject;
        private GameObject rowObject;
        private GameObject eventSystemObject;
        private ShopSellEntryView view;
        private int removed;

        [SetUp]
        public void SetUp()
        {
            canvasObject = new GameObject("Canvas", typeof(Canvas));
            listObject = new GameObject("list", typeof(RectTransform));
            listObject.transform.SetParent(canvasObject.transform, false);
            rowObject = new GameObject("list_item", typeof(RectTransform));
            rowObject.transform.SetParent(listObject.transform, false);
            view = rowObject.AddComponent<ShopSellEntryView>();
            view.Bind("50001", listObject.GetComponent<RectTransform>(), _ => removed++);
        }

        [TearDown]
        public void TearDown()
        {
            InventorySellDragPreview.End();
            if (eventSystemObject != null) Object.DestroyImmediate(eventSystemObject);
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
        }

        [Test]
        public void BeginDrag_CreatesNonBlockingPreview_AndEndWithoutPointerStillCleansIt()
        {
            Image background = rowObject.AddComponent<Image>();
            background.color = Color.cyan;
            view.OnBeginDrag(EventData(Vector2.zero));

            GameObject preview = GameObject.Find("InventorySellDragPreview");
            Assert.IsNotNull(preview);
            CanvasGroup group = preview.GetComponent<CanvasGroup>();
            Assert.AreEqual(0.6f, group.alpha);
            Assert.IsFalse(group.blocksRaycasts);
            Assert.IsFalse(group.interactable);
            Image clonedBackground = preview.GetComponentInChildren<Image>(true);
            Assert.IsNotNull(clonedBackground, "판매 행 배경까지 포함한 전체 실루엣이어야 한다.");
            Assert.AreEqual(background.color, clonedBackground.color);

            view.OnEndDrag(null);

            Assert.IsFalse(InventorySellDragPreview.HasActivePreview);
            Assert.AreEqual(0, removed);
        }

        [Test]
        public void EndDragOutsideSellList_RemovesEntryAndCleansPreview()
        {
            listObject.GetComponent<RectTransform>().sizeDelta = new Vector2(100f, 100f);
            view.OnBeginDrag(EventData(Vector2.zero));

            view.OnEndDrag(EventData(new Vector2(1000f, 1000f)));

            Assert.AreEqual(1, removed);
            Assert.IsFalse(InventorySellDragPreview.HasActivePreview);
        }

        [Test]
        public void EndDragInsideSellList_KeepsEntryAndCleansPreview()
        {
            listObject.GetComponent<RectTransform>().sizeDelta = new Vector2(100f, 100f);
            view.OnBeginDrag(EventData(Vector2.zero));

            view.OnEndDrag(EventData(Vector2.zero));

            Assert.AreEqual(0, removed);
            Assert.IsFalse(InventorySellDragPreview.HasActivePreview);
        }

        [Test]
        public void DisableDuringDrag_CleansPreviewWithoutRemovingEntry()
        {
            view.OnBeginDrag(EventData(Vector2.zero));
            rowObject.SetActive(false);
            typeof(ShopSellEntryView).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(view, null);

            Assert.IsFalse(InventorySellDragPreview.HasActivePreview);
            Assert.AreEqual(0, removed);
        }

        [Test]
        public void TooltipView_ShowsThroughSharedControllerAndCancelsOnExit()
        {
            ItemTooltipController controller = canvasObject.AddComponent<ItemTooltipController>();
            Set(controller, "tooltipPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Art/UI/Prefab/Inventory/item_ToolTip.prefab"));
            Set(controller, "tooltipRoot", canvasObject.transform as RectTransform);
            ShopItemTooltipView tooltip = rowObject.AddComponent<ShopItemTooltipView>();
            ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();

            try
            {
                tooltip.Bind(item, 1L);
                tooltip.OnPointerEnter(null);

                Assert.AreSame(item, Field(tooltip, "boundItem"));
                Assert.AreEqual(1L, Field(tooltip, "boundCount"));
                Assert.AreSame(tooltip, controller.VisibleOwner);
                Assert.IsTrue(controller.IsVisible);

                tooltip.OnPointerExit(null);

                Assert.IsFalse(controller.IsVisible);
            }
            finally
            {
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void ShopPanel_DefaultSuccessMessagesUseUiKeys80And81()
        {
            GameObject panelObject = new GameObject("ShopPanel");
            panelObject.SetActive(false);
            try
            {
                ShopPanel panel = panelObject.AddComponent<ShopPanel>();
                SerializedObject serialized = new SerializedObject(panel);

                Assert.AreEqual("80", serialized.FindProperty(
                    "purchaseSucceededMessage.m_TableEntryReference.m_Key").stringValue);
                Assert.AreEqual("81", serialized.FindProperty(
                    "sellSucceededMessage.m_TableEntryReference.m_Key").stringValue);
            }
            finally
            {
                Object.DestroyImmediate(panelObject);
            }
        }

        private static object Field(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private PointerEventData EventData(Vector2 position)
        {
            if (eventSystemObject == null) eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
            return new PointerEventData(eventSystemObject.GetComponent<EventSystem>()) { position = position };
        }
    }
}
