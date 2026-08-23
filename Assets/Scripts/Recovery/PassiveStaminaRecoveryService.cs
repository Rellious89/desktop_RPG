using System;
using System.Collections.Generic;
using System.Globalization;
using Character;
using Common;

namespace Recovery
{
    /// <summary>
    /// 회복소에 들어 있지 않은 보유 캐릭터의 자연 행동력 회복 규칙. 씬/MonoBehaviour/파일 경로를
    /// 모르며, UTC 시각·저장 문서·로스터 창구만 생성자로 받아 한 Tick을 원자적으로 처리한다.
    /// </summary>
    public sealed class PassiveStaminaRecoveryService
    {
        private const long PercentScale = 100L;

        private readonly RecoveryBalance balance;
        private readonly IRecoveryRoster roster;
        private readonly Func<SaveData> dataProvider;
        private readonly Func<bool> saveAction;
        private readonly Func<DateTime> utcNowProvider;
        private readonly List<ChangedState> changedStates = new List<ChangedState>();
        private readonly List<CharacterDefinition> staminaChanged = new List<CharacterDefinition>();
        private bool ticking;

        /// <summary>저장이 성공한 뒤 실제 행동력이 증가한 캐릭터에만 발생한다.</summary>
        public event Action<CharacterDefinition> StaminaChanged;

        public PassiveStaminaRecoveryService(RecoveryBalance balance, IRecoveryRoster roster,
                                             Func<SaveData> dataProvider, Func<bool> saveAction,
                                             Func<DateTime> utcNowProvider)
        {
            this.balance = balance;
            this.roster = roster ?? throw new ArgumentNullException(nameof(roster));
            this.dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
            this.saveAction = saveAction ?? throw new ArgumentNullException(nameof(saveAction));
            this.utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
        }

        /// <summary>한 번의 UTC 기준 자연 회복 계산 결과.</summary>
        public readonly struct TickResult
        {
            public readonly int StaminaChangedCount;
            public readonly bool SaveAttempted;
            public readonly bool SaveSucceeded;

            public TickResult(int staminaChangedCount, bool saveAttempted, bool saveSucceeded)
            {
                StaminaChangedCount = staminaChangedCount;
                SaveAttempted = saveAttempted;
                SaveSucceeded = saveSucceeded;
            }
        }

        /// <summary>
        /// 보유·정의 유효 캐릭터만 처리한다. 회복소 슬롯은 자연 회복을 하지 않으며, 그 체류 시간을
        /// 이후 자연 회복에 쓸 수 없도록 기준 시각/잔여 진행만 현재로 정리한다.
        /// </summary>
        public TickResult Tick()
        {
            if (!balance.IsValid || ticking) return new TickResult(0, false, true);

            ticking = true;
            try
            {
                return TickInternal();
            }
            finally
            {
                ticking = false;
            }
        }

        private TickResult TickInternal()
        {
            SaveData data = dataProvider();
            if (data == null || data.characters == null) return new TickResult(0, false, true);

            DateTime now = utcNowProvider().ToUniversalTime();
            changedStates.Clear();
            staminaChanged.Clear();

            IReadOnlyList<CharacterDefinition> owned = roster.RecoverableCharacters;
            for (int i = 0; i < owned.Count; i++)
            {
                CharacterDefinition character = owned[i];
                if (character == null) continue;

                CharacterSaveState state = FindState(data.characters, roster.GetCharacterId(character));
                if (state == null) continue;

                Track(state, character);
                if (RecoveryStation.IsCharacterIdInSavedSlot(data, state.characterId))
                {
                    ResetProgressAtNow(state, now);
                    continue;
                }

                int efficiency = IsPartyMember(data.partyCharacterIds, state.characterId)
                    ? balance.PartyPassiveRecoveryEfficiencyPercent
                    : balance.NonPartyPassiveRecoveryEfficiencyPercent;
                ApplyNaturalRecovery(state, character, now, efficiency);
            }

            if (staminaChanged.Count == 0) return new TickResult(0, false, true);

            bool saved;
            try
            {
                saved = saveAction();
            }
            catch
            {
                saved = false;
            }

            if (!saved)
            {
                Rollback();
                return new TickResult(0, true, false);
            }

            for (int i = 0; i < staminaChanged.Count; i++) StaminaChanged?.Invoke(staminaChanged[i]);
            return new TickResult(staminaChanged.Count, true, true);
        }

        private void ApplyNaturalRecovery(CharacterSaveState state, CharacterDefinition character, DateTime now, int efficiency)
        {
            int current = roster.GetStamina(character);
            int maximum = roster.GetMaxStamina(character);
            if (maximum <= 0) return;

            if (current >= maximum || efficiency <= 0)
            {
                ResetProgressAtNow(state, now);
                return;
            }

            if (!TryReadUtc(state.passiveStaminaLastCalculatedUtc, out DateTime last))
            {
                state.passiveStaminaLastCalculatedUtc = FormatUtc(now);
                state.passiveStaminaProgress = 0;
                return;
            }

            // 시계가 뒤로 가면 과거 기준 시각을 낮추지 않는다. 다시 그 시각을 넘기기 전까지는
            // 진행을 멈춰 두어 행동력이 줄거나 같은 구간이 두 번 계산되지 않게 한다.
            if (now <= last) return;

            long denominator = GetProgressDenominator();
            long elapsedTicks = now.Ticks - last.Ticks;
            long progress = NormalizeProgress(state.passiveStaminaProgress, denominator);
            long wholeFromLargeElapsed = (elapsedTicks / denominator) * efficiency;
            long scaledRemainder = (elapsedTicks % denominator) * efficiency + progress;
            long gained = wholeFromLargeElapsed + scaledRemainder / denominator;
            long remainder = scaledRemainder % denominator;

            state.passiveStaminaLastCalculatedUtc = FormatUtc(now);
            state.passiveStaminaProgress = remainder;
            if (gained <= 0) return;

            int missing = maximum - current;
            int target = gained >= missing ? maximum : current + (int)gained;
            if (target == maximum) state.passiveStaminaProgress = 0;
            if (roster.ApplyRecoveryStamina(character, target)) staminaChanged.Add(character);
        }

        private void ResetProgressAtNow(CharacterSaveState state, DateTime now)
        {
            // 미래 기준 시각을 현재보다 과거로 옮기면 시계가 정상화됐을 때 같은 시간이 재사용된다.
            if (!TryReadUtc(state.passiveStaminaLastCalculatedUtc, out DateTime last) || now >= last)
                state.passiveStaminaLastCalculatedUtc = FormatUtc(now);
            state.passiveStaminaProgress = 0;
        }

        private void Track(CharacterSaveState state, CharacterDefinition character)
        {
            for (int i = 0; i < changedStates.Count; i++)
            {
                if (ReferenceEquals(changedStates[i].State, state)) return;
            }

            changedStates.Add(new ChangedState(state, character, state.currentStamina,
                state.passiveStaminaLastCalculatedUtc, state.passiveStaminaProgress));
        }

        private void Rollback()
        {
            for (int i = 0; i < changedStates.Count; i++)
            {
                ChangedState changed = changedStates[i];
                changed.State.passiveStaminaLastCalculatedUtc = changed.LastCalculatedUtc;
                changed.State.passiveStaminaProgress = changed.Progress;
                changed.State.currentStamina = changed.Stamina;
                roster.ApplyRecoveryStamina(changed.Character, changed.Stamina);
            }
        }

        private long GetProgressDenominator()
        {
            return (long)balance.SecondsPerStamina * TimeSpan.TicksPerSecond * PercentScale;
        }

        private static long NormalizeProgress(long progress, long denominator)
        {
            return progress < 0 || progress >= denominator ? 0 : progress;
        }

        private static CharacterSaveState FindState(List<CharacterSaveState> states, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < states.Count; i++)
            {
                CharacterSaveState candidate = states[i];
                if (candidate != null && string.Equals(candidate.characterId, id, StringComparison.Ordinal)) return candidate;
            }
            return null;
        }

        private static bool IsPartyMember(List<string> partyCharacterIds, string id)
        {
            if (partyCharacterIds == null || string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < partyCharacterIds.Count; i++)
            {
                if (string.Equals(partyCharacterIds[i], id, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool TryReadUtc(string value, out DateTime utc)
        {
            if (!string.IsNullOrEmpty(value) && DateTime.TryParseExact(value, RecoveryStation.UtcFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                utc = parsed.ToUniversalTime();
                return true;
            }

            utc = default;
            return false;
        }

        private static string FormatUtc(DateTime utc) => utc.ToUniversalTime().ToString(RecoveryStation.UtcFormat, CultureInfo.InvariantCulture);

        private readonly struct ChangedState
        {
            public readonly CharacterSaveState State;
            public readonly CharacterDefinition Character;
            public readonly int Stamina;
            public readonly string LastCalculatedUtc;
            public readonly long Progress;

            public ChangedState(CharacterSaveState state, CharacterDefinition character, int stamina,
                                string lastCalculatedUtc, long progress)
            {
                State = state;
                Character = character;
                Stamina = stamina;
                LastCalculatedUtc = lastCalculatedUtc;
                Progress = progress;
            }
        }
    }
}
