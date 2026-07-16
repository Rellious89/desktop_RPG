using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 피격 지점에 이펙트 prefab을 생성하는 재사용 가능한 컴포넌트. Target/DamageNumberSpawner와
    /// 마찬가지로 피격받는 어떤 오브젝트에도 붙여 쓴다.
    ///
    /// 지금은 기본 타격 이펙트(defaultEffectPrefab) 하나만 쓰지만, 강공격/콤보 티어/치명타처럼
    /// 앞으로 늘어날 이펙트는 <see cref="Spawn(GameObject, float)"/>에 prefab을 직접 넘겨서 재사용한다.
    /// 생성된 인스턴스는 어떤 Transform에도 부모로 붙이지 않는다 - 피격 대상이 이후에 흔들리거나
    /// 이동해도 이펙트가 그 움직임을 따라가지 않도록 생성 시점 월드 좌표만 스냅샷으로 사용한다.
    ///
    /// 인스턴스를 prefab별로 풀링한다(DamageNumberSpawner와 같은 이유) - 원래 아무 제한 없이 매
    /// 타격마다 새로 Instantiate/Destroy했는데, 연타가 이어지면 그만큼 GC 압박과 엔진 쪽 오브젝트
    /// 생성/파괴 비용이 쌓여 메인 스레드가 순간적으로 멎을 수 있었다 - 이게 전역 키보드 후크/WndProc
    /// 응답을 늦춰 키 입력 중 마우스가 끊기는 것처럼 보이는 원인 중 하나였다. minSpawnInterval
    /// 만으로는 빈도만 줄일 뿐 Instantiate/Destroy 자체의 비용은 그대로 남아서, DamageNumberSpawner와
    /// 동일하게 풀링으로 바꾼다. 이펙트 prefab에 HitEffectPop이 있으면 그 컴포넌트의 재생 완료 콜백을
    /// 통해 풀로 반환하고, 없으면 스포너가 직접 코루틴으로 duration 후 반환한다.
    /// </summary>
    public class HitEffectSpawner : MonoBehaviour
    {
        [Header("기본 이펙트")]
        [Tooltip("impactPoint/durationOverride를 지정하지 않고 Spawn()을 호출했을 때 쓸 기본 타격 이펙트 prefab.")]
        [SerializeField] private GameObject defaultEffectPrefab;

        [Header("생성 위치")]
        [Tooltip("이펙트가 생성될 실제 지점. 비워두면 이 오브젝트의 Transform 기준 fallbackOffset 위치를 대신 쓴다.")]
        [SerializeField] private Transform impactPoint;

        [Tooltip("impactPoint가 비어 있을 때 이 오브젝트 기준으로 사용할 오프셋(월드 유닛).")]
        [SerializeField] private Vector2 fallbackOffset = new Vector2(0f, 0.3f);

        [Header("수명")]
        [Tooltip("이펙트 인스턴스를 재생 후 정리하기까지 걸리는 시간(초). 0.1~0.2 권장, 기본값 0.15.")]
        [SerializeField] private float defaultDuration = 0.15f;

        [Header("연타 제한")]
        [Tooltip("이 시간(초)보다 짧은 간격으로는 새 이펙트를 생성하지 않는다. 빠른 연타 시 Instantiate가 과도하게 쌓이는 것을 막는다.")]
        [SerializeField] private float minSpawnInterval = 0.05f;

        [Header("풀")]
        [Tooltip("defaultEffectPrefab 기준으로 미리 만들어두고 재사용할 이펙트 인스턴스 개수. 연타 중 동시에 재생 중일 수 있는 최대 개수보다 넉넉하게 잡는다.")]
        [SerializeField] private int poolSize = 8;

        private const float FallbackDuration = 0.15f;

        private float lastSpawnTime = -999f;

        // prefab별로 별도 풀을 둔다 - Spawn(prefabOverride)로 defaultEffectPrefab이 아닌 다른 prefab이
        // 들어올 수 있어서다(강공격/콤보 티어 이펙트 등, 아직 실사용처는 없지만 API가 이미 열려 있다).
        private readonly Dictionary<GameObject, Queue<GameObject>> poolsByPrefab = new Dictionary<GameObject, Queue<GameObject>>();
        private readonly Dictionary<GameObject, GameObject> prefabByInstance = new Dictionary<GameObject, GameObject>();

        private void Awake()
        {
            if (defaultEffectPrefab == null) return;

            Queue<GameObject> pool = GetOrCreatePool(defaultEffectPrefab);
            for (int i = 0; i < poolSize; i++)
            {
                pool.Enqueue(CreatePooledInstance(defaultEffectPrefab));
            }
        }

        private Queue<GameObject> GetOrCreatePool(GameObject prefab)
        {
            if (!poolsByPrefab.TryGetValue(prefab, out Queue<GameObject> pool))
            {
                pool = new Queue<GameObject>();
                poolsByPrefab[prefab] = pool;
            }
            return pool;
        }

        private GameObject CreatePooledInstance(GameObject prefab)
        {
            GameObject instance = Instantiate(prefab);
            instance.SetActive(false);
            prefabByInstance[instance] = prefab;
            return instance;
        }

        /// <summary>
        /// 이펙트를 생성한다. prefabOverride/durationOverride를 비워두면(null, 0 이하) 기본값을 쓴다.
        /// prefab이 끝내 없거나 duration이 비정상(0 이하, NaN 등)이어도 예외 없이 안전하게 무시/보정한다.
        /// minSpawnInterval 안에 들어오는 추가 요청은 조용히 무시한다(데미지/피격 반응 등 다른 처리에는 영향 없음).
        /// </summary>
        public void Spawn(GameObject prefabOverride = null, float durationOverride = 0f)
        {
            if (Time.time - lastSpawnTime < minSpawnInterval) return;

            GameObject prefabToSpawn = prefabOverride != null ? prefabOverride : defaultEffectPrefab;
            if (prefabToSpawn == null) return; // 연결된 prefab이 없으면 조용히 아무 것도 하지 않는다.

            lastSpawnTime = Time.time;

            float duration = durationOverride > 0f ? durationOverride : defaultDuration;
            if (!(duration > 0f) || float.IsNaN(duration) || float.IsInfinity(duration))
            {
                duration = FallbackDuration; // 비정상 duration은 안전한 기본값으로 보정한다.
            }

            Vector3 spawnPosition = impactPoint != null
                ? impactPoint.position
                : transform.position + (Vector3)fallbackOffset;

            Queue<GameObject> pool = GetOrCreatePool(prefabToSpawn);
            // 풀이 비어 있으면(동시에 재생 중인 개수가 poolSize를 넘어서면) 그때만 예외적으로 새로 만든다 -
            // 정상적인 연타 빈도에서는 poolSize만으로 충분해서 이 경로를 거의 타지 않는다.
            GameObject instance = pool.Count > 0 ? pool.Dequeue() : CreatePooledInstance(prefabToSpawn);

            instance.transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
            instance.SetActive(true);

            var pop = instance.GetComponent<HitEffectPop>();
            if (pop != null)
            {
                // Play()가 SetActive(true) 직후 OnEnable이 시작한 기본(Destroy 모드) 재생을 즉시
                // 취소하고 풀 반환 모드로 바꿔치기한다.
                pop.Play(duration, ReturnToPool);
            }
            else
            {
                // HitEffectPop이 없는 prefab은 재생 종료를 스스로 알릴 방법이 없으니 스포너가 직접 타이머로 회수한다.
                StartCoroutine(ReturnToPoolAfterDelay(instance, duration));
            }
        }

        private void ReturnToPool(HitEffectPop pop)
        {
            ReturnInstanceToPool(pop.gameObject);
        }

        private IEnumerator ReturnToPoolAfterDelay(GameObject instance, float duration)
        {
            yield return new WaitForSeconds(duration);
            ReturnInstanceToPool(instance);
        }

        private void ReturnInstanceToPool(GameObject instance)
        {
            if (instance == null) return;

            instance.SetActive(false);

            if (prefabByInstance.TryGetValue(instance, out GameObject prefab))
            {
                GetOrCreatePool(prefab).Enqueue(instance);
            }
        }
    }
}
