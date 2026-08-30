using CharacterArchive;
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
                var controller = root.GetComponent<CharacterStoryQuestUiController>();
                Assert.NotNull(controller);
                Assert.IsTrue(controller.HasRequiredReferences);
                Transform current = Find(root.transform, "pn_right/QuestInfo/QuestInfo/Current");
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
        }

        private static Transform Find(Transform root, string path)
        {
            Transform result = root.Find(path);
            Assert.NotNull(result, path);
            return result;
        }
    }
}
