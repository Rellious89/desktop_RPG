using System;
using System.Collections;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 재사용 가능한 내구도/처치/리젠 컴포넌트. 몬스터 등 공격받는 어떤 오브젝트에도 붙여 쓴다.
    /// isDefeated 상태에서는 ApplyDamage 호출을 무시한다 - 처치 중 추가 타격이 들어와도 안전하다.
    /// respawnDelay가 0이면 같은 호출 안에서(다음 프레임 지연 없이) 즉시 리젠된다.
    /// </summary>
    public class Target : MonoBehaviour
    {
        [SerializeField] private string targetId;
        [SerializeField] private int maxDurability = 30;
        [SerializeField] private float respawnDelay = 0f;

        /// <summary>targetId를 비워두면 GameObject 이름을 그대로 쓴다.</summary>
        public string TargetId => string.IsNullOrEmpty(targetId) ? gameObject.name : targetId;
        public int MaxDurability => maxDurability;
        public int CurrentDurability { get; private set; }
        public bool IsDefeated { get; private set; }

        /// <summary>데미지를 실제로 적용했을 때마다 발생. 이번에 받은 데미지량을 전달한다.</summary>
        public event Action<int> OnDamaged;

        /// <summary>이 인스턴스의 내구도가 0 이하가 되는 순간 발생. targetId를 전달한다.</summary>
        public event Action<string> OnDefeated;

        /// <summary>리젠이 끝나 다시 싸울 수 있는 상태로 돌아오는 순간 발생. targetId를 전달한다.</summary>
        public event Action<string> OnRespawned;

        /// <summary>씬의 어떤 Target이 처치되든 발생하는 정적 이벤트. 세션 킬카운트처럼 전역 집계에 쓴다.</summary>
        public static event Action<string> AnyTargetDefeated;

        private Coroutine respawnRoutine;

        private void Awake()
        {
            CurrentDurability = maxDurability;
        }

        private void OnDisable()
        {
            if (respawnRoutine != null)
            {
                StopCoroutine(respawnRoutine);
                respawnRoutine = null;
            }
        }

        public void ApplyDamage(int amount)
        {
            if (amount <= 0 || IsDefeated) return;

            CurrentDurability = Mathf.Max(0, CurrentDurability - amount);
            OnDamaged?.Invoke(amount);

            if (CurrentDurability <= 0)
            {
                Defeat();
            }
        }

        private void Defeat()
        {
            IsDefeated = true;
            OnDefeated?.Invoke(TargetId);
            AnyTargetDefeated?.Invoke(TargetId);

            if (respawnDelay <= 0f)
            {
                Respawn();
            }
            else
            {
                respawnRoutine = StartCoroutine(RespawnAfterDelay());
            }
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);
            respawnRoutine = null;
            Respawn();
        }

        private void Respawn()
        {
            CurrentDurability = maxDurability;
            IsDefeated = false;
            OnRespawned?.Invoke(TargetId);
        }
    }
}
