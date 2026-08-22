using Field;
using UnityEngine;
using UnityEngine.UI;

namespace Building
{
    /// <summary>
    /// 마을의 건물 슬롯 위에 떠 있는 상호작용 UI(btn_Build_Inn)를 <b>월드 앵커에 붙여 두는</b> 컨트롤러.
    /// 하는 일은 셋뿐이다 - (1) 앵커의 월드 좌표를 화면 좌표로 옮겨 버튼 위치를 맞추고, (2) 보여야 할
    /// 상황인지 판정해 상호작용 루트를 켜고 끄고, (3) 버튼이 눌리면 건물 정의 하나를 팝업에 넘겨 연다.
    ///
    /// <b>여기에 건설은 없다.</b> 비용을 평가하지도 내지도 않고 저장도 하지 않는다 - 클릭은
    /// <see cref="BuildingPopupPanel.Bind"/> + <see cref="Common.ModalPanel.Open"/>까지다.
    ///
    /// <b>이 컴포넌트는 자기가 끄는 오브젝트 안에 있으면 안 된다.</b> 상호작용 루트를 끄는 순간 그
    /// 안의 컴포넌트는 Update를 받지 못하므로, 한 번 숨기면 다시 켜 줄 주체가 사라진다. 그래서 이
    /// 컴포넌트는 언제나 켜져 있는 관리자 오브젝트(FieldSystem)에 붙이고, 상호작용 루트는 <b>참조로만</b>
    /// 다룬다.
    ///
    /// <b>위치 계산은 LateUpdate에서 한다.</b> 스테이지(<see cref="Common.StageVisualRootController"/>는
    /// 실행 순서 -100)와 캐릭터가 Update에서 움직인 <b>뒤</b>의 좌표를 써야, 버튼이 한 프레임 늦게
    /// 따라오는 흔들림이 생기지 않는다.
    ///
    /// <b>화면 좌표 변환은 Canvas의 렌더 모드를 따른다.</b> Screen Space - Overlay 캔버스는
    /// <see cref="RectTransformUtility.ScreenPointToLocalPointInRectangle"/>에 <b>null 카메라</b>를
    /// 넘겨야 하고(캔버스의 worldCamera를 넘기면 좌표가 어긋난다), 그 외 모드에서는 캔버스가 지정한
    /// 카메라를 넘긴다.
    ///
    /// <b>연결은 전부 Inspector 명시 참조다.</b> Find/이름 탐색/GetChild를 하나도 쓰지 않는다 - 씬 계층이
    /// 바뀌어도 이 코드가 조용히 다른 오브젝트를 잡지 않아야 하고, 무엇이 빠졌는지는 실행 즉시 로그로
    /// 드러나야 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class TownBuildingInteractionController : MonoBehaviour
    {
        [Header("Field (필수)")]
        [Tooltip("모드 전환의 단일 소유자. '지금 마을인가'는 이 매니저만 알고 여기서는 읽기만 한다 - " +
                 "모드를 직접 판정하거나 바꾸지 않는다.")]
        [SerializeField] private FieldModeManager fieldModeManager;

        [Tooltip("전환 연출의 소유자. 연출이 도는 동안에는 상호작용 UI를 숨기고 열린 팝업을 닫는다. " +
                 "비워두면 연출 판정 없이 모드만 본다(연출은 있으면 좋은 것이지 전제 조건이 아니다).")]
        [SerializeField] private FieldTransitionSequencer transitionSequencer;

        [Header("Anchor")]
        [Tooltip("월드 좌표를 화면으로 옮길 때 쓰는 스테이지 카메라(Main Camera).")]
        [SerializeField] private Camera stageCamera;

        [Tooltip("버튼이 따라다닐 월드 기준점(TownFieldRoot/BuildingRoot/InnSlot/UIAnchor). " +
                 "이 오브젝트의 위치만 읽고 <b>절대 옮기지 않는다</b>.")]
        [SerializeField] private Transform uiAnchor;

        [Header("Interaction UI")]
        [Tooltip("켜고 끌 상호작용 루트(Canvas/TownInteractionLayer). 이 컴포넌트는 이 오브젝트 " +
                 "<b>바깥</b>에 있어야 한다 - 안에 있으면 한 번 끈 뒤 스스로 다시 켤 수 없다.")]
        [SerializeField] private GameObject interactionRoot;

        [Tooltip("버튼 위치를 계산할 기준 사각형(TownInteractionLayer/Interaction). btn_Build_Inn의 " +
                 "부모여야 한다 - 화면 좌표를 이 사각형의 로컬 좌표로 바꿔 anchoredPosition에 넣는다.")]
        [SerializeField] private RectTransform interactionParent;

        [Tooltip("건설 버튼(Interaction/btn_Build_Inn). 위치를 옮기고 클릭 리스너를 런타임으로 건다 - " +
                 "버튼의 OnClick에 영구 호출을 저작하지 않는다.")]
        [SerializeField] private Button buildButton;

        [Tooltip("여관 입장 버튼(Interaction/btn_Open_Inn). 이번 단계에서는 <b>언제나 꺼진 채로</b> " +
                 "둔다 - 건물이 실제로 지어지는 다음 단계에서 켜진다.")]
        [SerializeField] private GameObject openInnButton;

        [Header("Popup")]
        [Tooltip("건설 버튼을 누르면 열릴 팝업(Dialog_UI/dialog_BuildingPopup). 시작 시 꺼져 있어야 " +
                 "하며, 꺼져 있어도 이 참조는 유효하다.")]
        [SerializeField] private BuildingPopupPanel buildingPopup;

        [Tooltip("건설 버튼이 팝업에 넘길 건물 정의(Generated/TableData/Building/Building_1). " +
                 "이 참조 하나가 '이 버튼이 어떤 건물인가'를 정한다.")]
        [SerializeField] private BuildingDefinition building;

        private RectTransform buildButtonRect;
        private Canvas interactionCanvas;
        private bool referencesValidated;

        /// <summary>마지막 판정에서 상호작용 UI가 보여야 했는지 여부. 읽기 전용 진단값이며 이 값을
        /// 바꿔서 표시를 바꿀 수는 없다 - 표시를 정하는 것은 언제나 <see cref="UpdateInteraction"/>이다.</summary>
        public bool IsInteractionVisible { get; private set; }

        private void OnEnable()
        {
            ValidateReferences();

            if (buildButton != null)
            {
                // 지웠다 다시 건다 - 껐다 켜도 리스너가 쌓이지 않는다.
                buildButton.onClick.RemoveListener(HandleBuildClicked);
                buildButton.onClick.AddListener(HandleBuildClicked);
            }

            UpdateInteraction();
        }

        private void OnDisable()
        {
            if (buildButton != null) buildButton.onClick.RemoveListener(HandleBuildClicked);
        }

        /// <summary>스테이지와 캐릭터가 모두 움직인 뒤에 위치를 맞춘다.</summary>
        private void LateUpdate()
        {
            UpdateInteraction();
        }

        /// <summary>
        /// 한 프레임분의 판정과 반영을 한 번에 한다. 순서가 중요하다:
        ///   1. 마을이고 전환 연출이 멈춰 있는가(<see cref="IsTownReady"/>) - 아니면 <b>열린 팝업을 닫는다</b>.
        ///   2. 앵커가 카메라 앞에 있고 화면 안에 있는가 - 아니면 숨기기만 한다(팝업은 그대로 둔다).
        ///   3. 보여야 하면 버튼 위치를 옮기고 루트를 켠다.
        ///
        /// 팝업을 닫는 조건과 버튼을 숨기는 조건을 <b>일부러 다르게</b> 두었다 - 마을 안에서 앵커가
        /// 화면 밖으로 잠깐 밀려났다고 사용자가 보고 있던 정보 창을 닫아 버리면 안 되기 때문이다.
        /// </summary>
        private void UpdateInteraction()
        {
            KeepOpenInnButtonInactive();

            bool townReady = IsTownReady();
            if (!townReady) CloseOpenPopup();

            bool visible = false;
            if (townReady && buildButtonRect != null && TryProjectAnchor(out Vector2 localPoint))
            {
                buildButtonRect.anchoredPosition = localPoint;
                visible = true;
            }

            SetInteractionVisible(visible);
        }

        /// <summary>마을이면서 전환 연출이 돌지 않는 상태인지. 매니저가 없으면 판정할 근거가 없으므로
        /// false다(모드를 여기서 대신 정하지 않는다).</summary>
        private bool IsTownReady()
        {
            if (fieldModeManager == null) return false;
            if (fieldModeManager.CurrentMode != FieldMode.Town) return false;
            if (transitionSequencer != null && transitionSequencer.IsPlaying) return false;
            return true;
        }

        private bool TryProjectAnchor(out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (stageCamera == null || uiAnchor == null || interactionParent == null) return false;

            // 화면 범위는 <b>카메라의 픽셀 사각형</b>으로 본다 - WorldToScreenPoint가 내놓는 좌표가
            // 바로 그 공간이기 때문이다. 스테이지 카메라의 Viewport는 언제나 전체 화면으로 고정되므로
            // (StageVisualRootController) 실제 값은 Screen.width/height와 같고, 시험에서는 카메라
            // 하나만 만들어 그대로 확인할 수 있다.
            return TryProjectAnchor(
                stageCamera, uiAnchor.position, interactionParent, ResolveEventCamera(),
                stageCamera.pixelWidth, stageCamera.pixelHeight, out localPoint);
        }

        /// <summary>
        /// 월드 위치를 상호작용 사각형의 로컬 좌표로 옮긴다. 씬 상태를 하나도 읽지 않으므로
        /// EditMode 테스트에서 카메라와 사각형만 만들어 그대로 확인할 수 있다.
        ///
        /// false를 돌려주는 경우는 둘이다 - 앵커가 <b>카메라 뒤</b>에 있거나(z가 0 이하면 화면 좌표가
        /// 뒤집혀 반대편에 그려진다), 화면 사각형 <b>밖</b>에 있는 경우. 둘 다 "그 자리에 UI를 띄울
        /// 수 없다"는 뜻이므로 숨김으로 이어진다.
        /// </summary>
        public static bool TryProjectAnchor(
            Camera camera, Vector3 worldPosition, RectTransform parent, Camera eventCamera,
            float screenWidth, float screenHeight, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (camera == null || parent == null) return false;

            Vector3 screenPoint = camera.WorldToScreenPoint(worldPosition);
            if (screenPoint.z <= 0f) return false;
            if (screenPoint.x < 0f || screenPoint.x > screenWidth) return false;
            if (screenPoint.y < 0f || screenPoint.y > screenHeight) return false;

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, screenPoint, eventCamera, out localPoint);
        }

        /// <summary>
        /// 화면 좌표를 로컬 좌표로 바꿀 때 넘길 카메라. Screen Space - Overlay 캔버스는 <b>반드시
        /// null</b>이어야 하며(캔버스의 worldCamera를 넘기면 좌표가 어긋난다), 그 외 모드에서는 캔버스가
        /// 지정한 카메라를 쓴다.
        /// </summary>
        public static Camera ResolveEventCamera(Canvas canvas)
        {
            if (canvas == null) return null;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        private Camera ResolveEventCamera()
        {
            if (interactionCanvas == null && interactionParent != null)
            {
                interactionCanvas = interactionParent.GetComponentInParent<Canvas>();
            }
            return ResolveEventCamera(interactionCanvas);
        }

        private void SetInteractionVisible(bool visible)
        {
            IsInteractionVisible = visible;
            if (interactionRoot == null) return;
            if (interactionRoot.activeSelf != visible) interactionRoot.SetActive(visible);
        }

        /// <summary>여관 입장 버튼은 이번 단계에서 켜지지 않는다. 저작 실수로 켜져 있어도 여기서
        /// 한 번 되돌린다 - "지어지지 않은 건물에 들어가는 버튼"이 보이는 경로를 만들지 않는다.</summary>
        private void KeepOpenInnButtonInactive()
        {
            if (openInnButton == null) return;
            if (openInnButton.activeSelf) openInnButton.SetActive(false);
        }

        /// <summary>열려 있는 건물 팝업을 <b>평소의 닫기 경로</b>로 닫는다 - 오브젝트를 직접 끄지 않는다
        /// (그러면 PopupPanelManager의 ESC 목록 정리와 구독 해제가 함께 지나가지 않는다).</summary>
        private void CloseOpenPopup()
        {
            if (buildingPopup == null) return;
            if (!buildingPopup.gameObject.activeSelf) return;

            buildingPopup.Close();
        }

        /// <summary>건설 버튼 클릭. <b>정의 하나를 넘기고 여는 것이 전부</b>다 - 비용을 확인하지도,
        /// 내지도, 저장하지도 않는다.</summary>
        private void HandleBuildClicked()
        {
            if (buildingPopup == null)
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': 건물 팝업이 연결되지 않아 " +
                               "건설 버튼이 아무 일도 하지 않습니다.", this);
                return;
            }

            if (building == null)
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': 건물 정의가 연결되지 않아 " +
                               "팝업에 보여 줄 내용이 없습니다 - Building_1을 연결하세요.", this);
                return;
            }

            buildingPopup.Bind(building);
            buildingPopup.Open();
        }

        private void ValidateReferences()
        {
            if (referencesValidated) return;
            referencesValidated = true;

            if (buildButton != null) buildButtonRect = buildButton.transform as RectTransform;

            if (fieldModeManager == null)
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': FieldModeManager가 연결되지 " +
                               "않아 상호작용 UI가 계속 숨겨집니다.", this);
            }
            if (stageCamera == null)
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': 스테이지 카메라가 연결되지 " +
                               "않아 버튼 위치를 계산할 수 없습니다.", this);
            }
            if (uiAnchor == null)
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': UIAnchor가 연결되지 않아 " +
                               "버튼이 따라갈 기준점이 없습니다.", this);
            }
            if (interactionRoot == null)
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': 상호작용 루트" +
                               "(TownInteractionLayer)가 연결되지 않았습니다.", this);
            }
            else if (interactionRoot.transform.IsChildOf(transform) || transform.IsChildOf(interactionRoot.transform))
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': 이 컴포넌트가 자신이 끄는 " +
                               "상호작용 루트 안에 있습니다 - 한 번 숨기면 다시 켤 수 없으므로 항상 켜져 있는 " +
                               "관리자 오브젝트로 옮기세요.", this);
            }
            if (interactionParent == null)
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': 버튼 기준 사각형" +
                               "(Interaction)이 연결되지 않았습니다.", this);
            }
            if (buildButton == null)
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': 건설 버튼(btn_Build_Inn)이 " +
                               "연결되지 않았습니다.", this);
            }
            if (buildingPopup == null)
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': 건물 팝업" +
                               "(dialog_BuildingPopup)이 연결되지 않았습니다.", this);
            }
            if (building == null)
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': 건물 정의(Building_1)가 " +
                               "연결되지 않았습니다.", this);
            }
        }
    }
}
