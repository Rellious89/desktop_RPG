using System.Collections;
using System.Collections.Generic;
using DesktopWindow;
using DG.Tweening;
using Dungeon;
using Field;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Common
{
    /// <summary>
    /// Game/Companion 양쪽에서 현재 캐릭터 위에 Stage 롱프레스 입력 영역을 맞춘다. 짧은 클릭은
    /// Companion에서만 인터렉션 메뉴를 열고 Game에서는 아무 동작도 하지 않는다.
    /// </summary>
    [DefaultExecutionOrder(-70)]
    [DisallowMultipleComponent]
    public sealed class CompanionInteractionMenuController : MonoBehaviour
    {
        [Header("Mode / Field")]
        [SerializeField] private PresentationModeController presentationModeController;
        [SerializeField] private FieldModeManager fieldModeManager;

        [Header("Character Screen Area")]
        [SerializeField] private RectTransform interactionCanvasRect;
        [SerializeField] private Camera stageCamera;
        [SerializeField] private SpriteRenderer playerRenderer;
        [Tooltip("던전 휴식 중에는 숨겨진 Player Renderer 대신 활성화된 각 휴식 캐릭터 슬롯을 클릭 기준으로 사용합니다.")]
        [SerializeField] private DungeonPartyRestEventController dungeonRestEventController;
        [SerializeField] private Vector2 hitPadding = new Vector2(4f, 4f);

        [Header("Interaction Menu")]
        [SerializeField] private RectTransform menuRoot;
        [SerializeField] private GameObject townMenuRoot;
        [SerializeField] private GameObject dungeonMenuRoot;
        [SerializeField] private Vector2 menuOffset = new Vector2(0f, 8f);

        [Header("자동 닫힘")]
        [Tooltip("인터렉션 메뉴 영역에서 마우스가 벗어난 뒤 자동으로 닫히기까지의 시간(초)입니다. " +
                 "메뉴나 자식 버튼 위에 마우스가 있는 동안은 계속 초기화됩니다. 0 이하면 자동 닫힘을 사용하지 않습니다.")]
        [SerializeField] private float autoCloseDelay = 1f;

        [Header("Interaction Menu Enter Tween")]
        [Tooltip("Companion 인터렉션 메뉴를 열 때 활성 필드 메뉴의 버튼을 순서대로 등장시킵니다.")]
        [SerializeField] private bool playMenuEnterTween = true;

        [Tooltip("각 버튼이 기준 위치로 들어오기 전에 떨어져 있을 위치 오프셋입니다. 양수 Y는 위쪽에서 내려옵니다.")]
        [SerializeField] private Vector2 menuButtonEnterOffset = new Vector2(0f, 24f);

        [Tooltip("첫 번째 버튼 Tween이 시작되기 전 대기 시간(초)입니다.")]
        [SerializeField] [Min(0f)] private float menuEnterInitialDelay;

        [Tooltip("버튼 하나가 기준 위치까지 이동하는 시간(초)입니다.")]
        [SerializeField] [Min(0.01f)] private float menuButtonEnterDuration = 0.18f;

        [Tooltip("앞 버튼과 다음 버튼의 시작 시간 차이(초)입니다.")]
        [SerializeField] [Min(0f)] private float menuButtonEnterStagger = 0.05f;

        [Tooltip("버튼 이동에 사용할 DOTween Ease입니다.")]
        [SerializeField] private Ease menuButtonEnterEase = Ease.OutCubic;

        [Tooltip("이동과 함께 버튼을 투명 상태에서 나타나게 합니다.")]
        [SerializeField] private bool fadeMenuButtonsOnEnter = true;

        [Tooltip("Fade 사용 시 버튼의 시작 Alpha입니다.")]
        [SerializeField] [Range(0f, 1f)] private float menuButtonEnterStartAlpha;

        [Tooltip("활성 메뉴의 Hierarchy 순서를 반대로 재생합니다.")]
        [SerializeField] private bool reverseMenuButtonEnterOrder;

        [Tooltip("등장 연출이 끝날 때까지 버튼과 Windows 입력 영역을 잠급니다.")]
        [SerializeField] private bool blockMenuInputDuringEnter = true;

        [Header("Companion Character Drag Feedback")]
        [SerializeField] private StageVisualRootController companionDragTarget;
        [Tooltip("드래그 중 외곽선 색상을 바꿀 플레이어 Actor Outline Controller입니다. 비워두면 Player Renderer에서 자동으로 찾습니다.")]
        [SerializeField] private ActorOutlineController companionDragOutlineTarget;

        [Tooltip("롱프레스가 완료되어 실제 드래그가 활성화된 동안에만 적용할 캐릭터 외곽선 색상입니다.")]
        [SerializeField] private Color companionDragOutlineColor = new Color(0f, 175f / 255f, 1f, 0.85f);

        [Header("Buttons")]
        [SerializeField] private UnityEngine.UI.Button townGameModeButton;
        [SerializeField] private UnityEngine.UI.Button dungeonGameModeButton;
        [SerializeField] private UnityEngine.UI.Button returnTownButton;

        private readonly List<UnityEngine.UI.Button> menuButtons = new List<UnityEngine.UI.Button>();
        private readonly List<CharacterHitArea> characterHitAreas = new List<CharacterHitArea>(4);
        private CharacterHitArea playerCharacterHitArea;
        private CharacterHitArea[] restCharacterHitAreas = System.Array.Empty<CharacterHitArea>();
        private MenuPointerRegion menuPointerRegion;
        private Coroutine pendingMenuClose;
        private Sequence menuEnterSequence;
        private ActorOutlineController activeCompanionDragOutlineTarget;
        private readonly List<MenuButtonEnterState> menuButtonEnterStates = new List<MenuButtonEnterState>();
        private readonly List<UnityEngine.UI.LayoutGroup> pausedMenuLayouts =
            new List<UnityEngine.UI.LayoutGroup>();
        private readonly LongPressDragGesture stageDragGesture = new LongPressDragGesture();
        private float closeMenuAtRealtime;
        private int pressedInteractionSlot = -1;
        private int selectedInteractionSlot = -1;

        private void Awake()
        {
            ValidateReferences();
            CreateCharacterHitArea();
            CacheMenuButtons();
            EnsureMenuInputRegions();
            EnsureMenuPointerRegion();
            CloseMenu();
            SyncFieldMenu(CurrentFieldMode());
            ApplyPresentationMode(CurrentPresentationMode());
        }

        private void OnEnable()
        {
            Subscribe();
            SubscribeLayoutDrag();
            AddButtonListeners();
            SyncFieldMenu(CurrentFieldMode());
            ApplyPresentationMode(CurrentPresentationMode());
        }

        private void OnDisable()
        {
            Unsubscribe();
            UnsubscribeLayoutDrag();
            RemoveButtonListeners();

            if (pendingMenuClose != null)
            {
                StopCoroutine(pendingMenuClose);
                pendingMenuClose = null;
            }

            SetCompanionDragOutlineActive(false);
            stageDragGesture.Cancel();
            CloseMenu();
            SetCharacterHitAreaActive(false);
        }

        private void LateUpdate()
        {
            UpdateCharacterScreenLayout();
            UpdateStageLongPressGesture();
            UpdateMenuAutoClose();
        }

        public static bool ShouldMaintainCharacterHitAreas(PresentationMode mode) =>
            mode == PresentationMode.Game || mode == PresentationMode.Companion;

        private void Subscribe()
        {
            if (presentationModeController != null)
            {
                presentationModeController.ModeChanged -= HandlePresentationModeChanged;
                presentationModeController.ModeChanged += HandlePresentationModeChanged;
            }

            if (fieldModeManager != null)
            {
                fieldModeManager.FieldModeChanged -= HandleFieldModeChanged;
                fieldModeManager.FieldModeChanged += HandleFieldModeChanged;
            }
        }

        private void Unsubscribe()
        {
            if (presentationModeController != null)
            {
                presentationModeController.ModeChanged -= HandlePresentationModeChanged;
            }

            if (fieldModeManager != null)
            {
                fieldModeManager.FieldModeChanged -= HandleFieldModeChanged;
            }
        }

        private void SubscribeLayoutDrag()
        {
            if (LayoutModeController.Instance == null) return;
            LayoutModeController.Instance.DragStarted -= HandleLayoutDragStarted;
            LayoutModeController.Instance.DragStarted += HandleLayoutDragStarted;
            LayoutModeController.Instance.DragEnded -= HandleLayoutDragEnded;
            LayoutModeController.Instance.DragEnded += HandleLayoutDragEnded;
        }

        private void UnsubscribeLayoutDrag()
        {
            if (LayoutModeController.Instance == null) return;
            LayoutModeController.Instance.DragStarted -= HandleLayoutDragStarted;
            LayoutModeController.Instance.DragEnded -= HandleLayoutDragEnded;
        }

        private void AddButtonListeners()
        {
            AddListener(townGameModeButton, HandleGameModeClicked);
            AddListener(dungeonGameModeButton, HandleGameModeClicked);
            AddListener(returnTownButton, HandleReturnTownClicked);

            for (int i = 0; i < menuButtons.Count; i++)
            {
                UnityEngine.UI.Button button = menuButtons[i];
                if (button == null) continue;
                button.onClick.RemoveListener(HandleMenuActionClicked);
                button.onClick.AddListener(HandleMenuActionClicked);
            }
        }

        private void RemoveButtonListeners()
        {
            RemoveListener(townGameModeButton, HandleGameModeClicked);
            RemoveListener(dungeonGameModeButton, HandleGameModeClicked);
            RemoveListener(returnTownButton, HandleReturnTownClicked);

            for (int i = 0; i < menuButtons.Count; i++)
            {
                if (menuButtons[i] != null) menuButtons[i].onClick.RemoveListener(HandleMenuActionClicked);
            }
        }

        private static void AddListener(UnityEngine.UI.Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;
            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private static void RemoveListener(UnityEngine.UI.Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.RemoveListener(action);
        }

        private void CreateCharacterHitArea()
        {
            if (interactionCanvasRect == null || playerCharacterHitArea != null) return;

            playerCharacterHitArea = CreateCharacterHitArea("CompanionCharacterClickArea", -1);

            int restSlotCount = dungeonRestEventController != null
                ? dungeonRestEventController.InteractionSlotCount
                : 0;
            restCharacterHitAreas = new CharacterHitArea[Mathf.Max(0, restSlotCount)];
            for (int i = 0; i < restCharacterHitAreas.Length; i++)
            {
                restCharacterHitAreas[i] = CreateCharacterHitArea($"CompanionRestCharacterClickArea{i + 1}", i);
            }
        }

        private CharacterHitArea CreateCharacterHitArea(string objectName, int restSlotIndex)
        {
            var hitArea = new GameObject(objectName);
            hitArea.SetActive(false);
            hitArea.layer = interactionCanvasRect.gameObject.layer;

            RectTransform hitRect = hitArea.AddComponent<RectTransform>();
            hitRect.SetParent(interactionCanvasRect, false);
            hitRect.anchorMin = new Vector2(0.5f, 0.5f);
            hitRect.anchorMax = new Vector2(0.5f, 0.5f);
            hitRect.pivot = new Vector2(0.5f, 0.5f);

            hitArea.AddComponent<CanvasRenderer>();
            UnityEngine.UI.Image image = hitArea.AddComponent<UnityEngine.UI.Image>();
            image.color = Color.clear;
            image.raycastTarget = true;

            UnityEngine.UI.Button hitButton = hitArea.AddComponent<UnityEngine.UI.Button>();
            hitButton.transition = UnityEngine.UI.Selectable.Transition.None;
            hitButton.targetGraphic = image;

            var eventTrigger = hitArea.AddComponent<EventTrigger>();
            AddPointerTrigger(eventTrigger, EventTriggerType.PointerDown,
                eventData => HandleCharacterPointerDown(eventData, restSlotIndex));
            AddPointerTrigger(eventTrigger, EventTriggerType.PointerExit,
                eventData => HandleCharacterPointerExit(eventData, restSlotIndex));
            AddPointerTrigger(eventTrigger, EventTriggerType.PointerUp,
                eventData => HandleCharacterPointerUp(eventData, restSlotIndex));
            AddPointerTrigger(eventTrigger, EventTriggerType.PointerClick,
                eventData => HandleCharacterPointerClick(eventData, restSlotIndex));
            AddPointerTrigger(eventTrigger, EventTriggerType.Drag,
                eventData => HandleCharacterPointerDrag(eventData, restSlotIndex));

            WindowInputRegion inputRegion = hitArea.AddComponent<WindowInputRegion>();
            inputRegion.ReceiveMouseInput = true;

            if (menuRoot != null)
            {
                hitRect.SetSiblingIndex(menuRoot.GetSiblingIndex());
            }

            var result = new CharacterHitArea(hitRect, restSlotIndex);
            characterHitAreas.Add(result);
            return result;
        }

        private static void AddPointerTrigger(
            EventTrigger trigger,
            EventTriggerType eventType,
            UnityEngine.Events.UnityAction<BaseEventData> listener)
        {
            var entry = new EventTrigger.Entry { eventID = eventType };
            entry.callback.AddListener(listener);
            trigger.triggers.Add(entry);
        }

        private void HandleCharacterPointerDown(BaseEventData eventData, int restSlotIndex)
        {
            if (!(eventData is PointerEventData pointer)
                || pointer.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            LayoutModeController.Instance?.ClearClickSuppression();
            pressedInteractionSlot = restSlotIndex;
            stageDragGesture.Press(pointer.position, Time.unscaledTime);
        }

        private void HandleCharacterPointerUp(BaseEventData eventData, int ignoredRestSlotIndex)
        {
            if (!(eventData is PointerEventData pointer)
                || pointer.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            bool wasActive = stageDragGesture.Release();
            if (wasActive)
            {
                TransparentWindowController window = TransparentWindowController.Instance;
                if (window != null) window.EndManualDrag();
                else LayoutModeController.Instance?.EndActiveDrag();
            }

            pressedInteractionSlot = -1;
        }

        private void HandleCharacterPointerExit(BaseEventData eventData, int ignoredRestSlotIndex)
        {
            stageDragGesture.Exit();
        }

        private void HandleCharacterPointerClick(BaseEventData eventData, int restSlotIndex)
        {
            if (!(eventData is PointerEventData pointer)
                || pointer.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            if (LayoutModeController.Instance != null &&
                LayoutModeController.Instance.ConsumeClickSuppression(LayoutModeController.StageGroupId))
            {
                return;
            }

            if (CurrentPresentationMode() == PresentationMode.Companion) ToggleMenu(restSlotIndex);
        }

        private void HandleCharacterPointerDrag(BaseEventData eventData, int ignoredRestSlotIndex)
        {
            if (!(eventData is PointerEventData pointer)) return;

            LayoutModeController controller = LayoutModeController.Instance;
            if (controller == null) return;

            if (stageDragGesture.IsWaiting)
            {
                stageDragGesture.Move(pointer.position, controller.PreActivationMovementPixels);
                return;
            }

            if (stageDragGesture.IsActive && LayoutModeController.UsesPointerEventDragDeltas)
            {
                CompanionDragTarget()?.ApplyDragDeltaPixels(
                    Mathf.RoundToInt(pointer.delta.x),
                    Mathf.RoundToInt(-pointer.delta.y));
            }
        }

        private void UpdateStageLongPressGesture()
        {
            LayoutModeController controller = LayoutModeController.Instance;
            if (controller == null || !stageDragGesture.IsWaiting) return;

            stageDragGesture.Move(Input.mousePosition, controller.PreActivationMovementPixels);
            if (!stageDragGesture.TryActivate(Time.unscaledTime, controller.HoldSeconds)) return;

            StageVisualRootController target = CompanionDragTarget();
            if (target == null)
            {
                stageDragGesture.Cancel();
                return;
            }

            controller.BeginGroupDrag(target);
            if (!controller.HasActiveDrag) stageDragGesture.Cancel();
        }

        private void HandleLayoutDragStarted(ILayoutDraggable target)
        {
            if (target == null || target.GroupId != LayoutModeController.StageGroupId ||
                CurrentPresentationMode() != PresentationMode.Companion) return;

            SetCompanionDragOutlineActive(true);
            CloseMenu();
        }

        private void HandleLayoutDragEnded(ILayoutDraggable target)
        {
            if (target == null || target.GroupId != LayoutModeController.StageGroupId) return;
            SetCompanionDragOutlineActive(false);
        }

        private void SetCompanionDragOutlineActive(bool active)
        {
            if (!active)
            {
                if (activeCompanionDragOutlineTarget != null)
                {
                    activeCompanionDragOutlineTarget.ClearOutlineColorOverride();
                }
                activeCompanionDragOutlineTarget = null;
                return;
            }

            SpriteRenderer interactionRenderer = ResolveInteractionRenderer(pressedInteractionSlot);
            bool usingRestCharacter = interactionRenderer != null && interactionRenderer != playerRenderer;
            ActorOutlineController target = usingRestCharacter ? null : companionDragOutlineTarget;
            if (target == null && interactionRenderer != null)
            {
                target = interactionRenderer.GetComponent<ActorOutlineController>();
            }
            if (target == null) return;

            // 이전 대상이 남아 있다면 먼저 원복한다. 캐릭터 교체와 드래그 시작이 같은 프레임에
            // 겹쳐도 다른 캐릭터에 강조 색상이 남지 않게 한다.
            if (activeCompanionDragOutlineTarget != null && activeCompanionDragOutlineTarget != target)
            {
                activeCompanionDragOutlineTarget.ClearOutlineColorOverride();
            }

            activeCompanionDragOutlineTarget = target;
            activeCompanionDragOutlineTarget.SetOutlineColorOverride(companionDragOutlineColor);
        }

        private StageVisualRootController CompanionDragTarget() =>
            companionDragTarget != null ? companionDragTarget : StageVisualRootController.Instance;

        private void CacheMenuButtons()
        {
            menuButtons.Clear();
            if (menuRoot == null) return;

            UnityEngine.UI.Button[] buttons = menuRoot.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null) menuButtons.Add(buttons[i]);
            }
        }

        /// <summary>
        /// 복사된 버튼 중 ControlDock의 영역 판정에 기대던 버튼도 Companion 메뉴 안에서는 각자의
        /// 실제 사각형으로 Windows 입력을 받게 한다. 이미 명시된 영역은 그대로 재사용한다.
        /// </summary>
        private void EnsureMenuInputRegions()
        {
            for (int i = 0; i < menuButtons.Count; i++)
            {
                UnityEngine.UI.Button button = menuButtons[i];
                if (button == null) continue;

                WindowInputRegion inputRegion = button.GetComponent<WindowInputRegion>();
                if (inputRegion == null) inputRegion = button.gameObject.AddComponent<WindowInputRegion>();
                inputRegion.ReceiveMouseInput = true;
            }
        }

        private void EnsureMenuPointerRegion()
        {
            if (menuRoot == null) return;

            menuPointerRegion = menuRoot.GetComponent<MenuPointerRegion>();
            if (menuPointerRegion == null)
            {
                // Scene/Prefab에 별도 컴포넌트를 붙이지 않아도 자식 버튼의
                // Pointer Enter/Exit가 부모로 전달되어 자동 닫힘을 중단할 수 있게 한다.
                menuPointerRegion = menuRoot.gameObject.AddComponent<MenuPointerRegion>();
            }
        }

        private void UpdateMenuAutoClose()
        {
            if (menuRoot == null || !menuRoot.gameObject.activeSelf || autoCloseDelay <= 0f) return;

            if (menuPointerRegion != null && menuPointerRegion.PointerInside)
            {
                NotifyMenuActivity();
                return;
            }

            if (Time.unscaledTime >= closeMenuAtRealtime) CloseMenu();
        }

        private void NotifyMenuActivity()
        {
            closeMenuAtRealtime = Time.unscaledTime + autoCloseDelay;
        }

        private void HandlePresentationModeChanged(PresentationMode mode)
        {
            ApplyPresentationMode(mode);
        }

        private void ApplyPresentationMode(PresentationMode mode)
        {
            if (!ShouldMaintainCharacterHitAreas(mode) && stageDragGesture.IsWaiting)
            {
                stageDragGesture.Cancel();
                pressedInteractionSlot = -1;
            }

            if (mode != PresentationMode.Companion)
            {
                SetCompanionDragOutlineActive(false);
                CloseMenu();
                UpdateCharacterScreenLayout();
                return;
            }

            CloseAllOpenPanels();
            SyncFieldMenu(CurrentFieldMode());
            UpdateCharacterScreenLayout();
        }

        private void HandleFieldModeChanged(FieldMode mode, DungeonDefinition ignoredDungeon)
        {
            CloseMenu();
            SyncFieldMenu(mode);
        }

        private void SyncFieldMenu(FieldMode mode)
        {
            SetActiveIfNeeded(townMenuRoot, mode == FieldMode.Town);
            SetActiveIfNeeded(dungeonMenuRoot, mode == FieldMode.Dungeon);
        }

        private void ToggleMenu(int interactionSlot)
        {
            if (CurrentPresentationMode() != PresentationMode.Companion) return;
            if (menuRoot == null) return;

            bool sameOpenTarget = menuRoot.gameObject.activeSelf
                                  && selectedInteractionSlot == interactionSlot;
            selectedInteractionSlot = interactionSlot;

            if (sameOpenTarget)
            {
                CloseMenu();
                return;
            }

            SyncFieldMenu(CurrentFieldMode());
            UpdateCharacterScreenLayout();
            menuRoot.SetAsLastSibling();
            menuRoot.gameObject.SetActive(true);
            NotifyMenuActivity();
            PlayMenuEnterAnimation();
        }

        private void CloseMenu()
        {
            StopMenuEnterAnimation(true);

            if (menuRoot != null && menuRoot.gameObject.activeSelf)
            {
                menuRoot.gameObject.SetActive(false);
            }
        }

        private void PlayMenuEnterAnimation()
        {
            StopMenuEnterAnimation(true);
            if (!playMenuEnterTween || menuRoot == null) return;

            GameObject activeMenu = CurrentFieldMode() == FieldMode.Dungeon
                ? dungeonMenuRoot
                : townMenuRoot;
            if (activeMenu == null || !activeMenu.activeInHierarchy) return;

            RectTransform activeMenuRect = activeMenu.transform as RectTransform;
            if (activeMenuRect != null)
            {
                // LayoutGroup이 정한 최종 위치를 먼저 확정한 다음, Tween 중에는 해당 LayoutGroup만
                // 잠시 멈춘다. 버튼 RectTransform과 레이아웃 계산이 서로 위치를 덮어쓰는 것을 막는다.
                Canvas.ForceUpdateCanvases();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(activeMenuRect);
            }

            UnityEngine.UI.Button[] activeButtons =
                activeMenu.GetComponentsInChildren<UnityEngine.UI.Button>(false);
            if (activeButtons.Length == 0) return;

            for (int i = 0; i < activeButtons.Length; i++)
            {
                UnityEngine.UI.Button button = activeButtons[i];
                if (button == null || !button.gameObject.activeInHierarchy) continue;

                UnityEngine.UI.LayoutGroup layout =
                    button.transform.parent != null
                        ? button.transform.parent.GetComponent<UnityEngine.UI.LayoutGroup>()
                        : null;
                if (layout != null && layout.enabled && !pausedMenuLayouts.Contains(layout))
                {
                    pausedMenuLayouts.Add(layout);
                }
            }

            for (int i = 0; i < pausedMenuLayouts.Count; i++)
            {
                pausedMenuLayouts[i].enabled = false;
            }

            int first = reverseMenuButtonEnterOrder ? activeButtons.Length - 1 : 0;
            int end = reverseMenuButtonEnterOrder ? -1 : activeButtons.Length;
            int step = reverseMenuButtonEnterOrder ? -1 : 1;

            for (int i = first; i != end; i += step)
            {
                UnityEngine.UI.Button button = activeButtons[i];
                if (button == null || !button.gameObject.activeInHierarchy) continue;

                RectTransform buttonRect = button.transform as RectTransform;
                if (buttonRect == null) continue;

                CanvasGroup canvasGroup = null;
                float baseAlpha = 1f;
                if (fadeMenuButtonsOnEnter)
                {
                    canvasGroup = button.GetComponent<CanvasGroup>();
                    if (canvasGroup == null) canvasGroup = button.gameObject.AddComponent<CanvasGroup>();
                    baseAlpha = canvasGroup.alpha;
                }

                WindowInputRegion inputRegion = button.GetComponent<WindowInputRegion>();
                var state = new MenuButtonEnterState(
                    button,
                    buttonRect,
                    buttonRect.anchoredPosition,
                    canvasGroup,
                    baseAlpha,
                    button.interactable,
                    inputRegion,
                    inputRegion != null && inputRegion.ReceiveMouseInput);
                menuButtonEnterStates.Add(state);

                buttonRect.anchoredPosition = state.BasePosition + menuButtonEnterOffset;
                if (canvasGroup != null) canvasGroup.alpha = menuButtonEnterStartAlpha;

                if (blockMenuInputDuringEnter)
                {
                    button.interactable = false;
                    if (inputRegion != null) inputRegion.ReceiveMouseInput = false;
                }
            }

            if (menuButtonEnterStates.Count == 0)
            {
                RestoreMenuEnterState();
                return;
            }

            float duration = Mathf.Max(0.01f, menuButtonEnterDuration);
            float stagger = Mathf.Max(0f, menuButtonEnterStagger);
            float initialDelay = Mathf.Max(0f, menuEnterInitialDelay);
            Sequence sequence = DOTween.Sequence();

            for (int i = 0; i < menuButtonEnterStates.Count; i++)
            {
                MenuButtonEnterState state = menuButtonEnterStates[i];
                float startTime = initialDelay + stagger * i;

                sequence.Insert(
                    startTime,
                    state.Rect.DOAnchorPos(state.BasePosition, duration).SetEase(menuButtonEnterEase));

                if (state.CanvasGroup != null)
                {
                    sequence.Insert(
                        startTime,
                        state.CanvasGroup.DOFade(state.BaseAlpha, duration).SetEase(Ease.Linear));
                }
            }

            menuEnterSequence = sequence;
            sequence.SetUpdate(true)
                .SetLink(menuRoot.gameObject, LinkBehaviour.KillOnDisable)
                .OnComplete(() =>
                {
                    if (menuEnterSequence == sequence) menuEnterSequence = null;
                    RestoreMenuEnterState();
                });
        }

        private void StopMenuEnterAnimation(bool restoreState)
        {
            Sequence sequence = menuEnterSequence;
            menuEnterSequence = null;
            if (sequence != null && sequence.IsActive()) sequence.Kill(false);

            if (restoreState) RestoreMenuEnterState();
        }

        private void RestoreMenuEnterState()
        {
            for (int i = 0; i < menuButtonEnterStates.Count; i++)
            {
                MenuButtonEnterState state = menuButtonEnterStates[i];
                if (state.Rect != null) state.Rect.anchoredPosition = state.BasePosition;
                if (state.CanvasGroup != null) state.CanvasGroup.alpha = state.BaseAlpha;
                if (state.Button != null) state.Button.interactable = state.Interactable;
                if (state.InputRegion != null)
                {
                    state.InputRegion.ReceiveMouseInput = state.ReceiveMouseInput;
                }
            }
            menuButtonEnterStates.Clear();

            for (int i = 0; i < pausedMenuLayouts.Count; i++)
            {
                if (pausedMenuLayouts[i] != null) pausedMenuLayouts[i].enabled = true;
            }
            pausedMenuLayouts.Clear();
        }

        private void HandleGameModeClicked()
        {
            presentationModeController?.SetMode(PresentationMode.Game);
        }

        private void HandleReturnTownClicked()
        {
            if (fieldModeManager == null) return;

            if (FieldTransitionSequencer.Instance != null
                && FieldTransitionSequencer.Instance.TryPlayReturnToTown())
            {
                return;
            }

            fieldModeManager.TryReturnToTown();
        }

        private void HandleMenuActionClicked()
        {
            if (pendingMenuClose != null) StopCoroutine(pendingMenuClose);
            pendingMenuClose = StartCoroutine(CloseMenuNextFrame());
        }

        private IEnumerator CloseMenuNextFrame()
        {
            yield return null;
            pendingMenuClose = null;
            CloseMenu();
        }

        private void UpdateCharacterScreenLayout()
        {
            PresentationMode presentationMode = CurrentPresentationMode();
            if (!ShouldMaintainCharacterHitAreas(presentationMode)
                || interactionCanvasRect == null
                || stageCamera == null)
            {
                if (stageDragGesture.IsWaiting) stageDragGesture.Cancel();
                CloseMenu();
                SetCharacterHitAreaActive(false);
                return;
            }

            bool resting = dungeonRestEventController != null && dungeonRestEventController.IsResting;
            SetHitAreaActive(playerCharacterHitArea, false);
            for (int i = 0; i < restCharacterHitAreas.Length; i++)
                SetHitAreaActive(restCharacterHitAreas[i], false);

            bool anyVisible = false;
            bool selectedVisible = false;
            int firstVisibleSlot = -1;
            Vector2 firstRectMin = default;
            Vector2 firstRectMax = default;
            Vector2 selectedRectMin = default;
            Vector2 selectedRectMax = default;

            if (resting)
            {
                for (int i = 0; i < restCharacterHitAreas.Length; i++)
                {
                    SpriteRenderer renderer = dungeonRestEventController.GetInteractionRenderer(i);
                    if (!TryApplyRendererToHitArea(restCharacterHitAreas[i], renderer, out Vector2 rectMin, out Vector2 rectMax))
                        continue;

                    if (!anyVisible)
                    {
                        firstVisibleSlot = i;
                        firstRectMin = rectMin;
                        firstRectMax = rectMax;
                    }
                    anyVisible = true;

                    if (selectedInteractionSlot == i)
                    {
                        selectedVisible = true;
                        selectedRectMin = rectMin;
                        selectedRectMax = rectMax;
                    }
                }
            }
            else if (TryApplyRendererToHitArea(
                         playerCharacterHitArea,
                         playerRenderer,
                         out Vector2 rectMin,
                         out Vector2 rectMax))
            {
                anyVisible = true;
                firstVisibleSlot = -1;
                firstRectMin = rectMin;
                firstRectMax = rectMax;
                selectedVisible = selectedInteractionSlot == -1;
                selectedRectMin = rectMin;
                selectedRectMax = rectMax;
            }

            if (!anyVisible)
            {
                if (stageDragGesture.IsWaiting) stageDragGesture.Cancel();
                CloseMenu();
                return;
            }

            if (!selectedVisible)
            {
                selectedInteractionSlot = firstVisibleSlot;
                selectedRectMin = firstRectMin;
                selectedRectMax = firstRectMax;
            }

            if (presentationMode == PresentationMode.Companion && menuRoot != null)
            {
                Vector2 characterTop = new Vector2(
                    (selectedRectMin.x + selectedRectMax.x) * 0.5f,
                    selectedRectMax.y);
                menuRoot.anchoredPosition = characterTop + menuOffset;
            }
        }

        private bool TryApplyRendererToHitArea(
            CharacterHitArea hitArea,
            SpriteRenderer renderer,
            out Vector2 rectMin,
            out Vector2 rectMax)
        {
            rectMin = default;
            rectMax = default;
            if (hitArea == null || !TryGetCharacterScreenRect(renderer, out Vector2 screenMin, out Vector2 screenMax))
            {
                SetHitAreaActive(hitArea, false);
                return false;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    interactionCanvasRect, screenMin, null, out Vector2 localMin)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    interactionCanvasRect, screenMax, null, out Vector2 localMax))
            {
                SetHitAreaActive(hitArea, false);
                return false;
            }

            rectMin = Vector2.Min(localMin, localMax);
            rectMax = Vector2.Max(localMin, localMax);
            Vector2 padding = new Vector2(Mathf.Max(0f, hitPadding.x), Mathf.Max(0f, hitPadding.y));
            hitArea.Rect.anchoredPosition = (rectMin + rectMax) * 0.5f;
            hitArea.Rect.sizeDelta = Vector2.Max(rectMax - rectMin + padding * 2f, Vector2.one);
            SetHitAreaActive(hitArea, true);
            return true;
        }

        private bool TryGetCharacterScreenRect(
            SpriteRenderer interactionRenderer,
            out Vector2 screenMin,
            out Vector2 screenMax)
        {
            screenMin = default;
            screenMax = default;

            if (interactionRenderer == null
                || !interactionRenderer.enabled
                || !interactionRenderer.gameObject.activeInHierarchy
                || interactionRenderer.sprite == null)
            {
                return false;
            }

            Bounds bounds = interactionRenderer.bounds;
            Vector3 bottomLeft = stageCamera.WorldToScreenPoint(
                new Vector3(bounds.min.x, bounds.min.y, bounds.center.z));
            Vector3 topRight = stageCamera.WorldToScreenPoint(
                new Vector3(bounds.max.x, bounds.max.y, bounds.center.z));

            if (bottomLeft.z <= 0f || topRight.z <= 0f) return false;

            screenMin = Vector2.Min(bottomLeft, topRight);
            screenMax = Vector2.Max(bottomLeft, topRight);
            return screenMax.x >= 0f && screenMin.x <= Screen.width
                                    && screenMax.y >= 0f && screenMin.y <= Screen.height;
        }

        private SpriteRenderer ResolveInteractionRenderer(int restSlotIndex)
        {
            if (dungeonRestEventController != null && dungeonRestEventController.IsResting)
            {
                return dungeonRestEventController.GetInteractionRenderer(restSlotIndex);
            }

            return playerRenderer;
        }

        private void SetCharacterHitAreaActive(bool active)
        {
            for (int i = 0; i < characterHitAreas.Count; i++)
                SetHitAreaActive(characterHitAreas[i], active);
        }

        private static void SetHitAreaActive(CharacterHitArea hitArea, bool active)
        {
            if (hitArea != null && hitArea.Rect != null)
                SetActiveIfNeeded(hitArea.Rect.gameObject, active);
        }

        private PresentationMode CurrentPresentationMode() =>
            presentationModeController != null
                ? presentationModeController.CurrentMode
                : PresentationMode.Game;

        private FieldMode CurrentFieldMode() =>
            fieldModeManager != null ? fieldModeManager.CurrentMode : FieldMode.Town;

        private static void CloseAllOpenPanels()
        {
            PopupPanelManager manager = PopupPanelManager.Instance;
            if (manager == null) return;

            const int safetyLimit = 64;
            int closed = 0;
            while (manager.ActivePanelCount > 0 && closed < safetyLimit)
            {
                if (!manager.CloseTopPanel()) break;
                closed++;
            }
        }

        private void ValidateReferences()
        {
            if (presentationModeController == null)
            {
                Debug.LogError($"[CompanionInteractionMenuController] '{name}': Presentation Mode Controller가 " +
                               "연결되지 않아 Companion 모드를 판정할 수 없습니다.", this);
            }
            if (fieldModeManager == null)
            {
                Debug.LogError($"[CompanionInteractionMenuController] '{name}': Field Mode Manager가 연결되지 " +
                               "않아 마을/던전 메뉴를 구분할 수 없습니다.", this);
            }
            if (interactionCanvasRect == null || stageCamera == null || playerRenderer == null)
            {
                Debug.LogError($"[CompanionInteractionMenuController] '{name}': 캐릭터 클릭 영역 계산에 필요한 " +
                               "Canvas Rect, Stage Camera 또는 Player Renderer가 비어 있습니다.", this);
            }
            if (menuRoot == null || townMenuRoot == null || dungeonMenuRoot == null)
            {
                Debug.LogError($"[CompanionInteractionMenuController] '{name}': Companion 인터렉션 메뉴의 " +
                               "Menu Root/Town Menu/Dungeon Menu 연결이 비어 있습니다.", this);
            }
            if (CompanionDragTarget() == null)
            {
                Debug.LogError($"[CompanionInteractionMenuController] '{name}': Companion 캐릭터 이동에 " +
                               "사용할 Stage Visual Root Controller가 비어 있습니다.", this);
            }
        }

        private static void SetActiveIfNeeded(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        private sealed class CharacterHitArea
        {
            public CharacterHitArea(RectTransform rect, int restSlotIndex)
            {
                Rect = rect;
                RestSlotIndex = restSlotIndex;
            }

            public RectTransform Rect { get; }
            public int RestSlotIndex { get; }
        }

        private sealed class MenuButtonEnterState
        {
            public MenuButtonEnterState(
                UnityEngine.UI.Button button,
                RectTransform rect,
                Vector2 basePosition,
                CanvasGroup canvasGroup,
                float baseAlpha,
                bool interactable,
                WindowInputRegion inputRegion,
                bool receiveMouseInput)
            {
                Button = button;
                Rect = rect;
                BasePosition = basePosition;
                CanvasGroup = canvasGroup;
                BaseAlpha = baseAlpha;
                Interactable = interactable;
                InputRegion = inputRegion;
                ReceiveMouseInput = receiveMouseInput;
            }

            public UnityEngine.UI.Button Button { get; }
            public RectTransform Rect { get; }
            public Vector2 BasePosition { get; }
            public CanvasGroup CanvasGroup { get; }
            public float BaseAlpha { get; }
            public bool Interactable { get; }
            public WindowInputRegion InputRegion { get; }
            public bool ReceiveMouseInput { get; }
        }
    }
}
