using Common;
using Character;
using Inventory;
using NUnit.Framework;
using Recovery;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CommonEditor.Tests
{
    public sealed class ItemUseTargetTests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++)
                if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [Test]
        public void CharacterTargetItem_IsDrivenByDefinitionEffectData()
        {
            ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
            try
            {
                var serialized = new SerializedObject(item);
                serialized.FindProperty("useEffectType").enumValueIndex = (int)ItemUseEffectType.RestoreStamina;
                serialized.FindProperty("useEffectValue").intValue = 20;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.IsTrue(item.CanTargetCharacter);
                Assert.AreEqual(20, item.UseEffectValue);
            }
            finally
            {
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void PreviewValue_ChangesOnlyPreviewFill()
        {
            GameObject root = new GameObject("Progress", typeof(RectTransform), typeof(Slider));
            GameObject preview = new GameObject("sp_ExpBar_Preview", typeof(RectTransform));
            preview.transform.SetParent(root.transform, false);
            ProgressBarView view = root.AddComponent<ProgressBarView>();
            try
            {
                view.SetValue(2, 10);
                view.SetPreviewValue(7, 10);

                Assert.AreEqual(0.2f, root.GetComponent<Slider>().value, 0.0001f);
                Assert.AreEqual(0.7f, ((RectTransform)preview.transform).anchorMax.x, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Dialog_ResolvesRowTemplateWithoutDependingOnContentCapitalization()
        {
            GameObject root = new GameObject("dialog_ItemUseTarget", typeof(RectTransform));
            root.SetActive(false);
            GameObject lowerCaseContent = new GameObject("content", typeof(RectTransform));
            lowerCaseContent.transform.SetParent(root.transform, false);
            GameObject template = new GameObject("list_Character", typeof(RectTransform));
            template.transform.SetParent(lowerCaseContent.transform, false);

            ItemUseTargetDialog dialog = root.AddComponent<ItemUseTargetDialog>();
            try
            {
                MethodInfo resolve = typeof(ItemUseTargetDialog).GetMethod(
                    "ResolveReferences", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo content = typeof(ItemUseTargetDialog).GetField(
                    "content", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo rowTemplate = typeof(ItemUseTargetDialog).GetField(
                    "rowTemplate", BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.IsNotNull(resolve);
                Assert.IsNotNull(content);
                Assert.IsNotNull(rowTemplate);
                resolve.Invoke(dialog, null);

                Assert.AreSame(lowerCaseContent.transform, content.GetValue(dialog));
                Assert.AreSame(template, rowTemplate.GetValue(dialog));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Dialog_OutsideClickCapture_DoesNotBlockUnderlyingUiRaycasts()
        {
            GameObject parent = new GameObject("Dialog_UI", typeof(RectTransform));
            GameObject root = new GameObject("dialog_ItemUseTarget", typeof(RectTransform));
            root.transform.SetParent(parent.transform, false);
            root.SetActive(false);
            ItemUseTargetDialog dialog = root.AddComponent<ItemUseTargetDialog>();

            try
            {
                MethodInfo ensure = typeof(ItemUseTargetDialog).GetMethod(
                    "EnsureOutsideClickBlocker", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo blockerField = typeof(ItemUseTargetDialog).GetField(
                    "outsideClickBlocker", BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.IsNotNull(ensure);
                Assert.IsNotNull(blockerField);
                ensure.Invoke(dialog, null);

                GameObject blocker = blockerField.GetValue(dialog) as GameObject;
                Assert.IsNotNull(blocker);
                Assert.IsFalse(blocker.GetComponent<Image>().raycastTarget,
                    "외부 클릭 수신 영역이 뒤쪽 인벤토리/다른 패널의 UI 입력을 가로채면 안 됩니다.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Dialog_PositionNextTo_PrefersLowerRightOfSourceIcon()
        {
            BuildPlacementFixture(
                new Vector2(800f, 600f), Vector2.zero,
                out GameObject canvasRoot, out RectTransform source, out ItemUseTargetDialog dialog);

            InvokePositionNextTo(dialog, source);

            GetWorldBounds(source, out Vector2 sourceMin, out Vector2 sourceMax);
            GetWorldBounds((RectTransform)dialog.transform, out Vector2 dialogMin, out Vector2 dialogMax);
            Assert.AreEqual(sourceMax.x + 8f, dialogMin.x, 0.01f);
            Assert.AreEqual(sourceMin.y - 8f, dialogMax.y, 0.01f);
            AssertRectInside((RectTransform)dialog.transform, (RectTransform)canvasRoot.transform);
        }

        [Test]
        public void Dialog_PositionNextTo_NearTopRightPrefersLowerRightInsideCanvas()
        {
            BuildPlacementFixture(
                new Vector2(800f, 600f), new Vector2(370f, 270f),
                out GameObject canvasRoot, out RectTransform source, out ItemUseTargetDialog dialog);

            InvokePositionNextTo(dialog, source);

            GetWorldBounds(source, out Vector2 sourceMin, out Vector2 sourceMax);
            GetWorldBounds((RectTransform)dialog.transform, out Vector2 dialogMin, out Vector2 dialogMax);
            Assert.AreEqual(sourceMax.x + 8f, dialogMin.x, 0.01f);
            Assert.AreEqual(sourceMin.y - 8f, dialogMax.y, 0.01f);
            AssertRectInside((RectTransform)dialog.transform, (RectTransform)canvasRoot.transform);
        }

        [Test]
        public void Dialog_PositionNextTo_ReplacesPreviousDraggedPositionOnEveryUse()
        {
            BuildPlacementFixture(
                new Vector2(800f, 600f), new Vector2(-250f, 100f),
                out GameObject canvasRoot, out RectTransform source, out ItemUseTargetDialog dialog);
            RectTransform dialogRect = (RectTransform)dialog.transform;
            dialogRect.anchoredPosition = new Vector2(310f, -210f);

            InvokePositionNextTo(dialog, source);

            GetWorldBounds(source, out Vector2 sourceMin, out Vector2 sourceMax);
            GetWorldBounds(dialogRect, out Vector2 dialogMin, out Vector2 dialogMax);
            Assert.AreEqual(sourceMax.x + 8f, dialogMin.x, 0.01f,
                "직전 드래그 위치가 아니라 새 아이콘의 우측을 기준으로 다시 배치해야 합니다.");
            Assert.AreEqual(sourceMin.y - 8f, dialogMax.y, 0.01f);
            AssertRectInside(dialogRect, (RectTransform)canvasRoot.transform);
        }

        [Test]
        public void CharacterItemUse_ConsumesOneItemAndRestoresStaminaWithOneSave()
        {
            CharacterDefinition character = NewCharacter();
            ItemDefinition item = NewStaminaItem(20);
            var inventory = new FakeUseInventory();
            var roster = new FakeUseRoster(character, 8, 30);
            int saves = 0;
            var service = new CharacterItemUseService(inventory, roster, () => { saves++; return true; });

            CharacterItemUseResult result = service.TryUse(item, character);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(20, result.StaminaRecovered);
            Assert.AreEqual(28, roster.Stamina);
            Assert.AreEqual(1, inventory.SpendCalls);
            Assert.AreEqual(1, saves);
            Assert.AreEqual(1, inventory.NotifyCalls);
            Assert.AreEqual(1, roster.NotifyCalls);
            Assert.AreEqual(0, inventory.RefundCalls);
        }

        [Test]
        public void CharacterItemUse_ClampsRecoveryAtMaximum()
        {
            CharacterDefinition character = NewCharacter();
            var roster = new FakeUseRoster(character, 25, 30);
            var service = new CharacterItemUseService(
                new FakeUseInventory(), roster, () => true);

            CharacterItemUseResult result = service.TryUse(NewStaminaItem(10), character);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(5, result.StaminaRecovered);
            Assert.AreEqual(30, roster.Stamina);
        }

        [Test]
        public void CharacterItemUse_FullStaminaDoesNotConsumeItemOrSave()
        {
            CharacterDefinition character = NewCharacter();
            var inventory = new FakeUseInventory();
            var roster = new FakeUseRoster(character, 30, 30);
            int saves = 0;
            var service = new CharacterItemUseService(inventory, roster, () => { saves++; return true; });

            CharacterItemUseResult result = service.TryUse(NewStaminaItem(10), character);

            Assert.AreEqual(CharacterItemUseResultCode.StaminaFull, result.Code);
            Assert.AreEqual(0, inventory.SpendCalls);
            Assert.AreEqual(0, saves);
            Assert.AreEqual(30, roster.Stamina);
        }

        [Test]
        public void CharacterItemUse_SaveFailureRollsBackItemAndStaminaWithoutNotifications()
        {
            CharacterDefinition character = NewCharacter();
            var inventory = new FakeUseInventory();
            var roster = new FakeUseRoster(character, 8, 30);
            var service = new CharacterItemUseService(inventory, roster, () => false);

            CharacterItemUseResult result = service.TryUse(NewStaminaItem(20), character);

            Assert.AreEqual(CharacterItemUseResultCode.SaveFailed, result.Code);
            Assert.AreEqual(8, roster.Stamina);
            Assert.AreEqual(1, inventory.RefundCalls);
            Assert.AreEqual(0, inventory.NotifyCalls);
            Assert.AreEqual(0, roster.NotifyCalls);
        }

        private CharacterDefinition NewCharacter()
        {
            CharacterDefinition character = ScriptableObject.CreateInstance<CharacterDefinition>();
            created.Add(character);
            return character;
        }

        private ItemDefinition NewStaminaItem(int value)
        {
            ItemDefinition item = ScriptableObject.CreateInstance<ItemDefinition>();
            created.Add(item);
            var serialized = new SerializedObject(item);
            serialized.FindProperty("itemId").stringValue = "stamina_item_" + value;
            serialized.FindProperty("useEffectType").enumValueIndex = (int)ItemUseEffectType.RestoreStamina;
            serialized.FindProperty("useEffectValue").intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        private void BuildPlacementFixture(
            Vector2 canvasSize, Vector2 sourcePosition,
            out GameObject canvasRoot, out RectTransform source, out ItemUseTargetDialog dialog)
        {
            canvasRoot = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            created.Add(canvasRoot);
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform canvasRect = (RectTransform)canvasRoot.transform;
            canvasRect.sizeDelta = canvasSize;
            canvasRect.position = Vector3.zero;

            RectTransform sourceParent = NewStretchedChild(canvasRect, "Panel_UI");
            RectTransform dialogParent = NewStretchedChild(canvasRect, "Dialog_UI");

            source = NewRect(sourceParent, "sp_ItemIcon", sourcePosition, new Vector2(40f, 40f));
            RectTransform dialogRect = NewRect(
                dialogParent, "dialog_ItemUseTarget", new Vector2(-123f, 77f), new Vector2(200f, 120f));
            dialogRect.gameObject.SetActive(false);
            dialog = dialogRect.gameObject.AddComponent<ItemUseTargetDialog>();
        }

        private static RectTransform NewStretchedChild(RectTransform parent, string name)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static RectTransform NewRect(
            RectTransform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;
            return rect;
        }

        private static void InvokePositionNextTo(ItemUseTargetDialog dialog, RectTransform source)
        {
            MethodInfo method = typeof(ItemUseTargetDialog).GetMethod(
                "PositionNextTo", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(dialog, new object[] { source });
        }

        private static void GetWorldBounds(RectTransform rect, out Vector2 min, out Vector2 max)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            min = corners[0];
            max = corners[0];
            for (int i = 1; i < corners.Length; i++)
            {
                min = Vector2.Min(min, corners[i]);
                max = Vector2.Max(max, corners[i]);
            }
        }

        private static void AssertRectInside(RectTransform rect, RectTransform bounds)
        {
            GetWorldBounds(rect, out Vector2 rectMin, out Vector2 rectMax);
            GetWorldBounds(bounds, out Vector2 boundsMin, out Vector2 boundsMax);
            Assert.GreaterOrEqual(rectMin.x, boundsMin.x - 0.01f);
            Assert.GreaterOrEqual(rectMin.y, boundsMin.y - 0.01f);
            Assert.LessOrEqual(rectMax.x, boundsMax.x + 0.01f);
            Assert.LessOrEqual(rectMax.y, boundsMax.y + 0.01f);
        }

        private sealed class FakeUseInventory : ICharacterItemUseInventory
        {
            public int SpendCalls { get; private set; }
            public int RefundCalls { get; private set; }
            public int NotifyCalls { get; private set; }

            public InventoryCostResult TrySpendCostWithoutSave(
                InventoryCostRequest request, out InventoryCostReceipt receipt)
            {
                SpendCalls++;
                receipt = InventoryCostReceipt.Empty;
                return InventoryCostResult.Payable;
            }

            public void RefundCostWithoutSave(InventoryCostReceipt receipt) => RefundCalls++;
            public void NotifyChangedAfterExternalSave() => NotifyCalls++;
        }

        private sealed class FakeUseRoster : IRecoveryRoster
        {
            private readonly CharacterDefinition character;

            public FakeUseRoster(CharacterDefinition character, int stamina, int maximum)
            {
                this.character = character;
                Stamina = stamina;
                Maximum = maximum;
            }

            public int Stamina { get; private set; }
            public int Maximum { get; }
            public int NotifyCalls { get; private set; }
            public IReadOnlyList<CharacterDefinition> RecoverableCharacters => new[] { character };
            public CharacterDefinition CurrentCharacter => character;
            public bool Contains(CharacterDefinition definition) => ReferenceEquals(character, definition);
            public CharacterDefinition FindById(string characterId) => null;
            public string GetCharacterId(CharacterDefinition definition) => string.Empty;
            public int GetStamina(CharacterDefinition definition) => Contains(definition) ? Stamina : 0;
            public int GetMaxStamina(CharacterDefinition definition) => Contains(definition) ? Maximum : 0;
            public bool ApplyRecoveryStamina(CharacterDefinition definition, int value)
            {
                if (!Contains(definition) || value == Stamina) return false;
                Stamina = value;
                return true;
            }
            public void RaiseCharacterStateChanged(CharacterDefinition definition)
            {
                if (Contains(definition)) NotifyCalls++;
            }
        }
    }
}
