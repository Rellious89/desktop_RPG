using System.Collections.Generic;
using System.Reflection;
using Common;
using NUnit.Framework;
using UnityEngine;

namespace CommonEditor.Tests
{
    /// <summary>
    /// 저장 문서에 새로 생긴 건설 기록(<see cref="SaveData.buildingConstructions"/>)의 저장 계층 시험.
    ///
    /// <b>파일도 엔진 수명주기도 건드리지 않는다.</b> 여기서 확인하는 것은 셋뿐이다 -
    /// (1) 이 칸이 없던 예전 파일이 그대로 열리는가, (2) 정규화가 <b>null만</b> 치우는가,
    /// (3) 변환의 작업 사본이 네 값을 전부 옮기고 원본과 끊는가.
    ///
    /// 건설 기록 자체는 형식 번호를 올리지 않았지만, 현재 형식은 파티 의미를 추가한 v3다.
    /// </summary>
    public sealed class BuildingConstructionSaveTests
    {
        /// <summary>실제 저장 경로가 쓰는 것과 같은 역직렬화기 - "없는 필드는 기본값"이라는 진짜
        /// 동작 위에서 확인한다.</summary>
        private static SaveData Deserialize(string json) => JsonUtility.FromJson<SaveData>(json);

        // ---- 예전 파일 ----

        [Test]
        public void 현재_저장_형식_번호는_3이다()
        {
            Assert.AreEqual(3, SaveData.CurrentSaveVersion);
            Assert.AreEqual(3, new SaveData().saveVersion);
        }

        [Test]
        public void 새_문서의_건설_목록은_비어_있고_null이_아니다()
        {
            var data = new SaveData();

            Assert.IsNotNull(data.buildingConstructions);
            Assert.AreEqual(0, data.buildingConstructions.Count);
        }

        [Test]
        public void v2_파일에_건설_칸이_없어도_빈_목록으로_읽힌다()
        {
            // 이 칸이 생기기 전에 저장된 v2 문서. 다른 값들은 그대로 살아 있어야 한다.
            const string json = "{\"saveVersion\":2,\"saveRevision\":7,\"currency\":1234}";

            SaveLoadResult result = SaveMigrationRunner.Default.Load(json, Deserialize);

            Assert.AreEqual(SaveLoadStatus.Migrated, result.Status);
            Assert.IsNotNull(result.Data.buildingConstructions,
                "없는 칸이 null로 남으면 호출부가 전부 null 검사를 해야 한다");
            Assert.AreEqual(0, result.Data.buildingConstructions.Count);
            Assert.AreEqual(1234, result.Data.currency, "새 칸을 더하면서 기존 값이 흔들리면 안 된다");
            Assert.AreEqual(7, result.Data.saveRevision);
            Assert.IsEmpty(result.Data.partyCharacterIds);
        }

        [Test]
        public void v0_v1_v2_어느_파일에서_올라와도_건설_목록은_비어_있다()
        {
            // 버전 필드가 없던 파일(v0), v1, v2 셋 다 건설 기록이 있을 수 없다 - 그 기능이 없었다.
            var jsons = new Dictionary<string, string>
            {
                { "v0", "{\"currentLevel\":3,\"currency\":10}" },
                { "v1", "{\"saveVersion\":1,\"currentLevel\":3,\"currency\":10}" },
                { "v2", "{\"saveVersion\":2,\"currentLevel\":3,\"currency\":10}" },
            };

            foreach (KeyValuePair<string, string> pair in jsons)
            {
                SaveLoadResult result = SaveMigrationRunner.Default.Load(pair.Value, Deserialize);

                Assert.IsTrue(result.Status == SaveLoadStatus.Loaded || result.Status == SaveLoadStatus.Migrated,
                    $"{pair.Key}: {result.Message}");
                Assert.IsNotNull(result.Data.buildingConstructions, pair.Key);
                Assert.AreEqual(0, result.Data.buildingConstructions.Count,
                    $"{pair.Key}: 변환이 있지도 않던 건설 기록을 만들어 내면 안 된다");
                Assert.AreEqual(SaveData.CurrentSaveVersion, result.Data.saveVersion, pair.Key);
            }
        }

        [Test]
        public void 새_게임도_빈_건설_목록으로_시작한다()
        {
            SaveLoadResult result = SaveMigrationRunner.Default.NewGame();

            Assert.IsNotNull(result.Data.buildingConstructions);
            Assert.AreEqual(0, result.Data.buildingConstructions.Count);
        }

        // ---- 정규화 ----

        [Test]
        public void 정규화는_null_목록을_빈_목록으로_바꾼다()
        {
            var data = new SaveData { buildingConstructions = null };

            SaveDataNormalizer.Normalize(data);

            Assert.IsNotNull(data.buildingConstructions);
            Assert.AreEqual(0, data.buildingConstructions.Count);
        }

        [Test]
        public void 정규화는_null_항목만_치우고_순서와_모르는_id를_그대로_둔다()
        {
            var data = new SaveData
            {
                buildingConstructions = new List<BuildingConstructionSaveState>
                {
                    New("2"),
                    null,
                    New("모르는_건물"),
                    New("1"),
                    null,
                    New("1"),
                },
            };

            SaveDataNormalizer.Normalize(data);

            Assert.AreEqual(4, data.buildingConstructions.Count, "null만 치운다");
            Assert.AreEqual("2", data.buildingConstructions[0].buildingId, "순서를 바꾸지 않는다");
            Assert.AreEqual("모르는_건물", data.buildingConstructions[1].buildingId,
                "표에서 잠시 빠진 건물의 기록을 지우면 그 건물이 돌아왔을 때 다시 지어야 한다");
            Assert.AreEqual("1", data.buildingConstructions[2].buildingId);
            Assert.AreEqual("1", data.buildingConstructions[3].buildingId,
                "같은 id가 두 줄이어도 저장 계층이 합치거나 지우지 않는다");
        }

        [Test]
        public void 정규화는_여러_번_지나도_결과가_같다()
        {
            var data = new SaveData
            {
                buildingConstructions = new List<BuildingConstructionSaveState> { New("1"), null },
            };

            SaveDataNormalizer.Normalize(data);
            SaveDataNormalizer.Normalize(data);
            SaveDataNormalizer.Normalize(data);

            Assert.AreEqual(1, data.buildingConstructions.Count);
            Assert.AreEqual("1", data.buildingConstructions[0].buildingId);
        }

        [Test]
        public void 정규화는_시각_문자열을_손대지_않는다()
        {
            var data = new SaveData
            {
                buildingConstructions = new List<BuildingConstructionSaveState>
                {
                    new BuildingConstructionSaveState
                    {
                        buildingId = " 1 ",
                        startedAtUtc = "읽을 수 없는 값",
                        completeAtUtc = null,
                    },
                },
            };

            SaveDataNormalizer.Normalize(data);

            Assert.AreEqual(" 1 ", data.buildingConstructions[0].buildingId, "id를 다듬지 않는다");
            Assert.AreEqual("읽을 수 없는 값", data.buildingConstructions[0].startedAtUtc);
            Assert.IsNull(data.buildingConstructions[0].completeAtUtc);
        }

        // ---- 깊은 사본 ----

        [Test]
        public void 건설_항목에_필드를_추가하면_깊은_사본도_함께_고쳐야_한다()
        {
            // CopyBuildingConstructions도 손으로 쓴 코드다. 칸을 늘리고 사본을 빠뜨리면 그 값이
            // 변환을 지날 때마다 조용히 사라진다.
            string[] expected = { "buildingId", "startedAtUtc", "completeAtUtc", "completionNotified" };

            var actual = new List<string>();
            foreach (FieldInfo field in typeof(BuildingConstructionSaveState).GetFields(
                BindingFlags.Public | BindingFlags.Instance))
            {
                actual.Add(field.Name);
            }

            CollectionAssert.AreEquivalent(expected, actual,
                "BuildingConstructionSaveState의 필드가 바뀌었습니다 - " +
                "SaveMigrationRunner의 CopyBuildingConstructions와 이 목록을 함께 고치세요.");
        }

        [Test]
        public void 깊은_사본은_네_값을_모두_옮기고_원본과_끊어_둔다()
        {
            var data = new SaveData
            {
                saveVersion = SaveData.CurrentSaveVersion,
                buildingConstructions = new List<BuildingConstructionSaveState>
                {
                    new BuildingConstructionSaveState
                    {
                        buildingId = "1",
                        startedAtUtc = "2026-08-22T10:00:00.0000000Z",
                        completeAtUtc = "2026-08-22T10:01:00.0000000Z",
                        completionNotified = true,
                    },
                },
            };

            BuildingConstructionSaveState original = data.buildingConstructions[0];

            SaveMigrationResult result = SaveMigrationRunner.Default.Migrate(data, SaveData.CurrentSaveVersion);

            Assert.AreEqual(SaveMigrationOutcome.AlreadyCurrent, result.Outcome);
            Assert.AreEqual(1, data.buildingConstructions.Count);
            Assert.AreEqual("1", data.buildingConstructions[0].buildingId);
            Assert.AreEqual("2026-08-22T10:00:00.0000000Z", data.buildingConstructions[0].startedAtUtc);
            Assert.AreEqual("2026-08-22T10:01:00.0000000Z", data.buildingConstructions[0].completeAtUtc);
            Assert.IsTrue(data.buildingConstructions[0].completionNotified,
                "완성 안내 표식을 사본이 빠뜨리면 변환을 지날 때마다 같은 안내가 다시 뜬다");

            Assert.AreNotSame(original, data.buildingConstructions[0],
                "목록 안의 항목까지 새로 만들어야 얕은 사본이 아니다");
        }

        [Test]
        public void 깊은_사본은_완성_표식을_true도_false도_그대로_옮긴다()
        {
            // 이 한 줄(CopyBuildingConstructions의 completionNotified)이 빠지면 변환을 지날 때마다
            // 표식이 false로 되돌아가고, 이미 본 완성 안내가 다시 뜬다.
            var data = new SaveData
            {
                saveVersion = SaveData.CurrentSaveVersion,
                buildingConstructions = new List<BuildingConstructionSaveState>
                {
                    new BuildingConstructionSaveState
                    {
                        buildingId = "이미_안내함",
                        startedAtUtc = "2026-08-22T10:00:00.0000000Z",
                        completeAtUtc = "2026-08-22T10:01:00.0000000Z",
                        completionNotified = true,
                    },
                    new BuildingConstructionSaveState
                    {
                        buildingId = "아직_안내_안_함",
                        startedAtUtc = "2026-08-22T10:00:00.0000000Z",
                        completeAtUtc = "2026-08-22T10:01:00.0000000Z",
                        completionNotified = false,
                    },
                },
            };

            SaveMigrationResult result = SaveMigrationRunner.Default.Migrate(data, SaveData.CurrentSaveVersion);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(2, data.buildingConstructions.Count);
            Assert.IsTrue(data.buildingConstructions[0].completionNotified,
                "이미 안내한 건물의 표식이 변환에서 사라지면 안내가 되풀이된다");
            Assert.IsFalse(data.buildingConstructions[1].completionNotified,
                "아직 안내하지 않은 건물에 표식이 생기면 안내를 영영 못 본다");
        }

        [Test]
        public void 완성_표식이_없던_저장_파일은_아직_안내하지_않은_것으로_읽힌다()
        {
            // 이 칸이 없던 v2 파일 - JsonUtility가 false로 채우고, 그 값이
            // 곧 "아직 안내하지 않았다"라서 그대로 옳다.
            string json =
                "{\"saveVersion\":2,\"buildingConstructions\":[{\"buildingId\":\"1\"," +
                "\"startedAtUtc\":\"2026-08-22T10:00:00.0000000Z\"," +
                "\"completeAtUtc\":\"2026-08-22T10:01:00.0000000Z\"}]}";

            SaveData data = JsonUtility.FromJson<SaveData>(json);

            Assert.AreEqual(1, data.buildingConstructions.Count);
            Assert.IsFalse(data.buildingConstructions[0].completionNotified);
            Assert.AreEqual(2, data.saveVersion,
                "역직렬화만 한 v2 원문의 버전 값은 마이그레이션 전까지 그대로다");
        }

        [Test]
        public void 변환이_실패해도_건설_기록은_시도_전_그대로다()
        {
            // 미래 버전은 읽지도 고치지도 않는다 - 이 목록도 예외가 아니다.
            var data = new SaveData
            {
                buildingConstructions = new List<BuildingConstructionSaveState> { New("1") },
            };

            SaveMigrationResult result = SaveMigrationRunner.Default.Migrate(
                data, SaveData.CurrentSaveVersion + 5);

            Assert.AreEqual(SaveMigrationOutcome.FutureVersion, result.Outcome);
            Assert.AreEqual(1, data.buildingConstructions.Count);
            Assert.AreEqual("1", data.buildingConstructions[0].buildingId);
        }

        private static BuildingConstructionSaveState New(string buildingId)
        {
            return new BuildingConstructionSaveState
            {
                buildingId = buildingId,
                startedAtUtc = "2026-08-22T10:00:00.0000000Z",
                completeAtUtc = "2026-08-22T10:01:00.0000000Z",
            };
        }
    }
}
