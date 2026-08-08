using UnityEngine;
using UnityEngine.EventSystems;

namespace Common
{
    /// <summary>
    /// 메뉴 버튼에 붙여 Hover 툴팁을 요청하는 컴포넌트. 표시/숨김/대기시간은 전부
    /// <see cref="HoverTooltipController"/>가 처리하고, 여기서는 "이 버튼에 마우스가 들어왔다/나갔다"만
    /// 알린다 - 그래서 버튼이 늘어나도 이 컴포넌트를 붙이는 것 외에 할 일이 없다.
    ///
    /// <b>버튼의 클릭 동작에는 관여하지 않는다.</b> IPointerEnter/ExitHandler만 구현하므로
    /// Button의 클릭 처리와 경로가 겹치지 않고, 기존 onClick과 <see cref="ModalPanelOpener"/>가 그대로 동작한다.
    ///
    /// <b>문구는 프로젝트의 로컬라이징을 그대로 쓴다.</b> Inspector에서 직접 지정할 수 있지만, 비워 두면
    /// 이 버튼의 자식 라벨(<see cref="LocalizedTMPText"/>)에 이미 지정된 참조를 그대로 쓴다 - 메뉴
    /// 버튼들은 라벨을 꺼 둔 채로도 각자의 Table/Key를 들고 있으므로, 같은 문구를 두 곳에 중복 저작하거나
    /// 한국어를 코드에 박아 넣을 이유가 없다. 라벨이 꺼져 있어도 참조 자체는 유효하다.
    ///
    /// <b>비활성화되면 반드시 툴팁을 거둔다.</b> 메뉴가 접히거나 버튼이 꺼지면 OnDisable에서 취소를
    /// 알리므로, 대기 중이던 예약도 이미 떠 있던 툴팁도 화면에 남지 않는다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class HoverTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("툴팁을 실제로 표시하는 컨트롤러. 비워두면 부모 계층에서 찾는다.")]
        [SerializeField] private HoverTooltipController controller;

        [Tooltip("툴팁에 표시할 문구. 비워두면 이 버튼의 자식 라벨(Localized TMP Text)에 지정된 " +
                 "Table/Key를 그대로 사용한다.")]
        [SerializeField] private LocalizedTextReference tooltipText;

        private RectTransform rectTransform;
        private LocalizedTextReference activeReference;
        private string localizedText;
        private bool subscribed;
        private bool referenceResolved;

        /// <summary>툴팁을 붙일 기준 영역. 이 버튼 자신의 RectTransform이다.</summary>
        public RectTransform TargetRect
        {
            get
            {
                if (rectTransform == null) rectTransform = transform as RectTransform;
                return rectTransform;
            }
        }

        /// <summary>지금 표시할 문구. 컨트롤러가 <b>대기시간이 끝난 뒤에</b> 읽으므로, 그 사이에
        /// 비동기 로드가 끝나면 그대로 반영된다.
        ///
        /// 그래도 아직 비어 있으면(게임 시작 직후처럼 테이블 로드가 끝나기 전에 Hover한 경우)
        /// 이번 한 번만 동기로 받아온다 - 여기서 포기하면 툴팁이 <b>아무 말 없이</b> 안 뜨는데,
        /// 그 침묵이 "왜 툴팁이 안 나오지"로 이어지는 가장 나쁜 실패다. 동기 로드는 이미 메모리에
        /// 올라온 테이블을 기다리는 정도라 첫 Hover 한 번에만 발생한다.</summary>
        public string TooltipText
        {
            get
            {
                if (!string.IsNullOrEmpty(localizedText)) return localizedText;
                if (activeReference == null || !activeReference.HasReference) return null;

                localizedText = activeReference.GetLocalizedString();
                return localizedText;
            }
        }

        private void Awake()
        {
            rectTransform = transform as RectTransform;
        }

        private void OnEnable()
        {
            if (controller == null) controller = GetComponentInParent<HoverTooltipController>();
            if (controller == null)
            {
                Debug.LogWarning($"[HoverTooltipTrigger] '{name}': HoverTooltipController를 찾지 못해 " +
                                 "툴팁이 표시되지 않습니다 - 부모(tgl_Panel 등)에 붙이거나 Inspector에서 " +
                                 "직접 연결하세요.", this);
            }

            ResolveReference();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();

            // 메뉴가 접히거나 버튼이 꺼지는 경로 - 예약된 표시와 이미 떠 있는 툴팁을 모두 거둔다.
            if (controller != null) controller.CancelShow(this);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (controller == null) return;

            controller.RequestShow(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (controller == null) return;

            controller.CancelShow(this);
        }

        /// <summary>Inspector 지정값이 우선이고, 없으면 자식 라벨의 참조를 쓴다. 한 번 정해지면
        /// 다시 찾지 않는다 - Hover마다 계층을 뒤지지 않기 위함이다.</summary>
        private void ResolveReference()
        {
            if (referenceResolved) return;
            referenceResolved = true;

            if (tooltipText != null && tooltipText.HasReference)
            {
                activeReference = tooltipText;
                return;
            }

            var label = GetComponentInChildren<LocalizedTMPText>(true);
            if (label != null && label.TextReference != null && label.TextReference.HasReference)
            {
                activeReference = label.TextReference;
                return;
            }

            Debug.LogWarning($"[HoverTooltipTrigger] '{name}': 표시할 문구를 찾지 못했습니다 - " +
                             "Inspector의 Tooltip Text에 Category와 Key를 지정하거나, 자식 라벨에 " +
                             "Localized TMP Text를 두세요.", this);
        }

        /// <summary>구독 자체가 최초 로드를 유발하고, 이후 언어가 바뀌면 다시 호출된다
        /// (<see cref="LocalizedTMPText"/>와 같은 방식).</summary>
        private void Subscribe()
        {
            if (subscribed || activeReference == null) return;

            activeReference.StringChanged += ApplyLocalizedText;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;

            activeReference.StringChanged -= ApplyLocalizedText;
            subscribed = false;
        }

        private void ApplyLocalizedText(string value)
        {
            localizedText = value;
        }
    }
}
