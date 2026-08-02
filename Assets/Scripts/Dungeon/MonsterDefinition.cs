using Common;
using Enemy;
using UnityEngine;

namespace Dungeon
{
    /// <summary>
    /// 몬스터 한 종의 정의 - "이 몬스터가 무엇인가"만 담는다(식별자, 표시 이름, 소속 월드, 모션 프로필,
    /// 미리보기 이미지, 최대 내구도, 목록 순서). <see cref="DungeonDefinition"/>이 이 에셋을 순서대로
    /// 참조해서 "이 던전에 무엇이 나오는가"를 표현한다.
    ///
    /// <b>모션 데이터는 여기에 복사하지 않는다.</b> 프레임/피격 반응/연출값의 원천은 언제나
    /// <see cref="MonsterMotionProfile"/> 하나이며, 이 에셋은 그 프로필을 <b>가리키기만</b> 한다 -
    /// 같은 값을 두 곳에 두면 어느 쪽이 진짜인지가 흐려지기 때문이다.
    ///
    /// <b>미리보기 이미지는 없어도 된다.</b> 따로 지정하지 않으면 <see cref="PreviewSprite"/>가
    /// 모션 프로필의 Base Idle 첫 프레임을 대신 돌려준다 - 대부분의 몬스터는 "가만히 서 있는 첫 그림"이
    /// 곧 미리보기라서, 같은 스프라이트를 에셋마다 한 번 더 지정하게 만들지 않기 위함이다.
    /// 프로필도 프레임도 없으면 null이며, 그리는 쪽이 빈 칸으로 표시한다.
    ///
    /// <b>진행 상태는 여기에 없다.</b> 최대 내구도는 이 몬스터 종이 가지는 값이고, 지금 남은 내구도는
    /// 런타임(전투 쪽)이 소유한다.
    /// </summary>
    [CreateAssetMenu(fileName = "MonsterDefinition", menuName = "Dungeon/Monster Definition")]
    public class MonsterDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("이 몬스터의 고유 식별자(slime_01 등). <b>반드시 직접 적는다</b> - 비워두면 이 몬스터는 " +
                 "목록에 나오지 않는다(파일 이름으로 대체하지 않는다). 한 번 정하면 바꾸지 않는다.")]
        [SerializeField] private string monsterId;

        [Header("Localization")]
        [Tooltip("화면에 표시할 몬스터 이름. 카테고리 번호 + 숫자 키로 지정한다.")]
        [SerializeField] private LocalizedTextReference localizedName = new LocalizedTextReference();

        [Header("Placement")]
        [Tooltip("이 몬스터가 속한 월드. 비어 있어도 몬스터 자체는 유효하다 - 소속은 분류를 위한 값이지 " +
                 "표시 조건이 아니다.")]
        [SerializeField] private WorldDefinition world;

        [Header("Motion")]
        [Tooltip("이 몬스터의 모션 제작 데이터. 프레임/피격 반응/연출값의 원천은 이 프로필 하나뿐이며, " +
                 "여기에 값을 복사해 두지 않는다.")]
        [SerializeField] private MonsterMotionProfile motionProfile;

        [Header("Presentation")]
        [Tooltip("미리보기 칸에 표시할 이미지(선택). 비워두면 Motion Profile의 Base Idle 첫 프레임을 " +
                 "대신 쓴다 - 둘 다 없으면 그 칸은 빈 칸이 된다.")]
        [SerializeField] private Sprite previewSprite;

        [Header("Combat")]
        [Tooltip("이 몬스터 종의 최대 내구도. 지금 남은 내구도는 여기가 아니라 전투 런타임이 들고 있다.")]
        [Min(1)]
        [SerializeField] private int maxDurability = 1;

        [Header("Ordering")]
        [Tooltip("몬스터를 정렬할 때 쓰는 순서 값. 작을수록 앞이다 - 이 값 자체가 목록을 만들지는 않으며, " +
                 "던전 상세의 표시 순서는 DungeonDefinition의 Monsters 작성 순서가 결정한다.")]
        [SerializeField] private int displayOrder;

        /// <summary>이 몬스터의 고유 식별자. 비어 있으면 빈 문자열을 돌려준다 - <b>에셋 이름으로
        /// 대체하지 않는다</b>. 앞뒤 공백은 제거해서, 공백만 적힌 값이 유효한 id처럼 보이지 않게 한다.</summary>
        public string MonsterId => string.IsNullOrWhiteSpace(monsterId) ? string.Empty : monsterId.Trim();

        /// <summary>목록에 올릴 수 있는 몬스터인지 여부. 지금 기준은 식별자 하나뿐이며, 이미지/프로필이
        /// 비어 있는 것은 표시 문제이지 목록에서 빼야 할 이유가 아니다.</summary>
        public bool IsValid => !string.IsNullOrWhiteSpace(monsterId);

        /// <summary>몬스터 이름 참조. 표시하는 쪽이 구독해서 현재 Locale 문자열을 받는다.
        /// <b>절대 null을 돌려주지 않는다</b> - 참조가 비어 있을 수는 있어도 객체 자체는 항상 있다.</summary>
        public LocalizedTextReference LocalizedName => localizedName ?? (localizedName = new LocalizedTextReference());

        /// <summary>몬스터 이름의 Table/Key가 지정되어 있는지 여부(번역 값의 존재를 보장하지는 않는다).</summary>
        public bool HasLocalizedName => localizedName != null && localizedName.HasReference;

        /// <summary>이 몬스터가 속한 월드. 지정하지 않았으면 null이며, 읽는 쪽이 그 경우를 처리한다.</summary>
        public WorldDefinition World => world;

        /// <summary>모션 제작 데이터. 지정하지 않았으면 null이다 - 전투에 실제로 세울 때는 읽는 쪽이
        /// 이 값의 유무를 확인해야 한다.</summary>
        public MonsterMotionProfile MotionProfile => motionProfile;

        /// <summary>
        /// 미리보기 칸에 그릴 이미지. 직접 지정한 이미지가 있으면 그것을, 없으면 모션 프로필의
        /// Base Idle 첫 프레임을 돌려준다. 프로필이 없거나 Base Idle에 프레임이 하나도 없으면 null이며,
        /// 그리는 쪽이 Image를 비우고 끈다 - <b>여기서 대체 이미지를 만들어 내지 않는다</b>.
        /// </summary>
        public Sprite PreviewSprite
        {
            get
            {
                if (previewSprite != null) return previewSprite;
                if (motionProfile == null) return null;

                MonsterMotionProfile.FrameClip baseIdle = motionProfile.BaseIdle;
                if (baseIdle == null) return null;

                // FrameClip.Frames는 null을 돌려주지 않지만, 칸 자체가 비어 있을 수는 있다.
                Sprite[] frames = baseIdle.Frames;
                return frames.Length > 0 ? frames[0] : null;
            }
        }

        /// <summary>이 몬스터 종의 최대 내구도. 최소 1을 보장한다 - 0으로 저장된 값이 "즉시 쓰러진
        /// 몬스터"로 해석되지 않게 한다.</summary>
        public int MaxDurability => Mathf.Max(1, maxDurability);

        /// <summary>정렬용 순서 값. 작을수록 앞이며, 지정하지 않으면 0이다.</summary>
        public int DisplayOrder => displayOrder;
    }
}
