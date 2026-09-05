using System.Reflection;
using Character;
using Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CommonEditor.Tests
{
    public sealed class CharacterSwapPanelTests
    {
        private GameObject host;

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
        }

        [Test]
        public void SuccessfulSwapRefresh_KeepsPanelOpenAndClearsPendingSelectionAndButton()
        {
            host = new GameObject("CharacterSwapPanel");
            host.SetActive(false);
            CharacterSwapPanel panel = host.AddComponent<CharacterSwapPanel>();
            Button button = host.AddComponent<Button>();
            CharacterDefinition pending = ScriptableObject.CreateInstance<CharacterDefinition>();
            try
            {
                button.interactable = true;
                Set(panel, "swapButton", button);
                Set(panel, "pendingCharacter", pending);
                host.SetActive(true);

                Invoke(panel, "RefreshAfterSuccessfulSwap");

                Assert.IsTrue(host.activeSelf, "성공한 교체가 모달을 닫으면 안 된다.");
                Assert.IsNull(Get(panel, "pendingCharacter"));
                Assert.IsFalse(button.interactable, "pending 선택이 비워지면 교체 버튼도 꺼져야 한다.");
            }
            finally
            {
                Object.DestroyImmediate(pending);
            }
        }

        private static void Invoke(object target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, method);
            info.Invoke(target, null);
        }

        private static object Get(object target, string field) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static void Set(object target, string field, object value) => target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
