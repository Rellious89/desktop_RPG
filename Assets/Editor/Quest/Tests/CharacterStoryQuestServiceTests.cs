using System.Collections.Generic;
using System.Reflection;
using Common;
using NUnit.Framework;
using Quest;
using UnityEditor;
using UnityEngine;

namespace QuestEditorTests
{
    public sealed class CharacterStoryQuestServiceTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created) Object.DestroyImmediate(item);
            created.Clear();
        }

        [Test]
        public void RootLevelObjective_IsEvaluatedImmediately_AndOnlyBecomesReady()
        {
            CharacterStoryQuestDefinition root = Quest("Q1", "CatKnight", "", false);
            CharacterStoryQuestObjectiveDefinition level = Objective("O1", "Q1", CharacterStoryQuestConditionType.CharacterLevelAtLeast, 5);
            CharacterStoryQuestService service = Service(new[] { root }, new[] { level });
            var data = new SaveData { characters = new List<CharacterSaveState> { new CharacterSaveState { characterId = "CatKnight", level = 5 } } };

            Assert.IsTrue(service.EnsureRootsForOwned(data));
            CharacterStoryQuestSaveState state = data.characterStoryQuests[0];
            Assert.AreEqual("Q1", state.activeQuestId);
            Assert.IsTrue(state.readyToComplete, "달성은 완료 대기일 뿐 자동 완료가 아니다.");
            Assert.IsEmpty(state.completedQuestIds);
        }

        [Test]
        public void MultipleObjectives_RequireAnd_AndClampAtTarget()
        {
            CharacterStoryQuestDefinition quest = Quest("Q3", "CatKnight", "", true);
            CharacterStoryQuestObjectiveDefinition defeats = Objective("O3A", "Q3", CharacterStoryQuestConditionType.MonsterDefeatCount, 30);
            CharacterStoryQuestObjectiveDefinition stamina = Objective("O3B", "Q3", CharacterStoryQuestConditionType.StaminaSpent, 50);
            CharacterStoryQuestService service = Service(new[] { quest }, new[] { defeats, stamina });
            var data = new SaveData { characterStoryQuests = new List<CharacterStoryQuestSaveState> { new CharacterStoryQuestSaveState { characterId = "CatKnight", activeQuestId = "Q3" } } };

            for (int i = 0; i < 30; i++)
                service.ApplyDefeatWithoutSave(data, "CatKnight", "any", i == 0 ? 49 : 0);
            CharacterStoryQuestSaveState state = data.characterStoryQuests[0];
            Assert.IsFalse(state.readyToComplete);
            Assert.AreEqual(2, state.objectiveProgress.Count);
            Assert.AreEqual(30, state.objectiveProgress.Find(p => p.objectiveId == "O3A").progress);
            Assert.AreEqual(49, state.objectiveProgress.Find(p => p.objectiveId == "O3B").progress);

            service.ApplyDefeatWithoutSave(data, "CatKnight", "any", 1);
            Assert.IsTrue(state.readyToComplete);
            service.ApplyDefeatWithoutSave(data, "CatKnight", "any", 100);
            Assert.AreEqual(50, state.objectiveProgress.Find(p => p.objectiveId == "O3B").progress);
        }

        private CharacterStoryQuestService Service(
            CharacterStoryQuestDefinition[] quests, CharacterStoryQuestObjectiveDefinition[] objectives)
        {
            var questCatalog = Create<CharacterStoryQuestCatalog>();
            Set(questCatalog, "quests", new List<CharacterStoryQuestDefinition>(quests));
            var objectiveCatalog = Create<CharacterStoryQuestObjectiveCatalog>();
            Set(objectiveCatalog, "objectives", new List<CharacterStoryQuestObjectiveDefinition>(objectives));
            var host = new GameObject("quest-service-test"); created.Add(host);
            var service = host.AddComponent<CharacterStoryQuestService>();
            Set(service, "questCatalog", questCatalog); Set(service, "objectiveCatalog", objectiveCatalog);
            return service;
        }

        private CharacterStoryQuestDefinition Quest(string id, string characterId, string previous, bool final)
        {
            var result = Create<CharacterStoryQuestDefinition>(); Set(result, "questId", id); Set(result, "characterId", characterId); Set(result, "previousQuestId", previous); Set(result, "isFinal", final); Set(result, "enabled", true); return result;
        }

        private CharacterStoryQuestObjectiveDefinition Objective(string id, string questId, CharacterStoryQuestConditionType type, int required)
        {
            var result = Create<CharacterStoryQuestObjectiveDefinition>(); Set(result, "objectiveId", id); Set(result, "questId", questId); Set(result, "conditionType", type); Set(result, "requiredValue", required); Set(result, "enabled", true); return result;
        }

        private T Create<T>() where T : ScriptableObject { T result = ScriptableObject.CreateInstance<T>(); created.Add(result); return result; }
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
