using System;
using System.Collections.Generic;
using System.Text;
using Dungeon;
using Field;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 하단 MainMenu의 버튼들이 <b>어느 필드에서 보일지</b>를 정하는 단일 소유자. 어떤 버튼이 마을에서
    /// 보이고 던전에서 숨는지는 코드가 아니라 Inspector의 <see cref="ButtonVisibilityEntry"/> 목록이
    /// 결정하므로, 버튼이 늘어나도 이 파일을 고치지 않고 항목만 추가하면 된다.
    ///
    /// <b>버튼 표시 여부는 여기서만 바꾼다.</b> 예전에는 던전/마을 복귀 버튼을
    /// <see cref="FieldModeUIController"/>가, 회복소 버튼을 <see cref="FieldModeRuntimeController"/>가
    /// 각각 껐다 켰다 - 같은 대상을 두 곳이 건드리면 어느 쪽이 마지막이었는지에 따라 결과가 달라지므로
    /// 그 처리를 전부 이쪽으로 모았다. 나머지 컨트롤러는 전투/연출/상태 표시만 담당한다.
    ///
    /// <b>btnArea가 아니라 MainMenu에 붙인다.</b> 접힘 상태에서는 btnArea가 꺼져 있어
    /// <c>OnEnable</c>도 <c>FieldModeChanged</c>도 도착하지 않는다 - 항상 켜져 있는 MainMenu에 두어야
    /// 접혀 있는 동안의 필드 전환도 놓치지 않고, 펼쳤을 때 이미 맞는 버튼만 서 있다. 꺼진 부모 밑에서도
    /// <see cref="GameObject.SetActive"/>는 각 버튼의 <c>activeSelf</c>를 정상적으로 갱신한다.
    ///
    /// <b>켜고 끄는 것은 언제나 바깥 루트다.</b> btn_* 은 <c>WindowInputRegion</c>을 든 바깥 오브젝트가
    /// 안쪽 Button을 감싸는 구조라, 안쪽만 끄면 그림은 사라져도 그 사각형이 계속 네이티브 마우스 입력을
    /// 잡아 바탕화면 클릭 관통을 막는다. Inspector에는 반드시 btnArea 바로 아래의 바깥 루트를 연결한다.
    ///
    /// <b>표시 규칙 외에는 아무것도 건드리지 않는다.</b> <c>Button.interactable</c>, 기존 <c>onClick</c>,
    /// 호버 툴팁, 건축물 해금 조건(<c>BuildingCompletionButtonGate</c>)은 그대로 둔다 - 예를 들어 기도
    /// 버튼은 마을에서 <b>보이되</b> 교회를 짓기 전에는 여전히 눌리지 않는다. "보이는가"와 "누를 수
    /// 있는가"는 서로 다른 질문이고, 서로 다른 컴포넌트가 답한다.
    ///
    /// <b>숨길 때만 패널을 닫고, 다시 보일 때 열지는 않는다.</b> 마을 전용 패널을 열어둔 채 던전에
    /// 들어가면 던전 화면 위에 마을 UI가 떠 있게 되므로 그 경로만 막는다 - 반대로 마을로 돌아왔다고 해서
    /// 사용자가 닫아둔 패널을 대신 열어주지는 않는다.
    ///
    /// <b>같은 상태를 몇 번 적용해도 결과가 같다.</b> 목록을 그대로 반영하기만 하므로 최초 동기화,
    /// 재활성화, 전환 이벤트 중 무엇으로 들어와도 같은 화면이 나온다.
    /// </summary>
    [DisallowMultipleComponent]
    public class FieldModeMenuButtonVisibilityController : MonoBehaviour
    {
        /// <summary>
        /// 버튼 하나가 <b>어느 필드에서 보일지</b>를 적어둔 Inspector 항목. 두 체크가 모두 켜지면 양쪽에서
        /// 보이고, 모두 꺼지면 어느 필드에서도 보이지 않는다 - "둘 다 꺼짐"은 실수가 아니라 임시로
        /// 감춰두는 유효한 설정이므로 경고하지 않는다.
        /// </summary>
        [Serializable]
        public class ButtonVisibilityEntry
        {
            [Tooltip("Inspector에서 항목을 알아보기 위한 이름/메모. 동작에는 쓰이지 않고 로그에만 나온다.")]
            public string label;

            [Tooltip("켜고 끌 버튼의 바깥 루트(btnArea 바로 아래, WindowInputRegion이 붙은 오브젝트). " +
                     "안쪽 Button을 연결하면 꺼진 뒤에도 그 사각형이 네이티브 마우스 입력을 계속 잡는다.")]
            public GameObject buttonRoot;

            [Tooltip("마을에서 이 버튼을 보일지.")]
            public bool showInTown = true;

            [Tooltip("던전에서 이 버튼을 보일지.")]
            public bool showInDungeon = true;

            [Tooltip("이 버튼이 숨겨질 때 <b>열려 있으면</b> 함께 닫을 패널(선택). 비워두면 아무 패널도 " +
                     "닫지 않는다 - 다시 보이게 되어도 이 패널을 대신 열어주지는 않는다.")]
            public ModalPanel panelToCloseWhenHidden;

            /// <summary>이 항목이 해당 모드에서 보여야 하는지.</summary>
            public bool ShouldShowIn(FieldMode mode) =>
                mode == FieldMode.Town ? showInTown : showInDungeon;

            /// <summary>로그에 쓸 이름. 메모가 비어 있으면 연결된 오브젝트 이름을 쓴다.</summary>
            public string DescribeForLog()
            {
                if (!string.IsNullOrWhiteSpace(label)) return label;
                return buttonRoot != null ? buttonRoot.name : "(비어 있음)";
            }
        }

        [Header("Field Mode (필수)")]
        [Tooltip("모드 전환의 단일 소유자. 이 컴포넌트는 여기서 나오는 FieldModeChanged만 듣고 따라간다 - " +
                 "모드를 직접 판정하지 않는다. 이름으로 찾지 않으므로 반드시 직접 연결한다.")]
        [SerializeField] private FieldModeManager fieldModeManager;

        [Header("Menu Buttons")]
        [Tooltip("필드별 표시를 제어할 버튼 목록. 씬의 버튼 순서대로 적어두면 Inspector에서 읽기 쉽다 - " +
                 "순서 자체는 동작에 영향을 주지 않는다. 버튼이 늘어나면 항목만 추가하면 된다.")]
        [SerializeField] private List<ButtonVisibilityEntry> buttons = new List<ButtonVisibilityEntry>();

        /// <summary>구성 검증을 이미 한 번 했는지. 전환 때마다 같은 경고를 쏟지 않도록 최초 적용에서
        /// 한 번만 남긴다.</summary>
        private bool validated;

        /// <summary>중복으로 등록되어 <b>건너뛸</b> 항목들. 같은 버튼을 서로 다른 설정으로 두 번 등록하면
        /// 결과가 목록 순서에 좌우되므로, 조용히 한쪽이 이기게 두지 않고 뒤쪽 항목을 처리에서 뺀다.</summary>
        private readonly HashSet<ButtonVisibilityEntry> skippedEntries = new HashSet<ButtonVisibilityEntry>();

        private bool subscribed;

        /// <summary>가장 마지막으로 적용한 모드(읽기 전용 런타임 상태). 검증/디버깅용이다.</summary>
        public FieldMode AppliedMode { get; private set; } = FieldMode.Town;

        /// <summary>
        /// 구독을 <b>지웠다 다시 건다</b> - 껐다 켜기를 반복해도 핸들러가 두 번 붙지 않는다. 꺼져 있는
        /// 동안 전환을 놓쳤을 수 있으므로, 구독 직후 매니저가 <b>지금</b> 말하는 모드로 곧바로 맞춘다.
        /// </summary>
        private void OnEnable()
        {
            Subscribe();
            Apply(CurrentMode());
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (fieldModeManager == null) return;

            fieldModeManager.FieldModeChanged -= HandleFieldModeChanged;
            fieldModeManager.FieldModeChanged += HandleFieldModeChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;

            subscribed = false;
            if (fieldModeManager != null) fieldModeManager.FieldModeChanged -= HandleFieldModeChanged;
        }

        /// <summary>매니저가 없으면 매니저의 시작값과 같은 마을로 본다 - 무엇이 빠졌는지는
        /// <see cref="ValidateOnce"/>가 이미 남겼다.</summary>
        private FieldMode CurrentMode() =>
            fieldModeManager != null ? fieldModeManager.CurrentMode : FieldMode.Town;

        /// <summary>전환이 <b>확정된 뒤에</b> 불린다 - 매니저가 짝지어 확정한 모드를 그대로 반영한다.
        /// 던전 정보는 표시 규칙에 쓰이지 않으므로 여기서 보지 않는다(어느 던전이든 "던전"이다).</summary>
        private void HandleFieldModeChanged(FieldMode mode, DungeonDefinition dungeon)
        {
            Apply(mode);
        }

        /// <summary>목록 전체를 모드 하나에 맞춘다. 진입점이 최초 동기화든 재활성화든 전환 이벤트든
        /// 언제나 이 경로 하나를 지나므로, 몇 번을 다시 불러도 같은 결과가 나온다.</summary>
        private void Apply(FieldMode mode)
        {
            ValidateOnce();

            AppliedMode = mode;

            for (int i = 0; i < buttons.Count; i++)
            {
                ButtonVisibilityEntry entry = buttons[i];

                // 비어 있거나 중복으로 등록된 항목은 검증에서 이미 경고를 남겼다 - 나머지 정상 항목의
                // 처리를 여기서 멈추지 않는다.
                if (entry == null || entry.buttonRoot == null || skippedEntries.Contains(entry)) continue;

                bool show = entry.ShouldShowIn(mode);

                // 숨기기 전에 닫는다 - 버튼이 사라진 뒤에도 그 패널만 화면에 남아 있으면 안 된다.
                if (!show) CloseAttachedPanelIfOpen(entry);

                SetActiveIfNeeded(entry.buttonRoot, show);
            }
        }

        /// <summary>이 항목에 연결된 패널이 <b>열려 있을 때만</b> 닫는다. 여는 것은 언제나 사용자의 몫이라
        /// 반대 방향(다시 보이게 될 때 자동으로 열기)은 하지 않는다.</summary>
        private static void CloseAttachedPanelIfOpen(ButtonVisibilityEntry entry)
        {
            ModalPanel panel = entry.panelToCloseWhenHidden;
            if (panel == null) return;
            if (!panel.gameObject.activeSelf) return;

            // 기존 공개 API만 쓴다 - 패널 내부 규칙(닫기 처리, 입력 차단 해제)은 건드리지 않는다.
            panel.Close();
        }

        /// <summary>구성이 잘못된 부분을 <b>한 번만</b> 모아서 남긴다. 여기서 하는 일은 알리는 것과
        /// 중복 항목을 처리 대상에서 빼는 것뿐이며, 정상 항목의 처리는 그대로 계속된다.</summary>
        private void ValidateOnce()
        {
            if (validated) return;
            validated = true;

            if (fieldModeManager == null)
            {
                Debug.LogError($"[FieldModeMenuButtonVisibilityController] '{name}': Field Mode Manager가 " +
                               "연결되지 않았습니다 - 모드 전환을 따라갈 수 없어 버튼이 마을 상태에서 " +
                               "멈춥니다.", this);
            }

            if (buttons.Count == 0)
            {
                Debug.LogWarning($"[FieldModeMenuButtonVisibilityController] '{name}': 버튼 목록이 비어 있어 " +
                                 "아무 버튼도 제어하지 않습니다 - Inspector에서 btnArea 아래의 버튼 루트를 " +
                                 "등록하세요.", this);
                return;
            }

            var seenRoots = new Dictionary<GameObject, ButtonVisibilityEntry>();
            var missing = new StringBuilder();

            for (int i = 0; i < buttons.Count; i++)
            {
                ButtonVisibilityEntry entry = buttons[i];

                if (entry == null || entry.buttonRoot == null)
                {
                    if (missing.Length > 0) missing.Append(", ");
                    missing.Append($"{i}번({entry?.DescribeForLog() ?? "(항목 없음)"})");
                    continue;
                }

                if (seenRoots.TryGetValue(entry.buttonRoot, out ButtonVisibilityEntry first))
                {
                    skippedEntries.Add(entry);
                    Debug.LogWarning($"[FieldModeMenuButtonVisibilityController] '{name}': " +
                                     $"'{entry.buttonRoot.name}'이(가) {i}번 항목에 다시 등록돼 있습니다 " +
                                     $"(먼저 등록된 항목: '{first.DescribeForLog()}') - 설정이 서로 달라지면 " +
                                     "결과가 목록 순서에 좌우되므로 뒤쪽 항목은 무시합니다.", this);
                    continue;
                }

                seenRoots.Add(entry.buttonRoot, entry);
            }

            if (missing.Length > 0)
            {
                Debug.LogWarning($"[FieldModeMenuButtonVisibilityController] '{name}': Button Root가 비어 있는 " +
                                 $"항목이 있어 건너뜁니다 - {missing}. Inspector에서 버튼 루트를 연결하세요.", this);
            }
        }

        /// <summary>오브젝트 하나를 켜거나 끈다. 이미 같은 상태면 다시 부르지 않는다 - 결과는 같고,
        /// 자식들의 OnEnable/OnDisable이 불필요하게 다시 돌지 않는다.</summary>
        private static void SetActiveIfNeeded(GameObject target, bool active)
        {
            if (target == null) return;
            if (target.activeSelf == active) return;

            target.SetActive(active);
        }
    }
}
