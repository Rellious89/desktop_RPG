using UnityEditor;
using UnityEngine;

namespace TableDataEditor
{
    /// <summary>
    /// 메뉴 두 개가 임포터의 유일한 진입점이다. <b>자동으로 도는 것은 없다</b> - CSV를 저장하는 순간
    /// 에셋이 바뀌면 사람이 의도하지 않은 시점에 프로젝트가 달라지므로, 언제 반영할지는 사람이 정한다.
    ///
    /// <b>Validate는 읽기만 한다.</b> 무엇이 잘못됐는지 확인하는 동안 프로젝트가 바뀌지 않으므로
    /// 마음 놓고 여러 번 눌러도 된다. <b>Rebuild는 같은 검사를 다시 하고, 오류가 하나라도 있으면
    /// 아무것도 쓰지 않는다.</b>
    /// </summary>
    public static class TableDataMenu
    {
        private const string MenuRoot = "Tools/Keybuddy/Table Data/";

        [MenuItem(MenuRoot + "Validate", priority = 100)]
        public static void Validate()
        {
            TableDataValidationResult result = TableDataValidator.Validate();
            TableDataValidator.LogToConsole(result, "[Table Data] Validate");

            string title = result.HasErrors ? "Table Data - 오류" : "Table Data - Validate";
            string body = result.HasErrors
                ? $"{TableDataValidator.DescribeCounts(result)}\n\n자세한 내용은 Console을 확인하세요.\n" +
                  "오류가 남아 있으면 Rebuild는 아무것도 쓰지 않습니다."
                : $"{TableDataValidator.DescribeCounts(result)}\n\n{result.Summary}\n\n" +
                  "오류가 없으므로 Rebuild를 실행할 수 있습니다.";

            EditorUtility.DisplayDialog(title, body, "확인");
        }

        [MenuItem(MenuRoot + "Rebuild", priority = 101)]
        public static void Rebuild()
        {
            Run(TableDataRebuildScope.All, "Rebuild", TableDataPaths.OutputRoot);
        }

        /// <summary>
        /// Character / Skill / CharacterSkill 세 표만 다시 만든다. <b>검사는 여덟 표 전부를 그대로</b>
        /// 하고, 쓰기만 이 세 폴더 안으로 좁힌다 - 캐릭터 표를 손보는 동안 기존 다섯 도메인의 생성
        /// 에셋 파일이 다시 쓰이지 않게 하기 위한 것이다.
        /// </summary>
        [MenuItem(MenuRoot + "Rebuild (Character / Skill / CharacterSkill only)", priority = 102)]
        public static void RebuildCharacterTables()
        {
            Run(TableDataRebuildScope.CharacterSkillTables,
                "Rebuild (Character / Skill / CharacterSkill)",
                TableDataPaths.CharacterOutputFolder + ", " +
                TableDataPaths.SkillOutputFolder + ", " +
                TableDataPaths.CharacterSkillOutputFolder);
        }

        private static void Run(TableDataRebuildScope scope, string label, string outputDescription)
        {
            TableDataRebuildResult result = TableDataRebuilder.Rebuild(scope);
            TableDataValidationResult validation = result.Validation;

            TableDataValidator.LogToConsole(
                validation, result.Wrote ? $"[Table Data] {label}" : $"[Table Data] {label} 중단");

            if (!result.Wrote)
            {
                EditorUtility.DisplayDialog(
                    $"Table Data - {label} 중단",
                    $"{TableDataValidator.DescribeCounts(validation)}\n\n" +
                    "오류가 있어 에셋을 하나도 만들거나 고치지 않았습니다.\n" +
                    "Console에서 원인을 확인하고 CSV를 고친 뒤 다시 실행하세요.",
                    "확인");
                return;
            }

            Debug.Log($"[Table Data] {label} 완료 - 새로 만든 에셋 {result.CreatedCount}개, " +
                      $"갱신한 에셋 {result.UpdatedCount}개. 출력 경로: {outputDescription}");

            EditorUtility.DisplayDialog(
                $"Table Data - {label} 완료",
                $"{validation.Summary}\n\n" +
                $"새로 만든 에셋 {result.CreatedCount}개, 갱신한 에셋 {result.UpdatedCount}개\n" +
                $"출력 경로: {outputDescription}",
                "확인");
        }
    }
}
