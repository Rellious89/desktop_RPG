using System.IO;
using NUnit.Framework;
using Quest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuestEditorTests
{
    /// <summary>런타임에서 Instance가 null인 채로 배포되지 않게 desktopScene_ReSize 부트스트랩을
    /// 직접 검사한다. UI 모양은 보지 않고 서비스 존재와 세 필수 참조만 검증한다.</summary>
    public sealed class CharacterStoryQuestSceneWiringTests
    {
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";
        private const string QuestCatalogPath = "Assets/Generated/TableData/CharacterStoryQuest/CharacterStoryQuestCatalog.asset";
        private const string ObjectiveCatalogPath = "Assets/Generated/TableData/CharacterStoryQuestObjective/CharacterStoryQuestObjectiveCatalog.asset";

        [Test]
        public void DesktopResize_HasConfiguredCharacterStoryQuestService()
        {
            Assert.IsTrue(File.Exists(QuestCatalogPath), "먼저 Tools/Keybuddy/Table Data/Rebuild (Character Story Quest only)를 실행하세요.");
            Assert.IsTrue(File.Exists(ObjectiveCatalogPath), "먼저 Tools/Keybuddy/Table Data/Rebuild (Character Story Quest only)를 실행하세요.");
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            CharacterStoryQuestService[] services = Object.FindObjectsOfType<CharacterStoryQuestService>(true);
            Assert.AreEqual(1, services.Length, $"{scene.name}에는 CharacterStoryQuestService가 정확히 하나여야 합니다.");
            Assert.IsTrue(services[0].HasRequiredReferences, "Quest/Objective Catalog 및 CharacterRoster 참조가 모두 필요합니다.");
        }
    }
}
