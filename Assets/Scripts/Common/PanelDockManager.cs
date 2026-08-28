using System.Collections.Generic;
using DesktopWindow;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// Panel_UI 직계 자식 공용 패널끼리만 좌우로 붙이는 1:1 도킹 관리자.
    /// 링크는 저장하지 않으며, 패널이 닫히거나 비활성화되면 즉시 사라진다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class PanelDockManager : MonoBehaviour
    {
        [Header("Docking")]
        [SerializeField, Min(0f)] private float snapDistance = 24f;
        [SerializeField, Min(0f)] private float detachDistance = 48f;

        [Header("Dock Handle (optional prefab)")]
        [SerializeField] private RectTransform dockHandlePrefab;
        [SerializeField] private Vector2 dockHandleSize = new Vector2(16f, 48f);
        [SerializeField] private Color fallbackDockHandleColor = new Color(0.25f, 0.8f, 1f, 0.9f);
        [SerializeField, Range(0f, 1f)] private float dockHandleVerticalAlignment = 0.5f;
        [SerializeField] private Vector2 dockHandlePositionOffset;

        private readonly List<DockLink> links = new List<DockLink>();
        private readonly Vector3[] worldCorners = new Vector3[4];
        private RectTransform panelUi;
        private PanelDragHandle individualDrag;
        private Vector2 individualDragStart;
        private DockLink dockDragLink;
        private Vector2 dockDragAStart;
        private Vector2 dockDragBStart;
        private Vector2 dockDragMinDelta;
        private Vector2 dockDragMaxDelta;

        /// <summary>테스트와 진단용 현재 링크 개수.</summary>
        public int LinkCount => links.Count;

        private RectTransform PanelUi
        {
            get
            {
                if (panelUi == null) panelUi = transform as RectTransform;
                return panelUi;
            }
        }

        private void Awake()
        {
            panelUi = transform as RectTransform;
            SanitizeInspectorValues();
        }

        private void OnValidate()
        {
            SanitizeInspectorValues();
        }

        private void Update()
        {
            PruneUnavailableLinks();
            for (int i = 0; i < links.Count; i++) PositionHandle(links[i]);
        }

        private void OnDestroy()
        {
            for (int i = links.Count - 1; i >= 0; i--) RemoveLink(links[i]);
        }

        public bool IsDocked(PanelDragHandle handle)
        {
            return FindLink(handle) != null;
        }

        /// <summary>기존 PanelDragHandle가 개별 드래그를 시작할 때 전달한다.</summary>
        public void BeginPanelDrag(PanelDragHandle handle)
        {
            if (!IsEligible(handle)) return;
            individualDrag = handle;
            individualDragStart = handle.TargetPanel.anchoredPosition;
        }

        /// <summary>개별 드래그 중에는 이미 연결된 패널만 detach 임계값을 넘기기 전까지 고정한다.</summary>
        public void MovePanelDuringDrag(PanelDragHandle handle, Vector2 candidatePosition)
        {
            if (handle == null || handle != individualDrag || handle.TargetPanel == null) return;

            DockLink link = FindLink(handle);
            if (link == null) return;

            if ((candidatePosition - individualDragStart).magnitude < detachDistance)
            {
                handle.SetAnchoredPositionFromDock(individualDragStart);
                return;
            }

            // PanelDragHandle가 누른 순간 기준의 후보 좌표를 계속 계산하므로, 여기서 링크만 끊으면
            // 포인터와 패널 사이에 점프가 생기지 않는다.
            RemoveLink(link);
        }

        /// <summary>개별 드래그가 끝난 위치에서 아직 미연결인 패널만 새 상대를 찾는다.</summary>
        public void EndPanelDrag(PanelDragHandle handle)
        {
            if (handle == null || handle != individualDrag)
            {
                return;
            }

            individualDrag = null;
            if (!IsEligible(handle) || FindLink(handle) != null) return;
            TryCreateLink(handle);
        }

        /// <summary>패널이 닫힘/비활성화/파괴되는 즉시 그 링크와 핸들을 정리한다.</summary>
        public void NotifyPanelUnavailable(PanelDragHandle handle)
        {
            if (handle == null) return;
            if (individualDrag == handle) individualDrag = null;
            RemoveLinksFor(handle);
        }

        // ---- Dock handle input ----

        public void FocusDockHandle(DockHandleDrag handle)
        {
            DockLink link = FindLink(handle);
            if (link != null) FocusPair(link);
        }

        public void BeginDockHandleDrag(DockHandleDrag handle)
        {
            dockDragLink = FindLink(handle);
            if (dockDragLink == null) return;

            FocusPair(dockDragLink);
            dockDragAStart = dockDragLink.a.TargetPanel.anchoredPosition;
            dockDragBStart = dockDragLink.b.TargetPanel.anchoredPosition;
            dockDragLink.a.GetMoveDeltaLimits(out Vector2 aMinDelta, out Vector2 aMaxDelta);
            dockDragLink.b.GetMoveDeltaLimits(out Vector2 bMinDelta, out Vector2 bMaxDelta);
            dockDragMinDelta = Vector2.Max(aMinDelta, bMinDelta);
            dockDragMaxDelta = Vector2.Min(aMaxDelta, bMaxDelta);
        }

        public void MoveDockHandleDrag(DockHandleDrag handle, Vector2 requestedDelta)
        {
            if (dockDragLink == null || dockDragLink.handle != handle || !IsLinkValid(dockDragLink)) return;

            // DockHandleDrag가 시작점 기준 누적 이동량을 전달하므로, Begin에서 캡처한 양쪽 제한의
            // 교집합으로만 제한한다. 현재 위치로 한계를 다시 계산하면 누적값과 섞여 가상의 중간
            // 경계가 생긴다. 두 패널에는 반드시 같은 최종 이동량을 적용한다.
            Vector2 sharedDelta = ClampDockDragDelta(requestedDelta);
            dockDragLink.a.SetAnchoredPositionFromDock(dockDragAStart + sharedDelta);
            dockDragLink.b.SetAnchoredPositionFromDock(dockDragBStart + sharedDelta);
            PositionHandle(dockDragLink);
        }

        public void EndDockHandleDrag(DockHandleDrag handle)
        {
            if (dockDragLink != null && dockDragLink.handle == handle) dockDragLink = null;
        }

        private Vector2 ClampDockDragDelta(Vector2 requestedDelta)
        {
            requestedDelta.x = ClampAxis(requestedDelta.x, dockDragMinDelta.x, dockDragMaxDelta.x);
            requestedDelta.y = ClampAxis(requestedDelta.y, dockDragMinDelta.y, dockDragMaxDelta.y);
            return requestedDelta;
        }

        private static float ClampAxis(float value, float min, float max)
        {
            if (min <= max) return Mathf.Clamp(value, min, max);
            return Mathf.Abs(value - min) <= Mathf.Abs(value - max) ? min : max;
        }

        // ---- Link creation / layout ----

        private void TryCreateLink(PanelDragHandle moving)
        {
            PanelDragHandle bestTarget = null;
            SnapCandidate bestCandidate = default;
            float bestDistance = float.MaxValue;

            PanelDragHandle[] handles = GetComponentsInChildren<PanelDragHandle>(true);
            foreach (PanelDragHandle candidate in handles)
            {
                if (candidate == moving || !IsEligible(candidate) || FindLink(candidate) != null) continue;
                if (!TryGetSnapCandidate(moving, candidate, out SnapCandidate snap)) continue;
                if (snap.horizontalDistance >= bestDistance) continue;

                bestDistance = snap.horizontalDistance;
                bestTarget = candidate;
                bestCandidate = snap;
            }

            if (bestTarget == null) return;

            // 후보 위치의 bounds를 상대의 좌우 면 및 상단에 정확히 맞춘다.
            moving.SetAnchoredPositionFromDock(moving.TargetPanel.anchoredPosition + bestCandidate.positionDelta);
            DockLink link = new DockLink(moving, bestTarget);
            link.handle = CreateDockHandle(out link.anchor);
            links.Add(link);
            PositionHandle(link);
        }

        private bool TryGetSnapCandidate(PanelDragHandle moving, PanelDragHandle target, out SnapCandidate result)
        {
            result = default;
            GetRectInPanelUiSpace(moving.SnapBoundsRect, out Vector2 movingMin, out Vector2 movingMax);
            GetRectInPanelUiSpace(target.SnapBoundsRect, out Vector2 targetMin, out Vector2 targetMax);

            float movingRightToTargetLeft = Mathf.Abs(movingMax.x - targetMin.x);
            float movingLeftToTargetRight = Mathf.Abs(movingMin.x - targetMax.x);
            float distance = Mathf.Min(movingRightToTargetLeft, movingLeftToTargetRight);
            if (distance > snapDistance) return false;

            bool movingOnLeft = movingRightToTargetLeft <= movingLeftToTargetRight;
            result.horizontalDistance = distance;
            result.positionDelta = movingOnLeft
                ? new Vector2(targetMin.x - movingMax.x, targetMax.y - movingMax.y)
                : new Vector2(targetMax.x - movingMin.x, targetMax.y - movingMax.y);
            return true;
        }

        private DockHandleDrag CreateDockHandle(out RectTransform anchor)
        {
            var anchorObject = new GameObject("DockHandleAnchor", typeof(RectTransform));
            anchorObject.layer = gameObject.layer;
            anchor = (RectTransform)anchorObject.transform;
            anchor.SetParent(PanelUi, false);
            anchor.anchorMin = anchor.anchorMax = new Vector2(0.5f, 0.5f);
            anchor.pivot = new Vector2(0.5f, 0.5f);
            anchor.sizeDelta = Vector2.zero;

            RectTransform handleRect;
            if (dockHandlePrefab != null)
            {
                // The anchor owns the edge-relative position. Keep every authored RectTransform
                // value on the prefab root (including its local position) as its visual offset.
                handleRect = Instantiate(dockHandlePrefab, anchor, false);
            }
            else
            {
                var handleObject = new GameObject("DockHandle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                handleObject.layer = gameObject.layer;
                handleRect = (RectTransform)handleObject.transform;
                handleRect.SetParent(anchor, false);
                Image image = handleObject.GetComponent<Image>();
                // Sprite가 없어도 Image는 기본 흰 텍스처로 색상 사각형을 그린다. 내장 스프라이트의
                // 경로에 의존하지 않아 headless 테스트/플랫폼별 리소스 차이도 피한다.
                image.color = fallbackDockHandleColor;
                image.raycastTarget = true;
                handleRect.anchorMin = handleRect.anchorMax = new Vector2(0.5f, 0.5f);
                handleRect.pivot = new Vector2(0.5f, 0.5f);
                handleRect.sizeDelta = dockHandleSize;
            }

            anchor.SetAsLastSibling();

            DockHandleDrag drag = handleRect.GetComponentInChildren<DockHandleDrag>(true);
            if (drag == null) drag = handleRect.gameObject.AddComponent<DockHandleDrag>();
            drag.Configure(this, PanelUi);

            // Prefer an authored region so custom prefabs do not gain a duplicate component.
            // When none exists, the drag RectTransform is also the native click-through region.
            WindowInputRegion inputRegion = handleRect.GetComponentInChildren<WindowInputRegion>(true);
            if (inputRegion == null) inputRegion = drag.gameObject.AddComponent<WindowInputRegion>();
            inputRegion.ReceiveMouseInput = true;
            return drag;
        }

        private void PositionHandle(DockLink link)
        {
            if (link.handle == null || link.anchor == null || !IsLinkValid(link)) return;
            GetRectInPanelUiSpace(link.a.SnapBoundsRect, out Vector2 aMin, out Vector2 aMax);
            GetRectInPanelUiSpace(link.b.SnapBoundsRect, out Vector2 bMin, out Vector2 bMax);

            float sharedX = Mathf.Abs(aMax.x - bMin.x) <= Mathf.Abs(bMax.x - aMin.x)
                ? (aMax.x + bMin.x) * 0.5f
                : (bMax.x + aMin.x) * 0.5f;
            float commonTop = Mathf.Min(aMax.y, bMax.y);
            float commonBottom = Mathf.Max(aMin.y, bMin.y);
            float alignedY = Mathf.Lerp(commonBottom, commonTop, dockHandleVerticalAlignment);
            link.anchor.anchoredPosition = new Vector2(sharedX, alignedY) + dockHandlePositionOffset;
        }

        private void FocusPair(DockLink link)
        {
            PanelDragHandle first = link.a.TargetPanel.GetSiblingIndex() <= link.b.TargetPanel.GetSiblingIndex() ? link.a : link.b;
            PanelDragHandle second = first == link.a ? link.b : link.a;
            FocusPanel(first);
            FocusPanel(second);
            if (link.anchor != null) link.anchor.SetAsLastSibling();
        }

        private static void FocusPanel(PanelDragHandle handle)
        {
            ModalPanel modal = handle.TargetPanel.GetComponent<ModalPanel>();
            if (modal != null && PopupPanelManager.Instance != null)
            {
                PopupPanelManager.Instance.FocusPanel(modal);
                return;
            }

            handle.TargetPanel.SetAsLastSibling();
        }

        // ---- Link lifetime / eligibility ----

        private DockLink FindLink(PanelDragHandle handle)
        {
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i].Contains(handle)) return links[i];
            }
            return null;
        }

        private DockLink FindLink(DockHandleDrag handle)
        {
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i].handle == handle) return links[i];
            }
            return null;
        }

        private void RemoveLinksFor(PanelDragHandle handle)
        {
            for (int i = links.Count - 1; i >= 0; i--)
            {
                if (links[i].Contains(handle)) RemoveLink(links[i]);
            }
        }

        private void PruneUnavailableLinks()
        {
            for (int i = links.Count - 1; i >= 0; i--)
            {
                if (!IsLinkValid(links[i])) RemoveLink(links[i]);
            }
        }

        private void RemoveLink(DockLink link)
        {
            if (link == null) return;
            links.Remove(link);
            if (dockDragLink == link) dockDragLink = null;
            if (link.anchor != null)
            {
                // Destroy는 프레임 끝에 실행되므로, 그 전까지도 Windows 네이티브 입력 영역이 남지 않게
                // 우선 비활성화한다.
                link.anchor.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(link.anchor.gameObject);
                else DestroyImmediate(link.anchor.gameObject);
            }
        }

        private bool IsEligible(PanelDragHandle handle)
        {
            return handle != null && handle.isActiveAndEnabled && handle.TargetPanel != null &&
                handle.TargetPanel.gameObject.activeInHierarchy && handle.TargetPanel.parent == transform;
        }

        private static bool IsLinkValid(DockLink link)
        {
            return link != null && link.a != null && link.b != null &&
                link.a.isActiveAndEnabled && link.b.isActiveAndEnabled &&
                link.a.TargetPanel != null && link.b.TargetPanel != null &&
                link.a.TargetPanel.gameObject.activeInHierarchy && link.b.TargetPanel.gameObject.activeInHierarchy;
        }

        private void GetRectInPanelUiSpace(RectTransform rect, out Vector2 min, out Vector2 max)
        {
            rect.GetWorldCorners(worldCorners);
            Vector2 a = PanelUi.InverseTransformPoint(worldCorners[0]);
            Vector2 b = PanelUi.InverseTransformPoint(worldCorners[2]);
            min = Vector2.Min(a, b);
            max = Vector2.Max(a, b);
        }

        private void SanitizeInspectorValues()
        {
            snapDistance = Mathf.Max(0f, snapDistance);
            detachDistance = Mathf.Max(snapDistance + 0.01f, detachDistance);
            dockHandleSize.x = Mathf.Max(1f, dockHandleSize.x);
            dockHandleSize.y = Mathf.Max(1f, dockHandleSize.y);
            dockHandleVerticalAlignment = Mathf.Clamp01(dockHandleVerticalAlignment);
        }

        private sealed class DockLink
        {
            public readonly PanelDragHandle a;
            public readonly PanelDragHandle b;
            public DockHandleDrag handle;
            public RectTransform anchor;

            public DockLink(PanelDragHandle a, PanelDragHandle b)
            {
                this.a = a;
                this.b = b;
            }

            public bool Contains(PanelDragHandle panel)
            {
                return a == panel || b == panel;
            }
        }

        private struct SnapCandidate
        {
            public float horizontalDistance;
            public Vector2 positionDelta;
        }
    }
}
