using System;
using Corruption;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CorruptionEditor.Tests
{
    public sealed class CorruptionStaminaCostPolicyTests
    {
        private CorruptionConfigDefinition config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<CorruptionConfigDefinition>();
            config.hideFlags = HideFlags.HideAndDontSave;
            Configure(config, max: 300, warning: 50, danger: 80, warningMultiplier: 2, dangerMultiplier: 3);
        }

        [TearDown]
        public void TearDown()
        {
            if (config != null) UnityEngine.Object.DestroyImmediate(config);
        }

        [TestCase(149.999d, 1)]
        [TestCase(150d, 2)]
        [TestCase(239.999d, 2)]
        [TestCase(240d, 3)]
        [TestCase(300d, 3)]
        [TestCase(999d, 3)]
        public void Calculate_UsesInclusiveConfiguredThresholds(double corruption, int expectedMultiplier)
        {
            Assert.AreEqual(expectedMultiplier,
                CorruptionStaminaCostPolicy.Calculate(corruption, 0, config, 1));
        }

        [Test]
        public void Calculate_UsesConfiguredMultipliersAndBaseCost()
        {
            Configure(config, max: 200, warning: 25, danger: 75, warningMultiplier: 4, dangerMultiplier: 7);

            Assert.AreEqual(12, CorruptionStaminaCostPolicy.Calculate(50d, 0, config, 3));
            Assert.AreEqual(21, CorruptionStaminaCostPolicy.Calculate(150d, 0, config, 3));
        }

        [Test]
        public void Calculate_UsesBaseCorruptionAsTheEffectiveLowerBound()
        {
            Assert.AreEqual(2, CorruptionStaminaCostPolicy.Calculate(0d, 150, config, 1));
        }

        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void Calculate_InvalidCurrentCorruption_IsSafelyNormalized(double corruption)
        {
            Assert.AreEqual(1, CorruptionStaminaCostPolicy.Calculate(corruption, 0, config, 1));
        }

        [Test]
        public void Calculate_MissingOrInvalidConfig_FallsBackToBaseCost()
        {
            Assert.AreEqual(5, CorruptionStaminaCostPolicy.Calculate(300d, 0, null, 5));
            Configure(config, max: 300, warning: 80, danger: 50, warningMultiplier: 2, dangerMultiplier: 3);
            Assert.AreEqual(5, CorruptionStaminaCostPolicy.Calculate(300d, 0, config, 5));
        }

        [Test]
        public void Calculate_Overflow_SaturatesAtIntMaximum()
        {
            Assert.AreEqual(int.MaxValue,
                CorruptionStaminaCostPolicy.Calculate(300d, 0, config, int.MaxValue));
        }

        private static void Configure(
            CorruptionConfigDefinition definition,
            int max,
            int warning,
            int danger,
            int warningMultiplier,
            int dangerMultiplier)
        {
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("configId").stringValue = "default";
            serialized.FindProperty("maxCorruption").intValue = max;
            serialized.FindProperty("warningThresholdPercent").intValue = warning;
            serialized.FindProperty("dangerThresholdPercent").intValue = danger;
            serialized.FindProperty("warningStaminaCostMultiplier").intValue = warningMultiplier;
            serialized.FindProperty("dangerStaminaCostMultiplier").intValue = dangerMultiplier;
            serialized.FindProperty("enabled").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
