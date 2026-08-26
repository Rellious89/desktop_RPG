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

        [TestCase(0L, 0.2d, 0d)]
        [TestCase(3L, 0.2d, 0.6d)]
        [TestCase(10L, 0.3d, 3d)]
        [TestCase(1L, 0d, 0d)]
        [TestCase(-1L, 1d, 0d)]
        public void CalculateGain_UsesDefeatCountOnly(long defeats, double gain, double expected)
        {
            Assert.AreEqual(expected, DungeonCorruptionSettlementService.CalculateGain(defeats, gain), 0.000000001d);
        }

        [Test]
        public void CalculateGain_Overflow_SaturatesAtDoubleMaximum()
        {
            Assert.AreEqual(double.MaxValue,
                DungeonCorruptionSettlementService.CalculateGain(long.MaxValue, double.MaxValue));
        }

        [Test]
        public void Settle_OnlyDefeatingCharactersReceiveGain_AndSaveOnce()
        {
            SaveData data = Data("A", "B", "C");
            DungeonSessionSnapshot snapshot = Snapshot(60d, new[] { "A", "B", "C" }, ("A", 10L), ("B", 3L));
            int saves = 0;
            var service = new DungeonCorruptionSettlementService(
                CharacterCatalog("A", "B", "C"), Config(300), () => { saves++; return true; });

            Assert.IsTrue(service.TrySettle(snapshot, data));

            Assert.AreEqual(1, saves);
            Assert.AreEqual(10d, data.characters[0].currentCorruption, 0.000000001d);
            Assert.AreEqual(3d, data.characters[1].currentCorruption, 0d);
            Assert.AreEqual(0d, data.characters[2].currentCorruption, 0d);
        }

        [Test]
        public void Settle_UsesBaseCapsAtConfigMaximum_AndDoesNotCreateUnknownState()
        {
            SaveData data = Data("A", "unknown");
            data.characters[0].currentCorruption = -2d;
            DungeonSessionSnapshot snapshot = Snapshot(60d, new[] { "A", "unknown", "missing" }, ("A", 10L), ("unknown", 1L));
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
            DungeonSessionSnapshot snapshot = Snapshot(60d, new[] { "A", "B" }, ("A", 1L), ("B", 1L));
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
            DungeonSessionSnapshot snapshot = Snapshot(60d, new[] { "A" }, ("A", 1L));
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

            Assert.IsFalse(service.TrySettle(Snapshot(60d, new[] { "A" }, ("A", 1L)), data));
            Assert.AreEqual(0, saves);
        }

        [Test]
        public void Settle_NoDefeats_DoesNotSaveRegardlessOfElapsedTime()
        {
            SaveData data = Data("A");
            int saves = 0;
            var service = new DungeonCorruptionSettlementService(
                CharacterCatalog("A"), Config(300), () => { saves++; return true; });

            Assert.IsFalse(service.TrySettle(Snapshot(999999d, new[] { "A" }), data));
            Assert.AreEqual(0d, data.characters[0].currentCorruption);
            Assert.AreEqual(0, saves);
        }

        [Test]
        public void Settle_FractionalGain_IsIndependentOfElapsedTimeAndPartySize()
        {
            SaveData shortSession = Data("A", "B", "C");
            SaveData longSession = Data("A", "B", "C");
            var service = new DungeonCorruptionSettlementService(
                CharacterCatalog("A", "B", "C"), Config(300), () => true);

            Assert.IsTrue(service.TrySettle(Snapshot(1d, new[] { "A", "B", "C" }, 0.2d, ("A", 3L)), shortSession));
            Assert.IsTrue(service.TrySettle(Snapshot(100000d, new[] { "A", "B", "C" }, 0.2d, ("A", 3L)), longSession));

            Assert.AreEqual(0.6d, shortSession.characters[0].currentCorruption, 0.000000001d);
            Assert.AreEqual(shortSession.characters[0].currentCorruption, longSession.characters[0].currentCorruption, 0d);
            Assert.AreEqual(0d, shortSession.characters[1].currentCorruption);
            Assert.AreEqual(0d, shortSession.characters[2].currentCorruption);
        }

        private DungeonSessionSnapshot Snapshot(
            double elapsed, string[] participants, params (string characterId, long defeats)[] defeats)
        {
            return Snapshot(elapsed, participants, 1d, defeats);
        }

        private DungeonSessionSnapshot Snapshot(
            double elapsed, string[] participants, double gainPerDefeat,
            params (string characterId, long defeats)[] defeats)
        {
            DungeonDefinition dungeon = Track(ScriptableObject.CreateInstance<DungeonDefinition>());
            SerializedObject serialized = new SerializedObject(dungeon);
            serialized.FindProperty("dungeonId").stringValue = "test";
            serialized.FindProperty("corruptionGainPerDefeat").doubleValue = gainPerDefeat;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var ledger = new DungeonSessionLedger();
            Assert.AreEqual(SessionStartResult.Started, ledger.TryStartSession(dungeon, participants));
            MonsterDefinition monster = Monster();
            foreach ((string characterId, long count) in defeats)
                for (long i = 0; i < count; i++) ledger.RecordDefeat(monster, characterId);
            Assert.IsTrue(ledger.TryCompleteSession(elapsed, out DungeonSessionSnapshot snapshot));
            return snapshot;
        }

        private MonsterDefinition Monster()
        {
            MonsterDefinition definition = Track(ScriptableObject.CreateInstance<MonsterDefinition>());
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("monsterId").stringValue = "monster";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
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
