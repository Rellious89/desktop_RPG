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
        public const string ItemCsvFileName = "Item.csv";
        public const string MonsterCsvFileName = "Monster.csv";
        public const string DungeonCsvFileName = "Dungeon.csv";

        public const string WorldCsvPath = InputRoot + "/" + WorldCsvFileName;
        public const string ItemCsvPath = InputRoot + "/" + ItemCsvFileName;
        public const string MonsterCsvPath = InputRoot + "/" + MonsterCsvFileName;
        public const string DungeonCsvPath = InputRoot + "/" + DungeonCsvFileName;

        /// <summary>
        /// <c>icon_key</c>가 가리키는 아이콘을 찾는 <b>유일한</b> 폴더. 프로젝트 전체에서 이름으로 찾지
        /// 않는 이유는 UI 팩의 이미지들이 서로 다른 폴더에 같은 파일 이름으로 들어 있어서, 전역 탐색은
        /// "이름이 겹쳐 정할 수 없다"만 돌려주기 때문이다 - 아이템 아이콘은 이 폴더에 두는 것을 규칙으로
        /// 삼고, 여기 없는 이름은 없는 것으로 본다.
        /// </summary>
        public const string ItemIconRoot = "Assets/Art/Item";

        // ---- 출력 ----

        public const string GeneratedRoot = "Assets/Generated";
        public const string OutputRoot = GeneratedRoot + "/TableData";

        public const string WorldOutputFolder = OutputRoot + "/World";
        public const string ItemOutputFolder = OutputRoot + "/Item";
        public const string MonsterOutputFolder = OutputRoot + "/Monster";
        public const string DungeonOutputFolder = OutputRoot + "/Dungeon";

        /// <summary>생성 에셋 파일 이름의 고정 접두사. 원본 ID를 그대로 뒤에 붙인다 - <b>ID 자체는
        /// 절대 바꾸지 않는다</b>. ID가 <see cref="TableDataFieldRules.IdPatternText"/>(양의 정수 또는
        /// snake_case)만 허용되므로 접두사와 합쳐도 파일 이름은 항상 안전하고, 서로 다른 ID가 같은
        /// 파일 이름이 되는 일도 없다.</summary>
        public const string WorldAssetPrefix = "World_";

        public const string ItemAssetPrefix = "Item_";
        public const string MonsterAssetPrefix = "Monster_";
        public const string DungeonAssetPrefix = "Dungeon_";

        public const string WorldCatalogAssetName = "WorldCatalog";
        public const string ItemCatalogAssetName = "ItemCatalog";
        public const string MonsterCatalogAssetName = "MonsterCatalog";
        public const string DungeonCatalogAssetName = "DungeonCatalog";

        public static string WorldAssetPath(string worldId)
        {
            return WorldOutputFolder + "/" + WorldAssetPrefix + worldId + ".asset";
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

        public static string WorldCatalogAssetPath => WorldOutputFolder + "/" + WorldCatalogAssetName + ".asset";

        public static string ItemCatalogAssetPath => ItemOutputFolder + "/" + ItemCatalogAssetName + ".asset";

        public static string MonsterCatalogAssetPath => MonsterOutputFolder + "/" + MonsterCatalogAssetName + ".asset";

        public static string DungeonCatalogAssetPath => DungeonOutputFolder + "/" + DungeonCatalogAssetName + ".asset";
    }
}
