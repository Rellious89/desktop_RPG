using System;
using System.IO;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 저장 문서 한 벌의 이름표. <b>식별과 위치는 별개다</b> - 이 클래스는 "어느 저장소의 어느
    /// 슬롯인가"(<see cref="Backend"/>/<see cref="Slot"/>)와 "어떤 파일 이름을 쓰는가"
    /// (<see cref="FileName"/>)만 들고 있고, 어느 폴더에 두는지는 <see cref="SavePathProvider"/>가
    /// 정한다.
    ///
    /// <b>식별자가 폴더 구조가 되면 안 된다.</b> local/primary라는 이름 때문에
    /// <c>.../local/primary/</c> 같은 하위 폴더를 만들면, 이미 저장된 사용자 파일
    /// (persistentDataPath/playerprogress.json)이 "없는 파일"이 되어 새 게임으로 시작된다. 이름은
    /// 코드가 슬롯을 가리키는 말일 뿐이고, 물리 경로는 <see cref="FileName"/>과 저장 폴더만으로
    /// 정해진다 - 나중에 저장소를 하나 더 붙여도 기존 경로는 그대로 남는다.
    ///
    /// <b>파일 이름은 경로가 아니다.</b> 구분자나 상대 경로(".", "..")가 들어오면 저장 폴더 밖으로
    /// 새 나가므로 만들 때 바로 막는다. 이 검사는 잘못 쓴 호출부를 조용히 통과시키지 않으려는
    /// 것이므로 예외로 알린다(IO 실패와 달리 실행 중에 벌어질 수 있는 사고가 아니라 코드 오류다).
    /// </summary>
    public sealed class SaveProfile
    {
        /// <summary>본 저장 슬롯의 논리 이름. 세이브 파일이 하나뿐인 지금도 이 이름을 쓰는 이유는,
        /// "주 파일"이라는 말이 물리 파일(<see cref="SavePathProvider.PrimaryPath"/>)과 논리 슬롯
        /// 양쪽에 쓰이기 때문이다 - 슬롯 쪽 이름을 고정해 두면 둘을 헷갈리지 않는다.</summary>
        public const string PrimarySlot = "primary";

        /// <summary>로컬 파일 저장소를 가리키는 이름. 나중에 다른 저장소가 생겨도 이 값이 곧
        /// "기기 안의 파일"을 뜻한다.</summary>
        public const string LocalBackend = "local";

        /// <summary>실제 게임이 쓰는 유일한 프로필(local/primary). 파일 이름은 예전 SaveSystem이 쓰던
        /// 것과 같아야 한다 - 식별자가 local/primary로 바뀌어도 물리 경로는 그대로다.</summary>
        public static readonly SaveProfile LocalPrimary =
            new SaveProfile(LocalBackend, PrimarySlot, "playerprogress.json");

        public SaveProfile(string backend, string slot, string fileName)
        {
            if (string.IsNullOrWhiteSpace(backend))
            {
                throw new ArgumentException("저장소 이름은 비어 있을 수 없습니다.", nameof(backend));
            }

            if (string.IsNullOrWhiteSpace(slot))
            {
                throw new ArgumentException("저장 슬롯 이름은 비어 있을 수 없습니다.", nameof(slot));
            }

            if (backend.IndexOf('/') >= 0 || slot.IndexOf('/') >= 0)
            {
                throw new ArgumentException(
                    "저장소/슬롯 이름에는 '/'를 넣을 수 없습니다 - 식별자를 나누는 구분자입니다.");
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("저장 파일 이름은 비어 있을 수 없습니다.", nameof(fileName));
            }

            if (fileName == "." || fileName == ".." ||
                fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0 ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException(
                    $"저장 파일 이름에 경로를 넣을 수 없습니다: '{fileName}'", nameof(fileName));
            }

            Backend = backend;
            Slot = slot;
            FileName = fileName;
        }

        /// <summary>어느 저장소인가("local"). 폴더 이름이 아니다.</summary>
        public string Backend { get; }

        /// <summary>어느 슬롯인가("primary"). 폴더 이름이 아니다.</summary>
        public string Slot { get; }

        /// <summary>코드와 로그가 이 프로필을 부르는 이름("local/primary"). <b>경로가 아니다</b> -
        /// 가운데 '/'는 저장소와 슬롯을 나누는 구분자일 뿐이고, 폴더 계층으로 옮겨지지 않는다.</summary>
        public string Id => $"{Backend}/{Slot}";

        /// <summary>본 저장 슬롯인지. 나중에 자동 저장/임시 슬롯이 생겨도 이 값으로 구분한다.</summary>
        public bool IsPrimary => Slot == PrimarySlot;

        /// <summary>저장 폴더 안에서 쓰는 주 파일 이름(확장자 포함). 물리 경로를 정하는 유일한 값이다.</summary>
        public string FileName { get; }

        public override string ToString() => $"{Id}({FileName})";
    }

    /// <summary>
    /// 저장 폴더 하나를 기준으로 주 파일/백업/임시본/격리본 경로를 만든다. <b>여기서 만드는 경로는
    /// 전부 같은 폴더(또는 그 바로 아래 격리 폴더) 안에 있다</b> - 임시본을 다른 폴더에 만들면
    /// 볼륨이 달라져 원자적 교체(<see cref="System.IO.File.Replace(string,string,string)"/>)가
    /// 성립하지 않고, 결국 "쓰다 만 파일"이 주 파일 자리에 남을 수 있다.
    ///
    /// <b>프로필 식별자(local/primary)는 경로에 전혀 쓰지 않는다</b> - 오직 저장 폴더와
    /// <see cref="SaveProfile.FileName"/>만으로 경로가 정해진다. 그래서 슬롯 이름을 바꾸거나 저장소를
    /// 하나 더 붙여도 이미 저장된 사용자 파일의 위치가 움직이지 않는다.
    ///
    /// 파일을 만들거나 지우지 않는다 - 경로 문자열만 계산한다. 그래서 시험에서 마음대로 불러도
    /// 디스크에 아무 흔적이 남지 않는다.
    /// </summary>
    public sealed class SavePathProvider
    {
        private const string BackupExtension = ".bak";
        private const string TemporaryExtension = ".tmp";
        private const string QuarantineDirectoryName = "corrupted";

        /// <summary>실제 게임이 쓰는 저장 폴더(<see cref="Application.persistentDataPath"/>).
        /// 정적 필드가 아니라 메서드인 이유는, 불러야만 persistentDataPath를 건드리기 때문이다 -
        /// 시험은 이 메서드를 부르지 않고 임시 폴더를 직접 넣는다.</summary>
        public static SavePathProvider CreatePersistentData()
        {
            return new SavePathProvider(Application.persistentDataPath);
        }

        public SavePathProvider(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("저장 폴더 경로는 비어 있을 수 없습니다.", nameof(rootDirectory));
            }

            RootDirectory = rootDirectory;
        }

        /// <summary>주 파일과 백업, 임시본이 모두 들어가는 폴더.</summary>
        public string RootDirectory { get; }

        /// <summary>실제로 읽고 쓰는 저장 파일.</summary>
        public string PrimaryPath(SaveProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            return Path.Combine(RootDirectory, profile.FileName);
        }

        /// <summary>직전까지 쓰던 정상본 <b>하나</b>. 세대별로 늘리지 않는다 - 백업이 여러 개면
        /// "어느 것이 마지막 정상본인지"를 다시 판단해야 하고, 그 판단이 틀리면 오래된 진행도로
        /// 되돌아간다.</summary>
        public string BackupPath(SaveProfile profile)
        {
            return PrimaryPath(profile) + BackupExtension;
        }

        /// <summary>기록 중인 임시본. 이름이 매번 같은 이유는 저장하는 쪽이 하나뿐이기 때문이며,
        /// 덕분에 앱이 기록 도중 죽어도 다음 실행이 "치울 파일"을 추측 없이 정확히 안다.</summary>
        public string TemporaryPath(SaveProfile profile)
        {
            return PrimaryPath(profile) + TemporaryExtension;
        }

        /// <summary>손상된 저장 파일을 옮겨 두는 폴더. 주 파일과 섞이지 않도록 한 단계 아래에 둔다.</summary>
        public string QuarantineDirectory => Path.Combine(RootDirectory, QuarantineDirectoryName);

        /// <summary>손상된 저장 파일의 보존 경로. 시각을 이름에 넣어 예전 격리본을 덮어쓰지 않는다 -
        /// 격리는 "지우지 않기 위해" 하는 일이므로 덮어쓰면 목적이 사라진다.</summary>
        /// <param name="attempt">같은 밀리초에 두 번 격리하는 드문 경우에만 1 이상을 넣는다.</param>
        public string QuarantinePath(SaveProfile profile, DateTime utcNow, int attempt = 0)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            string stem = Path.GetFileNameWithoutExtension(profile.FileName);
            string extension = Path.GetExtension(profile.FileName);
            string stamp = utcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
            string suffix = attempt > 0 ? $"-{attempt}" : string.Empty;

            return Path.Combine(QuarantineDirectory, $"{stem}-{stamp}{suffix}{extension}");
        }
    }
}
