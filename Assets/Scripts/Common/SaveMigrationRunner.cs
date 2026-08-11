using System;
using System.Collections.Generic;

namespace Common
{
    /// <summary>
    /// 저장 형식을 <b>딱 한 칸</b> 올리는 변환 하나. <see cref="ToVersion"/>은 반드시
    /// <see cref="FromVersion"/> + 1이며, 이를 어기는 단계는 러너가 만들어질 때 거부된다.
    ///
    /// <b>왜 한 칸씩만인가.</b> "v0에서 v3으로 한 번에" 같은 지름길을 허용하면 단계 조합마다 서로 다른
    /// 코드 경로가 생겨(0→3, 0→1→3, 0→2→3...) 실제로 돌아 본 적 없는 경로가 반드시 남는다. 한 칸씩만
    /// 두면 경로는 언제나 하나뿐이고, 새 버전을 추가할 때 확인할 것도 새로 쓴 그 한 칸뿐이다.
    /// </summary>
    public interface ISaveMigrationStep
    {
        int FromVersion { get; }

        int ToVersion { get; }

        /// <summary>문서를 제자리에서 고친다. 실패는 예외로 알린다 - 러너가 잡아서
        /// <see cref="SaveMigrationOutcome.StepFailed"/>로 바꾼다.</summary>
        void Apply(SaveData data);
    }

    /// <summary>변환을 시도한 결과의 갈래.</summary>
    public enum SaveMigrationOutcome
    {
        /// <summary>이미 현재 버전이라 할 일이 없었다.</summary>
        AlreadyCurrent,

        /// <summary>한 단계 이상 올려 현재 버전에 도달했다.</summary>
        Migrated,

        /// <summary>파일이 이 빌드보다 새롭다. 손대지 않는다.</summary>
        FutureVersion,

        /// <summary>중간 어느 칸을 올릴 단계가 등록돼 있지 않다(등록을 빠뜨린 개발 실수).</summary>
        StepMissing,

        /// <summary>단계가 예외를 던졌다.</summary>
        StepFailed,
    }

    /// <summary>변환 결과와, 어디까지 갔는지.</summary>
    public readonly struct SaveMigrationResult
    {
        public SaveMigrationOutcome Outcome { get; }

        /// <summary>시작 버전(파일에 적혀 있던 값).</summary>
        public int FromVersion { get; }

        /// <summary>실제로 도달한 버전. 실패했다면 <b>실패한 칸의 시작 버전</b>이다.</summary>
        public int ReachedVersion { get; }

        /// <summary>실패 사유(성공이면 null).</summary>
        public string FailureReason { get; }

        /// <summary>데이터를 그대로 써도 되는가. 실패/미래 버전이면 false다.</summary>
        public bool Succeeded =>
            Outcome == SaveMigrationOutcome.AlreadyCurrent || Outcome == SaveMigrationOutcome.Migrated;

        public SaveMigrationResult(SaveMigrationOutcome outcome, int fromVersion, int reachedVersion,
            string failureReason)
        {
            Outcome = outcome;
            FromVersion = fromVersion;
            ReachedVersion = reachedVersion;
            FailureReason = failureReason;
        }
    }

    /// <summary>
    /// 예전 버전 저장 문서를 현재 버전까지 <b>한 칸씩</b> 올린다.
    ///
    /// 러너는 파일도 JSON도 모른다 - 넘겨받은 <see cref="SaveData"/>와 "이 문서는 몇 번 버전인가"만
    /// 다룬다. 그래서 시험이 디스크 없이 전부 돌아가고, 저장소 구현(경로, 백업, 원자적 쓰기)이 바뀌어도
    /// 변환 규칙은 손댈 이유가 없다. <b>단계도 같은 규칙을 지킨다</b> - 에셋이나 카탈로그를 읽는 단계는
    /// 두지 않는다(<see cref="V1ToV2Step"/> 참고). 지금의 프로젝트 데이터를 읽어 예전 문서를 고치면
    /// 같은 파일이 빌드마다 다르게 변환된다.
    ///
    /// <b>변환은 전부 되거나 전혀 안 된다.</b> 단계는 문서를 제자리에서 고치므로 세 칸 중 두 번째에서
    /// 실패하면 반쯤 바뀐 문서가 남는데, 그것을 호출부에 보이지 않게 하려고 단계는 <b>작업 사본</b>
    /// 위에서만 돌고 성공했을 때만 그 결과가 호출부의 문서로 옮겨진다. 그래서 실패한 뒤의 문서는
    /// 변환을 시도하기 전과 완전히 같고, 실패 결과에는 <see cref="SaveLoadResult.Data"/>도 없어
    /// 호출부가 저장까지 막아 원본 파일을 지킨다.
    /// </summary>
    public sealed class SaveMigrationRunner
    {
        /// <summary>실제 게임이 쓰는 러너. 등록된 단계는 <see cref="CreateDefaultSteps"/>에 있다.</summary>
        public static SaveMigrationRunner Default { get; } = new SaveMigrationRunner();

        private readonly Dictionary<int, ISaveMigrationStep> stepsByFromVersion =
            new Dictionary<int, ISaveMigrationStep>();

        /// <summary>이 러너가 도달시키려는 버전.</summary>
        public int TargetVersion { get; }

        public SaveMigrationRunner()
            : this(CreateDefaultSteps(), SaveData.CurrentSaveVersion)
        {
        }

        /// <param name="steps">등록할 단계들. 한 칸(From+1 == To)이 아니거나 시작 버전이 겹치면
        /// <see cref="ArgumentException"/>을 던진다 - 잘못 등록한 표를 들고 조용히 돌기 시작하면
        /// 저장 파일이 실제로 망가진 뒤에야 알게 된다.</param>
        /// <param name="targetVersion">도달할 버전.</param>
        public SaveMigrationRunner(IEnumerable<ISaveMigrationStep> steps, int targetVersion)
        {
            TargetVersion = targetVersion;

            if (steps == null) return;

            foreach (ISaveMigrationStep step in steps)
            {
                if (step == null) throw new ArgumentException("마이그레이션 단계 목록에 null이 있습니다.", nameof(steps));

                if (step.ToVersion != step.FromVersion + 1)
                {
                    throw new ArgumentException(
                        $"마이그레이션 단계는 한 칸씩만 올릴 수 있습니다: v{step.FromVersion} -> v{step.ToVersion}",
                        nameof(steps));
                }

                if (stepsByFromVersion.ContainsKey(step.FromVersion))
                {
                    throw new ArgumentException(
                        $"v{step.FromVersion}에서 시작하는 단계가 둘 이상입니다.", nameof(steps));
                }

                stepsByFromVersion.Add(step.FromVersion, step);
            }
        }

        /// <summary>실제 게임이 쓰는 단계 표. 버전을 올릴 때마다 여기에 한 줄이 늘어난다.</summary>
        public static IEnumerable<ISaveMigrationStep> CreateDefaultSteps()
        {
            return new ISaveMigrationStep[]
            {
                new UnversionedToV1Step(),
                new V1ToV2Step(),
            };
        }

        /// <summary>
        /// <paramref name="data"/>를 <paramref name="fromVersion"/>에서 <see cref="TargetVersion"/>까지
        /// 한 칸씩 올린다.
        ///
        /// <b>단계는 호출부의 문서가 아니라 작업 사본 위에서 돈다.</b> 단계는 문서를 제자리에서 고치게
        /// 돼 있어서, 세 칸 중 두 번째에서 실패하면 호출부가 들고 있던 문서가 이미 반쯤 바뀐 상태로
        /// 남는다 - 그 문서로 한 번만 저장돼도 원본 파일은 되돌릴 수 없다. 그래서 성공했을 때만
        /// 사본의 내용을 호출부의 문서에 옮겨 담고, 실패하면(단계 없음/단계 예외/미래 버전) 호출부의
        /// 문서는 <b>필드 하나까지 그대로</b> 둔 채 결과만 알린다.
        ///
        /// 사본의 <see cref="SaveData.saveVersion"/>을 먼저 <paramref name="fromVersion"/>으로 덮어쓰는
        /// 것이 중요하다 - 역직렬화된 문서의 그 필드는 <b>믿을 수 없는 값</b>(버전 필드가 없던 파일은
        /// 필드 기본값이 들어간다)이고, 파일의 진짜 버전을 아는 것은 원문을 훑은 호출부뿐이다.
        /// </summary>
        public SaveMigrationResult Migrate(SaveData data, int fromVersion)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            if (fromVersion < SaveData.UnversionedSaveVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(fromVersion), fromVersion,
                    "저장 버전은 음수가 될 수 없습니다.");
            }

            if (fromVersion > TargetVersion)
            {
                return new SaveMigrationResult(SaveMigrationOutcome.FutureVersion, fromVersion, fromVersion,
                    $"파일 버전 v{fromVersion}이 이 빌드의 v{TargetVersion}보다 새롭습니다.");
            }

            SaveData working = CopyOf(data);
            working.saveVersion = fromVersion;

            int current = fromVersion;

            while (current < TargetVersion)
            {
                if (!stepsByFromVersion.TryGetValue(current, out ISaveMigrationStep step))
                {
                    return new SaveMigrationResult(SaveMigrationOutcome.StepMissing, fromVersion, current,
                        $"v{current}에서 v{current + 1}로 올리는 단계가 등록돼 있지 않습니다.");
                }

                try
                {
                    step.Apply(working);
                }
                catch (Exception e)
                {
                    return new SaveMigrationResult(SaveMigrationOutcome.StepFailed, fromVersion, current,
                        $"v{current} -> v{current + 1} 단계에서 예외: {e.Message}");
                }

                current = step.ToVersion;
                working.saveVersion = current;
            }

            // 여기까지 왔으면 모든 칸이 끝났다. 이제서야 호출부의 문서에 결과를 옮긴다.
            CopyInto(working, data);

            SaveMigrationOutcome outcome = current == fromVersion
                ? SaveMigrationOutcome.AlreadyCurrent
                : SaveMigrationOutcome.Migrated;

            return new SaveMigrationResult(outcome, fromVersion, current, null);
        }

        /// <summary>
        /// 변환용 작업 사본을 만든다. 목록과 그 안의 항목까지 새로 만드는 <b>깊은 사본</b>이어야 한다 -
        /// 목록만 얕게 복사하면 단계가 항목 하나를 고치는 순간 호출부의 문서도 같이 바뀐다.
        ///
        /// <see cref="SaveData"/>에 필드를 추가하면 <see cref="CopyInto"/>에도 반드시 추가해야 하며,
        /// 빠뜨리면 그 필드가 변환을 거치며 조용히 기본값으로 되돌아간다. 그 사고를 막기 위해 시험이
        /// 두 클래스의 필드 목록을 대조한다(빠뜨리면 시험이 먼저 실패한다).
        /// </summary>
        private static SaveData CopyOf(SaveData source)
        {
            SaveData copy = new SaveData();
            CopyInto(source, copy);
            return copy;
        }

        private static void CopyInto(SaveData source, SaveData target)
        {
            target.saveVersion = source.saveVersion;
            target.saveRevision = source.saveRevision;
            target.lastSavedAtUtc = source.lastSavedAtUtc;

            target.currentLevel = source.currentLevel;
            target.currentExp = source.currentExp;
            target.totalKillCount = source.totalKillCount;
            target.currency = source.currency;

            target.characters = CopyCharacters(source.characters);
            target.items = CopyItems(source.items);
            target.recoverySlots = CopyRecoverySlots(source.recoverySlots);
        }

        private static List<CharacterSaveState> CopyCharacters(List<CharacterSaveState> source)
        {
            List<CharacterSaveState> copy = new List<CharacterSaveState>();
            if (source == null) return copy;

            foreach (CharacterSaveState item in source)
            {
                // null 항목도 그대로 옮긴다 - 여기서 치우면 "변환은 값을 바꾸지 않는다"가 깨지고,
                // 목록 보정은 SaveDataNormalizer 한 곳의 일이다.
                copy.Add(item == null
                    ? null
                    : new CharacterSaveState
                    {
                        characterId = item.characterId,
                        level = item.level,
                        currentStamina = item.currentStamina,
                    });
            }

            return copy;
        }

        private static List<InventoryItemState> CopyItems(List<InventoryItemState> source)
        {
            List<InventoryItemState> copy = new List<InventoryItemState>();
            if (source == null) return copy;

            foreach (InventoryItemState item in source)
            {
                copy.Add(item == null
                    ? null
                    : new InventoryItemState
                    {
                        itemId = item.itemId,
                        count = item.count,
                    });
            }

            return copy;
        }

        private static List<RecoverySlotSaveState> CopyRecoverySlots(List<RecoverySlotSaveState> source)
        {
            List<RecoverySlotSaveState> copy = new List<RecoverySlotSaveState>();
            if (source == null) return copy;

            foreach (RecoverySlotSaveState slot in source)
            {
                copy.Add(slot == null
                    ? null
                    : new RecoverySlotSaveState
                    {
                        characterId = slot.characterId,
                        startStamina = slot.startStamina,
                        startedAtUtc = slot.startedAtUtc,
                        completeAtUtc = slot.completeAtUtc,
                        completionNotified = slot.completionNotified,
                    });
            }

            return copy;
        }

        /// <summary>
        /// 저장 원문 한 덩어리를 "바로 쓸 수 있는 결과"로 바꾼다. 훑기 → 역직렬화 → 변환 → 정규화의
        /// 순서가 여기 한 곳에만 있고, 호출부(저장소)는 파일을 읽어 문자열을 건네는 일만 한다.
        /// </summary>
        /// <param name="json">파일에서 읽은 원문. null이나 공백이면 새 게임으로 본다.</param>
        /// <param name="deserialize">문자열을 <see cref="SaveData"/>로 바꾸는 함수(보통
        /// <c>JsonUtility.FromJson&lt;SaveData&gt;</c>). <b>주입받는 이유</b>는 이 클래스를 UnityEngine에
        /// 묶지 않기 위해서다 - 그래야 변환 규칙 시험이 엔진 없이 순수하게 돌아간다.</param>
        public SaveLoadResult Load(string json, Func<string, SaveData> deserialize)
        {
            if (deserialize == null) throw new ArgumentNullException(nameof(deserialize));

            SaveVersionProbeResult probe = SaveVersionProbe.Probe(json);

            switch (probe.Status)
            {
                case SaveVersionProbeStatus.Empty:
                    return NewGame();

                case SaveVersionProbeStatus.Malformed:
                    return SaveLoadResult.CorruptFallback(
                        SaveDataNormalizer.Normalize(new SaveData()), "JSON 형식이 아닙니다.");
            }

            // 미래 버전은 <b>역직렬화조차 하지 않는다</b>. 모르는 형식을 지금 형식으로 읽어 들이면 그
            // 순간 모르는 필드가 버려지고, 그 상태로 한 번만 저장돼도 원본은 되돌릴 수 없다.
            if (probe.Version > TargetVersion)
            {
                return SaveLoadResult.FutureVersionBlocked(probe.Version, TargetVersion);
            }

            SaveData data;
            try
            {
                data = deserialize(json);
            }
            catch (Exception e)
            {
                return SaveLoadResult.CorruptFallback(
                    SaveDataNormalizer.Normalize(new SaveData()), $"역직렬화 실패: {e.Message}");
            }

            if (data == null)
            {
                return SaveLoadResult.CorruptFallback(
                    SaveDataNormalizer.Normalize(new SaveData()), "역직렬화 결과가 비었습니다.");
            }

            SaveMigrationResult migration = Migrate(data, probe.Version);

            switch (migration.Outcome)
            {
                case SaveMigrationOutcome.AlreadyCurrent:
                    return SaveLoadResult.Loaded(SaveDataNormalizer.Normalize(data), migration.ReachedVersion);

                case SaveMigrationOutcome.Migrated:
                    return SaveLoadResult.Migrated(
                        SaveDataNormalizer.Normalize(data), migration.FromVersion, migration.ReachedVersion);

                case SaveMigrationOutcome.FutureVersion:
                    return SaveLoadResult.FutureVersionBlocked(migration.FromVersion, TargetVersion);

                default:
                    return SaveLoadResult.MigrationFailed(
                        migration.FromVersion, migration.ReachedVersion, migration.FailureReason);
            }
        }

        /// <summary>저장 파일이 아예 없을 때 쓸 결과. 기본값 문서를 현재 버전으로 만들어 준다.</summary>
        public SaveLoadResult NewGame()
        {
            SaveData data = SaveDataNormalizer.Normalize(new SaveData());
            data.saveVersion = TargetVersion;
            return SaveLoadResult.NewGame(data);
        }
    }

    /// <summary>
    /// 버전 필드가 없던 파일(v0)을 v1으로 올린다.
    ///
    /// <b>값은 하나도 바꾸지 않는다.</b> v1이 v0에 더한 것은 메타데이터(형식 번호, 저장 일련번호,
    /// 저장 시각)뿐이고 진행 값의 의미는 전혀 달라지지 않았다 - 그래서 이 단계가 하는 일은 "없던
    /// 메타데이터에 '모름'에 해당하는 값을 넣는 것"이 전부다. 일련번호 0과 빈 시각이 곧 "언제 저장된
    /// 문서인지 알 수 없음"이며, 그것이 실제로 참이다(예전 파일에는 그 정보가 없었다).
    ///
    /// 목록 보정(null 목록, 항목 안의 null, 최소 회복 슬롯)은 여기서 하지 않는다 - 그것은 v0만의 문제가
    /// 아니라 어느 버전에서나 생길 수 있는 모양의 문제라 <see cref="SaveDataNormalizer"/>가 모든 경로에
    /// 대해 한 번씩 처리한다. 여기서 겹쳐서 하면 규칙이 두 곳에 생긴다.
    /// </summary>
    public sealed class UnversionedToV1Step : ISaveMigrationStep
    {
        public int FromVersion => SaveData.UnversionedSaveVersion;

        public int ToVersion => 1;

        public void Apply(SaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            data.saveRevision = 0;
            data.lastSavedAtUtc = null;
        }
    }

    /// <summary>
    /// v1 문서를 v2로 올린다. 하는 일은 하나다 - <b>그 시절 쓸 수 있던 캐릭터 여섯을
    /// <see cref="SaveData.characters"/>에 빠짐없이 남긴다.</b>
    ///
    /// <b>왜 필요한가.</b> v2에서 <see cref="SaveData.characters"/>의 항목은 곧 <b>보유</b>다 - 항목이
    /// 없는 캐릭터는 가지고 있지 않은 캐릭터다. v1에는 그 뜻이 없었다: 항목이 있든 없든 여섯 캐릭터를
    /// 모두 쓸 수 있었고 항목은 <b>실제로 한 번 써 본 뒤에야</b> 생겼다. 그래서 v1 문서를 그대로 v2의
    /// 규칙으로 읽으면 <b>한 번도 써 보지 않은 캐릭터가 미보유가 되어</b> 가진 것이 줄어든다. 이 단계가
    /// 그 여섯을 보유로 확정해 그 일을 막는다.
    ///
    /// <b>여섯 id를 이 클래스 안에 글자 그대로 박아 둔다.</b> 이 단계는 Unity 에셋도 CharacterCatalog도
    /// 읽지 않는다 - 변환은 "그때 무엇이 있었는가"라는 <b>역사적 사실</b>을 옮기는 일인데, 지금의 표를
    /// 읽으면 나중에 캐릭터를 추가하거나 지우는 순간 <b>예전 파일의 변환 결과가 함께 바뀐다</b>.
    /// 그러면 같은 저장 파일을 다른 빌드에서 열 때마다 다른 결과가 나온다. 표가 아니라 상수여야 하는
    /// 이유가 이것이다(러너를 엔진에 묶지 않는다는 기존 규칙과도 같은 방향이다).
    ///
    /// 그 목록과 기본값은 <b>바깥에 내보내지 않는다</b>. 이것은 이 한 칸의 <b>구현 세부</b>이지 다른
    /// 코드가 읽고 쓸 계약이 아니다 - 내보내면 "지금 지급해야 할 캐릭터 목록"으로 오해돼 여기저기서
    /// 참조되기 시작하고, 그러면 <b>역사적 사실이어야 할 값이 현재 밸런스처럼 고쳐진다</b>. 확인이
    /// 필요한 쪽(시험)은 이 필드를 들여다보는 대신 <b>변환 결과</b>를 본다.
    ///
    /// <b>기존 항목은 한 글자도 건드리지 않는다.</b> 순서, id, 레벨, 행동력, 중복 항목, null 항목까지
    /// 그대로 두고 <b>없는 것만 뒤에 덧붙인다</b>. 특히 대소문자를 맞추거나 중복을 합치지 않는다 -
    /// 'barbarian'과 'Barbarian'은 다른 키이고, 둘을 합치는 판단은 변환이 할 일이 아니다(합쳐 놓으면
    /// 어느 쪽 진행이 사라졌는지 아무도 모른다). 목록 모양 보정은 지금까지대로
    /// <see cref="SaveDataNormalizer"/> 한 곳의 몫이다.
    /// </summary>
    public sealed class V1ToV2Step : ISaveMigrationStep
    {
        /// <summary>
        /// v1 시절 모두가 쓸 수 있던 여섯 캐릭터의 id. <b>철자와 대소문자가 저장 키 그 자체</b>라
        /// 한 글자도 바꿀 수 없다(CharacterDefinition.CharacterId와 같은 값).
        ///
        /// 덧붙이는 차례도 이 배열의 순서다 - 변환 결과가 실행할 때마다 달라지지 않으려면 순서가
        /// 코드에 적혀 있어야 한다. <b>바깥에 내보내지 않는다</b>(클래스 설명 참고).
        /// </summary>
        private static readonly string[] LegacyCharacterIds =
        {
            "Barbarian",
            "CatKnight",
            "CatMage",
            "ElfArcher",
            "ElfGuardian",
            "RabbitHealer",
        };

        /// <summary>덧붙이는 항목의 레벨. 새 캐릭터의 기본값과 같다.</summary>
        private const int AppendedLevel = 1;

        /// <summary>덧붙이는 항목의 현재 행동력. <b>-1은 "아직 초기화되지 않음"</b>이라 실행 시점에
        /// 정의의 Max Stamina로 채워진다 - 0(소진)으로 적으면 예전 사용자가 행동력이 빈 채로 시작한다.</summary>
        private const int AppendedCurrentStamina = -1;

        public int FromVersion => 1;

        public int ToVersion => 2;

        public void Apply(SaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            List<CharacterSaveState> characters = data.characters;

            // 이미 적혀 있는 id를 모은다. 비교는 <b>Ordinal 완전 일치</b>다 - 대소문자를 맞추면
            // 'barbarian'이 'Barbarian'을 있는 것으로 만들어, 실제로는 없던 항목이 조용히 빠진다.
            HashSet<string> present = new HashSet<string>(StringComparer.Ordinal);

            if (characters != null)
            {
                foreach (CharacterSaveState state in characters)
                {
                    // null 항목과 id 없는 항목은 아무것도 가리키지 않으므로 "있다"의 근거가 될 수 없다.
                    // 그렇다고 지우지도 않는다 - 목록을 손보는 것은 이 단계의 일이 아니다.
                    if (state == null) continue;
                    if (string.IsNullOrEmpty(state.characterId)) continue;

                    present.Add(state.characterId);
                }
            }

            foreach (string id in LegacyCharacterIds)
            {
                if (!present.Add(id)) continue;

                // 목록이 없던 문서에서만, 그리고 실제로 덧붙일 것이 생겼을 때만 만든다.
                if (characters == null) characters = data.characters = new List<CharacterSaveState>();

                characters.Add(new CharacterSaveState
                {
                    characterId = id,
                    level = AppendedLevel,
                    currentStamina = AppendedCurrentStamina,
                });
            }
        }
    }
}
