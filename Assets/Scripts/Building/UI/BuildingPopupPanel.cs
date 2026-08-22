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
    /// 시스템을 새로 만들지 않는다. 이 클래스가 하는 일은 <b>건물 정의 하나를 받아 문구를 그리는 것</b>뿐이다.
    ///
    /// <b>여기에는 건설이 없다.</b> 비용을 평가하지도, 내지도, 저장하지도 않는다 -
    /// <see cref="BuildingDefinition.ToCostRequest"/>조차 부르지 않는다. 확인 버튼은 <b>눌리지 않는 채로</b>
    /// 놓여 있고(<see cref="Button.interactable"/> = false) 리스너도 걸지 않는다: 실제 건설은 다음 단계의
    /// 몫이고, 그때 이 버튼에 리스너를 하나 거는 것으로 끝나야 한다.
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

        [Header("Localization")]
        [Tooltip("설명 문구의 틀(01_UI / 40). 자리표시자는 {0}=해금 기능 이름, {1}=소요 시간(HH:mm:ss), " +
                 "{2}=비용이다. 비워두면 설명이 빈 칸으로 남는다 - 코드가 대체 문구를 지어내지 않는다.")]
        [SerializeField] private LocalizedTextReference descriptionFormat = new LocalizedTextReference();

        [Header("Buttons")]
        [Tooltip("확인 버튼(Bottom/btn_confirm). 이번 단계에서는 <b>언제나 눌리지 않는다</b> - " +
                 "리스너를 걸지 않고 interactable을 false로 유지한다. 실제 건설은 다음 단계의 몫이다.")]
        [SerializeField] private Button confirmButton;

        // 취소 버튼(Bottom/btn_cancle)은 ModalPanel의 닫기 버튼 칸(closeButton)에 연결한다 -
        // 그래야 취소와 ESC가 같은 Close 경로를 지난다. 여기에 같은 버튼을 한 번 더 들고 있으면
        // 리스너가 두 곳에서 걸리게 되므로 별도 칸을 두지 않는다.

        /// <summary>지금 이 팝업이 그리고 있는 건물. 아무것도 바인딩되지 않았으면 null이다.</summary>
        public BuildingDefinition BoundBuilding { get; private set; }

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

        private bool referencesValidated;
        private bool formatFailureLogged;
        private bool missingFormatWarned;

        /// <summary>닫기 버튼을 자동으로 찾아야 할 때 쓰는 이름. 이 팝업의 닫기는 btn_cancle이다
        /// (에셋 철자를 그대로 쓴다 - 'cancel'로 고치지 않는다).</summary>
        protected override string CloseButtonName => "btn_cancle";

        /// <summary>
        /// 그릴 건물을 정한다. <b>팝업이 닫혀 있어도 부를 수 있다</b> - 비활성 오브젝트의 컴포넌트
        /// 참조는 그대로 유효하므로, 여는 쪽이 Bind 후 <see cref="ModalPanel.Open"/>을 부르면 된다.
        ///
        /// 이미 열려 있는 상태에서 다른 건물로 바꿔도 안전하다 - 이전 구독을 <b>먼저</b> 끊고 새로
        /// 걸기 때문에, 이전 건물의 번역이 뒤늦게 도착해 새 건물의 문구를 덮어쓰는 경로가 없다.
        ///
        /// <b>인벤토리도 저장 데이터도 건드리지 않는다.</b> 비용은 표시용 문자열로만 쓰인다.
        /// </summary>
        public void Bind(BuildingDefinition building)
        {
            UnsubscribeLocalization();
            BoundBuilding = building;

            // 닫혀 있으면 여기서 구독하지 않는다 - 열릴 때 OnModalOpened가 구독하고 그 시점의
            // 언어로 그린다. 열려 있으면 지금 바로 다시 그린다.
            if (isActiveAndEnabled)
            {
                SubscribeLocalization();
                RefreshContents();
            }
        }

        protected override void OnModalOpened()
        {
            ValidateReferences();
            KeepConfirmButtonDisabled();
            SubscribeLocalization();
        }

        protected override void OnModalClosed()
        {
            UnsubscribeLocalization();
        }

        protected override void OnDestroy()
        {
            // OnDisable이 먼저 지나가는 것이 보통이지만, 파괴 경로에서도 구독이 남지 않게 한 번 더 끊는다.
            UnsubscribeLocalization();
            base.OnDestroy();
        }

        /// <summary>지금 도착해 있는 값들로 화면을 다시 만든다. 아직 오지 않은 값이 있으면 그 칸을
        /// 비워 둔다 - 반쪽짜리 문구를 내보내지 않는다.</summary>
        protected override void RefreshContents()
        {
            KeepConfirmButtonDisabled();
            ApplyBuildingName();
            ApplyDescription();
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

        private void ApplyLocalizedFormat(string value)
        {
            localizedFormat = value;
            ApplyDescription();
        }

        private void ApplyLocalizedBuildingName(string value)
        {
            localizedBuildingName = value;
            ApplyBuildingName();
        }

        private void ApplyLocalizedFunctionName(string value)
        {
            localizedFunctionName = value;
            ApplyDescription();
        }

        private void ApplyLocalizedCurrencyName(string value)
        {
            localizedCurrencyName = value;
            ApplyDescription();
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
        /// <b>인벤토리를 읽지 않는다.</b> "낼 수 있는가"는 이 팝업의 관심사가 아니다.
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

        // ---- 확인 버튼 ----

        /// <summary>확인 버튼을 <b>꺼진 모습 그대로</b> 유지한다. 리스너는 걸지 않는다 - 이번 단계에
        /// 확인으로 일어나는 상태 변화가 하나도 없어야 하기 때문이다. 오브젝트를 끄지 않고
        /// interactable만 false로 두므로 버튼은 계속 보이되 눌리지 않는다.</summary>
        private void KeepConfirmButtonDisabled()
        {
            if (confirmButton == null) return;
            if (confirmButton.interactable) confirmButton.interactable = false;
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
            if (confirmButton == null)
            {
                Debug.LogError($"[BuildingPopupPanel] '{name}': 확인 버튼(btn_confirm)이 연결되지 않았습니다.", this);
            }
        }
    }
}
