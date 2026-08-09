using System;
using System.Collections.Generic;
using System.Globalization;
using Character;
using Dungeon;
using Inventory;
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

        /// <summary>여덟 표가 모두 읽힌 경우의 파싱 결과. 파일/헤더 단계에서 실패하면 null이다.</summary>
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
            $"CharacterSkill {Snapshot?.CharacterSkills.Count ?? 0} 행, 오류 {ErrorCount}건, 경고 {WarningCount}건";
    }

    /// <summary>
    /// CSV 여덟 장을 읽고 <b>모든</b> 문제를 모아 보고한다. <b>에셋도 폴더도 CSV도 만들지 않고 고치지도
    /// 않는다</b> - Validate는 순수하게 읽기만 하는 동작이라, 사람이 결과를 보고 판단할 때까지
    /// 프로젝트 상태가 한 글자도 바뀌지 않는다.
    ///
    /// <b>첫 오류에서 멈추지 않는다.</b> World → Currency → Item → Monster → Dungeon → Character →
    /// Skill → CharacterSkill 순서로 끝까지 읽고, 참조 무결성까지 검사한 뒤 한 번에 돌려준다. 순서가
    /// 정해져 있는 이유는 뒤의 표가 앞의 표를 참조하기 때문이다(Monster는 World / Currency / Item을,
    /// Dungeon은 World / Monster / Item을, CharacterSkill은 Character / Skill을 가리킨다).
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

        /// <summary>메뉴와 테스트가 함께 쓰는 진입점. 부작용이 전혀 없다.
        /// 생성 에셋 쪽 점검까지 <b>여덟 도메인 전부</b>를 본다(기존 동작 그대로).</summary>
        public static TableDataValidationResult Validate()
        {
            return Validate(TableDataRebuildScope.All);
        }

        /// <summary>
        /// <paramref name="outputScope"/>는 <b>생성 에셋 쪽 점검의 범위만</b> 정한다.
        ///
        /// <b>CSV 입력 검증은 범위와 무관하게 언제나 여덟 표 전부다.</b> 파일/헤더/행/값/표 사이의
        /// 참조와 Localization·Motion Profile·Sprite 같은 <b>입력 자산</b> 조회는 하나도 줄어들지
        /// 않는다 - 범위를 좁혔다고 검사가 느슨해지면 "좁게 돌렸더니 통과했다"는 상태가 생긴다.
        ///
        /// 좁아지는 것은 <see cref="CheckOutputConflicts"/>와 <see cref="CheckOrphans"/>뿐이며,
        /// 이 둘은 <see cref="GeneratedOutputFolders"/>가 돌려주는 폴더만 로드한다. 범위 밖 도메인의
        /// 생성 폴더에는 <see cref="TableDataAssetIndex.LoadGeneratedById{T}"/>도
        /// <see cref="AssetDatabase.LoadAssetAtPath{T}"/>도 부르지 않는다 - 그래야 "targeted Rebuild는
        /// 기존 다섯 도메인의 생성 에셋을 로드조차 하지 않는다"가 말이 아니라 코드가 된다.
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

            var snapshot = new TableDataSnapshot();
            bool allTablesRead = worldTable != null && currencyTable != null && itemTable != null
                                 && monsterTable != null && dungeonTable != null
                                 && characterTable != null && skillTable != null && characterSkillTable != null;

            try
            {
                if (worldTable != null) ValidateWorlds(worldTable, snapshot, log);
                if (currencyTable != null) ValidateCurrencies(currencyTable, snapshot, assets, log);
                if (itemTable != null) ValidateItems(itemTable, snapshot, assets, log);
                if (monsterTable != null) ValidateMonsters(monsterTable, snapshot, assets, log);
                if (dungeonTable != null) ValidateDungeons(dungeonTable, snapshot, assets, log);
                if (characterTable != null) ValidateCharacters(characterTable, snapshot, assets, log);
                if (skillTable != null) ValidateSkills(skillTable, snapshot, assets, log);
                if (characterSkillTable != null) ValidateCharacterSkills(characterSkillTable, snapshot, log);

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

                // 아이템 이름은 Monster와 같은 선택 항목이다 - 인벤토리 슬롯은 아이콘과 수량만 그리므로
                // 이름이 비어 있어도 표시가 성립한다. 반쪽만 채워진 것은 실수일 가능성이 높아 경고한다.
                row.Name = ReadLocalizedName(
                    table, record, file, line, row.Enabled, nameRequiredWhenEnabled: false, log);

                ReadIcon(table, record, file, line, assets, row, log);

                if (!idOk) continue;

                snapshot.Items.Add(row);
                snapshot.ItemsById[row.Id] = row;
            }
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
                ReadRewardList(table, record, file, line, snapshot, row, log);

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

            folders.Add(TableDataPaths.CharacterOutputFolder);
            folders.Add(TableDataPaths.SkillOutputFolder);
            folders.Add(TableDataPaths.CharacterSkillOutputFolder);
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
