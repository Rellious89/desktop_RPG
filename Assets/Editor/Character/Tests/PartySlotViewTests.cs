using System.Reflection;
using Character;
using CharacterArchive;
using Common;
using Dungeon;
using NUnit.Framework;
using TMPro;
using UnityEditor;
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
        public void DragPreview_CopiesRootGraphicUsesPointerTopLeftAndCleansUp()
        {
            root = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var source = new GameObject("source", typeof(RectTransform), typeof(Image));
            source.transform.SetParent(root.transform, false);
            var sourceRect = (RectTransform)source.transform;
            sourceRect.sizeDelta = new Vector2(100f, 50f);
            source.GetComponent<Image>().color = new Color(0.2f, 0.4f, 0.6f, 0.8f);

            InvokePreview("Begin", source, new Vector2(30f, 40f));
            Assert.IsTrue(PreviewActive());
            GameObject preview = GameObject.Find("CharacterArchiveDragPreview");
            CanvasGroup group = preview.GetComponent<CanvasGroup>();
            Assert.AreEqual(0.6f, group.alpha);
            Assert.IsFalse(group.blocksRaycasts);
            Assert.IsFalse(group.interactable);

            RectTransform previewRect = (RectTransform)preview.transform;
            RectTransform cloneRect = (RectTransform)preview.transform.GetChild(0);
            Image cloneBackground = cloneRect.GetComponent<Image>();
            Assert.AreEqual(new Vector2(0f, 1f), previewRect.pivot);
            Assert.AreEqual(new Vector2(0f, 1f), cloneRect.pivot);
            Assert.AreEqual(sourceRect.rect.size, previewRect.rect.size);
            Assert.AreEqual(sourceRect.rect.size, cloneRect.rect.size);
            Assert.IsTrue(cloneBackground.enabled, "루트 배경 Graphic은 상호작용만 꺼진 복제본에 남아야 한다.");
            Assert.AreEqual(source.GetComponent<Image>().color, cloneBackground.color);

            InvokePreview("UpdatePosition", new Vector2(60f, 80f));
            RectTransform canvasRect = (RectTransform)root.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, new Vector2(60f, 80f), null, out Vector2 expected);
            Assert.That(previewRect.anchoredPosition, Is.EqualTo(expected).Within(0.01f),
                "프리뷰의 좌상단 피벗 위치는 입력 포인터와 같아야 한다.");

            InvokePreview("End");
            Assert.IsFalse(PreviewActive());
        }

        [Test]
        public void PartyToastReferences_UseConfiguredUiEntries()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/UI/Prefab/panel/pn_CharacterArchive.prefab");
            Assert.IsNotNull(prefab);
            var serializedPanel = new SerializedObject(prefab.GetComponent<CharacterArchivePanel>());

            AssertToastReference(serializedPanel, "recoveryLeaveBlockedToast", 9478137665544192L);
            AssertToastReference(serializedPanel, "partyJoinToast", 9478137678127104L);
            AssertToastReference(serializedPanel, "activeCharacterBlockedToast", 9478137678127105L);
        }

        [Test]
        public void PartySlotPrefab_HasTransparentRootRaycastGraphicWithoutBlockingRemoveButton()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/UI/Prefab/CharacterArchive/slot_CharacterArchive_Party1.prefab");
            Assert.IsNotNull(prefab);

            Image rootRaycastGraphic = prefab.GetComponent<Image>();
            Button removeButton = FindChild(prefab.transform, "btn_remove").GetComponent<Button>();
            Assert.IsTrue(rootRaycastGraphic.enabled);
            Assert.IsTrue(rootRaycastGraphic.raycastTarget);
            Assert.AreEqual(0f, rootRaycastGraphic.color.a);
            Assert.IsNotNull(removeButton, "자식 버튼은 루트 Graphic보다 먼저 레이캐스트되어 기존 클릭 경로를 유지한다.");
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

        private static void AssertToastReference(SerializedObject serializedPanel, string fieldName, long expectedKeyId)
        {
            SerializedProperty reference = serializedPanel.FindProperty(fieldName);
            Assert.IsNotNull(reference);
            Assert.AreEqual("GUID:32fd067a20b754a50b20446b9c78d2ae", reference.FindPropertyRelative("m_TableReference.m_TableCollectionName").stringValue);
            Assert.AreEqual(expectedKeyId, reference.FindPropertyRelative("m_TableEntryReference.m_KeyId").longValue);
        }

        private static Transform FindChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform nested = FindChild(child, name);
                if (nested != null) return nested;
            }

            return null;
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
