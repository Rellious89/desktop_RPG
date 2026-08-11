using System;
using System.Collections.Generic;
using Character;
using Skill;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 처치 보상을 받아 <b>지금 전투 중인 캐릭터</b>를 성장시키고, 누적 킬카운트를 소유하는 성장 루프.
    /// Target.AnyTargetDefeated(정적 이벤트)를 구독하기 때문에 어떤 몬스터가 처치되든 별도 연결 없이
    /// 자동으로 처리된다 - SessionKillCounter/CharacterRoster와 같은 구독 패턴이다.
    /// 씬에 하나만 두면 된다. 다른 스크립트는 정적 프로퍼티/이벤트로 상태를 읽는다.
    ///
    /// <b>레벨과 경험치는 더 이상 계정 전역 값이 아니다.</b> 예전에는 SaveData.currentLevel/currentExp
    /// 하나가 "플레이어의 레벨"이었지만, 캐릭터별 상태(SaveData.characters)가 생긴 뒤로 그 값은 누가
    /// 싸우든 같이 올라가는 <b>주인 없는 숫자</b>가 됐다. 이제 성장은 처치 시점에 실제로 투입돼 있던
    /// 캐릭터의 저장 항목에만 쌓인다.
    ///   - <see cref="CurrentLevel"/>/<see cref="CurrentExp"/>/<see cref="ExpToNextLevel"/>는
    ///     <b>지금 전투 중인 캐릭터</b>의 값을 비춘다(캐릭터가 없으면 안전한 기본값).
    ///   - <see cref="TotalKillCount"/>만 예전 그대로 계정 전역 값이다.
    ///
    /// <b>예전 전역 필드는 읽지도 쓰지도 않는다.</b> SaveData.currentLevel/currentExp는 이 컴포넌트가
    /// 더 이상 손대지 않으므로 <b>파일에 적혀 있던 값이 그대로 보존된다</b> - 지우지 않는 이유는,
    /// 계정 단위 성장을 다시 쓰게 될지 아직 정해지지 않았고 한 번 지운 값은 되돌릴 수 없기 때문이다.
    ///
    /// <b>계산 규칙은 <see cref="CharacterProgressionService"/> 하나가 소유한다.</b> 이 컴포넌트는
    /// "언제 얼마를 주는가"와 "무엇을 저장하고 무엇을 알리는가"만 정하고, 레벨이 몇이 되는지는
    /// 계산하지 않는다 - 규칙이 두 곳에 있으면 화면과 저장이 서로 다른 답을 하게 된다.
    ///
    /// <b>보유하지 않은 캐릭터에는 한 톨도 주지 않는다.</b> 로스터가 없거나, 투입된 캐릭터가 없거나,
    /// 그 캐릭터의 보유가 사라졌거나, 카탈로그가 없는 과도기 씬이면 캐릭터 경험치는 지급되지 않고
    /// <b>저장 문서에 항목이 생기지도 않는다</b>. 그래도 <b>정상적인 처치라면 누적 킬카운트는 오른다</b> -
    /// 그것은 캐릭터가 아니라 계정이 한 일이기 때문이다.
    ///
    /// 저장/불러오기: Awake에서 SaveSystem.Data(공유 저장 문서)를 읽고, 저장 파일이 없거나 손상돼서
    /// SaveSystem.LoadedFromFile이 false면 아래 Inspector 시작값으로 새 게임을 시작한다.
    /// expToNextLevel은 플레이어 상태가 아니라 디자인 값이라 저장 대상이 아니다.
    /// 저장은 처치 처리가 끝난 직후와 앱 종료 직전(OnApplicationQuit)에만 하며, <b>처치 하나당 이
    /// 컴포넌트의 저장은 정확히 한 번</b>이다(레벨이 여러 단계 올라도 마찬가지다).
    /// </summary>
    public class PlayerProgress : MonoBehaviour
    {
        [Header("Kill Count (저장 파일이 없을 때만 쓰는 시작값)")]
        [SerializeField] private int totalKillCount = 0;

        [Header("Design (저장 대상 아님)")]
        [Tooltip("레벨 하나에 필요한 경험치의 총량. 모든 레벨에서 같다(성장 곡선은 아직 없다).")]
        [Min(CharacterProgressionService.MinimumExperiencePerLevel)]
        [SerializeField] private int expToNextLevel = CharacterProgressionService.DefaultExperiencePerLevel;

        [Header("Reward")]
        [Tooltip("Target(허수아비 등) 하나를 처치할 때마다 지금 전투 중인 캐릭터에게 지급할 경험치")]
        [SerializeField] private int expPerTargetDefeat = 1;

        [Header("Skill Unlock (연결하지 않으면 해금 신호가 나가지 않는다)")]
        [Tooltip("정식 스킬 목록. 캐릭터 카탈로그는 CharacterRoster가 쓰는 것을 그대로 따르므로 " +
                 "여기에 다시 두지 않는다.")]
        [SerializeField] private SkillCatalog skillCatalog;

        [Tooltip("캐릭터-스킬 관계 목록. 지금 표에는 관계 행이 하나도 없어 비어 있는 것이 정상이며, " +
                 "그때는 해금 신호가 하나도 나가지 않는다.")]
        [SerializeField] private CharacterSkillCatalog characterSkillCatalog;

        /// <summary>지금 전투 중인 캐릭터의 레벨. 표시용이라 언제나 하한(1) 이상이다.</summary>
        public static int CurrentLevel { get; private set; } = CharacterProgressionService.MinimumLevel;

        /// <summary>지금 전투 중인 캐릭터가 이번 레벨에서 모은 경험치. 언제나 0 이상이다.</summary>
        public static int CurrentExp { get; private set; }

        /// <summary>다음 레벨까지 필요한 <b>총량</b>(남은 양이 아니다). EXP 바의 분모가 이 값이다.</summary>
        public static int ExpToNextLevel { get; private set; } = CharacterProgressionService.DefaultExperiencePerLevel;

        /// <summary>저장 데이터 로드가 끝나 위 값들이 진짜 값인지 여부. Awake 순서상 UI
        /// (PlayerProgressDisplay)의 OnEnable이 이 컴포넌트의 Awake보다 먼저 돌 수 있어서, 그때
        /// 읽으면 로드 전 값을 그대로 굳혀 버린다. 표시 쪽은 이 값이 true가 되기 전에는 아무 것도
        /// 그리지 않고, <see cref="OnProgressInitialized"/>를 기다린다.</summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>저장 데이터 로드가 끝난 직후 한 번 발생. 이 신호로 하는 일은 "현재 값을 즉시
        /// 표시"뿐이고, 경험치 획득 연출(레벨업 애니메이션 등)을 시작해서는 안 된다 - 실제 획득은
        /// <see cref="OnExpGained"/>/<see cref="OnLevelUp"/>이 담당한다.</summary>
        public static event Action OnProgressInitialized;

        /// <summary>
        /// <b>보고 있는 캐릭터가 바뀌어</b> 위 값들이 통째로 다른 캐릭터의 것이 됐을 때 발생한다.
        /// 캐릭터 교체와 시작 시점의 동기화가 여기로 온다.
        ///
        /// <b>획득 신호와 반드시 구분한다.</b> 교체는 경험치를 얻은 것이 아니므로
        /// <see cref="OnExpGained"/>/<see cref="OnLevelUp"/>/<see cref="OnExperienceChanged"/>는
        /// 하나도 발생하지 않는다 - 그것들을 대신 쓰면 Lv.3 캐릭터에서 Lv.12 캐릭터로 갈아탄 순간
        /// 레벨업 연출이 아홉 번 쏟아지고, 바가 뒤로 가는 교체는 "경험치를 잃은" 것처럼 보인다.
        /// 이 신호를 받은 쪽은 <b>진행 중인 연출을 취소하고 즉시</b> 새 값으로 맞춘다.
        /// </summary>
        public static event Action OnCurrentCharacterSynchronized;

        /// <summary>이번 실행이 아니라 누적으로 처치한 총 횟수. 세션 킬카운트(SessionKillCounter)와 달리 저장된다.</summary>
        public static int TotalKillCount { get; private set; }

        /// <summary>지금 전투 중인 캐릭터의 경험치/레벨이 <b>실제로 자랐을 때</b> 발생. EXP 바·퍼센트·
        /// 레벨 텍스트 갱신에 쓴다. 캐릭터 교체로 값이 달라진 것은 여기가 아니라
        /// <see cref="OnCurrentCharacterSynchronized"/>다.</summary>
        public static event Action OnExperienceChanged;

        /// <summary>경험치가 실제로 지급될 때마다 <b>실제로 받아들여진 양</b>과 함께 발생. 토스트 문구에
        /// 쓴다. 요청한 양이 아니라 받아들여진 양인 이유는, 저장 칸의 한계에 닿으면 요청보다 적게
        /// 들어갈 수 있기 때문이다(<see cref="CharacterProgressionService"/> 참고).</summary>
        public static event Action<int> OnExpGained;

        /// <summary>레벨이 오를 때마다 발생(한 번에 여러 레벨이 오르면 그 횟수만큼 발생). 새 레벨 값을 전달한다.</summary>
        public static event Action<int> OnLevelUp;

        /// <summary>
        /// 레벨이 올라 스킬이 <b>새로 열렸을 때</b> 그 관계 하나마다 한 번씩 발생. 어떤 캐릭터의 어떤
        /// 스킬인지 함께 보낸다 - 받는 쪽(토스트·스킬 목록)이 무엇을 그릴지 정할 수 있어야 한다.
        ///
        /// <b>해금은 어디에도 저장되지 않는다.</b> 이 신호는 "방금 조건을 넘었다"는 알림일 뿐이고,
        /// 지금 열려 있는지는 언제나 <see cref="CharacterSkillUnlockService"/>가 표와 레벨로 다시
        /// 계산해서 답한다 - 이 신호를 놓쳤다고 스킬이 잠기지 않는다.
        ///
        /// 나가지 않는 경우: 레벨이 오르지 않은 성장(어긋난 값의 정리), 이미 조건을 넘긴 뒤의 반복
        /// 획득, 캐릭터 교체, 줄 대상이 없는 처치, 그리고 표에 관계가 하나도 없는 지금 상태.
        /// </summary>
        public static event Action<CharacterDefinition, SkillDefinition> OnSkillUnlocked;

        /// <summary>같은 처치 이벤트가 두 번 들어와도 킬카운트와 경험치가 두 번 오르지 않게 막는 최소
        /// 방어(판정 규칙은 <see cref="DefeatEventFilter"/> 참고). <b>이 컴포넌트만의 필터</b>다 -
        /// 행동력 쪽(CharacterRoster)과 하나를 공유하면 먼저 처리한 쪽이 다른 쪽의 이벤트를 삼킨다.</summary>
        private readonly DefeatEventFilter defeatFilter = new DefeatEventFilter();

        private CharacterProgressionService progression;

        /// <summary>성장 규칙. Inspector의 디자인 값으로 한 번 만들고 계속 쓴다. Awake보다 먼저
        /// 값을 물어보는 경로가 있어도 안전하도록 필요할 때 만든다.</summary>
        private CharacterProgressionService Progression =>
            progression ??= new CharacterProgressionService(expToNextLevel);

        private void Awake()
        {
            IsInitialized = false;

            SaveData save = SaveSystem.Data;
            if (!SaveSystem.LoadedFromFile)
            {
                // 새 게임 - Inspector 시작값을 공유 저장 문서에도 그대로 반영해서, 다른 시스템이
                // 같은 문서를 저장할 때 이 값이 기본값으로 되돌아가지 않게 한다. 예전 전역
                // 레벨/경험치는 여기서도 건드리지 않는다.
                save.totalKillCount = totalKillCount;
            }

            TotalKillCount = save.totalKillCount;
            PublishCurrentCharacterSnapshot();

            // 로드가 끝난 뒤에만 표시를 허용한다 - 이 순간이 "레벨 0에서 시작해 올라가는" 가짜
            // 레벨업 연출과 실제 경험치 획득 연출을 가르는 경계다.
            IsInitialized = true;
            OnProgressInitialized?.Invoke();
        }

        private void OnEnable()
        {
            Target.AnyTargetDefeated += HandleAnyTargetDefeated;
            CharacterRoster.CurrentCharacterChanged += HandleCurrentCharacterChanged;
        }

        private void OnDisable()
        {
            Target.AnyTargetDefeated -= HandleAnyTargetDefeated;
            CharacterRoster.CurrentCharacterChanged -= HandleCurrentCharacterChanged;
        }

        /// <summary>
        /// <b>Awake 순서에 기대지 않기 위한 자리.</b> CharacterRoster는 자기 Awake에서 시작 캐릭터를
        /// 투입하며 <see cref="CharacterRoster.CurrentCharacterChanged"/>를 보내는데, 그 Awake가 이
        /// 컴포넌트의 OnEnable보다 먼저 돌면 <b>그 신호를 아무도 듣지 못한다</b> - 구독만으로는
        /// 놓친 이벤트를 되찾을 수 없다.
        ///
        /// Start는 씬의 모든 Awake가 끝난 뒤에 돌기 때문에, 여기서 현재 캐릭터를 다시 한 번 그대로
        /// 읽어 맞추면 두 순서 중 어느 쪽이든 같은 결과가 된다(이미 맞아 있으면 같은 값을 다시 넣을
        /// 뿐이다).
        /// </summary>
        private void Start()
        {
            SynchronizeToCurrentCharacter();
        }

        private void OnApplicationQuit()
        {
            SaveProgress();
        }

        /// <summary>전투 캐릭터가 바뀌었다 - 보고 있는 대상이 통째로 달라졌으므로 획득 연출 없이
        /// 즉시 새 값으로 맞춘다.</summary>
        private void HandleCurrentCharacterChanged(CharacterDefinition definition)
        {
            SynchronizeToCurrentCharacter();
        }

        private void SynchronizeToCurrentCharacter()
        {
            PublishCurrentCharacterSnapshot();
            OnCurrentCharacterSynchronized?.Invoke();
        }

        /// <summary>
        /// 처치가 확정된 순간. 순서가 중요하다.
        ///   1. <b>먼저</b> 이 이벤트가 처리할 값인지 가린다(빈 id, 같은 프레임의 같은 id). 걸러진
        ///      이벤트는 킬카운트도 경험치도 <b>둘 다</b> 건드리지 않는다 - 한쪽만 걸러지면 두 값이
        ///      서로 어긋난다.
        ///   2. 누적 킬카운트는 <b>캐릭터와 무관하게</b> 오른다. 아무도 투입되지 않았어도 처치는
        ///      일어난 일이다.
        ///   3. 경험치는 지금 투입된 보유 캐릭터에게만 간다.
        ///   4. <b>저장은 알리기 전에 한 번</b>. 레벨이 몇 단계를 오르든 파일 쓰기는 한 번이다.
        ///
        /// <b>알리기 전에 저장한다.</b> 구독자(토스트·스킬 목록·교체 패널)는 알림을 받은 순간 값을
        /// 다시 읽고, 그 자리에서 다른 저장을 부르는 것도 있다 - 아직 저장하지 않은 상태에서 알리면
        /// "화면에는 올라간 레벨이 보이는데 파일에는 없는" 창이 열리고, 그 사이에 앱이 꺼지면 사용자가
        /// 본 것과 다음에 불러오는 것이 달라진다. 저장을 마친 뒤에 알리면 알림이 언제나 <b>이미 남은
        /// 사실</b>을 가리킨다.
        ///
        /// <b>행동력이 0이 되는 마지막 처치도 경험치를 받는다.</b> 여기서는 행동력을 아예 보지 않기
        /// 때문에, 행동력을 깎는 CharacterRoster가 먼저 처리되든 나중에 처리되든 결과가 같다 -
        /// "마지막 한 방은 보상이 없다"는 구독 순서에 딸린 우연한 규칙을 만들지 않는다.
        /// </summary>
        private void HandleAnyTargetDefeated(string targetId)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                Debug.LogWarning("[PlayerProgress] 처치 이벤트의 targetId가 비어 있어 무시했습니다 - " +
                                 "id가 없으면 중복인지 가릴 수 없습니다.", this);
                return;
            }

            if (!defeatFilter.Accept(targetId, this)) return;

            TotalKillCount++;

            // 값을 먼저 전부 고치고, 한 번 저장하고, 그 다음에 알린다. 킬카운트와 경험치가 같은 파일
            // 쓰기 하나에 함께 들어가는 것도 이 순서 덕분이다.
            bool grew = ApplyExperienceToCurrentCharacter(expPerTargetDefeat, out Growth growth);
            SaveProgress();
            if (grew) RaiseGrowthEvents(growth);
        }

        /// <summary>
        /// 지금 전투 중인 캐릭터에게 경험치를 준다. 공개 모양은 예전 그대로다.
        ///
        /// <b>실제로 값이 달라졌을 때만</b> 저장하고 알린다 - 줄 캐릭터가 없거나, 0 이하를 넣었거나,
        /// 저장 칸의 한계에 닿아 한 톨도 들어가지 않았으면 파일도 이벤트도 건드리지 않는다.
        ///
        /// <b>저장이 먼저다.</b> 구독자가 알림을 받는 순간에는 그 값이 이미 파일에 남아 있어야 한다.
        /// </summary>
        public void AddExp(int amount)
        {
            if (!ApplyExperienceToCurrentCharacter(amount, out Growth growth)) return;

            SaveProgress();
            RaiseGrowthEvents(growth);
        }

        /// <summary>
        /// 경험치를 실제로 적용하고 표시값을 맞춘다. <b>저장하지도 알리지도 않는다</b> - 저장과 알림의
        /// 순서를 부르는 쪽이 정할 수 있도록 "값 고치기"와 "알리기"를 갈라 둔다.
        /// </summary>
        /// <returns>저장 항목이 실제로 달라졌으면 true. 그때만 <paramref name="growth"/>가 뜻을 갖는다.</returns>
        private bool ApplyExperienceToCurrentCharacter(int amount, out Growth growth)
        {
            growth = default;

            if (amount <= 0) return false;

            // 투입된 보유 캐릭터가 없으면 여기서 끝난다 - 항목을 만들지 않으므로 "주려고 했다"는
            // 사실만으로 캐릭터가 지급되거나 없던 상태가 생기지 않는다.
            if (!TryGetCurrentCharacterState(
                    out CharacterRoster roster, out CharacterDefinition canonical, out CharacterSaveState state))
            {
                return false;
            }

            CharacterProgressionResult result = Progression.Grant(state, amount);
            if (!result.Changed) return false;

            PublishSnapshot(result.NewLevel, result.NewExp);

            growth = new Growth(roster, canonical, result);
            return true;
        }

        /// <summary>이번 성장을 구독자에게 알린다. <b>저장이 이미 끝난 뒤에만</b> 불린다.</summary>
        private void RaiseGrowthEvents(Growth growth)
        {
            CharacterProgressionResult result = growth.Result;

            // 실제로 들어간 양이 0이면 알리지 않는다. 0을 얻었다는 토스트는 사용자에게 거짓말이고,
            // 여기까지 온 것은 어긋난 값이 정리된 경우뿐이다(저장 칸의 한계에 닿은 자리).
            if (result.ExperienceAdded > 0) OnExpGained?.Invoke(result.ExperienceAdded);

            // 오른 레벨마다 한 번씩, 예전처럼 "새 레벨 값"을 넘긴다.
            int levelBeforeGrowth = result.NewLevel - result.LevelsGained;
            for (int i = 1; i <= result.LevelsGained; i++) OnLevelUp?.Invoke(levelBeforeGrowth + i);

            OnExperienceChanged?.Invoke();

            // 값이 먼저 맞춰진 뒤에 그 결과(새로 열린 스킬)를 알린다 - 받는 쪽이 새 레벨을 이미 볼 수
            // 있는 상태여야 한다.
            RaiseSkillUnlocks(
                growth.Roster, growth.Character, levelBeforeGrowth, result.NewLevel, result.LevelsGained);

            // 캐릭터 교체 패널·회복소의 레벨 표시가 이 이벤트를 이미 구독하고 있다. 직접 부르지 않고
            // 로스터의 경로를 지나는 이유는, 그쪽이 <b>정식 정의</b>를 넘기고 보유가 사라진 캐릭터를
            // 걸러 주기 때문이다 - 알림 규칙을 여기 한 벌 더 두지 않는다.
            growth.Roster.RaiseCharacterStateChanged(growth.Character);
        }

        /// <summary>값을 고친 결과 중 <b>알릴 때 필요한 것</b>만 담아 두는 자리. 알리는 시점에 저장
        /// 항목을 다시 읽지 않기 위해서다 - 그 사이에 값이 또 바뀌었는지 알 수 없다.</summary>
        private readonly struct Growth
        {
            public CharacterRoster Roster { get; }

            public CharacterDefinition Character { get; }

            public CharacterProgressionResult Result { get; }

            public Growth(CharacterRoster roster, CharacterDefinition character, CharacterProgressionResult result)
            {
                Roster = roster;
                Character = character;
                Result = result;
            }
        }

        /// <summary>
        /// 이번 성장으로 <b>새로 열린</b> 스킬을 관계 하나마다 한 번씩 알린다.
        ///
        /// <b>레벨이 실제로 올랐을 때만 계산한다.</b> 어긋난 값이 정리되기만 한 성장은 조건을 넘긴 것이
        /// 아니므로 볼 것이 없고, 여기서 계산하면 저장 파일을 고칠 때마다 같은 스킬이 "새로 열렸다"고
        /// 나온다.
        ///
        /// <b>구간 계산은 한 번뿐이다.</b> 레벨이 한 번에 여러 단계 올라도 이전 레벨과 새 레벨을 한
        /// 구간으로 놓고 한 번 묻는다 - 단계마다 나눠 물으면 같은 스킬이 여러 번 나올 자리가 생긴다.
        ///
        /// <b>저장하지 않는다.</b> 해금은 어디에도 적히지 않으므로 이 계산은 파일을 건드릴 이유가
        /// 없다 - 처치 하나당 저장 한 번이라는 규칙이 이것 때문에 깨지면 안 된다.
        /// </summary>
        private void RaiseSkillUnlocks(
            CharacterRoster roster, CharacterDefinition canonical,
            int previousLevel, int newLevel, int levelsGained)
        {
            if (levelsGained <= 0) return;
            if (OnSkillUnlocked == null) return;

            // 카탈로그는 로스터가 쓰는 것을 그대로 따른다 - 여기에 한 벌 더 두면 두 곳이 서로 다른
            // 캐릭터 목록을 가리키는 씬을 만들 수 있다.
            var unlocks = new CharacterSkillUnlockService(
                roster.Catalog, skillCatalog, characterSkillCatalog, SaveSystem.Data);

            IReadOnlyList<SkillDefinition> opened =
                unlocks.GetNewlyUnlockedSkills(canonical.CharacterId, previousLevel, newLevel);

            for (int i = 0; i < opened.Count; i++) OnSkillUnlocked?.Invoke(canonical, opened[i]);
        }

        /// <summary>지금 전투 중인 캐릭터의 값을 정적 프로퍼티에 비춘다. 로스터·캐릭터가 없으면
        /// 안전한 기본값(레벨 1 / 경험치 0)이다 - 표시가 "레벨 0"으로 굳는 자리를 만들지 않는다.</summary>
        private void PublishCurrentCharacterSnapshot()
        {
            if (!TryGetCurrentCharacterState(out _, out _, out CharacterSaveState state))
            {
                PublishSnapshot(CharacterProgressionService.MinimumLevel, CharacterProgressionService.MinimumExp);
                return;
            }

            PublishSnapshot(state.level, state.currentExp);
        }

        /// <summary>표시용 값으로 다듬어 싣는다. <b>저장 항목을 고치지 않는다</b> - 어긋난 값(1보다 작은
        /// 레벨, 음수 경험치)은 보이는 자리에서만 하한으로 본다. 조회가 저장을 고치기 시작하면 무엇이
        /// 언제 바뀌었는지 아무도 짚을 수 없다.</summary>
        private void PublishSnapshot(int level, int exp)
        {
            int safeLevel = level < CharacterProgressionService.MinimumLevel
                ? CharacterProgressionService.MinimumLevel
                : level;

            CurrentLevel = safeLevel;
            CurrentExp = exp < CharacterProgressionService.MinimumExp ? CharacterProgressionService.MinimumExp : exp;
            ExpToNextLevel = Progression.GetRequiredExperience(safeLevel);
        }

        /// <summary>지금 전투 중인 보유 캐릭터의 정식 정의와 저장 항목, 그리고 그것을 답해 준 로스터.
        /// 로스터가 없는 씬에서도 안전하게 false다.
        ///
        /// 로스터를 함께 돌려주는 이유는, 상태를 고친 뒤 알릴 때 <see cref="CharacterRoster.Instance"/>를
        /// <b>다시 읽지 않기</b> 위해서다 - 그 사이에 인스턴스가 바뀌면 방금 고친 것과 다른 로스터에게
        /// 알리게 된다.</summary>
        private static bool TryGetCurrentCharacterState(
            out CharacterRoster roster, out CharacterDefinition canonical, out CharacterSaveState state)
        {
            canonical = null;
            state = null;

            roster = CharacterRoster.Instance;
            return roster != null && roster.TryGetCurrentState(out canonical, out state);
        }

        /// <summary>이 컴포넌트가 소유하는 저장 값을 문서에 싣고 한 번 저장한다.
        ///
        /// <b>싣는 것은 누적 킬카운트 하나뿐이다.</b> 캐릭터의 레벨/경험치는 이미
        /// <see cref="CharacterSaveState"/>에 직접 적혀 있으므로 여기서 다시 옮길 것이 없고,
        /// 예전 전역 필드(SaveData.currentLevel/currentExp)는 <b>한 글자도 쓰지 않는다</b> -
        /// 파일에 남아 있던 값이 그대로 보존된다.</summary>
        private void SaveProgress()
        {
            SaveSystem.Data.totalKillCount = TotalKillCount;
            SaveSystem.Save();
        }
    }
}
