using System.Collections.Generic;
using DesktopWindow;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Common
{
    /// <summary>
    /// Panel_UI 아래의 팝업 패널(pn_*)들을 <b>여러 개 동시에</b> 다루는 관리자. 활성 순서를 하나의
    /// 목록으로 소유해서, "화면에 가장 앞에 보이는 패널"과 "ESC를 눌렀을 때 먼저 닫히는 패널"이 항상
    /// 같은 패널이 되게 한다 - 표시 순서와 닫기 순서를 별도 목록으로 관리하면 둘이 어긋난다.
    ///
    /// 목록 규칙:
    /// <code>
    /// 앞쪽   = 가장 오래전에 활성화된 패널
    /// 마지막 = 지금 활성 패널(가장 앞에 보이고, ESC로 가장 먼저 닫힌다)
    /// </code>
    ///
    /// 활성 순서 갱신(<see cref="FocusPanel"/>)이 일어나는 경우는 다음 네 가지이며, 전부 이 메서드
    /// 하나를 거친다.
    ///   - 패널이 새로 열림(ModalPanel.OnEnable)
    ///   - 이미 열린 패널의 ControlDock 버튼을 다시 누름(ModalPanel.Open)
    ///   - 패널 내부를 클릭(아래 클릭 판정)
    ///   - 패널 타이틀 드래그 시작(PanelDragHandle)
    ///
    /// <b>클릭 판정은 루트의 IPointerDownHandler로 하지 않는다.</b> 패널 안의 Button/ScrollRect 같은
    /// 자식 UI가 포인터 이벤트를 먼저 소비하면 루트 핸들러는 호출되지 않기 때문이다. 대신 마우스 버튼이
    /// 눌린 프레임에 UI 레이캐스트를 직접 쏘고, 가장 위에 맞은 오브젝트에서 부모 쪽으로 올라가며
    /// ModalPanel을 찾아 활성화한다 - 자식 버튼/리스트/슬롯을 눌러도, 뒤쪽 패널의 노출된 영역을 눌러도
    /// 똑같이 그 패널이 앞으로 온다.
    ///
    /// <b>ESC는 이 앱에 포커스가 있을 때만 처리한다.</b> 전역 키보드 후크는 다른 프로그램에서 누른 키도
    /// 잡아내므로, 후크 신호만 보고 닫으면 다른 앱에서 ESC를 눌러도 패널이 닫힌다. 포커스 판정은
    /// <see cref="TransparentWindowController.HasWindowFocus"/>(네이티브 포그라운드 창 비교)를 쓴다.
    /// 키 자체는 공격 제외 키 테이블에 등록돼 있어서, 포커스가 없을 때 눌러도 공격으로도 쓰이지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PopupPanelManager : MonoBehaviour
    {
        [Tooltip("가장 최근에 활성화된 패널을 하나 닫는 키. 이 키는 공격 입력에서도 제외되어 있어야 " +
                 "한다(Attack Input Exclusion Table).")]
        [SerializeField] private KeyCode closeTopPanelKey = KeyCode.Escape;

        public static PopupPanelManager Instance { get; private set; }

        // 활성 순서. 마지막 원소가 현재 활성 패널이다.
        private readonly List<ModalPanel> activePanels = new List<ModalPanel>();

        // 클릭 판정용 재사용 버퍼(매 클릭마다 할당하지 않는다).
        private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
        private PointerEventData pointerEventData;

        /// <summary>지금 활성 패널(가장 앞에 보이고 ESC로 먼저 닫히는 패널). 열린 패널이 없으면 null.</summary>
        public ModalPanel TopPanel => activePanels.Count > 0 ? activePanels[activePanels.Count - 1] : null;

        public int ActivePanelCount => activePanels.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[PopupPanelManager] 씬에 PopupPanelManager가 이미 있습니다. 이 인스턴스는 무시합니다.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            UpdateFocusFromClick();
            UpdateCloseTopPanel();
        }

        // ---- 등록 / 활성 순서 ----

        /// <summary>패널이 열릴 때 호출된다. 등록과 활성화가 같은 처리라 <see cref="FocusPanel"/>로
        /// 합쳐 두었다 - 새로 열린 패널은 항상 맨 앞이자 활성 패널이 된다.</summary>
        public void Register(ModalPanel panel)
        {
            FocusPanel(panel);
        }

        /// <summary>패널이 닫히거나 파괴될 때 호출된다. 목록에 남아 있으면 비활성 패널이 ESC 대상이
        /// 되므로 반드시 빠져야 한다 - ModalPanel의 OnDisable/OnDestroy 양쪽에서 호출한다.</summary>
        public void Unregister(ModalPanel panel)
        {
            if (panel == null)
            {
                // 파괴된 항목이 섞여 있을 수 있으니 이 기회에 정리한다.
                activePanels.RemoveAll(p => p == null);
                return;
            }

            activePanels.Remove(panel);
        }

        /// <summary>
        /// 패널 활성화를 처리하는 <b>유일한</b> 진입점. 순서는 (1) 목록에서 기존 위치 제거,
        /// (2) 목록 마지막에 추가, (3) 시각적으로 맨 앞으로 이동이다. 표시 순서를 sibling 순서로
        /// 바꾸므로 목록과 화면이 같은 순서를 공유한다.
        ///
        /// SetAsLastSibling은 패널의 부모(Panel_UI) 안에서만 순서를 바꾸므로, 다른 시스템용 UI
        /// (ToastLayer, HUD 등 Canvas의 다른 자식)와의 상대 순서는 영향을 받지 않는다.
        /// </summary>
        public void FocusPanel(ModalPanel panel)
        {
            if (panel == null) return;

            activePanels.Remove(panel);
            activePanels.Add(panel);

            panel.transform.SetAsLastSibling();
        }

        // ---- 클릭으로 활성 패널 지정 ----

        /// <summary>마우스 버튼이 눌린 프레임에 UI 레이캐스트를 쏴서, 가장 위에 맞은 오브젝트가 속한
        /// 패널을 활성화한다. 자식 UI가 이벤트를 소비했는지와 무관하게 판정되는 것이 핵심이다.
        /// Input.GetMouseButtonDown은 이 창이 클릭을 실제로 받았을 때만 true이므로, 클릭 관통 영역의
        /// 입력에는 반응하지 않는다.</summary>
        private void UpdateFocusFromClick()
        {
            if (activePanels.Count == 0) return;
            if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1)) return;

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return;

            if (pointerEventData == null) pointerEventData = new PointerEventData(eventSystem);
            pointerEventData.Reset();
            pointerEventData.position = Input.mousePosition;

            raycastResults.Clear();
            eventSystem.RaycastAll(pointerEventData, raycastResults);
            if (raycastResults.Count == 0) return;

            // RaycastAll의 결과는 앞에 있는 것부터 정렬되어 있다 - 화면상 가장 앞의 UI만 본다.
            GameObject hit = raycastResults[0].gameObject;
            if (hit == null) return;

            ModalPanel panel = hit.GetComponentInParent<ModalPanel>(true);
            if (panel == null) return;
            if (panel == TopPanel) return; // 이미 활성 패널이면 순서를 건드리지 않는다.

            FocusPanel(panel);
        }

        // ---- ESC로 닫기 ----

        private void UpdateCloseTopPanel()
        {
            if (closeTopPanelKey == KeyCode.None) return;
            if (!GlobalKeyboardHook.WasExcludedKeyDownThisFrame(closeTopPanelKey)) return;

            // 다른 프로그램을 쓰는 중에 누른 ESC로 이 앱의 패널이 닫혀서는 안 된다.
            if (!TransparentWindowController.HasWindowFocus) return;

            CloseTopPanel();
        }

        /// <summary>지금 활성 패널 하나만 닫는다. 열린 패널이 없으면 아무 일도 하지 않는다.
        /// 후크가 자동 반복을 걸러 주므로(제외 키 전용) 키를 길게 눌러도 한 번만 실행된다.</summary>
        public bool CloseTopPanel()
        {
            // 파괴됐거나 이미 비활성인 항목이 남아 있으면 그것부터 걷어낸다 - 그런 패널이 ESC 대상이
            // 되면 "눌렀는데 아무 것도 닫히지 않는" 것처럼 보인다.
            for (int i = activePanels.Count - 1; i >= 0; i--)
            {
                ModalPanel panel = activePanels[i];
                if (panel == null || !panel.gameObject.activeSelf)
                {
                    activePanels.RemoveAt(i);
                    continue;
                }

                panel.Close(); // Close -> OnDisable -> Unregister로 목록에서 빠진다.
                return true;
            }

            return false;
        }
    }
}
