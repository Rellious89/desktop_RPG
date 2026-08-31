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
        public void LiveCsv_GeneratesExpectedRewardsForAllThreeQuests()
        {
            TableDataDiagnosticLog log = CharacterStoryQuestTablePipeline.ValidateOnly();
            Assert.IsFalse(log.HasErrors);

            AssertRewards("CatKnight_10001", (CharacterStoryQuestRewardType.Currency, "jewel", 100));
            AssertRewards("CatKnight_10002", (CharacterStoryQuestRewardType.Currency, "jewel", 150));
            AssertRewards("CatKnight_10003",
                (CharacterStoryQuestRewardType.Currency, "jewel", 200),
                (CharacterStoryQuestRewardType.Item, "50001", 3));
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
