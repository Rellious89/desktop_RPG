using System;
using System.Reflection;
using CharacterArchive;
using Common;
using Corruption;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CommonEditor.Tests
{
    /// <summary>
    /// 기능 패널의 <b>직접 Open 차단</b> 시험. 하단 버튼 게이트를 우회해 <c>Open()</c>(=활성화)이 직접
    /// 호출돼도, 전제 건물이 확정 완료되기 전에는 패널이 열리면 안 된다 - 미완공 상태에서 잠깐 보였다
    /// 닫히는 프레임도 없어야 하므로 <c>OnEnable</c>이 <c>base.OnEnable</c> 전에 스스로 비활성화한다.
    ///
    /// 용병 명부(건물 1)는 이번 작업에서 기도 패널(건물 2)과 같은 방식으로 차단을 추가했고, 기도 패널은
    /// 기존 차단이 그대로 유지되는지 회귀만 확인한다. 완공 판정은 공통 정책 하나만 재사용한다.
    ///
    /// <b>실제 저장 파일에 가지 않는다.</b> <see cref="SaveSystem"/>을 메모리 저장소로 바꿔 비어 있는
    /// (=미완공) 문서를 읽게 한다. EditMode에서는 활성화만으로 OnEnable이 오지 않으므로 비공개 OnEnable을
    /// 리플렉션으로 부른다.
    /// </summary>
    public sealed class PanelDirectOpenGateTests
    {
        private static readonly MethodInfo ConfigureSaveMethod = typeof(SaveSystem).GetMethod(
            "ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private GameObject panelObject;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(ConfigureSaveMethod, "SaveSystem.ConfigureForTests");
            // 비어 있는 저장 문서 - 어떤 건물도 완공되지 않은 상태.
            ConfigureSaveMethod.Invoke(null, new object[] { new FakeStorage(), null, null });
        }

        [TearDown]
        public void TearDown()
        {
            if (panelObject != null) Object.DestroyImmediate(panelObject);
            panelObject = null;
            ConfigureSaveMethod.Invoke(null, new object[] { null, null, null });
        }

        private static void InvokeOnEnable(Component panel)
        {
            typeof(ModalPanel)
                .GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(panel, null);
        }

        // ---- 11. 미완공 여관: 용병 명부 직접 Open 차단 ----

        [Test]
        public void 미완공_여관이면_용병_명부_패널은_직접_열어도_비활성화된다()
        {
            panelObject = new GameObject("pn_CharacterArchive");
            CharacterArchivePanel panel = panelObject.AddComponent<CharacterArchivePanel>();
            Assert.IsTrue(panelObject.activeSelf, "시작은 활성 상태에서 OnEnable 게이트를 검증한다");

            // 하단 버튼 게이트를 우회한 직접 Open()에 해당(활성화 → OnEnable 진입).
            InvokeOnEnable(panel);

            Assert.IsFalse(panelObject.activeSelf, "건물 1이 미완공이면 용병 명부는 열리지 않고 스스로 꺼진다");
        }

        // ---- 12. 미완공 교회: 기도 직접 Open 차단(기존 동작 회귀) ----

        [Test]
        public void 미완공_교회이면_기도_패널은_직접_열어도_비활성화된다()
        {
            panelObject = new GameObject("pn_Purification");
            PurificationPanel panel = panelObject.AddComponent<PurificationPanel>();
            Assert.IsTrue(panelObject.activeSelf);

            InvokeOnEnable(panel);

            Assert.IsFalse(panelObject.activeSelf, "건물 2가 미완공이면 기도 패널은 열리지 않고 스스로 꺼진다");
        }

        private sealed class FakeStorage : ISaveStorage
        {
            public bool WritesBlocked => false;
            public string BlockedReason => null;
            public SaveReadResult ReadPrimary() => SaveReadResult.Missing("fake://primary");
            public SaveReadResult ReadBackup() => SaveReadResult.Missing("fake://backup");

            public SaveWriteResult Write(string text) =>
                throw new InvalidOperationException("패널 차단 시험 중 저장이 시도되었습니다 - 이 경로는 읽기 전용입니다.");

            public SaveQuarantineResult QuarantinePrimary(string reason) =>
                SaveQuarantineResult.Moved("fake://corrupted/primary");
        }
    }
}
