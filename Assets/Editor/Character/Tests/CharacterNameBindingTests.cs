using System.Collections.Generic;
using System.Reflection;
using Character;
using NUnit.Framework;
using UnityEngine;

namespace CharacterEditor.Tests
{
    public sealed class CharacterNameBindingTests
    {
        private readonly List<CharacterDefinition> created = new List<CharacterDefinition>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [Test]
        public void MissingLocalizationReference_UsesLegacyDisplayName()
        {
            CharacterDefinition definition = Definition("legacy-name");

            Assert.AreEqual("legacy-name", CharacterNameBinding.GetCurrent(definition));

            string displayed = null;
            var binding = new CharacterNameBinding();
            binding.Bind(definition, value => displayed = value);

            Assert.AreEqual("legacy-name", displayed);
            binding.Unbind();
        }

        [Test]
        public void Rebind_ReplacesThePreviousCharacter_AndNullClearsTheName()
        {
            CharacterDefinition first = Definition("first");
            CharacterDefinition second = Definition("second");
            string displayed = null;
            var binding = new CharacterNameBinding();

            binding.Bind(first, value => displayed = value);
            Assert.AreEqual("first", displayed);

            binding.Bind(second, value => displayed = value);
            Assert.AreEqual("second", displayed);

            binding.Bind(null, value => displayed = value);
            Assert.AreEqual(string.Empty, displayed);
        }

        private CharacterDefinition Definition(string displayName)
        {
            var definition = ScriptableObject.CreateInstance<CharacterDefinition>();
            created.Add(definition);
            FieldInfo field = typeof(CharacterDefinition).GetField(
                "displayName", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(definition, displayName);
            return definition;
        }
    }
}
