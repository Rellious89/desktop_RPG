using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Common
{
    /// <summary>
    /// 게임 종료 확인창(dialog_ExitGame). 열려 있는 동안 남은 초를 세고, <b>확인 버튼</b>이나
    /// <b>0초 도달</b> 중 무엇이 먼저 오든 게임을 한 번 종료한다.
    ///
    /// <b>종료 요청은 정확히 한 번이다.</b> 확인 클릭과 타이머 만료는 같은 <see cref="RequestQuit"/>
    /// 하나를 지나고, 첫 요청이 <see cref="IsQuitting"/>을 세워 그 뒤의 요청을 전부 무시한다 - 확인을
    /// 연타하거나 확인을 누른 프레임에 타이머가 0에 닿아도 종료 실행은 한 번뿐이다.
    ///
    /// <b>시간은 <see cref="Time.unscaledDeltaTime"/>으로 센다.</b> 종료 확인은 게임이 멈춰 있어도
    /// (<see cref="Time.timeScale"/>이 0이어도) 흘러야 하는 시간이다 - 일시정지 중에 종료가 영영
    /// 오지 않는 자리를 만들지 않는다.
    ///
    /// <b>문구는 이 컴포넌트가 혼자 소유한다.</b> lb_warningMSG는 <c>{0}</c>에 남은 초가 들어가는 동적
    /// 문구라 정적 키를 쓰는 <see cref="LocalizedTMPText"/>와 함께 둘 수 없다 - 두 컴포넌트가 같은
    /// TMP를 서로 다른 근거로 덮어쓰면 어느 쪽이 마지막이었는지에 따라 화면이 달라지므로, 붙어 있으면
    /// 실행 중에 꺼 두고 경고를 남긴다.
    ///
    /// <b>표시 실패는 종료를 막지 않는다.</b> 번역 조회가 실패해도 남은 시간 계산은 그대로 진행된다 -
    /// 문구가 비는 것과 종료가 오지 않는 것은 심각도가 전혀 다르다.
    ///
    /// <b>저장은 여기서 하지 않는다.</b> 종료 직전 저장은 이미 <c>PlayerProgress.OnApplicationQuit</c>
    /// 등이 담당하므로, 이 창은 <see cref="Application.Quit"/>의 정상 종료 경로를 부르기만 한다 -
    /// 종료 버튼만 별도의 저장 순서를 만들면 저장 시점이 두 갈래로 갈라진다.
    ///
    /// <b>닫히면 언제나 취소다.</b> 취소 버튼, ESC, 외부 <see cref="ModalPanel.Close"/>, 그냥 비활성화
    /// 어느 쪽이든 <c>OnDisable</c>을 지나므로 카운트다운과 확인 버튼 상태가 그 자리에서 정리되고,
    /// 다음에 열 때는 <see cref="autoQuitSeconds"/>부터 새로 시작한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ExitGameDialog : ModalPanel
    {
        /// <summary>Inspector 값이 0 이하일 때 쓰는 최소 시간. 0초는 "열자마자 종료"라 사용자가 취소할
        /// 틈이 없으므로 확인창의 의미가 사라진다.</summary>
        private const int MinimumAutoQuitSeconds = 1;

        [Header("Exit Countdown")]
        [Tooltip("자동 종료까지의 시간(초). 팝업을 열 때마다 이 값부터 새로 시작한다 - 0 이하를 넣으면 " +
                 "1초로 보정한다.")]
        [SerializeField, Min(1)] private int autoQuitSeconds = 5;

        [Tooltip("종료 확인 버튼(btn_confirm). 클릭하면 남은 시간과 관계없이 즉시 종료한다 - 취소 버튼은 " +
                 "ModalPanel의 Close Button 칸(btn_cancle)에 연결한다.")]
        [SerializeField] private Button confirmButton;

        [Tooltip("남은 초를 표시할 TextMeshProUGUI(lb_warningMSG). {0}이 들어간 동적 문구라 같은 " +
                 "오브젝트에 LocalizedTMPText를 함께 두지 않는다 - 붙어 있으면 실행 중에 꺼 둔다.")]
        [SerializeField] private TextMeshProUGUI countdownText;

        [Tooltip("남은 초를 넣을 Localization 문구. 반드시 {0}을 포함해야 한다 - 비워두면 문구를 비우고 " +
                 "경고를 남긴다(코드에 문자열을 적어 메우지 않는다). 문구가 없어도 카운트다운 자체는 " +
                 "그대로 진행되어 시간이 다 되면 종료한다.")]
        [SerializeField] private LocalizedTextReference countdownMessage = new LocalizedTextReference();

        /// <summary>표시 초가 아직 한 번도 정해지지 않았음을 뜻하는 값. 첫 갱신이 반드시 일어나도록
        /// 실제로 나올 수 없는 값을 쓴다.</summary>
        private const int NoDisplayedSeconds = -1;

        /// <summary>실제로 종료를 실행하는 자리. 기본값은 프로덕션 동작(Editor는 Play Mode 정지,
        /// 빌드는 <see cref="Application.Quit"/>)이며, 시험이 여기만 바꿔 끼워 실제로 종료되지 않게
        /// 한다 - 종료 경로를 시험용으로 따로 만들지 않고 <b>같은 코드</b>를 지나게 하기 위함이다.</summary>
        private Action quitAction;

        /// <summary>남은 시간(초). 열려 있는 동안에만 의미가 있다.</summary>
        private float remainingSeconds;

        /// <summary>지금 화면에 적혀 있는 초. 같은 정수 초가 유지되는 동안에는 문구를 다시 만들지
        /// 않으려고 기억해 둔다 - 매 프레임 문자열을 조립하고 TMP를 다시 그리게 두지 않는다.</summary>
        private int displayedSeconds = NoDisplayedSeconds;

        private bool confirmListenerAttached;
        private bool messageSubscribed;

        /// <summary>이미 종료를 요청했는지. 한 번 켜지면 이 창이 닫힐 때까지 다시 꺼지지 않는다.</summary>
        public bool IsQuitting { get; private set; }

        /// <summary>남은 시간(초, 읽기 전용 런타임 상태). 검증/디버깅용이다.</summary>
        public float RemainingSeconds => remainingSeconds;

        /// <summary>지금 화면에 적혀 있는 초(읽기 전용 런타임 상태). 아직 정해지지 않았으면 -1이다.</summary>
        public int DisplayedSeconds => displayedSeconds;

        /// <summary>보정을 마친 자동 종료 시간. Inspector 값이 0 이하여도 최소 1초는 보장한다.</summary>
        public int AutoQuitSeconds => Mathf.Max(MinimumAutoQuitSeconds, autoQuitSeconds);

        /// <summary>
        /// 실제 종료를 실행할 동작을 바꿔 끼운다. <b>시험 전용이다</b> - 자동 시험이 진짜로
        /// <see cref="Application.Quit"/>을 부르거나 Play Mode를 정지시키지 않게 하려고 둔 자리이며,
        /// 프로덕션에서는 아무도 부르지 않으므로 기본 종료 동작이 그대로 쓰인다.
        /// </summary>
        /// <param name="action">종료 실행 동작. null을 넣으면 기본 동작으로 되돌아간다.</param>
        public void SetQuitActionForTests(Action action)
        {
            quitAction = action;
        }

        /// <summary>열릴 때마다 카운트다운을 처음부터 세운다 - 이전에 몇 초가 남아 있었든, 확인을
        /// 눌렀었든 상관없이 <see cref="AutoQuitSeconds"/>부터 다시 시작한다.</summary>
        protected override void OnModalOpened()
        {
            IsQuitting = false;
            remainingSeconds = AutoQuitSeconds;
            displayedSeconds = NoDisplayedSeconds;

            GuardStaticLocalizer();
            AttachConfirmListener();
            SubscribeMessage();

            if (confirmButton != null) confirmButton.interactable = true;

            // 첫 Update를 기다리지 않고 곧바로 전체 시간을 띄운다 - 창이 뜬 순간 문구가 비어 있으면
            // 안 된다.
            RefreshCountdownText();
        }

        /// <summary>닫히면 언제나 취소다 - 취소 버튼, ESC, 외부 Close, 그냥 비활성화 어느 쪽이든 이
        /// 경로를 지나므로 진행 중이던 카운트다운이 남지 않는다.</summary>
        protected override void OnModalClosed()
        {
            IsQuitting = false;
            remainingSeconds = 0f;
            displayedSeconds = NoDisplayedSeconds;

            UnsubscribeMessage();
            DetachConfirmListener();

            // 확인 버튼은 종료 요청 때 잠갔을 수 있다 - 다음에 열었을 때 눌리지 않는 채로 남지 않게
            // 여기서 되돌린다(여는 쪽에서도 한 번 더 켠다).
            if (confirmButton != null) confirmButton.interactable = true;
        }

        /// <summary>이 창에는 열 때마다 다시 그릴 데이터가 없다 - 표시는 카운트다운이 소유한다.</summary>
        protected override void RefreshContents()
        {
        }

        /// <summary>
        /// 남은 시간을 줄이고, <b>표시 초가 바뀐 프레임에만</b> 문구를 다시 만든다.
        ///
        /// 표시 초는 올림값이다(5.0~4.01 -> 5). 남은 시간이 0에 닿으면 확인을 누른 것과 같은 경로로
        /// 종료를 요청한다 - 이미 요청한 뒤라면 <see cref="RequestQuit"/>가 무시한다.
        /// </summary>
        private void Update()
        {
            // timeScale이 0이어도 흘러야 하는 시간이다 - 일시정지 중에 종료가 오지 않는 자리를
            // 만들지 않는다.
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>한 프레임 분량의 시간을 흘린다. <see cref="Update"/>와 나눠 둔 것은 <b>시간의
        /// 출처</b>만 갈아 끼울 수 있게 하기 위함이다 - EditMode 시험은 프레임이 흐르지 않으므로 이
        /// 메서드에 원하는 간격을 직접 넣어 같은 코드 경로를 지난다.</summary>
        /// <param name="unscaledDeltaSeconds">흘릴 시간(초). timeScale의 영향을 받지 않는 실시간이다.</param>
        private void Tick(float unscaledDeltaSeconds)
        {
            // 닫힌 창은 시간을 세지 않는다. 엔진은 꺼진 오브젝트의 Update를 부르지 않지만, 닫을 때
            // 남은 시간을 0으로 되돌리므로 이 자리에 한 번이라도 들어오면 곧바로 "0초 도달"로 읽힌다 -
            // 취소한 창이 종료를 부르는 경로를 상태가 아니라 조건으로 막아 둔다.
            if (!isActiveAndEnabled) return;
            if (IsQuitting) return;

            remainingSeconds -= unscaledDeltaSeconds;

            if (remainingSeconds <= 0f)
            {
                remainingSeconds = 0f;
                // 마지막 화면은 종료 직전 값(1초)에 머문다 - 0초를 한 프레임 스치듯 보여주지 않는다.
                RequestQuit();
                return;
            }

            RefreshCountdownText();
        }

        /// <summary>지금 남은 시간에 맞는 문구를 화면에 올린다. 같은 정수 초가 유지되는 동안에는
        /// 아무것도 하지 않는다 - 표시 초가 바뀌었을 때만 문자열을 조립한다.</summary>
        private void RefreshCountdownText()
        {
            int seconds = Mathf.CeilToInt(remainingSeconds);
            if (seconds == displayedSeconds) return;

            displayedSeconds = seconds;
            ApplyCountdownText();
        }

        /// <summary>
        /// 지금 표시 초로 문구를 다시 조립해 올린다. Locale이 바뀌었을 때도 <b>같은 표시 초로</b> 이
        /// 경로를 다시 지나므로 화면이 즉시 새 언어로 바뀐다.
        ///
        /// <b>여기서 실패해도 카운트다운은 계속된다.</b> 참조가 비었거나 조회가 실패하면 문구만 비우고
        /// 넘어간다 - 문구가 비는 것과 종료가 오지 않는 것은 심각도가 다르다.
        /// </summary>
        private void ApplyCountdownText()
        {
            if (countdownText == null) return;
            if (displayedSeconds == NoDisplayedSeconds) return;

            if (countdownMessage == null || !countdownMessage.HasReference)
            {
                // 참조가 없다고 해서 씬에 적혀 있던 임시 문구가 그대로 남아 있으면 안 된다.
                countdownText.text = string.Empty;
                return;
            }

            string localized;
            try
            {
                localized = countdownMessage.GetLocalizedString(displayedSeconds);
            }
            catch (Exception exception)
            {
                // 번역 조회 실패가 종료 카운트다운을 막지 않는다 - 문구만 포기하고 시간은 계속 흐른다.
                Debug.LogWarning($"[ExitGameDialog] '{name}': 종료 안내 문구를 가져오지 못했습니다 - " +
                                 $"문구 없이 카운트다운을 계속합니다. ({exception.Message})", this);
                countdownText.text = string.Empty;
                return;
            }

            countdownText.text = localized ?? string.Empty;
        }

        /// <summary>
        /// 게임 종료를 요청한다. <b>확인 버튼과 타이머 만료가 함께 쓰는 단 하나의 통로다</b> - 어느
        /// 쪽으로 들어와도 규칙(중복 무시, 확인 버튼 잠금, 실행 1회)이 똑같이 적용된다.
        ///
        /// 이미 요청한 뒤라면 아무 일도 하지 않는다.
        /// </summary>
        public void RequestQuit()
        {
            if (IsQuitting) return;

            // 실행보다 먼저 세운다 - 종료 실행이 동기적으로 다른 경로를 깨워 이 메서드로 다시 들어와도
            // 두 번째 요청은 여기서 막힌다.
            IsQuitting = true;

            // 눌린 확인 버튼이 계속 눌리는 채로 남지 않게 한다(종료가 한 프레임 안에 끝나지 않는다).
            if (confirmButton != null) confirmButton.interactable = false;

            ExecuteQuit();
        }

        /// <summary>실제 종료 실행. 주입된 동작이 있으면 그것을, 없으면 기본 종료 경로를 쓴다.</summary>
        private void ExecuteQuit()
        {
            if (quitAction != null)
            {
                quitAction();
                return;
            }

            QuitApplication();
        }

        /// <summary>프로덕션 종료 동작. Editor에서는 Play Mode를 정지하고, 빌드에서는 애플리케이션을
        /// 종료한다 - 어느 쪽이든 <c>OnApplicationQuit</c>이 도는 정상 종료 경로라 기존 저장 처리가
        /// 그대로 실행된다(이 창은 저장을 따로 부르지 않는다).</summary>
        private static void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---- 확인 버튼 ----

        private void AttachConfirmListener()
        {
            if (confirmButton == null)
            {
                Debug.LogWarning($"[ExitGameDialog] '{name}': Confirm Button이 연결되지 않아 확인 버튼으로 " +
                                 "종료할 수 없습니다 - Inspector에서 btn_confirm을 연결하세요(자동 종료는 " +
                                 "그대로 동작합니다).", this);
                return;
            }

            if (confirmListenerAttached) return;

            // 지웠다 다시 건다 - 여닫기를 반복해도 리스너가 쌓이지 않는다(한 번의 클릭이 두 번의 종료
            // 요청이 되면 안 된다).
            confirmButton.onClick.RemoveListener(RequestQuit);
            confirmButton.onClick.AddListener(RequestQuit);
            confirmListenerAttached = true;
        }

        private void DetachConfirmListener()
        {
            if (confirmButton != null) confirmButton.onClick.RemoveListener(RequestQuit);
            confirmListenerAttached = false;
        }

        // ---- 문구(로컬라이징) ----

        /// <summary>Locale 변경을 구독한다. 구독 자체가 최초 로드를 유발하지만 그 결과 문자열은 쓰지
        /// 않는다 - 인자 없이 조회된 값이라 <c>{0}</c>이 그대로 남아 있기 때문이다. 화면에 올릴 문구는
        /// 언제나 <see cref="ApplyCountdownText"/>가 <b>현재 표시 초로</b> 다시 조립한다.</summary>
        private void SubscribeMessage()
        {
            if (messageSubscribed) return;
            if (countdownMessage == null || !countdownMessage.HasReference)
            {
                Debug.LogWarning($"[ExitGameDialog] '{name}': Countdown Message에 Localization Table/Key가 " +
                                 "지정되지 않아 안내 문구를 비워 둡니다 - Inspector에서 Category와 Key를 " +
                                 "지정하세요(카운트다운과 자동 종료는 그대로 동작합니다).", this);
                if (countdownText != null) countdownText.text = string.Empty;
                return;
            }

            countdownMessage.StringChanged += HandleLocaleChanged;
            messageSubscribed = true;
        }

        private void UnsubscribeMessage()
        {
            if (!messageSubscribed) return;

            messageSubscribed = false;
            if (countdownMessage != null) countdownMessage.StringChanged -= HandleLocaleChanged;
        }

        /// <summary>Locale이 바뀌었을 때. 전달된 문자열은 <b>쓰지 않는다</b> - 인자가 빠진 값이므로
        /// 현재 표시 초로 다시 조립해야 <c>{0}</c> 자리가 채워진다.</summary>
        private void HandleLocaleChanged(string localizedWithoutArguments)
        {
            ApplyCountdownText();
        }

        /// <summary>같은 TMP를 정적 키로 덮어쓰는 컴포넌트가 남아 있으면 실행 중에는 꺼 둔다 - 두
        /// 컴포넌트가 같은 텍스트를 서로 다른 근거로 쓰면 어느 쪽이 마지막이었는지에 따라 문구가
        /// 달라진다. 씬 에셋을 고치지는 않으므로, 근본 정리(컴포넌트 제거)는 경고를 보고 사람이 한다.</summary>
        private void GuardStaticLocalizer()
        {
            if (countdownText == null) return;
            if (!countdownText.TryGetComponent(out LocalizedTMPText staticLocalizer)) return;
            if (!staticLocalizer.enabled) return;

            // 끄는 순간 OnDisable이 StringChanged 구독을 해제하므로, 이후 이 텍스트를 건드리는 것은
            // 이 창 하나뿐이다.
            staticLocalizer.enabled = false;

            Debug.LogWarning($"[ExitGameDialog] '{name}': '{countdownText.name}'에 LocalizedTMPText가 함께 " +
                             "붙어 있어 실행 중에는 꺼 둡니다 - 이 문구는 {0}에 남은 초가 들어가는 동적 " +
                             "문구이므로 정적 키로 덮어쓰면 안 됩니다. 에디터에서 해당 컴포넌트를 " +
                             "제거하세요.", countdownText);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (autoQuitSeconds < MinimumAutoQuitSeconds) autoQuitSeconds = MinimumAutoQuitSeconds;

            if (countdownText != null && countdownText.TryGetComponent(out LocalizedTMPText _))
            {
                Debug.LogWarning($"[ExitGameDialog] '{name}': Countdown Text('{countdownText.name}')에 " +
                                 "LocalizedTMPText가 함께 붙어 있습니다 - 남은 초가 들어가는 동적 문구이므로 " +
                                 "정적 키로 덮어쓰면 안 됩니다. 해당 컴포넌트를 제거하세요.", this);
            }
        }
#endif
    }
}
