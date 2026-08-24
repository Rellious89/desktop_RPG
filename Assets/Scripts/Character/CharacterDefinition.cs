using Common;
using Dungeon;
using UnityEngine;

namespace Character
{
    /// <summary>
    /// 캐릭터 한 종의 <b>비(非)모션</b> 정의 - 리스트/교체 UI가 캐릭터를 식별하고 표시하는 데 필요한
    /// 값만 담는다(저장 키, 표시 이름, 초상화, 최대 행동력). 이 에셋이 "캐릭터가 무엇인지"의 단일
    /// 원천이며, CharacterRoster는 이 정의만으로 보유 목록을 만든다 - 캐릭터마다 씬 오브젝트를 두던
    /// 구조는 사라졌고, 지금은 런타임 액터 하나가 이 정의의 프로필을 받아 그 캐릭터를 연기한다.
    ///
    /// <b>모션 데이터는 여기에 복사하지 않는다.</b> Idle/공격 풀/Attack Movement는 지금까지대로
    /// <see cref="CharacterMotionProfile"/> 하나만 소유하고, 이 에셋은 그 프로필을 참조만 한다 -
    /// 같은 값을 두 에셋에 나눠 적어두는 경로를 만들지 않는다.
    ///
    /// <b>진행 상태(레벨/현재 행동력)도 여기에 없다.</b> 그 값들은 SaveData.characters에 저장되며,
    /// 이 에셋은 그 상태의 기본값(Max Stamina)과 표시용 정보만 제공한다.
    ///
    /// <b>표에서 만들어지는 칸이 뒤에 붙어 있다.</b> Character.csv 임포터가 채우는
    /// <see cref="LocalizedName"/> / <see cref="BaseMaxHealth"/> / <see cref="DisplayOrder"/>는
    /// 기존 칸 <b>뒤에</b> 추가한 것이라, 이미 저장돼 있는 수동 에셋은 그대로 읽힌다(없는 칸은 Unity가
    /// 기본값으로 채운다). <b>기존 칸의 이름과 의미는 하나도 바뀌지 않았다</b> -
    /// <see cref="DisplayName"/>이 무엇을 돌려주는지도 그대로다. 화면은 <see cref="LocalizedName"/>을
    /// 우선 사용하고, 참조가 없는 레거시 수동 에셋에 한해서만 <see cref="DisplayName"/>으로 폴백한다.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterDefinition", menuName = "Character/Character Definition")]
    public class CharacterDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("저장 데이터에서 이 캐릭터를 가리키는 키. 비워두면 에셋 파일 이름을 쓴다. " +
                 "한 번 정하면 바꾸지 않는다 - 바꾸면 기존 저장 항목과 연결이 끊긴다.")]
        [SerializeField] private string characterId;

        [Tooltip("리스트에 표시할 이름. 비워두면 Motion Profile의 Display Name을 쓴다.")]
        [SerializeField] private string displayName;

        [Header("References")]
        [Tooltip("이 캐릭터가 태어난 월드. Character.csv의 origin_world_id가 가리키는 WorldDefinition 참조이며, " +
                 "월드 이름이나 로컬라이즈 텍스트를 복사하지 않는다.")]
        [SerializeField] private WorldDefinition originWorld;

        [Tooltip("이 캐릭터의 모션 데이터 원천. 캐릭터를 투입하면 런타임 액터가 이 프로필을 그대로 " +
                 "적용해 연기한다 - 비어 있거나 재생 가능한 Base Idle이 없으면 CharacterRoster가 시작 시 " +
                 "오류를 남기고 이 캐릭터를 목록에서 제외한다.")]
        [SerializeField] private CharacterMotionProfile motionProfile;

        [Tooltip("리스트 항목에 표시할 초상화. 비워두면 Motion Profile의 Base Idle 첫 프레임을 " +
                 "임시 초상화로 쓴다(전용 초상화 아트가 준비되기 전용 폴백).")]
        [SerializeField] private Sprite portrait;

        [Header("Stamina")]
        [Tooltip("이 캐릭터의 최대 행동력. 현재 행동력은 저장 데이터가 들고 있고, 이 값은 그 상한이다.")]
        [Min(1)]
        [SerializeField] private int maxStamina = 5;

        [Header("Corruption")]
        [Min(0)] [SerializeField] private int baseCorruption;

        [Header("New Game")]
        [Tooltip("새 게임을 시작할 때 이 캐릭터를 처음부터 가지고 시작하는가. " +
                 "이미 진행 중인 저장 데이터에는 소급 적용되지 않는다 - 보유 여부는 저장 데이터가 " +
                 "소유하며, 이 값은 저장 문서를 처음 만들 때만 참고하는 표의 정책이다.")]
        [SerializeField] private bool initiallyOwned;

        [Header("Localization")]
        [Tooltip("표에서 지정한 캐릭터 이름. 카테고리 번호 + 숫자 키로 가리킨다. " +
                 "화면은 이 값을 우선 사용하며, 참조가 없는 레거시 에셋만 Display Name으로 폴백한다.")]
        [SerializeField] private LocalizedTextReference localizedName = new LocalizedTextReference();

        [Header("Health")]
        [Tooltip("이 캐릭터의 기본 최대 체력. <b>선택 항목</b>이라 지정하지 않을 수 있고, 지정하지 " +
                 "않은 상태와 '0으로 지정한 상태'는 다르다 - 값이 있는지는 Has Base Max Health가 " +
                 "말한다. 체력 규칙 자체는 아직 없다.")]
        [SerializeField] private bool hasBaseMaxHealth;

        [Min(1)]
        [SerializeField] private int baseMaxHealth = 1;

        [Header("Ordering")]
        [Tooltip("캐릭터를 정렬할 때 쓰는 순서 값. 작을수록 앞이다 - 이 값 자체가 목록을 만들지는 " +
                 "않으며, 목록의 순서는 CharacterCatalog의 작성 순서가 결정한다.")]
        [SerializeField] private int displayOrder;

        public string CharacterId => string.IsNullOrWhiteSpace(characterId) ? name : characterId;

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(displayName)) return displayName;
                return motionProfile != null ? motionProfile.DisplayName : name;
            }
        }

        public CharacterMotionProfile MotionProfile => motionProfile;

        /// <summary>이 캐릭터가 태어난 월드 정의. 표시 이름을 복사한 문자열이 아니라 원본 정의 참조다.</summary>
        public WorldDefinition OriginWorld => originWorld;

        public int MaxStamina => Mathf.Max(1, maxStamina);
        public int BaseCorruption => Mathf.Max(0, baseCorruption);

        /// <summary>전용 초상화가 없으면 Base Idle의 첫 프레임을 돌려준다 - 초상화 아트가 준비되기
        /// 전에도 리스트에서 캐릭터를 구분할 수 있게 하기 위한 폴백이며, 둘 다 없으면 null이다
        /// (호출부가 이미지 자체를 숨긴다).</summary>
        public Sprite Portrait
        {
            get
            {
                if (portrait != null) return portrait;
                if (motionProfile == null) return null;

                Sprite[] idleFrames = motionProfile.BaseIdle != null ? motionProfile.BaseIdle.Frames : null;
                return idleFrames != null && idleFrames.Length > 0 ? idleFrames[0] : null;
            }
        }

        /// <summary>표에서 지정한 캐릭터 이름 참조. <b>절대 null을 돌려주지 않는다</b> - 참조가 비어
        /// 있을 수는 있어도 객체 자체는 항상 있다(<see cref="Inventory.CurrencyDefinition"/>와 같은
        /// 규칙). <b>이 값은 <see cref="DisplayName"/>에 끼어들지 않는다</b> - 표시 이름의 경로를
        /// 직접 바꾸지 않으며, 화면의 Locale 대응은 CharacterNameBinding이 담당한다.</summary>
        public LocalizedTextReference LocalizedName =>
            localizedName ?? (localizedName = new LocalizedTextReference());

        /// <summary>이름의 Table/Key가 지정되어 있는지 여부(번역 값의 존재를 보장하지는 않는다).</summary>
        public bool HasLocalizedName => localizedName != null && localizedName.HasReference;

        /// <summary>기본 최대 체력이 <b>지정되어 있는지</b>. 지정하지 않은 것과 작은 값을 지정한 것은
        /// 다른 상태이며, 표의 빈 칸은 언제나 "지정하지 않음"으로 들어온다 - 빈 칸을 0이나 1로 바꿔
        /// 채우면 "아직 정하지 않았다"가 데이터에서 사라진다.</summary>
        public bool HasBaseMaxHealth => hasBaseMaxHealth;

        /// <summary>기본 최대 체력. <b>지정하지 않았으면 0</b>이므로 값을 쓰기 전에 반드시
        /// <see cref="HasBaseMaxHealth"/>를 먼저 본다 - 0을 "체력 0"으로 읽으면 안 된다.</summary>
        public int BaseMaxHealth => hasBaseMaxHealth ? Mathf.Max(1, baseMaxHealth) : 0;

        /// <summary>
        /// <b>새 게임을 시작할 때</b> 이 캐릭터를 처음부터 가지고 시작하는지. 표(Character.csv)의
        /// <c>initially_owned</c>가 그대로 들어온다.
        ///
        /// <b>이 값은 "지금 이 플레이어가 그 캐릭터를 보유했는가"가 아니다.</b> 보유는 저장 문서
        /// (SaveData.characters)가 소유한다 - 그 목록에 항목이 있다는 것이 곧 보유이며, 이 정의 에셋은
        /// 어떤 플레이어의 상태도 알지 못한다.
        ///
        /// <b>이미 있는 저장 파일에 소급 적용되지 않는다.</b> 이 값을 나중에 켜거나 꺼도 진행 중인
        /// 저장 문서의 보유 목록은 달라지지 않는다 - 표를 고쳤다고 남의 캐릭터를 뺏거나 주지 않는다는
        /// 뜻이며, 새 게임을 시작하는 순간에만 읽히는 <b>시드 정책</b>이다.
        ///
        /// 값이 없는(= 이 칸이 생기기 전에 만들어진) 수동 에셋은 Unity가 <c>false</c>로 채운다.
        /// </summary>
        public bool InitiallyOwned => initiallyOwned;

        /// <summary>정렬용 순서 값. 작을수록 앞이며, 지정하지 않으면 0이다.</summary>
        public int DisplayOrder => displayOrder;
    }
}
