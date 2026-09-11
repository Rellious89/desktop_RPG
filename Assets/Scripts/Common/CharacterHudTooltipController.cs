using System.Collections;
using Character;
using DesktopWindow;
using Recovery;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// CharacterHUD 슬롯 전체에 공통으로 쓰는 한 장의 상세 툴팁을 관리한다. TooltipLayer에만
    /// 인스턴스를 만들기 때문에 슬롯을 빠르게 옮겨도 동시에 둘 이상 남지 않으며, HUD 레이아웃과
    /// Mask의 영향을 받지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterHudTooltipController : MonoBehaviour
    {
        [SerializeField] private GameObject tooltipPrefab;
        [SerializeField] private RectTransform tooltipRoot;
        [Min(0f)] [SerializeField] private float tooltipDelay = 0f;
        [Min(0f)] [SerializeField] private float verticalOffset = 8f;
        [Tooltip("SpeechBottom 배경의 하단 꼬리 끝이 툴팁 왼쪽에서 떨어진 거리(Canvas 기준 해상도 픽셀).")]
        [Min(0f)] [SerializeField] private float pointerOffsetX = 10f;
        [Tooltip("슬롯 기준 위치를 계산한 뒤 더하는 최종 배치 오프셋(Canvas 기준 단위).")]
        [SerializeField] private Vector2 placementOffset = new Vector2(15f, 10f);

        private readonly Vector3[] targetCorners = new Vector3[4];
        private readonly Vector3[] boundsCorners = new Vector3[4];
        private CharacterHudSlotView pendingOwner;
        private CharacterHudSlotView visibleOwner;
        private RectTransform visibleTarget;
        private CharacterHudTooltipView view;
        private RectTransform instanceRect;
        private Coroutine showRoutine;
        private bool instantiateFailed;

        public static CharacterHudTooltipController FindSharedController(Component owner)
        {
            return owner != null ? owner.GetComponentInParent<CharacterHudTooltipController>(true) : null;
        }

        public void RequestShow(CharacterHudSlotView owner)
        {
            if (owner == null || owner.BoundCharacter == null || !isActiveAndEnabled) return;

            CancelPendingRoutine();
            HideInstance();
            visibleOwner = null;
            visibleTarget = null;
            pendingOwner = owner;

            if (tooltipDelay <= 0f)
            {
                ShowNow(owner);
                return;
            }

            showRoutine = StartCoroutine(ShowAfterDelay(owner));
        }

        public void CancelShow(CharacterHudSlotView owner)
        {
            if (owner == null || (pendingOwner != owner && visibleOwner != owner)) return;
            Hide();
        }

        public void RefreshVisible(CharacterHudSlotView owner)
        {
            if (owner == null || visibleOwner != owner || instanceRect == null || !instanceRect.gameObject.activeSelf) return;
            BindAndPlace(owner);
        }

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
            Hide();
        }

        private IEnumerator ShowAfterDelay(CharacterHudSlotView owner)
        {
            yield return new WaitForSecondsRealtime(tooltipDelay);
            showRoutine = null;
            ShowNow(owner);
        }

        private void ShowNow(CharacterHudSlotView owner)
        {
            if (owner == null || !owner.isActiveAndEnabled || owner.BoundCharacter == null)
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
            visibleTarget = owner.transform as RectTransform;
            instanceRect.gameObject.SetActive(true);
            instanceRect.SetAsLastSibling();
            BindAndPlace(owner);
        }

        private void BindAndPlace(CharacterHudSlotView owner)
        {
            CharacterRoster roster = CharacterRoster.Instance;
            CharacterDefinition definition = owner != null ? owner.BoundCharacter : null;
            if (roster == null || definition == null || view == null || visibleTarget == null)
            {
                Hide();
                return;
            }

            RecoveryStation station = RecoveryService.Station;
            RecoveryCharacterState recoveryState = station != null
                ? station.GetState(definition)
                : RecoveryCharacterState.Available;
            CharacterSwapListItem.DisplayState displayState = CharacterSwapListItem.ResolveDisplayState(
                roster.GetSwapBlockReason(definition), recoveryState);

            view.Bind(definition,
                roster.GetLevel(definition),
                roster.GetStamina(definition),
                roster.GetMaxStamina(definition),
                roster.GetCorruption(definition),
                roster.GetCorruptionDisplayMaximum(),
                displayState);
            Place(visibleTarget);
        }

        private void LateUpdate()
        {
            if (instanceRect == null || !instanceRect.gameObject.activeSelf) return;
            Transform parent = instanceRect.parent;
            if (parent != null && instanceRect.GetSiblingIndex() != parent.childCount - 1) instanceRect.SetAsLastSibling();
        }

        private void Place(RectTransform target)
        {
            if (instanceRect == null || target == null) return;
            RectTransform parent = instanceRect.parent as RectTransform;
            if (parent == null) return;

            view.RebuildLayout();
            Rect rect = instanceRect.rect;
            float width = rect.width;
            float height = rect.height;
            target.GetWorldCorners(targetCorners);
            Vector2 targetTopCenter = parent.InverseTransformPoint((targetCorners[1] + targetCorners[2]) * 0.5f);

            if (!TryGetClampBounds(parent, out Vector2 boundsMin, out Vector2 boundsMax))
            {
                boundsMin = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                boundsMax = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            }

            // SpeechBottom의 하단 꼬리 끝은 이미지 가운데가 아니라 왼쪽에서 약 10px 지점이다.
            // 따라서 꼬리 끝을 슬롯 중앙에 맞춘다. 화면 모서리에서는 꼬리 정렬보다 본체 전체가
            // 보이는 편을 우선해 아래 clamp가 수평 위치를 안쪽으로 당긴다.
            float left = targetTopCenter.x - pointerOffsetX + placementOffset.x;
            float bottom = targetTopCenter.y + verticalOffset + placementOffset.y;
            if (boundsMax.x - boundsMin.x >= width) left = Mathf.Clamp(left, boundsMin.x, boundsMax.x - width);
            if (boundsMax.y - boundsMin.y >= height) bottom = Mathf.Clamp(bottom, boundsMin.y, boundsMax.y - height);

            Vector2 pivotPoint = new Vector2(
                left + instanceRect.pivot.x * width,
                bottom + instanceRect.pivot.y * height);
            instanceRect.position = parent.TransformPoint(pivotPoint);
        }

        private bool TryGetClampBounds(RectTransform parent, out Vector2 min, out Vector2 max)
        {
            min = default;
            max = default;
            Canvas canvas = parent.GetComponentInParent<Canvas>();
            RectTransform canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
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
                Debug.LogError("[CharacterHudTooltipController] CharacterHUD_HoverTooltip 프리팹이 연결되지 않았습니다.", this);
                instantiateFailed = true;
                return false;
            }

            RectTransform root = ResolveTooltipRoot();
            if (root == null)
            {
                Debug.LogError("[CharacterHudTooltipController] Canvas 직속 TooltipLayer를 찾지 못했습니다.", this);
                instantiateFailed = true;
                return false;
            }

            GameObject instance = Instantiate(tooltipPrefab, root);
            instanceRect = instance.transform as RectTransform;
            view = instance.GetComponent<CharacterHudTooltipView>();
            if (instanceRect == null || view == null)
            {
                Debug.LogError("[CharacterHudTooltipController] CharacterHUD_HoverTooltip에는 RectTransform과 CharacterHudTooltipView가 필요합니다.", this);
                Destroy(instance);
                instanceRect = null;
                view = null;
                instantiateFailed = true;
                return false;
            }

            foreach (Graphic graphic in instance.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            if (instance.GetComponentInChildren<Button>(true) != null || instance.GetComponentInChildren<WindowInputRegion>(true) != null)
            {
                Debug.LogError("[CharacterHudTooltipController] HUD 툴팁은 입력을 받는 Button 또는 WindowInputRegion을 포함하면 안 됩니다.", instance);
            }

            // 배치 계산은 프리팹의 편집 pivot과 무관하게 아래-가운데를 기준으로 한다. 실제 꼬리의
            // x 위치는 Place의 pointerOffsetX가 맡으므로, 이 pivot이 꼬리의 이미지상 위치를 뜻하지는 않는다.
            instanceRect.pivot = new Vector2(0.5f, 0f);
            instance.SetActive(false);
            return true;
        }

        private RectTransform ResolveTooltipRoot()
        {
            if (tooltipRoot != null) return tooltipRoot;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return null;

            RectTransform[] candidates = canvas.rootCanvas.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null && candidates[i].name == "TooltipLayer") return candidates[i];
            }

            return null;
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
            view?.Clear();
            instanceRect.gameObject.SetActive(false);
        }
    }
}
