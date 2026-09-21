using Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CommonEditor.Tests
{
    public sealed class LongPressDragGestureTests
    {
        [Test]
        public void Hold_ActivatesOnlyAfterConfiguredDelay()
        {
            var gesture = new LongPressDragGesture();
            gesture.Press(new Vector2(10f, 20f), 1f);

            Assert.IsFalse(gesture.TryActivate(1.49f, 0.5f));
            Assert.IsTrue(gesture.IsWaiting);
            Assert.IsTrue(gesture.TryActivate(1.5f, 0.5f));
            Assert.IsTrue(gesture.IsActive);
        }

        [Test]
        public void MovementBeyondAllowance_CancelsPendingHold()
        {
            var gesture = new LongPressDragGesture();
            gesture.Press(Vector2.zero, 0f);
            gesture.Move(new Vector2(9f, 0f), 8f);

            Assert.IsFalse(gesture.IsWaiting);
            Assert.IsFalse(gesture.TryActivate(1f, 0.5f));
        }

        [Test]
        public void ExitCancelsWaiting_ButDoesNotCancelActiveDrag()
        {
            var waiting = new LongPressDragGesture();
            waiting.Press(Vector2.zero, 0f);
            waiting.Exit();
            Assert.IsFalse(waiting.IsWaiting);

            var active = new LongPressDragGesture();
            active.Press(Vector2.zero, 0f);
            Assert.IsTrue(active.TryActivate(1f, 0.5f));
            active.Exit();
            Assert.IsTrue(active.IsActive);
            Assert.IsTrue(active.Release());
        }

        [Test]
        public void CharacterHudPrefab_ProvidesIndependentDraggableGroup()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Art/UI/Prefab/HUD/CharacterHUD.prefab");
            Assert.IsNotNull(prefab);

            UiGroupDraggable draggable = prefab.GetComponent<UiGroupDraggable>();
            Assert.IsNotNull(draggable);
            Assert.AreEqual(LayoutModeController.CharacterHudGroupId, draggable.GroupId);

            var serialized = new SerializedObject(draggable);
            Assert.IsTrue(serialized.FindProperty("directDragEnabled").boolValue);
        }

        [Test]
        public void StageUsesCharacterHitArea_NotPersistentFootprintInput()
        {
            Assert.IsFalse(LayoutModeController.IsDirectPointerInputGroup(LayoutModeController.StageGroupId));
            Assert.IsTrue(LayoutModeController.IsDirectPointerInputGroup(LayoutModeController.CharacterHudGroupId));
            Assert.IsTrue(LayoutModeController.IsDirectPointerInputGroup(LayoutModeController.ProgressGroupId));
        }

        [TestCase(PresentationMode.Game)]
        [TestCase(PresentationMode.Companion)]
        public void CharacterHitAreas_RemainAvailableInBothPresentationModes(PresentationMode mode)
        {
            Assert.IsTrue(CompanionInteractionMenuController.ShouldMaintainCharacterHitAreas(mode));
        }

        [Test]
        public void EditorPlayMode_UsesPointerEventDragDeltas_EvenWithWindowsBuildTarget()
        {
            Assert.IsTrue(LayoutModeController.UsesPointerEventDragDeltas);
        }

        [Test]
        public void UiGroupDraggable_RespectsParentCanvasGroupInputAndIgnoreParents()
        {
            GameObject parent = new GameObject("CanvasGroupParent", typeof(RectTransform), typeof(CanvasGroup));
            GameObject child = new GameObject(
                "DraggableChild", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UiGroupDraggable));
            child.transform.SetParent(parent.transform, false);

            try
            {
                CanvasGroup parentGroup = parent.GetComponent<CanvasGroup>();
                UiGroupDraggable draggable = child.GetComponent<UiGroupDraggable>();
                Assert.IsTrue(draggable.TryGetUnityScreenRect(out _));

                parentGroup.blocksRaycasts = false;
                Assert.IsFalse(draggable.TryGetUnityScreenRect(out _));

                parentGroup.blocksRaycasts = true;
                parentGroup.interactable = false;
                Assert.IsFalse(draggable.TryGetUnityScreenRect(out _));

                parentGroup.interactable = true;
                parentGroup.alpha = 0f;
                Assert.IsFalse(draggable.TryGetUnityScreenRect(out _));

                CanvasGroup localGroup = child.AddComponent<CanvasGroup>();
                localGroup.ignoreParentGroups = true;
                parentGroup.alpha = 1f;
                parentGroup.blocksRaycasts = false;
                Assert.IsTrue(draggable.TryGetUnityScreenRect(out _));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
