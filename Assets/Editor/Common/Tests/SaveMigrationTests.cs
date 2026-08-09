using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Common;
using NUnit.Framework;
using UnityEngine;

namespace CommonEditor.Tests
{
    /// <summary>
    /// 저장 버전/마이그레이션 도메인 시험. <b>파일을 읽지도 쓰지도 않는다</b> - 저장소(SaveSystem)는
    /// 원문 문자열을 건네는 일만 하고, 버전을 알아내고 올리고 모양을 맞추는 규칙은 전부 메모리 안에서
    /// 끝나기 때문이다. 그래서 여기 있는 시험은 persistentDataPath도, 씬도, 플레이 모드도 필요 없다.
    ///
    /// 시험이 확인하는 계약은 네 가지다.
    /// 1. <see cref="SaveVersionProbe"/>는 <b>역직렬화 전에</b> 원문만 보고 버전을 맞힌다.
    /// 2. <see cref="SaveMigrationRunner"/>는 한 번에 딱 한 칸만 올린다.
    /// 3. v0 -> v1은 진행 값을 하나도 바꾸지 않는다.
    /// 4. 실패/미래 버전은 데이터를 주지 않고 저장까지 막는다(원본 파일 보호).
    /// </summary>
    public sealed class SaveMigrationTests
    {
        // ---- 도우미 ----

        /// <summary>실제 저장 경로가 쓰는 것과 같은 역직렬화기. 시험이 진짜 JsonUtility 동작(없는 필드는
        /// 기본값으로 채운다)을 그대로 겪게 하려고 가짜로 바꾸지 않는다.</summary>
        private static readonly Func<string, SaveData> JsonDeserializer = JsonUtility.FromJson<SaveData>;

        /// <summary>버전 필드가 없던 시절의 저장 파일. 회복 슬롯까지 들어 있는 "꽉 찬" 예전 파일이다.</summary>
        private const string LegacyJson = @"{
            ""currentLevel"": 7,
            ""currentExp"": 240,
            ""totalKillCount"": 133,
            ""characters"": [
                {""characterId"": ""barbarian"", ""level"": 4, ""currentStamina"": 9},
                {""characterId"": ""scarecrow"", ""level"": 2, ""currentStamina"": 0}
            ],
            ""currency"": 1250,
            ""items"": [
                {""itemId"": ""potion"", ""count"": 3},
                {""itemId"": ""ore"", ""count"": 11}
            ],
            ""recoverySlots"": [
                {""characterId"": ""barbarian"", ""startStamina"": 2,
                 ""startedAtUtc"": ""2026-01-02T03:04:05.0000000Z"",
                 ""completeAtUtc"": ""2026-01-02T05:04:05.0000000Z"",
                 ""completionNotified"": true},
                {""characterId"": """", ""startStamina"": 0, ""startedAtUtc"": """",
                 ""completeAtUtc"": """", ""completionNotified"": false},
                {""characterId"": """", ""startStamina"": 0, ""startedAtUtc"": """",
                 ""completeAtUtc"": """", ""completionNotified"": false}
            ]
        }";

        private static SaveMigrationRunner RunnerWith(params ISaveMigrationStep[] steps)
        {
            return new SaveMigrationRunner(steps, SaveData.CurrentSaveVersion);
        }

        /// <summary>임의의 목표 버전까지 올리는 러너. "여러 칸"과 "빠진 칸"을 시험하려면 현재 버전(1)만으로는
        /// 모자라므로, 실제 표 대신 시험용 표를 끼워 넣는다.</summary>
        private static SaveMigrationRunner RunnerTo(int targetVersion, params ISaveMigrationStep[] steps)
        {
            return new SaveMigrationRunner(steps, targetVersion);
        }

        /// <summary>거쳐 간 칸을 기록하는 시험용 단계.</summary>
        private sealed class RecordingStep : ISaveMigrationStep
        {
            private readonly List<string> log;

            public RecordingStep(int fromVersion, List<string> log)
            {
                FromVersion = fromVersion;
                this.log = log;
            }

            public int FromVersion { get; }

            public int ToVersion => FromVersion + 1;

            public void Apply(SaveData data)
            {
                // 러너가 이 단계를 부르기 직전에 문서의 버전을 이 단계의 시작 버전으로 맞춰 뒀는지까지
                // 함께 확인한다 - 단계 안에서 "지금 몇 번 문서인가"를 볼 수 있어야 한다.
                Assert.AreEqual(FromVersion, data.saveVersion,
                    "단계에 들어올 때 문서의 saveVersion이 그 단계의 시작 버전이어야 합니다.");
                log.Add($"{FromVersion}->{ToVersion}");
            }
        }

        private sealed class ThrowingStep : ISaveMigrationStep
        {
            public ThrowingStep(int fromVersion) => FromVersion = fromVersion;

            public int FromVersion { get; }

            public int ToVersion => FromVersion + 1;

            public void Apply(SaveData data) => throw new InvalidOperationException("일부러 실패");
        }

        /// <summary>한 번에 두 칸을 건너뛰려는(금지된) 단계.</summary>
        private sealed class SkippingStep : ISaveMigrationStep
        {
            public int FromVersion => 0;

            public int ToVersion => 2;

            public void Apply(SaveData data)
            {
            }
        }

        // ---- 버전 훑기: 무엇이 v0인가 ----

        [Test]
        public void Probe_내용이_없으면_Empty다()
        {
            foreach (string json in new[] { null, "", "   ", "\r\n\t" })
            {
                SaveVersionProbeResult result = SaveVersionProbe.Probe(json);
                Assert.AreEqual(SaveVersionProbeStatus.Empty, result.Status, $"입력: '{json}'");
                Assert.IsFalse(result.IsReadable);
            }
        }

        [Test]
        public void Probe_빈_객체도_v0다()
        {
            // 필드가 하나도 없는 파일도 "버전 필드가 없는 파일"이므로 손상이 아니라 v0이다.
            foreach (string json in new[] { "{}", "  {  }  ", "{\n}\n" })
            {
                SaveVersionProbeResult result = SaveVersionProbe.Probe(json);
                Assert.AreEqual(SaveVersionProbeStatus.Unversioned, result.Status, $"입력: '{json}'");
                Assert.AreEqual(SaveData.UnversionedSaveVersion, result.Version);
                Assert.IsTrue(result.IsReadable);
            }
        }

        [Test]
        public void Probe_버전_항목이_없는_예전_파일은_v0다()
        {
            SaveVersionProbeResult result = SaveVersionProbe.Probe(LegacyJson);

            Assert.AreEqual(SaveVersionProbeStatus.Unversioned, result.Status);
            Assert.AreEqual(0, result.Version);
        }

        [Test]
        public void Probe_버전_항목이_있으면_그_값을_읽는다()
        {
            Assert.AreEqual(1, SaveVersionProbe.Probe(@"{""saveVersion"":1}").Version);
            Assert.AreEqual(3, SaveVersionProbe.Probe(@"{""currentLevel"":5,""saveVersion"":3}").Version);
            Assert.AreEqual(42, SaveVersionProbe.Probe("{ \"saveVersion\" : 42 , \"currentExp\": 1 }").Version);

            Assert.AreEqual(SaveVersionProbeStatus.Versioned,
                SaveVersionProbe.Probe(@"{""saveVersion"":1}").Status);
        }

        [Test]
        public void Probe_중첩된_같은_이름은_세지_않는다()
        {
            // 최상위에만 관심이 있다. 아이템 안에 우연히 같은 이름이 있어도 그건 파일의 버전이 아니다.
            string json = @"{""items"":[{""itemId"":""x"",""saveVersion"":9}],""currentLevel"":2}";

            SaveVersionProbeResult result = SaveVersionProbe.Probe(json);

            Assert.AreEqual(SaveVersionProbeStatus.Unversioned, result.Status);
            Assert.AreEqual(0, result.Version);
        }

        [Test]
        public void Probe_문자열_안의_특수문자에_속지_않는다()
        {
            // 값 문자열 안에 중괄호나 이스케이프된 따옴표가 있어도 구조 해석이 어긋나면 안 된다.
            string json = @"{""itemId"":""a\""}b{"",""saveVersion"":2}";

            SaveVersionProbeResult result = SaveVersionProbe.Probe(json);

            Assert.AreEqual(SaveVersionProbeStatus.Versioned, result.Status);
            Assert.AreEqual(2, result.Version);
        }

        [Test]
        public void Probe_JSON이_아니면_Malformed다()
        {
            string[] broken =
            {
                "그냥 글자",
                "[1,2,3]",
                "{",
                @"{""currentLevel"":1",
                @"{""currentLevel""}",
                @"{""unterminated"": ""abc}",
                @"{""saveVersion"":1} 뒤에 쓰레기",
            };

            foreach (string json in broken)
            {
                SaveVersionProbeResult result = SaveVersionProbe.Probe(json);
                Assert.AreEqual(SaveVersionProbeStatus.Malformed, result.Status, $"입력: '{json}'");
                Assert.AreEqual(SaveData.UnknownSaveVersion, result.Version);
                Assert.IsFalse(result.IsReadable);
            }
        }

        [Test]
        public void Probe_버전값이_정수가_아니면_Malformed다()
        {
            string[] broken =
            {
                @"{""saveVersion"":""1""}",
                @"{""saveVersion"":1.5}",
                @"{""saveVersion"":null}",
                @"{""saveVersion"":true}",
                @"{""saveVersion"":-1}",
                @"{""saveVersion"":}",
            };

            foreach (string json in broken)
            {
                Assert.AreEqual(SaveVersionProbeStatus.Malformed, SaveVersionProbe.Probe(json).Status,
                    $"입력: '{json}'");
            }
        }

        [Test]
        public void Probe_터무니없이_큰_버전은_손상이_아니라_미래_버전이다()
        {
            // 손상으로 다루면 호출부가 새 게임을 시작해 덮어쓸 수 있다. 모르는 파일은 막는 쪽이 안전하다.
            SaveVersionProbeResult result = SaveVersionProbe.Probe(@"{""saveVersion"":99999999999999}");

            Assert.AreEqual(SaveVersionProbeStatus.Versioned, result.Status);
            Assert.AreEqual(int.MaxValue, result.Version);
        }

        [Test]
        public void Probe_우리가_쓴_파일을_그대로_알아본다()
        {
            // 훑기와 실제 저장 서식이 어긋나면 모든 저장 파일이 한꺼번에 "손상"이 된다. 예쁜 출력
            // (들여쓰기/줄바꿈)까지 포함해 왕복을 확인한다.
            SaveData data = new SaveData();
            SaveData.MarkSaved(data, DateTime.UtcNow);

            foreach (bool pretty in new[] { false, true })
            {
                SaveVersionProbeResult result = SaveVersionProbe.Probe(JsonUtility.ToJson(data, pretty));

                Assert.AreEqual(SaveVersionProbeStatus.Versioned, result.Status, $"pretty={pretty}");
                Assert.AreEqual(SaveData.CurrentSaveVersion, result.Version, $"pretty={pretty}");
            }
        }

        // ---- 러너: 한 번에 한 칸 ----

        [Test]
        public void 러너는_두_칸을_건너뛰는_단계를_거부한다()
        {
            ArgumentException e = Assert.Throws<ArgumentException>(() => RunnerTo(2, new SkippingStep()));
            StringAssert.Contains("한 칸", e.Message);
        }

        [Test]
        public void 러너는_시작_버전이_겹치는_단계를_거부한다()
        {
            List<string> log = new List<string>();

            Assert.Throws<ArgumentException>(
                () => RunnerTo(2, new RecordingStep(0, log), new RecordingStep(0, log)));
        }

        [Test]
        public void 러너는_등록_순서와_무관하게_한_칸씩_차례로_올린다()
        {
            List<string> log = new List<string>();
            SaveMigrationRunner runner = RunnerTo(3,
                new RecordingStep(2, log), new RecordingStep(0, log), new RecordingStep(1, log));

            SaveData data = new SaveData();
            SaveMigrationResult result = runner.Migrate(data, 0);

            Assert.AreEqual(SaveMigrationOutcome.Migrated, result.Outcome);
            Assert.AreEqual(3, result.ReachedVersion);
            Assert.AreEqual(3, data.saveVersion);
            CollectionAssert.AreEqual(new[] { "0->1", "1->2", "2->3" }, log,
                "단계는 등록 순서가 아니라 버전 순서대로, 한 칸씩 불려야 합니다.");
        }

        [Test]
        public void 러너는_이미_현재_버전이면_아무것도_하지_않는다()
        {
            List<string> log = new List<string>();
            SaveMigrationRunner runner = RunnerWith(new RecordingStep(0, log));
            SaveData data = new SaveData();

            SaveMigrationResult result = runner.Migrate(data, SaveData.CurrentSaveVersion);

            Assert.AreEqual(SaveMigrationOutcome.AlreadyCurrent, result.Outcome);
            Assert.IsTrue(result.Succeeded);
            CollectionAssert.IsEmpty(log);
        }

        [Test]
        public void 러너는_믿을_수_없는_saveVersion_필드를_넘겨받은_버전으로_덮어쓴다()
        {
            // 이것이 훑기를 따로 두는 이유 그 자체다. v0 파일을 역직렬화하면 saveVersion에는 필드
            // 기본값(현재 버전)이 들어 있어 "이미 최신"으로 보이지만, 실제로는 올려야 한다.
            List<string> log = new List<string>();
            SaveMigrationRunner runner = RunnerWith(new RecordingStep(0, log));
            SaveData data = new SaveData { saveVersion = SaveData.CurrentSaveVersion };

            SaveMigrationResult result = runner.Migrate(data, SaveData.UnversionedSaveVersion);

            Assert.AreEqual(SaveMigrationOutcome.Migrated, result.Outcome);
            CollectionAssert.AreEqual(new[] { "0->1" }, log);
        }

        [Test]
        public void 러너는_중간_단계가_없으면_거기서_멈춘다()
        {
            List<string> log = new List<string>();
            SaveMigrationRunner runner = RunnerTo(3, new RecordingStep(0, log), new RecordingStep(2, log));

            SaveMigrationResult result = runner.Migrate(new SaveData(), 0);

            Assert.AreEqual(SaveMigrationOutcome.StepMissing, result.Outcome);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(1, result.ReachedVersion, "v1에서 v2로 갈 단계가 없으므로 v1에서 멈춰야 합니다.");
            CollectionAssert.AreEqual(new[] { "0->1" }, log);
        }

        [Test]
        public void 러너는_단계가_던진_예외를_실패로_바꾼다()
        {
            SaveMigrationRunner runner = RunnerTo(2, new ThrowingStep(0), new RecordingStep(1, new List<string>()));

            SaveMigrationResult result = runner.Migrate(new SaveData(), 0);

            Assert.AreEqual(SaveMigrationOutcome.StepFailed, result.Outcome);
            Assert.AreEqual(0, result.ReachedVersion);
            StringAssert.Contains("일부러 실패", result.FailureReason);
        }

        [Test]
        public void 러너는_미래_버전을_올리려_들지_않는다()
        {
            SaveMigrationResult result = SaveMigrationRunner.Default.Migrate(
                new SaveData(), SaveData.CurrentSaveVersion + 5);

            Assert.AreEqual(SaveMigrationOutcome.FutureVersion, result.Outcome);
            Assert.IsFalse(result.Succeeded);
        }

        [Test]
        public void 러너는_잘못된_입력에_조용히_넘어가지_않는다()
        {
            Assert.Throws<ArgumentNullException>(() => SaveMigrationRunner.Default.Migrate(null, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SaveMigrationRunner.Default.Migrate(new SaveData(), -1));
        }

        // ---- v0 -> v1: 값을 하나도 잃지 않는다 ----

        [Test]
        public void v0_파일을_올려도_진행_값이_그대로다()
        {
            SaveLoadResult result = SaveMigrationRunner.Default.Load(LegacyJson, JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.Migrated, result.Status);
            Assert.AreEqual(0, result.FromVersion);
            Assert.AreEqual(SaveData.CurrentSaveVersion, result.ToVersion);

            SaveData data = result.Data;
            Assert.IsNotNull(data);
            Assert.AreEqual(7, data.currentLevel);
            Assert.AreEqual(240, data.currentExp);
            Assert.AreEqual(133, data.totalKillCount);
            Assert.AreEqual(1250, data.currency);

            Assert.AreEqual(2, data.characters.Count);
            Assert.AreEqual("barbarian", data.characters[0].characterId);
            Assert.AreEqual(4, data.characters[0].level);
            Assert.AreEqual(9, data.characters[0].currentStamina);
            Assert.AreEqual("scarecrow", data.characters[1].characterId);
            Assert.AreEqual(0, data.characters[1].currentStamina, "행동력 0과 '초기화 안 됨'(-1)은 다른 값입니다.");

            // 아이템은 목록 순서가 곧 획득 순서다 - 변환이 순서를 흔들면 인벤토리 표시가 뒤바뀐다.
            Assert.AreEqual(2, data.items.Count);
            Assert.AreEqual("potion", data.items[0].itemId);
            Assert.AreEqual(3, data.items[0].count);
            Assert.AreEqual("ore", data.items[1].itemId);
            Assert.AreEqual(11, data.items[1].count);

            // 회복 슬롯은 인덱스가 곧 슬롯 번호다 - 0번에 있던 진행이 0번에 그대로 있어야 한다.
            Assert.AreEqual(3, data.recoverySlots.Count);
            Assert.AreEqual("barbarian", data.recoverySlots[0].characterId);
            Assert.AreEqual(2, data.recoverySlots[0].startStamina);
            Assert.AreEqual("2026-01-02T03:04:05.0000000Z", data.recoverySlots[0].startedAtUtc);
            Assert.AreEqual("2026-01-02T05:04:05.0000000Z", data.recoverySlots[0].completeAtUtc);
            Assert.IsTrue(data.recoverySlots[0].completionNotified);
            Assert.IsFalse(data.recoverySlots[1].HasCharacter);
        }

        [Test]
        public void v0_파일의_메타데이터는_모름으로_채워진다()
        {
            SaveLoadResult result = SaveMigrationRunner.Default.Load(LegacyJson, JsonDeserializer);
            SaveData data = result.Data;

            Assert.AreEqual(SaveData.CurrentSaveVersion, data.saveVersion);
            Assert.AreEqual(0, data.saveRevision, "예전 파일에는 저장 횟수 정보가 없으므로 0(모름)이어야 합니다.");
            Assert.IsTrue(string.IsNullOrEmpty(data.lastSavedAtUtc),
                "예전 파일에는 저장 시각 정보가 없으므로 비어 있어야 합니다.");
        }

        [Test]
        public void 빈_객체도_v0로_올라가고_기본값을_얻는다()
        {
            SaveLoadResult result = SaveMigrationRunner.Default.Load("{}", JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.Migrated, result.Status);
            Assert.AreEqual(0, result.FromVersion);
            Assert.AreEqual(1, result.Data.currentLevel);
            Assert.AreEqual(0, result.Data.currency);
            Assert.AreEqual(SaveData.DefaultRecoverySlotCount, result.Data.recoverySlots.Count);
            Assert.IsNotNull(result.Data.characters);
            Assert.IsNotNull(result.Data.items);
        }

        [Test]
        public void 올린_결과는_다시_저장해_두라고_알린다()
        {
            Assert.IsTrue(SaveMigrationRunner.Default.Load(LegacyJson, JsonDeserializer).ShouldResaveSoon);
            Assert.IsFalse(SaveMigrationRunner.Default.NewGame().ShouldResaveSoon);
        }

        [Test]
        public void 현재_버전_파일은_변환_없이_그대로_읽는다()
        {
            SaveData source = new SaveData { currentLevel = 12, currency = 77 };
            SaveData.MarkSaved(source, DateTime.UtcNow);
            string json = JsonUtility.ToJson(source);

            SaveLoadResult result = SaveMigrationRunner.Default.Load(json, JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.Loaded, result.Status);
            Assert.AreEqual(SaveData.CurrentSaveVersion, result.FromVersion);
            Assert.AreEqual(12, result.Data.currentLevel);
            Assert.AreEqual(77, result.Data.currency);
            Assert.AreEqual(1, result.Data.saveRevision, "그대로 읽기만 했으므로 일련번호가 바뀌면 안 됩니다.");
        }

        // ---- 상태 모델: 무엇을 저장해도 되는가 ----

        [Test]
        public void 파일이_없으면_새_게임이다()
        {
            SaveLoadResult result = SaveMigrationRunner.Default.Load(null, JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.NewGame, result.Status);
            Assert.IsTrue(result.HasData);
            Assert.IsFalse(result.ShouldBlockSaving);
            Assert.AreEqual(SaveData.CurrentSaveVersion, result.Data.saveVersion);
            Assert.AreEqual(SaveData.DefaultRecoverySlotCount, result.Data.recoverySlots.Count);
        }

        [Test]
        public void 손상된_파일은_기본값으로_진행하되_막지는_않는다()
        {
            SaveLoadResult result = SaveMigrationRunner.Default.Load("{망가짐", JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.CorruptFallback, result.Status);
            Assert.IsTrue(result.HasData, "진행 자체를 막지는 않는다 - 저장 하나 때문에 게임이 안 켜지면 안 된다.");
            Assert.IsFalse(result.ShouldBlockSaving);
            Assert.AreEqual(1, result.Data.currentLevel);
        }

        [Test]
        public void 역직렬화가_실패하거나_비면_손상으로_다룬다()
        {
            SaveLoadResult thrown = SaveMigrationRunner.Default.Load(
                "{}", _ => throw new InvalidOperationException("파싱 실패"));
            SaveLoadResult empty = SaveMigrationRunner.Default.Load("{}", _ => null);

            Assert.AreEqual(SaveLoadStatus.CorruptFallback, thrown.Status);
            Assert.IsTrue(thrown.HasData);
            Assert.AreEqual(SaveLoadStatus.CorruptFallback, empty.Status);
            Assert.IsTrue(empty.HasData);
        }

        [Test]
        public void 미래_버전_파일은_읽지도_저장하지도_않는다()
        {
            string json = $@"{{""saveVersion"":{SaveData.CurrentSaveVersion + 1},""currentLevel"":99}}";

            SaveLoadResult result = SaveMigrationRunner.Default.Load(json, _ =>
            {
                Assert.Fail("미래 버전 파일은 역직렬화조차 하면 안 됩니다 - 모르는 필드가 그 순간 버려집니다.");
                return null;
            });

            Assert.AreEqual(SaveLoadStatus.FutureVersionBlocked, result.Status);
            Assert.IsFalse(result.HasData, "데이터를 주면 호출부가 그걸로 진행하다 원본을 덮어씁니다.");
            Assert.IsTrue(result.ShouldBlockSaving);
            Assert.AreEqual(SaveData.CurrentSaveVersion + 1, result.FromVersion);
        }

        [Test]
        public void 변환이_실패하면_반쯤_바뀐_문서를_내주지_않는다()
        {
            SaveMigrationRunner runner = RunnerWith(new ThrowingStep(0));

            SaveLoadResult result = runner.Load(LegacyJson, JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.MigrationFailed, result.Status);
            Assert.IsFalse(result.HasData);
            Assert.IsTrue(result.ShouldBlockSaving, "저장을 막아야 원본 파일이 살아남습니다.");
            Assert.AreEqual(0, result.FromVersion);
        }

        [Test]
        public void 등록되지_않은_단계는_변환_실패다()
        {
            SaveMigrationRunner runner = new SaveMigrationRunner(new ISaveMigrationStep[0], SaveData.CurrentSaveVersion);

            SaveLoadResult result = runner.Load(LegacyJson, JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.MigrationFailed, result.Status);
            Assert.IsFalse(result.HasData);
            Assert.IsTrue(result.ShouldBlockSaving);
        }

        // ---- 정규화: 모양 맞추기 ----

        [Test]
        public void 정규화는_null을_받아도_문서를_돌려준다()
        {
            SaveData data = SaveDataNormalizer.Normalize(null);

            Assert.IsNotNull(data);
            Assert.IsNotNull(data.characters);
            Assert.IsNotNull(data.items);
            Assert.AreEqual(SaveData.DefaultRecoverySlotCount, data.recoverySlots.Count);
        }

        [Test]
        public void 정규화는_없는_목록을_빈_목록으로_만든다()
        {
            SaveData data = new SaveData { characters = null, items = null, recoverySlots = null };

            SaveDataNormalizer.Normalize(data);

            Assert.IsNotNull(data.characters);
            Assert.IsEmpty(data.characters);
            Assert.IsNotNull(data.items);
            Assert.IsEmpty(data.items);
            Assert.AreEqual(SaveData.DefaultRecoverySlotCount, data.recoverySlots.Count);
        }

        [Test]
        public void 정규화는_목록_안의_null을_치우되_순서를_지킨다()
        {
            SaveData data = new SaveData
            {
                items = new List<InventoryItemState>
                {
                    new InventoryItemState { itemId = "first", count = 1 },
                    null,
                    new InventoryItemState { itemId = "second", count = 2 },
                    null,
                },
                characters = new List<CharacterSaveState>
                {
                    null,
                    new CharacterSaveState { characterId = "barbarian" },
                },
            };

            SaveDataNormalizer.Normalize(data);

            Assert.AreEqual(2, data.items.Count);
            Assert.AreEqual("first", data.items[0].itemId, "획득 순서(= 표시 순서)가 유지돼야 합니다.");
            Assert.AreEqual("second", data.items[1].itemId);
            Assert.AreEqual(1, data.characters.Count);
            Assert.AreEqual("barbarian", data.characters[0].characterId);
        }

        [Test]
        public void 정규화는_회복_슬롯의_null은_지우지_않고_빈_슬롯으로_바꾼다()
        {
            // 여기서만 규칙이 다르다. 인덱스가 곧 슬롯 번호라서 지우면 뒤 슬롯이 앞으로 밀려
            // 다른 슬롯의 진행이 남의 자리로 옮겨간다.
            SaveData data = new SaveData
            {
                recoverySlots = new List<RecoverySlotSaveState>
                {
                    null,
                    new RecoverySlotSaveState { characterId = "barbarian", startStamina = 5 },
                    null,
                },
            };

            SaveDataNormalizer.Normalize(data);

            Assert.AreEqual(3, data.recoverySlots.Count);
            Assert.IsNotNull(data.recoverySlots[0]);
            Assert.IsFalse(data.recoverySlots[0].HasCharacter);
            Assert.AreEqual("barbarian", data.recoverySlots[1].characterId,
                "1번 슬롯의 진행은 1번 슬롯에 그대로 있어야 합니다.");
            Assert.AreEqual(5, data.recoverySlots[1].startStamina);
            Assert.IsNotNull(data.recoverySlots[2]);
        }

        [Test]
        public void 정규화는_최소_슬롯_수를_채우되_더_많은_슬롯을_자르지_않는다()
        {
            SaveData few = new SaveData { recoverySlots = new List<RecoverySlotSaveState>() };
            SaveDataNormalizer.Normalize(few);
            Assert.AreEqual(SaveData.DefaultRecoverySlotCount, few.recoverySlots.Count);

            SaveData many = new SaveData { recoverySlots = new List<RecoverySlotSaveState>() };
            for (int i = 0; i < 6; i++) many.recoverySlots.Add(new RecoverySlotSaveState());
            SaveDataNormalizer.Normalize(many);
            Assert.AreEqual(6, many.recoverySlots.Count,
                "슬롯 수를 나중에 줄여도 회복 중이던 저장 값을 지우면 안 됩니다.");
        }

        [Test]
        public void 정규화는_여러_번_해도_결과가_같다()
        {
            SaveData data = new SaveData { characters = null, items = null, recoverySlots = null };

            SaveDataNormalizer.Normalize(data);
            int slots = data.recoverySlots.Count;
            SaveDataNormalizer.Normalize(data);
            SaveDataNormalizer.Normalize(data);

            Assert.AreEqual(slots, data.recoverySlots.Count);
            Assert.IsEmpty(data.items);
            Assert.IsEmpty(data.characters);
        }

        [Test]
        public void 정규화는_있을_수_없는_일련번호를_모름으로_되돌린다()
        {
            SaveData data = new SaveData { saveRevision = -5 };

            SaveDataNormalizer.Normalize(data);

            Assert.AreEqual(0, data.saveRevision);
        }

        // ---- 저장 메타데이터 계약 ----

        [Test]
        public void MarkSaved는_버전과_일련번호와_시각을_찍는다()
        {
            SaveData data = new SaveData { saveVersion = 0, saveRevision = 41 };
            DateTime now = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

            SaveData.MarkSaved(data, now);

            Assert.AreEqual(SaveData.CurrentSaveVersion, data.saveVersion);
            Assert.AreEqual(42, data.saveRevision);
            Assert.AreEqual("2026-03-04T05:06:07.0000000Z", data.lastSavedAtUtc);
        }

        [Test]
        public void 저장_시각은_ISO_8601_UTC_왕복_문자열이다()
        {
            SaveData data = new SaveData();
            DateTime now = new DateTime(2026, 12, 31, 23, 59, 58, DateTimeKind.Utc).AddTicks(1234567);

            SaveData.MarkSaved(data, now);

            DateTime parsed = DateTime.ParseExact(
                data.lastSavedAtUtc, SaveData.TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);

            Assert.AreEqual(DateTimeKind.Utc, parsed.Kind);
            Assert.AreEqual(now, parsed, "왕복 서식이므로 틱 단위까지 같은 값으로 다시 읽혀야 합니다.");
        }

        [Test]
        public void 저장_시각은_지역_시각을_받아도_UTC로_적는다()
        {
            SaveData data = new SaveData();
            DateTime local = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Local);

            SaveData.MarkSaved(data, local);

            StringAssert.EndsWith("Z", data.lastSavedAtUtc);
            Assert.AreEqual(local.ToUniversalTime(),
                DateTime.ParseExact(data.lastSavedAtUtc, SaveData.TimestampFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }

        [Test]
        public void 저장_시각은_사용자_문화권을_타지_않는다()
        {
            // 지역/언어 설정이 다른 기기에서 만든 파일도 같은 문자열이어야 다시 읽힌다.
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");

                SaveData data = new SaveData();
                SaveData.MarkSaved(data, new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc));

                Assert.AreEqual("2026-03-04T05:06:07.0000000Z", data.lastSavedAtUtc);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Test]
        public void 저장이_실패하면_메타데이터를_되돌릴_수_있다()
        {
            // 되돌리지 않으면 메모리 쪽 일련번호가 디스크보다 앞서고, 그 뒤로는 어느 쪽이 최신인지
            // 일련번호로 가릴 수 없게 된다.
            SaveData data = new SaveData { currentLevel = 9 };
            SaveData.MarkSaved(data, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SaveMetadataSnapshot before = SaveData.MarkSaved(data, new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));

            Assert.AreEqual(2, data.saveRevision);

            SaveData.RestoreMetadata(data, before); // 쓰기 실패

            Assert.AreEqual(1, data.saveRevision);
            Assert.AreEqual("2026-01-01T00:00:00.0000000Z", data.lastSavedAtUtc);
            Assert.AreEqual(SaveData.CurrentSaveVersion, data.saveVersion);
            Assert.AreEqual(9, data.currentLevel, "되돌리는 것은 메타데이터뿐 - 플레이어의 진행은 그대로여야 합니다.");
        }

        [Test]
        public void 처음_저장에_실패하면_모름_상태로_되돌아간다()
        {
            SaveData data = new SaveData();

            SaveMetadataSnapshot before = SaveData.MarkSaved(data, DateTime.UtcNow);
            SaveData.RestoreMetadata(data, before);

            Assert.AreEqual(0, data.saveRevision);
            Assert.IsTrue(string.IsNullOrEmpty(data.lastSavedAtUtc));
        }

        [Test]
        public void 저장한_문서를_다시_읽으면_메타데이터가_그대로다()
        {
            SaveData source = new SaveData { currentLevel = 3, currency = 50 };
            SaveData.MarkSaved(source, new DateTime(2026, 5, 5, 5, 5, 5, DateTimeKind.Utc));
            SaveData.MarkSaved(source, new DateTime(2026, 5, 6, 6, 6, 6, DateTimeKind.Utc));

            SaveLoadResult result = SaveMigrationRunner.Default.Load(
                JsonUtility.ToJson(source, true), JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.Loaded, result.Status);
            Assert.AreEqual(2, result.Data.saveRevision);
            Assert.AreEqual("2026-05-06T06:06:06.0000000Z", result.Data.lastSavedAtUtc);
            Assert.AreEqual(SaveData.CurrentSaveVersion, result.Data.saveVersion);
        }

        // ---- 변환은 전부 되거나 전혀 안 된다 ----

        /// <summary>진행 값을 눈에 띄게 바꾸는 시험용 단계. 실패했을 때 이 흔적이 호출부의 문서에
        /// 남아 있는지로 "중간 변경이 새지 않는가"를 가린다.</summary>
        private sealed class MutatingStep : ISaveMigrationStep
        {
            public MutatingStep(int fromVersion) => FromVersion = fromVersion;

            public int FromVersion { get; }

            public int ToVersion => FromVersion + 1;

            public void Apply(SaveData data)
            {
                data.currentLevel += 100;
                data.currency = -777;
                data.items.Add(new InventoryItemState { itemId = "단계가_추가한_항목", count = 1 });

                // 목록 <b>안의 항목</b>까지 고친다 - 얕은 사본이면 여기서 호출부의 항목이 함께 바뀐다.
                if (data.items.Count > 0 && data.items[0] != null) data.items[0].count += 50;
                if (data.characters.Count > 0 && data.characters[0] != null) data.characters[0].level += 9;
            }
        }

        /// <summary>모든 필드가 기본값과 다른 문서. 어느 필드 하나라도 새어 나가면 눈에 띈다.</summary>
        private static SaveData FullyPopulated()
        {
            return new SaveData
            {
                saveVersion = 4242,
                saveRevision = 41,
                lastSavedAtUtc = "2026-01-02T03:04:05.0000000Z",
                currentLevel = 7,
                currentExp = 240,
                totalKillCount = 133,
                currency = 1250,
                characters = new List<CharacterSaveState>
                {
                    new CharacterSaveState { characterId = "barbarian", level = 4, currentStamina = 9 },
                },
                items = new List<InventoryItemState>
                {
                    new InventoryItemState { itemId = "potion", count = 3 },
                },
                recoverySlots = new List<RecoverySlotSaveState>
                {
                    new RecoverySlotSaveState
                    {
                        characterId = "barbarian",
                        startStamina = 2,
                        startedAtUtc = "2026-01-02T03:04:05.0000000Z",
                        completeAtUtc = "2026-01-02T05:04:05.0000000Z",
                        completionNotified = true,
                    },
                },
            };
        }

        private static void AssertUntouched(SaveData data, string because)
        {
            Assert.AreEqual(4242, data.saveVersion, $"{because} (saveVersion)");
            Assert.AreEqual(41, data.saveRevision, $"{because} (saveRevision)");
            Assert.AreEqual("2026-01-02T03:04:05.0000000Z", data.lastSavedAtUtc, $"{because} (lastSavedAtUtc)");
            Assert.AreEqual(7, data.currentLevel, $"{because} (currentLevel)");
            Assert.AreEqual(240, data.currentExp, $"{because} (currentExp)");
            Assert.AreEqual(133, data.totalKillCount, $"{because} (totalKillCount)");
            Assert.AreEqual(1250, data.currency, $"{because} (currency)");

            Assert.AreEqual(1, data.items.Count, $"{because} (items 개수)");
            Assert.AreEqual("potion", data.items[0].itemId, $"{because} (items[0].itemId)");
            Assert.AreEqual(3, data.items[0].count, $"{because} (items[0].count)");

            Assert.AreEqual(1, data.characters.Count, $"{because} (characters 개수)");
            Assert.AreEqual(4, data.characters[0].level, $"{because} (characters[0].level)");

            Assert.AreEqual(1, data.recoverySlots.Count, $"{because} (recoverySlots 개수)");
            Assert.AreEqual("barbarian", data.recoverySlots[0].characterId, $"{because} (recoverySlots[0])");
        }

        [Test]
        public void 러너는_중간_단계가_없으면_호출부의_문서에_아무_흔적도_남기지_않는다()
        {
            // v0->v1은 문서를 실컷 고치고, v1->v2가 없어 거기서 멈춘다. 예전에는 이때 호출부가 들고
            // 있던 문서에 v0->v1의 변경이 그대로 남았다 - 그 문서로 한 번만 저장돼도 원본은 끝이다.
            SaveMigrationRunner runner = RunnerTo(3, new MutatingStep(0), new MutatingStep(2));
            SaveData data = FullyPopulated();

            SaveMigrationResult result = runner.Migrate(data, 0);

            Assert.AreEqual(SaveMigrationOutcome.StepMissing, result.Outcome);
            AssertUntouched(data, "단계가 없어 멈췄으면 문서는 시도 전과 같아야 합니다");
        }

        [Test]
        public void 러너는_단계가_예외를_던져도_호출부의_문서에_아무_흔적도_남기지_않는다()
        {
            SaveMigrationRunner runner = RunnerTo(2, new MutatingStep(0), new ThrowingStep(1));
            SaveData data = FullyPopulated();

            SaveMigrationResult result = runner.Migrate(data, 0);

            Assert.AreEqual(SaveMigrationOutcome.StepFailed, result.Outcome);
            AssertUntouched(data, "단계가 실패했으면 문서는 시도 전과 같아야 합니다");
        }

        [Test]
        public void 러너는_미래_버전이면_saveVersion조차_건드리지_않는다()
        {
            SaveData data = FullyPopulated();

            SaveMigrationResult result = SaveMigrationRunner.Default.Migrate(
                data, SaveData.CurrentSaveVersion + 5);

            Assert.AreEqual(SaveMigrationOutcome.FutureVersion, result.Outcome);
            AssertUntouched(data, "미래 버전은 읽지도 고치지도 않습니다");
        }

        [Test]
        public void 러너는_성공하면_사본이_아니라_호출부의_문서에_결과를_남긴다()
        {
            // 작업 사본을 쓰더라도 성공했을 때의 계약은 그대로여야 한다 - 호출부는 넘긴 그 문서를 쓴다.
            SaveMigrationRunner runner = RunnerTo(1, new MutatingStep(0));
            SaveData data = FullyPopulated();

            SaveMigrationResult result = runner.Migrate(data, 0);

            Assert.AreEqual(SaveMigrationOutcome.Migrated, result.Outcome);
            Assert.AreEqual(1, data.saveVersion);
            Assert.AreEqual(107, data.currentLevel, "성공한 단계의 변경은 호출부의 문서에 남아야 합니다.");
            Assert.AreEqual(-777, data.currency);
            Assert.AreEqual(2, data.items.Count);
            Assert.AreEqual(53, data.items[0].count, "목록 안의 항목 변경도 그대로 남아야 합니다.");
            Assert.AreEqual(13, data.characters[0].level);
        }

        [Test]
        public void 작업_사본은_모든_진행_값을_왕복시킨다()
        {
            // 변환이 성공하는 경로는 반드시 사본을 한 번 거친다. 사본이 옮기지 못한 필드는 여기서
            // 기본값으로 되돌아가므로, 필드를 추가하고 CopyInto를 빠뜨리면 이 시험이 잡는다.
            SaveData data = FullyPopulated();
            data.saveVersion = SaveData.CurrentSaveVersion;

            SaveMigrationResult result = SaveMigrationRunner.Default.Migrate(data, SaveData.CurrentSaveVersion);

            Assert.AreEqual(SaveMigrationOutcome.AlreadyCurrent, result.Outcome);
            Assert.AreEqual(41, data.saveRevision);
            Assert.AreEqual("2026-01-02T03:04:05.0000000Z", data.lastSavedAtUtc);
            Assert.AreEqual(7, data.currentLevel);
            Assert.AreEqual(240, data.currentExp);
            Assert.AreEqual(133, data.totalKillCount);
            Assert.AreEqual(1250, data.currency);
            Assert.AreEqual("potion", data.items[0].itemId);
            Assert.AreEqual(3, data.items[0].count);
            Assert.AreEqual("barbarian", data.characters[0].characterId);
            Assert.AreEqual(9, data.characters[0].currentStamina);
            Assert.AreEqual("barbarian", data.recoverySlots[0].characterId);
            Assert.AreEqual(2, data.recoverySlots[0].startStamina);
            Assert.AreEqual("2026-01-02T05:04:05.0000000Z", data.recoverySlots[0].completeAtUtc);
            Assert.IsTrue(data.recoverySlots[0].completionNotified);
        }

        [Test]
        public void 저장_문서에_필드를_추가하면_작업_사본도_함께_고쳐야_한다()
        {
            // 사본은 손으로 쓴 코드라 필드를 늘리면 같이 늘려야 한다. 그것을 잊었을 때 조용히 값이
            // 사라지는 대신 여기서 걸리도록, 필드 목록 자체를 계약으로 박아 둔다.
            string[] expected =
            {
                "saveVersion", "saveRevision", "lastSavedAtUtc",
                "currentLevel", "currentExp", "totalKillCount",
                "characters", "currency", "items", "recoverySlots",
            };

            List<string> actual = new List<string>();
            foreach (System.Reflection.FieldInfo field in typeof(SaveData).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                actual.Add(field.Name);
            }

            CollectionAssert.AreEquivalent(expected, actual,
                "SaveData의 필드가 바뀌었습니다 - SaveMigrationRunner의 CopyInto와 이 목록을 함께 고치세요.");
        }

        // ---- 훑기는 JSON 문법을 엄격하게 지킨다 ----

        [Test]
        public void Probe_쉼표가_남거나_빠지면_Malformed다()
        {
            string[] broken =
            {
                @"{,""currentLevel"":1}",
                @"{""currentLevel"":1,}",
                @"{""currentLevel"":1,,""currentExp"":2}",
                @"{""currentLevel"":1 ""currentExp"":2}",
                @"{""saveVersion"":1,}",
                @"{""saveVersion"":1 ""currentLevel"":2}",
                @"{""items"":[1,2,]}",
                @"{""items"":[,1]}",
                @"{""items"":[1 2]}",
            };

            foreach (string json in broken)
            {
                Assert.AreEqual(SaveVersionProbeStatus.Malformed, SaveVersionProbe.Probe(json).Status,
                    $"입력: '{json}'");
            }
        }

        [Test]
        public void Probe_엉터리_리터럴은_Malformed다()
        {
            // 예전에는 "구분자가 나올 때까지"를 값으로 삼아서, 값 자리에 무엇이 있든 통과했다.
            string[] broken =
            {
                @"{""a"":tru}",
                @"{""a"":TRUE}",
                @"{""a"":undefined}",
                @"{""a"":12abc}",
                @"{""a"":01}",
                @"{""a"":+1}",
                @"{""a"":.5}",
                @"{""a"":1.}",
                @"{""a"":1e}",
                @"{""a"":}",
                @"{""a"":@#$}",
            };

            foreach (string json in broken)
            {
                Assert.AreEqual(SaveVersionProbeStatus.Malformed, SaveVersionProbe.Probe(json).Status,
                    $"입력: '{json}'");
            }
        }

        [Test]
        public void Probe_정상적인_리터럴과_수는_그대로_통과한다()
        {
            // 엄격하게 만들다가 진짜 저장 파일을 막아 버리면 최악이다 - 통과해야 할 것도 함께 박아 둔다.
            string[] fine =
            {
                @"{""a"":true,""b"":false,""c"":null}",
                @"{""a"":0,""b"":-1,""c"":1.5,""d"":-2.25e-3,""e"":1E+10}",
                @"{""a"":[],""b"":{},""c"":[{""d"":[1,2]}]}",
                @"{""a"":""é\t\\\/\""""}",
            };

            foreach (string json in fine)
            {
                Assert.AreEqual(SaveVersionProbeStatus.Unversioned, SaveVersionProbe.Probe(json).Status,
                    $"입력: '{json}'");
            }
        }

        [Test]
        public void Probe_JsonUtility가_쓰는_모양을_문자열_그대로_통과시킨다()
        {
            // 엄격해진 문법이 <b>실제 저장 파일 서식</b>을 막지 않는지는 무엇보다 중요하다. 위쪽의
            // 왕복 시험이 진짜 JsonUtility로 확인하지만, 그 시험은 Unity 안에서만 돈다 - 여기서는
            // 같은 서식(4칸 들여쓰기, ": " 구분, 빈 목록/중첩 객체)을 문자열로 박아 엔진 없이도 지킨다.
            string json =
                "{\n" +
                "    \"saveVersion\": 1,\n" +
                "    \"saveRevision\": 12,\n" +
                "    \"lastSavedAtUtc\": \"2026-05-06T06:06:06.0000000Z\",\n" +
                "    \"currentLevel\": 7,\n" +
                "    \"currentExp\": 240,\n" +
                "    \"totalKillCount\": 133,\n" +
                "    \"characters\": [],\n" +
                "    \"currency\": 1250,\n" +
                "    \"items\": [\n" +
                "        {\n" +
                "            \"itemId\": \"potion\",\n" +
                "            \"count\": 3\n" +
                "        }\n" +
                "    ],\n" +
                "    \"recoverySlots\": [\n" +
                "        {\n" +
                "            \"characterId\": \"\",\n" +
                "            \"startStamina\": 0,\n" +
                "            \"startedAtUtc\": \"\",\n" +
                "            \"completeAtUtc\": \"\",\n" +
                "            \"completionNotified\": false\n" +
                "        }\n" +
                "    ]\n" +
                "}";

            SaveVersionProbeResult result = SaveVersionProbe.Probe(json);

            Assert.AreEqual(SaveVersionProbeStatus.Versioned, result.Status);
            Assert.AreEqual(1, result.Version);
        }

        [Test]
        public void Probe_괄호_짝이_맞지_않으면_Malformed다()
        {
            string[] broken =
            {
                @"{""a"":[1,2}",
                @"{""a"":{""b"":1]}",
                @"{""a"":[1,2]]}",
                @"{""a"":[[1,2]}",
                @"{""a"":{""b"":[1}}",
            };

            foreach (string json in broken)
            {
                Assert.AreEqual(SaveVersionProbeStatus.Malformed, SaveVersionProbe.Probe(json).Status,
                    $"입력: '{json}'");
            }
        }

        [Test]
        public void Probe_문자열_안의_제어문자는_Malformed다()
        {
            Assert.AreEqual(SaveVersionProbeStatus.Malformed,
                SaveVersionProbe.Probe("{\"a\":\"줄\n바꿈\"}").Status);
        }

        [Test]
        public void Probe_끝없이_깊은_중첩에_스택을_넘기지_않는다()
        {
            // 손상된 파일은 예외 없이 결과값으로 다뤄야 한다. 깊이 제한이 없으면 이 입력 하나로
            // 잡을 수 없는 StackOverflow가 나서 앱이 죽는다.
            string json = @"{""a"":" + new string('[', 20000);

            Assert.AreEqual(SaveVersionProbeStatus.Malformed, SaveVersionProbe.Probe(json).Status);
        }

        [Test]
        public void Probe_saveVersion이_여러_번_나오면_가장_큰_값으로_본다()
        {
            // 어느 쪽이 진짜인지 알 수 없으니 가장 보수적인 쪽(미래 버전 차단에 걸리는 쪽)을 고른다.
            // 앞의 값을 믿으면 미래 형식 파일을 헌 형식으로 읽어 덮어쓰는 길이 열린다.
            Assert.AreEqual(99, SaveVersionProbe.Probe(@"{""saveVersion"":1,""saveVersion"":99}").Version);
            Assert.AreEqual(99, SaveVersionProbe.Probe(@"{""saveVersion"":99,""saveVersion"":1}").Version);
            Assert.AreEqual(1, SaveVersionProbe.Probe(@"{""saveVersion"":1,""saveVersion"":1}").Version);

            Assert.AreEqual(SaveVersionProbeStatus.Malformed,
                SaveVersionProbe.Probe(@"{""saveVersion"":1,""saveVersion"":""x""}").Status,
                "중복된 값 중 하나라도 정수가 아니면 믿을 수 없는 파일입니다.");
        }

        [Test]
        public void 중복된_saveVersion으로_미래_버전_차단을_피할_수_없다()
        {
            // 훑기의 보수적 판정이 실제로 차단까지 이어지는지를 끝에서 끝까지 확인한다.
            string json = $@"{{""saveVersion"":{SaveData.CurrentSaveVersion},"
                          + $@"""saveVersion"":{SaveData.CurrentSaveVersion + 1},""currentLevel"":99}}";

            SaveLoadResult result = SaveMigrationRunner.Default.Load(json, JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.FutureVersionBlocked, result.Status);
            Assert.IsNull(result.Data, "막힌 결과에 데이터를 쥐어 주면 호출부가 저장해 버립니다.");
            Assert.IsTrue(result.ShouldBlockSaving);
        }
    }
}
