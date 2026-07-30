using System;
using Character;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 캐릭터 교체 패널 리스트의 항목 하나(list_Character 프리팹). 자기 값을 직접 계산하지 않고
    /// <see cref="CharacterSwapPanel"/>이 넘겨준 값만 그린다 - 어떤 캐릭터가 현재/선택 상태인지도
    /// 패널이 판단해서 알려준다.
    ///
    /// 행동력 막대는 경험치 바 프리팹의 시각 구조를 그대로 쓰지만 값은 완전히 별개다
    /// (<see cref="ProgressBarView"/>에 캐릭터별 현재/최대 행동력을 직접 주입한다) - PlayerProgress나
    /// 경험치 이벤트는 이 컴포넌트 어디에서도 참조하지 않는다.
    ///
    /// 참조를 비워두면 프리팹의 기존 오브젝트 이름으로 자동 탐색한다 - 프리팹 구조를 바꾸지 않고도
    /// 동작하게 하기 위한 것이며, 구조를 바꾼 경우에는 Inspector에서 직접 연결하면 된다.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterSwapListItem : MonoBehaviour
    {
        private const string MissingReferenceDisplay = "<Missing Localization>";

        private const string PortraitName = "sp_portrait";
        private const string NameTextName = "lb_name";
        private const string LevelTextName = "lb_level";
        private const string StaminaValueTextName = "lb_percent";
        private const string StateTextName = "lb_state";

        /// <summary>패널이 알려주는 이 항목의 표시 상태. 색과 상태 문구가 이 값 하나로 결정된다.</summary>
        public enum DisplayState
        {
            /// <summary>전투 가능 - 선택해서 교체할 수 있다.</summary>
            Ready,
            /// <summary>지금 전투 중인 캐릭터.</summary>
            InUse,
            /// <summary>행동력 소진 - 선택해도 교체할 수 없다.</summary>
            Exhausted,
        }

        [Header("References (비워두면 프리팹 이름으로 자동 탐색)")]
        [Tooltip("항목 클릭을 받는 Button. 비워두면 이 GameObject의 Button을 쓴다.")]
        [SerializeField] private Button selectButton;

        [Tooltip("선택/사용 중/소진 상태에 따라 색을 바꿀 배경 Image. 비워두면 이 GameObject의 Image를 쓴다.")]
        [SerializeField] private Image background;

        [SerializeField] private Image portraitImage;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private TextMeshProUGUI staminaValueText;

        [Tooltip("행동력 상태(전투 가능/사용 중/소진)를 표시할 텍스트. 프리팹에 lb_state를 추가해 " +
                 "연결한다 - 없으면 색으로만 구분된다.")]
        [SerializeField] private TextMeshProUGUI stateText;

        [Tooltip("행동력 막대. 비워두면 자식에서 찾는다.")]
        [SerializeField] private ProgressBarView staminaBar;

        [Header("Formats (숫자 배치라 번역 대상이 아니다)")]
        [SerializeField] private string levelFormat = "Lv. {0}";
        [SerializeField] private string staminaFormat = "{0} / {1}";

        [Header("Localization (01 UI 카테고리)")]
        [SerializeField] private LocalizedTextReference stateReadyText;
        [SerializeField] private LocalizedTextReference stateInUseText;
        [SerializeField] private LocalizedTextReference stateExhaustedText;

        [Header("Selection Feedback")]
        [Tooltip("선택되지 않은 전투 가능 상태의 배경색.")]
        [SerializeField] private Color normalColor = Color.white;

        [Tooltip("교체 대상으로 선택된(pending) 항목의 배경색.")]
        [SerializeField] private Color selectedColor = new Color(1f, 0.85f, 0.35f, 1f);

        [Tooltip("지금 전투 중인 캐릭터의 배경색.")]
        [SerializeField] private Color inUseColor = new Color(0.55f, 0.8f, 1f, 1f);

        [Tooltip("행동력이 0이라 선택할 수 없는 항목의 배경색.")]
        [SerializeField] private Color exhaustedColor = new Color(0.45f, 0.45f, 0.45f, 1f);

        private CharacterDefinition boundCharacter;
        private Action<CharacterDefinition> selectionCallback;
        private DisplayState subscribedState;
        private bool stateSubscribed;
        private bool missingStateReferenceLogged;
        private bool resolved;

        /// <summary>이 항목이 표시 중인 캐릭터. 패널이 특정 캐릭터의 항목만 골라 갱신할 때 쓴다.</summary>
        public CharacterDefinition BoundCharacter => boundCharacter;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            UnsubscribeStateText();
        }

        /// <summary>이 항목이 어떤 캐릭터를 담당할지 정한다(클릭 콜백 포함). 값 표시는
        /// <see cref="Refresh"/>가 담당하므로 여기서는 그리지 않는다.</summary>
        public void Bind(CharacterDefinition character, Action<CharacterDefinition> onSelected)
        {
            ResolveReferences();

            boundCharacter = character;
            selectionCallback = onSelected;

            if (selectButton != null)
            {
                selectButton.onClick.RemoveListener(HandleClicked);
                selectButton.onClick.AddListener(HandleClicked);
            }

            if (nameText != null) nameText.text = character != null ? character.DisplayName : string.Empty;

            if (portraitImage != null)
            {
                Sprite portrait = character != null ? character.Portrait : null;
                portraitImage.sprite = portrait;
                // 초상화 아트도 Base Idle 폴백도 없으면 빈 사각형이 남지 않게 이미지를 숨긴다.
                portraitImage.enabled = portrait != null;
            }
        }

        /// <summary>패널이 계산한 현재 값으로 이 항목만 다시 그린다.</summary>
        public void Refresh(int level, int currentStamina, int maxStamina, DisplayState state, bool isPendingSelection)
        {
            ResolveReferences();

            if (levelText != null) levelText.text = string.Format(levelFormat, level);
            if (staminaValueText != null) staminaValueText.text = string.Format(staminaFormat, currentStamina, maxStamina);
            if (staminaBar != null) staminaBar.SetValue(currentStamina, maxStamina);

            ApplyStateText(state);
            ApplyBackgroundColor(state, isPendingSelection);

            // 교체 대상이 될 수 없는 항목(지금 사용 중 / 행동력 소진)은 클릭 자체를 막는다 - 눌러도
            // 아무 일이 없는 것보다, 누를 수 없다는 것이 보이는 편이 안전하다. 상태 문구와 색으로
            // 이유가 함께 표시되므로 "왜 안 되는지 모르는" 상태가 남지 않는다.
            if (selectButton != null) selectButton.interactable = state == DisplayState.Ready;
        }

        private void HandleClicked()
        {
            if (boundCharacter == null) return;
            selectionCallback?.Invoke(boundCharacter);
        }

        private void ApplyBackgroundColor(DisplayState state, bool isPendingSelection)
        {
            if (background == null) return;

            if (state == DisplayState.InUse) background.color = inUseColor;
            else if (state == DisplayState.Exhausted) background.color = exhaustedColor;
            else background.color = isPendingSelection ? selectedColor : normalColor;
        }

        /// <summary>상태 문구는 Locale이 바뀌면 자동으로 다시 들어와야 하므로, 값을 한 번 읽어 넣는
        /// 대신 지금 상태에 해당하는 참조 하나만 구독한 채로 둔다(상태가 바뀌면 갈아탄다).</summary>
        private void ApplyStateText(DisplayState state)
        {
            if (stateText == null) return;

            LocalizedTextReference reference = GetStateReference(state);
            if (reference == null || !reference.HasReference)
            {
                UnsubscribeStateText();
                // 번역 값 누락은 Unity Localization의 fallback이 처리한다. 여기로 오는 경우는
                // Table/Key 참조 자체가 없는 설정 오류이므로 조용히 한국어/영어로 대체하지 않는다.
                if (!missingStateReferenceLogged)
                {
                    missingStateReferenceLogged = true;
                    Debug.LogError($"[CharacterSwapListItem] '{name}': 행동력 상태 문구의 Localization Table/Key " +
                                   "참조가 비어 있습니다. Inspector에서 Category 01 UI의 상태 Key를 지정하세요.", this);
                }
                stateText.text = MissingReferenceDisplay;
                return;
            }

            if (stateSubscribed && subscribedState == state) return;

            UnsubscribeStateText();
            subscribedState = state;
            stateSubscribed = true;
            // 구독 자체가 최초 로드를 유발하고, 이후 Locale 변경 시 자동으로 다시 호출된다.
            reference.StringChanged += ApplyLocalizedStateText;
        }

        private void UnsubscribeStateText()
        {
            if (!stateSubscribed) return;

            LocalizedTextReference reference = GetStateReference(subscribedState);
            if (reference != null) reference.StringChanged -= ApplyLocalizedStateText;
            stateSubscribed = false;
        }

        private void ApplyLocalizedStateText(string localizedText)
        {
            if (stateText != null) stateText.text = localizedText;
        }

        private LocalizedTextReference GetStateReference(DisplayState state)
        {
            switch (state)
            {
                case DisplayState.InUse: return stateInUseText;
                case DisplayState.Exhausted: return stateExhaustedText;
                default: return stateReadyText;
            }
        }

        private void ResolveReferences()
        {
            if (resolved) return;
            resolved = true;

            if (selectButton == null) selectButton = GetComponent<Button>();
            if (background == null) background = GetComponent<Image>();
            if (staminaBar == null) staminaBar = GetComponentInChildren<ProgressBarView>(true);

            if (portraitImage == null) portraitImage = FindChildComponent<Image>(PortraitName);
            if (nameText == null) nameText = FindChildComponent<TextMeshProUGUI>(NameTextName);
            if (levelText == null) levelText = FindChildComponent<TextMeshProUGUI>(LevelTextName);
            if (staminaValueText == null) staminaValueText = FindChildComponent<TextMeshProUGUI>(StaminaValueTextName);
            if (stateText == null) stateText = FindChildComponent<TextMeshProUGUI>(StateTextName);

            if (selectButton == null)
            {
                Debug.LogError($"[CharacterSwapListItem] '{name}': Button이 없어 항목을 선택할 수 없습니다.", this);
            }
            if (staminaBar == null)
            {
                Debug.LogWarning($"[CharacterSwapListItem] '{name}': ProgressBarView를 찾지 못해 행동력 막대가 " +
                                 "갱신되지 않습니다(수치 텍스트만 표시됩니다).", this);
            }
            if (stateText == null)
            {
                Debug.LogWarning($"[CharacterSwapListItem] '{name}': 행동력 상태 텍스트('{StateTextName}')가 없어 " +
                                 "상태가 배경색으로만 구분됩니다.", this);
            }
        }

        private T FindChildComponent<T>(string childName) where T : Component
        {
            Transform found = FindDeepChild(transform, childName);
            return found != null ? found.GetComponent<T>() : null;
        }

        private static Transform FindDeepChild(Transform root, string childName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName) return child;

                Transform found = FindDeepChild(child, childName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
