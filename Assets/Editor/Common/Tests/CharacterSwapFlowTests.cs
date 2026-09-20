using System;
using System.Collections.Generic;
using System.Reflection;
using Character;
using Common;
using NUnit.Framework;
using Recovery;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CommonEditor.Tests
{
    /// <summary>
    /// Character Swap 패널과 Character HUD가 공유하는 회복 완료 교체 경로의 격리 EditMode 시험.
    /// 실제 저장 파일은 사용하지 않고 RecoveryStation의 저장 콜백 횟수만 센다.
    /// </summary>
    public sealed class CharacterSwapFlowTests
    {
        private static readonly FieldInfo DataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo LoadResultField =
            typeof(SaveSystem).GetField("loadResult", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo ConfigureMethod =
            typeof(SaveSystem).GetMethod("ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<Object> created = new List<Object>();
        private SaveData data;
        private DateTime now;
        private int saves;

        [SetUp]
        public void SetUp()
        {
            Assert.NotNull(DataField);
            Assert.NotNull(LoadResultField);
            Assert.NotNull(ConfigureMethod);

            now = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
            saves = 0;
            data = new SaveData
            {
                characters = new List<CharacterSaveState>
                {
                    State("CatKnight", 20),
                    State("ElfArcher", 10),
                },
                partyCharacterIds = new List<string> { "CatKnight", "ElfArcher" },
                recoverySlots = new List<RecoverySlotSaveState>(),
            };
            DataField.SetValue(null, data);
            LoadResultField.SetValue(null, SaveLoadResult.NewGame(data));
        }

        [TearDown]
        public void TearDown()
        {
            ConfigureMethod.Invoke(null, new object[] { null, null, null });
            foreach (Object value in created)
            {
                if (value != null) Object.DestroyImmediate(value);
            }
            created.Clear();
        }

        [Test]
        public void RecoveringCharacter_RemainsBlockedAndDoesNotLeaveSlot()
        {
            CharacterRoster roster = ReadyRoster();
            CharacterDefinition target = roster.Entries[1].definition;
            data.recoverySlots.Add(Slot(target.CharacterId, now, now.AddMinutes(10)));
            RecoveryStation station = Station(roster);

            Assert.AreEqual(CharacterRoster.SwapBlockReason.InRecovery,
                CharacterSwapFlow.GetBlockReason(roster, target, station));
            Assert.IsFalse(CharacterSwapFlow.TrySwitch(
                roster, target, out CharacterRoster.SwapBlockReason reason,
                out bool joinedFromRecovery, station));

            Assert.AreEqual(CharacterRoster.SwapBlockReason.InRecovery, reason);
            Assert.IsFalse(joinedFromRecovery);
            Assert.IsTrue(data.recoverySlots[0].HasCharacter);
            Assert.AreEqual("CatKnight", roster.Current.CharacterId);
            Assert.AreEqual(0, saves);
        }

        [Test]
        public void RecoveryCompleteCharacter_JoinsAndSwitchesThroughSharedFlow()
        {
            CharacterRoster roster = ReadyRoster();
            CharacterDefinition target = roster.Entries[1].definition;
            data.recoverySlots.Add(Slot(target.CharacterId, now.AddMinutes(-10), now));
            RecoveryStation station = Station(roster);

            Assert.AreEqual(CharacterRoster.SwapBlockReason.None,
                CharacterSwapFlow.GetBlockReason(roster, target, station),
                "Character Swap 목록과 HUD 확인 버튼이 함께 쓰는 판정은 완료 슬롯만 허용해야 한다.");

            LogAssert.ignoreFailingMessages = true;
            bool switched = CharacterSwapFlow.TrySwitch(
                roster, target, out CharacterRoster.SwapBlockReason reason,
                out bool joinedFromRecovery, station);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsTrue(switched);
            Assert.AreEqual(CharacterRoster.SwapBlockReason.None, reason);
            Assert.IsTrue(joinedFromRecovery);
            Assert.IsFalse(data.recoverySlots[0].HasCharacter, "기존 RecoveryStation 합류 경로가 슬롯을 비워야 한다.");
            Assert.AreEqual(target.MaxStamina, roster.GetStamina(target));
            Assert.AreSame(target, roster.Current);
            Assert.AreEqual(1, saves, "자동 합류는 기존 합류 트랜잭션의 저장 한 번만 사용해야 한다.");
        }

        [Test]
        public void OrdinaryCharacterSwitch_DoesNotTouchRecoveryOrSave()
        {
            CharacterRoster roster = ReadyRoster();
            CharacterDefinition target = roster.Entries[1].definition;
            RecoveryStation station = Station(roster);

            LogAssert.ignoreFailingMessages = true;
            bool switched = CharacterSwapFlow.TrySwitch(
                roster, target, out CharacterRoster.SwapBlockReason reason,
                out bool joinedFromRecovery, station);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsTrue(switched);
            Assert.AreEqual(CharacterRoster.SwapBlockReason.None, reason);
            Assert.IsFalse(joinedFromRecovery);
            Assert.AreSame(target, roster.Current);
            Assert.AreEqual(0, saves);
        }

        [Test]
        public void RecoveryStationManualJoin_StillUsesExistingSlotFlow()
        {
            CharacterRoster roster = ReadyRoster();
            CharacterDefinition target = roster.Entries[1].definition;
            data.recoverySlots.Add(Slot(target.CharacterId, now.AddMinutes(-10), now));
            RecoveryStation station = Station(roster);

            Assert.IsTrue(station.TryJoin(0, out CharacterDefinition joined));
            Assert.AreSame(target, joined);
            Assert.IsFalse(data.recoverySlots[0].HasCharacter);
            Assert.AreEqual("CatKnight", roster.Current.CharacterId,
                "회복소의 기존 합류 버튼 경로는 현재 캐릭터를 직접 바꾸지 않는다.");
            Assert.AreEqual(1, saves);
        }

        private RecoveryStation Station(CharacterRoster roster)
        {
            return new RecoveryStation(
                RecoveryBalance.Default,
                new CharacterRosterRecoveryAdapter(roster),
                new FakeWallet(),
                () => data,
                () => { saves++; return true; },
                () => now);
        }

        private CharacterRoster ReadyRoster()
        {
            CharacterDefinition first = Definition("CatKnight");
            CharacterDefinition second = Definition("ElfArcher");
            CharacterCatalog catalog = Catalog(first, second);

            var host = new GameObject("CharacterSwapFlowRoster");
            created.Add(host);
            host.SetActive(false);
            CharacterRoster roster = host.AddComponent<CharacterRoster>();
            SetPrivate(roster, "catalog", catalog);
            SetPrivate(roster, "owned", new OwnedCharacterCollection(catalog, data));
            Invoke(roster, "BuildUsableEntries");
            Invoke(roster, "NormalizeOwnedStamina");

            SetPrivate(roster, "runtimeActor", RuntimeActor());
            SetPrivate(roster, "current", roster.Entries[0].definition);
            return roster;
        }

        private CharacterRuntimeActor RuntimeActor()
        {
            var host = new GameObject("CharacterSwapFlowRuntimeActor");
            created.Add(host);
            host.SetActive(false);
            host.AddComponent<SpriteRenderer>();
            host.AddComponent<FlashOnCue>();
            host.AddComponent<HitEffectSpawner>();
            host.AddComponent<ActorOutlineController>();
            host.AddComponent<PlayerCharacterAnimator>();
            host.AddComponent<AttackMovement>();
            return host.AddComponent<CharacterRuntimeActor>();
        }

        private CharacterCatalog Catalog(params CharacterDefinition[] definitions)
        {
            var catalog = ScriptableObject.CreateInstance<CharacterCatalog>();
            created.Add(catalog);
            var serialized = new SerializedObject(catalog);
            SerializedProperty characters = serialized.FindProperty("characters");
            characters.arraySize = definitions.Length;
            for (int i = 0; i < definitions.Length; i++)
            {
                characters.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }

        private CharacterDefinition Definition(string id)
        {
            var definition = ScriptableObject.CreateInstance<CharacterDefinition>();
            created.Add(definition);
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("characterId").stringValue = id;
            serialized.FindProperty("maxStamina").intValue = 30;
            serialized.FindProperty("motionProfile").objectReferenceValue = Profile();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private CharacterMotionProfile Profile()
        {
            var profile = ScriptableObject.CreateInstance<CharacterMotionProfile>();
            created.Add(profile);
            var texture = new Texture2D(4, 4);
            created.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            created.Add(sprite);

            var serialized = new SerializedObject(profile);
            SerializedProperty frames = serialized.FindProperty("baseIdle").FindPropertyRelative("frames");
            frames.arraySize = 1;
            frames.GetArrayElementAtIndex(0).objectReferenceValue = sprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        private static RecoverySlotSaveState Slot(string characterId, DateTime startedAt, DateTime completeAt)
        {
            return new RecoverySlotSaveState
            {
                characterId = characterId,
                startStamina = 10,
                startedAtUtc = RecoveryStation.FormatUtc(startedAt),
                completeAtUtc = RecoveryStation.FormatUtc(completeAt),
            };
        }

        private static CharacterSaveState State(string id, int stamina)
        {
            return new CharacterSaveState { characterId = id, level = 1, currentStamina = stamina };
        }

        private static void SetPrivate(CharacterRoster roster, string field, object value)
        {
            FieldInfo info = typeof(CharacterRoster).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(info, $"CharacterRoster.{field}를 찾지 못했습니다.");
            info.SetValue(roster, value);
        }

        private static void Invoke(CharacterRoster roster, string methodName)
        {
            MethodInfo method = typeof(CharacterRoster).GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method, $"CharacterRoster.{methodName}을 찾지 못했습니다.");
            method.Invoke(roster, null);
        }

        private sealed class FakeWallet : IRecoveryWallet
        {
            public string CurrencyId => "Jewel";
            public int Balance => int.MaxValue;
            public bool TrySpendWithoutSave(int amount) => true;
            public void RefundWithoutSave(int amount) { }
            public void NotifyChangedAfterExternalSave() { }
        }
    }
}
