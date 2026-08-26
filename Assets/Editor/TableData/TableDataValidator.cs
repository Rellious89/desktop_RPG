using System;
using System.Collections.Generic;
using System.Globalization;
using Building;
using Character;
using Dungeon;
using Inventory;
using Party;
using Recruitment;
using Skill;
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

        /// <summary>아홉 표가 모두 읽힌 경우의 파싱 결과. 파일/헤더 단계에서 실패하면 null이다.</summary>
        public TableDataSnapshot Snapshot { get; }

        public TableDataAssetIndex Assets { get; }

        public IReadOnlyList<TableDataDiagnostic> Diagnostics => Log.Entries;

        public int ErrorCount => Log.ErrorCount;

        public int WarningCount => Log.WarningCount;

        public bool HasErrors => Log.HasErrors;

        /// <summary>Rebuild가 에셋을 만들어도 되는 상태인지. <b>오류가 하나라도 있으면 false</b>다.</summary>
        public bool CanRebuild => !HasErrors && Snapshot != null;

        public string Summary =>
            $"World {Snapshot?.Worlds.Count ?? 0} / Currency {Snapshot?.Currencies.Count ?? 0} / " +
            $"Item {Snapshot?.Items.Count ?? 0} / " +
            $"Monster {Snapshot?.Monsters.Count ?? 0} / " +
            $"Dungeon {Snapshot?.Dungeons.Count ?? 0} / " +
            $"Character {Snapshot?.Characters.Count ?? 0} / " +
            $"Skill {Snapshot?.Skills.Count ?? 0} / " +
            $"CharacterSkill {Snapshot?.CharacterSkills.Count ?? 0} / " +
            $"Building {Snapshot?.Buildings.Count ?? 0} / " +
            $"CharacterAcquisition {Snapshot?.CharacterAcquisitions.Count ?? 0} / " +
            $"RecruitmentType {Snapshot?.RecruitmentTypes.Count ?? 0} / " +
            $"RecruitmentPool {Snapshot?.RecruitmentPools.Count ?? 0} / " +
            $"RecruitmentAccess {Snapshot?.RecruitmentAccesses.Count ?? 0} / " +
            $"PartyConfig {Snapshot?.PartyConfigs.Count ?? 0} 행, " +
            $"CorruptionConfig {Snapshot?.CorruptionConfigs.Count ?? 0} 행, " +
            $"오류 {ErrorCount}건, 경고 {WarningCount}건";
    }

    /// <summary>
    /// CSV 아홉 장을 읽고 <b>모든</b> 문제를 모아 보고한다. <b>에셋도 폴더도 CSV도 만들지 않고 고치지도
    /// 않는다</b> - Validate는 순수하게 읽기만 하는 동작이라, 사람이 결과를 보고 판단할 때까지
    /// 프로젝트 상태가 한 글자도 바뀌지 않는다.
    ///
    /// <b>첫 오류에서 멈추지 않는다.</b> World → Currency → Item → Monster → Dungeon → Character →
    /// Skill → CharacterSkill → Building 순서로 끝까지 읽고, 참조 무결성까지 검사한 뒤 한 번에
    /// 돌려준다. 순서가 정해져 있는 이유는 뒤의 표가 앞의 표를 참조하기 때문이다(Monster는
    /// World / Currency / Item을, Dungeon은 World / Monster / Item을, CharacterSkill은
    /// Character / Skill을, Building은 Currency / Item을 가리킨다).
    /// 가리켜지는 표가 언제나 먼저 읽히므로, 참조를 확인할 때 스냅샷은 이미 완성되어 있다.
    ///
    /// 파일/헤더 단계에서 실패한 표는 행 검증을 건너뛴다 - 헤더가 어긋난 채 행을 읽으면 엉뚱한 칸을
    /// 가리키는 오류가 쏟아져 진짜 원인이 묻히기 때문이다. 그래도 나머지 표는 계속 읽는다.
    /// </summary>
    public static class TableDataValidator
    {
        /// <summary>
        /// ID가 비어 있는 생성 에셋을 진단에 적을 때 쓰는 표기. 빈 문자열을 그대로 찍으면 진단이
        /// "value ''"가 되어 무엇이 문제인지 읽을 수 없다.
        /// </summary>
        public const string EmptyIdLabel = "(빈 ID)";

        /// <summary>
        /// 아이템 설명의 숫자 키가 이름 키보다 얼마나 큰지. <b>데이터가 아니라 규칙</b>이라 코드에
        /// 둔다 - 표마다 다른 간격을 쓰기 시작하면 "이 아이템의 설명은 어디에 있는가"를 행마다 다시
        /// 확인해야 한다. 04_Item의 이름이 1..N, 설명이 10001..10000+N인 지금 저작 방식이 그대로
        /// 이 숫자다.
        /// </summary>
        public const int ItemDescriptionKeyOffset = 10000;

        /// <summary>메뉴와 테스트가 함께 쓰는 진입점. 부작용이 전혀 없다.
        /// 생성 에셋 쪽 점검까지 <b>아홉 도메인 전부</b>를 본다(기존 동작 그대로).</summary>
        public static TableDataValidationResult Validate()
        {
            return Validate(TableDataRebuildScope.All);
        }

        /// <summary>
        /// <paramref name="outputScope"/>는 <b>생성 에셋 쪽 점검의 범위만</b> 정한다.
        ///
        /// <b>CSV 입력 검증은 범위와 무관하게 언제나 아홉 표 전부다.</b> 파일/헤더/행/값/표 사이의
        /// 참조와 Localization·Motion Profile·Sprite 같은 <b>입력 자산</b> 조회는 하나도 줄어들지
        /// 않는다 - 범위를 좁혔다고 검사가 느슨해지면 "좁게 돌렸더니 통과했다"는 상태가 생긴다.
        ///
        /// 좁아지는 것은 <see cref="CheckOutputConflicts"/>와 <see cref="CheckOrphans"/>뿐이며,
        /// 이 둘은 <see cref="GeneratedOutputFolders"/>가 돌려주는 폴더만 점검한다. 단,
        /// Character-only 범위는 origin_world_id 참조를 잇기 위해 기존 World 생성 에셋을 읽어
        /// 정확히 하나인지 확인하지만, 절대 쓰거나 dirty로 표시하지 않는다.
        /// </summary>
        public static TableDataValidationResult Validate(TableDataRebuildScope outputScope)
        {
            TableDataRebuildScopes.EnsureSupported(outputScope, nameof(outputScope));

            var log = new TableDataDiagnosticLog();
            var assets = new TableDataAssetIndex();

            CsvTable worldTable = TableDataCsvReader.Read(
                TableDataPaths.WorldCsvPath, TableDataPaths.WorldCsvFileName, TableDataColumns.World, log);
            CsvTable currencyTable = TableDataCsvReader.Read(
                TableDataPaths.CurrencyCsvPath, TableDataPaths.CurrencyCsvFileName, TableDataColumns.Currency, log);
            CsvTable itemTable = TableDataCsvReader.Read(
                TableDataPaths.ItemCsvPath, TableDataPaths.ItemCsvFileName, TableDataColumns.Item, log);
            CsvTable monsterTable = TableDataCsvReader.Read(
                TableDataPaths.MonsterCsvPath, TableDataPaths.MonsterCsvFileName, TableDataColumns.Monster, log);
            CsvTable dungeonTable = TableDataCsvReader.Read(
                TableDataPaths.DungeonCsvPath, TableDataPaths.DungeonCsvFileName, TableDataColumns.Dungeon, log);
            CsvTable characterTable = TableDataCsvReader.Read(
                TableDataPaths.CharacterCsvPath, TableDataPaths.CharacterCsvFileName, TableDataColumns.Character, log);
            CsvTable skillTable = TableDataCsvReader.Read(
                TableDataPaths.SkillCsvPath, TableDataPaths.SkillCsvFileName, TableDataColumns.Skill, log);
            CsvTable characterSkillTable = TableDataCsvReader.Read(
                TableDataPaths.CharacterSkillCsvPath, TableDataPaths.CharacterSkillCsvFileName,
                TableDataColumns.CharacterSkill, log);
            CsvTable buildingTable = TableDataCsvReader.Read(
                TableDataPaths.BuildingCsvPath, TableDataPaths.BuildingCsvFileName,
                TableDataColumns.Building, log);
            CsvTable acquisitionTable = TableDataCsvReader.Read(
                TableDataPaths.CharacterAcquisitionCsvPath, TableDataPaths.CharacterAcquisitionCsvFileName,
                TableDataColumns.CharacterAcquisition, log);
            CsvTable recruitmentTypeTable = TableDataCsvReader.Read(
                TableDataPaths.RecruitmentTypeCsvPath, TableDataPaths.RecruitmentTypeCsvFileName,
                TableDataColumns.RecruitmentType, log);
            CsvTable recruitmentPoolTable = TableDataCsvReader.Read(
                TableDataPaths.RecruitmentPoolCsvPath, TableDataPaths.RecruitmentPoolCsvFileName,
                TableDataColumns.RecruitmentPool, log);
            CsvTable recruitmentAccessTable = TableDataCsvReader.Read(
                TableDataPaths.RecruitmentAccessCsvPath, TableDataPaths.RecruitmentAccessCsvFileName,
                TableDataColumns.RecruitmentAccess, log);
            CsvTable partyConfigTable = TableDataCsvReader.Read(
                TableDataPaths.PartyConfigCsvPath, TableDataPaths.PartyConfigCsvFileName,
                TableDataColumns.PartyConfig, log);
            CsvTable corruptionConfigTable = TableDataCsvReader.Read(
                TableDataPaths.CorruptionConfigCsvPath, TableDataPaths.CorruptionConfigCsvFileName,
                TableDataColumns.CorruptionConfig, log);
            CsvTable purificationConfigTable = TableDataCsvReader.Read(
                TableDataPaths.PurificationConfigCsvPath, TableDataPaths.PurificationConfigCsvFileName,
                TableDataColumns.PurificationConfig, log);

            var snapshot = new TableDataSnapshot();
            bool allTablesRead = worldTable != null && currencyTable != null && itemTable != null
                                 && monsterTable != null && dungeonTable != null
                                 && characterTable != null && skillTable != null && characterSkillTable != null
                                 && buildingTable != null
                                 && acquisitionTable != null && recruitmentTypeTable != null
                                 && recruitmentPoolTable != null && recruitmentAccessTable != null
                                 && partyConfigTable != null;
            allTablesRead = allTablesRead && corruptionConfigTable != null;
            allTablesRead = allTablesRead && purificationConfigTable != null;

            try
            {
                if (worldTable != null) ValidateWorlds(worldTable, snapshot, log);
                if (currencyTable != null) ValidateCurrencies(currencyTable, snapshot, assets, log);
                if (itemTable != null) ValidateItems(itemTable, snapshot, assets, log);
                if (monsterTable != null) ValidateMonsters(monsterTable, snapshot, assets, log);
                if (corruptionConfigTable != null) ValidateCorruptionConfigs(corruptionConfigTable, snapshot, log);
                if (dungeonTable != null) ValidateDungeons(dungeonTable, snapshot, assets, log);
                if (characterTable != null) ValidateCharacters(characterTable, snapshot, assets, log);
                if (skillTable != null) ValidateSkills(skillTable, snapshot, assets, log);
                if (characterSkillTable != null) ValidateCharacterSkills(characterSkillTable, snapshot, log);

                // Building은 Currency와 Item을 가리키므로 두 표가 모두 읽힌 뒤에 온다.
                if (buildingTable != null) ValidateBuildings(buildingTable, snapshot, log);
                if (purificationConfigTable != null) ValidatePurificationConfigs(purificationConfigTable, snapshot, log);

                // 모집 네 표는 맨 뒤다 - Character와 Building을 가리키므로 두 표가 이미 앞에 있어야
                // 한다. 넷 사이의 순서도 같은 규칙을 따른다(RecruitmentType → Pool/Access).
                if (acquisitionTable != null) ValidateCharacterAcquisitions(acquisitionTable, snapshot, log);
                if (recruitmentTypeTable != null) ValidateRecruitmentTypes(recruitmentTypeTable, snapshot, log);
                if (recruitmentPoolTable != null) ValidateRecruitmentPools(recruitmentPoolTable, snapshot, log);
                if (recruitmentAccessTable != null) ValidateRecruitmentAccesses(recruitmentAccessTable, snapshot, log);

                // 파티 설정은 어느 표도 가리키지 않으므로 순서에 매이지 않는다 - 맨 뒤에 이어 붙여
                // 앞의 순서를 한 칸도 건드리지 않는다.
                if (partyConfigTable != null) ValidatePartyConfigs(partyConfigTable, snapshot, log);

                // 출력 쪽 충돌과 orphan은 표가 다 읽힌 뒤에만 의미가 있다. 절반만 읽힌 상태에서 orphan을
                // 세면 "CSV에서 사라졌다"가 아니라 "아직 못 읽었다"를 보고하게 된다.
                if (allTablesRead)
                {
                    CheckOutputConflicts(snapshot, log, outputScope);
                    CheckOrphans(snapshot, log, outputScope);
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

        // ---- Currency ----

        /// <summary>
        /// Currency.csv 한 장. 규칙은 Item.csv와 거의 같다 - id 형식/중복, 이름 참조, 아이콘, 순서.
        /// 다른 점은 두 가지다. 첫째, <b>이름이 필수</b>다: 재화는 화면에 금액과 함께 이름이나
        /// 아이콘으로만 나타나므로, 활성 재화에 이름이 없으면 사람이 무엇을 얻었는지 알 수 없다
        /// (World / Dungeon과 같은 판정이다). 둘째, <b>아이콘은 완전한 선택 항목</b>이라 못 찾거나
        /// 여럿이어도 경고에 그친다(<see cref="ReadCurrencyIcon"/> 참고) - 아이콘 한 장 때문에 재화
        /// 표 전체가 반영되지 못하는 일을 만들지 않는다.
        ///
        /// <b>currency_id는 다듬지 않는다.</b> 저장 데이터와 Monster.csv가 함께 쓰는 키라,
        /// <see cref="TableDataFieldRules.TryReadRequiredId"/>가 앞뒤 공백이 붙은 값을 형식 오류로
        /// 걸러 낸다 - 소문자화도 공백 제거도 어디에서도 하지 않는다.
        /// </summary>
        private static void ValidateCurrencies(
            CsvTable table, TableDataSnapshot snapshot, TableDataAssetIndex assets, TableDataDiagnosticLog log)
        {
            string file = table.FileName;
            var displayOrders = new Dictionary<int, int>();

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new CurrencyRow { Line = line };

                string idRaw = table.Get(record, TableDataColumns.CurrencyId);
                bool idOk = TableDataFieldRules.TryReadRequiredId(
                    file, line, TableDataColumns.CurrencyId, idRaw, log, out string id);
                row.Id = id;

                if (idOk && snapshot.CurrenciesById.TryGetValue(id, out CurrencyRow existing))
                {
                    log.Error(file, line, TableDataColumns.CurrencyId, id,
                        $"currency_id가 {existing.Line}행과 중복됩니다 - 먼저 나온 행만 사용됩니다.");
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

                ReadCurrencyIcon(table, record, file, line, assets, row, log);

                // memo는 사람이 읽는 칸이라 검증하지 않는다.

                if (!idOk) continue;

                snapshot.Currencies.Add(row);
                snapshot.CurrenciesById[row.Id] = row;
            }
        }

        /// <summary>
        /// 재화 아이콘 한 칸. <b>재화 아이콘은 처음부터 끝까지 선택 항목이라, 세 가지 실패가 모두
        /// 경고다</b> - 비어 있든, 이름을 적었는데 찾지 못하든, 같은 이름이 여럿이라 정할 수 없든
        /// 결과는 같다: 아이콘을 비워 두고(<see cref="CurrencyRow.Icon"/>는 null) 넘어가며,
        /// Validate도 Rebuild도 이 때문에 멈추지 않는다. <b>정확히 하나를 찾은 경우에만</b> 아이콘을
        /// 연결한다.
        ///
        /// <b>아이템 아이콘(<see cref="ReadIcon"/>)과 일부러 다르다.</b> 그쪽은 못 찾은 이름을 오류로
        /// 막는데, 재화는 아이콘이 아직 한 장도 없는 상태에서 표부터 만들어 나가는 중이라 같은 규칙을
        /// 적용하면 아이콘 한 장이 없다는 이유로 재화 표 전체가 반영되지 못한다. 아이콘이 빠진 것은
        /// 화면에서 바로 보이는 종류의 문제이고, 경고는 그대로 남으므로 놓치지도 않는다.
        ///
        /// <b>폴더가 없는 것 자체도 문제가 아니다.</b> 아이콘을 쓰지 않는 동안에는
        /// <see cref="TableDataPaths.CurrencyIconRoot"/>가 없는 것이 정상이고, 그때는 어떤 이름도
        /// 찾을 수 없으므로 "0개" 경고가 된다.
        /// </summary>
        private static void ReadCurrencyIcon(
            CsvTable table, CsvRecord record, string file, int line,
            TableDataAssetIndex assets, CurrencyRow row, TableDataDiagnosticLog log)
        {
            string key = table.Get(record, TableDataColumns.IconKey);

            if (string.IsNullOrEmpty(key))
            {
                log.Warning(file, line, TableDataColumns.IconKey, key,
                    "icon_key가 비어 있습니다 - 재화가 아이콘 없이 금액과 이름만으로 표시됩니다(선택 항목).");
                return;
            }

            AssetLookupResult result = assets.FindCurrencyIcon(key, out Sprite icon, out int count);
            switch (result)
            {
                case AssetLookupResult.Found:
                    row.Icon = icon;
                    return;

                case AssetLookupResult.Ambiguous:
                    log.Warning(file, line, TableDataColumns.IconKey, key,
                        $"'{TableDataPaths.CurrencyIconRoot}' 아래에 이름이 정확히 '{key}'인 Sprite가 {count}개 있어 " +
                        "어느 것을 쓸지 정할 수 없습니다 - 아이콘 없이 생성합니다(선택 항목). " +
                        "아이콘을 붙이려면 이름을 하나로 만드세요.");
                    return;

                default:
                    log.Warning(file, line, TableDataColumns.IconKey, key,
                        $"'{TableDataPaths.CurrencyIconRoot}' 아래에서 이름이 정확히 '{key}'인 Sprite를 찾지 못했습니다(0개) - " +
                        "아이콘 없이 생성합니다(선택 항목). 재화 아이콘은 이 폴더에서만 찾습니다 - " +
                        "붙이려면 그 이름의 Sprite를 여기에 두세요(폴더가 없다면 만들어야 합니다).");
                    return;
            }
        }

        // ---- Item ----

        /// <summary>
        /// Item.csv 한 장. 다른 표와 다른 점이 하나 있다 - <b>item_id는 저장 파일의 키</b>라서, 같은
        /// id를 가진 수동 ItemDefinition이 프로젝트에 이미 있으면 오류로 막는다. 그대로 두면 같은 저장
        /// 키를 가진 정의가 둘이 되어, 인벤토리가 어느 쪽을 그릴지가 목록 작성 순서에 달리게 된다.
        /// </summary>
        private static void ValidateItems(
            CsvTable table, TableDataSnapshot snapshot, TableDataAssetIndex assets, TableDataDiagnosticLog log)
        {
            string file = table.FileName;
            var displayOrders = new Dictionary<int, int>();

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new ItemRow { Line = line };

                string idRaw = table.Get(record, TableDataColumns.ItemId);
                bool idOk = TableDataFieldRules.TryReadRequiredId(
                    file, line, TableDataColumns.ItemId, idRaw, log, out string id);
                row.Id = id;

                if (idOk && snapshot.ItemsById.TryGetValue(id, out ItemRow existing))
                {
                    log.Error(file, line, TableDataColumns.ItemId, id,
                        $"item_id가 {existing.Line}행과 중복됩니다 - 먼저 나온 행만 사용됩니다.");
                    idOk = false;
                }

                if (idOk) CheckManualItemConflict(file, line, id, assets, log);

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

                ReadItemLocalizedTexts(table, record, file, line, row, log);

                ReadIcon(table, record, file, line, assets, row, log);

                if (!idOk) continue;

                snapshot.Items.Add(row);
                snapshot.ItemsById[row.Id] = row;
            }
        }

        /// <summary>
        /// 아이템의 이름/설명 두 참조를 <b>한 덩어리로</b> 읽는다. 툴팁이 이름과 설명 두 줄로
        /// 이루어지므로, 활성 아이템에서 한쪽만 있는 상태는 "아직 안 적었다"가 아니라 반쪽만 그려지는
        /// 화면이다 - 그래서 enabled=1인 행은 <b>둘 다</b> 요구한다.
        ///
        /// 두 참조는 서로 독립이 아니다. 같은 아이템의 이름과 설명은 <b>같은 카테고리</b>에 있고,
        /// 설명의 숫자 키는 이름 키에 <see cref="ItemDescriptionKeyOffset"/>을 더한 값이다 - 이 규칙을
        /// 검사하지 않으면 엉뚱한 아이템의 설명을 가리키는 행이 조용히 통과한다. 존재 여부는
        /// <see cref="TableDataFieldRules.TryResolveLocalizedEntry"/>가 이미 보므로 여기서는
        /// <b>두 칸의 관계만</b> 본다.
        /// </summary>
        private static void ReadItemLocalizedTexts(
            CsvTable table, CsvRecord record, string file, int line, ItemRow row, TableDataDiagnosticLog log)
        {
            // 이름은 활성 행의 필수 항목이다 - 인벤토리 슬롯과 툴팁이 모두 이 값을 그린다.
            row.Name = ReadLocalizedName(
                table, record, file, line, row.Enabled, nameRequiredWhenEnabled: true, log);

            row.Description = ReadItemDescription(table, record, file, line, row.Enabled, log);

            CheckItemDescriptionKeyRule(table, record, file, line, log);
        }

        /// <summary>
        /// description_category / description_key 두 칸. 판정은 이름 칸과 같은 모양이지만
        /// <see cref="ReadOptionalLocalizedPair"/>와 달리 <b>활성 행에서는 필수</b>라 따로 둔다 -
        /// 두 규칙을 한 함수에 매개변수로 섞으면 어느 표가 무엇을 요구하는지가 호출부에서만 보인다.
        /// </summary>
        private static LocalizedEntryRef ReadItemDescription(
            CsvTable table, CsvRecord record, string file, int line, bool enabled, TableDataDiagnosticLog log)
        {
            string categoryRaw = table.Get(record, TableDataColumns.DescriptionCategory);
            string keyRaw = table.Get(record, TableDataColumns.DescriptionKey);

            bool hasCategory = !string.IsNullOrEmpty(categoryRaw);
            bool hasKey = !string.IsNullOrEmpty(keyRaw);

            if (hasCategory && hasKey)
            {
                TableDataFieldRules.TryResolveLocalizedEntry(
                    file, line, TableDataColumns.DescriptionCategory, categoryRaw,
                    TableDataColumns.DescriptionKey, keyRaw, log, out LocalizedEntryRef entry);
                return entry;
            }

            if (!hasCategory && !hasKey)
            {
                if (enabled)
                {
                    log.Error(file, line, TableDataColumns.DescriptionCategory, categoryRaw,
                        "enabled=1인 행은 description_category와 description_key가 모두 필요합니다 - " +
                        "아이템 툴팁은 이름과 설명 두 줄로 이루어집니다.");
                }
                else
                {
                    log.Warning(file, line, TableDataColumns.DescriptionCategory, categoryRaw,
                        "설명 참조가 비어 있습니다 - 툴팁이 설명 없이 표시됩니다(비활성 행이라 경고입니다).");
                }

                return LocalizedEntryRef.None;
            }

            string emptyColumn = hasCategory ? TableDataColumns.DescriptionKey : TableDataColumns.DescriptionCategory;
            string emptyValue = hasCategory ? keyRaw : categoryRaw;

            log.Error(file, line, emptyColumn, emptyValue,
                "description_category와 description_key는 함께 있어야 합니다 - " +
                "한쪽만으로는 참조를 만들 수 없습니다.");

            return LocalizedEntryRef.None;
        }

        /// <summary>
        /// 이름 키와 설명 키의 <b>관계</b>만 본다. 네 칸 중 하나라도 비어 있거나 숫자로 읽히지 않으면
        /// 아무것도 알리지 않고 돌아간다 - 그 문제는 각 칸을 읽은 쪽이 이미 보고했고, 여기서 한 번 더
        /// 말하면 원인 하나에 오류 두 줄이 나온다.
        ///
        /// 더하기는 <c>checked</c>로 한다. <c>int.MaxValue</c>에 가까운 name_key가 들어오면 그냥
        /// 더한 값은 음수로 감싸 돌아가고, 그 음수가 우연히 description_key와 같을 수는 없더라도
        /// <b>"왜 틀렸는지"가 아니라 엉뚱한 기대값이 오류 메시지에 찍힌다</b>.
        /// </summary>
        private static void CheckItemDescriptionKeyRule(
            CsvTable table, CsvRecord record, string file, int line, TableDataDiagnosticLog log)
        {
            if (!TryReadKeyNumber(table.Get(record, TableDataColumns.NameCategory), out int nameCategory)) return;
            if (!TryReadKeyNumber(table.Get(record, TableDataColumns.NameKey), out int nameKey)) return;
            if (!TryReadKeyNumber(table.Get(record, TableDataColumns.DescriptionCategory), out int descCategory)) return;
            if (!TryReadKeyNumber(table.Get(record, TableDataColumns.DescriptionKey), out int descKey)) return;

            if (nameCategory != descCategory)
            {
                log.Error(file, line, TableDataColumns.DescriptionCategory,
                    table.Get(record, TableDataColumns.DescriptionCategory),
                    $"설명은 이름과 같은 카테고리여야 합니다(name_category {nameCategory}, " +
                    $"description_category {descCategory}) - 한 아이템의 이름과 설명이 서로 다른 " +
                    "String Table에 흩어지지 않게 합니다.");
                return;
            }

            int expectedKey;
            try
            {
                expectedKey = checked(nameKey + ItemDescriptionKeyOffset);
            }
            catch (OverflowException)
            {
                log.Error(file, line, TableDataColumns.NameKey, table.Get(record, TableDataColumns.NameKey),
                    $"name_key에 {ItemDescriptionKeyOffset}을 더하면 정수 범위를 넘어 설명 키를 만들 수 " +
                    "없습니다 - 이름 키를 더 작은 값으로 바꾸세요.");
                return;
            }

            if (descKey == expectedKey) return;

            log.Error(file, line, TableDataColumns.DescriptionKey, table.Get(record, TableDataColumns.DescriptionKey),
                $"description_key는 name_key + {ItemDescriptionKeyOffset}이어야 합니다" +
                $"(name_key {nameKey}이므로 {expectedKey}) - 지금 값은 {descKey}입니다.");
        }

        /// <summary>
        /// 카테고리/숫자 키 칸을 <b>관계 검사를 할 수 있을 때만</b> 숫자로 돌려준다. 형식이 어긋나면
        /// false다 - <see cref="TableDataFieldRules.TryResolveLocalizedEntry"/>가 같은 값을 같은 규칙
        /// (부호 없는 정수, 1 이상)으로 이미 보고 오류를 남겼기 때문에 여기서는 조용히 물러난다.
        /// </summary>
        private static bool TryReadKeyNumber(string raw, out int value)
        {
            return int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
        }

        private static void CheckManualItemConflict(
            string file, int line, string id, TableDataAssetIndex assets, TableDataDiagnosticLog log)
        {
            AssetLookupResult result = assets.FindManualItemByItemId(id, out ItemDefinition manual, out int count);
            if (result == AssetLookupResult.NotFound) return;

            string where = result == AssetLookupResult.Found
                ? $"'{AssetDatabase.GetAssetPath(manual)}'"
                : $"{count}개";

            log.Error(file, line, TableDataColumns.ItemId, id,
                $"같은 item_id를 가진 수동 ItemDefinition이 생성 폴더 밖에 있습니다({where}) - " +
                "item_id는 저장 파일의 키라 정의가 둘이면 어느 쪽이 인벤토리에 그려질지 정해지지 않습니다. " +
                "CSV의 id를 바꾸거나 수동 에셋의 Item Id를 정리한 뒤 다시 실행하세요(임포터는 수동 에셋을 고치지 않습니다).");
        }

        private static void ReadIcon(
            CsvTable table, CsvRecord record, string file, int line,
            TableDataAssetIndex assets, ItemRow row, TableDataDiagnosticLog log)
        {
            string key = table.Get(record, TableDataColumns.IconKey);

            if (string.IsNullOrEmpty(key))
            {
                log.Warning(file, line, TableDataColumns.IconKey, key,
                    "icon_key가 비어 있습니다 - 인벤토리 슬롯이 아이콘 없이 수량만 보여줍니다(선택 항목).");
                return;
            }

            AssetLookupResult result = assets.FindItemIcon(key, out Sprite icon, out int count);
            switch (result)
            {
                case AssetLookupResult.Found:
                    row.Icon = icon;
                    return;

                case AssetLookupResult.Ambiguous:
                    log.Error(file, line, TableDataColumns.IconKey, key,
                        $"'{TableDataPaths.ItemIconRoot}' 아래에 이름이 정확히 '{key}'인 Sprite가 {count}개 있습니다 - " +
                        "어느 것을 쓸지 정할 수 없으니 이름을 하나로 만드세요.");
                    return;

                default:
                    log.Error(file, line, TableDataColumns.IconKey, key,
                        $"'{TableDataPaths.ItemIconRoot}' 아래에서 이름이 정확히 '{key}'인 Sprite를 찾지 못했습니다(0개). " +
                        "아이템 아이콘은 이 폴더에서만 찾습니다 - 다른 곳에 있으면 옮기거나 여기에 두세요. " +
                        "스프라이트 시트로 자른 이미지는 하위 Sprite 이름을 적어야 합니다.");
                    return;
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

                // base_monster_id는 이 행만 보고 판정할 수 없다(뒤에 나오는 행을 가리켜도 되므로).
                // 여기서는 값의 형식만 보고, 실재 여부는 표를 다 읽은 뒤 CheckBaseReferences가 본다.
                row.BaseMonsterId = ReadBaseMonsterId(table, record, file, line, log);

                // 보상 칸은 MonsterRewardRules가 읽는다. 칸을 이름으로만 넘기므로 표의 칸 순서와
                // 무관하고, 규칙만 따로 테스트할 수 있다.
                string GetCell(string column) => table.Get(record, column);
                MonsterRewardRules.ReadDrops(file, line, GetCell, snapshot, row, log);
                MonsterRewardRules.ReadCurrency(file, line, GetCell, row, log);

                // 재화 id의 <b>실재</b>는 규칙이 아니라 표 사이의 참조라서 여기서 본다 - Currency.csv가
                // Monster.csv보다 먼저 읽히므로 스냅샷은 이 시점에 이미 완성되어 있다.
                CheckCurrencyReference(file, snapshot, row, log);

                if (!idOk) continue;

                snapshot.Monsters.Add(row);
                snapshot.MonstersById[row.Id] = row;
            }

            CheckBaseReferences(file, snapshot, log);
        }

        /// <summary>
        /// 처치 재화 보상이 가리키는 재화가 <b>Currency.csv에 실제로 있는 활성 행</b>인지 본다.
        /// <see cref="MonsterRewardRules.ReadCurrency"/>는 세 칸의 <b>형식과 짝</b>만 보고, 표 사이의
        /// 참조는 여기가 본다 - 규칙 시험이 Currency 표 없이도 돌 수 있게 나눠 둔 것이다.
        ///
        /// 비교는 <b>다듬지 않은 값끼리의 Ordinal 완전 일치</b>다. 'jewel'과 'Jewel'은 다른 재화이고,
        /// 공백이 붙은 값은 애초에 형식 검사에서 걸러졌으므로 여기까지 오지 않는다.
        ///
        /// <b>enabled=0인 재화를 가리키는 것도 오류다.</b> 던전이 비활성 몬스터/아이템을 가리킬 수 없는
        /// 것과 같은 이유다 - 카탈로그에 없는 재화를 지급하면 화면에 이름도 아이콘도 없는 보상이 나온다.
        /// 몬스터 자신이 enabled=0이어도 마찬가지로 본다: 비활성 행의 잘못된 참조를 통과시키면 다시
        /// 켜는 순간 조용히 깨진다.
        /// </summary>
        private static void CheckCurrencyReference(
            string file, TableDataSnapshot snapshot, MonsterRow row, TableDataDiagnosticLog log)
        {
            if (string.IsNullOrEmpty(row.CurrencyId)) return;

            if (!snapshot.CurrenciesById.TryGetValue(row.CurrencyId, out CurrencyRow currency))
            {
                log.Error(file, row.Line, TableDataColumns.CurrencyId, row.CurrencyId,
                    $"{TableDataPaths.CurrencyCsvFileName}에 없는 currency_id입니다 - " +
                    "처치 재화 보상은 그 표에 실제로 있는 행만 가리킬 수 있습니다(대소문자를 구분합니다).");
                return;
            }

            if (!currency.Enabled)
            {
                log.Error(file, row.Line, TableDataColumns.CurrencyId, row.CurrencyId,
                    $"enabled=0인 재화({TableDataPaths.CurrencyCsvFileName} {currency.Line}행)를 지급합니다 - " +
                    "처치 보상에는 활성 재화만 넣을 수 있습니다.");
            }
        }

        /// <summary>
        /// base_monster_id 칸의 <b>형식만</b> 읽는다. 비어 있으면 그대로 비운다 - 묶음 참조는 선택
        /// 항목이고, 없다고 해서 무엇이 빠지지도 채워지지도 않는다.
        /// </summary>
        private static string ReadBaseMonsterId(
            CsvTable table, CsvRecord record, string file, int line, TableDataDiagnosticLog log)
        {
            string raw = table.Get(record, TableDataColumns.BaseMonsterId);
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            if (!TableDataFieldRules.IsValidId(raw))
            {
                log.Error(file, line, TableDataColumns.BaseMonsterId, raw,
                    $"base_monster_id 형식이 맞지 않습니다 - {TableDataFieldRules.IdPatternText} 를 만족해야 합니다.");
                return string.Empty;
            }

            return raw;
        }

        /// <summary>
        /// base_monster_id가 가리키는 행이 실제로 있는지 <b>표를 다 읽은 뒤에</b> 본다. 앞에서 검사하면
        /// 아직 읽지 않은 뒷줄을 가리키는 정상 데이터가 "없는 id"로 걸리기 때문이다(forward reference 허용).
        ///
        /// <b>여기서 아무것도 상속시키지 않는다.</b> base는 사람이 표를 묶어 보기 위한 분류값일 뿐이고,
        /// 값을 물려받는 경로는 검증에도 생성에도 존재하지 않는다.
        /// </summary>
        private static void CheckBaseReferences(string file, TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            foreach (MonsterRow row in snapshot.Monsters)
            {
                if (string.IsNullOrEmpty(row.BaseMonsterId)) continue;

                if (string.Equals(row.BaseMonsterId, row.Id, StringComparison.Ordinal))
                {
                    log.Error(file, row.Line, TableDataColumns.BaseMonsterId, row.BaseMonsterId,
                        "base_monster_id가 자기 자신을 가리킵니다 - 묶음의 기준은 다른 행이어야 합니다.");
                    row.BaseMonsterId = string.Empty;
                    continue;
                }

                if (snapshot.MonstersById.ContainsKey(row.BaseMonsterId)) continue;

                log.Error(file, row.Line, TableDataColumns.BaseMonsterId, row.BaseMonsterId,
                    $"{TableDataPaths.MonsterCsvFileName}에 없는 monster_id입니다 - " +
                    "base_monster_id는 이 표에 실제로 있는 행만 가리킬 수 있습니다.");
                row.BaseMonsterId = string.Empty;
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

        // ---- CorruptionConfig ----

        private static void ValidateCorruptionConfigs(CsvTable table, TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new CorruptionConfigRow { Line = line };
                bool idOk = TableDataFieldRules.TryReadRequiredId(table.FileName, line, TableDataColumns.ConfigId, table.Get(record, TableDataColumns.ConfigId), log, out string id);
                row.Id = id;
                if (idOk && snapshot.CorruptionConfigsById.TryGetValue(id, out CorruptionConfigRow prior))
                { log.Error(table.FileName, line, TableDataColumns.ConfigId, id, $"config_id가 {prior.Line}행과 중복됩니다."); idOk = false; }
                TableDataFieldRules.TryReadEnabled(table.FileName, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log, out row.Enabled);
                TableDataFieldRules.TryReadIntAtLeast(table.FileName, line, TableDataColumns.MaxCorruption, table.Get(record, TableDataColumns.MaxCorruption), 1, log, out row.MaxCorruption);
                TableDataFieldRules.TryReadIntAtLeast(table.FileName, line, TableDataColumns.WarningThresholdPercent, table.Get(record, TableDataColumns.WarningThresholdPercent), 1, log, out row.WarningThresholdPercent);
                TableDataFieldRules.TryReadIntAtLeast(table.FileName, line, TableDataColumns.DangerThresholdPercent, table.Get(record, TableDataColumns.DangerThresholdPercent), 1, log, out row.DangerThresholdPercent);
                TableDataFieldRules.TryReadIntAtLeast(table.FileName, line, TableDataColumns.WarningStaminaCostMultiplier, table.Get(record, TableDataColumns.WarningStaminaCostMultiplier), 1, log, out row.WarningStaminaCostMultiplier);
                TableDataFieldRules.TryReadIntAtLeast(table.FileName, line, TableDataColumns.DangerStaminaCostMultiplier, table.Get(record, TableDataColumns.DangerStaminaCostMultiplier), 1, log, out row.DangerStaminaCostMultiplier);
                if (row.WarningThresholdPercent >= 100 || row.DangerThresholdPercent > 100 || row.DangerThresholdPercent <= row.WarningThresholdPercent)
                    log.Error(table.FileName, line, TableDataColumns.DangerThresholdPercent, row.DangerThresholdPercent.ToString(), "위험 기준은 주의 기준보다 크고 100 이하여야 합니다.");
                if (row.DangerStaminaCostMultiplier < row.WarningStaminaCostMultiplier)
                    log.Error(table.FileName, line, TableDataColumns.DangerStaminaCostMultiplier, row.DangerStaminaCostMultiplier.ToString(), "위험 배율은 주의 배율 이상이어야 합니다.");
                if (!idOk) continue;
                snapshot.CorruptionConfigs.Add(row); snapshot.CorruptionConfigsById[row.Id] = row;
            }
            int defaults = 0; foreach (CorruptionConfigRow row in snapshot.CorruptionConfigs) if (row.Enabled && row.Id == "default") defaults++;
            if (defaults != 1) log.Error(table.FileName, TableDataDiagnostic.FileLevelRow, TableDataColumns.ConfigId, "default", "활성 default 설정이 정확히 하나여야 합니다.");
        }

        // ---- Dungeon ----

        private static void ValidatePurificationConfigs(CsvTable table, TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new PurificationConfigRow { Line = line };
                bool idOk = TableDataFieldRules.TryReadRequiredId(table.FileName, line,
                    TableDataColumns.PurificationTypeId, table.Get(record, TableDataColumns.PurificationTypeId), log, out string id);
                row.Id = id;
                if (idOk && snapshot.PurificationConfigsById.TryGetValue(id, out PurificationConfigRow prior))
                { log.Error(table.FileName, line, TableDataColumns.PurificationTypeId, id, $"purification_type_id가 {prior.Line}행과 중복됩니다."); idOk = false; }
                bool buildingOk = TableDataFieldRules.TryReadRequiredId(table.FileName, line,
                    TableDataColumns.RequiredBuildingId, table.Get(record, TableDataColumns.RequiredBuildingId), log, out string buildingId);
                row.RequiredBuildingId = buildingId;
                if (buildingOk && !snapshot.BuildingsById.ContainsKey(buildingId))
                    log.Error(table.FileName, line, TableDataColumns.RequiredBuildingId, buildingId, "Building.csv에 없는 building_id입니다.");
                TableDataFieldRules.TryReadIntAtLeast(table.FileName, line, TableDataColumns.PurificationIntervalSeconds, table.Get(record, TableDataColumns.PurificationIntervalSeconds), 1, log, out row.IntervalSeconds);
                TableDataFieldRules.TryReadIntAtLeast(table.FileName, line, TableDataColumns.PurificationValuePerInterval, table.Get(record, TableDataColumns.PurificationValuePerInterval), 1, log, out row.ValuePerInterval);
                TableDataFieldRules.TryReadIntAtLeast(table.FileName, line, TableDataColumns.BaseSlotCount, table.Get(record, TableDataColumns.BaseSlotCount), 1, log, out row.BaseSlotCount);
                TableDataFieldRules.TryReadEnabled(table.FileName, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log, out row.Enabled);
                if (!idOk) continue;
                snapshot.PurificationConfigs.Add(row); snapshot.PurificationConfigsById[row.Id] = row;
            }
        }

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
                ReadRewardList(table, record, file, line, snapshot, row, log);

                if (row.Enabled)
                {
                    if (TableDataFieldRules.TryReadFiniteDoubleAtLeast(
                            file, line, TableDataColumns.CorruptionGainPerDefeat,
                            table.Get(record, TableDataColumns.CorruptionGainPerDefeat), 0d, log,
                            out double gain))
                    {
                        row.CorruptionGainPerDefeat = gain;
                    }
                }

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

        /// <summary>
        /// 대표 보상 목록. monster_ids와 <b>같은 방식</b>으로 Item.csv의 행을 가리킨다 - 프로젝트를
        /// 뒤져 ItemDefinition을 찾지 않는다. 아이템의 원천이 Item.csv 하나가 된 뒤로, 표 밖의 에셋을
        /// 가리킬 수 있게 두면 "CSV에 없는 보상"이 생겨 어느 쪽이 진짜인지가 흐려지기 때문이다.
        /// </summary>
        private static void ReadRewardList(
            CsvTable table, CsvRecord record, string file, int line,
            TableDataSnapshot snapshot, DungeonRow row, TableDataDiagnosticLog log)
        {
            string raw = table.Get(record, TableDataColumns.RewardItemIds);
            TableDataFieldRules.ReadIdList(file, line, TableDataColumns.RewardItemIds, raw, log, row.RewardItemIds);

            if (row.RewardItemIds.Count == 0)
            {
                log.Warning(file, line, TableDataColumns.RewardItemIds, raw,
                    "대표 보상 목록이 비어 있습니다 - 던전 상세에 보상 칸이 하나도 표시되지 않습니다.");
                return;
            }

            foreach (string itemId in row.RewardItemIds)
            {
                if (!snapshot.ItemsById.TryGetValue(itemId, out ItemRow item))
                {
                    log.Error(file, line, TableDataColumns.RewardItemIds, itemId,
                        $"{TableDataPaths.ItemCsvFileName}에 없는 item_id입니다.");
                    continue;
                }

                if (!item.Enabled)
                {
                    log.Error(file, line, TableDataColumns.RewardItemIds, itemId,
                        $"enabled=0인 아이템({TableDataPaths.ItemCsvFileName} {item.Line}행)을 참조합니다 - " +
                        "던전 보상에는 활성 아이템만 넣을 수 있습니다.");
                }
            }
        }

        // ---- Character ----

        /// <summary>
        /// Character.csv 한 장. 다른 표와 다른 점이 세 가지 있다.
        ///
        /// 첫째, <b>id 형식이 이 표에서만 넓다</b>. 표준 ID 형식 외에 기존 캐릭터 여섯 개의
        /// PascalCase id를 예외로 인정한다(<see cref="TableDataFieldRules.IsValidCharacterId"/>) -
        /// 그 id들은 이미 저장 데이터가 쓰고 있어 바꿀 수 없기 때문이다. <b>전역 ID 정규식은 손대지
        /// 않는다</b>: 다른 표는 조금도 헐거워지지 않으며, 여섯 개에 없는 PascalCase는 여기서도 오류다.
        ///
        /// 둘째, <b>같은 id를 가진 수동 CharacterDefinition은 충돌이 아니다</b>. Item.csv가 수동
        /// ItemDefinition과의 id 겹침을 오류로 막는 것과 <b>일부러 다르다</b> - 지금은 생성 에셋과
        /// Assets/Data 이하의 수동 에셋이 같은 id로 함께 존재하는 것이 정상 상태이고(로스터는 여전히
        /// 수동 에셋을 쓴다), 그것을 오류로 막으면 표를 만들자마자 모든 행이 실패한다.
        ///
        /// 셋째, <b>모션 프로필은 활성 여부와 무관하게 필수</b>다. 프로필이 없는 캐릭터는 화면에 세울
        /// 수 없고, 그 상태를 나중에 켜는 순간 조용히 깨지기 때문이다.
        /// </summary>
        private static void ValidateCharacters(
            CsvTable table, TableDataSnapshot snapshot, TableDataAssetIndex assets, TableDataDiagnosticLog log)
        {
            string file = table.FileName;
            var displayOrders = new Dictionary<int, int>();

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new CharacterRow { Line = line };

                string idRaw = table.Get(record, TableDataColumns.CharacterId);
                bool idOk = TableDataFieldRules.TryReadRequiredCharacterId(
                    file, line, TableDataColumns.CharacterId, idRaw, log, out string id);
                row.Id = id;

                if (idOk && snapshot.CharactersById.TryGetValue(id, out CharacterRow existing))
                {
                    log.Error(file, line, TableDataColumns.CharacterId, id,
                        $"character_id가 {existing.Line}행과 중복됩니다 - 먼저 나온 행만 사용됩니다.");
                    idOk = false;
                }

                if (TableDataFieldRules.TryReadEnabled(
                        file, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log, out bool enabled))
                {
                    row.Enabled = enabled;
                }

                if (TableDataFieldRules.TryReadIntAtLeast(
                        file, line, TableDataColumns.DisplayOrder, table.Get(record, TableDataColumns.DisplayOrder),
                        0, log, out int order))
                {
                    row.DisplayOrder = order;
                    TableDataFieldRules.CheckDuplicateDisplayOrder(file, line, order, displayOrders, log);
                }

                // 캐릭터 이름은 World / Currency / Dungeon과 같은 판정이다 - 활성 캐릭터에 이름이 없으면
                // 목록에서 무엇을 고르는지 알 수 없다.
                row.Name = ReadLocalizedName(
                    table, record, file, line, row.Enabled, nameRequiredWhenEnabled: true, log);

                row.OriginWorldId = ReadWorldReference(
                    table, record, file, line, row.Enabled, snapshot, log, TableDataColumns.OriginWorldId);

                ReadCharacterMotionProfile(table, record, file, line, assets, row, log);
                ReadPortrait(table, record, file, line, assets, row, log);

                if (TableDataFieldRules.TryReadOptionalIntAtLeast(
                        file, line, TableDataColumns.BaseMaxHealth, table.Get(record, TableDataColumns.BaseMaxHealth),
                        1, log, out bool hasHealth, out int health))
                {
                    row.HasBaseMaxHealth = hasHealth;
                    row.BaseMaxHealth = health;
                }

                if (TableDataFieldRules.TryReadIntAtLeast(
                        file, line, TableDataColumns.MaxStamina, table.Get(record, TableDataColumns.MaxStamina),
                        1, log, out int stamina))
                {
                    row.MaxStamina = stamina;
                }
                bool hasDefaultConfig = snapshot.CorruptionConfigsById.TryGetValue("default", out CorruptionConfigRow config)
                                        && config.Enabled;
                if (TableDataFieldRules.TryReadIntAtLeast(file, line, TableDataColumns.BaseCorruption, table.Get(record, TableDataColumns.BaseCorruption), 0, log, out int corruption))
                {
                    row.BaseCorruption = corruption;
                    if (hasDefaultConfig && corruption > config.MaxCorruption)
                        log.Error(file, line, TableDataColumns.BaseCorruption, corruption.ToString(), "base_corruption은 default max_corruption 이하여야 합니다.");
                }

                // 새 게임 시작 구성은 <b>모든 행이 반드시 밝혀야 하는 값</b>이다 - 비워 두면 "정하지
                // 않았다"가 곧 "주지 않는다"로 조용히 굳으므로, enabled와 같은 엄격한 0/1로 받는다.
                if (TableDataFieldRules.TryReadFlag(
                        file, line, TableDataColumns.InitiallyOwned,
                        table.Get(record, TableDataColumns.InitiallyOwned), log, out bool initiallyOwned))
                {
                    row.InitiallyOwned = initiallyOwned;
                }

                // memo는 사람이 읽는 칸이라 검증하지 않는다.

                if (!idOk) continue;

                snapshot.Characters.Add(row);
                snapshot.CharactersById[row.Id] = row;
            }
        }

        /// <summary>
        /// 캐릭터의 모션 프로필 한 칸. 이름이 정확히 일치하는 <see cref="CharacterMotionProfile"/>을
        /// 찾고, <b>찾은 뒤에 재생 가능한지까지 본다</b> - 판정은 런타임과 <b>같은 규칙</b>
        /// (<see cref="CharacterMotionProfile.IsPlayable"/>)을 그대로 쓴다. 여기서 별도 기준을 세우면
        /// "임포트는 통과했는데 로스터가 목록에서 빼 버리는" 캐릭터가 생긴다.
        /// </summary>
        private static void ReadCharacterMotionProfile(
            CsvTable table, CsvRecord record, string file, int line,
            TableDataAssetIndex assets, CharacterRow row, TableDataDiagnosticLog log)
        {
            string key = table.Get(record, TableDataColumns.MotionProfileKey);

            if (string.IsNullOrEmpty(key))
            {
                log.Error(file, line, TableDataColumns.MotionProfileKey, key,
                    "motion_profile_key는 필수입니다 - 모션 데이터의 원천이 없으면 이 캐릭터는 화면에 세울 수 없습니다.");
                return;
            }

            AssetLookupResult result = assets.FindCharacterMotionProfile(key, out CharacterMotionProfile profile, out int count);
            switch (result)
            {
                case AssetLookupResult.Found:
                    if (!CharacterMotionProfile.IsPlayable(profile))
                    {
                        log.Error(file, line, TableDataColumns.MotionProfileKey, key,
                            $"'{key}'에 재생 가능한 Base Idle 프레임이 없습니다 - 런타임(CharacterRoster)이 " +
                            "이 캐릭터를 목록에서 제외하므로, 표에서 먼저 막습니다.");
                        return;
                    }

                    row.MotionProfile = profile;
                    return;

                case AssetLookupResult.Ambiguous:
                    log.Error(file, line, TableDataColumns.MotionProfileKey, key,
                        $"이름이 정확히 '{key}'인 CharacterMotionProfile이 {count}개 있습니다 - " +
                        "어느 것을 쓸지 정할 수 없으니 에셋 이름을 하나로 만드세요.");
                    return;

                default:
                    log.Error(file, line, TableDataColumns.MotionProfileKey, key,
                        $"이름이 정확히 '{key}'인 CharacterMotionProfile 에셋을 찾지 못했습니다(0개). " +
                        "몬스터용 MonsterMotionProfile은 여기에 쓸 수 없습니다.");
                    return;
            }
        }

        /// <summary>
        /// 초상화 한 칸. <b>비어 있는 것이 정상</b>이며 그때는 런타임이 Base Idle 첫 프레임을 대신
        /// 쓴다(<see cref="Character.CharacterDefinition.Portrait"/>의 기존 폴백). 판정은 몬스터의
        /// preview_sprite_key와 같다 - 빈 칸은 경고, <b>이름을 적었는데 찾지 못한 것은 오류</b>다
        /// (사람이 이름을 적었다는 것은 그 그림을 쓰겠다는 뜻이므로 조용히 비우지 않는다).
        /// </summary>
        private static void ReadPortrait(
            CsvTable table, CsvRecord record, string file, int line,
            TableDataAssetIndex assets, CharacterRow row, TableDataDiagnosticLog log)
        {
            string key = table.Get(record, TableDataColumns.PortraitKey);

            if (string.IsNullOrEmpty(key))
            {
                log.Warning(file, line, TableDataColumns.PortraitKey, key,
                    "portrait_key가 비어 있습니다 - 런타임이 Motion Profile의 Base Idle 첫 프레임을 대신 씁니다(선택 항목).");
                return;
            }

            ResolveSprite(assets, file, line, TableDataColumns.PortraitKey, key, log, out Sprite sprite);
            row.Portrait = sprite;
        }

        // ---- Skill ----

        /// <summary>
        /// Skill.csv 한 장. <b>행이 하나도 없는 것이 정상 상태</b>다 - 아직 정한 스킬이 없다는 뜻이며,
        /// 빈 표는 오류도 경고도 아니다(헤더만 맞으면 통과한다).
        ///
        /// id 예외는 <b>없다</b> - 캐릭터와 달리 스킬은 새로 만드는 데이터라 표준 ID 형식만 쓴다.
        /// </summary>
        private static void ValidateSkills(
            CsvTable table, TableDataSnapshot snapshot, TableDataAssetIndex assets, TableDataDiagnosticLog log)
        {
            string file = table.FileName;
            var displayOrders = new Dictionary<int, int>();

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new SkillRow { Line = line };

                string idRaw = table.Get(record, TableDataColumns.SkillId);
                bool idOk = TableDataFieldRules.TryReadRequiredId(
                    file, line, TableDataColumns.SkillId, idRaw, log, out string id);
                row.Id = id;

                if (idOk && snapshot.SkillsById.TryGetValue(id, out SkillRow existing))
                {
                    log.Error(file, line, TableDataColumns.SkillId, id,
                        $"skill_id가 {existing.Line}행과 중복됩니다 - 먼저 나온 행만 사용됩니다.");
                    idOk = false;
                }

                if (TableDataFieldRules.TryReadEnabled(
                        file, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log, out bool enabled))
                {
                    row.Enabled = enabled;
                }

                if (TableDataFieldRules.TryReadIntAtLeast(
                        file, line, TableDataColumns.DisplayOrder, table.Get(record, TableDataColumns.DisplayOrder),
                        0, log, out int order))
                {
                    row.DisplayOrder = order;
                    TableDataFieldRules.CheckDuplicateDisplayOrder(file, line, order, displayOrders, log);
                }

                row.Name = ReadLocalizedName(
                    table, record, file, line, row.Enabled, nameRequiredWhenEnabled: true, log);

                // 설명은 처음부터 끝까지 선택 항목이다 - 둘 다 비어 있으면 아무것도 알리지 않는다.
                row.Description = ReadOptionalLocalizedPair(
                    table, record, file, line,
                    TableDataColumns.DescriptionCategory, TableDataColumns.DescriptionKey, log);

                ReadSkillIcon(table, record, file, line, assets, row, log);

                if (TableDataFieldRules.TryReadOptionalLowercaseKey(
                        file, line, TableDataColumns.SkillType, table.Get(record, TableDataColumns.SkillType),
                        log, out string skillType))
                {
                    row.SkillType = skillType;
                }

                if (TableDataFieldRules.TryReadOptionalLowercaseKey(
                        file, line, TableDataColumns.BehaviorKey, table.Get(record, TableDataColumns.BehaviorKey),
                        log, out string behaviorKey))
                {
                    row.BehaviorKey = behaviorKey;
                }

                if (!idOk) continue;

                snapshot.Skills.Add(row);
                snapshot.SkillsById[row.Id] = row;
            }
        }

        /// <summary>
        /// 스킬 아이콘 한 칸. <b>선택 항목</b>이라 비어 있으면 경고에 그치고, 이름을 적었는데 찾지
        /// 못하면 오류다(초상화와 같은 판정). 아이콘 전용 폴더를 두지 않고 프로젝트 전체에서 이름으로
        /// 찾는 이유는, 스킬 아이콘의 자리가 아직 정해지지 않았기 때문이다 - 폴더를 먼저 정해 두면
        /// 그 폴더가 규칙이 되어 버린다.
        /// </summary>
        private static void ReadSkillIcon(
            CsvTable table, CsvRecord record, string file, int line,
            TableDataAssetIndex assets, SkillRow row, TableDataDiagnosticLog log)
        {
            string key = table.Get(record, TableDataColumns.IconKey);

            if (string.IsNullOrEmpty(key))
            {
                log.Warning(file, line, TableDataColumns.IconKey, key,
                    "icon_key가 비어 있습니다 - 스킬이 아이콘 없이 이름만으로 표시됩니다(선택 항목).");
                return;
            }

            ResolveSprite(assets, file, line, TableDataColumns.IconKey, key, log, out Sprite sprite);
            row.Icon = sprite;
        }

        // ---- CharacterSkill ----

        /// <summary>
        /// CharacterSkill.csv 한 장. <b>행이 하나도 없는 것이 정상 상태</b>다.
        ///
        /// 이 표는 스스로 무엇도 정의하지 않고 <b>두 표를 잇기만</b> 한다 - 그래서 모든 행의 양쪽 id가
        /// Character.csv / Skill.csv에 실재해야 하며, 없는 id를 가리키면 오류다. 활성 관계는 활성
        /// 캐릭터와 활성 스킬만 가리킬 수 있다(던전이 비활성 몬스터를 가리킬 수 없는 것과 같은 규칙).
        ///
        /// <b>여기서 아무것도 열어 주지 않는다.</b> required_character_level은 형식만 확인하고 그대로
        /// 옮긴다 - 그 값으로 스킬을 여닫는 규칙은 이 단계에 없다.
        /// </summary>
        private static void ValidateCharacterSkills(
            CsvTable table, TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            string file = table.FileName;
            var displayOrders = new Dictionary<int, int>();

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new CharacterSkillRow { Line = line };

                bool characterOk = TableDataFieldRules.TryReadRequiredCharacterId(
                    file, line, TableDataColumns.CharacterId, table.Get(record, TableDataColumns.CharacterId),
                    log, out string characterId);
                row.CharacterId = characterId;

                bool skillOk = TableDataFieldRules.TryReadRequiredId(
                    file, line, TableDataColumns.SkillId, table.Get(record, TableDataColumns.SkillId),
                    log, out string skillId);
                row.SkillId = skillId;

                bool pairOk = characterOk && skillOk;
                if (pairOk)
                {
                    row.PairId = CharacterSkillDefinition.BuildPairId(characterId, skillId);

                    if (snapshot.CharacterSkillsByPairId.TryGetValue(row.PairId, out CharacterSkillRow existing))
                    {
                        log.Error(file, line, TableDataColumns.SkillId, skillId,
                            $"'{characterId}' + '{skillId}' 짝이 {existing.Line}행과 중복됩니다 - " +
                            "먼저 나온 행만 사용됩니다.");
                        pairOk = false;
                    }
                }

                if (TableDataFieldRules.TryReadEnabled(
                        file, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log, out bool enabled))
                {
                    row.Enabled = enabled;
                }

                if (TableDataFieldRules.TryReadIntAtLeast(
                        file, line, TableDataColumns.DisplayOrder, table.Get(record, TableDataColumns.DisplayOrder),
                        0, log, out int order))
                {
                    row.DisplayOrder = order;
                    TableDataFieldRules.CheckDuplicateDisplayOrder(file, line, order, displayOrders, log);
                }

                // 상한은 두지 않는다 - 레벨의 최대치를 정하는 것은 이 표의 일이 아니다.
                if (TableDataFieldRules.TryReadIntAtLeast(
                        file, line, TableDataColumns.RequiredCharacterLevel,
                        table.Get(record, TableDataColumns.RequiredCharacterLevel), 1, log, out int level))
                {
                    row.RequiredCharacterLevel = level;
                }

                CheckCharacterSkillReferences(file, snapshot, characterOk, skillOk, row, log);

                if (!pairOk) continue;

                snapshot.CharacterSkills.Add(row);
                snapshot.CharacterSkillsByPairId[row.PairId] = row;
            }
        }

        /// <summary>
        /// 관계의 양쪽이 실제로 있는 행을 가리키는지 본다. 형식 검사에서 이미 걸린 쪽은 다시 보지
        /// 않는다 - 같은 칸에 오류를 두 번 쌓으면 원인이 무엇인지 흐려진다.
        ///
        /// <b>비활성 관계도 참조는 검사한다.</b> 잘못된 참조를 통과시키면 다시 켜는 순간 조용히 깨지기
        /// 때문이다. 다만 "활성인데 비활성 대상을 가리킨다"는 판정은 활성 관계에만 적용한다.
        /// </summary>
        private static void CheckCharacterSkillReferences(
            string file, TableDataSnapshot snapshot, bool characterOk, bool skillOk,
            CharacterSkillRow row, TableDataDiagnosticLog log)
        {
            if (characterOk)
            {
                if (!snapshot.CharactersById.TryGetValue(row.CharacterId, out CharacterRow character))
                {
                    log.Error(file, row.Line, TableDataColumns.CharacterId, row.CharacterId,
                        $"{TableDataPaths.CharacterCsvFileName}에 없는 character_id입니다 - " +
                        "관계는 그 표에 실제로 있는 행만 가리킬 수 있습니다(대소문자를 구분합니다).");
                }
                else if (row.Enabled && !character.Enabled)
                {
                    log.Error(file, row.Line, TableDataColumns.CharacterId, row.CharacterId,
                        $"enabled=0인 캐릭터({TableDataPaths.CharacterCsvFileName} {character.Line}행)를 가리킵니다 - " +
                        "활성 관계는 활성 캐릭터만 가리킬 수 있습니다.");
                }
            }

            if (!skillOk) return;

            if (!snapshot.SkillsById.TryGetValue(row.SkillId, out SkillRow skill))
            {
                log.Error(file, row.Line, TableDataColumns.SkillId, row.SkillId,
                    $"{TableDataPaths.SkillCsvFileName}에 없는 skill_id입니다 - " +
                    "관계는 그 표에 실제로 있는 행만 가리킬 수 있습니다(대소문자를 구분합니다).");
                return;
            }

            if (row.Enabled && !skill.Enabled)
            {
                log.Error(file, row.Line, TableDataColumns.SkillId, row.SkillId,
                    $"enabled=0인 스킬({TableDataPaths.SkillCsvFileName} {skill.Line}행)을 가리킵니다 - " +
                    "활성 관계는 활성 스킬만 가리킬 수 있습니다.");
            }
        }

        // ---- Building ----

        /// <summary>
        /// Building.csv 한 장. 기본 골격은 다른 표와 같고(id 형식/중복, 순서, 이름), 이 표에만 있는
        /// 규칙이 셋이다.
        ///
        /// 첫째, <b>기능 이름이 활성 행에서 필수</b>다. 건물 팝업은 "무엇을 짓는가"와 "그러면 무엇이
        /// 열리는가" 두 줄이 모두 있어야 성립하므로, 한쪽만 있는 상태를 "아직 안 정했다"는 정상으로
        /// 두지 않는다(Item.csv의 설명과 같은 판정이다). 기능 이름이 <b>다른 카테고리</b>를 가리키는
        /// 것은 정상이다 - 여관은 07_Building, 그 기능인 용병 모집은 01_UI에 있다.
        ///
        /// 둘째, <b>비용 재화 두 칸이 한 덩어리</b>다. id가 비면 금액도 비어야 하고, id가 있으면
        /// Currency.csv에 실제로 있는 <b>활성</b> 행을 가리켜야 한다 - 카탈로그에 없는 재화를 비용으로
        /// 받으면 화면에 이름도 아이콘도 없는 값을 내라고 하게 된다.
        ///
        /// 셋째, <b>비용 아이템 두 칸도 한 덩어리</b>다. 둘 다 비어 있는 것은 정상(재화만 내는 건물)
        /// 이고, 한쪽만 채워지거나 항목 수가 다르면 오류다 - 어느 아이템이 몇 개인지 정할 수 없는
        /// 상태를 조용히 넘기면 공짜로 지어지는 건물이 생긴다.
        ///
        /// <b>영어와 한국어 번역이 같거나 한국어가 들어 있는 것은 여기서 보지 않는다.</b> 그것은
        /// 번역의 내용이고, 이 표가 확인하는 것은 "그 Entry가 실제로 있는가"까지다.
        /// </summary>
        private static void ValidateBuildings(CsvTable table, TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            string file = table.FileName;
            var displayOrders = new Dictionary<int, int>();

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new BuildingRow { Line = line };

                string idRaw = table.Get(record, TableDataColumns.BuildingId);
                bool idOk = TableDataFieldRules.TryReadRequiredId(
                    file, line, TableDataColumns.BuildingId, idRaw, log, out string id);
                row.Id = id;

                if (idOk && snapshot.BuildingsById.TryGetValue(id, out BuildingRow existing))
                {
                    log.Error(file, line, TableDataColumns.BuildingId, id,
                        $"building_id가 {existing.Line}행과 중복됩니다 - 먼저 나온 행만 사용됩니다.");
                    idOk = false;
                }

                if (TableDataFieldRules.TryReadEnabled(
                        file, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log,
                        out bool enabled))
                {
                    row.Enabled = enabled;
                }

                if (TableDataFieldRules.TryReadInt(
                        file, line, TableDataColumns.DisplayOrder,
                        table.Get(record, TableDataColumns.DisplayOrder), log, out int order))
                {
                    row.DisplayOrder = order;
                    TableDataFieldRules.CheckDuplicateDisplayOrder(file, line, order, displayOrders, log);
                }

                row.Name = ReadLocalizedName(
                    table, record, file, line, row.Enabled, nameRequiredWhenEnabled: true, log);

                row.FunctionName = ReadBuildingFunctionName(table, record, file, line, row.Enabled, log);

                if (TableDataFieldRules.TryReadIntAtLeast(
                        file, line, TableDataColumns.BuildTime,
                        table.Get(record, TableDataColumns.BuildTime), 0, log, out int buildTime))
                {
                    row.BuildTimeSeconds = buildTime;
                }

                ReadBuildingCostCurrency(table, record, file, line, snapshot, row, log);
                ReadBuildingCostItems(table, record, file, line, snapshot, row, log);

                // memo는 사람이 읽는 칸이라 검증하지 않는다.

                if (!idOk) continue;

                snapshot.Buildings.Add(row);
                snapshot.BuildingsById[row.Id] = row;
            }
        }

        /// <summary>
        /// function_category / function_key 두 칸. 판정은 이름 칸과 <b>완전히 같다</b>(활성 행에서
        /// 필수, 한쪽만 채워진 것은 오류, 둘 다 있으면 실재까지 확인) - 그래서
        /// <see cref="ReadLocalizedName"/>의 규칙을 그대로 다시 쓰되 컬럼 이름만 바꿔서 부른다.
        /// </summary>
        private static LocalizedEntryRef ReadBuildingFunctionName(
            CsvTable table, CsvRecord record, string file, int line, bool enabled, TableDataDiagnosticLog log)
        {
            string categoryRaw = table.Get(record, TableDataColumns.FunctionCategory);
            string keyRaw = table.Get(record, TableDataColumns.FunctionKey);

            bool hasCategory = !string.IsNullOrEmpty(categoryRaw);
            bool hasKey = !string.IsNullOrEmpty(keyRaw);

            if (hasCategory && hasKey)
            {
                TableDataFieldRules.TryResolveLocalizedEntry(
                    file, line, TableDataColumns.FunctionCategory, categoryRaw,
                    TableDataColumns.FunctionKey, keyRaw, log, out LocalizedEntryRef entry);
                return entry;
            }

            if (!hasCategory && !hasKey)
            {
                if (enabled)
                {
                    log.Error(file, line, TableDataColumns.FunctionCategory, categoryRaw,
                        "enabled=1인 행은 function_category와 function_key가 모두 필요합니다 - " +
                        "건물 팝업은 해금되는 기능 이름 없이 성립하지 않습니다.");
                }

                return LocalizedEntryRef.None;
            }

            string emptyColumn = hasCategory ? TableDataColumns.FunctionKey : TableDataColumns.FunctionCategory;
            string emptyValue = hasCategory ? keyRaw : categoryRaw;

            log.Error(file, line, emptyColumn, emptyValue,
                "function_category와 function_key는 함께 있어야 합니다 - 한쪽만으로는 참조를 만들 수 없습니다.");

            return LocalizedEntryRef.None;
        }

        /// <summary>
        /// 비용 재화 두 칸. <b>둘이 한 덩어리</b>라 함께 비거나 함께 차 있어야 한다 - 한쪽만 적힌 행은
        /// "얼마인지 모르는 비용"이거나 "무엇을 내는지 모르는 금액"이고, 둘 다 조용히 넘길 수 없다.
        /// 금액 0은 <b>형식 오류가 아니다</b>(무료 건물을 재화 칸으로 적을 수 있다).
        ///
        /// 실재 확인은 <see cref="TableDataSnapshot.CurrenciesById"/>로 하며 <b>다듬지 않은 값끼리의
        /// Ordinal 완전 일치</b>다 - Currency.csv가 Building.csv보다 먼저 읽히므로 이 시점의 스냅샷은
        /// 이미 완성되어 있다. 비활성 재화를 가리키는 것도 오류이며, 건물 자신이 enabled=0이어도
        /// 마찬가지로 본다(비활성 행의 잘못된 참조를 통과시키면 다시 켜는 순간 조용히 깨진다).
        /// </summary>
        private static void ReadBuildingCostCurrency(
            CsvTable table, CsvRecord record, string file, int line,
            TableDataSnapshot snapshot, BuildingRow row, TableDataDiagnosticLog log)
        {
            string idRaw = table.Get(record, TableDataColumns.CostCurrencyId);
            string amountRaw = table.Get(record, TableDataColumns.CostCurrencyAmount);

            bool hasId = !string.IsNullOrEmpty(idRaw);
            bool hasAmount = !string.IsNullOrEmpty(amountRaw);

            if (!hasId && !hasAmount) return;

            if (!hasId)
            {
                log.Error(file, line, TableDataColumns.CostCurrencyId, idRaw,
                    $"{TableDataColumns.CostCurrencyAmount}만 적혀 있습니다 - 어떤 재화를 낼지 알 수 없으므로 " +
                    "두 칸을 함께 적거나 함께 비우세요.");
                return;
            }

            if (!hasAmount)
            {
                log.Error(file, line, TableDataColumns.CostCurrencyAmount, amountRaw,
                    $"{TableDataColumns.CostCurrencyId}를 적었으면 금액도 있어야 합니다 - " +
                    "두 칸을 함께 적거나 함께 비우세요.");
                return;
            }

            if (!TableDataFieldRules.IsValidId(idRaw))
            {
                log.Error(file, line, TableDataColumns.CostCurrencyId, idRaw,
                    $"cost_currency_id 형식이 맞지 않습니다 - {TableDataFieldRules.IdPatternText} 를 만족해야 합니다.");
                return;
            }

            if (!TableDataFieldRules.TryReadIntAtLeast(
                    file, line, TableDataColumns.CostCurrencyAmount, amountRaw, 0, log, out int amount))
            {
                return;
            }

            if (!snapshot.CurrenciesById.TryGetValue(idRaw, out CurrencyRow currency))
            {
                log.Error(file, line, TableDataColumns.CostCurrencyId, idRaw,
                    $"{TableDataPaths.CurrencyCsvFileName}에 없는 currency_id입니다 - " +
                    "건설 비용은 그 표에 실제로 있는 행만 가리킬 수 있습니다(대소문자를 구분합니다).");
                return;
            }

            if (!currency.Enabled)
            {
                log.Error(file, line, TableDataColumns.CostCurrencyId, idRaw,
                    $"enabled=0인 재화({TableDataPaths.CurrencyCsvFileName} {currency.Line}행)를 비용으로 받습니다 - " +
                    "건설 비용에는 활성 재화만 넣을 수 있습니다.");
                return;
            }

            row.CostCurrencyId = idRaw;
            row.CostCurrencyAmount = amount;
        }

        /// <summary>
        /// 비용 아이템 두 칸. <b>둘 다 비어 있는 것이 정상</b>이며 그때는 아무것도 알리지 않는다 -
        /// 재화만 내는 건물은 흔하고, 경고를 남기면 정상 상태가 매번 눈에 걸린다.
        ///
        /// 값이 있으면 <b>두 목록의 항목 수가 같아야 한다</b>. 비교는 <c>|</c>로 자른 <b>원본 토큰
        /// 수</b>로 하며, 형식 오류로 버려진 토큰 때문에 "개수가 다르다"는 두 번째 오류가 딸려 나오지
        /// 않게 한다 - 원인 하나에 진단 하나가 원칙이다. 개수는 <b>1 이상</b>이어야 한다: 0개짜리
        /// 비용은 낼 것이 없다는 뜻이라 표에 적을 이유가 없고, 조용히 통과시키면 비용이 적혀 있는데도
        /// 공짜인 행이 생긴다.
        /// </summary>
        private static void ReadBuildingCostItems(
            CsvTable table, CsvRecord record, string file, int line,
            TableDataSnapshot snapshot, BuildingRow row, TableDataDiagnosticLog log)
        {
            string idsRaw = table.Get(record, TableDataColumns.CostItemIds);
            string countsRaw = table.Get(record, TableDataColumns.CostItemCounts);

            bool hasIds = !string.IsNullOrEmpty(idsRaw);
            bool hasCounts = !string.IsNullOrEmpty(countsRaw);

            // 아이템 비용이 없는 건물. 두 칸이 함께 비어 있는 것은 정상이라 진단을 남기지 않는다.
            if (!hasIds && !hasCounts) return;

            if (!hasIds)
            {
                log.Error(file, line, TableDataColumns.CostItemIds, idsRaw,
                    $"{TableDataColumns.CostItemCounts}만 적혀 있습니다 - 어떤 아이템을 낼지 알 수 없으므로 " +
                    "두 칸을 함께 적거나 함께 비우세요.");
                return;
            }

            if (!hasCounts)
            {
                log.Error(file, line, TableDataColumns.CostItemCounts, countsRaw,
                    $"{TableDataColumns.CostItemIds}를 적었으면 개수도 있어야 합니다 - " +
                    "두 칸을 함께 적거나 함께 비우세요.");
                return;
            }

            string[] idTokens = idsRaw.Split('|');
            string[] countTokens = countsRaw.Split('|');

            if (idTokens.Length != countTokens.Length)
            {
                log.Error(file, line, TableDataColumns.CostItemCounts, countsRaw,
                    $"아이템 {idTokens.Length}개에 개수 {countTokens.Length}개가 적혀 있습니다 - " +
                    "두 목록의 항목 수가 같아야 어느 아이템이 몇 개인지 정할 수 있습니다.");
                return;
            }

            var ids = new List<string>();
            TableDataFieldRules.ReadIdList(file, line, TableDataColumns.CostItemIds, idsRaw, log, ids);

            // 형식/중복으로 버려진 토큰이 있으면 짝을 맞출 수 없다. 원인은 이미 보고됐으므로 여기서
            // 두 번째 오류를 덧붙이지 않는다.
            if (ids.Count != idTokens.Length) return;

            for (int i = 0; i < ids.Count; i++)
            {
                string itemId = ids[i];

                if (!TableDataFieldRules.TryReadIntAtLeast(
                        file, line, TableDataColumns.CostItemCounts, countTokens[i].Trim(), 1, log, out int count))
                {
                    continue;
                }

                if (!snapshot.ItemsById.TryGetValue(itemId, out ItemRow item))
                {
                    log.Error(file, line, TableDataColumns.CostItemIds, itemId,
                        $"{TableDataPaths.ItemCsvFileName}에 없는 item_id입니다 - " +
                        "건설 비용은 그 표에 실제로 있는 행만 가리킬 수 있습니다.");
                    continue;
                }

                if (!item.Enabled)
                {
                    log.Error(file, line, TableDataColumns.CostItemIds, itemId,
                        $"enabled=0인 아이템({TableDataPaths.ItemCsvFileName} {item.Line}행)을 비용으로 받습니다 - " +
                        "건설 비용에는 활성 아이템만 넣을 수 있습니다.");
                    continue;
                }

                row.ItemCosts.Add(new BuildingItemCostRow { ItemId = itemId, Count = count });
            }
        }

        // ---- CharacterAcquisition ----

        /// <summary>
        /// CharacterAcquisition.csv 한 장. 이 표는 스스로 캐릭터를 정의하지 않고 <b>Character.csv의
        /// 행에 획득 방식을 붙이기만</b> 한다 - 그래서 모든 행의 character_id가 그 표에 실재해야 하고,
        /// 활성 행은 활성 캐릭터만 가리킬 수 있다(CharacterSkill.csv와 같은 규칙이다).
        ///
        /// <b>획득 방식은 캐릭터 하나에 하나다.</b> 같은 캐릭터를 두 행에 적으면 어느 쪽이 참인지
        /// 정할 수 없고, 카탈로그가 앞의 행만 남기므로 뒤의 행은 조용히 사라진다 - 그런 상태를
        /// 통과시키지 않는다.
        ///
        /// <b>acquisition_type의 <i>뜻</i>은 여기서 보지 않는다.</b> 대문자 낱말 형식인지만 확인하며,
        /// 런타임이 아는 낱말인지는 그 값을 읽는 쪽이 판정한다
        /// (<see cref="RecruitmentCandidateSelector"/>는 모르는 낱말을 후보에서 뺀다) - 아직 코드가
        /// 지원하지 않는 방식을 표에 미리 적어 둘 수 있어야 하기 때문이다.
        /// </summary>
        private static void ValidateCharacterAcquisitions(
            CsvTable table, TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            string file = table.FileName;

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new CharacterAcquisitionRow { Line = line };

                bool idOk = TableDataFieldRules.TryReadRequiredId(
                    file, line, TableDataColumns.AcquisitionId,
                    table.Get(record, TableDataColumns.AcquisitionId), log, out string id);
                row.Id = id;

                if (idOk && snapshot.CharacterAcquisitionsById.TryGetValue(id, out CharacterAcquisitionRow existing))
                {
                    log.Error(file, line, TableDataColumns.AcquisitionId, id,
                        $"acquisition_id가 {existing.Line}행과 중복됩니다 - 먼저 나온 행만 사용됩니다.");
                    idOk = false;
                }

                bool characterOk = TableDataFieldRules.TryReadRequiredCharacterId(
                    file, line, TableDataColumns.CharacterId,
                    table.Get(record, TableDataColumns.CharacterId), log, out string characterId);
                row.CharacterId = characterId;

                if (TableDataFieldRules.TryReadEnabled(
                        file, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log,
                        out bool enabled))
                {
                    row.Enabled = enabled;
                }

                if (TableDataFieldRules.TryReadRequiredUppercaseKey(
                        file, line, TableDataColumns.AcquisitionType,
                        table.Get(record, TableDataColumns.AcquisitionType), log, out string acquisitionType))
                {
                    row.AcquisitionType = acquisitionType;
                }

                if (TableDataFieldRules.TryReadFlag(
                        file, line, TableDataColumns.AllowDuplicateRecruitment,
                        table.Get(record, TableDataColumns.AllowDuplicateRecruitment), log,
                        out bool allowDuplicate))
                {
                    row.AllowDuplicateRecruitment = allowDuplicate;
                }

                if (TableDataFieldRules.TryReadOptionalId(
                        file, line, TableDataColumns.ConditionId,
                        table.Get(record, TableDataColumns.ConditionId), log, out string conditionId))
                {
                    row.ConditionId = conditionId;
                }

                if (characterOk)
                {
                    CheckCharacterReference(
                        file, line, TableDataColumns.CharacterId, row.CharacterId, row.Enabled, snapshot, log);

                    if (snapshot.CharacterAcquisitionsByCharacterId.TryGetValue(
                            row.CharacterId, out CharacterAcquisitionRow sameCharacter))
                    {
                        log.Error(file, line, TableDataColumns.CharacterId, row.CharacterId,
                            $"character_id가 {sameCharacter.Line}행과 중복됩니다 - 한 캐릭터의 획득 방식은 " +
                            "하나여야 하며, 목록에는 먼저 나온 행만 남습니다.");
                        characterOk = false;
                    }
                }

                // memo는 사람이 읽는 칸이라 검증하지 않는다.

                if (!idOk || !characterOk) continue;

                snapshot.CharacterAcquisitions.Add(row);
                snapshot.CharacterAcquisitionsById[row.Id] = row;
                snapshot.CharacterAcquisitionsByCharacterId[row.CharacterId] = row;
            }
        }

        // ---- RecruitmentType ----

        /// <summary>
        /// RecruitmentType.csv 한 장. 담는 것이 키와 활성 여부뿐이라 검사도 그 둘뿐이다 - <b>없는
        /// 칸을 지어내지 않는다</b>(이 표에는 display_order도 이름 참조도 없다).
        ///
        /// id는 <see cref="TableDataFieldRules.RecruitmentIdPatternText"/>를 쓴다 - 이미
        /// <c>Inn_Normal</c>로 저작되어 있는 값이라 표준 형식(소문자/숫자)으로는 담을 수 없고,
        /// 표준 형식을 넓히면 기존 아홉 표의 검사가 함께 헐거워지기 때문이다.
        /// </summary>
        private static void ValidateRecruitmentTypes(
            CsvTable table, TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            string file = table.FileName;

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new RecruitmentTypeRow { Line = line };

                bool idOk = TableDataFieldRules.TryReadRequiredRecruitmentId(
                    file, line, TableDataColumns.RecruitmentTypeId,
                    table.Get(record, TableDataColumns.RecruitmentTypeId), log, out string id);
                row.Id = id;

                if (idOk && snapshot.RecruitmentTypesById.TryGetValue(id, out RecruitmentTypeRow existing))
                {
                    log.Error(file, line, TableDataColumns.RecruitmentTypeId, id,
                        $"recruitment_type_id가 {existing.Line}행과 중복됩니다 - 먼저 나온 행만 사용됩니다.");
                    idOk = false;
                }

                if (TableDataFieldRules.TryReadEnabled(
                        file, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log,
                        out bool enabled))
                {
                    row.Enabled = enabled;
                }

                if (!idOk) continue;

                snapshot.RecruitmentTypes.Add(row);
                snapshot.RecruitmentTypesById[row.Id] = row;
            }
        }

        // ---- RecruitmentPool ----

        /// <summary>
        /// RecruitmentPool.csv 한 장. 이 표도 스스로 무엇을 정의하지 않고 <b>모집과 캐릭터를 잇기만</b>
        /// 한다 - 양쪽 id가 실재해야 하고, 활성 칸은 활성 모집과 활성 캐릭터만 가리킬 수 있다.
        ///
        /// <b>같은 모집 안에서 같은 캐릭터를 두 번 적을 수 없다.</b> 두 칸이 각자 가중치를 가지면
        /// 확률이 조용히 합쳐지는데, 그 합은 표를 보고는 읽히지 않는다 - 뽑기도 뒤의 칸을 버리므로
        /// (앞선 칸만 남긴다) 적어 둔 가중치가 그대로 사라진다.
        ///
        /// <b>weight는 1 이상이어야 한다.</b> 0짜리 칸은 절대 뽑히지 않으므로 표에 적을 이유가 없고,
        /// 조용히 통과시키면 "후보에 있는데 나오지 않는" 유령 칸이 생긴다(건설 비용의 0개 칸을 오류로
        /// 막는 것과 같은 판정이다).
        /// </summary>
        private static void ValidateRecruitmentPools(
            CsvTable table, TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            string file = table.FileName;

            // 같은 모집 안의 캐릭터 중복만 본다 - 서로 다른 모집에 같은 캐릭터가 드는 것은 정상이다.
            var charactersByType = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new RecruitmentPoolRow { Line = line };

                bool typeOk = TableDataFieldRules.TryReadRequiredRecruitmentId(
                    file, line, TableDataColumns.RecruitmentTypeId,
                    table.Get(record, TableDataColumns.RecruitmentTypeId), log, out string typeId);
                row.RecruitmentTypeId = typeId;

                bool entryOk = TableDataFieldRules.TryReadRequiredId(
                    file, line, TableDataColumns.PoolEntryId,
                    table.Get(record, TableDataColumns.PoolEntryId), log, out string entryId);
                row.PoolEntryId = entryId;

                bool pairOk = typeOk && entryOk;
                if (pairOk)
                {
                    row.PairId = RecruitmentPoolEntryDefinition.BuildPairId(typeId, entryId);

                    if (snapshot.RecruitmentPoolsByPairId.TryGetValue(row.PairId, out RecruitmentPoolRow existing))
                    {
                        log.Error(file, line, TableDataColumns.PoolEntryId, entryId,
                            $"'{typeId}' + '{entryId}' 짝이 {existing.Line}행과 중복됩니다 - " +
                            "먼저 나온 행만 사용됩니다.");
                        pairOk = false;
                    }
                }

                bool characterOk = TableDataFieldRules.TryReadRequiredCharacterId(
                    file, line, TableDataColumns.CharacterId,
                    table.Get(record, TableDataColumns.CharacterId), log, out string characterId);
                row.CharacterId = characterId;

                if (TableDataFieldRules.TryReadEnabled(
                        file, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log,
                        out bool enabled))
                {
                    row.Enabled = enabled;
                }

                if (TableDataFieldRules.TryReadIntAtLeast(
                        file, line, TableDataColumns.Weight, table.Get(record, TableDataColumns.Weight),
                        1, log, out int weight))
                {
                    row.Weight = weight;
                }

                if (typeOk) CheckRecruitmentTypeReference(file, line, row.RecruitmentTypeId, row.Enabled, snapshot, log);

                if (characterOk)
                {
                    CheckCharacterReference(
                        file, line, TableDataColumns.CharacterId, row.CharacterId, row.Enabled, snapshot, log);

                    if (typeOk)
                    {
                        if (!charactersByType.TryGetValue(typeId, out Dictionary<string, int> seen))
                        {
                            seen = new Dictionary<string, int>(StringComparer.Ordinal);
                            charactersByType[typeId] = seen;
                        }

                        if (seen.TryGetValue(row.CharacterId, out int firstLine))
                        {
                            log.Error(file, line, TableDataColumns.CharacterId, row.CharacterId,
                                $"'{typeId}' 모집의 {firstLine}행이 이미 같은 캐릭터를 후보로 올렸습니다 - " +
                                "한 모집에 같은 캐릭터는 한 칸만 둘 수 있습니다(가중치를 합치려면 그 한 " +
                                "칸의 weight를 올리세요).");
                            pairOk = false;
                        }
                        else
                        {
                            seen[row.CharacterId] = line;
                        }
                    }
                }

                // memo는 사람이 읽는 칸이라 검증하지 않는다.

                if (!pairOk || !characterOk) continue;

                snapshot.RecruitmentPools.Add(row);
                snapshot.RecruitmentPoolsByPairId[row.PairId] = row;
            }
        }

        // ---- RecruitmentAccess ----

        /// <summary>
        /// RecruitmentAccess.csv 한 장. "어디서 어떤 모집이 열리는가"를 적는 표이며, 이 표에만 있는
        /// 규칙이 둘이다.
        ///
        /// 첫째, <b>대상은 종류 + id 두 칸</b>이다. 종류가 <c>BUILDING</c>이면 id는 Building.csv에
        /// 실제로 있는 행이어야 하고, 활성 창구는 활성 건물에만 붙을 수 있다. 종류가 그 밖의 낱말이면
        /// <b>가리키는 표가 없으므로 실재 검사를 하지 않고 경고만</b> 남긴다 - 아직 코드가 모르는
        /// 대상을 표에 미리 적어 둘 수 있어야 하기 때문이다(acquisition_type과 같은 태도다).
        ///
        /// 둘째, <b>한 대상에 창구가 여럿이면 경고</b>다. 오류가 아닌 이유는 그것이 표현할 수 있는
        /// 상태이기 때문이고, 경고인 이유는 조회
        /// (<see cref="RecruitmentAccessCatalog.FindBySource"/>)가 display_order가 앞선 하나만
        /// 돌려주므로 나머지가 조용히 열리지 않기 때문이다.
        /// </summary>
        private static void ValidateRecruitmentAccesses(
            CsvTable table, TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            string file = table.FileName;
            var displayOrders = new Dictionary<int, int>();
            var sources = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new RecruitmentAccessRow { Line = line };

                bool idOk = TableDataFieldRules.TryReadRequiredRecruitmentId(
                    file, line, TableDataColumns.RecruitmentAccessId,
                    table.Get(record, TableDataColumns.RecruitmentAccessId), log, out string id);
                row.Id = id;

                if (idOk && snapshot.RecruitmentAccessesById.TryGetValue(id, out RecruitmentAccessRow existing))
                {
                    log.Error(file, line, TableDataColumns.RecruitmentAccessId, id,
                        $"recruitment_access_id가 {existing.Line}행과 중복됩니다 - 먼저 나온 행만 사용됩니다.");
                    idOk = false;
                }

                bool typeOk = TableDataFieldRules.TryReadRequiredRecruitmentId(
                    file, line, TableDataColumns.RecruitmentTypeId,
                    table.Get(record, TableDataColumns.RecruitmentTypeId), log, out string typeId);
                row.RecruitmentTypeId = typeId;

                if (TableDataFieldRules.TryReadEnabled(
                        file, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log,
                        out bool enabled))
                {
                    row.Enabled = enabled;
                }

                if (TableDataFieldRules.TryReadInt(
                        file, line, TableDataColumns.DisplayOrder,
                        table.Get(record, TableDataColumns.DisplayOrder), log, out int order))
                {
                    row.DisplayOrder = order;
                    TableDataFieldRules.CheckDuplicateDisplayOrder(file, line, order, displayOrders, log);
                }

                bool sourceTypeOk = TableDataFieldRules.TryReadRequiredUppercaseKey(
                    file, line, TableDataColumns.SourceType,
                    table.Get(record, TableDataColumns.SourceType), log, out string sourceType);
                row.SourceType = sourceType;

                bool sourceIdOk = TableDataFieldRules.TryReadRequiredId(
                    file, line, TableDataColumns.SourceId,
                    table.Get(record, TableDataColumns.SourceId), log, out string sourceId);
                row.SourceId = sourceId;

                if (TableDataFieldRules.TryReadIntAtLeast(
                        file, line, TableDataColumns.ArrivalIntervalSeconds,
                        table.Get(record, TableDataColumns.ArrivalIntervalSeconds), 0, log, out int interval))
                {
                    row.ArrivalIntervalSeconds = interval;
                }

                if (TableDataFieldRules.TryReadIntAtLeast(
                        file, line, TableDataColumns.ConsumeAmount,
                        table.Get(record, TableDataColumns.ConsumeAmount), 0, log, out int consume))
                {
                    row.ConsumeAmount = consume;
                }

                if (typeOk) CheckRecruitmentTypeReference(file, line, row.RecruitmentTypeId, row.Enabled, snapshot, log);

                if (sourceTypeOk && sourceIdOk)
                {
                    CheckRecruitmentSource(file, line, row, snapshot, log);

                    string sourceKey = row.SourceType + "/" + row.SourceId;
                    if (sources.TryGetValue(sourceKey, out int firstLine))
                    {
                        log.Warning(file, line, TableDataColumns.SourceId, row.SourceId,
                            $"{firstLine}행이 이미 같은 대상('{sourceKey}')에 창구를 붙였습니다 - " +
                            "조회는 display_order가 앞선 하나만 돌려주므로 나머지는 열리지 않습니다.");
                    }
                    else
                    {
                        sources[sourceKey] = line;
                    }
                }

                // memo는 사람이 읽는 칸이라 검증하지 않는다.

                if (!idOk) continue;

                snapshot.RecruitmentAccesses.Add(row);
                snapshot.RecruitmentAccessesById[row.Id] = row;
            }
        }

        /// <summary>
        /// 창구가 붙은 대상이 실재하는지 본다. 지금 <b>실재를 확인할 수 있는 종류는
        /// <c>BUILDING</c> 하나</b>뿐이며, 그 밖의 낱말은 가리킬 표가 없으므로 경고만 남기고 넘어간다 -
        /// 표가 코드보다 앞서가는 것을 오류로 막지 않는다.
        /// </summary>
        private static void CheckRecruitmentSource(
            string file, int line, RecruitmentAccessRow row, TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            if (!string.Equals(row.SourceType, RecruitmentSourceTypes.Building, StringComparison.Ordinal))
            {
                log.Warning(file, line, TableDataColumns.SourceType, row.SourceType,
                    $"런타임이 아는 대상 종류는 '{RecruitmentSourceTypes.Building}' 하나뿐이라 " +
                    "이 창구는 어떤 조회에도 걸리지 않습니다 - 대상이 실제로 있는지도 확인하지 않습니다.");
                return;
            }

            if (!snapshot.BuildingsById.TryGetValue(row.SourceId, out BuildingRow building))
            {
                log.Error(file, line, TableDataColumns.SourceId, row.SourceId,
                    $"{TableDataPaths.BuildingCsvFileName}에 없는 building_id입니다 - " +
                    "창구는 그 표에 실제로 있는 행에만 붙을 수 있습니다(대소문자를 구분합니다).");
                return;
            }

            if (row.Enabled && !building.Enabled)
            {
                log.Error(file, line, TableDataColumns.SourceId, row.SourceId,
                    $"enabled=0인 건물({TableDataPaths.BuildingCsvFileName} {building.Line}행)에 붙어 " +
                    "있습니다 - 활성 창구는 활성 건물에만 붙을 수 있습니다.");
            }
        }

        // ---- PartyConfig ----

        /// <summary>
        /// PartyConfig.csv 한 장. 담는 것이 키와 정원과 활성 여부뿐이라 검사도 그 셋뿐이다 -
        /// <b>없는 칸을 지어내지 않는다</b>(이 표에는 display_order도 이름 참조도 없다).
        ///
        /// id는 <b>표준 형식</b>(<see cref="TableDataFieldRules.IdPatternText"/>)이다 - 모집 표처럼
        /// 대문자를 허용할 이유가 없는 새 표이고, 표준 형식으로 담을 수 있는 값(<c>default</c>)만
        /// 쓰기 때문이다.
        ///
        /// <b>base_capacity가 1 미만이면 오류다.</b> 값을 하한으로 끌어올려 통과시키지 않는다 -
        /// 보정해 넘기면 CSV에 적힌 값과 생성 에셋의 값이 달라져, 표만 보고는 실제 정원을 알 수
        /// 없게 된다(RecruitmentPool.csv의 weight와 같은 판정이다).
        /// </summary>
        private static void ValidatePartyConfigs(
            CsvTable table, TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            string file = table.FileName;

            foreach (CsvRecord record in table.Records)
            {
                int line = record.Line;
                var row = new PartyConfigRow { Line = line };

                bool idOk = TableDataFieldRules.TryReadRequiredId(
                    file, line, TableDataColumns.PartyConfigId,
                    table.Get(record, TableDataColumns.PartyConfigId), log, out string id);
                row.Id = id;

                if (idOk && snapshot.PartyConfigsById.TryGetValue(id, out PartyConfigRow existing))
                {
                    log.Error(file, line, TableDataColumns.PartyConfigId, id,
                        $"party_config_id가 {existing.Line}행과 중복됩니다 - 먼저 나온 행만 사용됩니다.");
                    idOk = false;
                }

                if (TableDataFieldRules.TryReadIntAtLeast(
                        file, line, TableDataColumns.BaseCapacity,
                        table.Get(record, TableDataColumns.BaseCapacity),
                        PartyConfigRules.MinimumBaseCapacity, log, out int baseCapacity))
                {
                    row.BaseCapacity = baseCapacity;
                }

                if (TableDataFieldRules.TryReadEnabled(
                        file, line, TableDataColumns.Enabled, table.Get(record, TableDataColumns.Enabled), log,
                        out bool enabled))
                {
                    row.Enabled = enabled;
                }

                if (!idOk) continue;

                snapshot.PartyConfigs.Add(row);
                snapshot.PartyConfigsById[row.Id] = row;
            }
        }

        /// <summary>
        /// 모집 키가 RecruitmentType.csv에 실재하는지 본다. <b>비활성 행의 참조도 검사한다</b> -
        /// 잘못된 참조를 통과시키면 다시 켜는 순간 조용히 깨지기 때문이다. 다만 "활성인데 비활성
        /// 대상을 가리킨다"는 판정은 활성 행에만 적용한다(CharacterSkill.csv와 같은 규칙이다).
        /// </summary>
        private static void CheckRecruitmentTypeReference(
            string file, int line, string recruitmentTypeId, bool rowEnabled,
            TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            if (!snapshot.RecruitmentTypesById.TryGetValue(recruitmentTypeId, out RecruitmentTypeRow type))
            {
                log.Error(file, line, TableDataColumns.RecruitmentTypeId, recruitmentTypeId,
                    $"{TableDataPaths.RecruitmentTypeCsvFileName}에 없는 recruitment_type_id입니다 - " +
                    "그 표에 실제로 있는 행만 가리킬 수 있습니다(대소문자를 구분합니다).");
                return;
            }

            if (rowEnabled && !type.Enabled)
            {
                log.Error(file, line, TableDataColumns.RecruitmentTypeId, recruitmentTypeId,
                    $"enabled=0인 모집({TableDataPaths.RecruitmentTypeCsvFileName} {type.Line}행)을 " +
                    "가리킵니다 - 활성 행은 활성 모집만 가리킬 수 있습니다.");
            }
        }

        /// <summary>
        /// character_id가 Character.csv에 실재하는지 본다. 모집 쪽 두 표가 <b>같은 규칙</b>을 쓰도록
        /// 한 자리에 모아 두었다 - 표마다 다른 판정을 적어 두면 한쪽만 고쳐질 수 있다.
        /// </summary>
        private static void CheckCharacterReference(
            string file, int line, string column, string characterId, bool rowEnabled,
            TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            if (!snapshot.CharactersById.TryGetValue(characterId, out CharacterRow character))
            {
                log.Error(file, line, column, characterId,
                    $"{TableDataPaths.CharacterCsvFileName}에 없는 character_id입니다 - " +
                    "그 표에 실제로 있는 행만 가리킬 수 있습니다(대소문자를 구분합니다).");
                return;
            }

            if (rowEnabled && !character.Enabled)
            {
                log.Error(file, line, column, characterId,
                    $"enabled=0인 캐릭터({TableDataPaths.CharacterCsvFileName} {character.Line}행)를 " +
                    "가리킵니다 - 활성 행은 활성 캐릭터만 가리킬 수 있습니다.");
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

        /// <summary>
        /// 카테고리/키 두 칸을 <b>처음부터 끝까지 선택 항목으로</b> 읽는다. 둘 다 비어 있으면 아무것도
        /// 알리지 않고 빈 참조를 돌려준다 - "설명을 아직 쓰지 않았다"는 정상적인 상태이므로 경고조차
        /// 남기지 않는다. <b>한쪽만 채워진 것은 오류</b>다: 참조를 만들 수 없는데도 사람이 무언가를
        /// 적었다는 뜻이라 조용히 비우면 그 의도가 사라진다. 둘 다 있으면 실재 여부까지 검사한다.
        ///
        /// 이름 칸(<see cref="ReadLocalizedName"/>)과 나눠 둔 이유는 판정이 다르기 때문이다 - 이름은
        /// 표마다 필수/선택이 갈리고 반쪽 입력에 경고를 쓰기도 하지만, 설명은 어느 표에서도 필수가
        /// 아니다.
        /// </summary>
        private static LocalizedEntryRef ReadOptionalLocalizedPair(
            CsvTable table, CsvRecord record, string file, int line,
            string categoryColumn, string keyColumn, TableDataDiagnosticLog log)
        {
            string categoryRaw = table.Get(record, categoryColumn);
            string keyRaw = table.Get(record, keyColumn);

            bool hasCategory = !string.IsNullOrEmpty(categoryRaw);
            bool hasKey = !string.IsNullOrEmpty(keyRaw);

            if (!hasCategory && !hasKey) return LocalizedEntryRef.None;

            if (hasCategory && hasKey)
            {
                TableDataFieldRules.TryResolveLocalizedEntry(
                    file, line, categoryColumn, categoryRaw, keyColumn, keyRaw, log, out LocalizedEntryRef entry);
                return entry;
            }

            string emptyColumn = hasCategory ? keyColumn : categoryColumn;
            string emptyValue = hasCategory ? keyRaw : categoryRaw;

            log.Error(file, line, emptyColumn, emptyValue,
                $"{categoryColumn}와 {keyColumn}는 함께 있어야 합니다 - 한쪽만으로는 참조를 만들 수 없습니다" +
                "(둘 다 비워 두는 것은 정상입니다).");

            return LocalizedEntryRef.None;
        }

        /// <summary>world_id 한 칸을 읽고 참조 무결성까지 본다. 다듬지 않은 원본 값을 그대로 판정한다.</summary>
        private static string ReadWorldReference(
            CsvTable table, CsvRecord record, string file, int line,
            bool enabled, TableDataSnapshot snapshot, TableDataDiagnosticLog log,
            string column = TableDataColumns.WorldId)
        {
            string raw = table.Get(record, column);

            if (string.IsNullOrEmpty(raw))
            {
                if (enabled)
                {
                    log.Error(file, line, column, raw,
                        $"enabled=1인 행은 {column}가 필요합니다.");
                }
                else
                {
                    log.Warning(file, line, column, raw,
                        $"{column}가 비어 있습니다 - 소속 월드 없이 생성됩니다.");
                }

                return string.Empty;
            }

            if (!TableDataFieldRules.IsValidId(raw))
            {
                log.Error(file, line, column, raw,
                    $"{column} 형식이 맞지 않습니다 - {TableDataFieldRules.IdPatternText} 를 만족해야 합니다.");
                return string.Empty;
            }

            if (!snapshot.WorldsById.TryGetValue(raw, out WorldRow world))
            {
                log.Error(file, line, column, raw,
                    $"{TableDataPaths.WorldCsvFileName}에 없는 {column}입니다.");
                return raw;
            }

            if (enabled && !world.Enabled)
            {
                log.Error(file, line, column, raw,
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
        /// 이 범위가 <b>생성 에셋을 로드해도 되는 폴더</b>. 출력 쪽 점검은 여기 있는 폴더만 만진다 -
        /// 목록을 한 곳에서만 만들어야 "범위 밖 도메인은 로드하지 않는다"가 두 검사(충돌/orphan)에서
        /// 어긋날 수 없다.
        ///
        /// <b>입력 자산은 여기 들어오지 않는다.</b> 수동 MonsterMotionProfile / CharacterMotionProfile /
        /// Sprite / 수동 ItemDefinition 조회는 범위와 무관하게 그대로 일어난다 - 그것들은 표가
        /// <b>읽는 입력</b>이지 임포터가 <b>쓰는 출력</b>이 아니다.
        /// </summary>
        public static IReadOnlyList<string> GeneratedOutputFolders(TableDataRebuildScope outputScope)
        {
            TableDataRebuildScopes.EnsureSupported(outputScope, nameof(outputScope));

            var folders = new List<string>();

            if (TableDataRebuildScopes.IncludesLegacyDomains(outputScope))
            {
                folders.Add(TableDataPaths.WorldOutputFolder);
                folders.Add(TableDataPaths.CurrencyOutputFolder);
                folders.Add(TableDataPaths.ItemOutputFolder);
                folders.Add(TableDataPaths.MonsterOutputFolder);
                folders.Add(TableDataPaths.DungeonOutputFolder);
            }

            if (TableDataRebuildScopes.IncludesDungeonTable(outputScope))
                folders.Add(TableDataPaths.DungeonOutputFolder);

            if (TableDataRebuildScopes.IncludesCharacterTables(outputScope))
            {
                folders.Add(TableDataPaths.CharacterOutputFolder);
                folders.Add(TableDataPaths.SkillOutputFolder);
                folders.Add(TableDataPaths.CharacterSkillOutputFolder);
            }

            if (TableDataRebuildScopes.IncludesBuildingTable(outputScope))
            {
                folders.Add(TableDataPaths.BuildingOutputFolder);
            }

            if (TableDataRebuildScopes.IncludesRecruitmentTables(outputScope))
            {
                folders.Add(TableDataPaths.CharacterAcquisitionOutputFolder);
                folders.Add(TableDataPaths.RecruitmentTypeOutputFolder);
                folders.Add(TableDataPaths.RecruitmentPoolOutputFolder);
                folders.Add(TableDataPaths.RecruitmentAccessOutputFolder);
            }

            if (TableDataRebuildScopes.IncludesPartyConfigTable(outputScope))
            {
                folders.Add(TableDataPaths.PartyConfigOutputFolder);
            }
            if (TableDataRebuildScopes.IncludesCorruptionConfigTable(outputScope))
            {
                folders.Add(TableDataPaths.CorruptionConfigOutputFolder);
            }
            if (TableDataRebuildScopes.IncludesPurificationConfigTable(outputScope))
            {
                folders.Add(TableDataPaths.PurificationConfigOutputFolder);
            }

            return folders;
        }

        /// <summary>
        /// <see cref="GeneratedOutputFolders"/>를 한 번만 펼쳐 둔 집합. 출력 쪽 점검은 이 집합을
        /// <b>실제로 물어보고</b> 도메인마다 열지 말지를 정한다 - 범위 판정을 두 번 적어 두면
        /// (한 번은 목록을 만들 때, 한 번은 검사에서 다시) 둘이 어긋나도 아무도 알 수 없고,
        /// "목록에 있는 폴더만 연다"는 설명이 코드로 확인되지 않는 말이 된다.
        /// </summary>
        private static HashSet<string> SelectedOutputFolders(TableDataRebuildScope outputScope)
        {
            return new HashSet<string>(GeneratedOutputFolders(outputScope), StringComparer.Ordinal);
        }

        /// <summary>이 폴더를 열어도 되는지. 판정의 근거는 오직 선택된 폴더 집합이다.</summary>
        private static bool InScope(HashSet<string> selected, string outputFolder)
        {
            return selected.Contains(outputFolder);
        }

        /// <summary>
        /// Rebuild가 실제로 건드릴 경로를 <b>쓰기 전에</b> 확인한다. 같은 ID를 가진 생성 에셋이 둘 이상
        /// 있거나, 쓰려는 경로가 다른 종류의 에셋에 이미 점유되어 있으면 여기서 오류로 잡는다 -
        /// 절반쯤 쓴 뒤에 실패해서 프로젝트가 어중간한 상태로 남는 것을 막기 위함이다.
        ///
        /// <b>범위 밖 도메인은 한 번도 로드하지 않는다.</b> 폴더 조회도, 경로 점유 확인도 건너뛴다 -
        /// 이번 Rebuild가 그 경로에 아무것도 쓰지 않으므로 확인할 충돌 자체가 없다. 어느 도메인을 열지는
        /// <see cref="SelectedOutputFolders"/>에게만 묻는다.
        /// </summary>
        private static void CheckOutputConflicts(
            TableDataSnapshot snapshot, TableDataDiagnosticLog log, TableDataRebuildScope outputScope)
        {
            HashSet<string> selected = SelectedOutputFolders(outputScope);

            if (InScope(selected, TableDataPaths.WorldOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<WorldDefinition>(
                        TableDataPaths.WorldOutputFolder, w => w.WorldId),
                    TableDataPaths.WorldCsvFileName, TableDataColumns.WorldId, log);

                foreach (WorldRow row in snapshot.Worlds)
                {
                    CheckOutputPath<WorldDefinition>(
                        TableDataPaths.WorldAssetPath(row.Id), row.Id, w => w.WorldId,
                        TableDataPaths.WorldCsvFileName, row.Line, TableDataColumns.WorldId, row.Id, log);
                }

                CheckOutputPath<WorldCatalog>(
                    TableDataPaths.WorldCatalogAssetPath, null, null,
                    TableDataPaths.WorldCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.WorldCatalogAssetName, log);
            }

            if (InScope(selected, TableDataPaths.CurrencyOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<CurrencyDefinition>(
                        TableDataPaths.CurrencyOutputFolder, c => c.CurrencyId),
                    TableDataPaths.CurrencyCsvFileName, TableDataColumns.CurrencyId, log);

                foreach (CurrencyRow row in snapshot.Currencies)
                {
                    CheckOutputPath<CurrencyDefinition>(
                        TableDataPaths.CurrencyAssetPath(row.Id), row.Id, c => c.CurrencyId,
                        TableDataPaths.CurrencyCsvFileName, row.Line, TableDataColumns.CurrencyId, row.Id, log);
                }

                CheckOutputPath<CurrencyCatalog>(
                    TableDataPaths.CurrencyCatalogAssetPath, null, null,
                    TableDataPaths.CurrencyCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.CurrencyCatalogAssetName, log);
            }

            if (InScope(selected, TableDataPaths.ItemOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<ItemDefinition>(
                        TableDataPaths.ItemOutputFolder, i => i.ItemId),
                    TableDataPaths.ItemCsvFileName, TableDataColumns.ItemId, log);

                foreach (ItemRow row in snapshot.Items)
                {
                    CheckOutputPath<ItemDefinition>(
                        TableDataPaths.ItemAssetPath(row.Id), row.Id, i => i.ItemId,
                        TableDataPaths.ItemCsvFileName, row.Line, TableDataColumns.ItemId, row.Id, log);
                }

                CheckOutputPath<ItemCatalog>(
                    TableDataPaths.ItemCatalogAssetPath, null, null,
                    TableDataPaths.ItemCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.ItemCatalogAssetName, log);
            }

            if (InScope(selected, TableDataPaths.MonsterOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<MonsterDefinition>(
                        TableDataPaths.MonsterOutputFolder, m => m.MonsterId),
                    TableDataPaths.MonsterCsvFileName, TableDataColumns.MonsterId, log);

                foreach (MonsterRow row in snapshot.Monsters)
                {
                    CheckOutputPath<MonsterDefinition>(
                        TableDataPaths.MonsterAssetPath(row.Id), row.Id, m => m.MonsterId,
                        TableDataPaths.MonsterCsvFileName, row.Line, TableDataColumns.MonsterId, row.Id, log);
                }

                CheckOutputPath<MonsterCatalog>(
                    TableDataPaths.MonsterCatalogAssetPath, null, null,
                    TableDataPaths.MonsterCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.MonsterCatalogAssetName, log);
            }

            if (InScope(selected, TableDataPaths.DungeonOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<DungeonDefinition>(
                        TableDataPaths.DungeonOutputFolder, d => d.DungeonId),
                    TableDataPaths.DungeonCsvFileName, TableDataColumns.DungeonId, log);

                foreach (DungeonRow row in snapshot.Dungeons)
                {
                    CheckOutputPath<DungeonDefinition>(
                        TableDataPaths.DungeonAssetPath(row.Id), row.Id, d => d.DungeonId,
                        TableDataPaths.DungeonCsvFileName, row.Line, TableDataColumns.DungeonId, row.Id, log);
                }

                CheckOutputPath<DungeonCatalog>(
                    TableDataPaths.DungeonCatalogAssetPath, null, null,
                    TableDataPaths.DungeonCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.DungeonCatalogAssetName, log);

                if (!TableDataRebuildScopes.IncludesLegacyDomains(outputScope))
                    CheckDungeonReferenceSourcesAreGenerated(snapshot, log);
            }

            if (InScope(selected, TableDataPaths.CharacterOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<CharacterDefinition>(
                        TableDataPaths.CharacterOutputFolder, c => c.CharacterId),
                    TableDataPaths.CharacterCsvFileName, TableDataColumns.CharacterId, log);

                foreach (CharacterRow row in snapshot.Characters)
                {
                    CheckOutputPath<CharacterDefinition>(
                        TableDataPaths.CharacterAssetPath(row.Id), row.Id, c => c.CharacterId,
                        TableDataPaths.CharacterCsvFileName, row.Line, TableDataColumns.CharacterId, row.Id, log);
                }

                CheckOutputPath<CharacterCatalog>(
                    TableDataPaths.CharacterCatalogAssetPath, null, null,
                    TableDataPaths.CharacterCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.CharacterCatalogAssetName, log);

                // Character-only Rebuild는 World를 다시 만들지 않는다. origin_world_id 참조는 이미
                // 만들어진 WorldDefinition을 읽어 이어야 하므로, 쓰기 전에 정확히 하나인지 확인한다.
                if (!TableDataRebuildScopes.IncludesLegacyDomains(outputScope))
                {
                    CheckCharacterOriginWorldSourcesAreGenerated(snapshot, log);
                }
            }

            if (InScope(selected, TableDataPaths.SkillOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<SkillDefinition>(
                        TableDataPaths.SkillOutputFolder, s => s.SkillId),
                    TableDataPaths.SkillCsvFileName, TableDataColumns.SkillId, log);

                foreach (SkillRow row in snapshot.Skills)
                {
                    CheckOutputPath<SkillDefinition>(
                        TableDataPaths.SkillAssetPath(row.Id), row.Id, s => s.SkillId,
                        TableDataPaths.SkillCsvFileName, row.Line, TableDataColumns.SkillId, row.Id, log);
                }

                CheckOutputPath<SkillCatalog>(
                    TableDataPaths.SkillCatalogAssetPath, null, null,
                    TableDataPaths.SkillCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.SkillCatalogAssetName, log);
            }

            if (InScope(selected, TableDataPaths.BuildingOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<BuildingDefinition>(
                        TableDataPaths.BuildingOutputFolder, b => b.BuildingId),
                    TableDataPaths.BuildingCsvFileName, TableDataColumns.BuildingId, log);

                foreach (BuildingRow row in snapshot.Buildings)
                {
                    CheckOutputPath<BuildingDefinition>(
                        TableDataPaths.BuildingAssetPath(row.Id), row.Id, b => b.BuildingId,
                        TableDataPaths.BuildingCsvFileName, row.Line, TableDataColumns.BuildingId, row.Id, log);
                }

                CheckOutputPath<BuildingCatalog>(
                    TableDataPaths.BuildingCatalogAssetPath, null, null,
                    TableDataPaths.BuildingCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.BuildingCatalogAssetName, log);

                // 좁은 범위(Building만)에서는 이번 Rebuild가 Currency/Item 에셋을 만들지 않는다.
                // 그러면 비용 참조를 어디서 가져올지가 문제가 되므로 <b>여기서 미리</b> 확인한다.
                if (!TableDataRebuildScopes.IncludesLegacyDomains(outputScope))
                {
                    CheckBuildingCostSourcesAreGenerated(snapshot, log);
                }
            }

            if (InScope(selected, TableDataPaths.CharacterAcquisitionOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<CharacterAcquisitionDefinition>(
                        TableDataPaths.CharacterAcquisitionOutputFolder, a => a.AcquisitionId),
                    TableDataPaths.CharacterAcquisitionCsvFileName, TableDataColumns.AcquisitionId, log);

                foreach (CharacterAcquisitionRow row in snapshot.CharacterAcquisitions)
                {
                    CheckOutputPath<CharacterAcquisitionDefinition>(
                        TableDataPaths.CharacterAcquisitionAssetPath(row.Id), row.Id, a => a.AcquisitionId,
                        TableDataPaths.CharacterAcquisitionCsvFileName, row.Line,
                        TableDataColumns.AcquisitionId, row.Id, log);
                }

                CheckOutputPath<CharacterAcquisitionCatalog>(
                    TableDataPaths.CharacterAcquisitionCatalogAssetPath, null, null,
                    TableDataPaths.CharacterAcquisitionCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.CharacterAcquisitionCatalogAssetName, log);
            }

            if (InScope(selected, TableDataPaths.RecruitmentTypeOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<RecruitmentTypeDefinition>(
                        TableDataPaths.RecruitmentTypeOutputFolder, t => t.RecruitmentTypeId),
                    TableDataPaths.RecruitmentTypeCsvFileName, TableDataColumns.RecruitmentTypeId, log);

                foreach (RecruitmentTypeRow row in snapshot.RecruitmentTypes)
                {
                    CheckOutputPath<RecruitmentTypeDefinition>(
                        TableDataPaths.RecruitmentTypeAssetPath(row.Id), row.Id, t => t.RecruitmentTypeId,
                        TableDataPaths.RecruitmentTypeCsvFileName, row.Line,
                        TableDataColumns.RecruitmentTypeId, row.Id, log);
                }

                CheckOutputPath<RecruitmentTypeCatalog>(
                    TableDataPaths.RecruitmentTypeCatalogAssetPath, null, null,
                    TableDataPaths.RecruitmentTypeCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.RecruitmentTypeCatalogAssetName, log);
            }

            if (InScope(selected, TableDataPaths.RecruitmentPoolOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<RecruitmentPoolEntryDefinition>(
                        TableDataPaths.RecruitmentPoolOutputFolder, e => e.PairId),
                    TableDataPaths.RecruitmentPoolCsvFileName, TableDataColumns.PoolEntryId, log);

                foreach (RecruitmentPoolRow row in snapshot.RecruitmentPools)
                {
                    CheckOutputPath<RecruitmentPoolEntryDefinition>(
                        TableDataPaths.RecruitmentPoolAssetPath(row.PairId), row.PairId, e => e.PairId,
                        TableDataPaths.RecruitmentPoolCsvFileName, row.Line,
                        TableDataColumns.PoolEntryId, row.PairId, log);
                }

                CheckOutputPath<RecruitmentPoolCatalog>(
                    TableDataPaths.RecruitmentPoolCatalogAssetPath, null, null,
                    TableDataPaths.RecruitmentPoolCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.RecruitmentPoolCatalogAssetName, log);
            }

            if (InScope(selected, TableDataPaths.RecruitmentAccessOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<RecruitmentAccessDefinition>(
                        TableDataPaths.RecruitmentAccessOutputFolder, a => a.RecruitmentAccessId),
                    TableDataPaths.RecruitmentAccessCsvFileName, TableDataColumns.RecruitmentAccessId, log);

                foreach (RecruitmentAccessRow row in snapshot.RecruitmentAccesses)
                {
                    CheckOutputPath<RecruitmentAccessDefinition>(
                        TableDataPaths.RecruitmentAccessAssetPath(row.Id), row.Id, a => a.RecruitmentAccessId,
                        TableDataPaths.RecruitmentAccessCsvFileName, row.Line,
                        TableDataColumns.RecruitmentAccessId, row.Id, log);
                }

                CheckOutputPath<RecruitmentAccessCatalog>(
                    TableDataPaths.RecruitmentAccessCatalogAssetPath, null, null,
                    TableDataPaths.RecruitmentAccessCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.RecruitmentAccessCatalogAssetName, log);
            }

            if (InScope(selected, TableDataPaths.PartyConfigOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<PartyConfigDefinition>(
                        TableDataPaths.PartyConfigOutputFolder, c => c.ConfigId),
                    TableDataPaths.PartyConfigCsvFileName, TableDataColumns.PartyConfigId, log);

                foreach (PartyConfigRow row in snapshot.PartyConfigs)
                {
                    CheckOutputPath<PartyConfigDefinition>(
                        TableDataPaths.PartyConfigAssetPath(row.Id), row.Id, c => c.ConfigId,
                        TableDataPaths.PartyConfigCsvFileName, row.Line,
                        TableDataColumns.PartyConfigId, row.Id, log);
                }

                CheckOutputPath<PartyConfigCatalog>(
                    TableDataPaths.PartyConfigCatalogAssetPath, null, null,
                    TableDataPaths.PartyConfigCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.PartyConfigCatalogAssetName, log);
            }

            if (InScope(selected, TableDataPaths.CorruptionConfigOutputFolder))
            {
                CheckDuplicateGenerated(
                    TableDataAssetIndex.LoadGeneratedById<Corruption.CorruptionConfigDefinition>(
                        TableDataPaths.CorruptionConfigOutputFolder, c => c.ConfigId),
                    TableDataPaths.CorruptionConfigCsvFileName, TableDataColumns.ConfigId, log);
                foreach (CorruptionConfigRow row in snapshot.CorruptionConfigs)
                {
                    CheckOutputPath<Corruption.CorruptionConfigDefinition>(
                        TableDataPaths.CorruptionConfigAssetPath(row.Id), row.Id, c => c.ConfigId,
                        TableDataPaths.CorruptionConfigCsvFileName, row.Line, TableDataColumns.ConfigId, row.Id, log);
                }
                CheckOutputPath<Corruption.CorruptionConfigCatalog>(
                    TableDataPaths.CorruptionConfigCatalogAssetPath, null, null,
                    TableDataPaths.CorruptionConfigCsvFileName, TableDataDiagnostic.FileLevelRow,
                    TableDataColumns.FilePseudoColumn, TableDataPaths.CorruptionConfigCatalogAssetName, log);
            }

            // 모집만 다시 만드는 좁은 범위에서는 이번 Rebuild가 Character 에셋을 만들지 않는다.
            // 그러면 후보/획득 참조를 어디서 가져올지가 문제가 되므로 <b>여기서 미리</b> 확인한다
            // (Building만 다시 만드는 범위가 Currency/Item을 확인하는 것과 같은 이유다).
            if (InScope(selected, TableDataPaths.CharacterAcquisitionOutputFolder)
                && !TableDataRebuildScopes.IncludesCharacterTables(outputScope))
            {
                CheckRecruitmentCharacterSourcesAreGenerated(snapshot, log);
            }

            if (!InScope(selected, TableDataPaths.CharacterSkillOutputFolder)) return;

            CheckDuplicateGenerated(
                TableDataAssetIndex.LoadGeneratedById<CharacterSkillDefinition>(
                    TableDataPaths.CharacterSkillOutputFolder, r => r.PairId),
                TableDataPaths.CharacterSkillCsvFileName, TableDataColumns.CharacterId, log);

            foreach (CharacterSkillRow row in snapshot.CharacterSkills)
            {
                CheckOutputPath<CharacterSkillDefinition>(
                    TableDataPaths.CharacterSkillAssetPath(row.PairId), row.PairId, r => r.PairId,
                    TableDataPaths.CharacterSkillCsvFileName, row.Line, TableDataColumns.CharacterId, row.PairId, log);
            }

            CheckOutputPath<CharacterSkillCatalog>(
                TableDataPaths.CharacterSkillCatalogAssetPath, null, null,
                TableDataPaths.CharacterSkillCsvFileName, TableDataDiagnostic.FileLevelRow,
                TableDataColumns.FilePseudoColumn, TableDataPaths.CharacterSkillCatalogAssetName, log);
        }

        private static void CheckDuplicateGenerated<T>(
            Dictionary<string, List<T>> map, string file, string idColumn, TableDataDiagnosticLog log)
            where T : ScriptableObject
        {
            foreach (KeyValuePair<string, List<T>> pair in map)
            {
                if (pair.Value.Count <= 1) continue;

                // ID가 빈 그룹도 여기까지 온다(생성 폴더 조회가 버리지 않는다). 빈 값을 그대로 찍으면
                // 진단이 "value ''"가 되어 무엇을 말하는지 알 수 없으므로 orphan과 같은 표기를 쓴다.
                string id = string.IsNullOrEmpty(pair.Key) ? EmptyIdLabel : pair.Key;

                log.Error(file, TableDataDiagnostic.FileLevelRow, idColumn, id,
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
        private static void CheckOrphans(
            TableDataSnapshot snapshot, TableDataDiagnosticLog log, TableDataRebuildScope outputScope)
        {
            HashSet<string> selected = SelectedOutputFolders(outputScope);

            if (InScope(selected, TableDataPaths.WorldOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<WorldDefinition>(TableDataPaths.WorldOutputFolder, w => w.WorldId),
                    snapshot.WorldsById.Keys, TableDataPaths.WorldCsvFileName, TableDataColumns.WorldId, log);
            }

            if (InScope(selected, TableDataPaths.CurrencyOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<CurrencyDefinition>(TableDataPaths.CurrencyOutputFolder, c => c.CurrencyId),
                    snapshot.CurrenciesById.Keys, TableDataPaths.CurrencyCsvFileName, TableDataColumns.CurrencyId, log);
            }

            if (InScope(selected, TableDataPaths.ItemOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<ItemDefinition>(TableDataPaths.ItemOutputFolder, i => i.ItemId),
                    snapshot.ItemsById.Keys, TableDataPaths.ItemCsvFileName, TableDataColumns.ItemId, log);
            }

            if (InScope(selected, TableDataPaths.MonsterOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<MonsterDefinition>(TableDataPaths.MonsterOutputFolder, m => m.MonsterId),
                    snapshot.MonstersById.Keys, TableDataPaths.MonsterCsvFileName, TableDataColumns.MonsterId, log);
            }

            if (InScope(selected, TableDataPaths.DungeonOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<DungeonDefinition>(TableDataPaths.DungeonOutputFolder, d => d.DungeonId),
                    snapshot.DungeonsById.Keys, TableDataPaths.DungeonCsvFileName, TableDataColumns.DungeonId, log);
            }

            // 캐릭터 쪽 세 도메인도 <b>같은 규칙</b>이다 - 자기 출력 폴더 안만 보고, 지우지 않으며,
            // 카탈로그에서만 빠진다. 새 도메인이라고 자동 삭제를 도입하지 않은 이유는 그것이 되돌릴 수
            // 없는 동작이기 때문이다: 씬이나 프리팹이 그 에셋을 이미 참조하고 있으면 삭제는 참조를
            // 끊고 GUID를 영원히 없앤다. 무엇이 남았는지는 경고로 다 보이므로 놓치지도 않는다.
            if (InScope(selected, TableDataPaths.CharacterOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<CharacterDefinition>(TableDataPaths.CharacterOutputFolder, c => c.CharacterId),
                    snapshot.CharactersById.Keys, TableDataPaths.CharacterCsvFileName, TableDataColumns.CharacterId, log);
            }

            if (InScope(selected, TableDataPaths.SkillOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<SkillDefinition>(TableDataPaths.SkillOutputFolder, s => s.SkillId),
                    snapshot.SkillsById.Keys, TableDataPaths.SkillCsvFileName, TableDataColumns.SkillId, log);
            }

            if (InScope(selected, TableDataPaths.CharacterSkillOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<CharacterSkillDefinition>(TableDataPaths.CharacterSkillOutputFolder, r => r.PairId),
                    snapshot.CharacterSkillsByPairId.Keys, TableDataPaths.CharacterSkillCsvFileName,
                    TableDataColumns.CharacterId, log);
            }

            if (InScope(selected, TableDataPaths.BuildingOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<BuildingDefinition>(TableDataPaths.BuildingOutputFolder, b => b.BuildingId),
                    snapshot.BuildingsById.Keys, TableDataPaths.BuildingCsvFileName,
                    TableDataColumns.BuildingId, log);
            }

            // 모집 쪽 네 도메인도 <b>같은 규칙</b>이다 - 자기 출력 폴더 안만 보고, 지우지 않으며,
            // 카탈로그에서만 빠진다.
            if (InScope(selected, TableDataPaths.CharacterAcquisitionOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<CharacterAcquisitionDefinition>(
                        TableDataPaths.CharacterAcquisitionOutputFolder, a => a.AcquisitionId),
                    snapshot.CharacterAcquisitionsById.Keys, TableDataPaths.CharacterAcquisitionCsvFileName,
                    TableDataColumns.AcquisitionId, log);
            }

            if (InScope(selected, TableDataPaths.RecruitmentTypeOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<RecruitmentTypeDefinition>(
                        TableDataPaths.RecruitmentTypeOutputFolder, t => t.RecruitmentTypeId),
                    snapshot.RecruitmentTypesById.Keys, TableDataPaths.RecruitmentTypeCsvFileName,
                    TableDataColumns.RecruitmentTypeId, log);
            }

            if (InScope(selected, TableDataPaths.RecruitmentPoolOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<RecruitmentPoolEntryDefinition>(
                        TableDataPaths.RecruitmentPoolOutputFolder, e => e.PairId),
                    snapshot.RecruitmentPoolsByPairId.Keys, TableDataPaths.RecruitmentPoolCsvFileName,
                    TableDataColumns.PoolEntryId, log);
            }

            if (InScope(selected, TableDataPaths.RecruitmentAccessOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<RecruitmentAccessDefinition>(
                        TableDataPaths.RecruitmentAccessOutputFolder, a => a.RecruitmentAccessId),
                    snapshot.RecruitmentAccessesById.Keys, TableDataPaths.RecruitmentAccessCsvFileName,
                    TableDataColumns.RecruitmentAccessId, log);
            }

            // 파티 설정도 <b>같은 규칙</b>이다 - 자기 출력 폴더 안만 보고, 지우지 않으며,
            // 카탈로그에서만 빠진다.
            if (InScope(selected, TableDataPaths.PartyConfigOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<PartyConfigDefinition>(
                        TableDataPaths.PartyConfigOutputFolder, c => c.ConfigId),
                    snapshot.PartyConfigsById.Keys, TableDataPaths.PartyConfigCsvFileName,
                    TableDataColumns.PartyConfigId, log);
            }

            if (InScope(selected, TableDataPaths.CorruptionConfigOutputFolder))
            {
                ReportOrphans(
                    TableDataAssetIndex.LoadGeneratedById<Corruption.CorruptionConfigDefinition>(
                        TableDataPaths.CorruptionConfigOutputFolder, c => c.ConfigId),
                    snapshot.CorruptionConfigsById.Keys, TableDataPaths.CorruptionConfigCsvFileName,
                    TableDataColumns.ConfigId, log);
            }
        }

        /// <summary>
        /// Character-only 좁은 범위에서, 각 Character.csv 행의 origin_world_id가 가리키는
        /// <see cref="WorldDefinition"/> 생성 에셋이 이미 정확히 하나 있는지 쓰기 전에 확인한다.
        /// 이 범위는 World 표를 다시 만들지 않으므로, 없거나 중복된 대상을 참조하면 안 된다.
        /// 읽기만 하며 World 생성 에셋을 dirty 또는 저장하지 않는다.
        /// </summary>
        private static void CheckCharacterOriginWorldSourcesAreGenerated(
            TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            Dictionary<string, List<WorldDefinition>> worlds =
                TableDataAssetIndex.LoadGeneratedById<WorldDefinition>(
                    TableDataPaths.WorldOutputFolder, w => w.WorldId);

            foreach (CharacterRow row in snapshot.Characters)
            {
                if (string.IsNullOrEmpty(row.OriginWorldId)) continue;

                RequireSingleGenerated(
                    worlds, row.OriginWorldId, TableDataPaths.WorldOutputFolder,
                    nameof(WorldDefinition), row.Line, TableDataColumns.OriginWorldId, log,
                    TableDataPaths.CharacterCsvFileName);
            }
        }

        /// <summary>
        /// 모집만 다시 만드는 좁은 범위에서, 후보와 획득 방식이 가리키는 <b>CharacterDefinition 생성
        /// 에셋이 이미 있는지</b>를 쓰기 전에 확인한다.
        ///
        /// 이 범위는 Character 표를 다시 만들지 않으므로 참조할 대상이 이번 Rebuild의 메모리에 없다.
        /// 그래서 Rebuild는 <b>이미 만들어져 있는 생성 에셋</b>을 읽어 참조를 잇는데, 그 에셋이 없으면
        /// 참조가 조용히 비어 버린다 - 여기서 오류로 잡으면 아무것도 쓰이지 않으므로 그런 에셋이
        /// 만들어질 자리가 없다(<see cref="CheckBuildingCostSourcesAreGenerated"/>와 같은 이유다).
        ///
        /// <b>읽기만 한다.</b> Character 생성 폴더를 로드할 뿐 <c>SetDirty</c>도 저장도 하지 않는다.
        /// </summary>
        private static void CheckRecruitmentCharacterSourcesAreGenerated(
            TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            Dictionary<string, List<CharacterDefinition>> characters =
                TableDataAssetIndex.LoadGeneratedById<CharacterDefinition>(
                    TableDataPaths.CharacterOutputFolder, c => c.CharacterId);

            foreach (CharacterAcquisitionRow row in snapshot.CharacterAcquisitions)
            {
                RequireSingleGeneratedCharacter(
                    characters, row.CharacterId, TableDataPaths.CharacterAcquisitionCsvFileName, row.Line, log);
            }

            foreach (RecruitmentPoolRow row in snapshot.RecruitmentPools)
            {
                RequireSingleGeneratedCharacter(
                    characters, row.CharacterId, TableDataPaths.RecruitmentPoolCsvFileName, row.Line, log);
            }
        }

        /// <summary>그 캐릭터의 생성 에셋이 <b>정확히 하나</b> 있는지. 없으면 참조를 이을 수 없고,
        /// 여럿이면 어느 것을 이을지 정할 수 없다 - 둘 다 오류이며 어느 쪽이든 아무것도 쓰이지 않는다.</summary>
        private static void RequireSingleGeneratedCharacter(
            Dictionary<string, List<CharacterDefinition>> generated, string characterId,
            string file, int line, TableDataDiagnosticLog log)
        {
            int count = generated.TryGetValue(characterId, out List<CharacterDefinition> matches) ? matches.Count : 0;
            if (count == 1) return;

            log.Error(file, line, TableDataColumns.CharacterId, characterId, count == 0
                ? $"'{TableDataPaths.CharacterOutputFolder}' 아래에 ID가 '{characterId}'인 " +
                  $"{nameof(CharacterDefinition)} 생성 에셋이 없습니다 - 모집만 다시 만드는 범위는 그 " +
                  "에셋을 만들지 않으므로, 먼저 Character 표를 포함한 Rebuild를 돌린 뒤 다시 실행하세요."
                : $"'{TableDataPaths.CharacterOutputFolder}' 아래에 ID가 '{characterId}'인 " +
                  $"{nameof(CharacterDefinition)} 생성 에셋이 {count}개 있어 어느 것을 참조할지 정할 수 " +
                  "없습니다 - 하나만 남기세요.");
        }

        /// <summary>
        /// Building만 다시 만드는 좁은 범위에서, 비용이 가리키는 <b>CurrencyDefinition /
        /// ItemDefinition 생성 에셋이 이미 있는지</b>를 쓰기 전에 확인한다.
        ///
        /// 이 범위는 Currency/Item 표를 다시 만들지 않으므로 참조할 대상이 이번 Rebuild의 메모리에
        /// 없다. 그래서 Rebuild는 <b>이미 만들어져 있는 생성 에셋</b>을 읽어 참조를 잇는데, 그 에셋이
        /// 없으면 참조가 조용히 비어 버린다 - "비용이 적혀 있는데 아무것도 안 내는 건물"이 그 결과다.
        /// 여기서 오류로 잡으면 아무것도 쓰이지 않으므로 그런 에셋이 만들어질 자리가 없다.
        ///
        /// <b>읽기만 한다.</b> Currency/Item 생성 폴더를 <see cref="AssetDatabase"/>로 로드할 뿐
        /// <c>SetDirty</c>도 저장도 하지 않으므로 그 도메인의 파일은 한 바이트도 달라지지 않는다 -
        /// 범위가 막는 것은 <b>쓰기</b>이지 읽기가 아니다(입력 자산을 읽는 것과 같은 성격이다).
        /// 전체 범위에서는 두 표가 같은 Rebuild에서 함께 만들어지므로 이 확인 자체가 필요 없다.
        /// </summary>
        private static void CheckBuildingCostSourcesAreGenerated(
            TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            Dictionary<string, List<CurrencyDefinition>> currencies =
                TableDataAssetIndex.LoadGeneratedById<CurrencyDefinition>(
                    TableDataPaths.CurrencyOutputFolder, c => c.CurrencyId);
            Dictionary<string, List<ItemDefinition>> items =
                TableDataAssetIndex.LoadGeneratedById<ItemDefinition>(
                    TableDataPaths.ItemOutputFolder, i => i.ItemId);

            foreach (BuildingRow row in snapshot.Buildings)
            {
                if (!string.IsNullOrEmpty(row.CostCurrencyId))
                {
                    RequireSingleGenerated(
                        currencies, row.CostCurrencyId, TableDataPaths.CurrencyOutputFolder,
                        nameof(CurrencyDefinition), row.Line, TableDataColumns.CostCurrencyId, log);
                }

                foreach (BuildingItemCostRow cost in row.ItemCosts)
                {
                    RequireSingleGenerated(
                        items, cost.ItemId, TableDataPaths.ItemOutputFolder,
                        nameof(ItemDefinition), row.Line, TableDataColumns.CostItemIds, log);
                }
            }
        }

        /// <summary>
        /// Dungeon 전용 Rebuild는 World/Monster/Item을 다시 쓰지 않는다. 따라서 던전 행이 참조하는
        /// 생성 에셋이 각각 정확히 하나인지 먼저 확인해 null 참조가 저장되는 것을 막는다.
        /// </summary>
        private static void CheckDungeonReferenceSourcesAreGenerated(
            TableDataSnapshot snapshot, TableDataDiagnosticLog log)
        {
            Dictionary<string, List<WorldDefinition>> worlds =
                TableDataAssetIndex.LoadGeneratedById<WorldDefinition>(
                    TableDataPaths.WorldOutputFolder, value => value.WorldId);
            Dictionary<string, List<MonsterDefinition>> monsters =
                TableDataAssetIndex.LoadGeneratedById<MonsterDefinition>(
                    TableDataPaths.MonsterOutputFolder, value => value.MonsterId);
            Dictionary<string, List<ItemDefinition>> items =
                TableDataAssetIndex.LoadGeneratedById<ItemDefinition>(
                    TableDataPaths.ItemOutputFolder, value => value.ItemId);

            foreach (DungeonRow row in snapshot.Dungeons)
            {
                if (!string.IsNullOrEmpty(row.WorldId))
                {
                    RequireSingleGenerated(worlds, row.WorldId, TableDataPaths.WorldOutputFolder,
                        nameof(WorldDefinition), row.Line, TableDataColumns.WorldId, log,
                        TableDataPaths.DungeonCsvFileName);
                }

                foreach (string monsterId in row.MonsterIds)
                {
                    RequireSingleGenerated(monsters, monsterId, TableDataPaths.MonsterOutputFolder,
                        nameof(MonsterDefinition), row.Line, TableDataColumns.MonsterIds, log,
                        TableDataPaths.DungeonCsvFileName);
                }

                foreach (string itemId in row.RewardItemIds)
                {
                    RequireSingleGenerated(items, itemId, TableDataPaths.ItemOutputFolder,
                        nameof(ItemDefinition), row.Line, TableDataColumns.RewardItemIds, log,
                        TableDataPaths.DungeonCsvFileName);
                }
            }
        }

        /// <summary>그 ID의 생성 에셋이 <b>정확히 하나</b> 있는지. 없으면(0개) 참조를 이을 수 없고,
        /// 여럿이면 어느 것을 이을지 정할 수 없다 - 둘 다 오류이며 어느 쪽이든 아무것도 쓰이지 않는다.</summary>
        private static void RequireSingleGenerated<T>(
            Dictionary<string, List<T>> generated, string id, string folder, string typeName,
            int line, string column, TableDataDiagnosticLog log, string file = null) where T : ScriptableObject
        {
            int count = generated.TryGetValue(id, out List<T> matches) ? matches.Count : 0;
            if (count == 1) return;

            log.Error(file ?? TableDataPaths.BuildingCsvFileName, line, column, id, count == 0
                ? $"'{folder}' 아래에 ID가 '{id}'인 {typeName} 생성 에셋이 없습니다 - " +
                  "이 좁은 Rebuild 범위는 그 에셋을 만들지 않으므로, 먼저 그 표를 포함한 Rebuild를 " +
                  "실행한 뒤 다시 시도하세요."
                : $"'{folder}' 아래에 ID가 '{id}'인 {typeName} 생성 에셋이 {count}개 있어 " +
                  "어느 것을 참조할지 정할 수 없습니다 - 하나만 남기세요.");
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
                    string id = string.IsNullOrEmpty(pair.Key) ? EmptyIdLabel : pair.Key;
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
