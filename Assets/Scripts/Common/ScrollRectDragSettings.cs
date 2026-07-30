using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// ScrollRect의 <b>마우스 클릭&amp;드래그 스크롤</b>만 켜고 끄는 재사용 컴포넌트. 스크롤 UI마다
    /// 하나씩 붙여 개별로 설정하며, 전역에서 모든 ScrollRect를 강제하지 않는다.
    ///
    /// <code>
    /// Allow Pointer Drag = On  -> 클릭&amp;드래그 스크롤 O, 휠 스크롤 O  (ScrollRect 기본 동작)
    /// Allow Pointer Drag = Off -> 클릭&amp;드래그 스크롤 X, 휠 스크롤 O
    /// </code>
    ///
    /// 기본값은 On이다 - 이미 있는 다른 스크롤 UI에 이 컴포넌트를 붙였을 때 동작이 바뀌지 않게 하기 위함이다.
    ///
    /// <b>붙이는 위치는 Viewport다</b>(ScrollRect가 붙어 있는 오브젝트의 자식이면서 리스트 항목의 조상).
    /// Unity EventSystem은 드래그를 처리할 대상을 "포인터가 누른 오브젝트에서 위로 올라가며 만나는 첫
    /// IDragHandler"로 정하므로, ScrollRect보다 아래에 있는 이 컴포넌트가 먼저 잡혀서 드래그 이벤트가
    /// ScrollRect까지 올라가지 않는다. On일 때는 받은 이벤트를 ScrollRect에 그대로 넘겨주므로 기존
    /// 동작이 유지된다.
    ///
    /// <b>휠 스크롤은 건드리지 않는다.</b> 휠은 IScrollHandler로 전달되고 이 컴포넌트는 그 인터페이스를
    /// 구현하지 않으므로, 휠 이벤트는 지금까지대로 ScrollRect까지 올라간다. Scroll Sensitivity를 0으로
    /// 두거나 ScrollRect.enabled를 끄는 방식은 휠까지 함께 막으므로 쓰지 않는다.
    ///
    /// <b>패널 드래그와 충돌하지 않는다.</b> 패널 이동은 타이틀 영역(bg_top)의 PanelDragHandle이 담당하고
    /// 그쪽은 이 컴포넌트의 조상이 아니라 다른 가지에 있다 - 리스트에서 드래그를 막아도 타이틀 드래그는
    /// 그대로 동작한다.
    ///
    /// 알려진 한 가지 경계: 리스트 항목이 Viewport를 다 덮지 않아 ScrollRect 오브젝트 자신의 배경
    /// (list의 Image)이 직접 눌리는 경우에는 이벤트가 Viewport를 거치지 않으므로 막히지 않는다. 다만
    /// 그 상황은 Content가 Viewport보다 작아 스크롤할 것이 없는 경우이므로(특히 Movement Type = Clamped)
    /// 실제로 화면이 움직이지는 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ScrollRectDragSettings : MonoBehaviour,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Tooltip("끄면 마우스 클릭&드래그로는 스크롤되지 않는다(휠 스크롤은 그대로 유지된다). " +
                 "기본값 On은 기존 ScrollRect 동작과 같다.")]
        [SerializeField] private bool allowPointerDrag = true;

        [Tooltip("드래그를 전달할 ScrollRect. 비워두면 부모 계층에서 찾는다(보통 이 Viewport의 부모).")]
        [SerializeField] private ScrollRect targetScrollRect;

        // 지금 ScrollRect로 넘겨주고 있는 드래그가 있는지. 도중에 설정이 꺼져도 ScrollRect가 드래그
        // 상태에 갇히지 않도록 종료를 한 번 넘겨주기 위해 들고 있는다.
        private bool forwardingDrag;
        private bool resolved;

        /// <summary>런타임에 켜고 끌 수 있다 - 드래그 도중에 꺼면 그 드래그는 즉시 정상 종료된다.</summary>
        public bool AllowPointerDrag
        {
            get => allowPointerDrag;
            set => allowPointerDrag = value;
        }

        private void Awake()
        {
            ResolveTarget();
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (!allowPointerDrag) return;

            ScrollRect target = ResolveTarget();
            if (target != null) target.OnInitializePotentialDrag(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            forwardingDrag = false;
            if (!allowPointerDrag) return;

            ScrollRect target = ResolveTarget();
            if (target == null) return;

            forwardingDrag = true;
            target.OnBeginDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!forwardingDrag) return;

            if (!allowPointerDrag)
            {
                EndForwardedDrag(eventData);
                return;
            }

            ScrollRect target = ResolveTarget();
            if (target != null) target.OnDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!forwardingDrag) return;
            EndForwardedDrag(eventData);
        }

        private void EndForwardedDrag(PointerEventData eventData)
        {
            forwardingDrag = false;

            ScrollRect target = ResolveTarget();
            if (target != null) target.OnEndDrag(eventData);
        }

        /// <summary>대상 ScrollRect를 한 번만 찾아 둔다. 부모 계층 조회는 이름 탐색이 아니라 이
        /// 컴포넌트가 붙은 자리(Viewport)의 구조적 관계이므로 안전하다 - Inspector에 직접 연결해 두면
        /// 그 값을 그대로 쓴다.</summary>
        private ScrollRect ResolveTarget()
        {
            if (targetScrollRect != null) return targetScrollRect;
            if (resolved) return null;
            resolved = true;

            targetScrollRect = GetComponentInParent<ScrollRect>(true);
            if (targetScrollRect == null)
            {
                // 참조가 없으면 On이어도 드래그를 넘길 곳이 없어 "조용히 막힌" 상태가 된다 - 반드시 알린다.
                Debug.LogError($"[ScrollRectDragSettings] '{name}': 상위에서 ScrollRect를 찾지 못했습니다 - " +
                               "이 컴포넌트는 Viewport처럼 ScrollRect의 자식 오브젝트에 붙여야 합니다. " +
                               "지금 상태에서는 Allow Pointer Drag가 켜져 있어도 드래그 스크롤이 동작하지 않습니다.", this);
            }
            return targetScrollRect;
        }
    }
}
