using System.Collections.Generic;
using DesktopWindow;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// Layout Mode로 드래그 가능한 모든 그룹(Stage/Combo/Progress/KillCount/Dock)을 한 곳에서
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
        public const string ComboGroupId = "combo";
        public const string ProgressGroupId = "progress";
        public const string KillCountGroupId = "killCount";
        public const string DockGroupId = "dock";

        [Header("Layout Mode 대상")]
        [SerializeField] private StageVisualRootController stageDraggableSource;
        [SerializeField] private UiGroupDraggable comboDraggableSource;
        [SerializeField] private UiGroupDraggable progressDraggableSource;
        [SerializeField] private UiGroupDraggable killCountDraggableSource;
        [SerializeField] private UiGroupDraggable dockDraggableSource;

        public bool IsLayoutMode { get; private set; }

        private List<ILayoutDraggable> allGroups;

        private ILayoutDraggable activeDragTarget;

        public IReadOnlyList<ILayoutDraggable> AllGroups => allGroups;

        private void Awake()
        {
            Instance = this;

            allGroups = new List<ILayoutDraggable>();

            Register(stageDraggableSource, "stageDraggableSource", "StageVisualRoot의 StageVisualRootController");
            Register(comboDraggableSource, "comboDraggableSource", "ComboGroup의 UiGroupDraggable");
            Register(progressDraggableSource, "progressDraggableSource", "ProgressGroup의 UiGroupDraggable");
            Register(killCountDraggableSource, "killCountDraggableSource", "KillCountGroup의 UiGroupDraggable");
            Register(dockDraggableSource, "dockDraggableSource", "ControlDock의 UiGroupDraggable");

            Debug.Log($"[LayoutModeController] 초기화 완료 - 등록된 그룹: {allGroups.Count}/5 ({string.Join(", ", allGroups.ConvertAll(g => g.GroupId))})");
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

        /// <summary>ControlDock의 배치 버튼(LayoutModeToggleButton) 또는 F9 키가 호출한다.</summary>
        public void ToggleLayoutMode()
        {
            SetLayoutMode(!IsLayoutMode);
        }

        public void SetLayoutMode(bool active)
        {
            if (IsLayoutMode == active) return;

            IsLayoutMode = active;

            foreach (ILayoutDraggable group in allGroups)
            {
                group.SetLayoutModeActive(active);
            }

            if (!active)
            {
                // Layout Mode 종료 시 한 번 더 저장(드래그 종료 시 이미 저장되지만, 요구사항이 명시적으로
                // "Layout Mode 종료 시" 저장을 요구하므로 안전하게 한 번 더 시도한다).
                TransparentWindowController.Instance?.SaveOverlayPlacement();
            }

            Debug.Log(active
                ? "[LayoutModeController] Layout Mode ON - Stage/Combo/Progress/KillCount/Dock 영역을 직접 드래그해 옮길 수 있습니다."
                : "[LayoutModeController] Layout Mode OFF - 일반 클릭 관통/버튼 동작으로 돌아갑니다.");
        }

        /// <summary>UiGroupDraggable.OnPointerDown 또는 TransparentWindowController의 Stage 영역
        /// 폴링 판정이 호출한다. 실제 드래그 폴링 루프는 TransparentWindowController가 소유한다.</summary>
        public void BeginGroupDrag(ILayoutDraggable target)
        {
            if (!IsLayoutMode || target == null) return;

            activeDragTarget = target;
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
            activeDragTarget = null;
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
