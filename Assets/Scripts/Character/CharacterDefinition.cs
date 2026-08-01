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

        public int MaxStamina => Mathf.Max(1, maxStamina);

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
    }
}
