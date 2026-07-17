using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// Canvas 기반 UI 그룹(GameHUDGroup, ControlDockGroup)을 Layout Mode에서 직접 드래그할 수 있게
    /// 만든다. 이 컴포넌트는 그룹의 "루트" 오브젝트에 직접 붙인다(별도 Catcher 자식 오브젝트를 두지
    /// 않는다) - 평소(일반 모드)에는 자신의 Image(RequireComponent, 알파 0)의 레이캐스트를 꺼둬서
    /// 안쪽 버튼들의 기존 클릭 동작을 그대로 두고, Layout Mode에서만 레이캐스트를 켜서 자기 자신이
    /// 클릭을 가로챈다.
    ///
    /// 문제: 이 컴포넌트가 자식 버튼(tgl_size 등)들의 부모에 있으므로, Unity UI 렌더링/레이캐스트
    /// 순서상 자식이 항상 부모보다 위에 그려진다 - 부모의 Image가 raycastTarget=true여도, 자식
    /// Button의 Graphic이 raycastTarget=true인 이상 그 버튼 영역을 클릭하면 GraphicRaycaster가 여전히
    /// 그 자식 Graphic을 "맞은 대상"으로 고른다(형제 순서를 조정해도 부모-자식 관계에서는 해결되지
    /// 않는다).
    ///
    /// Selectable.interactable만 끄는 방법은 부족하다: interactable=false여도 Button 컴포넌트
    /// 자체와 그 targetGraphic은 여전히 존재하고 raycastTarget도 그대로 true라서, GraphicRaycaster는
    /// 여전히 그 버튼을 "맞은 대상"으로 고르고 Unity의 이벤트 시스템은 그 오브젝트(또는 그 조상 중
    /// IPointerClickHandler를 가진 가장 가까운 것)에게 이벤트를 전달한다 - Button은 그저 내부적으로
    /// onClick 호출만 건너뛸 뿐, 이벤트 자체는 거기서 소비되어 더 위(부모 UiGroupDraggable)로
    /// 전달되지 않는다. 그래서 이 컴포넌트는 Layout Mode 진입 시 자식 서브트리의 모든 Graphic의
    /// raycastTarget을 직접 꺼서 GraphicRaycaster의 히트테스트 후보에서 아예 제외시킨다 - 그래야
    /// 레이캐스트가 그 지점에서 다음으로 위(=부모인 이 컴포넌트의 Image)를 찾아 정상적으로 맞는다.
    /// Selectable.interactable도 함께 끄는 것은 방어적 조치일 뿐이다(레이캐스트를 거치지 않는
    /// 키보드/게임패드 Submit 같은 입력 경로 차단용).
    ///
    /// 어떤 대상을 건드릴지는 이름을 나열하지 않고 매 Awake마다 GetComponentsInChildren으로 자동
    /// 수집한다 - 이후 Dock에 새 버튼이 추가돼도(씬을 다시 로드하면) 코드 수정 없이 자동으로
    /// 포함된다. 유일한 예외는 LayoutModeToggleButton이 붙은 서브트리(Layout Mode를 끄는 버튼
    /// 자신)로, 이건 마커 컴포넌트로 판정해서 항상 원래 상태(클릭 가능)를 유지한다 - 꺼버리면 Layout
    /// Mode를 다시 끌 방법이 없어진다. 일반 모드로 돌아갈 때는 진입 전에 캐싱해둔 원래 raycastTarget/
    /// interactable 값으로 정확히 복원한다(무조건 true로 되돌리지 않음 - 원래 디자인상 raycastTarget이
    /// false였던 장식용 그래픽까지 true로 바뀌는 부작용을 막기 위함).
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
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public class UiGroupDraggable : MonoBehaviour, ILayoutDraggable, IPointerDownHandler
    {
        [Tooltip("저장 데이터에 쓰이는 식별자. LayoutModeController의 Xxx GroupId 상수와 맞춰서 지정한다.")]
        [SerializeField] private string groupId = "HUD";

        [Tooltip("실제로 이동시킬 RectTransform. 비워두면 이 오브젝트 자신의 RectTransform을 옮긴다.")]
        [SerializeField] private RectTransform targetRect;

        [Tooltip("Layout Mode 중 강조 표시할 색(알파 포함). 평소엔 완전히 투명해진다.")]
        [SerializeField] private Color highlightColor = new Color(0.3f, 0.6f, 1f, 0.25f);

        [Tooltip("배치 가능 범위를 계산할 때 화면 가장자리로부터 항상 남겨둘 최소 여백(px).")]
        [SerializeField] private float safetyMarginPixels = 8f;

        public string GroupId => groupId;

        private Image dragCatcherImage;
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

        // Layout Mode 토글 버튼 서브트리를 제외한, 이 그룹 아래 모든 Graphic/Selectable과 그 원래
        // raycastTarget/interactable 값. Layout Mode 진입 시 전부 false로 내렸다가, 종료 시 정확히
        // 이 값으로 복원한다(무조건 true가 아님).
        private Graphic[] childGraphicsExcludingToggle;
        private bool[] originalRaycastTargetStates;
        private Selectable[] childSelectablesExcludingToggle;
        private bool[] originalInteractableStates;

        private void Awake()
        {
            // 다른 로직이 anchoredPosition을 건드리기 전에 가장 먼저 원래 값을 캡처한다.
            sceneAuthoredAnchoredPosition = TargetRect.anchoredPosition;

            dragCatcherImage = GetComponent<Image>();
            dragCatcherImage.color = Color.clear;
            dragCatcherImage.raycastTarget = false; // 일반 모드 기본값 - Layout Mode 진입 시에만 켠다.

            CacheChildInputTargets();
        }

        /// <summary>
        /// 이 그룹 서브트리의 모든 Graphic/Selectable을 자동 수집한다(이름 나열 없음 - 나중에 버튼이
        /// 추가돼도 씬을 다시 로드하면 자동 포함됨). LayoutModeToggleButton이 붙은 서브트리(Layout
        /// Mode 종료 버튼)만 마커 컴포넌트로 판정해 제외한다.
        /// </summary>
        private void CacheChildInputTargets()
        {
            var graphics = new List<Graphic>();
            foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == (Graphic)dragCatcherImage) continue; // 부모 자신의 캐처는 별도로 관리한다.
                if (IsWithinLayoutModeToggleSubtree(graphic.transform)) continue;
                graphics.Add(graphic);
            }
            childGraphicsExcludingToggle = graphics.ToArray();
            originalRaycastTargetStates = new bool[childGraphicsExcludingToggle.Length];
            for (int i = 0; i < childGraphicsExcludingToggle.Length; i++)
            {
                originalRaycastTargetStates[i] = childGraphicsExcludingToggle[i].raycastTarget;
            }

            var selectables = new List<Selectable>();
            foreach (Selectable selectable in GetComponentsInChildren<Selectable>(true))
            {
                if (IsWithinLayoutModeToggleSubtree(selectable.transform)) continue;
                selectables.Add(selectable);
            }
            childSelectablesExcludingToggle = selectables.ToArray();
            originalInteractableStates = new bool[childSelectablesExcludingToggle.Length];
            for (int i = 0; i < childSelectablesExcludingToggle.Length; i++)
            {
                originalInteractableStates[i] = childSelectablesExcludingToggle[i].interactable;
            }
        }

        /// <summary>t 자신 또는 그 조상(이 오브젝트 자신은 제외)이 LayoutModeToggleButton을 갖고 있으면 true.</summary>
        private bool IsWithinLayoutModeToggleSubtree(Transform t)
        {
            while (t != null && t != transform)
            {
                if (t.GetComponent<LayoutModeToggleButton>() != null) return true;
                t = t.parent;
            }
            return false;
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
            if (rect == null)
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
            dragCatcherImage.raycastTarget = active;
            dragCatcherImage.color = active ? highlightColor : Color.clear;

            // 자식 Graphic들이 이 컴포넌트보다 항상 레이캐스트 우선순위가 높으므로, Layout Mode
            // 중에는 그것들의 raycastTarget을 꺼서 GraphicRaycaster 후보에서 제외시킨다 - 그래야
            // 클릭이 부모(이 컴포넌트)까지 도달한다. 끌 때는 진입 전 원래 값으로 정확히 복원한다.
            for (int i = 0; i < childGraphicsExcludingToggle.Length; i++)
            {
                Graphic graphic = childGraphicsExcludingToggle[i];
                if (graphic == null) continue;
                graphic.raycastTarget = active ? false : originalRaycastTargetStates[i];
            }

            // 레이캐스트를 거치지 않는 입력 경로(키보드/게임패드 Submit 등) 차단용 방어 조치.
            for (int i = 0; i < childSelectablesExcludingToggle.Length; i++)
            {
                Selectable selectable = childSelectablesExcludingToggle[i];
                if (selectable == null) continue;
                selectable.interactable = active ? false : originalInteractableStates[i];
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // dragCatcherImage.raycastTarget이 꺼져 있으면(일반 모드) 애초에 이 이벤트 자체가 오지
            // 않는다 - 그래도 방어적으로 한 번 더 확인한다.
            if (!dragCatcherImage.raycastTarget) return;
            LayoutModeController.Instance?.BeginGroupDrag(this);
        }

        public string GetDebugState()
        {
            TryGetUnityScreenRect(out Rect rect);
            return $"rect={rect} active={gameObject.activeInHierarchy} raycast={dragCatcherImage.raycastTarget}";
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
