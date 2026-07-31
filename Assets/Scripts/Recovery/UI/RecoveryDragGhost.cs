using Common;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Recovery
{
    /// <summary>
    /// 드래그하는 동안 포인터를 따라다니는 <b>임시</b> 표시물. 새 스프라이트나 새 디자인을 만들지 않고
    /// 원본 리스트 항목의 초상화/이름 오브젝트를 그대로 복제해서 쓴다 - 끌고 있는 것이 무엇인지 알아볼
    /// 수 있으면 충분하고, 아트를 추가하면 원본과 따로 관리해야 하기 때문이다.
    ///
    /// <b>원본은 절대 움직이지 않는다.</b> 리스트 항목은 제자리에 그대로 있고, 복제본만 최상위 Canvas
    /// 아래로 올라간다 - 원본을 옮기면 Layout Group이 리스트를 다시 배치하면서 화면이 튄다.
    ///
    /// <b>드롭 대상을 가리지 않는다.</b> 고스트가 포인터 아래에 있으면 레이캐스트를 먼저 맞아서 슬롯의
    /// OnDrop이 호출되지 않는다. 그래서 CanvasGroup(blocksRaycasts/interactable = false)과 복제본에
    /// 포함된 모든 Graphic의 raycastTarget = false를 함께 적용한다 - 둘 중 하나만으로도 대개 충분하지만,
    /// 복제 대상 구조를 전제하지 않기 위해 양쪽 다 끈다.
    ///
    /// 수명은 <see cref="CharacterRecoveryDragSource"/>가 소유하며, 드래그 종료/비활성/파괴 어느
    /// 경로로 끝나도 <see cref="Dispose"/>가 불려 반드시 파괴된다.
    /// </summary>
    public class RecoveryDragGhost
    {
        private readonly RectTransform ghostRect;
        private readonly Canvas canvas;

        private RecoveryDragGhost(RectTransform ghostRect, Canvas canvas)
        {
            this.ghostRect = ghostRect;
            this.canvas = canvas;
        }

        /// <summary>
        /// 원본 항목의 초상화/이름을 복제한 고스트를 만든다. 복제할 것이 하나도 없으면 null을 돌려주며,
        /// 그 경우 드래그는 고스트 없이 그대로 진행된다 - 표시물이 없다고 등록 자체를 막지는 않는다.
        /// </summary>
        public static RecoveryDragGhost Create(CharacterSwapListItem source, Canvas canvas, float alpha)
        {
            if (source == null || canvas == null) return null;

            var ghostObject = new GameObject("RecoveryDragGhost", typeof(RectTransform), typeof(CanvasGroup));
            ghostObject.layer = canvas.gameObject.layer;

            var ghostRect = (RectTransform)ghostObject.transform;
            ghostRect.SetParent(canvas.transform, false);
            ghostRect.SetAsLastSibling();
            ghostRect.pivot = new Vector2(0.5f, 0.5f);
            ghostRect.anchorMin = new Vector2(0.5f, 0.5f);
            ghostRect.anchorMax = new Vector2(0.5f, 0.5f);

            var group = ghostObject.GetComponent<CanvasGroup>();
            group.alpha = Mathf.Clamp01(alpha);
            group.blocksRaycasts = false;
            group.interactable = false;

            int cloned = 0;
            cloned += CloneInto(source.GhostPortraitSource, ghostRect) ? 1 : 0;
            cloned += CloneInto(source.GhostNameSource, ghostRect) ? 1 : 0;

            if (cloned == 0)
            {
                Object.Destroy(ghostObject);
                return null;
            }

            // 원본이 스케일된 계층 아래에 있어도 화면상 크기가 같게 보이도록 Canvas 기준으로 맞춘다.
            ApplyMatchingScale(ghostRect, source.transform as RectTransform, canvas);

            return new RecoveryDragGhost(ghostRect, canvas);
        }

        /// <summary>포인터 위치로 옮긴다. Canvas의 Render Mode와 무관하게 같은 결과가 나오도록
        /// 화면 좌표를 Canvas의 로컬 좌표로 변환해서 쓴다(PanelDragHandle과 같은 방식).</summary>
        public void MoveTo(PointerEventData eventData)
        {
            if (ghostRect == null || eventData == null) return;

            var canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null) return;

            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : eventData.pressEventCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, eventData.position, camera, out Vector2 local))
            {
                ghostRect.anchoredPosition = local;
            }
        }

        /// <summary>고스트를 없앤다. Destroy는 프레임 끝에 처리되므로 <b>먼저 비활성화</b>해서 같은
        /// 프레임에도 화면에 남거나 레이캐스트에 걸리지 않게 한다 - 드래그가 끝난 뒤 한 프레임 동안
        /// 고스트가 보이는 일이 없다.</summary>
        public void Dispose()
        {
            if (ghostRect == null) return;

            GameObject ghostObject = ghostRect.gameObject;
            ghostObject.SetActive(false);
            Object.Destroy(ghostObject);
        }

        /// <summary>원본 오브젝트를 복제해 고스트 아래에 넣고, 복제본의 모든 Graphic이 레이캐스트를
        /// 받지 않게 만든다.</summary>
        private static bool CloneInto(RectTransform source, RectTransform parent)
        {
            if (source == null) return false;

            RectTransform clone = Object.Instantiate(source, parent, false);
            clone.name = source.name + "_ghost";
            clone.gameObject.SetActive(true);

            // 복제본은 표시 전용이다 - 붙어 있던 상호작용 컴포넌트가 살아 있으면 클릭/드래그를 가로챈다.
            var behaviours = clone.GetComponentsInChildren<Selectable>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                behaviours[i].enabled = false;
            }

            var graphics = clone.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                graphics[i].raycastTarget = false;
            }

            return true;
        }

        private static void ApplyMatchingScale(RectTransform ghostRect, RectTransform source, Canvas canvas)
        {
            if (source == null) return;

            var canvasRect = canvas.transform as RectTransform;
            if (canvasRect == null) return;

            Vector3 canvasScale = canvasRect.lossyScale;
            if (Mathf.Approximately(canvasScale.x, 0f) || Mathf.Approximately(canvasScale.y, 0f)) return;

            Vector3 sourceScale = source.lossyScale;
            ghostRect.localScale = new Vector3(
                sourceScale.x / canvasScale.x,
                sourceScale.y / canvasScale.y,
                1f);
        }
    }
}
