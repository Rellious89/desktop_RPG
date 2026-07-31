using System;
using System.Collections.Generic;

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
        public int currentLevel = 1;
        public int currentExp = 0;
        public int totalKillCount = 0;

        /// <summary>캐릭터별 진행 상태. 캐릭터 정의(CharacterDefinition)가 존재하는데 여기에 항목이
        /// 없으면 CharacterRoster가 정의의 기본값으로 새 항목을 만든다 - 캐릭터를 나중에 추가해도
        /// 기존 저장 파일이 그대로 유효하다.</summary>
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

        /// <summary>현재 행동력. -1은 "아직 한 번도 초기화되지 않음"을 뜻하며, CharacterRoster가
        /// 정의의 Max Stamina로 채운다 - 0(행동력 소진)과 구분하기 위해 0이 아닌 값을 쓴다.</summary>
        public int currentStamina = -1;
    }
}
