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

        private const float FallbackDuration = 0.15f;

        /// <summary>
        /// 이펙트를 생성한다. prefabOverride/durationOverride를 비워두면(null, 0 이하) 기본값을 쓴다.
        /// prefab이 끝내 없거나 duration이 비정상(0 이하, NaN 등)이어도 예외 없이 안전하게 무시/보정한다.
        /// </summary>
        public void Spawn(GameObject prefabOverride = null, float durationOverride = 0f)
        {
            GameObject prefabToSpawn = prefabOverride != null ? prefabOverride : defaultEffectPrefab;
            if (prefabToSpawn == null) return; // 연결된 prefab이 없으면 조용히 아무 것도 하지 않는다.

            float duration = durationOverride > 0f ? durationOverride : defaultDuration;
            if (!(duration > 0f) || float.IsNaN(duration) || float.IsInfinity(duration))
            {
                duration = FallbackDuration; // 비정상 duration은 안전한 기본값으로 보정한다.
            }

            Vector3 spawnPosition = impactPoint != null
                ? impactPoint.position
                : transform.position + (Vector3)fallbackOffset;

            GameObject instance = Instantiate(prefabToSpawn, spawnPosition, Quaternion.identity);
            Destroy(instance, duration);
        }
    }
}
