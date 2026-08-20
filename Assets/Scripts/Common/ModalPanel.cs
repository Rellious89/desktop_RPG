using DesktopWindow;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// Panel_UI 아래에 뜨는 모달 패널(캐릭터 교체, 인벤토리 등)의 공통 처리. 열고 닫기, 닫기 버튼
    /// 연결, 배경 입력 차단, Windows 클릭 관통 예외 등록을 여기서 한 번만 구현하고 패널마다 같은
    /// 방식이 되도록 한다 - 패널이 늘어날 때 이 규칙이 갈라지지 않게 하기 위함이다.
    ///
    /// <b>씬 시작 시 비활성이 전제다.</b> 비활성 오브젝트는 자기 자신을 열 수 없으므로, 여는 쪽
    /// (<see cref="ModalPanelOpener"/>)이 이 컴포넌트 참조를 들고 <see cref="Open"/>을 호출한다.
    /// 비활성 오브젝트의 컴포넌트 참조는 그대로 유효하다.
    ///
    /// <b>입력 차단.</b> 패널이 열려 있는 동안 전체 화면 InputBlocker가 뒤쪽 UI(ControlDock, 전투
    /// HUD)의 클릭을 전부 먹는다. 이 오브젝트는 패널의 부모(Panel_UI) 아래에 패널 바로 뒤 형제로
    /// 만들어지고, 패널마다 이름이 달라(InputBlocker_&lt;패널 이름&gt;) 서로의 차단을 꺼버리지 않는다.
    /// <see cref="OnDisable"/>에서 반드시 꺼지므로 패널을 닫은 뒤에 입력이 계속 막히는 경로가 없다.
    ///
    /// <b>Windows 클릭 관통은 이 클래스가 다루지 않는다.</b> 이 창은 ControlDock 영역 밖이 전부 클릭
    /// 관통이라 패널 영역을 따로 등록해야 클릭이 도달하는데, 그 등록은 패널 루트에 붙인
    /// <see cref="WindowInputRegion"/>(Receive Mouse Input = On)이 자기 활성 상태에 맞춰 스스로
    /// 처리한다 - 패널과 같은 GameObject에 있으므로 패널이 열리면 등록되고 닫히면 해제된다.
    /// 여러 영역이 동시에 등록될 수 있어서 패널이 둘 이상 열려도 서로를 덮어쓰지 않는다.
    /// InputBlocker(Unity UI 이벤트 차단)와 WindowInputRegion(네이티브 마우스 관통)은 서로 다른 층을
    /// 담당하므로 둘 다 유지한다.
    /// </summary>
    public abstract class ModalPanel : MonoBehaviour
    {
        private const string InputBlockerPrefix = "InputBlocker_";

        [Header("Modal (비워두면 이름으로 자동 탐색)")]
        [Tooltip("패널을 닫는 버튼(btn_close).")]
        [SerializeField] private Button closeButton;

        [Header("Background Input")]
        [Tooltip("켜면 패널이 열린 동안 전체 화면 차단막으로 뒤쪽 UI 클릭을 모두 막는다. " +
                 "여러 패널을 동시에 쓰는 지금 구조에서는 꺼 두는 것이 기본이며(다른 패널과 " +
                 "ControlDock을 계속 쓸 수 있어야 한다), 정말로 하나만 떠야 하는 경고/확인창에서만 켠다.")]
        [SerializeField] private bool blockBackgroundInput = false;

        [Tooltip("패널이 열린 동안 뒤쪽 UI 클릭을 막는 전체 화면 오브젝트. 비워두면 패널의 부모 아래에 " +
                 "런타임으로 만든다(패널 바로 뒤에 배치된다).")]
        [SerializeField] private RectTransform inputBlocker;

        [Tooltip("런타임으로 만든 InputBlocker의 색. 기본값은 완전 투명이며, 알파를 올리면 " +
                 "패널 뒤가 어두워지는 연출로 쓸 수 있다.")]
        [SerializeField] private Color inputBlockerColor = new Color(0f, 0f, 0f, 0f);

        private bool modalReferencesResolved;
        private bool inputRegionChecked;

        /// <summary>닫기 버튼을 자동으로 찾을 때 쓰는 자식 오브젝트 이름.</summary>
        protected virtual string CloseButtonName => "btn_close";

        /// <summary>패널을 연다. 이미 열려 있으면 <b>중복으로 만들지 않고</b> 내용을 새로 고치면서
        /// 활성 패널로 올린다 - 같은 ControlDock 버튼을 다시 눌렀을 때 그 패널이 맨 앞으로 오는 경로다.
        /// 닫혀 있었다면 활성화되면서 OnEnable이 등록과 활성화를 처리한다.</summary>
        public void Open()
        {
            if (gameObject.activeSelf)
            {
                PopupPanelManager.Instance?.FocusPanel(this);
                RefreshContents();
                return;
            }

            gameObject.SetActive(true);
        }

        public void Close()
        {
            OnCloseRequested();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 닫기 버튼, ESC 또는 호출자가 <see cref="Close"/>를 사용한 정상 닫기 요청에서만 호출된다.
        /// 외부 SetActive(false), 씬 종료, 파괴 경로에서는 호출되지 않는다.
        /// </summary>
        protected virtual void OnCloseRequested()
        {
        }

        /// <summary>패널에 보이는 값을 지금 데이터 기준으로 다시 그린다. 패널이 열릴 때마다 호출되므로,
        /// 닫았다 다시 열면 언제나 최신 상태가 표시된다.</summary>
        protected abstract void RefreshContents();

        /// <summary>패널이 열릴 때 <see cref="RefreshContents"/>보다 먼저 실행된다 - 이벤트 구독,
        /// 패널 고유 버튼 연결, 구조 준비처럼 "그리기 전에 끝나야 하는 일"을 여기서 한다.</summary>
        protected virtual void OnModalOpened()
        {
        }

        /// <summary>패널이 닫힐 때 실행된다 - <see cref="OnModalOpened"/>에서 건 구독과 리스너를
        /// 여기서 짝지어 해제한다.</summary>
        protected virtual void OnModalClosed()
        {
        }

        protected virtual void OnEnable()
        {
            ResolveModalReferences();

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }

            OnModalOpened();
            RefreshContents();
            SetInputBlockerActive(true);
            WarnIfMissingInputRegion();

            // 등록과 "활성 패널로 지정"은 같은 처리다 - 새로 열린 패널은 항상 맨 앞이 된다.
            PopupPanelManager.Instance?.Register(this);
        }

        protected virtual void OnDisable()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);

            OnModalClosed();
            SetInputBlockerActive(false);

            // 닫힌 패널이 목록에 남아 ESC 대상이 되면 안 된다 - 닫기 버튼으로 닫든 코드로 닫든
            // 모두 이 경로를 지난다.
            PopupPanelManager.Instance?.Unregister(this);
        }

        protected virtual void OnDestroy()
        {
            // OnDisable이 먼저 불리는 것이 보통이지만, 파괴 경로에서도 목록에 남지 않게 한 번 더 뺀다.
            PopupPanelManager.Instance?.Unregister(this);
        }

        private void ResolveModalReferences()
        {
            if (modalReferencesResolved) return;
            modalReferencesResolved = true;

            if (closeButton == null) closeButton = FindChildComponent<Button>(CloseButtonName);
            if (closeButton == null)
            {
                Debug.LogWarning($"[{GetType().Name}] '{name}': 닫기 버튼('{CloseButtonName}')을 찾지 못했습니다.", this);
            }
        }

        private void SetInputBlockerActive(bool active)
        {
            // 끄지 않는 옵션이면 차단막 자체를 만들지 않는다 - 다중 패널에서 서로의 클릭을 막지 않기 위함이다.
            if (!blockBackgroundInput)
            {
                if (inputBlocker != null) inputBlocker.gameObject.SetActive(false);
                return;
            }

            if (active) EnsureInputBlocker();

            if (inputBlocker != null) inputBlocker.gameObject.SetActive(active);
        }

        /// <summary>Windows 빌드에서 패널이 클릭되려면 패널 루트에 WindowInputRegion이 있어야 한다.
        /// 없으면 패널이 보이기만 하고 버튼이 눌리지 않는데, 그 원인을 Play Mode 로그에서 바로 알 수
        /// 있도록 열릴 때 한 번만 확인한다 - 자동으로 붙이지는 않는다(입력을 받을 영역은 씬에서
        /// 명시적으로 정하는 값이다).</summary>
        private void WarnIfMissingInputRegion()
        {
            if (inputRegionChecked) return;
            inputRegionChecked = true;

            var region = GetComponent<WindowInputRegion>();
            if (region != null && region.ReceiveMouseInput) return;

            Debug.LogWarning($"[{GetType().Name}] '{name}': 패널 루트에 Receive Mouse Input이 켜진 " +
                             "WindowInputRegion이 없습니다 - Windows 빌드에서는 이 패널의 버튼이 클릭되지 " +
                             "않습니다(창 전체가 기본 클릭 관통이기 때문). 패널 루트에 추가하세요.", this);
        }

        private void EnsureInputBlocker()
        {
            if (inputBlocker != null) return;

            Transform parent = transform.parent;
            if (parent == null)
            {
                Debug.LogWarning($"[{GetType().Name}] '{name}': 부모가 없어 InputBlocker를 만들지 못했습니다 - " +
                                 "패널이 열려 있어도 뒤쪽 UI 클릭이 막히지 않습니다.", this);
                return;
            }

            // 패널마다 이름을 다르게 둔다 - 이름이 같으면 두 패널이 같은 오브젝트를 공유해서, 한쪽을
            // 닫을 때 다른 쪽의 입력 차단까지 꺼진다.
            string blockerName = InputBlockerPrefix + name;
            Transform existing = parent.Find(blockerName);
            if (existing != null)
            {
                inputBlocker = existing as RectTransform;
                return;
            }

            var blocker = new GameObject(blockerName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            blocker.layer = gameObject.layer;
            inputBlocker = (RectTransform)blocker.transform;
            inputBlocker.SetParent(parent, false);
            Stretch(inputBlocker);
            // 패널 바로 앞 sibling - 패널보다 먼저 그려지고, 패널 밖의 클릭만 가로챈다.
            inputBlocker.SetSiblingIndex(transform.GetSiblingIndex());

            var image = blocker.GetComponent<Image>();
            image.color = inputBlockerColor;
            image.raycastTarget = true;
        }

        // ---- 파생 클래스가 함께 쓰는 헬퍼 ----

        protected static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        protected T FindChildComponent<T>(string childName) where T : Component
        {
            Transform found = FindDeepChild(transform, childName);
            return found != null ? found.GetComponent<T>() : null;
        }

        protected static Transform FindDeepChild(Transform root, string childName)
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
