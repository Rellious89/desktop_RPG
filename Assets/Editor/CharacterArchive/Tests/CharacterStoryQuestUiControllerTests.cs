using System.Collections.Generic;
using System.Reflection;
using CharacterArchive;
using Dungeon;
using NUnit.Framework;
using Quest;
using UnityEngine;
using UnityEngine.UI;

namespace CharacterArchiveEditorTests
{
    public sealed class CharacterStoryQuestUiControllerTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created) Object.DestroyImmediate(item);
            created.Clear();
        }

        [Test]
        public void CurrentProgress_UsesEqualObjectiveWeights_AndClampsEachObjective()
        {
            CharacterStoryQuestObjectiveDefinition first = Objective("A", 10);
            CharacterStoryQuestObjectiveDefinition second = Objective("B", 10);
            var halfAndHalf = Snapshot("A", 5, "B", 5);
            var completeAndEmpty = Snapshot("A", 10, "B", 0);

            Assert.AreEqual(.5f, CharacterStoryQuestUiController.CalculateCurrentProgress(new[] { first, second }, halfAndHalf));
            Assert.AreEqual(.5f, CharacterStoryQuestUiController.CalculateCurrentProgress(new[] { first, second }, completeAndEmpty));
            Assert.AreEqual(.5f, CharacterStoryQuestUiController.CalculateCurrentProgress(new[] { first, second }, Snapshot("A", 99, "B", -4)));
        }

        [Test]
        public void TotalProgress_UsesCompletedPlusOnlyTheActiveQuestProgress()
        {
            CharacterStoryQuestCatalog catalog = Create<CharacterStoryQuestCatalog>();
            Set(catalog, "quests", new List<CharacterStoryQuestDefinition>
            {
                Quest("Q1", 10), Quest("Q2", 20), Quest("Q3", 30)
            });
            var snapshot = new CharacterStoryQuestSnapshot("CatKnight", "Q2", false, false,
                new List<string> { "Q1", "Unknown" }, new Dictionary<string, int>());

            float result = CharacterStoryQuestUiController.CalculateTotalProgress(catalog, "CatKnight", snapshot, .5f,
                out int currentNumber, out int completed, out int total);

            Assert.AreEqual(2, currentNumber);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(3, total);
            Assert.AreEqual(.5f, result);
        }

        [Test]
        public void TotalProgress_HandlesGraduatedOrNoActiveQuestWithoutAddingCurrentProgress()
        {
            CharacterStoryQuestCatalog catalog = Create<CharacterStoryQuestCatalog>();
            Set(catalog, "quests", new List<CharacterStoryQuestDefinition> { Quest("Q1", 10), Quest("Q2", 20) });
            var snapshot = new CharacterStoryQuestSnapshot("CatKnight", string.Empty, false, true,
                new List<string> { "Q1", "Q2", "Unknown" }, new Dictionary<string, int>());

            float result = CharacterStoryQuestUiController.CalculateTotalProgress(catalog, "CatKnight", snapshot, 1f,
                out int currentNumber, out int completed, out int total);

            Assert.AreEqual(0, currentNumber);
            Assert.AreEqual(2, completed);
            Assert.AreEqual(2, total);
            Assert.AreEqual(1f, result);
        }

        [TestCase(0f, "0%")]
        [TestCase(.505f, "51%")]
        [TestCase(1f, "100%")]
        [TestCase(2f, "100%")]
        [TestCase(-1f, "0%")]
        public void ProgressPercent_IsClampedAndRenderedAsAnInteger(float progress, string expected)
        {
            Assert.AreEqual(expected, CharacterStoryQuestUiController.FormatProgressPercent(progress));
        }

        [Test]
        public void OpenFor_AlwaysRestoresTheInspectorConfiguredDefaultPage()
        {
            GameObject host = new GameObject("story-quest-ui-test"); created.Add(host);
            var controller = host.AddComponent<CharacterStoryQuestUiController>();
            GameObject characterPage = new GameObject("character-page"); created.Add(characterPage);
            GameObject questPage = new GameObject("quest-page"); created.Add(questPage);
            Set(controller, "characterInfoPage", characterPage);
            Set(controller, "questInfoPage", questPage);
            Set(controller, "defaultRightPage", CharacterStoryQuestUiController.RightPage.QuestInfo);

            controller.OpenFor(null);
            Assert.IsFalse(characterPage.activeSelf);
            Assert.IsTrue(questPage.activeSelf);
            characterPage.SetActive(true); questPage.SetActive(false);
            controller.OpenFor(null);
            Assert.IsFalse(characterPage.activeSelf);
            Assert.IsTrue(questPage.activeSelf);
        }

        [Test]
        public void QuestInfoPageToggle_RemainsBoundAfterTheControllerPageIsHidden()
        {
            GameObject characterPage = new GameObject("character-page"); created.Add(characterPage);
            GameObject questPage = new GameObject("quest-page"); created.Add(questPage);
            var controller = questPage.AddComponent<CharacterStoryQuestUiController>();
            GameObject swap = new GameObject("swap", typeof(Button)); created.Add(swap);
            Button swapButton = swap.GetComponent<Button>();
            Set(controller, "characterInfoPage", characterPage);
            Set(controller, "questInfoPage", questPage);
            Set(controller, "swapButton", swapButton);

            controller.OpenFor(null);
            Assert.IsTrue(characterPage.activeSelf);
            Assert.IsFalse(questPage.activeSelf);

            swapButton.onClick.Invoke();
            Assert.IsTrue(questPage.activeSelf);
            swapButton.onClick.Invoke();
            Assert.IsTrue(characterPage.activeSelf);
            Assert.IsFalse(questPage.activeSelf);
            swapButton.onClick.Invoke();
            Assert.IsTrue(questPage.activeSelf);
        }

        [Test]
        public void TargetDisplay_UsesAnyTargetOrOrderedSafeIdFallbacks()
        {
            GameObject host = new GameObject("story-quest-target-test"); created.Add(host);
            var controller = host.AddComponent<CharacterStoryQuestUiController>();
            MonsterCatalog monsters = Create<MonsterCatalog>();
            MonsterDefinition firstMonster = Create<MonsterDefinition>(); Set(firstMonster, "monsterId", "monster_a");
            MonsterDefinition secondMonster = Create<MonsterDefinition>(); Set(secondMonster, "monsterId", "monster_b");
            Set(monsters, "monsters", new List<MonsterDefinition> { firstMonster, secondMonster });
            Set(controller, "monsterCatalog", monsters);
            DungeonCatalog dungeons = Create<DungeonCatalog>();
            DungeonDefinition firstDungeon = Create<DungeonDefinition>(); Set(firstDungeon, "dungeonId", "dungeon_a");
            DungeonDefinition secondDungeon = Create<DungeonDefinition>(); Set(secondDungeon, "dungeonId", "dungeon_b");
            Set(dungeons, "dungeons", new List<DungeonDefinition> { firstDungeon, secondDungeon });
            Set(controller, "dungeonCatalog", dungeons);

            CharacterStoryQuestObjectiveDefinition any = Objective("Any", 1);
            Set(any, "conditionType", CharacterStoryQuestConditionType.MonsterDefeatCount);
            CharacterStoryQuestObjectiveDefinition one = Objective("One", 1);
            Set(one, "targetIds", new List<string> { "monster_a" });
            CharacterStoryQuestObjectiveDefinition many = Objective("Many", 1);
            Set(many, "targetIds", new List<string> { "monster_b", "missing", "monster_a" });
            CharacterStoryQuestObjectiveDefinition dungeonMany = Objective("DungeonMany", 1);
            Set(dungeonMany, "targetIds", new List<string> { "dungeon_b", "missing", "dungeon_a" });

            Assert.IsNotEmpty(TargetName(controller, any, true));
            Assert.AreEqual("monster_a", TargetName(controller, one, true));
            Assert.AreEqual("monster_b, missing, monster_a", TargetName(controller, many, true));
            Assert.AreEqual("dungeon_b, missing, dungeon_a", TargetName(controller, dungeonMany, false));
        }

        private CharacterStoryQuestObjectiveDefinition Objective(string id, int required)
        {
            CharacterStoryQuestObjectiveDefinition result = Create<CharacterStoryQuestObjectiveDefinition>();
            Set(result, "objectiveId", id); Set(result, "questId", "Q"); Set(result, "requiredValue", required); Set(result, "enabled", true);
            return result;
        }

        private CharacterStoryQuestDefinition Quest(string id, int order)
        {
            CharacterStoryQuestDefinition result = Create<CharacterStoryQuestDefinition>();
            Set(result, "questId", id); Set(result, "characterId", "CatKnight"); Set(result, "displayOrder", order); Set(result, "enabled", true);
            return result;
        }

        private static CharacterStoryQuestSnapshot Snapshot(string first, int firstValue, string second, int secondValue) =>
            new CharacterStoryQuestSnapshot("CatKnight", "Q", false, false, new List<string>(),
                new Dictionary<string, int> { { first, firstValue }, { second, secondValue } });

        private static string TargetName(CharacterStoryQuestUiController controller, CharacterStoryQuestObjectiveDefinition objective, bool monster) =>
            (string)typeof(CharacterStoryQuestUiController).GetMethod("TargetName", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { objective, monster });

        private T Create<T>() where T : ScriptableObject { T value = ScriptableObject.CreateInstance<T>(); created.Add(value); return value; }
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
