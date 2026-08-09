using System;
using System.IO;
using Common;
using NUnit.Framework;
using UnityEngine;

namespace CommonEditor.Tests
{
    /// <summary>
    /// 로컬 저장소의 안전 규칙 시험. 확인하려는 것은 값이 아니라 <b>파일이 놓이는 순서</b>다 -
    /// 어떤 단계에서 실패해도 이미 저장돼 있던 파일이 살아남는가, 백업은 항상 하나인가, 끝나지 않은
    /// 쓰기의 흔적이 다음 실행에 남는가, 손상본을 보존하지 못하면 저장이 멈추는가.
    ///
    /// <b>실제 저장 폴더는 읽기 위해서도 건드리지 않는다</b> - 이 파일에는 persistentDataPath도,
    /// 그것을 집어 오는 <see cref="SavePathProvider.CreatePersistentData"/>도, 기본
    /// <see cref="LocalFileSaveStorage"/> 생성자도 나오지 않는다. 매 시험은 시스템 임시 폴더 아래에
    /// 새로 만든 폴더만 주입해 쓰고 끝나면 지운다.
    ///
    /// 그래서 "기존 사용자 파일과 같은 자리에 저장되는가"는 실제 경로를 만들어 비교하는 대신 규칙으로
    /// 확인한다 - 경로가 <b>저장 폴더 + 파일 이름</b>만으로 정해지고 그 파일 이름이
    /// playerprogress.json으로 고정돼 있으면, 기본 생성자가 넣는 폴더가 무엇이든 결과는 예전 경로다.
    /// 기본 생성자와 그 폴더의 연결은 코드 구조(<see cref="LocalFileSaveStorage"/>의 기본 생성자)가
    /// 지킨다.
    ///
    /// 실패는 흉내내지 않고 <b>파일 시스템으로 진짜로 일으킨다</b> - 임시본/백업/격리 폴더가 놓일
    /// 자리에 폴더나 파일을 미리 만들어 두면 그 경로에 쓰는 동작이 실제로 실패한다. 가짜 저장소를
    /// 끼워 넣는 것과 달리 이 방식은 File.Replace 같은 실제 API의 동작까지 함께 확인한다.
    /// </summary>
    public sealed class SaveStorageTests
    {
        private const string GoodJson = "{\"currentLevel\":7}";
        private const string NewerJson = "{\"currentLevel\":8}";
        private const string CorruptJson = "{\"currentLevel\":";

        private string root;
        private SaveProfile profile;
        private SavePathProvider paths;
        private LocalFileSaveStorage storage;

        [SetUp]
        public void SetUp()
        {
            string testRootParent = Path.Combine(Path.GetTempPath(), "desktopRPG-SaveStorageTests");
            root = Path.Combine(testRootParent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            Assert.IsTrue(
                root.StartsWith(testRootParent, StringComparison.Ordinal) && Directory.Exists(root),
                "모든 시험은 시스템 임시 폴더 아래에서만 돌아야 합니다.");

            // 실제 프로필(local/primary)을 그대로 쓴다 - 식별자는 위치에 아무 영향을 주지 않으므로
            // 폴더만 임시 폴더로 바꿔 끼우면 사용자 파일과 완전히 분리된다.
            profile = SaveProfile.LocalPrimary;
            paths = new SavePathProvider(root);
            storage = new LocalFileSaveStorage(paths, profile);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"시험 폴더를 지우지 못했습니다: {e.Message}");
            }
        }

        // ---- 경로 규칙 ----

        [Test]
        public void LocalPrimaryProfile_IsTheLogicalPrimarySlot()
        {
            Assert.AreEqual("primary", SaveProfile.LocalPrimary.Slot, "논리 슬롯 이름은 primary입니다.");
            Assert.AreEqual("local", SaveProfile.LocalPrimary.Backend);
            Assert.AreEqual("local/primary", SaveProfile.LocalPrimary.Id);
            Assert.IsTrue(SaveProfile.LocalPrimary.IsPrimary);
        }

        [Test]
        public void LocalPrimaryProfile_StillUsesTheLegacyPlayerProgressFileName()
        {
            // 실제 저장 폴더는 이 시험이 다루지 않는다(읽기 전용 비교조차 하지 않는다). 대신 "폴더 +
            // 파일 이름"이라는 규칙과 그 파일 이름을 고정해 두면, 기본 생성자가 어느 폴더를 넣든
            // 결과 경로가 기존 사용자 파일과 같다는 것이 따라 나온다.
            Assert.AreEqual("playerprogress.json", SaveProfile.LocalPrimary.FileName,
                "파일 이름이 바뀌면 이미 저장된 사용자 진행도가 '없는 파일'이 됩니다.");

            Assert.AreEqual(
                Path.Combine(root, "playerprogress.json"),
                paths.PrimaryPath(SaveProfile.LocalPrimary),
                "주 파일 경로는 저장 폴더와 파일 이름만으로 정해져야 합니다.");
        }

        [Test]
        public void ProfileIdentity_NeverBecomesAFolderInThePath()
        {
            string primaryPath = paths.PrimaryPath(SaveProfile.LocalPrimary);

            Assert.AreEqual(Path.Combine(root, "playerprogress.json"), primaryPath,
                "local/primary는 이름일 뿐이라 하위 폴더가 되면 안 됩니다.");
            Assert.AreEqual(root, Path.GetDirectoryName(primaryPath));
            StringAssert.DoesNotContain(
                Path.Combine("local", "primary"), primaryPath,
                "식별자를 폴더로 만들면 기존 사용자 저장 파일이 '없는 파일'이 됩니다.");

            // 식별자가 달라도 파일 이름이 같으면 같은 물리 경로다 - 위치를 정하는 것은 파일 이름뿐이다.
            var otherIdentity = new SaveProfile("cloud", "slot2", "playerprogress.json");
            Assert.AreEqual(primaryPath, paths.PrimaryPath(otherIdentity));
        }

        [Test]
        public void AllPaths_StayInsideTheInjectedDirectory()
        {
            Assert.AreEqual(root, Path.GetDirectoryName(storage.PrimaryPath));
            Assert.AreEqual(root, Path.GetDirectoryName(storage.BackupPath),
                "백업은 주 파일과 같은 폴더에 있어야 합니다.");
            Assert.AreEqual(root, Path.GetDirectoryName(storage.TemporaryPath),
                "임시본이 다른 볼륨에 있으면 원자적 교체가 성립하지 않습니다.");

            Assert.AreNotEqual(storage.PrimaryPath, storage.BackupPath);
            Assert.AreNotEqual(storage.PrimaryPath, storage.TemporaryPath);
            Assert.AreNotEqual(storage.BackupPath, storage.TemporaryPath);
        }

        [Test]
        public void SaveProfile_RejectsFileNamesThatEscapeTheSaveDirectory()
        {
            Assert.Throws<ArgumentException>(() => new SaveProfile("local", "primary", "../playerprogress.json"));
            Assert.Throws<ArgumentException>(() => new SaveProfile("local", "primary", "sub/playerprogress.json"));
            Assert.Throws<ArgumentException>(() => new SaveProfile("local", "primary", ".."));
            Assert.Throws<ArgumentException>(() => new SaveProfile("local", "primary", " "));
            Assert.Throws<ArgumentException>(() => new SaveProfile(" ", "primary", "playerprogress.json"));
            Assert.Throws<ArgumentException>(() => new SaveProfile("local", " ", "playerprogress.json"));
            Assert.Throws<ArgumentException>(() => new SaveProfile("local/primary", "primary", "playerprogress.json"));
        }

        // ---- 정상 흐름 ----

        [Test]
        public void Write_CreatesPrimary_AndLeavesNoTemporaryFile()
        {
            SaveWriteResult result = storage.Write(GoodJson);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsFalse(result.BackupKept, "첫 저장에는 내려보낼 이전 정상본이 없습니다.");
            Assert.AreEqual(GoodJson, File.ReadAllText(storage.PrimaryPath));
            Assert.IsFalse(File.Exists(storage.TemporaryPath), "임시본이 남으면 안 됩니다.");
            Assert.IsFalse(File.Exists(storage.BackupPath), "백업할 이전 파일이 없었으므로 백업도 없어야 합니다.");
        }

        [Test]
        public void Write_RotatesPreviousContentIntoExactlyOneBackup()
        {
            storage.Write("{\"v\":1}");
            storage.Write("{\"v\":2}");
            SaveWriteResult third = storage.Write("{\"v\":3}");

            Assert.IsTrue(third.Succeeded, third.Message);
            Assert.IsTrue(third.BackupKept);
            Assert.AreEqual("{\"v\":3}", File.ReadAllText(storage.PrimaryPath), "주 파일은 마지막 내용입니다.");
            Assert.AreEqual("{\"v\":2}", File.ReadAllText(storage.BackupPath), "백업은 직전 내용입니다.");

            string[] files = Directory.GetFiles(root);
            Assert.AreEqual(2, files.Length,
                $"주 파일과 백업 하나만 남아야 하는데 실제로는: {string.Join(", ", files)}");
        }

        [Test]
        public void ReadPrimary_ReturnsWrittenText()
        {
            storage.Write(GoodJson);

            SaveReadResult read = storage.ReadPrimary();

            Assert.AreEqual(SaveReadStatus.Loaded, read.Status);
            Assert.AreEqual(GoodJson, read.Text);
            Assert.AreEqual(storage.PrimaryPath, read.Path);
        }

        [Test]
        public void ReadPrimary_ReturnsMissing_WhenNothingWasEverSaved()
        {
            SaveReadResult read = storage.ReadPrimary();

            Assert.AreEqual(SaveReadStatus.Missing, read.Status);
            Assert.IsNull(read.Text);
        }

        [Test]
        public void ReadPrimary_ReturnsUnreadable_WhenFileIsEmpty()
        {
            File.WriteAllText(storage.PrimaryPath, string.Empty);

            SaveReadResult read = storage.ReadPrimary();

            Assert.AreEqual(SaveReadStatus.Unreadable, read.Status,
                "0바이트 파일은 '저장한 적 없음'이 아니라 '쓰다 만 흔적'입니다.");
            Assert.IsNotNull(read.Message);
        }

        [Test]
        public void ReadBackup_ReadsTheBackupSlotSeparately()
        {
            storage.Write(GoodJson);
            storage.Write(NewerJson);

            Assert.AreEqual(NewerJson, storage.ReadPrimary().Text);
            Assert.AreEqual(GoodJson, storage.ReadBackup().Text);
        }

        [Test]
        public void ReadPrimary_RemovesLeftoverTemporaryFile()
        {
            storage.Write(GoodJson);
            File.WriteAllText(storage.TemporaryPath, "쓰다 만 내용");

            storage.ReadPrimary();

            Assert.IsFalse(File.Exists(storage.TemporaryPath),
                "끝나지 않은 쓰기의 임시본은 다음 읽기에서 치워져야 합니다.");
            Assert.AreEqual(GoodJson, File.ReadAllText(storage.PrimaryPath),
                "임시본을 치우는 동작이 주 파일을 건드리면 안 됩니다.");
        }

        // ---- 실패 흐름 ----

        [Test]
        public void Write_Fails_AndKeepsPrimaryAndBackup_WhenTemporaryFileCannotBeWritten()
        {
            storage.Write(GoodJson);
            storage.Write(NewerJson);

            // 임시본이 놓일 자리를 폴더로 막는다 - 그 경로에 파일을 쓰는 동작이 실제로 실패한다.
            Directory.CreateDirectory(storage.TemporaryPath);

            SaveWriteResult result = storage.Write("{\"v\":999}");

            Assert.AreEqual(SaveWriteStatus.Failed, result.Status);
            Assert.IsNotNull(result.Message);
            Assert.AreEqual(NewerJson, File.ReadAllText(storage.PrimaryPath),
                "쓰기에 실패했으면 기존 주 파일이 그대로 남아야 합니다.");
            Assert.AreEqual(GoodJson, File.ReadAllText(storage.BackupPath),
                "쓰기에 실패했으면 백업도 그대로 남아야 합니다.");
        }

        [Test]
        public void Write_Fails_AndKeepsPrimary_WhenBackupSlotIsUnusable()
        {
            storage.Write(GoodJson);

            // 백업이 놓일 자리를 폴더로 막는다 - File.Replace가 실제로 실패한다.
            Directory.CreateDirectory(storage.BackupPath);

            SaveWriteResult result = storage.Write(NewerJson);

            Assert.AreEqual(SaveWriteStatus.Failed, result.Status,
                "백업을 남기지 못하면 마지막 정상본이 사라지므로 교체까지 포기해야 합니다.");
            Assert.IsFalse(result.BackupKept);
            Assert.IsNotNull(result.Message);
            Assert.AreEqual(GoodJson, File.ReadAllText(storage.PrimaryPath),
                "교체에 실패했으면 기존 주 파일이 그대로 남아야 합니다.");
            Assert.IsTrue(Directory.Exists(storage.BackupPath),
                "막혀 있던 백업 자리를 저장소가 마음대로 치우면 안 됩니다.");
            Assert.IsFalse(File.Exists(storage.TemporaryPath), "실패해도 임시본은 치워야 합니다.");
        }

        [Test]
        public void Write_Fails_AndCleansUpTemporary_WhenPrimarySlotIsBlockedByADirectory()
        {
            // 백업이 끼어들지 않는 교체 실패 지점. 주 파일 자리가 폴더면 첫 저장의 File.Move가
            // 실제로 실패하므로, 교체 단계만 홀로 실패했을 때의 뒤처리를 확인할 수 있다.
            Directory.CreateDirectory(storage.PrimaryPath);

            SaveWriteResult result = storage.Write(GoodJson);

            Assert.AreEqual(SaveWriteStatus.Failed, result.Status);
            Assert.IsNotNull(result.Message);
            Assert.IsFalse(File.Exists(storage.TemporaryPath), "실패해도 임시본은 치워야 합니다.");
            Assert.IsFalse(File.Exists(storage.BackupPath), "실패한 쓰기가 백업을 만들면 안 됩니다.");
        }

        // ---- 손상본 격리 ----

        [Test]
        public void QuarantinePrimary_MovesCorruptFileAside_AndLeavesBackupIntact()
        {
            storage.Write(GoodJson);
            storage.Write(NewerJson);
            File.WriteAllText(storage.PrimaryPath, CorruptJson); // 밖에서 깨진 상황을 그대로 재현

            SaveQuarantineResult quarantine = storage.QuarantinePrimary("JSON 파싱 실패");

            Assert.IsTrue(quarantine.Succeeded, quarantine.Message);
            Assert.IsFalse(File.Exists(storage.PrimaryPath), "손상본은 주 파일 자리에서 치워져야 합니다.");
            Assert.IsTrue(File.Exists(quarantine.QuarantinePath), "손상본은 지우지 않고 보존해야 합니다.");
            Assert.AreEqual(CorruptJson, File.ReadAllText(quarantine.QuarantinePath),
                "격리는 내용을 그대로 남기는 일입니다.");
            Assert.AreEqual(GoodJson, storage.ReadBackup().Text, "백업은 격리와 무관하게 남아 있어야 합니다.");
            Assert.IsFalse(storage.WritesBlocked, "보존에 성공했으면 저장을 막을 이유가 없습니다.");
        }

        [Test]
        public void Write_AfterSuccessfulQuarantine_StartsFreshWithoutTouchingBackup()
        {
            storage.Write(GoodJson);
            storage.Write(NewerJson);
            File.WriteAllText(storage.PrimaryPath, CorruptJson);
            storage.QuarantinePrimary("JSON 파싱 실패");

            SaveWriteResult result = storage.Write("{\"v\":42}");

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual("{\"v\":42}", File.ReadAllText(storage.PrimaryPath));
            Assert.AreEqual(GoodJson, File.ReadAllText(storage.BackupPath),
                "격리 직후의 첫 저장이 마지막 정상본을 밀어내면 안 됩니다.");
        }

        [Test]
        public void QuarantinePrimary_WithNoPrimaryFile_DoesNothingAndDoesNotBlock()
        {
            SaveQuarantineResult quarantine = storage.QuarantinePrimary("파일 없음");

            Assert.IsTrue(quarantine.Succeeded, "잃을 파일이 없으면 격리는 할 일이 없습니다.");
            Assert.IsNull(quarantine.QuarantinePath);
            Assert.IsFalse(storage.WritesBlocked);
        }

        [Test]
        public void QuarantinePrimary_BlocksFurtherWrites_WhenTheCorruptFileCannotBePreserved()
        {
            storage.Write(GoodJson);
            File.WriteAllText(storage.PrimaryPath, CorruptJson);

            // 격리 폴더가 놓일 자리를 파일로 막는다 - 폴더를 만들 수 없으니 보존도 할 수 없다.
            File.WriteAllText(paths.QuarantineDirectory, "격리 폴더 자리를 막는 파일");

            SaveQuarantineResult quarantine = storage.QuarantinePrimary("JSON 파싱 실패");

            Assert.IsFalse(quarantine.Succeeded);
            Assert.IsTrue(storage.WritesBlocked, "보존하지 못한 손상본을 덮어쓰지 않도록 저장을 막아야 합니다.");
            Assert.IsNotNull(storage.BlockedReason);
            Assert.AreEqual(CorruptJson, File.ReadAllText(storage.PrimaryPath),
                "보존에 실패했으면 손상본조차 지우면 안 됩니다 - 사용자가 직접 복구할 마지막 수단입니다.");

            SaveWriteResult blocked = storage.Write(NewerJson);

            Assert.AreEqual(SaveWriteStatus.Blocked, blocked.Status);
            Assert.AreEqual(storage.BlockedReason, blocked.Message);
            Assert.AreEqual(CorruptJson, File.ReadAllText(storage.PrimaryPath),
                "막힌 뒤에는 아무것도 쓰지 않아야 합니다.");
            Assert.IsFalse(File.Exists(storage.TemporaryPath), "막혔으면 임시본조차 만들지 않아야 합니다.");
        }
    }
}
