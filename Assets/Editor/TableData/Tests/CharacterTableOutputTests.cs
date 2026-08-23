using System;
using System.Collections.Generic;
using System.Reflection;
using Character;
using Dungeon;
using NUnit.Framework;
using Skill;
using UnityEditor;
using UnityEngine;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// 캐릭터 쪽 세 표의 <b>출력</b>에 관한 시험 - 생성 경로의 경계, 좁은 범위 Rebuild의 계약,
    /// 카탈로그 정렬, CSV에서 사라진 생성 에셋(stale)의 처리, 그리고 실제로 만들어진 에셋의 모양이다.
    ///
    /// <b>여기서도 에셋을 만들거나 지우지 않는다.</b> 정렬과 stale 처리는 메모리 위의 임시
    /// ScriptableObject로 확인하고, 실제 생성 결과는 읽기만 한다. Rebuild를 부르는 시험은 없다 -
    /// 시험이 프로젝트를 다시 쓰기 시작하면 "시험을 돌렸더니 자산이 달라졌다"가 되기 때문이다.
    /// </summary>
    public sealed class CharacterTableOutputTests
    {
        private static readonly MethodInfo SortForCatalogMethod =
            typeof(TableDataRebuilder).GetMethod("SortForCatalog", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo SortRelationsMethod =
            typeof(TableDataRebuilder).GetMethod(
                "SortRelationsForCatalog", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo ReportOrphansMethod =
            typeof(TableDataValidator).GetMethod("ReportOrphans", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo CheckOutputPathMethod =
            typeof(TableDataValidator).GetMethod("CheckOutputPath", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>기존 다섯 도메인의 생성 폴더.</summary>
        private static readonly string[] LegacyOutputFolders =
        {
            TableDataPaths.WorldOutputFolder, TableDataPaths.CurrencyOutputFolder,
            TableDataPaths.ItemOutputFolder, TableDataPaths.MonsterOutputFolder,
            TableDataPaths.DungeonOutputFolder,
        };

        /// <summary>아홉 도메인의 생성 폴더 전부.</summary>
        private static readonly string[] AllOutputFolders =
        {
            TableDataPaths.WorldOutputFolder, TableDataPaths.CurrencyOutputFolder,
            TableDataPaths.ItemOutputFolder, TableDataPaths.MonsterOutputFolder,
            TableDataPaths.DungeonOutputFolder, TableDataPaths.CharacterOutputFolder,
            TableDataPaths.SkillOutputFolder, TableDataPaths.CharacterSkillOutputFolder,
            TableDataPaths.BuildingOutputFolder,
            TableDataPaths.CharacterAcquisitionOutputFolder, TableDataPaths.RecruitmentTypeOutputFolder,
            TableDataPaths.RecruitmentPoolOutputFolder, TableDataPaths.RecruitmentAccessOutputFolder,
            TableDataPaths.PartyConfigOutputFolder,
        };

        private const string TempRootName = "__TableDataTestsTemp";
        private const string TempRoot = "Assets/" + TempRootName;

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private readonly List<string> tempFolders = new List<string>();

        [SetUp]
        public void SetUpFixture()
        {
            Assert.IsNotNull(SortForCatalogMethod, "TableDataRebuilder.SortForCatalog를 찾지 못했습니다.");
            Assert.IsNotNull(SortRelationsMethod, "TableDataRebuilder.SortRelationsForCatalog를 찾지 못했습니다.");
            Assert.IsNotNull(ReportOrphansMethod, "TableDataValidator.ReportOrphans를 찾지 못했습니다.");
            Assert.IsNotNull(CheckOutputPathMethod, "TableDataValidator.CheckOutputPath를 찾지 못했습니다.");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object asset in created)
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }

            created.Clear();

            // 시험이 만든 임시 에셋은 실패했더라도 반드시 치운다.
            foreach (string folder in tempFolders)
            {
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }

            tempFolders.Clear();

            if (AssetDatabase.IsValidFolder(TempRoot)) AssetDatabase.DeleteAsset(TempRoot);
        }

        // ---- 출력 경로의 경계 ----

        [Test]
        public void NewOutputFolders_AreSiblingsOfTheExistingFive()
        {
            string[] existing =
            {
                TableDataPaths.WorldOutputFolder, TableDataPaths.CurrencyOutputFolder,
                TableDataPaths.ItemOutputFolder, TableDataPaths.MonsterOutputFolder,
                TableDataPaths.DungeonOutputFolder,
            };

            string[] added =
            {
                TableDataPaths.CharacterOutputFolder, TableDataPaths.SkillOutputFolder,
                TableDataPaths.CharacterSkillOutputFolder,
            };

            foreach (string newFolder in added)
            {
                Assert.IsTrue(newFolder.StartsWith(TableDataPaths.OutputRoot + "/", StringComparison.Ordinal),
                    $"'{newFolder}'는 생성 루트 아래에 있어야 한다.");

                foreach (string old in existing)
                {
                    Assert.AreNotEqual(old, newFolder);
                    Assert.IsFalse(newFolder.StartsWith(old + "/", StringComparison.Ordinal),
                        $"'{newFolder}'가 기존 도메인 '{old}' 안에 있으면 그 폴더의 정리 동작이 기존 자산에 닿는다.");
                    Assert.IsFalse(old.StartsWith(newFolder + "/", StringComparison.Ordinal),
                        $"기존 도메인 '{old}'가 새 폴더 '{newFolder}' 안에 있으면 안 된다.");
                }
            }
        }

        [Test]
        public void SkillFolderIsNotAPrefixOfTheRelationFolder()
        {
            // 'Skill'과 'CharacterSkill'은 이름이 겹쳐 보이지만 폴더 경로는 서로를 포함하지 않는다 -
            // 포함되면 스킬 폴더를 훑는 조회가 관계 에셋까지 끌어온다.
            Assert.IsFalse(
                TableDataPaths.CharacterSkillOutputFolder.StartsWith(
                    TableDataPaths.SkillOutputFolder + "/", StringComparison.Ordinal));
            Assert.IsFalse(
                TableDataPaths.SkillOutputFolder.StartsWith(
                    TableDataPaths.CharacterSkillOutputFolder + "/", StringComparison.Ordinal));
        }

        [Test]
        public void GeneratedCharactersLiveOutsideTheManualDataFolder()
        {
            // 생성 폴더와 수동 에셋 폴더가 겹치지 않는다는 것이, 임포터가 수동 CharacterDefinition을
            // 절대 덮어쓰지 않는다는 말의 실제 근거다.
            Assert.IsFalse(
                TableDataPaths.CharacterOutputFolder.StartsWith("Assets/Data", StringComparison.Ordinal));
            Assert.IsTrue(
                TableDataPaths.CharacterOutputFolder.StartsWith(
                    TableDataPaths.GeneratedRoot + "/", StringComparison.Ordinal));
        }

        // ---- 좁은 범위 Rebuild의 계약 ----

        [Test]
        public void RebuildScope_OffersOnlyWholeReferenceClosures()
        {
            // 임의의 부분집합을 허용하면 범위 밖 표를 가리키던 참조가 null로 덮어써진다. 값이 늘어날
            // 수 있는 유일한 조건은 <b>참조가 닫히는 묶음</b>이라는 것이다 - Building이 더해진 것도
            // 그 조건을 지켰기 때문이다(가리키는 Currency/Item 생성 에셋이 없으면 Validate가 쓰기 전에
            // 오류로 막는다). 새 값을 더할 때마다 여기가 실패하므로, 그 근거를 적지 않고 늘릴 수 없다.
            //
            // RecruitmentTables가 더해진 근거도 같다 - 네 표가 서로를 가리키는 참조(창구 → 모집)는
            // 모두 이 묶음 안에 있고, 밖으로 나가는 유일한 참조인 Character는 이미 만들어져 있는 생성
            // 에셋을 읽어 잇는다(없거나 여럿이면 Validate가 쓰기 전에 오류로 막는다). 창구가 가리키는
            // 건물은 아예 참조가 아니라 문자열 두 칸이라 지워질 참조 자체가 없다.
            //
            // PartyConfigTable이 더해진 근거는 가장 단순하다 - 이 표는 어느 표도 <b>가리키지 않으므로</b>
            // 지워질 참조 자체가 없고, 다른 도메인의 생성 에셋을 읽지도 쓰지도 않는다.
            CollectionAssert.AreEquivalent(
                new[]
                {
                    TableDataRebuildScope.All,
                    TableDataRebuildScope.CharacterSkillTables,
                    TableDataRebuildScope.BuildingTable,
                    TableDataRebuildScope.RecruitmentTables,
                    TableDataRebuildScope.PartyConfigTable,
                },
                Enum.GetValues(typeof(TableDataRebuildScope)),
                "Rebuild 범위가 늘어나면 참조가 조용히 지워지는 경로가 생긴다.");
        }

        [Test]
        public void Rebuild_HasBothTheWholeProjectEntryPointAndTheScopedOne()
        {
            Assert.IsNotNull(typeof(TableDataRebuilder).GetMethod("Rebuild", Type.EmptyTypes),
                "기존 진입점 Rebuild()는 그대로 남아 있어야 한다.");
            Assert.IsNotNull(
                typeof(TableDataRebuilder).GetMethod("Rebuild", new[] { typeof(TableDataRebuildScope) }),
                "범위를 받는 진입점이 있어야 좁은 Rebuild를 부를 수 있다.");
        }

        [Test]
        public void UnsupportedScope_IsRejectedBeforeAnythingIsTouched()
        {
            // enum은 아무 정수나 캐스팅해 넣을 수 있다. 그런 값이 'All이 아니다'라는 분기 하나만
            // 지나가면 의도한 적 없는 범위로 쓰기가 일어나므로, 쓰기 전에 명시적으로 막아야 한다.
            var unsupported = (TableDataRebuildScope)999;

            Assert.IsFalse(TableDataRebuildScopes.IsSupported(unsupported));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => TableDataRebuildScopes.EnsureSupported(unsupported, "scope"));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => TableDataRebuildScopes.IncludesLegacyDomains(unsupported));

            // Rebuild는 검증도 폴더 생성도 하기 전에 던진다 - 프로젝트가 한 글자도 바뀌지 않는다.
            Assert.Throws<ArgumentOutOfRangeException>(() => TableDataRebuilder.Rebuild(unsupported));

            // 출력 쪽 점검도 같은 값을 거부한다.
            Assert.Throws<ArgumentOutOfRangeException>(
                () => TableDataValidator.GeneratedOutputFolders(unsupported));
            Assert.Throws<ArgumentOutOfRangeException>(() => TableDataValidator.Validate(unsupported));
        }

        [Test]
        public void SupportedScopes_AreAcceptedAndClassified()
        {
            Assert.IsTrue(TableDataRebuildScopes.IsSupported(TableDataRebuildScope.All));
            Assert.IsTrue(TableDataRebuildScopes.IsSupported(TableDataRebuildScope.CharacterSkillTables));

            Assert.IsTrue(TableDataRebuildScopes.IncludesLegacyDomains(TableDataRebuildScope.All));
            Assert.IsFalse(TableDataRebuildScopes.IncludesLegacyDomains(TableDataRebuildScope.CharacterSkillTables));
        }

        [Test]
        public void TargetedScope_SelectsOnlyTheThreeNewGeneratedOutputFolders()
        {
            IReadOnlyList<string> targeted =
                TableDataValidator.GeneratedOutputFolders(TableDataRebuildScope.CharacterSkillTables);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    TableDataPaths.CharacterOutputFolder,
                    TableDataPaths.SkillOutputFolder,
                    TableDataPaths.CharacterSkillOutputFolder,
                },
                targeted,
                "좁은 범위의 출력 점검은 새 세 폴더만 열어야 한다.");

            CollectionAssert.DoesNotContain(targeted, TableDataPaths.BuildingOutputFolder,
                "캐릭터 쪽 좁은 범위는 Building 생성 폴더도 열지 않는다.");

            foreach (string legacy in LegacyOutputFolders)
            {
                CollectionAssert.DoesNotContain(targeted, legacy,
                    $"'{legacy}'를 열면 기존 도메인의 생성 에셋을 로드하게 된다.");
            }
        }

        [Test]
        public void AllScope_SelectsEveryGeneratedOutputFolder()
        {
            CollectionAssert.AreEquivalent(
                AllOutputFolders,
                TableDataValidator.GeneratedOutputFolders(TableDataRebuildScope.All),
                "전체 범위는 열네 도메인을 모두 본다(기존 동작 그대로 넓어졌다).");
        }

        [Test]
        public void TargetedValidation_ReportsNoOrphansForTheLegacyDomains()
        {
            // 기존 다섯 도메인에는 CSV에서 사라진 생성 에셋이 실제로 남아 있다(World animal_land 등).
            // 전체 범위에서는 그 경고가 나오고, 좁은 범위에서는 그 폴더를 열지 않으므로 나오지 않는다 -
            // 경고 유무가 "로드했는가"의 관측 가능한 증거다.
            TableDataValidationResult all = TableDataValidator.Validate(TableDataRebuildScope.All);
            TableDataValidationResult targeted =
                TableDataValidator.Validate(TableDataRebuildScope.CharacterSkillTables);

            Assert.AreEqual(0, all.ErrorCount, Describe(all));
            Assert.AreEqual(0, targeted.ErrorCount, Describe(targeted));

            Assert.Greater(CountLegacyOrphanWarnings(all), 0,
                "전제 확인 - 전체 범위에서는 기존 도메인의 orphan 경고가 보여야 한다.");
            Assert.AreEqual(0, CountLegacyOrphanWarnings(targeted),
                "좁은 범위는 기존 도메인의 생성 폴더를 열지 않으므로 그 경고가 나올 수 없다.");
        }

        [Test]
        public void OutputChecks_OnlyMentionGeneratedAssetsFromTheSelectedFolders()
        {
            // 목록을 따로 만들어 두고 검사는 다른 기준으로 분기하면, 목록과 실제 동작이 어긋나도
            // 아무도 알 수 없다. 여기서는 <b>실제 Validate가 낸 진단</b>이 가리키는 생성 에셋 경로가
            // 그 범위의 선택 목록 안에만 있는지를 본다 - 검사가 목록을 실제로 소비한다는 증거다.
            foreach (TableDataRebuildScope scope in
                     new[] { TableDataRebuildScope.All, TableDataRebuildScope.CharacterSkillTables })
            {
                IReadOnlyList<string> selected = TableDataValidator.GeneratedOutputFolders(scope);
                TableDataValidationResult result = TableDataValidator.Validate(scope);

                foreach (TableDataDiagnostic diagnostic in result.Diagnostics)
                {
                    foreach (string folder in AllOutputFolders)
                    {
                        if (diagnostic.Message.IndexOf(folder + "/", StringComparison.Ordinal) < 0) continue;

                        CollectionAssert.Contains(selected, folder,
                            $"{scope} 범위의 진단이 선택되지 않은 '{folder}'의 생성 에셋을 가리킨다 - " +
                            "그 폴더를 열었다는 뜻이다: " + diagnostic);
                    }
                }
            }
        }

        [Test]
        public void AllScope_ActuallyOpensTheLegacyFolders_SoTheSeamTestIsNotVacuous()
        {
            // 위 시험이 "진단이 하나도 없어서 통과"하는 상태가 아님을 못 박는다.
            TableDataValidationResult all = TableDataValidator.Validate(TableDataRebuildScope.All);

            int legacyMentions = 0;
            foreach (TableDataDiagnostic diagnostic in all.Diagnostics)
            {
                foreach (string folder in LegacyOutputFolders)
                {
                    if (diagnostic.Message.IndexOf(folder + "/", StringComparison.Ordinal) >= 0) legacyMentions++;
                }
            }

            Assert.Greater(legacyMentions, 0,
                "전체 범위에서는 기존 도메인의 생성 에셋을 가리키는 진단이 실제로 있어야 한다.");
        }

        [Test]
        public void EnsureFolders_TakesTheScopeSoItCannotTouchOutOfScopeDomains()
        {
            MethodInfo ensureFolders = typeof(TableDataRebuilder).GetMethod(
                "EnsureFolders", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(ensureFolders, "TableDataRebuilder.EnsureFolders를 찾지 못했습니다.");

            ParameterInfo[] parameters = ensureFolders.GetParameters();
            Assert.AreEqual(1, parameters.Length, "폴더 생성은 범위를 받아야 한다.");
            Assert.AreEqual(typeof(TableDataRebuildScope), parameters[0].ParameterType);
        }

        // ---- 카탈로그 정렬 ----

        [Test]
        public void CharacterCatalogOrder_IsDisplayOrderThenIdOrdinal_AndSkipsDisabledRows()
        {
            var rows = new List<CharacterRow>
            {
                Row("CatMage", 60, enabled: true),
                Row("hidden", 5, enabled: false),
                Row("CatKnight", 10, enabled: true),
                Row("Barbarian", 10, enabled: true),
                Row("ElfArcher", 20, enabled: true),
            };

            var assets = new Dictionary<string, CharacterDefinition>(StringComparer.Ordinal);
            foreach (CharacterRow row in rows) assets[row.Id] = NewCharacter(row.Id);

            List<CharacterDefinition> sorted = SortCharacters(rows, assets);

            CollectionAssert.AreEqual(
                new[] { "Barbarian", "CatKnight", "ElfArcher", "CatMage" },
                IdsOf(sorted),
                "display_order 오름차순 → 같으면 character_id Ordinal 오름차순이어야 하고, " +
                "enabled=0인 행은 들어가면 안 된다.");
        }

        [Test]
        public void CatalogOrderIsDeterministic_AcrossRepeatedSorts()
        {
            var rows = new List<CharacterRow>
            {
                Row("ElfGuardian", 10, enabled: true),
                Row("Barbarian", 10, enabled: true),
                Row("CatKnight", 10, enabled: true),
            };

            var assets = new Dictionary<string, CharacterDefinition>(StringComparer.Ordinal);
            foreach (CharacterRow row in rows) assets[row.Id] = NewCharacter(row.Id);

            List<string> first = IdsOf(SortCharacters(rows, assets));
            List<string> second = IdsOf(SortCharacters(rows, assets));

            CollectionAssert.AreEqual(first, second, "같은 입력은 언제나 같은 순서를 내야 한다.");
            CollectionAssert.AreEqual(new[] { "Barbarian", "CatKnight", "ElfGuardian" }, first,
                "동률은 Ordinal id로만 갈린다 - 목록에 적힌 차례가 끼어들면 안 된다.");
        }

        [Test]
        public void RelationCatalogOrder_IsDisplayThenCharacterThenSkill()
        {
            var rows = new List<CharacterSkillRow>
            {
                Relation("CatKnight", "ice_bolt", 10),
                Relation("CatKnight", "fire_bolt", 10),
                Relation("Barbarian", "zzz", 10),
                Relation("Barbarian", "aaa", 5),
                Relation("CatMage", "hidden", 1, enabled: false),
            };

            var assets = new Dictionary<string, CharacterSkillDefinition>(StringComparer.Ordinal);
            foreach (CharacterSkillRow row in rows) assets[row.PairId] = NewRelation(row.CharacterId, row.SkillId);

            var sorted = (List<CharacterSkillDefinition>)SortRelationsMethod.Invoke(null, new object[] { rows, assets });

            var pairs = new List<string>();
            foreach (CharacterSkillDefinition relation in sorted) pairs.Add(relation.PairId);

            CollectionAssert.AreEqual(
                new[] { "Barbarian__aaa", "Barbarian__zzz", "CatKnight__fire_bolt", "CatKnight__ice_bolt" },
                pairs,
                "display_order → character_id → skill_id 순서여야 하고, 비활성 관계는 빠져야 한다.");
        }

        [Test]
        public void RelationOrder_IsNotTheOrdinalOrderOfTheJoinedPairKey()
        {
            // 이어 붙인 키로 정렬하면 순서가 뒤집히는 실제 예. 구분자 '_'(0x5F)가 숫자보다 뒤에 온다.
            var rows = new List<CharacterSkillRow>
            {
                Relation("a1", "y", 10),
                Relation("a", "x", 10),
            };

            var assets = new Dictionary<string, CharacterSkillDefinition>(StringComparer.Ordinal);
            foreach (CharacterSkillRow row in rows) assets[row.PairId] = NewRelation(row.CharacterId, row.SkillId);

            var sorted = (List<CharacterSkillDefinition>)SortRelationsMethod.Invoke(null, new object[] { rows, assets });

            Assert.AreEqual("a__x", sorted[0].PairId, "character_id 'a'가 'a1'보다 앞이어야 한다.");
            Assert.AreEqual("a1__y", sorted[1].PairId);

            Assert.Less(string.CompareOrdinal("a1__y", "a__x"), 0,
                "전제 확인 - 짝 키를 그대로 Ordinal 비교하면 반대 순서가 나온다.");
        }

        // ---- CSV에서 사라진 생성 에셋(stale) ----

        [Test]
        public void StaleGeneratedAsset_IsReportedAsAWarningAndIsNeverDeleted()
        {
            CharacterDefinition stale = NewCharacter("removed_hero");
            var generated = new Dictionary<string, List<CharacterDefinition>>(StringComparer.Ordinal)
            {
                ["removed_hero"] = new List<CharacterDefinition> { stale },
            };

            var log = new TableDataDiagnosticLog();
            ReportOrphans(generated, new[] { "CatKnight" }, log);

            Assert.AreEqual(0, log.ErrorCount, "CSV에서 사라진 것은 오류가 아니다: " + Describe(log));
            Assert.AreEqual(1, log.WarningCount, Describe(log));
            Assert.IsTrue(stale != null, "경고만 남기고 <b>지우지 않는다</b> - 삭제는 되돌릴 수 없다.");
        }

        [Test]
        public void GeneratedAssetWithAnEmptyId_IsReportedAsAnOrphanWarning()
        {
            // SkillDefinition.SkillId는 값이 없으면 빈 문자열을 돌려준다(파일 이름으로 대체하지 않는다).
            // 그런 에셋이 생성 폴더에 남아 있으면 카탈로그에도 못 들어가고 CSV에도 대응 행이 없으므로,
            // 조용히 버리지 말고 사람 눈에 보이게 경고해야 한다.
            var nameless = ScriptableObject.CreateInstance<SkillDefinition>();
            created.Add(nameless);
            Assert.AreEqual(string.Empty, nameless.SkillId, "전제 확인 - ID가 비어 있어야 한다.");

            var generated = new Dictionary<string, List<SkillDefinition>>(StringComparer.Ordinal)
            {
                [string.Empty] = new List<SkillDefinition> { nameless },
            };

            var log = new TableDataDiagnosticLog();
            ReportOrphans(generated, new[] { "fire_bolt" }, log);

            Assert.AreEqual(0, log.ErrorCount, Describe(log));
            Assert.AreEqual(1, log.WarningCount, "빈 ID 생성 에셋도 orphan 경고 한 건으로 알려야 한다: " + Describe(log));
            Assert.AreEqual(TableDataValidator.EmptyIdLabel, log.Entries[0].Value,
                "빈 문자열을 그대로 찍으면 진단을 읽을 수 없다.");
            Assert.IsTrue(nameless != null, "경고만 남기고 지우지 않는다.");
        }

        [Test]
        public void GeneratedIndex_KeepsEmptyIdAssetsInsteadOfDroppingThem()
        {
            // 실제 조회 경로(LoadGeneratedById)가 빈 ID를 버리지 않는지를 임시 폴더로 확인한다.
            // 폴더와 에셋은 이 시험이 만들고 TearDown에서 지우므로 프로젝트 자산은 건드리지 않는다.
            string folder = CreateTempFolder();

            var nameless = ScriptableObject.CreateInstance<SkillDefinition>();
            AssetDatabase.CreateAsset(nameless, folder + "/Skill_nameless.asset");

            var named = ScriptableObject.CreateInstance<SkillDefinition>();
            var serialized = new SerializedObject(named);
            serialized.FindProperty("skillId").stringValue = "fire_bolt";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(named, folder + "/Skill_fire_bolt.asset");

            AssetDatabase.SaveAssetIfDirty(nameless);
            AssetDatabase.SaveAssetIfDirty(named);

            Dictionary<string, List<SkillDefinition>> map =
                TableDataAssetIndex.LoadGeneratedById<SkillDefinition>(folder, s => s.SkillId);

            Assert.IsTrue(map.ContainsKey(string.Empty),
                "ID가 빈 생성 에셋이 사라지면 orphan 경고가 영원히 나오지 않는다.");
            Assert.AreEqual(1, map[string.Empty].Count);
            Assert.IsTrue(map.ContainsKey("fire_bolt"));
            Assert.AreEqual(1, map["fire_bolt"].Count);
        }

        [Test]
        public void GeneratedIndex_StillUsesTheFilenameFallbackForCharacters()
        {
            // CharacterDefinition.CharacterId는 값이 비면 에셋 이름을 돌려준다. 빈 키 보존 변경이
            // 그 폴백을 깨뜨리지 않는지 - 캐릭터는 빈 키 그룹이 생기지 않아야 한다.
            string folder = CreateTempFolder();

            var blank = ScriptableObject.CreateInstance<CharacterDefinition>();
            AssetDatabase.CreateAsset(blank, folder + "/Character_FallbackName.asset");
            AssetDatabase.SaveAssetIfDirty(blank);

            Dictionary<string, List<CharacterDefinition>> map =
                TableDataAssetIndex.LoadGeneratedById<CharacterDefinition>(folder, c => c.CharacterId);

            Assert.IsFalse(map.ContainsKey(string.Empty),
                "캐릭터는 빈 ID일 때 에셋 이름을 쓰므로 빈 키 그룹이 생기면 안 된다.");
            Assert.IsTrue(map.ContainsKey("Character_FallbackName"),
                "파일 이름 폴백이 그대로 살아 있어야 한다.");
        }

        [Test]
        public void GeneratedAssetStillInTheCsv_IsNotReported()
        {
            var generated = new Dictionary<string, List<CharacterDefinition>>(StringComparer.Ordinal)
            {
                ["CatKnight"] = new List<CharacterDefinition> { NewCharacter("CatKnight") },
            };

            var log = new TableDataDiagnosticLog();
            ReportOrphans(generated, new[] { "CatKnight" }, log);

            Assert.AreEqual(0, log.Entries.Count, Describe(log));
        }

        // ---- 출력 경로 충돌 ----

        [Test]
        public void OutputPathHeldByAnotherKindOfAsset_IsAnError()
        {
            // 실제로 존재하는 다른 종류의 에셋(CSV 텍스트)을 캐릭터 생성 경로처럼 검사해 본다.
            // 읽기만 하므로 그 파일은 한 글자도 바뀌지 않는다.
            var log = new TableDataDiagnosticLog();
            CheckOutputPath<CharacterDefinition>(TableDataPaths.CharacterCsvPath, log);

            Assert.AreEqual(1, log.ErrorCount,
                "다른 종류의 에셋이 차지한 경로는 쓰기 전에 오류로 잡아야 한다: " + Describe(log));
        }

        [Test]
        public void EmptyOutputPath_IsNotAConflict()
        {
            var log = new TableDataDiagnosticLog();
            CheckOutputPath<CharacterDefinition>(
                TableDataPaths.CharacterAssetPath("no_such_character_for_tests"), log);

            Assert.AreEqual(0, log.Entries.Count, Describe(log));
        }

        // ---- 실제로 만들어진 에셋(읽기 전용) ----

        [Test]
        public void GeneratedCharacterCatalog_HoldsExactlyTheSixRowsInTableOrder()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(TableDataPaths.CharacterCatalogAssetPath);
            Assert.IsNotNull(catalog,
                $"'{TableDataPaths.CharacterCatalogAssetPath}'가 없습니다 - Table Data Rebuild를 먼저 실행하세요.");

            catalog.MarkDirty();

            var ids = new List<string>();
            foreach (CharacterDefinition character in catalog.Characters) ids.Add(character.CharacterId);

            CollectionAssert.AreEqual(
                new[] { "CatKnight", "ElfArcher", "Barbarian", "ElfGuardian", "RabbitHealer", "CatMage" },
                ids,
                "카탈로그는 display_order 오름차순(= 현재 로스터 순서)이어야 한다.");
        }

        [Test]
        public void GeneratedCharacterCatalog_ContainsOnlyGeneratedAssets()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(TableDataPaths.CharacterCatalogAssetPath);
            Assert.IsNotNull(catalog);

            catalog.MarkDirty();

            foreach (CharacterDefinition character in catalog.Characters)
            {
                string path = AssetDatabase.GetAssetPath(character);
                Assert.IsTrue(path.StartsWith(TableDataPaths.CharacterOutputFolder + "/", StringComparison.Ordinal),
                    $"수동 에셋 '{path}'이 카탈로그에 들어왔다 - 목록의 근거는 표 하나뿐이어야 한다.");
                Assert.AreEqual(TableDataPaths.CharacterAssetPath(character.CharacterId), path,
                    "생성 에셋은 규칙대로 된 이름의 자리에 있어야 한다.");
            }
        }

        [Test]
        public void ManualCharacterDefinitions_AreStillThereAndUntouched()
        {
            // 생성 에셋이 생겨도 수동 에셋은 그대로 남는다 - 같은 id로 공존하는 것이 지금의 정상 상태다.
            var manual = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(
                "Assets/Data/Characters/CatKnight_CharacterDefinition.asset");

            Assert.IsNotNull(manual, "수동 CharacterDefinition을 옮기거나 지우지 않았어야 한다.");
            Assert.AreEqual("CatKnight", manual.CharacterId);
            Assert.AreEqual(30, manual.MaxStamina);
        }

        [Test]
        public void GeneratedSkillCatalogs_ExistAndAreEmpty()
        {
            var skills = AssetDatabase.LoadAssetAtPath<SkillCatalog>(TableDataPaths.SkillCatalogAssetPath);
            Assert.IsNotNull(skills,
                $"'{TableDataPaths.SkillCatalogAssetPath}'가 없습니다 - Table Data Rebuild를 먼저 실행하세요.");
            skills.MarkDirty();
            Assert.AreEqual(0, skills.Count, "아직 스킬이 없으므로 비어 있는 것이 정상이다.");

            var relations = AssetDatabase.LoadAssetAtPath<CharacterSkillCatalog>(
                TableDataPaths.CharacterSkillCatalogAssetPath);
            Assert.IsNotNull(relations,
                $"'{TableDataPaths.CharacterSkillCatalogAssetPath}'가 없습니다 - Table Data Rebuild를 먼저 실행하세요.");
            relations.MarkDirty();
            Assert.AreEqual(0, relations.Count, "아직 관계가 없으므로 비어 있는 것이 정상이다.");
        }

        /// <summary>
        /// 새 게임에서 처음부터 가지고 시작하는 캐릭터는 <b>고양이기사 하나뿐</b>이고, 나머지 다섯은
        /// 모집으로 얻는다 - Character.csv의 <c>initially_owned</c>가 그렇게 적혀 있기 때문이다.
        ///
        /// 예전에는 여섯 모두가 true였다. 그 값이 바뀐 것은 모집이 생기면서 <b>표가 정책을 고쳤기</b>
        /// 때문이며, 생성 에셋은 표를 따라가야 한다 - 여기서 확인하는 것은 "표에 적힌 대로 옮겨졌는가"
        /// 하나다.
        /// </summary>
        [Test]
        public void GeneratedCharacters_MatchTheTablesInitiallyOwnedPolicy()
        {
            foreach ((string id, bool initiallyOwned) in new[]
                     {
                         ("CatKnight", true), ("ElfArcher", false), ("Barbarian", false),
                         ("ElfGuardian", false), ("RabbitHealer", false), ("CatMage", false),
                     })
            {
                var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(
                    TableDataPaths.CharacterAssetPath(id));

                Assert.IsNotNull(definition, $"생성 에셋이 없습니다 - Rebuild를 먼저 실행하세요: {id}");
                Assert.AreEqual(initiallyOwned, definition.InitiallyOwned,
                    $"{id}의 새 게임 시작 구성이 Character.csv와 다릅니다.");
            }
        }

        [Test]
        public void GeneratedCharacters_CarryTheValuesFromTheTable()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(TableDataPaths.CharacterCatalogAssetPath);
            Assert.IsNotNull(catalog);

            catalog.MarkDirty();

            foreach (CharacterDefinition character in catalog.Characters)
            {
                // initially_owned는 캐릭터마다 다르므로 여기서 값을 단정하지 않는다 - 그 판정은
                // GeneratedCharacters_MatchTheTablesInitiallyOwnedPolicy 하나가 갖는다.
                Assert.AreEqual(30, character.MaxStamina, $"{character.CharacterId}의 Max Stamina");
                Assert.IsFalse(character.HasBaseMaxHealth, $"{character.CharacterId}의 기본 체력은 아직 미지정이다.");
                Assert.AreEqual(0, character.BaseMaxHealth);
                Assert.IsTrue(character.HasLocalizedName, $"{character.CharacterId}의 이름 참조");
                Assert.IsNotNull(character.MotionProfile, $"{character.CharacterId}의 모션 프로필");
                Assert.IsTrue(CharacterMotionProfile.IsPlayable(character.MotionProfile));
            }
        }

        [Test]
        public void GeneratedCharacters_ReferenceTheirExactCsvOriginWorlds()
        {
            var expected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CatKnight"] = "1", ["CatMage"] = "1", ["RabbitHealer"] = "1",
                ["ElfArcher"] = "2", ["Barbarian"] = "2", ["ElfGuardian"] = "2",
            };

            foreach (KeyValuePair<string, string> pair in expected)
            {
                CharacterDefinition character = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(
                    TableDataPaths.CharacterAssetPath(pair.Key));
                WorldDefinition world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(
                    TableDataPaths.WorldAssetPath(pair.Value));

                Assert.IsNotNull(character, $"생성 CharacterDefinition이 없습니다: {pair.Key}");
                Assert.IsNotNull(world, $"생성 WorldDefinition이 없습니다: {pair.Value}");
                Assert.AreSame(world, character.OriginWorld,
                    $"{pair.Key}의 origin_world_id가 CSV와 다른 WorldDefinition을 가리킵니다.");
            }
        }

        // ---- 도우미 ----

        private static List<CharacterDefinition> SortCharacters(
            List<CharacterRow> rows, Dictionary<string, CharacterDefinition> assets)
        {
            MethodInfo sort = SortForCatalogMethod.MakeGenericMethod(typeof(CharacterRow), typeof(CharacterDefinition));

            return (List<CharacterDefinition>)sort.Invoke(null, new object[]
            {
                rows,
                (Func<CharacterRow, bool>)(r => r.Enabled),
                (Func<CharacterRow, int>)(r => r.DisplayOrder),
                (Func<CharacterRow, string>)(r => r.Id),
                assets,
            });
        }

        private static void ReportOrphans<T>(
            Dictionary<string, List<T>> generated, string[] csvIds, TableDataDiagnosticLog log)
            where T : ScriptableObject
        {
            ReportOrphansMethod
                .MakeGenericMethod(typeof(T))
                .Invoke(null, new object[]
                {
                    generated, csvIds, TableDataPaths.CharacterCsvFileName, TableDataColumns.CharacterId, log,
                });
        }

        /// <summary>
        /// 이 시험만 쓰는 임시 폴더. TearDown에서 통째로 지우므로 프로젝트에 남지 않는다 -
        /// 생성 폴더 조회는 실제 <see cref="AssetDatabase"/> 경로를 훑기 때문에 메모리만으로는
        /// 확인할 수 없는 유일한 자리다.
        /// </summary>
        private string CreateTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.CreateFolder("Assets", TempRootName);
            }

            string name = "Case_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder(TempRoot, name);

            string path = TempRoot + "/" + name;
            tempFolders.Add(path);
            return path;
        }

        private static void CheckOutputPath<T>(string path, TableDataDiagnosticLog log) where T : ScriptableObject
        {
            CheckOutputPathMethod
                .MakeGenericMethod(typeof(T))
                .Invoke(null, new object[]
                {
                    path, null, null, TableDataPaths.CharacterCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.CharacterId, path, log,
                });
        }

        private static CharacterRow Row(string id, int displayOrder, bool enabled)
        {
            return new CharacterRow { Id = id, DisplayOrder = displayOrder, Enabled = enabled };
        }

        private static CharacterSkillRow Relation(
            string characterId, string skillId, int displayOrder, bool enabled = true)
        {
            return new CharacterSkillRow
            {
                CharacterId = characterId,
                SkillId = skillId,
                PairId = CharacterSkillDefinition.BuildPairId(characterId, skillId),
                DisplayOrder = displayOrder,
                Enabled = enabled,
            };
        }

        /// <summary>메모리에만 존재하는 캐릭터 정의. 디스크에는 아무것도 남지 않는다.</summary>
        private CharacterDefinition NewCharacter(string id)
        {
            var asset = ScriptableObject.CreateInstance<CharacterDefinition>();
            created.Add(asset);

            var serialized = new SerializedObject(asset);
            serialized.FindProperty("characterId").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        private CharacterSkillDefinition NewRelation(string characterId, string skillId)
        {
            var asset = ScriptableObject.CreateInstance<CharacterSkillDefinition>();
            created.Add(asset);

            var serialized = new SerializedObject(asset);
            serialized.FindProperty("characterId").stringValue = characterId;
            serialized.FindProperty("skillId").stringValue = skillId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        private static List<string> IdsOf(List<CharacterDefinition> characters)
        {
            var ids = new List<string>();
            foreach (CharacterDefinition character in characters) ids.Add(character.CharacterId);
            return ids;
        }

        /// <summary>기존 다섯 도메인 CSV 이름으로 기록된 orphan 경고 수.</summary>
        private static int CountLegacyOrphanWarnings(TableDataValidationResult result)
        {
            var legacyFiles = new HashSet<string>(
                new[]
                {
                    TableDataPaths.WorldCsvFileName, TableDataPaths.CurrencyCsvFileName,
                    TableDataPaths.ItemCsvFileName, TableDataPaths.MonsterCsvFileName,
                    TableDataPaths.DungeonCsvFileName,
                },
                StringComparer.Ordinal);

            int count = 0;
            foreach (TableDataDiagnostic diagnostic in result.Diagnostics)
            {
                if (diagnostic.Severity != TableDataSeverity.Warning) continue;
                if (diagnostic.Row != TableDataDiagnostic.FileLevelRow) continue;
                if (legacyFiles.Contains(diagnostic.File)) count++;
            }

            return count;
        }

        private static string Describe(TableDataValidationResult result)
        {
            var lines = new List<string>();
            foreach (TableDataDiagnostic diagnostic in result.Diagnostics) lines.Add(diagnostic.ToString());
            return lines.Count == 0 ? "(진단 없음)" : "\n" + string.Join("\n", lines);
        }

        private static string Describe(TableDataDiagnosticLog log)
        {
            var lines = new List<string>();
            foreach (TableDataDiagnostic diagnostic in log.Entries) lines.Add(diagnostic.ToString());
            return lines.Count == 0 ? "(진단 없음)" : "\n" + string.Join("\n", lines);
        }
    }
}
