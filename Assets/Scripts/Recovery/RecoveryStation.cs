using System;
using System.Collections.Generic;
using System.Globalization;
using Character;
using Common;
using Corruption;
using UnityEngine;

namespace Recovery
{
    /// <summary>
    /// 회복소의 <b>규칙 전체</b>를 소유하는 순수 C# 클래스. 씬 오브젝트도 MonoBehaviour도 아니며,
    /// 필요한 바깥 세계(캐릭터/재화/저장/시각)는 전부 생성자로 받는다 - 그래서 씬 없이도 같은 규칙을
    /// 그대로 검증할 수 있다. 씬에 붙는 쪽은 <see cref="RecoveryService"/>다.
    ///
    /// <b>소유권</b>
    ///   - 회복 슬롯(누가 몇 번 슬롯에서 언제까지) : 이 클래스 + SaveData.recoverySlots
    ///   - 현재 행동력                              : CharacterRoster (여기서는 계산 결과만 넘긴다)
    ///   - 보유 재화                                : InventoryManager (여기서는 차감만 요청한다)
    ///   - PendingRecovery(시작 전 대기)            : 이 클래스의 런타임 배열 - <b>저장하지 않는다</b>
    ///
    /// <b>진행은 시각으로만 계산한다.</b> Time.time이나 코루틴 누적은 앱이 꺼지면 사라지므로 근거가 될
    /// 수 없다. 저장된 시작 시각(UTC)과 현재 UTC의 차이만 보므로, 앱을 꺼 둔 동안 흐른 시간도 그대로
    /// 반영된다.
    ///
    /// <b>매 프레임 저장하지 않는다.</b> <see cref="Tick"/>은 행동력이 실제로 한 단계 오른 경우에만
    /// 저장을 한 번 하고, 완료 전환만 일어난 경우에는 저장하지 않는다(완료는 이미 저장된
    /// completeAtUtc에서 파생되는 값이라 새로 기록할 것이 없다).
    /// </summary>
    public class RecoveryStation
    {
        /// <summary>UTC 시각을 저장할 때 쓰는 왕복(round-trip) 서식. InvariantCulture와 함께 써야
        /// 사용자의 지역/언어 설정과 무관하게 같은 문자열로 읽고 쓸 수 있다.</summary>
        public const string UtcFormat = "o";

        private readonly IRecoveryRoster roster;
        private readonly IRecoveryWallet wallet;
        private readonly Func<SaveData> dataProvider;
        private readonly Func<bool> saveAction;
        private readonly Func<DateTime> utcNowProvider;
        private readonly RecoveryBalance balance;

        // 시작 전 대기(PendingRecovery). 인덱스가 곧 슬롯 번호이며 저장하지 않는다.
        private readonly CharacterDefinition[] pendingBySlot;

        // <see cref="RecoveryCompleted"/> 도메인 이벤트를 슬롯당 한 번만 내보내기 위한 guard.
        // <b>이 인스턴스가 살아 있는 동안</b>에만 유효하며 저장하지 않는다 - 새 RecoveryStation을
        // 만들거나 앱을 다시 켜면 비어 있는 상태에서 시작한다.
        //
        // <b>알림의 1회성과는 다른 층이다.</b> "사용자에게 완료 알림을 이미 보냈는가"는 저장되는
        // per-cycle marker(RecoverySlotSaveState.completionNotified)가 담당하며, 재시작·중복 구독·
        // 오프라인 완료를 가로질러 회복 주기당 한 번을 보장한다. 여기 있는 표시는 그보다 좁게,
        // 같은 실행 안에서 같은 슬롯의 이벤트가 매 Tick 반복 발생하지 않게 막는 역할만 한다.
        private readonly HashSet<int> completionReported = new HashSet<int>();

        // 호출마다 할당하지 않기 위한 재사용 버퍼. Tick과 합류는 버퍼를 나눠 쓴다 - 완료 이벤트를
        // 받은 UI가 그 자리에서 합류를 호출해도 Tick이 순회 중인 목록이 비워지지 않게 하기 위함이다.
        private readonly List<CharacterDefinition> staminaChangedBuffer = new List<CharacterDefinition>();
        private readonly List<CompletedSlot> completedBuffer = new List<CompletedSlot>();
        private readonly List<int> startSlotBuffer = new List<int>();
        private readonly List<int> joinSlotBuffer = new List<int>();
        private readonly List<CharacterDefinition> joinChangedBuffer = new List<CharacterDefinition>();

        // Tick 중에 발생한 이벤트를 받은 쪽이 다시 Tick을 부르면 순회 중인 버퍼가 초기화된다.
        private bool ticking;

        // 같은 오류로 로그가 매 프레임 쏟아지지 않게 한 번만 남긴다.
        private bool warnedInvalidBalance;
        private readonly HashSet<int> warnedBrokenSlots = new HashSet<int>();

        /// <summary>슬롯 구성이나 상태가 바뀌었다(대기 등록/취소, 시작, 완료, 합류). UI는 이 신호
        /// 하나로 회복소 패널 전체를 다시 그리면 된다.</summary>
        public event Action SlotsChanged;

        /// <summary>회복 중인 캐릭터의 행동력이 한 단계 올랐다. 인자는 (캐릭터, 현재, 최대).
        /// 저장이 끝난 뒤에 발생한다.</summary>
        public event Action<CharacterDefinition, int, int> StaminaStepChanged;

        /// <summary>슬롯 하나의 회복이 완료됐다(Recovering → RecoveryComplete). 인자는 (슬롯 번호, 캐릭터).
        /// <b>자동으로 합류하지 않는다</b> - 사용자가 합류를 눌러야 Available이 된다.
        ///
        /// 같은 Tick에서 여러 슬롯이 완료되면 (완료 시각, 슬롯 번호) 오름차순으로 발생한다 - 이후
        /// 알림 단계가 순서를 임의로 정하지 않고 이 순서를 그대로 쓸 수 있게 하기 위함이다.</summary>
        public event Action<int, CharacterDefinition> RecoveryCompleted;

        public RecoveryStation(RecoveryBalance balance,
                               IRecoveryRoster roster,
                               IRecoveryWallet wallet,
                               Func<SaveData> dataProvider,
                               Func<bool> saveAction,
                               Func<DateTime> utcNowProvider)
        {
            this.balance = balance;
            this.roster = roster ?? throw new ArgumentNullException(nameof(roster));
            this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            this.saveAction = saveAction ?? throw new ArgumentNullException(nameof(saveAction));
            this.utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));

            SlotCount = balance.MaxSlots > 0 ? balance.MaxSlots : SaveData.DefaultRecoverySlotCount;
            pendingBySlot = new CharacterDefinition[SlotCount];
        }

        /// <summary>이 회복소가 쓰는 슬롯 수. 밸런스 테이블의 Max Slots가 정한다(값이 잘못된 경우에만
        /// 저장 계층 기본값 3을 쓴다 - 그때는 <see cref="IsOperational"/>이 false라 아무 동작도 하지 않는다).</summary>
        public int SlotCount { get; }

        /// <summary>밸런스 값이 정상이라 등록/진행/완료를 진행해도 되는지. false면 회복소는 조용히
        /// 기본값으로 대체하지 않고 <b>전부 멈춘다</b>.</summary>
        public bool IsOperational => balance.IsValid;

        public RecoveryBalance Balance => balance;

        public int PendingCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < pendingBySlot.Length; i++)
                {
                    if (pendingBySlot[i] != null) count++;
                }
                return count;
            }
        }

        /// <summary>지금 새로 올릴 수 있는 슬롯 수(회복 중도 아니고 대기도 없는 칸).</summary>
        public int FreeSlotCount
        {
            get
            {
                List<RecoverySlotSaveState> slots = GetSlots();
                int count = 0;
                for (int i = 0; i < SlotCount; i++)
                {
                    if (IsSlotFree(slots, i)) count++;
                }
                return count;
            }
        }

        // ---- 상태 판정 ----

        /// <summary>
        /// 캐릭터 한 명의 회복소 관점 상태. 저장된 값이 아니라 매번 파생시킨다
        /// (<see cref="RecoveryCharacterState"/>의 우선순위 주석 참고).
        ///
        /// 로스터에 없는 캐릭터나 null을 넣으면 <see cref="RecoveryCharacterState.Available"/>을
        /// 돌려준다 - 호출부는 먼저 로스터 소속을 확인한다.
        /// </summary>
        public RecoveryCharacterState GetState(CharacterDefinition definition)
        {
            if (definition == null) return RecoveryCharacterState.Available;

            int slotIndex = IndexOfRecoverySlot(definition);
            if (slotIndex >= 0)
            {
                return IsSlotCompleteNow(slotIndex)
                    ? RecoveryCharacterState.RecoveryComplete
                    : RecoveryCharacterState.Recovering;
            }

            // 전투에 나가 있는 캐릭터는 행동력이 0이어도 Active다 - "전투 중인 캐릭터는 회복소에
            // 넣지 않는다"가 Exhausted 규칙보다 우선한다.
            if (roster.CurrentCharacter == definition) return RecoveryCharacterState.Active;

            if (IndexOfPendingSlot(definition) >= 0) return RecoveryCharacterState.PendingRecovery;

            if (roster.GetStamina(definition) <= 0) return RecoveryCharacterState.Exhausted;
            return RecoveryCharacterState.Available;
        }

        /// <summary>회복 슬롯에 들어 있는지(Recovering 또는 RecoveryComplete). 아직 시작하지 않은
        /// 대기(PendingRecovery)는 <b>포함하지 않는다</b> - 대기는 재화를 내지 않은 UI 임시 상태라
        /// 실제 회복 중으로 취급하면 안 된다.
        ///
        /// CharacterRoster가 캐릭터 교체를 막을 때 이 판정을 쓴다. 판정 근거는 저장된 슬롯 목록
        /// 하나뿐이라(<see cref="IndexOfSavedSlot"/>), 회복소가 아직 시작되지 않았거나 밸런스 오류로
        /// 꺼져 있을 때 쓰는 저장 기반 폴백과 <b>절대 다른 답을 내지 않는다</b>.</summary>
        public bool IsInRecoverySlot(CharacterDefinition definition)
        {
            return IndexOfRecoverySlot(definition) >= 0;
        }

        // ---- 저장 슬롯 기반 정적 판정 (회복소 인스턴스 없이도 쓸 수 있는 단일 근거) ----
        //
        // CharacterRoster는 자기 Awake에서 시작 캐릭터를 고르는데, 그 시점에는 RecoveryService가 아직
        // 만들어지지 않았다. 밸런스 에셋이 비어 있어 회복소가 끝내 비활성화되는 구성도 있다. 그런
        // 경우에도 "이 캐릭터가 회복 슬롯에 들어 있는가"라는 질문의 답은 같아야 하므로, 판정을 저장
        // 데이터만 보는 정적 메서드 하나로 모으고 인스턴스 메서드도 이것을 쓴다.

        /// <summary>
        /// 저장된 회복 슬롯에서 이 캐릭터 id가 들어 있는 슬롯 번호를 찾는다. 없으면 -1.
        /// <b>저장 데이터를 고치지 않는다</b>(슬롯 목록을 늘리지도 않는다) - 아직 아무것도 초기화되지
        /// 않은 시점에도 안전하게 부를 수 있어야 하기 때문이다. null 입력에도 예외를 던지지 않는다.
        ///
        /// 밸런스의 Max Slots와 상관없이 <b>저장된 슬롯 전체</b>를 훑는다. Max Slots를 나중에 줄였을 때
        /// 범위 밖으로 밀려난 슬롯의 캐릭터가 "회복 중이 아닌 것"으로 잘못 판정돼 이중 등록되는 일을
        /// 막기 위함이다.
        /// </summary>
        public static int IndexOfSavedSlot(SaveData data, string characterId)
        {
            if (data == null || data.recoverySlots == null || string.IsNullOrEmpty(characterId)) return -1;

            List<RecoverySlotSaveState> slots = data.recoverySlots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && slots[i].characterId == characterId) return i;
            }
            return -1;
        }

        /// <summary>저장된 회복 슬롯에 이 캐릭터 id가 있는지(= Recovering 또는 RecoveryComplete).
        /// 회복소 인스턴스가 없어도 답할 수 있는 유일한 근거다.</summary>
        public static bool IsCharacterIdInSavedSlot(SaveData data, string characterId)
        {
            return IndexOfSavedSlot(data, characterId) >= 0;
        }

        /// <summary>이 캐릭터를 회복 슬롯에 올릴 수 없는 이유. <see cref="RecoveryRegisterBlockReason.None"/>
        /// 이면 지금 올릴 수 있다. 캐릭터 교체 판정(CharacterRoster.GetSwapBlockReason)과는 규칙이
        /// 다르므로 별도 API로 둔다.</summary>
        public RecoveryRegisterBlockReason GetRegisterBlockReason(CharacterDefinition definition)
        {
            RecoveryRegisterBlockReason reason = GetRegisterBlockReasonIgnoringCapacity(definition);
            if (reason != RecoveryRegisterBlockReason.None) return reason;
            return FreeSlotCount > 0 ? RecoveryRegisterBlockReason.None : RecoveryRegisterBlockReason.NoFreeSlot;
        }

        public bool CanRegister(CharacterDefinition definition)
        {
            return GetRegisterBlockReason(definition) == RecoveryRegisterBlockReason.None;
        }

        /// <summary>빈 슬롯 여부를 빼고 "이 캐릭터 자체가 회복 대상이 될 수 있는지"만 본다. 슬롯을
        /// 직접 지정하는 등록과 시작 시 재검증이 이 규칙을 쓴다.</summary>
        private RecoveryRegisterBlockReason GetRegisterBlockReasonIgnoringCapacity(CharacterDefinition definition)
        {
            if (!balance.IsValid) return RecoveryRegisterBlockReason.InvalidBalance;
            if (definition == null || !roster.Contains(definition)) return RecoveryRegisterBlockReason.NotInRoster;
            if (IndexOfRecoverySlot(definition) >= 0) return RecoveryRegisterBlockReason.AlreadyInRecovery;
            if (PurificationService.IsCharacterIdInSavedSlot(dataProvider(), roster.GetCharacterId(definition)))
                return RecoveryRegisterBlockReason.InPurification;
            if (roster.CurrentCharacter == definition) return RecoveryRegisterBlockReason.Active;
            if (IndexOfPendingSlot(definition) >= 0) return RecoveryRegisterBlockReason.AlreadyPending;
            if (GetMissingStamina(definition) <= 0) return RecoveryRegisterBlockReason.StaminaFull;
            return RecoveryRegisterBlockReason.None;
        }

        // ---- 견적 ----

        public int GetMissingStamina(CharacterDefinition definition)
        {
            if (definition == null) return 0;
            int missing = roster.GetMaxStamina(definition) - roster.GetStamina(definition);
            return missing > 0 ? missing : 0;
        }

        /// <summary>캐릭터 한 명의 비용/시간 견적. 등록할 수 없는 캐릭터면 false와 빈 견적을 돌려준다.</summary>
        public bool TryGetQuote(CharacterDefinition definition, out RecoveryCostQuote quote)
        {
            quote = RecoveryCostQuote.Empty;
            if (!balance.IsValid || definition == null || !roster.Contains(definition)) return false;

            int missing = GetMissingStamina(definition);
            if (missing <= 0) return false;

            quote = new RecoveryCostQuote(missing, balance.GetCost(missing), balance.GetDuration(missing), 1);
            return true;
        }

        /// <summary>지금 슬롯에 올려둔 대기 전체의 합계 견적. 실제 차감은 이 값이 아니라
        /// <see cref="StartRecovery"/>가 다시 계산한 값으로 한다.</summary>
        public RecoveryCostQuote GetPendingQuote()
        {
            if (!balance.IsValid) return RecoveryCostQuote.Empty;

            // 합계는 long으로 쌓는다. 개인 비용은 GetCost가 int.MaxValue로 포화하지만, 그것을 int로
            // 더하면 2~3명만 모여도 음수로 넘쳐서 "비용이 음수"인 견적이 나온다.
            long totalMissing = 0;
            long totalCost = 0;
            int count = 0;
            TimeSpan longest = TimeSpan.Zero;

            for (int i = 0; i < pendingBySlot.Length; i++)
            {
                CharacterDefinition character = pendingBySlot[i];
                if (character == null) continue;

                int missing = GetMissingStamina(character);
                if (missing <= 0) continue;

                totalMissing += missing;
                totalCost += balance.GetCost(missing);
                count++;

                TimeSpan duration = balance.GetDuration(missing);
                if (duration > longest) longest = duration;
            }

            return new RecoveryCostQuote(SaturateToInt(totalMissing), SaturateToInt(totalCost), longest, count);
        }

        // ---- 대기(Pending) 등록/취소 : 저장하지 않고 재화도 건드리지 않는다 ----

        /// <summary>비어 있는 첫 슬롯에 대기로 올린다.</summary>
        public bool TryAddPending(CharacterDefinition definition, out RecoveryRegisterBlockReason reason)
        {
            reason = GetRegisterBlockReasonIgnoringCapacity(definition);
            if (reason != RecoveryRegisterBlockReason.None) return false;

            int slotIndex = FindFirstFreeSlot();
            if (slotIndex < 0)
            {
                reason = RecoveryRegisterBlockReason.NoFreeSlot;
                return false;
            }

            pendingBySlot[slotIndex] = definition;
            SlotsChanged?.Invoke();
            return true;
        }

        /// <summary>슬롯 번호를 지정해서 대기로 올린다(드래그해서 특정 칸에 놓는 경우).</summary>
        public bool TryAddPendingToSlot(int slotIndex, CharacterDefinition definition,
                                        out RecoveryRegisterBlockReason reason)
        {
            reason = GetRegisterBlockReasonIgnoringCapacity(definition);
            if (reason != RecoveryRegisterBlockReason.None) return false;

            if (!IsSlotIndexValid(slotIndex) || !IsSlotFree(GetSlots(), slotIndex))
            {
                reason = RecoveryRegisterBlockReason.SlotUnavailable;
                return false;
            }

            pendingBySlot[slotIndex] = definition;
            SlotsChanged?.Invoke();
            return true;
        }

        public bool RemovePending(CharacterDefinition definition)
        {
            int slotIndex = IndexOfPendingSlot(definition);
            return slotIndex >= 0 && RemovePendingAtSlot(slotIndex);
        }

        public bool RemovePendingAtSlot(int slotIndex)
        {
            if (!IsSlotIndexValid(slotIndex) || pendingBySlot[slotIndex] == null) return false;

            pendingBySlot[slotIndex] = null;
            SlotsChanged?.Invoke();
            return true;
        }

        /// <summary>대기를 전부 지운다. <b>이미 회복 중이거나 완료된 슬롯에는 영향이 없다</b> -
        /// 재화를 낸 회복은 패널을 닫는다고 사라지지 않는다.</summary>
        /// <returns>실제로 지워진 대기 수.</returns>
        public int ClearPending()
        {
            int removed = 0;
            for (int i = 0; i < pendingBySlot.Length; i++)
            {
                if (pendingBySlot[i] == null) continue;
                pendingBySlot[i] = null;
                removed++;
            }

            if (removed > 0) SlotsChanged?.Invoke();
            return removed;
        }

        public CharacterDefinition GetPendingAtSlot(int slotIndex)
        {
            return IsSlotIndexValid(slotIndex) ? pendingBySlot[slotIndex] : null;
        }

        // ---- 회복 시작 (한 트랜잭션) ----

        /// <summary>
        /// 대기 중인 캐릭터 전원의 회복을 <b>한 번에</b> 시작한다. 순서는 다음과 같이 고정돼 있다.
        ///   a. 대기 목록 확인 → b. 각 캐릭터 상태 재검증 → c. 현재/최대 행동력 재확인 →
        ///   d. 각 비용 재계산 → e. 총합 → f. 보유 재화 확인 → g. 충분할 때만 총액 한 번 차감 →
        ///   h. 전원을 <b>같은</b> 시작 시각으로 Recovering 전환(완료 시각은 각자 계산) →
        ///   i. 저장 한 번.
        ///
        /// <b>부분 성공은 없다.</b> 한 명이라도 등록할 수 없는 상태이거나 재화가 모자라면 재화도,
        /// 캐릭터 행동력도, 회복 슬롯도 시작 전과 완전히 같다. 저장에 실패한 경우에도 메모리 변경을
        /// 되돌리므로 파일과 메모리가 어긋나지 않는다.
        /// </summary>
        public RecoveryStartResult StartRecovery()
        {
            int walletBalance = wallet.Balance;

            if (!balance.IsValid)
            {
                LogInvalidBalanceOnce();
                return RecoveryStartResult.Failure(RecoveryStartResultCode.InvalidBalance, 0, walletBalance);
            }

            // a. 대기 목록 확인 (슬롯 번호 오름차순 - 시작 순서를 결정적으로 만든다)
            startSlotBuffer.Clear();
            for (int i = 0; i < pendingBySlot.Length; i++)
            {
                if (pendingBySlot[i] != null) startSlotBuffer.Add(i);
            }
            if (startSlotBuffer.Count == 0)
            {
                return RecoveryStartResult.Failure(RecoveryStartResultCode.NoPending, 0, walletBalance);
            }

            if (!string.Equals(balance.CurrencyId, wallet.CurrencyId, StringComparison.Ordinal))
            {
                Debug.LogError($"[RecoveryStation] 밸런스 테이블의 Currency Id('{balance.CurrencyId}')와 " +
                               $"실제 재화('{wallet.CurrencyId}')가 다릅니다 - 회복을 시작하지 않습니다.");
                return RecoveryStartResult.Failure(RecoveryStartResultCode.InvalidBalance, 0, walletBalance);
            }

            // b~e. 재검증 + 비용 재계산. 화면에 보이던 견적을 그대로 믿지 않는다.
            // 합계는 long으로 쌓는다 - int로 더하면 개인 비용이 큰 값일 때 합계가 음수로 넘쳐서
            // "재화가 충분하다"는 잘못된 판정으로 회복이 공짜로 시작될 수 있다.
            long totalCostRaw = 0;
            for (int i = 0; i < startSlotBuffer.Count; i++)
            {
                CharacterDefinition character = pendingBySlot[startSlotBuffer[i]];

                RecoveryRegisterBlockReason reason = ValidateForStart(character);
                if (reason != RecoveryRegisterBlockReason.None)
                {
                    return new RecoveryStartResult(RecoveryStartResultCode.InvalidCharacterState, 0, walletBalance,
                                                   0, character, reason);
                }

                totalCostRaw += balance.GetCost(GetMissingStamina(character));
            }

            // 보고/표시는 int로 하되(포화), 잔액 비교는 넘치지 않은 long 원본으로 한다.
            int totalCost = SaturateToInt(totalCostRaw);

            // f. 보유 재화 확인 / g. 충분할 때만 총액을 한 번 차감
            if (totalCostRaw > walletBalance || !wallet.TrySpendWithoutSave(totalCost))
            {
                return RecoveryStartResult.Failure(RecoveryStartResultCode.InsufficientFunds, totalCost, walletBalance);
            }

            // h. 전원 같은 시작 시각. 완료 시각만 부족 행동력에 따라 각자 다르다.
            DateTime startedAtUtc = utcNowProvider().ToUniversalTime();
            string startedAtText = FormatUtc(startedAtUtc);
            List<RecoverySlotSaveState> slots = GetSlots();

            for (int i = 0; i < startSlotBuffer.Count; i++)
            {
                int slotIndex = startSlotBuffer[i];
                CharacterDefinition character = pendingBySlot[slotIndex];
                int missing = GetMissingStamina(character);

                RecoverySlotSaveState slot = slots[slotIndex];
                slot.characterId = roster.GetCharacterId(character);
                slot.startStamina = roster.GetStamina(character);
                slot.startedAtUtc = startedAtText;
                slot.completeAtUtc = FormatUtc(startedAtUtc + balance.GetDuration(missing));
                // 새 회복 주기가 시작됐으므로 완료 알림 표시를 초기화한다 - 같은 슬롯에서 다시
                // 완료되면 알림을 한 번 더 받아야 한다.
                slot.completionNotified = false;
                completionReported.Remove(slotIndex);
            }

            // i. 저장 한 번. 실패하면 여기까지의 메모리 변경을 전부 되돌린다.
            if (!saveAction())
            {
                for (int i = 0; i < startSlotBuffer.Count; i++)
                {
                    slots[startSlotBuffer[i]].Clear();
                }
                wallet.RefundWithoutSave(totalCost);

                Debug.LogError("[RecoveryStation] 회복 시작을 저장하지 못해 요청을 취소했습니다 - " +
                               "재화와 캐릭터 상태는 시작 전 그대로입니다.");
                return RecoveryStartResult.Failure(RecoveryStartResultCode.SaveFailed, totalCost, walletBalance);
            }

            int startedCount = startSlotBuffer.Count;
            for (int i = 0; i < startSlotBuffer.Count; i++)
            {
                pendingBySlot[startSlotBuffer[i]] = null;
            }

            wallet.NotifyChangedAfterExternalSave();
            for (int i = 0; i < startSlotBuffer.Count; i++)
            {
                CharacterDefinition character = roster.FindById(slots[startSlotBuffer[i]].characterId);
                if (character != null) roster.RaiseCharacterStateChanged(character);
            }
            SlotsChanged?.Invoke();

            return new RecoveryStartResult(RecoveryStartResultCode.Success, totalCost, walletBalance, startedCount,
                                           null, RecoveryRegisterBlockReason.None);
        }

        /// <summary>시작 직전 재검증. 자기 자신이 대기 중이라는 사실과 빈 슬롯 수는 실패 사유가 되지
        /// 않는다(그 슬롯은 이미 이 캐릭터의 자리다).</summary>
        private RecoveryRegisterBlockReason ValidateForStart(CharacterDefinition definition)
        {
            if (definition == null || !roster.Contains(definition)) return RecoveryRegisterBlockReason.NotInRoster;
            if (IndexOfRecoverySlot(definition) >= 0) return RecoveryRegisterBlockReason.AlreadyInRecovery;
            if (PurificationService.IsCharacterIdInSavedSlot(dataProvider(), roster.GetCharacterId(definition)))
                return RecoveryRegisterBlockReason.InPurification;
            if (roster.CurrentCharacter == definition) return RecoveryRegisterBlockReason.Active;
            if (GetMissingStamina(definition) <= 0) return RecoveryRegisterBlockReason.StaminaFull;
            return RecoveryRegisterBlockReason.None;
        }

        // ---- 진행 ----

        /// <summary>
        /// 저장된 시작 시각과 현재 UTC만 보고 모든 슬롯의 진행을 반영한다. 매 프레임 불러도 되며,
        /// <b>행동력이 실제로 한 단계 이상 오른 경우에만</b> 저장을 한 번 한다. 완료 전환만 일어난
        /// 경우에는 새로 기록할 값이 없으므로 저장하지 않고 이벤트만 보낸다.
        /// </summary>
        public void Tick()
        {
            if (!balance.IsValid)
            {
                LogInvalidBalanceOnce();
                return;
            }
            if (ticking) return;

            ticking = true;
            try
            {
                TickInternal();
            }
            finally
            {
                ticking = false;
            }
        }

        private void TickInternal()
        {
            DateTime now = utcNowProvider().ToUniversalTime();
            List<RecoverySlotSaveState> slots = GetSlots();

            staminaChangedBuffer.Clear();
            completedBuffer.Clear();

            // 등록 가능한 칸 수(SlotCount)가 아니라 저장된 슬롯 전체를 훑는다 - Max Slots를 나중에
            // 줄였을 때 범위 밖으로 밀려난 슬롯이 진행도 완료도 되지 않은 채 영원히 남지 않게 한다.
            for (int i = 0; i < slots.Count; i++)
            {
                RecoverySlotSaveState slot = slots[i];
                if (!slot.HasCharacter)
                {
                    completionReported.Remove(i);
                    continue;
                }

                CharacterDefinition character = roster.FindById(slot.characterId);
                if (character == null)
                {
                    // 정의가 사라졌거나 아직 로스터에 없는 id - 저장 값은 지우지 않고 진행만 멈춘다.
                    if (warnedBrokenSlots.Add(i))
                    {
                        Debug.LogWarning($"[RecoveryStation] 슬롯 {i}의 캐릭터 '{slot.characterId}'를 로스터에서 " +
                                         "찾지 못해 진행을 멈춥니다(저장 값은 유지됩니다).");
                    }
                    continue;
                }

                if (!TryParseUtc(slot.startedAtUtc, out DateTime startedAt))
                {
                    if (warnedBrokenSlots.Add(i))
                    {
                        Debug.LogError($"[RecoveryStation] 슬롯 {i}의 시작 시각('{slot.startedAtUtc}')을 읽지 못해 " +
                                       "진행을 멈춥니다 - 저장 값은 유지되므로 데이터를 확인하세요.");
                    }
                    continue;
                }

                int maxStamina = roster.GetMaxStamina(character);
                int target = ComputeCurrentStamina(slot, character, startedAt, now, maxStamina);
                if (roster.ApplyRecoveryStamina(character, target))
                {
                    staminaChangedBuffer.Add(character);
                }

                if (completionReported.Contains(i)) continue;

                if (IsComplete(slot, startedAt, now, target, maxStamina, out DateTime completeAt))
                {
                    completionReported.Add(i);
                    completedBuffer.Add(new CompletedSlot(i, character, completeAt));
                }
            }

            if (staminaChangedBuffer.Count == 0 && completedBuffer.Count == 0) return;

            // 슬롯 3개가 같은 프레임에 한 단계씩 올라도 파일 쓰기는 한 번이다.
            if (staminaChangedBuffer.Count > 0 && !saveAction())
            {
                Debug.LogError("[RecoveryStation] 회복 진행을 저장하지 못했습니다 - 이번 실행에는 반영되지만 " +
                               "앱을 다시 켜면 시작 시각 기준으로 다시 계산됩니다.");
            }

            for (int i = 0; i < staminaChangedBuffer.Count; i++)
            {
                CharacterDefinition character = staminaChangedBuffer[i];
                roster.RaiseCharacterStateChanged(character);
                StaminaStepChanged?.Invoke(character, roster.GetStamina(character), roster.GetMaxStamina(character));
            }

            if (completedBuffer.Count > 1) completedBuffer.Sort(CompletedSlot.Compare);
            for (int i = 0; i < completedBuffer.Count; i++)
            {
                RecoveryCompleted?.Invoke(completedBuffer[i].SlotIndex, completedBuffer[i].Character);
            }

            SlotsChanged?.Invoke();
        }

        // ---- 합류 ----

        /// <summary>
        /// 슬롯 하나의 <b>회복이 끝난</b> 캐릭터를 Available로 되돌리고 슬롯을 비운다. 아직 회복 중이면
        /// 거부한다(완료 전 합류 없음). 저장은 이 호출당 한 번이다.
        /// </summary>
        public bool TryJoin(int slotIndex, out CharacterDefinition joined)
        {
            joined = null;
            if (!IsAddressableSlotIndex(slotIndex)) return false;
            if (GetSlotState(slotIndex) != RecoveryCharacterState.RecoveryComplete) return false;

            CharacterDefinition character = roster.FindById(GetSlots()[slotIndex].characterId);

            joinSlotBuffer.Clear();
            joinSlotBuffer.Add(slotIndex);
            if (ApplyJoin(joinSlotBuffer) == 0) return false;

            joined = character;
            return true;
        }

        /// <summary>
        /// 회복이 <b>끝난</b> 캐릭터만 전부 Available로 되돌리고 그 슬롯을 비운다. 아직 진행 중인
        /// 슬롯은 그대로 남는다. 사용자가 버튼을 한 번 누른 동작이므로 저장도 한 번만 한다.
        /// </summary>
        /// <returns>합류한 캐릭터 수.</returns>
        public int JoinAllCompleted()
        {
            joinSlotBuffer.Clear();
            int savedSlotCount = GetSlots().Count;
            for (int i = 0; i < savedSlotCount; i++)
            {
                if (GetSlotState(i) == RecoveryCharacterState.RecoveryComplete) joinSlotBuffer.Add(i);
            }

            return joinSlotBuffer.Count == 0 ? 0 : ApplyJoin(joinSlotBuffer);
        }

        /// <summary>합류 대상 슬롯들을 한 덩어리로 처리한다 - 최종 행동력 반영, 슬롯 비우기, 저장 1회,
        /// 이벤트 순서다. 저장이 실패해도 메모리 값은 그대로 두고 오류만 남긴다(다시 켜면 시작 시각
        /// 기준으로 재계산되므로 값이 사라지지는 않는다).</summary>
        private int ApplyJoin(List<int> slotIndices)
        {
            DateTime now = utcNowProvider().ToUniversalTime();
            List<RecoverySlotSaveState> slots = GetSlots();

            joinChangedBuffer.Clear();
            int joinedCount = 0;

            for (int i = 0; i < slotIndices.Count; i++)
            {
                int slotIndex = slotIndices[i];
                RecoverySlotSaveState slot = slots[slotIndex];
                if (!slot.HasCharacter) continue;

                CharacterDefinition character = roster.FindById(slot.characterId);
                if (character != null)
                {
                    if (TryParseUtc(slot.startedAtUtc, out DateTime startedAt))
                    {
                        int maxStamina = roster.GetMaxStamina(character);
                        roster.ApplyRecoveryStamina(character, ComputeCurrentStamina(slot, character, startedAt, now, maxStamina));
                    }
                    joinChangedBuffer.Add(character);
                }

                slot.Clear();
                completionReported.Remove(slotIndex);
                warnedBrokenSlots.Remove(slotIndex);
                joinedCount++;
            }

            if (joinedCount == 0) return 0;

            if (!saveAction())
            {
                Debug.LogError("[RecoveryStation] 합류 결과를 저장하지 못했습니다 - 이번 실행에는 반영되지만 " +
                               "앱을 다시 켜면 회복 중 상태로 되돌아갑니다.");
            }

            for (int i = 0; i < joinChangedBuffer.Count; i++)
            {
                roster.RaiseCharacterStateChanged(joinChangedBuffer[i]);
            }
            SlotsChanged?.Invoke();

            return joinedCount;
        }

        // ---- 슬롯 조회 (UI용) ----

        public RecoveryCharacterState GetSlotState(int slotIndex)
        {
            if (!IsAddressableSlotIndex(slotIndex)) return RecoveryCharacterState.Available;

            List<RecoverySlotSaveState> slots = GetSlots();
            if (slots[slotIndex].HasCharacter)
            {
                return IsSlotCompleteNow(slotIndex)
                    ? RecoveryCharacterState.RecoveryComplete
                    : RecoveryCharacterState.Recovering;
            }

            return slotIndex < pendingBySlot.Length && pendingBySlot[slotIndex] != null
                ? RecoveryCharacterState.PendingRecovery
                : RecoveryCharacterState.Available;
        }

        // ---- 완료 알림 (per-cycle marker) ----
        //
        // 알림을 "이벤트를 몇 번 받았는가"가 아니라 <b>저장된 표시</b>로 판단한다. 그래서
        //   - 매 프레임/재시작마다 반복되지 않고,
        //   - 이벤트를 놓쳤거나(앱이 꺼져 있던 사이 완료) 중복 구독으로 두 번 받아도
        //   회복 주기당 정확히 한 번만 요청된다.

        /// <summary>
        /// 완료됐지만 아직 알림을 요청하지 않은 슬롯을 모아 준다. 결과는
        /// <see cref="RecoveryCompletionNotice.Compare"/> 기준(완료 시각 → 슬롯 번호 오름차순)으로
        /// 정렬되므로, 호출부가 순서대로 처리하면 마지막 항목이 화면에 남는다.
        ///
        /// 밸런스 값이 잘못됐거나 캐릭터 정의를 찾지 못한 슬롯은 완료 판정 자체가 되지 않으므로
        /// 여기에 들어오지 않는다 - 알림이 무한히 재시도되지 않는다.
        /// </summary>
        /// <returns>모인 개수.</returns>
        public int CollectPendingCompletionNotices(List<RecoveryCompletionNotice> buffer)
        {
            if (buffer == null) return 0;
            buffer.Clear();
            if (!balance.IsValid) return 0;

            List<RecoverySlotSaveState> slots = GetSlots();
            for (int i = 0; i < slots.Count; i++)
            {
                RecoverySlotSaveState slot = slots[i];
                if (slot == null || !slot.HasCharacter || slot.completionNotified) continue;
                if (!IsSlotCompleteNow(i)) continue;

                CharacterDefinition character = roster.FindById(slot.characterId);
                if (character == null) continue;

                // 완료 시각을 읽지 못하는 슬롯은 시작 시각으로 대체한다 - 정렬만을 위한 값이며,
                // 완료 판정 자체는 이미 위에서 끝났다.
                if (!TryParseUtc(slot.completeAtUtc, out DateTime completeAt)
                    && !TryParseUtc(slot.startedAtUtc, out completeAt))
                {
                    completeAt = DateTime.MinValue;
                }

                buffer.Add(new RecoveryCompletionNotice(i, character, completeAt));
            }

            if (buffer.Count > 1) buffer.Sort(RecoveryCompletionNotice.Compare);
            return buffer.Count;
        }

        /// <summary>
        /// 알림 요청이 <b>실제로 수락된</b> 슬롯들에 완료 알림 표시를 남기고 저장한다. 호출부는 Show가
        /// 성공한 슬롯만 넘겨야 한다 - 알림 매니저가 아직 준비되지 않았는데 미리 표시를 남기면 그
        /// 주기의 알림을 영원히 잃는다.
        ///
        /// 여러 슬롯을 한 번에 넘길 수 있고 <b>저장은 한 번</b>만 한다.
        /// </summary>
        /// <returns>실제로 표시가 바뀐 슬롯이 있으면 true.</returns>
        public bool MarkCompletionNotified(IReadOnlyList<int> slotIndices)
        {
            if (slotIndices == null || slotIndices.Count == 0) return false;

            List<RecoverySlotSaveState> slots = GetSlots();
            bool changed = false;

            for (int i = 0; i < slotIndices.Count; i++)
            {
                int slotIndex = slotIndices[i];
                if (slotIndex < 0 || slotIndex >= slots.Count) continue;

                RecoverySlotSaveState slot = slots[slotIndex];
                if (slot == null || !slot.HasCharacter || slot.completionNotified) continue;

                slot.completionNotified = true;
                changed = true;
            }

            if (!changed) return false;

            if (!saveAction())
            {
                // 저장에 실패하면 다음 실행에서 같은 알림이 한 번 더 뜬다 - 알림을 잃는 것보다 낫다.
                Debug.LogError("[RecoveryStation] 완료 알림 표시를 저장하지 못했습니다 - 앱을 다시 켜면 " +
                               "같은 회복 완료 알림이 한 번 더 표시될 수 있습니다.");
            }
            return true;
        }

        /// <summary>슬롯 한 칸을 UI가 그릴 수 있는 읽기 전용 정보로 만든다. 저장 구조를 밖으로
        /// 내보내지 않으므로 UI가 저장 값을 직접 고칠 수 없다.</summary>
        public RecoverySlotView GetSlot(int slotIndex)
        {
            if (!IsAddressableSlotIndex(slotIndex)) return RecoverySlotView.Empty(slotIndex);

            List<RecoverySlotSaveState> slots = GetSlots();
            RecoverySlotSaveState slot = slots[slotIndex];
            DateTime now = utcNowProvider().ToUniversalTime();

            if (!slot.HasCharacter)
            {
                CharacterDefinition pending = slotIndex < pendingBySlot.Length ? pendingBySlot[slotIndex] : null;
                if (pending == null) return RecoverySlotView.Empty(slotIndex);

                int pendingMissing = GetMissingStamina(pending);
                return new RecoverySlotView(slotIndex, pending, RecoveryCharacterState.PendingRecovery,
                                            roster.GetStamina(pending), roster.GetMaxStamina(pending),
                                            DateTime.MinValue, DateTime.MinValue,
                                            balance.GetDuration(pendingMissing), balance.GetCost(pendingMissing));
            }

            CharacterDefinition character = roster.FindById(slot.characterId);
            int maxStamina = character != null ? roster.GetMaxStamina(character) : 0;
            bool hasStartedAt = TryParseUtc(slot.startedAtUtc, out DateTime startedAt);
            TryParseUtc(slot.completeAtUtc, out DateTime completeAt);

            int current = character != null && hasStartedAt
                ? ComputeCurrentStamina(slot, character, startedAt, now, maxStamina)
                : slot.startStamina;

            TimeSpan remaining = completeAt > now ? completeAt - now : TimeSpan.Zero;
            RecoveryCharacterState state = IsSlotCompleteNow(slotIndex)
                ? RecoveryCharacterState.RecoveryComplete
                : RecoveryCharacterState.Recovering;

            return new RecoverySlotView(slotIndex, character, state, current, maxStamina,
                                        startedAt, completeAt, remaining,
                                        balance.GetCost(maxStamina - slot.startStamina));
        }

        // ---- 계산 ----

        /// <summary>
        /// long 합계를 UI/저장이 쓰는 int로 줄인다. <b>포화 정책</b>: int 범위를 넘으면 넘긴 만큼
        /// 감기(wrap)지 않고 <see cref="int.MaxValue"/>에서 멈춘다. 음수는 0으로 잘라 "비용이 음수"인
        /// 값이 밖으로 나가지 않게 한다.
        ///
        /// 포화된 값은 표시/보고용일 뿐이며, 재화가 충분한지는 언제나 <b>포화 전 long 원본</b>으로
        /// 비교한다 - 그래야 밸런스를 아무리 크게 잘못 넣어도 잔액보다 비싼 회복이 시작되지 않는다.
        /// </summary>
        public static int SaturateToInt(long value)
        {
            if (value < 0) return 0;
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }

        /// <summary>회복량 = floor(경과 초 / Seconds Per Stamina). 시계가 뒤로 간 경우(경과가 음수)에는
        /// 0으로 취급해 진행이 되돌아가지 않게 한다.</summary>
        private int ComputeRecoveredSteps(DateTime startedAtUtc, DateTime nowUtc)
        {
            double elapsedSeconds = (nowUtc - startedAtUtc).TotalSeconds;
            if (elapsedSeconds <= 0d) return 0;

            double steps = Math.Floor(elapsedSeconds / balance.SecondsPerStamina);
            return steps >= int.MaxValue ? int.MaxValue : (int)steps;
        }

        /// <summary>현재 행동력 = min(최대, 시작 행동력 + 회복량).
        ///
        /// 계산 결과가 지금 값보다 낮아도 <b>내리지 않는다</b>. 시스템 시계가 뒤로 갔거나 시작 시각이
        /// 미래로 기록된 경우 계산값이 순간적으로 작아질 수 있는데, 그때 이미 회복된 행동력을 빼앗으면
        /// 사용자가 낸 재화가 사라지는 것과 같다 - 회복은 행동력을 늘리기만 한다.</summary>
        private int ComputeCurrentStamina(RecoverySlotSaveState slot, CharacterDefinition character,
                                          DateTime startedAtUtc, DateTime nowUtc, int maxStamina)
        {
            long value = (long)slot.startStamina + ComputeRecoveredSteps(startedAtUtc, nowUtc);

            int currentStamina = roster.GetStamina(character);
            if (value < currentStamina) value = currentStamina;
            if (value > maxStamina) value = maxStamina;
            if (value < 0) value = 0;
            return (int)value;
        }

        private bool IsComplete(RecoverySlotSaveState slot, DateTime startedAt, DateTime now, int currentStamina,
                                int maxStamina, out DateTime completeAt)
        {
            bool hasCompleteAt = TryParseUtc(slot.completeAtUtc, out completeAt);
            if (!hasCompleteAt) completeAt = startedAt;

            // 최대치에 닿았거나 예정 시각을 지났으면 완료다. 두 조건을 함께 보는 이유는 저장 후
            // Max Stamina를 바꾼 경우에도 슬롯이 영원히 끝나지 않는 상태로 남지 않게 하기 위함이다.
            return (maxStamina > 0 && currentStamina >= maxStamina) || (hasCompleteAt && now >= completeAt);
        }

        private bool IsSlotCompleteNow(int slotIndex)
        {
            if (!balance.IsValid) return false;

            RecoverySlotSaveState slot = GetSlots()[slotIndex];
            if (!slot.HasCharacter) return false;

            CharacterDefinition character = roster.FindById(slot.characterId);
            if (character == null) return false;
            if (!TryParseUtc(slot.startedAtUtc, out DateTime startedAt)) return false;

            DateTime now = utcNowProvider().ToUniversalTime();
            int maxStamina = roster.GetMaxStamina(character);
            int current = ComputeCurrentStamina(slot, character, startedAt, now, maxStamina);
            return IsComplete(slot, startedAt, now, current, maxStamina, out _);
        }

        // ---- 시각 직렬화 ----

        /// <summary>UTC 시각을 문화권과 무관한 왕복 서식으로 만든다.</summary>
        public static string FormatUtc(DateTime utc)
        {
            return utc.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>저장된 시각 문자열을 UTC DateTime으로 되돌린다. 비어 있거나 서식이 어긋나면
        /// false를 돌려주며 예외를 던지지 않는다.</summary>
        public static bool TryParseUtc(string text, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrEmpty(text)) return false;

            if (!DateTime.TryParseExact(text, UtcFormat, CultureInfo.InvariantCulture,
                                        DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                return false;
            }

            utc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
            return true;
        }

        // ---- 내부 도우미 ----

        private List<RecoverySlotSaveState> GetSlots()
        {
            SaveData data = dataProvider();
            SaveData.EnsureRecoverySlots(data, SlotCount);
            return data.recoverySlots;
        }

        /// <summary>새로 대기를 올릴 수 있는 칸 번호인지. 등록 정원은 밸런스의 Max Slots가 정한다.</summary>
        private bool IsSlotIndexValid(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < SlotCount;
        }

        /// <summary>저장된 슬롯 목록에서 실제로 가리킬 수 있는 번호인지. 조회/진행/합류는 이 범위를
        /// 쓴다 - Max Slots를 줄여도 이미 회복 중이던 캐릭터를 합류시킬 수 없게 되지 않도록,
        /// 등록 정원보다 넓게 잡는다.</summary>
        private bool IsAddressableSlotIndex(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < GetSlots().Count;
        }

        private bool IsSlotFree(List<RecoverySlotSaveState> slots, int slotIndex)
        {
            return !slots[slotIndex].HasCharacter && pendingBySlot[slotIndex] == null;
        }

        private int FindFirstFreeSlot()
        {
            List<RecoverySlotSaveState> slots = GetSlots();
            for (int i = 0; i < SlotCount; i++)
            {
                if (IsSlotFree(slots, i)) return i;
            }
            return -1;
        }

        /// <summary>이 캐릭터가 들어 있는 저장 슬롯 번호. 정적 <see cref="IndexOfSavedSlot"/> 하나만
        /// 쓰므로, 회복소가 없을 때의 저장 기반 폴백과 답이 갈릴 수 없다.</summary>
        private int IndexOfRecoverySlot(CharacterDefinition definition)
        {
            if (definition == null) return -1;

            // GetSlots()를 먼저 불러 목록 길이를 보장한 뒤 정적 판정에 넘긴다.
            List<RecoverySlotSaveState> slots = GetSlots();
            string characterId = roster.GetCharacterId(definition);
            if (string.IsNullOrEmpty(characterId)) return -1;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && slots[i].characterId == characterId) return i;
            }
            return -1;
        }

        private int IndexOfPendingSlot(CharacterDefinition definition)
        {
            if (definition == null) return -1;
            for (int i = 0; i < pendingBySlot.Length; i++)
            {
                if (pendingBySlot[i] == definition) return i;
            }
            return -1;
        }

        private void LogInvalidBalanceOnce()
        {
            if (warnedInvalidBalance) return;
            warnedInvalidBalance = true;

            Debug.LogError($"[RecoveryStation] 회복 밸런스 값이 잘못돼 회복소를 멈춥니다 - {balance.DescribeInvalid()} " +
                           "(Recovery Balance Table 에셋을 고치세요. 기존 회복 슬롯의 저장 값은 지우지 않습니다.)");
        }

        /// <summary>같은 Tick에서 여러 슬롯이 완료됐을 때 순서를 정하는 근거. (완료 시각, 슬롯 번호)
        /// 오름차순이라 같은 시각에 끝난 슬롯도 항상 같은 순서로 나온다.</summary>
        private readonly struct CompletedSlot
        {
            public readonly int SlotIndex;
            public readonly CharacterDefinition Character;
            public readonly DateTime CompleteAtUtc;

            public CompletedSlot(int slotIndex, CharacterDefinition character, DateTime completeAtUtc)
            {
                SlotIndex = slotIndex;
                Character = character;
                CompleteAtUtc = completeAtUtc;
            }

            public static int Compare(CompletedSlot a, CompletedSlot b)
            {
                int byTime = a.CompleteAtUtc.CompareTo(b.CompleteAtUtc);
                return byTime != 0 ? byTime : a.SlotIndex.CompareTo(b.SlotIndex);
            }
        }
    }
}
