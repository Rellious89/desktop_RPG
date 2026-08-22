using System;
using Common;
using Field;
using Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Building
{
    /// <summary>
    /// 마을의 건물 슬롯 위에 떠 있는 상호작용 UI(btn_Build_Inn / pn_ConstructionTimer / btn_Open_Inn)를
    /// <b>월드 앵커에 붙여 두는</b> 컨트롤러. 하는 일은 여섯이다 - (1) 앵커의 월드 좌표를 화면 좌표로
    /// 옮겨 건설 버튼과 타이머의 위치를 맞추고, (2) 보여야 할 상황인지 판정해 상호작용 루트를 켜고 끄고,
    /// (3) 버튼이 눌리면 건물 정의 하나를 팝업에 넘겨 열고, (4) <b>건설 단계에 따라 셋 중 하나만
    /// 보이게 하고</b>, (5) 짓는 중이면 남은 시간을 1초에 한 번 고쳐 쓰고, (6) 건설이 시작되거나
    /// 완성되면 안내 토스트를 <b>한 번씩</b> 띄운다.
    ///
    /// <b>보이는 것은 언제나 셋 중 하나뿐이다.</b> 아직 안 지었으면 건설 버튼만, 짓는 중이면 타이머만,
    /// 다 지었으면 입장 버튼만 보인다. 단계의 근거는 저장 기록과 시각 비교 하나이며
    /// (<see cref="BuildingConstructionService.GetStatus(string)"/>), 그래서 앱을 꺼 둔 동안 흐른
    /// 시간이 그대로 반영된다. 완성 시각을 읽을 수 없는 손상된 기록에서는 셋 다 보이지 않는다 -
    /// <b>건설 버튼은 그때도 돌아오지 않는다</b>(기록이 있다는 것 자체가 "이미 시작했다"이다).
    ///
    /// <b>남은 시간은 저장하지 않는다.</b> 매 프레임 다시 계산할 뿐이고, 파일에 쓰는 것은 완성이
    /// 확정되는 <b>단 한 번</b>뿐이다(<see cref="BuildingConstructionService.TryNotifyCompletion(string)"/>).
    /// 계산에 엔진 시간이 없으므로 <see cref="Time.timeScale"/>이 0이어도 남은 시간은 계속 줄어들며,
    /// 타이머 연출도 Unscaled Time으로 돌린다.
    ///
    /// <b>건설의 규칙은 여기에 없다.</b> 비용을 평가하거나 내는 코드도, 저장을 부르는 코드도 한 줄도
    /// 없다 - 그 전부는 <see cref="BuildingConstructionService"/> 하나가 갖고 있고, 이 컨트롤러는
    /// 씬의 <see cref="InventoryManager"/>로 그 서비스를 <b>만들어 팝업에 건네</b> 줄 뿐이다.
    /// 팝업이 <b>어디에</b> 뜨는지도 여기서 정하지 않는다 - 기준이 될 버튼 사각형만 넘기고, 자리
    /// 계산은 팝업이 혼자 한다.
    ///
    /// <b>이미 시작된 건물의 버튼은 다시 켜지지 않는다.</b> 판정의 근거는 저장 문서의 건설 기록
    /// 하나이며(<see cref="BuildingConstructionService.HasConstruction(string)"/>), 그래서 앱을 껐다
    /// 켜도, 완성 시각이 지나도 건설 버튼은 계속 숨은 채로 남는다 - 기록이 있다는 것 자체가 "이미
    /// 시작했다"이기 때문이다.
    ///
    /// <b>완성 안내는 한 번뿐이다.</b> 완성됐다는 사실은 시각 비교라 켤 때마다 참이지만, 안내를
    /// 띄웠다는 사실은 저장 기록에 남는다
    /// (<see cref="Common.BuildingConstructionSaveState.completionNotified"/>) - 그래서 앱을 다시
    /// 켜도 같은 안내가 되풀이되지 않는다. 표식을 남기지 못했으면(저장 실패) 안내도 띄우지 않고
    /// 다음 갱신에서 다시 시도한다 - "안내했다고 적혔는데 화면은 못 본" 상태를 만들지 않는다.
    ///
    /// <b>안내 문구를 코드가 짓지 않는다.</b> 토스트에 실을 문장은 01_UI 표(시작 42번, 완성 43번)에서
    /// 오며, 아직 도착하지 않았으면 <b>토스트를 띄우지 않는다</b> - 대체 문구를 코드가 지어내면 표를
    /// 고쳐도 화면이 바뀌지 않는 자리가 생긴다.
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
                 "버튼의 OnClick에 영구 호출을 저작하지 않는다. 이 버튼의 사각형이 팝업이 뜰 자리의 " +
                 "기준이 된다.")]
        [SerializeField] private Button buildButton;

        [Tooltip("여관 입장 버튼(Interaction/btn_Open_Inn). 건설이 완성된 뒤에만 켜진다. 이번 " +
                 "단계에서는 눌러도 하는 일이 없다 - 무엇이 열릴지는 다음 단계의 몫이다.")]
        [SerializeField] private GameObject openInnButton;

        [Tooltip("건설 타이머 묶음(Interaction/pn_ConstructionTimer). 짓는 중에만 켜지며, 켜지는 " +
                 "자리는 건설 버튼과 같은 월드 앵커다. 시작 시 꺼져 있어야 한다.")]
        [SerializeField] private GameObject constructionTimerRoot;

        [Tooltip("남은 시간 텍스트(pn_ConstructionTimer/lb_ConstructionTimer). 표시는 HH:mm:ss이며 " +
                 "번역 대상이 아니다(숫자와 콜론뿐).")]
        [SerializeField] private TextMeshProUGUI constructionTimerText;

        [Tooltip("타이머 회전 연출(pn_ConstructionTimer/ani_Timer)의 Animator. 이 컴포넌트가 " +
                 "Update Mode를 Unscaled Time으로 맞춘다 - 화면이 멈춰도(timeScale 0) 연출은 돈다.")]
        [SerializeField] private Animator constructionTimerAnimator;

        [Header("Popup")]
        [Tooltip("건설 버튼을 누르면 열릴 팝업(Dialog_UI/dialog_BuildingPopup). 시작 시 꺼져 있어야 " +
                 "하며, 꺼져 있어도 이 참조는 유효하다.")]
        [SerializeField] private BuildingPopupPanel buildingPopup;

        [Tooltip("건설 버튼이 팝업에 넘길 건물 정의(Generated/TableData/Building/Building_1). " +
                 "이 참조 하나가 '이 버튼이 어떤 건물인가'를 정한다.")]
        [SerializeField] private BuildingDefinition building;

        [Header("Construction")]
        [Tooltip("비용의 소유자(씬의 InventoryManager). 이 참조로 건설 서비스를 만든다 - 비워두면 " +
                 "건설을 시작할 수 없고 확인 버튼이 아무 일도 하지 않는다. Find/이름 탐색을 쓰지 " +
                 "않는다는 이 컴포넌트의 규칙 그대로, 정적 Instance로 대신 찾지도 않는다.")]
        [SerializeField] private InventoryManager inventoryManager;

        [Tooltip("건설을 시작했을 때 띄울 안내 문구(01_UI / 42). 비워두거나 번역이 아직 도착하지 " +
                 "않았으면 토스트를 띄우지 않는다 - 코드가 대체 문구를 지어내지 않는다.")]
        [SerializeField] private LocalizedTextReference constructionStartedMessage = new LocalizedTextReference();

        [Tooltip("건설이 끝났을 때 띄울 안내 문구(01_UI / 43). 시작 안내와 같은 규칙이다 - 비워두거나 " +
                 "번역이 아직 도착하지 않았으면 토스트를 띄우지 않는다.")]
        [SerializeField] private LocalizedTextReference constructionCompletedMessage = new LocalizedTextReference();

        private RectTransform buildButtonRect;
        private RectTransform constructionTimerRect;
        private Canvas interactionCanvas;
        private bool referencesValidated;

        /// <summary>아직 한 번도 쓰지 않았음을 뜻하는 표시 초 수. 0초는 실제로 쓰는 값이라
        /// (완성 직전) 구분이 필요하다.</summary>
        private const long UnwrittenSeconds = -1L;

        /// <summary>마지막으로 텍스트에 써 넣은 초 수. 같은 값이면 문자열을 다시 만들지 않는다 -
        /// 매 프레임 갱신하지만 실제 쓰기는 <b>1초에 한 번</b>이 되게 한다(회복소 슬롯과 같은 규칙).</summary>
        private long lastDisplayedSeconds = UnwrittenSeconds;

        /// <summary>건설 규칙의 소유자. 이 컨트롤러가 만들고, 팝업에 건네주고, 시작 신호를 받는다.
        /// 인벤토리가 없으면 만들지 못하므로 null이며, 그때는 팝업의 확인이 아무 일도 하지 않는다.</summary>
        private BuildingConstructionService constructionService;

        /// <summary>서비스에 넘길 시계. 실제 실행에서는 언제나 UTC 시계이며, 시험만 고정된 순간을
        /// 끼워 넣어 남은 시간과 완성 경계를 글자 그대로 확인한다 - 그래서 서비스를 다시 만들지 않고
        /// 이 함수 하나만 갈아 끼울 수 있게 <b>한 겹</b>을 두었다.</summary>
        private Func<DateTime> utcNowProvider = () => DateTime.UtcNow;

        /// <summary>지금 구독 중인 안내 문구 참조. 걸어 둔 대상을 그대로 들고 있다가 짝지어 해제한다
        /// (팝업이 번역 구독을 다루는 방식과 같다).</summary>
        private LocalizedTextReference boundStartedMessage;

        /// <summary>도착한 안내 문구. null은 "아직 오지 않았다"는 뜻이며 빈 문자열과 구분한다.</summary>
        private string localizedStartedMessage;

        /// <summary>완성 안내 문구의 같은 짝. 시작 안내와 <b>따로</b> 구독한다 - 한쪽이 비어 있어도
        /// 다른 쪽은 그대로 동작해야 한다.</summary>
        private LocalizedTextReference boundCompletedMessage;

        private string localizedCompletedMessage;

        private bool missingToastWarned;

        private bool missingCompletedToastWarned;

        /// <summary>마지막 판정에서 상호작용 UI가 보여야 했는지 여부. 읽기 전용 진단값이며 이 값을
        /// 바꿔서 표시를 바꿀 수는 없다 - 표시를 정하는 것은 언제나 <see cref="UpdateInteraction"/>이다.</summary>
        public bool IsInteractionVisible { get; private set; }

        private void OnEnable()
        {
            ValidateReferences();
            ApplyTimerAnimatorUpdateMode();
            EnsureConstructionService();
            SubscribeStartedMessage();
            SubscribeCompletedMessage();

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

            UnsubscribeStartedMessage();
            UnsubscribeCompletedMessage();
            DetachConstructionService();
        }

        /// <summary>타이머 연출을 <see cref="AnimatorUpdateMode.UnscaledTime"/>으로 맞춘다. 저작에서
        /// 이미 그렇게 지정돼 있어도 여기서 한 번 더 확인한다 - 이 성질은 "화면이 멈춰도 타이머는
        /// 돈다"라는 규칙이지 저작 취향이 아니다.</summary>
        private void ApplyTimerAnimatorUpdateMode()
        {
            if (constructionTimerAnimator == null) return;
            if (constructionTimerAnimator.updateMode == AnimatorUpdateMode.UnscaledTime) return;

            constructionTimerAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
        }

        // ---- 건설 서비스 ----

        /// <summary>
        /// 건설 서비스를 <b>한 번만</b> 만든다. 만들 수 있는 조건은 씬의 인벤토리 하나뿐이며, 없으면
        /// 만들지 않는다(그 사실은 <see cref="ValidateReferences"/>가 이미 오류로 남겼다).
        ///
        /// 저장 문서와 저장 함수, 시계는 여기서 <see cref="SaveSystem"/>과 <see cref="DateTime.UtcNow"/>에
        /// 이어 붙인다 - 서비스 자신은 엔진도 파일도 모르는 순수 객체로 두어야 시험이 고정된 시계와
        /// 가짜 저장으로 그 규칙을 그대로 돌려 볼 수 있다(<see cref="Recovery.RecoveryService"/>와 같다).
        /// </summary>
        private void EnsureConstructionService()
        {
            if (constructionService != null) return;
            if (inventoryManager == null) return;

            constructionService = new BuildingConstructionService(
                inventoryManager,
                () => SaveSystem.Data,
                SaveSystem.Save,
                () => utcNowProvider());

            constructionService.ConstructionStarted += HandleConstructionStarted;
            constructionService.ConstructionCompleted += HandleConstructionCompleted;
        }

        private void DetachConstructionService()
        {
            if (constructionService == null) return;

            constructionService.ConstructionStarted -= HandleConstructionStarted;
            constructionService.ConstructionCompleted -= HandleConstructionCompleted;
            constructionService = null;
        }

        /// <summary>이 컨트롤러가 쓰는 건설 서비스. 인벤토리가 연결되지 않았으면 null이다(진단용).</summary>
        public BuildingConstructionService ConstructionService => constructionService;

        /// <summary>이 버튼이 맡은 건물의 건설이 이미 시작됐는지. 판정의 근거는 저장 기록 하나이며,
        /// <b>완성 시각이 지났는지는 보지 않는다</b> - 기록이 있으면 건설 버튼은 다시 나오지 않는다.
        /// 서비스나 정의가 없으면 "아직 시작하지 않았다"로 본다(감출 근거가 없다).</summary>
        public bool IsConstructionStarted =>
            constructionService != null && building != null &&
            constructionService.HasConstruction(building.BuildingId);

        /// <summary>마지막 갱신에서 판정한 건설 단계. 읽기 전용 진단값이며, 이 값을 바꿔서 표시를
        /// 바꿀 수는 없다 - 표시를 정하는 것은 언제나 <see cref="UpdateInteraction"/>이다.</summary>
        public BuildingConstructionPhase ConstructionPhase { get; private set; } =
            BuildingConstructionPhase.NotStarted;

        /// <summary>지금 타이머에 적혀 있는 문자열. 텍스트가 연결되지 않았으면 null이다(진단용).</summary>
        public string ConstructionTimerDisplay => constructionTimerText != null
            ? constructionTimerText.text
            : null;

        /// <summary>스테이지와 캐릭터가 모두 움직인 뒤에 위치를 맞춘다.</summary>
        private void LateUpdate()
        {
            UpdateInteraction();
        }

        /// <summary>
        /// 한 프레임분의 판정과 반영을 한 번에 한다. 순서가 중요하다:
        ///   1. 지금 어느 단계인가(저장 기록 + 시각 비교). <b>여기서만</b> 단계를 읽는다.
        ///   2. 완성으로 넘어갔으면 그 사실을 한 번 확정한다 - 마을 밖이라고 미루지 않는다.
        ///   3. 단계에 맞는 하나만 켜고 나머지를 끈다(건설 버튼 / 타이머 / 입장 버튼).
        ///   4. 짓는 중이면 남은 시간을 고쳐 쓴다(값이 그대로면 쓰지 않는다).
        ///   5. 마을이고 전환 연출이 멈춰 있는가(<see cref="IsTownReady"/>) - 아니면 <b>열린 팝업을 닫는다</b>.
        ///   6. 앵커가 카메라 앞에 있고 화면 안에 있는가 - 아니면 숨기기만 한다(팝업은 그대로 둔다).
        ///   7. 보여야 하면 건설 버튼과 타이머의 위치를 옮기고 루트를 켠다.
        ///
        /// 팝업을 닫는 조건과 버튼을 숨기는 조건을 <b>일부러 다르게</b> 두었다 - 마을 안에서 앵커가
        /// 화면 밖으로 잠깐 밀려났다고 사용자가 보고 있던 정보 창을 닫아 버리면 안 되기 때문이다.
        ///
        /// 상호작용 루트 하나를 끄면 그 안의 건설 버튼도 타이머도 입장 버튼도 함께 사라진다 -
        /// 던전이거나 전환 연출 중이면 이 UI가 하나도 보이지 않는 이유다.
        /// </summary>
        private void UpdateInteraction()
        {
            BuildingConstructionStatus status = ResolveConstructionStatus();
            ConstructionPhase = status.Phase;

            NotifyCompletionOnce(status);
            ApplyConstructionVisibility(status);
            ApplyTimerText(status);

            bool townReady = IsTownReady();
            if (!townReady) CloseOpenPopup();

            bool visible = false;
            if (townReady && HasPositionableRect() && TryProjectAnchor(out Vector2 localPoint))
            {
                ApplyAnchoredPosition(localPoint);
                visible = true;
            }

            SetInteractionVisible(visible);
        }

        /// <summary>지금 단계를 서비스에 물어본다. 서비스나 정의가 없으면 "아직 시작하지 않았다"로
        /// 본다 - 감출 근거가 없다.</summary>
        private BuildingConstructionStatus ResolveConstructionStatus()
        {
            if (constructionService == null || building == null) return default;
            return constructionService.GetStatus(building.BuildingId);
        }

        private bool HasPositionableRect()
        {
            return buildButtonRect != null || constructionTimerRect != null;
        }

        /// <summary>건설 버튼과 타이머를 <b>같은 자리</b>로 옮긴다. 둘은 서로 자리를 물려받는 사이이므로
        /// (짓기 시작하면 버튼 자리에 타이머가 뜬다) 좌표를 따로 계산하지 않는다. 지금 꺼져 있는 쪽도
        /// 함께 옮겨 두어야 켜지는 순간 제자리에서 나타난다.</summary>
        private void ApplyAnchoredPosition(Vector2 localPoint)
        {
            if (buildButtonRect != null) buildButtonRect.anchoredPosition = localPoint;
            if (constructionTimerRect != null) constructionTimerRect.anchoredPosition = localPoint;
        }

        /// <summary>
        /// 단계에 맞는 하나만 켠다. 켜고 끄는 것은 <b>각 오브젝트</b>이며 상호작용 루트는 건드리지
        /// 않는다 - 루트를 끄면 이 컨트롤러가 다루는 다른 UI까지 함께 사라진다.
        ///
        /// 매 프레임 판정하는 이유는 이 사실이 <b>저장 문서와 시각</b>에서 오기 때문이다. 시작이나
        /// 완성 이벤트를 놓쳤든, 앱을 다시 켰든, 다른 경로가 기록을 남겼든 결과는 언제나 같다.
        /// 손상된 기록(<see cref="BuildingConstructionPhase.Unreadable"/>)에서는 셋 다 꺼진다.
        /// </summary>
        private void ApplyConstructionVisibility(BuildingConstructionStatus status)
        {
            SetActiveIfNeeded(
                buildButton != null ? buildButton.gameObject : null,
                status.Phase == BuildingConstructionPhase.NotStarted);
            SetActiveIfNeeded(
                constructionTimerRoot, status.Phase == BuildingConstructionPhase.InProgress);
            SetActiveIfNeeded(
                openInnButton, status.Phase == BuildingConstructionPhase.Completed);
        }

        /// <summary>
        /// 남은 시간을 적는다. <b>표시 초 수가 그대로면 문자열을 다시 만들지 않는다</b> - 매 프레임
        /// 불려도 실제 쓰기는 1초에 한 번이고, 프레임마다 문자열 할당이 생기지 않는다.
        ///
        /// 짓는 중이 아니면 마지막 표시값을 잊는다 - 다시 켜졌을 때 지난번 값이 남아 있어 첫 1초 동안
        /// 낡은 시간이 보이는 일이 없게 한다.
        /// </summary>
        private void ApplyTimerText(BuildingConstructionStatus status)
        {
            if (constructionTimerText == null) return;

            if (status.Phase != BuildingConstructionPhase.InProgress)
            {
                lastDisplayedSeconds = UnwrittenSeconds;
                return;
            }

            long seconds = BuildingInfoFormatter.ToDisplaySeconds(status.Remaining);
            if (seconds == lastDisplayedSeconds) return;

            lastDisplayedSeconds = seconds;
            constructionTimerText.text = BuildingInfoFormatter.FormatBuildTime(seconds);
        }

        /// <summary>
        /// 완성을 <b>한 번만</b> 확정한다. 실제 판정과 저장은 서비스가 하고(그래서 앱을 다시 켜도
        /// 되풀이되지 않는다), 여기서는 완성 단계일 때 물어보기만 한다.
        ///
        /// 마을 밖이거나 전환 연출 중이어도 물어본다 - 완성은 화면이 아니라 시각이 정하는 사실이고,
        /// 저장이 실패하면 서비스가 표식을 되돌리므로 다음 갱신이 그대로 다시 시도한다.
        /// </summary>
        private void NotifyCompletionOnce(BuildingConstructionStatus status)
        {
            if (constructionService == null || building == null) return;
            if (status.Phase != BuildingConstructionPhase.Completed) return;

            constructionService.TryNotifyCompletion(building.BuildingId);
        }

        private static void SetActiveIfNeeded(GameObject target, bool active)
        {
            if (target == null) return;
            if (target.activeSelf != active) target.SetActive(active);
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

        /// <summary>열려 있는 건물 팝업을 <b>평소의 닫기 경로</b>로 닫는다 - 오브젝트를 직접 끄지 않는다
        /// (그러면 PopupPanelManager의 ESC 목록 정리와 구독 해제가 함께 지나가지 않는다).</summary>
        private void CloseOpenPopup()
        {
            if (buildingPopup == null) return;
            if (!buildingPopup.gameObject.activeSelf) return;

            buildingPopup.Close();
        }

        // ---- 시작 안내 토스트 ----

        /// <summary>
        /// 건설이 실제로 시작되어 <b>파일에 남은 뒤에만</b> 온다(실패한 시작에서는 오지 않는다).
        /// 그래서 여기서 하는 일은 안내 하나뿐이며, 저장도 인벤토리도 건드리지 않는다.
        ///
        /// 버튼을 감추는 것은 여기서 하지 않는다 - 그 판정은 매 프레임 저장 기록을 보고 하므로
        /// (<see cref="ApplyConstructionVisibility"/>) 이벤트를 놓쳐도 결과가 같아야 한다.
        /// </summary>
        private void HandleConstructionStarted(
            BuildingDefinition startedBuilding, BuildingConstructionSaveState state)
        {
            ShowStartedToast();
        }

        /// <summary>지금까지 띄운 시작 안내 토스트 수. 읽기 전용 진단값이다 - "한 번의 시작에 안내도
        /// 한 번"이라는 성질은 화면만 봐서는 확인하기 어려워 밖에서 셀 수 있어야 한다
        /// (<see cref="IsInteractionVisible"/>과 같은 성격의 값이다).</summary>
        public int StartedToastCount { get; private set; }

        /// <summary>지금 도착해 있는 안내 문구. 아직 오지 않았으면 null이며, 이 값이 없으면 토스트를
        /// 띄우지 않는다(진단용 - 코드가 문구를 지어내지 않는다는 것을 밖에서 확인할 수 있다).</summary>
        public string StartedToastMessage => localizedStartedMessage;

        /// <summary>안내 토스트를 <b>한 번</b> 띄운다. 문구가 아직 오지 않았거나 토스트 관리자가 없는
        /// 구성에서는 띄우지 않고 한 번만 알린다 - 코드가 문구를 지어내지 않는다.</summary>
        private void ShowStartedToast()
        {
            if (string.IsNullOrEmpty(localizedStartedMessage))
            {
                if (!missingToastWarned)
                {
                    missingToastWarned = true;
                    Debug.LogWarning($"[TownBuildingInteractionController] '{name}': 건설 시작 안내 문구" +
                                     "(01_UI / 42)가 아직 없어 토스트를 띄우지 않습니다 - Inspector에서 " +
                                     "Category와 Key를 지정하세요.", this);
                }
                return;
            }

            if (ToastManager.Instance == null)
            {
                if (!missingToastWarned)
                {
                    missingToastWarned = true;
                    Debug.LogWarning($"[TownBuildingInteractionController] '{name}': 씬에 ToastManager가 없어 " +
                                     "건설 시작 안내를 띄우지 못했습니다.", this);
                }
                return;
            }

            StartedToastCount++;
            ToastManager.Instance.Show(localizedStartedMessage);
        }

        private void SubscribeStartedMessage()
        {
            // 어떤 경로로 들어와도 두 번 걸리지 않게, 걸기 전에 항상 끊는다(팝업과 같은 규칙).
            UnsubscribeStartedMessage();

            if (constructionStartedMessage == null || !constructionStartedMessage.HasReference) return;

            boundStartedMessage = constructionStartedMessage;
            boundStartedMessage.StringChanged += ApplyStartedMessage;
        }

        private void UnsubscribeStartedMessage()
        {
            if (boundStartedMessage == null) return;

            boundStartedMessage.StringChanged -= ApplyStartedMessage;
            boundStartedMessage = null;
            localizedStartedMessage = null;
        }

        /// <summary>번역이 도착했다(언어를 바꾸면 다시 온다). 값을 들고 있다가 시작할 때 쓴다.</summary>
        private void ApplyStartedMessage(string value)
        {
            localizedStartedMessage = value;
        }

        // ---- 완성 안내 토스트 ----

        /// <summary>
        /// 완성이 <b>파일에 확정된 뒤에만</b> 온다(저장이 실패한 확정에서는 오지 않는다). 그래서
        /// 여기서 하는 일은 안내 하나뿐이며, 저장도 인벤토리도 건드리지 않는다.
        ///
        /// 입장 버튼을 켜는 것은 여기서 하지 않는다 - 그 판정도 매 프레임 저장 기록과 시각을 보고
        /// 하므로(<see cref="ApplyConstructionVisibility"/>) 이벤트를 놓쳐도 결과가 같아야 한다.
        /// </summary>
        private void HandleConstructionCompleted(BuildingConstructionSaveState state)
        {
            ShowCompletedToast();
        }

        /// <summary>지금까지 띄운 완성 안내 토스트 수. 읽기 전용 진단값이다 - "완성 한 번에 안내도
        /// 한 번, 앱을 다시 켜도 더는 없다"라는 성질은 화면만 봐서는 확인하기 어렵다.</summary>
        public int CompletedToastCount { get; private set; }

        /// <summary>지금 도착해 있는 완성 안내 문구. 아직 오지 않았으면 null이며, 이 값이 없으면
        /// 토스트를 띄우지 않는다.</summary>
        public string CompletedToastMessage => localizedCompletedMessage;

        /// <summary>완성 안내 토스트를 <b>한 번</b> 띄운다. 규칙은 시작 안내와 같다 - 문구가 아직
        /// 오지 않았거나 토스트 관리자가 없으면 띄우지 않고 한 번만 알린다.</summary>
        private void ShowCompletedToast()
        {
            if (string.IsNullOrEmpty(localizedCompletedMessage))
            {
                if (!missingCompletedToastWarned)
                {
                    missingCompletedToastWarned = true;
                    Debug.LogWarning($"[TownBuildingInteractionController] '{name}': 건설 완성 안내 문구" +
                                     "(01_UI / 43)가 아직 없어 토스트를 띄우지 않습니다 - Inspector에서 " +
                                     "Category와 Key를 지정하세요.", this);
                }
                return;
            }

            if (ToastManager.Instance == null)
            {
                if (!missingCompletedToastWarned)
                {
                    missingCompletedToastWarned = true;
                    Debug.LogWarning($"[TownBuildingInteractionController] '{name}': 씬에 ToastManager가 없어 " +
                                     "건설 완성 안내를 띄우지 못했습니다.", this);
                }
                return;
            }

            CompletedToastCount++;
            ToastManager.Instance.Show(localizedCompletedMessage);
        }

        private void SubscribeCompletedMessage()
        {
            UnsubscribeCompletedMessage();

            if (constructionCompletedMessage == null || !constructionCompletedMessage.HasReference) return;

            boundCompletedMessage = constructionCompletedMessage;
            boundCompletedMessage.StringChanged += ApplyCompletedMessage;
        }

        private void UnsubscribeCompletedMessage()
        {
            if (boundCompletedMessage == null) return;

            boundCompletedMessage.StringChanged -= ApplyCompletedMessage;
            boundCompletedMessage = null;
            localizedCompletedMessage = null;
        }

        /// <summary>번역이 도착했다(언어를 바꾸면 다시 온다). 값을 들고 있다가 완성할 때 쓴다.</summary>
        private void ApplyCompletedMessage(string value)
        {
            localizedCompletedMessage = value;
        }

        /// <summary>건설 버튼 클릭. <b>정의 하나와 누른 버튼, 그리고 건설 서비스를 넘기고 여는 것이
        /// 전부</b>다 - 비용을 확인하지도, 내지도, 저장하지도 않는다.
        ///
        /// 버튼의 사각형을 함께 넘기는 이유는 팝업이 <b>누른 버튼 옆에</b> 떠야 하기 때문이다. 어디에
        /// 뜰지는 팝업이 혼자 정하며(<see cref="BuildingPopupPanel.Bind(BuildingDefinition, RectTransform)"/>),
        /// 이 컨트롤러는 기준이 되는 버튼을 알려 주기만 한다 - 좌표 규칙이 두 곳으로 갈라지지 않게 한다.
        ///
        /// 서비스를 <b>열 때마다</b> 넘기는 이유는 팝업이 여러 버튼에 공용으로 쓰이기 때문이다 -
        /// 마지막으로 연 쪽의 서비스가 확인 버튼이 쓸 서비스여야 한다.</summary>
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

            EnsureConstructionService();

            buildingPopup.Bind(building, buildButtonRect);
            buildingPopup.SetConstructionService(constructionService);
            buildingPopup.Open();
        }

        private void ValidateReferences()
        {
            if (referencesValidated) return;
            referencesValidated = true;

            if (buildButton != null) buildButtonRect = buildButton.transform as RectTransform;
            if (constructionTimerRoot != null)
            {
                constructionTimerRect = constructionTimerRoot.transform as RectTransform;
            }

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
            if (inventoryManager == null)
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': InventoryManager가 연결되지 " +
                               "않아 건설을 시작할 수 없습니다 - 확인 버튼을 눌러도 아무 일도 일어나지 " +
                               "않습니다.", this);
            }
            if (constructionTimerRoot == null)
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': 건설 타이머" +
                               "(pn_ConstructionTimer)가 연결되지 않아 짓는 동안 아무것도 보이지 " +
                               "않습니다.", this);
            }
            if (constructionTimerText == null)
            {
                Debug.LogError($"[TownBuildingInteractionController] '{name}': 남은 시간 텍스트" +
                               "(lb_ConstructionTimer)가 연결되지 않아 시간이 표시되지 않습니다.", this);
            }
            if (constructionTimerAnimator == null)
            {
                Debug.LogWarning($"[TownBuildingInteractionController] '{name}': 타이머 Animator" +
                                 "(ani_Timer)가 연결되지 않아 Update Mode를 Unscaled Time으로 맞출 수 " +
                                 "없습니다 - 화면이 멈추면 연출도 멈춥니다.", this);
            }
            if (constructionStartedMessage == null || !constructionStartedMessage.HasReference)
            {
                Debug.LogWarning($"[TownBuildingInteractionController] '{name}': 건설 시작 안내 문구" +
                                 "(01_UI / 42)가 지정되지 않아 시작해도 토스트가 뜨지 않습니다.", this);
            }
            if (constructionCompletedMessage == null || !constructionCompletedMessage.HasReference)
            {
                Debug.LogWarning($"[TownBuildingInteractionController] '{name}': 건설 완성 안내 문구" +
                                 "(01_UI / 43)가 지정되지 않아 완성해도 토스트가 뜨지 않습니다.", this);
            }
        }
    }
}
