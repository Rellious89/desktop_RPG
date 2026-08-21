using System.Globalization;
using Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 아이템 툴팁 프리팹(item_ToolTip) <b>한 장의 내용</b>. 언제 뜨고 어디에 붙는지는 모르고,
    /// "이 아이템을 이 수량으로 그려라"만 받는다 - 위치와 수명은 <see cref="ItemTooltipController"/>가
    /// 소유한다.
    ///
    /// <b>이름과 설명은 구독으로 받는다.</b> Unity Localization의 문자열 로드는 비동기라
    /// <see cref="Bind"/>가 돌아온 순간에는 아직 값이 없을 수 있고, 실행 중에 Locale이 바뀌면 값이
    /// 다시 온다. 그래서 문자열을 한 번 읽어 넣지 않고 <see cref="LocalizedTextReference"/>를 구독하며,
    /// 구독은 <b>항상 짝을 이룬다</b> - 다른 아이템으로 바꿔 그릴 때, 툴팁을 숨길 때, 이 컴포넌트가
    /// 꺼질 때 모두 같은 해제 경로를 지난다. 구독이 남으면 이미 사라진 툴팁이 Locale 변경 때마다
    /// 다시 살아난다.
    ///
    /// <b>참조가 없는 아이템도 그린다.</b> 이름은 <see cref="ItemDefinition.DisplayName"/>(그마저
    /// 비어 있으면 Item Id)로 대신하고 설명은 빈 줄로 둔다 - 툴팁이 통째로 안 뜨는 것보다 이름만이라도
    /// 보이는 편이 무엇이 잘못됐는지 알기 쉽다.
    ///
    /// <b>제목(Title)과 Bottom은 건드리지 않는다.</b> 제목은 프리팹의 LocalizedTMPText가 이미
    /// 01_UI/39를 가리키고 있고, Bottom은 꺼진 채로 저장되어 있다 - 둘 다 이 컴포넌트의 관심사가 아니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ItemTooltipView : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("아이템 이름을 그릴 텍스트(lb_ItemName).")]
        [SerializeField] private TextMeshProUGUI nameText;

        [Tooltip("아이템 설명을 그릴 텍스트(lb_description). 높이가 내용에 따라 달라진다.")]
        [SerializeField] private TextMeshProUGUI descriptionText;

        [Tooltip("보유 수량을 그릴 텍스트(lb_ItemCount). 프리팹에 적힌 문구를 형식 문자열로 쓴다.")]
        [SerializeField] private TextMeshProUGUI countText;

        [Tooltip("아이템 아이콘(sp_ItemIcon). 아이콘이 없는 아이템에서는 Image만 꺼서 흰 사각형이 " +
                 "그려지지 않게 한다.")]
        [SerializeField] private Image iconImage;

        [Tooltip("크기가 내용에 따라 달라지는 뿌리(bg). 컨트롤러는 이 RectTransform을 움직인다 - " +
                 "프리팹 루트는 부모를 가득 채우므로 위치를 잡을 수 있는 대상이 아니다.")]
        [SerializeField] private RectTransform layoutRoot;

        /// <summary>수량이 들어갈 자리. 이 글자가 형식 문자열에 없으면 넣을 자리가 없는 것이다.</summary>
        private const string CountPlaceholder = "{0}";

        /// <summary>수량 형식 문자열. 프리팹의 lb_ItemCount에 적힌 값(<c>{0}</c>)을 처음 한 번 읽어
        /// 둔다 - 형식을 코드에 다시 적으면 프리팹을 고쳐도 반영되지 않는 자리가 하나 생긴다.</summary>
        private string countFormat;

        private bool resolved;

        private ItemDefinition boundDefinition;
        private int boundCount;
        private bool subscribed;

        /// <summary>컨트롤러가 위치를 잡을 대상. 내용에 따라 높이가 달라지는 바로 그 사각형이다.</summary>
        public RectTransform PlacementRect
        {
            get
            {
                ResolveReferences();
                return layoutRoot != null ? layoutRoot : transform as RectTransform;
            }
        }

        /// <summary>지금 그리고 있는 아이템. 아무것도 그리지 않으면 null이다.</summary>
        public ItemDefinition BoundDefinition => boundDefinition;

        /// <summary>로컬라이징 문자열이 도착해 크기가 달라졌을 때 알린다 - 컨트롤러는 이 신호를 받아
        /// 위치를 다시 잡는다. 구독 시점에는 아직 값이 없을 수 있어서, 한 번 잡은 위치를 그대로 두면
        /// 설명이 길어진 만큼 툴팁이 화면 밖으로 밀린다.</summary>
        public event System.Action LayoutChanged;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            // 꺼질 때 구독을 남기면 Locale이 바뀔 때마다 보이지도 않는 툴팁이 문자열을 받는다.
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        /// <summary><paramref name="definition"/>을 <paramref name="count"/>개 가진 것으로 그린다.
        /// 이전에 그리던 아이템의 구독은 여기서 끊긴다 - 같은 아이템을 다시 넘겨도 마찬가지라,
        /// 구독이 두 번 걸리는 경로가 없다.</summary>
        public void Bind(ItemDefinition definition, int count)
        {
            ResolveReferences();
            Unsubscribe();

            boundDefinition = definition;
            boundCount = count;

            ApplyIcon();
            ApplyCount();

            if (definition == null)
            {
                ApplyName(string.Empty);
                ApplyDescription(string.Empty);
                RebuildLayout();
                return;
            }

            // 참조가 없으면 구독할 것이 없다 - 대체 이름을 바로 넣고 끝낸다.
            ApplyName(null);
            ApplyDescription(null);

            Subscribe();
            RebuildLayout();
        }

        /// <summary>그리던 내용을 비우고 구독을 끊는다. 툴팁을 숨기는 쪽이 부른다 - 숨기기만 하고
        /// 구독을 남기면 화면에 없는 툴팁이 계속 갱신된다.</summary>
        public void Clear()
        {
            Unsubscribe();
            boundDefinition = null;
            boundCount = 0;
        }

        /// <summary>지금 내용 기준으로 크기를 다시 계산한다. ContentSizeFitter는 다음 프레임에야
        /// 크기를 잡으므로, 그대로 두면 한 프레임 동안 이전 아이템의 높이로 그려진다.</summary>
        public void RebuildLayout()
        {
            RectTransform target = PlacementRect;
            if (target == null) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(target);
        }

        private void Subscribe()
        {
            if (boundDefinition == null || subscribed) return;

            // 참조가 하나도 없으면 <b>구독한 적이 없는 상태</b>로 남긴다 - 플래그가 "핸들러가 걸려
            // 있다"는 뜻이 아니게 되면, 해제 경로가 무엇을 되돌리는지 코드에서 읽히지 않는다.
            bool attached = false;

            if (boundDefinition.HasLocalizedName)
            {
                boundDefinition.LocalizedName.StringChanged += OnNameChanged;
                attached = true;
            }

            if (boundDefinition.HasLocalizedDescription)
            {
                boundDefinition.LocalizedDescription.StringChanged += OnDescriptionChanged;
                attached = true;
            }

            subscribed = attached;
        }

        private void Unsubscribe()
        {
            if (!subscribed || boundDefinition == null)
            {
                subscribed = false;
                return;
            }

            // HasLocalized*는 구독한 뒤에 참조가 지워졌을 수도 있으므로 조건 없이 뗀다 - 걸리지 않은
            // 핸들러를 떼는 것은 아무 일도 하지 않는다.
            boundDefinition.LocalizedName.StringChanged -= OnNameChanged;
            boundDefinition.LocalizedDescription.StringChanged -= OnDescriptionChanged;
            subscribed = false;
        }

        private void OnNameChanged(string localized)
        {
            ApplyName(localized);
            RebuildLayout();
            LayoutChanged?.Invoke();
        }

        private void OnDescriptionChanged(string localized)
        {
            ApplyDescription(localized);
            RebuildLayout();
            LayoutChanged?.Invoke();
        }

        /// <summary>이름을 쓴다. <paramref name="localized"/>가 비어 있으면 정의의
        /// <see cref="ItemDefinition.DisplayName"/>(그것도 비어 있으면 Item Id)로 대신한다.</summary>
        private void ApplyName(string localized)
        {
            if (nameText == null) return;

            if (!string.IsNullOrEmpty(localized))
            {
                nameText.text = localized;
                return;
            }

            nameText.text = boundDefinition != null ? boundDefinition.DisplayName : string.Empty;
        }

        /// <summary>설명을 쓴다. 대체 문구는 두지 않는다 - 설명이 없는 아이템에 이름이나 id를 다시
        /// 적으면 같은 글자가 두 줄이 된다.</summary>
        private void ApplyDescription(string localized)
        {
            if (descriptionText == null) return;

            descriptionText.text = string.IsNullOrEmpty(localized) ? string.Empty : localized;
        }

        private void ApplyIcon()
        {
            if (iconImage == null) return;

            Sprite icon = boundDefinition != null ? boundDefinition.Icon : null;
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        /// <summary>수량을 프리팹의 형식 문자열로 쓴다. 형식이 비었거나 <c>{0}</c>이 없어 넣을 자리가
        /// 없으면 <b>숫자만</b> 쓴다 - 형식이 잘못됐다고 수량 자체를 안 보여 주지는 않는다.
        /// 자릿수 표기는 실행 환경의 지역 설정과 무관하게 같도록 InvariantCulture로 고정한다.
        ///
        /// <b>자리표시자의 존재는 직접 확인한다.</b> <c>string.Format</c>은 "보유 수량"처럼 중괄호가
        /// 아예 없는 문구를 예외 없이 <b>그대로</b> 돌려주므로, 예외만 붙잡아서는 수량이 통째로
        /// 사라진 화면을 잡을 수 없다. 예외 처리는 <c>{0}</c>은 있는데 <c>{1}</c>이 함께 있거나
        /// 중괄호가 짝을 잃은 경우를 위해 남겨 둔다.</summary>
        private void ApplyCount()
        {
            if (countText == null) return;

            string number = boundCount.ToString(CultureInfo.InvariantCulture);

            if (string.IsNullOrEmpty(countFormat)
                || countFormat.IndexOf(CountPlaceholder, System.StringComparison.Ordinal) < 0)
            {
                countText.text = number;
                return;
            }

            try
            {
                countText.text = string.Format(CultureInfo.InvariantCulture, countFormat, boundCount);
            }
            catch (System.FormatException)
            {
                countText.text = number;
            }
        }

        private void ResolveReferences()
        {
            if (resolved) return;
            resolved = true;

            if (layoutRoot == null && transform.childCount > 0)
            {
                layoutRoot = transform.GetChild(0) as RectTransform;
            }

            // 형식 문자열은 프리팹에 적힌 값을 그대로 쓴다. 여기서 읽어 두지 않으면 첫 Bind가 값을
            // 덮어쓴 뒤로는 원래 형식을 알 수 없다.
            countFormat = countText != null ? countText.text : string.Empty;

            if (nameText == null)
            {
                Debug.LogWarning($"[ItemTooltipView] '{name}': 이름 텍스트가 연결되지 않아 아이템 이름이 " +
                                 "표시되지 않습니다 - 프리팹에서 lb_ItemName을 연결하세요.", this);
            }
            if (descriptionText == null)
            {
                Debug.LogWarning($"[ItemTooltipView] '{name}': 설명 텍스트가 연결되지 않아 아이템 설명이 " +
                                 "표시되지 않습니다 - 프리팹에서 lb_description을 연결하세요.", this);
            }
            if (countText == null)
            {
                Debug.LogWarning($"[ItemTooltipView] '{name}': 수량 텍스트가 연결되지 않아 보유 수량이 " +
                                 "표시되지 않습니다 - 프리팹에서 lb_ItemCount를 연결하세요.", this);
            }
        }
    }
}
