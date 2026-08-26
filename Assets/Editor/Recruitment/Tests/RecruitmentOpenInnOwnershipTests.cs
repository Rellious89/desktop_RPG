using System.Reflection;
using NUnit.Framework;
using Recruitment;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RecruitmentEditor.Tests
{
    /// <summary>
    /// 여관 완료 확인 버튼(btn_Open_Inn)의 <b>표시 소유권</b> 시험. 이 버튼을 켜고 끄는 것은
    /// <c>TownBuildingInteractionController</c> 하나여야 하며, <see cref="RecruitmentUiController"/>는
    /// 모집 화면을 갱신하면서 이 버튼을 <b>건드리지 않는다</b>.
    ///
    /// 예전에는 <c>RecruitmentUiController.Apply()</c>가 매 프레임 <c>Set(openInnButton, false)</c>로
    /// 이 버튼을 다시 숨겨, 건축 타이머가 끝나고 뜬 완료 확인 버튼이 곧바로 사라졌다. 그 소유권 충돌이
    /// 사라졌는지를 두 가지로 못박는다 - (1) 직렬화 참조 자체가 없어졌는지, (2) 화면 갱신이 외부 버튼을
    /// 그대로 두는지.
    /// </summary>
    public sealed class RecruitmentOpenInnOwnershipTests
    {
        private GameObject controllerObject;
        private GameObject openInn;
        private GameObject progressRoot;
        private GameObject standbyRoot;
        private GameObject exhaustedRoot;
        private GameObject resultRoot;

        [TearDown]
        public void TearDown()
        {
            if (controllerObject != null) Object.DestroyImmediate(controllerObject);
            if (openInn != null) Object.DestroyImmediate(openInn);
            controllerObject = null;
            openInn = null;
        }

        // ---- 1. 소유권 참조 제거(회귀 방지) ----

        [Test]
        public void RecruitmentUiController는_openInnButton_참조를_더는_갖지_않는다()
        {
            FieldInfo field = typeof(RecruitmentUiController).GetField(
                "openInnButton", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.IsNull(field,
                "openInnButton 직렬화 참조가 남아 있으면 다시 소유권 충돌이 생길 수 있다 - 참조 자체를 없앤다");
        }

        // ---- 2. 화면 갱신이 완료 확인 버튼을 그대로 둔다 ----

        [Test]
        public void 화면을_숨김으로_갱신해도_외부_완료_버튼은_켜진_채로_남는다()
        {
            controllerObject = new GameObject("RecruitmentUi");
            RecruitmentUiController controller = controllerObject.AddComponent<RecruitmentUiController>();

            progressRoot = NewChild("progressRoot");
            standbyRoot = NewChild("standbyRoot");
            exhaustedRoot = NewChild("exhaustedRoot");
            resultRoot = NewChild("resultRoot");
            SetField(controller, "progressRoot", progressRoot);
            SetField(controller, "standbyRoot", standbyRoot);
            SetField(controller, "exhaustedRoot", exhaustedRoot);
            SetField(controller, "resultRoot", resultRoot);

            // TownBuildingInteractionController가 AwaitingConfirmation에서 켜 둔 완료 확인 버튼을 흉내 낸다.
            // 이 버튼은 RecruitmentUiController에 연결돼 있지 않다(연결할 참조 자체가 없다).
            openInn = new GameObject("btn_Open_Inn");
            openInn.SetActive(true);

            // 모집 화면을 여러 번 갱신한다(LateUpdate → Refresh → Apply에 해당).
            for (int i = 0; i < 3; i++) InvokeApplyHidden(controller);

            Assert.IsTrue(openInn.activeSelf, "모집 화면 갱신이 외부 완료 확인 버튼을 끄면 안 된다");
            Assert.IsFalse(progressRoot.activeSelf, "숨김 상태에서는 모집 화면들이 꺼진다");
            Assert.IsFalse(standbyRoot.activeSelf);
            Assert.IsFalse(exhaustedRoot.activeSelf);
            Assert.IsFalse(resultRoot.activeSelf);
        }

        private GameObject NewChild(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(controllerObject.transform, false);
            go.SetActive(true);
            return go;
        }

        private static void SetField(Object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }

        private static void InvokeApplyHidden(RecruitmentUiController controller)
        {
            MethodInfo apply = typeof(RecruitmentUiController).GetMethod(
                "Apply", BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(RecruitmentUiState) }, null);
            Assert.IsNotNull(apply, "Apply(RecruitmentUiState)");
            apply.Invoke(controller, new object[] { RecruitmentUiState.Hidden });
        }
    }
}
