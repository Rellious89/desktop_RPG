using System.Collections.Generic;
using Common;
using Inventory;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// 던전 한 곳의 정의 - "이 던전이 무엇인가"만 담는다(식별자, 표시 이름, 소속 월드, 대표 이미지,
    /// 등장 몬스터, 대표 보상, 목록 순서). CharacterDefinition / ItemDefinition과
    /// 같은 역할 분담이며, <b>진행 상태나 전투 규칙은 여기에 없다</b> - 입장 이후에 무슨 일이 일어나는지는
    /// 이 에셋도 입장 UI도 알지 못한다.
    ///
    /// <b>식별자는 반드시 직접 적는다.</b> 다른 정의 에셋들과 달리 파일 이름으로 대체하지 않는다 -
    /// 던전 id는 나중에 진행 저장과 필드 모드 전환의 키가 되므로, 에셋 파일 이름을 바꾸는 것만으로
    /// 식별자가 함께 바뀌는 경로를 애초에 만들지 않는다. 비어 있는 던전은
    /// <see cref="IsValid"/>가 false이고 <see cref="DungeonCatalog"/>가 목록에서 제외한다.
    ///
    /// <b>이름 문구는 로컬라이징 참조만 들고 있다.</b> 한국어/영어 문자열을 이 에셋이나 코드에
    /// 적어두지 않으며, 현재 Locale 적용은 Unity Localization이 담당한다 - 표시하는 쪽이
    /// <see cref="LocalizedTextReference"/>를 구독해서 그린다.
    ///
    /// <b>월드와 몬스터는 각각의 정의 에셋을 가리킨다.</b> 월드 이름 문구는 이 에셋이 아니라
    /// <see cref="WorldDefinition.LocalizedName"/>이 소유하고, 등장 몬스터는
    /// <see cref="MonsterDefinition"/> 목록이다 - 같은 몬스터가 여러 던전에 나올 때 이미지와 이름을
    /// 던전마다 다시 적지 않게 하기 위함이다. 미리보기 이미지도 몬스터 정의가 결정한다
    /// (<see cref="MonsterDefinition.PreviewSprite"/>).
    ///
    /// <b>보상은 ItemDefinition만.</b> 대표 아이템 정의 목록이며 <b>수량과 확률은 담지 않는다</b> -
    /// 이 화면은 "무엇이 나오는지"만 보여주는 단계라, 보유 수량(InventoryManager)도 조회하지 않는다.
    /// </summary>
    [CreateAssetMenu(fileName = "DungeonDefinition", menuName = "Dungeon/Dungeon Definition")]
    public class DungeonDefinition : ScriptableObject
    {
        private static readonly MonsterDefinition[] EmptyMonsters = new MonsterDefinition[0];
        private static readonly ItemDefinition[] EmptyRewards = new ItemDefinition[0];

        [Header("Identity")]
        [Tooltip("이 던전의 고유 식별자(forest_01 등). <b>반드시 직접 적는다</b> - 비워두면 이 던전은 " +
                 "목록에 나오지 않는다(파일 이름으로 대체하지 않는다). 한 번 정하면 바꾸지 않는다.")]
        [SerializeField] private string dungeonId;

        [Header("Localization")]
        [Tooltip("던전 목록에 표시할 이름. 카테고리 번호 + 숫자 키로 지정한다.")]
        [SerializeField] private LocalizedTextReference dungeonName = new LocalizedTextReference();

        [Header("Placement")]
        [Tooltip("이 던전이 속한 월드. 상세 상단 문구의 인자로 쓰이는 월드 이름은 이 에셋이 아니라 " +
                 "여기에 연결된 World Definition이 들고 있다 - 비어 있으면 월드 문구는 비워진다.")]
        [SerializeField] private WorldDefinition world;

        [Header("Presentation")]
        [Tooltip("목록 항목에 표시할 대표 이미지(선택). 항목 프리팹에 대표 Image가 연결되어 있을 때만 " +
                 "쓰이고, 비어 있으면 그 Image는 꺼진다 - 지금 item_dungeonList에는 대표 Image가 없다.")]
        [SerializeField] private Sprite representativeSprite;

        [Header("Preview")]
        [Tooltip("등장 몬스터. 순서대로 item_monster 칸이 만들어지며, 각 칸의 이미지는 몬스터 정의의 " +
                 "Preview Sprite(없으면 Motion Profile의 Base Idle 첫 프레임)를 쓴다.")]
        [SerializeField] private List<MonsterDefinition> monsters = new List<MonsterDefinition>();

        [Tooltip("대표 보상 아이템. 순서대로 item_item 칸이 만들어지며 아이콘만 표시한다 - " +
                 "수량/확률/보유량은 표시하지 않는다.")]
        [SerializeField] private List<ItemDefinition> rewardItems = new List<ItemDefinition>();

        [Header("Condition")]
        [Tooltip("표에 적힌 필요 캐릭터 레벨. 이 에셋은 값을 담기만 하고 스스로 무엇을 여닫지 않는다.")]
        [Min(1)]
        [SerializeField] private int requiredCharacterLevel = 1;

        [Header("Corruption")]
        [Min(1)] [SerializeField] private int corruptionIntervalSeconds = 1;
        [Min(1)] [SerializeField] private int corruptionGainPerInterval = 1;

        [Header("Ordering")]
        [Tooltip("던전을 정렬할 때 쓰는 순서 값. 작을수록 앞이다 - 이 값 자체가 목록을 만들지는 않으며, " +
                 "목록의 순서는 DungeonCatalog의 작성 순서가 결정한다.")]
        [SerializeField] private int displayOrder;

        /// <summary>이 던전의 고유 식별자. 비어 있으면 빈 문자열을 돌려준다 - <b>에셋 이름으로
        /// 대체하지 않는다</b>. 앞뒤 공백은 제거해서, 공백만 적힌 값이 유효한 id처럼 보이지 않게 한다.</summary>
        public string DungeonId => string.IsNullOrWhiteSpace(dungeonId) ? string.Empty : dungeonId.Trim();

        /// <summary>목록에 올릴 수 있는 던전인지 여부. 지금 기준은 식별자 하나뿐이며, 문구/이미지/월드가
        /// 비어 있는 것은 표시 문제이지 목록에서 빼야 할 이유가 아니다(그 경우는 그리는 쪽이 비워둔다).</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(dungeonId);

        /// <summary>던전 이름 참조. 표시하는 쪽이 구독해서 현재 Locale 문자열을 받는다.</summary>
        public LocalizedTextReference DungeonName => dungeonName;

        /// <summary>던전 이름의 Table/Key가 지정되어 있는지 여부(번역 값의 존재를 보장하지는 않는다).</summary>
        public bool HasDungeonName => dungeonName != null && dungeonName.HasReference;

        /// <summary>이 던전이 속한 월드. 지정하지 않았으면 null이며, 월드 이름 문구는 그리는 쪽이
        /// <see cref="WorldDefinition.LocalizedName"/>에서 직접 읽는다.</summary>
        public WorldDefinition World => world;

        /// <summary>목록 항목의 대표 이미지(선택). 없으면 null이며 그리는 쪽이 Image를 끈다.</summary>
        public Sprite RepresentativeSprite => representativeSprite;

        /// <summary>등장 몬스터 목록. <b>절대 null을 돌려주지 않는다</b> - 비어 있으면 빈 목록이다.
        /// 항목 자체가 비어 있는(null) 칸은 그리는 쪽이 건너뛴다.</summary>
        public IReadOnlyList<MonsterDefinition> Monsters =>
            monsters != null ? (IReadOnlyList<MonsterDefinition>)monsters : EmptyMonsters;

        /// <summary>대표 보상 아이템 목록. <b>절대 null을 돌려주지 않는다</b> - 비어 있으면 빈 목록이며,
        /// 비어 있는(null) 항목은 그리는 쪽이 건너뛴다.</summary>
        public IReadOnlyList<ItemDefinition> RewardItems =>
            rewardItems != null ? (IReadOnlyList<ItemDefinition>)rewardItems : EmptyRewards;

        /// <summary>표에 적힌 필요 캐릭터 레벨(1 이상).</summary>
        public int RequiredCharacterLevel => Mathf.Max(1, requiredCharacterLevel);
        public int CorruptionIntervalSeconds => Mathf.Max(1, corruptionIntervalSeconds);
        public int CorruptionGainPerInterval => Mathf.Max(1, corruptionGainPerInterval);

        /// <summary>정렬용 순서 값. 작을수록 앞이며, 지정하지 않으면 0이다.</summary>
        public int DisplayOrder => displayOrder;
    }
}
