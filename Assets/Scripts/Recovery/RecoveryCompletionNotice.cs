using System;
using Character;

namespace Recovery
{
    /// <summary>
    /// "이 슬롯의 회복이 끝났는데 아직 완료 알림을 요청하지 않았다"는 한 건. 알림 연결 컴포넌트가
    /// 도메인에서 받아 가는 읽기 전용 값이며, 저장 구조를 밖으로 내보내지 않기 위해 감싼 것이다.
    ///
    /// <b>순서가 이 구조체의 존재 이유다.</b> 여러 슬롯이 동시에(또는 앱이 꺼진 동안) 완료된 경우
    /// 어떤 캐릭터 이름이 마지막에 남는지가 결정적이어야 하므로, 정렬 기준(<see cref="Compare"/>)을
    /// 값과 함께 한곳에 둔다.
    /// </summary>
    public readonly struct RecoveryCompletionNotice
    {
        public readonly int SlotIndex;
        public readonly CharacterDefinition Character;

        /// <summary>저장된 완료 예정 시각(UTC). 정렬의 1순위 기준이다.</summary>
        public readonly DateTime CompleteAtUtc;

        public RecoveryCompletionNotice(int slotIndex, CharacterDefinition character, DateTime completeAtUtc)
        {
            SlotIndex = slotIndex;
            Character = character;
            CompleteAtUtc = completeAtUtc;
        }

        /// <summary>
        /// (완료 예정 시각, 슬롯 번호) <b>오름차순</b>. 알림은 이 순서대로 요청되므로, 같은 타입 알림
        /// 하나만 남기는 정책에서 <b>마지막 항목의 캐릭터 이름</b>이 화면에 남는다.
        ///   - 완료 시각이 다르면: 가장 늦게(= 가장 최근에) 완료된 캐릭터가 남는다.
        ///   - 완료 시각이 같으면: 슬롯 번호가 가장 큰 캐릭터가 남는다.
        /// 도메인의 <see cref="RecoveryStation.RecoveryCompleted"/> 이벤트 순서와 같은 기준이라,
        /// 실시간 완료와 오프라인 완료가 서로 다른 결과를 내지 않는다.
        /// </summary>
        public static int Compare(RecoveryCompletionNotice a, RecoveryCompletionNotice b)
        {
            int byTime = a.CompleteAtUtc.CompareTo(b.CompleteAtUtc);
            return byTime != 0 ? byTime : a.SlotIndex.CompareTo(b.SlotIndex);
        }
    }
}
