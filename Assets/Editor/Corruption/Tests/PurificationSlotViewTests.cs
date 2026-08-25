using System.Reflection;
using Corruption;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CorruptionEditor.Tests
{
    public sealed class PurificationSlotViewTests
    {
        private GameObject root;
        private PurificationSlotView view;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("PurificationSlotViewTest", typeof(RectTransform));
            var cellRoot = new GameObject("fill_cell", typeof(RectTransform));
            cellRoot.transform.SetParent(root.transform, false);
            for (int i = 0; i < 10; i++)
            {
                var cell = new GameObject($"cell_fill_{i + 1:00}", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image));
                cell.transform.SetParent(cellRoot.transform, false);
            }

            view = root.AddComponent<PurificationSlotView>();
            InvokePrivate("Awake");
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        [Test]
        public void SameBlinkCellAndSpeed_PreservesBlinkPhaseAcrossDisplayRefresh()
        {
            Refresh(9.5f);
            SetField("blinkElapsed", 1.25f);

            Refresh(9.4f);

            Assert.AreEqual(1.25f, GetField<float>("blinkElapsed"));
            Assert.AreEqual(0, GetField<int>("blinkingCell"));
            Assert.IsTrue(GetField<bool>("fastBlink"));
        }

        [Test]
        public void BlinkSpeedOrCellChange_RestartsBlinkPhase()
        {
            Refresh(9.5f);
            SetField("blinkElapsed", 1.25f);

            Refresh(8.5f);
            Assert.AreEqual(0f, GetField<float>("blinkElapsed"));
            Assert.IsFalse(GetField<bool>("fastBlink"));

            SetField("blinkElapsed", 0.75f);
            Refresh(19.5f);
            Assert.AreEqual(0f, GetField<float>("blinkElapsed"));
            Assert.AreEqual(1, GetField<int>("blinkingCell"));
            Assert.IsTrue(GetField<bool>("fastBlink"));
        }

        private void Refresh(float percent)
        {
            InvokePrivate("RefreshProgressVisuals", percent, 0d, false);
        }

        private void InvokePrivate(string name, params object[] args)
        {
            MethodInfo method = typeof(PurificationSlotView).GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(view, args);
        }

        private T GetField<T>(string name)
        {
            FieldInfo field = typeof(PurificationSlotView).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            return (T)field.GetValue(view);
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(PurificationSlotView).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(view, value);
        }
    }
}
