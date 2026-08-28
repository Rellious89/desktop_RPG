using Common;
using Inventory;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEditor;

namespace InventoryEditor.Tests
{
    /// <summary>판매 등록 드래그의 표시물과 등록 경로가 슬롯 원본을 건드리지 않는지 고정한다.</summary>
    public sealed class InventorySlotSellDragPreviewTests
    {
        private GameObject canvasObject;
        private GameObject slotObject;
        private GameObject eventSystemObject;
        private InventorySlotView slot;
        private ItemDefinition item;
        private RegistrationTarget target;

        [SetUp]
        public void SetUp()
        {
            canvasObject = new GameObject("Canvas", typeof(Canvas));
            slotObject = new GameObject("list_item", typeof(RectTransform));
            slotObject.transform.SetParent(canvasObject.transform, false);
            slot = slotObject.AddComponent<InventorySlotView>();
            item = ScriptableObject.CreateInstance<ItemDefinition>();
            SetSellable(item, true);
            slot.SetItem(item, 1);
            target = new RegistrationTarget { CanRegister = true, IsDropTarget = true };
            InventoryItemRegistrationContext.SetActiveTarget(target);
        }

        [TearDown]
        public void TearDown()
        {
            InventoryItemRegistrationContext.ClearActiveTarget(target);
            InventorySellDragPreview.End();
            if (item != null) Object.DestroyImmediate(item);
            if (eventSystemObject != null) Object.DestroyImmediate(eventSystemObject);
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
        }

        [Test]
        public void SellableRegistrationDrag_CreatesMovesAndCleansPreviewThenRegistersDrop()
        {
            var eventData = CreateEventData(new Vector2(20f, 30f));
            slot.OnBeginDrag(eventData);

            Assert.IsTrue(InventorySellDragPreview.HasActivePreview);
            GameObject preview = GameObject.Find("InventorySellDragPreview");
            Assert.IsNotNull(preview);
            Assert.AreEqual(canvasObject.transform, preview.transform.parent);
            Assert.IsFalse(preview.GetComponent<CanvasGroup>().blocksRaycasts);
            Assert.IsFalse(preview.GetComponent<CanvasGroup>().interactable);

            eventData.position = new Vector2(80f, 50f);
            slot.OnDrag(eventData);
            slot.OnEndDrag(eventData);

            Assert.AreEqual(1, target.RegisteredCount);
            Assert.IsFalse(InventorySellDragPreview.HasActivePreview);
            Assert.IsNull(GameObject.Find("InventorySellDragPreview"));
            Assert.AreSame(item, slot.Definition);
            Assert.AreEqual(1, slot.Count);
        }

        [Test]
        public void NonSellableRegistrationTarget_DoesNotCreatePreviewOrRegister()
        {
            SetSellable(item, false);
            var eventData = CreateEventData(Vector2.zero);

            slot.OnBeginDrag(eventData);
            slot.OnDrag(eventData);
            slot.OnEndDrag(eventData);

            Assert.IsFalse(InventorySellDragPreview.HasActivePreview);
            Assert.AreEqual(0, target.RegisteredCount);
        }

        [Test]
        public void RightClickRegistration_RemainsAvailableWithoutStartingPreview()
        {
            var eventData = CreateEventData(Vector2.zero);
            eventData.button = PointerEventData.InputButton.Right;

            slot.OnPointerClick(eventData);

            Assert.AreEqual(1, target.RegisteredCount);
            Assert.IsFalse(InventorySellDragPreview.HasActivePreview);
        }

        [Test]
        public void DisableDuringDrag_RemovesPreviewWithoutRegistering()
        {
            slot.OnBeginDrag(CreateEventData(Vector2.zero));
            Assert.IsTrue(InventorySellDragPreview.HasActivePreview);

            slotObject.SetActive(false);
            typeof(InventorySlotView).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(slot, null);

            Assert.IsFalse(InventorySellDragPreview.HasActivePreview);
            Assert.AreEqual(0, target.RegisteredCount);
        }

        private PointerEventData CreateEventData(Vector2 position)
        {
            if (eventSystemObject == null) eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
            var data = new PointerEventData(eventSystemObject.GetComponent<EventSystem>()) { position = position };
            return data;
        }

        private static void SetSellable(ItemDefinition definition, bool value)
        {
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("sellable").boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private sealed class RegistrationTarget : IInventoryItemRegistrationTarget
        {
            public bool CanRegister;
            public bool IsDropTarget;
            public int RegisteredCount;

            public bool CanRegisterInventoryItem(ItemDefinition value) => CanRegister && value != null && value.Sellable;
            public bool IsInventoryItemRegistrationDrop(Vector2 ignored, Camera ignoredCamera) => IsDropTarget;
            public void RegisterInventoryItem(ItemDefinition ignored) => RegisteredCount++;
        }
    }
}
