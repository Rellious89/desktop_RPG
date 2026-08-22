using System.Collections.Generic;
using Common;
using Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Building
{
    /// <summary>
    /// 건물 정보 팝업(dialog_BuildingPopup). 열고 닫기, ESC 순서, 포커스, Windows 클릭 관통은 전부 기존
    /// <see cref="ModalPanel"/> / <see cref="PopupPanelManager"/> 규칙을 그대로 쓴다 - 건물 전용 팝업
    /// 시스템을 새로 만들지 않는다. 이 클래스가 하는 일은 <b>건물 정의 하나를 받아 문구를 그리고,
    /// 지금 그 비용을 낼 수 있는지 보여 주고, 누른 버튼 옆에 자리를 잡는 것</b>이다.
    ///
    /// <b>그리는 동안에는 아무것도 바꾸지 않는다.</b> 열려 있는 내내 비용을 <b>판정만</b> 하고 내지
    /// 않는다 - <see cref="InventoryManager.EvaluateCost"/>는 저장 항목을 만들지도, 값을 바꾸지도,
    /// 저장하지도, 알림을 보내지도 않는 읽기 전용 경로다.
    ///
    /// <b>실제로 무언가가 바뀌는 자리는 확인 버튼 하나뿐이며, 그것도 이 클래스가 하지 않는다.</b>
    /// 차감·기록·저장은 <see cref="BuildingConstructionService"/>가 한 덩어리로 처리하고, 여기서는
    /// 그 결과를 화면에 옮길 뿐이다 - 성공이면 닫고, 실패면 열어 둔 채 경고를 켠다. 이 클래스에
    /// 재화를 읽거나 저장을 부르는 코드는 여전히 한 줄도 없다.
    ///
    /// <b>닫기는 btn_cancle 하나다.</b> ModalPanel의 닫기 버튼 칸에 btn_cancle을 연결해 두었으므로
    /// 취소 버튼도 ESC도 <see cref="ModalPanel.Close"/>라는 같은 경로를 지난다 - 닫는 방법마다 다른
    /// 코드가 도는 자리를 만들지 않는다(에셋 이름의 'cancle' 철자는 <b>그대로 둔다</b>).
    ///
    /// <b>문구는 네 갈래를 조합해 만든다.</b> 건물 이름(07_Building), 해금 기능 이름(01_UI), 설명 틀
    /// (01_UI / 40), 재화 이름(Currency 에셋)이 각각 다른 표에 있고 <b>따로따로</b> 도착하므로,
    /// <see cref="LocalizedTextReference.StringChanged"/>를 넷 다 구독했다가 값이 들어올 때마다 다시
    /// 조립한다 - 실행 중에 언어를 바꾸면 네 값이 모두 새로 들어오므로 열려 있는 팝업이 그 자리에서
    /// 그 언어로 바뀐다. 구독은 <b>다시 바인딩할 때 / 닫힐 때 / 비활성화될 때 / 파괴될 때</b> 모두
    /// 짝지어 해제된다.
    ///
    /// <b>부족 경고는 프리팹의 lb_warningMSG 하나다.</b> 문구는 그 오브젝트에 붙은
    /// LocalizeStringEvent(01_UI / 41)가 채우므로 이 코드는 <b>켜고 끄기만</b> 한다 - 경고 문구를
    /// 코드가 지어내면 표를 고쳐도 화면이 바뀌지 않는 자리가 생긴다.
    ///
    /// <b>자리는 누른 버튼을 기준으로 잡는다.</b> 옮기는 대상은 <see cref="placementRect"/>(bg)이며
    /// 전체 화면을 덮는 팝업 루트는 <b>절대 옮기지 않는다</b> - 루트를 옮기면 그 아래의 입력 영역까지
    /// 통째로 화면 밖으로 나간다. 후보 순서와 화면 안으로 당기는 규칙은
    /// <see cref="BuildingPopupPlacement"/>가 혼자 알고 있고, 이 클래스는 그 답을 RectTransform에
    /// 옮겨 담기만 한다.
    ///
    /// <b>코드에 한국어/영어 UI 문구를 적지 않는다.</b> 참조가 비어 있으면 그 자리를 비워 두고 경고만
    /// 남긴다 - 대체 문구를 코드가 지어내면 표를 고쳐도 화면이 바뀌지 않는 자리가 생긴다.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingPopupPanel : ModalPanel
    {
        [Header("Texts (정확한 오브젝트를 직접 연결한다 - 이름 탐색을 쓰지 않는다)")]
        [Tooltip("건물 이름을 그릴 TMP(Top/ItemIcon/lb_BuildingName). 건물 정의의 Localized Name을 " +
                 "그대로 표시한다.")]
        [SerializeField] private TextMeshProUGUI buildingNameText;

        [Tooltip("해금 기능/소요 시간/비용을 한 덩어리로 그릴 TMP(middle/lb_description).")]
        [SerializeField] private TextMeshProUGUI descriptionText;

        [Tooltip("비용이 부족할 때만 켜지는 경고 TMP(middle/lb_warningMSG). <b>문구는 이 코드가 " +
                 "쓰지 않는다</b> - 그 오브젝트의 LocalizeStringEvent(01_UI / 41)가 채운다. 여기서는 " +
                 "오브젝트를 켜고 끄기만 하며, 프리팹에서는 꺼진 채로 저작되어 있어야 한다.")]
        [SerializeField] private TextMeshProUGUI warningText;

        [Header("Localization")]
        [Tooltip("설명 문구의 틀(01_UI / 40). 자리표시자는 {0}=해금 기능 이름, {1}=소요 시간(HH:mm:ss), " +
                 "{2}=비용이다. 비워두면 설명이 빈 칸으로 남는다 - 코드가 대체 문구를 지어내지 않는다.")]
        [SerializeField] private LocalizedTextReference descriptionFormat = new LocalizedTextReference();

        [Header("Buttons")]
        [Tooltip("확인 버튼(Bottom/btn_confirm). 비용을 낼 수 있을 때만 눌린다 - 판정은 열 때, 다시 " +
                 "바인딩할 때, 인벤토리가 바뀔 때, 그리고 <b>누르기 직전에</b> 다시 한다.")]
        [SerializeField] private Button confirmButton;

        // 취소 버튼(Bottom/btn_cancle)은 ModalPanel의 닫기 버튼 칸(closeButton)에 연결한다 -
        // 그래야 취소와 ESC가 같은 Close 경로를 지난다. 여기에 같은 버튼을 한 번 더 들고 있으면
        // 리스너가 두 곳에서 걸리게 되므로 별도 칸을 두지 않는다.

        [Header("Placement")]
        [Tooltip("실제로 옮길 사각형(bg). <b>팝업 루트가 아니다</b> - 루트는 전체 화면을 덮는 " +
                 "사각형이라 옮기면 입력 영역까지 함께 화면 밖으로 나간다. 비워두면 위치를 " +
                 "계산하지 않고 저작된 자리에 그대로 둔다.")]
        [SerializeField] private RectTransform placementRect;

        [Tooltip("누른 버튼과의 간격이자 화면 가장자리에서 띄울 여백(Canvas 기준 해상도 픽셀).")]
        [Min(0f)]
        [SerializeField] private float placementMargin = 8f;

        /// <summary>지금 이 팝업이 그리고 있는 건물. 아무것도 바인딩되지 않았으면 null이다.</summary>
        public BuildingDefinition BoundBuilding { get; private set; }

        /// <summary>이 팝업을 연 버튼의 사각형. 지정되지 않았으면 null이고, 그때는 자리를 계산하지
        /// 않고 저작된 위치에 그대로 둔다(이전 단계와 같은 동작).</summary>
        public RectTransform SourceRect { get; private set; }

        /// <summary>마지막 판정에서 비용을 낼 수 있었는지. 읽기 전용 진단값이며 이 값을 바꿔서
        /// 화면을 바꿀 수는 없다 - 표시를 정하는 것은 언제나 <see cref="RefreshCostState"/>다.</summary>
        public bool IsCostPayable { get; private set; }

        /// <summary>마지막 판정 결과 그대로(부족한 재화/아이템과 수량이 들어 있다). 아직 판정한 적이
        /// 없거나 판정할 근거가 없었으면 null이다.</summary>
        public InventoryCostResult LastCostEvaluation { get; private set; }

        /// <summary>마지막으로 자리를 잡을 때 고른 후보. 진단용이다.</summary>
        public BuildingPopupSide LastPlacementSide { get; private set; }

        /// <summary>
        /// 확인을 눌렀을 때 건설을 맡길 서비스. 여는 쪽
        /// (<see cref="TownBuildingInteractionController"/>)이 <see cref="SetConstructionService"/>로
        /// 넘겨 준다 - 순수 C# 객체라 Inspector 칸으로 둘 수 없고, 팝업이 스스로 만들면 씬마다 다른
        /// 서비스가 생겨 "건설 규칙은 한 곳"이라는 규칙이 무너진다.
        ///
        /// 비어 있으면 확인은 <b>아무것도 하지 않는다</b>(경고를 켜고 버튼을 닫는다) - 낼 수 있는
        /// 것처럼 보이는데 눌러도 아무 일이 없는 자리를 남기지 않는다.
        /// </summary>
        private BuildingConstructionService constructionService;

        /// <summary>마지막 건설 시작 요청의 결과. 아직 눌린 적이 없으면 null이다(진단용).</summary>
        public BuildingConstructionStartResult? LastStartResult { get; private set; }

        // 구독 중인 참조들. 구독을 건 대상을 그대로 들고 있다가 짝지어 해제한다 - 해제할 때 다시
        // 정의에서 찾아오면, 그 사이에 정의가 바뀐 경우 엉뚱한 참조에서 해제하게 된다.
        private LocalizedTextReference boundNameReference;
        private LocalizedTextReference boundFunctionReference;
        private LocalizedTextReference boundFormatReference;
        private LocalizedTextReference boundCurrencyNameReference;

        // 각 갈래가 도착한 값. null은 "아직 오지 않았다"는 뜻이며 빈 문자열과 구분한다.
        private string localizedBuildingName;
        private string localizedFunctionName;
        private string localizedFormat;
        private string localizedCurrencyName;

        private readonly List<BuildingInfoFormatter.CostComponent> costComponents =
            new List<BuildingInfoFormatter.CostComponent>(2);

        // 자리 계산에 쓰는 재사용 버퍼(자리를 잡을 때마다 배열을 새로 만들지 않는다).
        private readonly Vector3[] sourceCorners = new Vector3[4];
        private readonly Vector3[] boundsCorners = new Vector3[4];

        // 마지막으로 자리를 잡을 때 본 기하. 이 값이 달라졌을 때만 다시 계산한다 - 창 크기가 바뀌거나
        // 버튼이 움직이는 것을 프레임마다 알려 주는 신호가 없으므로 여기서 직접 확인한다.
        private bool hasTrackedGeometry;
        private Vector2 trackedSourceMin;
        private Vector2 trackedSourceMax;
        private Vector2 trackedBoundsSize;

        private bool inventorySubscribed;
        private bool confirmListenerAttached;

        private bool referencesValidated;
        private bool formatFailureLogged;
        private bool missingFormatWarned;
        private bool missingInventoryWarned;

        /// <summary>직전 확인이 실패해서 경고를 켜 둔 상태인가. 판정만으로는 알 수 없는 실패(저장
        /// 실패처럼 <b>보유량은 충분한데</b> 시작하지 못한 경우)에도 경고가 남아 있게 하려고 따로
        /// 들고 있으며, 다시 바인딩하거나 열거나 인벤토리가 바뀌면 풀린다 - 그때는 새로 판정한
        /// 결과가 화면의 근거가 된다.</summary>
        private bool startFailureBlocked;

        /// <summary>닫기 버튼을 자동으로 찾아야 할 때 쓰는 이름. 이 팝업의 닫기는 btn_cancle이다
        /// (에셋 철자를 그대로 쓴다 - 'cancel'로 고치지 않는다).</summary>
        protected override string CloseButtonName => "btn_cancle";

        /// <summary>
        /// 그릴 건물을 정한다. <b>팝업이 닫혀 있어도 부를 수 있다</b> - 비활성 오브젝트의 컴포넌트
        /// 참조는 그대로 유효하므로, 여는 쪽이 Bind 후 <see cref="ModalPanel.Open"/>을 부르면 된다.
        ///
        /// 이 오버로드는 <b>기준 버튼을 바꾸지 않는다</b> - 이전 단계부터 있던 진입점이라 자리
        /// 계산 없이 저작된 위치에 그대로 두는 동작이 유지된다.
        /// </summary>
        public void Bind(BuildingDefinition building)
        {
            BindInternal(building, SourceRect);
        }

        /// <summary>
        /// 그릴 건물과 <b>이 팝업을 연 버튼</b>을 함께 정한다. 버튼을 알려 주면 팝업이 그 버튼 옆에
        /// 자리를 잡고, 이후 버튼이 움직이거나 창 크기가 바뀌어도 따라간다.
        ///
        /// <paramref name="sourceButton"/>에 null을 넘기면 기준 버튼을 <b>지운다</b> - 자리 계산 없이
        /// 저작된 위치에 그대로 두라는 뜻이다.
        ///
        /// 이미 열려 있는 상태에서 다른 건물로 바꿔도 안전하다 - 이전 구독을 <b>먼저</b> 끊고 새로
        /// 걸기 때문에, 이전 건물의 번역이 뒤늦게 도착해 새 건물의 문구를 덮어쓰는 경로가 없다.
        ///
        /// <b>인벤토리도 저장 데이터도 바꾸지 않는다.</b> 비용은 <see cref="InventoryManager.EvaluateCost"/>로
        /// 판정만 하며, 그 경로는 저장도 알림도 하지 않는다.
        /// </summary>
        public void Bind(BuildingDefinition building, RectTransform sourceButton)
        {
            BindInternal(building, sourceButton);
        }

        /// <summary>
        /// 확인을 눌렀을 때 건설을 맡길 서비스를 정한다. 여는 쪽이 <see cref="Bind(BuildingDefinition,
        /// RectTransform)"/> 앞뒤 어느 쪽에서 불러도 되며, 같은 서비스를 다시 넘겨도 안전하다.
        ///
        /// null을 넘기면 확인이 아무 일도 하지 않는 상태로 되돌아간다 - 그 상태를 조용히 두지 않고
        /// 눌렀을 때 경고를 켠다.
        /// </summary>
        public void SetConstructionService(BuildingConstructionService service)
        {
            constructionService = service;
        }

        /// <summary>기준 버튼을 정하고 <see cref="ModalPanel.Open"/>을 부른다 - 여는 쪽이 두 줄을
        /// 순서대로 쓰다가 하나를 빠뜨리는 자리를 없앤다.</summary>
        public void Open(RectTransform sourceButton)
        {
            SetSourceRect(sourceButton);
            Open();
        }

        /// <summary>기준 버튼만 바꾼다. 열려 있으면 그 자리에서 곧바로 다시 계산한다.</summary>
        public void SetSourceRect(RectTransform sourceButton)
        {
            if (SourceRect == sourceButton) return;

            SourceRect = sourceButton;
            hasTrackedGeometry = false;
            if (isActiveAndEnabled) UpdatePlacement();
        }

        private void BindInternal(BuildingDefinition building, RectTransform sourceButton)
        {
            UnsubscribeLocalization();
            BoundBuilding = building;
            SourceRect = sourceButton;
            hasTrackedGeometry = false;

            // 다른 건물을 그리기 시작하는 순간 직전 실패 표시는 남의 이야기가 된다.
            startFailureBlocked = false;
            LastStartResult = null;

            // 닫혀 있으면 여기서 구독하지 않는다 - 열릴 때 OnModalOpened가 구독하고 그 시점의
            // 언어와 인벤토리로 그린다. 열려 있으면 지금 바로 다시 그린다.
            if (isActiveAndEnabled)
            {
                SubscribeLocalization();
                RefreshContents();
            }
        }

        protected override void OnModalOpened()
        {
            ValidateReferences();

            // 다시 열 때는 언제나 지금 보유량이 근거다 - 지난번에 켜 두고 닫은 경고를 들고 오지 않는다.
            startFailureBlocked = false;

            SubscribeLocalization();
            SubscribeInventory();
            AttachConfirmListener();
        }

        protected override void OnModalClosed()
        {
            UnsubscribeLocalization();
            UnsubscribeInventory();
            DetachConfirmListener();

            // 닫힌 팝업에 경고가 켜진 채로 남아 있으면 다음에 열릴 때 한 프레임 동안 그 경고가 보인다.
            // 판정은 열릴 때 다시 하므로, 여기서는 <b>표시만</b> 초기 상태로 되돌린다 - 인벤토리도
            // 저장도 건드리지 않는다.
            startFailureBlocked = false;
            ResetCostDisplay();
        }

        /// <summary>
        /// 비용 표시를 저작된 초기 상태(<b>경고 꺼짐 + 확인 닫힘</b>)로 되돌린다.
        ///
        /// <see cref="ApplyCostState"/>에 false를 넘기는 것과 <b>다르다</b> - 그쪽은 "낼 수 없다"를
        /// 그리는 것이라 경고를 <b>켠다</b>. 여기서는 "아직 판정한 적이 없다"를 그리므로 둘 다 끈다.
        /// </summary>
        private void ResetCostDisplay()
        {
            IsCostPayable = false;

            if (warningText != null)
            {
                GameObject warningObject = warningText.gameObject;
                if (warningObject.activeSelf) warningObject.SetActive(false);
            }

            if (confirmButton != null && confirmButton.interactable) confirmButton.interactable = false;
        }

        protected override void OnDestroy()
        {
            // OnDisable이 먼저 지나가는 것이 보통이지만, 파괴 경로에서도 구독이 남지 않게 한 번 더 끊는다.
            UnsubscribeLocalization();
            UnsubscribeInventory();
            base.OnDestroy();
        }

        /// <summary>지금 도착해 있는 값들로 화면을 다시 만든다. 아직 오지 않은 값이 있으면 그 칸을
        /// 비워 둔다 - 반쪽짜리 문구를 내보내지 않는다.
        ///
        /// 순서가 중요하다: 문구와 경고를 <b>먼저</b> 정한 뒤에 자리를 잡는다. 경고가 켜지면 팝업이
        /// 그만큼 높아지므로, 높이를 확정하기 전에 잰 자리는 한 프레임 어긋난 자리가 된다.</summary>
        protected override void RefreshContents()
        {
            ApplyBuildingName();
            ApplyDescription();
            RefreshCostState();
            UpdatePlacement();
        }

        /// <summary>버튼이 움직였거나 창/캔버스 크기가 바뀌었으면 자리를 다시 잡는다. 열려 있는
        /// 동안에만 돈다(닫히면 이 컴포넌트가 비활성이라 호출되지 않는다).
        ///
        /// <b>LateUpdate인 이유</b>는 기준 버튼을 옮기는 쪽(<see cref="TownBuildingInteractionController"/>)도
        /// LateUpdate에서 움직이기 때문이다 - Update에서 보면 언제나 한 프레임 전 좌표를 본다.</summary>
        private void LateUpdate()
        {
            if (SourceRect == null || placementRect == null) return;
            if (!TryReadGeometry(out Vector2 sourceMin, out Vector2 sourceMax, out Rect bounds)) return;

            if (hasTrackedGeometry &&
                Approximately(sourceMin, trackedSourceMin) &&
                Approximately(sourceMax, trackedSourceMax) &&
                Approximately(bounds.size, trackedBoundsSize))
            {
                return;
            }

            UpdatePlacement();
        }

        // ---- 구독 ----

        private void SubscribeLocalization()
        {
            // 어떤 경로로 들어와도 두 번 걸리지 않게, 걸기 전에 항상 끊는다.
            UnsubscribeLocalization();

            if (descriptionFormat != null && descriptionFormat.HasReference)
            {
                boundFormatReference = descriptionFormat;
                boundFormatReference.StringChanged += ApplyLocalizedFormat;
            }
            else if (!missingFormatWarned)
            {
                missingFormatWarned = true;
                Debug.LogWarning($"[BuildingPopupPanel] '{name}': 설명 문구 틀(01_UI / 40)이 지정되지 않아 " +
                                 "설명을 비워 둡니다 - Inspector에서 Category와 Key를 지정하세요.", this);
            }

            BuildingDefinition building = BoundBuilding;
            if (building == null) return;

            if (building.HasLocalizedName)
            {
                boundNameReference = building.LocalizedName;
                boundNameReference.StringChanged += ApplyLocalizedBuildingName;
            }

            if (building.HasLocalizedFunctionName)
            {
                boundFunctionReference = building.LocalizedFunctionName;
                boundFunctionReference.StringChanged += ApplyLocalizedFunctionName;
            }

            CurrencyDefinition currency = building.CostCurrency;
            if (currency != null && currency.HasLocalizedName)
            {
                boundCurrencyNameReference = currency.LocalizedName;
                boundCurrencyNameReference.StringChanged += ApplyLocalizedCurrencyName;
            }
        }

        private void UnsubscribeLocalization()
        {
            if (boundFormatReference != null)
            {
                boundFormatReference.StringChanged -= ApplyLocalizedFormat;
                boundFormatReference = null;
            }
            if (boundNameReference != null)
            {
                boundNameReference.StringChanged -= ApplyLocalizedBuildingName;
                boundNameReference = null;
            }
            if (boundFunctionReference != null)
            {
                boundFunctionReference.StringChanged -= ApplyLocalizedFunctionName;
                boundFunctionReference = null;
            }
            if (boundCurrencyNameReference != null)
            {
                boundCurrencyNameReference.StringChanged -= ApplyLocalizedCurrencyName;
                boundCurrencyNameReference = null;
            }

            localizedBuildingName = null;
            localizedFunctionName = null;
            localizedFormat = null;
            localizedCurrencyName = null;
        }

        /// <summary>번역 구독이 하나라도 살아 있는지 여부. 읽기 전용 진단값이다 - "닫혔는데 구독이
        /// 남아 있는가"는 눈으로 보이지 않는 종류의 결함이라 밖에서 확인할 수 있어야 한다.</summary>
        public bool HasLocalizationSubscriptions =>
            boundFormatReference != null || boundNameReference != null ||
            boundFunctionReference != null || boundCurrencyNameReference != null;

        /// <summary>인벤토리 변경 구독이 살아 있는지 여부. 같은 이유로 밖에서 확인할 수 있어야 한다 -
        /// 닫힌 팝업이 인벤토리 신호를 계속 받으면 보이지도 않는 화면을 매번 다시 판정한다.</summary>
        public bool HasInventorySubscription => inventorySubscribed;

        /// <summary>
        /// 재화나 아이템이 바뀌면 <b>지금 이 자리에서</b> 다시 판정한다 - 팝업을 열어 둔 채로 다른
        /// 창에서 재화를 쓰거나 보상을 받았을 때, 확인 버튼과 경고가 실제 보유량과 어긋난 채로 남아
        /// 있으면 안 된다.
        ///
        /// <b>이 경로도 인벤토리를 바꾸지 않는다</b> - 판정만 다시 하고 화면만 고친다.
        /// </summary>
        private void HandleInventoryChanged()
        {
            // 보유량이 실제로 달라졌으므로 직전 실패 표시는 근거를 잃는다 - 지금 값으로 다시 판정한다.
            startFailureBlocked = false;

            RefreshCostState();
            UpdatePlacement();
        }

        private void SubscribeInventory()
        {
            if (inventorySubscribed) return;

            InventoryManager.InventoryChanged += HandleInventoryChanged;
            inventorySubscribed = true;
        }

        private void UnsubscribeInventory()
        {
            if (!inventorySubscribed) return;

            InventoryManager.InventoryChanged -= HandleInventoryChanged;
            inventorySubscribed = false;
        }

        private void ApplyLocalizedFormat(string value)
        {
            localizedFormat = value;
            ApplyDescription();
            UpdatePlacement();
        }

        private void ApplyLocalizedBuildingName(string value)
        {
            localizedBuildingName = value;
            ApplyBuildingName();
            UpdatePlacement();
        }

        private void ApplyLocalizedFunctionName(string value)
        {
            localizedFunctionName = value;
            ApplyDescription();
            UpdatePlacement();
        }

        private void ApplyLocalizedCurrencyName(string value)
        {
            localizedCurrencyName = value;
            ApplyDescription();
            UpdatePlacement();
        }

        // ---- 그리기 ----

        private void ApplyBuildingName()
        {
            if (buildingNameText == null) return;

            // 아직 도착하지 않았으면 비워 둔다 - 이전 건물의 이름이 남아 있으면 안 된다.
            buildingNameText.text = localizedBuildingName ?? string.Empty;
        }

        private void ApplyDescription()
        {
            if (descriptionText == null) return;

            BuildingDefinition building = BoundBuilding;
            if (building == null || localizedFormat == null)
            {
                descriptionText.text = string.Empty;
                return;
            }

            string time = BuildingInfoFormatter.FormatBuildTime(building.BuildTimeSeconds);
            string cost = BuildingInfoFormatter.ComposeCost(BuildCostComponents(building));

            string composed = BuildingInfoFormatter.ComposeDescription(
                localizedFormat, localizedFunctionName ?? string.Empty, time, cost, out bool formatFailed);

            if (formatFailed && !formatFailureLogged)
            {
                formatFailureLogged = true;
                Debug.LogError($"[BuildingPopupPanel] '{name}': 설명 문구 틀의 자리표시자가 맞지 않아 틀을 " +
                               "그대로 표시합니다 - 01_UI / 40은 {0}(기능), {1}(시간), {2}(비용) 세 개여야 합니다.",
                               this);
            }

            descriptionText.text = composed;
        }

        /// <summary>
        /// 이 건물의 비용을 <b>조각 목록</b>으로 만든다. 지금 표에 있는 비용은 재화 하나뿐이라 조각도
        /// 하나지만, 아이템 비용이 붙는 날에는 여기서 조각을 더 담기만 하면 된다 - 조각을 잇는 규칙은
        /// <see cref="BuildingInfoFormatter.ComposeCost"/>가 이미 알고 있다.
        ///
        /// <b>이것은 문구를 만드는 경로다.</b> "낼 수 있는가"는 여기서 판정하지 않는다 - 그 답은 언제나
        /// <see cref="InventoryManager.EvaluateCost"/>에서 온다.
        /// </summary>
        private IReadOnlyList<BuildingInfoFormatter.CostComponent> BuildCostComponents(BuildingDefinition building)
        {
            costComponents.Clear();

            int amount = building.CostCurrencyAmount;
            if (amount > 0 && building.CostCurrency != null)
            {
                costComponents.Add(new BuildingInfoFormatter.CostComponent(
                    BuildingInfoFormatter.FormatAmount(amount), localizedCurrencyName));
            }

            return costComponents;
        }

        // ---- 비용 판정 ----

        /// <summary>
        /// 지금 이 건물의 비용을 낼 수 있는지 <b>다시 판정</b>하고 경고와 확인 버튼을 그 답에 맞춘다.
        ///
        /// <b>판정은 InventoryManager 하나가 한다.</b> 이 클래스는 잔액도 보유량도 직접 읽지 않으며,
        /// <see cref="InventoryManager.EvaluateCost"/>는 저장 항목을 만들지도, 값을 바꾸지도, 저장하지도,
        /// 알림을 보내지도 않는 읽기 전용 경로다 - 몇 번을 불러도 프로젝트도 저장 파일도 달라지지 않는다.
        ///
        /// <b>판정할 근거가 없으면 낼 수 없는 것으로 본다.</b> 건물이 바인딩되지 않았거나 씬에
        /// InventoryManager가 없으면 "낼 수 있다"고 말할 수 있는 근거가 하나도 없으므로, 확인 버튼을
        /// 열어 두는 쪽이 아니라 닫아 두는 쪽으로 실패한다.
        /// </summary>
        private void RefreshCostState()
        {
            BuildingDefinition building = BoundBuilding;
            InventoryManager inventory = InventoryManager.Instance;

            if (building == null)
            {
                LastCostEvaluation = null;
                ApplyCostState(false);
                return;
            }

            if (inventory == null)
            {
                if (!missingInventoryWarned)
                {
                    missingInventoryWarned = true;
                    Debug.LogWarning($"[BuildingPopupPanel] '{name}': 씬에 InventoryManager가 없어 비용을 " +
                                     "판정할 수 없습니다 - 확인 버튼을 닫아 둔 채로 둡니다.", this);
                }

                LastCostEvaluation = null;
                ApplyCostState(false);
                return;
            }

            InventoryCostResult result = inventory.EvaluateCost(building.ToCostRequest());
            LastCostEvaluation = result;

            // 직전 시작이 실패해서 경고를 켜 둔 상태라면 판정이 통과해도 켜 둔 채로 남긴다 - 그 표시를
            // 푸는 것은 다시 열거나 다시 바인딩하거나 인벤토리가 실제로 바뀌었을 때뿐이다.
            ApplyCostState(result != null && result.IsPayable && !startFailureBlocked);
        }

        /// <summary>판정 결과를 화면에 옮긴다. 규칙은 하나다 - <b>낼 수 있을 때만</b> 경고가 꺼지고
        /// 확인이 눌린다. 부족/잘못된 요청/모르는 아이템은 모두 같은 취급이며, 무엇이 부족한지는
        /// <see cref="LastCostEvaluation"/>에 그대로 남는다.
        ///
        /// 확인 버튼은 <b>끄지 않고</b> interactable만 바꾼다 - 버튼이 사라졌다 나타나면 아래쪽 줄의
        /// 배치가 흔들린다. 경고는 반대로 <b>오브젝트째</b> 켜고 끈다 - 자리를 차지하지 않아야
        /// 부족하지 않을 때 팝업이 그만큼 낮아진다.</summary>
        private void ApplyCostState(bool payable)
        {
            IsCostPayable = payable;

            if (warningText != null)
            {
                GameObject warningObject = warningText.gameObject;
                if (warningObject.activeSelf == payable) warningObject.SetActive(!payable);
            }

            if (confirmButton != null && confirmButton.interactable != payable)
            {
                confirmButton.interactable = payable;
            }
        }

        // ---- 확인 버튼 ----

        private void AttachConfirmListener()
        {
            if (confirmButton == null || confirmListenerAttached) return;

            // 지웠다 다시 건다 - 껐다 켜도 리스너가 쌓이지 않는다.
            confirmButton.onClick.RemoveListener(HandleConfirmClicked);
            confirmButton.onClick.AddListener(HandleConfirmClicked);
            confirmListenerAttached = true;
        }

        private void DetachConfirmListener()
        {
            if (confirmButton == null)
            {
                confirmListenerAttached = false;
                return;
            }

            confirmButton.onClick.RemoveListener(HandleConfirmClicked);
            confirmListenerAttached = false;
        }

        /// <summary>
        /// 확인을 눌렀을 때. 가장 먼저 하는 일은 <b>비용을 처음부터 다시 판정하는 것</b>이다 - 팝업이
        /// 열려 있는 동안 다른 경로가 재화를 썼을 수 있으므로, 화면에 남아 있던 판정을 근거로 진행하면
        /// "보이기엔 낼 수 있었는데" 모자란 채로 지나가는 자리가 생긴다.
        ///
        /// 낼 수 있으면 <see cref="BuildingConstructionService.TryStartConstruction"/>에 넘긴다.
        /// <b>여기서 차감하거나 저장하지 않는다</b> - 비용과 건설 기록을 한 번의 저장으로 함께 남기는
        /// 것은 서비스 하나의 일이고, 이 클래스는 그 답을 화면에 옮기기만 한다.
        ///
        /// <list type="bullet">
        ///   <item><b>성공</b>: 평소의 닫기 경로로 닫는다(구독 해제와 ESC 목록 정리가 함께 지나간다).</item>
        ///   <item><b>실패</b>: 닫지 않는다. 곧바로 다시 판정해 화면을 실제 보유량에 맞추고, 그러고도
        ///         낼 수 있다고 나오면(저장 실패처럼 보유량 문제가 아닌 실패) <b>경고를 켠 채로</b>
        ///         확인을 닫아 둔다 - 실패했는데 아무 표시도 없이 눌리는 버튼을 남기지 않는다.</item>
        /// </list>
        ///
        /// 서비스가 없으면 시작할 방법이 없으므로 같은 실패로 다룬다(코드가 대신 차감하지 않는다).
        /// </summary>
        private void HandleConfirmClicked()
        {
            // 다시 판정하기 전에 직전 실패 표시를 푼다 - 지금 보유량이 근거가 되어야 한다.
            startFailureBlocked = false;

            RefreshCostState();
            UpdatePlacement();

            if (!IsCostPayable) return;

            BuildingDefinition building = BoundBuilding;

            if (constructionService == null)
            {
                Debug.LogError($"[BuildingPopupPanel] '{name}': 건설 서비스가 연결되지 않아 건설을 시작할 " +
                               "수 없습니다 - 팝업을 여는 쪽이 SetConstructionService로 넘겨야 합니다.", this);
                BlockAfterStartFailure();
                return;
            }

            BuildingConstructionStartResult result = constructionService.TryStartConstruction(building);
            LastStartResult = result;

            if (result.Success)
            {
                // 성공했으니 이 팝업이 할 일은 끝났다. 닫기는 언제나 같은 경로다.
                Close();
                return;
            }

            // 실패는 열어 둔 채로 알린다. 보유량이 실제로 모자랐다면 다시 판정하는 것만으로 경고가
            // 켜지고, 그렇지 않은 실패(저장 실패/중복 시작)는 아래에서 강제로 켠다.
            RefreshCostState();
            UpdatePlacement();

            if (IsCostPayable) BlockAfterStartFailure();
        }

        /// <summary>판정으로는 드러나지 않는 실패를 화면에 남긴다 - 경고를 켜고 확인을 닫는다.</summary>
        private void BlockAfterStartFailure()
        {
            startFailureBlocked = true;
            ApplyCostState(false);
            UpdatePlacement();
        }

        // ---- 자리 잡기 ----

        /// <summary>
        /// 지금 내용과 지금 버튼 위치로 <see cref="placementRect"/>(bg)의 자리를 다시 잡는다.
        ///
        /// <b>재기 전에 레이아웃을 확정한다.</b> 경고가 켜지고 꺼지면 ContentSizeFitter가 팝업 높이를
        /// 다시 잡는데, 그것은 원래 다음 프레임에 일어난다 - 그대로 재면 이전 높이로 자리를 잡아
        /// 한 프레임 동안 어긋난 위치가 화면에 나간다.
        ///
        /// <b>옮기는 것은 bg뿐이다.</b> 전체 화면을 덮는 팝업 루트는 건드리지 않고, bg의 pivot·앵커·
        /// 레이아웃 설정도 그대로 둔 채 위치만 바꾼다.
        /// </summary>
        private void UpdatePlacement()
        {
            if (placementRect == null || SourceRect == null) return;

            var parent = placementRect.parent as RectTransform;
            if (parent == null) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(placementRect);

            if (!TryReadGeometry(out Vector2 sourceMin, out Vector2 sourceMax, out Rect bounds)) return;

            Rect rect = placementRect.rect;
            var size = new Vector2(rect.width, rect.height);
            Rect source = Rect.MinMaxRect(sourceMin.x, sourceMin.y, sourceMax.x, sourceMax.y);

            Vector2 min = BuildingPopupPlacement.Solve(
                source, size, bounds, placementMargin, out BuildingPopupSide side);
            LastPlacementSide = side;

            // pivot이 어디에 있든 같은 결과가 되도록, 왼쪽-아래 모서리에서 pivot 지점을 되짚어 계산한다.
            var pivotPoint = new Vector2(
                min.x + size.x * placementRect.pivot.x,
                min.y + size.y * placementRect.pivot.y);

            placementRect.position = parent.TransformPoint(pivotPoint);

            trackedSourceMin = sourceMin;
            trackedSourceMax = sourceMax;
            trackedBoundsSize = bounds.size;
            hasTrackedGeometry = true;
        }

        /// <summary>
        /// 자리 계산에 필요한 두 사각형을 <b>팝업 부모의 로컬 좌표</b>로 읽는다 - 버튼이 차지한
        /// 사각형과, 넘어가면 안 되는 범위(루트 Canvas)다.
        ///
        /// 범위의 기준을 부모가 아니라 <b>루트 Canvas</b>로 잡는 이유는, 팝업이 붙어 있는 부모
        /// (Dialog_UI)가 화면보다 작게 잡혀 있어도 팝업은 화면 전체를 쓸 수 있어야 하기 때문이다.
        /// </summary>
        private bool TryReadGeometry(out Vector2 sourceMin, out Vector2 sourceMax, out Rect bounds)
        {
            sourceMin = Vector2.zero;
            sourceMax = Vector2.zero;
            bounds = default;

            if (SourceRect == null || placementRect == null) return false;

            var parent = placementRect.parent as RectTransform;
            if (parent == null) return false;

            // GetWorldCorners는 좌하-좌상-우상-우하 순서다.
            SourceRect.GetWorldCorners(sourceCorners);
            sourceMin = parent.InverseTransformPoint(sourceCorners[0]);
            sourceMax = parent.InverseTransformPoint(sourceCorners[2]);

            Canvas canvas = parent.GetComponentInParent<Canvas>();
            if (canvas == null) return false;

            var canvasRect = canvas.rootCanvas.transform as RectTransform;
            if (canvasRect == null) return false;

            canvasRect.GetWorldCorners(boundsCorners);
            Vector2 boundsMin = parent.InverseTransformPoint(boundsCorners[0]);
            Vector2 boundsMax = parent.InverseTransformPoint(boundsCorners[2]);
            bounds = Rect.MinMaxRect(boundsMin.x, boundsMin.y, boundsMax.x, boundsMax.y);
            return true;
        }

        private static bool Approximately(Vector2 left, Vector2 right)
        {
            return Mathf.Approximately(left.x, right.x) && Mathf.Approximately(left.y, right.y);
        }

        // ---- 참조 검증 ----

        /// <summary>빠진 참조를 자동으로 채우지 않고 무엇이 빠졌는지만 알린다(다른 패널과 같은 방침).</summary>
        private void ValidateReferences()
        {
            if (referencesValidated) return;
            referencesValidated = true;

            if (buildingNameText == null)
            {
                Debug.LogError($"[BuildingPopupPanel] '{name}': 건물 이름 TMP(lb_BuildingName)가 " +
                               "연결되지 않았습니다.", this);
            }
            if (descriptionText == null)
            {
                Debug.LogError($"[BuildingPopupPanel] '{name}': 설명 TMP(lb_description)가 " +
                               "연결되지 않았습니다.", this);
            }
            if (warningText == null)
            {
                Debug.LogError($"[BuildingPopupPanel] '{name}': 부족 경고 TMP(lb_warningMSG)가 연결되지 " +
                               "않아 비용이 모자라도 아무 표시가 나오지 않습니다.", this);
            }
            if (confirmButton == null)
            {
                Debug.LogError($"[BuildingPopupPanel] '{name}': 확인 버튼(btn_confirm)이 연결되지 않았습니다.", this);
            }
            if (placementRect == null)
            {
                Debug.LogError($"[BuildingPopupPanel] '{name}': 자리를 옮길 사각형(bg)이 연결되지 않아 " +
                               "팝업이 저작된 자리에 그대로 뜹니다.", this);
            }
            else if (placementRect.transform == transform)
            {
                Debug.LogError($"[BuildingPopupPanel] '{name}': 자리를 옮길 사각형이 팝업 루트로 " +
                               "지정되어 있습니다 - 루트는 전체 화면을 덮는 사각형이라 옮기면 입력 영역까지 " +
                               "화면 밖으로 나갑니다. bg를 연결하세요.", this);
            }
        }
    }
}
