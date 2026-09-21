using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// HoverTooltip 프리팹 <b>인스턴스 하나를 재사용</b>해서 버튼 위쪽에 툴팁을 띄우는 컨트롤러.
    /// 매번 새로 만들지 않으므로 화면에 툴팁이 둘 이상 뜨는 경로가 구조적으로 없다 - 버튼을 아무리
    /// 빠르게 오가도 "지금 보여 줄 대상"이 바뀔 뿐이다.
    ///
    /// <b>표시 요청의 주인을 항상 기억한다.</b> 마우스가 버튼 A에서 B로 빠르게 넘어가면 EventSystem이
    /// 같은 프레임에 A의 Exit와 B의 Enter를 보내는데, 순서가 어느 쪽이든 <see cref="CancelShow"/>는
    /// 자기가 주인일 때만 동작한다. 그래서 뒤늦게 도착한 A의 Exit가 이미 예약된 B의 툴팁을 지우지 않는다.
    ///
    /// <b>위치는 버튼의 RectTransform 월드 코너로 잡는다.</b> 기본 메뉴는 버튼 위쪽에 표시하고,
    /// Companion 메뉴는 버튼 왼쪽을 우선하되 화면 공간이 부족하면 오른쪽으로 옮긴 뒤 경계 안에 맞춘다.
    /// CanvasScaler 배율이나 해상도가 달라져도 여백은 부모 로컬 단위(= Canvas 기준 해상도 픽셀)로 유지된다.
    ///
    /// <b>툴팁은 입력을 받지 않는다.</b> 인스턴스를 만들 때 안쪽 모든 Graphic의 Raycast Target을 끄므로,
    /// 툴팁이 버튼과 겹쳐도 Hover가 끊기거나 버튼 클릭을 가로채지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class HoverTooltipController : MonoBehaviour
    {
        private enum TooltipPlacement
        {
            Above,
            HorizontalAdaptive
        }

        [Tooltip("표시에 사용할 HoverTooltip 프리팹. 안쪽의 TextMeshProUGUI를 문구 표시에 사용한다.")]
        [SerializeField] private GameObject tooltipPrefab;

        [Tooltip("버튼 왼쪽에 표시할 HoverTooltip_R 프리팹. 비워두면 기본 HoverTooltip을 사용한다.")]
        [SerializeField] private GameObject tooltipOnLeftPrefab;

        [Tooltip("버튼 오른쪽에 표시할 HoverTooltip_L 프리팹. 비워두면 기본 HoverTooltip을 사용한다.")]
        [SerializeField] private GameObject tooltipOnRightPrefab;

        [Tooltip("툴팁 인스턴스를 붙일 부모. 비워두면 이 컴포넌트가 붙은 오브젝트를 사용한다. " +
                 "메뉴보다 앞에 그려져야 하므로 표시할 때마다 형제 중 맨 뒤로 보낸다.")]
        [SerializeField] private RectTransform tooltipRoot;

        [Tooltip("툴팁을 화면 안에 유지할 기준 RectTransform. 비워두면 Tooltip Root를 사용한다.")]
        [SerializeField] private RectTransform tooltipBounds;

        [Tooltip("기본 메뉴는 Above, Companion 인터렉션 메뉴는 Horizontal Adaptive를 사용한다.")]
        [SerializeField] private TooltipPlacement placement = TooltipPlacement.Above;

        [Tooltip("마우스를 올린 뒤 툴팁이 나타나기까지의 대기시간(초). 이 시간 안에 마우스가 " +
                 "벗어나면 예약이 취소되어 툴팁은 나타나지 않는다.")]
        [SerializeField] private float tooltipDelay = 1f;

        [Tooltip("버튼 위쪽 변과 툴팁 사이 여백(Canvas 기준 해상도 픽셀).")]
        [SerializeField] private float verticalOffset = 8f;

        [Tooltip("버튼 좌우 변과 툴팁 사이 여백(Canvas 기준 해상도 픽셀).")]
        [SerializeField] private float horizontalOffset = 8f;

        [Tooltip("툴팁을 화면 경계에서 떨어뜨릴 최소 여백(Canvas 기준 해상도 픽셀).")]
        [SerializeField] private float boundsPadding = 4f;

        [Header("대상 버튼")]
        [Tooltip("툴팁을 붙일 버튼들이 들어 있는 영역(btnArea). 비워두면 같은 오브젝트의 " +
                 "MenuBarExpander가 가리키는 펼침 영역을 쓴다.")]
        [SerializeField] private Transform menuRoot;

        [Tooltip("켜면 위 영역 안의 Button마다 HoverTooltipTrigger를 자동으로 붙인다. " +
                 "끄면 버튼마다 직접 붙여야 한다(문구를 버튼별로 다르게 지정하고 싶을 때는 " +
                 "자동으로 붙은 컴포넌트의 Tooltip Text를 채우면 된다).")]
        [SerializeField] private bool autoAttachTriggers = true;

        // 버튼 RectTransform의 월드 코너를 받는 재사용 버퍼(툴팁을 띄울 때마다 할당하지 않는다).
        private readonly Vector3[] targetCorners = new Vector3[4];

        private RectTransform tooltipRect;
        private TextMeshProUGUI tooltipLabel;
        private GameObject instantiatedPrefab;
        private bool instantiateFailed;

        // 표시를 예약했거나 이미 표시 중인 대상. 둘 중 하나만 값을 가진다.
        private HoverTooltipTrigger pendingSource;
        private HoverTooltipTrigger visibleSource;
        private Coroutine showRoutine;

        /// <summary>Inspector에 설정된 대기시간(초). 트리거 쪽에서 읽을 일은 없고 진단용이다.</summary>
        public float TooltipDelay => tooltipDelay;

        private void Awake()
        {
            if (menuRoot == null)
            {
                var expander = GetComponent<MenuBarExpander>();
                if (expander != null && expander.ExpandedRoot != null) menuRoot = expander.ExpandedRoot.transform;
            }

            if (menuRoot == null)
            {
                Debug.LogWarning($"[HoverTooltipController] '{name}': 대상 영역(Menu Root)을 찾지 못해 " +
                                 "툴팁 트리거를 붙이지 못했습니다 - Inspector에서 btnArea를 연결하거나 " +
                                 "버튼마다 HoverTooltipTrigger를 직접 붙이세요.", this);
                return;
            }

            if (autoAttachTriggers) AttachTriggers();
            else BindExistingTriggers();
        }

        /// <summary>대상 영역 안의 Button마다 <see cref="HoverTooltipTrigger"/>를 보장한다.
        ///
        /// 버튼마다 손으로 붙이는 방식은 하나만 빠뜨려도 <b>오류 한 줄 없이 그 버튼만 툴팁이 안 뜨고</b>,
        /// 전부 빠뜨리면 기능 자체가 조용히 없는 것처럼 보인다 - 실제로 1차 적용에서 이 컴포넌트가
        /// 한 개도 붙지 않아 툴팁이 전혀 뜨지 않았다. 붙이는 일을 여기서 대신해 그 실패를 없앤다.
        ///
        /// 이미 붙어 있는 것은 건드리지 않으므로, 버튼별로 문구를 지정해 둔 트리거는 그대로 유지된다.
        /// 나중에 하위 메뉴 버튼이 이 영역 아래에 생겨도 같은 규칙으로 함께 잡힌다.</summary>
        private void AttachTriggers()
        {
            var buttons = menuRoot.GetComponentsInChildren<Button>(true);
            if (buttons.Length == 0)
            {
                Debug.LogWarning($"[HoverTooltipController] '{name}': '{menuRoot.name}' 안에서 Button을 " +
                                 "찾지 못했습니다 - 툴팁이 표시될 버튼이 없습니다.", this);
                return;
            }

            foreach (Button button in buttons)
            {
                HoverTooltipTrigger trigger = button.GetComponent<HoverTooltipTrigger>();
                if (trigger == null) trigger = button.gameObject.AddComponent<HoverTooltipTrigger>();
                trigger.BindController(this);
            }
        }

        /// <summary>수동으로 구성한 트리거를 이 메뉴의 컨트롤러에 명시적으로 연결한다.
        /// MainMenu와 CompanionInteractionMenu가 같은 TooltipLayer를 사용하더라도, 트리거가 다른
        /// 메뉴의 방향 프리팹 설정을 가져갈 수 없도록 소유권을 메뉴 루트 단위로 고정한다.</summary>
        private void BindExistingTriggers()
        {
            foreach (HoverTooltipTrigger trigger in menuRoot.GetComponentsInChildren<HoverTooltipTrigger>(true))
            {
                trigger.BindController(this);
            }
        }

        /// <summary><paramref name="source"/> 버튼의 툴팁 표시를 예약한다. 이전 예약이나 표시 중인
        /// 툴팁은 즉시 정리되므로, 같은 순간에 살아 있는 툴팁은 항상 하나뿐이다.
        ///
        /// 문구는 여기서 읽지 않고 <b>대기시간이 끝난 뒤에</b> 트리거에서 읽는다 - 로컬라이징 로드가
        /// 비동기라 Hover 시점에는 아직 비어 있을 수 있고, 그때 예약을 버리면 툴팁이 조용히 안 뜬다.</summary>
        public void RequestShow(HoverTooltipTrigger source)
        {
            if (source == null) return;

            CancelPendingRoutine();
            HideInstance();
            visibleSource = null;

            pendingSource = source;

            if (tooltipDelay <= 0f)
            {
                ShowNow(source);
                return;
            }

            showRoutine = StartCoroutine(ShowAfterDelay(source));
        }

        /// <summary><paramref name="source"/>가 지금 툴팁의 주인일 때만 예약을 취소하고 툴팁을 지운다.
        /// 다른 버튼이 이미 주인이 되었다면 아무것도 하지 않는다.</summary>
        public void CancelShow(HoverTooltipTrigger source)
        {
            if (source == null) return;
            if (pendingSource != source && visibleSource != source) return;

            Hide();
        }

        /// <summary>예약과 표시를 모두 없앤다. 메뉴가 접히거나 이 컨트롤러가 꺼질 때도 같은 경로를 지난다.</summary>
        public void Hide()
        {
            CancelPendingRoutine();
            pendingSource = null;
            visibleSource = null;
            HideInstance();
        }

        private void OnDisable()
        {
            // 컴포넌트가 꺼지면 코루틴은 Unity가 멈추지만, 남은 상태와 화면의 툴팁은 직접 정리해야 한다.
            // Tooltip Root가 메뉴 밖의 공용 TooltipLayer여도 인스턴스를 여기서 직접 숨긴다.
            Hide();
        }

        private IEnumerator ShowAfterDelay(HoverTooltipTrigger source)
        {
            // Time.timeScale과 무관하게 항상 같은 체감 대기시간이 되도록 실제 시간으로 센다.
            yield return new WaitForSecondsRealtime(tooltipDelay);

            showRoutine = null;
            ShowNow(source);
        }

        private void ShowNow(HoverTooltipTrigger source)
        {
            // 대기 중에 버튼이 꺼졌거나(메뉴가 접힘) 파괴됐으면 띄우지 않는다.
            if (source == null || !source.isActiveAndEnabled || source.TargetRect == null)
            {
                Hide();
                return;
            }

            string text = source.TooltipText;
            if (string.IsNullOrEmpty(text))
            {
                Hide();
                return;
            }

            pendingSource = null;
            visibleSource = source;

            if (placement == TooltipPlacement.HorizontalAdaptive)
                ShowHorizontal(source.TargetRect, text);
            else
                ShowAbove(source.TargetRect, text);
        }

        private void ShowAbove(RectTransform target, string text)
        {
            if (!PrepareTooltip(tooltipPrefab, text))
            {
                Hide();
                return;
            }

            tooltipRect.pivot = new Vector2(0.5f, 0f);
            PlaceAbove(target);
        }

        private void ShowHorizontal(RectTransform target, string text)
        {
            RectTransform boundsRect = ResolveBounds();
            if (boundsRect == null)
            {
                ShowAbove(target, text);
                return;
            }

            if (!PrepareTooltip(ResolveDirectionalPrefab(true), text))
            {
                Hide();
                return;
            }

            target.GetWorldCorners(targetCorners);
            float targetMinX = float.PositiveInfinity;
            float targetMaxX = float.NegativeInfinity;
            for (int i = 0; i < targetCorners.Length; i++)
            {
                float x = boundsRect.InverseTransformPoint(targetCorners[i]).x;
                targetMinX = Mathf.Min(targetMinX, x);
                targetMaxX = Mathf.Max(targetMaxX, x);
            }

            Rect bounds = boundsRect.rect;
            float padding = Mathf.Max(0f, boundsPadding);
            float gap = Mathf.Max(0f, horizontalOffset);
            float leftSpace = targetMinX - bounds.xMin - padding - gap;
            float rightSpace = bounds.xMax - padding - targetMaxX - gap;
            bool placeOnLeft = ChooseLeftSide(leftSpace, rightSpace, tooltipRect.rect.width);

            if (!placeOnLeft && !PrepareTooltip(ResolveDirectionalPrefab(false), text))
            {
                Hide();
                return;
            }

            tooltipRect.pivot = placeOnLeft ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
            tooltipRect.position = placeOnLeft
                ? (targetCorners[0] + targetCorners[1]) * 0.5f
                : (targetCorners[2] + targetCorners[3]) * 0.5f;
            tooltipRect.anchoredPosition += new Vector2(placeOnLeft ? -gap : gap, 0f);
            ClampInsideBounds(tooltipRect, boundsRect, padding);
        }

        private bool PrepareTooltip(GameObject prefab, string text)
        {
            if (!EnsureInstance(prefab)) return false;
            if (tooltipLabel != null) tooltipLabel.text = text;

            tooltipRect.gameObject.SetActive(true);
            tooltipRect.SetAsLastSibling();

            // ContentSizeFitter가 다음 프레임에 크기를 잡으면 한 프레임 어긋난 크기가 보인다 - 지금 맞춘다.
            LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);
            return true;
        }

        private static bool ChooseLeftSide(float leftSpace, float rightSpace, float tooltipWidth)
        {
            if (leftSpace >= tooltipWidth) return true;
            if (rightSpace >= tooltipWidth) return false;
            return leftSpace >= rightSpace;
        }

        private GameObject ResolveDirectionalPrefab(bool placeOnLeft)
        {
            GameObject directional = placeOnLeft ? tooltipOnLeftPrefab : tooltipOnRightPrefab;
            return directional != null ? directional : tooltipPrefab;
        }

        private RectTransform ResolveBounds()
        {
            if (tooltipBounds != null) return tooltipBounds;
            if (tooltipRoot != null) return tooltipRoot;
            return transform as RectTransform;
        }

        private static void ClampInsideBounds(RectTransform tooltip, RectTransform boundsRect, float padding)
        {
            Bounds tooltipInBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(boundsRect, tooltip);
            Rect bounds = boundsRect.rect;
            float minX = bounds.xMin + padding;
            float maxX = bounds.xMax - padding;
            float minY = bounds.yMin + padding;
            float maxY = bounds.yMax - padding;
            Vector3 correction = Vector3.zero;

            if (tooltipInBounds.size.x <= maxX - minX)
            {
                if (tooltipInBounds.min.x < minX) correction.x = minX - tooltipInBounds.min.x;
                else if (tooltipInBounds.max.x > maxX) correction.x = maxX - tooltipInBounds.max.x;
            }
            else correction.x = bounds.center.x - tooltipInBounds.center.x;

            if (tooltipInBounds.size.y <= maxY - minY)
            {
                if (tooltipInBounds.min.y < minY) correction.y = minY - tooltipInBounds.min.y;
                else if (tooltipInBounds.max.y > maxY) correction.y = maxY - tooltipInBounds.max.y;
            }
            else correction.y = bounds.center.y - tooltipInBounds.center.y;

            tooltip.position += boundsRect.TransformVector(correction);
        }

        private void PlaceAbove(RectTransform target)
        {
            target.GetWorldCorners(targetCorners);

            // GetWorldCorners는 좌하-좌상-우상-우하 순서다. 위쪽 변의 중앙이 툴팁이 붙을 지점이다.
            Vector3 topCenter = (targetCorners[1] + targetCorners[2]) * 0.5f;

            tooltipRect.position = topCenter;

            // 여백은 월드 단위가 아니라 부모 로컬 단위로 더한다 - Canvas 배율이 바뀌어도 같은 여백이 된다.
            tooltipRect.anchoredPosition += new Vector2(0f, verticalOffset);
        }

        private bool EnsureInstance(GameObject prefab)
        {
            if (tooltipRect != null && instantiatedPrefab == prefab) return true;
            if (tooltipRect != null) ReleaseInstance();
            if (instantiateFailed && prefab == null) return false;

            if (prefab == null)
            {
                Debug.LogError($"[HoverTooltipController] '{name}': HoverTooltip 프리팹이 연결되지 " +
                               "않았습니다 - Inspector에서 연결하세요.", this);
                instantiateFailed = true;
                return false;
            }

            Transform parent = tooltipRoot != null ? tooltipRoot : transform;
            GameObject instance = Instantiate(prefab, parent);
            tooltipRect = instance.transform as RectTransform;

            if (tooltipRect == null)
            {
                Debug.LogError($"[HoverTooltipController] '{name}': HoverTooltip 프리팹 루트에 " +
                               "RectTransform이 없습니다 - UI 프리팹이어야 합니다.", this);
                Destroy(instance);
                instantiateFailed = true;
                return false;
            }

            instantiatedPrefab = prefab;
            tooltipLabel = instance.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tooltipLabel == null)
            {
                Debug.LogWarning($"[HoverTooltipController] '{name}': HoverTooltip 프리팹 안에 " +
                                 "TextMeshProUGUI가 없어 문구를 표시할 수 없습니다.", this);
            }

            // 툴팁은 클릭도 Hover도 받을 필요가 없다. 켜 두면 버튼 위에 겹쳤을 때 Hover가 끊긴다.
            foreach (Graphic graphic in instance.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }

            instance.SetActive(false);
            return true;
        }

        private void ReleaseInstance()
        {
            if (tooltipRect != null)
            {
                tooltipRect.gameObject.SetActive(false);
                Destroy(tooltipRect.gameObject);
            }

            tooltipRect = null;
            tooltipLabel = null;
            instantiatedPrefab = null;
        }

        private void CancelPendingRoutine()
        {
            if (showRoutine == null) return;

            StopCoroutine(showRoutine);
            showRoutine = null;
        }

        private void HideInstance()
        {
            if (tooltipRect == null) return;

            tooltipRect.gameObject.SetActive(false);
        }
    }
}
