using Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RecoveryEditor.Tests
{
    /// <summary>회복 완료 notifier가 완료 표시를 기록하기 전에 호출하는 토스트 수락 계약을 검증한다.</summary>
    public sealed class NotificationToastAcceptanceTests
    {
        [Test]
        public void TryShow_ReturnsFalseWhenToastManagerHasNoTemplate()
        {
            var gameObject = new GameObject("ToastManagerWithoutTemplate");
            try
            {
                LogAssert.Expect(LogType.Error, "[ToastManager] template이 지정되지 않았습니다.");
                ToastManager manager = gameObject.AddComponent<ToastManager>();

                Assert.IsFalse(manager.TryShow("회복 완료"),
                    "표시 리소스가 없는 매니저의 요청은 수락으로 처리하면 안 됩니다.");
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
