using System.Collections.Generic;
using Character;

namespace Recovery
{
    /// <summary>
    /// 회복소가 캐릭터 쪽에 요구하는 <b>최소한의</b> 창구. 실제 구현은 CharacterRoster 하나뿐이고,
    /// 검증 하네스는 씬 없이 같은 규칙을 확인하기 위해 가짜 구현을 쓴다.
    ///
    /// <b>행동력의 소유자는 여전히 CharacterRoster다.</b> 회복소는 자기 행동력 사본을 들고 있지 않고,
    /// 계산 결과를 <see cref="ApplyRecoveryStamina"/>로 로스터에 돌려줄 뿐이다 - 같은 값을 두 곳에서
    /// 관리하면 반드시 어긋난다.
    /// </summary>
    public interface IRecoveryRoster
    {
        /// <summary>회복 대상이 될 수 있는(= 로스터에 정상 등록된) 캐릭터 목록.</summary>
        IReadOnlyList<CharacterDefinition> RecoverableCharacters { get; }

        /// <summary>지금 전투에 나가 있는 캐릭터. 없으면 null.</summary>
        CharacterDefinition CurrentCharacter { get; }

        bool Contains(CharacterDefinition definition);

        /// <summary>저장 데이터에서 캐릭터를 되찾는다. 정의가 사라진 id면 null.</summary>
        CharacterDefinition FindById(string characterId);

        string GetCharacterId(CharacterDefinition definition);

        int GetStamina(CharacterDefinition definition);

        int GetMaxStamina(CharacterDefinition definition);

        /// <summary>
        /// 회복 진행 결과를 현재 행동력에 반영한다. <b>메모리만 바꾸고 저장하지 않으며 이벤트도 보내지
        /// 않는다</b> - 슬롯 3개가 같은 프레임에 한 단계씩 오르더라도 파일 쓰기와 UI 갱신이 한 번에
        /// 묶이도록, 저장과 알림은 회복소가 마지막에 한 번만 한다.
        /// </summary>
        /// <returns>값이 실제로 달라졌으면 true.</returns>
        bool ApplyRecoveryStamina(CharacterDefinition definition, int value);

        /// <summary>회복소가 저장까지 마친 뒤, 값이 바뀐 캐릭터에 대해 로스터의 상태 변경 이벤트를
        /// 대신 보내 달라고 요청한다. 기존 UI(캐릭터 리스트/행동력 표시)가 그 이벤트를 이미 구독하고
        /// 있어 별도 연결이 필요 없다.</summary>
        void RaiseCharacterStateChanged(CharacterDefinition definition);
    }
}
