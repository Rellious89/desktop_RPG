using System.Collections;
using DesktopWindow;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Common
{
    /// <summary>
    /// 던전에서 자유 입력을 받는 타이핑 패드. 문자 입력은 별도의 공격 호출을 만들지 않고 기존
    /// GlobalKeyboardHook 경로를 그대로 통과시킨다. 이 컴포넌트는 패널 수명, 포커스, 입력 내용 초기화와
    /// 기능키(Enter)의 공격 제외만 담당한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyInputPanel : ModalPanel
    {
        [Header("Key Input")]
        [Tooltip("자유 입력을 받을 TMP Input Field. 비워두면 'InputField (TMP)' 이름으로 찾는다.")]
        [SerializeField] private TMP_InputField inputField;

        private Coroutine focusRoutine;
        private bool enterKeysExcluded;

        public TMP_InputField InputField => inputField;
        public bool HasRequiredReferences => inputField != null;

        protected override void OnModalOpened()
        {
            ResolveReferences();
            ConfigureInputField();
            AcquireEnterExclusions();

            if (inputField != null)
            {
                inputField.onSubmit.RemoveListener(HandleSubmit);
                inputField.onSubmit.AddListener(HandleSubmit);
            }
        }

        protected override void OnModalClosed()
        {
            if (inputField != null)
            {
                inputField.onSubmit.RemoveListener(HandleSubmit);
                inputField.SetTextWithoutNotify(string.Empty);
                inputField.DeactivateInputField();

                EventSystem eventSystem = EventSystem.current;
                if (eventSystem != null && eventSystem.currentSelectedGameObject == inputField.gameObject)
                    eventSystem.SetSelectedGameObject(null);
            }

            CancelFocusRoutine();
            ReleaseEnterExclusions();
        }

        protected override void RefreshContents()
        {
            ResolveReferences();
            ConfigureInputField();
            QueueInputFocus();
        }

        private void ResolveReferences()
        {
            if (inputField == null) inputField = FindChildComponent<TMP_InputField>("InputField (TMP)");
            if (inputField == null)
                Debug.LogError($"[KeyInputPanel] '{name}': TMP Input Field를 찾지 못했습니다.", this);
        }

        private void ConfigureInputField()
        {
            if (inputField == null) return;

            // Enter는 줄바꿈이 아니라 입력 새로고침이다. 실제 줄바꿈은 박스 폭에 맞춰 자동으로 일어난다.
            inputField.lineType = TMP_InputField.LineType.MultiLineSubmit;
            inputField.richText = false;
            if (inputField.textComponent != null)
            {
                inputField.textComponent.enableWordWrapping = true;
                inputField.textComponent.alignment = TextAlignmentOptions.TopLeft;
            }
        }

        private void HandleSubmit(string _)
        {
            if (inputField == null || !gameObject.activeInHierarchy) return;

            inputField.SetTextWithoutNotify(string.Empty);
            inputField.caretPosition = 0;
            inputField.selectionAnchorPosition = 0;
            inputField.selectionFocusPosition = 0;
            QueueInputFocus();
        }

        private void QueueInputFocus()
        {
            if (!isActiveAndEnabled || inputField == null) return;

            CancelFocusRoutine();
            focusRoutine = StartCoroutine(FocusAtEndOfFrame());
        }

        private IEnumerator FocusAtEndOfFrame()
        {
            // 열기 버튼의 PointerClick이 끝난 뒤 선택해야 같은 클릭이 입력창 포커스를 다시 빼앗지 않는다.
            yield return null;
            focusRoutine = null;

            if (!isActiveAndEnabled || inputField == null) yield break;
            inputField.Select();
            inputField.ActivateInputField();
            inputField.caretPosition = inputField.text?.Length ?? 0;
        }

        private void CancelFocusRoutine()
        {
            if (focusRoutine == null) return;
            StopCoroutine(focusRoutine);
            focusRoutine = null;
        }

        private void AcquireEnterExclusions()
        {
            if (enterKeysExcluded) return;
            enterKeysExcluded = true;
            GlobalKeyboardHook.AcquireScopedExcludedKey(KeyCode.Return);
            GlobalKeyboardHook.AcquireScopedExcludedKey(KeyCode.KeypadEnter);
        }

        private void ReleaseEnterExclusions()
        {
            if (!enterKeysExcluded) return;
            enterKeysExcluded = false;
            GlobalKeyboardHook.ReleaseScopedExcludedKey(KeyCode.Return);
            GlobalKeyboardHook.ReleaseScopedExcludedKey(KeyCode.KeypadEnter);
        }
    }
}
