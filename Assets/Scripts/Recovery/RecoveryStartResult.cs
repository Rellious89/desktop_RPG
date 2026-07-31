using Character;

namespace Recovery
{
    /// <summary>회복 시작 요청의 결과 코드. UI가 무엇을 해야 하는지가 코드마다 다르므로 실패를
    /// 하나로 뭉뚱그리지 않는다 - 특히 <see cref="InsufficientFunds"/>와
    /// <see cref="InvalidCharacterState"/>는 반드시 구분한다.</summary>
    public enum RecoveryStartResultCode
    {
        /// <summary>전원 회복 시작. 재화는 총액이 한 번 차감됐고 저장도 한 번 됐다.</summary>
        Success,

        /// <summary>슬롯에 올려둔 캐릭터가 없다. 아무것도 하지 않았다.</summary>
        NoPending,

        /// <summary>밸런스 테이블 값이 잘못돼 회복소가 멈춰 있다.</summary>
        InvalidBalance,

        /// <summary>Pending 중 한 명이라도 지금은 등록할 수 없는 상태다(전투 투입, 행동력 최대치 등).
        /// <b>부분 성공은 없다</b> - 나머지도 시작하지 않았고 재화도 차감하지 않았다.</summary>
        InvalidCharacterState,

        /// <summary>재화가 부족하다. 한 명도 시작하지 않았고 차감도 없다. UI는 이 코드를 받으면
        /// 패널을 닫고 Pending을 지운다(2단계에서 연결).</summary>
        InsufficientFunds,

        /// <summary>계산과 검증은 통과했지만 저장에 실패했다. 메모리 변경까지 되돌렸으므로 재화도
        /// 회복 슬롯도 시작 전과 같다.</summary>
        SaveFailed,
    }

    /// <summary>
    /// <see cref="RecoveryStation.StartRecovery"/>의 결과. 성공/실패와 함께 UI가 문구를 만들 수 있는
    /// 근거(총액, 잔액, 부족액, 막힌 캐릭터와 그 이유)를 같이 돌려준다.
    /// </summary>
    public readonly struct RecoveryStartResult
    {
        public readonly RecoveryStartResultCode Code;

        /// <summary>재검증 후 다시 계산한 총 비용.</summary>
        public readonly int TotalCost;

        /// <summary>요청 시점의 보유 재화.</summary>
        public readonly int Balance;

        /// <summary>회복을 시작한 캐릭터 수(실패면 0).</summary>
        public readonly int StartedCount;

        /// <summary><see cref="RecoveryStartResultCode.InvalidCharacterState"/>일 때 막힌 캐릭터.
        /// 다른 코드에서는 null이다.</summary>
        public readonly CharacterDefinition BlockedCharacter;

        /// <summary>막힌 이유. <see cref="BlockedCharacter"/>와 짝이다.</summary>
        public readonly RecoveryRegisterBlockReason BlockReason;

        public RecoveryStartResult(RecoveryStartResultCode code, int totalCost, int balance, int startedCount,
                                   CharacterDefinition blockedCharacter, RecoveryRegisterBlockReason blockReason)
        {
            Code = code;
            TotalCost = totalCost;
            Balance = balance;
            StartedCount = startedCount;
            BlockedCharacter = blockedCharacter;
            BlockReason = blockReason;
        }

        public bool IsSuccess => Code == RecoveryStartResultCode.Success;

        /// <summary>모자란 재화(부족하지 않으면 0). UI가 "N 부족" 문구에 쓴다.</summary>
        public int Shortfall => TotalCost > Balance ? TotalCost - Balance : 0;

        public static RecoveryStartResult Failure(RecoveryStartResultCode code, int totalCost, int balance)
        {
            return new RecoveryStartResult(code, totalCost, balance, 0, null, RecoveryRegisterBlockReason.None);
        }
    }
}
