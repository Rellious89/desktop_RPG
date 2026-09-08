using System.Collections.Generic;
using System.Reflection;
using Character;
using CharacterArchive;
using Common;
using NUnit.Framework;
using Quest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace QuestEditorTests
{
    public sealed class QuestPanelTests
    {
        private const string CardPath = "Assets/Art/UI/Prefab/Quest/item_CharacterQuestInfo.prefab";
        private const string PanelPath = "Assets/Art/UI/Prefab/panel/pn_Quest.prefab";
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < created.Count; i++) if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [Test]
        public void CardPrefab_HasRequiredReferencesAndSerializedSelectionSprites()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CardPath);
            try
            {
                CharacterQuestCardView card = root.GetComponent<CharacterQuestCardView>();
                Assert.NotNull(card);
                Assert.IsTrue(card.HasRequiredReferences);
                Assert.NotNull(card.DefaultSprite);
                Assert.NotNull(card.SelectedSprite);
                Assert.AreNotSame(card.DefaultSprite, card.SelectedSprite);

                Image image = root.GetComponent<Image>();
                card.SetSelected(false); Assert.AreSame(card.DefaultSprite, image.sprite);
                card.SetSelected(true); Assert.AreSame(card.SelectedSprite, image.sprite);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void PanelPrefab_WiresThreeFixedSlotsAndIndependentDetailWithoutTouchingGeneralQuestDummy()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PanelPath);
            try
            {
                QuestPanel panel = root.GetComponent<QuestPanel>();
                Assert.NotNull(panel);
                Assert.IsTrue(panel.HasRequiredReferences);
                Assert.AreEqual(3, panel.SlotCount);
                Assert.AreEqual(3, root.GetComponentsInChildren<CharacterQuestCardView>(true).Length);
                Assert.AreEqual(1, root.GetComponentsInChildren<CharacterStoryQuestDetailView>(true).Length);
                Assert.AreEqual(0, root.GetComponentsInChildren<CharacterStoryQuestUiController>(true).Length,
                    "pn_Quest 상세는 용병명부 전체목록/페이지 계약을 상속하지 않습니다.");

                Transform generalQuest = root.transform.Find("Main_Panel/Quest");
                Transform progress = root.transform.Find("Main_Panel/Progress");
                Assert.NotNull(generalQuest); Assert.NotNull(progress);
                Assert.AreEqual(0, generalQuest.GetComponentsInChildren<CharacterQuestCardView>(true).Length);
                Assert.AreEqual(0, progress.GetComponentsInChildren<CharacterQuestCardView>(true).Length);
                Assert.AreEqual(0, generalQuest.GetComponentsInChildren<CharacterStoryQuestDetailView>(true).Length);
                Assert.AreEqual(0, progress.GetComponentsInChildren<CharacterStoryQuestDetailView>(true).Length);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void ScenePanel_UsesTheExistingSharedItemTooltip()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            QuestPanel panel = Object.FindObjectOfType<QuestPanel>(true);
            Assert.NotNull(panel);
            Assert.NotNull(panel.DetailView);
            Assert.NotNull(panel.DetailView.RewardItemSlot);
            Assert.NotNull(ItemTooltipController.FindSharedController(panel.DetailView.RewardItemSlot));
        }

        [Test]
        public void QuestNotification_OpensIndependentPanelAndSelectsItsReadyPartyCard()
        {
            FieldInfo dataField = typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo instanceField = typeof(CharacterStoryQuestService).GetField("<Instance>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Static);
            object originalData = dataField.GetValue(null);
            object originalInstance = instanceField.GetValue(null);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            QuestPanel panel = Object.FindObjectOfType<QuestPanel>(true);
            QuestNotificationController notification = Object.FindObjectOfType<QuestNotificationController>(true);
            CharacterStoryQuestService service = Object.FindObjectOfType<CharacterStoryQuestService>(true);
            CharacterStoryQuestCatalog catalog = AssetDatabase.LoadAssetAtPath<CharacterStoryQuestCatalog>(
                "Assets/Generated/TableData/CharacterStoryQuest/CharacterStoryQuestCatalog.asset");
            CharacterStoryQuestDefinition quest = catalog.FindRoot("CatKnight");
            Assert.NotNull(panel); Assert.NotNull(notification); Assert.NotNull(service); Assert.NotNull(quest);
            try
            {
                dataField.SetValue(null, new SaveData
                {
                    characters = new List<CharacterSaveState> { new CharacterSaveState { characterId = "CatKnight", level = 7 } },
                    partyCharacterIds = new List<string> { string.Empty, "CatKnight", string.Empty },
                    characterStoryQuests = new List<CharacterStoryQuestSaveState>
                    {
                        new CharacterStoryQuestSaveState
                        {
                            characterId = "CatKnight", activeQuestId = quest.QuestId, readyToComplete = true,
                        },
                    },
                });
                instanceField.SetValue(null, service);

                notification.Refresh();
                notification.OpenCurrentReadyQuest();

                Assert.IsTrue(panel.gameObject.activeSelf, "필드 모드와 무관하게 알림 딥링크가 pn_Quest를 열어야 합니다.");
                Assert.AreEqual("CatKnight", panel.SelectedCharacterId);
                Assert.AreEqual("CatKnight", panel.DetailView.CharacterId);
                Assert.AreEqual(quest.QuestId, panel.DetailView.QuestId);
                Transform characterQuest = panel.transform.Find("Main_Panel/CharacterQuest");
                AssertFixedSlot(characterQuest, 1, false);
                AssertFixedSlot(characterQuest, 2, true);
                AssertFixedSlot(characterQuest, 3, false);
            }
            finally
            {
                if (panel != null && panel.gameObject.activeSelf) panel.Close();
                instanceField.SetValue(null, originalInstance);
                dataField.SetValue(null, originalData);
            }
        }

        [Test]
        public void Presentation_AveragesMultipleObjectiveProgress()
        {
            CharacterStoryQuestObjectiveDefinition first = Objective("o1", 10);
            CharacterStoryQuestObjectiveDefinition second = Objective("o2", 20);
            var snapshot = new CharacterStoryQuestSnapshot("A", "q", false, false, new List<string>(),
                new Dictionary<string, int> { { "o1", 5 }, { "o2", 20 } });

            Assert.AreEqual(.75f,
                CharacterStoryQuestPresentation.OverallProgress(new[] { first, second }, snapshot), .0001f);
            StringAssert.Contains("5/10", CharacterStoryQuestPresentation.ObjectiveText(first, snapshot));
            StringAssert.Contains("20/20", CharacterStoryQuestPresentation.ObjectiveText(second, snapshot));
        }

        private CharacterStoryQuestObjectiveDefinition Objective(string id, int required)
        {
            CharacterStoryQuestObjectiveDefinition result = ScriptableObject.CreateInstance<CharacterStoryQuestObjectiveDefinition>();
            created.Add(result);
            SerializedObject serialized = new SerializedObject(result);
            serialized.FindProperty("objectiveId").stringValue = id;
            serialized.FindProperty("questId").stringValue = "q";
            serialized.FindProperty("conditionType").enumValueIndex = (int)CharacterStoryQuestConditionType.StaminaSpent;
            serialized.FindProperty("requiredValue").intValue = required;
            serialized.FindProperty("enabled").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return result;
        }

        private static void AssertFixedSlot(Transform characterQuest, int oneBasedIndex, bool hasCharacter)
        {
            Transform slot = FindDescendant(characterQuest, "item_CharacterSlot" + oneBasedIndex);
            CharacterQuestCardView card = slot.GetComponentInChildren<CharacterQuestCardView>(true);
            Transform empty = FindDescendantPrefix(slot, "item_CharacterEmpty");
            Assert.AreEqual(hasCharacter, card.gameObject.activeSelf, "고정 슬롯 " + oneBasedIndex + " 카드 상태");
            Assert.AreEqual(!hasCharacter, empty.gameObject.activeSelf, "고정 슬롯 " + oneBasedIndex + " 빈칸 상태");
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == name) return all[i];
            Assert.Fail(root.name + " 아래에서 찾지 못했습니다: " + name);
            return null;
        }

        private static Transform FindDescendantPrefix(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name.StartsWith(name, System.StringComparison.Ordinal)) return all[i];
            Assert.Fail(root.name + " 아래에서 찾지 못했습니다: " + name);
            return null;
        }
    }
}
