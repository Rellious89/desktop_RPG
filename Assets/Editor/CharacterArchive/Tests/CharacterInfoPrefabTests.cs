using CharacterArchive;
using Common;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CharacterArchiveEditorTests
{
    public sealed class CharacterInfoPrefabTests
    {
        private const string PanelPath = "Assets/Art/UI/Prefab/panel/pn_CharacterArchive.prefab";
        private const string SkillPath = "Assets/Art/UI/Prefab/Skill/list_Skill.prefab";
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";
        private const string UiTablePath = "Assets/Localization/Tables/01_UI/01_UI_ko-KR.asset";

        [Test]
        public void SkillPrefab_HasDedicatedViewAndExplicitReferences()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(SkillPath);
            try
            {
                SkillListItemView view = root.GetComponent<SkillListItemView>();
                Assert.NotNull(view);
                SerializedObject serialized = new SerializedObject(view);
                Assert.AreSame(Find(root.transform, "mask_portrait/sp_portrait").GetComponent<Image>(),
                    serialized.FindProperty("iconImage").objectReferenceValue);
                Assert.AreSame(Find(root.transform, "sp_name/lb_SkillName").GetComponent<TMP_Text>(),
                    serialized.FindProperty("nameText").objectReferenceValue);
                Assert.AreSame(Find(root.transform, "sp_name/lb_SkillDescription").GetComponent<TMP_Text>(),
                    serialized.FindProperty("descriptionText").objectReferenceValue);
                Assert.AreSame(Find(root.transform, "sp_cooldown/lb_colldown").GetComponent<TMP_Text>(),
                    serialized.FindProperty("cooldownText").objectReferenceValue);
                Assert.AreSame(Find(root.transform, "sp_name").gameObject,
                    serialized.FindProperty("unlockedNameRoot").objectReferenceValue);
                Assert.AreSame(Find(root.transform, "sp_cooldown").gameObject,
                    serialized.FindProperty("cooldownRoot").objectReferenceValue);
                Assert.AreSame(Find(root.transform, "LockInfo").gameObject,
                    serialized.FindProperty("lockedInfoRoot").objectReferenceValue);
                Assert.AreSame(Find(root.transform, "LockInfo/LockInfo/lb_SkillName_Lock").GetComponent<TMP_Text>(),
                    serialized.FindProperty("lockedNameText").objectReferenceValue);
                Assert.AreSame(Find(root.transform, "LockInfo/LockInfo/lb_SkillDescription_Lock").GetComponent<TMP_Text>(),
                    serialized.FindProperty("lockedDescriptionText").objectReferenceValue);
                LocalizedTMPText lockFormat = (LocalizedTMPText)serialized
                    .FindProperty("lockedDescriptionLocalizer").objectReferenceValue;
                Assert.AreSame(Find(root.transform, "LockInfo/LockInfo/lb_SkillDescription_Lock").GetComponent<LocalizedTMPText>(), lockFormat);
                Assert.IsFalse(lockFormat.enabled, "동적 01/104 문구는 행이 레벨 인자로 포맷합니다.");
                StringTable uiTable = AssetDatabase.LoadAssetAtPath<StringTable>(UiTablePath);
                Assert.AreEqual(uiTable.GetEntry("104").KeyId, lockFormat.TextReference.TableEntryReference.KeyId);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void SkillPrefab_ExposesItsDesignedRowHeightToParentLayouts()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(SkillPath);
            try
            {
                LayoutElement layout = root.GetComponent<LayoutElement>();
                Assert.NotNull(layout);
                Assert.Greater(layout.minHeight, 0f);
                Assert.Greater(layout.preferredHeight, 0f);
                Assert.AreEqual(root.GetComponent<RectTransform>().rect.height, layout.preferredHeight);

                HorizontalLayoutGroup horizontal = root.GetComponent<HorizontalLayoutGroup>();
                Assert.NotNull(horizontal);
                Assert.IsFalse(horizontal.enabled, "행 내부의 기존 절대 배치는 외부 세로 목록이 소유하지 않습니다.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void CharacterArchive_CharacterInfoOwnsReferencesTemplateAndExpandingVerticalContent()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PanelPath);
            try
            {
                Transform characterInfo = Find(root.transform, "pn_right/CharacterInfo");
                CharacterInfoController controller = characterInfo.GetComponent<CharacterInfoController>();
                Assert.NotNull(controller);
                Assert.IsNull(root.GetComponent<CharacterInfoController>());
                Assert.IsTrue(controller.HasRequiredReferences);
                CharacterArchivePanel panel = root.GetComponent<CharacterArchivePanel>();
                Assert.AreSame(controller, new SerializedObject(panel).FindProperty("characterInfoUi").objectReferenceValue);
                Transform unlockInfo = Find(root.transform, "pn_right/CharacterInfo/UnlockInfo");
                CharacterUnlockInfoController unlockController = unlockInfo.GetComponent<CharacterUnlockInfoController>();
                Assert.NotNull(unlockController);
                Assert.IsTrue(unlockController.HasRequiredReferences);
                Assert.AreSame(unlockController, new SerializedObject(panel).FindProperty("characterUnlockInfoUi").objectReferenceValue);
                SerializedObject unlockSerialized = new SerializedObject(unlockController);
                TMP_Text unlockTemplate = (TMP_Text)unlockSerialized.FindProperty("conditionTemplate").objectReferenceValue;
                Assert.IsFalse(unlockTemplate.gameObject.activeSelf, "lb_contents는 조건 행 템플릿으로만 남아야 합니다.");
                Assert.AreSame(Find(unlockInfo, "Viewport/Content/list_UnlockInfo").GetComponent<RectTransform>(),
                    unlockSerialized.FindProperty("conditionContent").objectReferenceValue);
                Assert.AreSame(Find(unlockInfo, "Viewport/Content/list_UnlockInfo/lb_complete").gameObject,
                    unlockSerialized.FindProperty("completeRoot").objectReferenceValue);
                Transform unlockRows = Find(unlockInfo, "Viewport/Content/list_UnlockInfo");
                Assert.AreSame(unlockTemplate.transform, unlockRows.GetChild(0));
                Assert.AreSame(Find(unlockRows, "lb_complete"), unlockRows.GetChild(1),
                    "프리팹은 템플릿 뒤에 완료 안내를 두고 런타임 행이 그 앞에 삽입된다.");
                Transform checkOn = Find(unlockTemplate.transform, "sp_check/sp_checkOn");
                Assert.IsFalse(checkOn.gameObject.activeSelf, "체크 표시는 조건을 충족한 런타임 행에서만 켠다.");

                SerializedObject serialized = new SerializedObject(controller);
                SkillListItemView template = (SkillListItemView)serialized.FindProperty("skillTemplate").objectReferenceValue;
                Assert.NotNull(template);
                Assert.IsFalse(template.gameObject.activeSelf, "샘플 list_Skill은 템플릿으로만 남아야 합니다.");
                Transform content = ((RectTransform)serialized.FindProperty("skillContent").objectReferenceValue).transform;
                Assert.AreSame(content, template.transform.parent);
                Assert.NotNull(content.GetComponent<VerticalLayoutGroup>());
                ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
                Assert.NotNull(fitter);
                Assert.AreEqual(ContentSizeFitter.FitMode.PreferredSize, fitter.verticalFit);
                RectTransform contentRect = content.GetComponent<RectTransform>();
                Assert.AreEqual(Vector2.up, contentRect.anchorMin);
                Assert.AreEqual(Vector2.up, contentRect.anchorMax);
                Assert.AreEqual(new Vector2(.5f, 1f), contentRect.pivot);
                VerticalLayoutGroup rows = content.GetComponent<VerticalLayoutGroup>();
                Assert.IsTrue(rows.childControlHeight,
                    "행 컨테이너는 list_Skill의 LayoutElement preferred height를 소비해야 합니다.");
                TMP_Text title = FindDescendant(Find(characterInfo, "SkillInfo"), "lb_title").GetComponent<TMP_Text>();
                TMP_Text count = (TMP_Text)serialized.FindProperty("skillCountText").objectReferenceValue;
                GameObject empty = (GameObject)serialized.FindProperty("emptyState").objectReferenceValue;
                Transform skillInfo = Find(characterInfo, "SkillInfo");
                ScrollRect scroll = skillInfo.GetComponent<ScrollRect>();
                Assert.IsTrue(scroll.vertical);
                Assert.IsFalse(scroll.horizontal);
                Assert.AreEqual(ScrollRect.MovementType.Clamped, scroll.movementType);
                RectTransform scrollContent = content.parent as RectTransform;
                Assert.NotNull(scrollContent);
                Assert.AreSame(scrollContent, scroll.content,
                    "ScrollRect는 VerticalLayoutGroup이 배치하는 list_SkillInfo가 아니라 그 부모 Content를 움직여야 합니다.");
                Assert.AreSame(scroll.viewport, scrollContent.parent,
                    "ScrollRect Content는 Viewport의 직접 자식이어야 합니다.");
                Assert.AreSame(skillInfo, title.transform.parent,
                    "스킬 제목은 ScrollRect Content가 아니라 SkillInfo의 고정 헤더여야 합니다.");
                Assert.AreSame(title.transform, count.transform.parent,
                    "동적 스킬 카운트는 고정 제목 헤더와 함께 움직여야 합니다.");
                Assert.IsFalse(title.transform.IsChildOf(scroll.viewport),
                    "고정 제목은 스크롤 viewport에 포함되면 안 됩니다.");
                Assert.IsFalse(count.transform.IsChildOf(scroll.content),
                    "고정 카운트는 스크롤 content에 포함되면 안 됩니다.");
                Assert.AreSame(content, empty.transform.parent,
                    "0개 상태 안내는 스킬 목록 content 안에 남아야 합니다.");
                Assert.AreSame(content, template.transform.parent,
                    "템플릿과 런타임 스킬 행은 스킬 목록 content 안에 있어야 합니다.");
                Assert.IsNull(scroll.viewport.GetComponent<ScrollRect>(),
                    "Viewport는 mask만 소유하고 ScrollRect는 SkillInfo 하나만 소유합니다.");
                Assert.LessOrEqual(scroll.viewport.offsetMax.y, -title.rectTransform.rect.height,
                    "목록 viewport는 고정 헤더 아래에서 시작해야 합니다.");
                Assert.NotNull(scrollContent.GetComponent<VerticalLayoutGroup>());
                Assert.AreEqual(ContentSizeFitter.FitMode.PreferredSize,
                    scrollContent.GetComponent<ContentSizeFitter>().verticalFit);

                LocalizedTMPText titleLocalizer = title.GetComponent<LocalizedTMPText>();
                Assert.NotNull(titleLocalizer);
                Assert.IsTrue(titleLocalizer.enabled, "정적 01/95 제목은 프리팹 로컬라이저가 소유합니다.");
                StringTable uiTable = AssetDatabase.LoadAssetAtPath<StringTable>(UiTablePath);
                Assert.NotNull(uiTable);
                Assert.AreEqual(uiTable.GetEntry("95").KeyId, titleLocalizer.TextReference.TableEntryReference.KeyId);
                LocalizedTMPText countLocalizer = (LocalizedTMPText)serialized.FindProperty("skillCountLocalizer").objectReferenceValue;
                Assert.AreSame(FindDescendant(Find(characterInfo, "SkillInfo"), "lb_count").GetComponent<TMP_Text>(), count);
                Assert.IsFalse(countLocalizer.enabled, "동적 01/103 카운트는 컨트롤러가 포맷합니다.");
                Assert.AreEqual(uiTable.GetEntry("103").KeyId, countLocalizer.TextReference.TableEntryReference.KeyId);
                Assert.AreEqual(uiTable.GetEntry("96").KeyId,
                    empty.GetComponent<LocalizedTMPText>().TextReference.TableEntryReference.KeyId);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void CharacterArchive_SkillScrollLayoutAndHoverKeepContentAndRowsStable()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PanelPath);
            GameObject eventSystemObject = new GameObject("SkillInfoEventSystem", typeof(EventSystem));
            try
            {
                Transform characterInfo = Find(root.transform, "pn_right/CharacterInfo");
                ScrollRect scroll = Find(characterInfo, "SkillInfo").GetComponent<ScrollRect>();
                RectTransform scrollContent = scroll.content;
                RectTransform skillList = Find(scrollContent, "list_SkillInfo").GetComponent<RectTransform>();
                SkillListItemView template = skillList.GetComponentInChildren<SkillListItemView>(true);
                Find(skillList, "lb_empty").gameObject.SetActive(false);

                for (int i = 0; i < 8; i++)
                {
                    SkillListItemView row = Object.Instantiate(template, skillList);
                    row.name = "layout-row-" + i;
                    row.gameObject.SetActive(true);
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(skillList);
                LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
                Canvas.ForceUpdateCanvases();

                scroll.verticalNormalizedPosition = .35f;
                Canvas.ForceUpdateCanvases();

                RectTransform firstRow = skillList.GetChild(2) as RectTransform;
                Vector2 contentPosition = scrollContent.anchoredPosition;
                float contentHeight = scrollContent.rect.height;
                Vector2 firstRowPosition = firstRow.anchoredPosition;
                Vector2 firstRowSize = firstRow.rect.size;

                ExecuteEvents.Execute<IPointerEnterHandler>(firstRow.gameObject,
                    new PointerEventData(eventSystemObject.GetComponent<EventSystem>()), ExecuteEvents.pointerEnterHandler);
                LayoutRebuilder.ForceRebuildLayoutImmediate(skillList);
                LayoutRebuilder.ForceRebuildLayoutImmediate(scrollContent);
                Canvas.ForceUpdateCanvases();

                Assert.AreEqual(contentHeight, scrollContent.rect.height);
                Assert.AreEqual(contentPosition, scrollContent.anchoredPosition);
                Assert.AreEqual(firstRowPosition, firstRow.anchoredPosition);
                Assert.AreEqual(firstRowSize, firstRow.rect.size);
            }
            finally
            {
                Object.DestroyImmediate(eventSystemObject);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void DesktopResize_PrefabInstanceHasOneCharacterInfoControllerBesideQuestController()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            CharacterInfoController[] characterControllers = Object.FindObjectsOfType<CharacterInfoController>(true);
            CharacterStoryQuestUiController[] questControllers = Object.FindObjectsOfType<CharacterStoryQuestUiController>(true);
            Assert.AreEqual(1, characterControllers.Length, scene.name);
            Assert.AreEqual(1, questControllers.Length, scene.name);
            Assert.AreEqual("CharacterInfo", characterControllers[0].gameObject.name);
            Assert.AreEqual("QuestInfo", questControllers[0].gameObject.name);
            Assert.IsTrue(characterControllers[0].HasRequiredReferences);
            Assert.IsTrue(questControllers[0].HasRequiredReferences);
        }

        private static Transform Find(Transform root, string path)
        {
            Transform result = root.Find(path);
            Assert.NotNull(result, path);
            return result;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendantOrNull(child, name);
                if (found != null) return found;
            }
            Assert.Fail(name);
            return null;
        }

        private static Transform FindDescendantOrNull(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDescendantOrNull(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
