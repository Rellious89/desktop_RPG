using System;
using System.Collections.Generic;
using System.Globalization;

namespace Common
{
    /// <summary>
    /// 로컬에 저장하는 플레이어 진행도 전체. 필드를 늘려야 하면 여기에만 추가하면 된다.
    /// 세션 킬카운트, 콤보, 내구도, 공격/애니메이션 상태처럼 그때그때 휘발되는 값은 포함하지 않는다.
    /// 필드 기본값은 저장 파일이 없거나 일부 필드가 누락됐을 때 쓰는 새 게임 기본값과 같다.
    ///
    /// 이 클래스의 인스턴스는 <see cref="SaveSystem.Data"/> 하나뿐이다 - 여러 시스템
    /// (PlayerProgress, CharacterRoster, InventoryManager)이 각자 새 SaveData를 만들어 저장하면
    /// 서로의 필드를 덮어써서 지우기 때문에, 모두 같은 문서를 고쳐 쓰고 SaveSystem.Save()로 기록한다.
    /// 필드마다 소유자가 하나씩 정해져 있고, 다른 시스템은 남의 필드를 읽거나 쓰지 않는다.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        /// <summary>지금 코드가 쓰고 읽는 저장 형식 번호. 형식을 바꿀 때(필드 의미 변경, 값 이동,
        /// 단위 변경처럼 <b>예전 파일을 그대로 읽으면 틀리는</b> 변경) 이 값을 1 올리고
        /// <see cref="SaveMigrationRunner"/>에 그 한 단계를 등록한다. 필드를 새로 <i>추가</i>하기만
        /// 할 때는 올리지 않는다 - JsonUtility가 없는 필드를 기본값으로 채우므로 예전 파일이 그대로
        /// 유효하다.</summary>
        public const int CurrentSaveVersion = 2;

        /// <summary>버전 필드가 아예 없던 시절의 저장 파일 번호. 파일 안에 <c>saveVersion</c> 항목이
        /// 없으면(빈 객체 <c>{}</c> 포함) 그 파일은 이 버전이다 - 없는 것 자체가 곧 표식이라
        /// 예전 파일에 뭔가를 덧붙일 필요가 없다.</summary>
        public const int UnversionedSaveVersion = 0;

        /// <summary>버전을 아직/영영 알 수 없음(빈 내용이거나 JSON이 깨졌을 때). 파일에 기록되는 값이
        /// 아니라 <see cref="SaveVersionProbe"/>의 반환값 자리를 채우는 표식이다.</summary>
        public const int UnknownSaveVersion = -1;

        /// <summary>이 문서가 기록될 당시의 저장 형식 번호.
        ///
        /// <b>불러올 때 이 필드를 믿으면 안 된다.</b> 버전 필드가 없던 예전 파일을 역직렬화하면
        /// JsonUtility가 여기에 아래 기본값(= 현재 버전)을 넣어 버려서, v0 파일이 "이미 최신"으로
        /// 보인다. 그래서 파일의 진짜 버전은 <see cref="SaveVersionProbe"/>가 <b>역직렬화 전에</b>
        /// 원문에서 직접 읽고, <see cref="SaveMigrationRunner"/>가 그 값을 이 필드에 덮어쓴다.
        /// 기본값이 현재 버전인 이유는 새 게임(파일 없음)이 곧 최신 형식이기 때문이다.</summary>
        public int saveVersion = CurrentSaveVersion;

        /// <summary>저장할 때마다 1씩 오르는 일련번호. 같은 버전 안에서 어느 쪽이 더 나중 문서인지
        /// 가르는 값이며, 시계가 뒤로 가도(수동 변경, 기기 이동) 순서가 뒤집히지 않는다는 점에서
        /// <see cref="lastSavedAtUtc"/>보다 믿을 수 있다. 예전 파일에는 이 항목이 없으므로 0으로
        /// 읽히고, 그것이 곧 "몇 번 저장됐는지 모름"이다.</summary>
        public long saveRevision = 0;

        /// <summary>마지막 저장 시각(UTC). 회복 슬롯의 시각 필드와 <b>같은 서식</b>인 ISO-8601 왕복
        /// 문자열("o", InvariantCulture)이며, 비어 있으면 "모름"이다 - 저장 파일 안에서 시각을 적는
        /// 방법이 하나뿐이어야 읽는 쪽이 필드마다 다른 규칙을 기억하지 않는다.</summary>
        public string lastSavedAtUtc;

        public int currentLevel = 1;
        public int currentExp = 0;
        public int totalKillCount = 0;

        /// <summary>
        /// 보유 캐릭터 목록. <b>v2에서 이 목록의 뜻은 하나로 고정됐다</b> - 항목 하나는 <b>동시에 두
        /// 가지</b>를 말한다.
        ///
        /// <list type="number">
        ///   <item>플레이어가 <b>그 characterId를 가지고 있다</b>(보유).</item>
        ///   <item>그 캐릭터의 진행 상태(<see cref="CharacterSaveState.level"/>,
        ///         <see cref="CharacterSaveState.currentExp"/>,
        ///         <see cref="CharacterSaveState.currentStamina"/>)가 이 값이다.</item>
        /// </list>
        ///
        /// 둘은 나뉘지 않는다. 보유를 적는 별도의 목록이나 플래그는 없으며, <b>항목의 존재 자체가
        /// 보유</b>다. id 비교는 <see cref="StringComparer.Ordinal"/> 완전 일치이므로
        /// 'Barbarian'과 'barbarian'은 서로 다른 캐릭터다.
        ///
        /// 그래서 카탈로그(게임에 존재하는 캐릭터 전체)와 이 목록의 관계는 이렇게 갈린다.
        ///
        /// <list type="bullet">
        ///   <item><b>카탈로그에 있고 여기에 없다 → 미보유.</b> 아직 얻지 않은 캐릭터이며, 그 사실을
        ///         적기 위해 항목을 만들지 않는다(없다는 것이 곧 표식이다).</item>
        ///   <item><b>여기에 있고 카탈로그에 없다 → 값은 보존하되 지금 빌드에서는 쓸 수 없다.</b>
        ///         카탈로그에서 빠졌거나 아직 들어오지 않은 id이며, 항목을 지우면 그 캐릭터가 다시
        ///         들어왔을 때 레벨과 행동력이 사라진다. 저장 계층은 <b>모르는 id도 그대로 남긴다</b>.</item>
        /// </list>
        ///
        /// <b>목록에 항목을 더하는 것은 곧 캐릭터를 지급하는 것</b>이므로, 저장 계층에서 항목이 늘어나는
        /// 경로는 <see cref="V1ToV2Step"/>이 예전 문서를 올릴 때 한 번뿐이다 - 그 단계가 v1 시절 모두가
        /// 쓸 수 있던 여섯 캐릭터를 보유로 확정해, 버전이 올라가면서 가진 것이 줄어드는 일이 없게 한다.
        /// </summary>
        public List<CharacterSaveState> characters = new List<CharacterSaveState>();

        /// <summary>보유 재화(전역 값 하나). 아이템 목록과 완전히 별개이며 아이템 슬롯에 표시하지
        /// 않는다. 경험치/레벨/행동력과도 아무 관계가 없다. 인벤토리 데이터가 없는 예전 저장 파일을
        /// 읽으면 이 기본값 0이 그대로 쓰인다.</summary>
        public int currency = 0;

        /// <summary>보유 아이템 목록. 같은 아이템이 두 항목으로 나뉘지 않고 하나의 항목에 수량으로
        /// 누적된다. <b>목록 순서가 곧 획득 순서이자 인벤토리 표시 순서</b>다 - 처음 획득할 때 뒤에
        /// 추가되고 그 뒤로 자리가 바뀌지 않으므로, 저장/불러오기를 거쳐도 표시 순서가 유지된다.</summary>
        public List<InventoryItemState> items = new List<InventoryItemState>();

        /// <summary>회복소 슬롯. <b>목록의 인덱스가 곧 슬롯 번호</b>이며, 비어 있는 슬롯도 항목을
        /// 유지한다(목록을 줄이면 번호가 밀려 다른 슬롯의 진행이 뒤바뀐다). 회복소 기능이 없던 예전
        /// 저장 파일에는 이 항목 자체가 없으므로 <see cref="EnsureRecoverySlots"/>가 빈 슬롯
        /// <see cref="DefaultRecoverySlotCount"/>개로 맞춘다.
        ///
        /// 아직 시작하지 않은 대기(PendingRecovery)는 여기에 들어가지 않는다 - 재화를 내고 실제로
        /// 시간이 흐르기 시작한 슬롯만 저장한다.</summary>
        public List<RecoverySlotSaveState> recoverySlots = new List<RecoverySlotSaveState>();

        /// <summary>저장 계층이 보장하는 최소 슬롯 수. 실제 사용 가능한 슬롯 수는 회복 밸런스 테이블의
        /// Max Slots가 정하며, 그 값이 더 크면 회복소가 목록을 더 늘린다.</summary>
        public const int DefaultRecoverySlotCount = 3;

        /// <summary>
        /// 슬롯 목록을 항상 "null 없는 <paramref name="minimumCount"/>개 이상"으로 맞춘다. 저장 파일에
        /// 항목이 없거나(예전 파일), 일부가 null이거나, 슬롯 수를 늘린 경우를 <b>예외 없이</b> 흡수한다.
        /// 이미 있는 슬롯은 잘라내지 않는다 - Max Slots를 나중에 줄여도 회복 중이던 저장 값을 지우지
        /// 않기 위함이다.
        /// </summary>
        public static void EnsureRecoverySlots(SaveData data, int minimumCount = DefaultRecoverySlotCount)
        {
            if (data == null) return;
            if (minimumCount < DefaultRecoverySlotCount) minimumCount = DefaultRecoverySlotCount;

            if (data.recoverySlots == null)
            {
                data.recoverySlots = new List<RecoverySlotSaveState>();
            }

            for (int i = 0; i < data.recoverySlots.Count; i++)
            {
                if (data.recoverySlots[i] == null) data.recoverySlots[i] = new RecoverySlotSaveState();
            }

            while (data.recoverySlots.Count < minimumCount)
            {
                data.recoverySlots.Add(new RecoverySlotSaveState());
            }
        }

        /// <summary><see cref="lastSavedAtUtc"/>가 쓰는 시각 서식. 회복 슬롯의 시각 필드와 같다.</summary>
        public const string TimestampFormat = "o";

        /// <summary>
        /// 파일에 쓰기 <b>직전에</b> 메타데이터를 찍고, 찍기 전 값을 돌려준다. 저장 형식 번호를 현재
        /// 값으로 맞추고, 일련번호를 1 올리고, 시각을 기록한다.
        ///
        /// <b>반환값을 버리지 말 것.</b> 쓰기가 실패하면 호출부는 그 값을
        /// <see cref="RestoreMetadata"/>에 넘겨 메모리 상태를 되돌려야 한다 - 되돌리지 않으면 디스크에
        /// 있는 문서보다 메모리 쪽 일련번호가 앞서게 되고, "파일과 메모리 중 어느 쪽이 최신인가"를
        /// 일련번호로 가리는 곳이 전부 틀린 답을 얻는다.
        /// </summary>
        /// <param name="nowUtc">기록할 시각. 시계를 직접 읽지 않고 받는 이유는 시험이 고정된 값을 넣어
        /// 결과를 확인할 수 있게 하기 위함이다. Kind가 Local이면 UTC로 바꾸고, 지정되지 않았으면 이미
        /// UTC인 것으로 본다.</param>
        public static SaveMetadataSnapshot MarkSaved(SaveData data, DateTime nowUtc)
        {
            if (data == null) return default;

            SaveMetadataSnapshot before = SaveMetadataSnapshot.Capture(data);

            data.saveVersion = CurrentSaveVersion;
            if (data.saveRevision < 0) data.saveRevision = 0;
            if (data.saveRevision < long.MaxValue) data.saveRevision++;
            data.lastSavedAtUtc = FormatTimestamp(nowUtc);

            return before;
        }

        /// <summary>저장이 실패했을 때 <see cref="MarkSaved"/>가 찍기 전 메타데이터로 되돌린다.
        /// 저장 <b>내용</b>은 되돌리지 않는다 - 실패한 것은 기록일 뿐 플레이어의 진행이 아니다.</summary>
        public static void RestoreMetadata(SaveData data, SaveMetadataSnapshot snapshot)
        {
            if (data == null) return;

            data.saveVersion = snapshot.SaveVersion;
            data.saveRevision = snapshot.SaveRevision;
            data.lastSavedAtUtc = snapshot.LastSavedAtUtc;
        }

        /// <summary>시각을 <see cref="lastSavedAtUtc"/> 서식(ISO-8601 UTC 왕복 문자열)으로 만든다.</summary>
        public static string FormatTimestamp(DateTime value)
        {
            DateTime utc;
            if (value.Kind == DateTimeKind.Local) utc = value.ToUniversalTime();
            else if (value.Kind == DateTimeKind.Unspecified) utc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
            else utc = value;

            return utc.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 저장 직전 메타데이터의 사본. 쓰기가 실패했을 때 <see cref="SaveData.RestoreMetadata"/>로 되돌리기
    /// 위한 것이며, 진행 데이터는 담지 않는다(되돌릴 이유가 없다).
    /// </summary>
    public readonly struct SaveMetadataSnapshot
    {
        public int SaveVersion { get; }
        public long SaveRevision { get; }
        public string LastSavedAtUtc { get; }

        private SaveMetadataSnapshot(int saveVersion, long saveRevision, string lastSavedAtUtc)
        {
            SaveVersion = saveVersion;
            SaveRevision = saveRevision;
            LastSavedAtUtc = lastSavedAtUtc;
        }

        public static SaveMetadataSnapshot Capture(SaveData data)
        {
            if (data == null) return default;
            return new SaveMetadataSnapshot(data.saveVersion, data.saveRevision, data.lastSavedAtUtc);
        }
    }

    /// <summary>
    /// 회복 슬롯 한 칸의 저장 상태. 캐릭터가 없으면 빈 슬롯이다.
    ///
    /// <b>진행률은 저장하지 않는다.</b> 지금 몇까지 찼는지는 <see cref="startedAtUtc"/>와 밸런스
    /// 테이블만으로 다시 계산할 수 있고, 그래야 앱을 꺼 둔 동안 흐른 시간이 그대로 반영된다
    /// (Time.time이나 코루틴 누적값은 앱이 꺼지면 사라지므로 근거가 될 수 없다).
    ///
    /// 완료 여부도 저장하지 않는다 - <see cref="completeAtUtc"/>와 현재 시각의 비교로 파생된다.
    /// 상태 문자열을 따로 저장하면 실제 시각과 어긋난 상태가 파일에 남는다.
    /// </summary>
    [Serializable]
    public class RecoverySlotSaveState
    {
        /// <summary>CharacterDefinition.CharacterId와 같은 값. 비어 있으면 빈 슬롯이다.</summary>
        public string characterId;

        /// <summary>회복을 시작한 순간의 현재 행동력. 지금 값 = min(최대, 이 값 + 경과 단계).</summary>
        public int startStamina;

        /// <summary>회복 시작 시각(UTC). ISO-8601 왕복 서식("o")에 InvariantCulture로 기록하므로
        /// 사용자의 지역/언어 설정이 달라도 같은 값으로 다시 읽힌다.</summary>
        public string startedAtUtc;

        /// <summary>회복 완료 예정 시각(UTC). 서식은 <see cref="startedAtUtc"/>와 같다.</summary>
        public string completeAtUtc;

        /// <summary>
        /// <b>이번 회복 주기</b>의 완료 알림을 이미 요청했는지. 알림을 매 프레임/재시작마다 반복하지
        /// 않기 위한 표시이며, 슬롯 하나가 곧 회복 주기 하나이므로 이 값이 per-cycle marker다.
        ///
        /// 회복을 새로 시작할 때와 슬롯을 비울 때(합류) 모두 false로 초기화되므로, 같은 슬롯에서 다음
        /// 주기가 시작되면 다시 한 번 알린다.
        ///
        /// <b>예전 저장 파일에는 이 필드가 없다.</b> JsonUtility는 그 경우 false를 넣는데, 그것이 곧
        /// "아직 알리지 않음"이라 완료 상태로 저장돼 있던 슬롯이 다음 실행에서 정확히 한 번 알림을
        /// 받는다 - 기본값이 안전한 쪽이다.
        /// </summary>
        public bool completionNotified;

        public bool HasCharacter => !string.IsNullOrEmpty(characterId);

        public void Clear()
        {
            characterId = null;
            startStamina = 0;
            startedAtUtc = null;
            completeAtUtc = null;
            completionNotified = false;
        }
    }

    /// <summary>
    /// 아이템 한 종의 보유 상태. 아이템 이름/아이콘 같은 정의 값은 여기에 저장하지 않는다 - 그것들은
    /// ItemDefinition 에셋이 소유하고, 저장 파일에는 그 에셋을 가리키는 id와 수량만 남는다.
    /// </summary>
    [Serializable]
    public class InventoryItemState
    {
        /// <summary>ItemDefinition.ItemId와 같은 값. 이 문자열이 저장 항목의 유일한 키다.</summary>
        public string itemId;

        public int count;
    }

    /// <summary>
    /// 캐릭터 한 명의 저장 상태. <b>캐릭터 레벨과 계정 경험치는 별개다</b> - 여기 있는 level은 캐릭터 개별
    /// 성장치이며, PlayerProgress의 계정 레벨/경험치와는 아무 관계가 없다(값을 서로 복사하지 않는다).
    /// </summary>
    [Serializable]
    public class CharacterSaveState
    {
        /// <summary>CharacterDefinition.CharacterId와 같은 값. 이 문자열이 저장 항목의 유일한 키다.</summary>
        public string characterId;

        public int level = 1;

        /// <summary>
        /// 지금 레벨에서 <b>다음 레벨까지 모아 둔 경험치</b>. 누적 총량이 아니라 <b>이번 레벨 안에서의
        /// 진행</b>이며, 레벨이 오를 때마다 필요량만큼 빠지고 나머지가 이월된다.
        ///
        /// <b>이 칸이 없던 저장 파일은 0으로 읽힌다.</b> JsonUtility가 없는 필드를 기본값으로 채우고,
        /// 0은 "이번 레벨에서 아직 아무것도 모으지 않았다"는 뜻이라 그대로 옳은 값이다 - 그래서 이
        /// 칸을 더하면서 저장 형식 번호를 올리지 않았다(<see cref="SaveData.CurrentSaveVersion"/> 참고).
        /// </summary>
        public int currentExp = 0;

        /// <summary>현재 행동력. -1은 "아직 한 번도 초기화되지 않음"을 뜻하며, CharacterRoster가
        /// 정의의 Max Stamina로 채운다 - 0(행동력 소진)과 구분하기 위해 0이 아닌 값을 쓴다.</summary>
        public int currentStamina = -1;
    }
}
