using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BuildingEditor.Tests
{
    /// <summary>
    /// EditMode 시험에서 <b>엔진 대신</b> MonoBehaviour 수명주기 콜백을 부르는 도우미.
    ///
    /// Play Mode가 아니면 Unity는 <c>OnEnable</c>/<c>OnDisable</c>을 호출하지 않는다. 그래서
    /// <see cref="GameObject.SetActive"/>만으로는 "패널이 열리면 구독하고 닫히면 끊는다" 같은
    /// 규칙을 확인할 수 없다 - 이 도우미가 활성 상태를 바꾼 <b>직후</b> 같은 콜백을 직접 불러
    /// 실제 실행 순서를 재현한다(이 저장소의 다른 EditMode 시험도 같은 방식으로 비공개 진입점을
    /// 직접 호출한다).
    ///
    /// <b>대상 씬을 Play Mode로 켜지 않는다</b>는 규칙을 지키면서도 실제 코드 경로를 그대로 지나기
    /// 위한 최소한의 이음매이며, 시험이 스스로 만든 오브젝트에만 쓴다.
    /// </summary>
    internal static class EditModeLifecycle
    {
        /// <summary>활성화하고 <c>OnEnable</c>을 부른다.</summary>
        public static void Enable(MonoBehaviour target)
        {
            if (!target.gameObject.activeSelf) target.gameObject.SetActive(true);
            Invoke(target, "OnEnable");
        }

        /// <summary>이미 활성화된(예: <c>Open()</c>이 켠) 대상의 <c>OnEnable</c>만 부른다.</summary>
        public static void RaiseEnable(MonoBehaviour target)
        {
            Assert.IsTrue(target.gameObject.activeSelf, "활성 상태가 아닌 대상에는 OnEnable이 오지 않는다");
            Invoke(target, "OnEnable");
        }

        /// <summary>이미 비활성화된(예: <c>Close()</c>가 끈) 대상의 <c>OnDisable</c>만 부른다.</summary>
        public static void RaiseDisable(MonoBehaviour target)
        {
            Assert.IsFalse(target.gameObject.activeSelf, "활성 상태인 대상에는 OnDisable이 오지 않는다");
            Invoke(target, "OnDisable");
        }

        /// <summary>비활성화하고 <c>OnDisable</c>을 부른다.</summary>
        public static void Disable(MonoBehaviour target)
        {
            if (target.gameObject.activeSelf) target.gameObject.SetActive(false);
            Invoke(target, "OnDisable");
        }

        public static void Invoke(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, methodName);
            method.Invoke(target, null);
        }
    }
}
