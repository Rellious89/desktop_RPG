using System.Collections;
using DesktopWindow;
using Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 인벤토리 슬롯에 마우스를 올렸을 때 뜨는 <b>아이템 툴팁</b>의 주인. 메뉴바의
    /// <see cref="HoverTooltipController"/>와 <b>일부러 나눠 둔다</b> - 저쪽은 버튼 위쪽에 한 줄짜리
    /// 문구를 띄우는 것이 전부이고, 이쪽은 아이템 정의와 수량을 받아 이름/설명/아이콘을 그리며 슬롯의
    /// 오른쪽에 붙는다. 한 클래스에 두 규칙을 넣으면 어느 쪽을 고쳐도 다른 쪽이 함께 흔들린다.
    ///
    /// <b>인스턴스는 하나뿐이다.</b> 프리팹을 매번 만들지 않고 처음 한 번 만들어 재사용하므로,
    /// 슬롯을 아무리 빠르게 오가도 화면에 툴팁이 둘 이상 뜨는 경로가 구조적으로 없다.
    ///
    /// <b>인스턴스는 목록 밖에 붙는다.</b> 슬롯(list_item)과 슬롯 영역(list)에는 Mask가 걸려 있어서
    /// 그 아래에 만들면 툴팁이 칸 크기로 잘린다. 그래서 패널의 부모(Panel_UI) 아래에 만들고 표시할
    /// 때마다 형제 중 맨 뒤로 보내, 나중에 열린 다른 패널보다도 앞에 그려지게 한다.
    ///
    /// <b>툴팁은 입력을 받지 않는다.</b> 만들 때 안쪽 모든 Graphic의 Raycast Target을 끄므로 슬롯과
    /// 겹쳐도 Hover가 끊기지 않고 클릭/스크롤을 가로채지 않는다. 같은 이유로 툴팁 프리팹에는
    /// Button도 <see cref="WindowInputRegion"/>도 두지 않는다.
    ///
    /// <b>표시 요청의 주인을 항상 기억한다.</b> 마우스가 슬롯 A에서 B로 빠르게 넘어가면 EventSystem이
    /// 같은 프레임에 A의 Exit와 B의 Enter를 보내는데, 순서가 어느 쪽이든 <see cref="CancelShow"/>는
    /// 자기가 주인일 때만 동작한다 - 뒤늦게 도착한 A의 Exit가 이미 뜬 B의 툴팁을 지우지 않는다.
    ///
    /// <b>주인은 인벤토리 슬롯만이 아니다.</b> 던전 대표 보상 미리보기와 던전 귀환 결과의 아이템 줄도
    /// 같은 컨트롤러 하나를 쓰므로, 주인의 타입은 세 화면이 함께 만족할 수 있는 <see cref="MonoBehaviour"/>로
    /// 둔다. 주인에게서 필요한 것은 "아직 살아 있고 켜져 있는가"(파괴 여부와 <see cref="Behaviour.isActiveAndEnabled"/>)
    /// 뿐이고, 그 둘은 MonoBehaviour가 이미 답할 수 있다 - 화면마다 인터페이스를 새로 만들면 컨트롤러가
    /// 각 화면의 사정을 알게 된다.
    ///
    /// <b>패널이 앞으로 나와도 툴팁이 그 아래로 가지 않는다.</b> <see cref="PopupPanelManager"/>는 패널을
    /// 클릭할 때 그 패널을 Panel_UI 안에서 맨 뒤 형제로 보내는데, 툴팁 인스턴스도 같은 Panel_UI의 자식이라
    /// 그 순간 패널에 가려진다. 그래서 <b>떠 있는 동안에만</b> 프레임 끝(<c>LateUpdate</c>)에 형제 순서를
    /// 다시 맨 뒤로 되돌린다 - 다시 만들지도, 다시 바인딩하지도, 클릭 때문에 숨기지도 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ItemTooltipController : MonoBehaviour
    {
        [Tooltip("표시에 사용할 item_ToolTip 프리팹. 루트에 ItemTooltipView가 있어야 한다.")]
        [SerializeField] private GameObject tooltipPrefab;

        [Tooltip("툴팁 인스턴스를 붙일 부모. 비워두면 이 패널의 부모(Panel_UI)를 쓴다 - 슬롯 영역의 " +
                 "Mask 밖이어야 툴팁이 잘리지 않는다.")]
        [SerializeField] private RectTransform tooltipRoot;

        [Tooltip("마우스를 올린 뒤 툴팁이 나타나기까지의 대기시간(초). 0이면 즉시 뜬다 - 인벤토리는 " +
                 "칸을 훑어보는 화면이라 기본값이 즉시다.")]
        [SerializeField] private float tooltipDelay = 0f;

        [Tooltip("슬롯과 툴팁 사이의 가로 여백(Canvas 기준 해상도 픽셀).")]
        [SerializeField] private float horizontalOffset = 8f;

        // 슬롯/캔버스 RectTransform의 월드 코너를 받는 재사용 버퍼(툴팁을 띄울 때마다 할당하지 않는다).
        private readonly Vector3[] targetCorners = new Vector3[4];
        private readonly Vector3[] boundsCorners = new Vector3[4];

        private ItemTooltipView view;
        private RectTransform instanceRect;
        private bool instantiateFailed;

        // 표시를 예약했거나 이미 표시 중인 주인. 둘 중 하나만 값을 가진다.
        private MonoBehaviour pendingOwner;
        private MonoBehaviour visibleOwner;
        private RectTransform visibleTarget;
        private Coroutine showRoutine;

        /// <summary>Inspector에 설정된 대기시간(초). 0이면 즉시 표시다.</summary>
        public float TooltipDelay => tooltipDelay;

        /// <summary>지금 툴팁을 띄우고 있는 주인. 아무것도 떠 있지 않으면 null이다.</summary>
        public MonoBehaviour VisibleOwner => visibleOwner;

        /// <summary>툴팁이 지금 화면에 떠 있는지. 인스턴스를 아직 만들지 않았으면 false다.</summary>
        public bool IsVisible => instanceRect != null && instanceRect.gameObject.activeSelf;

        /// <summary>지금 만들어진 툴팁 인스턴스의 내용. 아직 한 번도 띄우지 않았으면 null이다.</summary>
        public ItemTooltipView View => view;

        /// <summary>
        /// <paramref name="owner"/>를 담고 있는 <b>하나뿐인</b> 컨트롤러를 부모 쪽으로 올라가며 찾는다.
        /// 인벤토리 슬롯, 던전 보상 미리보기, 던전 결과 아이템 줄이 모두 이 한 곳을 지나므로 <b>새 컨트롤러를
        /// 만들지 않는다</b> - 화면마다 만들면 툴팁 인스턴스도 화면 수만큼 생긴다.
        ///
        /// 세 화면의 패널(pn_*)은 전부 Panel_UI의 자식이고 컨트롤러는 그 Panel_UI에 붙어 있으므로, 부모
        /// 탐색 하나로 셋 다 같은 인스턴스에 닿는다. 꺼져 있는 부모도 함께 보는 것은(<c>includeInactive</c>)
        /// 패널이 닫힌 상태에서 만들어지는 칸이 있기 때문이다.
        /// </summary>
        public static ItemTooltipController FindSharedController(Component owner)
        {
            if (owner == null) return null;

            var controller = owner.GetComponentInParent<ItemTooltipController>(true);
            if (controller == null)
            {
                Debug.LogWarning($"[ItemTooltipController] '{owner.name}': 부모에서 ItemTooltipController를 " +
                                 "찾지 못해 아이템 툴팁이 표시되지 않습니다 - 씬의 Panel_UI에 컴포넌트를 " +
                                 "붙이세요(패널마다 새로 붙이지 않습니다).", owner);
            }

            return controller;
        }

        /// <summary><paramref name="owner"/>의 툴팁 표시를 <b>수량과 함께</b> 예약한다. 이전 예약이나
        /// 표시 중인 툴팁은 즉시 정리되므로, 같은 순간에 살아 있는 툴팁은 항상 하나뿐이다.
        ///
        /// 수량이 <c>long</c>인 것은 던전 세션 누적 획득량이 long이기 때문이다 - 인벤토리의 int는
        /// 손실 없이 넓어진다.</summary>
        public void RequestShow(MonoBehaviour owner, ItemDefinition definition, long count, RectTransform target)
        {
            RequestShow(owner, definition, count, hasCount: true, target);
        }

        /// <summary><paramref name="owner"/>의 툴팁 표시를 <b>수량 없이</b> 예약한다 - 던전 대표 보상
        /// 미리보기처럼 보여줄 수량 자체가 없는 화면이 쓴다.</summary>
        public void RequestShow(MonoBehaviour owner, ItemDefinition definition, RectTransform target)
        {
            RequestShow(owner, definition, 0L, hasCount: false, target);
        }

        private void RequestShow(
            MonoBehaviour owner, ItemDefinition definition, long count, bool hasCount, RectTransform target)
        {
            // owner가 파괴된 경우도 Unity의 == 규칙이 null로 잡아 준다.
            if (owner == null || definition == null || target == null) return;
            if (!isActiveAndEnabled) return;

            CancelPendingRoutine();
            HideInstance();
            visibleOwner = null;
            visibleTarget = null;

            pendingOwner = owner;

            if (tooltipDelay <= 0f)
            {
                ShowNow(owner, definition, count, hasCount, target);
                return;
            }

            showRoutine = StartCoroutine(ShowAfterDelay(owner, definition, count, hasCount, target));
        }

        /// <summary><paramref name="owner"/>가 지금 툴팁의 주인일 때만 예약을 취소하고 툴팁을 지운다.
        /// 다른 주인이 이미 자리를 넘겨받았다면 아무것도 하지 않는다.</summary>
        public void CancelShow(MonoBehaviour owner)
        {
            if (owner == null) return;
            if (pendingOwner != owner && visibleOwner != owner) return;

            Hide();
        }

        /// <summary>예약과 표시를 모두 없앤다. 패널이 닫히거나 이 컨트롤러가 꺼질 때도 같은 경로를 지난다.</summary>
        public void Hide()
        {
            CancelPendingRoutine();
            pendingOwner = null;
            visibleOwner = null;
            visibleTarget = null;
            HideInstance();
        }

        private void OnDisable()
        {
            // 컴포넌트가 꺼지면 코루틴은 Unity가 멈추지만, 남은 상태와 화면의 툴팁은 직접 정리해야 한다.
            Hide();
        }

        private IEnumerator ShowAfterDelay(
            MonoBehaviour owner, ItemDefinition definition, long count, bool hasCount, RectTransform target)
        {
            // Time.timeScale과 무관하게 항상 같은 체감 대기시간이 되도록 실제 시간으로 센다.
            yield return new WaitForSecondsRealtime(tooltipDelay);

            showRoutine = null;
            ShowNow(owner, definition, count, hasCount, target);
        }

        private void ShowNow(
            MonoBehaviour owner, ItemDefinition definition, long count, bool hasCount, RectTransform target)
        {
            // 대기 중에 주인이 꺼졌거나(패널이 닫힘) 파괴됐으면 띄우지 않는다.
            if (owner == null || !owner.isActiveAndEnabled || target == null || definition == null)
            {
                Hide();
                return;
            }

            if (!EnsureInstance())
            {
                Hide();
                return;
            }

            pendingOwner = null;
            visibleOwner = owner;
            visibleTarget = target;

            instanceRect.gameObject.SetActive(true);
            instanceRect.SetAsLastSibling();

            if (hasCount) view.Bind(definition, count);
            else view.Bind(definition);

            Place(target);
        }

        /// <summary>
        /// 떠 있는 툴팁을 형제 중 맨 뒤로 되돌린다. <see cref="PopupPanelManager.FocusPanel"/>이 클릭이나
        /// 열기로 패널을 맨 뒤 형제로 보내면, 같은 부모(Panel_UI)에 붙어 있는 툴팁이 그 패널 <b>뒤로</b>
        /// 밀려 가려지기 때문이다.
        ///
        /// <b>프레임의 끝에서 한 번만 본다.</b> 패널 순서를 바꾸는 쪽이 <c>Update</c>에서 도니, 같은
        /// 프레임의 <c>LateUpdate</c>에서 되돌리면 화면에 한 프레임도 가려진 모습이 나가지 않는다.
        ///
        /// <b>떠 있을 때만, 순서를 바꿀 때만 손댄다.</b> 인스턴스를 만들지도, 다시 바인딩하지도, 클릭
        /// 때문에 숨기지도 않는다 - 이미 맨 뒤면 <c>SetAsLastSibling</c>조차 부르지 않아 Hover와
        /// 레이아웃 재계산을 건드리지 않는다.
        /// </summary>
        private void LateUpdate()
        {
            if (!IsVisible) return;

            Transform parent = instanceRect.parent;
            if (parent == null) return;

            if (instanceRect.GetSiblingIndex() == parent.childCount - 1) return;

            instanceRect.SetAsLastSibling();
        }

        /// <summary>로컬라이징 문자열이 뒤늦게 도착해 높이가 달라졌을 때 위치를 다시 잡는다.
        /// 이 신호가 없으면 설명이 길어진 만큼 툴팁의 아래쪽이 화면 밖으로 나간다.</summary>
        private void OnViewLayoutChanged()
        {
            if (visibleOwner == null || visibleTarget == null) return;
            if (instanceRect == null || !instanceRect.gameObject.activeSelf) return;

            Place(visibleTarget);
        }

        /// <summary>
        /// 슬롯의 <b>오른쪽을 먼저</b> 시도하고, 그쪽이 화면 밖으로 나가면 왼쪽으로 넘긴다. 위쪽은
        /// 슬롯의 윗변에 맞추되 아래/위로 넘치면 화면 안으로 당긴다 - 격자의 마지막 열과 마지막 줄에
        /// 있는 슬롯에서도 툴팁 전체가 보이는 이유가 이것이다.
        ///
        /// 계산은 전부 <b>부모의 로컬 좌표</b>에서 한다. 월드 좌표로 더하고 빼면 Canvas 배율이 바뀔 때
        /// 여백만 함께 커지거나 작아진다.
        /// </summary>
        private void Place(RectTransform target)
        {
            RectTransform placement = view.PlacementRect;
            if (placement == null) return;

            var parent = placement.parent as RectTransform;
            if (parent == null) return;

            view.RebuildLayout();

            Rect rect = placement.rect;
            float width = rect.width;
            float height = rect.height;

            target.GetWorldCorners(targetCorners);

            // GetWorldCorners는 좌하-좌상-우상-우하 순서다.
            Vector2 targetMin = parent.InverseTransformPoint(targetCorners[0]);
            Vector2 targetMax = parent.InverseTransformPoint(targetCorners[2]);

            if (!TryGetClampBounds(parent, out Vector2 boundsMin, out Vector2 boundsMax))
            {
                boundsMin = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                boundsMax = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            }

            float left = targetMax.x + horizontalOffset;
            if (left + width > boundsMax.x)
            {
                // 오른쪽에 자리가 없으면 왼쪽으로 넘긴다. 왼쪽도 좁으면 아래의 clamp가 화면 안으로 당긴다.
                left = targetMin.x - horizontalOffset - width;
            }

            float top = targetMax.y;

            if (boundsMax.x - boundsMin.x >= width)
            {
                left = Mathf.Clamp(left, boundsMin.x, boundsMax.x - width);
            }

            if (boundsMax.y - boundsMin.y >= height)
            {
                top = Mathf.Clamp(top, boundsMin.y + height, boundsMax.y);
            }

            // pivot이 어디에 있든 같은 결과가 되도록, 왼쪽-위 모서리에서 pivot 지점을 되짚어 계산한다.
            var pivotPoint = new Vector2(
                left + placement.pivot.x * width,
                top - (1f - placement.pivot.y) * height);

            placement.position = parent.TransformPoint(pivotPoint);
        }

        /// <summary>툴팁이 넘어가면 안 되는 범위를 부모 로컬 좌표로 돌려준다. 기준은 루트 Canvas다 -
        /// 부모(Panel_UI) 자체가 화면보다 작게 잡혀 있어도 툴팁은 화면 전체를 쓸 수 있어야 한다.</summary>
        private bool TryGetClampBounds(RectTransform parent, out Vector2 min, out Vector2 max)
        {
            min = default;
            max = default;

            Canvas canvas = parent.GetComponentInParent<Canvas>();
            if (canvas == null) return false;

            var canvasRect = canvas.rootCanvas.transform as RectTransform;
            if (canvasRect == null) return false;

            canvasRect.GetWorldCorners(boundsCorners);
            min = parent.InverseTransformPoint(boundsCorners[0]);
            max = parent.InverseTransformPoint(boundsCorners[2]);
            return true;
        }

        private bool EnsureInstance()
        {
            if (instanceRect != null) return true;
            if (instantiateFailed) return false;

            if (tooltipPrefab == null)
            {
                Debug.LogError($"[ItemTooltipController] '{name}': 아이템 툴팁 프리팹이 연결되지 " +
                               "않았습니다 - Inspector에서 item_ToolTip을 연결하세요.", this);
                instantiateFailed = true;
                return false;
            }

            RectTransform parent = ResolveTooltipRoot();
            if (parent == null)
            {
                Debug.LogError($"[ItemTooltipController] '{name}': 툴팁을 붙일 부모를 찾지 못했습니다 - " +
                               "Inspector에서 Tooltip Root를 연결하세요(슬롯 영역의 Mask 밖이어야 합니다).", this);
                instantiateFailed = true;
                return false;
            }

            GameObject instance = Instantiate(tooltipPrefab, parent);
            instanceRect = instance.transform as RectTransform;

            if (instanceRect == null)
            {
                Debug.LogError($"[ItemTooltipController] '{name}': 툴팁 프리팹 루트에 RectTransform이 " +
                               "없습니다 - UI 프리팹이어야 합니다.", this);
                Destroy(instance);
                instantiateFailed = true;
                return false;
            }

            view = instance.GetComponent<ItemTooltipView>();
            if (view == null)
            {
                Debug.LogError($"[ItemTooltipController] '{name}': 툴팁 프리팹 루트에 ItemTooltipView가 " +
                               "없습니다 - 프리팹에 컴포넌트를 붙이고 참조를 연결하세요.", this);
                Destroy(instance);
                instanceRect = null;
                instantiateFailed = true;
                return false;
            }

            // 툴팁은 클릭도 Hover도 받지 않는다. 켜 두면 슬롯 위에 겹쳤을 때 Hover가 끊기고 스크롤을 먹는다.
            foreach (Graphic graphic in instance.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }

            WarnIfInstanceTakesInput(instance);

            view.LayoutChanged += OnViewLayoutChanged;
            instance.SetActive(false);
            return true;
        }

        /// <summary>툴팁 안에 입력을 받는 컴포넌트가 있으면 알린다. Raycast Target을 전부 꺼도
        /// <see cref="WindowInputRegion"/>은 Unity UI가 아니라 창 전체의 클릭 관통을 다루므로 따로 본다.</summary>
        private static void WarnIfInstanceTakesInput(GameObject instance)
        {
            if (instance.GetComponentInChildren<Button>(true) != null)
            {
                Debug.LogError($"[ItemTooltipController] 툴팁 프리팹 '{instance.name}' 안에 Button이 " +
                               "있습니다 - 툴팁은 입력을 받지 않아야 합니다.", instance);
            }

            if (instance.GetComponentInChildren<WindowInputRegion>(true) != null)
            {
                Debug.LogError($"[ItemTooltipController] 툴팁 프리팹 '{instance.name}' 안에 " +
                               "WindowInputRegion이 있습니다 - 툴팁은 클릭 관통 영역을 등록하지 않습니다.",
                    instance);
            }
        }

        /// <summary>툴팁을 붙일 부모. 지정이 없으면 이 패널의 부모(Panel_UI)를 쓴다 - 패널 안쪽은
        /// 어디든 Mask 아래라 툴팁이 잘린다.</summary>
        private RectTransform ResolveTooltipRoot()
        {
            if (tooltipRoot != null) return tooltipRoot;

            return transform.parent as RectTransform;
        }

        private void CancelPendingRoutine()
        {
            if (showRoutine == null) return;

            StopCoroutine(showRoutine);
            showRoutine = null;
        }

        private void HideInstance()
        {
            if (instanceRect == null) return;

            // 내용을 비우면서 로컬라이징 구독도 함께 끊긴다.
            view?.Clear();
            instanceRect.gameObject.SetActive(false);
        }
    }
}
