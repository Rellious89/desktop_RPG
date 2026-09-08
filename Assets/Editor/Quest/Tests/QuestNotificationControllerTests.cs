using System;
using System.Collections.Generic;
using System.Reflection;
using Character;
using Common;
using NUnit.Framework;
using Quest;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace QuestEditorTests
{
    public sealed class QuestNotificationControllerTests
    {
        private static readonly FieldInfo SaveDataField = typeof(SaveSystem).GetField("data",
            BindingFlags.NonPublic | BindingFlags.Static);
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private object originalSaveData;

        [SetUp]
        public void SetUp()
        {
            originalSaveData = SaveDataField.GetValue(null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object item in created)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
            SaveDataField.SetValue(null, originalSaveData);
        }

        [Test]
        public void ReadyQuery_UsesOrdinalIds_AndExcludesUnownedFinalAndInvalidStates()
        {
            CharacterCatalog characters = CharacterCatalogWith("F", "A", "C", "Missing", "Done", "Progress");
            CharacterStoryQuestCatalog quests = QuestCatalogWith(
                Quest("q-a", "A"), Quest("q-c", "C"), Quest("q-f", "F"), Quest("q-missing", "Missing"),
                Quest("q-done", "Done"), Quest("q-progress", "Progress"));
            var data = new SaveData
            {
                characters = new List<CharacterSaveState>
                {
                    Owned("F"), Owned("A"), Owned("C"), Owned("Done"), Owned("Progress"),
                },
                characterStoryQuests = new List<CharacterStoryQuestSaveState>
                {
                    Ready("F", "q-f"), Ready("C", "q-c"), Ready("A", "q-a"), Ready("Missing", "q-missing"),
                    new CharacterStoryQuestSaveState { characterId = "Done", activeQuestId = string.Empty, readyToComplete = false, graduated = true },
                    new CharacterStoryQuestSaveState { characterId = "Progress", activeQuestId = "q-progress", readyToComplete = false },
                },
            };

            CollectionAssert.AreEqual(new[] { "A", "C", "F" },
                CharacterStoryQuestReadyQuery.GetReadyCharacterIds(data, characters, quests));

            data.characterStoryQuests.Find(state => state.characterId == "A").readyToComplete = false;
            CollectionAssert.AreEqual(new[] { "C", "F" },
                CharacterStoryQuestReadyQuery.GetReadyCharacterIds(data, characters, quests),
                "완료 확정 뒤 다음 클릭 대상은 남은 첫 Ordinal ID여야 한다.");
        }

        [Test]
        public void Controller_RefreshesVisualAndCount_AndDoesNotDuplicateEventSubscription()
        {
            CharacterCatalog characters = CharacterCatalogWith("A");
            CharacterStoryQuestCatalog quests = QuestCatalogWith(Quest("q-a", "A"));
            var data = new SaveData
            {
                characters = new List<CharacterSaveState> { Owned("A") },
                partyCharacterIds = new List<string> { "A" },
                characterStoryQuests = new List<CharacterStoryQuestSaveState> { Ready("A", "q-a") },
            };
            SaveDataField.SetValue(null, data);

            GameObject root = new GameObject("QuestNotification"); created.Add(root);
            GameObject message = new GameObject("sp_messageBox"); created.Add(message);
            var count = new GameObject("lb_count", typeof(TextMeshProUGUI)).GetComponent<TMP_Text>(); created.Add(count.gameObject);
            var button = message.AddComponent<Button>();
            QuestNotificationController controller = root.AddComponent<QuestNotificationController>();
            Set(controller, "characterCatalog", characters); Set(controller, "questCatalog", quests);
            Set(controller, "messageBox", message); Set(controller, "countText", count); Set(controller, "messageButton", button);
            Invoke(controller, "OnEnable");

            controller.Refresh();
            Assert.IsTrue(message.activeSelf); Assert.AreEqual("1", count.text); Assert.AreEqual("A", controller.CurrentTargetId);
            data.characterStoryQuests[0].readyToComplete = false;
            RaiseQuestStateChanged("A");
            Assert.IsFalse(message.activeSelf); Assert.AreEqual("0", count.text);

            Invoke(controller, "OnDisable"); Invoke(controller, "OnEnable");
            Assert.AreEqual(1, ListenerCount(controller), "메시지 영역을 숨겼거나 재활성화해도 구독이 중복되면 안 됩니다.");
        }

        [Test]
        public void Controller_OnlyCountsReadyCharactersThatHaveFixedPartyCards()
        {
            CharacterCatalog characters = CharacterCatalogWith("A", "B");
            CharacterStoryQuestCatalog quests = QuestCatalogWith(Quest("q-a", "A"), Quest("q-b", "B"));
            SaveDataField.SetValue(null, new SaveData
            {
                characters = new List<CharacterSaveState> { Owned("A"), Owned("B") },
                partyCharacterIds = new List<string> { string.Empty, "B", string.Empty },
                characterStoryQuests = new List<CharacterStoryQuestSaveState> { Ready("A", "q-a"), Ready("B", "q-b") },
            });
            GameObject root = new GameObject("QuestNotification"); created.Add(root);
            GameObject message = new GameObject("sp_messageBox"); created.Add(message);
            var count = new GameObject("lb_count", typeof(TextMeshProUGUI)).GetComponent<TMP_Text>(); created.Add(count.gameObject);
            var button = message.AddComponent<Button>();
            QuestNotificationController controller = root.AddComponent<QuestNotificationController>();
            Set(controller, "characterCatalog", characters); Set(controller, "questCatalog", quests);
            Set(controller, "messageBox", message); Set(controller, "countText", count); Set(controller, "messageButton", button);

            controller.Refresh();

            Assert.AreEqual(1, controller.ReadyCount);
            Assert.AreEqual("B", controller.CurrentTargetId);
        }

        private CharacterCatalog CharacterCatalogWith(params string[] ids)
        {
            var catalog = Create<CharacterCatalog>();
            var values = new List<CharacterDefinition>();
            foreach (string id in ids)
            {
                CharacterDefinition definition = Create<CharacterDefinition>();
                Set(definition, "characterId", id); values.Add(definition);
            }
            Set(catalog, "characters", values); catalog.MarkDirty();
            return catalog;
        }

        private CharacterStoryQuestCatalog QuestCatalogWith(params CharacterStoryQuestDefinition[] entries)
        {
            var catalog = Create<CharacterStoryQuestCatalog>();
            Set(catalog, "quests", new List<CharacterStoryQuestDefinition>(entries)); catalog.MarkDirty();
            return catalog;
        }

        private CharacterStoryQuestDefinition Quest(string id, string characterId)
        {
            var quest = Create<CharacterStoryQuestDefinition>();
            Set(quest, "questId", id); Set(quest, "characterId", characterId); Set(quest, "enabled", true);
            return quest;
        }

        private static CharacterSaveState Owned(string id) => new CharacterSaveState { characterId = id, level = 1 };
        private static CharacterStoryQuestSaveState Ready(string characterId, string questId) =>
            new CharacterStoryQuestSaveState { characterId = characterId, activeQuestId = questId, readyToComplete = true };
        private T Create<T>() where T : ScriptableObject { T item = ScriptableObject.CreateInstance<T>(); created.Add(item); return item; }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(object target, string method) => target.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
        private static void RaiseQuestStateChanged(string id)
        {
            var listeners = typeof(CharacterStoryQuestService)
                .GetField("QuestStateChanged", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) as Action<string>;
            if (listeners == null) throw new AssertionException("QuestStateChanged listener가 없습니다.");
            listeners.Invoke(id);
        }
        private static int ListenerCount(QuestNotificationController controller)
        {
            Delegate listeners = (Delegate)typeof(CharacterStoryQuestService)
                .GetField("QuestStateChanged", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            int count = 0;
            if (listeners != null)
                foreach (Delegate listener in listeners.GetInvocationList()) if ((object)listener.Target == controller) count++;
            return count;
        }
    }
}
