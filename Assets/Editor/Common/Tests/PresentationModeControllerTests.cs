using System.Linq;
using Common;
using DesktopWindow;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CommonEditor.Tests
{
    public sealed class PresentationModeControllerTests
    {
        private const string BuildScenePath = "Assets/Scenes/desktopScene_ReSize.unity";

        [Test]
        public void CompanionMode_HidesPresentationOnly_AndRestoresPreviousGameState()
        {
            GameObject worldRoot = new GameObject("WorldVisualRoot");
            GameObject rendererObject = new GameObject("EnvironmentRenderer");
            rendererObject.transform.SetParent(worldRoot.transform, false);
            SpriteRenderer renderer = rendererObject.AddComponent<SpriteRenderer>();

            GameObject uiRoot = new GameObject("UiRoot", typeof(RectTransform), typeof(CanvasGroup));
            CanvasGroup canvasGroup = uiRoot.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0.65f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = true;

            GameObject inputObject = new GameObject("Input", typeof(RectTransform), typeof(WindowInputRegion));
            inputObject.transform.SetParent(uiRoot.transform, false);
            WindowInputRegion inputRegion = inputObject.GetComponent<WindowInputRegion>();
            inputRegion.ReceiveMouseInput = true;

            GameObject gameOnly = new GameObject("GameOnlyControls");
            GameObject controllerObject = new GameObject("PresentationModeController-Test");
            controllerObject.SetActive(false);
            PresentationModeController controller = controllerObject.AddComponent<PresentationModeController>();

            var serialized = new SerializedObject(controller);
            SetArrayReference(serialized.FindProperty("companionHiddenWorldVisualRoots"), 0, worldRoot);
            SetArrayReference(serialized.FindProperty("companionHiddenUiRoots"), 0, uiRoot);
            SetArrayReference(serialized.FindProperty("gameModeOnlyObjects"), 0, gameOnly);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            controllerObject.SetActive(true);

            try
            {
                Assert.AreEqual(PresentationMode.Game, controller.CurrentMode);
                controller.SetMode(PresentationMode.Companion);

                Assert.IsFalse(renderer.enabled);
                Assert.AreEqual(0f, canvasGroup.alpha);
                Assert.IsFalse(canvasGroup.interactable);
                Assert.IsFalse(canvasGroup.blocksRaycasts);
                Assert.IsFalse(inputRegion.ReceiveMouseInput);
                Assert.IsFalse(gameOnly.activeSelf);

                controller.SetMode(PresentationMode.Game);

                Assert.IsTrue(renderer.enabled);
                Assert.AreEqual(0.65f, canvasGroup.alpha);
                Assert.IsFalse(canvasGroup.interactable);
                Assert.IsTrue(canvasGroup.blocksRaycasts);
                Assert.IsTrue(inputRegion.ReceiveMouseInput);
                Assert.IsTrue(gameOnly.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(gameOnly);
                Object.DestroyImmediate(uiRoot);
                Object.DestroyImmediate(worldRoot);
            }
        }

        [Test]
        public void BuildScene_WiresPersistentToggleAndPresentationTargets()
        {
            Scene scene = EditorSceneManager.OpenScene(BuildScenePath, OpenSceneMode.Additive);
            try
            {
                PresentationModeController controller = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<PresentationModeController>(true))
                    .Single();
                PresentationModeToggleButton toggle = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<PresentationModeToggleButton>(true))
                    .Single();
                CompanionInteractionMenuController interaction = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<CompanionInteractionMenuController>(true))
                    .Single();

                var serializedController = new SerializedObject(controller);
                Assert.AreEqual(3, serializedController.FindProperty("companionHiddenWorldVisualRoots").arraySize);
                SerializedProperty hiddenUiRoots = serializedController.FindProperty("companionHiddenUiRoots");
                Assert.AreEqual(5, hiddenUiRoots.arraySize);
                CollectionAssert.Contains(
                    Enumerable.Range(0, hiddenUiRoots.arraySize)
                        .Select(index => hiddenUiRoots.GetArrayElementAtIndex(index).objectReferenceValue)
                        .OfType<GameObject>()
                        .Select(target => target.name)
                        .ToArray(),
                    "ToastLayer");
                Assert.AreEqual(1, serializedController.FindProperty("gameModeOnlyObjects").arraySize);

                var serializedToggle = new SerializedObject(toggle);
                Assert.AreSame(controller, serializedToggle.FindProperty("controller").objectReferenceValue);
                Button gameButton = serializedToggle.FindProperty("gameModeButton").objectReferenceValue as Button;
                Button companionButton = serializedToggle.FindProperty("companionModeButton").objectReferenceValue as Button;
                Assert.IsNotNull(gameButton);
                Assert.IsNotNull(companionButton);
                Assert.IsTrue(gameButton.GetComponent<WindowInputRegion>().ReceiveMouseInput);
                Assert.IsTrue(companionButton.GetComponent<WindowInputRegion>().ReceiveMouseInput);

                var serializedInteraction = new SerializedObject(interaction);
                Assert.AreSame(controller,
                    serializedInteraction.FindProperty("presentationModeController").objectReferenceValue);
                Assert.IsNotNull(serializedInteraction.FindProperty("fieldModeManager").objectReferenceValue);
                Assert.IsNotNull(serializedInteraction.FindProperty("interactionCanvasRect").objectReferenceValue);
                Assert.IsNotNull(serializedInteraction.FindProperty("stageCamera").objectReferenceValue);
                Assert.IsNotNull(serializedInteraction.FindProperty("playerRenderer").objectReferenceValue);
                Assert.IsNotNull(serializedInteraction.FindProperty("dungeonRestEventController").objectReferenceValue);
                Assert.IsNotNull(serializedInteraction.FindProperty("companionDragTarget").objectReferenceValue);
                Assert.AreEqual(1f, serializedInteraction.FindProperty("autoCloseDelay").floatValue, 0.001f);

                LayoutModeController layout = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<LayoutModeController>(true))
                    .Single();
                var serializedLayout = new SerializedObject(layout);
                Assert.GreaterOrEqual(serializedLayout.FindProperty("holdSeconds").floatValue, 0.05f);
                Assert.GreaterOrEqual(serializedLayout.FindProperty("preActivationMovementPixels").floatValue, 0f);

                var interactionMenu = serializedInteraction.FindProperty("menuRoot").objectReferenceValue as RectTransform;
                Assert.IsNotNull(interactionMenu);
                Assert.IsFalse(interactionMenu.gameObject.activeSelf);
                Assert.IsNotNull(serializedInteraction.FindProperty("townMenuRoot").objectReferenceValue);
                Assert.IsNotNull(serializedInteraction.FindProperty("dungeonMenuRoot").objectReferenceValue);
                Assert.IsNotNull(serializedInteraction.FindProperty("townGameModeButton").objectReferenceValue);
                Assert.IsNotNull(serializedInteraction.FindProperty("dungeonGameModeButton").objectReferenceValue);
                Assert.IsNotNull(serializedInteraction.FindProperty("returnTownButton").objectReferenceValue);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void SetArrayReference(SerializedProperty array, int index, Object value)
        {
            array.arraySize = index + 1;
            array.GetArrayElementAtIndex(index).objectReferenceValue = value;
        }
    }
}
