using System.Reflection;
using System.Linq;
using Common;
using Dungeon;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonEditor.Tests
{
    public sealed class DungeonRestEventDescriptionPresenterTests
    {
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";

        [TestCase(0.3f, 30)]
        [TestCase(0.806f, 81)]
        [TestCase(-1f, 0)]
        [TestCase(2f, 100)]
        public void ToResumePercent_ClampsAndRounds(float ratio, int expected)
        {
            Assert.AreEqual(expected, DungeonRestEventDescriptionPresenter.ToResumePercent(ratio));
        }

        [Test]
        public void StatusDots_LoopFromOneThroughThree()
        {
            int dots = 1;
            Assert.AreEqual("회복중.", DungeonRestEventDescriptionPresenter.ComposeStatus("회복중", dots));

            dots = DungeonRestEventDescriptionPresenter.NextDotCount(dots);
            Assert.AreEqual("회복중..", DungeonRestEventDescriptionPresenter.ComposeStatus("회복중", dots));

            dots = DungeonRestEventDescriptionPresenter.NextDotCount(dots);
            Assert.AreEqual("회복중...", DungeonRestEventDescriptionPresenter.ComposeStatus("회복중", dots));

            dots = DungeonRestEventDescriptionPresenter.NextDotCount(dots);
            Assert.AreEqual("회복중.", DungeonRestEventDescriptionPresenter.ComposeStatus("회복중...", dots));
        }

        [Test]
        public void ResumeValue_ReplacesLocalizedPlaceholderWithPercent()
        {
            string result = DungeonRestEventDescriptionPresenter.ComposeResumeValue(
                "필요 최소 행동력 : {0}%",
                0.3f,
                out bool failed);

            Assert.IsFalse(failed);
            Assert.AreEqual("필요 최소 행동력 : 30%", result);
        }

        [TestCase(0, 10, 0)]
        [TestCase(9, 10, 90)]
        [TestCase(299, 300, 99)]
        [TestCase(10, 10, 100)]
        [TestCase(20, 15, 133)]
        [TestCase(10, 0, 0)]
        public void RestStaminaPercent_FloorsBelowReadyAndDoesNotClampAboveReady(
            int current,
            int required,
            int expected)
        {
            Assert.AreEqual(expected, DungeonRestEventDescriptionPresenter.ToRestStaminaPercent(current, required));
        }

        [TestCase(99, "Ready", "99%")]
        [TestCase(100, "Ready", "Ready")]
        [TestCase(133, "Ready", "Ready")]
        public void RestStaminaValue_UsesLocalizedReadyTextAtThreshold(
            int percent, string readyText, string expected)
        {
            Assert.AreEqual(expected,
                DungeonRestEventDescriptionPresenter.ComposeRestStaminaValue(percent, 100, readyText));
        }

        [TestCase(49, 0)]
        [TestCase(50, 1)]
        [TestCase(99, 1)]
        [TestCase(100, 2)]
        [TestCase(133, 2)]
        public void RestStaminaColor_UsesLowAndReadyThresholds(int percent, int expectedColor)
        {
            Color selected = DungeonRestEventDescriptionPresenter.SelectRestStaminaColor(
                percent,
                50,
                100,
                Color.red,
                Color.yellow,
                Color.white);

            Color[] colors = { Color.red, Color.yellow, Color.white };
            Assert.AreEqual(colors[expectedColor], selected);
        }

        [Test]
        public void BuildScene_WiresRestDescriptionPresenterAndDynamicLocalizersHaveSingleWriter()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                DungeonRestEventDescriptionPresenter presenter = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<DungeonRestEventDescriptionPresenter>(true))
                    .Single();
                var serialized = new SerializedObject(presenter);

                Assert.IsNotNull(serialized.FindProperty("restEventController").objectReferenceValue);
                Assert.IsNotNull(serialized.FindProperty("stageCamera").objectReferenceValue);
                Assert.IsNotNull(serialized.FindProperty("worldAnchor").objectReferenceValue);
                Assert.IsNotNull(serialized.FindProperty("uiParent").objectReferenceValue);

                RectTransform presentationRoot =
                    serialized.FindProperty("presentationRoot").objectReferenceValue as RectTransform;
                RectTransform speechBubbleRoot =
                    serialized.FindProperty("speechBubbleRoot").objectReferenceValue as RectTransform;
                GameObject detailRoot = serialized.FindProperty("detailRoot").objectReferenceValue as GameObject;
                TextMeshProUGUI valueText =
                    serialized.FindProperty("resumeValueText").objectReferenceValue as TextMeshProUGUI;
                RectTransform statusPrototype =
                    serialized.FindProperty("statusPrototype").objectReferenceValue as RectTransform;
                SerializedProperty statusWorldAnchors = serialized.FindProperty("statusWorldAnchors");

                Assert.IsNotNull(presentationRoot);
                Assert.IsFalse(presentationRoot.gameObject.activeSelf);
                Assert.IsNotNull(speechBubbleRoot);
                Assert.IsNotNull(speechBubbleRoot.GetComponent<UnityEngine.UI.Button>());
                Assert.IsNotNull(detailRoot);
                Assert.IsFalse(detailRoot.activeSelf);
                Assert.IsNotNull(detailRoot.GetComponent<UITweenTransition>(),
                    "detail 토스트에는 Enter/Exit 전환 컴포넌트가 있어야 합니다.");
                Assert.IsNotNull(valueText);
                Assert.IsNotNull(statusPrototype);
                Assert.AreEqual("Status_SP", statusPrototype.name);
                Assert.IsFalse(statusPrototype.gameObject.activeSelf,
                    "Status_SP는 런타임 복제용 비활성 원본으로 남아야 합니다.");
                Assert.IsNotNull(statusPrototype.Find("lb_value"));
                Assert.AreEqual(3, statusWorldAnchors.arraySize);
                for (int i = 0; i < statusWorldAnchors.arraySize; i++)
                {
                    Transform anchor = statusWorldAnchors.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    Assert.IsNotNull(anchor, $"휴식 상태 UIAnchor {i + 1} 참조가 비어 있습니다.");
                    StringAssert.StartsWith("UIAnchor", anchor.name);
                    Assert.AreEqual($"CharacterSlot{i + 1}", anchor.parent.name);
                }
                Assert.AreEqual(50, serialized.FindProperty("lowStaminaPercentThreshold").intValue);
                Assert.AreEqual(100, serialized.FindProperty("readyStaminaPercentThreshold").intValue);
                Assert.AreEqual(Color.red, serialized.FindProperty("lowStaminaColor").colorValue);
                Assert.AreEqual(Color.yellow, serialized.FindProperty("recoveringStaminaColor").colorValue);
                Assert.AreEqual(Color.white, serialized.FindProperty("readyStaminaColor").colorValue);
                Assert.AreEqual(0.5f, serialized.FindProperty("statusDotInterval").floatValue, 0.0001f);
                Assert.Greater(serialized.FindProperty("detailToastDuration").floatValue, 0f,
                    "detail 토스트 유지 시간은 양수여야 합니다.");
                Assert.AreEqual(Vector2.zero, serialized.FindProperty("restStatusSlotOffset").vector2Value,
                    "RestStatusSlot 공통 오프셋 기본값은 기존 위치를 유지하도록 0이어야 합니다.");

                LocalizedTMPText statusLocalizer =
                    speechBubbleRoot.GetComponentInChildren<LocalizedTMPText>(true);
                LocalizedTMPText valueLocalizer = valueText.GetComponent<LocalizedTMPText>();
                Assert.IsNotNull(statusLocalizer);
                Assert.IsNotNull(valueLocalizer);
                Assert.IsFalse(statusLocalizer.enabled,
                    "UI/128은 점 연출 Presenter만 TMP에 써야 합니다.");
                Assert.IsFalse(valueLocalizer.enabled,
                    "UI/130은 {0} 인자를 채우는 Presenter만 TMP에 써야 합니다.");
                Assert.IsTrue(statusLocalizer.TextReference.HasReference);
                Assert.IsTrue(valueLocalizer.TextReference.HasReference);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void StatusPrototype_CreatesThreeReusableNonInteractiveClones()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            DungeonRestEventDescriptionPresenter presenter = null;
            try
            {
                presenter = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<DungeonRestEventDescriptionPresenter>(true))
                    .Single();

                Invoke(presenter, "EnsureStatusInstances");
                RectTransform[] roots = (RectTransform[])GetPrivate(presenter, "statusRoots");
                RectTransform uiParent = (RectTransform)GetPrivate(presenter, "uiParent");

                Assert.AreEqual(3, roots.Length);
                Assert.AreEqual(3, roots.Distinct().Count());
                for (int i = 0; i < roots.Length; i++)
                {
                    Assert.IsNotNull(roots[i]);
                    Assert.AreSame(uiParent, roots[i].parent);
                    Assert.IsFalse(roots[i].gameObject.activeSelf);
                    Assert.IsNotNull(roots[i].Find("lb_value"));

                    UnityEngine.UI.Graphic[] graphics =
                        roots[i].GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
                    Assert.IsNotEmpty(graphics);
                    Assert.IsTrue(graphics.All(graphic => !graphic.raycastTarget));

                    CanvasGroup canvasGroup = roots[i].GetComponent<CanvasGroup>();
                    Assert.IsNotNull(canvasGroup);
                    Assert.IsFalse(canvasGroup.interactable);
                    Assert.IsFalse(canvasGroup.blocksRaycasts);
                }

                Invoke(presenter, "EnsureStatusInstances");
                RectTransform[] reusedRoots = (RectTransform[])GetPrivate(presenter, "statusRoots");
                CollectionAssert.AreEqual(roots, reusedRoots, "반복 초기화 시 상태 UI 복제본이 누적되면 안 됩니다.");
            }
            finally
            {
                if (presenter != null) Invoke(presenter, "DestroyStatusInstances");
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void RefreshStatusViews_DoesNotReactivateSlotsWhileDetailIsClosed()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            DungeonRestEventDescriptionPresenter presenter = null;
            try
            {
                presenter = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<DungeonRestEventDescriptionPresenter>(true))
                    .Single();

                Invoke(presenter, "EnsureStatusInstances");
                RectTransform[] roots = (RectTransform[])GetPrivate(presenter, "statusRoots");
                foreach (RectTransform root in roots)
                    root.gameObject.SetActive(true);

                SetPrivate(presenter, "presentationVisible", true);
                // BuildScene keeps detailRoot inactive, matching the closed-detail state.
                Invoke(presenter, "RefreshStatusViews");

                Assert.IsTrue(roots.All(root => root == null || !root.gameObject.activeSelf),
                    "detail이 닫힌 동안 LateUpdate/RefreshStatusViews가 RestStatusSlot을 다시 켜면 안 됩니다.");
            }
            finally
            {
                if (presenter != null) Invoke(presenter, "DestroyStatusInstances");
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static object Invoke(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{target.GetType().Name}.{methodName}을 찾지 못했습니다.");
            return method.Invoke(target, null);
        }

        private static object GetPrivate(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{target.GetType().Name}.{fieldName}을 찾지 못했습니다.");
            return field.GetValue(target);
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{target.GetType().Name}.{fieldName}을 찾지 못했습니다.");
            field.SetValue(target, value);
        }
    }
}
