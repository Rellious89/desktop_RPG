using System;
using System.Collections.Generic;
using Character;
using Common;
using Corruption;
using Dungeon;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DungeonEditor.Tests
{
    public sealed class DungeonCorruptionSettlementServiceTests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object value in created)
                if (value != null) UnityEngine.Object.DestroyImmediate(value);
            created.Clear();
        }

        [TestCase(59d, 0L)]
        [TestCase(60d, 1L)]
        [TestCase(121d, 2L)]
        [TestCase(double.NaN, 0L)]
        [TestCase(double.PositiveInfinity, 0L)]
        public void CalculateTotal_UsesCompletedIntervalsOnly(double elapsed, long expected)
        {
            Assert.AreEqual(expected, DungeonCorruptionSettlementService.CalculateTotal(elapsed, 60, 1));
        }

        [Test]
        public void CalculateTotal_Overflow_SaturatesAtLongMaximum()
        {
            Assert.AreEqual(long.MaxValue,
                DungeonCorruptionSettlementService.CalculateTotal(double.MaxValue, 1, int.MaxValue));
        }

        [Test]
        public void Settle_ThreeParticipantsReceiveSameFraction_AndSaveOnce()
        {
            SaveData data = Data("A", "B", "C");
            DungeonSessionSnapshot snapshot = Snapshot(60d, new[] { "A", "B", "C" });
            int saves = 0;
            var service = new DungeonCorruptionSettlementService(
                CharacterCatalog("A", "B", "C"), Config(300), () => { saves++; return true; });

            Assert.IsTrue(service.TrySettle(snapshot, data));

            Assert.AreEqual(1, saves);
            Assert.AreEqual(1d / 3d, data.characters[0].currentCorruption, 0.000000001d);
            Assert.AreEqual(data.characters[0].currentCorruption, data.characters[1].currentCorruption, 0d);
            Assert.AreEqual(data.characters[0].currentCorruption, data.characters[2].currentCorruption, 0d);
        }

        [Test]
        public void Settle_UsesBaseCapsAtConfigMaximum_AndDoesNotCreateUnknownState()
        {
            SaveData data = Data("A", "unknown");
            data.characters[0].currentCorruption = -2d;
            DungeonSessionSnapshot snapshot = Snapshot(60d, new[] { "A", "unknown", "missing" });
            var service = new DungeonCorruptionSettlementService(
                CharacterCatalog(5, "A"), Config(5), () => true);

            Assert.IsTrue(service.TrySettle(snapshot, data));

            Assert.AreEqual(5d, data.characters[0].currentCorruption, 0.000000001d,
                "유효하지 않은 현재값은 BaseCorruption에서 시작하고 Config 최대치를 넘지 않는다");
            Assert.AreEqual(0d, data.characters[1].currentCorruption, 0d,
                "저장에는 있지만 정의가 없는 참가자는 새 상태를 만들거나 변경하지 않는다");
            Assert.AreEqual(2, data.characters.Count);
        }

        [Test]
        public void Settle_SaveFailure_RollsBackAllCharacterAndMetadataChanges()
        {
            SaveData data = Data("A", "B");
            data.saveRevision = 7;
            data.lastSavedAtUtc = "before";
            DungeonSessionSnapshot snapshot = Snapshot(60d, new[] { "A", "B" });
            var service = new DungeonCorruptionSettlementService(
                CharacterCatalog("A", "B"), Config(300), () =>
                {
                    data.saveRevision = 8;
                    data.lastSavedAtUtc = "changed";
                    return false;
                });

            Assert.IsFalse(service.TrySettle(snapshot, data));

            Assert.AreEqual(0d, data.characters[0].currentCorruption);
            Assert.AreEqual(0d, data.characters[1].currentCorruption);
            Assert.AreEqual(7, data.saveRevision);
            Assert.AreEqual("before", data.lastSavedAtUtc);
        }

        [Test]
        public void Settle_SaveException_RollsBackBeforeRethrowing()
        {
            SaveData data = Data("A");
            DungeonSessionSnapshot snapshot = Snapshot(60d, new[] { "A" });
            var service = new DungeonCorruptionSettlementService(
                CharacterCatalog("A"), Config(300), () => throw new InvalidOperationException("save"));

            Assert.Throws<InvalidOperationException>(() => service.TrySettle(snapshot, data));
            Assert.AreEqual(0d, data.characters[0].currentCorruption);
        }

        [Test]
        public void Settle_AlreadyAtMaximum_DoesNotSave()
        {
            SaveData data = Data("A");
            data.characters[0].currentCorruption = 300d;
            int saves = 0;
            var service = new DungeonCorruptionSettlementService(
                CharacterCatalog("A"), Config(300), () => { saves++; return true; });

            Assert.IsFalse(service.TrySettle(Snapshot(60d, new[] { "A" }), data));
            Assert.AreEqual(0, saves);
        }

        private DungeonSessionSnapshot Snapshot(double elapsed, string[] participants)
        {
            DungeonDefinition dungeon = Track(ScriptableObject.CreateInstance<DungeonDefinition>());
            SerializedObject serialized = new SerializedObject(dungeon);
            serialized.FindProperty("dungeonId").stringValue = "test";
            serialized.FindProperty("corruptionIntervalSeconds").intValue = 60;
            serialized.FindProperty("corruptionGainPerInterval").intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var ledger = new DungeonSessionLedger();
            Assert.AreEqual(SessionStartResult.Started, ledger.TryStartSession(dungeon, participants));
            Assert.IsTrue(ledger.TryCompleteSession(elapsed, out DungeonSessionSnapshot snapshot));
            return snapshot;
        }

        private CharacterCatalog CharacterCatalog(params string[] ids)
        {
            return CharacterCatalog(0, ids);
        }

        private CharacterCatalog CharacterCatalog(int baseCorruption, params string[] ids)
        {
            CharacterCatalog catalog = Track(ScriptableObject.CreateInstance<CharacterCatalog>());
            SerializedObject serializedCatalog = new SerializedObject(catalog);
            SerializedProperty definitions = serializedCatalog.FindProperty("characters");
            definitions.arraySize = ids.Length;
            for (int i = 0; i < ids.Length; i++)
            {
                CharacterDefinition definition = Track(ScriptableObject.CreateInstance<CharacterDefinition>());
                SerializedObject serializedDefinition = new SerializedObject(definition);
                serializedDefinition.FindProperty("characterId").stringValue = ids[i];
                serializedDefinition.FindProperty("baseCorruption").intValue = baseCorruption;
                serializedDefinition.ApplyModifiedPropertiesWithoutUndo();
                definitions.GetArrayElementAtIndex(i).objectReferenceValue = definition;
            }
            serializedCatalog.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }

        private CorruptionConfigCatalog Config(int max)
        {
            CorruptionConfigDefinition definition = Track(ScriptableObject.CreateInstance<CorruptionConfigDefinition>());
            SerializedObject serializedDefinition = new SerializedObject(definition);
            serializedDefinition.FindProperty("configId").stringValue = "default";
            serializedDefinition.FindProperty("maxCorruption").intValue = max;
            serializedDefinition.FindProperty("warningThresholdPercent").intValue = 50;
            serializedDefinition.FindProperty("dangerThresholdPercent").intValue = 80;
            serializedDefinition.FindProperty("warningStaminaCostMultiplier").intValue = 2;
            serializedDefinition.FindProperty("dangerStaminaCostMultiplier").intValue = 3;
            serializedDefinition.FindProperty("enabled").boolValue = true;
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

            CorruptionConfigCatalog catalog = Track(ScriptableObject.CreateInstance<CorruptionConfigCatalog>());
            SerializedObject serializedCatalog = new SerializedObject(catalog);
            serializedCatalog.FindProperty("configs").arraySize = 1;
            serializedCatalog.FindProperty("configs").GetArrayElementAtIndex(0).objectReferenceValue = definition;
            serializedCatalog.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }

        private static SaveData Data(params string[] ids)
        {
            var data = new SaveData { characters = new List<CharacterSaveState>() };
            foreach (string id in ids) data.characters.Add(new CharacterSaveState { characterId = id });
            return data;
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            value.hideFlags = HideFlags.HideAndDontSave;
            created.Add(value);
            return value;
        }
    }
}
