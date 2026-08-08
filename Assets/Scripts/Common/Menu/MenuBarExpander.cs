using Dungeon;
using Field;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 하단 메뉴바를 <b>접힘(메뉴 버튼 하나) ↔ 펼침(가로 버튼 목록)</b> 두 상태로만 전환하는 컴포넌트.
    /// tgl_Panel에 붙여서 btn_menubar와 panel/btnArea를 서로 반대로 켜고 끈다 - 둘은 화면상 같은
    /// 위치(오른쪽 아래 기준 -120, 30)에 있으므로, 버튼 하나가 목록으로 늘어난 것처럼 보인다.
    ///
    /// <b>메뉴 버튼들의 기존 동작에는 전혀 관여하지 않는다.</b> btnArea 안의 버튼은 각자
    /// <see cref="ModalPanelOpener"/> 등으로 자기 패널을 열고, 이 컴포넌트는 btnArea라는 오브젝트
    /// 하나의 활성 상태만 바꾼다 - 그래서 나중에 버튼이 늘거나 클릭 동작이 바뀌어도 여기를 고칠 일이 없다.
    ///
    /// <b>자동 접힘은 "마지막 조작 이후"를 센다.</b> 펼친 순간부터 무조건 세면 버튼을 고르려고 마우스를
    /// 올려둔 사용자의 메뉴가 접힌다. 마우스가 메뉴 영역 안에 있는 동안에는 타이머를 계속 되돌리고,
    /// 영역을 벗어난 시점부터 <see cref="autoCollapseDelay"/>를 센다. 클릭도 그동안 포인터가 메뉴
    /// 안에 있으므로 같은 규칙에 자연히 포함된다.
    ///
    /// <b>필드 이동은 듣기만 한다.</b> 마을/던전 전환은 <see cref="FieldModeManager"/>가 그대로 소유하고,
    /// 여기서는 전환이 <b>받아들여졌을 때만</b> 발행되는 이벤트를 구독해 접기만 한다 - 이동 로직을
    /// 복제하거나 버튼을 가로채지 않는다.
    ///
    /// 연출(페이드/슬라이드)과 하위 메뉴는 이 단계에서 다루지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class MenuBarExpander : MonoBehaviour
    {
        [Tooltip("접혀 있을 때만 보이는 메뉴 진입 버튼(btn_menubar). 이 오브젝트의 Button이 눌리면 펼쳐진다.")]
        [SerializeField] private GameObject collapsedRoot;

        [Tooltip("펼쳤을 때 보이는 메뉴 버튼 목록(panel/btnArea). 이 오브젝트의 활성 상태만 바뀌고 " +
                 "안쪽 버튼들의 동작은 건드리지 않는다.")]
        [SerializeField] private GameObject expandedRoot;

        [Tooltip("켜면 씬 시작 시 접힌 상태로 맞춘다. 씬에 저장된 활성 상태와 무관하게 항상 " +
                 "btn_menubar만 보이는 상태로 시작하고 싶을 때 사용한다.")]
        [SerializeField] private bool collapseOnAwake = true;

        [Header("자동 접힘")]
        [Tooltip("메뉴 영역에서 마우스가 벗어난 뒤 자동으로 접히기까지의 시간(초). " +
                 "마우스가 메뉴 위에 있는 동안에는 이 시간이 계속 초기화되므로 접히지 않는다. " +
                 "0 이하로 두면 자동 접힘을 쓰지 않는다.")]
        [SerializeField] private float autoCollapseDelay = 5f;

        [Header("필드 이동 시 접힘")]
        [Tooltip("켜면 마을 ↔ 던전 이동이 실제로 일어난 직후 메뉴를 즉시 접는다. " +
                 "끄면 이동 후에도 메뉴가 남고, 위의 자동 접힘 규칙에 따라 접힌다.")]
        [SerializeField] private bool collapseOnFieldMove = true;

        [Tooltip("필드 이동을 알려 줄 FieldModeManager. 비워두면 씬에서 찾는다. " +
                 "이동 자체는 이 컴포넌트가 관여하지 않고 결과만 구독한다.")]
        [SerializeField] private FieldModeManager fieldModeManager;

        private Button collapsedButton;
        private MenuPointerRegion pointerRegion;

        // 이 시각(Time.unscaledTime)을 지나면 접는다. 마우스가 메뉴 안에 있으면 계속 뒤로 밀린다.
        private float collapseAtRealtime;

        /// <summary>지금 메뉴 목록이 펼쳐져 있는지.</summary>
        public bool IsExpanded => expandedRoot != null && expandedRoot.activeSelf;

        /// <summary>펼쳤을 때 보이는 메뉴 영역. <see cref="HoverTooltipController"/>가 툴팁을 붙일
        /// 버튼들을 찾는 기준으로 쓴다 - 같은 오브젝트를 Inspector에 두 번 연결하지 않기 위함이다.</summary>
        public GameObject ExpandedRoot => expandedRoot;

        private void Awake()
        {
            if (collapsedRoot == null)
            {
                Debug.LogError($"[MenuBarExpander] '{name}': 접힘 상태 오브젝트(btn_menubar)가 연결되지 " +
                               "않았습니다 - Inspector에서 연결하세요.", this);
            }
            else
            {
                collapsedButton = collapsedRoot.GetComponent<Button>();
                if (collapsedButton == null)
                {
                    Debug.LogError($"[MenuBarExpander] '{name}': '{collapsedRoot.name}'에 Button이 없어 " +
                                   "메뉴를 펼칠 수 없습니다.", this);
                }
            }

            if (expandedRoot == null)
            {
                Debug.LogError($"[MenuBarExpander] '{name}': 펼침 상태 오브젝트(btnArea)가 연결되지 " +
                               "않았습니다 - Inspector에서 연결하세요.", this);
            }
            else
            {
                // 마우스가 메뉴 위에 있는지 알아야 자동 접힘을 멈출 수 있다. 씬에서 따로 붙이는 것을
                // 잊으면 조용히 "조작 중에도 접히는" 동작이 되므로 여기서 직접 보장한다.
                pointerRegion = expandedRoot.GetComponent<MenuPointerRegion>();
                if (pointerRegion == null) pointerRegion = expandedRoot.AddComponent<MenuPointerRegion>();
            }

            if (fieldModeManager == null) fieldModeManager = FindObjectOfType<FieldModeManager>(true);
            if (fieldModeManager == null && collapseOnFieldMove)
            {
                Debug.LogWarning($"[MenuBarExpander] '{name}': FieldModeManager를 찾지 못해 필드 이동 시 " +
                                 "즉시 접기가 동작하지 않습니다 - Inspector에서 연결하세요.", this);
            }

            if (collapseOnAwake) SetExpanded(false);
        }

        private void OnEnable()
        {
            if (collapsedButton != null) collapsedButton.onClick.AddListener(Expand);

            if (fieldModeManager != null) fieldModeManager.FieldModeChanged += HandleFieldModeChanged;
        }

        private void OnDisable()
        {
            if (collapsedButton != null) collapsedButton.onClick.RemoveListener(Expand);

            if (fieldModeManager != null) fieldModeManager.FieldModeChanged -= HandleFieldModeChanged;
        }

        private void Update()
        {
            if (!IsExpanded || autoCollapseDelay <= 0f) return;

            // 메뉴 위에 마우스가 있는 동안은 "조작 중"이다 - 계속 뒤로 민다.
            if (pointerRegion != null && pointerRegion.PointerInside)
            {
                NotifyMenuActivity();
                return;
            }

            if (Time.unscaledTime >= collapseAtRealtime) Collapse();
        }

        /// <summary>메뉴 버튼을 숨기고 버튼 목록을 편다. btn_menubar의 onClick이 부르는 경로다.</summary>
        public void Expand() => SetExpanded(true);

        /// <summary>버튼 목록을 접고 메뉴 버튼만 남긴다. 자동 접힘, 필드 이동, 그리고 외부에서 직접
        /// 접고 싶을 때 모두 이 경로를 지난다.</summary>
        public void Collapse() => SetExpanded(false);

        public void Toggle() => SetExpanded(!IsExpanded);

        /// <summary>자동 접힘 시간을 지금부터 다시 센다. 마우스 위치로 잡히지 않는 조작(단축키 등)을
        /// 나중에 추가할 때 외부에서 부르는 지점이다.</summary>
        public void NotifyMenuActivity()
        {
            collapseAtRealtime = Time.unscaledTime + autoCollapseDelay;
        }

        /// <summary>두 오브젝트의 활성 상태는 항상 서로 반대다 - 이 메서드 하나만 상태를 바꾸므로
        /// 둘 다 켜지거나 둘 다 꺼진 중간 상태가 생기지 않는다.
        ///
        /// 접을 때 툴팁을 따로 지우지 않아도 된다 - btnArea가 꺼지면 안쪽 버튼의
        /// <see cref="HoverTooltipTrigger"/>가 OnDisable에서 예약과 표시를 모두 거둔다.</summary>
        public void SetExpanded(bool expanded)
        {
            if (collapsedRoot != null) collapsedRoot.SetActive(!expanded);
            if (expandedRoot != null) expandedRoot.SetActive(expanded);

            // 펼친 직후에는 마우스가 아직 메뉴 밖일 수 있다 - 그때부터 세기 시작한다.
            if (expanded) NotifyMenuActivity();
        }

        /// <summary>마을 ↔ 던전 전환이 <b>받아들여졌을 때만</b> 불린다(거부된 전환에서는 발행되지 않는다).
        /// 그래서 "실제로 이동한 경우"와 정확히 일치한다.</summary>
        private void HandleFieldModeChanged(FieldMode mode, DungeonDefinition dungeon)
        {
            if (!collapseOnFieldMove || !IsExpanded) return;

            Collapse();
        }
    }
}
