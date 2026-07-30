using System;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 플레이어의 경험치/레벨/누적 킬카운트를 담당하는 최소 성장 루프. Target.AnyTargetDefeated(정적
    /// 이벤트)를 구독하기 때문에, 어떤 몬스터가 처치되든(지금은 Scarecrow뿐이지만 이후 다른 몬스터가
    /// 추가돼도 별도 연결 없이) 자동으로 경험치와 누적 킬카운트가 오른다 - SessionKillCounter와 같은
    /// 구독 패턴이지만, SessionKillCounter의 세션 킬카운트와 달리 이쪽은 저장 대상이다.
    /// 씬에 하나만 두면 된다. 다른 스크립트는 정적 프로퍼티/이벤트로 상태를 읽는다.
    /// 레벨별 경험치 테이블은 아직 없다 - expToNextLevel 하나만 재사용하고, 초과분은 다음 레벨로
    /// 이월한다. 공격력 증가 등 다른 성장 보상도 아직 없다.
    ///
    /// 저장/불러오기: Awake에서 SaveSystem.Data(공유 저장 문서)를 읽고, 저장 파일이 없거나 손상돼서
    /// SaveSystem.LoadedFromFile이 false면 아래 Inspector 시작값으로 새 게임을 시작한다.
    /// expToNextLevel은 플레이어 상태가 아니라 디자인 값이라 저장 대상이 아니며 항상 Inspector 값을 쓴다.
    /// 저장은 처치 처리(킬카운트+경험치 지급)가 끝난 직후와, 앱 종료 직전(OnApplicationQuit)에만
    /// 한다 - 키 입력/HitPoint마다 저장하지 않는다.
    ///
    /// <b>이 컴포넌트는 저장 문서의 경험치/레벨/누적 킬 필드만 소유한다.</b> 캐릭터별 상태
    /// (SaveData.characters)는 CharacterRoster가 소유하며, 서로의 필드를 읽거나 쓰지 않는다.
    /// </summary>
    public class PlayerProgress : MonoBehaviour
    {
        [Header("Level / Exp (저장 파일이 없을 때만 쓰는 시작값)")]
        [SerializeField] private int currentLevel = 1;
        [SerializeField] private int currentExp = 0;
        [SerializeField] private int totalKillCount = 0;

        [Header("Design (저장 대상 아님)")]
        [SerializeField] private int expToNextLevel = 10;

        [Header("Reward")]
        [Tooltip("Target(허수아비 등) 하나를 처치할 때마다 지급할 경험치")]
        [SerializeField] private int expPerTargetDefeat = 1;

        public static int CurrentLevel { get; private set; }
        public static int CurrentExp { get; private set; }
        public static int ExpToNextLevel { get; private set; }

        /// <summary>저장 데이터 로드가 끝나 CurrentLevel/CurrentExp가 진짜 값인지 여부. Awake 순서상
        /// UI(PlayerProgressDisplay)의 OnEnable이 이 컴포넌트의 Awake보다 먼저 돌 수 있어서, 그때
        /// 읽으면 정적 기본값 0을 "레벨 0"으로 표시하고 그대로 굳어버린다. 표시 쪽은 이 값이 true가
        /// 되기 전에는 아무 것도 그리지 않고, <see cref="OnProgressInitialized"/>를 기다린다.</summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>저장 데이터 로드가 끝난 직후 한 번 발생. 이 신호로 하는 일은 "현재 값을 즉시
        /// 표시"뿐이고, 경험치 획득 연출(레벨업 애니메이션 등)을 시작해서는 안 된다 - 실제 획득은
        /// <see cref="OnExpGained"/>/<see cref="OnLevelUp"/>이 담당한다.</summary>
        public static event Action OnProgressInitialized;

        /// <summary>이번 실행이 아니라 누적으로 처치한 총 횟수. 세션 킬카운트(SessionKillCounter)와 달리 저장된다.</summary>
        public static int TotalKillCount { get; private set; }

        /// <summary>경험치/레벨이 바뀔 때마다 발생. EXP 바·퍼센트·레벨 텍스트 갱신에 쓴다.</summary>
        public static event Action OnExperienceChanged;

        /// <summary>AddExp가 호출될 때마다 실제로 지급된 경험치량과 함께 발생. 토스트 문구에 쓴다.</summary>
        public static event Action<int> OnExpGained;

        /// <summary>레벨이 오를 때마다 발생(한 번에 여러 레벨이 오르면 그 횟수만큼 발생). 새 레벨 값을 전달한다.</summary>
        public static event Action<int> OnLevelUp;

        private void Awake()
        {
            IsInitialized = false;
            ExpToNextLevel = expToNextLevel;

            SaveData save = SaveSystem.Data;
            if (!SaveSystem.LoadedFromFile)
            {
                // 새 게임 - Inspector 시작값을 공유 저장 문서에도 그대로 반영해서, 다른 시스템이
                // 같은 문서를 저장할 때 이 값이 기본값으로 되돌아가지 않게 한다.
                save.currentLevel = currentLevel;
                save.currentExp = currentExp;
                save.totalKillCount = totalKillCount;
            }

            CurrentLevel = save.currentLevel;
            CurrentExp = save.currentExp;
            TotalKillCount = save.totalKillCount;

            // 로드가 끝난 뒤에만 표시를 허용한다 - 이 순간이 "레벨 0에서 시작해 29까지 올라가는" 가짜
            // 레벨업 연출과 실제 경험치 획득 연출을 가르는 경계다.
            IsInitialized = true;
            OnProgressInitialized?.Invoke();
        }

        private void OnEnable()
        {
            Target.AnyTargetDefeated += HandleAnyTargetDefeated;
        }

        private void OnDisable()
        {
            Target.AnyTargetDefeated -= HandleAnyTargetDefeated;
        }

        private void OnApplicationQuit()
        {
            SaveProgress();
        }

        private void HandleAnyTargetDefeated(string targetId)
        {
            TotalKillCount++;
            AddExp(expPerTargetDefeat);
            SaveProgress();
        }

        public void AddExp(int amount)
        {
            if (amount <= 0) return;

            CurrentExp += amount;
            OnExpGained?.Invoke(amount);

            while (ExpToNextLevel > 0 && CurrentExp >= ExpToNextLevel)
            {
                CurrentExp -= ExpToNextLevel;
                CurrentLevel++;
                OnLevelUp?.Invoke(CurrentLevel);
            }

            OnExperienceChanged?.Invoke();
        }

        private void SaveProgress()
        {
            SaveData save = SaveSystem.Data;
            save.currentLevel = CurrentLevel;
            save.currentExp = CurrentExp;
            save.totalKillCount = TotalKillCount;
            SaveSystem.Save();
        }
    }
}
