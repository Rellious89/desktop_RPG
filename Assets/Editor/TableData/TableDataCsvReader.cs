using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace TableDataEditor
{
    /// <summary>
    /// CSV 열세 장의 <b>컬럼 이름</b>. CSV는 이 이름을 그대로 써야 한다 -
    /// 헤더가 어긋나면 값을 추측해 읽지 않고 오류로 멈춘다.
    /// </summary>
    public static class TableDataColumns
    {
        // 공통
        public const string DisplayOrder = "display_order";
        public const string Enabled = "enabled";
        public const string Memo = "memo";
        public const string NameCategory = "name_category";
        public const string NameKey = "name_key";
        public const string WorldId = "world_id";

        // World.csv
        public static readonly string[] World =
        {
            WorldId, NameCategory, NameKey, DisplayOrder, Enabled, Memo,
        };

        // Item.csv
        //
        // 사람이 읽는 이름과 설명은 <c>$item_name</c> / <c>$item_description</c>이라는 참조 컬럼으로
        // 따로 두며 여기 넣지 않는다 - 넣으면 필수 컬럼이 되고 임포터가 값을 읽는 칸처럼 보인다.
        // description_category / description_key는 Skill.csv와 <b>같은 상수</b>를 쓴다(아래 Skill.csv
        // 절에 정의) - 두 표가 같은 뜻의 칸을 가리키므로 이름을 두 번 적어 두면 한쪽만 고쳐질 수 있다.
        public const string ItemId = "item_id";
        public const string IconKey = "icon_key";

        public static readonly string[] Item =
        {
            ItemId, NameCategory, NameKey, DescriptionCategory, DescriptionKey, IconKey,
            DisplayOrder, Enabled, Memo,
        };

        // Currency.csv
        //
        // 사람이 읽는 재화 이름은 <c>$currency_name</c>이라는 참조 컬럼으로 따로 두며, 여기 넣지 않는다 -
        // 넣으면 필수 컬럼이 되고 임포터가 값을 읽는 칸처럼 보인다. currency_id 컬럼 이름은
        // Monster.csv의 참조 칸과 <b>같은 상수</b>를 쓴다(아래 Monster.csv 절에 정의) - 두 표가 같은
        // 이름을 가리켜야 참조가 성립하므로, 이름을 두 번 적어 두면 한쪽만 고쳐질 수 있다.
        public static readonly string[] Currency =
        {
            CurrencyId, NameCategory, NameKey, IconKey, DisplayOrder, Enabled, Memo,
        };

        // Monster.csv
        public const string MonsterId = "monster_id";
        public const string BaseMonsterId = "base_monster_id";
        public const string MotionProfileKey = "motion_profile_key";
        public const string PreviewSpriteKey = "preview_sprite_key";
        public const string MaxDurability = "max_durability";

        /// <summary>드롭 슬롯 개수. <b>CSV는 항상 이만큼의 칸을 가진다</b> - 슬롯 수를 늘리고 줄이는 것은
        /// 표 구조를 바꾸는 일이므로 데이터가 아니라 코드에서 정한다.</summary>
        public const int MonsterDropSlotCount = 3;

        /// <summary>슬롯 번호(1부터)로 드롭 컬럼 이름을 만든다. 이름을 한 곳에서만 만들어 두면
        /// 헤더 상수와 읽는 코드가 어긋날 수 없다.</summary>
        public static string DropItemId(int slot) => "drop_item_id_" + slot;

        public static string DropChance(int slot) => "drop_chance_" + slot;

        public static string DropCount(int slot) => "drop_count_" + slot;

        /// <summary>처치 재화 보상 세 칸. <b>세 칸이 한 덩어리</b>다 - id가 비면 금액 두 칸도 비어야 하고,
        /// id가 있으면 금액 두 칸이 모두 있어야 한다. 사람이 읽는 재화 이름은 <c>$currency_name</c>이라는
        /// 참조 컬럼으로 따로 두며, 임포터는 그 칸을 읽지 않는다.</summary>
        public const string CurrencyId = "currency_id";

        public const string CurrencyAmountMin = "currency_amount_min";

        public const string CurrencyAmountMax = "currency_amount_max";

        public static readonly string[] Monster = BuildMonsterColumns();

        private static string[] BuildMonsterColumns()
        {
            var columns = new List<string>
            {
                MonsterId, NameCategory, NameKey, WorldId, MotionProfileKey, PreviewSpriteKey,
                MaxDurability, BaseMonsterId,
            };

            for (int slot = 1; slot <= MonsterDropSlotCount; slot++)
            {
                // $drop_item_name_n은 작업자용 참조 컬럼이라 여기 넣지 않는다 - 넣으면 필수 컬럼이 되고,
                // 임포터가 값을 읽는 칸처럼 보이게 된다.
                columns.Add(DropItemId(slot));
                columns.Add(DropChance(slot));
                columns.Add(DropCount(slot));
            }

            // $currency_name도 $drop_item_name_n과 같은 작업자용 참조 컬럼이라 여기 넣지 않는다.
            columns.Add(CurrencyId);
            columns.Add(CurrencyAmountMin);
            columns.Add(CurrencyAmountMax);

            columns.Add(DisplayOrder);
            columns.Add(Enabled);
            columns.Add(Memo);
            return columns.ToArray();
        }

        // Dungeon.csv
        public const string DungeonId = "dungeon_id";
        public const string RepresentativeSpriteKey = "representative_sprite_key";
        public const string MonsterIds = "monster_ids";
        public const string RewardItemIds = "reward_item_ids";
        public const string CorruptionGainPerDefeat = "corruption_gain_per_defeat";

        public static readonly string[] Dungeon =
        {
            DungeonId, NameCategory, NameKey, WorldId, RepresentativeSpriteKey, CorruptionGainPerDefeat,
            MonsterIds, RewardItemIds, DisplayOrder, Enabled, Memo,
        };

        // Character.csv
        //
        // motion_profile_key는 Monster.csv와 <b>같은 상수</b>를 쓴다 - 가리키는 에셋의 타입만 다르고
        // (CharacterMotionProfile / MonsterMotionProfile) 칸의 뜻은 "이름이 정확히 이것인 모션 프로필"로
        // 같다. 사람이 읽는 캐릭터 이름은 <c>$character_name</c>이라는 참조 컬럼으로 따로 둔다.
        public const string CharacterId = "character_id";

        /// <summary>캐릭터가 태어난 월드의 ID. 사람이 읽는 월드명은 참조용 열이라 읽지 않는다.</summary>
        public const string OriginWorldId = "origin_world_id";

        /// <summary>초상화 칸. 비어 있으면 런타임이 Base Idle 첫 프레임을 대신 쓴다(선택 항목).</summary>
        public const string PortraitKey = "portrait_key";

        /// <summary>기본 최대 체력. <b>빈 칸이 정상</b>이며 "아직 정하지 않았다"는 뜻이다 -
        /// 0이나 1로 채워 넣지 않는다.</summary>
        public const string BaseMaxHealth = "base_max_health";

        public const string MaxStamina = "max_stamina";
        public const string BaseCorruption = "base_corruption";

        /// <summary>
        /// <b>새 게임을 시작할 때</b> 이 캐릭터를 처음부터 가지고 시작하는가. <c>enabled</c>와 같은
        /// 엄격한 0/1 칸이며 빈 칸도 <c>true</c>도 받지 않는다.
        ///
        /// <b>지금 플레이어가 그 캐릭터를 보유했는지와는 다른 값이다.</b> 보유는 저장 문서
        /// (SaveData.characters)가 소유하며, 이 칸은 저장 문서를 <b>처음 만들 때</b> 무엇을 넣을지를
        /// 정하는 표의 정책일 뿐이다 - 이미 있는 저장 파일에 소급 적용되지 않는다.
        /// </summary>
        public const string InitiallyOwned = "initially_owned";

        public static readonly string[] Character =
        {
            CharacterId, NameCategory, NameKey, OriginWorldId, MotionProfileKey, PortraitKey, BaseCorruption,
            BaseMaxHealth, MaxStamina, InitiallyOwned, DisplayOrder, Enabled, Memo,
        };

        public const string ConfigId = "config_id";
        public const string MaxCorruption = "max_corruption";
        public const string WarningThresholdPercent = "warning_threshold_percent";
        public const string DangerThresholdPercent = "danger_threshold_percent";
        public const string WarningStaminaCostMultiplier = "warning_stamina_cost_multiplier";
        public const string DangerStaminaCostMultiplier = "danger_stamina_cost_multiplier";
        public static readonly string[] CorruptionConfig = { ConfigId, MaxCorruption, WarningThresholdPercent, DangerThresholdPercent, WarningStaminaCostMultiplier, DangerStaminaCostMultiplier, Enabled };
        public const string PurificationTypeId = "purification_type_id";
        public const string RequiredBuildingId = "required_building_id";
        public const string PurificationIntervalSeconds = "purification_interval_seconds";
        public const string PurificationValuePerInterval = "purification_value_per_interval";
        public const string BaseSlotCount = "base_slot_count";
        public static readonly string[] PurificationConfig = { PurificationTypeId, RequiredBuildingId, PurificationIntervalSeconds, PurificationValuePerInterval, BaseSlotCount, Enabled, Memo };

        // Skill.csv
        public const string SkillId = "skill_id";
        public const string DescriptionCategory = "description_category";
        public const string DescriptionKey = "description_key";
        public const string SkillType = "skill_type";
        public const string BehaviorKey = "behavior_key";

        public static readonly string[] Skill =
        {
            SkillId, NameCategory, NameKey, DescriptionCategory, DescriptionKey, IconKey,
            SkillType, BehaviorKey, DisplayOrder, Enabled, Memo,
        };

        // CharacterSkill.csv
        //
        // character_id / skill_id는 Character.csv / Skill.csv의 상수를 <b>그대로</b> 쓴다 - 두 표가 같은
        // 이름을 가리켜야 참조가 성립하므로 이름을 두 번 적어 두지 않는다.
        public const string RequiredCharacterLevel = "required_character_level";

        public static readonly string[] CharacterSkill =
        {
            CharacterId, SkillId, RequiredCharacterLevel, DisplayOrder, Enabled, Memo,
        };

        // Building.csv
        //
        // 사람이 읽는 이름/기능/시간/재화/아이템은 <c>$building_name</c> / <c>$function_description</c> /
        // <c>$build_time</c> / <c>$cost_currency</c> / <c>$cost_item</c>이라는 참조 컬럼으로 따로 두며
        // 여기 넣지 않는다 - 넣으면 필수 컬럼이 되고 임포터가 값을 읽는 칸처럼 보인다. 특히
        // <c>$build_time</c>은 <c>00:01:00</c> 같은 사람용 표기라, 초 단위 정수인 <c>build_time</c>과
        // <b>같은 값을 두 표기로</b> 적어 둔 것이다 - 임포터가 읽는 것은 언제나 초 쪽 하나뿐이다.
        //
        // 비용 재화 칸의 이름은 Monster.csv의 <c>currency_id</c>와 <b>일부러 다르다</b>. 같은 표에
        // "보상 재화"와 "비용 재화"가 함께 오게 되면 어느 쪽인지 이름만으로 갈리지 않으므로,
        // 처음부터 <c>cost_</c> 접두사로 뜻을 붙여 둔다.
        public const string BuildingId = "building_id";

        /// <summary>이 건물을 지으면 열리는 기능의 이름 참조. 이름과 <b>다른 카테고리를 가리켜도 된다</b>
        /// (여관은 07_Building, 그 기능인 용병 모집은 01_UI에 있다).</summary>
        public const string FunctionCategory = "function_category";

        public const string FunctionKey = "function_key";

        /// <summary>건설에 걸리는 시간(초). 사람이 읽는 <c>00:01:00</c> 표기는 <c>$build_time</c> 참조
        /// 컬럼의 몫이며 임포터는 그 칸을 읽지 않는다.</summary>
        public const string BuildTime = "build_time";

        /// <summary>건설 비용 재화 두 칸. <b>두 칸이 한 덩어리</b>다 - id가 비면 금액 칸도 비어야 하고,
        /// id가 있으면 금액 칸이 있어야 한다(Monster.csv의 보상 재화 세 칸과 같은 규칙이다).</summary>
        public const string CostCurrencyId = "cost_currency_id";

        public const string CostCurrencyAmount = "cost_currency_amount";

        /// <summary>건설 비용 아이템 두 칸. <b>두 칸이 한 덩어리</b>이며 <c>|</c>로 구분한 목록이다 -
        /// 둘 다 비어 있는 것은 정상(재화만 내는 건물)이고, 한쪽만 채워지거나 길이가 다르면 오류다.</summary>
        public const string CostItemIds = "cost_item_ids";

        public const string CostItemCounts = "cost_item_counts";

        public static readonly string[] Building =
        {
            BuildingId, NameCategory, NameKey, FunctionCategory, FunctionKey, BuildTime,
            CostCurrencyId, CostCurrencyAmount, CostItemIds, CostItemCounts,
            DisplayOrder, Enabled, Memo,
        };

        // CharacterAcquisition.csv
        //
        // character_id는 Character.csv의 상수를 <b>그대로</b> 쓴다 - 두 표가 같은 이름을 가리켜야
        // 참조가 성립하므로 이름을 두 번 적어 두지 않는다. 사람이 읽는 캐릭터 이름은
        // <c>$character_name</c>이라는 참조 컬럼으로 따로 두며 여기 넣지 않는다.
        public const string AcquisitionId = "acquisition_id";

        /// <summary>획득 방식 낱말. <c>RECRUIT_ONLY</c>처럼 <b>대문자 키</b> 형식이며, 런타임이 아는
        /// 낱말인지는 표가 아니라 런타임이 판정한다 - 표는 형식만 지키면 된다.</summary>
        public const string AcquisitionType = "acquisition_type";

        /// <summary>이미 보유해도 다시 모집될 수 있는가. <c>enabled</c>와 같은 엄격한 0/1 칸이다.</summary>
        public const string AllowDuplicateRecruitment = "allow_duplicate_recruitment";

        /// <summary>획득에 걸린 조건의 키. <b>빈 칸이 정상</b>이며 "조건 없음"이라는 뜻이다.</summary>
        public const string ConditionId = "condition_id";

        public static readonly string[] CharacterAcquisition =
        {
            AcquisitionId, CharacterId, AcquisitionType, AllowDuplicateRecruitment, ConditionId,
            Enabled, Memo,
        };

        // RecruitmentType.csv
        //
        // 이 표에는 display_order도 이름 참조도 없다 - 화면에 줄지어 보이는 목록이 아니라 다른 표가
        // 가리키는 <b>키의 목록</b>이기 때문이다. 없는 칸을 지어내지 않는다.
        public const string RecruitmentTypeId = "recruitment_type_id";

        public static readonly string[] RecruitmentType =
        {
            RecruitmentTypeId, Enabled, Memo,
        };

        // RecruitmentPool.csv
        //
        // recruitment_type_id / character_id는 각각 RecruitmentType.csv / Character.csv의 상수를
        // 그대로 쓴다. 사람이 읽는 캐릭터 이름은 <c>$character_name</c> 참조 컬럼의 몫이다.
        public const string PoolEntryId = "pool_entry_id";

        /// <summary>뽑기 가중치. <b>1 이상</b>만 뜻이 있다 - 0은 "뽑히지 않는 칸"이라 표에 적을 이유가
        /// 없고, 조용히 통과시키면 확률 합에 들어가지 않는 유령 칸이 생긴다.</summary>
        public const string Weight = "weight";

        public static readonly string[] RecruitmentPool =
        {
            RecruitmentTypeId, PoolEntryId, CharacterId, Weight, Enabled, Memo,
        };

        // RecruitmentAccess.csv
        //
        // 창구가 붙은 대상은 <b>종류 + id 두 칸</b>으로 가리킨다 - 건물 참조 하나로 두면 건물이 아닌
        // 곳에 창구를 붙일 수 없게 되고, 그때 표 구조를 통째로 바꿔야 한다. 사람이 읽는 대상 이름은
        // <c>$source_name</c>이라는 참조 컬럼으로 따로 둔다.
        public const string RecruitmentAccessId = "recruitment_access_id";

        /// <summary>창구가 붙어 있는 대상의 종류 낱말(<c>BUILDING</c> 등). 획득 방식과 같은
        /// <b>대문자 키</b> 형식이다.</summary>
        public const string SourceType = "source_type";

        /// <summary>창구가 붙어 있는 대상의 id. <c>source_type</c>이 <c>BUILDING</c>이면 Building.csv의
        /// <c>building_id</c>이며, 그 표에 실제로 있는 활성 행이어야 한다.</summary>
        public const string SourceId = "source_id";

        /// <summary>다음 후보가 도착하기까지의 간격(초). 0 이상이며 0은 "기다림 없음"이라는 뜻이다.</summary>
        public const string ArrivalIntervalSeconds = "arrival_interval_seconds";

        /// <summary>모집 한 번에 드는 비용 수량. 0 이상이며 0은 무료라는 뜻이다.</summary>
        public const string ConsumeAmount = "consume_amount";

        public static readonly string[] RecruitmentAccess =
        {
            RecruitmentAccessId, RecruitmentTypeId, SourceType, SourceId, ArrivalIntervalSeconds,
            ConsumeAmount, DisplayOrder, Enabled, Memo,
        };

        // PartyConfig.csv
        //
        // 이 표에도 display_order도 이름 참조도 없다 - 화면에 줄지어 보이는 목록이 아니라 코드가
        // 키로 하나를 집어 오는 <b>설정의 목록</b>이기 때문이다. 없는 칸을 지어내지 않는다.
        public const string PartyConfigId = "party_config_id";

        /// <summary>파티에 넣을 수 있는 기본 인원. <b>1 이상</b>만 뜻이 있다 - 0명짜리 파티는 설정으로서
        /// 뜻이 없고, 조용히 통과시키면 "설정은 있는데 아무도 넣을 수 없는" 상태가 생긴다.</summary>
        public const string BaseCapacity = "base_capacity";

        public static readonly string[] PartyConfig =
        {
            PartyConfigId, BaseCapacity, Enabled, Memo,
        };

        /// <summary>
        /// 작업자용 참조 컬럼의 머리글자. 헤더가 이 글자로 시작하면 <b>런타임 컬럼이 아니라 사람이
        /// 보라고 붙인 칸</b>이다(스프레드시트 함수로 채운 몬스터명/월드명 등). 임포터는 이 칸의 값을
        /// 읽지도, 검증하지도, 생성 에셋에 넣지도 않는다.
        /// </summary>
        public const string ReferenceOnlyPrefix = "$";

        /// <summary>컬럼이 아니라 파일 자체가 문제일 때 쓰는 컬럼 이름.</summary>
        public const string FilePseudoColumn = "(file)";

        /// <summary>헤더 줄 전체가 문제일 때 쓰는 컬럼 이름.</summary>
        public const string HeaderPseudoColumn = "(header)";
    }

    /// <summary>
    /// CSV 한 장을 읽어 <see cref="CsvTable"/>로 만든다. 파일 없음 / 인코딩 / 파싱 / 헤더 스키마까지가
    /// 여기 책임이고, 값의 의미는 보지 않는다.
    ///
    /// <b>여기서 실패하면 그 파일의 행 검증은 하지 않는다.</b> 헤더가 틀린 채로 행을 읽으면 엉뚱한
    /// 컬럼을 가리키는 오류가 수백 개 쏟아져 진짜 원인이 묻히기 때문이다. 대신 나머지 파일은
    /// 계속 읽는다 - Validate의 목적은 한 번에 모든 문제를 보여주는 것이다.
    ///
    /// <b><c>$</c>로 시작하는 헤더는 작업자용 참조 컬럼</b>이라 알려진 컬럼이 아니어도 통과시킨다
    /// (<see cref="IsReferenceOnlyColumn"/>). 다만 <b>헤더에서 지우지는 않는다</b> - 행의 칸 수는
    /// 참조 컬럼까지 포함한 전체 헤더 수와 맞아야 하고, 헤더에서 빼 버리면 값이 한 칸씩 밀린 행을
    /// 정상으로 읽게 된다. 값을 실제로 무시하는 것은 이후 단계가 컬럼을 <b>이름으로만</b> 읽기
    /// 때문이다 - 참조 컬럼 이름을 묻는 곳이 없으므로 스냅샷에도 생성 에셋에도 남지 않는다.
    /// </summary>
    public static class TableDataCsvReader
    {
        /// <summary>읽기에 실패하면 null을 돌려주고 원인을 <paramref name="log"/>에 남긴다.</summary>
        public static CsvTable Read(string assetPath, string fileName, string[] expectedColumns, TableDataDiagnosticLog log)
        {
            string fullPath = ToFullPath(assetPath);

            if (!File.Exists(fullPath))
            {
                log.Error(fileName, TableDataDiagnostic.FileLevelRow, TableDataColumns.FilePseudoColumn, assetPath,
                    "CSV 파일이 없습니다 - 이 경로에 UTF-8 CSV를 두세요.");
                return null;
            }

            if (!CsvParser.TryReadUtf8(fullPath, out string text, out string readError))
            {
                log.Error(fileName, TableDataDiagnostic.FileLevelRow, TableDataColumns.FilePseudoColumn, assetPath, readError);
                return null;
            }

            if (!CsvParser.TryParse(text, out List<CsvRecord> records, out string parseError, out int errorLine))
            {
                log.Error(fileName, errorLine, TableDataColumns.FilePseudoColumn, assetPath, "CSV 형식 오류: " + parseError);
                return null;
            }

            if (records.Count == 0)
            {
                log.Error(fileName, TableDataDiagnostic.FileLevelRow, TableDataColumns.FilePseudoColumn, assetPath,
                    "파일이 비어 있습니다 - 최소한 헤더 한 줄이 있어야 합니다.");
                return null;
            }

            CsvRecord headerRecord = records[0];
            string[] header = NormalizeHeader(headerRecord.Fields);
            records.RemoveAt(0);

            if (!ValidateHeader(fileName, header, expectedColumns, log)) return null;

            var table = new CsvTable(fileName, header, records);

            // 헤더는 맞는데 칸 수가 다른 줄은 값이 밀려 들어간 것이므로 행 단위로 보고한다.
            for (int i = table.Records.Count - 1; i >= 0; i--)
            {
                CsvRecord record = table.Records[i];
                if (record.Fields.Length == header.Length) continue;

                log.Error(fileName, record.Line, TableDataColumns.HeaderPseudoColumn,
                    record.Fields.Length.ToString(CultureInfo.InvariantCulture),
                    $"칸 수가 헤더와 다릅니다(헤더 {header.Length}칸, 이 행 {record.Fields.Length}칸) - 이 행은 건너뜁니다.");
                table.Records.RemoveAt(i);
            }

            return table;
        }

        /// <summary>헤더 칸의 앞뒤 공백만 다듬는다. 값과 달리 헤더 공백은 의미가 없고, 여기서 다듬지
        /// 않으면 편집기가 넣은 공백 하나로 파일 전체가 실패한다.</summary>
        private static string[] NormalizeHeader(string[] fields)
        {
            var header = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++) header[i] = (fields[i] ?? string.Empty).Trim();
            return header;
        }

        private static bool ValidateHeader(string fileName, string[] header, string[] expected, TableDataDiagnosticLog log)
        {
            bool ok = true;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);

            for (int i = 0; i < header.Length; i++)
            {
                string column = header[i];

                if (column.Length == 0)
                {
                    log.Error(fileName, TableDataDiagnostic.HeaderRow, TableDataColumns.HeaderPseudoColumn,
                        string.Join(",", header), $"{i + 1}번째 헤더 칸이 비어 있습니다.");
                    ok = false;
                    continue;
                }

                if (!seen.Add(column))
                {
                    log.Error(fileName, TableDataDiagnostic.HeaderRow, column, column,
                        "같은 컬럼 이름이 두 번 있습니다 - 어느 칸을 읽어야 할지 정할 수 없습니다.");
                    ok = false;
                    continue;
                }

                // 참조 컬럼은 이름이 정해져 있지 않으므로 알려진 컬럼 목록과 대조하지 않는다. 중복 검사는
                // 위에서 이미 지났다 - 같은 이름이 두 번 있으면 참조 컬럼이라도 어느 칸인지 정할 수 없다.
                if (IsReferenceOnlyColumn(column)) continue;

                if (HasReferencePrefix(column))
                {
                    log.Error(fileName, TableDataDiagnostic.HeaderRow, column, column,
                        $"'{TableDataColumns.ReferenceOnlyPrefix}' 뒤에 이름이 없습니다 - 참조 컬럼은 " +
                        $"'{TableDataColumns.ReferenceOnlyPrefix}monster_name'처럼 이름을 붙여야 합니다.");
                    ok = false;
                    continue;
                }

                if (!expectedSet.Contains(column))
                {
                    log.Error(fileName, TableDataDiagnostic.HeaderRow, column, column,
                        "알 수 없는 컬럼입니다 - 오타를 조용히 무시하지 않기 위해 오류로 처리합니다. " +
                        "허용 컬럼: " + string.Join(", ", expected) + ". " +
                        $"작업자용 참조 컬럼이라면 이름 앞에 '{TableDataColumns.ReferenceOnlyPrefix}'를 붙이세요.");
                    ok = false;
                }
            }

            foreach (string column in expected)
            {
                if (seen.Contains(column)) continue;

                log.Error(fileName, TableDataDiagnostic.HeaderRow, column, string.Join(",", header),
                    "필수 컬럼이 없습니다.");
                ok = false;
            }

            return ok;
        }

        /// <summary>
        /// 이 헤더가 <b>작업자용 참조 컬럼</b>인지. 조건은 두 가지다 - <c>$</c>로 시작할 것, 그리고
        /// <c>$</c> 뒤에 실제 이름이 있을 것. 이름 없는 <c>$</c> 하나짜리 헤더를 참조 컬럼으로 봐 주면
        /// 헤더 오타 하나가 검사 없이 통과하므로 <b>일부러 걸러 낸다</b>.
        ///
        /// 참조 컬럼은 CSV의 <b>어느 위치에 있어도</b> 된다. 위치가 아니라 이름으로 판정하고, 이후
        /// 단계도 컬럼을 이름으로만 읽기 때문이다.
        /// </summary>
        public static bool IsReferenceOnlyColumn(string column)
        {
            if (!HasReferencePrefix(column)) return false;

            return column.Substring(TableDataColumns.ReferenceOnlyPrefix.Length).Trim().Length > 0;
        }

        /// <summary>이름이 있든 없든 <c>$</c>로 시작하는지. "참조 컬럼처럼 쓰려다 만 헤더"를 알 수 없는
        /// 컬럼이 아니라 그에 맞는 오류로 안내하기 위해 나눠 둔다.</summary>
        private static bool HasReferencePrefix(string column)
        {
            return !string.IsNullOrEmpty(column)
                   && column.StartsWith(TableDataColumns.ReferenceOnlyPrefix, StringComparison.Ordinal);
        }

        /// <summary>"Assets/..." 프로젝트 상대 경로를 실제 파일 경로로 바꾼다.</summary>
        public static string ToFullPath(string assetPath)
        {
            // Application.dataPath는 ".../Assets"이므로 프로젝트 루트를 기준으로 합친다.
            string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName;
            return string.IsNullOrEmpty(projectRoot)
                ? assetPath
                : Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
