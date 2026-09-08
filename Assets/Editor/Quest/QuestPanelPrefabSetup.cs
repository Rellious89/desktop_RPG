using Character;
using CharacterArchive;
using Common;
using Dungeon;
using Quest;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace QuestEditor
{
    /// <summary>독립 pn_Quest 카드/상세/씬 딥링크를 반복 실행 가능한 한 번의 편집으로 연결한다.</summary>
    public static class QuestPanelPrefabSetup
    {
        private const string CardPrefabPath = "Assets/Art/UI/Prefab/Quest/item_CharacterQuestInfo.prefab";
        private const string PanelPrefabPath = "Assets/Art/UI/Prefab/panel/pn_Quest.prefab";
        private const string NotificationPrefabPath = "Assets/Art/UI/Prefab/QuestNotification.prefab";
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";
        private const string CharacterCatalogPath = "Assets/Generated/TableData/Character/CharacterCatalog.asset";
        private const string QuestCatalogPath = "Assets/Generated/TableData/CharacterStoryQuest/CharacterStoryQuestCatalog.asset";
        private const string ObjectiveCatalogPath = "Assets/Generated/TableData/CharacterStoryQuestObjective/CharacterStoryQuestObjectiveCatalog.asset";
        private const string DefaultSpritePath = "Assets/Art/UI/PixelDesign/Pixel UI & HUD/Sprites/Panels/Blue/GridPanelInactive.png";
        private const string SelectedSpritePath = "Assets/Art/UI/PixelDesign/Pixel UI & HUD/Sprites/Panels/Blue/GridPanelInactive_Select.png";
        private const string ClearSpritePath = "Assets/Art/UI/PixelDesign/Pixel UI & HUD/Sprites/Panels/Blue/GridPanelInactive_AllClear.png";
        private const string ClearSelectedSpritePath = "Assets/Art/UI/PixelDesign/Pixel UI & HUD/Sprites/Panels/Blue/GridPanelInactive_AllClearSelect.png";
        private const string MonsterCatalogPath = "Assets/Generated/TableData/Monster/MonsterCatalog.asset";
        private const string DungeonCatalogPath = "Assets/Generated/TableData/Dungeon/DungeonCatalog.asset";

        [MenuItem("Tools/Keybuddy/Quest/Setup Independent Quest Panel", priority = 130)]
        public static void Setup()
        {
            SetupCardPrefab();
            SetupPanelPrefab();
            NormalizeNotificationPrefab();
            SetupScene();
            AssetDatabase.SaveAssets();
        }

        private static void SetupCardPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CardPrefabPath);
            try
            {
                CharacterQuestCardView view = root.GetComponent<CharacterQuestCardView>();
                if (view == null) view = root.AddComponent<CharacterQuestCardView>();
                Transform characterInfo = FindDescendant(root.transform, "CharacterInfo");
                Transform questInfo = FindDescendant(root.transform, "QuestInfo");
                Transform reward = FindDescendant(root.transform, "QuestReward");
                Transform currency = FindDescendant(reward, "Reward_Currency");
                Transform item = FindDescendant(reward, "Reward_Item");
                Transform complete = FindDescendant(root.transform, "btn_QuestComplete");
                TMP_Text questTitle = FindDescendant(questInfo, "lb_QuestName").GetComponent<TMP_Text>();
                DisableLocalizer(questTitle);

                SerializedObject serialized = new SerializedObject(view);
                Set(serialized, "selectionImage", root.GetComponent<Image>());
                Set(serialized, "defaultSprite", AssetDatabase.LoadAssetAtPath<Sprite>(DefaultSpritePath));
                Set(serialized, "selectedSprite", AssetDatabase.LoadAssetAtPath<Sprite>(SelectedSpritePath));
                Set(serialized, "clearSprite", AssetDatabase.LoadAssetAtPath<Sprite>(ClearSpritePath));
                Set(serialized, "clearSelectedSprite", AssetDatabase.LoadAssetAtPath<Sprite>(ClearSelectedSpritePath));
                Set(serialized, "portrait", FindDescendant(root.transform, "sp_portrait").GetComponent<Image>());
                Set(serialized, "levelText", FindDescendant(characterInfo, "lb_Level").GetComponent<TMP_Text>());
                Set(serialized, "nameText", FindDescendant(characterInfo, "lb_Name").GetComponent<TMP_Text>());
                Set(serialized, "questTitleText", questTitle);
                Set(serialized, "allClearText", FindDescendant(questInfo, "lb_QuestAllClear").GetComponent<TMP_Text>());
                Set(serialized, "objectiveLineTemplate", questTitle);
                Set(serialized, "rewardRoot", reward.gameObject);
                Set(serialized, "rewardCurrencyRoot", currency.gameObject);
                Set(serialized, "rewardCurrencyAmountText", FindDescendant(currency, "lb_RewardValue").GetComponent<TMP_Text>());
                Transform currencyIcon = FindDescendant(currency, "sp_currencyIcon (1)");
                Set(serialized, "rewardCurrencyIcon", currencyIcon.GetComponent<Image>());
                Set(serialized, "rewardCurrencyAnimator", currencyIcon.GetComponent<Animator>());
                Set(serialized, "rewardItemRoot", item.gameObject);
                Set(serialized, "rewardItemSlot", item.GetComponentInChildren<InventorySlotView>(true));
                Set(serialized, "rewardItemAmountText", FindDescendant(item, "lb_RewardValue").GetComponent<TMP_Text>());
                Set(serialized, "completeButton", complete.GetComponent<Button>());
                Set(serialized, "completeButtonText", FindDescendant(complete, "lb_QuestComplete").GetComponent<TMP_Text>());
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void SetupPanelPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PanelPrefabPath);
            try
            {
                QuestPanel panel = root.GetComponent<QuestPanel>();
                if (panel == null) panel = root.AddComponent<QuestPanel>();
                Transform characterQuest = Find(root.transform, "Main_Panel/CharacterQuest");
                SerializedObject serialized = new SerializedObject(panel);
                Set(serialized, "closeButton", Find(root.transform, "Main_Panel/bg/top/btn_close").GetComponent<Button>());
                Set(serialized, "characterCatalog", AssetDatabase.LoadAssetAtPath<CharacterCatalog>(CharacterCatalogPath));
                Set(serialized, "questCatalog", AssetDatabase.LoadAssetAtPath<CharacterStoryQuestCatalog>(QuestCatalogPath));
                Set(serialized, "objectiveCatalog", AssetDatabase.LoadAssetAtPath<CharacterStoryQuestObjectiveCatalog>(ObjectiveCatalogPath));
                SerializedProperty slots = serialized.FindProperty("slots");
                slots.arraySize = 3;
                for (int i = 0; i < 3; i++)
                {
                    Transform slotRoot = FindDescendant(characterQuest, "item_CharacterSlot" + (i + 1));
                    Transform empty = FindDescendantPrefix(slotRoot, "item_CharacterEmpty");
                    CharacterQuestCardView card = slotRoot.GetComponentInChildren<CharacterQuestCardView>(true);
                    if (card == null) throw new System.InvalidOperationException(slotRoot.name + " 아래에 CharacterQuestCardView가 없습니다.");
                    Transform character = card.transform;
                    SerializedProperty element = slots.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("emptyRoot").objectReferenceValue = empty.gameObject;
                    element.FindPropertyRelative("characterRoot").objectReferenceValue = character.gameObject;
                    element.FindPropertyRelative("card").objectReferenceValue = card;
                }

                Transform detailRoot = Find(root.transform, "Sub_Panel/QuestInfo");
                CharacterStoryQuestUiController archiveController = detailRoot.GetComponent<CharacterStoryQuestUiController>();
                if (archiveController != null)
                    Object.DestroyImmediate(archiveController, true);
                CharacterStoryQuestDetailView detail = detailRoot.GetComponent<CharacterStoryQuestDetailView>();
                if (detail == null) detail = detailRoot.gameObject.AddComponent<CharacterStoryQuestDetailView>();
                ConfigureDetail(detailRoot, detail);
                Set(serialized, "detailView", detail);
                Set(serialized, "subPanel", Find(root.transform, "Sub_Panel").gameObject);
                Set(serialized, "subPanelCloseButton",
                    FindDescendant(detailRoot, "btn_close").GetComponent<Button>());
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PanelPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ConfigureDetail(Transform detailRoot, CharacterStoryQuestDetailView detail)
        {
            Transform current = Find(detailRoot, "QuestInfo/Current");
            Transform allClear = Find(detailRoot, "QuestInfo/lb_AllClear");
            Transform scroll = Find(current, "ObjectiveScroll");
            Transform content = Find(scroll, "Viewport/Content");
            Transform type = FindDescendant(content, "QuestType");
            Transform description = FindDescendant(content, "QuestDesctiption");
            TMP_Text typeText = FindDescendant(type, "lb_contents").GetComponent<TMP_Text>();
            TMP_Text descriptionText = FindDescendant(description, "lb_contents").GetComponent<TMP_Text>();
            DisableLocalizer(typeText);
            DisableLocalizer(descriptionText);
            Transform progress = FindDescendant(current, "CurrentProgress");
            Transform reward = FindDescendant(content, "QuestReward");
            Transform currency = FindDescendant(reward, "Reward_Currency");
            Transform item = FindDescendant(reward, "Reward_Item");
            Transform complete = FindDescendant(detailRoot, "btn_QuestComplete");

            SerializedObject serialized = new SerializedObject(detail);
            Set(serialized, "currentRoot", current.gameObject);
            Set(serialized, "allClearText", allClear.GetComponent<TMP_Text>());
            Set(serialized, "questTitleText", typeText);
            Set(serialized, "questDescriptionText", descriptionText);
            Set(serialized, "objectiveTypeLineTemplate", typeText);
            Set(serialized, "objectiveDescriptionLineTemplate", descriptionText);
            Set(serialized, "progressSlider", progress.GetComponent<Slider>());
            Set(serialized, "progressPercentText", FindDescendant(progress, "lb_percent").GetComponent<TMP_Text>());
            Set(serialized, "objectiveScroll", scroll.GetComponent<ScrollRect>());
            Set(serialized, "monsterCatalog", AssetDatabase.LoadAssetAtPath<MonsterCatalog>(MonsterCatalogPath));
            Set(serialized, "dungeonCatalog", AssetDatabase.LoadAssetAtPath<DungeonCatalog>(DungeonCatalogPath));
            Set(serialized, "rewardRoot", reward.gameObject);
            Set(serialized, "rewardCurrencyRoot", currency.gameObject);
            Set(serialized, "rewardCurrencyAmountText", FindDescendant(currency, "lb_RewardValue").GetComponent<TMP_Text>());
            Transform currencyIcon = FindDescendant(currency, "sp_currencyIcon (1)");
            Set(serialized, "rewardCurrencyIcon", currencyIcon.GetComponent<Image>());
            Set(serialized, "rewardCurrencyAnimator", currencyIcon.GetComponent<Animator>());
            Set(serialized, "rewardItemRoot", item.gameObject);
            Set(serialized, "rewardItemSlot", item.GetComponentInChildren<InventorySlotView>(true));
            Set(serialized, "completeButton", complete.GetComponent<Button>());
            Set(serialized, "completeButtonText", FindDescendant(complete, "lb_QuestComplete").GetComponent<TMP_Text>());
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetupScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            QuestPanel[] panels = Object.FindObjectsOfType<QuestPanel>(true);
            QuestNotificationController[] notifications = Object.FindObjectsOfType<QuestNotificationController>(true);
            if (panels.Length != 1) throw new System.InvalidOperationException("씬의 QuestPanel은 정확히 하나여야 합니다: " + panels.Length);
            if (notifications.Length != 1) throw new System.InvalidOperationException("씬의 QuestNotificationController는 정확히 하나여야 합니다: " + notifications.Length);
            SerializedObject serialized = new SerializedObject(notifications[0]);
            Set(serialized, "questPanel", panels[0]);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void NormalizeNotificationPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(NotificationPrefabPath);
            try
            {
                QuestNotificationController controller = root.GetComponent<QuestNotificationController>();
                if (controller == null) throw new System.InvalidOperationException("QuestNotificationController를 찾지 못했습니다.");
                SerializedObject serialized = new SerializedObject(controller);
                Set(serialized, "questPanel", null);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, NotificationPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void DisableLocalizer(TMP_Text text)
        {
            if (text != null && text.TryGetComponent(out LocalizedTMPText localizer)) localizer.enabled = false;
        }

        private static Transform Find(Transform root, string path)
        {
            Transform found = root.Find(path);
            if (found == null) throw new System.InvalidOperationException("프리팹 경로를 찾지 못했습니다: " + path);
            return found;
        }

        private static Transform FindDescendant(Transform root, string objectName)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) if (all[i].name == objectName) return all[i];
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < all.Length; i++) names.Add(all[i].name);
            throw new System.InvalidOperationException(root.name + " 아래에서 찾지 못했습니다: " + objectName +
                                                        " / descendants=" + string.Join(",", names));
        }

        private static Transform FindDescendantPrefix(Transform root, string objectName)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name.StartsWith(objectName, System.StringComparison.Ordinal)) return all[i];
            throw new System.InvalidOperationException(root.name + " 아래에서 찾지 못했습니다: " + objectName);
        }

        private static void Set(SerializedObject target, string propertyName, Object value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null) throw new System.InvalidOperationException("직렬화 필드가 없습니다: " + propertyName);
            property.objectReferenceValue = value;
        }
    }
}
