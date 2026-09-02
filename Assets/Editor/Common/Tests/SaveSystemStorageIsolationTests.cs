using System;
using System.IO;
using System.Reflection;
using Common;
using NUnit.Framework;

namespace CommonEditor.Tests
{
    /// <summary>SaveSystem의 시험 저장 경계가 기본 경로와 섞이지 않는지 보는 회귀 시험.</summary>
    public sealed class SaveSystemStorageIsolationTests
    {
        private static readonly MethodInfo PushOverrideMethod = typeof(SaveSystem).GetMethod(
            "PushStorageOverrideForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo ConfigureMethod = typeof(SaveSystem).GetMethod(
            "ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private string temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(PushOverrideMethod);
            Assert.IsNotNull(ConfigureMethod);
            temporaryRoot = Path.Combine(Path.GetTempPath(), "desktopRPG-SaveIsolationTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
        }

        [TearDown]
        public void TearDown()
        {
            Configure(null);
            if (!string.IsNullOrEmpty(temporaryRoot) && Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, true);
            temporaryRoot = null;
        }

        [Test]
        public void DefaultStorage_StillUsesTheLegacyPersistentDataPath()
        {
            var defaultStorage = new LocalFileSaveStorage();
            Assert.AreEqual(Path.Combine(UnityEngine.Application.persistentDataPath, "playerprogress.json"),
                defaultStorage.PrimaryPath);
        }

        [Test]
        public void ScopedOverride_SaveLoadAndNewGameUseOnlyTheScopedTemporaryStorage()
        {
            LocalFileSaveStorage scopedStorage = NewStorage("scoped");
            string livePrimary = Path.Combine(UnityEngine.Application.persistentDataPath, "playerprogress.json");

            using (Push(scopedStorage))
            {
                Configure(null);
                Assert.AreEqual(SaveLoadStatus.NewGame, SaveSystem.LoadStatus);
                SaveSystem.Data.currency = 42;
                Assert.IsTrue(SaveSystem.Save());

                Assert.IsTrue(File.Exists(scopedStorage.PrimaryPath));
                Assert.AreNotEqual(livePrimary, scopedStorage.PrimaryPath);

                Configure(null);
                Assert.AreEqual(42, SaveSystem.Data.currency, "Load도 같은 scoped 저장소를 읽어야 합니다.");
            }

            Assert.IsFalse(File.Exists(Path.Combine(temporaryRoot, "playerprogress.json")),
                "scope 밖 루트에는 scoped 저장 파일이 남아 있으면 안 됩니다.");
        }

        [Test]
        public void NestedOverrides_RestoreTheOuterStorageAndRejectOutOfOrderDispose()
        {
            LocalFileSaveStorage outerStorage = NewStorage("outer");
            LocalFileSaveStorage innerStorage = NewStorage("inner");
            IDisposable outer = Push(outerStorage);
            IDisposable inner = null;

            try
            {
                Configure(null);
                SaveSystem.Data.currency = 1;
                Assert.IsTrue(SaveSystem.Save());
                Assert.IsTrue(File.Exists(outerStorage.PrimaryPath));

                inner = Push(innerStorage);
                Configure(null);
                SaveSystem.Data.currency = 2;
                Assert.IsTrue(SaveSystem.Save());
                Assert.IsTrue(File.Exists(innerStorage.PrimaryPath));

                Assert.Throws<InvalidOperationException>(() => outer.Dispose(),
                    "바깥 범위를 먼저 닫으면 조용히 기본 저장소로 새면 안 됩니다.");

                inner.Dispose();
                inner = null;
                Configure(null);
                SaveSystem.Data.currency = 3;
                Assert.IsTrue(SaveSystem.Save());
                Assert.AreEqual(3, ReadCurrency(outerStorage));
                Assert.AreEqual(2, ReadCurrency(innerStorage));
            }
            finally
            {
                if (inner != null) inner.Dispose();
                outer.Dispose();
            }
        }

        [Test]
        public void ExceptionInsideScopedOverride_DoesNotLeakTheTemporaryStorage()
        {
            LocalFileSaveStorage failedStorage = NewStorage("failed");
            LocalFileSaveStorage afterStorage = NewStorage("after");

            Assert.Throws<InvalidOperationException>(() =>
            {
                using (Push(failedStorage))
                {
                    Configure(null);
                    SaveSystem.Data.currency = 7;
                    Assert.IsTrue(SaveSystem.Save());
                    throw new InvalidOperationException("intentional test failure");
                }
            });

            using (Push(afterStorage))
            {
                Configure(null);
                SaveSystem.Data.currency = 8;
                Assert.IsTrue(SaveSystem.Save());
            }

            Assert.IsTrue(File.Exists(failedStorage.PrimaryPath));
            Assert.IsTrue(File.Exists(afterStorage.PrimaryPath));
            Assert.AreEqual(7, ReadCurrency(failedStorage));
            Assert.AreEqual(8, ReadCurrency(afterStorage));
        }

        private LocalFileSaveStorage NewStorage(string name)
        {
            string root = Path.Combine(temporaryRoot, name, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new LocalFileSaveStorage(new SavePathProvider(root), SaveProfile.LocalPrimary);
        }

        private static IDisposable Push(ISaveStorage storage)
        {
            return (IDisposable)PushOverrideMethod.Invoke(null, new object[] { storage });
        }

        private static void Configure(ISaveStorage storage)
        {
            ConfigureMethod.Invoke(null, new object[] { storage, null, null });
        }

        private static int ReadCurrency(LocalFileSaveStorage storage)
        {
            SaveReadResult read = storage.ReadPrimary();
            Assert.IsTrue(read.IsLoaded);
            return UnityEngine.JsonUtility.FromJson<SaveData>(read.Text).currency;
        }
    }
}
