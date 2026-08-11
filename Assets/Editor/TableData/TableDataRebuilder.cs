using System;
using System.Collections.Generic;
using Character;
using CommonEditor;
using Dungeon;
using Inventory;
using Skill;
using UnityEditor;
using UnityEngine;

namespace TableDataEditor
{
    /// <summary>
    /// Rebuild가 <b>건드릴 도메인의 범위</b>. 값이 두 개뿐인 것은 의도적이다 - "아무 표나 골라서"를
    /// 허용하지 않는다.
    ///
    /// 임의의 부분집합을 허용하면 <b>참조가 조용히 지워진다</b>. Monster만 다시 쓰는 범위를 허용하면
    /// 그 몬스터가 가리키던 Item/Currency 에셋이 이번 Rebuild의 메모리 사전에 없으므로, 쓰기 코드가
    /// 참조를 null로 덮어쓴다. 그래서 <b>가리키는 표와 가리켜지는 표가 언제나 함께 들어가는 묶음</b>만
    /// 범위로 노출한다.
    /// </summary>
    public enum TableDataRebuildScope
    {
        /// <summary>여덟 표 전부. 기존 Rebuild와 완전히 같은 동작이다.</summary>
        All = 0,

        /// <summary>
        /// Character / Skill / CharacterSkill 세 표만. 이 셋은 앞의 다섯 표를 <b>하나도 참조하지
        /// 않으므로</b> 따로 떼어 내도 참조가 끊길 곳이 없고, 관계 표가 가리키는 Character와 Skill은
        /// 둘 다 이 묶음 안에 있다.
        ///
        /// 기존 다섯 도메인의 생성 에셋은 <b>읽지도 쓰지도 SetDirty하지도 않는다</b> - 이미 만들어
        /// 놓은 World/Currency/Item/Monster/Dungeon 에셋의 파일이 이 범위의 Rebuild로 달라지는 일이
        /// 없다는 것이 이 값의 존재 이유다.
        /// </summary>
        CharacterSkillTables = 1,
    }

    /// <summary>
    /// 범위 값을 다루는 유일한 자리. <b>지원하지 않는 값을 조용히 넘기지 않는다</b> - C#의 enum은
    /// 아무 정수나 캐스팅해 넣을 수 있어서, <c>(TableDataRebuildScope)7</c> 같은 값이 들어와도
    /// 컴파일러는 막지 않는다. 그런 값이 <c>if (scope == All)</c> 하나만 지나가면 "기존 다섯 도메인은
    /// 건드리지 않는다"는 분기로 흘러 들어가 <b>의도한 적 없는 범위로 쓰기가 일어난다</b>.
    /// </summary>
    public static class TableDataRebuildScopes
    {
        public static bool IsSupported(TableDataRebuildScope scope)
        {
            return scope == TableDataRebuildScope.All || scope == TableDataRebuildScope.CharacterSkillTables;
        }

        /// <summary>지원하지 않는 값이면 <b>아무것도 하기 전에</b> 던진다.</summary>
        public static void EnsureSupported(TableDataRebuildScope scope, string parameterName)
        {
            if (IsSupported(scope)) return;

            throw new ArgumentOutOfRangeException(parameterName, scope,
                "지원하지 않는 Rebuild 범위입니다 - " +
                $"{nameof(TableDataRebuildScope.All)} 또는 {nameof(TableDataRebuildScope.CharacterSkillTables)}만 " +
                "쓸 수 있습니다. 임의의 부분집합을 허용하면 범위 밖 표를 가리키던 참조가 지워집니다.");
        }

        /// <summary>이 범위가 기존 다섯 도메인(World/Currency/Item/Monster/Dungeon)을 포함하는지.</summary>
        public static bool IncludesLegacyDomains(TableDataRebuildScope scope)
        {
            EnsureSupported(scope, nameof(scope));
            return scope == TableDataRebuildScope.All;
        }
    }

    /// <summary>Rebuild 한 번의 결과.</summary>
    public sealed class TableDataRebuildResult
    {
        public TableDataValidationResult Validation;

        /// <summary>이번 Rebuild가 실제로 건드린 범위.</summary>
        public TableDataRebuildScope Scope;

        /// <summary>실제로 에셋을 쓴 경우에만 true. 검증에서 걸리면 false이고 <b>프로젝트는 한 글자도
        /// 바뀌지 않는다</b>.</summary>
        public bool Wrote;

        public int CreatedCount;
        public int UpdatedCount;
    }

    /// <summary>
    /// 검증을 통과한 스냅샷을 생성 에셋으로 옮긴다.
    ///
    /// <b>쓰기 전에 반드시 전체 Validate를 다시 돌린다.</b> 오류가 하나라도 있으면 폴더 생성,
    /// <c>CreateAsset</c>, <c>SetDirty</c>, <c>SaveAssets</c> 무엇도 호출하지 않고 그대로 끝낸다 -
    /// "일부만 반영된 상태"가 가장 다루기 어려운 상태이기 때문이다. 경로/타입 충돌과 재사용 대상의
    /// 모호함처럼 쓰는 도중에야 드러날 문제도 <see cref="TableDataValidator"/>가 미리 잡아 준다.
    ///
    /// <b>enabled=0 행도 Definition은 만든다.</b> 카탈로그에서만 빠질 뿐, 에셋 자체는 남아 있어야
    /// 나중에 다시 켤 때 GUID와 참조가 그대로 살아난다. 반대로 CSV에서 사라진 생성 에셋은
    /// <b>지우지 않는다</b> - 경고만 남기고 사람이 판단한다.
    ///
    /// <b>같은 ID면 같은 에셋을 다시 쓴다.</b> 파일 이름이 아니라 에셋이 들고 있는 ID로 찾아 재사용하므로
    /// 다시 만들어도 GUID가 보존되고, 씬/프리팹이 그 에셋을 참조하고 있어도 끊기지 않는다.
    /// </summary>
    public static class TableDataRebuilder
    {
        /// <summary>CSV가 아니라 런타임 클래스 쪽 문제일 때 진단의 "파일" 칸에 쓰는 이름.</summary>
        private const string RuntimeSchemaPseudoFile = "(runtime schema)";

        // MonsterDefinition.DropEntry 안쪽 필드 이름. 사전 검사와 쓰기가 같은 상수를 보게 해서
        // 한쪽만 고쳐진 채로 "검사는 통과했는데 쓰다가 죽는" 상태가 생기지 않게 한다.
        private const string DropsField = "drops";
        private const string DropItemField = "item";
        private const string DropChanceField = "chanceBasisPoints";
        private const string DropCountField = "count";

        // 처치 재화 보상 칸. 드롭과 같은 이유로 상수를 한 곳에 둔다. 재화는 <b>id 문자열이 아니라
        // CurrencyDefinition 참조</b>다 - 검증을 통과한 Currency.csv 행으로 만든 에셋만 여기 들어온다.
        private const string CurrencyField = "currency";
        private const string CurrencyAmountMinField = "currencyAmountMin";
        private const string CurrencyAmountMaxField = "currencyAmountMax";

        // 선택형 기본 체력의 두 칸. "값이 있는지"와 "값"이 나뉘어 있어야 빈 CSV 칸을 0/1로 뭉개지 않고
        // 그대로 옮길 수 있다 - 사전 검사와 쓰기가 같은 상수를 보게 해 둔다.
        private const string HasBaseMaxHealthField = "hasBaseMaxHealth";
        private const string BaseMaxHealthField = "baseMaxHealth";

        // 새 게임 시작 구성. 참/거짓 칸이라 타입까지 확인한다 - 정수 칸으로 되돌아가 있으면
        // boolValue에 넣은 값이 조용히 버려져 아무도 처음부터 가지지 못한 채로 만들어진다.
        private const string InitiallyOwnedField = "initiallyOwned";

        // 관계가 가리키는 두 참조 칸. 문자열로 되돌아가 있으면 objectReferenceValue에 넣은 값이
        // 조용히 버려지므로, 이름뿐 아니라 타입까지 미리 확인한다(재화 칸과 같은 이유다).
        private const string RelationCharacterField = "character";
        private const string RelationSkillField = "skill";

        /// <summary>여덟 표 전부를 다시 만든다. 기존 진입점의 동작을 그대로 둔다.</summary>
        public static TableDataRebuildResult Rebuild()
        {
            return Rebuild(TableDataRebuildScope.All);
        }

        /// <summary>
        /// 정해진 <paramref name="scope"/>의 도메인만 다시 만든다.
        ///
        /// <b>검증은 범위와 무관하게 언제나 여덟 표 전부를 본다.</b> 좁은 범위로 실행한다고 해서
        /// 검사가 느슨해지지 않으며, 어느 표에든 오류가 하나라도 있으면 <b>아무것도 쓰지 않고</b>
        /// 끝낸다 - 범위는 "무엇을 쓸지"만 정하고 "무엇을 확인할지"는 정하지 않는다.
        /// </summary>
        public static TableDataRebuildResult Rebuild(TableDataRebuildScope scope)
        {
            // 아무것도 하기 전에 막는다 - 폴더 생성도, 검증도, 로드도 하기 전이다.
            TableDataRebuildScopes.EnsureSupported(scope, nameof(scope));

            var result = new TableDataRebuildResult
            {
                Scope = scope,

                // 범위는 <b>생성 에셋 쪽 점검</b>에만 전달된다. CSV 입력 검증은 언제나 여덟 표 전부다.
                Validation = TableDataValidator.Validate(scope),
            };

            if (!result.Validation.CanRebuild) return result;

            // 필드 이름이 런타임 클래스와 어긋나면 절반쯤 쓰고 실패한다. 메모리 안의 임시 인스턴스로
            // 미리 확인하고, 어긋나면 아무것도 쓰지 않고 끝낸다(에셋을 만들지 않는 순수 검사다).
            // 범위와 무관하게 전부 확인한다 - 아무것도 쓰지 않는 검사라 좁힐 이유가 없다.
            if (!VerifySerializedLayout(result.Validation.Log)) return result;

            TableDataSnapshot snapshot = result.Validation.Snapshot;
            bool legacy = TableDataRebuildScopes.IncludesLegacyDomains(scope);

            EnsureFolders(scope);

            if (!legacy) return RebuildCharacterTables(result, snapshot);

            var worldAssets = ResolveTargets<WorldDefinition>(
                TableDataPaths.WorldOutputFolder, w => w.WorldId,
                snapshot.Worlds.ConvertAll(r => r.Id), TableDataPaths.WorldAssetPath,
                TableDataPaths.WorldCsvFileName, TableDataColumns.WorldId, result);
            var currencyAssets = ResolveTargets<CurrencyDefinition>(
                TableDataPaths.CurrencyOutputFolder, c => c.CurrencyId,
                snapshot.Currencies.ConvertAll(r => r.Id), TableDataPaths.CurrencyAssetPath,
                TableDataPaths.CurrencyCsvFileName, TableDataColumns.CurrencyId, result);
            var itemAssets = ResolveTargets<ItemDefinition>(
                TableDataPaths.ItemOutputFolder, i => i.ItemId,
                snapshot.Items.ConvertAll(r => r.Id), TableDataPaths.ItemAssetPath,
                TableDataPaths.ItemCsvFileName, TableDataColumns.ItemId, result);
            var monsterAssets = ResolveTargets<MonsterDefinition>(
                TableDataPaths.MonsterOutputFolder, m => m.MonsterId,
                snapshot.Monsters.ConvertAll(r => r.Id), TableDataPaths.MonsterAssetPath,
                TableDataPaths.MonsterCsvFileName, TableDataColumns.MonsterId, result);
            var dungeonAssets = ResolveTargets<DungeonDefinition>(
                TableDataPaths.DungeonOutputFolder, d => d.DungeonId,
                snapshot.Dungeons.ConvertAll(r => r.Id), TableDataPaths.DungeonAssetPath,
                TableDataPaths.DungeonCsvFileName, TableDataColumns.DungeonId, result);

            var worldCatalog = ResolveSingleton<WorldCatalog>(TableDataPaths.WorldCatalogAssetPath, result);
            var currencyCatalog = ResolveSingleton<CurrencyCatalog>(TableDataPaths.CurrencyCatalogAssetPath, result);
            var itemCatalog = ResolveSingleton<ItemCatalog>(TableDataPaths.ItemCatalogAssetPath, result);
            var monsterCatalog = ResolveSingleton<MonsterCatalog>(TableDataPaths.MonsterCatalogAssetPath, result);
            var dungeonCatalog = ResolveSingleton<DungeonCatalog>(TableDataPaths.DungeonCatalogAssetPath, result);

            CharacterTableTargets characterTargets = ResolveCharacterTableTargets(snapshot, result);

            AssetDatabase.StartAssetEditing();
            try
            {
                // World -> Currency -> Item -> Monster -> Dungeon 순서로 채운다. 뒤의 것이 앞의 것을
                // 참조하므로, 참조할 대상은 이미 메모리에 만들어져 있어야 한다(여기서는 다시 로드하지 않고
                // 위에서 만든 인스턴스를 쓴다). Currency와 Item을 Monster보다 앞에 둔 것은 이후 단계에서
                // 몬스터가 재화/드롭 아이템 에셋을 가리키게 되어도 이 순서를 다시 뒤집지 않게 하기 위함이다.
                foreach (WorldRow row in snapshot.Worlds) WriteWorld(worldAssets[row.Id], row);
                foreach (CurrencyRow row in snapshot.Currencies) WriteCurrency(currencyAssets[row.Id], row);
                foreach (ItemRow row in snapshot.Items) WriteItem(itemAssets[row.Id], row);
                foreach (MonsterRow row in snapshot.Monsters) WriteMonster(monsterAssets[row.Id], row, worldAssets, currencyAssets, itemAssets);
                foreach (DungeonRow row in snapshot.Dungeons) WriteDungeon(dungeonAssets[row.Id], row, worldAssets, monsterAssets, itemAssets);

                WriteCatalog(worldCatalog, "worlds", SortForCatalog(snapshot.Worlds, r => r.Enabled, r => r.DisplayOrder, r => r.Id, worldAssets));
                WriteCatalog(currencyCatalog, "currencies", SortForCatalog(snapshot.Currencies, r => r.Enabled, r => r.DisplayOrder, r => r.Id, currencyAssets));
                WriteCatalog(itemCatalog, "items", SortForCatalog(snapshot.Items, r => r.Enabled, r => r.DisplayOrder, r => r.Id, itemAssets));
                WriteCatalog(monsterCatalog, "monsters", SortForCatalog(snapshot.Monsters, r => r.Enabled, r => r.DisplayOrder, r => r.Id, monsterAssets));
                WriteCatalog(dungeonCatalog, "dungeons", SortForCatalog(snapshot.Dungeons, r => r.Enabled, r => r.DisplayOrder, r => r.Id, dungeonAssets));

                // Character -> Skill -> CharacterSkill. 관계 표가 앞의 둘을 가리키므로 순서가 이렇다.
                WriteCharacterTables(snapshot, characterTargets);
            }
            finally
            {
                // 예외가 나도 반드시 풀어 준다 - 여기서 빠져나가지 못하면 에디터가 에셋 변경을 계속
                // 묶어 둔 채로 남아 프로젝트 전체가 이상하게 동작한다.
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 카탈로그는 목록 검사 결과를 캐시하므로, 방금 바꾼 내용으로 다시 검사하게 표시한다.
            worldCatalog.MarkDirty();
            currencyCatalog.MarkDirty();
            itemCatalog.MarkDirty();
            monsterCatalog.MarkDirty();
            dungeonCatalog.MarkDirty();
            characterTargets.MarkDirty();

            result.Wrote = true;
            return result;
        }

        // ---- Character / Skill / CharacterSkill ----

        /// <summary>
        /// 세 표의 생성 대상. 기존 다섯 도메인과 <b>완전히 분리해</b> 들고 다니기 위한 묶음이다 -
        /// 좁은 범위의 Rebuild가 기존 도메인의 사전을 아예 만들지 않게 하려면 이쪽만 따로 다룰 수
        /// 있어야 한다.
        /// </summary>
        private sealed class CharacterTableTargets
        {
            public Dictionary<string, CharacterDefinition> Characters;
            public Dictionary<string, SkillDefinition> Skills;
            public Dictionary<string, CharacterSkillDefinition> Relations;

            public CharacterCatalog CharacterCatalog;
            public SkillCatalog SkillCatalog;
            public CharacterSkillCatalog RelationCatalog;

            public void MarkDirty()
            {
                CharacterCatalog.MarkDirty();
                SkillCatalog.MarkDirty();
                RelationCatalog.MarkDirty();
            }

            /// <summary>
            /// 이번 범위가 실제로 건드린 에셋 <b>전부</b>를 한 번씩. 새로 만든 것과 다시 쓴 것을
            /// 가리지 않고 담으며, 사전이 같은 인스턴스를 두 번 가리키더라도(있을 수 없지만)
            /// 참조 동일성으로 걸러 <b>중복 저장도 누락도 없게</b> 한다.
            /// </summary>
            public List<UnityEngine.Object> AllTargets()
            {
                var all = new List<UnityEngine.Object>();
                var seen = new HashSet<int>();

                void Add(UnityEngine.Object asset)
                {
                    if (asset == null) return;
                    if (!seen.Add(asset.GetInstanceID())) return;
                    all.Add(asset);
                }

                foreach (CharacterDefinition character in Characters.Values) Add(character);
                foreach (SkillDefinition skill in Skills.Values) Add(skill);
                foreach (CharacterSkillDefinition relation in Relations.Values) Add(relation);

                Add(CharacterCatalog);
                Add(SkillCatalog);
                Add(RelationCatalog);
                return all;
            }
        }

        /// <summary>
        /// 세 표만 다시 만든다. <b>기존 다섯 도메인의 생성 에셋은 로드조차 하지 않는다</b> -
        /// <see cref="ResolveTargets{T}"/>도 <see cref="ResolveSingleton{T}"/>도 그쪽 폴더에 대해
        /// 불리지 않고, 검증의 출력 쪽 점검도 범위 밖 폴더를 열지 않으므로
        /// (<see cref="TableDataValidator.GeneratedOutputFolders"/>) <c>SetDirty</c>가 걸릴 대상
        /// 자체가 존재하지 않는다.
        ///
        /// <b>여기서는 <c>AssetDatabase.SaveAssets()</c>를 부르지 않는다.</b> 그것은 프로젝트에서
        /// dirty 상태인 <b>모든</b> 에셋을 디스크에 쓰는 전역 동작이라, 사람이 인스펙터에서 고쳐 두고
        /// 아직 저장하지 않은 <b>무관한 에셋까지</b> 이 Rebuild의 이름으로 함께 커밋된다 - "이 범위
        /// 밖은 건드리지 않는다"는 약속이 저장 단계에서 깨진다. 대신 이번에 만든/다시 쓴 대상만
        /// <see cref="AssetDatabase.SaveAssetIfDirty"/>로 하나씩 저장한다.
        /// </summary>
        private static TableDataRebuildResult RebuildCharacterTables(
            TableDataRebuildResult result, TableDataSnapshot snapshot)
        {
            CharacterTableTargets targets = ResolveCharacterTableTargets(snapshot, result);

            AssetDatabase.StartAssetEditing();
            try
            {
                WriteCharacterTables(snapshot, targets);
            }
            finally
            {
                // 예외가 나도 반드시 풀어 준다 - 여기서 빠져나가지 못하면 에디터가 에셋 변경을 계속
                // 묶어 둔 채로 남는다. 예외로 빠져나가는 경우 아래 저장 단계는 <b>실행되지 않는다</b>.
                AssetDatabase.StopAssetEditing();
            }

            foreach (UnityEngine.Object asset in targets.AllTargets())
            {
                AssetDatabase.SaveAssetIfDirty(asset);
            }

            targets.MarkDirty();

            result.Wrote = true;
            return result;
        }

        private static CharacterTableTargets ResolveCharacterTableTargets(
            TableDataSnapshot snapshot, TableDataRebuildResult result)
        {
            return new CharacterTableTargets
            {
                Characters = ResolveTargets<CharacterDefinition>(
                    TableDataPaths.CharacterOutputFolder, c => c.CharacterId,
                    snapshot.Characters.ConvertAll(r => r.Id), TableDataPaths.CharacterAssetPath,
                    TableDataPaths.CharacterCsvFileName, TableDataColumns.CharacterId, result),

                Skills = ResolveTargets<SkillDefinition>(
                    TableDataPaths.SkillOutputFolder, s => s.SkillId,
                    snapshot.Skills.ConvertAll(r => r.Id), TableDataPaths.SkillAssetPath,
                    TableDataPaths.SkillCsvFileName, TableDataColumns.SkillId, result),

                Relations = ResolveTargets<CharacterSkillDefinition>(
                    TableDataPaths.CharacterSkillOutputFolder, r => r.PairId,
                    snapshot.CharacterSkills.ConvertAll(r => r.PairId), TableDataPaths.CharacterSkillAssetPath,
                    TableDataPaths.CharacterSkillCsvFileName, TableDataColumns.CharacterId, result),

                CharacterCatalog = ResolveSingleton<CharacterCatalog>(
                    TableDataPaths.CharacterCatalogAssetPath, result),
                SkillCatalog = ResolveSingleton<SkillCatalog>(TableDataPaths.SkillCatalogAssetPath, result),
                RelationCatalog = ResolveSingleton<CharacterSkillCatalog>(
                    TableDataPaths.CharacterSkillCatalogAssetPath, result),
            };
        }

        private static void WriteCharacterTables(TableDataSnapshot snapshot, CharacterTableTargets targets)
        {
            foreach (CharacterRow row in snapshot.Characters) WriteCharacter(targets.Characters[row.Id], row);
            foreach (SkillRow row in snapshot.Skills) WriteSkill(targets.Skills[row.Id], row);
            foreach (CharacterSkillRow row in snapshot.CharacterSkills)
            {
                WriteCharacterSkill(targets.Relations[row.PairId], row, targets.Characters, targets.Skills);
            }

            WriteCatalog(targets.CharacterCatalog, "characters",
                SortForCatalog(snapshot.Characters, r => r.Enabled, r => r.DisplayOrder, r => r.Id, targets.Characters));
            WriteCatalog(targets.SkillCatalog, "skills",
                SortForCatalog(snapshot.Skills, r => r.Enabled, r => r.DisplayOrder, r => r.Id, targets.Skills));
            WriteCatalog(targets.RelationCatalog, "relations",
                SortRelationsForCatalog(snapshot.CharacterSkills, targets.Relations));
        }

        // ---- 대상 에셋 확보 ----

        /// <summary>
        /// 이번 범위가 쓸 폴더만 만든다. <b>범위 밖 폴더는 만들지도 확인하지도 않는다</b> - 좁은 범위의
        /// Rebuild가 없는 도메인 폴더를 새로 만들어 두면, 그 폴더가 언제 왜 생겼는지 알 수 없는
        /// 빈 자리로 남는다.
        /// </summary>
        private static void EnsureFolders(TableDataRebuildScope scope)
        {
            EnsureFolder("Assets", "Generated");
            EnsureFolder(TableDataPaths.GeneratedRoot, "TableData");

            if (scope == TableDataRebuildScope.All)
            {
                EnsureFolder(TableDataPaths.OutputRoot, "World");
                EnsureFolder(TableDataPaths.OutputRoot, "Currency");
                EnsureFolder(TableDataPaths.OutputRoot, "Item");
                EnsureFolder(TableDataPaths.OutputRoot, "Monster");
                EnsureFolder(TableDataPaths.OutputRoot, "Dungeon");
            }

            EnsureFolder(TableDataPaths.OutputRoot, "Character");
            EnsureFolder(TableDataPaths.OutputRoot, "Skill");
            EnsureFolder(TableDataPaths.OutputRoot, "CharacterSkill");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }

        /// <summary>
        /// ID마다 쓸 에셋을 확정한다. 같은 ID의 생성 에셋이 있으면 <b>그것을 그 자리에서 다시 쓴다</b> -
        /// 파일을 옮기지 않는 것은 의도적이다. GUID 보존이 파일 이름 규칙보다 우선하고, 이름을 맞추려고
        /// 파일을 옮기다 보면 서로의 자리를 바꿔야 하는 경우가 생겨 <b>중간에 남의 에셋을 덮어쓸</b> 위험이
        /// 생기기 때문이다. 이름이 규칙과 다르면 경고만 남긴다.
        ///
        /// 새로 만들 때 경로가 이미 차 있으면 <see cref="AssetDatabase.GenerateUniqueAssetPath"/>로 비켜
        /// 만든다. 이 상황은 <see cref="TableDataValidator"/>가 이미 오류로 잡으므로 정상 흐름에서는
        /// 오지 않지만, <b>어떤 경우에도 기존 파일을 덮어쓰지 않는다</b>는 것을 코드로 보장해 둔다.
        ///
        /// 생성은 <c>StartAssetEditing</c> 바깥에서 한다 - 묶음 편집 중에 만든 에셋은 아직 데이터베이스에
        /// 등록되기 전이라, 그 안에서 경로로 다시 찾으면 없는 것처럼 보인다.
        /// </summary>
        private static Dictionary<string, T> ResolveTargets<T>(
            string folder, Func<T, string> idSelector, List<string> ids, Func<string, string> pathBuilder,
            string csvFile, string idColumn, TableDataRebuildResult result) where T : ScriptableObject
        {
            var existing = TableDataAssetIndex.LoadGeneratedById(folder, idSelector);
            var targets = new Dictionary<string, T>(StringComparer.Ordinal);
            TableDataDiagnosticLog log = result.Validation.Log;

            foreach (string id in ids)
            {
                string desiredPath = pathBuilder(id);

                if (existing.TryGetValue(id, out List<T> matches) && matches.Count == 1)
                {
                    T reused = matches[0];
                    string currentPath = AssetDatabase.GetAssetPath(reused);

                    if (!string.Equals(currentPath, desiredPath, StringComparison.Ordinal))
                    {
                        log.Warning(csvFile, TableDataDiagnostic.FileLevelRow, idColumn, id,
                            $"생성 에셋이 규칙과 다른 이름('{currentPath}')으로 있어 GUID를 지키려고 그 자리에서 갱신합니다 - " +
                            $"'{desiredPath}' 이름을 쓰려면 기존 파일을 지우고 다시 실행하세요.");
                    }

                    targets[id] = reused;
                    result.UpdatedCount++;
                    continue;
                }

                string createPath = desiredPath;
                if (AssetDatabase.LoadMainAssetAtPath(desiredPath) != null)
                {
                    createPath = AssetDatabase.GenerateUniqueAssetPath(desiredPath);
                    log.Warning(csvFile, TableDataDiagnostic.FileLevelRow, idColumn, id,
                        $"'{desiredPath}'가 이미 다른 에셋에 쓰이고 있어 '{createPath}'에 만듭니다 - " +
                        "기존 파일을 정리한 뒤 다시 실행하세요.");
                }

                var created = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(created, createPath);
                targets[id] = created;
                result.CreatedCount++;
            }

            return targets;
        }

        private static T ResolveSingleton<T>(string path, TableDataRebuildResult result) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                result.UpdatedCount++;
                return existing;
            }

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            result.CreatedCount++;
            return created;
        }

        // ---- 필드 쓰기 ----

        private static void WriteWorld(WorldDefinition asset, WorldRow row)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("worldId").stringValue = row.Id;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            ApplyLocalizedName(serialized.FindProperty("localizedName"), row.Name);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// currency_id도 item_id와 같은 무게의 키라, CSV에 적힌 값을 <b>한 글자도 바꾸지 않고</b> 쓴다
        /// (검증이 다듬지 않은 값을 그대로 통과시켰으므로 그대로 쓴다). 잔액은 여기서 쓰지 않는다 -
        /// <see cref="CurrencyDefinition"/>에는 잔액 칸 자체가 없고, 표가 정하는 것은 표시 정보뿐이다.
        /// </summary>
        private static void WriteCurrency(CurrencyDefinition asset, CurrencyRow row)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("currencyId").stringValue = row.Id;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            serialized.FindProperty("icon").objectReferenceValue = row.Icon;
            ApplyLocalizedName(serialized.FindProperty("localizedName"), row.Name);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// item_id는 <b>저장 파일의 키</b>라 여기서 쓰는 값이 CSV에 적힌 것과 한 글자도 달라서는 안 된다
        /// (검증이 다듬지 않은 값을 그대로 통과시켰으므로 그대로 쓴다). 사람이 손으로 채우던
        /// <c>displayName</c>은 임포터의 관심사가 아니라 건드리지 않는다 - 이름의 원천은 localizedName이다.
        /// </summary>
        private static void WriteItem(ItemDefinition asset, ItemRow row)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("itemId").stringValue = row.Id;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            serialized.FindProperty("icon").objectReferenceValue = row.Icon;
            ApplyLocalizedName(serialized.FindProperty("localizedName"), row.Name);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// base_monster_id는 <b>문자열 그대로</b> 적는다 - 다른 에셋을 가리키는 참조로 만들지 않는 것은
        /// 의도적이다. 참조가 되는 순간 "base를 따라가면 값이 있다"는 경로가 생기고, 그러면 상속하지
        /// 않는다는 규칙이 코드가 아니라 약속으로만 남는다.
        /// </summary>
        private static void WriteMonster(
            MonsterDefinition asset, MonsterRow row,
            Dictionary<string, WorldDefinition> worlds, Dictionary<string, CurrencyDefinition> currencies,
            Dictionary<string, ItemDefinition> items)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("monsterId").stringValue = row.Id;
            serialized.FindProperty("baseMonsterId").stringValue = row.BaseMonsterId;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            serialized.FindProperty("maxDurability").intValue = row.MaxDurability;
            serialized.FindProperty("motionProfile").objectReferenceValue = row.MotionProfile;
            serialized.FindProperty("previewSprite").objectReferenceValue = row.PreviewSprite;
            serialized.FindProperty("world").objectReferenceValue = Lookup(worlds, row.WorldId);
            ApplyLocalizedName(serialized.FindProperty("localizedName"), row.Name);

            SerializedProperty dropList = serialized.FindProperty(DropsField);
            dropList.arraySize = row.Drops.Count;
            for (int i = 0; i < row.Drops.Count; i++)
            {
                MonsterDropRow drop = row.Drops[i];
                SerializedProperty element = dropList.GetArrayElementAtIndex(i);

                // 배열을 늘리면 Unity가 바로 앞 칸을 복사해 넣기도 하므로 세 칸을 모두 덮어쓴다.
                element.FindPropertyRelative(DropItemField).objectReferenceValue = Lookup(items, drop.ItemId);
                element.FindPropertyRelative(DropChanceField).intValue = drop.ChanceBasisPoints;
                element.FindPropertyRelative(DropCountField).intValue = drop.Count;
            }

            // 재화 보상은 세 칸을 <b>언제나</b> 함께 쓴다. CSV에서 지웠는데 에셋에 예전 참조나 금액이
            // 남아 있으면 "표에 없는 재화"가 계속 지급되므로, 지정이 없는 행에는 빈 참조와 0을 적어 둔다.
            //
            // 참조는 <b>방금 이 Rebuild가 만든 CurrencyDefinition</b>에서 찾는다(프로젝트를 뒤지지 않는다).
            // row.CurrencyId가 Currency.csv에 실재하는 활성 행인지는 Validate가 이미 확인했고 하나라도
            // 어긋나면 여기까지 오지 못하므로, 조회가 빗나가는 것은 정상 흐름에 없다 - 그래도 Lookup이
            // null을 돌려주면 그대로 비워 둔다(있지도 않은 재화를 가리키는 참조를 만들지 않는다).
            serialized.FindProperty(CurrencyField).objectReferenceValue = Lookup(currencies, row.CurrencyId);
            serialized.FindProperty(CurrencyAmountMinField).intValue = row.CurrencyAmountMin;
            serialized.FindProperty(CurrencyAmountMaxField).intValue = row.CurrencyAmountMax;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void WriteDungeon(
            DungeonDefinition asset, DungeonRow row,
            Dictionary<string, WorldDefinition> worlds, Dictionary<string, MonsterDefinition> monsters,
            Dictionary<string, ItemDefinition> items)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("dungeonId").stringValue = row.Id;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            serialized.FindProperty("representativeSprite").objectReferenceValue = row.RepresentativeSprite;
            serialized.FindProperty("world").objectReferenceValue = Lookup(worlds, row.WorldId);
            ApplyLocalizedName(serialized.FindProperty("dungeonName"), row.Name);

            SerializedProperty monsterList = serialized.FindProperty("monsters");
            monsterList.arraySize = row.MonsterIds.Count;
            for (int i = 0; i < row.MonsterIds.Count; i++)
            {
                monsterList.GetArrayElementAtIndex(i).objectReferenceValue = Lookup(monsters, row.MonsterIds[i]);
            }

            SerializedProperty rewardList = serialized.FindProperty("rewardItems");
            rewardList.arraySize = row.RewardItemIds.Count;
            for (int i = 0; i < row.RewardItemIds.Count; i++)
            {
                rewardList.GetArrayElementAtIndex(i).objectReferenceValue = Lookup(items, row.RewardItemIds[i]);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// character_id는 <b>저장 데이터(SaveData.characters)의 키</b>라 CSV에 적힌 값을 한 글자도
        /// 바꾸지 않고 쓴다.
        ///
        /// <b><c>displayName</c>은 건드리지 않는다.</b> 사람이 손으로 채우던 칸이고 지금도 화면에
        /// 나오는 이름의 근거이므로, 임포터가 그 위에 무엇을 쓰면 표시 이름이 조용히 달라진다
        /// (<see cref="WriteItem"/>이 ItemDefinition의 displayName을 건드리지 않는 것과 같은 이유다).
        /// 표에서 온 이름은 <c>localizedName</c>에만 들어간다.
        ///
        /// 기본 체력은 <b>두 칸을 언제나 함께</b> 쓴다 - 표에서 값을 지웠는데 에셋에 예전 숫자가 남아
        /// 있으면 "정하지 않았다"가 사라진다. 지정이 없으면 플래그를 끄고 숫자 칸은 하한값으로 되돌린다.
        /// </summary>
        private static void WriteCharacter(CharacterDefinition asset, CharacterRow row)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("characterId").stringValue = row.Id;
            serialized.FindProperty("motionProfile").objectReferenceValue = row.MotionProfile;
            serialized.FindProperty("portrait").objectReferenceValue = row.Portrait;
            serialized.FindProperty("maxStamina").intValue = row.MaxStamina;
            serialized.FindProperty(InitiallyOwnedField).boolValue = row.InitiallyOwned;
            serialized.FindProperty(HasBaseMaxHealthField).boolValue = row.HasBaseMaxHealth;
            serialized.FindProperty(BaseMaxHealthField).intValue = row.HasBaseMaxHealth ? row.BaseMaxHealth : 1;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            ApplyLocalizedName(serialized.FindProperty("localizedName"), row.Name);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// 스킬 한 종. 분류 키와 동작 키는 <b>문자열 그대로</b> 적는다 - 그 값을 무엇으로 바꾸는
        /// 해석기는 이 단계에 없고, 여기서 참조로 만들면 "키를 따라가면 동작이 있다"는 경로가 생긴다.
        /// 설명 참조는 지정이 없으면 <b>비운다</b>(예전 값이 남으면 지운 설명이 계속 나온다).
        /// </summary>
        private static void WriteSkill(SkillDefinition asset, SkillRow row)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("skillId").stringValue = row.Id;
            serialized.FindProperty("icon").objectReferenceValue = row.Icon;
            serialized.FindProperty("skillType").stringValue = row.SkillType;
            serialized.FindProperty("behaviorKey").stringValue = row.BehaviorKey;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            ApplyLocalizedName(serialized.FindProperty("localizedName"), row.Name);
            ApplyLocalizedName(serialized.FindProperty("localizedDescription"), row.Description);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// 관계 한 줄. id 문자열과 참조를 <b>같은 행에서 함께</b> 쓰므로 둘이 어긋날 수 없다.
        /// 참조는 <b>방금 이 Rebuild가 만든 정의</b>에서 찾는다(프로젝트를 뒤지지 않는다) - 양쪽 id가
        /// 표에 실재하는지는 Validate가 이미 확인했고 하나라도 어긋나면 여기까지 오지 못하지만,
        /// 그래도 조회가 빗나가면 그대로 비워 둔다(있지도 않은 대상을 가리키는 참조를 만들지 않는다).
        /// </summary>
        private static void WriteCharacterSkill(
            CharacterSkillDefinition asset, CharacterSkillRow row,
            Dictionary<string, CharacterDefinition> characters, Dictionary<string, SkillDefinition> skills)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("characterId").stringValue = row.CharacterId;
            serialized.FindProperty("skillId").stringValue = row.SkillId;
            serialized.FindProperty(RelationCharacterField).objectReferenceValue = Lookup(characters, row.CharacterId);
            serialized.FindProperty(RelationSkillField).objectReferenceValue = Lookup(skills, row.SkillId);
            serialized.FindProperty("requiredCharacterLevel").intValue = row.RequiredCharacterLevel;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void WriteCatalog<T>(ScriptableObject catalog, string listFieldName, List<T> items)
            where T : ScriptableObject
        {
            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty(listFieldName);
            list.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        /// <summary>
        /// Table GUID + Entry Key ID를 그대로 기록한다. 참조가 해석되지 않은(선택 항목이 빈) 경우에는
        /// <b>비운다</b> - 예전 값이 남아 있으면 CSV를 지웠는데도 이름이 계속 나오게 된다.
        /// </summary>
        private static void ApplyLocalizedName(SerializedProperty property, LocalizedEntryRef entry)
        {
            if (!entry.Resolved)
            {
                LocalizedTextReferenceProperty.Clear(property);
                return;
            }

            LocalizedTextReferenceProperty.FindTableCollectionName(property).stringValue =
                LocalizedTextReferenceProperty.ToGuidString(entry.TableGuid);
            LocalizedTextReferenceProperty.FindKeyId(property).longValue = entry.KeyId;

            // Key ID로 가리키므로 이름 기반 참조는 비워 둔다(CommonEditor의 기존 규칙과 동일).
            LocalizedTextReferenceProperty.FindKeyName(property).stringValue = string.Empty;
        }

        private static TValue Lookup<TValue>(Dictionary<string, TValue> map, string id) where TValue : class
        {
            return !string.IsNullOrEmpty(id) && map.TryGetValue(id, out TValue value) ? value : null;
        }

        /// <summary>
        /// 카탈로그에 담을 항목을 고르고 정렬한다. <b>enabled=1만</b> 담고,
        /// display_order 오름차순 → 같으면 ID Ordinal 오름차순으로 둔다. Ordinal을 쓰는 이유는
        /// 현재 Locale에 따라 순서가 달라지지 않게 하기 위함이다.
        /// </summary>
        private static List<TAsset> SortForCatalog<TRow, TAsset>(
            List<TRow> rows,
            Func<TRow, bool> enabledSelector,
            Func<TRow, int> orderSelector,
            Func<TRow, string> idSelector,
            Dictionary<string, TAsset> assets) where TAsset : ScriptableObject
        {
            var selected = new List<TRow>();
            foreach (TRow row in rows)
            {
                if (enabledSelector(row)) selected.Add(row);
            }

            selected.Sort((a, b) =>
            {
                int byOrder = orderSelector(a).CompareTo(orderSelector(b));
                return byOrder != 0 ? byOrder : string.CompareOrdinal(idSelector(a), idSelector(b));
            });

            var result = new List<TAsset>(selected.Count);
            foreach (TRow row in selected)
            {
                if (assets.TryGetValue(idSelector(row), out TAsset asset) && asset != null) result.Add(asset);
            }

            return result;
        }

        /// <summary>
        /// 관계 카탈로그에 담을 항목을 고르고 정렬한다. <b>enabled=1만</b> 담고,
        /// display_order → character_id → skill_id 순서로 <b>세 단계</b> 오름차순이다.
        ///
        /// <see cref="SortForCatalog{TRow,TAsset}"/>에 짝 키(<c>a__b</c>)를 넘겨 두 단계로 줄이지
        /// <b>않는다</b>. 이어 붙인 문자열의 Ordinal 순서는 두 id를 차례로 비교한 순서와 다르기
        /// 때문이다 - 구분자 <c>_</c>(0x5F)가 숫자나 대문자보다 뒤에 오므로, 예를 들어 캐릭터
        /// <c>a</c>와 <c>a1</c>의 앞뒤가 뒤집힌다.
        /// </summary>
        private static List<CharacterSkillDefinition> SortRelationsForCatalog(
            List<CharacterSkillRow> rows, Dictionary<string, CharacterSkillDefinition> assets)
        {
            var selected = new List<CharacterSkillRow>();
            foreach (CharacterSkillRow row in rows)
            {
                if (row.Enabled) selected.Add(row);
            }

            selected.Sort((a, b) =>
            {
                int byOrder = a.DisplayOrder.CompareTo(b.DisplayOrder);
                if (byOrder != 0) return byOrder;

                int byCharacter = string.CompareOrdinal(a.CharacterId, b.CharacterId);
                return byCharacter != 0 ? byCharacter : string.CompareOrdinal(a.SkillId, b.SkillId);
            });

            var result = new List<CharacterSkillDefinition>(selected.Count);
            foreach (CharacterSkillRow row in selected)
            {
                if (assets.TryGetValue(row.PairId, out CharacterSkillDefinition asset) && asset != null)
                {
                    result.Add(asset);
                }
            }

            return result;
        }

        // ---- 사전 점검 ----

        /// <summary>
        /// 런타임 클래스의 직렬화 필드 이름이 임포터가 쓰려는 이름과 일치하는지 메모리에서 확인한다.
        /// 에셋을 만들지 않고 임시 인스턴스만 쓰므로 프로젝트는 바뀌지 않는다.
        /// </summary>
        private static bool VerifySerializedLayout(TableDataDiagnosticLog log)
        {
            bool ok = true;
            ok &= VerifyFields<WorldDefinition>(log, "worldId", "localizedName", "displayOrder");
            ok &= VerifyFields<CurrencyDefinition>(log, "currencyId", "localizedName", "icon", "displayOrder");
            ok &= VerifyFields<ItemDefinition>(log, "itemId", "localizedName", "icon", "displayOrder");
            ok &= VerifyFields<MonsterDefinition>(log, "monsterId", "baseMonsterId", "localizedName", "world",
                "motionProfile", "previewSprite", "maxDurability", DropsField,
                CurrencyField, CurrencyAmountMinField, CurrencyAmountMaxField, "displayOrder");
            ok &= VerifyMonsterCurrencyIsAReference(log);
            ok &= VerifyDropEntryFields(log);
            ok &= VerifyFields<DungeonDefinition>(log, "dungeonId", "dungeonName", "world", "representativeSprite",
                "monsters", "rewardItems", "displayOrder");
            ok &= VerifyFields<WorldCatalog>(log, "worlds");
            ok &= VerifyFields<CurrencyCatalog>(log, "currencies");
            ok &= VerifyFields<ItemCatalog>(log, "items");
            ok &= VerifyFields<MonsterCatalog>(log, "monsters");
            ok &= VerifyFields<DungeonCatalog>(log, "dungeons");

            // 캐릭터 정의는 <b>기존 칸까지 함께</b> 확인한다 - 이 임포터가 기존 칸(characterId /
            // motionProfile / portrait / maxStamina)에도 값을 쓰기 때문이다. displayName은 쓰지 않지만
            // 목록에 넣어 두었다: 그 칸이 사라지면 표시 이름의 근거가 통째로 달라지므로, 임포터를 함께
            // 고쳐야 하는 변경이라는 것을 여기서 드러낸다.
            ok &= VerifyFields<CharacterDefinition>(log, "characterId", "displayName", "motionProfile", "portrait",
                "maxStamina", InitiallyOwnedField, "localizedName", HasBaseMaxHealthField, BaseMaxHealthField,
                "displayOrder");
            ok &= VerifyPropertyType<CharacterDefinition>(
                log, InitiallyOwnedField, SerializedPropertyType.Boolean,
                "새 게임 시작 구성은 참/거짓 칸이어야 합니다 - 다른 타입이면 표의 값이 조용히 버려집니다.");
            ok &= VerifyPropertyType<CharacterDefinition>(
                log, HasBaseMaxHealthField, SerializedPropertyType.Boolean,
                "기본 체력을 '지정했는지'는 참/거짓 칸이어야 합니다 - 숫자 칸 하나로 합치면 빈 CSV 칸과 " +
                "0을 구분할 수 없습니다.");
            ok &= VerifyPropertyType<CharacterDefinition>(
                log, BaseMaxHealthField, SerializedPropertyType.Integer,
                "기본 체력은 정수 칸이어야 합니다.");

            ok &= VerifyFields<SkillDefinition>(log, "skillId", "localizedName", "localizedDescription", "icon",
                "skillType", "behaviorKey", "displayOrder");
            ok &= VerifyPropertyType<SkillDefinition>(
                log, "behaviorKey", SerializedPropertyType.String,
                "동작 키는 문자열 칸이어야 합니다 - 참조 칸이 되면 표만 보고 동작을 정할 수 없게 됩니다.");

            ok &= VerifyFields<CharacterSkillDefinition>(log, "characterId", "skillId", RelationCharacterField,
                RelationSkillField, "requiredCharacterLevel", "displayOrder");
            ok &= VerifyPropertyType<CharacterSkillDefinition>(
                log, RelationCharacterField, SerializedPropertyType.ObjectReference,
                $"관계의 캐릭터 칸은 {nameof(CharacterDefinition)}을 가리키는 참조여야 합니다(id 문자열이 아닙니다).");
            ok &= VerifyPropertyType<CharacterSkillDefinition>(
                log, RelationSkillField, SerializedPropertyType.ObjectReference,
                $"관계의 스킬 칸은 {nameof(SkillDefinition)}을 가리키는 참조여야 합니다(id 문자열이 아닙니다).");

            ok &= VerifyFields<CharacterCatalog>(log, "characters");
            ok &= VerifyFields<SkillCatalog>(log, "skills");
            ok &= VerifyFields<CharacterSkillCatalog>(log, "relations");
            return ok;
        }

        /// <summary>
        /// 칸의 <b>타입</b>까지 확인한다. 이름만 보는 것으로는 부족하다 - 같은 이름이 다른 타입으로
        /// 바뀌어 있으면 <c>intValue</c>/<c>boolValue</c>/<c>objectReferenceValue</c>에 넣은 값이
        /// 조용히 버려져, 검사는 통과했는데 내용이 빈 에셋이 만들어진다.
        ///
        /// 칸이 아예 없는 경우는 <see cref="VerifyFields{T}"/>가 이미 보고했으므로 여기서는 통과로 둔다.
        /// </summary>
        private static bool VerifyPropertyType<T>(
            TableDataDiagnosticLog log, string field, SerializedPropertyType expected, string reason)
            where T : ScriptableObject
        {
            var probe = ScriptableObject.CreateInstance<T>();
            try
            {
                SerializedProperty property = new SerializedObject(probe).FindProperty(field);
                if (property == null || property.propertyType == expected) return true;

                log.Error(RuntimeSchemaPseudoFile, TableDataDiagnostic.FileLevelRow, field, typeof(T).Name,
                    $"{typeof(T).Name}의 '{field}'가 {expected} 칸이 아닙니다({property.propertyType}) - {reason}");
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        /// 재화 칸이 <b>참조 칸</b>인지 확인한다. 이름만 보는 것으로는 부족하다 - 같은 이름이 문자열
        /// 칸으로 되돌아가 있으면 <c>objectReferenceValue</c>에 넣은 값이 조용히 버려져, 재화를 지정한
        /// 몬스터가 아무것도 주지 않는 에셋으로 만들어진다. 확률 칸의 타입을 보는 것과 같은 이유다.
        /// </summary>
        private static bool VerifyMonsterCurrencyIsAReference(TableDataDiagnosticLog log)
        {
            var probe = ScriptableObject.CreateInstance<MonsterDefinition>();
            try
            {
                SerializedProperty currency = new SerializedObject(probe).FindProperty(CurrencyField);

                // 칸이 없는 경우는 VerifyFields가 이미 보고했다.
                if (currency == null || currency.propertyType == SerializedPropertyType.ObjectReference) return true;

                log.Error(RuntimeSchemaPseudoFile, TableDataDiagnostic.FileLevelRow,
                    CurrencyField, nameof(MonsterDefinition),
                    $"{nameof(MonsterDefinition)}의 '{CurrencyField}'가 참조 칸이 아닙니다({currency.propertyType}) - " +
                    $"재화는 {nameof(CurrencyDefinition)}을 가리키는 참조여야 합니다(id 문자열이 아닙니다).");
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        /// 드롭 칸 안쪽 필드 이름까지 확인한다. 목록 필드가 있다는 것만으로는 부족하다 - 안쪽 이름이
        /// 어긋나면 <c>FindPropertyRelative</c>가 null을 돌려주고, 그때는 이미 앞선 몬스터 몇 마리를
        /// 쓴 뒤라 프로젝트가 반쯤 갱신된 상태로 남는다. 임시 인스턴스에서 칸을 하나 늘려 확인하며,
        /// 인스턴스는 곧바로 버리므로 프로젝트에는 아무것도 남지 않는다.
        /// </summary>
        private static bool VerifyDropEntryFields(TableDataDiagnosticLog log)
        {
            var probe = ScriptableObject.CreateInstance<MonsterDefinition>();
            try
            {
                var serialized = new SerializedObject(probe);
                SerializedProperty list = serialized.FindProperty(DropsField);
                if (list == null) return false; // 목록 자체가 없는 경우는 VerifyFields가 이미 보고했다.

                list.arraySize = 1;
                SerializedProperty element = list.GetArrayElementAtIndex(0);

                bool ok = true;
                foreach (string field in new[] { DropItemField, DropChanceField, DropCountField })
                {
                    if (element.FindPropertyRelative(field) != null) continue;

                    log.Error(RuntimeSchemaPseudoFile, TableDataDiagnostic.FileLevelRow,
                        field, nameof(MonsterDefinition.DropEntry),
                        $"{nameof(MonsterDefinition.DropEntry)}에 직렬화 필드 '{field}'가 없습니다 - " +
                        "런타임 클래스가 바뀌었으니 임포터를 함께 고쳐야 합니다.");
                    ok = false;
                }

                // 확률 칸은 이름뿐 아니라 <b>타입</b>까지 본다. 예전처럼 실수 칸으로 되돌아가 있으면
                // 정수를 쓰는 이 임포터가 값을 넣지 못한 채 지나가, 확률이 0인 드롭이 만들어진다.
                SerializedProperty chance = element.FindPropertyRelative(DropChanceField);
                if (chance != null && chance.propertyType != SerializedPropertyType.Integer)
                {
                    log.Error(RuntimeSchemaPseudoFile, TableDataDiagnostic.FileLevelRow,
                        DropChanceField, nameof(MonsterDefinition.DropEntry),
                        $"{nameof(MonsterDefinition.DropEntry)}의 '{DropChanceField}'가 정수 칸이 아닙니다" +
                        $"({chance.propertyType}) - 확률은 만분율 정수여야 합니다.");
                    ok = false;
                }

                return ok;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        private static bool VerifyFields<T>(TableDataDiagnosticLog log, params string[] fields) where T : ScriptableObject
        {
            var probe = ScriptableObject.CreateInstance<T>();
            try
            {
                var serialized = new SerializedObject(probe);
                bool ok = true;

                foreach (string field in fields)
                {
                    if (serialized.FindProperty(field) != null) continue;

                    log.Error(RuntimeSchemaPseudoFile, TableDataDiagnostic.FileLevelRow,
                        field, typeof(T).Name,
                        $"{typeof(T).Name}에 직렬화 필드 '{field}'가 없습니다 - 런타임 클래스가 바뀌었으니 임포터를 함께 고쳐야 합니다.");
                    ok = false;
                }

                return ok;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }
    }
}
