using System;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 이 프로세스가 공유하는 저장 문서 하나를 들고, 불러오기/저장의 <b>순서</b>를 정하는 자리.
    /// 파일이 없거나, 손상됐거나, 쓰기에 실패해도 예외를 밖으로 던지지 않는다 - 저장 기능 하나 때문에
    /// 공격/콤보/경험치 같은 기존 동작이 막히면 안 된다.
    ///
    /// <b>저장 문서는 <see cref="Data"/> 하나뿐이다.</b> 예전에는 저장할 때마다 호출부가 새 SaveData를
    /// 만들어 넘겼는데, 그러면 저장 대상이 둘 이상이 되는 순간(경험치 + 캐릭터 상태) 나중에 저장한
    /// 쪽이 상대의 필드를 기본값으로 덮어써서 지운다. 지금은 모든 시스템이 이 인스턴스를 직접 고친 뒤
    /// <see cref="Save"/>를 호출한다.
    ///
    /// <b>여기서는 경로도 JSON 문법도 모른다.</b> 파일을 어디에 어떤 순서로 놓는지는
    /// <see cref="ISaveStorage"/>가, 버전을 알아내고 올리는 일은 <see cref="SaveMigrationRunner"/>가
    /// 맡는다. 이 클래스에 남은 결정은 하나다 - <b>어떤 상황에서 저장을 허락하는가.</b>
    ///
    /// <b>저장을 막아야 하는 상황이 있다.</b> 파일이 이 빌드보다 새롭거나(미래 버전) 변환이 실패했을
    /// 때 지금 내용을 쓰면 아직 멀쩡한 사용자 파일을 헌 형식으로 덮어써서 되돌릴 수 없게 만든다.
    /// 그때도 <see cref="Data"/>는 <b>null이 아니다</b> - 기존 호출부가 전부 null 검사 없이 이 값을
    /// 쓰고 있어서, null을 주면 저장 기능 하나 때문에 게임이 통째로 죽는다. 그래서 메모리에는 기본값
    /// 문서를 두어 게임을 계속 굴리고, 파일은 손대지 않은 채로 남긴다.
    /// </summary>
    public static class SaveSystem
    {
        /// <summary>이 프로세스의 유일한 저장 문서. <b>이름을 바꾸지 말 것</b> - 시험이 실제 저장 파일을
        /// 읽지 않도록 이 필드를 리플렉션으로 갈아 끼운다(Inventory/DefeatRewardTests).</summary>
        private static SaveData data;

        private static bool loadedFromFile;

        /// <summary>지연 생성한다 - 실제 저장 폴더를 건드리는 시점을 "정말 필요할 때"로 미뤄야
        /// 문서를 미리 끼워 넣은 시험이 파일 근처에도 가지 않는다.</summary>
        private static ISaveStorage storage;

        private static Func<DateTime> utcNowProvider;

        /// <summary>변환 표. 실제로는 <see cref="SaveMigrationRunner.Default"/> 하나뿐이지만, 시험이
        /// "중간 단계가 없다/단계가 터졌다"를 만들려면 표를 갈아 끼우는 수밖에 없다 - 실제 표에는
        /// 그런 구멍이 없고, 없어야 정상이기 때문이다.</summary>
        private static SaveMigrationRunner migrationRunner;

        /// <summary>마지막 불러오기의 전말. 상태별 안내가 필요한 호출부가 생기면 여기서 꺼내 쓴다.</summary>
        private static SaveLoadResult loadResult;

        /// <summary>지금 저장하면 사용자 파일이 망가지는가. <see cref="SaveLoadResult.ShouldBlockSaving"/>과
        /// 따로 두는 이유는 손상본 격리에 <b>실패</b>한 경우 때문이다 - 그때 결과 자체는
        /// "기본값으로 진행(CorruptFallback)"이지만, 원본을 치우지 못했으므로 저장은 막아야 한다.</summary>
        private static bool saveBlocked;

        private static ISaveStorage Storage => storage ??= new LocalFileSaveStorage();

        private static SaveMigrationRunner Runner => migrationRunner ??= SaveMigrationRunner.Default;

        private static DateTime UtcNow => (utcNowProvider ?? DefaultUtcNow)();

        private static DateTime DefaultUtcNow() => DateTime.UtcNow;

        /// <summary>저장 파일에서 실제로 값을 읽어왔는지 여부. false면 <see cref="Data"/>는 기본값이고,
        /// 호출부는 "새 게임 시작값"을 채운다.
        ///
        /// <b>파일이 있었으면 읽기에 실패해도 true다.</b> 손상/미래 버전/변환 실패를 "파일 없음"과 같이
        /// 다루면 호출부가 새 게임 시작값을 문서에 채워 넣고, 그 문서가 저장되는 순간 살아 있을지도
        /// 모르는 진행이 사라진다. false는 <b>정말로 저장한 적이 없을 때</b>만이다.</summary>
        public static bool LoadedFromFile
        {
            get
            {
                EnsureLoaded();
                return loadedFromFile;
            }
        }

        /// <summary>
        /// 마지막 불러오기가 어떤 갈래였는지. <see cref="LoadedFromFile"/>이 "파일이 있었는가"만
        /// 답하는 것과 달리, 여기서는 <b>왜 그 상태가 됐는지</b>까지 갈린다 - 같은
        /// <c>LoadedFromFile == true</c>라도 그대로 읽은 것과, 올린 것과, 손상돼서 기본값으로 진행하는
        /// 것과, 미래 버전이라 막힌 것은 사용자에게 할 말이 저마다 다르다.
        ///
        /// <b>분기 조건으로 쓰되 저장 가능 여부를 여기서 다시 판단하지 말 것.</b> 저장해도 되는지는
        /// <see cref="Save"/>가 이미 알고 있고(막힌 상태면 false를 돌려준다), 손상본 격리 실패처럼
        /// 상태만으로는 알 수 없는 경우가 있다 - 그때 상태는
        /// <see cref="SaveLoadStatus.CorruptFallback"/>이지만 저장은 막혀 있다.
        ///
        /// 시험이 <see cref="data"/>를 직접 끼워 넣은 경우처럼 불러오기를 거치지 않았다면 기본값
        /// (<see cref="SaveLoadStatus.NewGame"/>)이다.
        /// </summary>
        public static SaveLoadStatus LoadStatus
        {
            get
            {
                EnsureLoaded();
                return loadResult.Status;
            }
        }

        /// <summary>이 프로세스가 공유하는 유일한 저장 문서. 최초 접근 시 파일을 한 번 읽는다.
        /// <b>절대 null이 아니다.</b></summary>
        public static SaveData Data
        {
            get
            {
                EnsureLoaded();
                return data;
            }
        }

        /// <summary>
        /// 현재 <see cref="Data"/>를 파일에 기록하고 성공 여부를 돌려준다. 실패해도 예외를 밖으로
        /// 던지지 않으며, 쓰기가 실패한 경우 기존 저장 파일은 그대로 남는다(부분 기록으로 망가뜨리지
        /// 않는다).
        ///
        /// <b>실패하면 메타데이터를 되돌린다.</b> 저장 일련번호와 시각은 쓰기 <i>직전</i>에 찍는데,
        /// 쓰기가 실패했는데도 그 값이 메모리에 남으면 디스크의 문서보다 메모리 쪽 번호가 앞서게 되고
        /// "파일과 메모리 중 어느 쪽이 최신인가"를 번호로 가리는 곳이 전부 틀린 답을 얻는다.
        /// </summary>
        public static bool Save()
        {
            EnsureLoaded();

            if (saveBlocked)
            {
                Debug.LogWarning($"[SaveSystem] 저장을 막았습니다: {loadResult.Message}");
                return false;
            }

            // 찍기 전 값을 들고 있는다 - 실패하면 이 값으로 되돌린다.
            SaveMetadataSnapshot before = SaveData.MarkSaved(data, UtcNow);

            try
            {
                string json = JsonUtility.ToJson(data, true);
                SaveWriteResult write = Storage.Write(json);

                if (write.Succeeded) return true;

                Debug.LogWarning($"[SaveSystem] 저장 실패: {write.Message}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] 저장 실패: {e.Message}");
            }

            SaveData.RestoreMetadata(data, before);
            return false;
        }

        /// <summary>
        /// 저장 문서를 아직 만들지 않았다면 파일에서 한 번 읽어 만든다.
        ///
        /// 순서가 곧 규칙이다 - <b>읽기 → 훑기 → 미래 버전 차단 → 역직렬화 → 한 칸씩 변환 → 정규화.</b>
        /// 훑기와 차단이 역직렬화보다 <b>앞</b>에 있는 것이 핵심이다. 모르는 형식을 지금 형식으로 읽어
        /// 들이는 순간 모르는 필드가 버려지고, 그 문서가 한 번만 저장돼도 원본은 되돌릴 수 없다.
        /// (훑기부터 정규화까지는 <see cref="SaveMigrationRunner.Load"/> 안에 한 줄로 이어져 있다.)
        ///
        /// <b>여기서는 절대 파일을 쓰지 않는다.</b> v0 파일을 올렸더라도 그 결과를 곧바로 기록하지
        /// 않고 다음 <see cref="Save"/>까지 기다린다 - 게임을 켜기만 하고 아무것도 하지 않은 사용자의
        /// 파일을 바꿔 놓지 않기 위해서다. 그때가 오면 원자적 교체가 v0 원본을 백업 자리에 남긴다.
        /// </summary>
        private static void EnsureLoaded()
        {
            if (data != null) return;

            ISaveStorage activeStorage = Storage;

            // 읽으면서 끝나지 않은 쓰기의 임시본까지 저장소가 치운다 - 여기서 따로 청소하지 않는다.
            SaveReadResult read = activeStorage.ReadPrimary();

            if (read.Status == SaveReadStatus.Missing)
            {
                // 정말로 저장한 적이 없는 유일한 경우.
                loadResult = Runner.NewGame();
                data = loadResult.Data;
                loadedFromFile = false;
                saveBlocked = false;
                return;
            }

            // 여기서부터는 파일이 있었다. 어떤 결과가 나오든 "새 게임"이 아니다.
            loadedFromFile = true;

            if (read.Status == SaveReadStatus.Unreadable)
            {
                QuarantineAndFallBack(activeStorage, $"저장 파일을 읽지 못했습니다: {read.Message}");
                return;
            }

            loadResult = Runner.Load(read.Text, JsonUtility.FromJson<SaveData>);

            switch (loadResult.Status)
            {
                case SaveLoadStatus.Loaded:
                case SaveLoadStatus.Migrated:
                    data = loadResult.Data;
                    saveBlocked = false;
                    if (loadResult.Status == SaveLoadStatus.Migrated)
                    {
                        Debug.LogWarning($"[SaveSystem] {loadResult.Message}");
                    }

                    return;

                case SaveLoadStatus.CorruptFallback:
                    QuarantineAndFallBack(activeStorage, loadResult.Message);
                    return;

                default:
                    // 미래 버전이거나 변환에 실패했다. 파일도 백업도 임시본도 건드리지 않고, 메모리에만
                    // 기본값 문서를 둬서 게임이 도는 동안 null로 터지지 않게 한다.
                    data = SaveDataNormalizer.Normalize(new SaveData());
                    saveBlocked = true;
                    Debug.LogWarning($"[SaveSystem] {loadResult.Message}");
                    return;
            }
        }

        /// <summary>
        /// 손상된 주 파일을 옆으로 치우고 기본값 문서로 게임을 이어 간다.
        ///
        /// <b>치우는 데 성공해야만 저장을 허락한다.</b> 성공했다면 주 파일 자리가 비어 있으므로 다음
        /// 저장이 새 파일을 만들 뿐 아무것도 잃지 않는다. 실패했다면 손상본이 아직 주 파일 자리에
        /// 있고, 그것이 사용자가 직접 복구할 마지막 수단이라 덮어쓰게 둘 수 없다.
        /// </summary>
        private static void QuarantineAndFallBack(ISaveStorage activeStorage, string reason)
        {
            SaveQuarantineResult quarantine = activeStorage.QuarantinePrimary(reason);

            data = SaveDataNormalizer.Normalize(new SaveData());
            loadResult = SaveLoadResult.CorruptFallback(data, reason);
            saveBlocked = !quarantine.Succeeded;

            Debug.LogWarning(quarantine.Succeeded
                ? $"[SaveSystem] {loadResult.Message}"
                : $"[SaveSystem] {loadResult.Message} 손상본을 보존하지 못해 저장을 막습니다: {quarantine.Message}");
        }

        /// <summary>
        /// <b>시험 전용 이음매.</b> 저장소와 시계를 갈아 끼우고 메모리 상태를 초기화한다. 리플렉션으로만
        /// 불리므로 공개 API 표면은 늘어나지 않는다 - 저장 하나 때문에 <see cref="Data"/>,
        /// <see cref="Save"/>, <see cref="LoadedFromFile"/> 말고 다른 것을 밖에 내보이고 싶지 않다.
        ///
        /// 초기화가 이 메서드 안에 있는 이유는, 시험이 필드를 하나씩 리플렉션으로 되돌리다 하나를
        /// 빠뜨리면 <b>앞 시험의 상태가 뒤 시험으로 새기</b> 때문이다. 되돌릴 것이 늘어나도 고칠 곳은
        /// 여기 한 곳이다.
        /// </summary>
        private static void ConfigureForTests(
            ISaveStorage testStorage, Func<DateTime> testClock, SaveMigrationRunner testRunner)
        {
            storage = testStorage;
            utcNowProvider = testClock;
            migrationRunner = testRunner;

            data = null;
            loadedFromFile = false;
            loadResult = default;
            saveBlocked = false;
        }
    }
}
