using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CommonEditor;
using NUnit.Framework;
using Quest;
using TableDataEditor;
using UnityEditor;

namespace TableDataEditorTests
{
    /// <summary>서사 퀘스트 보상 두 칸의 표 계약과 실제 생성 결과를 함께 고정한다.</summary>
    public sealed class CharacterStoryQuestRewardTableTests
    {
        private static readonly Type PipelineType = typeof(CharacterStoryQuestTablePipeline);
        private static readonly Type QuestRowType = PipelineType.GetNestedType("QuestRow", BindingFlags.NonPublic);
        private static readonly Type RewardRowType = PipelineType.GetNestedType("RewardRow", BindingFlags.NonPublic);
        private static readonly MethodInfo ValidateRewardsMethod = PipelineType.GetMethod("ValidateRewards",
            BindingFlags.NonPublic | BindingFlags.Static);

        [Test]
        public void LiveCsv_GeneratesExpectedRewardsForAllCommittedQuests()
        {
            TableDataDiagnosticLog log = CharacterStoryQuestTablePipeline.ValidateOnly();
            Assert.IsFalse(log.HasErrors);

            AssertRewards("CatKnight_10001", (CharacterStoryQuestRewardType.Currency, "jewel", 100));
            AssertRewards("CatKnight_10002", (CharacterStoryQuestRewardType.Currency, "jewel", 150));
            AssertRewards("CatKnight_10003",
                (CharacterStoryQuestRewardType.Currency, "jewel", 200),
                (CharacterStoryQuestRewardType.Item, "50001", 3));
            AssertRewards("Barbarian_10001", (CharacterStoryQuestRewardType.Currency, "jewel", 100));
            AssertRewards("Barbarian_10002", (CharacterStoryQuestRewardType.Currency, "jewel", 150));
            AssertRewards("Barbarian_10003", (CharacterStoryQuestRewardType.Currency, "jewel", 200));
            AssertRewards("ElfArcher_10001", (CharacterStoryQuestRewardType.Currency, "jewel", 100));
            AssertRewards("ElfArcher_10002", (CharacterStoryQuestRewardType.Currency, "jewel", 150));
            AssertRewards("ElfArcher_10003", (CharacterStoryQuestRewardType.Currency, "jewel", 200));
        }

        [Test]
        public void GeneratedCatalogs_ContainTheApprovedCharacterQuestPilotChains()
        {
            var quests = AssetDatabase.LoadAssetAtPath<CharacterStoryQuestCatalog>(
                CharacterStoryQuestTablePipeline.OutputFolder + "/CharacterStoryQuestCatalog.asset");
            var objectives = AssetDatabase.LoadAssetAtPath<CharacterStoryQuestObjectiveCatalog>(
                CharacterStoryQuestTablePipeline.ObjectiveOutputFolder + "/CharacterStoryQuestObjectiveCatalog.asset");
            Assert.NotNull(quests);
            Assert.NotNull(objectives);

            CollectionAssert.AreEqual(new[]
            {
                "Barbarian_10001", "CatKnight_10001", "ElfArcher_10001",
                "Barbarian_10002", "CatKnight_10002", "ElfArcher_10002",
                "Barbarian_10003", "CatKnight_10003", "ElfArcher_10003",
            }, QuestIds(quests));

            AssertQuest(quests.Find("Barbarian_10001"), "Barbarian", "", 10, false);
            AssertQuest(quests.Find("Barbarian_10002"), "Barbarian", "Barbarian_10001", 20, false);
            AssertQuest(quests.Find("Barbarian_10003"), "Barbarian", "Barbarian_10002", 30, true);
            AssertQuest(quests.Find("ElfArcher_10001"), "ElfArcher", "", 10, false);
            AssertQuest(quests.Find("ElfArcher_10002"), "ElfArcher", "ElfArcher_10001", 20, false);
            AssertQuest(quests.Find("ElfArcher_10003"), "ElfArcher", "ElfArcher_10002", 30, true);

            AssertObjectives(objectives, "Barbarian_10001",
                ("Barbarian_10001_01", CharacterStoryQuestConditionType.CharacterLevelAtLeast, 3, 10));
            AssertObjectives(objectives, "Barbarian_10002",
                ("Barbarian_10002_01", CharacterStoryQuestConditionType.MonsterDefeatCount, 12, 10));
            AssertObjectives(objectives, "Barbarian_10003",
                ("Barbarian_10003_01", CharacterStoryQuestConditionType.CharacterLevelAtLeast, 6, 10),
                ("Barbarian_10003_02", CharacterStoryQuestConditionType.StaminaSpent, 20, 20));
            AssertObjectives(objectives, "ElfArcher_10001",
                ("ElfArcher_10001_01", CharacterStoryQuestConditionType.DungeonEnterCount, 2, 10));
            AssertObjectives(objectives, "ElfArcher_10002",
                ("ElfArcher_10002_01", CharacterStoryQuestConditionType.CharacterLevelAtLeast, 4, 10));
            AssertObjectives(objectives, "ElfArcher_10003",
                ("ElfArcher_10003_01", CharacterStoryQuestConditionType.CharacterLevelAtLeast, 6, 10),
                ("ElfArcher_10003_02", CharacterStoryQuestConditionType.MonsterDefeatCount, 10, 20));
        }

        [Test]
        public void RewardSlots_EmptyAndNoneAreIgnored_ButInvalidKindsAreRejected()
        {
            Assert.IsFalse(Validate(NewReward(1, CharacterStoryQuestRewardType.None, "", "leftover", 0),
                NewReward(2, CharacterStoryQuestRewardType.Currency, "Currency", "jewel", 1)).HasErrors);
            Assert.IsFalse(Validate(NewReward(1, CharacterStoryQuestRewardType.Currency, "Currency", "jewel", 1),
                NewReward(2, CharacterStoryQuestRewardType.Item, "Item", "50001", 1)).HasErrors);

            Assert.IsTrue(Validate(NewReward(1, (CharacterStoryQuestRewardType)(-1), "Gold", "jewel", 1),
                NewReward(2, CharacterStoryQuestRewardType.None, "", "", 0)).HasErrors, "알 수 없는 타입");
            Assert.IsTrue(Validate(NewReward(1, CharacterStoryQuestRewardType.Currency, "Currency", "jewel", 0),
                NewReward(2, CharacterStoryQuestRewardType.None, "", "", 0)).HasErrors, "0 수량");
            Assert.IsTrue(Validate(NewReward(1, CharacterStoryQuestRewardType.Currency, "Currency", "gold", 1),
                NewReward(2, CharacterStoryQuestRewardType.None, "", "", 0)).HasErrors, "jewel 이외 재화");
            Assert.IsTrue(Validate(NewReward(1, CharacterStoryQuestRewardType.Item, "Item", "missing", 1),
                NewReward(2, CharacterStoryQuestRewardType.None, "", "", 0)).HasErrors, "없는 아이템");
            Assert.IsTrue(Validate(NewReward(1, CharacterStoryQuestRewardType.Currency, "Currency", "jewel", 1),
                NewReward(2, CharacterStoryQuestRewardType.Currency, "Currency", "jewel", 2)).HasErrors, "중복 타입");
        }

        private static void AssertRewards(string questId,
            params (CharacterStoryQuestRewardType type, string targetId, int amount)[] expected)
        {
            CharacterStoryQuestDefinition quest = AssetDatabase.LoadAssetAtPath<CharacterStoryQuestDefinition>(
                CharacterStoryQuestTablePipeline.OutputFolder + "/Quest_" + questId + ".asset");
            Assert.NotNull(quest);
            Assert.AreEqual(expected.Length, quest.Rewards.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                CharacterStoryQuestRewardDefinition reward = quest.Rewards[i];
                Assert.AreEqual(expected[i].type, reward.RewardType);
                Assert.AreEqual(expected[i].amount, reward.Amount);
                Assert.AreEqual(expected[i].targetId, reward.RewardType == CharacterStoryQuestRewardType.Currency
                    ? reward.Currency.CurrencyId : reward.Item.ItemId);
            }
        }

        private static string[] QuestIds(CharacterStoryQuestCatalog catalog)
        {
            var ids = new string[catalog.Quests.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = catalog.Quests[i].QuestId;
            return ids;
        }

        private static void AssertQuest(CharacterStoryQuestDefinition quest, string characterId,
            string previousQuestId, int displayOrder, bool isFinal)
        {
            Assert.NotNull(quest);
            Assert.AreEqual(characterId, quest.CharacterId);
            Assert.AreEqual(previousQuestId, quest.PreviousQuestId);
            Assert.AreEqual(displayOrder, quest.DisplayOrder);
            Assert.AreEqual(isFinal, quest.IsFinal);
            Assert.IsTrue(quest.Enabled);
            Assert.IsTrue(quest.LocalizedTitle.HasReference);
            Assert.IsTrue(quest.LocalizedDescription.HasReference);
        }

        private static void AssertObjectives(CharacterStoryQuestObjectiveCatalog catalog, string questId,
            params (string id, CharacterStoryQuestConditionType condition, int required, int order)[] expected)
        {
            List<CharacterStoryQuestObjectiveDefinition> actual = catalog.ForQuest(questId);
            Assert.AreEqual(expected.Length, actual.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                CharacterStoryQuestObjectiveDefinition objective = actual[i];
                Assert.AreEqual(expected[i].id, objective.ObjectiveId);
                Assert.AreEqual(questId, objective.QuestId);
                Assert.AreEqual(expected[i].condition, objective.ConditionType);
                Assert.AreEqual(expected[i].required, objective.RequiredValue);
                Assert.AreEqual(expected[i].order, objective.DisplayOrder);
                Assert.IsEmpty(objective.TargetIds);
                Assert.IsTrue(objective.Enabled);
            }
        }

        private static TableDataDiagnosticLog Validate(object first, object second)
        {
            object quest = Activator.CreateInstance(QuestRowType, true);
            Set(quest, "Line", 2);
            IList rewards = (IList)Get(quest, "Rewards");
            rewards.Add(first); rewards.Add(second);
            var log = new TableDataDiagnosticLog();
            ValidateRewardsMethod.Invoke(null, new object[]
            {
                quest,
                new HashSet<string>(new[] { "jewel", "gold" }, StringComparer.Ordinal),
                new HashSet<string>(new[] { "50001" }, StringComparer.Ordinal),
                log,
            });
            return log;
        }

        private static object NewReward(int slot, CharacterStoryQuestRewardType type, string typeText,
            string targetId, int amount)
        {
            object reward = Activator.CreateInstance(RewardRowType, true);
            Set(reward, "Slot", slot); Set(reward, "Type", type); Set(reward, "TypeText", typeText);
            Set(reward, "TargetId", targetId); Set(reward, "Amount", amount);
            return reward;
        }

        private static object Get(object target, string name) => target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
    }
}
