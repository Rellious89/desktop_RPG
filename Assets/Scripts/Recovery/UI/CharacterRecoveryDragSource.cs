using Character;
using Common;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Recovery
{
    /// <summary>
    /// 캐릭터 교체 리스트 항목(list_Character)에 붙여, 그 항목을 회복소 슬롯으로 <b>끌어다 놓을 수</b>
    /// 있게 하는 컴포넌트. 어떤 캐릭터인지는 같은 오브젝트의 <see cref="CharacterSwapListItem"/>에서
    /// 읽는다 - 자기 사본을 들고 있지 않으므로 리스트가 다시 그려져도 어긋나지 않는다.
    ///
    /// <b>스크롤을 망가뜨리지 않는 것이 이 컴포넌트의 가장 중요한 책임이다.</b>
    /// Unity EventSystem은 드래그 대상을 "누른 오브젝트에서 위로 올라가며 만나는 첫 IDragHandler"로
    /// 정한다. 이 컴포넌트는 리스트 항목(= ScrollRect보다 아래)에 있으므로, 아무 처리도 하지 않으면
    /// 항목 위에서 시작한 드래그가 ScrollRect까지 올라가지 못해 <b>세로 스크롤이 죽는다</b>.
    /// 그래서 <see cref="ScrollRectDragSettings"/>와 같은 전달(forward) 방식을 쓴다.
    ///
    /// <b>제스처 판정은 3-상태 기계다.</b>
    /// <code>
    /// OnBeginDrag
    ///   좌클릭 아님 / 등록 불가 / 세로 우세          -> Scroll   (ScrollRect에 Begin 전달)
    ///   가로 우세 + 가로 이동 >= 임계값               -> Recovery (고스트 생성)
    ///   가로 우세 + 가로 이동 &lt; 임계값             -> Undecided(아무 것도 전달하지 않고 보류)
    ///
    /// OnDrag (Undecided인 동안에만 재평가한다)
    ///   가로 우세 + 가로 이동 >= 임계값               -> Recovery (고스트 생성)
    ///   세로 우세로 바뀜                              -> Scroll   (여기서 Begin을 전달한 뒤 Drag 전달)
    ///   그 외                                         -> Undecided 유지
    /// </code>
    /// Begin 한 번만 보고 확정하면 <b>천천히 가로로 끄는 제스처</b>가 임계값에 닿기 전에 스크롤로
    /// 굳어져 회복 드래그가 영영 시작되지 않는다. 그래서 판정을 보류할 수 있게 했다.
    ///
    /// <b>한 번 Recovery나 Scroll로 정해지면 그 드래그가 끝날 때까지 바꾸지 않는다.</b> 중간에 갈아타면
    /// ScrollRect에 Begin 없이 Drag가 가거나 End가 두 번 가는 상태가 만들어진다.
    ///
    /// <b>ScrollRect로 보내는 Begin/Drag/End는 정확히 한 번씩 균형을 이룬다.</b> 전달을 시작했으면
    /// 정상 종료든 취소(리스트 재생성/패널 닫기/컴포넌트 비활성)든 End를 반드시 한 번 보낸다.
    ///
    /// <b>단순 클릭은 기존 선택 동작 그대로다.</b> 클릭에는 OnBeginDrag 자체가 오지 않는다. 회복
    /// 드래그가 시작되면 그 제스처의 클릭만 삼키고(<see cref="ShouldSuppressClick"/>), 억제는
    /// <b>드래그가 끝난 프레임까지만</b> 유효하다 - 다음에 사용자가 새로 누르는 클릭은 정상 동작한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterSwapListItem))]
    public class CharacterRecoveryDragSource : MonoBehaviour,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>이번 드래그를 어떻게 처리할지. Undecided는 "아직 정하지 않았고 아무 것도 전달하지
        /// 않은" 상태다.</summary>
        private enum DragMode
        {
            None,
            Undecided,
            Recovery,
            Scroll,
        }

        [Header("Drag Gesture")]
        [Tooltip("회복 드래그로 인정하기 위해 가로로 움직여야 하는 최소 거리(픽셀). 이 값에 닿기 전까지는 " +
                 "판정을 보류하므로, 천천히 끌어도 임계값을 넘는 순간 회복 드래그가 시작된다.")]
        [Min(1f)]
        [SerializeField] private float horizontalStartDistance = 12f;

        [Tooltip("드래그를 넘겨줄 ScrollRect. 비워두면 부모 계층에서 찾는다(리스트의 ScrollRect).")]
        [SerializeField] private ScrollRect scrollRect;

        [Header("Ghost")]
        [Tooltip("고스트를 올려 둘 Canvas. 비워두면 이 오브젝트의 상위 Canvas 중 가장 바깥 것을 쓴다.")]
        [SerializeField] private Canvas ghostCanvas;

        [Tooltip("끌고 다니는 고스트의 투명도.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float ghostAlpha = 0.8f;

        private CharacterSwapListItem item;
        private RecoveryDragGhost ghost;

        private DragMode mode = DragMode.None;

        // ScrollRect에 Begin을 넘겼는지. 넘겼으면 반드시 End도 한 번 넘겨야 ScrollRect가 드래그
        // 상태에 갇히지 않는다.
        private bool forwardingToScroll;

        // 취소 경로에서 ScrollRect에 End를 넘길 때 쓸 이벤트. Begin을 넘긴 그 이벤트를 그대로 들고
        // 있는다 - null을 넘기면 ScrollRect 내부에서 예외가 난다.
        private PointerEventData forwardedEventData;

        // 클릭을 삼킬 마지막 프레임. 드래그 중에는 int.MaxValue, 드래그가 끝나면 그 프레임 번호로
        // 낮춘다 - 그래서 억제가 다음 클릭까지 남지 않는다.
        private int suppressClickThroughFrame = -1;

        private bool resolved;

        /// <summary>이 항목이 회복소로 끌려가는 중인지. 슬롯의 OnDrop이 이 값을 확인한다 -
        /// 스크롤로 넘긴 드래그가 슬롯 위에서 끝나도 등록되면 안 되기 때문이다.</summary>
        public bool IsDraggingToRecovery => mode == DragMode.Recovery;

        /// <summary>지금 끌고 있는 캐릭터(드래그 중이 아니면 바인딩된 캐릭터).</summary>
        public CharacterDefinition DraggedCharacter => item != null ? item.BoundCharacter : null;

        /// <summary>이번 클릭을 삼켜야 하는지. 회복 드래그 제스처가 만들어 낸 클릭만 막고, 드래그가
        /// 끝난 다음 프레임부터는 false를 돌려준다 - 사용자가 새로 누르는 클릭은 정상 동작한다.
        ///
        /// 억제 여부를 <b>소비해서 지우지 않고 프레임 번호로 만료</b>시키는 이유는, 드래그 제스처의
        /// 클릭이 실제로 발생하지 않는 경우가 많기 때문이다(EventSystem이 클릭 자격을 스스로 내린다).
        /// 소비 방식이면 그때 쓰이지 않은 표시가 남아 <b>다음 클릭</b>을 잡아먹는다.</summary>
        public bool ShouldSuppressClick()
        {
            if (Time.frameCount <= suppressClickThroughFrame) return true;

            suppressClickThroughFrame = -1;
            return false;
        }

        private void Awake()
        {
            Resolve();
        }

        private void OnDisable()
        {
            // 리스트가 다시 만들어지거나 패널이 닫히는 도중, 또는 드롭 성공으로 이 항목이 드래그
            // 불가가 되어 컴포넌트가 꺼지는 경우다. 어느 쪽이든 진행 중이던 드래그를 깨끗이 끝낸다.
            CancelDrag();
        }

        private void OnDestroy()
        {
            CancelDrag();
        }

        private void Resolve()
        {
            if (resolved) return;
            resolved = true;

            if (item == null) item = GetComponent<CharacterSwapListItem>();
            if (scrollRect == null) scrollRect = GetComponentInParent<ScrollRect>(true);

            if (ghostCanvas == null)
            {
                Canvas nearest = GetComponentInParent<Canvas>(true);
                ghostCanvas = nearest != null ? nearest.rootCanvas : null;
            }
        }

        // ---- 드래그 ----

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            Resolve();

            // 아직 어떤 제스처인지 모르므로 ScrollRect의 관성 초기화는 그대로 넘겨준다 - 여기서
            // 넘기지 않으면 스크롤이 이전 속도를 물고 튄다. Begin/End 균형과는 무관한 호출이다.
            if (scrollRect != null && eventData != null) scrollRect.OnInitializePotentialDrag(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            Resolve();

            mode = DragMode.None;
            forwardingToScroll = false;
            forwardedEventData = null;

            if (eventData == null)
            {
                // 넘길 이벤트가 없으면 ScrollRect에 아무 것도 보내지 않는다(예외 방지).
                mode = DragMode.Scroll;
                return;
            }

            if (eventData.button != PointerEventData.InputButton.Left || !CanStartRecoveryDrag())
            {
                BeginScrollForwarding(eventData);
                return;
            }

            switch (EvaluateGesture(eventData))
            {
                case DragMode.Recovery:
                    BeginRecoveryDrag(eventData);
                    break;
                case DragMode.Scroll:
                    BeginScrollForwarding(eventData);
                    break;
                default:
                    // 아직 가로로 충분히 움직이지 않았다. 여기서 스크롤로 확정해 버리면 천천히 끄는
                    // 제스처가 회복 드래그가 될 기회를 잃는다 - 판정을 보류한다.
                    mode = DragMode.Undecided;
                    break;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData == null) return;

            if (mode == DragMode.Undecided)
            {
                // 보류 중일 때만 다시 판정한다. 한 번 정해지면 그 드래그 동안 바꾸지 않는다.
                switch (EvaluateGesture(eventData))
                {
                    case DragMode.Recovery:
                        BeginRecoveryDrag(eventData);
                        break;
                    case DragMode.Scroll:
                        // 여기서 처음으로 ScrollRect에 Begin을 넘긴다 - Begin 없이 Drag만 가는 일이 없다.
                        BeginScrollForwarding(eventData);
                        break;
                }
            }

            if (mode == DragMode.Recovery)
            {
                ghost?.MoveTo(eventData);
                return;
            }

            if (mode == DragMode.Scroll && forwardingToScroll && scrollRect != null)
            {
                forwardedEventData = eventData;
                scrollRect.OnDrag(eventData);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (mode == DragMode.Recovery)
            {
                // 드롭 성공/실패와 무관하게 고스트는 여기서 사라진다. 실제 등록은 슬롯의 OnDrop이
                // 이 이벤트보다 먼저 처리한다.
                DestroyGhost();
                ExpireClickSuppression();
                mode = DragMode.None;
                return;
            }

            if (forwardingToScroll)
            {
                EndScrollForwarding(eventData ?? forwardedEventData);
            }

            // 보류 상태로 끝난 제스처(Undecided)는 아무 것도 전달하지 않았으므로 정리만 하면 된다.
            mode = DragMode.None;
        }

        // ---- 판정 ----

        /// <summary>이 캐릭터를 지금 회복소에 올릴 수 있는지. <b>드래그를 시작하려는 시점에 다시
        /// 확인</b>하므로, 리스트를 그린 뒤 상태가 바뀌었어도 끌리지 않는다.</summary>
        private bool CanStartRecoveryDrag()
        {
            if (item == null || item.BoundCharacter == null) return false;

            RecoveryStation station = RecoveryService.Station;
            return station != null && station.CanRegister(item.BoundCharacter);
        }

        /// <summary>누른 지점부터의 누적 이동량으로 제스처를 판정한다.
        /// <see cref="DragMode.Undecided"/>는 "아직 판정할 수 없다"는 뜻이다.</summary>
        private DragMode EvaluateGesture(PointerEventData eventData)
        {
            Vector2 delta = eventData.position - eventData.pressPosition;
            float horizontal = Mathf.Abs(delta.x);
            float vertical = Mathf.Abs(delta.y);

            // 세로가 더 크면 스크롤 의도로 본다.
            if (vertical > horizontal) return DragMode.Scroll;

            return horizontal >= horizontalStartDistance ? DragMode.Recovery : DragMode.Undecided;
        }

        private void BeginRecoveryDrag(PointerEventData eventData)
        {
            mode = DragMode.Recovery;

            // 드래그가 끝날 때까지 이 제스처의 클릭을 삼킨다(끝난 프레임까지만 유효해진다).
            suppressClickThroughFrame = int.MaxValue;

            ghost = RecoveryDragGhost.Create(item, ghostCanvas, ghostAlpha);
            ghost?.MoveTo(eventData);
        }

        private void BeginScrollForwarding(PointerEventData eventData)
        {
            mode = DragMode.Scroll;

            if (scrollRect == null || eventData == null) return;

            forwardingToScroll = true;
            forwardedEventData = eventData;
            scrollRect.OnBeginDrag(eventData);
        }

        /// <summary>ScrollRect에 End를 <b>정확히 한 번</b> 넘긴다. 이미 넘겼거나 Begin을 넘긴 적이
        /// 없으면 아무 일도 하지 않는다.</summary>
        private void EndScrollForwarding(PointerEventData eventData)
        {
            if (!forwardingToScroll) return;

            forwardingToScroll = false;
            PointerEventData data = eventData ?? forwardedEventData;
            forwardedEventData = null;

            // 넘길 이벤트가 없으면 호출하지 않는다 - ScrollRect.OnEndDrag(null)은 예외를 낸다.
            if (scrollRect != null && data != null) scrollRect.OnEndDrag(data);
        }

        /// <summary>
        /// 드래그를 비정상 종료시킨다(오브젝트 비활성/파괴/리스트 재생성/드롭 성공으로 인한 비활성화).
        ///
        /// <b>ScrollRect에 Begin을 넘긴 상태였다면 End를 반드시 한 번 넘긴다</b> - 그러지 않으면
        /// ScrollRect 내부의 dragging 상태가 남아 다음 스크롤이 튄다. 반대로 회복 드래그였다면 Begin을
        /// 넘긴 적이 없으므로 End도 보내지 않는다.
        ///
        /// <b>관성을 임의로 죽이지 않는다.</b> 예전에는 취소할 때 항상 StopMovement()를 불렀는데,
        /// 그러면 사용자가 스크롤을 튕겨 둔 상태에서 패널이 닫혔다 열릴 때 스크롤이 부자연스럽게 멈춘다.
        /// End를 정상적으로 넘기면 ScrollRect가 자기 규칙대로 관성을 이어가거나 멈춘다.
        /// </summary>
        private void CancelDrag()
        {
            DestroyGhost();

            if (forwardingToScroll) EndScrollForwarding(forwardedEventData);

            if (mode != DragMode.None) ExpireClickSuppression();

            mode = DragMode.None;
            forwardedEventData = null;
        }

        /// <summary>클릭 억제를 "이 프레임까지"로 낮춘다. 즉시 지우지 않는 이유는 EventSystem이 같은
        /// 프레임 안에서 <b>OnEndDrag보다 먼저</b> 클릭을 처리하기 때문이다 - 지금 지우면 이번 제스처의
        /// 클릭이 통과할 수 있다.</summary>
        private void ExpireClickSuppression()
        {
            suppressClickThroughFrame = Time.frameCount;
        }

        private void DestroyGhost()
        {
            if (ghost == null) return;
            ghost.Dispose();
            ghost = null;
        }
    }
}
