using System;
using System.Collections.Generic;
using System.Reflection;
using Character;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// Character.csv 파이프라인 시험. <b>파일을 쓰지도 에셋을 만들지도 않는다</b> - 행 검증은 메모리에서
    /// 만든 <see cref="CsvTable"/>로 돌리고, 실제 데이터 확인은 읽기 전용인
    /// <see cref="TableDataValidator.Validate"/>만 쓴다. Rebuild는 프로젝트를 바꾸므로 여기서 부르지
    /// 않는다(<see cref="CurrencyTableTests"/>와 같은 규칙이다).
    ///
    /// 시험이 쓰는 모션 프로필과 Sprite는 <b>메모리에만</b> 존재하며 TearDown에서 지운다 - Assets 아래에
    /// 시험용 에셋을 만들면 그것이 곧 사용자 자산을 고치는 일이 된다.
    /// </summary>
    public sealed class CharacterTableTests
    {
        private const string File = TableDataPaths.CharacterCsvFileName;

        /// <summary>실제 프로젝트에 있는 캐릭터 카테고리(06_Character)와 그 첫 숫자 키.</summary>
        private const string Category = "6";
        private const string Key = "1";

        private static readonly MethodInfo ValidateCharactersMethod =
            typeof(TableDataValidator).GetMethod(
                "ValidateCharacters", BindingFlags.NonPublic | BindingFlags.Static);

        private static TableDataValidationResult liveResult;

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUpFixture()
        {
            Assert.IsNotNull(ValidateCharactersMethod,
                "TableDataValidator.ValidateCharacters를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");
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
                    "character_id", "name_category", "name_key", "motion_profile_key", "portrait_key",
                    "base_max_health", "max_stamina", "display_order", "enabled", "memo",
                },
                TableDataColumns.Character,
                "Character.csv의 필수 컬럼과 순서가 약속과 달라졌습니다.");
        }

        [Test]
        public void Schema_DoesNotIncludeTheReferenceOnlyNameColumn()
        {
            CollectionAssert.DoesNotContain(TableDataColumns.Character, "$character_name",
                "$character_name은 작업자용 참조 컬럼이라 필수 컬럼이 되면 안 된다.");
            Assert.IsTrue(TableDataCsvReader.IsReferenceOnlyColumn("$character_name"));
        }

        [Test]
        public void Schema_SharesTheMotionProfileColumnNameWithMonsterCsv()
        {
            // 두 표가 같은 상수를 가리켜야 "모션 프로필 이름 칸"이라는 뜻이 하나로 유지된다.
            CollectionAssert.Contains(TableDataColumns.Character, TableDataColumns.MotionProfileKey);
            CollectionAssert.Contains(TableDataColumns.Monster, TableDataColumns.MotionProfileKey);
        }

        [Test]
        public void Paths_AreTheAgreedLocations()
        {
            Assert.AreEqual("Assets/TableData/Game/Character.csv", TableDataPaths.CharacterCsvPath);
            Assert.AreEqual("Assets/Generated/TableData/Character", TableDataPaths.CharacterOutputFolder);
            Assert.AreEqual("Assets/Generated/TableData/Character/CharacterCatalog.asset",
                TableDataPaths.CharacterCatalogAssetPath);
        }

        [Test]
        public void AssetPath_UsesTheRawIdWithoutNormalizing()
        {
            Assert.AreEqual("Assets/Generated/TableData/Character/Character_CatKnight.asset",
                TableDataPaths.CharacterAssetPath("CatKnight"));
            Assert.AreEqual("Assets/Generated/TableData/Character/Character_ice_mage.asset",
                TableDataPaths.CharacterAssetPath("ice_mage"));
        }

        // ---- id 규칙 ----

        [Test]
        public void LegacyPascalCaseIds_AreAcceptedOnlyForTheSixKnownCharacters()
        {
            foreach (string id in TableDataFieldRules.LegacyCharacterIds)
            {
                Assert.IsTrue(TableDataFieldRules.IsValidCharacterId(id), $"'{id}'는 기존 캐릭터 id라 허용해야 한다.");
            }

            CollectionAssert.AreEquivalent(
                new[] { "Barbarian", "CatKnight", "CatMage", "ElfArcher", "ElfGuardian", "RabbitHealer" },
                TableDataFieldRules.LegacyCharacterIds,
                "예외 목록이 늘어나면 안 된다 - 새 캐릭터는 표준 ID 형식을 쓴다.");
        }

        [Test]
        public void OtherPascalCaseIds_AreRejected()
        {
            // 프로젝트에 실제로 있는 테스트 캐릭터들이지만 예외 목록에는 없다.
            Assert.IsFalse(TableDataFieldRules.IsValidCharacterId("IceMage"));
            Assert.IsFalse(TableDataFieldRules.IsValidCharacterId("Leopard"));
            Assert.IsFalse(TableDataFieldRules.IsValidCharacterId("NewHero"));
        }

        [Test]
        public void CharacterIdComparisonIsOrdinal_AndNeverNormalized()
        {
            Assert.IsFalse(TableDataFieldRules.IsValidCharacterId("CATKNIGHT"));
            Assert.IsFalse(TableDataFieldRules.IsValidCharacterId(" CatKnight"));
            Assert.IsFalse(TableDataFieldRules.IsValidCharacterId("CatKnight "));

            // 'catknight'는 그 자체로 올바른 표준 id(snake_case 한 낱말)라 형식 검사는 통과한다.
            // 여기서 확인하려는 것은 그것이 <b>'CatKnight'와 같은 값이 되지 않는다</b>는 것이다 -
            // 대소문자를 맞춰 하나로 합치는 경로가 어디에도 없어야, 두 id가 같은 저장 키를 다투지 않는다.
            Assert.IsTrue(TableDataFieldRules.IsValidCharacterId("catknight"));
            Assert.AreNotEqual("CatKnight", "catknight");

            TableDataSnapshot snapshot = Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", displayOrder: "10"),
                Row("catknight", motionKey: "P", displayOrder: "20"));

            Assert.AreEqual(0, log.ErrorCount,
                "대소문자만 다른 두 id는 서로 다른 캐릭터이므로 중복이 아니다: " + Describe(log));
            Assert.AreEqual(2, snapshot.Characters.Count);
            Assert.AreEqual(10, snapshot.CharactersById["CatKnight"].DisplayOrder);
            Assert.AreEqual(20, snapshot.CharactersById["catknight"].DisplayOrder);
        }

        [Test]
        public void StandardIdFormats_AreStillAcceptedHere()
        {
            Assert.IsTrue(TableDataFieldRules.IsValidCharacterId("ice_mage"));
            Assert.IsTrue(TableDataFieldRules.IsValidCharacterId("101"));
        }

        [Test]
        public void TheGlobalIdRuleIsNotLoosenedByTheCharacterException()
        {
            // 예외는 Character 전용 검사에만 있다 - 전역 규칙은 한 글자도 헐거워지지 않았다.
            foreach (string id in TableDataFieldRules.LegacyCharacterIds)
            {
                Assert.IsFalse(TableDataFieldRules.IsValidId(id),
                    $"전역 ID 규칙이 '{id}'를 받아들이면 다섯 개 기존 표의 id 검사가 함께 헐거워진다.");
            }

            Assert.AreEqual("^(?:[1-9][0-9]*|[a-z][a-z0-9]*(?:_[a-z0-9]+)*)$", TableDataFieldRules.IdPatternText,
                "전역 ID 정규식은 이번 작업에서 바뀌지 않아야 한다.");
        }

        [Test]
        public void PascalCaseId_IsAFormatErrorAndTheRowIsDropped()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log,
                Row("IceMage", motionKey: "P"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CharacterId), Describe(log));
            Assert.AreEqual(0, snapshot.Characters.Count);
        }

        [Test]
        public void BlankId_IsAnErrorAndTheRowIsDropped()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log, Row("", motionKey: "P"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CharacterId), Describe(log));
            Assert.AreEqual(0, snapshot.Characters.Count);
        }

        [Test]
        public void PaddedId_IsAFormatErrorAndIsNeverTrimmed()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log,
                Row("  CatKnight  ", motionKey: "P"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CharacterId), Describe(log));
            Assert.IsFalse(snapshot.CharactersById.ContainsKey("CatKnight"));
        }

        [Test]
        public void DuplicateId_IsAnErrorAndTheFirstRowWins()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", displayOrder: "10"),
                Row("CatKnight", motionKey: "P", displayOrder: "20"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.CharacterId), Describe(log));
            Assert.AreEqual(1, snapshot.Characters.Count);
            Assert.AreEqual(10, snapshot.CharactersById["CatKnight"].DisplayOrder, "먼저 나온 행이 남아야 한다.");
        }

        // ---- 값 규칙 ----

        [Test]
        public void ValidRow_EntersSnapshotWithItsAuthoredValues()
        {
            CharacterMotionProfile profile = NewPlayableProfile();
            TableDataSnapshot snapshot = Validate(
                IndexWith(profile, "CatKnight_MotionProfile"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "CatKnight_MotionProfile", maxStamina: "30", displayOrder: "10"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, snapshot.Characters.Count);

            CharacterRow row = snapshot.Characters[0];
            Assert.AreEqual("CatKnight", row.Id);
            Assert.AreEqual(30, row.MaxStamina);
            Assert.AreEqual(10, row.DisplayOrder);
            Assert.IsTrue(row.Enabled);
            Assert.IsTrue(row.Name.Resolved, "카테고리 6 / 키 1은 프로젝트에 실제로 있는 Entry여야 한다.");
            Assert.AreSame(profile, row.MotionProfile);
            Assert.IsFalse(row.HasBaseMaxHealth, "빈 base_max_health는 '지정하지 않음'이어야 한다.");
            Assert.AreEqual(0, row.BaseMaxHealth);
            Assert.AreSame(row, snapshot.CharactersById["CatKnight"]);
        }

        [Test]
        public void EnabledMustBeExactlyOneOrZero()
        {
            Validate(out TableDataDiagnosticLog log, Row("CatKnight", motionKey: "P", enabled: "true"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.Enabled), Describe(log));
        }

        [Test]
        public void DisabledRow_StaysInTheSnapshotSoItsAssetIsStillBuilt()
        {
            CharacterMotionProfile profile = NewPlayableProfile();
            TableDataSnapshot snapshot = Validate(
                IndexWith(profile, "P"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", enabled: "0"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, snapshot.Characters.Count, "enabled=0이어도 Definition은 만들어야 하므로 행은 남는다.");
            Assert.IsFalse(snapshot.Characters[0].Enabled);
        }

        [Test]
        public void EnabledRow_RequiresBothLocalizationCells()
        {
            TableDataSnapshot snapshot = Validate(
                IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", category: "", key: ""));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.NameCategory), Describe(log));
            Assert.IsFalse(snapshot.Characters[0].Name.Resolved);
        }

        [Test]
        public void UnknownLocalizationKey_IsAnError()
        {
            Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", key: "999999"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.NameKey), Describe(log));
        }

        [Test]
        public void UnknownLocalizationCategory_IsAnError()
        {
            Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", category: "999"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.NameCategory), Describe(log));
        }

        [Test]
        public void MaxStamina_MustBeAtLeastOne()
        {
            Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog zero,
                Row("CatKnight", motionKey: "P", maxStamina: "0"));
            Assert.AreEqual(1, CountErrors(zero, TableDataColumns.MaxStamina), Describe(zero));

            Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog blank,
                Row("CatKnight", motionKey: "P", maxStamina: ""));
            Assert.AreEqual(1, CountErrors(blank, TableDataColumns.MaxStamina), Describe(blank));

            TableDataSnapshot ok = Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog fine,
                Row("CatKnight", motionKey: "P", maxStamina: "1"));
            Assert.AreEqual(0, fine.ErrorCount, Describe(fine));
            Assert.AreEqual(1, ok.Characters[0].MaxStamina);
        }

        [Test]
        public void BaseMaxHealth_IsOptionalButMustBeAtLeastOneWhenPresent()
        {
            TableDataSnapshot set = Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", baseMaxHealth: "120"));
            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.IsTrue(set.Characters[0].HasBaseMaxHealth);
            Assert.AreEqual(120, set.Characters[0].BaseMaxHealth);

            Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog zero,
                Row("CatKnight", motionKey: "P", baseMaxHealth: "0"));
            Assert.AreEqual(1, CountErrors(zero, TableDataColumns.BaseMaxHealth), Describe(zero));

            Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog negative,
                Row("CatKnight", motionKey: "P", baseMaxHealth: "-5"));
            Assert.AreEqual(1, CountErrors(negative, TableDataColumns.BaseMaxHealth), Describe(negative));

            Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog text,
                Row("CatKnight", motionKey: "P", baseMaxHealth: "많이"));
            Assert.AreEqual(1, CountErrors(text, TableDataColumns.BaseMaxHealth), Describe(text));
        }

        [Test]
        public void BlankBaseMaxHealth_IsNotAWarningEither()
        {
            // "아직 정하지 않았다"는 정상적인 상태이므로 알릴 것이 없다.
            Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", baseMaxHealth: ""));

            Assert.AreEqual(0, CountErrors(log, TableDataColumns.BaseMaxHealth), Describe(log));
            Assert.AreEqual(0, CountWarnings(log, TableDataColumns.BaseMaxHealth), Describe(log));
        }

        [Test]
        public void DisplayOrder_MustNotBeNegative()
        {
            Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", displayOrder: "-1"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.DisplayOrder), Describe(log));

            TableDataSnapshot zero = Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog ok,
                Row("CatKnight", motionKey: "P", displayOrder: "0"));
            Assert.AreEqual(0, ok.ErrorCount, Describe(ok));
            Assert.AreEqual(0, zero.Characters[0].DisplayOrder);
        }

        [Test]
        public void DuplicateDisplayOrder_IsAWarningNotAnError()
        {
            CharacterMotionProfile profile = NewPlayableProfile();
            TableDataSnapshot snapshot = Validate(IndexWith(profile, "P"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", displayOrder: "10"),
                Row("CatMage", motionKey: "P", displayOrder: "10"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, CountWarnings(log, TableDataColumns.DisplayOrder), Describe(log));
            Assert.AreEqual(2, snapshot.Characters.Count);
        }

        // ---- 모션 프로필 ----

        [Test]
        public void MotionProfile_IsRequired()
        {
            Validate(out TableDataDiagnosticLog log, Row("CatKnight", motionKey: ""));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.MotionProfileKey), Describe(log));
        }

        [Test]
        public void MotionProfile_ThatIsNotFound_IsAnError()
        {
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "NoSuchProfile"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.MotionProfileKey), Describe(log));
            Assert.IsNull(snapshot.Characters[0].MotionProfile);
        }

        [Test]
        public void MotionProfile_ThatIsAmbiguous_IsAnError()
        {
            TableDataAssetIndex assets = IndexWith(new[] { NewPlayableProfile(), NewPlayableProfile() }, "P");

            TableDataSnapshot snapshot = Validate(assets, out TableDataDiagnosticLog log, Row("CatKnight", motionKey: "P"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.MotionProfileKey), Describe(log));
            Assert.IsNull(snapshot.Characters[0].MotionProfile, "여럿 중 하나를 임의로 고르면 안 된다.");
        }

        [Test]
        public void MotionProfile_WithoutPlayableBaseIdle_IsAnError()
        {
            // 런타임(CharacterRoster)이 목록에서 빼 버릴 캐릭터를 임포터가 통과시키면 안 된다.
            CharacterMotionProfile empty = NewProfile(playable: false);
            Assert.IsFalse(CharacterMotionProfile.IsPlayable(empty), "전제 확인 - 이 프로필은 재생 불가여야 한다.");

            TableDataSnapshot snapshot = Validate(IndexWith(empty, "P"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.MotionProfileKey), Describe(log));
            Assert.IsNull(snapshot.Characters[0].MotionProfile);
        }

        [Test]
        public void MonsterMotionProfileName_DoesNotSatisfyACharacterRow()
        {
            // 캐릭터 인덱스에는 없고 몬스터 인덱스에만 있는 이름 - 타입이 섞이면 안 된다.
            TableDataSnapshot snapshot = Validate(out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "Scarecrow_MotionProfile"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.MotionProfileKey), Describe(log));
            Assert.IsNull(snapshot.Characters[0].MotionProfile);
        }

        // ---- 초상화 ----

        [Test]
        public void BlankPortrait_IsAWarningAndLeavesItEmpty()
        {
            TableDataSnapshot snapshot = Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", portraitKey: ""));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, CountWarnings(log, TableDataColumns.PortraitKey), Describe(log));
            Assert.IsNull(snapshot.Characters[0].Portrait);
        }

        [Test]
        public void NamedPortrait_ThatIsNotFound_IsAnError()
        {
            TableDataSnapshot snapshot = Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", portraitKey: "sp_no_such_icon"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.PortraitKey), Describe(log));
            Assert.IsNull(snapshot.Characters[0].Portrait);
        }

        [Test]
        public void NamedPortrait_FoundExactlyOnce_IsAssigned()
        {
            Sprite only = NewSprite();
            TableDataAssetIndex assets = IndexWith(NewPlayableProfile(), "P");
            SeedSprites(assets, "sp_character_icon", only);

            TableDataSnapshot snapshot = Validate(assets, out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", portraitKey: "sp_character_icon"));

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreSame(only, snapshot.Characters[0].Portrait);
        }

        [Test]
        public void AmbiguousPortrait_IsAnError()
        {
            TableDataAssetIndex assets = IndexWith(NewPlayableProfile(), "P");
            SeedSprites(assets, "sp_character_icon", NewSprite(), NewSprite());

            TableDataSnapshot snapshot = Validate(assets, out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P", portraitKey: "sp_character_icon"));

            Assert.AreEqual(1, CountErrors(log, TableDataColumns.PortraitKey), Describe(log));
            Assert.IsNull(snapshot.Characters[0].Portrait);
        }

        // ---- 수동 에셋과의 공존 ----

        [Test]
        public void ManualCharacterDefinitionWithTheSameId_IsNotAConflict()
        {
            // Item.csv는 같은 id의 수동 ItemDefinition을 오류로 막지만, 캐릭터는 <b>일부러</b> 다르다 -
            // 지금은 생성 에셋과 Assets/Data 이하의 수동 에셋이 같은 id로 함께 있는 것이 정상 상태다.
            var manual = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(
                "Assets/Data/Characters/CatKnight_CharacterDefinition.asset");
            Assert.IsNotNull(manual, "전제 확인 - 수동 CatKnight 정의가 프로젝트에 있어야 한다.");
            Assert.AreEqual("CatKnight", manual.CharacterId);

            TableDataSnapshot snapshot = Validate(IndexWith(NewPlayableProfile(), "P"), out TableDataDiagnosticLog log,
                Row("CatKnight", motionKey: "P"));

            Assert.AreEqual(0, log.ErrorCount,
                "같은 id의 수동 CharacterDefinition은 충돌이 아니다: " + Describe(log));
            Assert.AreEqual(1, snapshot.Characters.Count);
        }

        // ---- 실제 프로젝트 데이터(읽기 전용) ----

        [Test]
        public void LiveCsv_HasExactlyTheSixAgreedRowsInRosterOrder()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot, "여덟 표가 모두 읽혀야 스냅샷이 만들어진다: " + Live().Summary);

            var ids = new List<string>();
            foreach (CharacterRow row in snapshot.Characters) ids.Add(row.Id);

            CollectionAssert.AreEqual(
                new[] { "CatKnight", "ElfArcher", "Barbarian", "ElfGuardian", "RabbitHealer", "CatMage" },
                ids,
                "Character.csv는 현재 로스터 순서를 그대로 옮긴 여섯 행이어야 한다.");
        }

        [Test]
        public void LiveCsv_EveryRowHasStaminaThirtyAndNoBaseHealth()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot);

            foreach (CharacterRow row in snapshot.Characters)
            {
                Assert.AreEqual(30, row.MaxStamina, $"{row.Id}의 max_stamina");
                Assert.IsFalse(row.HasBaseMaxHealth, $"{row.Id}의 base_max_health는 아직 비어 있어야 한다.");
                Assert.IsTrue(row.Enabled, $"{row.Id}는 활성이어야 한다.");
                Assert.IsTrue(row.Name.Resolved, $"{row.Id}의 이름 참조가 해석되어야 한다.");
                Assert.IsNotNull(row.MotionProfile, $"{row.Id}의 모션 프로필이 연결되어야 한다.");
            }
        }

        [Test]
        public void LiveCsv_DisplayOrderIsStrictlyIncreasing()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot);

            for (int i = 1; i < snapshot.Characters.Count; i++)
            {
                Assert.Less(snapshot.Characters[i - 1].DisplayOrder, snapshot.Characters[i].DisplayOrder,
                    "표시 순서가 겹치거나 뒤집히면 로스터 순서를 그대로 옮겼다고 할 수 없다.");
            }
        }

        [Test]
        public void LiveCsv_OnlyCatKnightHasAnExplicitPortrait()
        {
            TableDataSnapshot snapshot = Live().Snapshot;
            Assert.IsNotNull(snapshot);

            foreach (CharacterRow row in snapshot.Characters)
            {
                if (string.Equals(row.Id, "CatKnight", StringComparison.Ordinal))
                {
                    Assert.IsNotNull(row.Portrait, "CatKnight는 기존 수동 에셋이 쓰던 초상화를 그대로 이어받아야 한다.");
                    Assert.AreEqual("sp_character_icon", row.Portrait.name);
                    continue;
                }

                Assert.IsNull(row.Portrait, $"{row.Id}는 초상화를 비우고 Base Idle 폴백을 쓴다.");
            }
        }

        [Test]
        public void LiveCsv_HasNoCharacterErrors()
        {
            var errors = new List<string>();
            foreach (TableDataDiagnostic diagnostic in Live().Diagnostics)
            {
                if (!string.Equals(diagnostic.File, File, StringComparison.Ordinal)) continue;
                if (diagnostic.Severity == TableDataSeverity.Error) errors.Add(diagnostic.ToString());
            }

            Assert.AreEqual(0, errors.Count, "Character.csv는 오류 없이 통과해야 한다:\n" + string.Join("\n", errors));
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

            var table = new CsvTable(File, TableDataColumns.Character, records);
            var snapshot = new TableDataSnapshot();
            log = new TableDataDiagnosticLog();

            ValidateCharactersMethod.Invoke(null, new object[] { table, snapshot, assets, log });
            return snapshot;
        }

        /// <summary>컬럼 순서는 <see cref="TableDataColumns.Character"/>와 같다.</summary>
        private static string[] Row(
            string id,
            string category = Category,
            string key = Key,
            string motionKey = "",
            string portraitKey = "",
            string baseMaxHealth = "",
            string maxStamina = "30",
            string displayOrder = "10",
            string enabled = "1")
        {
            return new[]
            {
                id, category, key, motionKey, portraitKey, baseMaxHealth, maxStamina, displayOrder, enabled,
                string.Empty,
            };
        }

        /// <summary>
        /// 조회 결과를 미리 심어 둔 인덱스. <b>디스크에는 아무것도 만들지 않는다</b> - 프로젝트를 훑지
        /// 않도록 "이미 훑었다"고 표시하고 사전에 값을 넣어, 실제 조회 경로
        /// (<see cref="TableDataAssetIndex.FindCharacterMotionProfile"/>)를 그대로 지나가게 한다.
        /// 판정(0개/1개/여럿)은 프로덕션 코드가 내린다.
        /// </summary>
        private static TableDataAssetIndex IndexWith(CharacterMotionProfile profile, string name)
        {
            return IndexWith(new[] { profile }, name);
        }

        private static TableDataAssetIndex IndexWith(CharacterMotionProfile[] profiles, string name)
        {
            var assets = new TableDataAssetIndex();
            Type type = typeof(TableDataAssetIndex);
            const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

            FieldInfo builtField = type.GetField("characterMotionProfilesBuilt", Instance);
            FieldInfo mapField = type.GetField("characterMotionProfiles", Instance);

            Assert.IsNotNull(builtField, "TableDataAssetIndex.characterMotionProfilesBuilt를 찾지 못했습니다.");
            Assert.IsNotNull(mapField, "TableDataAssetIndex.characterMotionProfiles를 찾지 못했습니다.");

            builtField.SetValue(assets, true);
            ((Dictionary<string, List<CharacterMotionProfile>>)mapField.GetValue(assets))[name] =
                new List<CharacterMotionProfile>(profiles);

            return assets;
        }

        /// <summary>Sprite 조회 결과를 심는다. 프로젝트 전체 Sprite 인덱스를 만들지 않게 막아 두므로
        /// 시험이 프로젝트 상태에 따라 달라지지 않는다.</summary>
        private static void SeedSprites(TableDataAssetIndex assets, string name, params Sprite[] sprites)
        {
            Type type = typeof(TableDataAssetIndex);
            const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

            FieldInfo builtField = type.GetField("spriteNamesBuilt", Instance);
            FieldInfo cacheField = type.GetField("resolvedSprites", Instance);

            Assert.IsNotNull(builtField, "TableDataAssetIndex.spriteNamesBuilt를 찾지 못했습니다.");
            Assert.IsNotNull(cacheField, "TableDataAssetIndex.resolvedSprites를 찾지 못했습니다.");

            builtField.SetValue(assets, true);
            ((Dictionary<string, List<Sprite>>)cacheField.GetValue(assets))[name] = new List<Sprite>(sprites);
        }

        private CharacterMotionProfile NewPlayableProfile()
        {
            return NewProfile(playable: true);
        }

        /// <summary>메모리에만 존재하는 모션 프로필. 재생 가능하게 만들려면 Base Idle에 프레임을 한 장
        /// 넣는다 - 판정은 런타임의 <see cref="CharacterMotionProfile.IsPlayable"/>이 그대로 한다.</summary>
        private CharacterMotionProfile NewProfile(bool playable)
        {
            var profile = ScriptableObject.CreateInstance<CharacterMotionProfile>();
            created.Add(profile);

            if (!playable) return profile;

            var serialized = new SerializedObject(profile);
            SerializedProperty frames = serialized.FindProperty("baseIdle").FindPropertyRelative("frames");
            Assert.IsNotNull(frames, "CharacterMotionProfile.baseIdle.frames를 찾지 못했습니다.");

            frames.arraySize = 1;
            frames.GetArrayElementAtIndex(0).objectReferenceValue = NewSprite();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return profile;
        }

        /// <summary>메모리에만 존재하는 Sprite. 에셋으로 저장하지 않으므로 프로젝트에 남지 않는다.</summary>
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
