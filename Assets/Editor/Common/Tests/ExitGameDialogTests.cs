using System;
using System.Collections.Generic;
using System.Reflection;
using Common;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CommonEditor.Tests
{
    /// <summary>
    /// 게임 종료 확인창(<see cref="ExitGameDialog"/>)의 카운트다운·확인·취소 시험.
    ///
    /// <b>실제로 애플리케이션을 종료하지 않는다.</b> 창이 제공하는 종료 실행 주입 지점
    /// (<see cref="ExitGameDialog.SetQuitActionForTests"/>)에 세는 동작만 끼워 넣으므로,
    /// <see cref="Application.Quit"/>도 Play Mode 정지도 일어나지 않는다 - 나머지 경로(중복 무시,
    /// 확인 버튼 잠금, 요청 1회)는 프로덕션과 완전히 같은 코드를 지난다.
    ///
    /// <b>시간은 시험이 직접 흘린다.</b> EditMode에는 프레임이 없어
    /// <see cref="Time.unscaledDeltaTime"/>이 늘지 않으므로, 창이 프레임마다 부르는 비공개
    /// <c>Tick(float)</c>에 원하는 간격을 직접 넣는다 - 시간의 출처만 바뀌고 계산은 같은 코드다.
    ///
    /// <b>번역 값 자체는 확인하지 않는다.</b> EditMode에는 Locale이 선택되어 있지 않을 수 있어 조회
    /// 결과가 환경에 달려 있다. 대신 <c>{0}</c>에 <b>어떤 숫자가 들어가는지</b>를 확인하려고, 시험은
    /// 표시 초(<see cref="ExitGameDialog.DisplayedSeconds"/>)와 그 값으로 조립한 문자열을 함께 본다.
    /// </summary>
    public sealed class ExitGameDialogTests
    {
        private static readonly MethodInfo TickMethod = typeof(ExitGameDialog).GetMethod(
            "Tick", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo AutoQuitSecondsField = typeof(ExitGameDialog).GetField(
            "autoQuitSeconds", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo ConfirmButtonField = typeof(ExitGameDialog).GetField(
            "confirmButton", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo CountdownTextField = typeof(ExitGameDialog).GetField(
            "countdownText", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo CloseButtonField = typeof(ModalPanel).GetField(
            "closeButton", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<Object> created = new List<Object>();

        private ExitGameDialog dialog;
        private Button confirmButton;
        private Button cancelButton;
        private TextMeshProUGUI countdownText;

        /// <summary>주입된 종료 실행이 불린 횟수. 실제 종료 대신 이것만 늘어난다.</summary>
        private int quitCalls;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(TickMethod, "Tick(float)");
            Assert.IsNotNull(AutoQuitSecondsField, "autoQuitSeconds");
            Assert.IsNotNull(ConfirmButtonField, "confirmButton");
            Assert.IsNotNull(CountdownTextField, "countdownText");
            Assert.IsNotNull(CloseButtonField, "closeButton");

            quitCalls = 0;

            // 씬과 같은 모양: 창 아래에 확인/취소 버튼과 안내 라벨이 있다. 부모를 두는 것은
            // ModalPanel이 InputBlocker를 부모 아래에 만들기 때문이다.
            var parent = NewObject("Dialog_UI");
            var root = NewObject("dialog_ExitGame");
            root.transform.SetParent(parent.transform, false);
            root.SetActive(false);

            confirmButton = NewButton(root, "btn_confirm");
            cancelButton = NewButton(root, "btn_cancle");

            var label = NewObject("lb_warningMSG");
            label.transform.SetParent(root.transform, false);
            countdownText = label.AddComponent<TextMeshProUGUI>();

            dialog = root.AddComponent<ExitGameDialog>();
            ConfirmButtonField.SetValue(dialog, confirmButton);
            CloseButtonField.SetValue(dialog, cancelButton);
            CountdownTextField.SetValue(dialog, countdownText);
            dialog.SetQuitActionForTests(() => quitCalls++);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
            created.Clear();
        }

        // ---- 도우미 ----

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            return go;
        }

        private Button NewButton(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            created.Add(go);
            return go.AddComponent<Button>();
        }

        private void SetAutoQuitSeconds(int seconds) => AutoQuitSecondsField.SetValue(dialog, seconds);

        /// <summary>엔진 대신 열기/닫기의 수명주기 콜백을 부른다 - EditMode에서는 활성 상태를 바꿔도
        /// OnEnable/OnDisable이 오지 않는다.</summary>
        private void OpenDialog()
        {
            dialog.Open();
            Assert.IsTrue(dialog.gameObject.activeSelf, "Open()이 창을 켜야 한다");
            Invoke("OnEnable");
        }

        private void CloseDialog()
        {
            dialog.Close();
            Assert.IsFalse(dialog.gameObject.activeSelf, "Close()가 창을 꺼야 한다");
            Invoke("OnDisable");
        }

        private void Invoke(string methodName)
        {
            MethodInfo method = typeof(ExitGameDialog).GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, methodName);
            method.Invoke(dialog, null);
        }

        /// <summary>한 프레임 분량의 실시간을 흘린다.</summary>
        private void Tick(float seconds) => TickMethod.Invoke(dialog, new object[] { seconds });

        /// <summary>여러 프레임에 걸쳐 시간을 흘린다 - 실제 재생처럼 잘게 나눠 넣는다.</summary>
        private void TickFrames(int frames, float secondsPerFrame)
        {
            for (int i = 0; i < frames; i++) Tick(secondsPerFrame);
        }

        // ---- 시험 ----

        [Test]
        public void Opening_InitializesCountdownToConfiguredSeconds()
        {
            SetAutoQuitSeconds(5);

            OpenDialog();

            Assert.AreEqual(5f, dialog.RemainingSeconds, 0.0001f, "남은 시간은 설정값부터 시작한다");
            Assert.AreEqual(5, dialog.DisplayedSeconds, "여는 즉시 전체 시간이 표시된다");
            Assert.IsFalse(dialog.IsQuitting, "열기만 해서는 종료 요청이 없다");
            Assert.AreEqual(0, quitCalls, "열기만 해서는 종료하지 않는다");
        }

        [Test]
        public void NonPositiveInspectorValue_IsClampedToOneSecond()
        {
            SetAutoQuitSeconds(0);

            OpenDialog();

            Assert.AreEqual(1, dialog.AutoQuitSeconds, "0 이하는 최소 1초로 보정한다");
            Assert.AreEqual(1f, dialog.RemainingSeconds, 0.0001f, "보정된 값부터 시작한다");
        }

        [Test]
        public void DisplayedSeconds_UseCeiling_AndDecreaseWithUnscaledTime()
        {
            SetAutoQuitSeconds(5);
            OpenDialog();

            Tick(0.5f); // 4.5 -> 5
            Assert.AreEqual(5, dialog.DisplayedSeconds, "4.5초는 올림해서 5로 보인다");

            Tick(0.5f); // 4.0 -> 4
            Assert.AreEqual(4, dialog.DisplayedSeconds, "정확히 4.0초는 4로 보인다");

            Tick(0.99f); // 3.01 -> 4
            Assert.AreEqual(4, dialog.DisplayedSeconds, "3.01초는 올림해서 4로 보인다");

            Tick(0.01f); // 3.0 -> 3
            Assert.AreEqual(3, dialog.DisplayedSeconds, "3.0초는 3으로 보인다");

            Assert.AreEqual(0, quitCalls, "아직 시간이 남아 있으면 종료하지 않는다");
        }

        [Test]
        public void ReachingZero_RequestsQuitExactlyOnce()
        {
            SetAutoQuitSeconds(2);
            OpenDialog();

            TickFrames(20, 0.1f);

            Assert.AreEqual(1, quitCalls, "0초 도달 시 종료 요청은 정확히 한 번이다");
            Assert.IsTrue(dialog.IsQuitting, "종료 요청 중 상태가 남는다");
            Assert.AreEqual(0f, dialog.RemainingSeconds, 0.0001f, "남은 시간은 0에서 멈춘다");
        }

        [Test]
        public void TimerKeepsRunning_WhenTimeScaleIsZero()
        {
            float originalScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;

                SetAutoQuitSeconds(2);
                OpenDialog();

                // Tick에 넣는 값은 unscaledDeltaTime이므로 timeScale과 무관하게 그대로 흐른다.
                TickFrames(20, 0.1f);

                Assert.AreEqual(1, quitCalls, "timeScale이 0이어도 종료 시간은 흐른다");
            }
            finally
            {
                Time.timeScale = originalScale;
            }
        }

        [Test]
        public void ConfirmClick_QuitsImmediatelyOnce()
        {
            SetAutoQuitSeconds(5);
            OpenDialog();

            confirmButton.onClick.Invoke();

            Assert.AreEqual(1, quitCalls, "확인 클릭은 남은 시간과 관계없이 즉시 종료한다");
            Assert.IsTrue(dialog.IsQuitting);
            Assert.IsFalse(confirmButton.interactable, "중복 입력을 막기 위해 확인 버튼이 잠긴다");
        }

        [Test]
        public void RepeatedConfirmClicks_QuitOnlyOnce()
        {
            SetAutoQuitSeconds(5);
            OpenDialog();

            confirmButton.onClick.Invoke();
            confirmButton.onClick.Invoke();
            confirmButton.onClick.Invoke();

            Assert.AreEqual(1, quitCalls, "확인을 연타해도 종료 요청은 한 번뿐이다");
        }

        [Test]
        public void AfterConfirm_TimerDoesNotRequestQuitAgain()
        {
            SetAutoQuitSeconds(2);
            OpenDialog();

            confirmButton.onClick.Invoke();
            Assert.AreEqual(1, quitCalls);

            // 종료가 한 프레임에 끝나지 않는 실제 상황을 흉내 낸다 - 그동안 타이머가 다시 종료를
            // 요청하면 안 된다.
            TickFrames(40, 0.1f);

            Assert.AreEqual(1, quitCalls, "확인 뒤 타이머가 다시 종료를 요청하지 않는다");
        }

        [Test]
        public void Cancel_DoesNotQuit()
        {
            SetAutoQuitSeconds(5);
            OpenDialog();

            Tick(1f);
            cancelButton.onClick.Invoke();
            Invoke("OnDisable");

            Assert.IsFalse(dialog.gameObject.activeSelf, "취소 버튼이 창을 닫는다");
            Assert.AreEqual(0, quitCalls, "취소는 종료하지 않는다");
            Assert.IsFalse(dialog.IsQuitting, "종료 요청 상태가 남지 않는다");
        }

        [Test]
        public void ClosedDialog_StopsCountingDown()
        {
            SetAutoQuitSeconds(2);
            OpenDialog();

            CloseDialog();

            // 닫힌 창은 Update가 돌지 않지만, 혹시 한 번 더 들어와도 종료하지 않아야 한다.
            TickFrames(40, 0.1f);

            Assert.AreEqual(0, quitCalls, "닫힌 뒤에는 종료 요청이 생기지 않는다");
        }

        [Test]
        public void Reopening_RestartsFromFullDuration()
        {
            SetAutoQuitSeconds(5);

            OpenDialog();
            Tick(3f);
            Assert.AreEqual(2, dialog.DisplayedSeconds, "3초를 흘린 뒤에는 2초가 남는다");

            CloseDialog();
            OpenDialog();

            Assert.AreEqual(5f, dialog.RemainingSeconds, 0.0001f, "다시 열면 전체 시간부터 시작한다");
            Assert.AreEqual(5, dialog.DisplayedSeconds, "표시도 전체 시간으로 돌아간다");
            Assert.IsTrue(confirmButton.interactable, "확인 버튼 상태도 복구된다");
        }

        [Test]
        public void ReopeningAfterConfirm_RestoresConfirmButton()
        {
            SetAutoQuitSeconds(5);

            OpenDialog();
            confirmButton.onClick.Invoke();
            Assert.IsFalse(confirmButton.interactable);

            CloseDialog();
            OpenDialog();

            Assert.IsTrue(confirmButton.interactable, "다시 열면 확인 버튼을 누를 수 있다");
            Assert.IsFalse(dialog.IsQuitting, "종료 요청 상태도 초기화된다");

            confirmButton.onClick.Invoke();
            Assert.AreEqual(2, quitCalls, "새로 연 창에서는 확인이 다시 동작한다");
        }

        [Test]
        public void ReopeningManyTimes_DoesNotStackConfirmListeners()
        {
            SetAutoQuitSeconds(5);

            for (int i = 0; i < 3; i++)
            {
                OpenDialog();
                CloseDialog();
            }

            OpenDialog();
            confirmButton.onClick.Invoke();

            Assert.AreEqual(1, quitCalls, "리스너가 쌓였다면 한 번의 클릭이 여러 번의 종료가 된다");
        }

        [Test]
        public void CountdownMessage_FormatsCurrentDisplayedSecond()
        {
            SetAutoQuitSeconds(5);
            OpenDialog();

            // 실제 조회 값은 Locale에 달려 있으므로, {0}에 들어가는 숫자가 무엇인지를 확인한다.
            Assert.AreEqual(5, dialog.DisplayedSeconds, "여는 순간의 표시 초");
            Assert.AreEqual("5초 후 자동 종료됩니다.", Format(dialog.DisplayedSeconds));

            Tick(1.5f);
            Assert.AreEqual(4, dialog.DisplayedSeconds, "1.5초 뒤의 표시 초는 올림해서 4다");
            Assert.AreEqual("4초 후 자동 종료됩니다.", Format(dialog.DisplayedSeconds));
        }

        /// <summary>씬에 저작된 문구 틀과 같은 모양으로 조립해 본다 - 확인하려는 것은 <c>{0}</c>에
        /// 들어가는 값이지 번역 자체가 아니다.</summary>
        private static string Format(int seconds) =>
            string.Format("{0}초 후 자동 종료됩니다.", seconds);

        [Test]
        public void StaticLocalizer_IsDisabled_SoOnlyOneOwnerWritesTheText()
        {
            var staticLocalizer = countdownText.gameObject.AddComponent<LocalizedTMPText>();
            Assert.IsTrue(staticLocalizer.enabled, "시작할 때는 켜져 있다");

            SetAutoQuitSeconds(5);

            UnityEngine.TestTools.LogAssert.Expect(
                LogType.Warning, new System.Text.RegularExpressions.Regex("LocalizedTMPText"));

            OpenDialog();

            Assert.IsFalse(staticLocalizer.enabled,
                "정적 로컬라이저가 꺼져야 두 컴포넌트가 같은 TMP를 번갈아 덮어쓰지 않는다");
        }

        [Test]
        public void MissingConfirmButton_DoesNotStopAutoQuit()
        {
            ConfirmButtonField.SetValue(dialog, null);
            SetAutoQuitSeconds(1);

            UnityEngine.TestTools.LogAssert.Expect(
                LogType.Warning, new System.Text.RegularExpressions.Regex("Confirm Button"));

            OpenDialog();
            TickFrames(15, 0.1f);

            Assert.AreEqual(1, quitCalls, "확인 버튼이 없어도 자동 종료는 그대로 동작한다");
        }

        [Test]
        public void MissingCountdownMessage_DoesNotStopAutoQuit()
        {
            // countdownMessage는 기본값(참조 없음) 그대로다 - 문구는 비지만 시간은 흘러야 한다.
            SetAutoQuitSeconds(1);

            UnityEngine.TestTools.LogAssert.Expect(
                LogType.Warning, new System.Text.RegularExpressions.Regex("Countdown Message"));

            OpenDialog();

            Assert.AreEqual(string.Empty, countdownText.text, "참조가 없으면 문구를 비운다");

            TickFrames(15, 0.1f);

            Assert.AreEqual(1, quitCalls, "번역 조회가 안 되어도 자동 종료는 그대로 동작한다");
        }
    }
}
