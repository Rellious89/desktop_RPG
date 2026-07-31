using Character;
using Common;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Recovery
{
    /// <summary>
    /// 회복소 슬롯 한 칸(list_RecoverySlot_1/2/3)의 표시와 드롭 수락을 담당한다. 슬롯 번호는
    /// <see cref="slotIndex"/>로 <b>에디터에서 명시</b>한다 - 계층 순서나 이름 파싱으로 추정하지 않는다
    /// (같은 이름의 자식이 슬롯마다 반복되므로 자동 탐색이 서로의 참조를 물어올 수 있다).
    ///
    /// <b>이 컴포넌트는 규칙을 갖지 않는다.</b> 상태 판정, 등록 가능 여부, 합류 처리는 전부
    /// <see cref="RecoveryStation"/>이 소유하고, 여기서는 그 결과를 프리팹의 기존 오브젝트에 그리기만
    /// 한다. 새 스프라이트나 새 계층을 만들지 않는다.
    ///
    /// <b>계층 전제(list_RecoverySlot_1 프리팹 기준).</b>
    /// <code>
    /// list_RecoverySlot_N              &lt;- 이 컴포넌트 + IDropHandler
    ///   list_EmptySlot_Recovery        &lt;- 비어 있을 때만 활성
    ///   list_RecoveryCharacter         &lt;- 대기/회복중/완료일 때만 활성
    ///     mask_portrait/sp_portrait
    ///     sp_name/lb_level, sp_name/lb_name
    ///     sp_stamina/Progress(+lb_percent)
    ///     Status/lb_Timer              &lt;- 남은 시간 라벨(자식 lb_time을 포함한다)
    ///       lb_time                    &lt;- 실제 시간 값
    ///     Status/btn_JoinParty         &lt;- 이 슬롯 하나만 합류시키는 버튼
    /// </code>
    ///
    /// <b>lb_Timer와 lb_time의 역할 구분.</b> lb_time은 lb_Timer의 <b>자식</b>이므로 lb_Timer를 끄면
    /// 시간 값도 함께 사라진다. 그래서 규칙을 다음과 같이 고정한다.
    /// <code>
    /// Pending          : lb_Timer 켬 - lb_time = "시작하면 걸릴 총 시간"(줄어들지 않는 예상값)
    /// Recovering       : lb_Timer 켬 - lb_time = "남은 시간"(줄어드는 실제 값)
    /// RecoveryComplete : lb_Timer 끔 - 대신 btn_JoinParty를 켠다
    /// </code>
    /// 예상값과 실제 카운트다운은 같은 자리에 표시되지만, Pending에서는 <b>패널이 시간 갱신 대상으로
    /// 잡지 않으므로</b> 값이 흐르지 않는다 - 아직 재화를 내지 않아 시작 시각 자체가 없기 때문이다.
    /// </summary>
    [DisallowMultipleComponent]
    public class RecoveryStationSlotView : MonoBehaviour, IDropHandler
    {
        [Header("Slot")]
        [Tooltip("이 슬롯의 번호(0부터). list_RecoverySlot_1 = 0, _2 = 1, _3 = 2. " +
                 "슬롯마다 반드시 다른 값을 넣는다 - 패널이 시작할 때 중복을 검사한다.")]
        [Min(0)]
        [SerializeField] private int slotIndex;

        [Header("Roots (프리팹의 기존 오브젝트를 직접 연결한다)")]
        [Tooltip("비어 있을 때 보여줄 오브젝트(list_EmptySlot_Recovery).")]
        [SerializeField] private GameObject emptyRoot;

        [Tooltip("캐릭터가 들어 있을 때 보여줄 오브젝트(list_RecoveryCharacter).")]
        [SerializeField] private GameObject characterRoot;

        [Header("Character Display (list_RecoveryCharacter 아래)")]
        [Tooltip("초상화(mask_portrait/sp_portrait).")]
        [SerializeField] private Image portraitImage;

        [Tooltip("이름(sp_name/lb_name).")]
        [SerializeField] private TextMeshProUGUI nameText;

        [Tooltip("레벨(sp_name/lb_level).")]
        [SerializeField] private TextMeshProUGUI levelText;

        [Tooltip("행동력 수치(sp_stamina/Progress/bg_Bar/lb_percent).")]
        [SerializeField] private TextMeshProUGUI staminaValueText;

        [Tooltip("행동력 막대(sp_stamina/Progress의 ProgressBarView).")]
        [SerializeField] private ProgressBarView staminaBar;

        [Header("Timer / Join (Status 아래)")]
        [Tooltip("남은 시간 라벨(Status/lb_Timer). 자식으로 lb_time을 갖고 있어, 끄면 시간 값도 함께 숨는다.")]
        [SerializeField] private GameObject timerRoot;

        [Tooltip("시간 값 텍스트(Status/lb_Timer/lb_time).")]
        [SerializeField] private TextMeshProUGUI timeText;

        [Tooltip("이 슬롯 하나만 합류시키는 버튼(Status/btn_JoinParty). " +
                 "패널 하단의 같은 이름 버튼과 혼동하지 않도록 반드시 이 슬롯 안의 것을 연결한다.")]
        [SerializeField] private Button joinButton;

        [Header("Formats (숫자 배치라 번역 대상이 아니다)")]
        [SerializeField] private string levelFormat = "Lv. {0}";
        [SerializeField] private string staminaFormat = "{0} / {1}";

        private RecoveryStationPanel owner;

        // 마지막으로 그린 값. 매 갱신마다 같은 문자열을 다시 만들지 않기 위한 캐시다.
        private RecoveryCharacterState lastState = (RecoveryCharacterState)(-1);
        private CharacterDefinition lastCharacter;
        private int lastDisplayedSeconds = int.MinValue;
        private bool hasDisplayedState;

        public int SlotIndex => slotIndex;

        /// <summary>지금 이 슬롯이 그리고 있는 상태. 패널이 시간 갱신 대상을 고를 때 본다.</summary>
        public RecoveryCharacterState DisplayedState => lastState;

        /// <summary>지금 이 슬롯이 실제로 무언가를 담고 있는지(대기 포함).</summary>
        public bool HasContent => hasDisplayedState && lastCharacter != null;

        /// <summary>패널이 자기 자신을 등록한다. 슬롯은 패널을 통해서만 도메인 API를 호출한다 -
        /// 슬롯이 직접 RecoveryService를 만지면 갱신/저장 순서가 패널과 어긋난다.</summary>
        public void Bind(RecoveryStationPanel panel)
        {
            owner = panel;

            if (joinButton != null)
            {
                joinButton.onClick.RemoveListener(HandleJoinClicked);
                joinButton.onClick.AddListener(HandleJoinClicked);
            }
        }

        public void Unbind()
        {
            if (joinButton != null) joinButton.onClick.RemoveListener(HandleJoinClicked);
            owner = null;
        }

        /// <summary>슬롯 전체를 지금 상태로 다시 그린다. 회복소가 없으면 빈 슬롯으로 그린다 -
        /// 회복소를 쓰지 않는 구성에서도 예외 없이 안전하게 보이게 하기 위함이다.</summary>
        public void Refresh()
        {
            RecoveryStation station = RecoveryService.Station;
            if (station == null || slotIndex >= station.SlotCount)
            {
                ApplyEmpty();
                return;
            }

            RecoverySlotView slot = station.GetSlot(slotIndex);
            if (slot.IsEmpty)
            {
                ApplyEmpty();
                return;
            }

            ApplyCharacter(slot);
        }

        /// <summary>
        /// 남은 시간만 다시 그린다(패널이 짧은 주기로 부른다). <b>상태가 바뀌었으면 전체 갱신으로
        /// 넘긴다</b> - 시간과 상태를 같은 스냅샷에서 읽으므로, "Recovering인데 0초"나 "완료됐는데
        /// 타이머가 남아 있는" 어긋난 화면이 만들어지지 않는다.
        /// </summary>
        public void RefreshRemainingTime()
        {
            RecoveryStation station = RecoveryService.Station;
            if (station == null || slotIndex >= station.SlotCount) return;

            RecoverySlotView slot = station.GetSlot(slotIndex);
            if (slot.IsEmpty || slot.State != lastState || slot.Character != lastCharacter)
            {
                Refresh();
                return;
            }

            if (slot.State != RecoveryCharacterState.Recovering) return;
            ApplyTimeText(slot.Remaining);
        }

        // ---- 표시 ----

        private void ApplyEmpty()
        {
            SetActiveSafe(emptyRoot, true);
            SetActiveSafe(characterRoot, false);

            lastState = RecoveryCharacterState.Available;
            lastCharacter = null;
            lastDisplayedSeconds = int.MinValue;
            hasDisplayedState = true;
        }

        private void ApplyCharacter(RecoverySlotView slot)
        {
            SetActiveSafe(emptyRoot, false);
            SetActiveSafe(characterRoot, true);

            bool characterChanged = slot.Character != lastCharacter;
            if (characterChanged && portraitImage != null)
            {
                Sprite portrait = slot.Character != null ? slot.Character.Portrait : null;
                portraitImage.sprite = portrait;
                // 초상화가 없으면 빈 사각형이 남지 않게 이미지를 숨긴다(교체 리스트와 같은 규칙).
                portraitImage.enabled = portrait != null;
            }
            if (characterChanged && nameText != null)
            {
                nameText.text = slot.Character != null ? slot.Character.DisplayName : string.Empty;
            }

            if (levelText != null)
            {
                CharacterRoster roster = CharacterRoster.Instance;
                int level = roster != null ? roster.GetLevel(slot.Character) : 0;
                levelText.text = string.Format(levelFormat, level);
            }

            if (staminaValueText != null)
            {
                staminaValueText.text = string.Format(staminaFormat, slot.CurrentStamina, slot.MaxStamina);
            }
            if (staminaBar != null) staminaBar.SetValue(slot.CurrentStamina, slot.MaxStamina);

            bool complete = slot.State == RecoveryCharacterState.RecoveryComplete;

            // 완료면 타이머 자리를 비우고 합류 버튼을 그 자리에 보여준다.
            SetActiveSafe(timerRoot, !complete);
            SetActiveSafe(joinButton != null ? joinButton.gameObject : null, complete);

            if (!complete)
            {
                // Pending은 "시작하면 걸릴 총 시간", Recovering은 "남은 시간". 둘 다 같은 텍스트에
                // 들어가지만 Pending은 패널의 시간 갱신 대상이 아니라서 값이 흐르지 않는다.
                ApplyTimeText(slot.Remaining, forceRewrite: true);
            }
            else
            {
                lastDisplayedSeconds = int.MinValue;
            }

            lastState = slot.State;
            lastCharacter = slot.Character;
            hasDisplayedState = true;
        }

        /// <summary>초 단위로 잘라서 표시하고, 표시값이 그대로면 문자열을 다시 만들지 않는다 -
        /// 짧은 주기로 불려도 프레임마다 할당이 생기지 않게 한다.</summary>
        private void ApplyTimeText(System.TimeSpan remaining, bool forceRewrite = false)
        {
            if (timeText == null) return;

            double totalSeconds = remaining.TotalSeconds;
            // 올림으로 표시한다 - 0.4초 남았을 때 "0"이 보이면 이미 끝난 것처럼 읽힌다.
            int seconds = totalSeconds <= 0d ? 0 : (int)System.Math.Ceiling(totalSeconds);

            if (!forceRewrite && seconds == lastDisplayedSeconds) return;
            lastDisplayedSeconds = seconds;

            timeText.text = RecoveryTimeFormat.Format(seconds);
        }

        private static void SetActiveSafe(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        // ---- 합류 / 드롭 ----

        private void HandleJoinClicked()
        {
            owner?.JoinSlot(slotIndex);
        }

        /// <summary>
        /// 캐릭터 리스트에서 끌어온 항목을 이 슬롯에 놓는다. 수락 조건(빈 슬롯인지, 그 캐릭터가 지금
        /// 등록 가능한지)은 <see cref="RecoveryStation.TryAddPendingToSlot"/>이 최종 판정하며,
        /// 여기서는 드래그 원본을 찾아 넘겨주기만 한다 - 판정을 UI에 복사하지 않는다.
        /// </summary>
        public void OnDrop(PointerEventData eventData)
        {
            if (owner == null || eventData == null || eventData.pointerDrag == null) return;

            var source = eventData.pointerDrag.GetComponent<CharacterRecoveryDragSource>();
            if (source == null || !source.IsDraggingToRecovery) return;

            owner.HandleSlotDrop(slotIndex, source.DraggedCharacter);
        }
    }
}
