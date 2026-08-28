using UnityEngine;
using UnityEngine.EventSystems;

namespace Common
{
    /// <summary>
    /// 패널의 상단 타이틀 영역에 붙여, 그 영역을 드래그하면 패널이 따라오게 하는 <b>공용</b>
    /// 드래그 핸들. 배치 모드나 이동 활성화 버튼 없이, 패널이 켜져 있는 동안에는 항상 드래그된다.
    /// 패널 종류를 전제하지 않고 정적 인스턴스도 쓰지 않으므로 패널마다 하나씩 붙여 쓰면 된다.
    ///
    /// <b>ModalPanel과 독립적이다.</b> 열기/닫기/InputBlocker와 아무 것도 주고받지 않고, ModalPanel이
    /// 없는 UI에도 그대로 붙일 수 있다. 다만 대상이 ModalPanel이고 <see cref="PopupPanelManager"/>가
    /// 있으면 "맨 앞으로 올리기"만 그쪽에 맡긴다 - 화면 순서와 ESC 닫기 순서가 어긋나지 않게 하기
    /// 위한 것이며, 드래그 자체는 여전히 이 컴포넌트가 단독으로 처리한다.
    ///
    /// <b>닫기 버튼은 자연히 제외된다.</b> Unity EventSystem은 드래그를 처리할 대상을 "포인터가 누른
    /// 오브젝트에서 위로 올라가며 만나는 첫 IDragHandler"로 정한다. btn_close는 이 핸들(bg_top)의
    /// 자식이 아니라 형제라서 위로 올라가도 이 컴포넌트를 만나지 않는다 - 닫기 버튼을 끌어도 패널이
    /// 움직이지 않고, 클릭은 지금까지대로 동작한다. 반대로 타이틀 텍스트(lb_title)처럼 핸들의
    /// <b>자식</b>인 장식 오브젝트는 Raycast Target이 켜져 있어도 드래그가 여기로 올라오므로,
    /// 그것들의 설정을 바꿀 필요가 없다.
    ///
    /// 좌표는 화면 좌표를 패널 부모의 로컬 좌표로 변환해서 다룬다
    /// (<see cref="RectTransformUtility.ScreenPointToLocalPointInRectangle"/> + pressEventCamera) -
    /// 월드 좌표나 고정 픽셀 보정을 쓰지 않으므로 Canvas의 Render Mode나 Canvas Scaler 설정이 달라도
    /// 같은 결과가 나온다. 이동은 "누른 순간의 위치에서 그동안 포인터가 움직인 만큼"만 더하는 방식이라
    /// 포인터를 아무리 빨리 움직여도 패널이 튀지 않고, 누른 지점과 패널 사이의 간격이 유지된다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class PanelDragHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Tooltip("드래그로 움직일 패널의 RectTransform(보통 패널 루트). 이 핸들 자신이 아니라 " +
                 "패널 전체를 넣는다.")]
        [SerializeField] private RectTransform targetPanel;

        [Tooltip("항상 화면 안에 남아 있어야 하는 영역. 비워두면 이 핸들의 부모(타이틀 행 = 타이틀바 + " +
                 "닫기 버튼)를 쓰고, 부모도 없으면 핸들 자신을 쓴다.")]
        [SerializeField] private RectTransform keepInsideRect;

        [Tooltip("도킹할 때 패널의 시각 외곽으로 쓸 영역. 비워두면 Target Panel 자체를 사용한다.")]
        [SerializeField] private RectTransform snapBoundsRect;

        // 패널의 좌표계 기준이 되는 부모. anchoredPosition의 변화량과 이 공간의 이동량이 1:1이라
        // 앵커 설정(점 앵커/스트레치)에 상관없이 같은 계산을 쓸 수 있다.
        private RectTransform parentRect;

        // 이동 범위를 제한할 기준 영역(루트 Canvas).
        private RectTransform canvasRect;

        private readonly Vector3[] worldCorners = new Vector3[4];

        // 대상 패널이 ModalPanel이면 활성 순서를 PopupPanelManager에 맡긴다(없으면 null).
        private ModalPanel targetModalPanel;

        private Vector2 dragStartLocalPoint;
        private Vector2 dragStartAnchoredPosition;
        private bool dragging;
        private bool resolved;

        /// <summary>이 핸들이 움직이는 패널 루트.</summary>
        public RectTransform TargetPanel => targetPanel;

        /// <summary>도킹의 좌우 외곽과 상단을 계산할 영역.</summary>
        public RectTransform SnapBoundsRect => snapBoundsRect != null ? snapBoundsRect : targetPanel;

        private void Awake()
        {
            ResolveReferences();
        }

        /// <summary>드래그가 아닌 단순 클릭에도 이 패널을 맨 앞으로 올린다 - "마지막으로 만진 패널이
        /// 앞"이 되게 한다.
        ///
        /// 대상이 <see cref="ModalPanel"/>이면 순서 변경을 <see cref="PopupPanelManager"/>에 맡긴다 -
        /// 여기서 sibling만 직접 바꾸면 화면 순서와 관리자의 ESC 닫기 순서가 어긋난다. 관리자나
        /// ModalPanel이 없는 UI에 붙인 경우에는 예전처럼 직접 맨 앞으로 옮긴다.</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            ResolveReferences();
            if (targetPanel == null) return;
            FocusPanel();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            ResolveReferences();
            dragging = false;
            if (targetPanel == null || parentRect == null) return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, eventData.position, eventData.pressEventCamera, out dragStartLocalPoint))
            {
                return;
            }

            dragStartAnchoredPosition = targetPanel.anchoredPosition;
            dragging = true;
            FindDockManager()?.BeginPanelDrag(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging) return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                return;
            }

            // 매 프레임의 절대 위치가 아니라 "누른 순간부터의 이동량"을 더한다 - 한 프레임에 포인터가
            // 크게 움직여도 패널이 그만큼만 따라가고, 누른 지점과의 간격이 그대로 유지된다.
            targetPanel.anchoredPosition = dragStartAnchoredPosition + (localPoint - dragStartLocalPoint);
            ClampIntoCanvas();
            FindDockManager()?.MovePanelDuringDrag(this, targetPanel.anchoredPosition);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (dragging) FindDockManager()?.EndPanelDrag(this);
            dragging = false;
        }

        private void OnDisable()
        {
            dragging = false;
            FindDockManager()?.NotifyPanelUnavailable(this);
        }

        private void OnDestroy()
        {
            FindDockManager()?.NotifyPanelUnavailable(this);
        }

        /// <summary>공용 도킹 핸들이 두 패널을 함께 옮길 때 쓰는 화면 이탈 제한값이다.</summary>
        public Vector2 ClampMoveDelta(Vector2 requestedDelta)
        {
            ResolveReferences();
            if (canvasRect == null || keepInsideRect == null || parentRect == null) return requestedDelta;

            GetRectInParentSpace(canvasRect, out Vector2 canvasMin, out Vector2 canvasMax);
            GetRectInParentSpace(keepInsideRect, out Vector2 keepMin, out Vector2 keepMax);

            float minX = canvasMin.x - keepMin.x;
            float maxX = canvasMax.x - keepMax.x;
            float minY = canvasMin.y - keepMin.y;
            float maxY = canvasMax.y - keepMax.y;

            // 기존 위치가 이미 경계를 약간 벗어나 있거나 영역이 Canvas보다 큰 경우에도, 현재 동작의
            // "완전히 사라지지 않음" 원칙을 깨지 않도록 가능한 쪽으로만 보정한다.
            requestedDelta.x = ClampAxis(requestedDelta.x, minX, maxX);
            requestedDelta.y = ClampAxis(requestedDelta.y, minY, maxY);
            return requestedDelta;
        }

        /// <summary>도킹 관리자가 두 패널에 <b>같은</b> 이동량을 적용할 때 사용한다.</summary>
        public void SetAnchoredPositionFromDock(Vector2 anchoredPosition)
        {
            ResolveReferences();
            if (targetPanel != null) targetPanel.anchoredPosition = anchoredPosition;
        }

        private static float ClampAxis(float value, float min, float max)
        {
            if (min <= max) return Mathf.Clamp(value, min, max);
            return Mathf.Abs(value - min) <= Mathf.Abs(value - max) ? min : max;
        }

        private void FocusPanel()
        {
            if (targetModalPanel != null && PopupPanelManager.Instance != null)
            {
                PopupPanelManager.Instance.FocusPanel(targetModalPanel);
                return;
            }

            targetPanel.SetAsLastSibling();
        }

        private PanelDockManager FindDockManager()
        {
            return targetPanel != null ? targetPanel.GetComponentInParent<PanelDockManager>() : null;
        }

        /// <summary>패널이 화면 밖으로 완전히 사라지지 않도록, <see cref="keepInsideRect"/>(기본값은
        /// 타이틀바 + 닫기 버튼이 들어 있는 타이틀 행)가 Canvas 영역 안에 남도록 위치를 되돌린다.
        /// 패널의 크기나 앵커는 건드리지 않고 anchoredPosition만 보정한다.</summary>
        private void ClampIntoCanvas()
        {
            if (canvasRect == null || keepInsideRect == null || parentRect == null) return;

            GetRectInParentSpace(canvasRect, out Vector2 canvasMin, out Vector2 canvasMax);
            GetRectInParentSpace(keepInsideRect, out Vector2 keepMin, out Vector2 keepMax);

            Vector2 delta = Vector2.zero;
            if (keepMin.x < canvasMin.x) delta.x = canvasMin.x - keepMin.x;
            else if (keepMax.x > canvasMax.x) delta.x = canvasMax.x - keepMax.x;

            if (keepMin.y < canvasMin.y) delta.y = canvasMin.y - keepMin.y;
            else if (keepMax.y > canvasMax.y) delta.y = canvasMax.y - keepMax.y;

            if (delta != Vector2.zero) targetPanel.anchoredPosition += delta;
        }

        /// <summary>임의의 RectTransform이 차지하는 영역을 패널 부모의 로컬 좌표로 옮겨 min/max로
        /// 돌려준다 - 이동 계산과 같은 좌표계에서 비교하기 위함이다.</summary>
        private void GetRectInParentSpace(RectTransform rect, out Vector2 min, out Vector2 max)
        {
            rect.GetWorldCorners(worldCorners); // 0=bottom-left, 2=top-right
            Vector2 a = parentRect.InverseTransformPoint(worldCorners[0]);
            Vector2 b = parentRect.InverseTransformPoint(worldCorners[2]);

            min = Vector2.Min(a, b);
            max = Vector2.Max(a, b);
        }

        private void ResolveReferences()
        {
            if (resolved) return;
            resolved = true;

            if (targetPanel == null)
            {
                Debug.LogError($"[PanelDragHandle] '{name}': 움직일 Target Panel이 연결되지 않았습니다 - " +
                               "Inspector에서 패널 루트(pn_CharacterSwap / pn_Inventory)를 연결하세요. " +
                               "이 핸들은 동작하지 않습니다.", this);
                return;
            }

            parentRect = targetPanel.parent as RectTransform;
            if (parentRect == null)
            {
                Debug.LogError($"[PanelDragHandle] '{name}': Target Panel의 부모가 RectTransform이 아니어서 " +
                               "이동 좌표를 계산할 수 없습니다.", this);
                return;
            }

            targetModalPanel = targetPanel.GetComponent<ModalPanel>();

            if (keepInsideRect == null)
            {
                // 기존 프리팹 중에는 핸들이 패널 루트에 직접 붙어 있는 경우가 있다. 그 경우 부모인
                // Panel_UI를 제한 영역으로 쓰면 패널을 아무 데로나 옮길 수 있으므로, 패널 자체를 쓴다.
                keepInsideRect = transform == targetPanel
                    ? targetPanel
                    : transform.parent as RectTransform;
                if (keepInsideRect == null) keepInsideRect = (RectTransform)transform;
            }

            // 비활성 상태에서도 찾을 수 있게 includeInactive로 조회한다(패널은 꺼진 채로 시작한다).
            Canvas canvas = targetPanel.GetComponentInParent<Canvas>(true);
            canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
            if (canvasRect == null)
            {
                Debug.LogWarning($"[PanelDragHandle] '{name}': 상위 Canvas를 찾지 못해 화면 밖 이동 제한이 " +
                                 "적용되지 않습니다.", this);
            }
        }
    }
}
