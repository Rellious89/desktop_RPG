using System;
using TableDataEditor;
using UnityEditor;

namespace TableDataEditor
{
    /// <summary>배치 검증에서도 두 서사 퀘스트 도메인만 재생성하기 위한 무대화면 없는 진입점.</summary>
    public static class CharacterStoryQuestRebuildCommand
    {
        [MenuItem("Tools/Keybuddy/Table Data/Rebuild Character Story Quest (Batch)", priority = 111)]
        public static void Rebuild()
        {
            TableDataDiagnosticLog log = CharacterStoryQuestTablePipeline.Rebuild();
            if (log.HasErrors) throw new InvalidOperationException($"CharacterStoryQuest Rebuild 오류: {log.ErrorCount}건");
        }
    }
}
