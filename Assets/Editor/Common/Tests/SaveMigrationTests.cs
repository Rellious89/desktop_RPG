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
            SaveMigrationRunner runner = RunnerTo(1, new RecordingStep(0, log));
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

            // 예전 파일에 적혀 있던 두 항목은 <b>앞자리에 그대로</b> 남는다. 그 뒤로 v1->v2가 그 시절
            // 여섯 캐릭터를 덧붙이므로 개수는 2 + 6이다('barbarian'은 'Barbarian'과 다른 키다).
            Assert.AreEqual(2 + ExpectedLegacyIds.Length, data.characters.Count);
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
            Assert.IsNotNull(data.partyCharacterIds);
            Assert.IsNotNull(data.items);
            Assert.AreEqual(SaveData.DefaultRecoverySlotCount, data.recoverySlots.Count);
            Assert.AreEqual(SaveData.DefaultPurificationSlotCount, data.purificationSlots.Count);
        }

        [Test]
        public void 정규화는_없는_목록을_빈_목록으로_만든다()
        {
            SaveData data = new SaveData
            {
                characters = null, partyCharacterIds = null, items = null, recoverySlots = null,
                purificationSlots = null,
            };

            SaveDataNormalizer.Normalize(data);

            Assert.IsNotNull(data.characters);
            Assert.IsEmpty(data.characters);
            Assert.IsNotNull(data.partyCharacterIds);
            Assert.IsEmpty(data.partyCharacterIds);
            Assert.IsNotNull(data.items);
            Assert.IsEmpty(data.items);
            Assert.AreEqual(SaveData.DefaultRecoverySlotCount, data.recoverySlots.Count);
            Assert.AreEqual(SaveData.DefaultPurificationSlotCount, data.purificationSlots.Count);
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
        public void 정규화는_정화_슬롯의_번호와_첫_캐릭터를_보존하고_잘못된_값만_비운다()
        {
            SaveData data = new SaveData
            {
                purificationSlots = new List<PurificationSlotSaveState>
                {
                    null,
                    new PurificationSlotSaveState
                    {
                        purificationTypeId = "church_prayer", characterId = "CatMage",
                        lastCalculatedAtUtc = "not-a-time", progressTicks = -10,
                    },
                    new PurificationSlotSaveState
                    {
                        purificationTypeId = "unknown_type", characterId = "CatMage",
                        lastCalculatedAtUtc = "raw-time", progressTicks = 99,
                    },
                    new PurificationSlotSaveState
                    {
                        purificationTypeId = "leftover", characterId = null,
                        lastCalculatedAtUtc = "leftover-time", progressTicks = 77,
                    },
                },
            };

            SaveDataNormalizer.Normalize(data);

            Assert.AreEqual(4, data.purificationSlots.Count, "추가 슬롯과 인덱스를 보존해야 합니다.");
            Assert.IsFalse(data.purificationSlots[0].HasCharacter);
            Assert.AreEqual("CatMage", data.purificationSlots[1].characterId);
            Assert.AreEqual("church_prayer", data.purificationSlots[1].purificationTypeId);
            Assert.AreEqual("not-a-time", data.purificationSlots[1].lastCalculatedAtUtc,
                "저장 계층은 읽을 수 없는 시각 원문을 지우지 않습니다.");
            Assert.AreEqual(0, data.purificationSlots[1].progressTicks);
            Assert.IsFalse(data.purificationSlots[2].HasCharacter, "뒤의 중복 캐릭터 슬롯은 비웁니다.");
            Assert.IsFalse(data.purificationSlots[3].HasCharacter);
            Assert.IsTrue(string.IsNullOrEmpty(data.purificationSlots[3].purificationTypeId));
            Assert.IsTrue(string.IsNullOrEmpty(data.purificationSlots[3].lastCalculatedAtUtc));
            Assert.AreEqual(0, data.purificationSlots[3].progressTicks);
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
        public void 정규화는_파티에서_빈값_중복과_미보유를_비우고_슬롯_순서를_지킨다()
        {
            SaveData data = new SaveData
            {
                characters = new List<CharacterSaveState>
                {
                    new CharacterSaveState { characterId = "CatKnight" },
                    new CharacterSaveState { characterId = "UnknownFromOldBuild" },
                    new CharacterSaveState { characterId = "ElfArcher" },
                },
                partyCharacterIds = new List<string>
                {
                    "ElfArcher", null, "CatKnight", "ElfArcher", "Missing", "UnknownFromOldBuild", "",
                },
            };

            SaveDataNormalizer.Normalize(data);
            CollectionAssert.AreEqual(
                new[] { "ElfArcher", string.Empty, "CatKnight", string.Empty, string.Empty, "UnknownFromOldBuild", string.Empty },
                data.partyCharacterIds);

            SaveDataNormalizer.Normalize(data);
            CollectionAssert.AreEqual(
                new[] { "ElfArcher", string.Empty, "CatKnight", string.Empty, string.Empty, "UnknownFromOldBuild", string.Empty },
                data.partyCharacterIds,
                "정규화는 여러 번 실행해도 결과가 같아야 합니다.");
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
                    new CharacterSaveState
                    {
                        characterId = "barbarian", level = 4, currentExp = 5, currentStamina = 9,
                    },
                },
                partyCharacterIds = new List<string> { "barbarian" },
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
                buildingConstructions = new List<BuildingConstructionSaveState>
                {
                    new BuildingConstructionSaveState
                    {
                        buildingId = "1",
                        startedAtUtc = "2026-01-02T03:04:05.0000000Z",
                        completeAtUtc = "2026-01-02T03:05:05.0000000Z",
                    },
                },
                recruitmentCycles = new List<RecruitmentCycleSaveState>
                {
                    new RecruitmentCycleSaveState
                    {
                        recruitmentAccessId = "Inn_Normal_Access",
                        startedAtUtc = "2026-01-02T03:05:05.0000000Z",
                        readyAtUtc = "2026-01-02T04:05:05.0000000Z",
                        pendingCharacterId = "CatMage",
                    },
                },
                purificationSlots = new List<PurificationSlotSaveState>
                {
                    new PurificationSlotSaveState
                    {
                        purificationTypeId = "church_prayer",
                        characterId = "barbarian",
                        lastCalculatedAtUtc = "2026-01-02T03:06:05.0000000Z",
                        progressTicks = 1234567,
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

            CollectionAssert.AreEqual(new[] { "barbarian" }, data.partyCharacterIds,
                $"{because} (partyCharacterIds)");

            Assert.AreEqual(1, data.items.Count, $"{because} (items 개수)");
            Assert.AreEqual("potion", data.items[0].itemId, $"{because} (items[0].itemId)");
            Assert.AreEqual(3, data.items[0].count, $"{because} (items[0].count)");

            Assert.AreEqual(1, data.characters.Count, $"{because} (characters 개수)");
            Assert.AreEqual(4, data.characters[0].level, $"{because} (characters[0].level)");

            Assert.AreEqual(1, data.recoverySlots.Count, $"{because} (recoverySlots 개수)");
            Assert.AreEqual("barbarian", data.recoverySlots[0].characterId, $"{because} (recoverySlots[0])");

            Assert.AreEqual(1, data.buildingConstructions.Count, $"{because} (buildingConstructions 개수)");
            Assert.AreEqual("1", data.buildingConstructions[0].buildingId,
                $"{because} (buildingConstructions[0])");

            Assert.AreEqual(1, data.recruitmentCycles.Count, $"{because} (recruitmentCycles 개수)");
            Assert.AreEqual("Inn_Normal_Access", data.recruitmentCycles[0].recruitmentAccessId,
                $"{because} (recruitmentCycles[0])");
            Assert.AreEqual("CatMage", data.recruitmentCycles[0].pendingCharacterId,
                $"{because} (recruitmentCycles[0].pendingCharacterId)");

            Assert.AreEqual(1, data.purificationSlots.Count, $"{because} (purificationSlots 개수)");
            Assert.AreEqual("church_prayer", data.purificationSlots[0].purificationTypeId,
                $"{because} (purificationSlots[0].purificationTypeId)");
            Assert.AreEqual("barbarian", data.purificationSlots[0].characterId,
                $"{because} (purificationSlots[0].characterId)");
            Assert.AreEqual("2026-01-02T03:06:05.0000000Z", data.purificationSlots[0].lastCalculatedAtUtc,
                $"{because} (purificationSlots[0].lastCalculatedAtUtc)");
            Assert.AreEqual(1234567, data.purificationSlots[0].progressTicks,
                $"{because} (purificationSlots[0].progressTicks)");
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
            Assert.AreEqual(5, data.characters[0].currentExp, "사본이 경험치를 빠뜨리면 안 된다.");
            CollectionAssert.AreEqual(new[] { "barbarian" }, data.partyCharacterIds,
                "파티 목록도 사본 경계를 지나 보존해야 한다.");
            Assert.AreEqual("barbarian", data.recoverySlots[0].characterId);
            Assert.AreEqual(2, data.recoverySlots[0].startStamina);
            Assert.AreEqual("2026-01-02T05:04:05.0000000Z", data.recoverySlots[0].completeAtUtc);
            Assert.IsTrue(data.recoverySlots[0].completionNotified);
            Assert.AreEqual("1", data.buildingConstructions[0].buildingId,
                "사본이 건설 기록을 빠뜨리면 안 된다.");
            Assert.AreEqual("2026-01-02T03:04:05.0000000Z", data.buildingConstructions[0].startedAtUtc);
            Assert.AreEqual("2026-01-02T03:05:05.0000000Z", data.buildingConstructions[0].completeAtUtc);
            Assert.AreEqual("Inn_Normal_Access", data.recruitmentCycles[0].recruitmentAccessId,
                "사본이 모집 주기 기록을 빠뜨리면 안 된다.");
            Assert.AreEqual("2026-01-02T03:05:05.0000000Z", data.recruitmentCycles[0].startedAtUtc);
            Assert.AreEqual("2026-01-02T04:05:05.0000000Z", data.recruitmentCycles[0].readyAtUtc);
            Assert.AreEqual("CatMage", data.recruitmentCycles[0].pendingCharacterId);
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
                "characters", "partyCharacterIds", "currency", "items", "recoverySlots", "buildingConstructions",
                "recruitmentCycles", "unlockedRecruitmentCharacterIds", "purificationSlots", "characterStoryQuests",
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

        [Test]
        public void 캐릭터_항목에_필드를_추가하면_깊은_사본도_함께_고쳐야_한다()
        {
            // CopyCharacters도 손으로 쓴 코드다. 캐릭터 항목에 칸을 늘리고 사본을 빠뜨리면 그 값이
            // 변환을 지날 때마다 조용히 기본값으로 되돌아간다.
            string[] expected = { "characterId", "level", "currentExp", "currentStamina", "passiveStaminaLastCalculatedUtc", "passiveStaminaProgress", "currentCorruption" };

            List<string> actual = new List<string>();
            foreach (System.Reflection.FieldInfo field in typeof(CharacterSaveState).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                actual.Add(field.Name);
            }

            CollectionAssert.AreEquivalent(expected, actual,
                "CharacterSaveState의 필드가 바뀌었습니다 - SaveMigrationRunner의 CopyCharacters와 이 목록을 함께 고치세요.");
        }

        [Test]
        public void 정화_슬롯에_필드를_추가하면_깊은_사본도_함께_고쳐야_한다()
        {
            string[] expected =
            {
                "purificationTypeId", "characterId", "lastCalculatedAtUtc", "progressTicks",
            };

            var actual = new List<string>();
            foreach (System.Reflection.FieldInfo field in typeof(PurificationSlotSaveState).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                actual.Add(field.Name);
            }

            CollectionAssert.AreEquivalent(expected, actual,
                "PurificationSlotSaveState 필드가 바뀌면 CopyPurificationSlots도 함께 고쳐야 합니다.");
        }

        [Test]
        public void 깊은_사본은_캐릭터_경험치와_파티_목록을_옮기고_원본과_끊어_둔다()
        {
            SaveData data = new SaveData
            {
                saveVersion = SaveData.CurrentSaveVersion,
                characters = new List<CharacterSaveState>
                {
                    new CharacterSaveState
                    {
                        characterId = "CatKnight", level = 4, currentExp = 7, currentStamina = 9,
                        passiveStaminaLastCalculatedUtc = "2026-08-24T00:00:00.0000000Z", passiveStaminaProgress = 1234, currentCorruption = 77.25d,
                    },
                },
                partyCharacterIds = new List<string> { "CatKnight" },
                purificationSlots = new List<PurificationSlotSaveState>
                {
                    new PurificationSlotSaveState
                    {
                        purificationTypeId = "church_prayer",
                        characterId = "CatKnight",
                        lastCalculatedAtUtc = "2026-08-24T01:00:00.0000000Z",
                        progressTicks = 4321,
                    },
                },
            };

            CharacterSaveState original = data.characters[0];
            List<string> originalParty = data.partyCharacterIds;
            List<PurificationSlotSaveState> originalPurificationSlots = data.purificationSlots;
            PurificationSlotSaveState originalPurificationSlot = data.purificationSlots[0];

            SaveMigrationResult result = SaveMigrationRunner.Default.Migrate(data, SaveData.CurrentSaveVersion);

            Assert.AreEqual(SaveMigrationOutcome.AlreadyCurrent, result.Outcome);
            Assert.AreEqual(4, data.characters[0].level);
            Assert.AreEqual(7, data.characters[0].currentExp, "사본이 경험치를 빠뜨리면 안 된다.");
            Assert.AreEqual(9, data.characters[0].currentStamina);
            Assert.AreEqual("2026-08-24T00:00:00.0000000Z", data.characters[0].passiveStaminaLastCalculatedUtc);
            Assert.AreEqual(1234, data.characters[0].passiveStaminaProgress);
            Assert.AreEqual(77.25d, data.characters[0].currentCorruption, 0.0000001d);
            Assert.AreEqual("church_prayer", data.purificationSlots[0].purificationTypeId);
            Assert.AreEqual("CatKnight", data.purificationSlots[0].characterId);
            Assert.AreEqual("2026-08-24T01:00:00.0000000Z", data.purificationSlots[0].lastCalculatedAtUtc);
            Assert.AreEqual(4321, data.purificationSlots[0].progressTicks);

            // 사본을 거쳤으므로 호출부의 문서에는 <b>새 항목</b>이 들어와 있어야 한다.
            Assert.AreNotSame(original, data.characters[0], "목록 안의 항목까지 새로 만들어야 얕은 사본이 아니다.");
            Assert.AreNotSame(originalParty, data.partyCharacterIds, "파티 목록도 새 목록이어야 한다.");
            Assert.AreNotSame(originalPurificationSlots, data.purificationSlots,
                "정화 슬롯 목록도 새 목록이어야 한다.");
            Assert.AreNotSame(originalPurificationSlot, data.purificationSlots[0],
                "정화 슬롯 항목도 새 객체여야 한다.");
            CollectionAssert.AreEqual(new[] { "CatKnight" }, data.partyCharacterIds);
        }

        [Test]
        public void 오염도_정규화는_유한한_소수값만_보존한다()
        {
            SaveData data = new SaveData { characters = new List<CharacterSaveState>
            {
                new CharacterSaveState { characterId = "A", currentCorruption = 300.75d },
                new CharacterSaveState { characterId = "B", currentCorruption = -1d },
                new CharacterSaveState { characterId = "C", currentCorruption = double.NaN },
                new CharacterSaveState { characterId = "D", currentCorruption = double.PositiveInfinity },
            }};
            SaveDataNormalizer.Normalize(data);
            Assert.AreEqual(300.75d, data.characters[0].currentCorruption, 0.0000001d);
            Assert.AreEqual(0d, data.characters[1].currentCorruption);
            Assert.AreEqual(0d, data.characters[2].currentCorruption);
            Assert.AreEqual(0d, data.characters[3].currentCorruption);
        }

        [Test]
        public void v1_변환은_캐릭터의_경험치를_건드리지_않는다()
        {
            SaveData data = V1Document(
                Character("Barbarian", 4, 9),
                Character("scarecrow", 2, 0));

            data.characters[0].currentExp = 6;
            data.characters[1].currentExp = 3;

            ApplyV1ToV2(data);

            Assert.AreEqual(6, data.characters[0].currentExp, "변환은 진행 값을 바꾸지 않는다.");
            Assert.AreEqual(3, data.characters[1].currentExp);

            // 새로 덧붙는 항목의 경험치는 기본값 0이다.
            for (int i = 2; i < data.characters.Count; i++)
            {
                Assert.AreEqual(0, data.characters[i].currentExp,
                    $"덧붙인 {data.characters[i].characterId}의 경험치는 0에서 시작한다.");
            }
        }

        [Test]
        public void 경험치_항목이_없는_예전_파일은_0으로_읽힌다()
        {
            // 이 칸을 더하면서 저장 형식 번호를 올리지 않은 근거다 - 없는 필드는 0으로 채워지고,
            // 0은 "이번 레벨에서 아직 아무것도 모으지 않았다"라서 그대로 옳은 값이다.
            string json = $@"{{""saveVersion"":{SaveData.CurrentSaveVersion},"
                          + @"""characters"":[{""characterId"":""CatKnight"",""level"":4,""currentStamina"":9}]}";

            SaveLoadResult result = SaveMigrationRunner.Default.Load(json, JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.Loaded, result.Status);
            Assert.AreEqual(1, result.Data.characters.Count);
            Assert.AreEqual(4, result.Data.characters[0].level, "기존 값은 그대로 읽혀야 한다.");
            Assert.AreEqual(9, result.Data.characters[0].currentStamina);
            Assert.AreEqual(0, result.Data.characters[0].currentExp,
                "없는 경험치 칸은 0이어야 한다(형식 번호를 올리지 않는 근거).");
        }

        [Test]
        public void 경험치_항목이_없는_v0_파일도_v2까지_올라가고_0을_얻는다()
        {
            SaveLoadResult result = SaveMigrationRunner.Default.Load(LegacyJson, JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.Migrated, result.Status);
            Assert.AreEqual(0, result.FromVersion);
            Assert.AreEqual(SaveData.CurrentSaveVersion, result.ToVersion);

            foreach (CharacterSaveState state in result.Data.characters)
            {
                Assert.AreEqual(0, state.currentExp, $"{state.characterId}의 경험치는 0으로 읽혀야 한다.");
            }

            // 예전 파일에 적혀 있던 값은 그대로다 - 경험치 칸이 생겼다고 다른 값이 달라지지 않는다.
            Assert.AreEqual(4, result.Data.characters[0].level);
            Assert.AreEqual(9, result.Data.characters[0].currentStamina);
        }

        [Test]
        public void 경험치를_담은_문서는_저장하고_다시_읽어도_그대로다()
        {
            SaveData source = new SaveData
            {
                characters = new List<CharacterSaveState>
                {
                    new CharacterSaveState { characterId = "CatKnight", level = 12, currentExp = 7, currentStamina = 3 },
                },
            };

            SaveData.MarkSaved(source, DateTime.UtcNow);
            string json = JsonUtility.ToJson(source);

            SaveLoadResult result = SaveMigrationRunner.Default.Load(json, JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.Loaded, result.Status);
            Assert.AreEqual(12, result.Data.characters[0].level);
            Assert.AreEqual(7, result.Data.characters[0].currentExp, "왕복해도 경험치가 살아 있어야 한다.");
            Assert.AreEqual(3, result.Data.characters[0].currentStamina);
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

        // ---- v1 -> v2: 그 시절 여섯 캐릭터를 파일에 남긴다 ----
        //
        // v1까지는 "이 캐릭터를 가지고 있는가"를 적는 자리가 없었다. 쓸 수 있는 캐릭터는 씬에
        // 직렬화된 로스터 목록이 정했고, 저장 목록은 그 목록을 따라 시작할 때 만들어진 상태 기록이라
        // 보유의 근거가 아니었다. v2가 그 권한을 저장 문서로 옮기므로, 항목이 없는 캐릭터를 잃지
        // 않도록 그 시절 여섯을 지금 파일에 적어 두는 것이 이 단계의 전부다. 아래 시험들이 확인하는
        // 것은 두 가지뿐이다. (1) 없는 것만 정확히 덧붙인다. (2) 이미 있던 것은 한 글자도 건드리지 않는다.

        /// <summary>
        /// v1 시절 모두가 쓸 수 있던 여섯 캐릭터의 id를, 변환이 덧붙여야 하는 <b>차례 그대로</b>.
        ///
        /// <b>일부러 프로덕션 코드를 참조하지 않고 여기에 다시 적는다.</b> 변환의 상수를 그대로 가져다
        /// 비교하면 그 상수를 잘못 고쳐도 시험이 함께 따라가 통과한다 - 그러면 예전 사용자의 캐릭터가
        /// 사라지는 변경을 아무도 막지 못한다. 이 목록은 시험이 독립적으로 들고 있는 <b>기대값</b>이며,
        /// 변환이 무엇을 하든 결과가 이것과 같아야 한다.
        /// </summary>
        private static readonly string[] ExpectedLegacyIds =
        {
            "Barbarian", "CatKnight", "CatMage", "ElfArcher", "ElfGuardian", "RabbitHealer",
        };

        /// <summary>변환이 새로 덧붙이는 항목의 기대 레벨.</summary>
        private const int ExpectedAppendedLevel = 1;

        /// <summary>변환이 새로 덧붙이는 항목의 기대 행동력. -1은 "아직 초기화되지 않음"이다.</summary>
        private const int ExpectedAppendedStamina = -1;

        /// <summary>덧붙여진 항목이 기대한 기본값을 가졌는지.</summary>
        private static void AssertAppendedDefaults(CharacterSaveState state)
        {
            Assert.IsNotNull(state);
            Assert.AreEqual(ExpectedAppendedLevel, state.level, $"{state.characterId}의 레벨");
            Assert.AreEqual(ExpectedAppendedStamina, state.currentStamina,
                $"{state.characterId}의 행동력 - -1(초기화 안 됨)이어야 하며 0(소진)이면 안 됩니다.");
        }

        /// <summary>v1 문서 하나를 만든다. 캐릭터 목록만 시험마다 다르게 넣는다.</summary>
        private static SaveData V1Document(params CharacterSaveState[] characters)
        {
            return new SaveData
            {
                saveVersion = 1,
                saveRevision = 5,
                lastSavedAtUtc = "2026-01-02T03:04:05.0000000Z",
                currentLevel = 7,
                currentExp = 240,
                totalKillCount = 133,
                currency = 1250,
                characters = new List<CharacterSaveState>(characters),
                items = new List<InventoryItemState>
                {
                    new InventoryItemState { itemId = "potion", count = 3 },
                },
                recoverySlots = new List<RecoverySlotSaveState>
                {
                    new RecoverySlotSaveState { characterId = "CatKnight", startStamina = 2 },
                },
            };
        }

        private static CharacterSaveState Character(string id, int level, int stamina)
        {
            return new CharacterSaveState { characterId = id, level = level, currentStamina = stamina };
        }

        private static List<string> IdsOf(List<CharacterSaveState> characters)
        {
            List<string> ids = new List<string>();
            foreach (CharacterSaveState state in characters) ids.Add(state == null ? null : state.characterId);
            return ids;
        }

        /// <summary>러너를 거치지 않고 단계만 돌린다 - 정규화가 목록을 손대기 전의 결과를 봐야 하는
        /// 시험(특히 null 항목 보존)이 있기 때문이다.</summary>
        private static SaveData ApplyV1ToV2(SaveData data)
        {
            new V1ToV2Step().Apply(data);
            return data;
        }

        private static SaveData ApplyV2ToV3(SaveData data)
        {
            new V2ToV3Step().Apply(data);
            return data;
        }

        [Test]
        public void v2_v3_단계는_한_칸짜리_단계다()
        {
            V2ToV3Step step = new V2ToV3Step();

            Assert.AreEqual(2, step.FromVersion);
            Assert.AreEqual(3, step.ToVersion);
        }

        [Test]
        public void v2_파티는_보유_순서에서_최대_셋만_선정하고_원본_캐릭터는_보존한다()
        {
            CharacterSaveState first = Character("UnknownFromOldBuild", 9, 4);
            CharacterSaveState duplicate = Character("CatKnight", 2, 8);
            SaveData data = new SaveData
            {
                saveVersion = 2,
                characters = new List<CharacterSaveState>
                {
                    null,
                    Character(string.Empty, 3, 2),
                    first,
                    Character("CatKnight", 5, 7),
                    duplicate,
                    Character("ElfArcher", 4, 6),
                    Character("RabbitHealer", 1, 3),
                },
                partyCharacterIds = new List<string> { "DoNotTrustV2" },
            };

            ApplyV2ToV3(data);

            CollectionAssert.AreEqual(
                new[] { "UnknownFromOldBuild", "CatKnight", "ElfArcher" }, data.partyCharacterIds);
            Assert.AreSame(first, data.characters[2]);
            Assert.AreSame(duplicate, data.characters[4]);
            Assert.IsNull(data.characters[0]);
            Assert.AreEqual(string.Empty, data.characters[1].characterId);
        }

        [Test]
        public void v2_파티는_보유가_없으면_빈_목록이며_잘못된_입력은_예외다()
        {
            SaveData data = new SaveData { saveVersion = 2, characters = null };

            ApplyV2ToV3(data);

            Assert.IsNotNull(data.partyCharacterIds);
            Assert.IsEmpty(data.partyCharacterIds);
            Assert.Throws<ArgumentNullException>(() => new V2ToV3Step().Apply(null));
        }

        [Test]
        public void v1_v2_단계는_한_칸짜리_단계다()
        {
            V1ToV2Step step = new V1ToV2Step();

            Assert.AreEqual(1, step.FromVersion);
            Assert.AreEqual(2, step.ToVersion);
            Assert.AreEqual(step.FromVersion + 1, step.ToVersion);
        }

        [Test]
        public void 변환_결과의_여섯_id는_철자와_순서까지_고정이다()
        {
            // 이 문자열이 곧 저장 키다. 한 글자라도 달라지면 예전 사용자의 진행과 연결이 끊긴다.
            // 변환의 내부 상수가 아니라 <b>변환이 실제로 만들어 낸 목록</b>을 본다.
            SaveData data = ApplyV1ToV2(V1Document());

            CollectionAssert.AreEqual(ExpectedLegacyIds, IdsOf(data.characters),
                "여섯 id의 철자와 덧붙이는 차례가 달라지면 예전 사용자의 진행과 연결이 끊깁니다.");

            foreach (CharacterSaveState state in data.characters) AssertAppendedDefaults(state);
        }

        [Test]
        public void 기본_표는_v0부터_현재_버전까지_빈틈없이_등록한다()
        {
            List<ISaveMigrationStep> steps = new List<ISaveMigrationStep>(SaveMigrationRunner.CreateDefaultSteps());

            Assert.AreEqual(SaveData.CurrentSaveVersion, steps.Count,
                "v0부터 현재 버전까지 한 칸씩 올리려면 칸 수만큼의 단계가 있어야 합니다.");

            List<int> fromVersions = new List<int>();
            foreach (ISaveMigrationStep step in steps) fromVersions.Add(step.FromVersion);

            var expectedFromVersions = new List<int>();
            for (int version = 0; version < SaveData.CurrentSaveVersion; version++)
            {
                expectedFromVersions.Add(version);
            }
            CollectionAssert.AreEquivalent(expectedFromVersions, fromVersions,
                "v0부터 현재 버전까지 모든 단일 단계가 있어야 합니다.");
            Assert.AreEqual(SaveData.CurrentSaveVersion, SaveMigrationRunner.Default.TargetVersion);
        }

        [Test]
        public void v1_문서의_캐릭터_목록이_비어_있으면_여섯이_모두_생긴다()
        {
            SaveData data = ApplyV1ToV2(V1Document());

            CollectionAssert.AreEqual(ExpectedLegacyIds, IdsOf(data.characters),
                "덧붙이는 차례는 정해진 순서 그대로여야 합니다.");

            foreach (CharacterSaveState state in data.characters) AssertAppendedDefaults(state);
        }

        [Test]
        public void v1_문서의_캐릭터_목록이_null이어도_여섯이_모두_생긴다()
        {
            SaveData data = V1Document();
            data.characters = null;

            ApplyV1ToV2(data);

            Assert.IsNotNull(data.characters);
            CollectionAssert.AreEqual(ExpectedLegacyIds, IdsOf(data.characters));
        }

        [Test]
        public void v1_문서에_여섯이_이미_다_있으면_아무것도_덧붙이지_않는다()
        {
            SaveData data = V1Document(
                Character("Barbarian", 4, 9),
                Character("CatKnight", 3, 0),
                Character("CatMage", 2, 30),
                Character("ElfArcher", 5, 1),
                Character("ElfGuardian", 1, -1),
                Character("RabbitHealer", 9, 12));

            ApplyV1ToV2(data);

            Assert.AreEqual(6, data.characters.Count, "이미 다 있으면 늘어날 이유가 없습니다.");

            // 값도 순서도 그대로여야 한다 - 변환은 '없는 것을 채우는' 일만 한다.
            Assert.AreEqual("Barbarian", data.characters[0].characterId);
            Assert.AreEqual(4, data.characters[0].level);
            Assert.AreEqual(9, data.characters[0].currentStamina);
            Assert.AreEqual("CatKnight", data.characters[1].characterId);
            Assert.AreEqual(0, data.characters[1].currentStamina, "행동력 0을 -1로 되돌리면 안 됩니다.");
            Assert.AreEqual("RabbitHealer", data.characters[5].characterId);
            Assert.AreEqual(9, data.characters[5].level);
            Assert.AreEqual(12, data.characters[5].currentStamina);
        }

        [Test]
        public void v1_문서에_일부만_있으면_없는_것만_뒤에_덧붙인다()
        {
            SaveData data = V1Document(
                Character("CatMage", 8, 3),
                Character("Barbarian", 2, 0));

            ApplyV1ToV2(data);

            CollectionAssert.AreEqual(
                new[] { "CatMage", "Barbarian", "CatKnight", "ElfArcher", "ElfGuardian", "RabbitHealer" },
                IdsOf(data.characters),
                "있던 항목은 자리를 지키고, 없던 것만 코드에 적힌 차례로 뒤에 붙습니다.");

            Assert.AreEqual(8, data.characters[0].level, "있던 항목의 값은 그대로여야 합니다.");
            Assert.AreEqual(3, data.characters[0].currentStamina);
            Assert.AreEqual(2, data.characters[1].level);
            Assert.AreEqual(0, data.characters[1].currentStamina);

            for (int i = 2; i < data.characters.Count; i++) AssertAppendedDefaults(data.characters[i]);
        }

        [Test]
        public void v1_변환은_모르는_id를_지우지도_바꾸지도_않는다()
        {
            SaveData data = V1Document(
                Character("scarecrow", 3, 7),
                Character("IceMage", 6, 2));

            ApplyV1ToV2(data);

            Assert.AreEqual(2 + 6, data.characters.Count);
            Assert.AreEqual("scarecrow", data.characters[0].characterId, "모르는 id도 자리를 지킵니다.");
            Assert.AreEqual(3, data.characters[0].level);
            Assert.AreEqual(7, data.characters[0].currentStamina);
            Assert.AreEqual("IceMage", data.characters[1].characterId);
            Assert.AreEqual(6, data.characters[1].level);
        }

        [Test]
        public void v1_변환은_대소문자를_구분한다()
        {
            // 'barbarian'은 'Barbarian'이 아니다 - 저장 키는 Ordinal 완전 일치로만 같다.
            SaveData data = V1Document(Character("barbarian", 4, 9), Character("CATMAGE", 2, 1));

            ApplyV1ToV2(data);

            Assert.AreEqual(2 + 6, data.characters.Count,
                "대소문자만 다른 항목은 그 캐릭터가 '있다'는 근거가 될 수 없습니다.");

            List<string> ids = IdsOf(data.characters);
            Assert.AreEqual("barbarian", ids[0], "원래 있던 철자를 고쳐 쓰면 안 됩니다.");
            Assert.AreEqual("CATMAGE", ids[1]);
            CollectionAssert.Contains(ids, "Barbarian");
            CollectionAssert.Contains(ids, "CatMage");
            Assert.AreEqual(4, data.characters[0].level, "원래 항목의 값도 그대로입니다.");
        }

        [Test]
        public void v1_변환은_중복_항목을_합치지도_지우지도_않는다()
        {
            SaveData data = V1Document(
                Character("CatMage", 8, 3),
                Character("CatMage", 1, 0));

            ApplyV1ToV2(data);

            List<string> ids = IdsOf(data.characters);
            Assert.AreEqual("CatMage", ids[0]);
            Assert.AreEqual("CatMage", ids[1], "중복을 합치면 어느 쪽 진행이 사라졌는지 아무도 모릅니다.");
            Assert.AreEqual(8, data.characters[0].level);
            Assert.AreEqual(1, data.characters[1].level);

            Assert.AreEqual(2 + 5, data.characters.Count, "CatMage는 이미 있으므로 덧붙지 않습니다.");
            CollectionAssert.DoesNotContain(ids.GetRange(2, ids.Count - 2), "CatMage");
        }

        [Test]
        public void v1_변환은_null_항목과_빈_id를_그대로_둔다()
        {
            SaveData data = V1Document(null, Character(string.Empty, 2, 2), Character("Barbarian", 4, 9));

            ApplyV1ToV2(data);

            Assert.IsNull(data.characters[0], "목록을 손보는 것은 변환의 일이 아닙니다(정규화의 몫).");
            Assert.AreEqual(string.Empty, data.characters[1].characterId);
            Assert.AreEqual(2, data.characters[1].level);
            Assert.AreEqual("Barbarian", data.characters[2].characterId);

            // null과 빈 id는 아무것도 가리키지 않으므로 '있다'의 근거가 아니다 - Barbarian만 이미 있다.
            Assert.AreEqual(3 + 5, data.characters.Count);
        }

        [Test]
        public void v1_변환은_다른_필드를_하나도_건드리지_않는다()
        {
            SaveData data = V1Document(Character("Barbarian", 4, 9));

            ApplyV1ToV2(data);

            Assert.AreEqual(5, data.saveRevision, "일련번호는 v1->v2가 손댈 값이 아닙니다.");
            Assert.AreEqual("2026-01-02T03:04:05.0000000Z", data.lastSavedAtUtc,
                "저장 시각도 그대로여야 합니다(v0->v1과 달리 v1에는 이미 값이 있습니다).");
            Assert.AreEqual(7, data.currentLevel);
            Assert.AreEqual(240, data.currentExp);
            Assert.AreEqual(133, data.totalKillCount);
            Assert.AreEqual(1250, data.currency);
            Assert.AreEqual(1, data.items.Count);
            Assert.AreEqual("potion", data.items[0].itemId);
            Assert.AreEqual(3, data.items[0].count);
            Assert.AreEqual(1, data.recoverySlots.Count);
            Assert.AreEqual("CatKnight", data.recoverySlots[0].characterId);
            Assert.AreEqual(2, data.recoverySlots[0].startStamina);
        }

        [Test]
        public void v1_변환은_두_번_돌려도_결과가_같다()
        {
            SaveData data = V1Document(Character("CatMage", 8, 3));

            ApplyV1ToV2(data);
            List<string> afterFirst = IdsOf(data.characters);

            ApplyV1ToV2(data);

            CollectionAssert.AreEqual(afterFirst, IdsOf(data.characters),
                "두 번 돌아도 같은 항목이 두 벌 생기면 안 됩니다.");
        }

        [Test]
        public void v1_변환은_잘못된_입력에_조용히_넘어가지_않는다()
        {
            Assert.Throws<ArgumentNullException>(() => new V1ToV2Step().Apply(null));
        }

        // ---- v0 -> v1 -> v2: 두 칸을 이어서 ----

        [Test]
        public void v0_문서는_v1을_거쳐_v2까지_한_칸씩_올라간다()
        {
            SaveLoadResult result = SaveMigrationRunner.Default.Load(LegacyJson, JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.Migrated, result.Status);
            Assert.AreEqual(0, result.FromVersion);
            Assert.AreEqual(SaveData.CurrentSaveVersion, result.ToVersion);
            Assert.AreEqual(SaveData.CurrentSaveVersion, result.Data.saveVersion);

            // v0->v1의 결과(메타데이터를 '모름'으로)와 v1->v2의 결과(여섯 캐릭터)가 <b>둘 다</b> 보여야
            // 두 칸을 실제로 거쳤다고 할 수 있다.
            Assert.AreEqual(0, result.Data.saveRevision, "v0->v1이 일련번호를 모름(0)으로 두었어야 합니다.");
            Assert.IsTrue(string.IsNullOrEmpty(result.Data.lastSavedAtUtc));

            List<string> ids = IdsOf(result.Data.characters);
            foreach (string legacyId in ExpectedLegacyIds)
            {
                CollectionAssert.Contains(ids, legacyId, "v1->v2가 여섯을 채웠어야 합니다.");
            }

            Assert.AreEqual("barbarian", ids[0], "예전 파일에 있던 항목이 앞자리를 지켜야 합니다.");
            Assert.AreEqual("scarecrow", ids[1]);
            CollectionAssert.AreEqual(new[] { "barbarian", "scarecrow", "Barbarian" }, result.Data.partyCharacterIds);
        }

        [Test]
        public void v1_문서를_읽으면_한_칸만_올라간다()
        {
            string json = $@"{{""saveVersion"":1,""saveRevision"":5,"
                          + $@"""lastSavedAtUtc"":""2026-01-02T03:04:05.0000000Z"","
                          + $@"""currentLevel"":7,""currency"":1250,"
                          + $@"""characters"":[{{""characterId"":""CatMage"",""level"":8,""currentStamina"":3}}]}}";

            SaveLoadResult result = SaveMigrationRunner.Default.Load(json, JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.Migrated, result.Status);
            Assert.AreEqual(1, result.FromVersion);
            Assert.AreEqual(SaveData.CurrentSaveVersion, result.ToVersion);
            Assert.IsTrue(result.ShouldResaveSoon, "올린 문서는 다음 명시적 저장으로 굳혀야 합니다.");

            // v1->v2는 메타데이터를 건드리지 않는다 - v0->v1이 함께 돌지 않았다는 증거이기도 하다.
            Assert.AreEqual(5, result.Data.saveRevision);
            Assert.AreEqual("2026-01-02T03:04:05.0000000Z", result.Data.lastSavedAtUtc);

            CollectionAssert.AreEqual(
                new[] { "CatMage", "Barbarian", "CatKnight", "ElfArcher", "ElfGuardian", "RabbitHealer" },
                IdsOf(result.Data.characters));
            Assert.AreEqual(8, result.Data.characters[0].level, "있던 항목의 값은 그대로입니다.");
            CollectionAssert.AreEqual(new[] { "CatMage", "Barbarian", "CatKnight" }, result.Data.partyCharacterIds);
        }

        [Test]
        public void v2_문서는_v3으로_올리되_여섯을_다시_덧붙이지_않는다()
        {
            SaveData source = V1Document(Character("CatMage", 8, 3));
            source.saveVersion = 2;
            string json = JsonUtility.ToJson(source);

            SaveLoadResult result = SaveMigrationRunner.Default.Load(json, JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.Migrated, result.Status);
            Assert.AreEqual(1, result.Data.characters.Count,
                "v2 문서에 여섯을 다시 채워 넣으면 지운 캐릭터가 되살아납니다.");
            Assert.AreEqual("CatMage", result.Data.characters[0].characterId);
            CollectionAssert.AreEqual(new[] { "CatMage" }, result.Data.partyCharacterIds);
        }

        [Test]
        public void v3_문서는_v4로_올리며_파티_순서를_바꾸지_않는다()
        {
            SaveData source = new SaveData
            {
                saveVersion = 3,
                characters = new List<CharacterSaveState>
                {
                    Character("CatKnight", 4, 9), Character("UnknownFromOldBuild", 2, 3),
                },
                partyCharacterIds = new List<string> { "UnknownFromOldBuild", "CatKnight" },
            };

            SaveLoadResult result = SaveMigrationRunner.Default.Load(JsonUtility.ToJson(source), JsonDeserializer);

            Assert.AreEqual(SaveLoadStatus.Migrated, result.Status);
            Assert.AreEqual(3, result.FromVersion);
            Assert.AreEqual(SaveData.CurrentSaveVersion, result.ToVersion);
            CollectionAssert.AreEqual(
                new[] { "UnknownFromOldBuild", "CatKnight" }, result.Data.partyCharacterIds);
            CollectionAssert.AreEqual(new[] { "CatKnight", "UnknownFromOldBuild" }, IdsOf(result.Data.characters));
        }

        [Test]
        public void v1에서_올리다_뒤_칸이_없으면_캐릭터를_덧붙인_흔적도_남지_않는다()
        {
            // 실제 두 단계를 그대로 쓰되 목표만 한 칸 더 높여 '뒤 칸 없음'을 만든다. 여기서 롤백이
            // 깨지면 여섯이 덧붙은 채로 실패 문서가 호출부에 남는다.
            SaveMigrationRunner runner = RunnerTo(3, new UnversionedToV1Step(), new V1ToV2Step());
            SaveData data = FullyPopulated();

            SaveMigrationResult result = runner.Migrate(data, 0);

            Assert.AreEqual(SaveMigrationOutcome.StepMissing, result.Outcome);
            Assert.AreEqual(2, result.ReachedVersion, "v2까지는 갔고 v2->v3이 없어 멈춥니다.");
            AssertUntouched(data, "뒤 칸이 없어 멈췄으면 문서는 시도 전과 같아야 합니다");
            Assert.AreEqual(1, data.characters.Count, "덧붙인 여섯이 호출부의 문서에 새면 안 됩니다.");
        }

        [Test]
        public void v1_변환_뒤에도_작업_사본이_모든_필드를_왕복시킨다()
        {
            // 성공 경로는 반드시 사본을 거친다. v1->v2를 실제로 거치면서도 필드가 하나도 빠지지
            // 않는지를 본다 - CopyInto에 빠진 필드가 있으면 여기서 기본값으로 되돌아간다.
            SaveData data = FullyPopulated();
            data.saveVersion = 1;

            SaveMigrationResult result = SaveMigrationRunner.Default.Migrate(data, 1);

            Assert.AreEqual(SaveMigrationOutcome.Migrated, result.Outcome);
            Assert.AreEqual(SaveData.CurrentSaveVersion, data.saveVersion);
            Assert.AreEqual(41, data.saveRevision);
            Assert.AreEqual("2026-01-02T03:04:05.0000000Z", data.lastSavedAtUtc);
            Assert.AreEqual(7, data.currentLevel);
            Assert.AreEqual(240, data.currentExp);
            Assert.AreEqual(133, data.totalKillCount);
            Assert.AreEqual(1250, data.currency);
            Assert.AreEqual(1, data.items.Count);
            Assert.AreEqual("potion", data.items[0].itemId);
            Assert.AreEqual(3, data.items[0].count);
            Assert.AreEqual(1, data.recoverySlots.Count);
            Assert.AreEqual("barbarian", data.recoverySlots[0].characterId);
            Assert.IsTrue(data.recoverySlots[0].completionNotified);
            Assert.AreEqual(1, data.recruitmentCycles.Count);
            Assert.AreEqual("Inn_Normal_Access", data.recruitmentCycles[0].recruitmentAccessId);
            Assert.AreEqual("2026-01-02T04:05:05.0000000Z", data.recruitmentCycles[0].readyAtUtc);
            Assert.AreEqual(1, data.purificationSlots.Count,
                "v5->v6은 이전의 비정식 정화 슬롯 대신 빈 기본 슬롯 하나를 만듭니다.");
            Assert.IsFalse(data.purificationSlots[0].HasCharacter);

            // FullyPopulated의 'barbarian'은 여섯과 다른 키이므로 1 + 6이다.
            Assert.AreEqual(1 + 6, data.characters.Count);
            Assert.AreEqual("barbarian", data.characters[0].characterId);
            Assert.AreEqual(4, data.characters[0].level);
        }

        [Test]
        public void v5_문서는_기존_진행을_보존하며_빈_정화_슬롯을_갖춘_현재_버전이_된다()
        {
            SaveData data = FullyPopulated();
            data.saveVersion = 5;
            data.characters[0].currentCorruption = 77.25d;

            SaveMigrationResult result = SaveMigrationRunner.Default.Migrate(data, 5);

            Assert.AreEqual(SaveMigrationOutcome.Migrated, result.Outcome);
            Assert.AreEqual(SaveData.CurrentSaveVersion, result.ReachedVersion);
            Assert.AreEqual(SaveData.CurrentSaveVersion, data.saveVersion);
            Assert.AreEqual(77.25d, data.characters[0].currentCorruption, 0.0000001d);
            CollectionAssert.AreEqual(new[] { "barbarian" }, data.partyCharacterIds);
            Assert.AreEqual("barbarian", data.recoverySlots[0].characterId);
            Assert.AreEqual("1", data.buildingConstructions[0].buildingId);
            Assert.AreEqual("Inn_Normal_Access", data.recruitmentCycles[0].recruitmentAccessId);
            Assert.AreEqual("potion", data.items[0].itemId);
            Assert.AreEqual(1250, data.currency);
            Assert.AreEqual(1, data.purificationSlots.Count);
            Assert.IsFalse(data.purificationSlots[0].HasCharacter,
                "v5에 우연히 있던 비정식 정화 슬롯은 v6의 빈 기본 슬롯으로 교체합니다.");
        }

        [Test]
        public void v5에서_v6으로_올리는_단계는_null_문서를_거부한다()
        {
            Assert.Throws<ArgumentNullException>(() => new V5ToV6Step().Apply(null));
        }

        [Test]
        public void v6보다_새로운_문서는_여전히_읽지도_고치지도_않는다()
        {
            SaveData data = FullyPopulated();

            SaveMigrationResult result = SaveMigrationRunner.Default.Migrate(
                data, SaveData.CurrentSaveVersion + 1);

            Assert.AreEqual(SaveMigrationOutcome.FutureVersion, result.Outcome);
            AssertUntouched(data, "미래 버전은 읽지도 고치지도 않습니다");
            Assert.AreEqual(1, data.characters.Count, "미래 버전 문서에 여섯을 덧붙이면 안 됩니다.");
        }
    }
}
