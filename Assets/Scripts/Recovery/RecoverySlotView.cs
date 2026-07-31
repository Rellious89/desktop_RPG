using System;
using Character;

namespace Recovery
{
    /// <summary>
    /// 회복 슬롯 한 칸을 UI가 그리는 데 필요한 <b>읽기 전용</b> 정보. 저장 구조
    /// (Common.RecoverySlotSaveState)를 그대로 밖으로 내보내면 UI가 저장 필드를 직접 고칠 수 있게 되므로
    /// InventoryManager.Entry와 같은 방식으로 값 구조체에 담아 넘긴다.
    ///
    /// Pending(아직 시작 전) 슬롯도 이 구조체로 표현되며, 그때는 <see cref="StartedAtUtc"/>/
    /// <see cref="CompleteAtUtc"/>가 <see cref="DateTime.MinValue"/>이고 <see cref="Remaining"/>이
    /// "시작하면 걸릴 시간"이다.
    /// </summary>
    public readonly struct RecoverySlotView
    {
        public readonly int SlotIndex;

        /// <summary>이 슬롯에 들어 있는 캐릭터. 빈 슬롯이면 null이다.</summary>
        public readonly CharacterDefinition Character;

        /// <summary>슬롯에 들어 있는 캐릭터의 상태. 빈 슬롯이면
        /// <see cref="RecoveryCharacterState.Available"/>이지만 <see cref="IsEmpty"/>로 먼저 걸러야 한다.</summary>
        public readonly RecoveryCharacterState State;

        /// <summary>지금 시점의 현재 행동력.</summary>
        public readonly int CurrentStamina;

        public readonly int MaxStamina;

        /// <summary>회복을 시작한 시각(UTC). Pending이면 <see cref="DateTime.MinValue"/>.</summary>
        public readonly DateTime StartedAtUtc;

        /// <summary>회복이 끝나는 시각(UTC). Pending이면 <see cref="DateTime.MinValue"/>.</summary>
        public readonly DateTime CompleteAtUtc;

        /// <summary>남은 시간. 완료됐으면 <see cref="TimeSpan.Zero"/>.</summary>
        public readonly TimeSpan Remaining;

        /// <summary>이 슬롯에 필요한(또는 이미 지불한) 비용.</summary>
        public readonly int Cost;

        public RecoverySlotView(int slotIndex, CharacterDefinition character, RecoveryCharacterState state,
                                int currentStamina, int maxStamina, DateTime startedAtUtc, DateTime completeAtUtc,
                                TimeSpan remaining, int cost)
        {
            SlotIndex = slotIndex;
            Character = character;
            State = state;
            CurrentStamina = currentStamina;
            MaxStamina = maxStamina;
            StartedAtUtc = startedAtUtc;
            CompleteAtUtc = completeAtUtc;
            Remaining = remaining;
            Cost = cost;
        }

        public bool IsEmpty => Character == null;

        /// <summary>아직 시작하지 않은(재화를 내지 않은) 슬롯인지.</summary>
        public bool IsPending => State == RecoveryCharacterState.PendingRecovery;

        /// <summary>합류를 누를 수 있는 슬롯인지.</summary>
        public bool CanJoin => State == RecoveryCharacterState.RecoveryComplete;

        public static RecoverySlotView Empty(int slotIndex)
        {
            return new RecoverySlotView(slotIndex, null, RecoveryCharacterState.Available, 0, 0,
                                        DateTime.MinValue, DateTime.MinValue, TimeSpan.Zero, 0);
        }
    }
}
