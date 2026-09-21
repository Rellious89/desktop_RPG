using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Common;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CommonEditor.Tests
{
    public sealed class HoverTooltipControllerTests
    {
        private const string BuildScenePath = "Assets/Scenes/desktopScene_ReSize.unity";

        [TestCase(120f, 200f, 100f, true)]
        [TestCase(80f, 200f, 100f, false)]
        [TestCase(40f, 60f, 100f, false)]
        [TestCase(60f, 40f, 100f, true)]
        public void HorizontalSide_PrefersLeft_ThenUsesAvailableOrLargerSide(
            float leftSpace,
            float rightSpace,
            float tooltipWidth,
            bool expectedLeft)
        {
            MethodInfo choose = typeof(HoverTooltipController).GetMethod(
                "ChooseLeftSide",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(choose);
            Assert.AreEqual(expectedLeft, choose.Invoke(null, new object[] { leftSpace, rightSpace, tooltipWidth }));
        }

        [Test]
        public void DirectionalPrefab_MissingVariantFallsBackToBase()
        {
            var owner = new GameObject("HoverTooltipController-Test", typeof(RectTransform));
            var basePrefab = new GameObject("HoverTooltip-Base", typeof(RectTransform));
            var controller = owner.AddComponent<HoverTooltipController>();

            try
            {
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("tooltipPrefab").objectReferenceValue = basePrefab;
                serialized.FindProperty("tooltipOnLeftPrefab").objectReferenceValue = null;
                serialized.FindProperty("tooltipOnRightPrefab").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                MethodInfo resolve = typeof(HoverTooltipController).GetMethod(
                    "ResolveDirectionalPrefab",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.IsNotNull(resolve);
                Assert.AreSame(basePrefab, resolve.Invoke(controller, new object[] { true }));
                Assert.AreSame(basePrefab, resolve.Invoke(controller, new object[] { false }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(basePrefab);
            }
        }

        [Test]
        public void OnDisable_HidesTooltipParentedUnderExternalLayer()
        {
            var owner = new GameObject("HoverTooltipController-Owner", typeof(RectTransform));
            var externalLayer = new GameObject("TooltipLayer-Test", typeof(RectTransform));
            var tooltip = new GameObject("Tooltip-Test", typeof(RectTransform));
            tooltip.transform.SetParent(externalLayer.transform, false);
            var controller = owner.AddComponent<HoverTooltipController>();

            try
            {
                typeof(HoverTooltipController).GetField(
                        "tooltipRect",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(controller, tooltip.GetComponent<RectTransform>());

                typeof(HoverTooltipController).GetMethod(
                        "OnDisable",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(controller, null);

                Assert.IsFalse(tooltip.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(externalLayer);
            }
        }

        [Test]
        public void BuildScene_WiresCompanionTooltipVariantsBoundsAndLocalizedButtons()
        {
            Scene scene = EditorSceneManager.OpenScene(BuildScenePath, OpenSceneMode.Additive);
            try
            {
                Transform[] transforms = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .ToArray();
                Transform menu = transforms.Single(transform => transform.name == "CompanionInteractionMenu");
                HoverTooltipController controller = menu.GetComponent<HoverTooltipController>();
                Assert.IsNotNull(controller);

                var serialized = new SerializedObject(controller);
                Assert.AreEqual("HoverTooltip", serialized.FindProperty("tooltipPrefab").objectReferenceValue.name);
                Assert.AreEqual("HoverTooltip_R", serialized.FindProperty("tooltipOnLeftPrefab").objectReferenceValue.name);
                Assert.AreEqual("HoverTooltip_L", serialized.FindProperty("tooltipOnRightPrefab").objectReferenceValue.name);
                Assert.AreEqual("TooltipLayer", serialized.FindProperty("tooltipRoot").objectReferenceValue.name);
                Assert.AreEqual("AlwaysOn", serialized.FindProperty("tooltipBounds").objectReferenceValue.name);
                Assert.AreEqual(1, serialized.FindProperty("placement").enumValueIndex);
                Assert.IsFalse(serialized.FindProperty("autoAttachTriggers").boolValue);

                UnityEngine.UI.Button[] buttons = menu.GetComponentsInChildren<UnityEngine.UI.Button>(true);
                UnityEngine.UI.Button[] gameModeButtons = buttons.Where(button => button.name == "GameMode").ToArray();
                UnityEngine.UI.Button[] functionalButtons = buttons.Where(button => button.name != "GameMode").ToArray();
                Assert.AreEqual(2, gameModeButtons.Length);
                Assert.IsTrue(gameModeButtons.All(button => button.GetComponent<HoverTooltipTrigger>() == null));
                Assert.IsNotEmpty(functionalButtons);
                Assert.IsTrue(functionalButtons.All(button => button.GetComponent<HoverTooltipTrigger>() != null));
                Assert.IsTrue(functionalButtons.All(HasLocalizedTooltipReference));

                Transform mainMenu = transforms.Single(transform => transform.name == "MainMenu");
                var mainMenuKeys = new HashSet<ulong>(mainMenu
                    .GetComponentsInChildren<HoverTooltipTrigger>(true)
                    .Select(GetLocalizedTooltipKey));
                Assert.IsTrue(functionalButtons.All(button =>
                    mainMenuKeys.Contains(GetLocalizedTooltipKey(button.GetComponent<HoverTooltipTrigger>()))));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void BuildScene_MainMenuUsesOnlyBaseTooltipAndOwnController()
        {
            Scene scene = EditorSceneManager.OpenScene(BuildScenePath, OpenSceneMode.Additive);
            try
            {
                Transform[] transforms = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .ToArray();
                Transform mainMenu = transforms.Single(transform => transform.name == "MainMenu");
                HoverTooltipController mainController = mainMenu.GetComponent<HoverTooltipController>();
                Assert.IsNotNull(mainController);

                var serialized = new SerializedObject(mainController);
                Assert.AreEqual("HoverTooltip", serialized.FindProperty("tooltipPrefab").objectReferenceValue.name);
                Assert.IsNull(serialized.FindProperty("tooltipOnLeftPrefab").objectReferenceValue);
                Assert.IsNull(serialized.FindProperty("tooltipOnRightPrefab").objectReferenceValue);
                Assert.AreEqual(0, serialized.FindProperty("placement").enumValueIndex);
                Assert.IsTrue(serialized.FindProperty("autoAttachTriggers").boolValue);

                MethodInfo awake = typeof(HoverTooltipController).GetMethod(
                    "Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(awake);
                awake.Invoke(mainController, null);

                FieldInfo controllerField = typeof(HoverTooltipTrigger).GetField(
                    "controller",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(controllerField);
                HoverTooltipTrigger[] triggers = mainMenu.GetComponentsInChildren<HoverTooltipTrigger>(true);
                Assert.IsNotEmpty(triggers);
                Assert.IsTrue(triggers.All(trigger => ReferenceEquals(controllerField.GetValue(trigger), mainController)));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void BuildScene_CompanionTriggersBindOnlyToCompanionController()
        {
            Scene scene = EditorSceneManager.OpenScene(BuildScenePath, OpenSceneMode.Additive);
            try
            {
                Transform menu = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .Single(transform => transform.name == "CompanionInteractionMenu");
                HoverTooltipController companionController = menu.GetComponent<HoverTooltipController>();
                Assert.IsNotNull(companionController);

                MethodInfo awake = typeof(HoverTooltipController).GetMethod(
                    "Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(awake);
                awake.Invoke(companionController, null);

                FieldInfo controllerField = typeof(HoverTooltipTrigger).GetField(
                    "controller",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(controllerField);
                HoverTooltipTrigger[] triggers = menu.GetComponentsInChildren<HoverTooltipTrigger>(true);
                Assert.IsNotEmpty(triggers);
                Assert.IsTrue(triggers.All(trigger =>
                    ReferenceEquals(controllerField.GetValue(trigger), companionController)));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static bool HasLocalizedTooltipReference(UnityEngine.UI.Button button)
        {
            var trigger = button.GetComponent<HoverTooltipTrigger>();
            var serialized = new SerializedObject(trigger);
            SerializedProperty reference = serialized.FindProperty("tooltipText");
            SerializedProperty table = reference.FindPropertyRelative("m_TableReference");
            SerializedProperty entry = reference.FindPropertyRelative("m_TableEntryReference");
            return table != null && entry != null
                                 && !string.IsNullOrEmpty(table.FindPropertyRelative("m_TableCollectionName").stringValue)
                                 && entry.FindPropertyRelative("m_KeyId").ulongValue != 0;
        }

        private static ulong GetLocalizedTooltipKey(HoverTooltipTrigger trigger)
        {
            var serialized = new SerializedObject(trigger);
            return serialized.FindProperty("tooltipText")
                .FindPropertyRelative("m_TableEntryReference")
                .FindPropertyRelative("m_KeyId")
                .ulongValue;
        }
    }
}
