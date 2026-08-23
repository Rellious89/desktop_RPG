using System.Reflection;
using Character;
using CharacterArchive;
using Common;
using Dungeon;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CharacterEditor.Tests
{
    public sealed class PartySlotViewTests
    {
        private GameObject root;
        private WorldDefinition world;
        private CharacterDefinition character;
        private CharacterCatalog catalog;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (world != null) Object.DestroyImmediate(world);
            if (character != null) Object.DestroyImmediate(character);
            if (catalog != null) Object.DestroyImmediate(catalog);
        }

        [Test]
        public void WorldBinding_BindsValidReferenceAndClearsStaleTextWhenUnbound()
        {
            root = new GameObject("slot_CharacterArchive_Party1", typeof(RectTransform), typeof(PartySlotView));
            var label = new GameObject("lb_WorldName", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(root.transform, false);
            PartySlotView view = root.GetComponent<PartySlotView>();
            Invoke(view, "ResolveViews");

            world = ScriptableObject.CreateInstance<WorldDefinition>();
            SetPrivate(world, "localizedName", new LocalizedTextReference("GUID:32fd067a20b754a50b20446b9c78d2ae", "52"));
            character = ScriptableObject.CreateInstance<CharacterDefinition>();
            SetPrivate(character, "originWorld", world);

            Invoke(view, "BindWorldName", character);
            Assert.AreSame(world.LocalizedName, GetPrivate(view, "worldName"));

            Invoke(view, "BindWorldName", character);
            Assert.AreSame(world.LocalizedName, GetPrivate(view, "worldName"),
                "같은 캐릭터 재바인딩은 같은 참조 하나만 유지한다.");

            label.GetComponent<TextMeshProUGUI>().text = "stale";
            Invoke(view, "BindWorldName", (object)null);
            Assert.AreEqual(string.Empty, label.GetComponent<TextMeshProUGUI>().text);

            Invoke(view, "UnbindWorldName");
            Assert.IsNull(GetPrivate(view, "worldName"));
        }

        [Test]
        public void DragPreview_IsSingleTransparentNonRaycastAndCleansUp()
        {
            root = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var source = new GameObject("source", typeof(RectTransform), typeof(Image));
            source.transform.SetParent(root.transform, false);

            InvokePreview("Begin", source, new Vector2(30f, 40f));
            Assert.IsTrue(PreviewActive());
            GameObject preview = GameObject.Find("CharacterArchiveDragPreview");
            CanvasGroup group = preview.GetComponent<CanvasGroup>();
            Assert.AreEqual(0.6f, group.alpha);
            Assert.IsFalse(group.blocksRaycasts);
            Assert.IsFalse(group.interactable);

            InvokePreview("UpdatePosition", new Vector2(60f, 80f));
            Assert.AreNotEqual(Vector2.zero, ((RectTransform)preview.transform).anchoredPosition);

            InvokePreview("End");
            Assert.IsFalse(PreviewActive());
        }

        [Test]
        public void RemoveButton_IsHiddenForOneMemberAndShownForEveryOccupiedMemberAboveOne()
        {
            root = new GameObject("root", typeof(RectTransform));
            var panelObject = new GameObject("panel", typeof(RectTransform), typeof(CharacterArchivePanel));
            panelObject.transform.SetParent(root.transform, false);
            var slotObject = new GameObject("slot_CharacterArchive_Party1", typeof(RectTransform), typeof(PartySlotView));
            slotObject.transform.SetParent(root.transform, false);
            AddChild(slotObject, "item_Party_enable");
            AddChild(slotObject, "item_Party_disable");
            GameObject remove = AddChild(slotObject, "btn_remove", typeof(Button));
            remove.SetActive(false);

            character = ScriptableObject.CreateInstance<CharacterDefinition>();
            SetPrivate(character, "characterId", "hero");
            catalog = ScriptableObject.CreateInstance<CharacterCatalog>();
            var catalogSo = new UnityEditor.SerializedObject(catalog);
            catalogSo.FindProperty("characters").arraySize = 1;
            catalogSo.FindProperty("characters").GetArrayElementAtIndex(0).objectReferenceValue = character;
            catalogSo.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            SetPrivate(panelObject.GetComponent<CharacterArchivePanel>(), "catalog", catalog);

            PartySlotView view = slotObject.GetComponent<PartySlotView>();
            view.Bind(panelObject.GetComponent<CharacterArchivePanel>(), 0);
            var one = new SaveData { partyCharacterIds = new System.Collections.Generic.List<string> { "hero" } };
            view.Refresh(one, character, 3);
            Assert.IsFalse(remove.activeSelf);

            var two = new SaveData { partyCharacterIds = new System.Collections.Generic.List<string> { "hero", "other" } };
            view.Refresh(two, character, 3);
            Assert.IsTrue(remove.activeSelf);
            Assert.IsTrue(remove.GetComponent<Button>().interactable,
                "현재/회복 상태는 클릭 경로가 막더라도 2명 이상 파티의 탈퇴 버튼을 숨기지 않는다.");
        }

        private static GameObject AddChild(GameObject parent, string name, params System.Type[] components)
        {
            var child = new GameObject(name, components.Length == 0 ? new[] { typeof(RectTransform) } : components);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static void InvokePreview(string methodName, params object[] arguments)
        {
            System.Type type = typeof(PartySlotView).Assembly.GetType("CharacterArchive.CharacterArchiveDragPreview");
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(null, arguments);
        }

        private static bool PreviewActive()
        {
            System.Type type = typeof(PartySlotView).Assembly.GetType("CharacterArchive.CharacterArchiveDragPreview");
            PropertyInfo property = type.GetProperty("HasActivePreview", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return (bool)property.GetValue(null);
        }

        private static void Invoke(PartySlotView view, string name)
        {
            MethodInfo method = typeof(PartySlotView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(view, null);
        }

        private static void Invoke(PartySlotView view, string name, object argument)
        {
            MethodInfo method = typeof(PartySlotView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(view, new[] { argument });
        }

        private static void SetPrivate(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(target, value);
        }

        private static object GetPrivate(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            return field.GetValue(target);
        }
    }
}
