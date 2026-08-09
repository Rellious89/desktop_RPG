using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Skill;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// CharacterSkill.csv 파이프라인 시험. 관계 표는 스스로 무엇도 정의하지 않고 두 표를 잇기만 하므로,
    /// 시험이 확인하는 것도 대부분 <b>참조</b>다 - 실재하는가, 활성인가, 같은 짝이 두 번 오지 않는가.
    ///
    /// 시험이 쓰는 캐릭터/스킬 행은 전부 <b>가짜</b>다(메모리 위의 스냅샷에 직접 넣는다) - 실제
    /// Character.csv나 Skill.csv를 읽지 않으므로 프로덕션 데이터가 바뀌어도 이 시험은 흔들리지 않는다.
    /// </summary>
    public sealed class CharacterSkillTableTests
    {
        private const string File = TableDataPaths.CharacterSkillCsvFileName;

        private static readonly MethodInfo ValidateCharacterSkillsMethod =
            typeof(TableDataValidator).GetMethod(
                "ValidateCharacterSkills", BindingFlags.NonPublic | BindingFlags.Static);

        private static TableDataValidationResult liveResult;

        [SetUp]
        public void SetUpFixture()
        {
            Assert.IsNotNull(ValidateCharacterSkillsMethod,
                "TableDataValidator.ValidateCharacterSkills를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");
        }

        // ---- 스키마와 경로 ----

        [Test]
        public void Schema_IsExactlyTheAgreedColumns()
        {
            CollectionAssert.AreEqual(
                new[] { "character_id", "skill_id", "required_character_level", "display_order", "enabled", "memo" },
                TableDataColumns.CharacterSkill,
                "CharacterSkill.csv의 필수 컬럼과 순서가 약속과 달라졌습니다.");
        }

        [Test]
        public void Schema_DoesNotIncludeTheReferenceOnlyColumns()
        {
            CollectionAssert.DoesNotContain(TableDataColumns.CharacterSkill, "$character_name");
            CollectionAssert.DoesNotContain(TableDataColumns.CharacterSkill, "$skill_name");
        }

        [Test]
        public void Schema_SharesTheIdColumnNamesWithTheTablesItPointsAt()
        {
            CollectionAssert.Contains(TableDataColumns.CharacterSkill, TableDataColumns.CharacterId);
            CollectionAssert.Contains(TableDataColumns.Character, TableDataColumns.CharacterId);
            CollectionAssert.Contains(TableDataColumns.CharacterSkill, TableDataColumns.SkillId);
            CollectionAssert.Contains(TableDataColumns.Skill, TableDataColumns.SkillId);
        }

        [Test]
        public void Paths_AreTheAgreedLocations()
        {
            Assert.AreEqual("Assets/TableData/Game/CharacterSkill.csv", TableDataPaths.CharacterSkillCsvPath);
            Assert.AreEqual("Assets/Generated/TableData/CharacterSkill", TableDataPaths.CharacterSkillOutputFolder);
            Assert.AreEqual("Assets/Generated/TableData/CharacterSkill/CharacterSkillCatalog.asset",
                TableDataPaths.CharacterSkillCatalogAssetPath);
            Assert.AreEqual("Assets/Generated/TableData/CharacterSkill/CharacterSkill_CatKnight__fire_bolt.asset",
                TableDataPaths.CharacterSkillAssetPath(
                    CharacterSkillDefinition.BuildPairId("CatKnight", "fire_bolt")));
        }

        [Test]
        public void PairKey_CannotBeAmbiguous()
        {
            // 구분자가 밑줄 하나였다면 'a_b'+'c'와 'a'+'b_c'가 같은 키가 된다.
            Assert.AreEqual("__", CharacterSkillDefinition.PairIdSeparator);
            Assert.AreNotEqual(
                CharacterSkillDefinition.BuildPairId("a_b", "c"),
                CharacterSkillDefinition.BuildPairId("a", "b_c"));

            // 밑줄 두 개는 어느 id 형식으로도 만들 수 없다.
            Assert.IsFalse(TableDataFieldRules.IsValidId("a__b"));
            Assert.IsFalse(TableDataFieldRules.IsValidCharacterId("a__b"));
        }

        // ---- 빈 표 ----

        [Test]
        public void EmptyTable_IsValid()
        {
            TableDataSnapshot snapshot = Snapshot();
            TableDataDiagnosticLog log = Validate(snapshot);

            Assert.AreEqual(0, log.Entries.Count, "행이 없는 표는 알릴 것이 없다: " + Describe(log));
            Assert.AreEqual(0, snapshot.CharacterSkills.Count);
        }

        // ---- 참조 ----

        [Test]
        public void ValidRow_EntersSnapshotWithItsAuthoredValues()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", true), ("fire_bolt", true));
            TableDataDiagnosticLog log = Validate(snapshot, Row("CatKnight", "fire_bolt", level: "5", displayOrder: "10"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, snapshot.CharacterSkills.Count);

            CharacterSkillRow row = snapshot.CharacterSkills[0];
            Assert.AreEqual("CatKnight", row.CharacterId);
            Assert.AreEqual("fire_bolt", row.SkillId);
            Assert.AreEqual("CatKnight__fire_bolt", row.PairId);
            Assert.AreEqual(5, row.RequiredCharacterLevel);
            Assert.AreEqual(10, row.DisplayOrder);
            Assert.IsTrue(row.Enabled);
            Assert.AreSame(row, snapshot.CharacterSkillsByPairId["CatKnight__fire_bolt"]);
        }

        [Test]
        public void MissingCharacterReference_IsAnError()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", true), ("fire_bolt", true));
            TableDataDiagnosticLog log = Validate(snapshot, Row("CatMage", "fire_bolt"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CharacterId), Describe(log));
        }

        [Test]
        public void MissingSkillReference_IsAnError()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", true), ("fire_bolt", true));
            TableDataDiagnosticLog log = Validate(snapshot, Row("CatKnight", "ice_bolt"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.SkillId), Describe(log));
        }

        [Test]
        public void ReferencesAreCaseSensitive()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", true), ("fire_bolt", true));

            // 'catknight'는 그 자체로는 올바른 id 형식(snake_case)이지만 표에 없는 값이다 - 조회는
            // Ordinal 완전 일치라 'CatKnight'를 대신 찾아 주지 않는다.
            TableDataDiagnosticLog log = Validate(snapshot, Row("catknight", "fire_bolt"));
            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CharacterId), Describe(log));
            Assert.IsFalse(snapshot.CharactersById.ContainsKey("catknight"));
        }

        [Test]
        public void MalformedIdsAreReportedOnceAndTheReferenceIsNotChecked()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", true), ("fire_bolt", true));
            TableDataDiagnosticLog log = Validate(snapshot, Row("IceMage", "FireBolt"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CharacterId), Describe(log));
            Assert.AreEqual(1, CountErrors(log, TableDataColumns.SkillId), Describe(log));
            Assert.AreEqual(0, snapshot.CharacterSkills.Count);
        }

        [Test]
        public void DuplicatePair_IsAnErrorAndTheFirstRowWins()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", true), ("fire_bolt", true));
            TableDataDiagnosticLog log = Validate(snapshot,
                Row("CatKnight", "fire_bolt", level: "1"),
                Row("CatKnight", "fire_bolt", level: "9"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.SkillId), Describe(log));
            Assert.AreEqual(1, snapshot.CharacterSkills.Count);
            Assert.AreEqual(1, snapshot.CharacterSkillsByPairId["CatKnight__fire_bolt"].RequiredCharacterLevel,
                "먼저 나온 행이 남아야 한다.");
        }

        [Test]
        public void SameCharacterWithDifferentSkills_IsNotADuplicate()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", true), ("fire_bolt", true), ("ice_bolt", true));
            TableDataDiagnosticLog log = Validate(snapshot,
                Row("CatKnight", "fire_bolt", displayOrder: "10"),
                Row("CatKnight", "ice_bolt", displayOrder: "20"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(2, snapshot.CharacterSkills.Count);
        }

        // ---- 활성 규칙 ----

        [Test]
        public void EnabledRelation_CannotPointAtADisabledCharacter()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", false), ("fire_bolt", true));
            TableDataDiagnosticLog log = Validate(snapshot, Row("CatKnight", "fire_bolt", enabled: "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CharacterId), Describe(log));
        }

        [Test]
        public void EnabledRelation_CannotPointAtADisabledSkill()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", true), ("fire_bolt", false));
            TableDataDiagnosticLog log = Validate(snapshot, Row("CatKnight", "fire_bolt", enabled: "1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.SkillId), Describe(log));
        }

        [Test]
        public void DisabledRelation_MayPointAtDisabledRows()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", false), ("fire_bolt", false));
            TableDataDiagnosticLog log = Validate(snapshot, Row("CatKnight", "fire_bolt", enabled: "0"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, snapshot.CharacterSkills.Count);
            Assert.IsFalse(snapshot.CharacterSkills[0].Enabled);
        }

        [Test]
        public void DisabledRelation_StillCannotPointAtRowsThatDoNotExist()
        {
            // 잘못된 참조를 통과시키면 다시 켜는 순간 조용히 깨진다.
            TableDataSnapshot snapshot = Snapshot(("CatKnight", true), ("fire_bolt", true));
            TableDataDiagnosticLog log = Validate(snapshot, Row("CatKnight", "ice_bolt", enabled: "0"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.SkillId), Describe(log));
        }

        [Test]
        public void EnabledMustBeExactlyOneOrZero()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", true), ("fire_bolt", true));
            TableDataDiagnosticLog log = Validate(snapshot, Row("CatKnight", "fire_bolt", enabled: "yes"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.Enabled), Describe(log));
        }

        // ---- 필요 레벨 ----

        [Test]
        public void RequiredLevel_MustBeAtLeastOne()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", true), ("fire_bolt", true));

            TableDataDiagnosticLog zero = Validate(snapshot, Row("CatKnight", "fire_bolt", level: "0"));
            Assert.AreEqual(1, CountErrors(zero, TableDataColumns.RequiredCharacterLevel), Describe(zero));

            TableDataSnapshot other = Snapshot(("CatKnight", true), ("fire_bolt", true));
            TableDataDiagnosticLog blank = Validate(other, Row("CatKnight", "fire_bolt", level: ""));
            Assert.AreEqual(1, CountErrors(blank, TableDataColumns.RequiredCharacterLevel), Describe(blank));
        }

        [Test]
        public void RequiredLevel_HasNoUpperBound()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", true), ("fire_bolt", true));
            TableDataDiagnosticLog log = Validate(snapshot, Row("CatKnight", "fire_bolt", level: "999999"));

            Assert.AreEqual(0, log.ErrorCount, "레벨 상한을 정하는 것은 이 표의 일이 아니다: " + Describe(log));
            Assert.AreEqual(999999, snapshot.CharacterSkills[0].RequiredCharacterLevel);
        }

        [Test]
        public void DisplayOrder_MustNotBeNegative()
        {
            TableDataSnapshot snapshot = Snapshot(("CatKnight", true), ("fire_bolt", true));
            TableDataDiagnosticLog log = Validate(snapshot, Row("CatKnight", "fire_bolt", displayOrder: "-1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.DisplayOrder), Describe(log));
        }

        // ---- 실제 프로젝트 데이터(읽기 전용) ----

        [Test]
        public void LiveCsv_HasNoProductionRowsAndNoDiagnostics()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot, "여덟 표가 모두 읽혀야 스냅샷이 만들어진다: " + Live().Summary);

            Assert.AreEqual(0, snapshot.CharacterSkills.Count,
                "지금 단계의 CharacterSkill.csv에는 실제 관계 행이 없어야 한다.");

            var messages = new List<string>();
            foreach (TableDataDiagnostic diagnostic in Live().Diagnostics)
            {
                if (string.Equals(diagnostic.File, File, StringComparison.Ordinal)) messages.Add(diagnostic.ToString());
            }

            Assert.AreEqual(0, messages.Count,
                "헤더만 있는 표는 오류도 경고도 남기지 않아야 한다:\n" + string.Join("\n", messages));
        }

        // ---- 도우미 ----

        private static TableDataValidationResult Live()
        {
            return liveResult ?? (liveResult = TableDataValidator.Validate());
        }

        /// <summary>
        /// 가짜 캐릭터/스킬 행만 담은 스냅샷. id가 소문자 snake_case면 스킬로, 그 밖이면 캐릭터로
        /// 넣는다 - 시험이 쓰는 id 두 종류(legacy PascalCase 캐릭터 / snake_case 스킬)를 그대로
        /// 구분하기에 충분하다.
        /// </summary>
        private static TableDataSnapshot Snapshot(params (string Id, bool Enabled)[] rows)
        {
            var snapshot = new TableDataSnapshot();
            int line = 2;

            foreach ((string id, bool enabled) in rows)
            {
                if (TableDataFieldRules.IsValidId(id))
                {
                    var skill = new SkillRow { Line = line++, Id = id, Enabled = enabled };
                    snapshot.Skills.Add(skill);
                    snapshot.SkillsById[id] = skill;
                    continue;
                }

                var character = new CharacterRow { Line = line++, Id = id, Enabled = enabled };
                snapshot.Characters.Add(character);
                snapshot.CharactersById[id] = character;
            }

            return snapshot;
        }

        private static TableDataDiagnosticLog Validate(TableDataSnapshot snapshot, params string[][] rows)
        {
            var records = new List<CsvRecord>();
            for (int i = 0; i < rows.Length; i++) records.Add(new CsvRecord(i + 2, rows[i]));

            var table = new CsvTable(File, TableDataColumns.CharacterSkill, records);
            var log = new TableDataDiagnosticLog();

            ValidateCharacterSkillsMethod.Invoke(null, new object[] { table, snapshot, log });
            return log;
        }

        /// <summary>컬럼 순서는 <see cref="TableDataColumns.CharacterSkill"/>과 같다.</summary>
        private static string[] Row(
            string characterId,
            string skillId,
            string level = "1",
            string displayOrder = "10",
            string enabled = "1")
        {
            return new[] { characterId, skillId, level, displayOrder, enabled, string.Empty };
        }

        private static int CountErrors(TableDataDiagnosticLog log, string column)
        {
            int count = 0;
            foreach (TableDataDiagnostic diagnostic in log.Entries)
            {
                if (diagnostic.Severity == TableDataSeverity.Error
                    && string.Equals(diagnostic.Column, column, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static string Describe(TableDataDiagnosticLog log)
        {
            var lines = new List<string>();
            foreach (TableDataDiagnostic diagnostic in log.Entries) lines.Add(diagnostic.ToString());
            return lines.Count == 0 ? "(진단 없음)" : "\n" + string.Join("\n", lines);
        }
    }
}
