using System.IO;
using System.Reflection;
using DesktopWindow;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CommonEditor.Tests
{
    public sealed class UnfocusedHoverInputModuleTests
    {
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";
        private const string ScriptPath = "Assets/Scripts/DesktopWindow/UnfocusedHoverInputModule.cs";
        private const string StandaloneInputModuleGuid = "4f231c4fb786f3946a6b90b886c48677";

        [Test]
        public void 무포커스_Hover_모듈은_기존_Standalone_입력설정을_상속한다()
        {
            Assert.IsTrue(typeof(StandaloneInputModule).IsAssignableFrom(typeof(UnfocusedHoverInputModule)));
        }

        [Test]
        public void Win32_좌상단_화면좌표를_Unity_좌하단_클라이언트좌표로_변환한다()
        {
            MethodInfo converter = typeof(UnfocusedHoverInputModule).GetMethod(
                "ConvertNativeToUnityScreenPosition",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(converter);
            var position = (Vector2)converter.Invoke(null, new object[] { 340, 260, 100, 50, 720 });

            Assert.AreEqual(new Vector2(240f, 510f), position);
        }

        [Test]
        public void 메인씬_EventSystem은_무포커스_Hover_모듈을_사용한다()
        {
            string scriptGuid = AssetDatabase.AssetPathToGUID(ScriptPath);
            string sceneYaml = File.ReadAllText(Path.GetFullPath(ScenePath));

            Assert.IsNotEmpty(scriptGuid);
            StringAssert.Contains($"guid: {scriptGuid}", sceneYaml);
            StringAssert.DoesNotContain($"guid: {StandaloneInputModuleGuid}", sceneYaml);
            StringAssert.Contains("m_HorizontalAxis: Horizontal", sceneYaml);
            StringAssert.Contains("m_VerticalAxis: Vertical", sceneYaml);
            StringAssert.Contains("m_InputActionsPerSecond: 10", sceneYaml);
            StringAssert.Contains("m_RepeatDelay: 0.5", sceneYaml);
        }
    }
}
