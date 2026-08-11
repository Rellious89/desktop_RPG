using System;
using Common;

namespace Character
{
    /// <summary>
    /// 경험치를 한 번 넣은 결과. <b>바꿀 수 없는 값</b>이며, 호출부(UI·연출·저장 판단)가 "무엇이
    /// 어떻게 달라졌는가"를 이 하나만 보고 알 수 있도록 <b>이전 값과 이후 값을 함께</b> 담는다 -
    /// 저장 상태를 다시 읽어 비교하게 만들면 그 사이에 값이 또 바뀌었는지 알 수 없다.
    /// </summary>
    public readonly struct CharacterProgressionResult
    {
        /// <summary>어느 캐릭터의 결과인지. 저장 항목의 키와 같은 값이다.</summary>
        public string CharacterId { get; }

        /// <summary>실제로 더해진 경험치. 무시된 요청(0 이하)은 0이다.</summary>
        public int ExperienceAdded { get; }

        /// <summary><b>손대기 전</b> 저장 항목에 적혀 있던 레벨. 정규화 전의 값이므로 1보다 작을 수도
        /// 있다 - 무엇이 고쳐졌는지 보이게 하려고 원본을 그대로 담는다.</summary>
        public int PreviousLevel { get; }

        /// <summary><b>손대기 전</b> 저장 항목에 적혀 있던 경험치. 정규화 전의 값이므로 음수일 수도 있다.</summary>
        public int PreviousExp { get; }

        public int NewLevel { get; }

        public int NewExp { get; }

        /// <summary>이번 호출로 오른 레벨 수. 정규화로 레벨이 <b>내려간</b> 경우(1보다 작은 값을
        /// 고친 경우)에는 0이다 - "얻은 레벨"은 음수가 될 수 없다.</summary>
        public int LevelsGained { get; }

        /// <summary>저장 항목의 레벨이나 경험치가 실제로 달라졌는지. 호출부는 이 값이 true일 때만
        /// 저장하거나 연출을 내면 된다.</summary>
        public bool Changed { get; }

        /// <summary>레벨이 하나라도 올랐는지.</summary>
        public bool LeveledUp => LevelsGained > 0;

        public CharacterProgressionResult(
            string characterId,
            int experienceAdded,
            int previousLevel,
            int previousExp,
            int newLevel,
            int newExp,
            int levelsGained,
            bool changed)
        {
            CharacterId = characterId;
            ExperienceAdded = experienceAdded;
            PreviousLevel = previousLevel;
            PreviousExp = previousExp;
            NewLevel = newLevel;
            NewExp = newExp;
            LevelsGained = levelsGained;
            Changed = changed;
        }

        /// <summary>아무것도 하지 않았을 때의 결과. 이전 값과 이후 값이 같고 <see cref="Changed"/>는 false다.</summary>
        public static CharacterProgressionResult Unchanged(string characterId, int level, int exp)
        {
            return new CharacterProgressionResult(characterId, 0, level, exp, level, exp, 0, false);
        }
    }

    /// <summary>
    /// 캐릭터 한 명의 경험치와 레벨을 계산하는 <b>순수한</b> 규칙.
    ///
    /// <b>저장소도 씬도 에셋도 모른다.</b> 넘겨받은 <see cref="CharacterSaveState"/> 하나만 고치고,
    /// 저장할지 말지·화면에 무엇을 띄울지는 호출부가 정한다 - 그래야 규칙을 디스크 없이 시험할 수 있고,
    /// 나중에 획득 경로가 늘어도 계산이 여러 곳으로 흩어지지 않는다.
    ///
    /// <b>규칙은 지금 단계에서 가장 단순한 형태다.</b> 레벨 하나에 필요한 경험치는
    /// <see cref="ExperiencePerLevel"/>로 <b>고정</b>이며 레벨에 따라 달라지지 않는다(성장 곡선은 아직
    /// 정하지 않았다). 최대 레벨도 두지 않는다 - 상한은 기획이 정할 값이지 이 계산이 정할 값이 아니다.
    ///
    /// <b>남는 경험치는 이월된다.</b> 필요량을 넘긴 만큼은 버려지지 않고 다음 레벨의 진행으로 넘어가며,
    /// 한 번에 여러 레벨이 오를 수도 있다.
    ///
    /// <b>진행도는 하나의 값으로 다룬다.</b> 레벨과 이번 레벨 진행도는 화면에 보이는 표기일 뿐이고,
    /// 계산은 "레벨 1 / 경험치 0에서 여기까지 온 총 진행량" 하나로 한다
    /// (<c>(level - 1) * 필요량 + exp</c>). 그래서 양수를 넣으면 진행량은 <b>절대 뒤로 가지 않는다</b> -
    /// 예전처럼 나머지만 따로 굴리면 상한 근처에서 레벨이 잘리며 진행이 되레 줄어드는 자리가 생겼다.
    ///
    /// <b><see cref="int.MaxValue"/>는 기획상의 최대 레벨이 아니라 저장 칸이 담을 수 있는 한계다.</b>
    /// 표현할 수 있는 마지막 자리는 <c>레벨 = int.MaxValue</c>, <c>경험치 = 필요량 - 1</c>이며, 그 자리를
    /// 넘겨 받은 경험치는 <b>받아들이지 않는다</b> - 담을 수 없는 값을 받은 척하면
    /// <see cref="CharacterProgressionResult.ExperienceAdded"/>가 거짓말이 된다. 그래서 결과의
    /// "더해진 양"은 <b>실제로 받아들인 양</b>이고, 상한에 닿아 있으면 0이다.
    /// </summary>
    public sealed class CharacterProgressionService
    {
        /// <summary>레벨 하나에 필요한 경험치의 기본값.</summary>
        public const int DefaultExperiencePerLevel = 10;

        /// <summary>필요 경험치의 하한. 0이나 음수를 허용하면 경험치 1로 무한히 레벨이 오른다.</summary>
        public const int MinimumExperiencePerLevel = 1;

        /// <summary>레벨의 하한. 저장 항목의 기본값과 같다.</summary>
        public const int MinimumLevel = 1;

        /// <summary>이번 레벨 진행도의 하한.</summary>
        public const int MinimumExp = 0;

        /// <summary>레벨 하나에 필요한 경험치. 모든 레벨에서 같다.</summary>
        public int ExperiencePerLevel { get; }

        public CharacterProgressionService()
            : this(DefaultExperiencePerLevel)
        {
        }

        /// <param name="experiencePerLevel">레벨 하나에 필요한 경험치.
        /// <see cref="MinimumExperiencePerLevel"/>보다 작으면 그 값으로 올린다 - 0 이하를 그대로 받으면
        /// 나눗셈이 성립하지 않거나 경험치 1로 레벨이 끝없이 오른다.</param>
        public CharacterProgressionService(int experiencePerLevel)
        {
            ExperiencePerLevel = experiencePerLevel < MinimumExperiencePerLevel
                ? MinimumExperiencePerLevel
                : experiencePerLevel;
        }

        /// <summary>
        /// 경험치를 넣는다. <paramref name="amount"/>가 <b>0 이하면 아무 일도 하지 않는다</b> -
        /// 저장 항목을 읽지도 고치지도 않으며 정규화도 하지 않는다. "아무것도 주지 않는 요청"이
        /// 값을 조용히 바꾸면, 보상이 0인 경로가 지나갈 때마다 상태가 달라진다.
        ///
        /// 양수를 넣을 때는 <b>먼저 정규화</b>한다(1보다 작은 레벨과 음수 경험치를 고친다) - 그 상태에서
        /// 계산해야 결과가 뜻을 갖기 때문이다.
        ///
        /// 계산은 나눗셈 한 번이다. 레벨을 하나씩 올리는 반복문을 쓰지 않는 이유는
        /// <see cref="int.MaxValue"/>에 가까운 값이 들어오면 그 반복이 사실상 끝나지 않기 때문이다.
        ///
        /// <b>담을 수 있는 만큼만 받아들인다.</b> 표현할 수 있는 마지막 자리까지 남은 여유보다 많이
        /// 들어오면 그 여유만큼만 받고 나머지는 받지 않으며, 결과의
        /// <see cref="CharacterProgressionResult.ExperienceAdded"/>에는 <b>실제로 받아들인 양</b>이
        /// 적힌다 - 요청한 값을 그대로 적으면 "받았다는데 진행이 그대로"인 결과가 나온다.
        /// </summary>
        /// <returns>이전 값과 이후 값을 함께 담은 결과. 무시된 요청은
        /// <see cref="CharacterProgressionResult.Changed"/>가 false다.</returns>
        public CharacterProgressionResult Grant(CharacterSaveState state, int amount)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            int previousLevel = state.level;
            int previousExp = state.currentExp;

            // 0 이하는 그대로 돌려보낸다. 정규화조차 하지 않는 것이 중요하다 - 보상이 0인 경로가
            // 저장 항목을 고치기 시작하면 "무엇이 값을 바꿨는가"를 아무도 짚을 수 없다.
            if (amount <= 0) return CharacterProgressionResult.Unchanged(state.characterId, previousLevel, previousExp);

            int level = ClampLevel(previousLevel);

            // 지금 서 있는 진행량. 담을 수 있는 마지막 자리를 넘겨 적혀 있었다면(어긋난 값) 여기서
            // 그 자리로 끌어내린다 - 양수를 넣을 때는 먼저 정규화한다는 규칙 그대로다.
            long capped = Math.Min(ProgressOf(level, ClampExp(previousExp)), MaxProgress);

            // 마지막 자리까지 남은 여유. 이보다 많이 들어와도 그만큼만 받는다.
            long capacity = MaxProgress - capped;
            long accepted = Math.Min(amount, capacity);

            ApplyProgress(state, capped + accepted);

            int levelsGained = state.level > level ? state.level - level : 0;
            bool changed = state.level != previousLevel || state.currentExp != previousExp;

            return new CharacterProgressionResult(
                state.characterId, (int)accepted, previousLevel, previousExp,
                state.level, state.currentExp, levelsGained, changed);
        }

        /// <summary>
        /// 저장 항목의 레벨과 경험치를 <b>쓸 수 있는 값으로만</b> 맞춘다. 경험치를 더하지 않는다.
        ///
        /// 이것이 <b>명시적인</b> 정규화 경로다. 값을 읽기만 하는 곳에서 저절로 불리지 않으며,
        /// 부르는 쪽이 "지금 고쳐 두겠다"고 정할 때만 쓴다 - 조회가 값을 고치기 시작하면 무엇이
        /// 언제 바뀌었는지 알 수 없다.
        ///
        /// 모아 둔 경험치가 필요량을 이미 넘긴 상태(다른 경로로 값이 어긋난 경우)도 여기서 정리한다.
        /// </summary>
        public CharacterProgressionResult Normalize(CharacterSaveState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            int previousLevel = state.level;
            int previousExp = state.currentExp;

            int level = ClampLevel(previousLevel);

            // 이미 필요량을 넘겨 모여 있던 경험치는 레벨로 바뀌고(이월 규칙과 같다), 담을 수 있는
            // 마지막 자리를 넘겼다면 그 자리로 내려온다.
            ApplyProgress(state, Math.Min(ProgressOf(level, ClampExp(previousExp)), MaxProgress));

            int levelsGained = state.level > level ? state.level - level : 0;
            bool changed = state.level != previousLevel || state.currentExp != previousExp;

            return new CharacterProgressionResult(
                state.characterId, 0, previousLevel, previousExp,
                state.level, state.currentExp, levelsGained, changed);
        }

        /// <summary>
        /// 그 레벨에서 다음 레벨로 가는 데 필요한 <b>총량</b>. 지금은 모든 레벨에서 같은 값이지만,
        /// 레벨을 인자로 받아 두는 이유는 나중에 성장 곡선이 생겨도 <b>부르는 쪽을 고치지 않기</b>
        /// 위해서다 - 계산이 늘어날 자리를 여기 하나로 정해 둔다.
        ///
        /// <b>"남은 양"이 아니라 "필요한 총량"이다.</b> 계정 진행도 쪽의
        /// <c>PlayerProgress.ExpToNextLevel</c>이 이미 총량을 뜻하므로 같은 말이 두 곳에서 다른 값을
        /// 가리키지 않게 이름을 맞췄다. 지금 모아 둔 값에서 <b>얼마나 더</b> 필요한지는
        /// <see cref="ExperienceRemainingToNextLevel"/>가 답한다.
        ///
        /// <b>어떤 값도 고치지 않는다</b>(순수한 조회). 1보다 작은 레벨은 계산할 때만 하한으로 본다.
        /// </summary>
        public int GetRequiredExperience(int level)
        {
            // 레벨은 아직 결과에 쓰이지 않는다 - 필요량이 고정이기 때문이다. 그래도 하한을 맞춰
            // 두는 것은, 곡선이 생기는 순간 이 자리에서 음수 레벨을 그대로 쓰게 두지 않기 위함이다.
            _ = ClampLevel(level);

            return ExperiencePerLevel;
        }

        /// <summary>
        /// 다음 레벨까지 <b>앞으로 더 필요한</b> 경험치. <see cref="GetRequiredExperience"/>가 총량을
        /// 말하는 것과 달리 이쪽은 지금 모아 둔 값을 뺀 나머지다.
        ///
        /// <b>저장 항목을 고치지 않는다</b> - 읽기만 하는 값이라 정규화도 하지 않고, 어긋난 값은
        /// 계산할 때만 하한으로 본다.
        /// </summary>
        public int ExperienceRemainingToNextLevel(CharacterSaveState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            int required = GetRequiredExperience(state.level);
            int exp = ClampExp(state.currentExp);

            // 담을 수 있는 마지막 레벨에서는 "다음 레벨"이 없다. 남은 것은 <b>표현할 수 있는 여유</b>
            // 뿐이므로 그것을 답한다 - 마지막 자리(경험치 = 필요량 - 1)에서는 0이다.
            if (state.level >= MaxRepresentableLevel)
            {
                int room = required - 1 - exp;
                return room < 0 ? 0 : room;
            }

            int remaining = required - (exp % required);
            return remaining == 0 ? required : remaining;
        }

        /// <summary>
        /// 저장 칸이 담을 수 있는 마지막 레벨. <b>기획이 정한 최대 레벨이 아니다</b> - 설계상의 상한은
        /// 없고, 이것은 <c>int</c>가 더 큰 수를 담지 못해서 생기는 한계일 뿐이다.
        /// </summary>
        public const int MaxRepresentableLevel = int.MaxValue;

        /// <summary>
        /// 표현할 수 있는 마지막 진행량. 레벨이 <see cref="MaxRepresentableLevel"/>이고 이번 레벨
        /// 진행도가 <c>필요량 - 1</c>인 자리다 - 여기서 1을 더하면 담을 칸이 없다.
        /// </summary>
        private long MaxProgress => ProgressOf(MaxRepresentableLevel, ExperiencePerLevel - 1);

        /// <summary>
        /// 레벨/경험치 표기를 <b>하나의 진행량</b>으로 편다. 레벨 1 / 경험치 0이 0이다.
        ///
        /// <c>long</c>으로 계산하는 이유는 <c>(int.MaxValue - 1) * 필요량</c>이 <c>int</c>를 훌쩍 넘기
        /// 때문이다. 필요량과 레벨이 모두 <see cref="int.MaxValue"/>여도 곱은 약 4.6e18이라
        /// <c>long</c>(약 9.2e18) 안에 들어온다.
        /// </summary>
        private long ProgressOf(int level, int exp)
        {
            return ((long)level - 1) * ExperiencePerLevel + exp;
        }

        /// <summary>진행량을 다시 레벨/경험치 표기로 되돌려 저장 항목에 적는다.</summary>
        private void ApplyProgress(CharacterSaveState state, long progress)
        {
            if (progress < 0) progress = 0;
            if (progress > MaxProgress) progress = MaxProgress;

            state.level = (int)(progress / ExperiencePerLevel + 1);
            state.currentExp = (int)(progress % ExperiencePerLevel);
        }

        private static int ClampLevel(int level)
        {
            return level < MinimumLevel ? MinimumLevel : level;
        }

        private static int ClampExp(int exp)
        {
            return exp < MinimumExp ? MinimumExp : exp;
        }
    }
}
