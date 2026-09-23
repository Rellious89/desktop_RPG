using System;
using System.IO;
using System.Reflection;
using Common;
using Inventory;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace InventoryEditor.Tests
{
    public sealed class InventorySlotMoveTests
    {
        private static readonly MethodInfo PushStorage = typeof(SaveSystem).GetMethod(
            "PushStorageOverrideForTests", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo ConfigureSave = typeof(SaveSystem).GetMethod(
            "ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo SaveOverride = typeof(InventoryManager).GetField(
            "saveOverride", BindingFlags.NonPublic | BindingFlags.Static);

        private IDisposable storageScope;
        private string temporaryRoot;
        private GameObject managerObject;
        private InventoryManager manager;
        private int changedCount;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(PushStorage);
            Assert.IsNotNull(ConfigureSave);
            Assert.IsNotNull(SaveOverride);
            temporaryRoot = Path.Combine(Path.GetTempPath(), "desktopRPG-SlotMoveTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            var storage = new LocalFileSaveStorage(new SavePathProvider(temporaryRoot), SaveProfile.LocalPrimary);
            storageScope = (IDisposable)PushStorage.Invoke(null, new object[] { storage });
            ConfigureSave.Invoke(null, new object[] { null, null, null });
            managerObject = new GameObject("InventorySlotMoveTests");
            manager = managerObject.AddComponent<InventoryManager>();
            typeof(InventoryManager).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(manager, null);
            changedCount = 0;
            InventoryManager.InventoryChanged += CountChanged;
        }

        [TearDown]
        public void TearDown()
        {
            InventoryManager.InventoryChanged -= CountChanged;
            SaveOverride.SetValue(null, null);
            if (managerObject != null) UnityEngine.Object.DestroyImmediate(managerObject);
            ConfigureSave.Invoke(null, new object[] { null, null, null });
            storageScope?.Dispose();
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
        }

        [Test]
        public void EmptySlotMove_PersistsGapAndWholeStack_AndNewItemsFillFirstFreeSlot()
        {
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "a", count = 7 });
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "b", count = 3 });

            Assert.IsTrue(manager.TryMoveSlot(0, 3, 6, "a"));
            CollectionAssert.AreEqual(new[] { "", "b", "", "a", "", "" }, manager.GetSlotItemIds(6));
            Assert.AreEqual(7, SaveSystem.Data.items[0].count);
            Assert.AreEqual(1, changedCount);

            ConfigureSave.Invoke(null, new object[] { null, null, null });
            CollectionAssert.AreEqual(new[] { "", "b", "", "a", "", "" }, manager.GetSlotItemIds(6));
            Assert.AreEqual(7, SaveSystem.Data.items[0].count);

            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "c", count = 2 });
            CollectionAssert.AreEqual(new[] { "c", "b", "", "a", "", "" }, manager.GetSlotItemIds(6));
            SaveSystem.Data.items.RemoveAt(1);
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "d", count = 1 });
            CollectionAssert.AreEqual(new[] { "c", "d", "", "a", "", "" }, manager.GetSlotItemIds(6));
        }

        [Test]
        public void OccupiedSlotMove_SwapsPositionsOnly_AndInvalidDropsDoNotSave()
        {
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "a", count = 7 });
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "b", count = 3 });
            Assert.IsTrue(manager.TryMoveSlot(0, 1, 4, "a"));
            CollectionAssert.AreEqual(new[] { "b", "a", "", "" }, manager.GetSlotItemIds(4));
            Assert.AreEqual(7, SaveSystem.Data.items[0].count);
            Assert.AreEqual(3, SaveSystem.Data.items[1].count);
            long revision = SaveSystem.Data.saveRevision;

            Assert.IsFalse(manager.TryMoveSlot(0, 0, 4, "b"));
            Assert.IsFalse(manager.TryMoveSlot(0, 9, 4, "b"));
            Assert.IsFalse(manager.TryMoveSlot(0, 2, 4, "a"));
            Assert.AreEqual(revision, SaveSystem.Data.saveRevision);
            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void SaveFailure_RestoresExactPreviousLayout_WithoutChangeEvent()
        {
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "a", count = 7 });
            var original = SaveSystem.Data.inventorySlotItemIds;
            SaveOverride.SetValue(null, new Func<bool>(() => false));
            LogAssert.Expect(LogType.Error, "[InventoryManager] 슬롯 이동을 저장하지 못해 이전 배치로 되돌렸습니다.");

            Assert.IsFalse(manager.TryMoveSlot(0, 2, 4, "a"));
            Assert.AreSame(original, SaveSystem.Data.inventorySlotItemIds);
            CollectionAssert.AreEqual(new[] { "a", "", "", "" }, manager.GetSlotItemIds(4));
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void DragPreview_ShowsIconAndCount_FollowsPointer_ThenMovesToRaycastSlot()
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            var panelObject = new GameObject("pn_Inventory", typeof(RectTransform));
            var listObject = new GameObject("list", typeof(RectTransform));
            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f));
            var item = ScriptableObject.CreateInstance<ItemDefinition>();
            try
            {
                panelObject.SetActive(false);
                panelObject.transform.SetParent(canvas.transform, false);
                listObject.transform.SetParent(panelObject.transform, false);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Art/UI/Prefab/Inventory/list_item.prefab");
                Assert.IsNotNull(prefab);
                var source = UnityEngine.Object.Instantiate(prefab, listObject.transform, false).GetComponent<InventorySlotView>();
                var target = UnityEngine.Object.Instantiate(prefab, listObject.transform, false).GetComponent<InventorySlotView>();
                Assert.IsNotNull(source);
                Assert.IsNotNull(target);
                panelObject.AddComponent<InventoryPanel>();

                var serialized = new SerializedObject(item);
                serialized.FindProperty("itemId").stringValue = "a";
                serialized.FindProperty("icon").objectReferenceValue = sprite;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                source.SetItem(item, 7);
                target.SetEmpty();
                SaveSystem.Data.items.Add(new InventoryItemState { itemId = "a", count = 7 });

                var eventData = new PointerEventData(eventSystemObject.GetComponent<EventSystem>())
                { position = new Vector2(20, 30), button = PointerEventData.InputButton.Left };
                source.OnBeginDrag(eventData);
                Assert.IsTrue(InventorySellDragPreview.HasActivePreview);
                GameObject preview = GameObject.Find("InventorySellDragPreview");
                Assert.IsNotNull(preview);
                Assert.IsFalse(preview.GetComponent<CanvasGroup>().blocksRaycasts);
                Assert.AreEqual(sprite, FindPreviewIcon(preview));
                Assert.AreEqual("7", preview.GetComponentInChildren<TextMeshProUGUI>(true).text);

                Vector3 originalPosition = preview.transform.localPosition;
                eventData.position = new Vector2(85, 60);
                source.OnDrag(eventData);
                Assert.AreNotEqual(originalPosition, preview.transform.localPosition);
                eventData.pointerCurrentRaycast = new RaycastResult { gameObject = target.gameObject };
                source.OnEndDrag(eventData);
                Assert.IsFalse(InventorySellDragPreview.HasActivePreview);
                CollectionAssert.AreEqual(new[] { "", "a" }, manager.GetSlotItemIds(2));
            }
            finally
            {
                InventorySellDragPreview.End();
                UnityEngine.Object.DestroyImmediate(item);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(eventSystemObject);
                UnityEngine.Object.DestroyImmediate(canvas);
            }
        }

        private static Sprite FindPreviewIcon(GameObject preview)
        {
            foreach (Image image in preview.GetComponentsInChildren<Image>(true))
                if (image.gameObject.name == "sp_ItemIcon") return image.sprite;
            return null;
        }

        private void CountChanged() => changedCount++;
    }
}
