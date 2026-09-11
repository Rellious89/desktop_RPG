using System;
using System.Collections.Generic;
using Common;
using Field;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CommonEditor.Tests
{
    public sealed class CharacterHudViewTests
    {
        [TestCase(-1, 3, 0)]
        [TestCase(0, 3, 0)]
        [TestCase(2, 3, 2)]
        [TestCase(3, 3, 3)]
        [TestCase(4, 3, 3)]
        public void VisibleSlotCount_HidesEmptyAndOverLimitSlots(int rosterCount, int maximum, int expected)
        {
            Assert.AreEqual(expected, CharacterHudController.GetVisibleSlotCount(rosterCount, maximum));
        }

        [Test]
        public void FieldModeVisibility_UsesIndependentTownAndDungeonInspectorToggles()
        {
            Assert.IsTrue(CharacterHudController.ShouldDisplayIn(FieldMode.Town, true, true));
            Assert.IsTrue(CharacterHudController.ShouldDisplayIn(FieldMode.Dungeon, true, true));
            Assert.IsTrue(CharacterHudController.ShouldDisplayIn(FieldMode.Town, true, false));
            Assert.IsFalse(CharacterHudController.ShouldDisplayIn(FieldMode.Dungeon, true, false));
            Assert.IsFalse(CharacterHudController.ShouldDisplayIn(FieldMode.Town, false, true));
            Assert.IsTrue(CharacterHudController.ShouldDisplayIn(FieldMode.Dungeon, false, true));
            Assert.IsFalse(CharacterHudController.ShouldDisplayIn(FieldMode.Town, false, false));
            Assert.IsFalse(CharacterHudController.ShouldDisplayIn(FieldMode.Dungeon, false, false));
        }

        [Test]
        public void CorruptionCells_UsePurificationTenPercentBlinkRulesForAnyMaximum()
        {
            CharacterHudSlotView.CorruptionCellState empty = CharacterHudSlotView.CalculateCorruptionCellState(0d, 500);
            CharacterHudSlotView.CorruptionCellState halfCell = CharacterHudSlotView.CalculateCorruptionCellState(25d, 500);
            CharacterHudSlotView.CorruptionCellState fastCell = CharacterHudSlotView.CalculateCorruptionCellState(49d, 500);
            CharacterHudSlotView.CorruptionCellState full = CharacterHudSlotView.CalculateCorruptionCellState(500d, 500);

            Assert.AreEqual(0, empty.FullCellCount);
            Assert.AreEqual(-1, empty.BlinkingCellIndex);
            Assert.AreEqual(0, halfCell.FullCellCount);
            Assert.AreEqual(0, halfCell.BlinkingCellIndex);
            Assert.IsFalse(halfCell.FastBlink);
            Assert.IsTrue(fastCell.FastBlink);
            Assert.AreEqual(CharacterHudSlotView.CorruptionCellCount, full.FullCellCount);
            Assert.AreEqual(-1, full.BlinkingCellIndex);
        }

        [Test]
        public void HudPrefabs_HaveExplicitControllerSlotAndStaminaProgressReferences()
        {
            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/UI/Prefab/HUD/CharacterHUD.prefab");
            GameObject slotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/UI/Prefab/HUD/item_CharacterHUD.prefab");
            CharacterHudController controller = hudPrefab != null ? hudPrefab.GetComponent<CharacterHudController>() : null;
            CharacterHudSlotView slot = slotPrefab != null ? slotPrefab.GetComponent<CharacterHudSlotView>() : null;
            ProgressBarView progress = slotPrefab != null ? slotPrefab.GetComponentInChildren<ProgressBarView>(true) : null;

            Assert.NotNull(controller);
            Assert.NotNull(slot);
            Assert.NotNull(progress);

            SerializedObject serializedController = new SerializedObject(controller);
            Assert.IsTrue(serializedController.FindProperty("showInTown").boolValue);
            Assert.IsTrue(serializedController.FindProperty("showInDungeon").boolValue);
        }

        [Test]
        public void SceneCharacterHud_ReferencesFieldModeManagerForImmediateModeUpdates()
        {
            Scene scene = EditorSceneManager.OpenScene(
                "Assets/Scenes/desktopScene_ReSize.unity", OpenSceneMode.Additive);
            try
            {
                CharacterHudController controller = null;
                FieldModeManager manager = null;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (controller == null) controller = roots[i].GetComponentInChildren<CharacterHudController>(true);
                    if (manager == null) manager = roots[i].GetComponentInChildren<FieldModeManager>(true);
                }

                Assert.NotNull(controller);
                Assert.NotNull(manager);
                SerializedObject serializedController = new SerializedObject(controller);
                Assert.AreEqual(manager, serializedController.FindProperty("fieldModeManager").objectReferenceValue,
                    "Character HUD는 FieldModeChanged를 즉시 받을 FieldModeManager를 씬에서 직접 참조해야 한다.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void StaminaProgressPrefab_UsesLeftToRightValueDirection()
        {
            GameObject slotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/UI/Prefab/HUD/item_CharacterHUD.prefab");
            ProgressBarView progress = slotPrefab != null ? slotPrefab.GetComponentInChildren<ProgressBarView>(true) : null;
            Slider slider = progress != null ? progress.GetComponent<Slider>() : null;
            SlantedProgressBar slanted = progress != null ? progress.GetComponent<SlantedProgressBar>() : null;

            Assert.NotNull(slider);
            Assert.NotNull(slanted);
            Assert.AreEqual(Slider.Direction.LeftToRight, slider.direction);

            SerializedObject serialized = new SerializedObject(slanted);
            Assert.AreEqual(0, serialized.FindProperty("fillDirection").enumValueIndex,
                "HUD 행동력 사선 Fill도 0=왼쪽, 1=오른쪽 방향을 따라야 한다.");
        }

        [Test]
        public void CorruptionCells_FillFromVisualLeftToRightDespiteRotatedPrefabHierarchy()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/UI/Prefab/HUD/item_CharacterHUD.prefab");
            Assert.NotNull(prefab);
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            try
            {
                CharacterHudSlotView view = instance.GetComponent<CharacterHudSlotView>();
                Assert.NotNull(view);
                view.RefreshCorruption(90d, 300);

                Transform cellsRoot = FindDeepChild(instance.transform, "fill_cell");
                Assert.NotNull(cellsRoot);
                LayoutRebuilder.ForceRebuildLayoutImmediate(cellsRoot as RectTransform);
                Image[] images = cellsRoot.GetComponentsInChildren<Image>(true);
                var fills = new List<Image>();
                for (int i = 0; i < images.Length; i++)
                {
                    if (images[i] != null && images[i].name.StartsWith("cell_fill_", StringComparison.Ordinal)) fills.Add(images[i]);
                }
                fills.Sort((left, right) => left.rectTransform.position.x.CompareTo(right.rectTransform.position.x));

                Assert.AreEqual(CharacterHudSlotView.CorruptionCellCount, fills.Count);
                for (int i = 0; i < fills.Count; i++)
                {
                    Assert.AreEqual(i < 3 ? 1f : 0f, fills[i].color.a, 0.0001f,
                        "HUD 오염도는 계층 순서가 아니라 실제 화면 좌표에서 왼쪽부터 채워야 한다.");
                }
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void CharacterSwapPopup_UsesHudRowTopForFixedHeightAndClampsOnlyHorizontally()
        {
            Rect parent = new Rect(-500f, -300f, 1000f, 600f);
            Vector2 popupSize = new Vector2(200f, 112f);
            Vector2 popupPivot = new Vector2(0.5f, 1f);

            Vector2 normal = CharacterSwapConfirmationDialog.CalculateHorizontallyClampedPopupPosition(
                new Vector2(0f, -250f), popupSize, popupPivot, parent, 8f);
            Assert.AreEqual(new Vector2(0f, -130f), normal,
                "top pivot의 popup bottom은 슬롯 위쪽 + gap에 맞아야 한다.");

            Vector2 clamped = CharacterSwapConfirmationDialog.CalculateHorizontallyClampedPopupPosition(
                new Vector2(480f, 280f), popupSize, popupPivot, parent, 8f);
            Assert.AreEqual(new Vector2(400f, 400f), clamped,
                "x만 Canvas 안으로 보정하고 y는 HUD 열 위 고정 높이를 유지해야 한다.");
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            if (root == null) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName) return child;
                Transform found = FindDeepChild(child, childName);
                if (found != null) return found;
            }

            return null;
        }
    }
}
