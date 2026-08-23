using System.Reflection;
using Character;
using CharacterArchive;
using Common;
using Dungeon;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace CharacterEditor.Tests
{
    public sealed class PartySlotViewTests
    {
        private GameObject root;
        private WorldDefinition world;
        private CharacterDefinition character;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (world != null) Object.DestroyImmediate(world);
            if (character != null) Object.DestroyImmediate(character);
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
