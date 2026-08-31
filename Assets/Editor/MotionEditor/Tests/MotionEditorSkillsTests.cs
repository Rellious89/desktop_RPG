using System.Collections.Generic;
using NUnit.Framework;
using Skill;
using UnityEditor;
using UnityEngine;

namespace CharacterEditor.Tests
{
    /// <summary>Skills 작업공간의 표 데이터 선택 규칙은 IMGUI 상태와 분리해 검증한다. 빈 카탈로그가
    /// 정상이라는 13A 계약과, display_order 다음 stable id 정렬 규칙을 여기서 고정한다.</summary>
    public class MotionEditorSkillsTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [Test]
        public void OrderSkillRelations_EmptyInput_IsSafe()
        {
            List<CharacterSkillDefinition> ordered = MotionEditorWindow.OrderSkillRelations(null, "CatKnight");

            Assert.IsNotNull(ordered);
            Assert.IsEmpty(ordered);
        }

        [Test]
        public void OrderSkillRelations_FiltersCharacterAndSortsByDisplayOrderThenSkillId()
        {
            CharacterSkillDefinition later = CreateRelation("CatKnight", "zeta", 20);
            CharacterSkillDefinition sameOrderLaterId = CreateRelation("CatKnight", "omega", 10);
            CharacterSkillDefinition sameOrderEarlierId = CreateRelation("CatKnight", "alpha", 10);
            CharacterSkillDefinition anotherCharacter = CreateRelation("CatMage", "outside", 0);

            List<CharacterSkillDefinition> ordered = MotionEditorWindow.OrderSkillRelations(
                new List<CharacterSkillDefinition> { later, sameOrderLaterId, anotherCharacter, sameOrderEarlierId }, "CatKnight");

            CollectionAssert.AreEqual(new[] { sameOrderEarlierId, sameOrderLaterId, later }, ordered);
        }

        [Test]
        public void BuildSkillMotionName_IsDeterministicAndCopyableAsMotionKey()
        {
            Assert.AreEqual("CatKnight_Skill_arc_slash", MotionEditorWindow.BuildSkillMotionName("CatKnight", "arc_slash"));
        }

        private CharacterSkillDefinition CreateRelation(string characterId, string skillId, int displayOrder)
        {
            CharacterSkillDefinition relation = ScriptableObject.CreateInstance<CharacterSkillDefinition>();
            created.Add(relation);
            var serialized = new SerializedObject(relation);
            serialized.FindProperty("characterId").stringValue = characterId;
            serialized.FindProperty("skillId").stringValue = skillId;
            serialized.FindProperty("displayOrder").intValue = displayOrder;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return relation;
        }
    }
}
