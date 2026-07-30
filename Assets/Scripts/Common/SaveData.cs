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
