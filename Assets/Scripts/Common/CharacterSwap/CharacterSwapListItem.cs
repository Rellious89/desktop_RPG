using System;
using Character;
using Recovery;
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
        private const string PortraitName = "sp_portrait";
        private const string NameTextName = "lb_name";
        private const string LevelTextName = "lb_level";
        private const string StaminaValueTextName = "lb_percent";
        private const string StateTextName = "lb_state";
        private const string StateBackgroundName = "bg_status";

        /// <summary>실제 프리팹(list_Character)의 상태 텍스트 이름. <see cref="StateTextName"/>으로
        /// 찾지 못했을 때만 이 이름으로 한 번 더 찾는다 - 이 항목 프리팹 안에서만 찾으므로 다른 영역의
        /// 같은 이름을 물어올 수 없다.</summary>
        private const string StateTextFallbackName = "lb_status";

        /// <summary>패널이 알려주는 이 항목의 표시 상태. 색과 상태 문구가 이 값 하나로 결정된다.</summary>
        public enum DisplayState
        {
            /// <summary>전투 가능 - 선택해서 교체할 수 있다.</summary>
            Ready,
            /// <summary>지금 전투 중인 캐릭터.</summary>
            InUse,
            /// <summary>행동력 소진 - 선택해도 교체할 수 없다. 단 회복소로는 끌어갈 수 있다.</summary>
            Exhausted,
            /// <summary>회복소에서 회복 중 - 선택/교체/드래그 모두 불가.</summary>
            Recovering,
            /// <summary>회복이 끝나 합류를 기다리는 중 - 선택/교체/드래그 모두 불가.</summary>
            RecoveryComplete,
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

        [Tooltip("상태별 스프라이트를 표시할 Image. 비워두면 자식의 Status/bg_status를 찾는다.")]
        [SerializeField] private Image stateBackgroundImage;

        [Tooltip("행동력 막대. 비워두면 자식에서 찾는다.")]
        [SerializeField] private ProgressBarView staminaBar;

        [Header("Formats (숫자 배치라 번역 대상이 아니다)")]
        [SerializeField] private string levelFormat = "Lv. {0}";
        [SerializeField] private string staminaFormat = "{0} / {1}";

        [Header("Localization (01 UI 카테고리)")]
        [SerializeField] private LocalizedTextReference stateReadyText;
        [SerializeField] private LocalizedTextReference stateInUseText;
        [SerializeField] private LocalizedTextReference stateExhaustedText;

        [Tooltip("회복 중 상태 문구. 남은 시간은 여기에 넣지 않는다 - 시간 표시는 회복소 슬롯의 " +
                 "lb_time이 담당한다.")]
        [SerializeField] private LocalizedTextReference stateRecoveringText;

        [Tooltip("회복이 끝나 합류를 기다리는 상태 문구.")]
        [SerializeField] private LocalizedTextReference stateRecoveryCompleteText;

        [Header("State Sprites (Status/bg_status)")]
        [SerializeField] private Sprite stateReadySprite;
        [SerializeField] private Sprite stateInUseSprite;
        [SerializeField] private Sprite stateExhaustedSprite;
        [SerializeField] private Sprite stateRecoveringSprite;
        [SerializeField] private Sprite stateRecoveryCompleteSprite;

        [Header("Selection Feedback")]
        [Tooltip("선택되지 않은 전투 가능 상태의 배경색.")]
        [SerializeField] private Color normalColor = Color.white;

        [Tooltip("교체 대상으로 선택된(pending) 항목의 배경색.")]
        [SerializeField] private Color selectedColor = new Color(1f, 0.85f, 0.35f, 1f);

        [Tooltip("지금 전투 중인 캐릭터의 배경색.")]
        [SerializeField] private Color inUseColor = new Color(0.55f, 0.8f, 1f, 1f);

        [Tooltip("행동력이 0이라 선택할 수 없는 항목의 배경색.")]
        [SerializeField] private Color exhaustedColor = new Color(0.45f, 0.45f, 0.45f, 1f);

        [Tooltip("회복 중/합류 대기 항목의 배경색. 새 스프라이트를 만들지 않고 색으로만 구분한다.")]
        [SerializeField] private Color inRecoveryColor = new Color(0.4f, 0.55f, 0.5f, 1f);

        [Header("Recovery Drag (같은 오브젝트의 컴포넌트. 없으면 드래그 기능이 없는 것으로 본다)")]
        [Tooltip("회복소로 끌어다 놓기를 담당하는 컴포넌트. 비워두면 같은 GameObject에서 찾는다.")]
        [SerializeField] private CharacterRecoveryDragSource dragSource;

        private CharacterDefinition boundCharacter;
        private Action<CharacterDefinition> selectionCallback;
        private DisplayState subscribedState;
        private bool stateSubscribed;
        private bool missingStateReferenceLogged;
        private bool resolved;
        private readonly CharacterNameBinding characterName = new CharacterNameBinding();


        /// <summary>이 항목이 표시 중인 캐릭터. 패널이 특정 캐릭터의 항목만 골라 갱신할 때 쓴다.</summary>
        public CharacterDefinition BoundCharacter => boundCharacter;

        /// <summary>드래그 고스트가 복제할 초상화 오브젝트. 초상화 Image의 부모(mask_portrait)가 있으면
        /// 그것을 쓴다 - 마스크까지 함께 복제해야 원본과 같은 모양으로 보인다. 없으면 null이며
        /// 그 경우 고스트는 이름만으로 만들어진다.</summary>
        public RectTransform GhostPortraitSource
        {
            get
            {
                ResolveReferences();
                if (portraitImage == null) return null;

                // 초상화가 마스크 안에 들어 있는 구조(mask_portrait/sp_portrait)면 마스크째 복제한다.
                Transform parent = portraitImage.transform.parent;
                if (parent != null && parent.GetComponent<Mask>() != null) return parent as RectTransform;
                return portraitImage.transform as RectTransform;
            }
        }

        /// <summary>드래그 고스트가 복제할 이름 텍스트 오브젝트. 없으면 null이다.</summary>
        public RectTransform GhostNameSource
        {
            get
            {
                ResolveReferences();
                return nameText != null ? nameText.transform as RectTransform : null;
            }
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (boundCharacter != null) characterName.Bind(boundCharacter, ApplyLocalizedCharacterName);
        }

        private void OnDisable()
        {
            UnsubscribeStateText();
            characterName.Unbind();
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

            characterName.Bind(character, ApplyLocalizedCharacterName);

            if (portraitImage != null)
            {
                Sprite portrait = character != null ? character.Portrait : null;
                portraitImage.sprite = portrait;
                // 초상화 아트도 Base Idle 폴백도 없으면 빈 사각형이 남지 않게 이미지를 숨긴다.
                portraitImage.enabled = portrait != null;
            }
        }

        /// <summary>
        /// 패널이 계산한 현재 값으로 이 항목만 다시 그린다.
        ///
        /// <b>교체 가능 여부와 회복 드래그 가능 여부는 별개의 값으로 받는다.</b> 두 규칙은 서로 다르다 -
        /// 예를 들어 행동력이 0인 캐릭터는 교체할 수 없지만 회복소로는 끌어갈 수 있고, 행동력이 최대인
        /// 캐릭터는 교체할 수 있지만 회복시킬 것이 없어 끌 수 없다. 그래서 하나의
        /// <see cref="Button.interactable"/>로 두 규칙을 묶지 않는다.
        /// </summary>
        /// <param name="canSwap">교체 대상으로 고를 수 있는지(CharacterRoster.GetSwapBlockReason 결과).</param>
        /// <param name="canDragToRecovery">회복소로 끌어다 놓을 수 있는지(RecoveryStation.CanRegister 결과).</param>
        public void Refresh(int level, int currentStamina, int maxStamina, DisplayState state,
                            bool isPendingSelection, bool canSwap, bool canDragToRecovery)
        {
            ResolveReferences();

            if (levelText != null) levelText.text = string.Format(levelFormat, level);
            if (staminaValueText != null) staminaValueText.text = string.Format(staminaFormat, currentStamina, maxStamina);
            if (staminaBar != null) staminaBar.SetValue(currentStamina, maxStamina);

            ApplyStateText(state);
            ApplyStateSprite(state);
            ApplyBackgroundColor(state, isPendingSelection);

            // 교체 대상이 될 수 없는 항목은 클릭 자체를 막는다 - 눌러도 아무 일이 없는 것보다, 누를 수
            // 없다는 것이 보이는 편이 안전하다. 상태 문구와 색으로 이유가 함께 표시된다.
            if (selectButton != null) selectButton.interactable = canSwap;

            // 드래그는 완전히 별개의 판정이다. 교체가 막힌 항목(행동력 0)도 끌 수 있고, 교체가 되는
            // 항목(행동력 최대)도 끌 수 없을 수 있다.
            if (dragSource != null) dragSource.enabled = canDragToRecovery;
        }

        private void HandleClicked()
        {
            if (boundCharacter == null) return;

            // 회복 드래그 제스처가 만들어 낸 클릭은 선택으로 취급하지 않는다. EventSystem도 드래그가
            // 시작되면 클릭 자격을 내리지만, 그 동작에만 기대지 않고 여기서 한 번 더 막는다.
            // 억제는 드래그가 끝난 프레임까지만 유효하므로 다음 클릭은 정상 동작한다.
            if (dragSource != null && dragSource.ShouldSuppressClick()) return;

            selectionCallback?.Invoke(boundCharacter);
        }

        private void ApplyBackgroundColor(DisplayState state, bool isPendingSelection)
        {
            if (background == null) return;

            if (state == DisplayState.InUse) background.color = inUseColor;
            else if (state == DisplayState.Recovering || state == DisplayState.RecoveryComplete) background.color = inRecoveryColor;
            else if (state == DisplayState.Exhausted) background.color = exhaustedColor;
            else background.color = isPendingSelection ? selectedColor : normalColor;
        }

        /// <summary>Status/bg_status의 이미지를 현재 상태에 대응하는 스프라이트로 교체한다.</summary>
        private void ApplyStateSprite(DisplayState state)
        {
            if (stateBackgroundImage == null) return;

            Sprite sprite = GetStateSprite(state);
            stateBackgroundImage.sprite = sprite;
            stateBackgroundImage.enabled = sprite != null;
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
                // Table/Key 참조 자체가 없는 설정 오류다 - 조용히 넘어가지 않고 반드시 로그를 남긴다.
                if (!missingStateReferenceLogged)
                {
                    missingStateReferenceLogged = true;
                    Debug.LogError($"[CharacterSwapListItem] '{name}': 상태 문구의 Localization Table/Key " +
                                   "참조가 비어 있습니다. Inspector에서 Category 01 UI의 상태 Key를 지정하세요.", this);
                }

                stateText.text = string.Empty;
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

        /// <summary>Localization이 넘겨준 문구를 그대로 쓴다. <b>남은 시간 같은 값을 여기에 섞지
        /// 않는다</b> - 시간 표시는 회복소 슬롯의 lb_time이 담당한다.</summary>
        private void ApplyLocalizedStateText(string localizedText)
        {
            if (stateText != null) stateText.text = localizedText;
        }

        private void ApplyLocalizedCharacterName(string localizedText)
        {
            if (nameText != null) nameText.text = localizedText;
        }

        private LocalizedTextReference GetStateReference(DisplayState state)
        {
            switch (state)
            {
                case DisplayState.InUse: return stateInUseText;
                case DisplayState.Exhausted: return stateExhaustedText;
                case DisplayState.Recovering: return stateRecoveringText;
                case DisplayState.RecoveryComplete: return stateRecoveryCompleteText;
                default: return stateReadyText;
            }
        }

        private Sprite GetStateSprite(DisplayState state)
        {
            switch (state)
            {
                case DisplayState.InUse: return stateInUseSprite;
                case DisplayState.Exhausted: return stateExhaustedSprite;
                case DisplayState.Recovering: return stateRecoveringSprite;
                case DisplayState.RecoveryComplete: return stateRecoveryCompleteSprite;
                default: return stateReadySprite;
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
            // 실제 프리팹은 lb_status를 쓴다 - 이 항목의 자식 안에서만 찾으므로 다른 영역의 같은
            // 이름을 물어오지 않는다.
            if (stateText == null) stateText = FindChildComponent<TextMeshProUGUI>(StateTextFallbackName);
            if (stateBackgroundImage == null) stateBackgroundImage = FindChildComponent<Image>(StateBackgroundName);
            if (dragSource == null) dragSource = GetComponent<CharacterRecoveryDragSource>();

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
                Debug.LogWarning($"[CharacterSwapListItem] '{name}': 상태 텍스트('{StateTextName}' 또는 " +
                                 $"'{StateTextFallbackName}')가 없어 상태가 배경색으로만 구분됩니다.", this);
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
