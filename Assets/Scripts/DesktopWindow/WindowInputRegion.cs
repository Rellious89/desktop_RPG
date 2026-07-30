using UnityEngine;

namespace DesktopWindow
{
    /// <summary>
    /// 이 RectTransform 영역이 <b>Windows 투명 창의 네이티브 마우스 입력을 받을지</b> 지정하는 컴포넌트.
    /// 이 창은 기본적으로 전체가 클릭 관통(WS_EX_TRANSPARENT)이라, 등록된 영역 밖에서는 클릭이 우리
    /// 창에 도달하지 않고 바탕화면/다른 앱으로 넘어간다.
    ///
    /// <b>Unity UI의 클릭 처리와는 다른 층이다.</b> Unity UI 내부의 Raycast/Button/InputBlocker는
    /// "창 안에 들어온 입력을 어느 UI가 먹을지"를 정하고, 이 컴포넌트는 "그 입력이 애초에 창까지
    /// 들어오는지"를 정한다. 둘은 대체 관계가 아니므로 함께 쓴다.
    ///
    /// <b>자식은 부모 영역을 그대로 따른다.</b> 판정은 화면 좌표 사각형 하나로만 하므로, 부모 UI에
    /// 하나만 붙이고 Receive Mouse Input을 켜면 그 사각형 안에 있는 자식(닫기 버튼, 리스트 항목,
    /// 내부 버튼 등)이 모두 함께 입력을 받는다 - 자식마다 붙일 필요가 없다. 자식 영역만 따로 켜고
    /// 싶을 때는 그 자식에 별도로 하나 더 붙이면 된다(등록은 여러 개가 동시에 유효하다).
    ///
    /// 등록/해제는 이 컴포넌트의 활성 상태에 그대로 묶인다 - 컴포넌트나 GameObject가 꺼지면(패널이
    /// 닫히면) 자동으로 해제되어 그 자리는 다시 마우스 관통 상태가 된다.
    ///
    /// Windows 빌드가 아닌 환경에서는 등록만 이루어지고 아무 효과가 없다(클릭 관통 자체가 없다).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class WindowInputRegion : MonoBehaviour
    {
        [Tooltip("끄면(기본값) 이 영역은 마우스 입력을 통과시킨다 - 클릭이 바탕화면/다른 앱으로 넘어간다. " +
                 "켜면 이 영역에서 마우스 입력을 받는다(클릭 관통을 막는다).")]
        [SerializeField] private bool receiveMouseInput = false;

        private RectTransform rectTransform;
        private bool registered;

        /// <summary>네이티브 판정에 쓸 영역. 컨트롤러가 <b>메인 스레드에서만</b> 읽어 화면 좌표로
        /// 변환하고, 훅 스레드는 그 결과 사각형만 본다.</summary>
        public RectTransform RegionRect
        {
            get
            {
                if (rectTransform == null) rectTransform = transform as RectTransform;
                return rectTransform;
            }
        }

        /// <summary>지금 실제로 입력을 받아야 하는 영역인지. 설정이 켜져 있고 계층상 활성일 때만 true다.</summary>
        public bool IsReceivingMouseInput => receiveMouseInput && isActiveAndEnabled;

        /// <summary>런타임에 입력 수신 여부를 바꾼다(등록 자체는 유지되고 판정에서만 제외/포함된다).</summary>
        public bool ReceiveMouseInput
        {
            get => receiveMouseInput;
            set => receiveMouseInput = value;
        }

        private void Awake()
        {
            rectTransform = transform as RectTransform;
        }

        private void OnEnable()
        {
            TryRegister();
        }

        /// <summary>컨트롤러가 이 컴포넌트보다 늦게 초기화된 경우를 대비해 한 번 더 시도한다 - Start는
        /// 모든 Awake 이후에 실행된다. 등록은 중복되지 않는다(컨트롤러가 이미 있으면 무시한다).</summary>
        private void Start()
        {
            TryRegister();
        }

        private void OnDisable()
        {
            if (!registered) return;

            TransparentWindowController.Instance?.UnregisterInputRegion(this);
            registered = false;
        }

        private void TryRegister()
        {
            if (registered) return;

            TransparentWindowController controller = TransparentWindowController.Instance;
            if (controller == null) return;

            controller.RegisterInputRegion(this);
            registered = true;
        }
    }
}
