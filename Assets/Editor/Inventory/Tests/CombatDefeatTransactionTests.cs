using System;
using System.Collections.Generic;
using System.Reflection;
using Character;
using Common;
using Corruption;
using Dungeon;
using Field;
using Inventory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace InventoryEditor.Tests
{
    /// <summary>처치 조정자가 메모리 변경을 한 번 저장으로 확정하는지 확인한다. 모든 저장은 메모리다.</summary>
    public sealed class CombatDefeatTransactionTests
    {
        private static readonly FieldInfo DataField = typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo LoadField = typeof(SaveSystem).GetField("loadResult", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo Configure = typeof(SaveSystem).GetMethod("ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(DataField); Assert.IsNotNull(LoadField); Assert.IsNotNull(Configure);
        }

        [TearDown]
        public void TearDown()
        {
            Configure.Invoke(null, new object[] { null, null, null });
            foreach (UnityEngine.Object value in created) if (value != null) UnityEngine.Object.DestroyImmediate(value);
            created.Clear();
            SetStaticInstance(typeof(InventoryManager), null);
            SetStaticInstance(typeof(CharacterRoster), null);
        }

        [Test]
        public void Defeat_ChangesAllValues_AndSavesExactlyOnce()
        {
            var storage = Setup(out DefeatRewardDistributor distributor, out CharacterSaveState state);
            int inventoryEvents = 0;
            InventoryManager.InventoryChanged += CountInventory;
            try
            {
                InvokeDefeat(distributor, Monster());
                Assert.AreEqual(1, storage.Writes);
                Assert.AreEqual(5, SaveSystem.Data.currency);
                Assert.AreEqual(1, SaveSystem.Data.totalKillCount);
                Assert.AreEqual(1, state.currentExp);
                Assert.AreEqual(9, state.currentStamina);
                Assert.AreEqual(0.2d, state.currentCorruption, 0.000001d);
                Assert.AreEqual(1, inventoryEvents);

                // 이후 AnyTargetDefeated는 표시/오디오 소비자만 받으며, 저장 데이터는 다시 적용되지 않는다.
                InvokeAnyTargetDefeated("same-defeat");
                Assert.AreEqual(1, storage.Writes);
                Assert.AreEqual(1, SaveSystem.Data.totalKillCount);
                Assert.AreEqual(1, state.currentExp);
                Assert.AreEqual(9, state.currentStamina);
                Assert.AreEqual(0.2d, state.currentCorruption, 0.000001d);
            }
            finally
            {
                InventoryManager.InventoryChanged -= CountInventory;
            }
            void CountInventory() => inventoryEvents++;
        }

        [Test]
        public void Defeat_SaveFailure_RollsBackWithoutSuccessNotifications()
        {
            var storage = Setup(out DefeatRewardDistributor distributor, out CharacterSaveState state, writeSucceeds: false);
            int inventoryEvents = 0;
            InventoryManager.InventoryChanged += CountInventory;
            try
            {
                InvokeDefeat(distributor, Monster());
                Assert.AreEqual(1, storage.Writes);
                Assert.AreEqual(0, SaveSystem.Data.currency);
                Assert.AreEqual(0, SaveSystem.Data.totalKillCount);
                Assert.AreEqual(0, state.currentExp);
                Assert.AreEqual(10, state.currentStamina);
                Assert.AreEqual(0d, state.currentCorruption);
                Assert.AreEqual(0, inventoryEvents);
            }
            finally
            {
                InventoryManager.InventoryChanged -= CountInventory;
            }
            void CountInventory() => inventoryEvents++;
        }

        private Storage Setup(out DefeatRewardDistributor distributor, out CharacterSaveState state, bool writeSucceeds = true)
        {
            state = new CharacterSaveState { characterId = "hero", level = 1, currentExp = 0, currentStamina = 10, currentCorruption = 0d };
            var data = new SaveData { characters = new List<CharacterSaveState> { state }, partyCharacterIds = new List<string> { "hero" } };
            DataField.SetValue(null, data); LoadField.SetValue(null, SaveLoadResult.Loaded(data, SaveData.CurrentSaveVersion));
            var storage = new Storage(writeSucceeds); Configure.Invoke(null, new object[] { storage, null, null });
            DataField.SetValue(null, data); LoadField.SetValue(null, SaveLoadResult.Loaded(data, SaveData.CurrentSaveVersion));

            CharacterDefinition hero = Character("hero"); CharacterCatalog characters = CharacterCatalog(hero);
            var rosterGo = NewGo("Roster"); var roster = rosterGo.AddComponent<CharacterRoster>();
            Set(roster, "catalog", characters); Set(roster, "owned", new OwnedCharacterCollection(characters, data));
            Invoke(roster, "BuildUsableEntries"); Set(roster, "current", hero); SetStaticInstance(typeof(CharacterRoster), roster);

            var progressGo = NewGo("Progress"); var progress = progressGo.AddComponent<PlayerProgress>();
            Set(progress, "expPerTargetDefeat", 1); Invoke(progress, "Awake");
            var inventoryGo = NewGo("Inventory"); var inventory = inventoryGo.AddComponent<InventoryManager>(); Invoke(inventory, "Awake");
            var fmmGo = NewGo("Fmm"); var fmm = fmmGo.AddComponent<FieldModeManager>();
            DungeonDefinition dungeon = Dungeon();
            SetAuto(fmm, "CurrentMode", FieldMode.Dungeon); SetAuto(fmm, "CurrentDungeon", dungeon);
            var distributorGo = NewGo("Distributor"); distributor = distributorGo.AddComponent<DefeatRewardDistributor>();
            Set(distributor, "inventoryManager", inventory); Set(distributor, "playerProgress", progress); Set(distributor, "characterRoster", roster);
            Set(distributor, "fieldModeManager", fmm); Set(distributor, "characterCatalog", characters); Set(distributor, "corruptionConfigCatalog", Config());
            return storage;
        }

        private GameObject NewGo(string name) { var go = new GameObject(name); created.Add(go); go.SetActive(false); return go; }
        private CharacterDefinition Character(string id) { var d = ScriptableObject.CreateInstance<CharacterDefinition>(); created.Add(d); var so = new SerializedObject(d); so.FindProperty("characterId").stringValue = id; so.FindProperty("maxStamina").intValue = 10; so.FindProperty("motionProfile").objectReferenceValue = Profile(); so.ApplyModifiedPropertiesWithoutUndo(); return d; }
        private CharacterMotionProfile Profile() { var p = ScriptableObject.CreateInstance<CharacterMotionProfile>(); created.Add(p); var t = new Texture2D(2, 2); created.Add(t); var s = Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f)); created.Add(s); var so = new SerializedObject(p); var frames = so.FindProperty("baseIdle").FindPropertyRelative("frames"); frames.arraySize = 1; frames.GetArrayElementAtIndex(0).objectReferenceValue = s; so.ApplyModifiedPropertiesWithoutUndo(); return p; }
        private CharacterCatalog CharacterCatalog(CharacterDefinition definition) { var c = ScriptableObject.CreateInstance<CharacterCatalog>(); created.Add(c); var so = new SerializedObject(c); var p = so.FindProperty("characters"); p.arraySize = 1; p.GetArrayElementAtIndex(0).objectReferenceValue = definition; so.ApplyModifiedPropertiesWithoutUndo(); return c; }
        private DungeonDefinition Dungeon() { var d = ScriptableObject.CreateInstance<DungeonDefinition>(); created.Add(d); var so = new SerializedObject(d); so.FindProperty("dungeonId").stringValue = "test"; so.FindProperty("corruptionGainPerDefeat").doubleValue = 0.2d; so.ApplyModifiedPropertiesWithoutUndo(); return d; }
        private CorruptionConfigCatalog Config() { var d = ScriptableObject.CreateInstance<CorruptionConfigDefinition>(); created.Add(d); var s = new SerializedObject(d); s.FindProperty("configId").stringValue = "default"; s.FindProperty("maxCorruption").intValue = 300; s.FindProperty("warningThresholdPercent").intValue = 50; s.FindProperty("dangerThresholdPercent").intValue = 80; s.FindProperty("warningStaminaCostMultiplier").intValue = 2; s.FindProperty("dangerStaminaCostMultiplier").intValue = 3; s.FindProperty("enabled").boolValue = true; s.ApplyModifiedPropertiesWithoutUndo(); var c = ScriptableObject.CreateInstance<CorruptionConfigCatalog>(); created.Add(c); var cs = new SerializedObject(c); var p = cs.FindProperty("configs"); p.arraySize = 1; p.GetArrayElementAtIndex(0).objectReferenceValue = d; cs.ApplyModifiedPropertiesWithoutUndo(); return c; }
        private MonsterDefinition Monster() { var c = ScriptableObject.CreateInstance<CurrencyDefinition>(); created.Add(c); var cs = new SerializedObject(c); cs.FindProperty("currencyId").stringValue = "gold"; cs.ApplyModifiedPropertiesWithoutUndo(); var m = ScriptableObject.CreateInstance<MonsterDefinition>(); created.Add(m); var s = new SerializedObject(m); s.FindProperty("monsterId").stringValue = "monster"; s.FindProperty("currency").objectReferenceValue = c; s.FindProperty("currencyAmountMin").intValue = 5; s.FindProperty("currencyAmountMax").intValue = 5; s.ApplyModifiedPropertiesWithoutUndo(); return m; }
        private static void InvokeDefeat(DefeatRewardDistributor d, MonsterDefinition m) => Invoke(d, "HandleMonsterDefeated", m);
        private static void InvokeAnyTargetDefeated(string id) { var f = typeof(Target).GetField("AnyTargetDefeated", BindingFlags.NonPublic | BindingFlags.Static); ((Action<string>)f.GetValue(null))?.Invoke(id); }
        private static void Invoke(object target, string name, params object[] args) { typeof(object).ToString(); var m = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance); Assert.IsNotNull(m); m.Invoke(target, args); }
        private static void Set(object target, string name, object value) { target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value); }
        private static void SetAuto(object target, string name, object value) { target.GetType().GetField("<" + name + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value); }
        private static void SetStaticInstance(Type type, object value) => type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetSetMethod(true)?.Invoke(null, new[] { value });
        private sealed class Storage : ISaveStorage { private readonly bool succeeds; public int Writes; public Storage(bool succeeds) { this.succeeds = succeeds; } public bool WritesBlocked => false; public string BlockedReason => null; public SaveReadResult ReadPrimary() => SaveReadResult.Missing("memory"); public SaveReadResult ReadBackup() => SaveReadResult.Missing("memory"); public SaveWriteResult Write(string text) { Writes++; return succeeds ? SaveWriteResult.Written(true) : SaveWriteResult.Failed("fail"); } public SaveQuarantineResult QuarantinePrimary(string reason) => SaveQuarantineResult.Moved("memory"); }
    }
}
