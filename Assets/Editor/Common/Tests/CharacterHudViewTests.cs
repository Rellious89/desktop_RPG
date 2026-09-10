using Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
        }
    }
}
