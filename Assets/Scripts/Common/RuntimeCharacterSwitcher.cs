using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// ControlDock의 캐릭터 변경 테스트 버튼(btn_switching). 런타임에 스프라이트나
    /// CharacterMotionProfile만 바꾸는 대신, 씬에 미리 준비된 캐릭터 GameObject 자체를 켜고 끈다 -
    /// PlayerCharacterAnimator가 Awake에서 캐릭터별 모션/공격 이동값을 독립적으로 초기화하므로,
    /// 켜져 있는 오브젝트만 자연스럽게 자신의 Base Idle부터 새로 시작한다.
    ///
    /// characters 배열은 순서가 곧 순환 순서다(현재: CatKnight -> Barbarian -> 다시 CatKnight).
    /// 캐릭터가 2종뿐인 테스트 단계 기능이라 목록/드롭다운/저장은 없다 - 마지막 선택은 저장하지 않고,
    /// 앱을 재시작하면 항상 defaultCharacterIndex로 돌아간다.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class RuntimeCharacterSwitcher : MonoBehaviour
    {
        [Tooltip("전환 대상 캐릭터 GameObject 목록. 클릭할 때마다 이 순서대로 다음 캐릭터로 넘어간다.")]
        [SerializeField] private GameObject[] characters;

        [Tooltip("앱 시작 시 활성화할 캐릭터의 배열 인덱스.")]
        [SerializeField] private int defaultCharacterIndex;

        private Button button;
        private int currentIndex;

        private void Awake()
        {
            button = GetComponent<Button>();
            currentIndex = (characters != null && characters.Length > 0)
                ? Mathf.Clamp(defaultCharacterIndex, 0, characters.Length - 1)
                : 0;
            ApplyActiveCharacter();
        }

        private void OnEnable()
        {
            button.onClick.AddListener(SwitchToNextCharacter);
        }

        private void OnDisable()
        {
            button.onClick.RemoveListener(SwitchToNextCharacter);
        }

        public void SwitchToNextCharacter()
        {
            if (characters == null || characters.Length == 0) return;

            currentIndex = (currentIndex + 1) % characters.Length;
            ApplyActiveCharacter();
        }

        private void ApplyActiveCharacter()
        {
            if (characters == null) return;

            for (int i = 0; i < characters.Length; i++)
            {
                if (characters[i] != null)
                {
                    characters[i].SetActive(i == currentIndex);
                }
            }
        }
    }
}
