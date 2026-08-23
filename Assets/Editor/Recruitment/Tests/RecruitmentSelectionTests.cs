using System;
using System.Collections.Generic;
using System.Reflection;
using Character;
using Common;
using NUnit.Framework;
using Recruitment;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RecruitmentEditor.Tests
{
    /// <summary>
    /// 모집 창구 조회와 후보 뽑기 시험.
    ///
    /// <b>씬도 UI도 저장 파일도 쓰지 않는다.</b> 카탈로그와 정의를 메모리에서 만들고 난수를 직접
    /// 끼워 넣으므로, 결과가 실행 환경이나 시험 순서에 따라 달라지지 않는다 - 가중치 경계를 글자
    /// 그대로 확인할 수 있는 것도 그래서다.
    ///
    /// <b>저장 문서를 건드리지 않는다는 것도 여기서 확인한다.</b> 뽑기는 보유 여부를
    /// <see cref="IRecruitmentOwnership"/>로 넘겨받을 뿐이므로, 몇 번을 굴려도 저장 문서의 내용도
    /// 저장소의 쓰기 횟수도 달라지지 않아야 한다.
    /// </summary>
    public sealed class RecruitmentSelectionTests
    {
        private const string InnNormal = "Inn_Normal";
        private const string InnAccess = "Inn_Normal_Access";
        private const string BuildingId = "1";

        private static readonly MethodInfo ConfigureSaveMethod = typeof(SaveSystem).GetMethod(
            "ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object asset in created)
            {
                if (asset != null) Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        // ---- 창구 조회: BUILDING/1 -> Inn_Normal_Access -> Inn_Normal ----

        [Test]
        public void Resolve_BuildingOne_ReachesInnNormal()
        {
            RecruitmentTypeDefinition type = Type(InnNormal);
            RecruitmentAccessCatalog accesses = AccessCatalog(Access(InnAccess, InnNormal, type));

            RecruitmentAccessResolution resolution =
                RecruitmentAccessResolver.ResolveForBuilding(BuildingId, accesses, TypeCatalog(type));

            Assert.AreEqual(RecruitmentAccessOutcome.Resolved, resolution.Outcome);
            Assert.IsTrue(resolution.IsResolved);
            Assert.AreEqual(InnAccess, resolution.Access.RecruitmentAccessId);
            Assert.AreEqual(InnNormal, resolution.RecruitmentTypeId);
            Assert.AreSame(type, resolution.RecruitmentType);
        }

        [Test]
        public void Resolve_OtherBuilding_ReturnsNoAccess()
        {
            RecruitmentTypeDefinition type = Type(InnNormal);
            RecruitmentAccessCatalog accesses = AccessCatalog(Access(InnAccess, InnNormal, type));

            RecruitmentAccessResolution resolution =
                RecruitmentAccessResolver.ResolveForBuilding("2", accesses, TypeCatalog(type));

            Assert.AreEqual(RecruitmentAccessOutcome.NoAccess, resolution.Outcome);
            Assert.IsNull(resolution.Access);
            Assert.AreEqual(string.Empty, resolution.RecruitmentTypeId);
        }

        [Test]
        public void Resolve_SourceTypeMustMatchExactly()
        {
            RecruitmentTypeDefinition type = Type(InnNormal);
            RecruitmentAccessCatalog accesses = AccessCatalog(Access(InnAccess, InnNormal, type));

            // 종류가 다르면 id가 같아도 다른 대상이다 - 소문자로 적어도 같은 것으로 보지 않는다.
            Assert.AreEqual(RecruitmentAccessOutcome.NoAccess,
                RecruitmentAccessResolver.Resolve("QUEST", BuildingId, accesses, TypeCatalog(type)).Outcome);
            Assert.AreEqual(RecruitmentAccessOutcome.NoAccess,
                RecruitmentAccessResolver.Resolve("building", BuildingId, accesses, TypeCatalog(type)).Outcome);
        }

        [Test]
        public void Resolve_DisabledAccess_StopsAtTheAccess()
        {
            RecruitmentTypeDefinition type = Type(InnNormal);
            RecruitmentAccessDefinition access = Access(InnAccess, InnNormal, type, enabled: false);

            RecruitmentAccessResolution resolution = RecruitmentAccessResolver.ResolveForBuilding(
                BuildingId, AccessCatalog(access), TypeCatalog(type));

            Assert.AreEqual(RecruitmentAccessOutcome.AccessDisabled, resolution.Outcome);
            Assert.AreSame(access, resolution.Access);
            Assert.IsFalse(resolution.IsResolved);
        }

        [Test]
        public void Resolve_DisabledRecruitmentType_StopsAtTheType()
        {
            RecruitmentTypeDefinition type = Type(InnNormal, enabled: false);

            RecruitmentAccessResolution resolution = RecruitmentAccessResolver.ResolveForBuilding(
                BuildingId, AccessCatalog(Access(InnAccess, InnNormal, type)), TypeCatalog(type));

            Assert.AreEqual(RecruitmentAccessOutcome.RecruitmentTypeDisabled, resolution.Outcome);
        }

        [Test]
        public void Resolve_BrokenTypeReference_ReportsUnknownTypeButKeepsTheId()
        {
            // 참조도 없고 목록에도 없다 - 무엇을 열려 했는지는 id로 남아야 한다.
            RecruitmentAccessDefinition access = Access(InnAccess, InnNormal, null);

            RecruitmentAccessResolution resolution = RecruitmentAccessResolver.ResolveForBuilding(
                BuildingId, AccessCatalog(access), TypeCatalog());

            Assert.AreEqual(RecruitmentAccessOutcome.UnknownRecruitmentType, resolution.Outcome);
            Assert.AreEqual(InnNormal, resolution.RecruitmentTypeId);
            Assert.IsNull(resolution.RecruitmentType);
        }

        [Test]
        public void Resolve_ReferenceIsUsedOnlyWhenItAgreesWithTheId()
        {
            RecruitmentTypeDefinition agreeing = Type(InnNormal);
            RecruitmentTypeDefinition disagreeing = Type("Inn_Rare");

            // 목록이 비어 있어도 id가 정확히 같은 참조는 쓸 수 있다.
            Assert.AreEqual(RecruitmentAccessOutcome.Resolved,
                RecruitmentAccessResolver.ResolveForBuilding(
                    BuildingId, AccessCatalog(Access(InnAccess, InnNormal, agreeing)), TypeCatalog()).Outcome);

            // id가 어긋나는 참조는 쓰지 않는다 - 표가 가리키는 것과 다른 모집이 열리는 것보다 낫다.
            Assert.AreEqual(RecruitmentAccessOutcome.UnknownRecruitmentType,
                RecruitmentAccessResolver.ResolveForBuilding(
                    BuildingId, AccessCatalog(Access(InnAccess, InnNormal, disagreeing)), TypeCatalog()).Outcome);
        }

        // ---- 후보 거르기 ----

        [Test]
        public void CollectEligible_KeepsTheAuthoredOrder()
        {
            RecruitmentPoolCatalog pool = Pool(
                Entry("1", "CatKnight", 100), Entry("2", "ElfArcher", 60), Entry("3", "Barbarian", 80));

            IReadOnlyList<RecruitmentPoolEntryDefinition> candidates = RecruitmentCandidateSelector.CollectEligible(
                InnNormal, pool, Acquisitions("CatKnight", "ElfArcher", "Barbarian"), RecruitmentOwnership.None);

            CollectionAssert.AreEqual(
                new[] { "CatKnight", "ElfArcher", "Barbarian" }, CharacterIds(candidates),
                "후보 순서가 곧 가중치 경계의 순서다 - 임의로 정렬하면 같은 난수가 다른 답을 낸다.");
        }

        [Test]
        public void CollectEligible_DropsInvalidWeights()
        {
            RecruitmentPoolCatalog pool = Pool(
                Entry("1", "CatKnight", 0),
                Entry("2", "ElfArcher", -5),
                Entry("3", "Barbarian", 1));

            IReadOnlyList<RecruitmentPoolEntryDefinition> candidates = RecruitmentCandidateSelector.CollectEligible(
                InnNormal, pool, Acquisitions("CatKnight", "ElfArcher", "Barbarian"), RecruitmentOwnership.None);

            CollectionAssert.AreEqual(new[] { "Barbarian" }, CharacterIds(candidates),
                "가중치가 1 미만인 칸은 절대 뽑히지 않으므로 후보가 아니다.");
        }

        [Test]
        public void CollectEligible_DropsDisabledRows()
        {
            RecruitmentPoolCatalog pool = Pool(
                Entry("1", "CatKnight", 100, enabled: false),
                Entry("2", "ElfArcher", 60),
                Entry("3", "Barbarian", 80));

            CharacterAcquisitionCatalog acquisitions = AcquisitionCatalog(
                Acquisition("ElfArcher"),
                Acquisition("Barbarian", enabled: false));

            IReadOnlyList<RecruitmentPoolEntryDefinition> candidates = RecruitmentCandidateSelector.CollectEligible(
                InnNormal, pool, acquisitions, RecruitmentOwnership.None);

            CollectionAssert.AreEqual(new[] { "ElfArcher" }, CharacterIds(candidates),
                "꺼진 후보 칸도, 꺼진 획득 방식도 후보가 되지 못한다.");
        }

        [Test]
        public void CollectEligible_DropsBrokenAndUnknownAcquisitions()
        {
            RecruitmentPoolCatalog pool = Pool(
                Entry("1", "CatKnight", 100),   // 획득 방식 행이 아예 없다(끊어진 참조).
                Entry("2", "ElfArcher", 60),    // 런타임이 모르는 낱말이다.
                Entry("3", "Barbarian", 80),    // 조건이 걸려 있다.
                Entry("4", "CatMage", 40));

            CharacterAcquisitionCatalog acquisitions = AcquisitionCatalog(
                Acquisition("ElfArcher", acquisitionType: "QUEST_ONLY"),
                Acquisition("Barbarian", conditionId: "42"),
                Acquisition("CatMage"));

            IReadOnlyList<RecruitmentPoolEntryDefinition> candidates = RecruitmentCandidateSelector.CollectEligible(
                InnNormal, pool, acquisitions, RecruitmentOwnership.None);

            CollectionAssert.AreEqual(new[] { "CatMage" }, CharacterIds(candidates),
                "획득 방식을 모르거나 조건이 걸린 캐릭터는 뽑지 않는다 - 뽑아 놓고 줄 수 없는 것이 더 나쁘다.");
        }

        [Test]
        public void CollectEligible_DropsEntryWithoutCharacterId()
        {
            RecruitmentPoolCatalog pool = Pool(Entry("1", string.Empty, 100), Entry("2", "CatMage", 40));

            IReadOnlyList<RecruitmentPoolEntryDefinition> candidates = RecruitmentCandidateSelector.CollectEligible(
                InnNormal, pool, Acquisitions("CatMage"), RecruitmentOwnership.None);

            CollectionAssert.AreEqual(new[] { "CatMage" }, CharacterIds(candidates));
        }

        [Test]
        public void CollectEligible_KeepsOnlyTheFirstEntryOfADuplicatedCharacter()
        {
            RecruitmentPoolEntryDefinition first = Entry("1", "CatKnight", 100);
            RecruitmentPoolCatalog pool = Pool(first, Entry("2", "CatKnight", 5), Entry("3", "CatMage", 40));

            IReadOnlyList<RecruitmentPoolEntryDefinition> candidates = RecruitmentCandidateSelector.CollectEligible(
                InnNormal, pool, Acquisitions("CatKnight", "CatMage"), RecruitmentOwnership.None);

            CollectionAssert.AreEqual(new[] { "CatKnight", "CatMage" }, CharacterIds(candidates));
            Assert.AreSame(first, candidates[0], "겹칠 때는 먼저 적힌 칸이 남는다.");
        }

        [Test]
        public void CollectEligible_IgnoresEntriesOfAnotherRecruitmentType()
        {
            RecruitmentPoolCatalog pool = Pool(
                Entry("1", "CatKnight", 100),
                Entry("2", "CatMage", 40, recruitmentTypeId: "Inn_Rare"));

            IReadOnlyList<RecruitmentPoolEntryDefinition> candidates = RecruitmentCandidateSelector.CollectEligible(
                InnNormal, pool, Acquisitions("CatKnight", "CatMage"), RecruitmentOwnership.None);

            CollectionAssert.AreEqual(new[] { "CatKnight" }, CharacterIds(candidates));
        }

        [Test]
        public void CollectEligible_BrokenCharacterReferenceStillCounts()
        {
            // 정의 참조가 끊겨도 <b>id가 판정의 기준</b>이므로 후보로 남는다.
            RecruitmentPoolEntryDefinition entry = Entry("1", "CatKnight", 100, brokenCharacterReference: true);

            IReadOnlyList<RecruitmentPoolEntryDefinition> candidates = RecruitmentCandidateSelector.CollectEligible(
                InnNormal, Pool(entry), Acquisitions("CatKnight"), RecruitmentOwnership.None);

            CollectionAssert.AreEqual(new[] { "CatKnight" }, CharacterIds(candidates));
            Assert.IsNull(candidates[0].Character);
        }

        // ---- 보유 제외 ----

        [Test]
        public void Select_OwnedCatKnightIsExcluded()
        {
            RecruitmentPoolCatalog pool = Pool(Entry("1", "CatKnight", 100), Entry("2", "CatMage", 40));
            CharacterAcquisitionCatalog acquisitions = Acquisitions("CatKnight", "CatMage");

            RecruitmentSelection selection = RecruitmentCandidateSelector.Select(
                InnNormal, pool, acquisitions, RecruitmentOwnership.Of("CatKnight"), Roll(0));

            Assert.AreEqual(RecruitmentSelectionOutcome.Selected, selection.Outcome);
            Assert.AreEqual("CatMage", selection.CharacterId,
                "새 게임에서 이미 가지고 시작하는 고양이기사는 모집 후보에서 빠져야 한다.");
            Assert.AreEqual(40, selection.TotalWeight, "빠진 후보의 가중치는 합에도 들어가지 않는다.");
        }

        [Test]
        public void Select_OwnedCharacterStaysWhenDuplicateRecruitmentIsAllowed()
        {
            RecruitmentPoolCatalog pool = Pool(Entry("1", "CatKnight", 100));
            CharacterAcquisitionCatalog acquisitions =
                AcquisitionCatalog(Acquisition("CatKnight", allowDuplicate: true));

            RecruitmentSelection selection = RecruitmentCandidateSelector.Select(
                InnNormal, pool, acquisitions, RecruitmentOwnership.Of("CatKnight"), Roll(0));

            Assert.AreEqual("CatKnight", selection.CharacterId);
        }

        [Test]
        public void Select_OwnershipComparisonIsOrdinal()
        {
            RecruitmentPoolCatalog pool = Pool(Entry("1", "CatKnight", 100));

            // 'catknight'는 'CatKnight'가 아니다 - 저장 문서의 키와 표의 id가 어긋난 것을 덮지 않는다.
            RecruitmentSelection selection = RecruitmentCandidateSelector.Select(
                InnNormal, pool, Acquisitions("CatKnight"), RecruitmentOwnership.Of("catknight"), Roll(0));

            Assert.AreEqual("CatKnight", selection.CharacterId);
        }

        [Test]
        public void Select_AllOwned_ReturnsNoEligibleCandidate()
        {
            RecruitmentPoolCatalog pool = Pool(
                Entry("1", "CatKnight", 100), Entry("2", "ElfArcher", 60), Entry("3", "CatMage", 40));

            RecruitmentSelection selection = RecruitmentCandidateSelector.Select(
                InnNormal, pool, Acquisitions("CatKnight", "ElfArcher", "CatMage"),
                RecruitmentOwnership.Of("CatKnight", "ElfArcher", "CatMage"), Roll(0));

            Assert.AreEqual(RecruitmentSelectionOutcome.NoEligibleCandidate, selection.Outcome);
            Assert.IsFalse(selection.IsSelected);
            Assert.IsNull(selection.Entry);
            Assert.AreEqual(string.Empty, selection.CharacterId, "뽑지 못했을 때도 null을 돌려주지 않는다.");
            Assert.AreEqual(0, selection.TotalWeight);
        }

        [Test]
        public void Select_EmptyPool_ReturnsNoEligibleCandidate()
        {
            Assert.AreEqual(RecruitmentSelectionOutcome.NoEligibleCandidate,
                RecruitmentCandidateSelector.Select(
                    InnNormal, Pool(), Acquisitions(), RecruitmentOwnership.None, Roll(0)).Outcome);

            Assert.AreEqual(RecruitmentSelectionOutcome.NoEligibleCandidate,
                RecruitmentCandidateSelector.Select(
                    InnNormal, null, Acquisitions(), RecruitmentOwnership.None, Roll(0)).Outcome);
        }

        // ---- 가중치 경계 ----

        /// <summary>
        /// 실제 표에 적힌 여섯 후보의 가중치(100/60/80/50/30/60, 합 380)로 경계를 글자 그대로 확인한다.
        /// <b>왼쪽 끝은 포함, 오른쪽 끝은 제외</b>이므로 99는 첫째, 100은 둘째다.
        /// </summary>
        [TestCase(0, "CatKnight")]
        [TestCase(99, "CatKnight")]
        [TestCase(100, "ElfArcher")]
        [TestCase(159, "ElfArcher")]
        [TestCase(160, "Barbarian")]
        [TestCase(239, "Barbarian")]
        [TestCase(240, "ElfGuardian")]
        [TestCase(289, "ElfGuardian")]
        [TestCase(290, "RabbitHealer")]
        [TestCase(319, "RabbitHealer")]
        [TestCase(320, "CatMage")]
        [TestCase(379, "CatMage")]
        public void Select_WeightBoundaries_AreExact(int roll, string expected)
        {
            RecruitmentSelection selection = RecruitmentCandidateSelector.Select(
                InnNormal, LivePool(), LiveAcquisitions(), RecruitmentOwnership.None, Roll(roll));

            Assert.AreEqual(RecruitmentSelectionOutcome.Selected, selection.Outcome);
            Assert.AreEqual(380, selection.TotalWeight);
            Assert.AreEqual(roll, selection.Roll);
            Assert.AreEqual(expected, selection.CharacterId);
        }

        [Test]
        public void Select_RollIsAlwaysInsideTheTotalWeight()
        {
            var spy = new RecordingRandom(0);
            RecruitmentCandidateSelector.Select(
                InnNormal, LivePool(), LiveAcquisitions(), RecruitmentOwnership.None, spy);

            Assert.AreEqual(380, spy.LastMaxExclusive,
                "난수는 후보 가중치 합을 상한으로 받아야 한다 - 후보 수가 아니다.");
        }

        [Test]
        public void SelectFrom_OutOfRangeRoll_IsClampedInsteadOfSelectingNobody()
        {
            IReadOnlyList<RecruitmentPoolEntryDefinition> candidates = RecruitmentCandidateSelector.CollectEligible(
                InnNormal, LivePool(), LiveAcquisitions(), RecruitmentOwnership.None);

            Assert.AreEqual("CatKnight", RecruitmentCandidateSelector.SelectFrom(candidates, Roll(-1)).CharacterId);
            Assert.AreEqual("CatMage", RecruitmentCandidateSelector.SelectFrom(candidates, Roll(9999)).CharacterId);
        }

        [Test]
        public void Select_SameSeedGivesTheSameResult()
        {
            string first = RecruitmentCandidateSelector.Select(
                InnNormal, LivePool(), LiveAcquisitions(), RecruitmentOwnership.None,
                new SystemRecruitmentRandom(1234)).CharacterId;

            string second = RecruitmentCandidateSelector.Select(
                InnNormal, LivePool(), LiveAcquisitions(), RecruitmentOwnership.None,
                new SystemRecruitmentRandom(1234)).CharacterId;

            Assert.AreEqual(first, second);
            Assert.IsNotEmpty(first);
        }

        // ---- 저장 문서를 건드리지 않는다 ----

        [Test]
        public void Select_DoesNotTouchSaveData()
        {
            Assert.IsNotNull(ConfigureSaveMethod,
                "SaveSystem.ConfigureForTests를 찾지 못했습니다 - 그대로 두면 시험이 실제 저장 파일을 읽고 씁니다.");

            var storage = new FakeStorage();
            ConfigureSaveMethod.Invoke(null, new object[] { storage, null, null });

            try
            {
                SaveData document = SaveSystem.Data;
                Assert.IsNotNull(document);

                string before = JsonUtility.ToJson(document);
                int writesBefore = storage.WriteCalls;

                RecruitmentPoolCatalog pool = LivePool();
                CharacterAcquisitionCatalog acquisitions = LiveAcquisitions();
                IRecruitmentOwnership ownership = RecruitmentOwnership.Of("CatKnight");

                for (int roll = 0; roll < 380; roll += 37)
                {
                    RecruitmentSelection selection = RecruitmentCandidateSelector.Select(
                        InnNormal, pool, acquisitions, ownership, Roll(roll));

                    Assert.AreEqual(RecruitmentSelectionOutcome.Selected, selection.Outcome);
                    Assert.AreNotEqual("CatKnight", selection.CharacterId);
                }

                RecruitmentAccessResolver.ResolveForBuilding(
                    BuildingId, AccessCatalog(Access(InnAccess, InnNormal, Type(InnNormal))),
                    TypeCatalog(Type(InnNormal)));

                Assert.AreEqual(before, JsonUtility.ToJson(document),
                    "뽑기와 창구 조회는 저장 문서를 한 글자도 바꾸지 않아야 한다.");
                Assert.AreEqual(writesBefore, storage.WriteCalls, "뽑기는 저장을 부르지 않아야 한다.");
            }
            finally
            {
                ConfigureSaveMethod.Invoke(null, new object[] { null, null, null });
            }
        }

        // ---- 도우미 ----

        private static string[] CharacterIds(IReadOnlyList<RecruitmentPoolEntryDefinition> entries)
        {
            var ids = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++) ids[i] = entries[i].CharacterId;
            return ids;
        }

        /// <summary>실제 RecruitmentPool.csv와 같은 여섯 후보(순서와 가중치 그대로).</summary>
        private RecruitmentPoolCatalog LivePool()
        {
            return Pool(
                Entry("1", "CatKnight", 100),
                Entry("2", "ElfArcher", 60),
                Entry("3", "Barbarian", 80),
                Entry("4", "ElfGuardian", 50),
                Entry("5", "RabbitHealer", 30),
                Entry("6", "CatMage", 60));
        }

        private CharacterAcquisitionCatalog LiveAcquisitions()
        {
            return Acquisitions("CatKnight", "ElfArcher", "Barbarian", "ElfGuardian", "RabbitHealer", "CatMage");
        }

        private CharacterAcquisitionCatalog Acquisitions(params string[] characterIds)
        {
            var entries = new CharacterAcquisitionDefinition[characterIds.Length];
            for (int i = 0; i < characterIds.Length; i++) entries[i] = Acquisition(characterIds[i]);
            return AcquisitionCatalog(entries);
        }

        private CharacterAcquisitionCatalog AcquisitionCatalog(params CharacterAcquisitionDefinition[] entries)
        {
            var catalog = Create<CharacterAcquisitionCatalog>();
            FillList(catalog, "acquisitions", entries);
            catalog.MarkDirty();
            return catalog;
        }

        private CharacterAcquisitionDefinition Acquisition(
            string characterId,
            string acquisitionType = RecruitmentAcquisitionTypes.RecruitOnly,
            bool allowDuplicate = false,
            string conditionId = "",
            bool enabled = true)
        {
            var definition = Create<CharacterAcquisitionDefinition>();
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("acquisitionId").stringValue = characterId;
            serialized.FindProperty("characterId").stringValue = characterId;
            serialized.FindProperty("character").objectReferenceValue = Character(characterId);
            serialized.FindProperty("acquisitionType").stringValue = acquisitionType;
            serialized.FindProperty("allowDuplicateRecruitment").boolValue = allowDuplicate;
            serialized.FindProperty("conditionId").stringValue = conditionId;
            serialized.FindProperty("enabled").boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private RecruitmentPoolCatalog Pool(params RecruitmentPoolEntryDefinition[] entries)
        {
            var catalog = Create<RecruitmentPoolCatalog>();
            FillList(catalog, "entries", entries);
            catalog.MarkDirty();
            return catalog;
        }

        private RecruitmentPoolEntryDefinition Entry(
            string poolEntryId,
            string characterId,
            int weight,
            bool enabled = true,
            string recruitmentTypeId = InnNormal,
            bool brokenCharacterReference = false)
        {
            var definition = Create<RecruitmentPoolEntryDefinition>();
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("recruitmentTypeId").stringValue = recruitmentTypeId;
            serialized.FindProperty("poolEntryId").stringValue = poolEntryId;
            serialized.FindProperty("characterId").stringValue = characterId;
            serialized.FindProperty("character").objectReferenceValue =
                brokenCharacterReference || string.IsNullOrEmpty(characterId) ? null : Character(characterId);
            serialized.FindProperty("weight").intValue = weight;
            serialized.FindProperty("enabled").boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private RecruitmentTypeCatalog TypeCatalog(params RecruitmentTypeDefinition[] types)
        {
            var catalog = Create<RecruitmentTypeCatalog>();
            FillList(catalog, "types", types);
            catalog.MarkDirty();
            return catalog;
        }

        private RecruitmentTypeDefinition Type(string recruitmentTypeId, bool enabled = true)
        {
            var definition = Create<RecruitmentTypeDefinition>();
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("recruitmentTypeId").stringValue = recruitmentTypeId;
            serialized.FindProperty("enabled").boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private RecruitmentAccessCatalog AccessCatalog(params RecruitmentAccessDefinition[] accesses)
        {
            var catalog = Create<RecruitmentAccessCatalog>();
            FillList(catalog, "accesses", accesses);
            catalog.MarkDirty();
            return catalog;
        }

        private RecruitmentAccessDefinition Access(
            string accessId,
            string recruitmentTypeId,
            RecruitmentTypeDefinition recruitmentType,
            bool enabled = true,
            string sourceType = RecruitmentSourceTypes.Building,
            string sourceId = BuildingId)
        {
            var definition = Create<RecruitmentAccessDefinition>();
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("recruitmentAccessId").stringValue = accessId;
            serialized.FindProperty("recruitmentTypeId").stringValue = recruitmentTypeId;
            serialized.FindProperty("recruitmentType").objectReferenceValue = recruitmentType;
            serialized.FindProperty("sourceType").stringValue = sourceType;
            serialized.FindProperty("sourceId").stringValue = sourceId;
            serialized.FindProperty("arrivalIntervalSeconds").intValue = 60;
            serialized.FindProperty("consumeAmount").intValue = 0;
            serialized.FindProperty("displayOrder").intValue = 10;
            serialized.FindProperty("enabled").boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private CharacterDefinition Character(string characterId)
        {
            var definition = Create<CharacterDefinition>();
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("characterId").stringValue = characterId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private T Create<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            created.Add(asset);
            return asset;
        }

        private static void FillList(ScriptableObject owner, string field, Object[] values)
        {
            var serialized = new SerializedObject(owner);
            SerializedProperty list = serialized.FindProperty(field);
            Assert.IsNotNull(list, $"{owner.GetType().Name}에 '{field}' 목록 칸이 없습니다.");

            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static IRecruitmentRandom Roll(int value) => new FixedRandom(value);

        /// <summary>정해진 수 하나만 돌려주는 난수. 경계를 글자 그대로 확인하기 위한 것이다.</summary>
        private sealed class FixedRandom : IRecruitmentRandom
        {
            private readonly int value;

            internal FixedRandom(int value)
            {
                this.value = value;
            }

            public int Next(int maxExclusive) => value;
        }

        /// <summary>무엇을 상한으로 받았는지 적어 두는 난수.</summary>
        private sealed class RecordingRandom : IRecruitmentRandom
        {
            private readonly int value;

            internal RecordingRandom(int value)
            {
                this.value = value;
            }

            internal int LastMaxExclusive { get; private set; }

            public int Next(int maxExclusive)
            {
                LastMaxExclusive = maxExclusive;
                return value;
            }
        }

        /// <summary>메모리 위의 가짜 저장소. 실제 저장 파일을 읽지도 쓰지도 않는다.</summary>
        private sealed class FakeStorage : ISaveStorage
        {
            public int WriteCalls;

            public bool WritesBlocked => false;

            public string BlockedReason => null;

            public SaveReadResult ReadPrimary() => SaveReadResult.Missing("fake://primary");

            public SaveReadResult ReadBackup() => SaveReadResult.Missing("fake://backup");

            public SaveWriteResult Write(string text)
            {
                WriteCalls++;
                return SaveWriteResult.Written(false);
            }

            public SaveQuarantineResult QuarantinePrimary(string reason) =>
                SaveQuarantineResult.Moved("fake://corrupted/primary");
        }
    }
}
