using System;
using System.Collections.Generic;
using System.IO;
using Character;
using NUnit.Framework;
using UnityEditor;

namespace TableDataEditor.Tests
{
    /// <summary>
    /// 생성된 CharacterDefinition이 <b>실제로 어느 Entry를 가리키는지</b>를 확인하는 시험.
    ///
    /// 이름 참조는 Table GUID + Entry Key ID <b>두 숫자</b>로만 저장된다. 두 값이 조금이라도 어긋나면
    /// 화면에는 빈 문자열이나 엉뚱한 문구가 나오는데, 그 상태를 "카탈로그에 6명이 들어 있다" 같은
    /// 검사로는 절대 잡을 수 없다 - 그래서 06_Character Shared Data가 들고 있는 값과 생성 에셋에
    /// 적힌 값을 직접 맞춰 본다.
    ///
    /// <b>전부 읽기만 한다.</b> 에셋을 만들지도 고치지도 않는다.
    /// </summary>
    public sealed class CharacterLocalizationBindingTests
    {
        private const string CollectionFolder = "Assets/Localization/Tables/06_Character";
        private const string SharedDataPath = CollectionFolder + "/06_Character Shared Data.asset";
        private const string EnglishTablePath = CollectionFolder + "/06_Character_en.asset";
        private const string KoreanTablePath = CollectionFolder + "/06_Character_ko-KR.asset";

        /// <summary>
        /// 교환용 CSV는 <b>Assets 밖</b>에 있다(빌드 리소스가 아니다). 프로젝트 루트 기준 경로이므로
        /// 현재 작업 디렉터리에 기대지 않고 <see cref="UnityEngine.Application.dataPath"/>에서 역산한다.
        /// </summary>
        private const string ExchangeCsvProjectRelativePath = "TableData/Localization/06_Character.csv";

        private static string ExchangeCsvPath =>
            Path.Combine(
                Directory.GetParent(UnityEngine.Application.dataPath).FullName,
                ExchangeCsvProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>
        /// Addressables 그룹마다 <b>어느 에셋이 어떤 주소로</b> 등록되어야 하는지. 개수만 세면
        /// "06_Character라는 글자가 어딘가 있다"까지만 확인되고, 주소가 틀렸거나 GUID가 다른 에셋을
        /// 가리키는 상태를 잡지 못한다 - 그러면 런타임에 테이블을 못 찾는다.
        /// </summary>
        private static readonly (string GroupPath, string Address, string AssetPath)[] AddressableEntries =
        {
            ("Assets/AddressableAssetsData/AssetGroups/Localization-Assets-Shared.asset",
                SharedDataPath, SharedDataPath),
            ("Assets/AddressableAssetsData/AssetGroups/Localization-String-Tables-English (en).asset",
                "06_Character_en", EnglishTablePath),
            ("Assets/AddressableAssetsData/AssetGroups/Localization-String-Tables-Korean (South Korea) (ko-KR).asset",
                "06_Character_ko-KR", KoreanTablePath),
        };

        /// <summary>Character.csv의 name_key와 기대 표시명. 기준값은 기존 MotionProfile의 영어 표시명이다.</summary>
        private static readonly (string CharacterId, string Key, string EnglishName, string KoreanName)[] Expected =
        {
            ("CatKnight", "1", "CatKnight", "고양이기사"),
            ("ElfArcher", "2", "ElfArcher", "엘프궁수"),
            ("Barbarian", "3", "Barbarian", "바바리안"),
            ("ElfGuardian", "4", "ElfGuardian", "엘프수호자"),
            ("RabbitHealer", "5", "RabbitHealer", "토끼힐러"),
            ("CatMage", "6", "CatMage", "고양이마법사"),
        };

        // ---- Shared Data ----

        [Test]
        public void SharedData_HasExactlyTheSixNumericKeys()
        {
            Dictionary<string, string> idsByKey = ReadSharedEntries();

            CollectionAssert.AreEquivalent(
                new[] { "1", "2", "3", "4", "5", "6" }, idsByKey.Keys,
                "06_Character는 숫자 키 1..6만 가져야 한다.");

            foreach (KeyValuePair<string, string> pair in idsByKey)
            {
                Assert.IsFalse(string.IsNullOrEmpty(pair.Value), $"키 {pair.Key}의 내부 ID가 비어 있다.");
            }
        }

        [Test]
        public void BothLocaleTables_CarryTheAgreedTemporaryDisplayNames()
        {
            Dictionary<string, string> idsByKey = ReadSharedEntries();
            Dictionary<string, string> english = ReadLocaleValues(EnglishTablePath);
            Dictionary<string, string> korean = ReadLocaleValues(KoreanTablePath);

            foreach ((string characterId, string key, string englishName, string koreanName) in Expected)
            {
                string entryId = idsByKey[key];

                Assert.AreEqual(englishName, english[entryId], $"{characterId}의 영어 값");
                Assert.AreEqual(koreanName, korean[entryId], $"{characterId}의 한국어 값");
            }
        }

        [Test]
        public void ExchangeCsv_MatchesTheSharedDataExactly()
        {
            Assert.IsTrue(File.Exists(ExchangeCsvPath),
                $"'{ExchangeCsvProjectRelativePath}'가 없습니다 - " +
                "localization workflow §6이 요구하는 최초 Export 스냅샷입니다.");

            string[] lines = File.ReadAllText(ExchangeCsvPath).Replace("\r\n", "\n").Split('\n');

            Assert.AreEqual("Key,Id,English(en),Korean (South Korea)(ko-KR)", lines[0],
                "헤더는 Unity가 만든 기본 헤더 그대로여야 한다.");
            Assert.AreEqual(Expected.Length + 1, lines.Length, "헤더 한 줄 + 여섯 행이어야 한다.");

            Dictionary<string, string> idsByKey = ReadSharedEntries();

            for (int i = 0; i < Expected.Length; i++)
            {
                (string characterId, string key, string englishName, string koreanName) = Expected[i];
                string[] cells = lines[i + 1].Split(',');

                Assert.AreEqual(4, cells.Length, $"{characterId} 행의 칸 수");
                Assert.AreEqual(key, cells[0], $"{characterId}의 Key");
                Assert.IsTrue(string.IsNullOrEmpty(cells[1]) || cells[1] == idsByKey[key],
                    $"{characterId}의 Id가 비어 있지 않다면 Shared Data의 실제 내부 ID와 같아야 한다.");
                Assert.AreEqual(englishName, cells[2], $"{characterId}의 영어 값");
                Assert.AreEqual(koreanName, cells[3], $"{characterId}의 한국어 값");
            }
        }

        // ---- 생성 에셋이 가리키는 실제 참조 ----

        [Test]
        public void GeneratedCharacters_PointAtTheExactTableGuidAndKeyId()
        {
            string tableGuid = ReadSharedTableCollectionGuid();
            Assert.IsFalse(string.IsNullOrEmpty(tableGuid), "Shared Data의 Table Collection GUID를 읽지 못했다.");

            Dictionary<string, string> idsByKey = ReadSharedEntries();

            foreach ((string characterId, string key, string _, string __) in Expected)
            {
                var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(
                    TableDataPaths.CharacterAssetPath(characterId));

                Assert.IsNotNull(definition,
                    $"생성 에셋이 없습니다 - Table Data Rebuild를 먼저 실행하세요: {characterId}");

                var serialized = new SerializedObject(definition);
                SerializedProperty localizedName = serialized.FindProperty("localizedName");
                Assert.IsNotNull(localizedName, "localizedName 칸이 없습니다.");

                SerializedProperty table = localizedName
                    .FindPropertyRelative("m_TableReference")
                    .FindPropertyRelative("m_TableCollectionName");
                SerializedProperty keyId = localizedName
                    .FindPropertyRelative("m_TableEntryReference")
                    .FindPropertyRelative("m_KeyId");
                SerializedProperty keyName = localizedName
                    .FindPropertyRelative("m_TableEntryReference")
                    .FindPropertyRelative("m_Key");

                Assert.AreEqual("GUID:" + tableGuid, table.stringValue,
                    $"{characterId}가 06_Character가 아닌 다른 Table을 가리킨다.");
                Assert.AreEqual(idsByKey[key], keyId.longValue.ToString(),
                    $"{characterId}가 숫자 키 {key}의 Entry Key ID를 가리켜야 한다.");
                Assert.AreEqual(string.Empty, keyName.stringValue,
                    "Key ID로 가리키므로 이름 기반 참조는 비어 있어야 한다(기존 규칙).");

                Assert.IsTrue(definition.HasLocalizedName, $"{characterId}의 이름 참조가 비어 있다.");
            }
        }

        // ---- Addressables ----

        [Test]
        public void EachLocalizationGroup_HasExactlyOne06CharacterEntryWithTheRightAddressAndGuid()
        {
            foreach ((string groupPath, string address, string assetPath) in AddressableEntries)
            {
                Assert.IsTrue(File.Exists(groupPath), $"'{groupPath}'가 없습니다.");

                List<(string Guid, string Address)> entries = ReadAddressableEntries(groupPath);

                var matching = new List<(string Guid, string Address)>();
                foreach ((string entryGuid, string entryAddress) in entries)
                {
                    if (entryAddress.IndexOf("06_Character", StringComparison.Ordinal) >= 0)
                    {
                        matching.Add((entryGuid, entryAddress));
                    }
                }

                Assert.AreEqual(1, matching.Count,
                    $"'{groupPath}'에는 06_Character 항목이 정확히 하나만 있어야 한다(그룹마다 1건).");

                // 주소가 한 글자만 달라도 런타임 조회가 실패한다 - 부분 일치가 아니라 완전 일치를 본다.
                Assert.AreEqual(address, matching[0].Address, $"'{groupPath}'의 06_Character 주소");

                // GUID는 그 그룹이 <b>정확히 어떤 파일</b>을 들고 있는지다. .meta의 GUID와 맞춰 본다.
                Assert.AreEqual(ReadMetaGuid(assetPath), matching[0].Guid,
                    $"'{groupPath}'의 항목이 '{assetPath}'가 아닌 다른 에셋을 가리킨다.");
            }
        }

        [Test]
        public void TheThreeLocalizationAssets_HaveDistinctGuids()
        {
            // 세 항목이 같은 GUID를 가리키면 위 시험이 통과해도 실제로는 한 파일만 등록된 것이다.
            var guids = new HashSet<string>(StringComparer.Ordinal);

            foreach ((string _, string _, string assetPath) in AddressableEntries)
            {
                string guid = ReadMetaGuid(assetPath);
                Assert.IsFalse(string.IsNullOrEmpty(guid), $"'{assetPath}'의 .meta GUID를 읽지 못했다.");
                Assert.IsTrue(guids.Add(guid), $"'{assetPath}'의 GUID가 다른 에셋과 겹친다.");
            }

            Assert.AreEqual(3, guids.Count);
        }

        // ---- 도우미 (전부 읽기 전용) ----

        /// <summary>
        /// Addressables 그룹 파일의 항목을 <c>(GUID, Address)</c> 짝으로 읽는다. 직렬화 순서는
        /// <c>- m_GUID:</c> 다음 줄에 <c>m_Address:</c>가 오는 형태다.
        /// </summary>
        private static List<(string Guid, string Address)> ReadAddressableEntries(string groupPath)
        {
            var entries = new List<(string Guid, string Address)>();
            string pendingGuid = null;

            foreach (string line in File.ReadAllLines(groupPath))
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("- m_GUID:", StringComparison.Ordinal))
                {
                    pendingGuid = trimmed.Substring("- m_GUID:".Length).Trim();
                    continue;
                }

                if (pendingGuid == null || !trimmed.StartsWith("m_Address:", StringComparison.Ordinal)) continue;

                entries.Add((pendingGuid, trimmed.Substring("m_Address:".Length).Trim()));
                pendingGuid = null;
            }

            return entries;
        }

        /// <summary>에셋의 <c>.meta</c>가 들고 있는 GUID. Addressables 항목이 가리켜야 하는 값이다.</summary>
        private static string ReadMetaGuid(string assetPath)
        {
            string metaPath = assetPath + ".meta";
            Assert.IsTrue(File.Exists(metaPath), $"'{metaPath}'가 없습니다.");

            foreach (string line in File.ReadAllLines(metaPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("guid:", StringComparison.Ordinal))
                {
                    return trimmed.Substring("guid:".Length).Trim();
                }
            }

            return null;
        }

        /// <summary>Shared Data의 숫자 키 -> 내부 Entry Key ID.</summary>
        private static Dictionary<string, string> ReadSharedEntries()
        {
            Assert.IsTrue(File.Exists(SharedDataPath), $"'{SharedDataPath}'가 없습니다.");

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            string pendingId = null;

            foreach (string line in File.ReadAllLines(SharedDataPath))
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("- m_Id:", StringComparison.Ordinal))
                {
                    pendingId = trimmed.Substring("- m_Id:".Length).Trim();
                    continue;
                }

                if (pendingId == null || !trimmed.StartsWith("m_Key:", StringComparison.Ordinal)) continue;

                map[trimmed.Substring("m_Key:".Length).Trim()] = pendingId;
                pendingId = null;
            }

            return map;
        }

        private static string ReadSharedTableCollectionGuid()
        {
            foreach (string line in File.ReadAllLines(SharedDataPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("m_TableCollectionNameGuidString:", StringComparison.Ordinal))
                {
                    return trimmed.Substring("m_TableCollectionNameGuidString:".Length).Trim();
                }
            }

            return null;
        }

        /// <summary>Locale 테이블의 Entry Key ID -> 번역 값.</summary>
        private static Dictionary<string, string> ReadLocaleValues(string tablePath)
        {
            Assert.IsTrue(File.Exists(tablePath), $"'{tablePath}'가 없습니다.");

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            string pendingId = null;

            foreach (string line in File.ReadAllLines(tablePath))
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("- m_Id:", StringComparison.Ordinal))
                {
                    pendingId = trimmed.Substring("- m_Id:".Length).Trim();
                    continue;
                }

                if (pendingId == null || !trimmed.StartsWith("m_Localized:", StringComparison.Ordinal)) continue;

                map[pendingId] = trimmed.Substring("m_Localized:".Length).Trim();
                pendingId = null;
            }

            return map;
        }
    }
}
