using System;
using System.Collections.Generic;
using System.Reflection;
using CharacterArchive;
using Common;
using Dungeon;
using NUnit.Framework;
using Quest;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

namespace CharacterArchiveEditorTests
{
    public sealed class CharacterStoryQuestUiControllerTests
    {
        private const string QuestLocaleTablePath = "Assets/Localization/Tables/09_Quest/09_Quest_ko-KR.asset";
        private const string UiLocaleTablePath = "Assets/Localization/Tables/01_UI/01_UI_ko-KR.asset";
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object item in created) UnityEngine.Object.DestroyImmediate(item);
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
        public void TotalProgress_UsesOnlyConfirmedCompletions()
        {
            CharacterStoryQuestCatalog catalog = Create<CharacterStoryQuestCatalog>();
            Set(catalog, "quests", new List<CharacterStoryQuestDefinition>
            {
                Quest("Q1", 10), Quest("Q2", 20), Quest("Q3", 30)
            });
            var firstHalfComplete = new CharacterStoryQuestSnapshot("CatKnight", "Q1", false, false,
                new List<string>(), new Dictionary<string, int> { { "A", 5 } });
            var firstReadyButUnconfirmed = new CharacterStoryQuestSnapshot("CatKnight", "Q1", true, false,
                new List<string>(), new Dictionary<string, int> { { "A", 10 } });
            var firstConfirmed = new CharacterStoryQuestSnapshot("CatKnight", "Q2", false, false,
                new List<string> { "Q1" }, new Dictionary<string, int>());
            var secondPartiallyComplete = new CharacterStoryQuestSnapshot("CatKnight", "Q2", false, false,
                new List<string> { "Q1" }, new Dictionary<string, int> { { "B", 5 } });
            var allConfirmed = new CharacterStoryQuestSnapshot("CatKnight", string.Empty, false, true,
                new List<string> { "Q1", "Q2", "Q3" }, new Dictionary<string, int>());

            CharacterStoryQuestObjectiveDefinition currentObjective = Objective("A", 10);
            Assert.AreEqual(.5f, CharacterStoryQuestUiController.CalculateCurrentProgress(new[] { currentObjective }, firstHalfComplete));
            AssertTotal(catalog, firstHalfComplete, 1, 0, 3, 0f);
            AssertTotal(catalog, firstReadyButUnconfirmed, 1, 0, 3, 0f);
            AssertTotal(catalog, firstConfirmed, 2, 1, 3, 1f / 3f);
            AssertTotal(catalog, secondPartiallyComplete, 2, 1, 3, 1f / 3f);
            AssertTotal(catalog, allConfirmed, 0, 3, 3, 1f);
        }

        [Test]
        public void TotalProgress_HandlesGraduatedOrNoActiveQuestWithoutAddingCurrentProgress()
        {
            CharacterStoryQuestCatalog catalog = Create<CharacterStoryQuestCatalog>();
            Set(catalog, "quests", new List<CharacterStoryQuestDefinition> { Quest("Q1", 10), Quest("Q2", 20) });
            var snapshot = new CharacterStoryQuestSnapshot("CatKnight", string.Empty, false, true,
                new List<string> { "Q1", "Q2", "Unknown" }, new Dictionary<string, int>());

            float result = CharacterStoryQuestUiController.CalculateTotalProgress(catalog, "CatKnight", snapshot,
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
        public void RuntimeQuestReferences_UseGuidTableReferences_NotGuidNamedTables()
        {
            LocalizedTextReference quest = CreateLocalizedReference("11805744adb144cd3bb37f325635e0d9", 10002);
            LocalizedTextReference ui = CreateLocalizedReference("32fd067a20b754a50b20446b9c78d2ae", 87);

            Assert.AreEqual(TableReference.Type.Guid, quest.TableReference.ReferenceType);
            Assert.AreEqual(new Guid("11805744adb144cd3bb37f325635e0d9"), quest.TableReference.TableCollectionNameGuid);
            Assert.AreEqual("10002", quest.TableEntryReference.Key);
            Assert.AreEqual(TableReference.Type.Guid, ui.TableReference.ReferenceType);
            Assert.AreEqual(new Guid("32fd067a20b754a50b20446b9c78d2ae"), ui.TableReference.TableCollectionNameGuid);
            Assert.AreEqual("87", ui.TableEntryReference.Key);
        }

        [Test]
        public void ConfiguredLocale_QuestAndTotalProgressFormatsResolveAndApplyArguments()
        {
            StringTable quest = AssetDatabase.LoadAssetAtPath<StringTable>(QuestLocaleTablePath);
            StringTable ui = AssetDatabase.LoadAssetAtPath<StringTable>(UiLocaleTablePath);
            Assert.NotNull(quest);
            Assert.NotNull(ui);

            Assert.IsNotEmpty(quest.GetEntry("2").Value);
            Assert.AreEqual("대상 3마리 처치 (1/3)", string.Format(quest.GetEntry("10002").Value, "대상", 3, 1, 3));
            Assert.IsNotEmpty(quest.GetEntry("4").Value);
            Assert.AreEqual("던전 2회 입장 (1/2)", string.Format(quest.GetEntry("10004").Value, "던전", 2, 1, 2));
            Assert.AreEqual("2번 퀘스트 진행 중 (1/3)", string.Format(ui.GetEntry("87").Value, 2, 1, 3));
        }

        [TestCase(0f)]
        [TestCase(.5f)]
        [TestCase(1f)]
        public void ProgressSliders_ReceiveClampedNormalizedValues(float value)
        {
            GameObject currentObject = new GameObject("current", typeof(Slider)); created.Add(currentObject);
            GameObject totalObject = new GameObject("total", typeof(Slider)); created.Add(totalObject);
            Slider current = currentObject.GetComponent<Slider>();
            Slider total = totalObject.GetComponent<Slider>();

            SetSliderProgress(current, value);
            SetSliderProgress(total, value);

            Assert.AreEqual(value, current.normalizedValue);
            Assert.AreEqual(value, total.normalizedValue);
        }

        [Test]
        public void CloseAndReopen_ReusesOneSetOfLocaleSubscriptions()
        {
            GameObject host = new GameObject("story-quest-localization-lifecycle"); created.Add(host);
            var controller = host.AddComponent<CharacterStoryQuestUiController>();
            controller.OpenFor(null);
            Assert.AreEqual(11, LocalizationSubscriptionCount(controller));

            controller.Close();
            Assert.AreEqual(0, LocalizationSubscriptionCount(controller));

            controller.OpenFor(null);
            Assert.AreEqual(11, LocalizationSubscriptionCount(controller));
            controller.OpenFor(null);
            Assert.AreEqual(11, LocalizationSubscriptionCount(controller));
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

        private static LocalizedTextReference CreateLocalizedReference(string tableGuid, int key) =>
            (LocalizedTextReference)typeof(CharacterStoryQuestUiController).GetMethod("CreateLocalizedReference", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { tableGuid, key });

        private static void SetSliderProgress(Slider slider, float value) =>
            typeof(CharacterStoryQuestUiController).GetMethod("SetSliderProgress", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { slider, value });

        private static int LocalizationSubscriptionCount(CharacterStoryQuestUiController controller) =>
            ((System.Collections.IDictionary)typeof(CharacterStoryQuestUiController).GetField("localizationHandlers", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(controller)).Count;

        private static void AssertTotal(CharacterStoryQuestCatalog catalog, CharacterStoryQuestSnapshot snapshot,
            int expectedCurrentNumber, int expectedCompleted, int expectedTotal, float expectedProgress)
        {
            float progress = CharacterStoryQuestUiController.CalculateTotalProgress(catalog, "CatKnight", snapshot,
                out int currentNumber, out int completed, out int total);
            Assert.AreEqual(expectedCurrentNumber, currentNumber);
            Assert.AreEqual(expectedCompleted, completed);
            Assert.AreEqual(expectedTotal, total);
            Assert.AreEqual(expectedProgress, progress);
        }

        private T Create<T>() where T : ScriptableObject { T value = ScriptableObject.CreateInstance<T>(); created.Add(value); return value; }
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
