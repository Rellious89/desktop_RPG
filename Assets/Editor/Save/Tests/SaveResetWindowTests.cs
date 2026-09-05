using System.Collections.Generic;
using Character;
using CommonEditor.Save;
using NUnit.Framework;
using Party;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization;

namespace CommonEditor.SaveTests
{
    public sealed class SaveResetWindowTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object value in created)
            {
                if (value != null) Object.DestroyImmediate(value);
            }

            created.Clear();
        }

        [Test]
        public void Character_seed는_현재_저장이_아닌_catalog의_InitiallyOwned_전체를_순서대로_사용한다()
        {
            CharacterCatalog catalog = CharacterCatalogOf(
                CharacterDefinitionOf("CatKnight", initiallyOwned: true, baseCorruption: 3),
                CharacterDefinitionOf("ElfArcher", initiallyOwned: false, baseCorruption: 8),
                CharacterDefinitionOf("Paladin", initiallyOwned: true, baseCorruption: 17));

            List<InitialCharacterResetSeed> seeds = SaveResetWindow.BuildInitialCharacterSeeds(catalog);

            Assert.AreEqual(2, seeds.Count,
                "저장에 현재 존재하는 ID를 보지 않고 catalog의 InitiallyOwned 정의를 모두 전달해야 합니다.");
            Assert.AreEqual("CatKnight", seeds[0].CharacterId);
            Assert.AreEqual(3d, seeds[0].BaseCorruption);
            Assert.AreEqual("Paladin", seeds[1].CharacterId);
            Assert.AreEqual(17d, seeds[1].BaseCorruption);
        }

        [Test]
        public void Character_catalog가_없거나_비어_있으면_seed도_비어_명시적_실패가_가능하다()
        {
            Assert.AreEqual(0, SaveResetWindow.BuildInitialCharacterSeeds(null).Count);
            Assert.AreEqual(0, SaveResetWindow.BuildInitialCharacterSeeds(CharacterCatalogOf()).Count);
        }

        [Test]
        public void PartyConfig_default의_BaseCapacity를_고정_슬롯_길이로_전달한다()
        {
            PartyConfigCatalog valid = PartyCatalogOf(PartyConfigOf("default", 3, enabled: true));
            PartyConfigCatalog missingDefault = PartyCatalogOf(PartyConfigOf("other", 9, enabled: true));

            Assert.AreEqual(3, SaveResetWindow.ResolvePartySlotCount(valid));
            Assert.AreEqual(0, SaveResetWindow.ResolvePartySlotCount(null));
            Assert.AreEqual(0, SaveResetWindow.ResolvePartySlotCount(missingDefault));
        }

        [Test]
        public void Character와_Quest_행은_동일한_캐릭터별_이름_해석과_폴백을_쓴다()
        {
            CharacterDefinition cat = CharacterDefinitionOf("CatKnight", initiallyOwned: true, baseCorruption: 0);
            CharacterDefinition elf = CharacterDefinitionOf("ElfArcher", initiallyOwned: false, baseCorruption: 0);
            CharacterDefinition missing = CharacterDefinitionOf("NoTranslation", initiallyOwned: false, baseCorruption: 0);
            cat.LocalizedName.SetReference("06_Character", 101L);
            elf.LocalizedName.SetReference("06_Character", 102L);
            SetDisplayName(missing, "Legacy Name");
            var definitions = new Dictionary<string, CharacterDefinition>
            {
                [cat.CharacterId] = cat,
                [elf.CharacterId] = elf,
                [missing.CharacterId] = missing,
            };

            string Resolve(LocalizedString reference)
            {
                return reference.TableEntryReference.KeyId == 101L ? "고양이기사" :
                    reference.TableEntryReference.KeyId == 102L ? "엘프궁수" : null;
            }

            // DrawCharacterRow와 DrawQuestSection 모두 이 같은 DescribeCharacterName 경로를 호출한다.
            Assert.AreEqual("고양이기사 (CatKnight)",
                SaveResetWindow.DescribeCharacterName("CatKnight", definitions, Resolve));
            Assert.AreEqual("엘프궁수 (ElfArcher)",
                SaveResetWindow.DescribeCharacterName("ElfArcher", definitions, Resolve));
            Assert.AreEqual("Legacy Name (NoTranslation)",
                SaveResetWindow.DescribeCharacterName("NoTranslation", definitions, Resolve));
            Assert.AreEqual("Unknown", SaveResetWindow.DescribeCharacterName("Unknown", definitions, Resolve));
        }

        private CharacterDefinition CharacterDefinitionOf(
            string id, bool initiallyOwned, int baseCorruption)
        {
            CharacterDefinition definition = ScriptableObject.CreateInstance<CharacterDefinition>();
            created.Add(definition);
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("characterId").stringValue = id;
            serialized.FindProperty("initiallyOwned").boolValue = initiallyOwned;
            serialized.FindProperty("baseCorruption").intValue = baseCorruption;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private static void SetDisplayName(CharacterDefinition definition, string value)
        {
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("displayName").stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private CharacterCatalog CharacterCatalogOf(params CharacterDefinition[] definitions)
        {
            CharacterCatalog catalog = ScriptableObject.CreateInstance<CharacterCatalog>();
            created.Add(catalog);
            var serialized = new SerializedObject(catalog);
            SerializedProperty characters = serialized.FindProperty("characters");
            characters.arraySize = definitions.Length;
            for (int i = 0; i < definitions.Length; i++)
            {
                characters.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }

        private PartyConfigDefinition PartyConfigOf(string id, int capacity, bool enabled)
        {
            PartyConfigDefinition definition = ScriptableObject.CreateInstance<PartyConfigDefinition>();
            created.Add(definition);
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("configId").stringValue = id;
            serialized.FindProperty("baseCapacity").intValue = capacity;
            serialized.FindProperty("enabled").boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private PartyConfigCatalog PartyCatalogOf(params PartyConfigDefinition[] definitions)
        {
            PartyConfigCatalog catalog = ScriptableObject.CreateInstance<PartyConfigCatalog>();
            created.Add(catalog);
            var serialized = new SerializedObject(catalog);
            SerializedProperty configs = serialized.FindProperty("configs");
            configs.arraySize = definitions.Length;
            for (int i = 0; i < definitions.Length; i++)
            {
                configs.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }
    }
}
