using System;
using System.Collections.Generic;
using Character;
using Common;
using NUnit.Framework;
using Recovery;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RecoveryEditor.Tests
{
    /// <summary>자연 행동력 회복의 저장/시간 경계만 확인하는 격리 EditMode 시험.</summary>
    public sealed class PassiveStaminaRecoveryServiceTests
    {
        private readonly List<Object> created = new List<Object>();
        private SaveData data;
        private FakeRoster roster;
        private DateTime now;
        private int saves;
        private bool saveSucceeds;
        private int events;

        [SetUp]
        public void SetUp()
        {
            now = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc);
            data = new SaveData { characters = new List<CharacterSaveState>(), partyCharacterIds = new List<string>(), recoverySlots = new List<RecoverySlotSaveState>() };
            roster = new FakeRoster(data);
            saves = 0;
            saveSucceeds = true;
            events = 0;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object value in created) if (value != null) Object.DestroyImmediate(value);
            created.Clear();
        }

        [Test]
        public void PartyAndNonParty_UseThirtyAndTenPercentOfRecoveryStationSpeed()
        {
            CharacterDefinition party = Add("party", 0, now);
            CharacterDefinition nonParty = Add("nonParty", 0, now);
            data.partyCharacterIds.Add("party");
            now = now.AddSeconds(300);

            PassiveStaminaRecoveryService.TickResult result = Service().Tick();

            Assert.AreEqual(3, roster.GetStamina(party));
            Assert.AreEqual(1, roster.GetStamina(nonParty));
            Assert.AreEqual(2, result.StaminaChangedCount);
            Assert.AreEqual(1, saves);
            Assert.AreEqual(2, events);
        }

        [Test]
        public void RecoveryStationCharacter_IsExcludedAndItsPassiveTimeIsCleared()
        {
            CharacterDefinition party = Add("party", 1, now.AddMinutes(-10), progress: 77);
            data.partyCharacterIds.Add("party");
            data.recoverySlots.Add(new RecoverySlotSaveState { characterId = "party" });

            Service().Tick();

            Assert.AreEqual(1, roster.GetStamina(party));
            Assert.AreEqual(Format(now), data.characters[0].passiveStaminaLastCalculatedUtc);
            Assert.AreEqual(0, data.characters[0].passiveStaminaProgress);
            Assert.AreEqual(0, saves);
        }

        [Test]
        public void FirstPassiveTick_SetsBaselineWithoutRetroactiveRecovery()
        {
            CharacterDefinition character = Add("hero", 0, null);
            now = now.AddDays(20);

            PassiveStaminaRecoveryService.TickResult result = Service().Tick();

            Assert.AreEqual(0, roster.GetStamina(character));
            Assert.AreEqual(Format(now), data.characters[0].passiveStaminaLastCalculatedUtc);
            Assert.AreEqual(0, data.characters[0].passiveStaminaProgress);
            Assert.AreEqual(0, result.StaminaChangedCount);
            Assert.AreEqual(0, saves);
        }

        [Test]
        public void PartialProgress_CarriesAcrossTicksUsingIntegerRemainder()
        {
            CharacterDefinition character = Add("hero", 0, now);
            now = now.AddSeconds(100);
            Service().Tick();
            Assert.AreEqual(0, roster.GetStamina(character));
            Assert.Greater(data.characters[0].passiveStaminaProgress, 0);

            now = now.AddSeconds(200);
            Service().Tick();
            Assert.AreEqual(1, roster.GetStamina(character));
            Assert.AreEqual(0, data.characters[0].passiveStaminaProgress);
            Assert.AreEqual(1, saves);
        }

        [Test]
        public void FullStamina_ClearsProgressSoLaterLossCannotBankTime()
        {
            CharacterDefinition character = Add("hero", 10, now.AddHours(-1), progress: 9);
            Service().Tick();
            Assert.AreEqual(0, data.characters[0].passiveStaminaProgress);
            Assert.AreEqual(Format(now), data.characters[0].passiveStaminaLastCalculatedUtc);

            roster.SetStamina(character, 9);
            now = now.AddSeconds(300);
            Service().Tick();
            Assert.AreEqual(10, roster.GetStamina(character), "비파티 10%는 최대 행동력 이후 300초만 계산해야 한다.");
        }

        [Test]
        public void BackwardClock_DoesNotDecreaseOrReuseProgress()
        {
            CharacterDefinition character = Add("hero", 2, now, progress: 17);
            now = now.AddMinutes(-1);

            Service().Tick();

            Assert.AreEqual(2, roster.GetStamina(character));
            Assert.AreEqual(Format(new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc)), data.characters[0].passiveStaminaLastCalculatedUtc);
            Assert.AreEqual(17, data.characters[0].passiveStaminaProgress);
            Assert.AreEqual(0, saves);
        }

        [Test]
        public void UnknownSavedCharacter_IsPreservedWithoutNaturalRecoveryMutation()
        {
            Add("known", 0, now);
            var unknown = new CharacterSaveState { characterId = "unknown", currentStamina = 4, passiveStaminaLastCalculatedUtc = "invalid", passiveStaminaProgress = 55 };
            data.characters.Add(unknown);
            now = now.AddHours(1);

            Service().Tick();

            Assert.AreSame(unknown, data.characters[1]);
            Assert.AreEqual(4, unknown.currentStamina);
            Assert.AreEqual("invalid", unknown.passiveStaminaLastCalculatedUtc);
            Assert.AreEqual(55, unknown.passiveStaminaProgress);
        }

        [Test]
        public void MultipleChanges_SaveOnceAndSaveFailureRollsBackAllState()
        {
            CharacterDefinition party = Add("party", 0, now);
            CharacterDefinition nonParty = Add("nonParty", 0, now);
            data.partyCharacterIds.Add("party");
            now = now.AddSeconds(300);
            saveSucceeds = false;

            PassiveStaminaRecoveryService.TickResult result = Service().Tick();

            Assert.IsTrue(result.SaveAttempted);
            Assert.IsFalse(result.SaveSucceeded);
            Assert.AreEqual(0, roster.GetStamina(party));
            Assert.AreEqual(0, roster.GetStamina(nonParty));
            Assert.AreEqual(Format(new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc)), data.characters[0].passiveStaminaLastCalculatedUtc);
            Assert.AreEqual(Format(new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc)), data.characters[1].passiveStaminaLastCalculatedUtc);
            Assert.AreEqual(0, data.characters[0].passiveStaminaProgress);
            Assert.AreEqual(0, data.characters[1].passiveStaminaProgress);
            Assert.AreEqual(1, saves);
            Assert.AreEqual(0, events);
        }

        private PassiveStaminaRecoveryService Service()
        {
            var service = new PassiveStaminaRecoveryService(
                new RecoveryBalance("test", "Jewel", 0, 30, 3, 30, 10), roster,
                () => data, () => { saves++; return saveSucceeds; }, () => now);
            service.StaminaChanged += _ => events++;
            return service;
        }

        private CharacterDefinition Add(string id, int stamina, DateTime? baseline, long progress = 0)
        {
            CharacterDefinition definition = ScriptableObject.CreateInstance<CharacterDefinition>();
            definition.name = id;
            created.Add(definition);
            data.characters.Add(new CharacterSaveState
            {
                characterId = id,
                currentStamina = stamina,
                passiveStaminaLastCalculatedUtc = baseline.HasValue ? Format(baseline.Value) : null,
                passiveStaminaProgress = progress,
            });
            roster.Add(definition, stamina, 10);
            return definition;
        }

        private static string Format(DateTime value) => value.ToUniversalTime().ToString(RecoveryStation.UtcFormat, System.Globalization.CultureInfo.InvariantCulture);

        private sealed class FakeRoster : IRecoveryRoster
        {
            private readonly SaveData data;
            private readonly List<CharacterDefinition> characters = new List<CharacterDefinition>();
            private readonly Dictionary<string, int> stamina = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> maximum = new Dictionary<string, int>(StringComparer.Ordinal);

            public FakeRoster(SaveData data) { this.data = data; }
            public IReadOnlyList<CharacterDefinition> RecoverableCharacters => characters;
            public CharacterDefinition CurrentCharacter => null;
            public void Add(CharacterDefinition definition, int current, int max) { characters.Add(definition); stamina[definition.CharacterId] = current; maximum[definition.CharacterId] = max; }
            public bool Contains(CharacterDefinition definition) => definition != null && stamina.ContainsKey(definition.CharacterId);
            public CharacterDefinition FindById(string id) => characters.Find(value => value != null && value.CharacterId == id);
            public string GetCharacterId(CharacterDefinition definition) => definition != null ? definition.CharacterId : null;
            public int GetStamina(CharacterDefinition definition) => stamina[definition.CharacterId];
            public int GetMaxStamina(CharacterDefinition definition) => maximum[definition.CharacterId];
            public void SetStamina(CharacterDefinition definition, int value) { stamina[definition.CharacterId] = value; FindState(definition.CharacterId).currentStamina = value; }
            public bool ApplyRecoveryStamina(CharacterDefinition definition, int value)
            {
                int clamped = Math.Max(0, Math.Min(maximum[definition.CharacterId], value));
                if (stamina[definition.CharacterId] == clamped) return false;
                stamina[definition.CharacterId] = clamped;
                FindState(definition.CharacterId).currentStamina = clamped;
                return true;
            }
            public void RaiseCharacterStateChanged(CharacterDefinition definition) { }
            private CharacterSaveState FindState(string id) => data.characters.Find(value => value != null && value.characterId == id);
        }
    }
}
