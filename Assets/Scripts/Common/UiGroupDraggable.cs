using DesktopWindow;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// Canvas 기반 UI 그룹을 평상시 상태에서 직접 롱프레스로 옮긴다. 짧은 클릭/호버는 기존 자식 UI가
    /// 그대로 처리하며, 롱프레스가 성립한 뒤에만 이동과 강조 표시가 활성화된다.
    ///
    /// 이동 대상(targetRect)은 이 오브젝트 자신일 수도, 별도로 지정한 RectTransform일 수도 있다.
    ///
    /// 좌표 계산은 anchorMin/anchorMax가 한 점(스트레치 아님)이라고 가정하고 pivot까지 반영해서
    /// anchoredPosition <-> 화면 픽셀을 변환한다 - 앵커가 어느 모서리든(좌상단/우상단/...) 동일한
    /// 공식으로 안전 여백 클램프와 저장용 정규화 값을 계산할 수 있다(코너별 분기 없음).
    ///
    /// 저장 데이터가 없을 때의 "기본 배치"는 Inspector에 하드코딩한 값이 아니라 씬에 authoring된
    /// 실제 anchoredPosition을 Awake에서 그대로 캡처해서 쓴다(sceneAuthoredAnchoredPosition) - 새
    /// UI 그룹을 추가할 때 별도로 기본 오프셋을 계산/입력할 필요 없이, 에디터에서 원하는 자리에
    /// 두는 것 자체가 곧 기본값이 되게 하기 위함이다.
    /// </summary>
    [DisallowMultipleComponent]
    public class UiGroupDraggable : MonoBehaviour, ILayoutDraggable, IPointerDownHandler,
        IPointerUpHandler, IPointerExitHandler, IDragHandler
    {
        [Tooltip("저장 데이터에 쓰이는 식별자. LayoutModeController의 Xxx GroupId 상수와 맞춰서 지정한다.")]
        [SerializeField] private string groupId = "HUD";

        [Tooltip("실제로 이동시킬 RectTransform. 비워두면 이 오브젝트 자신의 RectTransform을 옮긴다.")]
        [SerializeField] private RectTransform targetRect;

        [Tooltip("롱프레스 이동 중 강조 표시할 색(알파 포함). 루트에 Image가 있을 때만 적용됩니다.")]
        [SerializeField] private Color highlightColor = new Color(0.3f, 0.6f, 1f, 0.25f);

        [Tooltip("배치 가능 범위를 계산할 때 화면 가장자리로부터 항상 남겨둘 최소 여백(px).")]
        [SerializeField] private float safetyMarginPixels = 8f;

        [Tooltip("이 그룹에서 직접 롱프레스 이동을 허용합니다. ControlDock처럼 이동 대상이 아니면 끕니다.")]
        [SerializeField] private bool directDragEnabled = true;

        public string GroupId => groupId;

        private Image dragCatcherImage;
        private readonly LongPressDragGesture gesture = new LongPressDragGesture();
        private readonly List<CanvasGroup> canvasGroupBuffer = new List<CanvasGroup>(2);
        private RectTransform TargetRect => targetRect != null ? targetRect : (RectTransform)transform;

        // anchoredPosition을 work area 픽셀 크기로 나눈 정규화 값. 앵커가 어느 모서리에 있든 그
        // 앵커로부터의 상대 오프셋이라 해상도가 달라져도 같은 비율로 복원된다.
        private float normalizedOffsetX;
        private float normalizedOffsetY;
        private int workAreaWidth = 1920;
        private int workAreaHeight = 1080;

        // "기본 배치"는 Inspector에 하드코딩된 정규화 값이 아니라, 씬에 authoring된 실제
        // anchoredPosition을 Awake 시점에 그대로 캡처해서 쓴다 - 그래야 해상도를 가정하지 않고
        // "디자이너가 에디터에서 배치한 그 자리"를 저장 데이터가 없을 때의 기본값으로 정확히 쓸 수
        // 있다(요구사항: "데이터가 없으면 씬의 현재 위치를 기본값으로 사용"). 이 방식 덕분에 새 그룹을
        // 추가할 때도 별도로 기본 오프셋을 계산/입력할 필요가 없다 - 에디터에서 원하는 자리에 두기만
        // 하면 그게 곧 기본값이다.
        private Vector2 sceneAuthoredAnchoredPosition;

        private void Awake()
        {
            // 다른 로직이 anchoredPosition을 건드리기 전에 가장 먼저 원래 값을 캡처한다.
            sceneAuthoredAnchoredPosition = TargetRect.anchoredPosition;

            dragCatcherImage = GetComponent<Image>();
            if (dragCatcherImage != null)
            {
                dragCatcherImage.color = Color.clear;
                dragCatcherImage.raycastTarget = directDragEnabled;
            }
        }

        private void Update()
        {
            LayoutModeController controller = LayoutModeController.Instance;
            if (controller == null || !gesture.IsWaiting) return;

            // Presentation 전환이 GameObject를 끄지 않고 상위 CanvasGroup만 숨기는 경우에도 대기 중인
            // 롱프레스가 뒤늦게 활성화되지 않도록 즉시 취소한다.
            if (!CanReceiveDirectDragInput())
            {
                gesture.Cancel();
                return;
            }

            // EventSystem의 Drag 이벤트는 자체 dragThreshold를 넘긴 뒤에야 오므로, 더 작은 Inspector
            // 허용 거리를 정확히 지키기 위해 대기 중에는 현재 포인터 위치도 직접 비교한다.
            gesture.Move(Input.mousePosition, controller.PreActivationMovementPixels);
            if (gesture.TryActivate(Time.unscaledTime, controller.HoldSeconds))
            {
                controller.BeginGroupDrag(this);
            }
        }

        private void OnDisable()
        {
            gesture.Cancel();
        }

        public void NotifyWorkAreaChanged(int widthPixels, int heightPixels)
        {
            if (widthPixels <= 0 || heightPixels <= 0) return;
            workAreaWidth = widthPixels;
            workAreaHeight = heightPixels;

            // 항상 다시 적용한다(최초 배치뿐 아니라 모니터 이동/해상도 변경 시에도) - 그래야 저장된
            // 정규화 오프셋이 새 Work Area 기준으로 다시 클램프되어 화면 밖으로 나가지 않는다.
            ApplyNormalizedOffset();
        }

        public void ApplyDragDeltaPixels(int deltaXPixels, int deltaYPixels)
        {
            // Win32 화면 좌표는 Y가 아래로 증가하지만 Unity RectTransform의 anchoredPosition은 위로
            // 증가한다 - Y만 부호를 반전한다.
            RectTransform rect = TargetRect;
            ClampAndApply(rect.anchoredPosition.x + deltaXPixels, rect.anchoredPosition.y - deltaYPixels);
        }

        public void SetPlacement(float rightMarginFraction, float bottomMarginFraction)
        {
            normalizedOffsetX = rightMarginFraction;
            normalizedOffsetY = bottomMarginFraction;
            ApplyNormalizedOffset();
        }

        public (float rightMarginFraction, float bottomMarginFraction) GetPlacement()
        {
            return (normalizedOffsetX, normalizedOffsetY);
        }

        /// <summary>저장된 배치가 없을 때(첫 실행, 마이그레이션 대상 없음, 새로 추가된 그룹 등)
        /// 씬에 authoring된 원래 위치로 되돌린다. ClampAndApply를 거치므로 화면 밖으로 나가지 않게
        /// 안전 여백도 함께 적용된다.</summary>
        public void ResetToDefaultPlacement()
        {
            ClampAndApply(sceneAuthoredAnchoredPosition.x, sceneAuthoredAnchoredPosition.y);
        }

        public bool TryGetUnityScreenRect(out Rect unityScreenRect)
        {
            RectTransform rect = TargetRect;
            if (rect == null || !CanReceiveDirectDragInput())
            {
                unityScreenRect = default;
                return false;
            }

            var corners = new Vector3[4];
            rect.GetWorldCorners(corners); // 0=bottom-left, 2=top-right
            Vector2 min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);

            unityScreenRect = new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
            return true;
        }

        public void SetLayoutModeActive(bool active)
        {
            if (dragCatcherImage != null) dragCatcherImage.color = active ? highlightColor : Color.clear;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!CanReceiveDirectDragInput() || eventData.button != PointerEventData.InputButton.Left) return;
            LayoutModeController.Instance?.ClearClickSuppression();
            gesture.Press(eventData.position, Time.unscaledTime);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            bool wasActive = gesture.Release();
            if (!wasActive) return;

            TransparentWindowController window = TransparentWindowController.Instance;
            if (window != null) window.EndManualDrag();
            else LayoutModeController.Instance?.EndActiveDrag();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            gesture.Exit();
        }

        public void OnDrag(PointerEventData eventData)
        {
            LayoutModeController controller = LayoutModeController.Instance;
            if (controller == null) return;

            if (gesture.IsWaiting)
            {
                gesture.Move(eventData.position, controller.PreActivationMovementPixels);
                return;
            }

            if (gesture.IsActive && LayoutModeController.UsesPointerEventDragDeltas)
            {
                ApplyDragDeltaPixels(Mathf.RoundToInt(eventData.delta.x), Mathf.RoundToInt(-eventData.delta.y));
            }
        }

        public bool ConsumeCompletedDragClick()
        {
            return LayoutModeController.Instance != null &&
                   LayoutModeController.Instance.ConsumeClickSuppression(groupId);
        }

        /// <summary>
        /// Unity UI가 실제로 포인터 입력을 받을 수 있는 상태인지 확인한다. PresentationModeController가
        /// GameObject를 끄지 않고 CanvasGroup으로 HUD를 숨기므로, activeInHierarchy만 확인하면 보이지
        /// 않는 영역이 Windows 네이티브 click-through를 막는다. 현재 오브젝트부터 부모 방향으로
        /// CanvasGroup을 검사하며, ignoreParentGroups가 있으면 그 레벨의 상태까지 적용한 뒤 상위 검사를
        /// 중단한다(UI GraphicRaycaster의 부모 그룹 차단 규칙과 같은 의미).
        /// </summary>
        public bool CanReceiveDirectDragInput()
        {
            if (!directDragEnabled || !isActiveAndEnabled || !gameObject.activeInHierarchy) return false;

            Transform current = transform;
            while (current != null)
            {
                canvasGroupBuffer.Clear();
                current.GetComponents(canvasGroupBuffer);
                bool ignoreParents = false;
                for (int i = 0; i < canvasGroupBuffer.Count; i++)
                {
                    CanvasGroup group = canvasGroupBuffer[i];
                    if (group == null || !group.isActiveAndEnabled) continue;
                    if (group.alpha <= 0.001f || !group.blocksRaycasts || !group.interactable) return false;
                    if (group.ignoreParentGroups) ignoreParents = true;
                }

                if (ignoreParents) break;
                current = current.parent;
            }

            return true;
        }

        public string GetDebugState()
        {
            TryGetUnityScreenRect(out Rect rect);
            bool raycast = dragCatcherImage != null && dragCatcherImage.raycastTarget;
            return $"rect={rect} active={gameObject.activeInHierarchy} raycast={raycast} " +
                   $"directDrag={directDragEnabled} inputReady={CanReceiveDirectDragInput()}";
        }

        private void ApplyNormalizedOffset()
        {
            if (workAreaWidth <= 0 || workAreaHeight <= 0) return;
            ClampAndApply(normalizedOffsetX * workAreaWidth, normalizedOffsetY * workAreaHeight);
        }

        /// <summary>
        /// anchorMin(= anchorMax로 가정하는 단일 지점 앵커)의 화면 픽셀 위치 + anchoredPosition +
        /// pivot을 조합해 그룹의 실제 화면 사각형을 구하고, 그 사각형이 항상 safetyMarginPixels만큼은
        /// Work Area 안에 남도록 x/y(=anchoredPosition 후보값)를 클램프한 뒤 적용한다. 앵커가 어느
        /// 모서리든 같은 공식으로 동작한다(코너별 분기 없음).
        /// </summary>
        private void ClampAndApply(float x, float y)
        {
            if (workAreaWidth <= 0 || workAreaHeight <= 0) return;

            RectTransform rect = TargetRect;
            float width = rect.rect.width;
            float height = rect.rect.height;
            Vector2 pivot = rect.pivot;
            float anchorScreenX = rect.anchorMin.x * workAreaWidth;
            float anchorScreenY = rect.anchorMin.y * workAreaHeight;

            float minX = safetyMarginPixels - anchorScreenX + pivot.x * width;
            float maxX = workAreaWidth - safetyMarginPixels - anchorScreenX - (1f - pivot.x) * width;
            float minY = safetyMarginPixels - anchorScreenY + pivot.y * height;
            float maxY = workAreaHeight - safetyMarginPixels - anchorScreenY - (1f - pivot.y) * height;

            // 그룹이 안전 영역보다 크면(아주 작은 모니터 등) 범위가 뒤집힐 수 있으니 중간값으로 보정한다.
            if (minX > maxX) minX = maxX = (minX + maxX) / 2f;
            if (minY > maxY) minY = maxY = (minY + maxY) / 2f;

            x = Mathf.Clamp(x, minX, maxX);
            y = Mathf.Clamp(y, minY, maxY);

            rect.anchoredPosition = new Vector2(x, y);
            normalizedOffsetX = x / workAreaWidth;
            normalizedOffsetY = y / workAreaHeight;
        }
    }
}
