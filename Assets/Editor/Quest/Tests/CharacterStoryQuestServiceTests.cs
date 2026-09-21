using System.Collections.Generic;
using System.Reflection;
using Common;
using Character;
using Inventory;
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
        public void MissingCatalogOrRoster_IsReportedAsInvalidWiring()
        {
            var host = new GameObject("quest-service-unwired-test"); created.Add(host);
            var service = host.AddComponent<CharacterStoryQuestService>();
            Assert.IsFalse(service.HasRequiredReferences);
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

        [Test]
        public void ReadyNotification_IsPublishedOnceOnlyAfterCallerConfirmsSave()
        {
            CharacterStoryQuestDefinition quest = Quest("Q1", "CatKnight", "", true);
            CharacterStoryQuestObjectiveDefinition defeats = Objective(
                "O1", "Q1", CharacterStoryQuestConditionType.MonsterDefeatCount, 2);
            CharacterStoryQuestService service = Service(new[] { quest }, new[] { defeats });
            var data = new SaveData
            {
                characterStoryQuests = new List<CharacterStoryQuestSaveState>
                {
                    new CharacterStoryQuestSaveState { characterId = "CatKnight", activeQuestId = "Q1" },
                },
            };

            int notifications = 0;
            int stateChanges = 0;
            string notifiedCharacterId = null;
            System.Action<string> handler = id => { notifications++; notifiedCharacterId = id; };
            CharacterStoryQuestService.QuestBecameReadyToComplete += handler;
            System.Action<string> stateHandler = _ => stateChanges++;
            CharacterStoryQuestService.QuestStateChanged += stateHandler;
            try
            {
                CharacterStoryQuestMutationReceipt first = service.ApplyDefeatWithoutSave(
                    data, "CatKnight", "Monster_1", 0);
                Assert.IsFalse(service.NotifyReadyAfterExternalSave(first));
                Assert.AreEqual(0, notifications, "목표 중간 진행에는 알림이 없어야 합니다.");
                Assert.AreEqual(1, stateChanges, "저장 성공한 중간 진행은 열린 퀘스트 UI가 다시 그릴 수 있어야 합니다.");

                CharacterStoryQuestMutationReceipt completed = service.ApplyDefeatWithoutSave(
                    data, "CatKnight", "Monster_1", 0);
                Assert.AreEqual(0, notifications, "저장 성공 확정 전에는 알림을 발행하지 않습니다.");
                Assert.IsTrue(service.NotifyReadyAfterExternalSave(completed));
                Assert.AreEqual(1, notifications);
                Assert.AreEqual(2, stateChanges, "각 저장 성공 뒤 HUD와 열린 패널이 다시 조회할 상태 알림이 필요합니다.");
                Assert.AreEqual("CatKnight", notifiedCharacterId);
                Assert.IsFalse(service.NotifyReadyAfterExternalSave(completed), "같은 저장 영수증은 중복 알림을 만들지 않습니다.");
                Assert.AreEqual(1, notifications);
                Assert.AreEqual(2, stateChanges);
            }
            finally
            {
                CharacterStoryQuestService.QuestBecameReadyToComplete -= handler;
                CharacterStoryQuestService.QuestStateChanged -= stateHandler;
            }
        }

        [Test]
        public void RolledBackReadyMutation_DoesNotPublishNotification()
        {
            CharacterStoryQuestDefinition quest = Quest("Q1", "CatKnight", "", true);
            CharacterStoryQuestObjectiveDefinition defeats = Objective(
                "O1", "Q1", CharacterStoryQuestConditionType.MonsterDefeatCount, 1);
            CharacterStoryQuestService service = Service(new[] { quest }, new[] { defeats });
            var data = new SaveData
            {
                characterStoryQuests = new List<CharacterStoryQuestSaveState>
                {
                    new CharacterStoryQuestSaveState { characterId = "CatKnight", activeQuestId = "Q1" },
                },
            };

            int notifications = 0;
            System.Action<string> handler = _ => notifications++;
            CharacterStoryQuestService.QuestBecameReadyToComplete += handler;
            try
            {
                CharacterStoryQuestMutationReceipt receipt = service.ApplyDefeatWithoutSave(
                    data, "CatKnight", "Monster_1", 0);
                service.Rollback(receipt);

                Assert.IsFalse(service.NotifyReadyAfterExternalSave(receipt));
                Assert.AreEqual(0, notifications);
                Assert.IsFalse(data.characterStoryQuests[0].readyToComplete);
            }
            finally
            {
                CharacterStoryQuestService.QuestBecameReadyToComplete -= handler;
            }
        }

        [Test]
        public void GlobalAction_OnlyAdvancesMatchingActiveObjective()
        {
            CharacterStoryQuestDefinition quest = Quest("Q1", "CatKnight", "", true);
            CharacterStoryQuestObjectiveDefinition purchase = Objective(
                "O1", "Q1", CharacterStoryQuestConditionType.ItemPurchaseCount, 2, "50005");
            CharacterStoryQuestService service = Service(new[] { quest }, new[] { purchase });
            var data = new SaveData
            {
                characterStoryQuests = new List<CharacterStoryQuestSaveState>
                {
                    new CharacterStoryQuestSaveState { characterId = "CatKnight", activeQuestId = "Q1" },
                },
            };

            CharacterStoryQuestMutationReceipt ignored = service.ApplyGlobalActionWithoutSave(
                data, CharacterStoryQuestConditionType.ItemPurchaseCount, "50006");
            Assert.IsNotNull(ignored);
            Assert.IsEmpty(data.characterStoryQuests[0].objectiveProgress);

            service.ApplyGlobalActionWithoutSave(
                data, CharacterStoryQuestConditionType.ItemPurchaseCount, "50005", 3);
            CharacterStoryQuestSaveState state = data.characterStoryQuests[0];
            Assert.AreEqual(2, state.objectiveProgress[0].progress, "목표값에서 포화해야 합니다.");
            Assert.IsTrue(state.readyToComplete);
        }

        [Test]
        public void StateEvaluation_RecoversCompletedBuildingAndPartyMembership()
        {
            CharacterStoryQuestDefinition quest = Quest("Q1", "CatKnight", "", true);
            CharacterStoryQuestObjectiveDefinition building = Objective(
                "O1", "Q1", CharacterStoryQuestConditionType.BuildingCompleted, 1, "1");
            CharacterStoryQuestObjectiveDefinition party = Objective(
                "O2", "Q1", CharacterStoryQuestConditionType.PartyContainsCharacter, 1, "ElfArcher");
            CharacterStoryQuestService service = Service(new[] { quest }, new[] { building, party });
            var data = new SaveData
            {
                buildingConstructions = new List<BuildingConstructionSaveState>
                {
                    new BuildingConstructionSaveState { buildingId = "1", completionNotified = true },
                },
                partyCharacterIds = new List<string> { "CatKnight", "ElfArcher" },
                characterStoryQuests = new List<CharacterStoryQuestSaveState>
                {
                    new CharacterStoryQuestSaveState { characterId = "CatKnight", activeQuestId = "Q1" },
                },
            };

            CharacterStoryQuestMutationReceipt receipt = service.EvaluateStateObjectivesWithoutSave(data);

            Assert.IsNotNull(receipt);
            Assert.IsTrue(data.characterStoryQuests[0].readyToComplete);
            Assert.AreEqual(2, data.characterStoryQuests[0].objectiveProgress.Count);
        }

        [Test]
        public void ConfirmingPreviousQuest_EvaluatesNextPartyMembershipImmediately()
        {
            CharacterStoryQuestDefinition recoveryComplete = Quest("Q180", "CatKnight", "", false);
            CharacterStoryQuestDefinition partyJoin = Quest("Q190", "CatKnight", "Q180", true);
            CharacterStoryQuestObjectiveDefinition recoveryObjective = Objective(
                "O180", "Q180", CharacterStoryQuestConditionType.CharacterRecoveryComplete, 1, "RabbitHealer");
            CharacterStoryQuestObjectiveDefinition partyObjective = Objective(
                "O190", "Q190", CharacterStoryQuestConditionType.PartyContainsCharacter, 1, "RabbitHealer");
            CharacterStoryQuestService service = Service(
                new[] { recoveryComplete, partyJoin }, new[] { recoveryObjective, partyObjective });
            var data = new SaveData
            {
                partyCharacterIds = new List<string> { "CatKnight", "RabbitHealer" },
                characterStoryQuests = new List<CharacterStoryQuestSaveState>
                {
                    new CharacterStoryQuestSaveState
                    {
                        characterId = "CatKnight", activeQuestId = "Q180", readyToComplete = true,
                    },
                },
            };

            MethodInfo confirm = typeof(CharacterStoryQuestService).GetMethod(
                "ConfirmWithoutSave", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsTrue((bool)confirm.Invoke(service, new object[] { data, "CatKnight" }));

            CharacterStoryQuestSaveState state = data.characterStoryQuests[0];
            Assert.AreEqual("Q190", state.activeQuestId);
            Assert.IsTrue(state.readyToComplete);
            Assert.AreEqual(1, state.objectiveProgress.Find(p => p.objectiveId == "O190").progress);
        }

        [Test]
        public void TutorialStaminaItemUse_WhenTargetAlreadyFull_BecomesReadyWithoutConsumingItem()
        {
            CharacterStoryQuestDefinition quest = Quest("TutorialUse", "CatKnight", "", false);
            Set(quest, "tutorialStep", true);
            CharacterStoryQuestObjectiveDefinition objective = Objective("UseCat", "TutorialUse",
                CharacterStoryQuestConditionType.ItemUseCount, 1, "50007@CatKnight");
            CharacterStoryQuestService service = Service(new[] { quest }, new[] { objective });

            CharacterDefinition cat = Create<CharacterDefinition>();
            Set(cat, "characterId", "CatKnight");
            Set(cat, "maxStamina", 80);
            CharacterCatalog characters = Create<CharacterCatalog>();
            Set(characters, "characters", new List<CharacterDefinition> { cat });
            var rosterHost = new GameObject("quest-cat-roster-test"); created.Add(rosterHost);
            rosterHost.SetActive(false);
            CharacterRoster roster = rosterHost.AddComponent<CharacterRoster>();
            Set(roster, "catalog", characters);
            Set(service, "roster", roster);

            ItemDefinition staminaItem = Create<ItemDefinition>();
            Set(staminaItem, "itemId", "50007");
            Set(staminaItem, "useEffectType", ItemUseEffectType.RestoreStamina);
            Set(staminaItem, "useEffectValue", 30);
            var inventoryHost = new GameObject("quest-cat-inventory-test"); created.Add(inventoryHost);
            InventoryManager inventory = inventoryHost.AddComponent<InventoryManager>();
            Set(inventory, "itemCatalog", new List<ItemDefinition> { staminaItem });
            typeof(InventoryManager).GetMethod("BuildDefinitionLookup", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(inventory, null);
            Set(service, "inventoryManager", inventory);

            var catState = new CharacterSaveState { characterId = "CatKnight", currentStamina = 79 };
            var questState = new CharacterStoryQuestSaveState
                { characterId = "CatKnight", activeQuestId = "TutorialUse" };
            var data = new SaveData
            {
                characters = new List<CharacterSaveState> { catState },
                characterStoryQuests = new List<CharacterStoryQuestSaveState> { questState },
            };

            service.EvaluateStateObjectivesWithoutSave(data);
            Assert.IsFalse(questState.readyToComplete, "아직 행동력이 부족하고 아이템도 쓰지 않았다면 진행하지 않습니다.");
            Assert.IsEmpty(questState.objectiveProgress);

            catState.currentStamina = 80;
            service.EvaluateStateObjectivesWithoutSave(data);
            Assert.IsTrue(questState.readyToComplete, "이미 가득 차서 아이템을 쓸 수 없다면 튜토리얼을 막지 않습니다.");
            Assert.AreEqual(1, questState.objectiveProgress[0].progress);

            Set(quest, "tutorialStep", false);
            questState.readyToComplete = false;
            questState.objectiveProgress.Clear();
            service.EvaluateStateObjectivesWithoutSave(data);
            Assert.IsFalse(questState.readyToComplete, "일반 아이템 사용 목표에는 최대 행동력 예외가 없습니다.");

            Set(quest, "tutorialStep", true);
            Set(staminaItem, "useEffectType", ItemUseEffectType.None);
            service.EvaluateStateObjectivesWithoutSave(data);
            Assert.IsFalse(questState.readyToComplete, "회복 아이템이 아닌 튜토리얼 목표는 자동 달성되지 않습니다.");
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

        private CharacterStoryQuestObjectiveDefinition Objective(
            string id, string questId, CharacterStoryQuestConditionType type, int required,
            params string[] targetIds)
        {
            var result = Create<CharacterStoryQuestObjectiveDefinition>();
            Set(result, "objectiveId", id);
            Set(result, "questId", questId);
            Set(result, "conditionType", type);
            Set(result, "requiredValue", required);
            Set(result, "targetIds", new List<string>(targetIds ?? new string[0]));
            Set(result, "enabled", true);
            return result;
        }

        private T Create<T>() where T : ScriptableObject { T result = ScriptableObject.CreateInstance<T>(); created.Add(result); return result; }
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
