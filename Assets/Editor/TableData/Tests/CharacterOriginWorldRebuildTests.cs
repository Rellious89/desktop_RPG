using System;
using System.Collections.Generic;
using System.IO;
using Character;
using Dungeon;
using NUnit.Framework;
using UnityEditor;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// origin_world_id를 넣은 Character-only Rebuild의 경계 시험. World 에셋은 참조 대상으로 읽을
    /// 뿐 쓰지 않는다는 계약을 실제 디스크 내용으로 확인한다.
    /// </summary>
    public sealed class CharacterOriginWorldRebuildTests
    {
        [Test]
        public void CharacterOnlyRebuild_WritesExactOriginWorldReferencesWithoutChangingWorldAssets()
        {
            Dictionary<string, byte[]> worldsBefore = ReadWorldFiles();

            TableDataRebuildResult result =
                TableDataRebuilder.Rebuild(TableDataRebuildScope.CharacterSkillTables);

            Assert.IsTrue(result.Wrote, TableDataValidator.DescribeCounts(result.Validation));
            Assert.AreEqual(0, result.Validation.ErrorCount, TableDataValidator.DescribeCounts(result.Validation));
            AssertWorldFilesUnchanged(worldsBefore);

            foreach ((string characterId, string worldId) in ExpectedOrigins())
            {
                CharacterDefinition character = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(
                    TableDataPaths.CharacterAssetPath(characterId));
                WorldDefinition world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(
                    TableDataPaths.WorldAssetPath(worldId));

                Assert.IsNotNull(character, $"생성 CharacterDefinition이 없습니다: {characterId}");
                Assert.AreSame(world, character.OriginWorld,
                    $"{characterId}의 origin_world_id가 CSV와 다른 WorldDefinition을 가리킵니다.");
            }
        }

        private static IEnumerable<(string characterId, string worldId)> ExpectedOrigins()
        {
            yield return ("CatKnight", "1");
            yield return ("CatMage", "1");
            yield return ("RabbitHealer", "1");
            yield return ("ElfArcher", "2");
            yield return ("Barbarian", "2");
            yield return ("ElfGuardian", "2");
        }

        private static Dictionary<string, byte[]> ReadWorldFiles()
        {
            string folder = Path.GetFullPath(TableDataPaths.WorldOutputFolder);
            var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                contents[file] = File.ReadAllBytes(file);
            }

            return contents;
        }

        private static void AssertWorldFilesUnchanged(Dictionary<string, byte[]> before)
        {
            Dictionary<string, byte[]> after = ReadWorldFiles();
            CollectionAssert.AreEquivalent(before.Keys, after.Keys, "World 생성 파일 목록이 바뀌었습니다.");
            foreach (KeyValuePair<string, byte[]> pair in before)
            {
                CollectionAssert.AreEqual(pair.Value, after[pair.Key],
                    $"Character-only Rebuild가 World 생성 파일을 바꿨습니다: {pair.Key}");
            }
        }
    }
}
