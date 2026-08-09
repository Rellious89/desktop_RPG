using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// Skill.csv 파이프라인 시험. <b>파일을 쓰지도 에셋을 만들지도 않는다</b>(다른 표 시험과 같은 규칙).
    ///
    /// 이 표에서 가장 중요하게 못 박는 것은 <b>"비어 있는 표가 정상"</b>이라는 점이다 - 지금 프로덕션
    /// Skill.csv에는 행이 하나도 없고, 그 상태가 오류로도 경고로도 보고되면 안 된다.
    /// </summary>
    public sealed class SkillTableTests
    {
        private const string File = TableDataPaths.SkillCsvFileName;

        /// <summary>실제 프로젝트에 있는 카테고리와 숫자 키. 스킬 전용 카테고리는 아직 만들지 않았으므로
        /// 시험은 기존 카테고리를 빌려 쓴다 - 참조가 해석되는지만 보면 되는 자리다.</summary>
        private const string Category = "6";
        private const string Key = "1";

        private static readonly MethodInfo ValidateSkillsMethod =
            typeof(TableDataValidator).GetMethod("ValidateSkills", BindingFlags.NonPublic | BindingFlags.Static);

        private static TableDataValidationResult liveResult;

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUpFixture()
        {
            Assert.IsNotNull(ValidateSkillsMethod,
                "TableDataValidator.ValidateSkills를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object asset in created)
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        // ---- 스키마와 경로 ----

        [Test]
        public void Schema_IsExactlyTheAgreedColumns()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "skill_id", "name_category", "name_key", "description_category", "description_key",
                    "icon_key", "skill_type", "behavior_key", "display_order", "enabled", "memo",
                },
                TableDataColumns.Skill,
                "Skill.csv의 필수 컬럼과 순서가 약속과 달라졌습니다.");
        }

        [Test]
        public void Schema_DoesNotIncludeTheReferenceOnlyColumns()
        {
            CollectionAssert.DoesNotContain(TableDataColumns.Skill, "$skill_name");
            CollectionAssert.DoesNotContain(TableDataColumns.Skill, "$skill_description");
            Assert.IsTrue(TableDataCsvReader.IsReferenceOnlyColumn("$skill_name"));
            Assert.IsTrue(TableDataCsvReader.IsReferenceOnlyColumn("$skill_description"));
        }

        [Test]
        public void Paths_AreTheAgreedLocations()
        {
            Assert.AreEqual("Assets/TableData/Game/Skill.csv", TableDataPaths.SkillCsvPath);
            Assert.AreEqual("Assets/Generated/TableData/Skill", TableDataPaths.SkillOutputFolder);
            Assert.AreEqual("Assets/Generated/TableData/Skill/SkillCatalog.asset", TableDataPaths.SkillCatalogAssetPath);
            Assert.AreEqual("Assets/Generated/TableData/Skill/Skill_fire_bolt.asset",
                TableDataPaths.SkillAssetPath("fire_bolt"));
        }

        // ---- 빈 표 ----

        [Test]
        public void EmptyTable_IsValid()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log);

            Assert.AreEqual(0, log.Entries.Count, "행이 없는 표는 알릴 것이 없다: " + Describe(log));
            Assert.AreEqual(0, snapshot.Skills.Count);
        }

        // ---- id 규칙 ----

        [Test]
        public void StandardIdIsRequired_AndTheCharacterExceptionDoesNotApplyHere()
        {
            // 캐릭터 표의 legacy 예외는 그 표 전용이다 - 스킬은 새로 만드는 데이터라 표준 형식만 쓴다.
            Validate(out TableDataDiagnosticLog pascal, Row("FireBolt"));
            Assert.AreEqual(1, CountErrors(pascal, TableDataColumns.SkillId), Describe(pascal));

            Validate(out TableDataDiagnosticLog legacy, Row("CatKnight"));
            Assert.AreEqual(1, CountErrors(legacy, TableDataColumns.SkillId),
                "Character.csv의 예외 목록이 다른 표로 새면 안 된다.");

            TableDataSnapshot ok = Validate(out TableDataDiagnosticLog fine, Row("fire_bolt"));
            Assert.AreEqual(0, fine.ErrorCount, Describe(fine));
            Assert.AreEqual("fire_bolt", ok.Skills[0].Id);
        }

        [Test]
        public void BlankId_IsAnErrorAndTheRowIsDropped()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log, Row(""));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.SkillId), Describe(log));
            Assert.AreEqual(0, snapshot.Skills.Count);
        }

        [Test]
        public void PaddedId_IsAFormatErrorAndIsNeverTrimmed()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log, Row("  fire_bolt  "));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.SkillId), Describe(log));
            Assert.IsFalse(snapshot.SkillsById.ContainsKey("fire_bolt"));
        }

        [Test]
        public void DuplicateId_IsAnErrorAndTheFirstRowWins()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log,
                Row("fire_bolt", displayOrder: "10"),
                Row("fire_bolt", displayOrder: "20"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.SkillId), Describe(log));
            Assert.AreEqual(1, snapshot.Skills.Count);
            Assert.AreEqual(10, snapshot.SkillsById["fire_bolt"].DisplayOrder);
        }

        // ---- 이름과 설명 ----

        [Test]
        public void ValidRow_EntersSnapshotWithItsAuthoredValues()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log,
                Row("fire_bolt", displayOrder: "10", skillType: "attack", behaviorKey: "projectile_single"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));

            SkillRow row = snapshot.Skills[0];
            Assert.AreEqual("fire_bolt", row.Id);
            Assert.AreEqual(10, row.DisplayOrder);
            Assert.IsTrue(row.Enabled);
            Assert.IsTrue(row.Name.Resolved);
            Assert.AreEqual("attack", row.SkillType);
            Assert.AreEqual("projectile_single", row.BehaviorKey);
        }

        [Test]
        public void EnabledRow_RequiresBothNameCells()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log,
                Row("fire_bolt", category: "", key: ""));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.NameCategory), Describe(log));
            Assert.IsFalse(snapshot.Skills[0].Name.Resolved);
        }

        [Test]
        public void UnknownNameReference_IsAnError()
        {
            Validate(out TableDataDiagnosticLog key, Row("fire_bolt", key: "999999"));
            Assert.AreEqual(1, CountErrors(key, TableDataColumns.NameKey), Describe(key));

            Validate(out TableDataDiagnosticLog category, Row("fire_bolt", category: "999"));
            Assert.AreEqual(1, CountErrors(category, TableDataColumns.NameCategory), Describe(category));
        }

        [Test]
        public void BlankDescription_IsNeitherErrorNorWarning()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log, Row("fire_bolt"));

            Assert.AreEqual(0, CountErrors(log, TableDataColumns.DescriptionCategory), Describe(log));
            Assert.AreEqual(0, CountWarnings(log, TableDataColumns.DescriptionCategory), Describe(log));
            Assert.AreEqual(0, CountWarnings(log, TableDataColumns.DescriptionKey), Describe(log));
            Assert.IsFalse(snapshot.Skills[0].Description.Resolved);
        }

        [Test]
        public void HalfFilledDescription_IsAnError()
        {
            Validate(out TableDataDiagnosticLog onlyCategory,
                Row("fire_bolt", descriptionCategory: Category, descriptionKey: ""));
            Assert.AreEqual(1, CountErrors(onlyCategory, TableDataColumns.DescriptionKey), Describe(onlyCategory));

            Validate(out TableDataDiagnosticLog onlyKey,
                Row("fire_bolt", descriptionCategory: "", descriptionKey: Key));
            Assert.AreEqual(1, CountErrors(onlyKey, TableDataColumns.DescriptionCategory), Describe(onlyKey));
        }

        [Test]
        public void CompleteDescription_IsResolved()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log,
                Row("fire_bolt", descriptionCategory: Category, descriptionKey: Key));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.IsTrue(snapshot.Skills[0].Description.Resolved);
        }

        [Test]
        public void UnknownDescriptionReference_IsAnError()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("fire_bolt", descriptionCategory: Category, descriptionKey: "999999"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.DescriptionKey), Describe(log));
        }

        // ---- 아이콘 ----

        [Test]
        public void BlankIcon_IsAWarningAndLeavesItEmpty()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log, Row("fire_bolt"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, CountWarnings(log, TableDataColumns.IconKey), Describe(log));
            Assert.IsNull(snapshot.Skills[0].Icon);
        }

        [Test]
        public void NamedIcon_ThatIsNotFound_IsAnError()
        {
            TableDataSnapshot snapshot = Validate(
                SpriteIndex("sp_skill_fire"), out TableDataDiagnosticLog log,
                Row("fire_bolt", iconKey: "sp_no_such_icon"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.IconKey), Describe(log));
            Assert.IsNull(snapshot.Skills[0].Icon);
        }

        [Test]
        public void NamedIcon_FoundExactlyOnce_IsAssigned()
        {
            Sprite only = NewSprite();
            TableDataSnapshot snapshot = Validate(
                SpriteIndex("sp_skill_fire", only), out TableDataDiagnosticLog log,
                Row("fire_bolt", iconKey: "sp_skill_fire"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreSame(only, snapshot.Skills[0].Icon);
        }

        [Test]
        public void AmbiguousIcon_IsAnError()
        {
            TableDataSnapshot snapshot = Validate(
                SpriteIndex("sp_skill_fire", NewSprite(), NewSprite()), out TableDataDiagnosticLog log,
                Row("fire_bolt", iconKey: "sp_skill_fire"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.IconKey), Describe(log));
            Assert.IsNull(snapshot.Skills[0].Icon);
        }

        // ---- 분류 키 / 동작 키 ----

        [Test]
        public void BlankTypeAndBehavior_AreAccepted()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log,
                Row("fire_bolt", skillType: "", behaviorKey: ""));

            Assert.AreEqual(0, CountErrors(log, TableDataColumns.SkillType), Describe(log));
            Assert.AreEqual(0, CountErrors(log, TableDataColumns.BehaviorKey), Describe(log));
            Assert.AreEqual(string.Empty, snapshot.Skills[0].SkillType);
            Assert.AreEqual(string.Empty, snapshot.Skills[0].BehaviorKey);
        }

        [Test]
        public void NonBlankTypeAndBehavior_MustBeLowercaseKeys()
        {
            Validate(out TableDataDiagnosticLog upper, Row("fire_bolt", skillType: "Attack"));
            Assert.AreEqual(1, CountErrors(upper, TableDataColumns.SkillType), Describe(upper));

            Validate(out TableDataDiagnosticLog numeric, Row("fire_bolt", behaviorKey: "1"));
            Assert.AreEqual(1, CountErrors(numeric, TableDataColumns.BehaviorKey),
                "숫자만으로 된 키는 나중에 무엇으로 읽어야 할지 알 수 없다.");

            Validate(out TableDataDiagnosticLog padded, Row("fire_bolt", skillType: " attack"));
            Assert.AreEqual(1, CountErrors(padded, TableDataColumns.SkillType), Describe(padded));

            Validate(out TableDataDiagnosticLog doubleUnderscore, Row("fire_bolt", behaviorKey: "a__b"));
            Assert.AreEqual(1, CountErrors(doubleUnderscore, TableDataColumns.BehaviorKey), Describe(doubleUnderscore));
        }

        [Test]
        public void LowercaseKeyRule_IsStricterThanTheIdRule()
        {
            // 두 규칙이 다르다는 것 자체를 못 박는다 - 숫자 id는 있어도 숫자 키는 없다.
            Assert.IsTrue(TableDataFieldRules.IsValidId("101"));
            Assert.IsFalse(TableDataFieldRules.IsValidLowercaseKey("101"));
            Assert.IsTrue(TableDataFieldRules.IsValidLowercaseKey("projectile_single"));
        }

        // ---- 순서와 활성 ----

        [Test]
        public void EnabledMustBeExactlyOneOrZero()
        {
            Validate(out TableDataDiagnosticLog log, Row("fire_bolt", enabled: "TRUE"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.Enabled), Describe(log));
        }

        [Test]
        public void DisabledRow_StaysInTheSnapshot()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log, Row("fire_bolt", enabled: "0"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, snapshot.Skills.Count);
            Assert.IsFalse(snapshot.Skills[0].Enabled);
        }

        [Test]
        public void DisplayOrder_MustNotBeNegative()
        {
            Validate(out TableDataDiagnosticLog log, Row("fire_bolt", displayOrder: "-1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.DisplayOrder), Describe(log));
        }

        [Test]
        public void DuplicateDisplayOrder_IsAWarningNotAnError()
        {
            Validate(out TableDataDiagnosticLog log,
                Row("fire_bolt", displayOrder: "10"),
                Row("ice_bolt", displayOrder: "10"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, CountWarnings(log, TableDataColumns.DisplayOrder), Describe(log));
        }

        // ---- 실제 프로젝트 데이터(읽기 전용) ----

        [Test]
        public void LiveCsv_HasNoProductionRowsAndNoDiagnostics()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot, "여덟 표가 모두 읽혀야 스냅샷이 만들어진다: " + Live().Summary);

            Assert.AreEqual(0, snapshot.Skills.Count, "지금 단계의 Skill.csv에는 실제 스킬 행이 없어야 한다.");

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

        private static TableDataSnapshot Validate(out TableDataDiagnosticLog log, params string[][] rows)
        {
            return Validate(new TableDataAssetIndex(), out log, rows);
        }

        private static TableDataSnapshot Validate(
            TableDataAssetIndex assets, out TableDataDiagnosticLog log, params string[][] rows)
        {
            var records = new List<CsvRecord>();
            for (int i = 0; i < rows.Length; i++) records.Add(new CsvRecord(i + 2, rows[i]));

            var table = new CsvTable(File, TableDataColumns.Skill, records);
            var snapshot = new TableDataSnapshot();
            log = new TableDataDiagnosticLog();

            ValidateSkillsMethod.Invoke(null, new object[] { table, snapshot, assets, log });
            return snapshot;
        }

        /// <summary>컬럼 순서는 <see cref="TableDataColumns.Skill"/>과 같다.</summary>
        private static string[] Row(
            string id,
            string category = Category,
            string key = Key,
            string descriptionCategory = "",
            string descriptionKey = "",
            string iconKey = "",
            string skillType = "",
            string behaviorKey = "",
            string displayOrder = "10",
            string enabled = "1")
        {
            return new[]
            {
                id, category, key, descriptionCategory, descriptionKey, iconKey, skillType, behaviorKey,
                displayOrder, enabled, string.Empty,
            };
        }

        /// <summary>Sprite 조회 결과를 심어 둔 인덱스. 디스크에는 아무것도 만들지 않는다.</summary>
        private static TableDataAssetIndex SpriteIndex(string name, params Sprite[] sprites)
        {
            var assets = new TableDataAssetIndex();
            Type type = typeof(TableDataAssetIndex);
            const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

            FieldInfo builtField = type.GetField("spriteNamesBuilt", Instance);
            FieldInfo cacheField = type.GetField("resolvedSprites", Instance);

            Assert.IsNotNull(builtField, "TableDataAssetIndex.spriteNamesBuilt를 찾지 못했습니다.");
            Assert.IsNotNull(cacheField, "TableDataAssetIndex.resolvedSprites를 찾지 못했습니다.");

            builtField.SetValue(assets, true);
            ((Dictionary<string, List<Sprite>>)cacheField.GetValue(assets))[name] = new List<Sprite>(sprites);

            return assets;
        }

        private Sprite NewSprite()
        {
            var texture = new Texture2D(4, 4);
            created.Add(texture);

            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            created.Add(sprite);
            return sprite;
        }

        private static int CountErrors(TableDataDiagnosticLog log, string column)
        {
            return Count(log, TableDataSeverity.Error, column);
        }

        private static int CountWarnings(TableDataDiagnosticLog log, string column)
        {
            return Count(log, TableDataSeverity.Warning, column);
        }

        private static int Count(TableDataDiagnosticLog log, TableDataSeverity severity, string column)
        {
            int count = 0;
            foreach (TableDataDiagnostic diagnostic in log.Entries)
            {
                if (diagnostic.Severity == severity
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
