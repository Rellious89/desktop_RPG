using CharacterArchive;
using Common;
using Dungeon;
using Quest;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CharacterArchiveEditor
{
    /// <summary>Phase 13C의 명부 프리팹 연결을 재현 가능한 한 번의 좁은 편집으로 유지한다.</summary>
    public static class CharacterStoryQuestUiPrefabSetup
    {
        private const string PrefabPath = "Assets/Art/UI/Prefab/panel/pn_CharacterArchive.prefab";

        [MenuItem("Tools/Keybuddy/Character Archive/Setup Story Quest UI", priority = 120)]
        public static void Setup()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                CharacterArchivePanel panel = root.GetComponent<CharacterArchivePanel>();
                if (panel == null) throw new System.InvalidOperationException("CharacterArchivePanel을 찾을 수 없습니다.");
                Transform questInfo = Find(root.transform, "pn_right/QuestInfo");
                Transform current = Find(root.transform, "pn_right/QuestInfo/QuestInfo/Current");
                Transform scroll = current.Find("ObjectiveScroll");
                Transform type;
                Transform description;
                if (scroll == null)
                {
                    type = Find(current, "lb_QeustInfo/QuestType");
                    description = Find(current, "lb_QeustInfo/QuestDesctiption");
                    Transform wrapper = type.parent;
                    scroll = CreateScroll(current);
                    Transform newContent = scroll.Find("Viewport/Content");
                    type.SetParent(newContent, false); description.SetParent(newContent, false);
                    if (wrapper != null && wrapper.childCount == 0) Object.DestroyImmediate(wrapper.gameObject);
                }
                else
                {
                    type = Find(scroll, "Viewport/Content/QuestType");
                    description = Find(scroll, "Viewport/Content/QuestDesctiption");
                }
                SetLayerRecursively(scroll, current.gameObject.layer);
                TMP_Text typeTemplate = Find(type, "lb_contents").GetComponent<TMP_Text>();
                TMP_Text descriptionTemplate = Find(description, "lb_contents").GetComponent<TMP_Text>();
                typeTemplate.gameObject.SetActive(false); descriptionTemplate.gameObject.SetActive(false);
                DisableTextRaycasts(type); DisableTextRaycasts(description);

                // QuestInfo가 퀘스트 전용 Inspector 참조와 수명을 소유한다. 루트에 남아 있던
                // 초기 13C 컴포넌트는 제거해 프리팹/씬 인스턴스가 중복되지 않게 한다.
                CharacterStoryQuestUiController rootController = root.GetComponent<CharacterStoryQuestUiController>();
                if (rootController != null) Object.DestroyImmediate(rootController);
                var controller = questInfo.GetComponent<CharacterStoryQuestUiController>();
                if (controller == null) controller = questInfo.gameObject.AddComponent<CharacterStoryQuestUiController>();
                SerializedObject serialized = new SerializedObject(controller);
                Set(serialized, "questCatalog", AssetDatabase.LoadAssetAtPath<CharacterStoryQuestCatalog>("Assets/Generated/TableData/CharacterStoryQuest/CharacterStoryQuestCatalog.asset"));
                Set(serialized, "objectiveCatalog", AssetDatabase.LoadAssetAtPath<CharacterStoryQuestObjectiveCatalog>("Assets/Generated/TableData/CharacterStoryQuestObjective/CharacterStoryQuestObjectiveCatalog.asset"));
                Set(serialized, "monsterCatalog", AssetDatabase.LoadAssetAtPath<MonsterCatalog>("Assets/Generated/TableData/Monster/MonsterCatalog.asset"));
                Set(serialized, "dungeonCatalog", AssetDatabase.LoadAssetAtPath<DungeonCatalog>("Assets/Generated/TableData/Dungeon/DungeonCatalog.asset"));
                Set(serialized, "characterInfoPage", Find(root.transform, "pn_right/CharacterInfo").gameObject);
                Set(serialized, "questInfoPage", Find(root.transform, "pn_right/QuestInfo").gameObject);
                Set(serialized, "swapButton", Find(root.transform, "pn_right/btn_swap").GetComponent<Button>());
                Set(serialized, "closeButton", Find(root.transform, "pn_right/QuestInfo/bg/top/btn_close").GetComponent<Button>());
                Set(serialized, "currentProgressSlider", Find(current, "CurrentProgress").GetComponent<Slider>());
                Set(serialized, "totalProgressSlider", Find(root.transform, "pn_right/QuestInfo/QuestInfo/TotalProgress").GetComponent<Slider>());
                Set(serialized, "currentProgressPercentText", Find(current, "CurrentProgress/lb_percent").GetComponent<TMP_Text>());
                Set(serialized, "totalProgressPercentText", Find(root.transform, "pn_right/QuestInfo/QuestInfo/TotalProgress/lb_percent").GetComponent<TMP_Text>());
                Transform totalProgressDescription = Find(root.transform,
                    "pn_right/QuestInfo/QuestInfo/TotalProgress/bottomDeco/sp_description/lb_totalProgress");
                Set(serialized, "totalProgressText", totalProgressDescription.GetComponent<TMP_Text>());
                Set(serialized, "questTypeTitle", Find(type, "lb_title").GetComponent<TMP_Text>());
                Set(serialized, "questDescriptionTitle", Find(description, "lb_title").GetComponent<TMP_Text>());
                Set(serialized, "questTypeLineTemplate", typeTemplate);
                Set(serialized, "questDescriptionLineTemplate", descriptionTemplate);
                Set(serialized, "completeButton", Find(root.transform, "pn_right/QuestInfo/QuestInfo/btn_QuestComplete").GetComponent<Button>());
                Set(serialized, "completeButtonText", Find(root.transform, "pn_right/QuestInfo/QuestInfo/btn_QuestComplete/lb_QuestComplete").GetComponent<TMP_Text>());
                Set(serialized, "objectiveScroll", scroll.GetComponent<ScrollRect>());
                serialized.ApplyModifiedPropertiesWithoutUndo();

                // lb_totalProgress는 동적 인자가 필요한 문구다. 정적 LocalizedTMPText가
                // 비동기 콜백에서 컨트롤러의 조립 결과를 덮지 않도록 소유권을 컨트롤러로 통일한다.
                LocalizedTMPText totalProgressLocalizer = totalProgressDescription.GetComponent<LocalizedTMPText>();
                if (totalProgressLocalizer != null) totalProgressLocalizer.enabled = false;

                SerializedObject panelSerialized = new SerializedObject(panel);
                Set(panelSerialized, "storyQuestUi", controller);
                Set(panelSerialized, "rightPanel", Find(root.transform, "pn_right").gameObject);
                Set(panelSerialized, "rightCloseButton", Find(root.transform, "pn_right/CharacterInfo/bg/top/btn_close").GetComponent<Button>());
                panelSerialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Transform CreateScroll(Transform current)
        {
            var scroll = new GameObject("ObjectiveScroll", typeof(RectTransform), typeof(ScrollRect));
            scroll.transform.SetParent(current, false);
            scroll.layer = current.gameObject.layer;
            RectTransform scrollRect = scroll.GetComponent<RectTransform>();
            Stretch(scrollRect, new Vector2(0f, 11f), Vector2.zero);
            ScrollRect scrollRectComponent = scroll.GetComponent<ScrollRect>();
            scrollRectComponent.horizontal = false; scrollRectComponent.vertical = true;
            scrollRectComponent.movementType = ScrollRect.MovementType.Clamped;
            scrollRectComponent.scrollSensitivity = 12f;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(scroll.transform, false);
            viewport.layer = current.gameObject.layer;
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect, Vector2.zero, Vector2.zero);
            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            content.layer = current.gameObject.layer;
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f); contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(.5f, 1f); contentRect.anchoredPosition = Vector2.zero; contentRect.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft; layout.spacing = 3f;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRectComponent.viewport = viewportRect; scrollRectComponent.content = contentRect;
            return scroll.transform;
        }

        private static void Stretch(RectTransform transform, Vector2 minOffset, Vector2 maxOffset)
        {
            transform.anchorMin = Vector2.zero; transform.anchorMax = Vector2.one;
            transform.offsetMin = minOffset; transform.offsetMax = maxOffset;
        }

        private static void DisableTextRaycasts(Transform root)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.raycastTarget = false;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayerRecursively(child, layer);
        }

        private static Transform Find(Transform root, string path)
        {
            Transform found = root.Find(path);
            if (found == null) throw new System.InvalidOperationException("프리팹 경로를 찾지 못했습니다: " + path);
            return found;
        }

        private static void Set(SerializedObject target, string propertyName, Object value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null) throw new System.InvalidOperationException("직렬화 필드가 없습니다: " + propertyName);
            property.objectReferenceValue = value;
        }
    }
}
