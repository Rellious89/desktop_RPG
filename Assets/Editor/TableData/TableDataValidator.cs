using System;
using System.Collections.Generic;
using System.Globalization;
using Dungeon;
using UnityEditor;
using UnityEngine;

namespace TableDataEditor
{
    /// <summary>
    /// Validate 한 번의 결과 전체. 진단 목록과, 통과했을 때 Rebuild가 그대로 쓸 수 있는 스냅샷을
    /// 함께 들고 있다 - Rebuild가 CSV를 다시 읽지 않게 해서 "검사한 내용"과 "쓰는 내용"이 어긋나는
    /// 경로를 없앤다.
    /// </summary>
    public sealed class TableDataValidationResult
    {
        public TableDataValidationResult(TableDataDiagnosticLog log, TableDataSnapshot snapshot, TableDataAssetIndex assets)
        {
            Log = log;
            Snapshot = snapshot;
            Assets = assets;
        }

        public TableDataDiagnosticLog Log { get; }

        /// <summary>세 표가 모두 읽힌 경우의 파싱 결과. 파일/헤더 단계에서 실패하면 null이다.</summary>
        public TableDataSnapshot Snapshot { get; }

        public TableDataAssetIndex Assets { get; }

        public IReadOnlyList<TableDataDiagnostic> Diagnostics => Log.Entries;

        public int ErrorCount => Log.ErrorCount;

        public int WarningCount => Log.WarningCount;

        public bool HasErrors => Log.HasErrors;

        /// <summary>Rebuild가 에셋을 만들어도 되는 상태인지. <b>오류가 하나라도 있으면 false</b>다.</summary>
        public bool CanRebuild => !HasErrors && Snapshot != null;

        public string Summary =>
            $"World {Snapshot?.Worlds.Count ?? 0} / Monster {Snapshot?.Monsters.Count ?? 0} / " +
            $"Dungeon {Snapshot?.Dungeons.Count ?? 0} 행, 오류 {ErrorCount}건, 경고 {WarningCount}건";
    }

    /// <summary>
    /// CSV 세 장을 읽고 <b>모든</b> 문제를 모아 보고한다. <b>에셋도 폴더도 CSV도 만들지 않고 고치지도
    /// 않는다</b> - Validate는 순수하게 읽기만 하는 동작이라, 사람이 결과를 보고 판단할 때까지
    /// 프로젝트 상태가 한 글자도 바뀌지 않는다.
    ///
    /// <b>첫 오류에서 멈추지 않는다.</b> World → Monster → Dungeon 순서로 끝까지 읽고, 참조 무결성까지
    /// 검사한 뒤 한 번에 돌려준다. 순서가 정해져 있는 이유는 뒤의 표가 앞의 표를 참조하기 때문이다
    /// (Monster는 World를, Dungeon은 World와 Monster를 가리킨다).
    ///
    /// 파일/헤더 단계에서 실패한 표는 행 검증을 건너뛴다 - 헤더가 어긋난 채 행을 읽으면 엉뚱한 칸을
    /// 가리키는 오류가 쏟아져 진짜 원인이 묻히기 때문이다. 그래도 나머지 두 표는 계속 읽는다.
    /// </summary>
    public static class TableDataValidator
    {
        /// <summary>메뉴와 테스트가 함께 쓰는 진입점. 부작용이 전혀 없다.</summary>
        public static TableDataValidationResult Validate()
        {
            var log = new TableDataDiagnosticLog();
            var assets = new TableDataAssetIndex();

            CsvTable worldTable = TableDataCsvReader.Read(
                TableDataPaths.WorldCsvPath, TableDataPaths.WorldCsvFileName, TableDataColumns.World, log);
            CsvTable monsterTable = TableDataCsvReader.Read(
                TableDataPaths.MonsterCsvPath, TableDataPaths.MonsterCsvFileName, TableDataColumns.Monster, log);
            CsvTable dungeonTable = TableDataCsvReader.Read(
                TableDataPaths.DungeonCsvPath, TableDataPaths.DungeonCsvFileName, TableDataColumns.Dungeon, log);

            var snapshot = new TableDataSnapshot();
            bool allTablesRead = worldTable != null && monsterTable != null && dungeonTable != null;

            try
            {
                if (worldTable != null) ValidateWorlds(worldTable, snapshot, log);
                if (monsterTable != null) ValidateMonsters(monsterTable, snapshot, assets, log);
                if (dungeonTable != null) ValidateDungeons(dungeonTable, snapshot, assets, log);

                // 출력 쪽 충돌과 orphan은 표가 다 읽힌 뒤에만 의미가 있다. 절반만 읽힌 상태에서 orphan을
                // 세면 "CSV에서 사라졌다"가 아니라 "아직 못 읽었다"를 보고하게 된다.
                if (allTablesRead)
                {
                    CheckOutputConflicts(snapshot, log);
                    CheckOrphans(snapshot, log);
                }
            }
            catch (OperationCanceledException e)
            {
                // Sprite 인덱스 작성을 사람이 취소한 경우. 반쪽짜리 결과를 "통과"로 보이게 하지 않는다.
                log.Error(TableDataPaths.InputRoot, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.InputRoot,
                    "검사를 취소했습니다 - 결과가 완전하지 않으므로 Rebuild는 실행되지 않습니다. " + e.Message);
                allTablesRead = false;
            }

            return new TableDataValidationResult(log, allTablesRead ? snapshot : null, assets);
        }

        // ---- World ----

        private static void ValidateWorlds(CsvTable table, TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            string file = table.FileName;
            var displayOrders = new Dictionary<int, int>();

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new WorldRow { Line = line };

                string idRaw = table.Get(record, TableDataColumns.WorldId);
                bool idOk = TableDataFieldRules.TryReadRequiredId(
                    file, line, TableDataColumns.WorldId, idRaw, log, out string id);
                row.Id = id;

                if (idOk && snapshot.WorldsById.TryGetValue(id, out WorldRow existing))
                {
                    log.Error(file, line, TableDataColumns.WorldId, id,
                        $"world_id가 {existing.Line}행과 중복됩니다 - 먼저 나온 행만 사용됩니다.");
                    idOk = false;
                }

                if (TableDataFieldRules.TryReadEnabled(
                        file, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log, out bool enabled))
                {
                    row.Enabled = enabled;
                }

                if (TableDataFieldRules.TryReadInt(
                        file, line, TableDataColumns.DisplayOrder, table.Get(record, TableDataColumns.DisplayOrder), log, out int order))
                {
                    row.DisplayOrder = order;
                    TableDataFieldRules.CheckDuplicateDisplayOrder(file, line, order, displayOrders, log);
                }

                row.Name = ReadLocalizedName(
                    table, record, file, line, row.Enabled, nameRequiredWhenEnabled: true, log);

                // memo는 사람이 읽는 칸이라 검증하지 않는다.

                if (!idOk) continue;

                snapshot.Worlds.Add(row);
                snapshot.WorldsById[row.Id] = row;
            }
        }

        // ---- Monster ----

        private static void ValidateMonsters(
            CsvTable table, TableDataSnapshot snapshot, TableDataAssetIndex assets, TableDataDiagnosticLog log)
        {
            string file = table.FileName;
            var displayOrders = new Dictionary<int, int>();

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new MonsterRow { Line = line };

                string idRaw = table.Get(record, TableDataColumns.MonsterId);
                bool idOk = TableDataFieldRules.TryReadRequiredId(
                    file, line, TableDataColumns.MonsterId, idRaw, log, out string id);
                row.Id = id;

                if (idOk && snapshot.MonstersById.TryGetValue(id, out MonsterRow existing))
                {
                    log.Error(file, line, TableDataColumns.MonsterId, id,
                        $"monster_id가 {existing.Line}행과 중복됩니다 - 먼저 나온 행만 사용됩니다.");
                    idOk = false;
                }

                if (TableDataFieldRules.TryReadEnabled(
                        file, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log, out bool enabled))
                {
                    row.Enabled = enabled;
                }

                if (TableDataFieldRules.TryReadInt(
                        file, line, TableDataColumns.DisplayOrder, table.Get(record, TableDataColumns.DisplayOrder), log, out int order))
                {
                    row.DisplayOrder = order;
                    TableDataFieldRules.CheckDuplicateDisplayOrder(file, line, order, displayOrders, log);
                }

                // 몬스터 이름은 선택 항목이다. 다만 반쪽만 채워진 것은 실수일 가능성이 높아 경고한다.
                row.Name = ReadLocalizedName(
                    table, record, file, line, row.Enabled, nameRequiredWhenEnabled: false, log);

                row.WorldId = ReadWorldReference(table, record, file, line, row.Enabled, snapshot, log);

                ReadMotionProfile(table, record, file, line, assets, row, log);
                ReadPreviewSprite(table, record, file, line, assets, row, log);

                string durabilityRaw = table.Get(record, TableDataColumns.MaxDurability);
                if (TableDataFieldRules.TryReadInt(
                        file, line, TableDataColumns.MaxDurability, durabilityRaw, log, out int durability))
                {
                    row.MaxDurability = durability;
                    if (durability < 1)
                    {
                        log.Warning(file, line, TableDataColumns.MaxDurability, durabilityRaw,
                            "max_durability가 1보다 작습니다 - 런타임이 1로 보정하므로 CSV 값과 실제 값이 달라집니다.");
                    }
                }

                if (!idOk) continue;

                snapshot.Monsters.Add(row);
                snapshot.MonstersById[row.Id] = row;
            }
        }

        private static void ReadMotionProfile(
            CsvTable table, CsvRecord record, string file, int line,
            TableDataAssetIndex assets, MonsterRow row, TableDataDiagnosticLog log)
        {
            string key = table.Get(record, TableDataColumns.MotionProfileKey);

            if (string.IsNullOrEmpty(key))
            {
                log.Error(file, line, TableDataColumns.MotionProfileKey, key,
                    "motion_profile_key는 필수입니다 - 모션 데이터의 원천이 없으면 이 몬스터는 화면에 세울 수 없습니다.");
                return;
            }

            AssetLookupResult result = assets.FindMotionProfile(key, out var profile, out int count);
            switch (result)
            {
                case AssetLookupResult.Found:
                    row.MotionProfile = profile;
                    return;

                case AssetLookupResult.Ambiguous:
                    log.Error(file, line, TableDataColumns.MotionProfileKey, key,
                        $"이름이 정확히 '{key}'인 MonsterMotionProfile이 {count}개 있습니다 - 어느 것을 쓸지 정할 수 없으니 에셋 이름을 하나로 만드세요.");
                    return;

                default:
                    log.Error(file, line, TableDataColumns.MotionProfileKey, key,
                        $"이름이 정확히 '{key}'인 MonsterMotionProfile 에셋을 찾지 못했습니다(0개).");
                    return;
            }
        }

        private static void ReadPreviewSprite(
            CsvTable table, CsvRecord record, string file, int line,
            TableDataAssetIndex assets, MonsterRow row, TableDataDiagnosticLog log)
        {
            string key = table.Get(record, TableDataColumns.PreviewSpriteKey);

            if (string.IsNullOrEmpty(key))
            {
                log.Warning(file, line, TableDataColumns.PreviewSpriteKey, key,
                    "preview_sprite_key가 비어 있습니다 - 런타임이 Motion Profile의 Base Idle 첫 프레임을 대신 씁니다(선택 항목).");
                return;
            }

            ResolveSprite(assets, file, line, TableDataColumns.PreviewSpriteKey, key, log, out Sprite sprite);
            row.PreviewSprite = sprite;
        }

        // ---- Dungeon ----

        private static void ValidateDungeons(
            CsvTable table, TableDataSnapshot snapshot, TableDataAssetIndex assets, TableDataDiagnosticLog log)
        {
            string file = table.FileName;
            var displayOrders = new Dictionary<int, int>();

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new DungeonRow { Line = line };

                string idRaw = table.Get(record, TableDataColumns.DungeonId);
                bool idOk = TableDataFieldRules.TryReadRequiredId(
                    file, line, TableDataColumns.DungeonId, idRaw, log, out string id);
                row.Id = id;

                if (idOk && snapshot.DungeonsById.TryGetValue(id, out DungeonRow existing))
                {
                    log.Error(file, line, TableDataColumns.DungeonId, id,
                        $"dungeon_id가 {existing.Line}행과 중복됩니다 - 먼저 나온 행만 사용됩니다.");
                    idOk = false;
                }

                if (TableDataFieldRules.TryReadEnabled(
                        file, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log, out bool enabled))
                {
                    row.Enabled = enabled;
                }

                if (TableDataFieldRules.TryReadInt(
                        file, line, TableDataColumns.DisplayOrder, table.Get(record, TableDataColumns.DisplayOrder), log, out int order))
                {
                    row.DisplayOrder = order;
                    TableDataFieldRules.CheckDuplicateDisplayOrder(file, line, order, displayOrders, log);
                }

                row.Name = ReadLocalizedName(
                    table, record, file, line, row.Enabled, nameRequiredWhenEnabled: true, log);

                row.WorldId = ReadWorldReference(table, record, file, line, row.Enabled, snapshot, log);

                string spriteKey = table.Get(record, TableDataColumns.RepresentativeSpriteKey);
                if (string.IsNullOrEmpty(spriteKey))
                {
                    log.Warning(file, line, TableDataColumns.RepresentativeSpriteKey, spriteKey,
                        "representative_sprite_key가 비어 있습니다 - 대표 이미지 없이 표시됩니다(선택 항목).");
                }
                else
                {
                    ResolveSprite(assets, file, line, TableDataColumns.RepresentativeSpriteKey, spriteKey, log, out Sprite sprite);
                    row.RepresentativeSprite = sprite;
                }

                ReadMonsterList(table, record, file, line, snapshot, row, log);
                ReadRewardList(table, record, file, line, assets, row, log);

                if (!idOk) continue;

                snapshot.Dungeons.Add(row);
                snapshot.DungeonsById[row.Id] = row;
            }
        }

        private static void ReadMonsterList(
            CsvTable table, CsvRecord record, string file, int line,
            TableDataSnapshot snapshot, DungeonRow row, TableDataDiagnosticLog log)
        {
            string raw = table.Get(record, TableDataColumns.MonsterIds);
            TableDataFieldRules.ReadIdList(file, line, TableDataColumns.MonsterIds, raw, log, row.MonsterIds);

            if (row.MonsterIds.Count == 0)
            {
                log.Warning(file, line, TableDataColumns.MonsterIds, raw,
                    "등장 몬스터 목록이 비어 있습니다 - 던전 상세에 몬스터 칸이 하나도 표시되지 않습니다.");
                return;
            }

            foreach (string monsterId in row.MonsterIds)
            {
                if (!snapshot.MonstersById.TryGetValue(monsterId, out MonsterRow monster))
                {
                    log.Error(file, line, TableDataColumns.MonsterIds, monsterId,
                        "Monster.csv에 없는 monster_id입니다.");
                    continue;
                }

                if (!monster.Enabled)
                {
                    log.Error(file, line, TableDataColumns.MonsterIds, monsterId,
                        $"enabled=0인 몬스터({TableDataPaths.MonsterCsvFileName} {monster.Line}행)를 참조합니다 - " +
                        "던전에는 활성 몬스터만 넣을 수 있습니다.");
                }
            }
        }

        private static void ReadRewardList(
            CsvTable table, CsvRecord record, string file, int line,
            TableDataAssetIndex assets, DungeonRow row, TableDataDiagnosticLog log)
        {
            string raw = table.Get(record, TableDataColumns.RewardItemIds);
            TableDataFieldRules.ReadIdList(file, line, TableDataColumns.RewardItemIds, raw, log, row.RewardItemIds);
            row.RewardItems.Clear();

            if (row.RewardItemIds.Count == 0)
            {
                log.Warning(file, line, TableDataColumns.RewardItemIds, raw,
                    "대표 보상 목록이 비어 있습니다 - 던전 상세에 보상 칸이 하나도 표시되지 않습니다.");
                return;
            }

            foreach (string itemId in row.RewardItemIds)
            {
                AssetLookupResult result = assets.FindItemByItemId(itemId, out var item, out int count);
                switch (result)
                {
                    case AssetLookupResult.Found:
                        row.RewardItems.Add(item);
                        break;

                    case AssetLookupResult.Ambiguous:
                        log.Error(file, line, TableDataColumns.RewardItemIds, itemId,
                            $"ItemId가 '{itemId}'인 ItemDefinition이 {count}개 있습니다 - 어느 것을 쓸지 정할 수 없습니다.");
                        break;

                    default:
                        log.Error(file, line, TableDataColumns.RewardItemIds, itemId,
                            $"ItemId가 '{itemId}'인 ItemDefinition을 찾지 못했습니다(0개).");
                        break;
                }
            }
        }

        // ---- 공용 칸 읽기 ----

        /// <summary>
        /// name_category / name_key 두 칸을 함께 읽는다. 둘 다 비어 있는지, 한쪽만 있는지, 둘 다
        /// 있는지에 따라 판정이 달라진다 - <b>둘 다 있으면 언제나 실재 여부까지 검사</b>하고(잘못된
        /// 참조는 enabled와 무관하게 데이터 오류다), 필수 여부만 <paramref name="nameRequiredWhenEnabled"/>와
        /// <paramref name="enabled"/>로 갈린다.
        /// </summary>
        private static LocalizedEntryRef ReadLocalizedName(
            CsvTable table, CsvRecord record, string file, int line,
            bool enabled, bool nameRequiredWhenEnabled, TableDataDiagnosticLog log)
        {
            string categoryRaw = table.Get(record, TableDataColumns.NameCategory);
            string keyRaw = table.Get(record, TableDataColumns.NameKey);

            bool hasCategory = !string.IsNullOrEmpty(categoryRaw);
            bool hasKey = !string.IsNullOrEmpty(keyRaw);

            if (hasCategory && hasKey)
            {
                TableDataFieldRules.TryResolveLocalizedEntry(
                    file, line, TableDataColumns.NameCategory, categoryRaw,
                    TableDataColumns.NameKey, keyRaw, log, out LocalizedEntryRef entry);
                return entry;
            }

            bool required = nameRequiredWhenEnabled && enabled;

            if (!hasCategory && !hasKey)
            {
                if (required)
                {
                    log.Error(file, line, TableDataColumns.NameCategory, categoryRaw,
                        "enabled=1인 행은 name_category와 name_key가 모두 필요합니다.");
                }
                else if (!nameRequiredWhenEnabled)
                {
                    log.Warning(file, line, TableDataColumns.NameCategory, categoryRaw,
                        "이름 참조가 비어 있습니다 - 이름 없이 표시됩니다(선택 항목).");
                }

                return LocalizedEntryRef.None;
            }

            string emptyColumn = hasCategory ? TableDataColumns.NameKey : TableDataColumns.NameCategory;
            string emptyValue = hasCategory ? keyRaw : categoryRaw;

            if (required)
            {
                log.Error(file, line, emptyColumn, emptyValue,
                    "name_category와 name_key는 함께 있어야 합니다 - 한쪽만으로는 참조를 만들 수 없습니다.");
            }
            else
            {
                log.Warning(file, line, emptyColumn, emptyValue,
                    "name_category와 name_key 중 한쪽만 채워져 있어 이름 참조를 비웁니다.");
            }

            return LocalizedEntryRef.None;
        }

        /// <summary>world_id 한 칸을 읽고 참조 무결성까지 본다. 다듬지 않은 원본 값을 그대로 판정한다.</summary>
        private static string ReadWorldReference(
            CsvTable table, CsvRecord record, string file, int line,
            bool enabled, TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            string raw = table.Get(record, TableDataColumns.WorldId);

            if (string.IsNullOrEmpty(raw))
            {
                if (enabled)
                {
                    log.Error(file, line, TableDataColumns.WorldId, raw,
                        "enabled=1인 행은 world_id가 필요합니다.");
                }
                else
                {
                    log.Warning(file, line, TableDataColumns.WorldId, raw,
                        "world_id가 비어 있습니다 - 소속 월드 없이 생성됩니다.");
                }

                return string.Empty;
            }

            if (!TableDataFieldRules.IsValidId(raw))
            {
                log.Error(file, line, TableDataColumns.WorldId, raw,
                    $"world_id 형식이 맞지 않습니다 - {TableDataFieldRules.IdPatternText} 를 만족해야 합니다.");
                return string.Empty;
            }

            if (!snapshot.WorldsById.TryGetValue(raw, out WorldRow world))
            {
                log.Error(file, line, TableDataColumns.WorldId, raw,
                    $"{TableDataPaths.WorldCsvFileName}에 없는 world_id입니다.");
                return raw;
            }

            if (enabled && !world.Enabled)
            {
                log.Error(file, line, TableDataColumns.WorldId, raw,
                    $"enabled=0인 월드({TableDataPaths.WorldCsvFileName} {world.Line}행)를 참조합니다 - " +
                    "활성 행은 활성 월드만 가리킬 수 있습니다.");
            }

            return raw;
        }

        private static void ResolveSprite(
            TableDataAssetIndex assets, string file, int line, string column, string key,
            TableDataDiagnosticLog log, out Sprite sprite)
        {
            AssetLookupResult result = assets.FindSprite(key, out sprite, out int count);

            if (result == AssetLookupResult.Ambiguous)
            {
                log.Error(file, line, column, key,
                    $"이름이 정확히 '{key}'인 Sprite가 {count}개 있습니다 - 어느 것을 쓸지 정할 수 없습니다.");
            }
            else if (result == AssetLookupResult.NotFound)
            {
                log.Error(file, line, column, key,
                    $"이름이 정확히 '{key}'인 Sprite를 찾지 못했습니다(0개). 스프라이트 시트로 자른 이미지는 하위 Sprite 이름을 적어야 합니다.");
            }
        }

        // ---- 출력 쪽 사전 점검 ----

        /// <summary>
        /// Rebuild가 실제로 건드릴 경로를 <b>쓰기 전에</b> 확인한다. 같은 ID를 가진 생성 에셋이 둘 이상
        /// 있거나, 쓰려는 경로가 다른 종류의 에셋에 이미 점유되어 있으면 여기서 오류로 잡는다 -
        /// 절반쯤 쓴 뒤에 실패해서 프로젝트가 어중간한 상태로 남는 것을 막기 위함이다.
        /// </summary>
        private static void CheckOutputConflicts(TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            var worlds = TableDataAssetIndex.LoadGeneratedById<WorldDefinition>(
                TableDataPaths.WorldOutputFolder, w => w.WorldId);
            var monsters = TableDataAssetIndex.LoadGeneratedById<MonsterDefinition>(
                TableDataPaths.MonsterOutputFolder, m => m.MonsterId);
            var dungeons = TableDataAssetIndex.LoadGeneratedById<DungeonDefinition>(
                TableDataPaths.DungeonOutputFolder, d => d.DungeonId);

            CheckDuplicateGenerated(worlds, TableDataPaths.WorldCsvFileName, TableDataColumns.WorldId, log);
            CheckDuplicateGenerated(monsters, TableDataPaths.MonsterCsvFileName, TableDataColumns.MonsterId, log);
            CheckDuplicateGenerated(dungeons, TableDataPaths.DungeonCsvFileName, TableDataColumns.DungeonId, log);

            foreach (WorldRow row in snapshot.Worlds)
            {
                CheckOutputPath<WorldDefinition>(
                    TableDataPaths.WorldAssetPath(row.Id), row.Id, w => w.WorldId,
                    TableDataPaths.WorldCsvFileName, row.Line, TableDataColumns.WorldId, row.Id, log);
            }

            foreach (MonsterRow row in snapshot.Monsters)
            {
                CheckOutputPath<MonsterDefinition>(
                    TableDataPaths.MonsterAssetPath(row.Id), row.Id, m => m.MonsterId,
                    TableDataPaths.MonsterCsvFileName, row.Line, TableDataColumns.MonsterId, row.Id, log);
            }

            foreach (DungeonRow row in snapshot.Dungeons)
            {
                CheckOutputPath<DungeonDefinition>(
                    TableDataPaths.DungeonAssetPath(row.Id), row.Id, d => d.DungeonId,
                    TableDataPaths.DungeonCsvFileName, row.Line, TableDataColumns.DungeonId, row.Id, log);
            }

            CheckOutputPath<WorldCatalog>(
                TableDataPaths.WorldCatalogAssetPath, null, null,
                TableDataPaths.WorldCsvFileName, TableDataDiagnostic.FileLevelRow,
                TableDataColumns.FilePseudoColumn, TableDataPaths.WorldCatalogAssetName, log);
            CheckOutputPath<MonsterCatalog>(
                TableDataPaths.MonsterCatalogAssetPath, null, null,
                TableDataPaths.MonsterCsvFileName, TableDataDiagnostic.FileLevelRow,
                TableDataColumns.FilePseudoColumn, TableDataPaths.MonsterCatalogAssetName, log);
            CheckOutputPath<DungeonCatalog>(
                TableDataPaths.DungeonCatalogAssetPath, null, null,
                TableDataPaths.DungeonCsvFileName, TableDataDiagnostic.FileLevelRow,
                TableDataColumns.FilePseudoColumn, TableDataPaths.DungeonCatalogAssetName, log);
        }

        private static void CheckDuplicateGenerated<T>(
            Dictionary<string, List<T>> map, string file, string idColumn, TableDataDiagnosticLog log)
            where T : ScriptableObject
        {
            foreach (KeyValuePair<string, List<T>> pair in map)
            {
                if (pair.Value.Count <= 1) continue;

                log.Error(file, TableDataDiagnostic.FileLevelRow, idColumn, pair.Key,
                    $"생성 폴더에 같은 ID를 가진 {typeof(T).Name} 에셋이 {pair.Value.Count}개 있습니다 - " +
                    "어느 것을 재사용할지 정할 수 없으니 하나만 남기세요.");
            }
        }

        /// <summary>
        /// 쓰려는 경로가 안전한지 본다. 두 가지를 잡는다 - (1) 다른 <b>종류</b>의 에셋이 이미 그 경로를
        /// 쓰고 있는 경우, (2) 같은 종류지만 <b>다른 ID</b>를 들고 있는 생성 에셋이 그 자리에 있는 경우.
        /// (2)를 잡지 않으면 재사용 대상이 아닌 파일 위에 새 에셋을 만들어 남의 GUID를 날리게 된다.
        /// </summary>
        private static void CheckOutputPath<T>(
            string path, string expectedId, Func<T, string> idSelector,
            string file, int line, string column, string value, TableDataDiagnosticLog log)
            where T : ScriptableObject
        {
            Type existing = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (existing == null) return;

            if (!typeof(T).IsAssignableFrom(existing))
            {
                log.Error(file, line, column, value,
                    $"생성 경로 '{path}'가 이미 다른 종류의 에셋({existing.Name})에 쓰이고 있습니다 - " +
                    "그 파일을 옮기거나 지운 뒤 다시 실행하세요.");
                return;
            }

            if (expectedId == null || idSelector == null) return;

            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) return;

            string actualId = idSelector(asset) ?? string.Empty;
            if (string.Equals(actualId, expectedId, StringComparison.Ordinal)) return;

            log.Error(file, line, column, value,
                $"생성 경로 '{path}'에 ID가 '{actualId}'인 {typeof(T).Name}이 이미 있습니다 - " +
                "그 에셋은 다른 행이 재사용할 대상이므로 덮어쓰지 않습니다. 파일을 정리한 뒤 다시 실행하세요.");
        }

        /// <summary>
        /// CSV에서 사라졌지만 생성 폴더에 남아 있는 Definition을 경고한다. <b>지우지 않는다</b> -
        /// 다른 곳에서 아직 참조하고 있을 수 있고, 자동 삭제는 되돌릴 수 없는 동작이라 사람이 판단할
        /// 몫으로 남긴다. 카탈로그에서는 CSV에 없으므로 자연히 빠진다.
        ///
        /// "행"이 존재하지 않으므로 행 번호는 0으로, 값에는 <b>에셋이 실제로 들고 있는 ID</b>를 적는다.
        /// </summary>
        private static void CheckOrphans(TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            ReportOrphans(
                TableDataAssetIndex.LoadGeneratedById<WorldDefinition>(TableDataPaths.WorldOutputFolder, w => w.WorldId),
                snapshot.WorldsById.Keys, TableDataPaths.WorldCsvFileName, TableDataColumns.WorldId, log);

            ReportOrphans(
                TableDataAssetIndex.LoadGeneratedById<MonsterDefinition>(TableDataPaths.MonsterOutputFolder, m => m.MonsterId),
                snapshot.MonstersById.Keys, TableDataPaths.MonsterCsvFileName, TableDataColumns.MonsterId, log);

            ReportOrphans(
                TableDataAssetIndex.LoadGeneratedById<DungeonDefinition>(TableDataPaths.DungeonOutputFolder, d => d.DungeonId),
                snapshot.DungeonsById.Keys, TableDataPaths.DungeonCsvFileName, TableDataColumns.DungeonId, log);
        }

        private static void ReportOrphans<T>(
            Dictionary<string, List<T>> generated, ICollection<string> csvIds,
            string file, string idColumn, TableDataDiagnosticLog log) where T : ScriptableObject
        {
            var known = new HashSet<string>(csvIds, StringComparer.Ordinal);

            foreach (KeyValuePair<string, List<T>> pair in generated)
            {
                if (known.Contains(pair.Key)) continue;

                foreach (T asset in pair.Value)
                {
                    string id = string.IsNullOrEmpty(pair.Key) ? "(빈 ID)" : pair.Key;
                    log.Warning(file, TableDataDiagnostic.FileLevelRow, idColumn, id,
                        $"생성 에셋 '{AssetDatabase.GetAssetPath(asset)}'의 ID가 CSV에 없습니다 - " +
                        "자동으로 지우지 않으며 카탈로그에서만 빠집니다. 필요 없으면 직접 삭제하세요.");
                }
            }
        }

        /// <summary>진단을 Console에 사람이 읽기 좋은 한 덩어리로 남긴다.</summary>
        public static void LogToConsole(TableDataValidationResult result, string header)
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(header).Append(" - ").AppendLine(result.Summary);

            foreach (TableDataDiagnostic diagnostic in result.Diagnostics)
            {
                builder.AppendLine(diagnostic.ToString());
            }

            string text = builder.ToString();
            if (result.ErrorCount > 0) Debug.LogError(text);
            else if (result.WarningCount > 0) Debug.LogWarning(text);
            else Debug.Log(text);
        }

        /// <summary>요약 한 줄. 진행 표시와 대화 상자에 쓴다.</summary>
        public static string DescribeCounts(TableDataValidationResult result)
        {
            return string.Format(
                CultureInfo.InvariantCulture, "오류 {0}건, 경고 {1}건", result.ErrorCount, result.WarningCount);
        }
    }
}
