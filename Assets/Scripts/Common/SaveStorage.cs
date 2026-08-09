using System;
using System.IO;
using UnityEngine;

namespace Common
{
    /// <summary>저장 파일 한 번 읽기의 결과 종류.</summary>
    public enum SaveReadStatus
    {
        /// <summary>내용을 읽었다. <see cref="SaveReadResult.Text"/>가 비어 있지 않다.</summary>
        Loaded,

        /// <summary>파일이 없다. 손상이 아니라 "아직 저장한 적 없음"이며, 새 게임으로 시작하면 된다.</summary>
        Missing,

        /// <summary>파일은 있는데 내용을 얻지 못했다(읽기 실패이거나 내용이 비어 있음).
        /// <b>Missing과 절대 같이 취급하면 안 된다</b> - 여기서 새 게임을 시작하면 아직 살아 있을지
        /// 모르는 사용자 진행도를 다음 저장 때 덮어쓴다.</summary>
        Unreadable
    }

    /// <summary>저장 파일 한 번 쓰기의 결과 종류.</summary>
    public enum SaveWriteStatus
    {
        /// <summary>주 파일이 새 내용으로 교체됐다.</summary>
        Written,

        /// <summary>손상본을 보존하지 못해 저장이 막혀 있다. 아무것도 쓰지 않았다.</summary>
        Blocked,

        /// <summary>쓰지 못했다. 기존 주 파일과 백업은 건드리지 않았다.</summary>
        Failed
    }

    /// <summary>읽기 한 번의 결과. 실패를 예외 대신 값으로 돌려주므로 호출부가 "무엇을 할지"를 고를 수 있다.</summary>
    public readonly struct SaveReadResult
    {
        private SaveReadResult(SaveReadStatus status, string path, string text, string message)
        {
            Status = status;
            Path = path;
            Text = text;
            Message = message;
        }

        public SaveReadStatus Status { get; }

        /// <summary>읽으려 한 파일 경로. 상태와 상관없이 항상 채워진다(로그/안내용).</summary>
        public string Path { get; }

        /// <summary><see cref="SaveReadStatus.Loaded"/>일 때만 내용이 들어 있다. 그 외에는 null.</summary>
        public string Text { get; }

        /// <summary>실패 사유. 성공이면 null.</summary>
        public string Message { get; }

        public bool IsLoaded => Status == SaveReadStatus.Loaded;

        public static SaveReadResult Loaded(string path, string text) =>
            new SaveReadResult(SaveReadStatus.Loaded, path, text, null);

        public static SaveReadResult Missing(string path) =>
            new SaveReadResult(SaveReadStatus.Missing, path, null, null);

        public static SaveReadResult Unreadable(string path, string message) =>
            new SaveReadResult(SaveReadStatus.Unreadable, path, null, message);
    }

    /// <summary>쓰기 한 번의 결과.</summary>
    public readonly struct SaveWriteResult
    {
        private SaveWriteResult(SaveWriteStatus status, bool backupKept, string message)
        {
            Status = status;
            BackupKept = backupKept;
            Message = message;
        }

        public SaveWriteStatus Status { get; }

        /// <summary>이번 쓰기로 직전 정상본이 백업 자리에 남았는지. 첫 저장이라 내려보낼 이전 파일이
        /// 없었을 때만 성공과 함께 false가 된다 - <b>백업에 실패한 채로 성공하는 경우는 없다</b>
        /// (백업을 남기지 못하면 교체 자체를 하지 않고 <see cref="SaveWriteStatus.Failed"/>다).</summary>
        public bool BackupKept { get; }

        /// <summary>실패 사유. 성공이면 null.</summary>
        public string Message { get; }

        public bool Succeeded => Status == SaveWriteStatus.Written;

        public static SaveWriteResult Written(bool backupKept) =>
            new SaveWriteResult(SaveWriteStatus.Written, backupKept, null);

        public static SaveWriteResult Blocked(string message) =>
            new SaveWriteResult(SaveWriteStatus.Blocked, false, message);

        public static SaveWriteResult Failed(string message) =>
            new SaveWriteResult(SaveWriteStatus.Failed, false, message);
    }

    /// <summary>손상본 격리 한 번의 결과.</summary>
    public readonly struct SaveQuarantineResult
    {
        private SaveQuarantineResult(bool succeeded, string quarantinePath, string message)
        {
            Succeeded = succeeded;
            QuarantinePath = quarantinePath;
            Message = message;
        }

        /// <summary>손상본이 안전하게 치워졌는지. 격리할 파일이 애초에 없었던 경우도 성공이다
        /// (잃을 것이 없으므로).</summary>
        public bool Succeeded { get; }

        /// <summary>실제로 옮겨 둔 경로. 옮긴 파일이 없으면 null.</summary>
        public string QuarantinePath { get; }

        public string Message { get; }

        public static SaveQuarantineResult Moved(string quarantinePath) =>
            new SaveQuarantineResult(true, quarantinePath, null);

        public static SaveQuarantineResult NothingToMove() =>
            new SaveQuarantineResult(true, null, "격리할 저장 파일이 없습니다.");

        public static SaveQuarantineResult Failed(string message) =>
            new SaveQuarantineResult(false, null, message);
    }

    /// <summary>
    /// 저장 문서 하나를 읽고 쓰는 자리. <b>내용의 뜻은 모른다</b> - JSON 문자열을 그대로 주고받으며,
    /// 그것이 올바른 SaveData인지 판단하는 일은 저장 시스템(호출부)의 몫이다. 그래서 형식이 바뀌어도
    /// 이 계층은 그대로 남는다.
    ///
    /// 주 파일과 백업을 따로 읽는 메서드를 둔 이유는, "주 파일이 깨졌을 때 백업으로 되돌릴지"가
    /// 저장 정책이지 저장소의 결정이 아니기 때문이다. 저장소는 두 자리를 보여 주기만 한다.
    /// </summary>
    public interface ISaveStorage
    {
        /// <summary>손상본을 보존하지 못해 쓰기가 막혀 있는지. true인 동안
        /// <see cref="Write"/>는 아무것도 하지 않고 <see cref="SaveWriteStatus.Blocked"/>를 돌려준다.</summary>
        bool WritesBlocked { get; }

        /// <summary>막힌 사유(사용자에게 보여 줄 수 있는 문장). 막혀 있지 않으면 null.</summary>
        string BlockedReason { get; }

        /// <summary>주 파일을 읽는다. 예외를 밖으로 던지지 않는다.</summary>
        SaveReadResult ReadPrimary();

        /// <summary>직전 정상본(백업)을 읽는다. 예외를 밖으로 던지지 않는다.</summary>
        SaveReadResult ReadBackup();

        /// <summary>주 파일을 <paramref name="text"/>로 교체한다. 실패해도 기존 주 파일은 그대로 남는다.</summary>
        SaveWriteResult Write(string text);

        /// <summary>주 파일을 손상본으로 보고 옆으로 치운다. 치우지 못하면 이후 쓰기를 막는다.</summary>
        /// <param name="reason">왜 손상으로 판단했는지(로그에 남긴다).</param>
        SaveQuarantineResult QuarantinePrimary(string reason);
    }

    /// <summary>
    /// 로컬 파일 한 벌(주 파일 + 백업 1개 + 임시본)로 <see cref="ISaveStorage"/>를 구현한다.
    ///
    /// <b>쓰기 순서가 이 클래스의 전부다.</b> 같은 폴더에 임시본을 먼저 다 쓰고, 그 다음
    /// <see cref="File.Replace(string,string,string)"/>로 주 파일 자리에 밀어 넣는다. Replace는
    /// 이름 바꾸기 한 번이라 <b>중간 상태가 디스크에 보이지 않으며</b>, 같은 동작으로 직전 주 파일이
    /// 백업 자리로 내려간다 - 그래서 "교체"와 "백업 1개 유지"가 따로 실패할 수 없다. 임시본을 다른
    /// 폴더(예: 시스템 temp)에 만들면 볼륨이 달라져 이 보장이 사라지므로 항상 같은 폴더에 만든다.
    ///
    /// 어느 단계에서 실패하든 <b>기존 주 파일은 손대지 않은 채로 남는다</b>. 앱이 기록 도중 죽어서
    /// 임시본이 남아 있으면 다음 <see cref="ReadPrimary"/>가 지운다 - 남은 임시본은 "끝나지 않은
    /// 쓰기"라는 뜻이므로 살려 쓰지 않는다(내용이 온전한지 알 방법이 없다).
    ///
    /// 저장 폴더는 주입할 수 있다. 기본 생성자만 실제
    /// <see cref="Application.persistentDataPath"/>를 쓰며, 시험은 임시 폴더를 넣어 사용자 파일을
    /// 건드리지 않는다.
    /// </summary>
    public sealed class LocalFileSaveStorage : ISaveStorage
    {
        /// <summary>같은 밀리초에 격리가 겹칠 때 이름을 바꿔 가며 시도하는 횟수. 넘어가면 실패로 본다.</summary>
        private const int QuarantineNameAttempts = 20;

        private readonly SavePathProvider paths;
        private readonly SaveProfile profile;

        /// <summary>local/primary 프로필을 기존 저장 위치(persistentDataPath/playerprogress.json)에서
        /// 그대로 읽고 쓰는 실제 저장소.</summary>
        public LocalFileSaveStorage()
            : this(SavePathProvider.CreatePersistentData(), SaveProfile.LocalPrimary)
        {
        }

        public LocalFileSaveStorage(SavePathProvider paths, SaveProfile profile)
        {
            this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        public string PrimaryPath => paths.PrimaryPath(profile);
        public string BackupPath => paths.BackupPath(profile);
        public string TemporaryPath => paths.TemporaryPath(profile);

        public bool WritesBlocked { get; private set; }
        public string BlockedReason { get; private set; }

        /// <summary>주 파일을 읽으면서, 남아 있던 임시본(끝나지 않은 쓰기의 흔적)을 치운다.</summary>
        public SaveReadResult ReadPrimary()
        {
            DeleteStaleTemporaryFile();
            return ReadTextFile(PrimaryPath);
        }

        public SaveReadResult ReadBackup()
        {
            return ReadTextFile(BackupPath);
        }

        public SaveWriteResult Write(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            if (WritesBlocked)
            {
                return SaveWriteResult.Blocked(BlockedReason);
            }

            try
            {
                Directory.CreateDirectory(paths.RootDirectory);
            }
            catch (Exception e)
            {
                return SaveWriteResult.Failed($"저장 폴더를 만들지 못했습니다: {e.Message}");
            }

            string temporaryPath = TemporaryPath;

            try
            {
                File.WriteAllText(temporaryPath, text);
            }
            catch (Exception e)
            {
                // 임시본은 아직 아무 자리도 차지하지 않았다 - 치우고 기존 주 파일을 그대로 둔다.
                DeleteQuietly(temporaryPath);
                return SaveWriteResult.Failed($"임시 파일 기록 실패: {e.Message}");
            }

            if (TryPlaceOverPrimary(temporaryPath, BackupPath, out bool backupKept, out string error))
            {
                return SaveWriteResult.Written(backupKept);
            }

            // 교체 단계가 실패하면 <b>여기서 끝낸다</b>. 백업 없이 교체만 다시 시도하면 새 내용은
            // 남지만 마지막 정상본이 사라지고, 하필 그 새 내용이 나중에 문제로 드러나면 되돌아갈 곳이
            // 없다. 백업을 쓸 수 없는 상태는 저장 폴더 자체가 이상하다는 신호이므로, 기존 주 파일과
            // 백업을 손대지 않은 채로 두고 실패를 알리는 편이 안전하다.
            DeleteQuietly(temporaryPath);
            return SaveWriteResult.Failed($"저장 파일 교체 실패: {error}");
        }

        public SaveQuarantineResult QuarantinePrimary(string reason)
        {
            string primaryPath = PrimaryPath;

            try
            {
                if (!File.Exists(primaryPath)) return SaveQuarantineResult.NothingToMove();
            }
            catch (Exception e)
            {
                return Block($"손상된 저장 파일을 확인하지 못했습니다: {e.Message}");
            }

            try
            {
                Directory.CreateDirectory(paths.QuarantineDirectory);

                DateTime utcNow = DateTime.UtcNow;
                string target = paths.QuarantinePath(profile, utcNow);
                for (int attempt = 1; attempt < QuarantineNameAttempts && File.Exists(target); attempt++)
                {
                    target = paths.QuarantinePath(profile, utcNow, attempt);
                }

                File.Move(primaryPath, target);

                Debug.LogWarning(
                    $"[LocalFileSaveStorage] 손상된 저장 파일을 격리했습니다({reason}). 보존 위치: {target}");
                return SaveQuarantineResult.Moved(target);
            }
            catch (Exception e)
            {
                // 손상본을 보존하지 못한 채로 계속 저장하면 사용자의 마지막 진행도를 영영 덮어쓴다.
                // 여기서 막고, 그 사실을 호출부가 사용자에게 알리도록 상태로 남긴다.
                return Block(
                    $"손상된 저장 파일을 보존하지 못해 저장을 멈췄습니다({e.Message}). " +
                    $"'{primaryPath}' 파일을 직접 옮기거나 지운 뒤 다시 시작하세요.");
            }
        }

        /// <summary>
        /// 다 쓴 임시본을 주 파일 자리로 밀어 넣는다. 주 파일이 이미 있으면 그 파일이 백업 자리로
        /// 내려간다 - <b>교체와 백업이 같은 한 번의 동작</b>이라 둘 중 하나만 성공하는 상태가 없다.
        /// </summary>
        private bool TryPlaceOverPrimary(string temporaryPath, string backupPath, out bool backupKept, out string error)
        {
            backupKept = false;
            error = null;

            try
            {
                if (File.Exists(PrimaryPath))
                {
                    File.Replace(temporaryPath, PrimaryPath, backupPath);
                    backupKept = true;
                    return true;
                }

                // 첫 저장(또는 격리 직후)이라 내려보낼 이전 정상본이 없다.
                File.Move(temporaryPath, PrimaryPath);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static SaveReadResult ReadTextFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return SaveReadResult.Missing(path);

                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text))
                {
                    // 0바이트 파일은 "저장한 적 없음"이 아니라 "쓰다 만 흔적"이다. Missing으로 뭉뚱그리면
                    // 호출부가 새 게임을 시작해 버린다.
                    return SaveReadResult.Unreadable(path, "저장 파일이 비어 있습니다.");
                }

                return SaveReadResult.Loaded(path, text);
            }
            catch (Exception e)
            {
                return SaveReadResult.Unreadable(path, e.Message);
            }
        }

        /// <summary>끝나지 않은 쓰기가 남긴 임시본을 치운다. 지우지 못해도 그냥 넘어간다 - 다음 쓰기가
        /// 어차피 같은 이름에 덮어쓰므로 여기서 막을 이유가 없다.</summary>
        private void DeleteStaleTemporaryFile()
        {
            string temporaryPath = TemporaryPath;

            try
            {
                if (!File.Exists(temporaryPath)) return;

                File.Delete(temporaryPath);
                Debug.LogWarning($"[LocalFileSaveStorage] 끝나지 않은 저장의 임시 파일을 치웠습니다: {temporaryPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalFileSaveStorage] 임시 파일을 치우지 못했습니다: {e.Message}");
            }
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalFileSaveStorage] 임시 파일을 치우지 못했습니다: {e.Message}");
            }
        }

        private SaveQuarantineResult Block(string reason)
        {
            WritesBlocked = true;
            BlockedReason = reason;
            Debug.LogWarning($"[LocalFileSaveStorage] {reason}");
            return SaveQuarantineResult.Failed(reason);
        }
    }
}
