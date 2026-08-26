using System;
using System.Collections.Generic;
using System.Reflection;
using Character;
using Common;
using Corruption;
using NUnit.Framework;
using Recovery;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace CharacterEditor.Tests
{
    /// <summary>
    /// 카탈로그 기반 <see cref="CharacterRoster"/> 시험.
    ///
    /// <b>실제 저장 파일을 읽지도 쓰지도 않는다.</b> <see cref="SaveSystem"/>의 문서를 리플렉션으로
    /// 직접 끼워 넣으므로 저장소가 아예 불리지 않는다(기존 SaveSystemIntegrationTests가 쓰는 것과 같은
    /// 이음매다). 씬도 만들지 않는다 - 로스터 컴포넌트를 임시 GameObject에 붙이고 <b>Awake를 부르지
    /// 않은 채</b> 필요한 비공개 메서드만 골라 부른다. 그래야 런타임 액터나 회복소 같은 씬 의존이
    /// 시험에 끌려 들어오지 않는다.
    ///
    /// 확인하는 것은 하나로 모인다 - <b>보유하지 않은 캐릭터는 목록에도, 값에도, 저장 문서에도
    /// 나타나지 않으며, 물어보거나 고치려 해도 아무 일이 일어나지 않는다.</b>
    /// </summary>
    public sealed class CharacterRosterCatalogTests
    {
        private static readonly string[] SixIds =
        {
            "CatKnight", "ElfArcher", "Barbarian", "ElfGuardian", "RabbitHealer", "CatMage",
        };

        private static readonly FieldInfo DataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo LoadResultField =
            typeof(SaveSystem).GetField("loadResult", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo ConfigureMethod =
            typeof(SaveSystem).GetMethod("ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(DataField, "SaveSystem.data를 찾지 못했습니다 - 그대로 두면 시험이 실제 저장 파일을 읽습니다.");
            Assert.IsNotNull(LoadResultField, "SaveSystem.loadResult를 찾지 못했습니다.");
            Assert.IsNotNull(ConfigureMethod, "SaveSystem.ConfigureForTests를 찾지 못했습니다.");
        }

        [TearDown]
        public void TearDown()
        {
            // 실제 저장소/문서로 되돌린다 - 이 시험의 주입이 다음 시험으로 새면 안 된다.
            ConfigureMethod.Invoke(null, new object[] { null, null, null });

            foreach (UnityEngine.Object asset in created)
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }

            created.Clear();
            SetInstance(null);
        }

        // ---- 목록은 유효한 출전 파티원만, 저장 파티 차례로 ----

        [Test]
        public void UsableEntries_AreOwnedOnlyInSavedPartyOrder()
        {
            // 보유/파티 목록의 차례는 뒤죽박죽이고 모르는 id도 섞여 있다.
            SaveData document = Inject(State("CatMage"), State("GhostHero"), State("ElfArcher"));
            document.partyCharacterIds = new List<string> { "ElfArcher", "GhostHero", "CatMage" };
            CharacterRoster roster = Roster(Catalog(SixIds));

            BuildUsableEntries(roster);

            CollectionAssert.AreEqual(new[] { "ElfArcher", "CatMage" }, EntryIds(roster),
                "저장 파티 차례로, 유효하고 보유한 것만 남아야 한다.");
            Assert.AreEqual(3, document.characters.Count, "목록을 만드는 것만으로 저장 문서가 달라지면 안 된다.");
        }

        [Test]
        public void UsableEntries_AreEmptyWhenNothingIsOwned()
        {
            Inject();
            CharacterRoster roster = Roster(Catalog(SixIds));

            BuildUsableEntries(roster);

            Assert.AreEqual(0, roster.Entries.Count, "카탈로그에 여섯이 있어도 보유가 없으면 쓸 수 없다.");
            Assert.AreEqual(0, SaveSystem.Data.characters.Count, "빈 목록을 몰래 채우면 안 된다.");
        }

        [Test]
        public void UsableEntries_SkipDefinitionsWithoutPlayableMotion()
        {
            Inject(State("CatKnight"), State("ElfArcher"));

            CharacterCatalog catalog = Catalog(
                Definition("CatKnight", playable: true),
                Definition("ElfArcher", playable: false));

            CharacterRoster roster = Roster(catalog);
            LogAssert.ignoreFailingMessages = true;
            BuildUsableEntries(roster);
            LogAssert.ignoreFailingMessages = false;

            CollectionAssert.AreEqual(new[] { "CatKnight" }, EntryIds(roster),
                "보유했어도 재생할 수 없는 프로필은 기존 규칙대로 걸러진다.");
        }

        [Test]
        public void UsableEntries_SkipInvalidPartyIdsWithoutMutatingSavedParty()
        {
            SaveData document = Inject(State("CatKnight"), State("CatMage"));
            document.partyCharacterIds = new List<string> { "GhostHero", "ElfArcher", "CatMage", "CatMage" };
            CharacterRoster roster = Roster(Catalog(SixIds));
            string before = string.Join("|", document.partyCharacterIds);

            BuildUsableEntries(roster);

            CollectionAssert.AreEqual(new[] { "CatMage" }, EntryIds(roster));
            Assert.AreEqual(before, string.Join("|", document.partyCharacterIds),
                "미보유/알 수 없음/중복 파티 ID는 저장을 고치지 않고 런타임 Entries에서만 제외한다.");
        }

        [Test]
        public void RefreshOwnedCharacters_DoesNotAutoJoinNewRecruit()
        {
            SaveData document = Inject(State("CatKnight"));
            CharacterRoster roster = Ready(Catalog(SixIds));

            document.characters.Add(State("CatMage", level: 30));
            roster.RefreshOwnedCharactersAfterExternalSave();

            CollectionAssert.AreEqual(new[] { "CatKnight" }, EntryIds(roster));
            CollectionAssert.AreEqual(new[] { "CatKnight" }, document.partyCharacterIds,
                "영입은 보유 목록만 바꾸며 파티에 자동 합류하지 않는다.");
        }

        [Test]
        public void RefreshPartyFallback_UsesFirstAvailablePartyMemberInSavedOrder()
        {
            SaveData document = Inject(State("CatKnight"), State("ElfArcher"), State("CatMage"));
            document.partyCharacterIds = new List<string> { "CatMage", "ElfArcher" };
            CharacterRoster roster = Ready(Catalog(SixIds));

            CharacterDefinition fallback = (CharacterDefinition)Invoke(roster, "ResolveFirstAvailablePartyCharacter");

            Assert.AreEqual("CatMage", fallback.CharacterId,
                "현재 캐릭터가 외부 파티 변경으로 빠지면 첫 번째 사용 가능한 파티원으로 재선택한다.");
        }

        [Test]
        public void RefreshParty_KeepsCurrentCharacterWhenItRemainsInParty()
        {
            SaveData document = Inject(State("CatKnight"), State("ElfArcher"));
            document.partyCharacterIds = new List<string> { "CatKnight", "ElfArcher" };
            CharacterRoster roster = Ready(Catalog(SixIds));
            CharacterDefinition current = roster.Entries[0].definition;
            SetPrivate(roster, "current", current);

            document.partyCharacterIds = new List<string> { "ElfArcher", "CatKnight" };
            roster.RefreshPartyAfterExternalSave();

            Assert.AreSame(current, roster.Current,
                "외부 파티 순서 변경 뒤에도 현재 캐릭터가 파티에 남아 있으면 유지한다.");
            CollectionAssert.AreEqual(new[] { "ElfArcher", "CatKnight" }, EntryIds(roster));
        }

        // ---- 처치 행동력 소진 자동 교체는 v4 고정 슬롯 순서를 쓴다 ----

        [Test]
        public void AutoSwitchSlotOrder_UsesFixedSlotsAndWrapsWithoutCompression()
        {
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, AutoSwitchSlots(3, 0));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, AutoSwitchSlots(3, 2));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, AutoSwitchSlots(3, -1),
                "현재 캐릭터가 저장 슬롯에 없으면 첫 슬롯부터 찾는다.");
        }

        [Test]
        public void DefeatAtZero_AutoSwitchesToNextUsableFixedSlotWithoutExtraSave()
        {
            SaveData document = Inject(State("CatKnight", stamina: 1), State("ElfArcher", stamina: 5), State("CatMage", stamina: 7));
            document.partyCharacterIds = new List<string> { "CatKnight", string.Empty, "ElfArcher", "CatMage" };
            document.recoverySlots = new List<RecoverySlotSaveState>
            {
                new RecoverySlotSaveState { characterId = "ElfArcher" },
            };

            MemoryStorage storage = UseMemoryStorage();
            CharacterRoster roster = Ready(Catalog("CatKnight", "ElfArcher", "CatMage"));
            SetPrivate(roster, "current", roster.Entries[0].definition);
            SetPrivate(roster, "runtimeActor", RuntimeActor());

            var changed = new List<CharacterDefinition>();
            Action<CharacterDefinition> handler = changed.Add;
            CharacterRoster.CurrentCharacterChanged += handler;
            try
            {
                // 최소 모션 프로필은 전환에 필요한 Idle만 가진다. 액터가 공격 풀 누락 진단을 내더라도
                // 이 시험의 대상(슬롯 순서/저장/현재 캐릭터)이 아니므로 로그 스코프에서만 분리한다.
                LogAssert.ignoreFailingMessages = true;
                InvokeWithString(roster, "HandleAnyTargetDefeated", "target-1");
                LogAssert.ignoreFailingMessages = false;

                Assert.AreEqual(0, roster.GetStamina(Definition("CatKnight")));
                Assert.AreEqual("CatMage", roster.Current.CharacterId,
                    "빈 슬롯과 회복소 후보를 건너뛰고 다음 고정 슬롯으로 전환해야 한다.");
                CollectionAssert.AreEqual(new[] { "CatKnight", string.Empty, "ElfArcher", "CatMage" }, document.partyCharacterIds,
                    "자동 교체는 파티 저장이나 슬롯 위치를 바꾸지 않는다.");
                Assert.AreEqual(1, storage.WriteCalls, "행동력 차감만 저장하고 교체로 추가 저장하지 않는다.");
                Assert.AreEqual(1, changed.Count);

                InvokeWithString(roster, "HandleAnyTargetDefeated", "target-1");
                Assert.AreEqual("CatMage", roster.Current.CharacterId);
                Assert.AreEqual(1, storage.WriteCalls, "중복 처치 이벤트는 다시 차감하거나 교체하지 않는다.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                CharacterRoster.CurrentCharacterChanged -= handler;
            }
        }

        [Test]
        public void DefeatAtZero_LeavesCurrentWhenNoOtherPartyMemberCanAct()
        {
            SaveData document = Inject(State("CatKnight", stamina: 1), State("ElfArcher", stamina: 0));
            document.partyCharacterIds = new List<string> { "CatKnight", string.Empty, "ElfArcher" };

            MemoryStorage storage = UseMemoryStorage();
            CharacterRoster roster = Ready(Catalog("CatKnight", "ElfArcher"));
            CharacterDefinition catKnight = roster.Entries[0].definition;
            SetPrivate(roster, "current", catKnight);
            SetPrivate(roster, "runtimeActor", RuntimeActor());

            InvokeWithString(roster, "HandleAnyTargetDefeated", "target-2");

            Assert.AreSame(catKnight, roster.Current, "1인 파티 또는 전원 행동력 0이면 현재 캐릭터를 유지한다.");
            Assert.AreEqual(0, roster.GetStamina(catKnight));
            Assert.AreEqual(1, storage.WriteCalls);
            CollectionAssert.AreEqual(new[] { "CatKnight", string.Empty, "ElfArcher" }, document.partyCharacterIds);
        }

        [Test]
        public void Defeat_CorruptionMultiplierSpendsOnceAndStillAutoSwitchesAtZero()
        {
            SaveData document = Inject(
                State("CatKnight", stamina: 3),
                State("ElfArcher", stamina: 5));
            document.characters[0].currentCorruption = 240d;
            document.partyCharacterIds = new List<string> { "CatKnight", "ElfArcher" };

            MemoryStorage storage = UseMemoryStorage();
            CharacterRoster roster = Ready(Catalog("CatKnight", "ElfArcher"));
            SetPrivate(roster, "current", roster.Entries[0].definition);
            SetPrivate(roster, "runtimeActor", RuntimeActor());
            SetPrivate(roster, "corruptionConfigCatalog", CorruptionCatalog());

            int stateChanges = 0;
            Action<CharacterDefinition> handler = _ => stateChanges++;
            CharacterRoster.CharacterStateChanged += handler;
            try
            {
                LogAssert.ignoreFailingMessages = true;
                InvokeWithString(roster, "HandleAnyTargetDefeated", "corruption-target");
                LogAssert.ignoreFailingMessages = false;

                Assert.AreEqual(0, roster.GetStamina(Definition("CatKnight")));
                Assert.AreEqual("ElfArcher", roster.Current.CharacterId,
                    "3배 비용으로 0이 된 뒤에도 기존 자동 교체 경로를 사용해야 한다.");
                Assert.AreEqual(1, storage.WriteCalls, "처치 차감은 배율과 관계없이 한 번만 저장한다.");
                Assert.AreEqual(1, stateChanges, "소진 캐릭터의 상태 변경은 한 번만 알려야 한다.");

                InvokeWithString(roster, "HandleAnyTargetDefeated", "corruption-target");
                Assert.AreEqual(1, storage.WriteCalls, "중복 처치 이벤트는 추가 차감을 만들지 않는다.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                CharacterRoster.CharacterStateChanged -= handler;
            }
        }

        [Test]
        public void DefeatStaminaMutation_WithoutSave_RollsBack_AndAutoSwitchesOnlyAfterSuccess()
        {
            SaveData document = Inject(State("CatKnight", stamina: 1), State("ElfArcher", stamina: 5));
            document.partyCharacterIds = new List<string> { "CatKnight", "ElfArcher" };
            MemoryStorage storage = UseMemoryStorage();
            CharacterRoster roster = Ready(Catalog("CatKnight", "ElfArcher"));
            CharacterDefinition catKnight = roster.Entries[0].definition;
            SetPrivate(roster, "current", catKnight);
            SetPrivate(roster, "runtimeActor", RuntimeActor());
            SetInstance(roster);

            int stateEvents = 0;
            int currentEvents = 0;
            Action<CharacterDefinition> stateHandler = _ => stateEvents++;
            Action<CharacterDefinition> currentHandler = _ => currentEvents++;
            CharacterRoster.CharacterStateChanged += stateHandler;
            CharacterRoster.CurrentCharacterChanged += currentHandler;
            try
            {
                CharacterRoster.DefeatStaminaMutationReceipt receipt = roster.SpendDefeatStaminaWithoutSave(catKnight);

                Assert.IsTrue(receipt.Changed);
                Assert.AreEqual(1, receipt.StaminaBefore);
                Assert.IsTrue(receipt.AutoSwitchNeeded);
                Assert.AreEqual(0, storage.WriteCalls, "메모리 전용 행동력 차감은 저장하지 않는다.");
                Assert.AreEqual(0, stateEvents);
                Assert.AreEqual(0, currentEvents);
                Assert.AreSame(catKnight, roster.Current, "성공 처리 전에는 자동 교체하지 않는다.");
                Assert.AreEqual(0, document.characters[0].currentStamina);

                roster.RollbackDefeatStamina(receipt);
                Assert.AreEqual(1, document.characters[0].currentStamina);
                Assert.AreSame(catKnight, roster.Current);

                receipt = roster.SpendDefeatStaminaWithoutSave(catKnight);
                LogAssert.ignoreFailingMessages = true;
                roster.NotifyDefeatStaminaAfterExternalSave(receipt);
                LogAssert.ignoreFailingMessages = false;

                Assert.AreEqual(0, storage.WriteCalls, "성공 후 알림도 외부 저장을 다시 부르지 않는다.");
                Assert.AreEqual(1, stateEvents);
                Assert.AreEqual(1, currentEvents);
                Assert.AreEqual("ElfArcher", roster.Current.CharacterId,
                    "자동 교체는 저장 성공 뒤 명시적 성공 처리에서만 일어난다.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                CharacterRoster.CharacterStateChanged -= stateHandler;
                CharacterRoster.CurrentCharacterChanged -= currentHandler;
            }
        }

        [Test]
        public void Scene_CharacterRosterHasGeneratedCorruptionConfig()
        {
            UnityEngine.SceneManagement.Scene scene =
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    "Assets/Scenes/desktopScene_ReSize.unity",
                    UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                CharacterRoster[] sceneRosters = Array.FindAll(
                    UnityEngine.Object.FindObjectsOfType<CharacterRoster>(),
                    roster => roster.gameObject.scene == scene);
                Assert.AreEqual(1, sceneRosters.Length,
                    "대상 씬에는 CharacterRoster가 정확히 하나여야 한다.");

                var config = (CorruptionConfigCatalog)GetPrivate(
                    sceneRosters[0], "corruptionConfigCatalog");
                Assert.IsNotNull(config, "CharacterRoster에 CorruptionConfigCatalog가 연결되어야 한다.");
                Assert.IsNotNull(config.Find("default"),
                    "연결된 Catalog는 유효한 default 설정을 제공해야 한다.");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ---- 읽기와 거부된 변경은 문서를 바꾸지 않는다 ----

        [Test]
        public void ReadsForUnownedCharactersReturnZeroAndCreateNothing()
        {
            SaveData document = Inject(State("CatKnight", level: 3, stamina: 7));
            CharacterRoster roster = Ready(Catalog(SixIds));

            CharacterDefinition elfArcher = Definition("ElfArcher");

            string before = Describe(document);

            Assert.AreEqual(0, roster.GetLevel(elfArcher), "보유하지 않은 캐릭터의 레벨은 0이다.");
            Assert.AreEqual(0, roster.GetStamina(elfArcher));
            Assert.AreEqual(0, roster.GetLevel(null));
            Assert.AreEqual(0, roster.GetStamina(null));

            Assert.AreEqual(before, Describe(document), "물어보는 것만으로 캐릭터가 지급되면 안 된다.");
        }

        [Test]
        public void ReadsForOwnedCharactersReturnTheSavedValues()
        {
            Inject(State("CatKnight", level: 4, stamina: 9));
            CharacterRoster roster = Ready(Catalog(SixIds));

            CharacterDefinition catKnight = roster.Entries[0].definition;

            Assert.AreEqual(4, roster.GetLevel(catKnight));
            Assert.AreEqual(9, roster.GetStamina(catKnight));
            Assert.AreEqual(30, roster.GetMaxStamina(catKnight));
        }

        [Test]
        public void MutationsForUnownedCharactersAreNoOps()
        {
            SaveData document = Inject(State("CatKnight", level: 1, stamina: 5));
            CharacterRoster roster = Ready(Catalog(SixIds));

            CharacterDefinition elfArcher = Definition("ElfArcher");
            string before = Describe(document);

            roster.SetStamina(elfArcher, 12);
            roster.SpendStamina(elfArcher, 1);
            Assert.IsFalse(roster.ApplyRecoveryStamina(elfArcher, 20),
                "보유하지 않은 캐릭터는 회복 대상이 될 수 없다.");

            Assert.AreEqual(before, Describe(document),
                "거부된 변경은 저장 목록의 개수도 내용도 바꾸지 않는다.");
        }

        [Test]
        public void MutationsForOwnedCharactersStillWork()
        {
            Inject(State("CatKnight", level: 1, stamina: 5));
            CharacterRoster roster = Ready(Catalog(SixIds));
            CharacterDefinition catKnight = roster.Entries[0].definition;

            Assert.IsTrue(roster.ApplyRecoveryStamina(catKnight, 12));
            Assert.AreEqual(12, roster.GetStamina(catKnight));
            Assert.AreEqual(1, SaveSystem.Data.characters.Count, "기존 항목을 고칠 뿐 새로 만들지 않는다.");
        }

        // ---- 저장된 행동력의 정규화 ----

        [Test]
        public void NormalizeStamina_TouchesOnlyOwnedStatesAndResolvesMinusOne()
        {
            SaveData document = Inject(
                State("CatKnight", stamina: -1),
                State("ElfArcher", stamina: 99),
                State("Barbarian", stamina: 4),
                State("GhostHero", stamina: -1));

            CharacterRoster roster = Ready(Catalog(SixIds));

            Assert.AreEqual(30, document.characters[0].currentStamina, "-1은 정의의 Max Stamina로 확정된다.");
            Assert.AreEqual(30, document.characters[1].currentStamina, "Max를 넘는 값은 잘린다.");
            Assert.AreEqual(4, document.characters[2].currentStamina, "쓸 수 있는 값은 그대로 둔다.");
            Assert.AreEqual(-1, document.characters[3].currentStamina,
                "카탈로그에 없는 id는 정규화 대상이 아니다(값을 건드리지 않는다).");
            Assert.AreEqual(4, document.characters.Count);
        }

        // ---- 참조가 아니라 id로 잇는다 ----

        [Test]
        public void ManualDefinitionResolvesToTheGeneratedDefinitionById()
        {
            Inject(State("CatKnight"));
            CharacterRoster roster = Ready(Catalog(SixIds));

            CharacterDefinition generated = roster.Entries[0].definition;
            CharacterDefinition manual = Definition("CatKnight");

            Assert.AreNotSame(generated, manual, "전제 확인 - 서로 다른 에셋이어야 한다.");
            Assert.AreSame(generated, ResolveUsable(roster, manual),
                "같은 id의 수동 에셋은 카탈로그의 생성 정의로 이어져야 한다.");
            Assert.AreEqual(1, roster.GetLevel(manual), "수동 에셋으로 물어도 같은 저장 상태를 본다.");
        }

        [Test]
        public void DefaultCharacterResolvesByIdEvenWhenItIsAManualAsset()
        {
            Inject(State("Barbarian"));
            CharacterCatalog catalog = Catalog(SixIds);
            CharacterRoster roster = Roster(catalog);

            // 씬이 들고 있는 것은 수동 에셋이다.
            SetPrivate(roster, "defaultCharacter", Definition("Barbarian"));
            Ready(roster);

            CharacterDefinition start = ResolveStartCharacter(roster);

            Assert.IsNotNull(start);
            Assert.AreEqual("Barbarian", start.CharacterId);
            Assert.AreSame(roster.Entries[0].definition, start, "실제로 투입되는 것은 생성 정의다.");
        }

        [Test]
        public void UnownedDefaultCharacterFallsBackToTheFirstOwnedInCatalogOrder()
        {
            Inject(State("ElfGuardian"), State("CatMage"));
            CharacterRoster roster = Roster(Catalog(SixIds));
            SetPrivate(roster, "defaultCharacter", Definition("CatKnight"));

            LogAssert.ignoreFailingMessages = true;
            Ready(roster);
            CharacterDefinition start = ResolveStartCharacter(roster);
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual("ElfGuardian", start.CharacterId,
                "보유하지 않은 기본 캐릭터 대신 카탈로그 차례로 처음 만나는 보유 캐릭터를 쓴다.");
        }

        [Test]
        public void AllOwnedCharactersRecovering_StartsWithNobody()
        {
            // 회복소 인스턴스 없이도 저장된 슬롯이 같은 판정을 내린다(RecoveryService의 폴백 경로).
            SaveData document = Inject(State("CatKnight"), State("ElfArcher"));
            document.recoverySlots = new List<RecoverySlotSaveState>
            {
                new RecoverySlotSaveState { characterId = "CatKnight" },
                new RecoverySlotSaveState { characterId = "ElfArcher" },
            };

            CharacterRoster roster = Ready(Catalog(SixIds));

            LogAssert.ignoreFailingMessages = true;
            CharacterDefinition start = ResolveStartCharacter(roster);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsNull(start, "전원이 회복 중이면 아무도 투입하지 않는다.");
        }

        // ---- 교체 판정 ----

        [Test]
        public void TrySwitchTo_UnownedIsRejectedAsNotAvailable()
        {
            SaveData document = Inject(State("CatKnight"));
            CharacterRoster roster = Ready(Catalog(SixIds));

            CharacterDefinition elfArcher = Definition("ElfArcher");
            string before = Describe(document);

            Assert.IsFalse(roster.TrySwitchTo(elfArcher, out CharacterRoster.SwapBlockReason reason));
            Assert.AreEqual(CharacterRoster.SwapBlockReason.NotAvailable, reason);
            Assert.AreEqual(before, Describe(document), "거부된 교체가 캐릭터를 지급하면 안 된다.");
        }

        [Test]
        public void GetSwapBlockReason_UnownedAndNullAreNotAvailable()
        {
            Inject(State("CatKnight"));
            CharacterRoster roster = Ready(Catalog(SixIds));

            Assert.AreEqual(CharacterRoster.SwapBlockReason.NotAvailable,
                roster.GetSwapBlockReason(Definition("ElfArcher")));
            Assert.AreEqual(CharacterRoster.SwapBlockReason.NotAvailable,
                roster.GetSwapBlockReason(null));
        }

        // ---- 공격 허용 판정 ----

        [Test]
        public void CurrentCharacterCanAct_IsTrueWhenThereIsNoRosterAtAll()
        {
            SetInstance(null);
            Assert.IsTrue(CharacterRoster.CurrentCharacterCanAct,
                "로스터를 쓰지 않는 씬의 기존 전투를 막지 않는다.");
        }

        [Test]
        public void CurrentCharacterCanAct_IsFalseWhenACatalogRosterOwnsNobody()
        {
            Inject();
            CharacterRoster roster = Ready(Catalog(SixIds));
            SetInstance(roster);

            Assert.AreEqual(0, roster.Entries.Count);
            Assert.IsFalse(CharacterRoster.CurrentCharacterCanAct,
                "보유 0은 '로스터가 없다'가 아니다 - 싸울 주체가 없으므로 막아야 한다.");
        }

        [Test]
        public void CurrentCharacterCanAct_IsFalseWhenNobodyIsDeployed()
        {
            Inject(State("CatKnight", stamina: 5));
            CharacterRoster roster = Ready(Catalog(SixIds));
            SetInstance(roster);

            Assert.AreEqual(1, roster.Entries.Count);
            Assert.IsNull(roster.Current, "전제 확인 - 아직 아무도 투입하지 않았다.");
            Assert.IsFalse(CharacterRoster.CurrentCharacterCanAct);
        }

        [Test]
        public void CurrentCharacterCanAct_KeepsLegacyCompatibilityWithoutACatalog()
        {
            Inject();
            CharacterRoster roster = Roster(null);
            SetInstance(roster);

            Assert.AreEqual(0, roster.Entries.Count);
            Assert.IsTrue(CharacterRoster.CurrentCharacterCanAct,
                "카탈로그가 없는 과도기 구성에서는 예전 판정을 유지한다.");
        }

        // ---- 새 게임 초기 지급의 조건 ----

        [Test]
        public void NewGameGrantHappensOnlyForTheNewGameLoadStatus()
        {
            SaveData document = Inject();
            Assert.AreEqual(SaveLoadStatus.NewGame, SaveSystem.LoadStatus, "전제 확인");

            CharacterRoster roster = Roster(Catalog(SixIds));
            GrantInitialCharacters(roster);

            Assert.AreEqual(6, document.characters.Count, "새 게임에서는 표의 시작 캐릭터를 지급한다.");
        }

        [Test]
        public void LoadedEmptyOwnershipStaysEmpty()
        {
            SaveData document = Inject();
            SetLoadStatus(SaveLoadResult.Loaded(document, SaveData.CurrentSaveVersion));

            CharacterRoster roster = Roster(Catalog(SixIds));
            GrantInitialCharacters(roster);

            Assert.AreEqual(0, document.characters.Count,
                "v2에서 빈 보유 목록은 정상이다 - 불러온 문서를 다시 채우면 안 된다.");
        }

        [Test]
        public void NoBlockedOrRecoveredLoadStatusEverGrants()
        {
            foreach (SaveLoadResult result in new[]
                     {
                         SaveLoadResult.Migrated(new SaveData(), 1, 2),
                         SaveLoadResult.CorruptFallback(new SaveData(), "손상"),
                         SaveLoadResult.FutureVersionBlocked(99, 2),
                         SaveLoadResult.MigrationFailed(1, 1, "실패"),
                     })
            {
                SaveData document = Inject();
                SetLoadStatus(result);

                CharacterRoster roster = Roster(Catalog(SixIds));
                GrantInitialCharacters(roster);

                Assert.AreEqual(0, document.characters.Count,
                    $"{result.Status}에서는 한 항목도 만들지 않아야 한다.");
            }
        }

        // ---- 저장 전용(카탈로그에 없는) id는 보존하되 감춘다 ----

        [Test]
        public void SaveOnlyUnknownId_IsInvisibleToEveryPublicStatePath()
        {
            // 예전 빌드에서 남은 id가 저장 문서에 있다. 그 id를 가리키는 정의를 만들어 넘기는 것만으로
            // 값을 읽거나 고칠 수 있으면 "감춘다"가 말뿐이 된다.
            SaveData document = Inject(State("CatKnight", stamina: 5), State("GhostHero", level: 9, stamina: 4));
            CharacterRoster roster = Ready(Catalog(SixIds));

            CharacterDefinition ghost = Definition("GhostHero");
            string before = Describe(document);

            Assert.AreEqual(0, roster.GetLevel(ghost), "저장 전용 id의 값이 드러나면 안 된다.");
            Assert.AreEqual(0, roster.GetStamina(ghost));
            Assert.AreEqual(0, roster.GetMaxStamina(ghost), "쓸 수 없는 캐릭터의 상한은 0이다.");
            Assert.AreEqual(CharacterRoster.SwapBlockReason.NotAvailable, roster.GetSwapBlockReason(ghost));

            roster.SetStamina(ghost, 1);
            roster.SpendStamina(ghost, 2);
            Assert.IsFalse(roster.ApplyRecoveryStamina(ghost, 0));

            Assert.AreEqual(before, Describe(document),
                "저장 전용 id의 값은 한 글자도 바뀌지 않아야 한다(보존하되 감춘다).");
            Assert.AreEqual(9, document.characters[1].level);
            Assert.AreEqual(4, document.characters[1].currentStamina);
        }

        [Test]
        public void SaveOnlyUnknownId_NeverRaisesStateChanged()
        {
            Inject(State("CatKnight"), State("GhostHero"));
            CharacterRoster roster = Ready(Catalog(SixIds));

            var seen = new List<CharacterDefinition>();
            Action<CharacterDefinition> handler = seen.Add;
            CharacterRoster.CharacterStateChanged += handler;

            try
            {
                roster.RaiseCharacterStateChanged(Definition("GhostHero"));
                roster.RaiseCharacterStateChanged(Definition("ElfArcher"));
                roster.RaiseCharacterStateChanged(null);
            }
            finally
            {
                CharacterRoster.CharacterStateChanged -= handler;
            }

            CollectionAssert.IsEmpty(seen,
                "목록에 없는 캐릭터의 상태 변경은 알릴 것이 없다 - 구독자가 없는 값을 그리려 든다.");
        }

        [Test]
        public void RaiseCharacterStateChanged_EmitsTheCanonicalDefinition()
        {
            Inject(State("CatKnight"));
            CharacterRoster roster = Ready(Catalog(SixIds));

            CharacterDefinition generated = roster.Entries[0].definition;
            CharacterDefinition manual = Definition("CatKnight");

            var seen = new List<CharacterDefinition>();
            Action<CharacterDefinition> handler = seen.Add;
            CharacterRoster.CharacterStateChanged += handler;

            try
            {
                roster.RaiseCharacterStateChanged(manual);
            }
            finally
            {
                CharacterRoster.CharacterStateChanged -= handler;
            }

            Assert.AreEqual(1, seen.Count);
            Assert.AreSame(generated, seen[0], "구독자는 언제나 목록에 있는 그 객체를 받아야 한다.");
        }

        // ---- 수동 에셋은 정식 정의로 정규화된다 ----

        [Test]
        public void ManualDefinitionWithADifferentMaxStamina_ClampsToTheGeneratedMax()
        {
            Inject(State("CatKnight", stamina: 5));
            CharacterRoster roster = Ready(Catalog(Definition("CatKnight", maxStamina: 30)));

            // 씬에 남아 있는 수동 에셋이 더 큰 상한을 들고 있다.
            CharacterDefinition manual = Definition("CatKnight", maxStamina: 999);

            Assert.AreEqual(30, roster.GetMaxStamina(manual), "상한은 표(카탈로그)가 정한 값 하나여야 한다.");

            roster.SetStamina(manual, 999);
            Assert.AreEqual(30, roster.GetStamina(manual), "수동 에셋의 큰 상한으로 잘리면 안 된다.");
            Assert.AreEqual(30, SaveSystem.Data.characters[0].currentStamina);
        }

        [Test]
        public void SetStaminaThroughAManualDefinition_EmitsTheGeneratedObject()
        {
            Inject(State("CatKnight", stamina: 5));
            UseMemoryStorage();
            CharacterRoster roster = Ready(Catalog(SixIds));

            CharacterDefinition generated = roster.Entries[0].definition;
            CharacterDefinition manual = Definition("CatKnight");

            var seen = new List<CharacterDefinition>();
            Action<CharacterDefinition> handler = seen.Add;
            CharacterRoster.CharacterStateChanged += handler;

            try
            {
                roster.SetStamina(manual, 7);
            }
            finally
            {
                CharacterRoster.CharacterStateChanged -= handler;
            }

            Assert.AreEqual(1, seen.Count);
            Assert.AreSame(generated, seen[0], "이벤트 인자도 정식 정의여야 한다.");
        }

        [Test]
        public void ApplyRecoveryStaminaThroughAManualDefinition_UsesTheGeneratedMax()
        {
            Inject(State("CatKnight", stamina: 1));
            CharacterRoster roster = Ready(Catalog(Definition("CatKnight", maxStamina: 30)));

            Assert.IsTrue(roster.ApplyRecoveryStamina(Definition("CatKnight", maxStamina: 999), 999));
            Assert.AreEqual(30, SaveSystem.Data.characters[0].currentStamina);
        }

        // ---- 보유 캐릭터의 SetStamina / SpendStamina ----

        [Test]
        public void SetStamina_ForAnOwnedCharacterWritesOnceAndRaisesTheEvent()
        {
            Inject(State("CatKnight", stamina: 5));
            MemoryStorage storage = UseMemoryStorage();
            CharacterRoster roster = Ready(Catalog(SixIds));
            CharacterDefinition catKnight = roster.Entries[0].definition;

            var seen = new List<CharacterDefinition>();
            Action<CharacterDefinition> handler = seen.Add;
            CharacterRoster.CharacterStateChanged += handler;

            try
            {
                roster.SetStamina(catKnight, 3);
                Assert.AreEqual(3, roster.GetStamina(catKnight));
                Assert.AreEqual(1, storage.WriteCalls, "값이 바뀌었으므로 정확히 한 번 저장한다.");
                Assert.AreEqual(1, seen.Count);
                Assert.AreSame(catKnight, seen[0]);

                // 같은 값을 다시 넣으면 파일도 이벤트도 건드리지 않는다.
                roster.SetStamina(catKnight, 3);
                Assert.AreEqual(1, storage.WriteCalls, "값이 그대로면 저장하지 않는다.");
                Assert.AreEqual(1, seen.Count);
            }
            finally
            {
                CharacterRoster.CharacterStateChanged -= handler;
            }
        }

        [Test]
        public void SpendStamina_ForAnOwnedCharacterSubtractsAndSaves()
        {
            Inject(State("CatKnight", stamina: 5));
            MemoryStorage storage = UseMemoryStorage();
            CharacterRoster roster = Ready(Catalog(SixIds));
            CharacterDefinition catKnight = roster.Entries[0].definition;

            roster.SpendStamina(catKnight, 2);

            Assert.AreEqual(3, roster.GetStamina(catKnight));
            Assert.AreEqual(1, storage.WriteCalls);

            roster.SpendStamina(catKnight, 99);
            Assert.AreEqual(0, roster.GetStamina(catKnight), "0 아래로 내려가지 않는다.");
            Assert.AreEqual(2, storage.WriteCalls);
        }

        [Test]
        public void SetStaminaAndSpendStamina_ForUnownedOrUnknownNeverWrite()
        {
            SaveData document = Inject(State("CatKnight", stamina: 5), State("GhostHero", stamina: 4));
            MemoryStorage storage = UseMemoryStorage();
            CharacterRoster roster = Ready(Catalog(SixIds));

            var seen = new List<CharacterDefinition>();
            Action<CharacterDefinition> handler = seen.Add;
            CharacterRoster.CharacterStateChanged += handler;

            string before = Describe(document);

            try
            {
                roster.SetStamina(Definition("ElfArcher"), 1);   // 보유하지 않음
                roster.SpendStamina(Definition("ElfArcher"), 1);
                roster.SetStamina(Definition("GhostHero"), 1);   // 카탈로그에 없음
                roster.SpendStamina(Definition("GhostHero"), 1);
                roster.SetStamina(null, 1);
                roster.SpendStamina(null, 1);
            }
            finally
            {
                CharacterRoster.CharacterStateChanged -= handler;
            }

            Assert.AreEqual(0, storage.WriteCalls, "거부된 변경은 파일을 쓰지 않는다.");
            CollectionAssert.IsEmpty(seen, "거부된 변경은 상태 변경을 알리지 않는다.");
            Assert.AreEqual(before, Describe(document));
        }

        // ---- Awake 뒤에 보유가 사라진 경우 ----
        //
        // usableEntries는 Awake 때 한 번 만든 목록이다. 그 뒤에 저장 문서에서 항목이 사라져도 목록에는
        // 그대로 남아 있으므로, 목록만 믿는 판정은 <b>이미 잃은 캐릭터를 아직 가진 것처럼</b> 다룬다.
        // 아래 시험들은 세 경로가 지금 저장 문서까지 확인하는지를 못 박는다.

        [Test]
        public void GetMaxStamina_ReturnsZeroAfterTheStateIsRemoved()
        {
            SaveData document = Inject(State("CatKnight", stamina: 9));
            CharacterRoster roster = Ready(Catalog(SixIds));
            CharacterDefinition catKnight = roster.Entries[0].definition;

            Assert.AreEqual(30, roster.GetMaxStamina(catKnight), "전제 확인 - 아직 보유 중이다.");

            document.characters.Clear();

            Assert.AreEqual(0, roster.GetMaxStamina(catKnight),
                "보유가 사라졌으면 목록에 남아 있어도 상한이 나오면 안 된다.");
            Assert.AreEqual(0, document.characters.Count, "조회가 상태를 되살리면 안 된다.");
        }

        [Test]
        public void GetSwapBlockReason_IsNotAvailableAfterTheStateIsRemovedEvenIfItWasCurrent()
        {
            SaveData document = Inject(State("CatKnight", stamina: 9));
            CharacterRoster roster = Ready(Catalog(SixIds));
            CharacterDefinition catKnight = roster.Entries[0].definition;

            // 이 캐릭터가 지금 투입 중이고 행동력도 넉넉한 상태로 만든다 - 그 두 가지가 판정을
            // 통과시키던 근거였으므로, 보유가 사라진 뒤에도 통과하면 잃은 캐릭터로 갈아탈 수 있다.
            SetPrivate(roster, "current", catKnight);
            SetPrivate(roster, "runtimeActor", roster.gameObject.AddComponent<CharacterRuntimeActor>());

            document.characters.Clear();

            Assert.AreEqual(CharacterRoster.SwapBlockReason.NotAvailable,
                roster.GetSwapBlockReason(catKnight),
                "보유가 사라졌으면 투입 중이었어도, 행동력이 남아 있었어도 교체 대상이 아니다.");

            Assert.IsFalse(roster.TrySwitchTo(catKnight, out CharacterRoster.SwapBlockReason reason));
            Assert.AreEqual(CharacterRoster.SwapBlockReason.NotAvailable, reason);
            Assert.AreEqual(0, document.characters.Count, "거부된 교체가 상태를 되살리면 안 된다.");
        }

        [Test]
        public void RaiseCharacterStateChanged_EmitsNothingAfterTheStateIsRemoved()
        {
            SaveData document = Inject(State("CatKnight", stamina: 9));
            CharacterRoster roster = Ready(Catalog(SixIds));
            CharacterDefinition catKnight = roster.Entries[0].definition;

            document.characters.Clear();

            var seen = new List<CharacterDefinition>();
            Action<CharacterDefinition> handler = seen.Add;
            CharacterRoster.CharacterStateChanged += handler;

            try
            {
                roster.RaiseCharacterStateChanged(catKnight);
            }
            finally
            {
                CharacterRoster.CharacterStateChanged -= handler;
            }

            CollectionAssert.IsEmpty(seen,
                "구독자가 받아서 그리려는 순간 그 값들은 이미 없다 - 알릴 것이 없다.");
            Assert.AreEqual(0, document.characters.Count);
        }

        [Test]
        public void EveryPathIsQuietAfterTheStateIsRemoved()
        {
            SaveData document = Inject(State("CatKnight", stamina: 9));
            MemoryStorage storage = UseMemoryStorage();
            CharacterRoster roster = Ready(Catalog(SixIds));
            CharacterDefinition catKnight = roster.Entries[0].definition;

            document.characters.Clear();

            var seen = new List<CharacterDefinition>();
            Action<CharacterDefinition> handler = seen.Add;
            CharacterRoster.CharacterStateChanged += handler;

            try
            {
                Assert.AreEqual(0, roster.GetLevel(catKnight));
                Assert.AreEqual(0, roster.GetStamina(catKnight));
                Assert.AreEqual(0, roster.GetMaxStamina(catKnight));

                roster.SetStamina(catKnight, 3);
                roster.SpendStamina(catKnight, 1);
                Assert.IsFalse(roster.ApplyRecoveryStamina(catKnight, 3));
                roster.RaiseCharacterStateChanged(catKnight);
            }
            finally
            {
                CharacterRoster.CharacterStateChanged -= handler;
            }

            Assert.AreEqual(0, storage.WriteCalls, "저장이 일어나면 안 된다.");
            CollectionAssert.IsEmpty(seen, "이벤트가 나가면 안 된다.");
            Assert.AreEqual(0, document.characters.Count, "어떤 경로도 상태를 되살리면 안 된다.");
        }

        [Test]
        public void RemovingOneCharacterLeavesTheOthersWorking()
        {
            // 한 캐릭터의 보유만 사라진 경우, 나머지는 그대로 동작해야 한다.
            SaveData document = Inject(State("CatKnight", stamina: 9), State("ElfArcher", stamina: 4));
            CharacterRoster roster = Ready(Catalog(SixIds));

            CharacterDefinition catKnight = roster.Entries[0].definition;
            CharacterDefinition elfArcher = roster.Entries[1].definition;

            document.characters.RemoveAt(0);

            Assert.AreEqual(0, roster.GetMaxStamina(catKnight));
            Assert.AreEqual(CharacterRoster.SwapBlockReason.NotAvailable, roster.GetSwapBlockReason(catKnight));

            Assert.AreEqual(30, roster.GetMaxStamina(elfArcher), "남은 캐릭터는 그대로여야 한다.");
            Assert.AreEqual(4, roster.GetStamina(elfArcher));
        }

        // ---- 카탈로그가 없는 과도기 폴백 ----
        //
        // 카탈로그가 연결되지 않은 구성에는 <b>보유라는 개념 자체가 없다</b>. 저장 문서를 다시 확인할
        // 대상이 없으므로 예전처럼 직렬화된 목록만 보고 답해야 하며, 여기서 "쓸 수 없음"으로 무너지면
        // 그 씬의 교체 판정과 상태 변경 알림이 통째로 죽는다.

        [Test]
        public void LegacyFallback_GetMaxStaminaKeepsTheDefinitionMax()
        {
            Inject();
            CharacterDefinition catKnight = Definition("CatKnight", maxStamina: 42);
            CharacterRoster roster = LegacyRoster(catKnight);

            Assert.AreEqual(42, roster.GetMaxStamina(catKnight),
                "폴백 구성에서는 넘어온 정의의 상한을 그대로 본다.");
            Assert.AreEqual(0, SaveSystem.Data.characters.Count, "폴백 경로가 상태를 만들면 안 된다.");
        }

        [Test]
        public void LegacyFallback_GetSwapBlockReasonStillUsesTheSerializedEntries()
        {
            Inject();
            CharacterDefinition catKnight = Definition("CatKnight");
            CharacterRoster roster = LegacyRoster(catKnight);

            // 액터를 붙이고 이 캐릭터를 투입 중으로 만들면, 살아 있는 판정은 AlreadyCurrent여야 한다.
            // ResolveOwnedUsable이 폴백에서 null을 돌려주면 여기서 NotAvailable이 나온다.
            SetPrivate(roster, "runtimeActor", roster.gameObject.AddComponent<CharacterRuntimeActor>());
            SetPrivate(roster, "current", catKnight);

            Assert.AreEqual(CharacterRoster.SwapBlockReason.AlreadyCurrent,
                roster.GetSwapBlockReason(catKnight),
                "폴백 구성에서도 직렬화된 목록 기준의 이유가 그대로 나와야 한다.");

            Assert.AreEqual(CharacterRoster.SwapBlockReason.NotAvailable,
                roster.GetSwapBlockReason(Definition("ElfArcher")),
                "목록에 없는 캐릭터는 폴백에서도 쓸 수 없다.");

            Assert.AreEqual(0, SaveSystem.Data.characters.Count);
        }

        [Test]
        public void LegacyFallback_RaiseCharacterStateChangedEmitsTheSerializedEntryDefinition()
        {
            Inject();
            CharacterDefinition serialized = Definition("CatKnight");
            CharacterRoster roster = LegacyRoster(serialized);

            // 같은 id의 <b>다른</b> 에셋으로 알려도, 구독자는 목록에 있는 그 객체를 받아야 한다.
            CharacterDefinition manual = Definition("CatKnight");
            Assert.AreNotSame(serialized, manual, "전제 확인 - 서로 다른 에셋이어야 한다.");

            var seen = new List<CharacterDefinition>();
            Action<CharacterDefinition> handler = seen.Add;
            CharacterRoster.CharacterStateChanged += handler;

            try
            {
                roster.RaiseCharacterStateChanged(manual);
                roster.RaiseCharacterStateChanged(Definition("ElfArcher"));
            }
            finally
            {
                CharacterRoster.CharacterStateChanged -= handler;
            }

            Assert.AreEqual(1, seen.Count, "목록에 없는 캐릭터는 폴백에서도 알리지 않는다.");
            Assert.AreSame(serialized, seen[0], "폴백에서도 목록에 있는 정의를 넘겨야 한다.");
            Assert.AreEqual(0, SaveSystem.Data.characters.Count);
        }

        // ---- 회복 어댑터 ----

        [Test]
        public void RecoveryAdapterMatchesByIdNotReference()
        {
            Inject(State("CatKnight"));
            CharacterRoster roster = Ready(Catalog(SixIds));

            var adapter = new CharacterRosterRecoveryAdapter(roster);

            Assert.IsTrue(adapter.Contains(Definition("CatKnight")),
                "같은 id의 다른 에셋도 같은 캐릭터로 봐야 한다.");
            Assert.AreSame(roster.Entries[0].definition, adapter.FindById("CatKnight"));
        }

        [Test]
        public void RecoveryAdapterReturnsNullForUnknownAndUnownedSlotIds()
        {
            SaveData document = Inject(State("CatKnight"));
            CharacterRoster roster = Ready(Catalog(SixIds));

            var adapter = new CharacterRosterRecoveryAdapter(roster);
            string before = Describe(document);

            Assert.IsNull(adapter.FindById("GhostHero"), "모르는 슬롯 id는 null이다(호출부가 그대로 다룬다).");
            Assert.IsNull(adapter.FindById("ElfArcher"), "보유하지 않은 캐릭터도 회복소에 나오지 않는다.");
            Assert.IsNull(adapter.FindById(null));
            Assert.IsFalse(adapter.Contains(Definition("ElfArcher")));
            Assert.IsFalse(adapter.Contains(null));

            Assert.AreEqual(before, Describe(document), "찾지 못한 조회가 상태를 만들면 안 된다.");
        }

        [Test]
        public void RecoveryAdapterExposesOnlyOwnedCharacters()
        {
            SaveData document = Inject(State("ElfArcher"), State("CatMage"));
            document.partyCharacterIds = new List<string> { "ElfArcher" };
            CharacterRoster roster = Ready(Catalog(SixIds));

            var adapter = new CharacterRosterRecoveryAdapter(roster);

            var ids = new List<string>();
            foreach (CharacterDefinition definition in adapter.RecoverableCharacters) ids.Add(definition.CharacterId);

            CollectionAssert.AreEqual(new[] { "ElfArcher", "CatMage" }, ids);
            CollectionAssert.AreEqual(new[] { "ElfArcher" }, EntryIds(roster),
                "출전 파티 경계가 회복소의 전체 보유 목록을 축소하면 안 된다.");
        }

        // ---- 도우미 ----

        /// <summary>저장 문서를 직접 끼워 넣는다 - 저장소가 아예 불리지 않으므로 실제 파일을 읽지 않는다.</summary>
        private static SaveData Inject(params CharacterSaveState[] states)
        {
            var document = new SaveData
            {
                characters = new List<CharacterSaveState>(states),
                partyCharacterIds = new List<string>(Array.ConvertAll(states, state => state != null ? state.characterId : null)),
            };
            DataField.SetValue(null, document);
            LoadResultField.SetValue(null, SaveLoadResult.NewGame(document));
            return document;
        }

        /// <summary>
        /// 저장을 <b>메모리에만</b> 받아 두는 저장소를 끼운다. 파일도 폴더도 만들지 않으므로
        /// persistentDataPath는 어디에서도 쓰이지 않는다 - 그러면서 "정말로 저장했는가"를 횟수로 셀 수
        /// 있다. 문서는 이 호출 뒤에도 그대로 유지된다(끼운 뒤 다시 넣어 준다).
        /// </summary>
        private static MemoryStorage UseMemoryStorage()
        {
            SaveData document = SaveSystem.Data;
            SaveLoadResult status = (SaveLoadResult)LoadResultField.GetValue(null);

            var storage = new MemoryStorage();
            ConfigureMethod.Invoke(null, new object[] { storage, null, null });

            DataField.SetValue(null, document);
            LoadResultField.SetValue(null, status);
            return storage;
        }

        /// <summary>메모리 위의 저장소. 쓰기 횟수만 세고 아무 데도 기록하지 않는다.</summary>
        private sealed class MemoryStorage : ISaveStorage
        {
            public int WriteCalls;

            public bool WritesBlocked => false;

            public string BlockedReason => null;

            public SaveReadResult ReadPrimary() => SaveReadResult.Missing("memory://primary");

            public SaveReadResult ReadBackup() => SaveReadResult.Missing("memory://backup");

            public SaveWriteResult Write(string text)
            {
                WriteCalls++;
                return SaveWriteResult.Written(backupKept: true);
            }

            public SaveQuarantineResult QuarantinePrimary(string reason) =>
                SaveQuarantineResult.Moved("memory://quarantine");
        }

        private static void SetLoadStatus(SaveLoadResult result)
        {
            LoadResultField.SetValue(null, result);
        }

        private static void SetInstance(CharacterRoster roster)
        {
            MethodInfo setter = typeof(CharacterRoster)
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetSetMethod(nonPublic: true);

            Assert.IsNotNull(setter, "CharacterRoster.Instance의 setter를 찾지 못했습니다.");
            setter.Invoke(null, new object[] { roster });
        }

        /// <summary>
        /// 로스터 컴포넌트. <b>호스트를 비활성으로 먼저 만든 뒤</b> 컴포넌트를 붙인다 - 활성
        /// GameObject에 붙이면 Unity가 Awake/OnEnable을 그 자리에서 부르고, 그러면 이 시험이
        /// 의도하지 않은 로그와 <see cref="CharacterRoster.Instance"/> 설정, 저장소 접근까지 함께
        /// 일어난다. 비활성으로 두는 동안에는 어떤 런타임 수명 주기도 돌지 않으므로, 시험이 부르고
        /// 싶은 비공개 단계만 골라 부를 수 있다.
        /// </summary>
        private CharacterRoster Roster(CharacterCatalog catalog)
        {
            var host = new GameObject("RosterTestHost");
            created.Add(host);
            host.SetActive(false);

            CharacterRoster roster = host.AddComponent<CharacterRoster>();

            Assert.IsNull(CharacterRoster.Instance,
                "비활성 호스트에 붙였으므로 Awake가 돌지 않아야 합니다 - Instance가 세워졌다면 " +
                "이 시험이 런타임 수명 주기를 건드린 것입니다.");

            SetPrivate(roster, "catalog", catalog);
            return roster;
        }

        /// <summary>
        /// 카탈로그가 없는 과도기 구성의 로스터. 직렬화된 <c>entries</c>만 채우고 목록을 만든다 -
        /// 호스트는 계속 비활성이라 Awake/OnEnable은 돌지 않는다.
        /// </summary>
        private CharacterRoster LegacyRoster(params CharacterDefinition[] definitions)
        {
            CharacterRoster roster = Roster(null);

            var entries = new List<CharacterRoster.Entry>();
            foreach (CharacterDefinition definition in definitions)
            {
                entries.Add(new CharacterRoster.Entry { definition = definition });
            }

            SetPrivate(roster, "entries", entries);
            Invoke(roster, "BuildUsableEntries");

            Assert.AreEqual(definitions.Length, roster.Entries.Count, "전제 확인 - 폴백 목록이 만들어져야 한다.");
            return roster;
        }

        /// <summary>Awake가 하는 일 중 <b>목록 구성과 행동력 정규화</b>만 재현한다.</summary>
        private CharacterRoster Ready(CharacterCatalog catalog)
        {
            return Ready(Roster(catalog));
        }

        private CharacterRoster Ready(CharacterRoster roster)
        {
            SetPrivate(roster, "owned", NewOwnedCollection(roster));
            BuildUsableEntries(roster);
            Invoke(roster, "NormalizeOwnedStamina");
            return roster;
        }

        private static object NewOwnedCollection(CharacterRoster roster)
        {
            var catalog = (CharacterCatalog)GetPrivate(roster, "catalog");
            return catalog == null ? null : new OwnedCharacterCollection(catalog, SaveSystem.Data);
        }

        private static void BuildUsableEntries(CharacterRoster roster)
        {
            if (GetPrivate(roster, "owned") == null) SetPrivate(roster, "owned", NewOwnedCollection(roster));
            Invoke(roster, "BuildUsableEntries");
        }

        private static void GrantInitialCharacters(CharacterRoster roster)
        {
            SetPrivate(roster, "owned", NewOwnedCollection(roster));
            Invoke(roster, "GrantInitialCharactersOnNewGameOnly");
        }

        private static CharacterDefinition ResolveStartCharacter(CharacterRoster roster)
        {
            return (CharacterDefinition)Invoke(roster, "ResolveStartCharacter");
        }

        private static CharacterDefinition ResolveUsable(CharacterRoster roster, CharacterDefinition definition)
        {
            MethodInfo method = typeof(CharacterRoster).GetMethod(
                "ResolveUsable", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "CharacterRoster.ResolveUsable을 찾지 못했습니다.");
            return (CharacterDefinition)method.Invoke(roster, new object[] { definition });
        }

        private static object Invoke(CharacterRoster roster, string name)
        {
            MethodInfo method = typeof(CharacterRoster).GetMethod(
                name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"CharacterRoster.{name}을 찾지 못했습니다 - 이름이 바뀌었다면 시험도 함께 고치세요.");
            return method.Invoke(roster, null);
        }

        private static void InvokeWithString(CharacterRoster roster, string name, string value)
        {
            MethodInfo method = typeof(CharacterRoster).GetMethod(
                name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"CharacterRoster.{name}을 찾지 못했습니다.");
            method.Invoke(roster, new object[] { value });
        }

        private static List<int> AutoSwitchSlots(int count, int exhaustedSlot)
        {
            MethodInfo method = typeof(CharacterRoster).GetMethod(
                "EnumerateAutoSwitchSlots", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "CharacterRoster.EnumerateAutoSwitchSlots을 찾지 못했습니다.");

            var slots = new List<int>();
            foreach (int slot in (System.Collections.IEnumerable)method.Invoke(null, new object[] { count, exhaustedSlot })) slots.Add(slot);
            return slots;
        }

        private static void SetPrivate(CharacterRoster roster, string field, object value)
        {
            FieldInfo info = typeof(CharacterRoster).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"CharacterRoster.{field}를 찾지 못했습니다.");
            info.SetValue(roster, value);
        }

        private static object GetPrivate(CharacterRoster roster, string field)
        {
            FieldInfo info = typeof(CharacterRoster).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"CharacterRoster.{field}를 찾지 못했습니다.");
            return info.GetValue(roster);
        }

        private static List<string> EntryIds(CharacterRoster roster)
        {
            var ids = new List<string>();
            foreach (CharacterRoster.Entry entry in roster.Entries) ids.Add(entry.definition.CharacterId);
            return ids;
        }

        private CharacterCatalog Catalog(params string[] ids)
        {
            var definitions = new CharacterDefinition[ids.Length];
            for (int i = 0; i < ids.Length; i++) definitions[i] = Definition(ids[i]);
            return Catalog(definitions);
        }

        private CorruptionConfigCatalog CorruptionCatalog()
        {
            var definition = ScriptableObject.CreateInstance<CorruptionConfigDefinition>();
            created.Add(definition);
            var serializedDefinition = new SerializedObject(definition);
            serializedDefinition.FindProperty("configId").stringValue = "default";
            serializedDefinition.FindProperty("maxCorruption").intValue = 300;
            serializedDefinition.FindProperty("warningThresholdPercent").intValue = 50;
            serializedDefinition.FindProperty("dangerThresholdPercent").intValue = 80;
            serializedDefinition.FindProperty("warningStaminaCostMultiplier").intValue = 2;
            serializedDefinition.FindProperty("dangerStaminaCostMultiplier").intValue = 3;
            serializedDefinition.FindProperty("enabled").boolValue = true;
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

            var catalog = ScriptableObject.CreateInstance<CorruptionConfigCatalog>();
            created.Add(catalog);
            var serializedCatalog = new SerializedObject(catalog);
            SerializedProperty configs = serializedCatalog.FindProperty("configs");
            configs.arraySize = 1;
            configs.GetArrayElementAtIndex(0).objectReferenceValue = definition;
            serializedCatalog.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }

        private CharacterRuntimeActor RuntimeActor()
        {
            var host = new GameObject("AutoSwitchRuntimeActor");
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
            SerializedProperty list = serialized.FindProperty("characters");
            list.arraySize = definitions.Length;
            for (int i = 0; i < definitions.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }

        /// <summary>메모리에만 있는 정의. 재생 가능한 프로필을 붙여 두어야 로스터의 기존 검사를 통과한다.</summary>
        private CharacterDefinition Definition(string id, bool playable = true, int maxStamina = 30)
        {
            var definition = ScriptableObject.CreateInstance<CharacterDefinition>();
            created.Add(definition);

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("characterId").stringValue = id;
            serialized.FindProperty("initiallyOwned").boolValue = true;
            serialized.FindProperty("maxStamina").intValue = maxStamina;
            serialized.FindProperty("motionProfile").objectReferenceValue = Profile(playable);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private CharacterMotionProfile Profile(bool playable)
        {
            var profile = ScriptableObject.CreateInstance<CharacterMotionProfile>();
            created.Add(profile);

            if (!playable) return profile;

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

        private static CharacterSaveState State(string id, int level = 1, int stamina = 10)
        {
            return new CharacterSaveState { characterId = id, level = level, currentStamina = stamina };
        }

        private static string Describe(SaveData document)
        {
            if (document.characters == null) return "(null)";

            var parts = new List<string>();
            foreach (CharacterSaveState state in document.characters)
            {
                parts.Add(state == null ? "(null)" : $"{state.characterId}:{state.level}:{state.currentStamina}");
            }

            return string.Join("|", parts);
        }
    }
}
