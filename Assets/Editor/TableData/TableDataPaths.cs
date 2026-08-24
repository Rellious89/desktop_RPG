namespace TableDataEditor
{
    /// <summary>
    /// CSV 입력과 생성 에셋 출력의 <b>고정 경로</b>. 경로를 다른 파일에 적어 두지 않고 여기 한 곳에만
    /// 둔다 - 임포터가 어디를 읽고 어디를 쓰는지가 흩어지면 "수동 에셋을 건드리지 않는다"는 경계가
    /// 코드로 확인되지 않기 때문이다.
    ///
    /// <b>출력 루트 밖은 임포터의 관심사가 아니다.</b> <see cref="OutputRoot"/> 아래만 생성/갱신하며,
    /// Assets/Data 이하의 수동 에셋(ItemDefinition, MonsterMotionProfile, 기존 DungeonDefinition)은
    /// 읽기만 한다.
    /// </summary>
    public static class TableDataPaths
    {
        // ---- 입력 ----

        public const string InputRoot = "Assets/TableData/Game";

        public const string WorldCsvFileName = "World.csv";
        public const string CurrencyCsvFileName = "Currency.csv";
        public const string ItemCsvFileName = "Item.csv";
        public const string MonsterCsvFileName = "Monster.csv";
        public const string DungeonCsvFileName = "Dungeon.csv";
        public const string CharacterCsvFileName = "Character.csv";
        public const string SkillCsvFileName = "Skill.csv";
        public const string CharacterSkillCsvFileName = "CharacterSkill.csv";
        public const string BuildingCsvFileName = "Building.csv";
        public const string CharacterAcquisitionCsvFileName = "CharacterAcquisition.csv";
        public const string RecruitmentTypeCsvFileName = "RecruitmentType.csv";
        public const string RecruitmentPoolCsvFileName = "RecruitmentPool.csv";
        public const string RecruitmentAccessCsvFileName = "RecruitmentAccess.csv";
        public const string PartyConfigCsvFileName = "PartyConfig.csv";
        public const string CorruptionConfigCsvFileName = "CorruptionConfig.csv";
        public const string PurificationConfigCsvFileName = "PurificationConfig.csv";

        public const string WorldCsvPath = InputRoot + "/" + WorldCsvFileName;
        public const string CurrencyCsvPath = InputRoot + "/" + CurrencyCsvFileName;
        public const string ItemCsvPath = InputRoot + "/" + ItemCsvFileName;
        public const string MonsterCsvPath = InputRoot + "/" + MonsterCsvFileName;
        public const string DungeonCsvPath = InputRoot + "/" + DungeonCsvFileName;
        public const string CharacterCsvPath = InputRoot + "/" + CharacterCsvFileName;
        public const string SkillCsvPath = InputRoot + "/" + SkillCsvFileName;
        public const string CharacterSkillCsvPath = InputRoot + "/" + CharacterSkillCsvFileName;
        public const string BuildingCsvPath = InputRoot + "/" + BuildingCsvFileName;

        public const string CharacterAcquisitionCsvPath = InputRoot + "/" + CharacterAcquisitionCsvFileName;
        public const string RecruitmentTypeCsvPath = InputRoot + "/" + RecruitmentTypeCsvFileName;
        public const string RecruitmentPoolCsvPath = InputRoot + "/" + RecruitmentPoolCsvFileName;
        public const string RecruitmentAccessCsvPath = InputRoot + "/" + RecruitmentAccessCsvFileName;

        public const string PartyConfigCsvPath = InputRoot + "/" + PartyConfigCsvFileName;
        public const string CorruptionConfigCsvPath = InputRoot + "/" + CorruptionConfigCsvFileName;
        public const string PurificationConfigCsvPath = InputRoot + "/" + PurificationConfigCsvFileName;

        /// <summary>
        /// <c>icon_key</c>가 가리키는 아이콘을 찾는 <b>유일한</b> 폴더. 프로젝트 전체에서 이름으로 찾지
        /// 않는 이유는 UI 팩의 이미지들이 서로 다른 폴더에 같은 파일 이름으로 들어 있어서, 전역 탐색은
        /// "이름이 겹쳐 정할 수 없다"만 돌려주기 때문이다 - 아이템 아이콘은 이 폴더에 두는 것을 규칙으로
        /// 삼고, 여기 없는 이름은 없는 것으로 본다.
        ///
        /// <b>값이 실제 아이콘이 놓인 자리를 가리켜야 한다.</b> 아이템 아이콘은 UI 아트와 함께
        /// <c>Assets/Art/UI/Item</c>에 들어 있고, 임포터는 폴더를 만들지도 아이콘을 옮기지도 않는다 -
        /// 자산의 자리는 사람이 정하고 이 상수는 그 자리를 따라간다.
        /// </summary>
        public const string ItemIconRoot = "Assets/Art/UI/Item";

        /// <summary>
        /// 재화 아이콘을 찾는 <b>유일한</b> 폴더. <see cref="ItemIconRoot"/>와 같은 이유로 전역 탐색을
        /// 쓰지 않는다 - 이름이 겹치는 UI 이미지가 많아 전역 탐색은 "정할 수 없다"만 돌려준다.
        ///
        /// <b>폴더가 아직 없어도 정상이다.</b> 재화 아이콘을 한 장도 넣지 않은 프로젝트에서는 이 폴더가
        /// 만들어질 이유가 없고, icon_key가 비어 있는 행은 그 자체로 통과하는 값이다(경고만 남는다).
        /// 임포터는 이 폴더를 <b>만들지 않는다</b> - 입력 자산의 자리는 사람이 정한다.
        /// </summary>
        public const string CurrencyIconRoot = "Assets/Art/Currency";

        // ---- 출력 ----

        public const string GeneratedRoot = "Assets/Generated";
        public const string OutputRoot = GeneratedRoot + "/TableData";

        public const string WorldOutputFolder = OutputRoot + "/World";
        public const string CurrencyOutputFolder = OutputRoot + "/Currency";
        public const string ItemOutputFolder = OutputRoot + "/Item";
        public const string MonsterOutputFolder = OutputRoot + "/Monster";
        public const string DungeonOutputFolder = OutputRoot + "/Dungeon";

        /// <summary>
        /// 캐릭터/스킬/관계 세 표의 출력 폴더. <b>기존 다섯 도메인과 나란한 형제 폴더</b>이며 서로의
        /// 안쪽을 절대 건드리지 않는다 - 어떤 정리 동작도 자기 폴더 밖으로 나가지 않는다는 것이 코드로
        /// 보이는 자리가 여기다.
        ///
        /// <b>수동 CharacterDefinition은 여전히 Assets/Data 이하에 있고, 임포터의 출력 대상이 아니다.</b>
        /// 같은 character_id를 가진 생성 에셋과 수동 에셋이 당분간 함께 존재하는 것은 정상이며 충돌
        /// 오류가 아니다 - 로스터가 무엇을 쓰는지는 이번 단계에서 바꾸지 않는다.
        /// </summary>
        public const string CharacterOutputFolder = OutputRoot + "/Character";

        public const string SkillOutputFolder = OutputRoot + "/Skill";
        public const string CharacterSkillOutputFolder = OutputRoot + "/CharacterSkill";

        /// <summary>
        /// 건물 표의 출력 폴더. <b>기존 여덟 도메인과 나란한 형제 폴더</b>이며 서로의 안쪽을 절대
        /// 건드리지 않는다 - Building만 다시 만드는 좁은 범위가 다른 도메인의 생성 파일을 한 바이트도
        /// 바꾸지 않는다는 것이 코드로 보이는 자리가 여기다.
        /// </summary>
        public const string BuildingOutputFolder = OutputRoot + "/Building";

        /// <summary>
        /// 모집 네 표의 출력 폴더. <b>기존 아홉 도메인과 나란한 형제 폴더</b>들이며 서로의 안쪽을
        /// 절대 건드리지 않는다 - 모집만 다시 만드는 좁은 범위가 다른 도메인의 생성 파일을 한
        /// 바이트도 바꾸지 않는다는 것이 코드로 보이는 자리가 여기다.
        ///
        /// 네 표를 <b>한 폴더에 몰아넣지 않은</b> 것도 같은 이유다 - 도메인 하나에 폴더 하나라는
        /// 기존 규칙을 그대로 따르면, orphan 검사와 충돌 검사가 표마다 자기 폴더만 보면 된다.
        /// </summary>
        public const string CharacterAcquisitionOutputFolder = OutputRoot + "/CharacterAcquisition";

        public const string RecruitmentTypeOutputFolder = OutputRoot + "/RecruitmentType";
        public const string RecruitmentPoolOutputFolder = OutputRoot + "/RecruitmentPool";
        public const string RecruitmentAccessOutputFolder = OutputRoot + "/RecruitmentAccess";

        /// <summary>
        /// 파티 설정 표의 출력 폴더. <b>기존 열세 도메인과 나란한 형제 폴더</b>이며 서로의 안쪽을
        /// 절대 건드리지 않는다 - 파티 표만 다시 만드는 좁은 범위가 다른 도메인의 생성 파일을 한
        /// 바이트도 바꾸지 않는다는 것이 코드로 보이는 자리가 여기다.
        /// </summary>
        public const string PartyConfigOutputFolder = OutputRoot + "/PartyConfig";
        public const string CorruptionConfigOutputFolder = OutputRoot + "/CorruptionConfig";
        public const string PurificationConfigOutputFolder = OutputRoot + "/PurificationConfig";

        /// <summary>생성 에셋 파일 이름의 고정 접두사. 원본 ID를 그대로 뒤에 붙인다 - <b>ID 자체는
        /// 절대 바꾸지 않는다</b>. ID가 <see cref="TableDataFieldRules.IdPatternText"/>(양의 정수 또는
        /// snake_case)만 허용되므로 접두사와 합쳐도 파일 이름은 항상 안전하고, 서로 다른 ID가 같은
        /// 파일 이름이 되는 일도 없다.</summary>
        public const string WorldAssetPrefix = "World_";

        public const string CurrencyAssetPrefix = "Currency_";
        public const string ItemAssetPrefix = "Item_";
        public const string MonsterAssetPrefix = "Monster_";
        public const string DungeonAssetPrefix = "Dungeon_";
        public const string CharacterAssetPrefix = "Character_";
        public const string SkillAssetPrefix = "Skill_";
        public const string CharacterSkillAssetPrefix = "CharacterSkill_";
        public const string BuildingAssetPrefix = "Building_";
        public const string CharacterAcquisitionAssetPrefix = "CharacterAcquisition_";
        public const string RecruitmentTypeAssetPrefix = "RecruitmentType_";
        public const string RecruitmentPoolAssetPrefix = "RecruitmentPool_";
        public const string RecruitmentAccessAssetPrefix = "RecruitmentAccess_";
        public const string PartyConfigAssetPrefix = "PartyConfig_";
        public const string CorruptionConfigAssetPrefix = "CorruptionConfig_";
        public const string PurificationConfigAssetPrefix = "PurificationConfig_";

        public const string WorldCatalogAssetName = "WorldCatalog";
        public const string CurrencyCatalogAssetName = "CurrencyCatalog";
        public const string ItemCatalogAssetName = "ItemCatalog";
        public const string MonsterCatalogAssetName = "MonsterCatalog";
        public const string DungeonCatalogAssetName = "DungeonCatalog";
        public const string CharacterCatalogAssetName = "CharacterCatalog";
        public const string SkillCatalogAssetName = "SkillCatalog";
        public const string CharacterSkillCatalogAssetName = "CharacterSkillCatalog";
        public const string BuildingCatalogAssetName = "BuildingCatalog";
        public const string CharacterAcquisitionCatalogAssetName = "CharacterAcquisitionCatalog";
        public const string RecruitmentTypeCatalogAssetName = "RecruitmentTypeCatalog";
        public const string RecruitmentPoolCatalogAssetName = "RecruitmentPoolCatalog";
        public const string RecruitmentAccessCatalogAssetName = "RecruitmentAccessCatalog";
        public const string PartyConfigCatalogAssetName = "PartyConfigCatalog";
        public const string CorruptionConfigCatalogAssetName = "CorruptionConfigCatalog";
        public const string PurificationConfigCatalogAssetName = "PurificationConfigCatalog";

        public static string WorldAssetPath(string worldId)
        {
            return WorldOutputFolder + "/" + WorldAssetPrefix + worldId + ".asset";
        }

        /// <summary>재화 생성 에셋의 경로. <b>CSV에 적힌 currency_id를 한 글자도 바꾸지 않고</b> 파일
        /// 이름에 붙인다 - 소문자화도 공백 제거도 하지 않는다(형식 검사를 통과한 값만 여기까지 온다).</summary>
        public static string CurrencyAssetPath(string currencyId)
        {
            return CurrencyOutputFolder + "/" + CurrencyAssetPrefix + currencyId + ".asset";
        }

        public static string ItemAssetPath(string itemId)
        {
            return ItemOutputFolder + "/" + ItemAssetPrefix + itemId + ".asset";
        }

        public static string MonsterAssetPath(string monsterId)
        {
            return MonsterOutputFolder + "/" + MonsterAssetPrefix + monsterId + ".asset";
        }

        public static string DungeonAssetPath(string dungeonId)
        {
            return DungeonOutputFolder + "/" + DungeonAssetPrefix + dungeonId + ".asset";
        }

        /// <summary>캐릭터 생성 에셋의 경로. <b>CSV에 적힌 character_id를 한 글자도 바꾸지 않고</b>
        /// 파일 이름에 붙인다 - 캐릭터 id는 표준 ID(양의 정수 / snake_case)이거나 기존 6종의 legacy
        /// PascalCase라, 둘 다 파일 이름에 그대로 쓸 수 있는 글자만으로 이루어져 있다.</summary>
        public static string CharacterAssetPath(string characterId)
        {
            return CharacterOutputFolder + "/" + CharacterAssetPrefix + characterId + ".asset";
        }

        public static string SkillAssetPath(string skillId)
        {
            return SkillOutputFolder + "/" + SkillAssetPrefix + skillId + ".asset";
        }

        /// <summary>관계 생성 에셋의 경로. 파일 이름은 <c>CharacterSkill_&lt;character_id&gt;__&lt;skill_id&gt;</c>
        /// 이며, 구분자가 밑줄 두 개인 이유는
        /// <see cref="Skill.CharacterSkillDefinition.PairIdSeparator"/>에 적어 두었다 - 서로 다른 짝이
        /// 같은 파일 이름이 되는 경우가 없다.</summary>
        public static string CharacterSkillAssetPath(string pairId)
        {
            return CharacterSkillOutputFolder + "/" + CharacterSkillAssetPrefix + pairId + ".asset";
        }

        /// <summary>건물 생성 에셋의 경로. <b>CSV에 적힌 building_id를 한 글자도 바꾸지 않고</b> 파일
        /// 이름에 붙인다 - 형식 검사를 통과한 값만 여기까지 온다.</summary>
        public static string BuildingAssetPath(string buildingId)
        {
            return BuildingOutputFolder + "/" + BuildingAssetPrefix + buildingId + ".asset";
        }

        public static string WorldCatalogAssetPath => WorldOutputFolder + "/" + WorldCatalogAssetName + ".asset";

        public static string CurrencyCatalogAssetPath => CurrencyOutputFolder + "/" + CurrencyCatalogAssetName + ".asset";

        public static string ItemCatalogAssetPath => ItemOutputFolder + "/" + ItemCatalogAssetName + ".asset";

        public static string MonsterCatalogAssetPath => MonsterOutputFolder + "/" + MonsterCatalogAssetName + ".asset";

        public static string DungeonCatalogAssetPath => DungeonOutputFolder + "/" + DungeonCatalogAssetName + ".asset";

        public static string CharacterCatalogAssetPath =>
            CharacterOutputFolder + "/" + CharacterCatalogAssetName + ".asset";

        public static string SkillCatalogAssetPath => SkillOutputFolder + "/" + SkillCatalogAssetName + ".asset";

        public static string CharacterSkillCatalogAssetPath =>
            CharacterSkillOutputFolder + "/" + CharacterSkillCatalogAssetName + ".asset";

        public static string BuildingCatalogAssetPath =>
            BuildingOutputFolder + "/" + BuildingCatalogAssetName + ".asset";

        /// <summary>획득 방식 생성 에셋의 경로. <b>CSV에 적힌 acquisition_id를 한 글자도 바꾸지 않고</b>
        /// 파일 이름에 붙인다.</summary>
        public static string CharacterAcquisitionAssetPath(string acquisitionId)
        {
            return CharacterAcquisitionOutputFolder + "/" + CharacterAcquisitionAssetPrefix + acquisitionId + ".asset";
        }

        /// <summary>모집 종류 생성 에셋의 경로. recruitment_type_id는 대소문자와 밑줄만 쓰는 값이라
        /// (<see cref="TableDataFieldRules.RecruitmentIdPatternText"/>) 파일 이름에 그대로 쓸 수 있다.</summary>
        public static string RecruitmentTypeAssetPath(string recruitmentTypeId)
        {
            return RecruitmentTypeOutputFolder + "/" + RecruitmentTypeAssetPrefix + recruitmentTypeId + ".asset";
        }

        /// <summary>후보 칸 생성 에셋의 경로. 파일 이름은
        /// <c>RecruitmentPool_&lt;recruitment_type_id&gt;__&lt;pool_entry_id&gt;</c>이며, 구분자가 밑줄
        /// 두 개인 이유는 <see cref="Recruitment.RecruitmentPoolEntryDefinition.PairIdSeparator"/>에
        /// 적어 두었다.</summary>
        public static string RecruitmentPoolAssetPath(string pairId)
        {
            return RecruitmentPoolOutputFolder + "/" + RecruitmentPoolAssetPrefix + pairId + ".asset";
        }

        /// <summary>모집 창구 생성 에셋의 경로.</summary>
        public static string RecruitmentAccessAssetPath(string recruitmentAccessId)
        {
            return RecruitmentAccessOutputFolder + "/" + RecruitmentAccessAssetPrefix + recruitmentAccessId + ".asset";
        }

        public static string CharacterAcquisitionCatalogAssetPath =>
            CharacterAcquisitionOutputFolder + "/" + CharacterAcquisitionCatalogAssetName + ".asset";

        public static string RecruitmentTypeCatalogAssetPath =>
            RecruitmentTypeOutputFolder + "/" + RecruitmentTypeCatalogAssetName + ".asset";

        public static string RecruitmentPoolCatalogAssetPath =>
            RecruitmentPoolOutputFolder + "/" + RecruitmentPoolCatalogAssetName + ".asset";

        public static string RecruitmentAccessCatalogAssetPath =>
            RecruitmentAccessOutputFolder + "/" + RecruitmentAccessCatalogAssetName + ".asset";

        /// <summary>파티 설정 생성 에셋의 경로. <b>CSV에 적힌 party_config_id를 한 글자도 바꾸지 않고</b>
        /// 파일 이름에 붙인다 - 표준 ID(<see cref="TableDataFieldRules.IdPatternText"/>)만 여기까지
        /// 오므로 파일 이름에 그대로 쓸 수 있다.</summary>
        public static string PartyConfigAssetPath(string partyConfigId)
        {
            return PartyConfigOutputFolder + "/" + PartyConfigAssetPrefix + partyConfigId + ".asset";
        }

        public static string CorruptionConfigAssetPath(string configId) =>
            CorruptionConfigOutputFolder + "/" + CorruptionConfigAssetPrefix + configId + ".asset";
        public static string PurificationConfigAssetPath(string purificationTypeId) =>
            PurificationConfigOutputFolder + "/" + PurificationConfigAssetPrefix + purificationTypeId + ".asset";

        public static string PartyConfigCatalogAssetPath =>
            PartyConfigOutputFolder + "/" + PartyConfigCatalogAssetName + ".asset";

        public static string CorruptionConfigCatalogAssetPath =>
            CorruptionConfigOutputFolder + "/" + CorruptionConfigCatalogAssetName + ".asset";
        public static string PurificationConfigCatalogAssetPath =>
            PurificationConfigOutputFolder + "/" + PurificationConfigCatalogAssetName + ".asset";
    }
}
