using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Common;
using NUnit.Framework;
using UnityEngine;

namespace CommonEditor.Tests
{
    /// <summary>
    /// SaveSystem 통합 시험. 아래 계층(저장소/훑기/변환/정규화)은 각자 시험이 있으므로, 여기서
    /// 확인하는 것은 그것들을 <b>어떤 순서로 엮었고 어떤 상황에서 저장을 허락하는가</b> 하나뿐이다.
    ///
    /// <b>실제 저장 파일은 어떤 시험도 건드리지 않는다.</b> 대부분은 메모리 위의 가짜 저장소를 끼워
    /// 넣고, 진짜 파일 동작(원자적 교체가 v0 원본을 백업에 남기는가)을 봐야 하는 두 시험만
    /// 시스템 임시 폴더에 만든 <see cref="LocalFileSaveStorage"/>를 쓴다 - persistentDataPath는
    /// 읽지도 않는다.
    ///
    /// 주입은 전부 리플렉션으로 한다. 저장 하나 때문에 <see cref="SaveSystem.Data"/>,
    /// <see cref="SaveSystem.Save"/>, <see cref="SaveSystem.LoadedFromFile"/>,
    /// <see cref="SaveSystem.LoadStatus"/> 말고 다른 것을 공개 API로 내보내고 싶지 않기 때문이며,
    /// 이름이 바뀌면 <see cref="SetUp"/>에서 곧바로 실패하므로 조용히 통과하는 경로는 없다.
    /// </summary>
    public sealed class SaveSystemIntegrationTests
    {
        private const string LegacyV0Json =
            @"{""currentLevel"":7,""currentExp"":240,""totalKillCount"":133,""currency"":1250}";

        /// <summary>
        /// v1 파일. 캐릭터 항목이 하나만 있는 문서다 - v1에서 쓸 수 있는 캐릭터를 정한 것은 씬의
        /// 직렬화된 로스터 목록이었고 저장 목록은 그 목록을 따라 만들어진 상태 기록이었으므로,
        /// 로스터 구성이 달랐던 시점에 저장된 문서는 이렇게 일부만 담고 있을 수 있다. 그 문서를 v2로
        /// 올릴 때 나머지가 미보유로 사라지지 않는지를 이 상수로 확인한다.
        /// </summary>
        private const string LegacyV1Json =
            @"{""saveVersion"":1,""saveRevision"":5,""lastSavedAtUtc"":""2026-01-02T03:04:05.0000000Z"","
            + @"""currentLevel"":7,""currency"":1250,"
            + @"""characters"":[{""characterId"":""CatMage"",""level"":8,""currentStamina"":3}]}";

        private static readonly DateTime FixedNow = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        private const string FixedNowText = "2026-05-06T07:08:09.0000000Z";

        private static readonly MethodInfo ConfigureMethod = typeof(SaveSystem).GetMethod(
            "ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo DataField = typeof(SaveSystem).GetField(
            "data", BindingFlags.NonPublic | BindingFlags.Static);

        private FakeStorage fake;
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(ConfigureMethod,
                "SaveSystem.ConfigureForTests를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요. " +
                "그대로 두면 시험이 실제 저장 파일을 읽고 씁니다.");
            Assert.IsNotNull(DataField,
                "SaveSystem.data를 찾지 못했습니다 - 이름이 바뀌었다면 이 시험도 함께 고치세요.");

            fake = new FakeStorage();
            Configure(fake, SaveMigrationRunner.Default);
        }

        [TearDown]
        public void TearDown()
        {
            // 실제 저장소/시계/표로 되돌린다 - 다음 시험 파일로 이 시험의 주입이 새면 안 된다.
            ConfigureMethod.Invoke(null, new object[] { null, null, null });

            if (tempRoot != null && Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, true); }
                catch (Exception e) { Debug.LogWarning($"시험 폴더를 지우지 못했습니다: {e.Message}"); }
            }

            tempRoot = null;
        }

        private static void Configure(ISaveStorage storage, SaveMigrationRunner runner)
        {
            ConfigureMethod.Invoke(null, new object[] { storage, (Func<DateTime>)(() => FixedNow), runner });
        }

        /// <summary>진짜 파일 동작을 봐야 하는 시험만 쓰는 임시 폴더 저장소.</summary>
        private LocalFileSaveStorage UseTemporaryDirectoryStorage()
        {
            tempRoot = Path.Combine(
                Path.GetTempPath(), "desktopRPG-SaveSystemTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            LocalFileSaveStorage real = new LocalFileSaveStorage(
                new SavePathProvider(tempRoot), SaveProfile.LocalPrimary);
            Configure(real, SaveMigrationRunner.Default);
            return real;
        }

        // ---- 저장 파일이 없을 때 ----

        [Test]
        public void 저장_파일이_없으면_새_게임이고_LoadedFromFile은_false다()
        {
            fake.ReadResult = SaveReadResult.Missing("fake://primary");

            Assert.IsNotNull(SaveSystem.Data);
            Assert.AreEqual(SaveLoadStatus.NewGame, SaveSystem.LoadStatus);
            Assert.IsFalse(SaveSystem.LoadedFromFile,
                "정말로 저장한 적이 없을 때만 false여야 합니다 - 호출부는 이 값으로 새 게임 시작값을 채웁니다.");
            Assert.AreEqual(SaveData.CurrentSaveVersion, SaveSystem.Data.saveVersion);
            Assert.AreEqual(0, fake.WriteCalls, "불러오기는 파일을 쓰지 않습니다.");
            Assert.AreEqual(0, fake.QuarantineCalls, "파일이 없는 것은 손상이 아닙니다.");
        }

        // ---- 현재 버전 파일 ----

        [Test]
        public void 현재_버전_파일은_그대로_읽고_LoadedFromFile은_true다()
        {
            fake.ReadResult = SaveReadResult.Loaded("fake://primary", CurrentVersionJson(level: 5, currency: 900));

            Assert.IsTrue(SaveSystem.LoadedFromFile);
            Assert.AreEqual(SaveLoadStatus.Loaded, SaveSystem.LoadStatus,
                "형식을 바꾼 것이 없으므로 '그대로 읽음'이어야 합니다.");
            Assert.AreEqual(5, SaveSystem.Data.currentLevel);
            Assert.AreEqual(900, SaveSystem.Data.currency);
            Assert.AreEqual(0, fake.QuarantineCalls);
            Assert.AreEqual(0, fake.WriteCalls);
        }

        // ---- v0 파일 ----

        [Test]
        public void v0_파일은_올려서_읽되_그_자리에서_쓰지_않는다()
        {
            fake.ReadResult = SaveReadResult.Loaded("fake://primary", LegacyV0Json);

            Assert.IsTrue(SaveSystem.LoadedFromFile, "예전 파일도 엄연히 '읽어 온 파일'입니다.");
            Assert.AreEqual(SaveLoadStatus.Migrated, SaveSystem.LoadStatus,
                "호출부는 이 값을 보고 '올렸으니 곧 한 번 저장해 두라'를 판단합니다.");
            Assert.AreEqual(7, SaveSystem.Data.currentLevel, "변환은 진행 값을 바꾸지 않습니다.");
            Assert.AreEqual(1250, SaveSystem.Data.currency);
            Assert.AreEqual(SaveData.CurrentSaveVersion, SaveSystem.Data.saveVersion);
            Assert.AreEqual(0, fake.WriteCalls,
                "켜기만 한 사용자의 파일을 바꿔 놓지 않도록, 올린 결과는 다음 명시적 Save까지 기다립니다.");
        }

        [Test]
        public void 올린_뒤_명시적_Save가_현재_버전을_쓰고_v0_원본을_백업에_남긴다()
        {
            LocalFileSaveStorage real = UseTemporaryDirectoryStorage();
            File.WriteAllText(real.PrimaryPath, LegacyV0Json);

            Assert.AreEqual(7, SaveSystem.Data.currentLevel);
            Assert.IsFalse(File.Exists(real.BackupPath), "아직 아무것도 쓰지 않았으므로 백업도 없습니다.");

            Assert.IsTrue(SaveSystem.Save());

            Assert.AreEqual(SaveData.CurrentSaveVersion,
                SaveVersionProbe.Probe(File.ReadAllText(real.PrimaryPath)).Version,
                "주 파일은 이제 현재 버전입니다.");
            Assert.AreEqual(LegacyV0Json, File.ReadAllText(real.BackupPath),
                "원자적 교체가 v0 원본을 백업 자리에 그대로 남겨야 합니다 - 되돌아갈 곳입니다.");
        }

        [Test]
        public void v1_파일은_올려서_읽되_그_자리에서_쓰지_않는다()
        {
            fake.ReadResult = SaveReadResult.Loaded("fake://primary", LegacyV1Json);

            Assert.IsTrue(SaveSystem.LoadedFromFile);
            Assert.AreEqual(SaveLoadStatus.Migrated, SaveSystem.LoadStatus);
            Assert.AreEqual(SaveData.CurrentSaveVersion, SaveSystem.Data.saveVersion);

            // v1->v2가 그 시절 여섯을 채운다. 파일에 이미 있던 'CatMage'는 앞자리를 지킨다.
            Assert.AreEqual("CatMage", SaveSystem.Data.characters[0].characterId);
            Assert.AreEqual(8, SaveSystem.Data.characters[0].level, "변환은 있던 값을 바꾸지 않습니다.");
            // 기대값은 이 시험이 독립적으로 들고 있다 - 변환의 상수를 그대로 가져다 비교하면 그 상수를
            // 잘못 고쳐도 시험이 함께 따라가 통과한다.
            CollectionAssert.AreEqual(
                new[] { "CatMage", "Barbarian", "CatKnight", "ElfArcher", "ElfGuardian", "RabbitHealer" },
                IdsOf(SaveSystem.Data.characters),
                "CatMage는 여섯 중 하나이므로 나머지 다섯만 정해진 차례로 덧붙습니다.");
            CollectionAssert.AreEqual(
                new[] { "CatMage", "Barbarian", "CatKnight" }, SaveSystem.Data.partyCharacterIds,
                "v3 초기 파티는 v2 최종 보유 순서에서 첫 세 명만 고릅니다.");

            Assert.AreEqual(0, fake.WriteCalls,
                "켜기만 한 사용자의 파일을 바꿔 놓지 않도록, 올린 결과는 다음 명시적 Save까지 기다립니다.");
        }

        [Test]
        public void 올린_뒤_명시적_Save가_v3를_쓰고_v1_원본을_백업에_남긴다()
        {
            LocalFileSaveStorage real = UseTemporaryDirectoryStorage();
            File.WriteAllText(real.PrimaryPath, LegacyV1Json);

            Assert.AreEqual(SaveLoadStatus.Migrated, SaveSystem.LoadStatus, "v1 파일은 한 칸 올라갑니다.");
            Assert.IsFalse(File.Exists(real.BackupPath), "아직 아무것도 쓰지 않았으므로 백업도 없습니다.");

            Assert.IsTrue(SaveSystem.Save());

            Assert.AreEqual(SaveData.CurrentSaveVersion,
                SaveVersionProbe.Probe(File.ReadAllText(real.PrimaryPath)).Version,
                "주 파일은 이제 v3입니다.");
            Assert.AreEqual(LegacyV1Json, File.ReadAllText(real.BackupPath),
                "원자적 교체가 v1 원본을 백업 자리에 그대로 남겨야 합니다 - 되돌아갈 곳입니다.");
        }

        // ---- 손상된 파일 ----

        [Test]
        public void 손상된_파일은_격리에_성공하면_기본값으로_진행하고_저장할_수_있다()
        {
            fake.ReadResult = SaveReadResult.Loaded("fake://primary", @"{""currentLevel"":");
            fake.QuarantineSucceeds = true;

            Assert.IsNotNull(SaveSystem.Data, "막힌 상황에서도 Data는 null이 아니어야 합니다.");
            Assert.IsTrue(SaveSystem.LoadedFromFile, "파일은 있었습니다 - 새 게임이 아닙니다.");
            Assert.AreEqual(SaveLoadStatus.CorruptFallback, SaveSystem.LoadStatus);
            Assert.AreEqual(1, SaveSystem.Data.currentLevel, "손상본 대신 기본값 문서로 진행합니다.");
            Assert.AreEqual(1, fake.QuarantineCalls, "손상본은 지우지 않고 옆으로 치웁니다.");

            Assert.IsTrue(SaveSystem.Save(),
                "손상본을 안전하게 치웠으면 주 파일 자리는 비어 있으므로 저장해도 잃을 것이 없습니다.");
            Assert.AreEqual(1, fake.WriteCalls);
        }

        [Test]
        public void 손상된_파일을_격리하지_못하면_저장을_막는다()
        {
            fake.ReadResult = SaveReadResult.Loaded("fake://primary", @"{""currentLevel"":");
            fake.QuarantineSucceeds = false;

            Assert.IsNotNull(SaveSystem.Data);
            Assert.AreEqual(SaveLoadStatus.CorruptFallback, SaveSystem.LoadStatus,
                "격리에 실패해도 진행 갈래 자체는 '기본값으로 진행'입니다 - 저장 차단은 상태가 아니라 Save()가 답합니다.");
            Assert.AreEqual(1, fake.QuarantineCalls);

            Assert.IsFalse(SaveSystem.Save(),
                "손상본이 아직 주 파일 자리에 있습니다 - 사용자가 직접 복구할 마지막 수단을 덮어쓸 수 없습니다.");
            Assert.AreEqual(0, fake.WriteCalls, "막혔으면 쓰기를 시도조차 하지 않습니다.");
        }

        [Test]
        public void 읽을_수_없는_주_파일도_격리_경로를_탄다()
        {
            // 0바이트 파일이나 권한 오류. "파일 없음"과 달리 새 게임으로 시작하면 안 된다.
            fake.ReadResult = SaveReadResult.Unreadable("fake://primary", "저장 파일이 비어 있습니다.");

            Assert.IsNotNull(SaveSystem.Data);
            Assert.IsTrue(SaveSystem.LoadedFromFile);
            Assert.AreEqual(SaveLoadStatus.CorruptFallback, SaveSystem.LoadStatus);
            Assert.AreEqual(1, fake.QuarantineCalls);
            Assert.IsTrue(SaveSystem.Save());
        }

        // ---- 미래 버전 ----

        [Test]
        public void 미래_버전_파일은_역직렬화도_저장도_하지_않는다()
        {
            string json = $@"{{""saveVersion"":{SaveData.CurrentSaveVersion + 1},""currentLevel"":77}}";
            fake.ReadResult = SaveReadResult.Loaded("fake://primary", json);

            Assert.IsNotNull(SaveSystem.Data, "막혀도 게임은 돌아야 합니다 - null이면 호출부가 통째로 죽습니다.");
            Assert.IsTrue(SaveSystem.LoadedFromFile);
            Assert.AreEqual(SaveLoadStatus.FutureVersionBlocked, SaveSystem.LoadStatus);
            Assert.AreNotEqual(77, SaveSystem.Data.currentLevel,
                "미래 형식은 역직렬화조차 하지 않습니다 - 값이 새어 나왔다면 순서가 틀린 것입니다.");

            Assert.IsFalse(SaveSystem.Save());
            Assert.AreEqual(0, fake.WriteCalls, "파일을 건드리지 않습니다.");
            Assert.AreEqual(0, fake.QuarantineCalls, "미래 버전은 손상이 아니므로 격리하지도 않습니다.");
        }

        // ---- 변환 실패 ----

        [Test]
        public void 올릴_단계가_없으면_저장을_막고_파일을_건드리지_않는다()
        {
            // 실제 표에는 구멍이 없다(있으면 안 된다). 그 상황을 만들려면 표를 갈아 끼우는 수밖에 없다.
            Configure(fake, new SaveMigrationRunner(new ISaveMigrationStep[0], targetVersion: 3));
            fake.ReadResult = SaveReadResult.Loaded("fake://primary", LegacyV0Json);

            Assert.IsNotNull(SaveSystem.Data);
            Assert.IsTrue(SaveSystem.LoadedFromFile);
            Assert.AreEqual(SaveLoadStatus.MigrationFailed, SaveSystem.LoadStatus);
            Assert.IsFalse(SaveSystem.Save());
            Assert.AreEqual(0, fake.WriteCalls);
            Assert.AreEqual(0, fake.QuarantineCalls);
        }

        [Test]
        public void 변환_단계가_터지면_저장을_막고_파일을_건드리지_않는다()
        {
            Configure(fake, new SaveMigrationRunner(new ISaveMigrationStep[] { new ThrowingStep() }, 1));
            fake.ReadResult = SaveReadResult.Loaded("fake://primary", LegacyV0Json);

            Assert.IsNotNull(SaveSystem.Data);
            Assert.AreEqual(SaveLoadStatus.MigrationFailed, SaveSystem.LoadStatus);
            Assert.IsFalse(SaveSystem.Save());
            Assert.AreEqual(0, fake.WriteCalls);
            Assert.AreEqual(0, fake.QuarantineCalls);
        }

        // ---- 저장 ----

        [Test]
        public void 저장에_성공하면_일련번호가_오르고_시각이_찍힌다()
        {
            fake.ReadResult = SaveReadResult.Missing("fake://primary");

            Assert.IsTrue(SaveSystem.Save());

            Assert.AreEqual(1, SaveSystem.Data.saveRevision, "저장할 때마다 하나씩 오릅니다.");
            Assert.AreEqual(FixedNowText, SaveSystem.Data.lastSavedAtUtc);
            Assert.AreEqual(SaveData.CurrentSaveVersion, SaveSystem.Data.saveVersion);
            Assert.AreEqual(1, fake.WriteCalls);
            Assert.IsNotNull(fake.LastWrittenText);

            Assert.IsTrue(SaveSystem.Save());
            Assert.AreEqual(2, SaveSystem.Data.saveRevision);
        }

        [Test]
        public void 저장소가_실패하면_메타데이터를_되돌린다()
        {
            fake.ReadResult = SaveReadResult.Missing("fake://primary");
            fake.WriteBehaviour = _ => SaveWriteResult.Failed("일부러 실패");

            SaveData live = SaveSystem.Data;
            Assert.IsFalse(SaveSystem.Save());

            Assert.AreEqual(0, live.saveRevision,
                "쓰지 못했는데 번호만 오르면 파일과 메모리 중 어느 쪽이 최신인지 가릴 수 없게 됩니다.");
            Assert.IsTrue(string.IsNullOrEmpty(live.lastSavedAtUtc));
        }

        [Test]
        public void 저장소가_예외를_던져도_메타데이터를_되돌린다()
        {
            fake.ReadResult = SaveReadResult.Missing("fake://primary");
            fake.ThrowOnWrite = true;

            SaveData live = SaveSystem.Data;
            Assert.IsFalse(SaveSystem.Save(), "예외는 밖으로 나가지 않고 false가 됩니다.");
            Assert.AreEqual(0, live.saveRevision);
            Assert.IsTrue(string.IsNullOrEmpty(live.lastSavedAtUtc));
        }

        [Test]
        public void 저장소가_차단_상태면_저장은_실패하고_메타데이터도_그대로다()
        {
            fake.ReadResult = SaveReadResult.Missing("fake://primary");
            fake.WriteBehaviour = _ => SaveWriteResult.Blocked("저장소가 막혀 있습니다");

            SaveData live = SaveSystem.Data;
            Assert.IsFalse(SaveSystem.Save());
            Assert.AreEqual(0, live.saveRevision);
        }

        [Test]
        public void 두_번째_저장이_실패해도_첫_저장의_값까지_되돌리지는_않는다()
        {
            fake.ReadResult = SaveReadResult.Missing("fake://primary");

            Assert.IsTrue(SaveSystem.Save());
            fake.WriteBehaviour = _ => SaveWriteResult.Failed("두 번째만 실패");
            Assert.IsFalse(SaveSystem.Save());

            Assert.AreEqual(1, SaveSystem.Data.saveRevision, "되돌릴 곳은 직전 성공 지점입니다.");
            Assert.AreEqual(FixedNowText, SaveSystem.Data.lastSavedAtUtc);
        }

        // ---- 왕복 ----

        [Test]
        public void 저장한_값은_다시_켜도_그대로다()
        {
            LocalFileSaveStorage real = UseTemporaryDirectoryStorage();

            SaveSystem.Data.currentLevel = 12;
            SaveSystem.Data.currency = 4321;
            SaveSystem.Data.items.Add(new InventoryItemState { itemId = "potion", count = 3 });

            // 캐릭터 진행도 세 칸을 <b>모두 0이 아닌 값</b>으로 넣는다 - 경험치가 저장/직렬화 경로 어딘가에서
            // 빠지면 기본값 0으로 되돌아오므로, 0이 아닌 값이어야 그 사고가 보인다.
            SaveSystem.Data.characters.Add(new CharacterSaveState
            {
                characterId = "CatKnight",
                level = 5,
                currentExp = 7,
                currentStamina = 21,
            });

            Assert.IsTrue(SaveSystem.Save());

            // "다시 켠" 상태: 메모리 문서를 비우고 같은 폴더에서 읽는다.
            Configure(real, SaveMigrationRunner.Default);

            Assert.IsTrue(SaveSystem.LoadedFromFile);
            Assert.AreEqual(SaveLoadStatus.Loaded, SaveSystem.LoadStatus,
                "방금 쓴 파일은 현재 버전이므로 변환 없이 그대로 읽혀야 합니다.");
            Assert.AreEqual(12, SaveSystem.Data.currentLevel);
            Assert.AreEqual(4321, SaveSystem.Data.currency);
            Assert.AreEqual(1, SaveSystem.Data.items.Count);
            Assert.AreEqual("potion", SaveSystem.Data.items[0].itemId);
            Assert.AreEqual(1, SaveSystem.Data.saveRevision);
            Assert.AreEqual(FixedNowText, SaveSystem.Data.lastSavedAtUtc);

            Assert.AreEqual(1, SaveSystem.Data.characters.Count);
            CharacterSaveState character = SaveSystem.Data.characters[0];
            Assert.AreEqual("CatKnight", character.characterId);
            Assert.AreEqual(5, character.level);
            Assert.AreEqual(7, character.currentExp,
                "경험치가 실제 파일을 한 번 왕복해도 그대로여야 합니다.");
            Assert.AreEqual(21, character.currentStamina);
        }

        [Test]
        public void v3_저장_형식_번호가_실제_파일에도_기록된다()
        {
            Assert.AreEqual(3, SaveData.CurrentSaveVersion,
                "v2의 보유 목록에서 역사적 초기 파티를 만들려면 v3 마이그레이션이 필요합니다.");

            LocalFileSaveStorage real = UseTemporaryDirectoryStorage();
            SaveSystem.Data.characters.Add(new CharacterSaveState
            {
                characterId = "CatKnight", level = 3, currentExp = 4, currentStamina = 9,
            });

            Assert.IsTrue(SaveSystem.Save());

            Assert.AreEqual(3, SaveVersionProbe.Probe(File.ReadAllText(real.PrimaryPath)).Version,
                "실제로 쓴 파일에도 3이 적혀 있어야 합니다.");
        }

        // ---- 기존 호출부와의 계약 ----

        [Test]
        public void 기존_공개_API와_비공개_필드가_그대로다()
        {
            PropertyInfo dataProperty = typeof(SaveSystem).GetProperty(
                "Data", BindingFlags.Public | BindingFlags.Static);
            PropertyInfo loadedProperty = typeof(SaveSystem).GetProperty(
                "LoadedFromFile", BindingFlags.Public | BindingFlags.Static);
            MethodInfo saveMethod = typeof(SaveSystem).GetMethod(
                "Save", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);

            Assert.IsNotNull(dataProperty, "PlayerProgress/CharacterRoster/InventoryManager가 씁니다.");
            Assert.AreEqual(typeof(SaveData), dataProperty.PropertyType);
            Assert.IsNotNull(loadedProperty, "PlayerProgress가 새 게임 여부를 이 값으로 가립니다.");
            Assert.AreEqual(typeof(bool), loadedProperty.PropertyType);
            Assert.IsNotNull(saveMethod, "인자 없는 Save()여야 합니다.");
            Assert.AreEqual(typeof(bool), saveMethod.ReturnType);

            PropertyInfo statusProperty = typeof(SaveSystem).GetProperty(
                "LoadStatus", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(statusProperty,
                "런타임이 여섯 갈래를 구분하려면 이 프로퍼티가 필요합니다.");
            Assert.AreEqual(typeof(SaveLoadStatus), statusProperty.PropertyType);
            Assert.IsNull(statusProperty.GetSetMethod(), "읽기 전용이어야 합니다.");

            Assert.IsNotNull(DataField, "DefeatRewardTests가 이 필드로 실제 저장 파일 접근을 막습니다.");
            Assert.AreEqual(typeof(SaveData), DataField.FieldType);
        }

        [Test]
        public void 문서를_직접_끼워_넣으면_저장소를_아예_건드리지_않는다()
        {
            // DefeatRewardTests가 기대는 성질 그대로다 - 문서가 이미 있으면 파일을 읽지 않는다.
            DataField.SetValue(null, new SaveData { currency = 555 });

            Assert.AreEqual(555, SaveSystem.Data.currency);
            Assert.AreEqual(0, fake.ReadPrimaryCalls, "메모리에 문서가 있으면 파일을 읽을 이유가 없습니다.");
        }

        // ---- 시험용 저장소/단계 ----

        /// <summary>메모리 위의 저장소. 읽기 결과와 쓰기/격리 실패를 그대로 주입한다.</summary>
        private sealed class FakeStorage : ISaveStorage
        {
            public SaveReadResult ReadResult = SaveReadResult.Missing("fake://primary");
            public SaveReadResult BackupResult = SaveReadResult.Missing("fake://backup");

            public bool QuarantineSucceeds = true;
            public bool ThrowOnWrite;
            public Func<string, SaveWriteResult> WriteBehaviour;

            public int ReadPrimaryCalls;
            public int WriteCalls;
            public int QuarantineCalls;
            public string LastWrittenText;

            public bool WritesBlocked { get; private set; }

            public string BlockedReason { get; private set; }

            public SaveReadResult ReadPrimary()
            {
                ReadPrimaryCalls++;
                return ReadResult;
            }

            public SaveReadResult ReadBackup() => BackupResult;

            public SaveWriteResult Write(string text)
            {
                WriteCalls++;
                LastWrittenText = text;

                if (ThrowOnWrite) throw new IOException("일부러 터뜨린 쓰기");

                return WriteBehaviour != null ? WriteBehaviour(text) : SaveWriteResult.Written(true);
            }

            public SaveQuarantineResult QuarantinePrimary(string reason)
            {
                QuarantineCalls++;

                if (QuarantineSucceeds) return SaveQuarantineResult.Moved("fake://corrupted/primary");

                WritesBlocked = true;
                BlockedReason = "격리 실패(주입)";
                return SaveQuarantineResult.Failed(BlockedReason);
            }
        }

        private sealed class ThrowingStep : ISaveMigrationStep
        {
            public int FromVersion => 0;

            public int ToVersion => 1;

            public void Apply(SaveData data) => throw new InvalidOperationException("일부러 실패");
        }

        private static List<string> IdsOf(List<CharacterSaveState> characters)
        {
            List<string> ids = new List<string>();
            foreach (CharacterSaveState state in characters) ids.Add(state == null ? null : state.characterId);
            return ids;
        }

        private static string CurrentVersionJson(int level, int currency)
        {
            return $@"{{""saveVersion"":{SaveData.CurrentSaveVersion},""saveRevision"":0,"
                   + $@"""currentLevel"":{level},""currency"":{currency}}}";
        }
    }
}
