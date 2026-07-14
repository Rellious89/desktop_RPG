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
    /// 저장/불러오기: Awake에서 SaveSystem.Load()를 먼저 시도하고, 저장 파일이 없거나 손상됐으면
    /// (SaveSystem이 null을 반환하면) 아래 Inspector 시작값으로 새 게임을 시작한다. expToNextLevel은
    /// 플레이어 상태가 아니라 디자인 값이라 저장 대상이 아니며 항상 Inspector 값을 쓴다.
    /// 저장은 처치 처리(킬카운트+경험치 지급)가 끝난 직후와, 앱 종료 직전(OnApplicationQuit)에만
    /// 한다 - 키 입력/HitPoint마다 저장하지 않는다.
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
            ExpToNextLevel = expToNextLevel;

            SaveData save = SaveSystem.Load();
            if (save != null)
            {
                CurrentLevel = save.currentLevel;
                CurrentExp = save.currentExp;
                TotalKillCount = save.totalKillCount;
            }
            else
            {
                CurrentLevel = currentLevel;
                CurrentExp = currentExp;
                TotalKillCount = totalKillCount;
            }
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
            SaveSystem.Save(new SaveData
            {
                currentLevel = CurrentLevel,
                currentExp = CurrentExp,
                totalKillCount = TotalKillCount,
            });
        }
    }
}
