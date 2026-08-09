namespace Common
{
    /// <summary>
    /// 저장 파일을 읽으려 시도한 결과. <b>"불러왔다/못 불러왔다" 두 갈래로 뭉치지 않는 이유</b>는
    /// 갈래마다 호출부가 해야 할 일이 다르기 때문이다 - 어떤 것은 그대로 놀면 되고, 어떤 것은 곧바로
    /// 다시 저장해야 하며, 어떤 것은 <b>절대 저장하면 안 된다</b>. 하나로 뭉치면 그 차이가 사라져서
    /// 결국 남의 저장 파일을 덮어쓰게 된다.
    /// </summary>
    public enum SaveLoadStatus
    {
        /// <summary>저장 파일이 없었다(또는 내용이 비어 있었다). 기본값으로 새로 시작한다.</summary>
        NewGame,

        /// <summary>현재 버전 파일을 그대로 읽었다. 형식을 바꾼 것이 없다.</summary>
        Loaded,

        /// <summary>예전 버전 파일을 읽어 현재 버전으로 올렸다. 호출부는 <b>되도록 빨리 한 번
        /// 저장</b>해서 올린 결과를 파일에도 남기는 것이 좋다 - 그래야 다음 실행에서 같은 변환을
        /// 되풀이하지 않는다.</summary>
        Migrated,

        /// <summary>파일이 손상돼 읽을 수 없어 기본값으로 시작한다. 진행할 수는 있지만 <b>예전
        /// 내용은 사라진다</b> - 호출부는 덮어쓰기 전에 손상된 파일을 따로 남겨 두는 편이 좋다.</summary>
        CorruptFallback,

        /// <summary>파일이 이 빌드보다 새로운 형식이다. <b>진행하지도 저장하지도 않는다</b> - 모르는
        /// 형식을 헌 형식으로 덮어쓰면 최신 클라이언트에서 만든 진행이 그대로 사라진다.</summary>
        FutureVersionBlocked,

        /// <summary>변환 도중 실패했다(단계가 없거나 단계가 예외를 던졌다). 반쯤 바뀐 데이터는
        /// 버리고, <b>저장을 막아</b> 원본 파일을 지킨다.</summary>
        MigrationFailed,
    }

    /// <summary>
    /// 불러오기 한 번의 전말. 상태와 함께 <b>쓸 수 있는 데이터가 있는지</b>, <b>저장해도 되는지</b>를
    /// 같이 들고 다닌다.
    ///
    /// <see cref="Data"/>가 null인 결과(<see cref="SaveLoadStatus.FutureVersionBlocked"/>,
    /// <see cref="SaveLoadStatus.MigrationFailed"/>)는 "데이터를 못 만들었다"가 아니라 "일부러 만들지
    /// 않았다"이다 - 그 상황에서 기본값 문서를 쥐어 주면 호출부가 그것을 정상 진행으로 착각하고
    /// 저장해서 원본을 날린다.
    /// </summary>
    public readonly struct SaveLoadResult
    {
        public SaveLoadStatus Status { get; }

        /// <summary>진행에 쓸 저장 문서. 막힌 결과에서는 null이다.</summary>
        public SaveData Data { get; }

        /// <summary>파일에 적혀 있던 버전. 알 수 없으면 <see cref="SaveData.UnknownSaveVersion"/>.</summary>
        public int FromVersion { get; }

        /// <summary>이 결과의 데이터가 도달한 버전. 데이터가 없으면
        /// <see cref="SaveData.UnknownSaveVersion"/>.</summary>
        public int ToVersion { get; }

        /// <summary>사람이 읽을 사유(로그/안내용). 분기 조건으로 쓰지 않는다.</summary>
        public string Message { get; }

        /// <summary>이 결과로 게임을 진행할 수 있는가.</summary>
        public bool HasData => Data != null;

        /// <summary>지금 저장하면 파일이 망가지는가. true면 호출부는 저장 경로를 막아야 한다.</summary>
        public bool ShouldBlockSaving =>
            Status == SaveLoadStatus.FutureVersionBlocked || Status == SaveLoadStatus.MigrationFailed;

        /// <summary>올린 결과를 파일에도 남기려면 한 번 저장해 두는 것이 좋은가.</summary>
        public bool ShouldResaveSoon => Status == SaveLoadStatus.Migrated;

        private SaveLoadResult(SaveLoadStatus status, SaveData data, int fromVersion, int toVersion, string message)
        {
            Status = status;
            Data = data;
            FromVersion = fromVersion;
            ToVersion = toVersion;
            Message = message;
        }

        public static SaveLoadResult NewGame(SaveData data) =>
            new SaveLoadResult(SaveLoadStatus.NewGame, data,
                SaveData.UnknownSaveVersion, SaveData.CurrentSaveVersion,
                "저장 파일이 없어 새 게임 기본값으로 시작합니다.");

        public static SaveLoadResult Loaded(SaveData data, int version) =>
            new SaveLoadResult(SaveLoadStatus.Loaded, data, version, version,
                $"저장 파일(v{version})을 그대로 불러왔습니다.");

        public static SaveLoadResult Migrated(SaveData data, int fromVersion, int toVersion) =>
            new SaveLoadResult(SaveLoadStatus.Migrated, data, fromVersion, toVersion,
                $"저장 파일을 v{fromVersion}에서 v{toVersion}으로 올렸습니다.");

        public static SaveLoadResult CorruptFallback(SaveData data, string reason) =>
            new SaveLoadResult(SaveLoadStatus.CorruptFallback, data,
                SaveData.UnknownSaveVersion, SaveData.CurrentSaveVersion,
                $"저장 파일을 읽을 수 없어 기본값으로 시작합니다: {reason}");

        public static SaveLoadResult FutureVersionBlocked(int fileVersion, int supportedVersion) =>
            new SaveLoadResult(SaveLoadStatus.FutureVersionBlocked, null, fileVersion, SaveData.UnknownSaveVersion,
                $"저장 파일(v{fileVersion})이 이 빌드가 아는 형식(v{supportedVersion})보다 새롭습니다. " +
                "덮어쓰지 않기 위해 불러오기와 저장을 모두 막습니다.");

        public static SaveLoadResult MigrationFailed(int fromVersion, int stoppedAtVersion, string reason) =>
            new SaveLoadResult(SaveLoadStatus.MigrationFailed, null, fromVersion, SaveData.UnknownSaveVersion,
                $"저장 파일을 v{fromVersion}에서 올리는 도중 v{stoppedAtVersion}에서 실패했습니다: {reason}");

        public override string ToString() => $"{Status}: {Message}";
    }
}
