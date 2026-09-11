using System;
using System.Collections.Generic;
using Character;
using Common;
using Field;
using NUnit.Framework;
using Recovery;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
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

        [TestCase(CharacterRoster.SwapBlockReason.AlreadyCurrent, RecoveryCharacterState.Available,
            CharacterSwapListItem.DisplayState.InUse)]
        [TestCase(CharacterRoster.SwapBlockReason.None, RecoveryCharacterState.Available,
            CharacterSwapListItem.DisplayState.Ready)]
        [TestCase(CharacterRoster.SwapBlockReason.NoStamina, RecoveryCharacterState.Exhausted,
            CharacterSwapListItem.DisplayState.Exhausted)]
        [TestCase(CharacterRoster.SwapBlockReason.AlreadyCurrent, RecoveryCharacterState.Recovering,
            CharacterSwapListItem.DisplayState.Recovering)]
        [TestCase(CharacterRoster.SwapBlockReason.NoStamina, RecoveryCharacterState.RecoveryComplete,
            CharacterSwapListItem.DisplayState.RecoveryComplete)]
        [TestCase(CharacterRoster.SwapBlockReason.InRecovery, RecoveryCharacterState.Available,
            CharacterSwapListItem.DisplayState.Recovering)]
        public void CharacterCondition_UsesTheSameRecoveryFirstPriorityAsSwapList(
            CharacterRoster.SwapBlockReason swapReason,
            RecoveryCharacterState recoveryState,
            CharacterSwapListItem.DisplayState expected)
        {
            Assert.AreEqual(expected, CharacterSwapListItem.ResolveDisplayState(swapReason, recoveryState));
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
        public void HudTooltip_UsesDedicatedPrefabControllerAndWholeSlotHoverHandlers()
        {
            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/UI/Prefab/HUD/CharacterHUD.prefab");
            GameObject slotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/UI/Prefab/HUD/item_CharacterHUD.prefab");
            GameObject tooltipPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/UI/Prefab/HUD/CharacterHUD_HoverTooltip.prefab");

            CharacterHudTooltipController controller = hudPrefab != null
                ? hudPrefab.GetComponent<CharacterHudTooltipController>() : null;
            CharacterHudSlotView slot = slotPrefab != null ? slotPrefab.GetComponent<CharacterHudSlotView>() : null;
            CharacterHudTooltipView tooltipView = tooltipPrefab != null
                ? tooltipPrefab.GetComponent<CharacterHudTooltipView>() : null;

            Assert.NotNull(controller);
            Assert.NotNull(slot);
            Assert.NotNull(tooltipView);
            Assert.IsTrue(typeof(IPointerEnterHandler).IsAssignableFrom(typeof(CharacterHudSlotView)));
            Assert.IsTrue(typeof(IPointerExitHandler).IsAssignableFrom(typeof(CharacterHudSlotView)));

            SerializedObject serialized = new SerializedObject(controller);
            Assert.AreEqual(tooltipPrefab, serialized.FindProperty("tooltipPrefab").objectReferenceValue,
                "HUD controller는 CharacterHUD_HoverTooltip 프리팹 하나를 재사용해야 한다.");
            Assert.AreEqual(10f, serialized.FindProperty("pointerOffsetX").floatValue, 0.0001f,
                "SpeechBottom 꼬리의 실제 x 오프셋으로 슬롯 중앙을 맞춰야 한다.");
            Assert.AreEqual(new Vector2(15f, 10f), serialized.FindProperty("placementOffset").vector2Value,
                "HUD 툴팁은 기존 슬롯 기준 위치에서 오른쪽 15, 위쪽 10만큼 이동해야 한다.");

            Graphic[] tooltipGraphics = tooltipPrefab.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < tooltipGraphics.Length; i++)
            {
                Assert.IsFalse(tooltipGraphics[i].raycastTarget,
                    "HUD 툴팁 그래픽은 Hover/클릭을 가로채면 안 된다.");
            }

            LocalizedTMPText levelLocalizer = FindDeepChild(tooltipPrefab.transform, "lb_CharacterLevel")
                ?.GetComponent<LocalizedTMPText>();
            Assert.NotNull(levelLocalizer);
            Assert.IsFalse(levelLocalizer.enabled,
                "동적 레벨 값은 LocalizedTMPText가 아닌 TooltipView가 형식을 적용해야 한다.");

            LocalizedTMPText staminaTitleLocalizer =
                FindDeepChild(FindDeepChild(tooltipPrefab.transform, "Stamina"), "lb_title")
                    ?.GetComponent<LocalizedTMPText>();
            LocalizedTMPText purificationTitleLocalizer =
                FindDeepChild(FindDeepChild(tooltipPrefab.transform, "Purification"), "lb_title")
                    ?.GetComponent<LocalizedTMPText>();
            Assert.NotNull(staminaTitleLocalizer);
            Assert.NotNull(purificationTitleLocalizer);
            Assert.IsTrue(staminaTitleLocalizer.enabled,
                "행동력 제목의 정적 로컬라이징은 유지해야 한다.");
            Assert.IsTrue(purificationTitleLocalizer.enabled,
                "오염도 제목의 정적 로컬라이징은 유지해야 한다.");
        }

        [Test]
        public void HudTooltip_CorruptionUsesConciseSingleDecimalFormat()
        {
            Assert.AreEqual("12.1", CharacterHudTooltipView.FormatCorruption(12.05d));
            Assert.AreEqual("12", CharacterHudTooltipView.FormatCorruption(12d));
            Assert.AreEqual("0", CharacterHudTooltipView.FormatCorruption(double.NaN));
        }

        [Test]
        public void HudTooltip_ShowsExactlyOneOfTheFivePreparedConditionObjects()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Art/UI/Prefab/HUD/CharacterHUD_HoverTooltip.prefab");
            Assert.NotNull(prefab);

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            try
            {
                Assert.NotNull(instance);
                CharacterHudTooltipView view = instance.GetComponent<CharacterHudTooltipView>();
                Transform condition = FindDeepChild(instance.transform, "condition");
                Assert.NotNull(view);
                Assert.NotNull(condition);

                GameObject[] objects =
                {
                    FindDeepChild(condition, "active")?.gameObject,
                    FindDeepChild(condition, "Available")?.gameObject,
                    FindDeepChild(condition, "Exhausted")?.gameObject,
                    FindDeepChild(condition, "Recovering")?.gameObject,
                    FindDeepChild(condition, "RecoveryComplete")?.gameObject,
                };
                CharacterSwapListItem.DisplayState[] states =
                {
                    CharacterSwapListItem.DisplayState.InUse,
                    CharacterSwapListItem.DisplayState.Ready,
                    CharacterSwapListItem.DisplayState.Exhausted,
                    CharacterSwapListItem.DisplayState.Recovering,
                    CharacterSwapListItem.DisplayState.RecoveryComplete,
                };

                var localizers = new LocalizedTMPText[objects.Length];
                for (int i = 0; i < objects.Length; i++)
                {
                    Assert.NotNull(objects[i]);
                    localizers[i] = objects[i].GetComponentInChildren<LocalizedTMPText>(true);
                    Assert.NotNull(localizers[i], $"{objects[i].name} 상태 문구는 기존 LocalizedTMPText를 유지해야 한다.");
                }
                for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
                {
                    view.SetCondition(states[stateIndex]);
                    for (int objectIndex = 0; objectIndex < objects.Length; objectIndex++)
                    {
                        Assert.AreEqual(stateIndex == objectIndex, objects[objectIndex].activeSelf,
                            $"{states[stateIndex]} 상태에서는 대응 오브젝트 하나만 활성화되어야 한다.");
                        Assert.AreEqual(stateIndex == objectIndex, localizers[objectIndex].isActiveAndEnabled,
                            "활성 상태의 LocalizedTMPText만 구독 수명주기를 유지해야 한다.");
                    }
                }
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            }
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
