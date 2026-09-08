using System.Collections.Generic;
using System.Reflection;
using Character;
using CharacterArchive;
using Common;
using Inventory;
using NUnit.Framework;
using Quest;
using TMPro;
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
                Assert.NotNull(card.ClearSprite);
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
                Assert.IsTrue(panel.IsDetailOpen, "프리팹은 독립 상세 패널 참조를 직렬화해야 합니다.");
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
        public void Card_AllClearPreservesCharacterInfoAndClearAppearanceWhileHidingQuestControls()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CardPath);
            CharacterDefinition character = ScriptableObject.CreateInstance<CharacterDefinition>();
            created.Add(character);
            try
            {
                SerializedObject characterSerialized = new SerializedObject(character);
                characterSerialized.FindProperty("characterId").stringValue = "CatKnight";
                characterSerialized.ApplyModifiedPropertiesWithoutUndo();
                CharacterQuestCardView card = root.GetComponent<CharacterQuestCardView>();
                CharacterStoryQuestSnapshot graduated = new CharacterStoryQuestSnapshot("CatKnight", string.Empty, false, true,
                    new List<string>(), new Dictionary<string, int>());

                card.Bind(character, 7, null, new List<CharacterStoryQuestObjectiveDefinition>(), graduated, false, _ => { }, (_, __) => { });
                card.SetSelected(true);

                Assert.IsTrue(card.IsAllClear);
                Assert.AreSame(card.ClearSprite, root.GetComponent<Image>().sprite, "완료 카드는 선택 상태보다 clear 외형이 우선한다.");
                Assert.IsFalse(FindDescendant(root.transform, "lb_QuestName").gameObject.activeSelf);
                Assert.IsTrue(FindDescendant(root.transform, "lb_QuestAllClear").gameObject.activeSelf);
                Assert.IsFalse(FindDescendant(root.transform, "QuestReward").gameObject.activeSelf);
                Assert.IsFalse(FindDescendant(root.transform, "btn_QuestComplete").gameObject.activeSelf);
                Assert.AreEqual("Lv. 7", FindDescendant(root.transform, "lb_Level").GetComponent<TMP_Text>().text);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void Card_HidesQuestTitleAndUsesObjectiveTextForProgress()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CardPath);
            CharacterStoryQuestDefinition quest = ScriptableObject.CreateInstance<CharacterStoryQuestDefinition>();
            created.Add(quest);
            CharacterStoryQuestObjectiveDefinition objective = Objective("objective", 10);
            try
            {
                Set(quest, "questId", "Q");
                CharacterQuestCardView card = root.GetComponent<CharacterQuestCardView>();
                var snapshot = new CharacterStoryQuestSnapshot("CatKnight", "Q", false, false,
                    new List<string>(), new Dictionary<string, int> { { "objective", 4 } });

                card.Bind(null, 1, quest, new[] { objective }, snapshot, false, _ => { }, (_, __) => { });

                Assert.IsFalse(FindDescendant(root.transform, "lb_QuestName").gameObject.activeSelf,
                    "메인 카드에는 퀘스트 제목을 표시하지 않습니다.");
                TMP_Text objectiveText = FindDescendant(root.transform, "lb_QuestName_Objective").GetComponent<TMP_Text>();
                StringAssert.Contains("4/10", objectiveText.text);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void Detail_AllClearHidesCurrentAndShowsConfiguredAllClearText()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PanelPath);
            try
            {
                CharacterStoryQuestDetailView detail = root.GetComponentInChildren<CharacterStoryQuestDetailView>(true);
                Assert.NotNull(detail);
                var graduated = new CharacterStoryQuestSnapshot("CatKnight", string.Empty, false, true,
                    new List<string>(), new Dictionary<string, int>());

                detail.Bind("CatKnight", null, new List<CharacterStoryQuestObjectiveDefinition>(), graduated, false,
                    (_, __) => { });

                Transform current = root.transform.Find("Sub_Panel/QuestInfo/QuestInfo/Current");
                Transform allClear = root.transform.Find("Sub_Panel/QuestInfo/QuestInfo/lb_AllClear");
                Assert.NotNull(current); Assert.NotNull(allClear);
                Assert.IsFalse(current.gameObject.activeSelf);
                Assert.IsTrue(allClear.gameObject.activeSelf);
                Assert.IsTrue(detail.IsAllClear);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void AllClearCard_RemainsSelectableAndReopensOnlyItsClosedDetailPanel()
        {
            FieldInfo dataField = typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo instanceField = typeof(CharacterStoryQuestService).GetField("<Instance>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Static);
            object originalData = dataField.GetValue(null);
            object originalInstance = instanceField.GetValue(null);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            QuestPanel panel = Object.FindObjectOfType<QuestPanel>(true);
            CharacterStoryQuestService service = Object.FindObjectOfType<CharacterStoryQuestService>(true);
            Assert.NotNull(panel); Assert.NotNull(service);
            try
            {
                dataField.SetValue(null, new SaveData
                {
                    characters = new List<CharacterSaveState> { new CharacterSaveState { characterId = "CatKnight", level = 7 } },
                    partyCharacterIds = new List<string> { "CatKnight", string.Empty, string.Empty },
                    characterStoryQuests = new List<CharacterStoryQuestSaveState>
                    {
                        new CharacterStoryQuestSaveState { characterId = "CatKnight", graduated = true },
                    },
                });
                instanceField.SetValue(null, service);

                Assert.IsTrue(panel.OpenForCharacter("CatKnight"));
                Assert.AreEqual("CatKnight", panel.SelectedCharacterId);
                Assert.IsTrue(panel.DetailView.IsAllClear);

                Button detailClose = FindDescendant(panel.transform.Find("Sub_Panel/QuestInfo"), "btn_close").GetComponent<Button>();
                detailClose.onClick.Invoke();
                Assert.IsTrue(panel.gameObject.activeSelf, "상세 닫기는 pn_Quest 자체를 닫지 않습니다.");
                Assert.IsFalse(panel.IsDetailOpen);

                CharacterQuestCardView card = null;
                foreach (CharacterQuestCardView candidate in panel.GetComponentsInChildren<CharacterQuestCardView>(true))
                    if (candidate.CharacterId == "CatKnight") { card = candidate; break; }
                Assert.NotNull(card);
                card.OnPointerClick(null);
                Assert.IsTrue(panel.IsDetailOpen, "카드를 다시 고르면 Sub_Panel만 다시 열립니다.");
                Assert.IsTrue(panel.DetailView.IsAllClear);
            }
            finally
            {
                if (panel != null && panel.gameObject.activeSelf) panel.Close();
                instanceField.SetValue(null, originalInstance);
                dataField.SetValue(null, originalData);
            }
        }

        [Test]
        public void Card_ItemRewardShowsDistinctValidItemKindsWithoutBindingTheTooltipSlot()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CardPath);
            ItemDefinition first = ScriptableObject.CreateInstance<ItemDefinition>(); created.Add(first);
            ItemDefinition second = ScriptableObject.CreateInstance<ItemDefinition>(); created.Add(second);
            CharacterStoryQuestDefinition quest = ScriptableObject.CreateInstance<CharacterStoryQuestDefinition>(); created.Add(quest);
            try
            {
                Set(first, "itemId", "A"); Set(second, "itemId", "B");
                Set(quest, "questId", "Q");
                Set(quest, "rewards", new List<CharacterStoryQuestRewardDefinition>
                {
                    Reward(first, 2), Reward(second, 5), Reward(first, 9), new CharacterStoryQuestRewardDefinition(),
                });
                CharacterQuestCardView card = root.GetComponent<CharacterQuestCardView>();
                Transform reward = FindDescendant(root.transform, "Reward_Item");
                Image fixedIcon = FindDescendant(reward, "sp_ItemIcon").GetComponent<Image>();
                Sprite configuredIcon = fixedIcon.sprite;
                card.Bind(null, 1, quest, new List<CharacterStoryQuestObjectiveDefinition>(),
                    CharacterStoryQuestSnapshot.Empty(string.Empty), false, _ => { }, (_, __) => { });

                InventorySlotView slot = reward.GetComponentInChildren<InventorySlotView>(true);
                Assert.AreEqual("2", FindDescendant(reward, "lb_RewardValue").GetComponent<TMP_Text>().text);
                Assert.IsFalse(slot.enabled, "카드는 실제 아이템 bind 및 hover tooltip을 사용하지 않는다.");
                Assert.IsNull(slot.Definition);
                Assert.AreSame(configuredIcon, fixedIcon.sprite, "프리팹의 상징 아이콘은 보상 아이템 아이콘으로 바꾸지 않는다.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
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

            CharacterStoryQuestObjectiveDefinition monster = Objective("monster", 30);
            Set(monster, "conditionType", CharacterStoryQuestConditionType.MonsterDefeatCount);
            string cardText = CharacterStoryQuestPresentation.ObjectiveText(monster, snapshot);
            StringAssert.Contains("30", cardText);
            StringAssert.DoesNotContain("Defeat", cardText);
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

        private static CharacterStoryQuestRewardDefinition Reward(ItemDefinition item, int amount)
        {
            var reward = new CharacterStoryQuestRewardDefinition();
            Set(reward, "rewardType", CharacterStoryQuestRewardType.Item);
            Set(reward, "item", item);
            Set(reward, "amount", amount);
            return reward;
        }

        private static void Set(object target, string property, object value)
        {
            if (target is CharacterStoryQuestRewardDefinition)
            {
                typeof(CharacterStoryQuestRewardDefinition).GetField(property,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(target, value);
                return;
            }
            var serialized = new SerializedObject((Object)target);
            SerializedProperty field = serialized.FindProperty(property);
            if (field.propertyType == SerializedPropertyType.String) field.stringValue = value as string;
            else if (field.propertyType == SerializedPropertyType.Integer) field.intValue = (int)value;
            else if (field.propertyType == SerializedPropertyType.Enum) field.enumValueIndex = (int)value;
            else if (field.propertyType == SerializedPropertyType.Generic)
            {
                target.GetType().GetField(property,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(target, value);
                return;
            }
            else field.objectReferenceValue = value as Object;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
