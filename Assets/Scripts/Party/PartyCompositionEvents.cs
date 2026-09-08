using System;
using UnityEngine;

namespace Party
{
    /// <summary>
    /// 저장에 성공해 고정 파티 슬롯이 실제로 달라진 뒤에만 보내는 공용 읽기 갱신 신호다.
    /// UI가 각 파티 변경 서비스의 구현을 알 필요가 없도록 좁은 성공 경계 하나만 공개한다.
    /// </summary>
    public static class PartyCompositionEvents
    {
        public static event Action ChangedAfterSave;

        internal static void NotifyChangedAfterSave()
        {
            Delegate[] listeners = ChangedAfterSave?.GetInvocationList();
            if (listeners == null) return;
            for (int i = 0; i < listeners.Length; i++)
            {
                try { ((Action)listeners[i]).Invoke(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
    }
}
