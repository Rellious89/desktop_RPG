using System;
using System.Collections.Generic;
using Common;
using CommonEditor.Save;
using NUnit.Framework;

namespace CommonEditor.SaveTests
{
    /// <summary>
    /// <see cref="SaveResetService"/> 집중 시험. <b>실제 저장 파일을 절대 건드리지 않는다</b> - 격리된
    /// 메모리 <see cref="SaveData"/>와 호출 횟수를 세는 저장 대리자만 쓴다(SaveSystem도, persistentDataPath도
    /// 건드리지 않는다).
    ///
    /// 확인하는 것은 하나다 - <b>고른 항목만 정확히 초기화하고, 저장은 한 번만 하고, 실패하면 전부 되돌리며,
    /// 나머지 필드는 그대로 두는가.</b>
    /// </summary>
    public sealed class SaveResetServiceTests
    {
        private const int PartySlotCount = 3;

        private static List<InitialCharacterResetSeed> DefaultInitialSeeds()
        {
            return new List<InitialCharacterResetSeed>
            {
                new InitialCharacterResetSeed("CatKnight", 0d),
            };
        }

        private static SaveResetResult ApplyCharacterReset(
            SaveData data,
            SaveResetTargets targets,
            IReadOnlyList<string> idsToRemove,
            Func<bool> save,
            IReadOnlyList<InitialCharacterResetSeed> seeds = null,
            int partySlotCount = PartySlotCount,
            IReadOnlyList<StoryQuestResetDefinition> questDefinitions = null)
        {
            return SaveResetService.Apply(
                data, targets, idsToRemove, seeds ?? DefaultInitialSeeds(), partySlotCount, questDefinitions, save);
        }

        /// <summary>세 초기화 대상과 몇 개의 비대상 필드를 모두 0이 아닌 값으로 채운 문서를 만든다 -
        /// 비대상이 기본값으로 되돌아오는 사고가 있으면 보이도록 전부 눈에 띄는 값으로 둔다.</summary>
        private static SaveData MakePopulated()
        {
            return new SaveData
            {
                currency = 1250,
                currentLevel = 7,
                currentExp = 240,
                totalKillCount = 133,
                saveRevision = 5,
                items = new List<InventoryItemState>
                {
                    new InventoryItemState { itemId = "red_potion", count = 3 },
                    new InventoryItemState { itemId = "blue_potion", count = 1 },
                },
                buildingConstructions = new List<BuildingConstructionSaveState>
                {
                    new BuildingConstructionSaveState { buildingId = "1", startedAtUtc = "s", completeAtUtc = "c" },
                },
                recruitmentCycles = new List<RecruitmentCycleSaveState>
                {
                    new RecruitmentCycleSaveState
                    {
                        recruitmentAccessId = "Inn_Normal_Access",
                        startedAtUtc = "rs",
                        readyAtUtc = "rr",
                    },
                },
                characters = new List<CharacterSaveState>
                {
                    new CharacterSaveState { characterId = "CatKnight", level = 5, currentExp = 7, currentStamina = 21 },
                },
                recoverySlots = new List<RecoverySlotSaveState>
                {
                    new RecoverySlotSaveState { characterId = "Barbarian", startStamina = 2 },
                },
                purificationSlots = new List<PurificationSlotSaveState>
                {
                    new PurificationSlotSaveState
                    {
                        purificationTypeId = "church_prayer", characterId = "CatKnight",
                        lastCalculatedAtUtc = "ps", progressTicks = 123,
                    },
                },
            };
        }

        private static Func<bool> Counting(out Box<int> calls, bool succeeds = true)
        {
            var box = new Box<int>();
            calls = box;
            return () => { box.Value++; return succeeds; };
        }

        private sealed class Box<T> { public T Value; }

        private static void AssertNonTargetsPreserved(SaveData data)
        {
            Assert.AreEqual(7, data.currentLevel, "계정 레벨은 초기화 대상이 아닙니다.");
            Assert.AreEqual(240, data.currentExp);
            Assert.AreEqual(133, data.totalKillCount);
            Assert.AreEqual(1, data.characters.Count, "캐릭터 보유는 절대 건드리지 않습니다.");
            Assert.AreEqual("CatKnight", data.characters[0].characterId);
            Assert.AreEqual(5, data.characters[0].level);
            Assert.AreEqual(1, data.recoverySlots.Count, "회복소는 초기화 대상이 아닙니다.");
            Assert.AreEqual("Barbarian", data.recoverySlots[0].characterId);
        }

        // ---- 1. Item만 ----

        [Test]
        public void Item만_초기화하고_나머지는_보존한다()
        {
            SaveData data = MakePopulated();
            SaveResetResult result = SaveResetService.Apply(data, SaveResetTargets.Item, Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(0, data.items.Count, "아이템은 빈 목록이 됩니다.");
            Assert.AreEqual(1250, data.currency, "재화는 그대로입니다.");
            Assert.AreEqual(1, data.buildingConstructions.Count, "건축 기록은 그대로입니다.");
            Assert.AreEqual(1, data.recruitmentCycles.Count, "모집 주기도 그대로입니다.");
            Assert.AreEqual("CatKnight", data.purificationSlots[0].characterId, "기도 슬롯도 그대로입니다.");
            AssertNonTargetsPreserved(data);
            Assert.AreEqual(1, calls.Value);
        }

        // ---- 2. Currency만 ----

        [Test]
        public void Currency만_0으로_만들고_나머지는_보존한다()
        {
            SaveData data = MakePopulated();
            SaveResetResult result = SaveResetService.Apply(data, SaveResetTargets.Currency, Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(0, data.currency);
            Assert.AreEqual(2, data.items.Count, "아이템은 그대로입니다.");
            Assert.AreEqual(1, data.buildingConstructions.Count, "건축 기록은 그대로입니다.");
            Assert.AreEqual(1, data.recruitmentCycles.Count, "모집 주기도 그대로입니다.");
            Assert.AreEqual("CatKnight", data.purificationSlots[0].characterId, "기도 슬롯도 그대로입니다.");
            AssertNonTargetsPreserved(data);
            Assert.AreEqual(1, calls.Value);
        }

        // ---- 3. Construction만 ----

        [Test]
        public void Construction만_초기화하고_나머지는_보존한다()
        {
            SaveData data = MakePopulated();
            SaveResetResult result = SaveResetService.Apply(data, SaveResetTargets.Construction, Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(0, data.buildingConstructions.Count);
            Assert.AreEqual(0, data.recruitmentCycles.Count, "Construction은 모집 주기도 함께 지웁니다.");
            Assert.AreEqual(1, data.purificationSlots.Count);
            Assert.IsFalse(data.purificationSlots[0].HasCharacter,
                "Construction은 존재하지 않는 교회에 기도 기록이 남지 않도록 정화 슬롯도 비웁니다.");
            Assert.AreEqual(2, data.items.Count, "아이템은 그대로입니다.");
            Assert.AreEqual(1250, data.currency, "재화는 그대로입니다.");
            AssertNonTargetsPreserved(data);
            Assert.AreEqual(1, calls.Value);
        }

        // ---- 4. All ----

        [Test]
        public void Character를_제외한_전체는_비캐릭터_항목을_모두_초기화한다()
        {
            SaveData data = MakePopulated();
            SaveResetTargets targets = SaveResetTargets.All & ~SaveResetTargets.Character;
            SaveResetResult result = SaveResetService.Apply(data, targets, Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(0, data.items.Count);
            Assert.AreEqual(0, data.currency);
            Assert.AreEqual(0, data.buildingConstructions.Count);
            Assert.AreEqual(0, data.recruitmentCycles.Count);
            Assert.AreEqual(1, data.purificationSlots.Count);
            Assert.IsFalse(data.purificationSlots[0].HasCharacter);
            AssertNonTargetsPreserved(data);
            Assert.AreEqual(1, calls.Value);
        }

        // ---- 5. All에서 하나 해제한 조합 ----

        [Test]
        public void All에서_Currency를_해제하면_Item과_Construction만_초기화한다()
        {
            SaveData data = MakePopulated();
            SaveResetTargets targets = SaveResetTargets.All &
                                       ~(SaveResetTargets.Currency | SaveResetTargets.Character);

            SaveResetResult result = SaveResetService.Apply(data, targets, Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(SaveResetTargets.Item | SaveResetTargets.Construction | SaveResetTargets.Quest,
                result.AppliedTargets);
            Assert.AreEqual(0, data.items.Count, "Item은 초기화됩니다.");
            Assert.AreEqual(0, data.buildingConstructions.Count, "Construction은 초기화됩니다.");
            Assert.AreEqual(0, data.recruitmentCycles.Count, "모집 주기도 Construction에 종속됩니다.");
            Assert.AreEqual(1, data.purificationSlots.Count);
            Assert.IsFalse(data.purificationSlots[0].HasCharacter);
            Assert.AreEqual(1250, data.currency, "해제한 Currency는 그대로여야 합니다.");
            AssertNonTargetsPreserved(data);
            Assert.AreEqual(1, calls.Value);
        }

        // ---- 6. 미선택 ----

        [Test]
        public void 아무것도_고르지_않으면_저장하지_않고_아무것도_바꾸지_않는다()
        {
            SaveData data = MakePopulated();
            SaveResetResult result = SaveResetService.Apply(data, SaveResetTargets.None, Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.NothingSelected, result.Outcome);
            Assert.AreEqual(0, calls.Value, "고른 항목이 없으면 저장 대리자를 부르지 않습니다.");
            Assert.AreEqual(2, data.items.Count);
            Assert.AreEqual(1250, data.currency);
            Assert.AreEqual(1, data.buildingConstructions.Count);
            Assert.AreEqual(1, data.recruitmentCycles.Count);
            Assert.AreEqual("CatKnight", data.purificationSlots[0].characterId);
            AssertNonTargetsPreserved(data);
        }

        // ---- 7. 저장 호출 정확히 1회 ----

        [Test]
        public void 성공하면_저장_대리자를_정확히_한_번만_부른다()
        {
            SaveData data = MakePopulated();
            SaveResetService.Apply(
                data, SaveResetTargets.All & ~SaveResetTargets.Character, Counting(out Box<int> calls));

            Assert.AreEqual(1, calls.Value, "선택 항목이 몇 개든 저장은 한 번에 모아 한 번만 합니다.");
        }

        // ---- 8. 저장 실패 시 전체 롤백 ----

        [Test]
        public void 저장에_실패하면_선택_항목을_전부_되돌린다()
        {
            SaveData data = MakePopulated();
            List<InventoryItemState> originalItems = data.items;
            List<BuildingConstructionSaveState> originalConstructions = data.buildingConstructions;
            List<RecruitmentCycleSaveState> originalRecruitmentCycles = data.recruitmentCycles;
            List<PurificationSlotSaveState> originalPurificationSlots = data.purificationSlots;

            SaveResetResult result =
                SaveResetService.Apply(
                    data, SaveResetTargets.All & ~SaveResetTargets.Character,
                    Counting(out Box<int> calls, succeeds: false));

            Assert.AreEqual(SaveResetOutcome.SaveFailed, result.Outcome);
            Assert.AreEqual(1, calls.Value, "실패해도 저장 시도는 한 번뿐입니다.");

            // 원래 값으로 전부 복구 - 성공한 일부만 남는 부분 초기화가 없어야 한다.
            Assert.AreSame(originalItems, data.items, "실패하면 원래 아이템 목록 참조로 되돌립니다.");
            Assert.AreEqual(2, data.items.Count);
            Assert.AreEqual(1250, data.currency, "재화도 되돌립니다.");
            Assert.AreSame(originalConstructions, data.buildingConstructions, "건축 기록도 되돌립니다.");
            Assert.AreEqual(1, data.buildingConstructions.Count);
            Assert.AreSame(originalRecruitmentCycles, data.recruitmentCycles, "모집 주기도 되돌립니다.");
            Assert.AreEqual(1, data.recruitmentCycles.Count);
            Assert.AreSame(originalPurificationSlots, data.purificationSlots, "기도 슬롯도 원래 목록으로 되돌립니다.");
            Assert.AreEqual("CatKnight", data.purificationSlots[0].characterId);
            AssertNonTargetsPreserved(data);
        }

        // ---- 9. 비대상 필드 보존(성공 경로) ----

        [Test]
        public void 초기화해도_캐릭터_회복소_계정진행은_그대로다()
        {
            SaveData data = MakePopulated();
            SaveResetService.Apply(
                data, SaveResetTargets.All & ~SaveResetTargets.Character, Counting(out Box<int> _));

            AssertNonTargetsPreserved(data);
            Assert.AreEqual(5, data.saveRevision, "저장 메타데이터는 이 로직이 건드리지 않습니다(저장 대리자의 몫).");
        }

        // ---- 방어: 정의되지 않은 비트는 무시 ----

        [Test]
        public void 정의되지_않은_비트만_들어오면_미선택으로_취급한다()
        {
            SaveData data = MakePopulated();
            SaveResetResult result =
                SaveResetService.Apply(data, (SaveResetTargets)(1 << 5), Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.NothingSelected, result.Outcome);
            Assert.AreEqual(0, calls.Value);
        }

        // ---- 방어: null 인자 ----

        [Test]
        public void data가_null이면_예외를_던진다()
        {
            Assert.Throws<ArgumentNullException>(
                () => SaveResetService.Apply(null, SaveResetTargets.Item, () => true));
        }

        [Test]
        public void save_대리자가_null이면_예외를_던진다()
        {
            Assert.Throws<ArgumentNullException>(
                () => SaveResetService.Apply(new SaveData(), SaveResetTargets.Item, null));
        }

        // ==== 캐릭터 선택 삭제 ====

        /// <summary>
        /// 여러 캐릭터와, 서로 다른 인덱스의 회복 슬롯을 갖춘 문서. 슬롯 인덱스가 곧 슬롯 번호이므로
        /// index0(ElfArcher 회복 중) / index1(빈 슬롯) / index2(CatKnight 회복 중)로 두어, 앞 슬롯을
        /// 비웠을 때 뒤 슬롯 번호가 밀리지 않는지를 볼 수 있게 한다.
        /// </summary>
        private static SaveData MakeCharacterFixture()
        {
            return new SaveData
            {
                currency = 500,
                items = new List<InventoryItemState> { new InventoryItemState { itemId = "sword", count = 1 } },
                buildingConstructions = new List<BuildingConstructionSaveState>
                {
                    new BuildingConstructionSaveState { buildingId = "2" },
                },
                recruitmentCycles = new List<RecruitmentCycleSaveState>
                {
                    new RecruitmentCycleSaveState { recruitmentAccessId = "Inn_Normal_Access" },
                },
                characters = new List<CharacterSaveState>
                {
                    new CharacterSaveState { characterId = "CatKnight", level = 5, currentExp = 12, currentStamina = 8 },
                    new CharacterSaveState { characterId = "Barbarian", level = 9, currentExp = 3, currentStamina = 20 },
                    new CharacterSaveState { characterId = "ElfArcher", level = 3, currentExp = 1, currentStamina = 15 },
                },
                partyCharacterIds = new List<string> { "CatKnight", "ElfArcher", "Barbarian" },
                recoverySlots = new List<RecoverySlotSaveState>
                {
                    new RecoverySlotSaveState
                    {
                        characterId = "ElfArcher", startStamina = 4, startedAtUtc = "es", completeAtUtc = "ec",
                    },
                    new RecoverySlotSaveState(), // index1: 빈 슬롯
                    new RecoverySlotSaveState
                    {
                        characterId = "CatKnight", startStamina = 6, startedAtUtc = "cs", completeAtUtc = "cc",
                    },
                },
                purificationSlots = new List<PurificationSlotSaveState>
                {
                    new PurificationSlotSaveState
                    {
                        purificationTypeId = "church_prayer", characterId = "ElfArcher",
                        lastCalculatedAtUtc = "eps", progressTicks = 11,
                    },
                    new PurificationSlotSaveState(),
                    new PurificationSlotSaveState
                    {
                        purificationTypeId = "church_prayer", characterId = "CatKnight",
                        lastCalculatedAtUtc = "cps", progressTicks = 22,
                    },
                },
            };
        }

        private static List<string> IdsOf(List<CharacterSaveState> characters)
        {
            var ids = new List<string>();
            foreach (CharacterSaveState c in characters) ids.Add(c?.characterId);
            return ids;
        }

        [Test]
        public void 선택한_캐릭터만_제거하고_나머지는_보존한다()
        {
            SaveData data = MakeCharacterFixture();

            SaveResetResult result = ApplyCharacterReset(
                data, SaveResetTargets.Character, new List<string> { "ElfArcher" },
                Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(SaveResetTargets.Character, result.AppliedTargets);
            Assert.AreEqual(1, result.RemovedCharacterCount);
            Assert.AreEqual(1, calls.Value);

            CollectionAssert.AreEqual(new[] { "CatKnight", "Barbarian" }, IdsOf(data.characters),
                "고른 ElfArcher만 빠지고 나머지는 순서 그대로 남아야 합니다.");
            CollectionAssert.AreEqual(new[] { "CatKnight", string.Empty, string.Empty }, data.partyCharacterIds,
                "Character reset은 고정 길이를 지키며 catalog 순서의 기본 편성으로 돌아가야 합니다.");

            // 비대상은 그대로.
            Assert.AreEqual(500, data.currency);
            Assert.AreEqual(1, data.items.Count);
            Assert.AreEqual(1, data.buildingConstructions.Count);
            Assert.AreEqual(1, data.recruitmentCycles.Count);
        }

        [Test]
        public void 기본_보유_캐릭터는_요청해도_삭제하지_않는다()
        {
            SaveData data = MakeCharacterFixture();

            SaveResetResult result = ApplyCharacterReset(
                data, SaveResetTargets.Character,
                new List<string> { "CatKnight", "ElfArcher" },
                Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(1, result.RemovedCharacterCount, "기본 캐릭터 CatKnight는 빠지지 않습니다.");
            CollectionAssert.AreEqual(new[] { "CatKnight", "Barbarian" }, IdsOf(data.characters));
            Assert.AreEqual(1, calls.Value);
        }

        [Test]
        public void 삭제한_캐릭터의_회복_슬롯만_비우고_다른_슬롯의_인덱스와_값은_보존한다()
        {
            SaveData data = MakeCharacterFixture();

            ApplyCharacterReset(
                data, SaveResetTargets.Character, new List<string> { "ElfArcher" }, Counting(out Box<int> _));

            Assert.AreEqual(3, data.recoverySlots.Count, "슬롯을 목록에서 빼면 안 됩니다(인덱스=슬롯 번호).");

            // index0: ElfArcher가 회복 중이던 슬롯 -> 빈 상태.
            Assert.IsFalse(data.recoverySlots[0].HasCharacter, "삭제한 캐릭터의 슬롯은 비워져야 합니다.");
            Assert.IsTrue(string.IsNullOrEmpty(data.recoverySlots[0].characterId));
            Assert.AreEqual(0, data.recoverySlots[0].startStamina);
            Assert.IsTrue(string.IsNullOrEmpty(data.recoverySlots[0].startedAtUtc));

            // index1: 원래 빈 슬롯 그대로.
            Assert.IsFalse(data.recoverySlots[1].HasCharacter);

            // index2: 초기화되는 기본 캐릭터 CatKnight 슬롯도 같은 자리에서 비운다.
            Assert.IsFalse(data.recoverySlots[2].HasCharacter);
            Assert.AreEqual(0, data.recoverySlots[2].startStamina);
            Assert.IsTrue(string.IsNullOrEmpty(data.recoverySlots[2].startedAtUtc));
            Assert.IsTrue(string.IsNullOrEmpty(data.recoverySlots[2].completeAtUtc));
        }

        [Test]
        public void 삭제한_캐릭터의_기도_슬롯만_비우고_다른_슬롯의_인덱스와_값은_보존한다()
        {
            SaveData data = MakeCharacterFixture();

            ApplyCharacterReset(
                data, SaveResetTargets.Character, new List<string> { "ElfArcher" }, Counting(out Box<int> _));

            Assert.AreEqual(3, data.purificationSlots.Count);
            Assert.IsFalse(data.purificationSlots[0].HasCharacter);
            Assert.IsTrue(string.IsNullOrEmpty(data.purificationSlots[0].purificationTypeId));
            Assert.IsTrue(string.IsNullOrEmpty(data.purificationSlots[0].lastCalculatedAtUtc));
            Assert.AreEqual(0, data.purificationSlots[0].progressTicks);
            Assert.IsFalse(data.purificationSlots[1].HasCharacter);
            Assert.IsFalse(data.purificationSlots[2].HasCharacter,
                "초기화되는 기본 캐릭터의 정화 슬롯도 비워야 합니다.");
            Assert.IsTrue(string.IsNullOrEmpty(data.purificationSlots[2].purificationTypeId));
            Assert.AreEqual(0, data.purificationSlots[2].progressTicks);
        }

        [Test]
        public void Character만_초기화하면_아이템_재화_건축_모집은_그대로다()
        {
            SaveData data = MakeCharacterFixture();

            ApplyCharacterReset(
                data, SaveResetTargets.Character, new List<string> { "ElfArcher" }, Counting(out Box<int> _));

            Assert.AreEqual(1, data.items.Count, "아이템은 그대로입니다.");
            Assert.AreEqual(500, data.currency, "재화는 그대로입니다.");
            Assert.AreEqual(1, data.buildingConstructions.Count, "건축 기록은 그대로입니다.");
            Assert.AreEqual(1, data.recruitmentCycles.Count, "모집 주기는 그대로입니다.");
            Assert.AreEqual(3, data.purificationSlots.Count, "기도 슬롯 목록은 그대로입니다.");
            Assert.IsFalse(data.purificationSlots[0].HasCharacter, "삭제한 캐릭터의 기도 슬롯만 비웁니다.");
            Assert.IsFalse(data.purificationSlots[2].HasCharacter,
                "초기화되는 기본 캐릭터의 기도 슬롯도 비웁니다.");
        }

        [Test]
        public void 복합_초기화에서도_저장은_정확히_한_번이고_캐릭터도_함께_지운다()
        {
            SaveData data = MakeCharacterFixture();

            SaveResetResult result = ApplyCharacterReset(
                data, SaveResetTargets.All, new List<string> { "ElfArcher" },
                Counting(out Box<int> calls), questDefinitions: MakeStoryDefinitions());

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(1, calls.Value, "여러 대상을 골라도 저장은 한 번에 모아 한 번만 합니다.");
            Assert.AreEqual(
                SaveResetTargets.Item | SaveResetTargets.Currency | SaveResetTargets.Construction |
                SaveResetTargets.Character | SaveResetTargets.Quest,
                result.AppliedTargets);

            Assert.AreEqual(0, data.items.Count);
            Assert.AreEqual(0, data.currency);
            Assert.AreEqual(0, data.buildingConstructions.Count);
            Assert.AreEqual(0, data.recruitmentCycles.Count);
            Assert.AreEqual(1, data.purificationSlots.Count);
            Assert.IsFalse(data.purificationSlots[0].HasCharacter);
            CollectionAssert.AreEqual(new[] { "CatKnight", "Barbarian" }, IdsOf(data.characters));
        }

        [Test]
        public void 저장에_실패하면_캐릭터와_회복_슬롯과_다른_대상을_전부_되돌린다()
        {
            SaveData data = MakeCharacterFixture();
            data.unlockedRecruitmentCharacterIds = new List<string> { "ElfArcher", "Barbarian" };
            data.characterStoryQuests = MakeStoryFixture().characterStoryQuests;
            List<CharacterSaveState> originalCharacters = data.characters;
            List<string> originalParty = data.partyCharacterIds;
            List<InventoryItemState> originalItems = data.items;
            List<RecoverySlotSaveState> originalRecoverySlots = data.recoverySlots;
            List<PurificationSlotSaveState> originalPurificationSlots = data.purificationSlots;
            List<string> originalUnlocks = data.unlockedRecruitmentCharacterIds;
            List<CharacterStoryQuestSaveState> originalStories = data.characterStoryQuests;

            SaveResetResult result = ApplyCharacterReset(
                data, SaveResetTargets.All, new List<string> { "ElfArcher" },
                Counting(out Box<int> calls, succeeds: false), questDefinitions: MakeStoryDefinitions());

            Assert.AreEqual(SaveResetOutcome.SaveFailed, result.Outcome);
            Assert.AreEqual(1, calls.Value);

            // 캐릭터 목록과 다른 대상 모두 원래대로.
            Assert.AreSame(originalCharacters, data.characters, "실패하면 원래 캐릭터 목록 참조로 되돌립니다.");
            CollectionAssert.AreEqual(new[] { "CatKnight", "Barbarian", "ElfArcher" }, IdsOf(data.characters));
            Assert.AreSame(originalParty, data.partyCharacterIds,
                "실패하면 원래 파티 목록 참조로 되돌립니다.");
            CollectionAssert.AreEqual(new[] { "CatKnight", "ElfArcher", "Barbarian" }, data.partyCharacterIds);
            Assert.AreSame(originalItems, data.items);
            Assert.AreEqual(500, data.currency);
            Assert.AreEqual(1, data.buildingConstructions.Count);
            Assert.AreEqual(1, data.recruitmentCycles.Count);
            Assert.AreSame(originalRecoverySlots, data.recoverySlots);
            Assert.AreSame(originalPurificationSlots, data.purificationSlots);
            Assert.AreSame(originalUnlocks, data.unlockedRecruitmentCharacterIds);
            Assert.AreSame(originalStories, data.characterStoryQuests);
            Assert.AreEqual("ElfArcher", data.purificationSlots[0].characterId);
            Assert.AreEqual("CatKnight", data.purificationSlots[2].characterId);

            // 비웠던 회복 슬롯도 원래 값으로 복구(같은 인덱스).
            Assert.AreEqual("ElfArcher", data.recoverySlots[0].characterId, "롤백하면 슬롯 값이 되살아나야 합니다.");
            Assert.AreEqual(4, data.recoverySlots[0].startStamina);
            Assert.AreEqual("es", data.recoverySlots[0].startedAtUtc);
            Assert.AreEqual("CatKnight", data.recoverySlots[2].characterId);
        }

        [Test]
        public void 캐릭터_삭제_저장_예외도_파티를_포함해_전부_되돌린다()
        {
            SaveData data = MakeCharacterFixture();
            data.unlockedRecruitmentCharacterIds = new List<string> { "ElfArcher", "Barbarian" };
            data.characterStoryQuests = MakeStoryFixture().characterStoryQuests;
            List<CharacterSaveState> originalCharacters = data.characters;
            List<string> originalParty = data.partyCharacterIds;
            List<RecoverySlotSaveState> originalRecoverySlots = data.recoverySlots;
            List<PurificationSlotSaveState> originalPurificationSlots = data.purificationSlots;
            List<string> originalUnlocks = data.unlockedRecruitmentCharacterIds;
            List<CharacterStoryQuestSaveState> originalStories = data.characterStoryQuests;

            Assert.Throws<InvalidOperationException>(() => ApplyCharacterReset(
                data, SaveResetTargets.Character, new List<string> { "ElfArcher" },
                () => { throw new InvalidOperationException("write failed"); }));

            Assert.AreSame(originalCharacters, data.characters);
            Assert.AreSame(originalParty, data.partyCharacterIds);
            Assert.AreSame(originalRecoverySlots, data.recoverySlots);
            Assert.AreSame(originalPurificationSlots, data.purificationSlots);
            Assert.AreSame(originalUnlocks, data.unlockedRecruitmentCharacterIds);
            Assert.AreSame(originalStories, data.characterStoryQuests);
            CollectionAssert.AreEqual(new[] { "CatKnight", "ElfArcher", "Barbarian" }, data.partyCharacterIds);
            Assert.AreEqual("ElfArcher", data.recoverySlots[0].characterId);
            Assert.AreEqual("ElfArcher", data.purificationSlots[0].characterId);
            Assert.AreEqual("eps", data.purificationSlots[0].lastCalculatedAtUtc);
        }

        [Test]
        public void 기본_캐릭터만_있고_삭제_선택이_없어도_Character는_저장_한_번으로_적용된다()
        {
            SaveData data = new SaveData
            {
                characters = new List<CharacterSaveState>
                {
                    new CharacterSaveState { characterId = "CatKnight", level = 1, currentStamina = -1 },
                },
                partyCharacterIds = new List<string> { "CatKnight", string.Empty, string.Empty },
            };

            SaveResetResult result = ApplyCharacterReset(
                data, SaveResetTargets.Character, new List<string>(), Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(SaveResetTargets.Character, result.AppliedTargets);
            Assert.AreEqual(1, result.ResetInitialCharacterCount);
            Assert.AreEqual(0, result.RemovedCharacterCount);
            Assert.AreEqual(1, calls.Value);
        }

        [Test]
        public void 존재하는_기본_캐릭터의_모든_진행값을_정의된_초기값으로_되돌린다()
        {
            SaveData data = new SaveData
            {
                characters = new List<CharacterSaveState>
                {
                    new CharacterSaveState
                    {
                        characterId = "CatKnight",
                        level = 17,
                        currentExp = 932,
                        currentStamina = 2,
                        passiveStaminaLastCalculatedUtc = "2026-08-31T12:00:00.0000000Z",
                        passiveStaminaProgress = 987654,
                        currentCorruption = 77.5d,
                    },
                },
                partyCharacterIds = new List<string> { string.Empty },
            };

            SaveResetResult result = ApplyCharacterReset(
                data, SaveResetTargets.Character, null, Counting(out Box<int> calls),
                new[] { new InitialCharacterResetSeed("CatKnight", 12d) });

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(1, result.ResetInitialCharacterCount);
            Assert.AreEqual(1, calls.Value);
            CharacterSaveState state = data.characters[0];
            Assert.AreEqual("CatKnight", state.characterId);
            Assert.AreEqual(1, state.level);
            Assert.AreEqual(0, state.currentExp);
            Assert.AreEqual(-1, state.currentStamina,
                "Roster가 다음 초기화에서 정의 MaxStamina를 적용할 sentinel이어야 합니다.");
            Assert.AreEqual(string.Empty, state.passiveStaminaLastCalculatedUtc);
            Assert.AreEqual(0, state.passiveStaminaProgress);
            Assert.AreEqual(12d, state.currentCorruption);
        }

        [Test]
        public void 저장에서_누락된_기본_캐릭터를_catalog_시드로_복구한다()
        {
            SaveData data = new SaveData
            {
                characters = new List<CharacterSaveState>
                {
                    new CharacterSaveState { characterId = "Barbarian", level = 6 },
                },
                partyCharacterIds = new List<string> { string.Empty },
            };

            SaveResetResult result = ApplyCharacterReset(
                data, SaveResetTargets.Character, null, Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(1, calls.Value);
            CollectionAssert.AreEqual(new[] { "Barbarian", "CatKnight" }, IdsOf(data.characters));
            CharacterSaveState restored = data.characters.Find(c => c.characterId == "CatKnight");
            Assert.NotNull(restored);
            Assert.AreEqual(1, restored.level);
            Assert.AreEqual(0, restored.currentExp);
            Assert.AreEqual(-1, restored.currentStamina);
            Assert.AreEqual(0d, restored.currentCorruption);
            CollectionAssert.AreEqual(new[] { "CatKnight", string.Empty, string.Empty }, data.partyCharacterIds);
        }

        [Test]
        public void 복수_기본_캐릭터는_catalog_순서대로_고정_파티_슬롯에_배치된다()
        {
            var seeds = new List<InitialCharacterResetSeed>
            {
                new InitialCharacterResetSeed("CatKnight", 1d),
                new InitialCharacterResetSeed("Paladin", 2d),
                new InitialCharacterResetSeed("Priest", 3d),
                new InitialCharacterResetSeed("Ranger", 4d),
            };
            SaveData data = new SaveData
            {
                characters = new List<CharacterSaveState>
                {
                    new CharacterSaveState { characterId = "Paladin", level = 9 },
                    new CharacterSaveState { characterId = "Guest", level = 4 },
                },
                partyCharacterIds = new List<string> { "Guest" },
            };

            ApplyCharacterReset(
                data, SaveResetTargets.Character, null, Counting(out Box<int> calls), seeds, partySlotCount: 3);

            Assert.AreEqual(1, calls.Value);
            CollectionAssert.AreEqual(new[] { "CatKnight", "Paladin", "Priest" }, data.partyCharacterIds,
                "정원을 넘는 기본 캐릭터도 보유에는 남되 가능한 슬롯까지만 catalog 순서로 편성합니다.");
            CollectionAssert.IsSubsetOf(
                new[] { "CatKnight", "Paladin", "Priest", "Ranger" }, IdsOf(data.characters));
            Assert.AreEqual(4d, data.characters.Find(c => c.characterId == "Ranger").currentCorruption);
        }

        [Test]
        public void Character_reset은_기본_초기화와_선택한_비기본_삭제를_동시에_수행한다()
        {
            SaveData data = MakeCharacterFixture();

            SaveResetResult result = ApplyCharacterReset(
                data, SaveResetTargets.Character, new[] { "ElfArcher" }, Counting(out Box<int> calls),
                new[] { new InitialCharacterResetSeed("CatKnight", 9d) });

            Assert.AreEqual(1, calls.Value);
            Assert.AreEqual(1, result.RemovedCharacterCount);
            Assert.AreEqual(1, result.ResetInitialCharacterCount);
            CollectionAssert.AreEqual(new[] { "CatKnight", "Barbarian" }, IdsOf(data.characters));
            CharacterSaveState initial = data.characters.Find(c => c.characterId == "CatKnight");
            Assert.AreEqual(1, initial.level);
            Assert.AreEqual(-1, initial.currentStamina);
            Assert.AreEqual(9d, initial.currentCorruption);
        }

        [Test]
        public void Character만_선택하면_기본_퀘스트는_유지하고_삭제된_캐릭터_퀘스트만_제거한다()
        {
            SaveData data = MakeStoryFixture();
            data.partyCharacterIds = new List<string> { "ElfArcher", "CatKnight" };
            CharacterStoryQuestSaveState originalCat = data.characterStoryQuests[0];

            ApplyCharacterReset(
                data, SaveResetTargets.Character, new[] { "ElfArcher" }, Counting(out Box<int> calls),
                questDefinitions: MakeStoryDefinitions());

            Assert.AreEqual(1, calls.Value);
            Assert.AreEqual(1, data.characterStoryQuests.Count);
            Assert.AreSame(originalCat, data.characterStoryQuests[0],
                "Character reset만으로 기본 캐릭터의 Quest 진행을 초기화하면 안 됩니다.");
            Assert.AreEqual("CatKnight_10003", data.characterStoryQuests[0].activeQuestId);
        }

        [Test]
        public void Character와_Quest를_함께_선택하면_남은_캐릭터_퀘스트를_첫_단계로_초기화한다()
        {
            SaveData data = MakeStoryFixture();
            data.partyCharacterIds = new List<string> { "ElfArcher", "CatKnight", string.Empty };

            SaveResetResult result = ApplyCharacterReset(
                data, SaveResetTargets.Character | SaveResetTargets.Quest,
                new[] { "ElfArcher" }, Counting(out Box<int> calls),
                questDefinitions: MakeStoryDefinitions());

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(1, calls.Value);
            Assert.AreEqual(1, data.characterStoryQuests.Count);
            Assert.AreEqual("CatKnight_10001", data.characterStoryQuests[0].activeQuestId);
            Assert.AreEqual(0, data.characterStoryQuests[0].completedQuestIds.Count);
            Assert.AreEqual(0, data.characterStoryQuests[0].objectiveProgress.Count);
        }

        [Test]
        public void Character_reset은_선택_삭제의_모집해금만_지우고_All은_전체_해금을_지운다()
        {
            SaveData selected = MakeCharacterFixture();
            selected.unlockedRecruitmentCharacterIds = new List<string> { "CatKnight", "ElfArcher", "Barbarian" };
            ApplyCharacterReset(
                selected, SaveResetTargets.Character, new[] { "ElfArcher" }, Counting(out Box<int> selectedCalls));

            Assert.AreEqual(1, selectedCalls.Value);
            CollectionAssert.AreEqual(
                new[] { "CatKnight", "Barbarian" }, selected.unlockedRecruitmentCharacterIds);

            SaveData all = MakeCharacterFixture();
            all.unlockedRecruitmentCharacterIds = new List<string> { "CatKnight", "ElfArcher", "Barbarian" };
            ApplyCharacterReset(
                all, SaveResetTargets.All, new[] { "ElfArcher" }, Counting(out Box<int> allCalls),
                questDefinitions: MakeStoryDefinitions());

            Assert.AreEqual(1, allCalls.Value);
            Assert.AreEqual(0, all.unlockedRecruitmentCharacterIds.Count);
        }

        [Test]
        public void Character_설정이_없거나_유효하지_않으면_혼합_대상도_변경하거나_저장하지_않는다()
        {
            IReadOnlyList<InitialCharacterResetSeed>[] invalidSeeds =
            {
                null,
                new InitialCharacterResetSeed[0],
                new[] { new InitialCharacterResetSeed(string.Empty, 0d) },
                new[] { new InitialCharacterResetSeed("CatKnight", -1d) },
                new[]
                {
                    new InitialCharacterResetSeed("CatKnight", 0d),
                    new InitialCharacterResetSeed("CatKnight", 0d),
                },
            };

            foreach (IReadOnlyList<InitialCharacterResetSeed> seeds in invalidSeeds)
            {
                SaveData data = MakeCharacterFixture();
                List<CharacterSaveState> originalCharacters = data.characters;
                List<InventoryItemState> originalItems = data.items;
                SaveResetResult result = SaveResetService.Apply(
                    data, SaveResetTargets.Character | SaveResetTargets.Item, new[] { "ElfArcher" },
                    seeds, PartySlotCount, null, Counting(out Box<int> calls));

                Assert.AreEqual(SaveResetOutcome.InvalidCharacterResetConfiguration, result.Outcome);
                Assert.AreEqual(SaveResetTargets.None, result.AppliedTargets);
                Assert.AreEqual(0, calls.Value);
                Assert.AreSame(originalCharacters, data.characters);
                Assert.AreSame(originalItems, data.items);
            }

            SaveData invalidSlots = MakeCharacterFixture();
            List<CharacterSaveState> original = invalidSlots.characters;
            SaveResetResult slotResult = SaveResetService.Apply(
                invalidSlots, SaveResetTargets.Character, null, DefaultInitialSeeds(), 0, null,
                Counting(out Box<int> slotCalls));
            Assert.AreEqual(SaveResetOutcome.InvalidCharacterResetConfiguration, slotResult.Outcome);
            Assert.AreEqual(0, slotCalls.Value);
            Assert.AreSame(original, invalidSlots.characters);
        }

        [Test]
        public void ResolveRemovableIds는_요청과_존재의_교집합에서_보호를_뺀다()
        {
            SaveData data = MakeCharacterFixture();

            HashSet<string> removable = SaveResetService.ResolveRemovableIds(
                data.characters,
                new List<string> { "ElfArcher", "Barbarian", "Ghost", "" },
                new List<string> { "Barbarian" });

            CollectionAssert.AreEquivalent(new[] { "ElfArcher" }, removable,
                "존재하고(=ElfArcher) 보호가 아니며(Barbarian 제외) 빈 값/미존재(Ghost, \"\")가 아닌 것만 남습니다.");
        }

        private static List<StoryQuestResetDefinition> MakeStoryDefinitions()
        {
            return new List<StoryQuestResetDefinition>
            {
                new StoryQuestResetDefinition("CatKnight_10001", "CatKnight", ""),
                new StoryQuestResetDefinition("CatKnight_10002", "CatKnight", "CatKnight_10001"),
                new StoryQuestResetDefinition("CatKnight_10003", "CatKnight", "CatKnight_10002"),
                new StoryQuestResetDefinition("ElfArcher_10001", "ElfArcher", ""),
                new StoryQuestResetDefinition("ElfArcher_10002", "ElfArcher", "ElfArcher_10001"),
            };
        }

        private static SaveData MakeStoryFixture()
        {
            return new SaveData
            {
                currency = 777,
                characters = new List<CharacterSaveState>
                {
                    new CharacterSaveState { characterId = "CatKnight", level = 8 },
                    new CharacterSaveState { characterId = "ElfArcher", level = 4 },
                },
                characterStoryQuests = new List<CharacterStoryQuestSaveState>
                {
                    new CharacterStoryQuestSaveState
                    {
                        characterId = "CatKnight",
                        activeQuestId = "CatKnight_10003",
                        completedQuestIds = new List<string> { "CatKnight_10001", "CatKnight_10002" },
                        objectiveProgress = new List<CharacterStoryObjectiveProgressSaveState>
                        {
                            new CharacterStoryObjectiveProgressSaveState { objectiveId = "kill", progress = 9 },
                        },
                        readyToComplete = true,
                    },
                    new CharacterStoryQuestSaveState
                    {
                        characterId = "ElfArcher",
                        activeQuestId = "ElfArcher_10002",
                        completedQuestIds = new List<string> { "ElfArcher_10001" },
                        objectiveProgress = new List<CharacterStoryObjectiveProgressSaveState>
                        {
                            new CharacterStoryObjectiveProgressSaveState { objectiveId = "entry", progress = 2 },
                        },
                    },
                },
            };
        }

        [Test]
        public void Quest_전체_초기화는_보유_캐릭터를_각_첫_단계로_되돌린다()
        {
            SaveData data = MakeStoryFixture();

            SaveResetResult result = SaveResetService.Apply(
                data, SaveResetTargets.Quest, null, null, 0, MakeStoryDefinitions(),
                Counting(out Box<int> calls));

            Assert.AreEqual(SaveResetOutcome.Success, result.Outcome);
            Assert.AreEqual(SaveResetTargets.Quest, result.AppliedTargets);
            Assert.AreEqual(1, calls.Value);
            Assert.AreEqual(2, data.characterStoryQuests.Count);
            Assert.AreEqual("CatKnight_10001", data.characterStoryQuests[0].activeQuestId);
            Assert.AreEqual("ElfArcher_10001", data.characterStoryQuests[1].activeQuestId);
            foreach (CharacterStoryQuestSaveState state in data.characterStoryQuests)
            {
                Assert.AreEqual(0, state.completedQuestIds.Count);
                Assert.AreEqual(0, state.objectiveProgress.Count);
                Assert.IsFalse(state.readyToComplete);
                Assert.IsFalse(state.graduated);
            }
            Assert.AreEqual(777, data.currency, "Quest만 선택하면 다른 저장 영역은 유지합니다.");
        }

        [Test]
        public void 지정_초기화는_해당_캐릭터만_목표_단계의_시작_상태로_만든다()
        {
            SaveData data = MakeStoryFixture();
            CharacterStoryQuestSaveState oldElf = data.characterStoryQuests[1];

            StoryQuestResetOutcome result = SaveResetService.ResetStoryQuestTo(
                data, "CatKnight_10002", MakeStoryDefinitions(), Counting(out Box<int> calls));

            Assert.AreEqual(StoryQuestResetOutcome.Success, result);
            Assert.AreEqual(1, calls.Value);
            CharacterStoryQuestSaveState cat = data.characterStoryQuests.Find(s => s.characterId == "CatKnight");
            Assert.NotNull(cat);
            Assert.AreEqual("CatKnight_10002", cat.activeQuestId);
            CollectionAssert.AreEqual(new[] { "CatKnight_10001" }, cat.completedQuestIds,
                "지정 단계보다 앞선 퀘스트는 완료 이력이어야 전체 진행률과 다음 연결이 맞습니다.");
            Assert.AreEqual(0, cat.objectiveProgress.Count);
            Assert.IsFalse(cat.readyToComplete);
            Assert.IsFalse(cat.graduated);
            Assert.AreSame(oldElf, data.characterStoryQuests.Find(s => s.characterId == "ElfArcher"));
        }

        [Test]
        public void 존재하지_않는_퀘스트_ID는_저장하거나_변경하지_않는다()
        {
            SaveData data = MakeStoryFixture();
            List<CharacterStoryQuestSaveState> original = data.characterStoryQuests;

            StoryQuestResetOutcome result = SaveResetService.ResetStoryQuestTo(
                data, "Missing_99999", MakeStoryDefinitions(), Counting(out Box<int> calls));

            Assert.AreEqual(StoryQuestResetOutcome.QuestNotFound, result);
            Assert.AreEqual(0, calls.Value);
            Assert.AreSame(original, data.characterStoryQuests);
        }

        [Test]
        public void 지정_퀘스트_저장_실패는_전체_퀘스트_목록을_되돌린다()
        {
            SaveData data = MakeStoryFixture();
            List<CharacterStoryQuestSaveState> original = data.characterStoryQuests;

            StoryQuestResetOutcome result = SaveResetService.ResetStoryQuestTo(
                data, "CatKnight_10002", MakeStoryDefinitions(), Counting(out Box<int> calls, succeeds: false));

            Assert.AreEqual(StoryQuestResetOutcome.SaveFailed, result);
            Assert.AreEqual(1, calls.Value);
            Assert.AreSame(original, data.characterStoryQuests);
            Assert.AreEqual("CatKnight_10003", data.characterStoryQuests[0].activeQuestId);
        }

        [Test]
        public void 끊기거나_순환하는_선행_퀘스트는_지정_초기화를_거부한다()
        {
            SaveData data = MakeStoryFixture();
            var broken = new List<StoryQuestResetDefinition>
            {
                new StoryQuestResetDefinition("CatKnight_10002", "CatKnight", "CatKnight_Missing"),
            };

            StoryQuestResetOutcome result = SaveResetService.ResetStoryQuestTo(
                data, "CatKnight_10002", broken, Counting(out Box<int> calls));

            Assert.AreEqual(StoryQuestResetOutcome.InvalidQuestChain, result);
            Assert.AreEqual(0, calls.Value);
            Assert.AreEqual("CatKnight_10003", data.characterStoryQuests[0].activeQuestId);
        }
    }
}
