using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Recruitment;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// 모집 네 표(CharacterAcquisition / RecruitmentType / RecruitmentPool / RecruitmentAccess)의
    /// 스키마와 경로, 그리고 <b>실제 CSV에 적혀 있는 내용</b>을 확인한다.
    ///
    /// <b>파일을 쓰지도 에셋을 만들지도 않는다.</b> 읽기 전용인
    /// <see cref="TableDataValidator.Validate()"/>만 쓰며, Rebuild는 프로젝트를 바꾸므로 여기서
    /// 부르지 않는다(<see cref="BuildingTableTests"/>와 같은 방식이다).
    ///
    /// <b>뽑기의 규칙은 여기서 보지 않는다.</b> 그것은 표가 아니라 런타임의 일이고,
    /// <c>RecruitmentSelectionTests</c>가 카탈로그 없이 확인한다.
    /// </summary>
    public sealed class RecruitmentTableTests
    {
        /// <summary>RecruitmentAccess.csv에 실제로 적혀 있는 유일한 창구(여관)의 값들.</summary>
        private const string LiveAccessId = "Inn_Normal_Access";

        private const string LiveRecruitmentTypeId = "Inn_Normal";
        private const string LiveSourceId = "1";
        private const int LiveArrivalIntervalSeconds = 60;
        private const int LiveConsumeAmount = 0;
        private const int LiveAccessDisplayOrder = 10;

        /// <summary>RecruitmentPool.csv에 적혀 있는 여섯 후보. <b>표에 적힌 순서 그대로</b>이며,
        /// 이 순서가 곧 가중치 경계의 순서다.</summary>
        private static readonly (string CharacterId, int Weight)[] LivePool =
        {
            ("CatKnight", 100),
            ("ElfArcher", 60),
            ("Barbarian", 80),
            ("ElfGuardian", 50),
            ("RabbitHealer", 30),
            ("CatMage", 60),
        };

        /// <summary>실제 CSV를 읽는 검증은 무거우므로 한 번만 돌리고 결과를 나눠 쓴다. 읽기 전용이라
        /// 시험 사이에 상태가 새지 않는다.</summary>
        private static TableDataValidationResult liveResult;

        private static TableDataValidationResult Live =>
            liveResult ?? (liveResult = TableDataValidator.Validate());

        // ---- 스키마와 경로 ----

        [Test]
        public void Schema_IsExactlyTheAgreedColumns()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "acquisition_id", "character_id", "acquisition_type", "allow_duplicate_recruitment",
                    "condition_id", "enabled", "memo",
                },
                TableDataColumns.CharacterAcquisition,
                "CharacterAcquisition.csv의 필수 컬럼과 순서가 약속과 달라졌습니다.");

            CollectionAssert.AreEqual(
                new[] { "recruitment_type_id", "enabled", "memo" },
                TableDataColumns.RecruitmentType,
                "RecruitmentType.csv의 필수 컬럼과 순서가 약속과 달라졌습니다.");

            CollectionAssert.AreEqual(
                new[] { "recruitment_type_id", "pool_entry_id", "character_id", "weight", "enabled", "memo" },
                TableDataColumns.RecruitmentPool,
                "RecruitmentPool.csv의 필수 컬럼과 순서가 약속과 달라졌습니다.");

            CollectionAssert.AreEqual(
                new[]
                {
                    "recruitment_access_id", "recruitment_type_id", "source_type", "source_id",
                    "arrival_interval_seconds", "consume_amount", "display_order", "enabled", "memo",
                },
                TableDataColumns.RecruitmentAccess,
                "RecruitmentAccess.csv의 필수 컬럼과 순서가 약속과 달라졌습니다.");
        }

        [Test]
        public void Schema_DoesNotIncludeTheReferenceOnlyColumns()
        {
            // $ 컬럼은 사람이 보라고 붙인 칸이라 <b>필수 컬럼이 되면 안 된다</b>.
            foreach (string reference in new[] { "$character_name", "$source_name" })
            {
                CollectionAssert.DoesNotContain(TableDataColumns.CharacterAcquisition, reference);
                CollectionAssert.DoesNotContain(TableDataColumns.RecruitmentPool, reference);
                CollectionAssert.DoesNotContain(TableDataColumns.RecruitmentAccess, reference);
                Assert.IsTrue(TableDataCsvReader.IsReferenceOnlyColumn(reference),
                    $"{reference}는 기존 참조 컬럼 정책으로 통과해야 한다.");
            }
        }

        [Test]
        public void Schema_RecruitmentTablesHaveNoInventedColumns()
        {
            // 없는 칸을 지어내지 않는다 - 이 세 표에는 display_order도 이름 참조도 없다.
            foreach (string column in new[] { "display_order", "name_category", "name_key" })
            {
                CollectionAssert.DoesNotContain(TableDataColumns.CharacterAcquisition, column);
                CollectionAssert.DoesNotContain(TableDataColumns.RecruitmentType, column);
                CollectionAssert.DoesNotContain(TableDataColumns.RecruitmentPool, column);
            }

            // 창구만 display_order를 가진다(한 대상에 여럿이 붙을 수 있으므로 순서가 필요하다).
            CollectionAssert.Contains(TableDataColumns.RecruitmentAccess, "display_order");
        }

        [Test]
        public void Paths_AreTheAgreedLocations()
        {
            Assert.AreEqual("Assets/TableData/Game/CharacterAcquisition.csv",
                TableDataPaths.CharacterAcquisitionCsvPath);
            Assert.AreEqual("Assets/TableData/Game/RecruitmentType.csv", TableDataPaths.RecruitmentTypeCsvPath);
            Assert.AreEqual("Assets/TableData/Game/RecruitmentPool.csv", TableDataPaths.RecruitmentPoolCsvPath);
            Assert.AreEqual("Assets/TableData/Game/RecruitmentAccess.csv", TableDataPaths.RecruitmentAccessCsvPath);

            Assert.AreEqual("Assets/Generated/TableData/CharacterAcquisition",
                TableDataPaths.CharacterAcquisitionOutputFolder);
            Assert.AreEqual("Assets/Generated/TableData/RecruitmentType", TableDataPaths.RecruitmentTypeOutputFolder);
            Assert.AreEqual("Assets/Generated/TableData/RecruitmentPool", TableDataPaths.RecruitmentPoolOutputFolder);
            Assert.AreEqual("Assets/Generated/TableData/RecruitmentAccess",
                TableDataPaths.RecruitmentAccessOutputFolder);

            Assert.AreEqual("Assets/Generated/TableData/RecruitmentPool/RecruitmentPool_Inn_Normal__1.asset",
                TableDataPaths.RecruitmentPoolAssetPath(
                    RecruitmentPoolEntryDefinition.BuildPairId(LiveRecruitmentTypeId, "1")));
        }

        [Test]
        public void Scope_RecruitmentOnlyWritesTheFourRecruitmentFolders()
        {
            IReadOnlyList<string> folders =
                TableDataValidator.GeneratedOutputFolders(TableDataRebuildScope.RecruitmentTables);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    TableDataPaths.CharacterAcquisitionOutputFolder,
                    TableDataPaths.RecruitmentTypeOutputFolder,
                    TableDataPaths.RecruitmentPoolOutputFolder,
                    TableDataPaths.RecruitmentAccessOutputFolder,
                },
                folders,
                "모집만 다시 만드는 범위는 자기 네 폴더 밖을 열지 않아야 한다.");
        }

        // ---- id 형식 ----

        [Test]
        public void RecruitmentIdFormat_AcceptsTheAuthoredIds_ButNotNumbersOrSpaces()
        {
            Assert.IsTrue(TableDataFieldRules.IsValidRecruitmentId(LiveRecruitmentTypeId));
            Assert.IsTrue(TableDataFieldRules.IsValidRecruitmentId(LiveAccessId));

            // 숫자로 시작할 수 없다 - 건물 id와 눈으로 구분되게 남겨 둔 성질이다.
            Assert.IsFalse(TableDataFieldRules.IsValidRecruitmentId("1"));
            Assert.IsFalse(TableDataFieldRules.IsValidRecruitmentId(" Inn_Normal"));
            Assert.IsFalse(TableDataFieldRules.IsValidRecruitmentId("Inn Normal"));
            Assert.IsFalse(TableDataFieldRules.IsValidRecruitmentId("Inn__Normal"));

            // 표준 ID 규칙은 <b>넓어지지 않았다</b> - 모집 id를 다른 표에 쓸 수는 없다.
            Assert.IsFalse(TableDataFieldRules.IsValidId(LiveRecruitmentTypeId),
                "표준 ID 형식이 대문자를 받아들이게 되면 기존 표의 검사가 함께 헐거워진다.");
        }

        [Test]
        public void UppercaseKeyFormat_AcceptsTheAuthoredWords_ButNotLowercase()
        {
            Assert.IsTrue(TableDataFieldRules.IsValidUppercaseKey(RecruitmentAcquisitionTypes.RecruitOnly));
            Assert.IsTrue(TableDataFieldRules.IsValidUppercaseKey(RecruitmentSourceTypes.Building));
            Assert.IsFalse(TableDataFieldRules.IsValidUppercaseKey("recruit_only"));
            Assert.IsFalse(TableDataFieldRules.IsValidUppercaseKey("Recruit_Only"));
            Assert.IsFalse(TableDataFieldRules.IsValidUppercaseKey(string.Empty));
        }

        // ---- 실제 CSV ----

        [Test]
        public void LiveCsv_HasNoErrors()
        {
            Assert.IsNotNull(Live.Snapshot, "열세 표가 모두 읽히지 않았습니다: " + Live.Summary);
            Assert.AreEqual(0, Live.ErrorCount, "모집 표를 포함한 검증에서 오류가 남아 있습니다: " + Live.Summary);
        }

        [Test]
        public void LiveCsv_AcquisitionCoversEveryPoolCandidate()
        {
            TableDataSnapshot snapshot = Live.Snapshot;
            Assert.IsNotNull(snapshot);

            foreach (RecruitmentPoolRow row in snapshot.RecruitmentPools)
            {
                Assert.IsTrue(
                    snapshot.CharacterAcquisitionsByCharacterId.ContainsKey(row.CharacterId),
                    $"'{row.CharacterId}'는 후보에 있으나 CharacterAcquisition.csv에 획득 방식이 없어 " +
                    "런타임이 후보에서 빼 버린다.");
            }
        }

        [Test]
        public void LiveCsv_EveryAcquisitionIsRecruitOnlyAndUnconditional()
        {
            TableDataSnapshot snapshot = Live.Snapshot;
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(LivePool.Length, snapshot.CharacterAcquisitions.Count);

            foreach (CharacterAcquisitionRow row in snapshot.CharacterAcquisitions)
            {
                Assert.AreEqual(RecruitmentAcquisitionTypes.RecruitOnly, row.AcquisitionType);
                Assert.IsFalse(row.AllowDuplicateRecruitment,
                    $"'{row.CharacterId}'는 지금 중복 모집을 허용하지 않는 것이 표의 값이다.");
                Assert.AreEqual(string.Empty, row.ConditionId);
                Assert.IsTrue(row.Enabled);
            }
        }

        [Test]
        public void LiveCsv_PoolKeepsTheAuthoredOrderAndWeights()
        {
            TableDataSnapshot snapshot = Live.Snapshot;
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(LivePool.Length, snapshot.RecruitmentPools.Count);

            for (int i = 0; i < LivePool.Length; i++)
            {
                RecruitmentPoolRow row = snapshot.RecruitmentPools[i];
                Assert.AreEqual(LiveRecruitmentTypeId, row.RecruitmentTypeId);
                Assert.AreEqual(LivePool[i].CharacterId, row.CharacterId, $"{i}번째 후보의 순서가 달라졌습니다.");
                Assert.AreEqual(LivePool[i].Weight, row.Weight, $"'{row.CharacterId}'의 가중치가 달라졌습니다.");
                Assert.IsTrue(row.Enabled);
            }
        }

        [Test]
        public void LiveCsv_InnAccessPointsAtBuildingOneAndInnNormal()
        {
            TableDataSnapshot snapshot = Live.Snapshot;
            Assert.IsNotNull(snapshot);

            Assert.IsTrue(snapshot.RecruitmentAccessesById.TryGetValue(LiveAccessId, out RecruitmentAccessRow access),
                $"'{LiveAccessId}' 창구가 표에 없습니다.");

            Assert.AreEqual(RecruitmentSourceTypes.Building, access.SourceType);
            Assert.AreEqual(LiveSourceId, access.SourceId);
            Assert.AreEqual(LiveRecruitmentTypeId, access.RecruitmentTypeId);
            Assert.AreEqual(LiveArrivalIntervalSeconds, access.ArrivalIntervalSeconds);
            Assert.AreEqual(LiveConsumeAmount, access.ConsumeAmount);
            Assert.AreEqual(LiveAccessDisplayOrder, access.DisplayOrder);
            Assert.IsTrue(access.Enabled);

            // 창구가 가리키는 대상과 모집이 모두 실재해야 조회가 성립한다.
            Assert.IsTrue(snapshot.BuildingsById.ContainsKey(access.SourceId));
            Assert.IsTrue(snapshot.RecruitmentTypesById.ContainsKey(access.RecruitmentTypeId));
        }

        [Test]
        public void LiveCsv_OnlyCatKnightStartsOwned()
        {
            TableDataSnapshot snapshot = Live.Snapshot;
            Assert.IsNotNull(snapshot);

            var owned = new List<string>();
            foreach (CharacterRow row in snapshot.Characters)
            {
                if (row.InitiallyOwned) owned.Add(row.Id);
            }

            CollectionAssert.AreEqual(new[] { "CatKnight" }, owned,
                "새 게임에서 처음부터 가지고 시작하는 캐릭터는 고양이기사 하나여야 한다 - " +
                "나머지는 모집으로 얻는다.");
        }

        // ---- 임포터가 쓰는 칸이 실제로 있는지 ----

        [Test]
        public void SerializedLayout_MatchesWhatTheImporterWrites()
        {
            MethodInfo verify = typeof(TableDataRebuilder).GetMethod(
                "VerifySerializedLayout", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(verify,
                "TableDataRebuilder.VerifySerializedLayout을 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");

            var log = new TableDataDiagnosticLog();
            var ok = (bool)verify.Invoke(null, new object[] { log });

            Assert.IsTrue(ok, "임포터가 쓰려는 직렬화 칸이 런타임 클래스와 어긋납니다: " +
                              string.Join("\n", log.Entries));
        }
    }
}
