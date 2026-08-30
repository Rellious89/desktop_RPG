using CharacterArchive;
using Common;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CharacterArchiveEditorTests
{
    public sealed class CharacterStoryQuestArchivePrefabTests
    {
        private const string PrefabPath = "Assets/Art/UI/Prefab/panel/pn_CharacterArchive.prefab";
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";

        [Test]
        public void CharacterArchive_QuestUiHasConfiguredSingleVerticalScrollView()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform questInfo = Find(root.transform, "pn_right/QuestInfo");
                var controller = questInfo.GetComponent<CharacterStoryQuestUiController>();
                Assert.NotNull(controller);
                Assert.IsNull(root.GetComponent<CharacterStoryQuestUiController>());
                var panel = root.GetComponent<CharacterArchivePanel>();
                Assert.AreSame(controller, new SerializedObject(panel).FindProperty("storyQuestUi").objectReferenceValue);
                Assert.IsTrue(controller.HasRequiredReferences);
                SerializedObject panelSerialized = new SerializedObject(panel);
                Assert.AreSame(Find(root.transform, "pn_right").gameObject,
                    panelSerialized.FindProperty("rightPanel").objectReferenceValue,
                    "우측 셸 전체가 닫혀야 공용 전환 버튼도 함께 숨습니다.");
                Assert.AreSame(Find(root.transform, "pn_right/CharacterInfo/bg/top/btn_close").GetComponent<Button>(),
                    panelSerialized.FindProperty("rightCloseButton").objectReferenceValue);
                Transform current = Find(root.transform, "pn_right/QuestInfo/QuestInfo/Current");
                SerializedObject controllerSerialized = new SerializedObject(controller);
                Assert.AreSame(Find(root.transform, "pn_right/QuestInfo/bg/top/btn_close").GetComponent<Button>(),
                    controllerSerialized.FindProperty("closeButton").objectReferenceValue,
                    "퀘스트 페이지가 자신의 닫기 버튼을 소유해야 합니다.");
                Assert.AreSame(current.Find("CurrentProgress").GetComponent<Slider>(),
                    controllerSerialized.FindProperty("currentProgressSlider").objectReferenceValue);
                Assert.AreSame(Find(root.transform, "pn_right/QuestInfo/QuestInfo/TotalProgress").GetComponent<Slider>(),
                    controllerSerialized.FindProperty("totalProgressSlider").objectReferenceValue);
                Transform totalProgressText = Find(root.transform,
                    "pn_right/QuestInfo/QuestInfo/TotalProgress/bottomDeco/sp_description/lb_totalProgress");
                Assert.AreSame(totalProgressText.GetComponent<TMP_Text>(),
                    controllerSerialized.FindProperty("totalProgressText").objectReferenceValue,
                    "보이는 전체 진행 문구가 컨트롤러에 연결되어야 합니다.");
                Transform content = Find(current, "ObjectiveScroll/Viewport/Content");
                ScrollRect scroll = current.Find("ObjectiveScroll").GetComponent<ScrollRect>();
                Assert.IsTrue(scroll.vertical); Assert.IsFalse(scroll.horizontal);
                Assert.AreEqual(ScrollRect.MovementType.Clamped, scroll.movementType);
                Assert.NotNull(scroll.viewport.GetComponent<RectMask2D>());
                Assert.NotNull(content.GetComponent<VerticalLayoutGroup>());
                ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
                Assert.NotNull(fitter); Assert.AreEqual(ContentSizeFitter.FitMode.PreferredSize, fitter.verticalFit);
                Assert.AreSame(content, Find(content, "QuestType").parent);
                Assert.AreSame(content, Find(content, "QuestDesctiption").parent);
                LocalizedTMPText totalProgressLocalizer = totalProgressText.GetComponent<LocalizedTMPText>();
                Assert.IsTrue(totalProgressLocalizer == null || !totalProgressLocalizer.enabled,
                    "동적 총 진행 문구는 컨트롤러가 단독 소유해야 합니다.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void CharacterArchive_QuestLineTemplatesAreInactiveAndDoNotBlockScrolling()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform content = Find(root.transform, "pn_right/QuestInfo/QuestInfo/Current/ObjectiveScroll/Viewport/Content");
                TMP_Text typeTemplate = Find(content, "QuestType/lb_contents").GetComponent<TMP_Text>();
                TMP_Text descriptionTemplate = Find(content, "QuestDesctiption/lb_contents").GetComponent<TMP_Text>();
                Assert.IsFalse(typeTemplate.gameObject.activeSelf);
                Assert.IsFalse(descriptionTemplate.gameObject.activeSelf);
                Assert.IsFalse(typeTemplate.raycastTarget);
                Assert.IsFalse(descriptionTemplate.raycastTarget);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void DesktopResize_CharacterArchivePrefabInstanceHasQuestUiController()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            CharacterStoryQuestUiController[] controllers = Object.FindObjectsOfType<CharacterStoryQuestUiController>(true);
            Assert.AreEqual(1, controllers.Length, $"{scene.name}에 연결된 CharacterStoryQuestUiController가 하나 필요합니다.");
            Assert.IsTrue(controllers[0].HasRequiredReferences);
            Assert.AreEqual("QuestInfo", controllers[0].gameObject.name);
        }

        private static Transform Find(Transform root, string path)
        {
            Transform result = root.Find(path);
            Assert.NotNull(result, path);
            return result;
        }
    }
}
