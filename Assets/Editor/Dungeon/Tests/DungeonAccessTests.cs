using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Character;
using Common;
using Dungeon;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace DungeonEditor.Tests
{
    public sealed class DungeonAccessTests
    {
        private static readonly FieldInfo DataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo LoadResultField =
            typeof(SaveSystem).GetField("loadResult", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo ConfigureMethod =
            typeof(SaveSystem).GetMethod("ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo SetAccessServiceMethod =
            typeof(DungeonEntryService).GetMethod("SetAccessServiceForTests",
                BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private readonly List<DungeonDefinition> eventLog = new List<DungeonDefinition>();
        private int stateChangedEmissions;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(DataField);
            Assert.IsNotNull(LoadResultField);
            Assert.IsNotNull(ConfigureMethod);
            Assert.IsNotNull(SetAccessServiceMethod,
                "DungeonEntryService.SetAccessServiceForTests must be reachable via reflection");

            DungeonEntryService.ResetRequestState();
            SetRosterInstance(null);
        }

        [TearDown]
        public void TearDown()
        {
            DungeonEntryService.DungeonEnterRequested -= RecordEvent;
            CharacterRoster.CharacterStateChanged -= OnCharacterStateChanged;
            stateChangedEmissions = 0;

            DungeonEntryService.ResetRequestState();
            SetRosterInstance(null);

            ConfigureMethod.Invoke(null, new object[] { null, null, null });

            foreach (UnityEngine.Object obj in created)
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();
            eventLog.Clear();
        }

        // ---- DungeonAccessService: 등가/높음/낮음 ----

        [Test]
        public void Access_EqualLevel_Allowed()
        {
            var service = new DungeonAccessService(new StubLevelSource(5));
            DungeonAccessResult result = service.Evaluate(Dungeon("d1", requiredLevel: 5));

            Assert.IsTrue(result.Allowed);
            Assert.AreEqual(5, result.DungeonRequiredLevel);
            Assert.AreEqual(5, result.HighestPartyLevel);
            Assert.AreEqual(DungeonAccessFailureReason.None, result.FailureReason);
        }

        [Test]
        public void Access_HigherLevel_Allowed()
        {
            var service = new DungeonAccessService(new StubLevelSource(10));
            DungeonAccessResult result = service.Evaluate(Dungeon("d1", requiredLevel: 3));

            Assert.IsTrue(result.Allowed);
            Assert.AreEqual(10, result.HighestPartyLevel);
        }

        [Test]
        public void Access_LowerLevel_Denied()
        {
            var service = new DungeonAccessService(new StubLevelSource(2));
            DungeonAccessResult result = service.Evaluate(Dungeon("d1", requiredLevel: 5));

            Assert.IsFalse(result.Allowed);
            Assert.AreEqual(DungeonAccessFailureReason.InsufficientLevel, result.FailureReason);
            Assert.AreEqual(5, result.DungeonRequiredLevel);
            Assert.AreEqual(2, result.HighestPartyLevel);
        }

        // ---- 선택된 현재 캐릭터가 낮아도 다른 보유 캐릭터가 높으면 허용 ----

        [Test]
        public void Access_SelectedCurrentLowButAnotherOwnedHighAllows()
        {
            Inject(State("low", level: 1), State("high", level: 10));
            CharacterRoster roster = ReadyRoster("low", "high");

            SetPrivate(roster, "current", roster.Entries[0].definition);
            Assert.AreEqual("low", roster.Current.CharacterId);

            var service = new DungeonAccessService(roster);
            DungeonAccessResult result = service.Evaluate(Dungeon("d1", requiredLevel: 5));

            Assert.IsTrue(result.Allowed);
            Assert.AreEqual(10, result.HighestPartyLevel);
        }

        // ---- 모션 프로필 없는 보유 캐릭터 제외 ----

        [Test]
        public void Access_NoPlayableMotionProfileExcludedFromHighestLevel()
        {
            Inject(State("good", level: 3), State("noprofile", level: 99));

            CharacterDefinition goodDef = Definition("good");
            CharacterDefinition noProfileDef = DefinitionNoProfile("noprofile");

            LogAssert.Expect(LogType.Error, new Regex(
                @"\[CharacterRoster\].*noprofile.*Motion Profile"));
            CharacterRoster roster = ReadyRosterFromDefinitions(goodDef, noProfileDef);

            Assert.AreEqual(3, roster.HighestPartyCharacterLevel,
                "프로필 없는 캐릭터(level 99)는 usableEntries에서 제외되어야 한다.");
        }

        // ---- usableEntries 구축 후 저장 상태 제거 → 즉시 반영 ----

        [Test]
        public void Access_RemovedStateAfterBuildFallsToLow()
        {
            SaveData doc = Inject(State("low", level: 2), State("high", level: 10));
            CharacterRoster roster = ReadyRoster("low", "high");

            Assert.AreEqual(10, roster.HighestPartyCharacterLevel);

            doc.characters.RemoveAll(s => s.characterId == "high");

            Assert.AreEqual(2, roster.HighestPartyCharacterLevel,
                "저장 상태가 제거되면 재평가에 즉시 반영되어야 한다.");
        }

        // ---- 비파티 고레벨 제외 ----

        [Test]
        public void Access_NonPartyHighLevelCannotBypassRequirement()
        {
            SaveData document = Inject(State("low", level: 2), State("high", level: 99));
            document.partyCharacterIds = new List<string> { "low" };
            CharacterRoster roster = ReadyRoster("low", "high");

            var service = new DungeonAccessService(roster);
            DungeonAccessResult result = service.Evaluate(Dungeon("d1", requiredLevel: 5));

            Assert.IsFalse(result.Allowed);
            Assert.AreEqual(2, result.HighestPartyLevel, "보유했어도 비파티 고레벨은 입장 제한을 우회할 수 없다.");
        }

        // ---- 카탈로그에 없는 저장 ID 제외 ----

        [Test]
        public void Access_UnknownStoredIdExcluded()
        {
            Inject(State("known", level: 2), State("ghost", level: 99));
            CharacterRoster roster = ReadyRoster("known");

            var service = new DungeonAccessService(roster);
            Assert.AreEqual(2, roster.HighestPartyCharacterLevel);

            DungeonAccessResult result = service.Evaluate(Dungeon("d1", requiredLevel: 50));
            Assert.IsFalse(result.Allowed);
            Assert.AreEqual(2, result.HighestPartyLevel);
        }

        // ---- 사용 가능한 출전 파티원 0명 → 거부 ----

        [Test]
        public void Access_ZeroUsable_Denied()
        {
            var service = new DungeonAccessService(new StubLevelSource(0));
            DungeonAccessResult result = service.Evaluate(Dungeon("d1", requiredLevel: 1));

            Assert.IsFalse(result.Allowed);
            Assert.AreEqual(DungeonAccessFailureReason.NoUsablePartyCharacter, result.FailureReason);
        }

        // ---- 로스터/소스 없음 → 거부 ----

        [Test]
        public void Access_MissingSource_Denied()
        {
            var service = new DungeonAccessService(null);
            DungeonAccessResult result = service.Evaluate(Dungeon("d1", requiredLevel: 1));

            Assert.IsFalse(result.Allowed);
            Assert.AreEqual(DungeonAccessFailureReason.MissingRosterOrProgression, result.FailureReason);
        }

        // ---- 조회가 상태를 생성/변경하지 않는다 ----

        [Test]
        public void Access_QueryDoesNotCreateOrChangeStates()
        {
            SaveData doc = Inject(State("hero", level: 3));
            CharacterRoster roster = ReadyRoster("hero");
            string before = Describe(doc);

            stateChangedEmissions = 0;
            CharacterRoster.CharacterStateChanged += OnCharacterStateChanged;

            var service = new DungeonAccessService(roster);
            service.Evaluate(Dungeon("d1", requiredLevel: 1));
            service.Evaluate(Dungeon("d2", requiredLevel: 99));

            Assert.AreEqual(before, Describe(doc));
            Assert.AreEqual(0, stateChangedEmissions, "평가가 CharacterStateChanged를 발생시키면 안 된다.");
        }

        // ---- 상태 변경 후 재평가 즉시 반영 ----

        [Test]
        public void Access_ReevaluationReflectsLevelChanges()
        {
            SaveData doc = Inject(State("hero", level: 1));
            CharacterRoster roster = ReadyRoster("hero");
            var service = new DungeonAccessService(roster);

            Assert.IsFalse(service.Evaluate(Dungeon("d1", requiredLevel: 5)).Allowed);

            doc.characters[0].level = 10;

            Assert.IsTrue(service.Evaluate(Dungeon("d1", requiredLevel: 5)).Allowed);
            Assert.AreEqual(10, service.Evaluate(Dungeon("d1", requiredLevel: 5)).HighestPartyLevel);
        }

        // ---- 유효하지 않은 저장 레벨 < 1은 런타임 1로 읽는다 ----

        [Test]
        public void Access_InvalidStoredLevelTreatedAsOne()
        {
            SaveData doc = Inject(State("hero", level: -5));
            CharacterRoster roster = ReadyRoster("hero");

            Assert.AreEqual(1, roster.HighestPartyCharacterLevel);

            var service = new DungeonAccessService(roster);

            DungeonAccessResult allowed = service.Evaluate(Dungeon("d1", requiredLevel: 1));
            Assert.IsTrue(allowed.Allowed);
            Assert.AreEqual(1, allowed.HighestPartyLevel);

            DungeonAccessResult denied = service.Evaluate(Dungeon("d2", requiredLevel: 2));
            Assert.IsFalse(denied.Allowed);
            Assert.AreEqual(DungeonAccessFailureReason.InsufficientLevel, denied.FailureReason);

            Assert.AreEqual(-5, doc.characters[0].level, "저장 값을 수정하지 않는다.");
        }

        // ---- int.MaxValue ----

        [Test]
        public void Access_IntMaxValueLevelIsValid()
        {
            var service = new DungeonAccessService(new StubLevelSource(int.MaxValue));
            DungeonAccessResult result = service.Evaluate(Dungeon("d1", requiredLevel: 1));

            Assert.IsTrue(result.Allowed);
            Assert.AreEqual(int.MaxValue, result.HighestPartyLevel);
        }

        // ---- 유효하지 않은 던전 ----

        [Test]
        public void Access_NullDungeon_Denied()
        {
            var service = new DungeonAccessService(new StubLevelSource(10));
            DungeonAccessResult result = service.Evaluate(null);

            Assert.IsFalse(result.Allowed);
            Assert.AreEqual(DungeonAccessFailureReason.MissingOrInvalidDungeon, result.FailureReason);
        }

        [Test]
        public void Access_InvalidDungeon_Denied()
        {
            var service = new DungeonAccessService(new StubLevelSource(10));
            DungeonAccessResult result = service.Evaluate(Dungeon("", requiredLevel: 1));

            Assert.IsFalse(result.Allowed);
            Assert.AreEqual(DungeonAccessFailureReason.MissingOrInvalidDungeon, result.FailureReason);
        }

        // ---- DungeonEntryService: 허용된 요청 → 이벤트 발행, 추적 갱신 ----

        [Test]
        public void Request_Allowed_FiresOnceAndUpdateTracking()
        {
            DungeonEntryService.DungeonEnterRequested += RecordEvent;
            SetAccessOverride(
                new DungeonAccessService(new StubLevelSource(5)));

            DungeonDefinition dungeon = Dungeon("d1", requiredLevel: 3);
            bool accepted = DungeonEntryService.RequestEnterDungeon(dungeon);

            Assert.IsTrue(accepted);
            Assert.AreEqual(1, eventLog.Count);
            Assert.AreSame(dungeon, eventLog[0]);
            Assert.AreSame(dungeon, DungeonEntryService.LastRequestedDungeon);
            Assert.AreEqual("d1", DungeonEntryService.LastRequestedDungeonId);
            Assert.AreEqual(1, DungeonEntryService.AcceptedRequestCount);
        }

        // ---- 거부된 요청 → 이벤트/추적 불변 (이전 허용 후에도) ----

        [Test]
        public void Request_DeniedAfterPreviousAccept_TrackingUnchanged()
        {
            DungeonEntryService.DungeonEnterRequested += RecordEvent;
            SetAccessOverride(
                new DungeonAccessService(new StubLevelSource(5)));

            DungeonDefinition first = Dungeon("d1", requiredLevel: 1);
            DungeonEntryService.RequestEnterDungeon(first);
            Assert.AreEqual(1, DungeonEntryService.AcceptedRequestCount);

            SetAccessOverride(
                new DungeonAccessService(new StubLevelSource(1)));

            DungeonDefinition second = Dungeon("d2", requiredLevel: 99);
            bool accepted = DungeonEntryService.RequestEnterDungeon(second);

            Assert.IsFalse(accepted);
            Assert.AreEqual(1, eventLog.Count);
            Assert.AreSame(first, DungeonEntryService.LastRequestedDungeon);
            Assert.AreEqual("d1", DungeonEntryService.LastRequestedDungeonId);
            Assert.AreEqual(1, DungeonEntryService.AcceptedRequestCount);
        }

        // ---- 직접 서비스 요청은 우회할 수 없다 ----

        [Test]
        public void Request_CannotBypassAccessCheck()
        {
            DungeonEntryService.DungeonEnterRequested += RecordEvent;
            SetAccessOverride(
                new DungeonAccessService(new StubLevelSource(1)));

            DungeonDefinition dungeon = Dungeon("d1", requiredLevel: 5);
            bool accepted = DungeonEntryService.RequestEnterDungeon(dungeon);

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, eventLog.Count);
            Assert.AreEqual(0, DungeonEntryService.AcceptedRequestCount);
        }

        // ---- 평가기/로스터 없으면 거부 ----

        [Test]
        public void Request_AbsentEvaluator_Denied()
        {
            DungeonEntryService.DungeonEnterRequested += RecordEvent;
            SetRosterInstance(null);

            DungeonDefinition dungeon = Dungeon("d1", requiredLevel: 1);
            bool accepted = DungeonEntryService.RequestEnterDungeon(dungeon);

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, eventLog.Count);
            Assert.AreEqual(0, DungeonEntryService.AcceptedRequestCount);
        }

        [Test]
        public void Request_AbsentRoster_Denied()
        {
            DungeonEntryService.DungeonEnterRequested += RecordEvent;
            DungeonEntryService.ResetRequestState();
            SetRosterInstance(null);

            DungeonDefinition dungeon = Dungeon("d1", requiredLevel: 1);
            bool accepted = DungeonEntryService.RequestEnterDungeon(dungeon);

            Assert.IsFalse(accepted);
            Assert.AreEqual(0, eventLog.Count);
        }

        // ---- 유효하지 않은 던전 요청 ----

        [Test]
        public void Request_InvalidDungeon_DeniedWithoutEvent()
        {
            DungeonEntryService.DungeonEnterRequested += RecordEvent;
            SetAccessOverride(new DungeonAccessService(new StubLevelSource(99)));

            LogAssert.Expect(LogType.Error,
                "[DungeonEntryService] 입장 요청에 던전이 없습니다 - 요청을 무시합니다.");
            Assert.IsFalse(DungeonEntryService.RequestEnterDungeon(null));

            LogAssert.Expect(LogType.Error, new Regex(
                @"\[DungeonEntryService\].*Dungeon Id가 없어"));
            Assert.IsFalse(DungeonEntryService.RequestEnterDungeon(Dungeon("", requiredLevel: 1)));

            Assert.AreEqual(0, eventLog.Count);
            Assert.AreEqual(0, DungeonEntryService.AcceptedRequestCount);
        }

        // ---- 도우미 ----

        private void RecordEvent(DungeonDefinition d) => eventLog.Add(d);
        private void OnCharacterStateChanged(CharacterDefinition _) => stateChangedEmissions++;

        private static void SetAccessOverride(DungeonAccessService service)
        {
            SetAccessServiceMethod.Invoke(null, new object[] { service });
        }

        private DungeonDefinition Dungeon(string id, int requiredLevel = 1)
        {
            var def = ScriptableObject.CreateInstance<DungeonDefinition>();
            created.Add(def);

            var so = new SerializedObject(def);
            so.FindProperty("dungeonId").stringValue = id;
            so.FindProperty("requiredCharacterLevel").intValue = requiredLevel;
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        private static SaveData Inject(params CharacterSaveState[] states)
        {
            var doc = new SaveData
            {
                characters = new List<CharacterSaveState>(states),
                partyCharacterIds = new List<string>(Array.ConvertAll(states, state => state != null ? state.characterId : null)),
            };
            DataField.SetValue(null, doc);
            LoadResultField.SetValue(null, SaveLoadResult.NewGame(doc));
            return doc;
        }

        private static CharacterSaveState State(string id, int level = 1, int stamina = 10)
        {
            return new CharacterSaveState { characterId = id, level = level, currentStamina = stamina };
        }

        private CharacterRoster ReadyRoster(params string[] catalogIds)
        {
            var host = new GameObject("AccessTestHost");
            created.Add(host);
            host.SetActive(false);

            CharacterRoster roster = host.AddComponent<CharacterRoster>();
            SetPrivate(roster, "catalog", Catalog(catalogIds));
            SetPrivate(roster, "owned", new OwnedCharacterCollection(
                (CharacterCatalog)GetPrivate(roster, "catalog"), SaveSystem.Data));

            Invoke(roster, "BuildUsableEntries");
            return roster;
        }

        private CharacterCatalog Catalog(params string[] ids)
        {
            var definitions = new CharacterDefinition[ids.Length];
            for (int i = 0; i < ids.Length; i++) definitions[i] = Definition(ids[i]);

            var catalog = ScriptableObject.CreateInstance<CharacterCatalog>();
            created.Add(catalog);

            var so = new SerializedObject(catalog);
            SerializedProperty list = so.FindProperty("characters");
            list.arraySize = definitions.Length;
            for (int i = 0; i < definitions.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }

        private CharacterDefinition Definition(string id)
        {
            var def = ScriptableObject.CreateInstance<CharacterDefinition>();
            created.Add(def);

            var so = new SerializedObject(def);
            so.FindProperty("characterId").stringValue = id;
            so.FindProperty("initiallyOwned").boolValue = true;
            so.FindProperty("maxStamina").intValue = 30;
            so.FindProperty("motionProfile").objectReferenceValue = Profile();
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        private CharacterDefinition DefinitionNoProfile(string id)
        {
            var def = ScriptableObject.CreateInstance<CharacterDefinition>();
            created.Add(def);

            var so = new SerializedObject(def);
            so.FindProperty("characterId").stringValue = id;
            so.FindProperty("initiallyOwned").boolValue = true;
            so.FindProperty("maxStamina").intValue = 30;
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        private CharacterCatalog CatalogFromDefinitions(params CharacterDefinition[] definitions)
        {
            var catalog = ScriptableObject.CreateInstance<CharacterCatalog>();
            created.Add(catalog);

            var so = new SerializedObject(catalog);
            SerializedProperty list = so.FindProperty("characters");
            list.arraySize = definitions.Length;
            for (int i = 0; i < definitions.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }

        private CharacterRoster ReadyRosterFromDefinitions(params CharacterDefinition[] definitions)
        {
            var host = new GameObject("AccessTestHost");
            created.Add(host);
            host.SetActive(false);

            CharacterRoster roster = host.AddComponent<CharacterRoster>();
            CharacterCatalog cat = CatalogFromDefinitions(definitions);
            SetPrivate(roster, "catalog", cat);
            SetPrivate(roster, "owned", new OwnedCharacterCollection(cat, SaveSystem.Data));
            Invoke(roster, "BuildUsableEntries");
            return roster;
        }

        private CharacterMotionProfile Profile()
        {
            var profile = ScriptableObject.CreateInstance<CharacterMotionProfile>();
            created.Add(profile);

            var tex = new Texture2D(4, 4);
            created.Add(tex);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            created.Add(sprite);

            var so = new SerializedObject(profile);
            SerializedProperty frames = so.FindProperty("baseIdle").FindPropertyRelative("frames");
            frames.arraySize = 1;
            frames.GetArrayElementAtIndex(0).objectReferenceValue = sprite;
            so.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        private static void SetRosterInstance(CharacterRoster roster)
        {
            PropertyInfo prop = typeof(CharacterRoster).GetProperty(
                "Instance", BindingFlags.Public | BindingFlags.Static);
            MethodInfo setter = prop.GetSetMethod(nonPublic: true);
            Assert.IsNotNull(setter);
            setter.Invoke(null, new object[] { roster });
        }

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{field} not found");
            info.SetValue(target, value);
        }

        private static object GetPrivate(object target, string field)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info);
            return info.GetValue(target);
        }

        private static object Invoke(object target, string method)
        {
            MethodInfo mi = target.GetType().GetMethod(method,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi);
            return mi.Invoke(target, null);
        }

        private static string Describe(SaveData doc)
        {
            if (doc.characters == null) return "(null)";
            var parts = new List<string>();
            foreach (CharacterSaveState s in doc.characters)
                parts.Add(s == null ? "(null)" : $"{s.characterId}:{s.level}:{s.currentStamina}");
            return string.Join("|", parts);
        }

        private sealed class StubLevelSource : IPartyCharacterLevelSource
        {
            private readonly int level;
            public StubLevelSource(int level) { this.level = level; }
            public int HighestPartyCharacterLevel => level;
        }
    }
}
