using System.Collections.Generic;
using DesktopWindow;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 직접 롱프레스로 드래그 가능한 모든 그룹(Stage/CharacterHUD/Combo/Progress/KillCount)을 한 곳에서
    /// 관리하는 상태 허브다. TransparentWindowController(Win32 클릭 관통/드래그 폴링을 소유)와 각
    /// ILayoutDraggable 구현체(StageVisualRootController, UiGroupDraggable) 사이의 중재자 역할만
    /// 한다 - Win32 API를 직접 호출하지 않는다.
    ///
    /// 넓은 HUDLayoutRoot(컨테이너, 입력 관통 전용) 자체는 여기 등록하지 않는다 - 실제로 드래그
    /// 가능한 대상은 그 하위의 작은 그룹들(ComboGroup/ProgressGroup/KillCountGroup)뿐이다. HUD
    /// 전체를 하나의 넓은 드래그 영역으로 두면 그 안의 빈 공간이 StageVisualRoot 위를 덮어서 캐릭터를
    /// 선택할 수 없게 되는 문제가 있었다.
    ///
    /// 그룹들은 구체 타입(StageVisualRootController/UiGroupDraggable)으로 직접 참조한다 - 예전에는
    /// Unity가 인터페이스 필드를 직렬화하지 못해 MonoBehaviour 타입으로 받고 Awake에서
    /// ILayoutDraggable로 캐스팅했는데, 이 방식은 Inspector에서 GameObject를 필드에 드래그할 때
    /// Unity가 그 오브젝트의 "어떤" MonoBehaviour를 담을지 알아서 고르는 문제가 있었다(예: 여러
    /// 스크립트가 같이 있는 오브젝트를 드래그하면 엉뚱한 컴포넌트가 담길 수 있음) - 결과적으로 특정
    /// 그룹이 Layout Mode에서 아예 반응하지 않는 회귀로 이어진 적이 있다. 구체 타입 필드는
    /// Inspector가 정확히 그 컴포넌트만 드래그 대상으로 받아들이므로 이 클래스의 오배선 자체가
    /// 불가능해진다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class LayoutModeController : MonoBehaviour
    {
        public static LayoutModeController Instance { get; private set; }

        public const string StageGroupId = StageVisualRootController.Id;
        public const string CharacterHudGroupId = "characterHud";
        public const string ComboGroupId = "combo";
        public const string ProgressGroupId = "progress";
        public const string KillCountGroupId = "killCount";
        // 새 목록에는 등록하지 않지만 기존 저장 파일의 dock 배치를 읽어 넘기기 위해 ID는 유지한다.
        public const string DockGroupId = "dock";

        [Header("직접 롱프레스 이동 대상")]
        [SerializeField] private StageVisualRootController stageDraggableSource;
        [SerializeField] private UiGroupDraggable characterHudDraggableSource;
        [SerializeField] private UiGroupDraggable comboDraggableSource;
        [SerializeField] private UiGroupDraggable progressDraggableSource;
        [SerializeField] private UiGroupDraggable killCountDraggableSource;

        [Header("롱프레스 조작감")]
        [Tooltip("대상을 누른 뒤 이동이 활성화될 때까지 기다리는 시간(초).")]
        [SerializeField] [Min(0.05f)] private float holdSeconds = 0.5f;
        [Tooltip("활성화 전에 허용할 포인터 이동 거리(px). 이보다 움직이면 해당 롱프레스는 취소됩니다.")]
        [SerializeField] [Min(0f)] private float preActivationMovementPixels = 8f;

        private List<ILayoutDraggable> allGroups;

        private ILayoutDraggable activeDragTarget;
        private string suppressedClickGroupId;

        public IReadOnlyList<ILayoutDraggable> AllGroups => allGroups;
        public float HoldSeconds => Mathf.Max(0.05f, holdSeconds);
        public float PreActivationMovementPixels => Mathf.Max(0f, preActivationMovementPixels);
        public bool HasActiveDrag => activeDragTarget != null;

        /// <summary>
        /// 에디터와 Win32 네이티브 폴링을 사용할 수 없는 플랫폼에서는 EventSystem의 pointer delta로
        /// 이동한다. Windows가 활성 빌드 타깃이어도 Editor Play 모드에서는 네이티브 창 핸들을
        /// 초기화하지 않으므로 반드시 pointer delta 경로를 사용해야 한다.
        /// </summary>
        public static bool UsesPointerEventDragDeltas
        {
            get
            {
#if UNITY_EDITOR || !UNITY_STANDALONE_WIN
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Windows 클릭 관통을 상시 해제할 수 있는 UI 그룹인지 판정한다. Stage는 실제 캐릭터
        /// hit area가 별도 WindowInputRegion을 제공하므로 넓은 배치 footprint를 입력 영역으로 쓰지 않는다.</summary>
        public static bool IsDirectPointerInputGroup(string groupId) => groupId != StageGroupId;

        public event System.Action<ILayoutDraggable> DragStarted;
        public event System.Action<ILayoutDraggable> DragEnded;

        private void Awake()
        {
            Instance = this;

            allGroups = new List<ILayoutDraggable>();

            Register(stageDraggableSource, "stageDraggableSource", "StageVisualRoot의 StageVisualRootController");
            characterHudDraggableSource = ResolveOptionalUiGroup(
                characterHudDraggableSource, CharacterHudGroupId);
            Register(characterHudDraggableSource, "characterHudDraggableSource", "CharacterHUD의 UiGroupDraggable");
            Register(comboDraggableSource, "comboDraggableSource", "ComboGroup의 UiGroupDraggable");
            Register(progressDraggableSource, "progressDraggableSource", "ProgressGroup의 UiGroupDraggable");
            Register(killCountDraggableSource, "killCountDraggableSource", "KillCountGroup의 UiGroupDraggable");

            Debug.Log($"[LayoutModeController] 초기화 완료 - 등록된 그룹: {allGroups.Count}/5 ({string.Join(", ", allGroups.ConvertAll(g => g.GroupId))})");
        }

        private static UiGroupDraggable ResolveOptionalUiGroup(UiGroupDraggable assigned, string groupId)
        {
            if (assigned != null) return assigned;

            UiGroupDraggable[] candidates = FindObjectsOfType<UiGroupDraggable>(true);
            foreach (UiGroupDraggable candidate in candidates)
            {
                if (candidate != null && candidate.GroupId == groupId) return candidate;
            }
            return null;
        }

        private void Register(ILayoutDraggable source, string fieldName, string hint)
        {
            if (source != null)
            {
                allGroups.Add(source);
            }
            else
            {
                Debug.LogError($"[LayoutModeController] {fieldName}가 비어 있습니다 - {hint}를 Inspector에서 연결해주세요.");
            }
        }

        /// <summary>롱프레스가 성립한 뒤 호출한다. 실제 Windows 드래그 폴링은 투명창 컨트롤러가 맡는다.</summary>
        public void BeginGroupDrag(ILayoutDraggable target)
        {
            if (target == null || !allGroups.Contains(target) || activeDragTarget != null) return;

            activeDragTarget = target;
            suppressedClickGroupId = target.GroupId;
            target.SetLayoutModeActive(true);
            DragStarted?.Invoke(target);
            TransparentWindowController.Instance?.BeginManualDrag();
        }

        /// <summary>TransparentWindowController.ContinueOrEndDrag가 매 프레임 호출한다.</summary>
        public void ApplyActiveDragDeltaPixels(int deltaXPixels, int deltaYPixels)
        {
            activeDragTarget?.ApplyDragDeltaPixels(deltaXPixels, deltaYPixels);
        }

        /// <summary>드래그가 끝났을 때(마우스 버튼을 뗐을 때) TransparentWindowController가 호출한다.</summary>
        public void EndActiveDrag()
        {
            ILayoutDraggable ended = activeDragTarget;
            if (ended == null) return;

            ended.SetLayoutModeActive(false);
            activeDragTarget = null;
            DragEnded?.Invoke(ended);
        }

        public void ClearClickSuppression()
        {
            suppressedClickGroupId = null;
        }

        public bool ConsumeClickSuppression(string groupId)
        {
            if (string.IsNullOrEmpty(groupId) || suppressedClickGroupId != groupId) return false;
            suppressedClickGroupId = null;
            return true;
        }

        public bool TryGetGroup(string groupId, out ILayoutDraggable group)
        {
            foreach (ILayoutDraggable candidate in allGroups)
            {
                if (candidate.GroupId == groupId)
                {
                    group = candidate;
                    return true;
                }
            }

            group = null;
            return false;
        }

        public void NotifyWorkAreaChanged(int widthPixels, int heightPixels)
        {
            foreach (ILayoutDraggable group in allGroups)
            {
                group.NotifyWorkAreaChanged(widthPixels, heightPixels);
            }
        }
    }
}
