using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Common;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 모든 EditMode 시험보다 먼저 SaveSystem의 기본 저장소를 고유 임시 폴더로 바꾼다.
/// 각 시험이 별도 가짜 저장소를 넣었다가 해제해도 이 바깥 경계가 남으므로, 정적 상태의
/// 해제 순서나 예외 때문에 실제 persistentDataPath로 돌아가는 일이 없다.
/// </summary>
[SetUpFixture]
public sealed class SaveSystemTestIsolationFixture
{
    private static readonly MethodInfo PushOverrideMethod = typeof(SaveSystem).GetMethod(
        "PushStorageOverrideForTests", BindingFlags.NonPublic | BindingFlags.Static);

    private IDisposable scope;
    private string temporaryRoot;
    private FileFingerprint primaryBefore;
    private FileFingerprint backupBefore;

    [OneTimeSetUp]
    public void SetUp()
    {
        Assert.IsNotNull(PushOverrideMethod,
            "SaveSystem.PushStorageOverrideForTests를 찾지 못했습니다. 실제 저장소 격리를 시작할 수 없습니다.");

        SavePathProvider livePaths = SavePathProvider.CreatePersistentData();
        primaryBefore = FileFingerprint.Capture(livePaths.PrimaryPath(SaveProfile.LocalPrimary));
        backupBefore = FileFingerprint.Capture(livePaths.BackupPath(SaveProfile.LocalPrimary));

        temporaryRoot = Path.Combine(Path.GetTempPath(), "desktopRPG-EditModeSaveIsolation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var testStorage = new LocalFileSaveStorage(new SavePathProvider(temporaryRoot), SaveProfile.LocalPrimary);

        scope = (IDisposable)PushOverrideMethod.Invoke(null, new object[] { testStorage });
        Assert.IsNotNull(scope, "SaveSystem 테스트 저장소 override가 범위 토큰을 돌려주지 않았습니다.");
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        try
        {
            SavePathProvider livePaths = SavePathProvider.CreatePersistentData();
            primaryBefore.AssertUnchanged(livePaths.PrimaryPath(SaveProfile.LocalPrimary), "주 저장 파일");
            backupBefore.AssertUnchanged(livePaths.BackupPath(SaveProfile.LocalPrimary), "백업 저장 파일");
        }
        finally
        {
            if (scope != null) scope.Dispose();
            scope = null;

            if (!string.IsNullOrEmpty(temporaryRoot) && Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, true);
            }

            temporaryRoot = null;
        }
    }

    private readonly struct FileFingerprint
    {
        private FileFingerprint(bool exists, string sha256, long lastWriteUtcTicks)
        {
            Exists = exists;
            Sha256 = sha256;
            LastWriteUtcTicks = lastWriteUtcTicks;
        }

        private bool Exists { get; }
        private string Sha256 { get; }
        private long LastWriteUtcTicks { get; }

        public static FileFingerprint Capture(string path)
        {
            if (!File.Exists(path)) return new FileFingerprint(false, null, 0);

            var info = new FileInfo(path);
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (SHA256 sha256 = SHA256.Create())
            {
                return new FileFingerprint(true, BitConverter.ToString(sha256.ComputeHash(stream)),
                    info.LastWriteTimeUtc.Ticks);
            }
        }

        public void AssertUnchanged(string path, string label)
        {
            FileFingerprint after = Capture(path);
            Assert.AreEqual(Exists, after.Exists, $"{label}의 존재 여부가 EditMode 시험 중 바뀌었습니다: {path}");
            Assert.AreEqual(Sha256, after.Sha256, $"{label}의 해시가 EditMode 시험 중 바뀌었습니다: {path}");
            Assert.AreEqual(LastWriteUtcTicks, after.LastWriteUtcTicks,
                $"{label}의 수정 시각이 EditMode 시험 중 바뀌었습니다: {path}");
        }
    }
}
